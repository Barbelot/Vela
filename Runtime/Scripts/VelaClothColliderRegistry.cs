using System;
using System.Collections.Generic;
using Vela.Internal;
using UnityEngine;

namespace Vela
{
    /// <summary>Packs the enabled <see cref="VelaClothCollider"/>s into one GPU array, in cloth object space. One instance per solver, so each cloth keeps its own previous-transform history and two cloths never rob each other of collider drag.</summary>
    public sealed class VelaClothColliderRegistry : IDisposable
    {
        public const int MaxColliders = 64;

        static readonly List<VelaClothCollider> Active = new List<VelaClothCollider>();

        /// <summary>The collider's placement relative to the cloth, both ways round.</summary>
        struct Pose
        {
            public Matrix4x4 clothToCollider;
            public Matrix4x4 colliderToCloth;
        }

        readonly GpuCollider[] _packed = new GpuCollider[MaxColliders];

        Dictionary<VelaClothCollider, Pose> _previous = new Dictionary<VelaClothCollider, Pose>();
        Dictionary<VelaClothCollider, Pose> _current = new Dictionary<VelaClothCollider, Pose>();
        GraphicsBuffer _buffer;

        public int Count { get; private set; }
        public GraphicsBuffer Buffer => _buffer;

        /// <summary>The cloth the colliders are packed relative to. Set once; <see cref="Pack"/> reads it every step.</summary>
        public Transform Cloth { get; set; }

        internal static void Register(VelaClothCollider collider)
        {
            if (!Active.Contains(collider))
                Active.Add(collider);
        }

        internal static void Unregister(VelaClothCollider collider) => Active.Remove(collider);

        public VelaClothColliderRegistry()
        {
            _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxColliders, GpuCollider.Stride);
        }

        /// <summary>Rebuilds the GPU array against <see cref="Cloth"/>. Called once per solver step, so a driver running two steps in a frame does not apply the same collider motion twice.</summary>
        public void Pack()
        {
            if (_buffer == null || Cloth == null)
                return;

            Matrix4x4 clothToWorld = Cloth.localToWorldMatrix;
            Matrix4x4 worldToCloth = Cloth.worldToLocalMatrix;

            _current.Clear();
            int n = 0;

            for (int i = 0; i < Active.Count && n < MaxColliders; i++)
            {
                VelaClothCollider collider = Active[i];
                if (collider == null || !collider.isActiveAndEnabled)
                    continue;

                Transform t = collider.transform;
                // Rigid on purpose: scale lives in the primitive's parameters, so thickness and friction stay metric.
                Matrix4x4 colliderToWorld = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                var pose = new Pose
                {
                    clothToCollider = colliderToWorld.inverse * clothToWorld,
                    colliderToCloth = worldToCloth * colliderToWorld
                };

                if (!_previous.TryGetValue(collider, out Pose prev))
                    prev = pose;

                _current[collider] = pose;

                _packed[n++] = new GpuCollider
                {
                    clothToCollider = pose.clothToCollider,
                    colliderToCloth = pose.colliderToCloth,
                    prevClothToCollider = prev.clothToCollider,
                    prevColliderToCloth = prev.colliderToCloth,
                    paramsA = collider.PackedParams,
                    paramsB = new Vector4(collider.Friction, collider.Thickness, 0f, 0f),
                    type = (uint)collider.Type
                };
            }

            Count = n;
            if (n > 0)
                _buffer.SetData(_packed, 0, 0, n);

            (_previous, _current) = (_current, _previous);
        }

        /// <summary>Drops the transform history, so the next step reports no collider motion. Call after a reset or a cache seek.</summary>
        public void ClearHistory()
        {
            _previous.Clear();
            _current.Clear();
        }

        public void Dispose()
        {
            _buffer?.Dispose();
            _buffer = null;
            Count = 0;
            ClearHistory();
        }
    }
}
