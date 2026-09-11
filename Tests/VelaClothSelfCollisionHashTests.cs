using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Vela.Tests
{
    /// <summary>Drives VelaClothSelfCollision.compute directly, because a silently wrong prefix sum produces sporadic missed contacts rather than a crash.</summary>
    public sealed class VelaClothSelfCollisionHashTests
    {
        const int ThreadsScan = 512;
        const int Threads1D = 256;

        ComputeShader _cs;

        [SetUp]
        public void SetUp()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("No compute shader support on this device.");

            _cs = Object.Instantiate(Resources.Load<ComputeShader>("Shaders/VelaClothSelfCollision"));
            Assert.IsNotNull(_cs, "VelaClothSelfCollision.compute did not load.");
        }

        [TearDown]
        public void TearDown()
        {
            if (_cs != null)
                Object.DestroyImmediate(_cs);
        }

        // 64 leaves the single scan block seven eighths empty, 512 fills it exactly, and 2048 needs the
        // block-total pass to carry between blocks.
        static readonly int[] CellCounts = { 64, 512, 1024, 2048 };

        [Test]
        public void PrefixSumMatchesACpuExclusiveScan([ValueSource(nameof(CellCounts))] int numCells)
        {
            var counts = new uint[numCells];
            var random = new System.Random(7);
            for (int i = 0; i < numCells; i++)
                counts[i] = (uint)random.Next(0, 5);

            uint total = 0;
            var expected = new uint[numCells];
            for (int i = 0; i < numCells; i++)
            {
                expected[i] = total;
                total += counts[i];
            }

            int blocks = Mathf.Max(1, (numCells + ThreadsScan - 1) / ThreadsScan);
            var cellCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numCells, sizeof(uint));
            var cellStart = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numCells + 1, sizeof(uint));
            var scanBlocks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, blocks, sizeof(uint));

            try
            {
                cellCount.SetData(counts);

                _cs.SetInt("_NumCells", numCells);
                _cs.SetInt("_ScanBlockCount", blocks);
                _cs.SetInt("_VertexCount", (int)total);

                foreach (string kernel in new[] { "KScanLocal", "KScanBlocks", "KScanApply" })
                {
                    int k = _cs.FindKernel(kernel);
                    _cs.SetBuffer(k, "_CellCount", cellCount);
                    _cs.SetBuffer(k, "_CellStart", cellStart);
                    _cs.SetBuffer(k, "_ScanBlocks", scanBlocks);
                }

                _cs.Dispatch(_cs.FindKernel("KScanLocal"), blocks, 1, 1);
                _cs.Dispatch(_cs.FindKernel("KScanBlocks"), 1, 1, 1);
                _cs.Dispatch(_cs.FindKernel("KScanApply"),
                    Mathf.Max(1, (numCells + Threads1D - 1) / Threads1D), 1, 1);

                var actual = new uint[numCells + 1];
                cellStart.GetData(actual);

                for (int i = 0; i < numCells; i++)
                    Assert.AreEqual(expected[i], actual[i], $"cell {i} of {numCells}");

                Assert.AreEqual(total, actual[numCells], "the sentinel must close the last cell's range");
            }
            finally
            {
                cellCount.Dispose();
                cellStart.Dispose();
                scanBlocks.Dispose();
            }
        }

        [Test]
        public void CountingSortPlacesEveryVertexOnceInItsOwnCell()
        {
            VelaClothGrid grid = new VelaClothGrid
            {
                width = 17,
                height = 11,
                size = new Vector2(0.8f, 0.5f),
                pivot = new Vector2(0.5f, 1f)
            };

            int n = grid.VertexCount;
            int numCells = NextPowerOfTwo(2 * n);
            int blocks = Mathf.Max(1, (numCells + ThreadsScan - 1) / ThreadsScan);
            float cellSize = 2f * 0.4f * Mathf.Min(grid.RestDx, grid.RestDy);

            var positions = new Vector4[n];
            for (int y = 0; y < grid.height; y++)
            for (int x = 0; x < grid.width; x++)
            {
                Vector3 p = grid.RestPosition(x, y);
                positions[grid.Id(x, y)] = new Vector4(p.x, p.y, p.z, 1f);
            }

            var pos = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 4 * sizeof(float));
            var cellCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numCells, sizeof(uint));
            var cellStart = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numCells + 1, sizeof(uint));
            var sortedIds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, sizeof(uint));
            var scanBlocks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, blocks, sizeof(uint));

            try
            {
                pos.SetData(positions);

                _cs.SetInt("_W", grid.width);
                _cs.SetInt("_H", grid.height);
                _cs.SetInt("_VertexCount", n);
                _cs.SetInt("_NumCells", numCells);
                _cs.SetInt("_ScanBlockCount", blocks);
                _cs.SetFloat("_CellSize", cellSize);

                string[] kernels =
                {
                    "KHashClear", "KHashCount", "KScanLocal", "KScanBlocks",
                    "KScanApply", "KHashScatter", "KHashSort"
                };

                foreach (string kernel in kernels)
                {
                    int k = _cs.FindKernel(kernel);
                    _cs.SetBuffer(k, "_Pos", pos);
                    _cs.SetBuffer(k, "_CellCount", cellCount);
                    _cs.SetBuffer(k, "_CellStart", cellStart);
                    _cs.SetBuffer(k, "_SortedIds", sortedIds);
                    _cs.SetBuffer(k, "_ScanBlocks", scanBlocks);
                }

                int cellGroups = Mathf.Max(1, (numCells + Threads1D - 1) / Threads1D);
                int vertexGroups = Mathf.Max(1, (n + Threads1D - 1) / Threads1D);

                _cs.Dispatch(_cs.FindKernel("KHashClear"), cellGroups, 1, 1);
                _cs.Dispatch(_cs.FindKernel("KHashCount"), vertexGroups, 1, 1);
                _cs.Dispatch(_cs.FindKernel("KScanLocal"), blocks, 1, 1);
                _cs.Dispatch(_cs.FindKernel("KScanBlocks"), 1, 1, 1);
                _cs.Dispatch(_cs.FindKernel("KScanApply"), cellGroups, 1, 1);
                _cs.Dispatch(_cs.FindKernel("KHashScatter"), vertexGroups, 1, 1);
                _cs.Dispatch(_cs.FindKernel("KHashSort"), cellGroups, 1, 1);

                var starts = new uint[numCells + 1];
                var sorted = new uint[n];
                var cursor = new uint[numCells];
                cellStart.GetData(starts);
                sortedIds.GetData(sorted);
                cellCount.GetData(cursor);

                var seen = new HashSet<uint>();
                foreach (uint id in sorted)
                {
                    Assert.Less(id, (uint)n, "scatter wrote an out-of-range vertex id");
                    Assert.IsTrue(seen.Add(id), $"vertex {id} was scattered twice");
                }

                for (int c = 0; c < numCells; c++)
                {
                    Assert.AreEqual(0u, cursor[c], $"cell {c}'s cursor did not count back down to zero");
                    Assert.LessOrEqual(starts[c], starts[c + 1], $"cell {c} has a reversed range");

                    for (uint k = starts[c]; k < starts[c + 1]; k++)
                    {
                        Assert.AreEqual(c, Hash(positions[sorted[k]], cellSize, numCells),
                            $"slot {k} holds a vertex belonging to another cell");

                        if (k > starts[c])
                            Assert.Less(sorted[k - 1], sorted[k], $"cell {c} is not ordered by id");
                    }
                }
            }
            finally
            {
                pos.Dispose();
                cellCount.Dispose();
                cellStart.Dispose();
                sortedIds.Dispose();
                scanBlocks.Dispose();
            }
        }

        /// <summary>Mirrors ClothCellHash exactly; a divergence here would make the test pass against the wrong thing.</summary>
        static int Hash(Vector4 p, float cellSize, int numCells)
        {
            int cx = Mathf.FloorToInt(p.x / cellSize);
            int cy = Mathf.FloorToInt(p.y / cellSize);
            int cz = Mathf.FloorToInt(p.z / cellSize);

            unchecked
            {
                uint h = (uint)(cx * 92837111) ^ (uint)(cy * 689287499) ^ (uint)(cz * 283923481);
                return (int)(h & (uint)(numCells - 1));
            }
        }

        static int NextPowerOfTwo(int value)
        {
            int p = 64;
            while (p < value)
                p <<= 1;

            return p;
        }
    }
}
