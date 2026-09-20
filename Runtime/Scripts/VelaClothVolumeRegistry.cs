using System;
using System.Collections.Generic;
using Vela.Internal;
using UnityEngine;

namespace Vela
{
    /// <summary>Packs the enabled <see cref="VelaClothForceVolume"/>s into two GPU arrays, wind-mode and acceleration-mode, in cloth object space. One instance per solver: the turbulence scroll history is per-cloth state, integrated from the solver's own dt so a bake and a live run see the same eddies.</summary>
    public sealed class VelaClothVolumeRegistry : IDisposable
    {
        public const int MaxVolumes = 16;

        static readonly List<VelaClothForceVolume> Active = new List<VelaClothForceVolume>();

        readonly GpuVolume[] _wind = new GpuVolume[MaxVolumes];
        readonly GpuVolume[] _force = new GpuVolume[MaxVolumes];
        readonly List<(VelaClothForceVolume volume, float rate)> _scrolling = new List<(VelaClothForceVolume, float)>();

        Dictionary<VelaClothForceVolume, float> _scroll = new Dictionary<VelaClothForceVolume, float>();
        Dictionary<VelaClothForceVolume, float> _next = new Dictionary<VelaClothForceVolume, float>();
        GraphicsBuffer _windBuffer, _forceBuffer;

        public int WindCount { get; private set; }
        public int ForceCount { get; private set; }
        public GraphicsBuffer WindBuffer => _windBuffer;
        public GraphicsBuffer ForceBuffer => _forceBuffer;

        /// <summary>The cloth the volumes are packed relative to. Set once; <see cref="Pack"/> reads it every step.</summary>
        public Transform Cloth { get; set; }

        /// <summary>Only volumes on these layers are packed.</summary>
        public LayerMask Mask { get; set; } = ~0;

        internal static void Register(VelaClothForceVolume volume)
        {
            if (!Active.Contains(volume))
                Active.Add(volume);
        }

        internal static void Unregister(VelaClothForceVolume volume) => Active.Remove(volume);

        public VelaClothVolumeRegistry()
        {
            _windBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxVolumes, GpuVolume.Stride);
            _forceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxVolumes, GpuVolume.Stride);
        }

        /// <summary>Rebuilds both GPU arrays against <see cref="Cloth"/>. Called once per solver step.</summary>
        public void Pack()
        {
            if (_windBuffer == null || Cloth == null)
                return;

            Matrix4x4 clothToWorld = Cloth.localToWorldMatrix;
            Matrix4x4 worldToCloth = Cloth.worldToLocalMatrix;

            _next.Clear();
            _scrolling.Clear();
            int nWind = 0, nForce = 0;

            for (int i = 0; i < Active.Count; i++)
            {
                VelaClothForceVolume volume = Active[i];
                if (volume == null || !volume.isActiveAndEnabled || !volume.HasEffect)
                    continue;
                if ((Mask.value & (1 << volume.gameObject.layer)) == 0)
                    continue;

                bool wind = volume.Mode == VelaClothForceMode.Wind;
                if ((wind ? nWind : nForce) >= MaxVolumes)
                    continue;

                Transform t = volume.transform;
                // Rigid on purpose: scale lives in the extents, so the blend distance stays metric.
                Matrix4x4 volumeToWorld = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);

                _scroll.TryGetValue(volume, out float scroll);
                _next[volume] = scroll;

                float intensity = volume.Intensity;
                Vector3 extents = volume.ScaledExtents;
                Vector4 paramsB = volume.Field == VelaClothForceField.Turbulence
                    ? new Vector4(volume.ScrollSpeed * intensity, 0f, volume.NoiseScale, scroll)
                    : new Vector4(volume.InwardPull * intensity, volume.AxialLift * intensity, 0f, 0f);

                if (volume.Field == VelaClothForceField.Turbulence && volume.ScrollSpeed > 0f)
                    _scrolling.Add((volume, volume.ScrollSpeed * intensity));

                var packed = new GpuVolume
                {
                    clothToVolume = volumeToWorld.inverse * clothToWorld,
                    volumeToCloth = worldToCloth * volumeToWorld,
                    shapeParams = new Vector4(extents.x, extents.y, extents.z, volume.BlendDistance),
                    paramsA = new Vector4(volume.Strength * intensity, volume.GustAmplitude, volume.GustFrequency, volume.Weight),
                    paramsB = paramsB,
                    shape = volume.Global ? 0u : volume.Shape == VelaClothVolumeShape.Box ? 1u : 2u,
                    field = (uint)volume.Field
                };

                if (wind)
                    _wind[nWind++] = packed;
                else
                    _force[nForce++] = packed;
            }

            WindCount = nWind;
            ForceCount = nForce;
            if (nWind > 0)
                _windBuffer.SetData(_wind, 0, 0, nWind);
            if (nForce > 0)
                _forceBuffer.SetData(_force, 0, 0, nForce);

            (_scroll, _next) = (_next, _scroll);
        }

        /// <summary>Advances every packed turbulence field's scroll by the step just run. Integrated rather than derived as speed × time, so animating the speed never snaps the eddies.</summary>
        public void Advance(float dt)
        {
            foreach ((VelaClothForceVolume volume, float rate) in _scrolling)
                _scroll[volume] += rate * dt;
        }

        /// <summary>Drops the scroll history. Call after a reset or a cache seek.</summary>
        public void ClearHistory()
        {
            _scroll.Clear();
            _next.Clear();
            _scrolling.Clear();
        }

        public void Dispose()
        {
            _windBuffer?.Dispose();
            _forceBuffer?.Dispose();
            _windBuffer = _forceBuffer = null;
            WindCount = ForceCount = 0;
            ClearHistory();
        }
    }
}
