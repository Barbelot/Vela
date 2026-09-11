using UnityEngine;

namespace Vela.Samples
{
    [ExecuteAlways]
    [RequireComponent(typeof(VelaClothSimulation), typeof(MeshRenderer))]
    [AddComponentMenu("Vela/Samples/Cloth Art Binder")]
    public sealed class VelaClothArtBinder : MonoBehaviour
    {
        static readonly int PositionsId = Shader.PropertyToID("_VelaPositions");
        static readonly int VelocitiesId = Shader.PropertyToID("_VelaVelocities");
        static readonly int GridId = Shader.PropertyToID("_VelaGrid");
        static readonly int SheetId = Shader.PropertyToID("_VelaSheet");
        static readonly int SunDirectionId = Shader.PropertyToID("_VelaSunDirection");
        static readonly int SunColorId = Shader.PropertyToID("_VelaSunColor");

        [Tooltip("Directional light the sample shaders shade with. Empty uses the scene sun, then the first directional light found.")]
        [SerializeField] Light sun;

        // Global fallback so draws without a block (material previews) are silently degenerate instead of warning.
        static GraphicsBuffer _fallback;
        static int _binders;

        VelaClothSimulation _cloth;
        MeshRenderer _renderer;
        MaterialPropertyBlock _block;
        Light _foundSun;

        void OnEnable()
        {
            _cloth = GetComponent<VelaClothSimulation>();
            _renderer = GetComponent<MeshRenderer>();
            _block = new MaterialPropertyBlock();
            AcquireFallback();
            Bind();
        }

        void OnDisable()
        {
            if (_renderer != null)
                _renderer.SetPropertyBlock(null);
            ReleaseFallback();
        }

        static void AcquireFallback()
        {
            if (_binders++ > 0)
                return;
            _fallback = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 16);
            _fallback.SetData(new[] { Vector4.zero });
            Shader.SetGlobalBuffer(PositionsId, _fallback);
            Shader.SetGlobalBuffer(VelocitiesId, _fallback);
        }

        static void ReleaseFallback()
        {
            if (--_binders > 0)
                return;
            _fallback?.Release();
            _fallback = null;
        }

        // LateUpdate so the block holds the buffers a Rebuild in this frame's Update created.
        void LateUpdate() => Bind();

        // A draw whose StructuredBuffer is unbound is dropped, so the cloth is invisible until this has run once.
        void Bind()
        {
            PushSun();

            GraphicsBuffer positions = _cloth.PositionBuffer;
            GraphicsBuffer velocities = _cloth.VelocityBuffer;
            if (positions == null || velocities == null)
                return;

            VelaClothGrid grid = _cloth.Grid;
            _renderer.GetPropertyBlock(_block);
            _block.SetBuffer(PositionsId, positions);
            _block.SetBuffer(VelocitiesId, velocities);
            _block.SetVector(GridId, new Vector4(grid.width, grid.height, grid.RestDx, grid.RestDy));
            _block.SetVector(SheetId, new Vector4(grid.size.x, grid.size.y, grid.pivot.x, grid.pivot.y));
            _renderer.SetPropertyBlock(_block);
        }

        void PushSun()
        {
            Light light = sun != null ? sun : RenderSettings.sun;
            if (light == null)
            {
                if (_foundSun == null)
                    _foundSun = FindDirectional();
                light = _foundSun;
            }

            Vector3 direction = light != null ? -light.transform.forward : new Vector3(0.3f, 0.8f, -0.5f).normalized;
            Color color = Color.white;
            if (light != null)
            {
                color = light.color;
                if (light.useColorTemperature)
                    color *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature);
            }

            Shader.SetGlobalVector(SunDirectionId, new Vector4(direction.x, direction.y, direction.z, 1f));
            Shader.SetGlobalVector(SunColorId, color);
        }

        static Light FindDirectional()
        {
            foreach (Light l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional && l.enabled)
                    return l;
            return null;
        }
    }
}
