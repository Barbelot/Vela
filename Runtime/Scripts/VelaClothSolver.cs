using System;
using Vela.Internal;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vela
{
    /// <summary>The environment pulling on this drape, as opposed to what the drape is made of. Damping and the speed clamp live on the quality profile, because they describe the fabric.</summary>
    [Serializable]
    public struct VelaClothForceSettings
    {
        [Tooltip("World-space acceleration in m/s², rotated into object space alongside the wind. It is an acceleration, so a heavier fabric does not fall more slowly.")]
        public Vector3 gravity;

        public static VelaClothForceSettings Default => new VelaClothForceSettings
        {
            gravity = new Vector3(0f, -9.81f, 0f)
        };
    }

    /// <summary>GPU XPBD solver. <see cref="Step"/> never reads <c>Time</c>, which is what lets one solver serve realtime, bake and playback.</summary>
    public sealed class VelaClothSolver : IDisposable
    {
        const int Threads1D = 256;

        readonly ComputeShader _solverCs;
        readonly ComputeShader _meshWriteCs;
        readonly bool _writeMotionVectors;

        readonly int _kPredict, _kDistanceH, _kDistanceV, _kUpdateVelocity, _kReset;
        readonly int _kShearA, _kShearB, _kBendH, _kBendV, _kBendDiagA, _kBendDiagB;
        readonly int _kWriteVertexBuffer, _kResetPrevFrame;
        readonly int _kCollideAnalytic, _kLra, _kApplyTransform;

        GraphicsBuffer _pos, _posPrev, _posRest, _vel, _posPrevFrame;
        GraphicsBuffer _lraAnchor, _lraDist;
        VelaClothBounds _bounds;
        VelaClothColliderRegistry _colliders;
        VelaClothSelfCollision _selfCollision;
        readonly float _smoothingScale;
        float _lastStepDt = 1f / 60f;
        bool _lraSettled;
        bool _aeroKeyword;

        public VelaClothGrid Grid { get; }
        public VelaClothColliderRegistry Colliders => _colliders;
        public VelaClothProfile Profile { get; set; }
        public VelaClothForceSettings Forces { get; set; } = VelaClothForceSettings.Default;

        /// <summary>Wind direction must already be in cloth object space, the space the solver runs in.</summary>
        public VelaClothWindSettings Wind { get; set; } = VelaClothWindSettings.Default;

        VelaClothProfile ActiveProfile => Profile != null ? Profile : VelaClothProfile.Fallback;

        /// <summary>Accumulated from the <c>dt</c> passed to <see cref="Step"/>, never from a clock — that is what makes a bake and a live run see the same gust.</summary>
        public float SimulationTime { get; set; }

        // Integrated per step rather than derived as direction × time: the direction is re-rotated into
        // object space every frame, and a lever arm that long would re-sample the whole field on any turn.
        Vector3 _windAdvection;

        public GraphicsBuffer PositionBuffer => _pos;
        public GraphicsBuffer VelocityBuffer => _vel;

        /// <summary>Null whenever the profile has self-collision off, which is what makes toggling it off give the memory back.</summary>
        public VelaClothSelfCollision SelfCollision => _selfCollision;

        /// <summary>Anchors per vertex in the uploaded tables, 0 when nothing is pinned.</summary>
        public int LongRangeAnchorCount { get; private set; }

        public VelaClothSolver(VelaClothGrid grid, ComputeShader solverShader, ComputeShader meshWriteShader,
            bool writeMotionVectors)
        {
            if (solverShader == null || meshWriteShader == null)
                throw new ArgumentNullException(nameof(solverShader), "Cloth compute shaders are missing.");

            Grid = grid;
            _smoothingScale = VelaClothConstraintScale.SmoothingScale(grid);
            _writeMotionVectors = writeMotionVectors;

            // Per-instance copies: keywords and uniforms on a ComputeShader asset are shared state otherwise.
            _solverCs = UnityEngine.Object.Instantiate(solverShader);
            _meshWriteCs = UnityEngine.Object.Instantiate(meshWriteShader);

            _kPredict = _solverCs.FindKernel("KPredict");
            _kDistanceH = _solverCs.FindKernel("KDistanceH");
            _kDistanceV = _solverCs.FindKernel("KDistanceV");
            _kShearA = _solverCs.FindKernel("KShearA");
            _kShearB = _solverCs.FindKernel("KShearB");
            _kBendH = _solverCs.FindKernel("KBendH");
            _kBendV = _solverCs.FindKernel("KBendV");
            _kBendDiagA = _solverCs.FindKernel("KBendDiagA");
            _kBendDiagB = _solverCs.FindKernel("KBendDiagB");
            _kLra = _solverCs.FindKernel("KLra");
            _kCollideAnalytic = _solverCs.FindKernel("KCollideAnalytic");
            _kUpdateVelocity = _solverCs.FindKernel("KUpdateVelocity");
            _kReset = _solverCs.FindKernel("KReset");
            _kApplyTransform = _solverCs.FindKernel("KApplyTransform");
            _kWriteVertexBuffer = _meshWriteCs.FindKernel("KWriteVertexBuffer");
            _kResetPrevFrame = _meshWriteCs.FindKernel("KResetPrevFrame");

            if (_writeMotionVectors)
                _meshWriteCs.EnableKeyword("WRITE_MOTION_VECTORS");
            else
                _meshWriteCs.DisableKeyword("WRITE_MOTION_VECTORS");

            _colliders = new VelaClothColliderRegistry();

            AllocateBuffers();
            PushGridConstants();

            _bounds = new VelaClothBounds(_meshWriteCs, _pos, Grid.VertexCount);
        }

        void AllocateBuffers()
        {
            int n = Grid.VertexCount;
            const int stride = 4 * sizeof(float);

            _pos = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, stride);
            _posPrev = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, stride);
            _posRest = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, stride);
            _vel = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, stride);

            if (_writeMotionVectors)
                _posPrevFrame = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, stride);
        }

        // Before every dispatch batch, not once: a shader reimport or device reset drops every buffer binding.
        void BindSolverBuffers()
        {
            foreach (int k in new[]
                     {
                         _kPredict, _kDistanceH, _kDistanceV, _kShearA, _kShearB,
                         _kBendH, _kBendV, _kBendDiagA, _kBendDiagB,
                         _kLra, _kCollideAnalytic, _kUpdateVelocity, _kReset, _kApplyTransform
                     })
            {
                _solverCs.SetBuffer(k, ShaderIds.Pos, _pos);
                _solverCs.SetBuffer(k, ShaderIds.PosPrev, _posPrev);
                _solverCs.SetBuffer(k, ShaderIds.PosRest, _posRest);
                _solverCs.SetBuffer(k, ShaderIds.Vel, _vel);
            }

            _solverCs.SetBuffer(_kCollideAnalytic, ShaderIds.Colliders, _colliders.Buffer);

            if (_lraAnchor != null)
            {
                _solverCs.SetBuffer(_kLra, ShaderIds.LraAnchor, _lraAnchor);
                _solverCs.SetBuffer(_kLra, ShaderIds.LraDist, _lraDist);
            }
        }

        void BindMeshWriteBuffers(int kernel)
        {
            _meshWriteCs.SetBuffer(kernel, ShaderIds.Pos, _pos);
            _meshWriteCs.SetBuffer(kernel, ShaderIds.PosPrev, _posPrev);
            _meshWriteCs.SetBuffer(kernel, ShaderIds.PosRest, _posRest);
            _meshWriteCs.SetBuffer(kernel, ShaderIds.Vel, _vel);

            if (_writeMotionVectors)
                _meshWriteCs.SetBuffer(kernel, ShaderIds.PosPrevFrame, _posPrevFrame);
        }

        void PushGridConstants()
        {
            foreach (ComputeShader cs in new[] { _solverCs, _meshWriteCs })
            {
                cs.SetInt(ShaderIds.W, Grid.width);
                cs.SetInt(ShaderIds.H, Grid.height);
                cs.SetInt(ShaderIds.VertexCount, Grid.VertexCount);
                cs.SetFloat(ShaderIds.RestDx, Grid.RestDx);
                cs.SetFloat(ShaderIds.RestDy, Grid.RestDy);
                cs.SetFloat(ShaderIds.RestDiag,
                    Mathf.Sqrt(Grid.RestDx * Grid.RestDx + Grid.RestDy * Grid.RestDy));
            }
        }

        /// <summary>Uploads rest positions with invMass in w, then snaps the live state to them.</summary>
        public void SetRestState(NativeArray<Vector4> rest)
        {
            if (rest.Length != Grid.VertexCount)
                throw new ArgumentException($"Expected {Grid.VertexCount} rest vertices, got {rest.Length}.");

            _posRest.SetData(rest);
            Reset();
        }

        /// <summary>Uploads the geodesic tables. Slack is applied in the shader, so it stays live-tunable without a rebuild.</summary>
        public void SetLongRangeAttachments(VelaClothLongRangeAttachment tables)
        {
            ReleaseLongRangeAttachments();

            if (tables == null || tables.AnchorCount <= 0)
                return;

            int count = tables.Anchors.Length;
            _lraAnchor = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(uint));
            _lraDist = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, sizeof(float));
            _lraAnchor.SetData(tables.Anchors);
            _lraDist.SetData(tables.Distances);
            LongRangeAnchorCount = tables.AnchorCount;
        }

        void ReleaseLongRangeAttachments()
        {
            _lraAnchor?.Dispose();
            _lraDist?.Dispose();
            _lraAnchor = _lraDist = null;
            LongRangeAnchorCount = 0;
            _lraSettled = false;
        }

        public void Reset()
        {
            SimulationTime = 0f;
            _windAdvection = Vector3.zero;
            BindSolverBuffers();
            _solverCs.Dispatch(_kReset, Groups(Grid.VertexCount), 1, 1);
            _colliders?.ClearHistory();
            ResetMotionVectorHistory();
        }

        /// <summary>Call after any discontinuity — reset, cache seek, loop wrap — or the temporal passes smear across it.</summary>
        public void ResetMotionVectorHistory()
        {
            if (!_writeMotionVectors)
                return;

            BindMeshWriteBuffers(_kResetPrevFrame);
            _meshWriteCs.Dispatch(_kResetPrevFrame, Groups(Grid.VertexCount), 1, 1);
        }

        /// <summary>Maps every vertex from the cloth's previous frame into its current one, so the sheet keeps its world placement; the next <see cref="Step"/> sweeps the pins back to rest across its substeps. Call once per frame the transform moved, before <see cref="Step"/>.</summary>
        public void ApplyTransformDelta(Matrix4x4 previousToCurrentLocal, float inertia)
        {
            if (inertia <= 0f)
                return;

            BindSolverBuffers();
            _solverCs.SetMatrix(ShaderIds.TransformDelta, previousToCurrentLocal);
            _solverCs.SetFloat(ShaderIds.TransformInertia, Mathf.Min(1f, inertia));
            _solverCs.Dispatch(_kApplyTransform, Groups(Grid.VertexCount), 1, 1);
        }

        public void Step(float dt)
        {
            if (dt <= 0f)
                return;

            VelaClothProfile profile = ActiveProfile;
            int substeps = Mathf.Max(1, profile.substeps);
            float h = dt / substeps;

            // Once per step, not per frame: a driver running two steps in one frame must not replay the
            // same collider motion twice, and one running none must not lose it.
            _colliders?.Pack();

            bool aero = AerodynamicsActive(profile);
            BindSolverBuffers();
            PushStepConstants(h, profile, aero);
            _lastStepDt = dt;

            int colliderCount = _colliders != null ? _colliders.Count : 0;
            int selfStride = Mathf.Max(1, profile.selfCollisionStride);
            bool self = UpdateSelfCollision(profile);
            bool lra = LongRangeActive(profile);
            bool shear = profile.useShear;
            Vector3 windDrift = aero ? Wind.NormalizedDirection * Wind.turbulenceSpeed : Vector3.zero;
            int bendDirections = profile.EffectiveBendingDirections;
            float alphaBend = profile.BendingCompliance() / (h * h);

            int nGroups = Groups(Grid.VertexCount);
            int hGroupsX = Groups(Grid.width / 2 + 1);
            int vGroupsX = Groups(Grid.width);
            int vGroupsY = Grid.height / 2 + 1;
            int quadGroupsX = Groups((Grid.width - 1) / 2 + 1);
            int quadRows = Mathf.Max(1, Grid.height - 1);
            int bendGroupsX = Groups((Grid.width - 2 + 2) / 3);
            int bendGroupsY = Mathf.Max(1, (Grid.height - 2 + 2) / 3);

            // Switching the constraint on mid-drape is a large one-off correction. Landing it outside the
            // substep loop keeps it out of v = (x - x^n)/h, or the sheet is flung up by the whole stretch it
            // had accumulated and rings for seconds. KPredict rewrites _PosPrev, so nothing else sees it.
            if (lra && !_lraSettled)
                _solverCs.Dispatch(_kLra, nGroups, 1, 1);

            _lraSettled = lra;

            for (int s = 0; s < substeps; s++)
            {
                // Per substep rather than per step, so a gust is sampled where it actually is — at 8 substeps
                // a step-granular clock would hold the field still for 16 ms and then jump it. The wind is
                // applied at the substep's end, in KUpdateVelocity, hence s + 1.
                if (aero)
                {
                    _solverCs.SetFloat(ShaderIds.WindTime, SimulationTime + (s + 1) * h);
                    _solverCs.SetVector(ShaderIds.WindAdvection, _windAdvection + windDrift * ((s + 1) * h));
                }

                // Sequential lerp by the remaining fraction walks the pins linearly to rest without a
                // step-start copy of them.
                _solverCs.SetFloat(ShaderIds.PinSweep, 1f / (substeps - s));
                _solverCs.Dispatch(_kPredict, nGroups, 1, 1);

                for (int parity = 0; parity < 2; parity++)
                {
                    _solverCs.SetInt(ShaderIds.Parity, parity);
                    _solverCs.Dispatch(_kDistanceH, hGroupsX, Grid.height, 1);
                }

                for (int parity = 0; parity < 2; parity++)
                {
                    _solverCs.SetInt(ShaderIds.Parity, parity);
                    _solverCs.Dispatch(_kDistanceV, vGroupsX, vGroupsY, 1);
                }

                if (shear)
                {
                    for (int parity = 0; parity < 2; parity++)
                    {
                        _solverCs.SetInt(ShaderIds.Parity, parity);
                        _solverCs.Dispatch(_kShearA, quadGroupsX, quadRows, 1);
                        _solverCs.Dispatch(_kShearB, quadGroupsX, quadRows, 1);
                    }
                }

                for (int phase = 0; phase < 3; phase++)
                {
                    _solverCs.SetInt(ShaderIds.Phase, phase);
                    _solverCs.SetFloat(ShaderIds.AlphaBend, alphaBend);
                    _solverCs.Dispatch(_kBendH, bendGroupsX, Grid.height, 1);
                    _solverCs.Dispatch(_kBendV, vGroupsX, bendGroupsY, 1);

                    if (bendDirections < 4)
                        continue;

                    // The two diagonal families carry half the weight, which is twice the compliance.
                    _solverCs.SetFloat(ShaderIds.AlphaBend, alphaBend * 2f);
                    _solverCs.Dispatch(_kBendDiagA, bendGroupsX, quadRows, 1);
                    _solverCs.Dispatch(_kBendDiagB, bendGroupsX, quadRows, 1);
                }

                if (lra)
                    _solverCs.Dispatch(_kLra, nGroups, 1, 1);

                // Rebuilt with the resolve rather than once per step: KSelfCollideAccum tests distance
                // against live positions but takes its candidates from _SortedIds, so a membership built
                // substeps ago drops contacts whose vertices have since crossed a cell.
                if (self && s % selfStride == 0)
                {
                    _selfCollision.BuildHash();
                    _selfCollision.Resolve();
                }

                // Last positional pass of the substep, so nothing ends it inside a primitive. Self-collision
                // pushes a pile apart in whatever direction it needs, and half of those directions point
                // into the ground.
                if (colliderCount > 0)
                {
                    _solverCs.SetFloat(ShaderIds.ColliderT0, s / (float)substeps);
                    _solverCs.SetFloat(ShaderIds.ColliderT1, (s + 1) / (float)substeps);
                    _solverCs.Dispatch(_kCollideAnalytic, nGroups, 1, 1);
                }

                _solverCs.Dispatch(_kUpdateVelocity, vGroupsX, Grid.height, 1);
            }

            SimulationTime += dt;
            _windAdvection += windDrift * dt;
        }

        /// <summary>Allocates or releases the hash to match the profile and pushes its constants.</summary>
        bool UpdateSelfCollision(VelaClothProfile profile)
        {
            bool wanted = profile.useSelfCollision && VelaClothResources.SelfCollision != null;

            if (!wanted)
            {
                _selfCollision?.Dispose();
                _selfCollision = null;
                return false;
            }

            _selfCollision ??= new VelaClothSelfCollision(VelaClothResources.SelfCollision, Grid, _pos, _posPrev);

            float radius = VelaClothSelfCollision.ContactRadius(Grid, profile);
            _selfCollision.PushConstants(
                radius,
                VelaClothSelfCollision.CellSize(radius),
                profile.maxContactsPerVertex,
                profile.selfCollisionFriction);
            return true;
        }

        /// <summary>Off when the profile disables it and when nothing is pinned, which leaves no anchors to pull towards.</summary>
        public bool LongRangeActive(VelaClothProfile profile) =>
            profile.useLongRangeAttachment && LongRangeAnchorCount > 0;

        /// <summary>Per-triangle aero is the only path wind has into the cloth, so still air or a fabric with no drag and no lift switches it off whole.</summary>
        public bool AerodynamicsActive(VelaClothProfile profile) =>
            profile.HasAerodynamicResponse && Wind.HasEffect;

        void PushStepConstants(float h, VelaClothProfile profile, bool aero)
        {
            _solverCs.SetFloat(ShaderIds.SubstepDt, h);
            _solverCs.SetFloat(ShaderIds.InvSubstepDt, 1f / h);
            _solverCs.SetVector(ShaderIds.Gravity, Forces.gravity);
            _solverCs.SetFloat(ShaderIds.MaxVelocity, Mathf.Max(0.01f, profile.maxVelocity));
            // Both damping terms are authored as rates in 1/s, so they compound to exp(-rate * t) across any
            // substep count instead of meaning something different at every quality setting. The local term
            // needs the spacing correction on top; the clamp is a real ceiling on it at high resolution.
            _solverCs.SetFloat(ShaderIds.VelocityDamp, Mathf.Clamp01(1f - profile.globalDamping * h));
            _solverCs.SetFloat(ShaderIds.VelocitySmooth,
                Mathf.Clamp01(profile.localDamping * h * _smoothingScale));
            _solverCs.SetInt(ShaderIds.ColliderCount, _colliders != null ? _colliders.Count : 0);
            _solverCs.SetInt(ShaderIds.LraAnchorCount, LongRangeAnchorCount);
            _solverCs.SetFloat(ShaderIds.LraSlack, profile.LraSlack);

            // The classic XPBD bug is dividing by the frame dt instead of the substep dt.
            float invH2 = 1f / (h * h);
            float alpha = profile.DistanceCompliance(profile.structuralStiffness) * invH2;
            _solverCs.SetFloat(ShaderIds.AlphaStructuralH, alpha);
            _solverCs.SetFloat(ShaderIds.AlphaStructuralV, alpha);
            _solverCs.SetFloat(ShaderIds.AlphaShear,
                profile.DistanceCompliance(profile.shearStiffness) * invH2);

            PushWindConstants(profile, aero);
        }

        void PushWindConstants(VelaClothProfile profile, bool aero)
        {
            if (aero != _aeroKeyword)
            {
                if (aero)
                    _solverCs.EnableKeyword("CLOTH_AERODYNAMICS");
                else
                    _solverCs.DisableKeyword("CLOTH_AERODYNAMICS");

                _aeroKeyword = aero;
            }

            if (!aero)
                return;

            VelaClothWindSettings wind = Wind;

            _solverCs.SetVector(ShaderIds.WindDir, wind.NormalizedDirection);
            _solverCs.SetFloat(ShaderIds.WindSpeed, wind.EffectiveSpeed);
            _solverCs.SetFloat(ShaderIds.GustAmplitude, wind.gustAmplitude);
            _solverCs.SetFloat(ShaderIds.GustFrequency, wind.gustFrequency);
            _solverCs.SetFloat(ShaderIds.Turbulence, wind.EffectiveTurbulence);
            _solverCs.SetFloat(ShaderIds.TurbulenceScale, wind.turbulenceScale);
            // The dynamic-pressure half and the air density fold into the coefficients here rather than
            // costing two multiplies per triangle per substep.
            _solverCs.SetFloat(ShaderIds.DragFactor, 0.5f * wind.airDensity * profile.dragCoefficient);
            _solverCs.SetFloat(ShaderIds.LiftFactor, 0.5f * wind.airDensity * profile.liftCoefficient);
        }

        /// <summary>Recomputes normals and tangents from grid neighbours straight into the mesh's own vertex buffer.</summary>
        public void WriteToMesh(GraphicsBuffer vertexBuffer, int vertexStride)
        {
            BindMeshWriteBuffers(_kWriteVertexBuffer);
            _meshWriteCs.SetBuffer(_kWriteVertexBuffer, ShaderIds.VertexBuffer, vertexBuffer);
            _meshWriteCs.SetInt(ShaderIds.VertexStride, vertexStride);
            _meshWriteCs.Dispatch(_kWriteVertexBuffer, Groups(Grid.VertexCount), 1, 1);
        }

        /// <summary>Applies the newest completed GPU AABB to the mesh, padded against the readback latency.</summary>
        public void UpdateBounds(Mesh mesh)
        {
            if (_bounds == null || mesh == null)
                return;

            if (!_bounds.Pump())
                return;

            Bounds b = _bounds.Value;
            // Two frames of readback latency, and Expand grows the size rather than the extent.
            b.Expand(4f * Mathf.Max(0.01f, ActiveProfile.maxVelocity) * _lastStepDt);
            mesh.bounds = b;
        }

        static int Groups(int count) => Mathf.Max(1, (count + Threads1D - 1) / Threads1D);

        public void Dispose()
        {
            _bounds?.Dispose();
            _bounds = null;

            _colliders?.Dispose();
            _colliders = null;

            _selfCollision?.Dispose();
            _selfCollision = null;

            _pos?.Dispose();
            _posPrev?.Dispose();
            _posRest?.Dispose();
            _vel?.Dispose();
            _posPrevFrame?.Dispose();
            _pos = _posPrev = _posRest = _vel = _posPrevFrame = null;

            ReleaseLongRangeAttachments();

            DestroyInstance(_solverCs);
            DestroyInstance(_meshWriteCs);
        }

        static void DestroyInstance(UnityEngine.Object o)
        {
            if (o == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(o);
            else
                UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
