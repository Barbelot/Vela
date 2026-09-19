using System;
using Vela.Internal;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vela
{
    /// <summary>GPU AABB reduce over the position buffer, read back asynchronously so no frame ever stalls on it.</summary>
    public sealed class VelaClothBounds : IDisposable
    {
        const int Threads1D = 256;
        const int Components = 6;

        readonly ComputeShader _cs;
        readonly int _kClear, _kReduce;
        readonly int _vertexCount;
        readonly GraphicsBuffer _positions;

        GraphicsBuffer _atomic;
        AsyncGPUReadbackRequest _request;
        bool _pending;

        /// <summary>Latest completed AABB, in object space and unpadded. Falsy until the first readback lands.</summary>
        public bool HasResult { get; private set; }
        public Bounds Value { get; private set; }

        public VelaClothBounds(ComputeShader meshWriteShader, GraphicsBuffer positions, int vertexCount)
        {
            _cs = meshWriteShader;
            _vertexCount = vertexCount;
            _positions = positions;

            _kClear = _cs.FindKernel("KBoundsClear");
            _kReduce = _cs.FindKernel("KBoundsReduce");

            _atomic = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Components, sizeof(uint));
        }

        /// <summary>Issues a reduce when none is in flight and harvests a finished one. Returns true on the frames a new AABB arrives.</summary>
        public bool Pump()
        {
            bool arrived = false;

            if (_pending && _request.done)
            {
                _pending = false;
                arrived = Decode();
            }

            if (!_pending && _atomic != null)
            {
                _cs.SetBuffer(_kClear, ShaderIds.BoundsAtomic, _atomic);
                _cs.SetBuffer(_kReduce, ShaderIds.BoundsAtomic, _atomic);
                _cs.SetBuffer(_kReduce, ShaderIds.Pos, _positions);
                _cs.Dispatch(_kClear, 1, 1, 1);
                _cs.Dispatch(_kReduce, Mathf.Max(1, (_vertexCount + Threads1D - 1) / Threads1D), 1, 1);
                _request = AsyncGPUReadback.Request(_atomic);
                _pending = true;
            }

            return arrived;
        }

        bool Decode()
        {
            if (_request.hasError)
                return false;

            NativeArray<uint> data = _request.GetData<uint>();
            if (data.Length < Components)
                return false;

            var min = new Vector3(Ordered(data[0]), Ordered(data[1]), Ordered(data[2]));
            var max = new Vector3(Ordered(data[3]), Ordered(data[4]), Ordered(data[5]));

            if (!IsFinite(min) || !IsFinite(max) || max.x < min.x || max.y < min.y || max.z < min.z)
                return false;

            var b = new Bounds();
            b.SetMinMax(min, max);
            Value = b;
            HasResult = true;
            return true;
        }

        static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)) &&
            !(float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        static float Ordered(uint u)
        {
            uint bits = (u & 0x80000000u) != 0 ? u & 0x7FFFFFFFu : ~u;
            return BitConverter.Int32BitsToSingle(unchecked((int)bits));
        }

        public void Dispose()
        {
            if (_pending)
            {
                _request.WaitForCompletion();
                _pending = false;
            }

            _atomic?.Dispose();
            _atomic = null;
        }
    }
}
