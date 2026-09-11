using System;
using Vela.Internal;
using Unity.Profiling;
using UnityEngine;

namespace Vela
{
    /// <summary>Spatial hash plus a Jacobi accumulate/apply pair, allocated only while a profile asks for it.</summary>
    public sealed class VelaClothSelfCollision : IDisposable
    {
        const int Threads1D = 256;
        const int ThreadsScan = 512;

        /// <summary>Under-relaxation on the accumulated correction, which is what keeps a Jacobi resolve stable.</summary>
        const float Relaxation = 0.5f;

        public const string HashMarkerName = "Cloth.SelfCollision.Hash";
        public const string ResolveMarkerName = "Cloth.SelfCollision.Resolve";

        static readonly ProfilerMarker HashMarker = new ProfilerMarker(HashMarkerName);
        static readonly ProfilerMarker ResolveMarker = new ProfilerMarker(ResolveMarkerName);

        readonly ComputeShader _cs;
        readonly int _vertexCount;
        readonly int _numCells;
        readonly int _scanBlockCount;

        readonly int _kHashClear, _kHashCount, _kScanLocal, _kScanBlocks, _kScanApply, _kHashSort, _kHashScatter;
        readonly int _kAccum, _kApply;

        GraphicsBuffer _cellCount, _cellStart, _sortedIds, _scanBlocks, _deltaPos;

        public int CellCount => _numCells;

        /// <summary>Everything the feature adds to the solver's footprint, so the inspector can show what toggling it off gives back.</summary>
        public long MemoryBytes =>
            4L * (_numCells + (_numCells + 1) + _vertexCount + _scanBlockCount) + 16L * _vertexCount;

        /// <summary>Building the hash costs this many, paid together with every resolve.</summary>
        public const int HashDispatches = 7;

        /// <summary>The accumulate/apply pair, on every <c>selfCollisionStride</c>-th substep.</summary>
        public const int ResolveDispatches = 2;

        /// <summary>Hash plus resolve, the whole cost of one solved substep.</summary>
        public const int SolveDispatches = HashDispatches + ResolveDispatches;

        public VelaClothSelfCollision(ComputeShader shader, in VelaClothGrid grid, GraphicsBuffer pos, GraphicsBuffer posPrev)
        {
            _cs = UnityEngine.Object.Instantiate(shader);
            _vertexCount = grid.VertexCount;
            _numCells = NextPowerOfTwo(2 * _vertexCount);
            _scanBlockCount = Mathf.Max(1, (_numCells + ThreadsScan - 1) / ThreadsScan);

            _kHashClear = _cs.FindKernel("KHashClear");
            _kHashCount = _cs.FindKernel("KHashCount");
            _kScanLocal = _cs.FindKernel("KScanLocal");
            _kScanBlocks = _cs.FindKernel("KScanBlocks");
            _kScanApply = _cs.FindKernel("KScanApply");
            _kHashScatter = _cs.FindKernel("KHashScatter");
            _kHashSort = _cs.FindKernel("KHashSort");
            _kAccum = _cs.FindKernel("KSelfCollideAccum");
            _kApply = _cs.FindKernel("KSelfCollideApply");

            _cellCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _numCells, sizeof(uint));
            _cellStart = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _numCells + 1, sizeof(uint));
            _sortedIds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _vertexCount, sizeof(uint));
            _scanBlocks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _scanBlockCount, sizeof(uint));
            _deltaPos = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _vertexCount, 4 * sizeof(float));

            foreach (int k in new[]
                     {
                         _kHashClear, _kHashCount, _kScanLocal, _kScanBlocks, _kScanApply,
                         _kHashScatter, _kHashSort, _kAccum, _kApply
                     })
            {
                _cs.SetBuffer(k, ShaderIds.Pos, pos);
                _cs.SetBuffer(k, ShaderIds.PosPrev, posPrev);
                _cs.SetBuffer(k, ShaderIds.CellCount, _cellCount);
                _cs.SetBuffer(k, ShaderIds.CellStart, _cellStart);
                _cs.SetBuffer(k, ShaderIds.SortedIds, _sortedIds);
                _cs.SetBuffer(k, ShaderIds.ScanBlocks, _scanBlocks);
                _cs.SetBuffer(k, ShaderIds.DeltaPos, _deltaPos);
            }

            _cs.SetInt(ShaderIds.W, grid.width);
            _cs.SetInt(ShaderIds.H, grid.height);
            _cs.SetInt(ShaderIds.VertexCount, _vertexCount);
            _cs.SetInt(ShaderIds.NumCells, _numCells);
            _cs.SetInt(ShaderIds.ScanBlockCount, _scanBlockCount);
            _cs.SetFloat(ShaderIds.SelfRelax, Relaxation);
        }

        /// <summary>Ceiling on the contact radius as a fraction of rest spacing: the accumulate skips the 3x3 grid neighbourhood, so the closest pair an undeformed sheet can test is its 2-ring.</summary>
        public const float MaxRadiusScale = 0.95f;

        /// <summary>The cloth's half-thickness in metres, however the profile authors it.</summary>
        public static float ContactRadius(in VelaClothGrid grid, VelaClothProfile profile)
        {
            float spacing = Mathf.Min(grid.RestDx, grid.RestDy);
            float requested = profile.selfCollisionThicknessMode == VelaClothThicknessMode.Metres
                ? profile.selfCollisionThickness
                : profile.selfCollisionRadiusScale * spacing;

            return Mathf.Clamp(requested, 1e-6f, MaxRadiusScale * spacing);
        }

        /// <summary>True when the grid is too coarse to carry the authored metric thickness, which silently thins the cloth unless something reports it.</summary>
        public static bool IsThicknessClamped(in VelaClothGrid grid, VelaClothProfile profile) =>
            profile.selfCollisionThicknessMode == VelaClothThicknessMode.Metres &&
            profile.selfCollisionThickness > MaxRadiusScale * Mathf.Min(grid.RestDx, grid.RestDy);

        /// <summary>A cell the size of the contact distance, which is exactly what the 27-cell walk needs: the hash is rebuilt immediately before the resolve with nothing writing positions in between, so it carries no motion margin and is coupled to neither <c>maxVelocity</c> nor resolution.</summary>
        public static float CellSize(float radius) => 2f * radius;

        public void PushConstants(float radius, float cellSize, int maxContacts, float friction)
        {
            _cs.SetFloat(ShaderIds.SelfRadius, radius);
            _cs.SetFloat(ShaderIds.CellSize, Mathf.Max(1e-6f, cellSize));
            _cs.SetInt(ShaderIds.MaxContacts, Mathf.Max(1, maxContacts));
            _cs.SetFloat(ShaderIds.SelfFriction, Mathf.Clamp01(friction));
        }

        /// <summary>Counting sort into contiguous cells: no atomic linked list, so neighbour reads stay coherent.</summary>
        public void BuildHash()
        {
            using (HashMarker.Auto())
            {
                int cellGroups = Groups(_numCells, Threads1D);
                int vertexGroups = Groups(_vertexCount, Threads1D);

                _cs.Dispatch(_kHashClear, cellGroups, 1, 1);
                _cs.Dispatch(_kHashCount, vertexGroups, 1, 1);
                _cs.Dispatch(_kScanLocal, _scanBlockCount, 1, 1);
                _cs.Dispatch(_kScanBlocks, 1, 1, 1);
                _cs.Dispatch(_kScanApply, cellGroups, 1, 1);
                _cs.Dispatch(_kHashScatter, vertexGroups, 1, 1);
                _cs.Dispatch(_kHashSort, cellGroups, 1, 1);
            }
        }

        public void Resolve()
        {
            using (ResolveMarker.Auto())
            {
                int vertexGroups = Groups(_vertexCount, Threads1D);
                _cs.Dispatch(_kAccum, vertexGroups, 1, 1);
                _cs.Dispatch(_kApply, vertexGroups, 1, 1);
            }
        }

        static int Groups(int count, int threads) => Mathf.Max(1, (count + threads - 1) / threads);

        /// <summary>Power of two so the hash can mask instead of dividing; a small grid leaves the last scan block partly empty, which the kernels guard for.</summary>
        static int NextPowerOfTwo(int value)
        {
            int p = 64;
            while (p < value)
                p <<= 1;

            return p;
        }

        public void Dispose()
        {
            _cellCount?.Dispose();
            _cellStart?.Dispose();
            _sortedIds?.Dispose();
            _scanBlocks?.Dispose();
            _deltaPos?.Dispose();
            _cellCount = _cellStart = _sortedIds = _scanBlocks = _deltaPos = null;

            if (_cs == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(_cs);
            else
                UnityEngine.Object.DestroyImmediate(_cs);
        }
    }
}
