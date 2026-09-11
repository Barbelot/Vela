using Vela.Drivers;
using Vela.Internal;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vela
{
    /// <summary>The single component an artist adds: owns the grid, the mesh, the solver and the driver.</summary>
    [ExecuteAlways]
    [AddComponentMenu("Vela/Cloth Simulation")]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class VelaClothSimulation : MonoBehaviour
    {
        [Tooltip("Width and height of the sheet in metres, before the transform's scale. A bigger drape hangs longer at the same profile.")]
        [SerializeField] Vector2 sizeMeters = new Vector2(2f, 3f);

        [Tooltip("Vertices along the longer axis; the other follows, keeping quads square. The profile means the same fabric at any value here.")]
        [SerializeField, Range(VelaClothGrid.MinResolution, VelaClothGrid.MaxResolution)] int resolution = 64;

        [Tooltip("Where the transform sits on the sheet, in 0-1 of its size. (0.5, 1) hangs it from the middle of its top edge.")]
        [SerializeField] Vector2 pivot = new Vector2(0.5f, 1f);

        [Tooltip("The fabric: mass, stiffness, damping, and how hard to solve it. Meant to be shared between drapes.")]
        [SerializeField] VelaClothProfile qualityProfile;

        [Tooltip("Simulation steps per second, independent of the render rate. The profile's substeps subdivide each one.")]
        [SerializeField, Min(1f)] float simulationRate = 60f;

        [Tooltip("Steps allowed in a single frame. On a hitch the sim falls behind in slow motion instead of spiralling.")]
        [SerializeField, Min(1)] int maxStepsPerFrame = 3;

        [Tooltip("Steps run at rebuild so the drape starts settled instead of snapping down from flat. Wind runs during them too.")]
        [SerializeField, Min(0)] int preRollSteps = 60;

        [Tooltip("Which vertices are held in place. With nothing pinned, long-range attachment disables itself.")]
        [SerializeField] VelaClothPinMode pinMode = VelaClothPinMode.TopEdge;

        [Tooltip("The air around this drape, in world space. How strongly the cloth answers it is the profile's drag and lift.")]
        [SerializeField] VelaClothWindSettings wind = VelaClothWindSettings.Default;

        [Tooltip("Forces acting on the drape from outside it, in world space.")]
        [SerializeField] VelaClothForceSettings forces = VelaClothForceSettings.Default;

        [Tooltip("Assigned to the MeshRenderer. HDRP needs \"Add Precomputed Velocity\" on for correct motion vectors.")]
        [SerializeField] Material material;

        [Tooltip("TwoSided is usually right for a drape: a single-sided caster leaves a lit sheet with no shadow.")]
        [SerializeField] ShadowCastingMode castShadows = ShadowCastingMode.TwoSided;

        [Tooltip("Writes per-vertex displacement to TEXCOORD4, which TAA, motion blur, SSR, SSGI and temporal upscalers read. Off saves 12 B/vertex.")]
        [SerializeField] bool writeMotionVectors = true;

        VelaClothMeshBinding _binding;
        VelaClothSolver _solver;
        VelaClothRealtimeDriver _driver;
        MeshFilter _meshFilter;
        MeshRenderer _meshRenderer;
        int _builtAnchorCount;
        float _builtAreaDensity;
        bool _dirty;

        public VelaClothGrid Grid => _binding?.Grid ?? ResolveGrid();
        public Mesh Mesh => _binding?.Mesh;
        public GraphicsBuffer PositionBuffer => _solver?.PositionBuffer;
        public GraphicsBuffer VelocityBuffer => _solver?.VelocityBuffer;
        public VelaClothProfile Profile => qualityProfile != null ? qualityProfile : VelaClothProfile.Fallback;
        public float SimulationRate => simulationRate;
        public int ColliderCount => _solver?.Colliders?.Count ?? 0;
        public int LongRangeAnchorCount => _solver?.LongRangeAnchorCount ?? 0;
        public bool LongRangeActive => _solver != null && _solver.LongRangeActive(Profile);
        public bool AerodynamicsActive => _solver != null && _solver.AerodynamicsActive(Profile);
        public VelaClothForceSettings Forces => forces;
        public VelaClothWindSettings Wind => wind;

        /// <summary>Null until a profile turns self-collision on, and null again the step after it goes off.</summary>
        public VelaClothSelfCollision SelfCollision => _solver?.SelfCollision;

        public int Resolution
        {
            get => resolution;
            set
            {
                int clamped = Mathf.Clamp(value, VelaClothGrid.MinResolution, VelaClothGrid.MaxResolution);
                if (clamped == resolution) return;
                resolution = clamped;
                _dirty = true;
            }
        }

        void OnEnable()
        {
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Release;
#endif
            Rebuild();
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Release;
#endif
            Release();
        }

        void OnValidate() => _dirty = true;

        void Update()
        {
            if (_dirty)
                Rebuild();

            if (_solver == null)
                return;

            // Editing the profile asset never reaches this component's OnValidate, and both of these are baked
            // into CPU state rather than pushed per step. Density rides in every vertex's invMass, so it needs
            // the full rebuild and its pre-roll — deferred by a frame rather than torn down mid-Update; the
            // anchor count only invalidates the geodesic tables.
            if (!Mathf.Approximately(Profile.areaDensity, _builtAreaDensity))
                _dirty = true;
            else if (Profile.lraAnchorCount != _builtAnchorCount)
                BuildPinState(_binding.Grid, false);

            PushSettings();
            _driver.Tick(Application.isPlaying ? Time.deltaTime : Mathf.Min(Time.deltaTime, 1f / 30f));
            _solver.WriteToMesh(_binding.VertexBuffer, _binding.Stride);
            _solver.UpdateBounds(_binding.Mesh);
        }

        VelaClothGrid ResolveGrid() => VelaClothGrid.FromResolution(sizeMeters, resolution, pivot);

        /// <summary>Regenerates mesh, buffers and pin state. Guarded by the dirty flag rather than run per inspector repaint.</summary>
        public void Rebuild()
        {
            _dirty = false;
            Release();

            VelaClothGrid grid = ResolveGrid();
            _binding = new VelaClothMeshBinding(grid, writeMotionVectors);

            if (_meshFilter != null)
                _meshFilter.sharedMesh = _binding.Mesh;

            ApplyRendererSettings();

            if (VelaClothResources.Solver == null || VelaClothResources.MeshWrite == null)
                return;

            _solver = new VelaClothSolver(grid, VelaClothResources.Solver, VelaClothResources.MeshWrite, writeMotionVectors);
            _driver = new VelaClothRealtimeDriver(_solver);

            PushSettings();

            BuildPinState(grid, true);

            _driver.PreRoll(preRollSteps);
            _solver.WriteToMesh(_binding.VertexBuffer, _binding.Stride);
        }

        /// <summary>Fills rest positions and invMass, then rebuilds the geodesic tables from them. <paramref name="uploadRest"/> is false when only the tables went stale, since uploading rest state resets the drape.</summary>
        void BuildPinState(in VelaClothGrid grid, bool uploadRest)
        {
            _builtAreaDensity = Profile.areaDensity;
            _builtAnchorCount = Profile.lraAnchorCount;

            var rest = new NativeArray<Vector4>(grid.VertexCount, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            try
            {
                VelaClothPinning.FillRestState(grid, pinMode, _builtAreaDensity, rest);

                if (uploadRest)
                    _solver.SetRestState(rest);

                _solver.SetLongRangeAttachments(
                    VelaClothLongRangeAttachment.Build(grid, rest, _builtAnchorCount));
            }
            finally
            {
                rest.Dispose();
            }
        }

        void PushSettings()
        {
            _solver.Profile = qualityProfile;
            _solver.Colliders.Cloth = transform;

            // Gravity and wind are authored in world space, but the solver runs in object space — without this,
            // rotating the cloth rotates both with it and a tilted sheet still behaves as an untilted one.
            Quaternion toObject = Quaternion.Inverse(transform.rotation);

            VelaClothForceSettings local = forces;
            local.gravity = toObject * forces.gravity;
            _solver.Forces = local;

            VelaClothWindSettings localWind = wind;
            localWind.direction = toObject * wind.NormalizedDirection;
            _solver.Wind = localWind;

            _driver.SimulationRate = simulationRate;
            _driver.MaxStepsPerFrame = maxStepsPerFrame;
        }

        void ApplyRendererSettings()
        {
            if (_meshRenderer == null)
                return;

            // Only assign on change, or the editor marks the scene dirty every repaint.
            if (_meshRenderer.sharedMaterial != material)
                _meshRenderer.sharedMaterial = material;

            if (_meshRenderer.shadowCastingMode != castShadows)
                _meshRenderer.shadowCastingMode = castShadows;

            MotionVectorGenerationMode mvMode = writeMotionVectors
                ? MotionVectorGenerationMode.Object
                : MotionVectorGenerationMode.ForceNoMotion;

            if (_meshRenderer.motionVectorGenerationMode != mvMode)
                _meshRenderer.motionVectorGenerationMode = mvMode;
        }

        void Release()
        {
            if (_meshFilter != null && _binding != null && _meshFilter.sharedMesh == _binding.Mesh)
                _meshFilter.sharedMesh = null;

            _solver?.Dispose();
            _solver = null;
            _driver = null;
            _builtAnchorCount = 0;
            _builtAreaDensity = 0f;

            _binding?.Dispose();
            _binding = null;
        }
    }
}
