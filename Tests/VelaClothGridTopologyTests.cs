using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace Vela.Tests
{
    public sealed class VelaClothGridTopologyTests
    {
        static readonly Vector2Int[] Dims =
        {
            new Vector2Int(2, 2),
            new Vector2Int(5, 5),
            new Vector2Int(8, 5),
            new Vector2Int(7, 12),
            new Vector2Int(64, 33)
        };

        static VelaClothGrid Make(Vector2Int dim) => new VelaClothGrid
        {
            width = dim.x,
            height = dim.y,
            size = new Vector2(dim.x - 1, dim.y - 1) * 0.05f,
            pivot = new Vector2(0.5f, 1f)
        };

        [Test]
        public void StructuralColourClassesAreMatchings([ValueSource(nameof(Dims))] Vector2Int dim)
        {
            VelaClothGrid grid = Make(dim);

            for (int parity = 0; parity < 2; parity++)
            {
                AssertMatching(HorizontalEdges(grid, parity), $"H parity {parity} on {dim}");
                AssertMatching(VerticalEdges(grid, parity), $"V parity {parity} on {dim}");
            }
        }

        [Test]
        public void StructuralColoursCoverEveryEdgeExactlyOnce([ValueSource(nameof(Dims))] Vector2Int dim)
        {
            VelaClothGrid grid = Make(dim);
            var seen = new HashSet<(int, int)>();
            int total = 0;

            for (int parity = 0; parity < 2; parity++)
            foreach ((int a, int b) in HorizontalEdges(grid, parity))
            {
                total++;
                Assert.IsTrue(seen.Add((a, b)), "Edge covered twice.");
            }

            for (int parity = 0; parity < 2; parity++)
            foreach ((int a, int b) in VerticalEdges(grid, parity))
            {
                total++;
                Assert.IsTrue(seen.Add((a, b)), "Edge covered twice.");
            }

            int expected = (grid.width - 1) * grid.height + grid.width * (grid.height - 1);
            Assert.AreEqual(expected, total);
        }

        [Test]
        public void ShearColourClassesAreMatchings([ValueSource(nameof(Dims))] Vector2Int dim)
        {
            VelaClothGrid grid = Make(dim);

            for (int parity = 0; parity < 2; parity++)
            {
                AssertMatching(DiagonalEdges(grid, parity, true), $"DiagA parity {parity} on {dim}");
                AssertMatching(DiagonalEdges(grid, parity, false), $"DiagB parity {parity} on {dim}");
            }
        }

        [Test]
        public void BendingPhasesTouchEachVertexAtMostOnce([ValueSource(nameof(Dims))] Vector2Int dim)
        {
            VelaClothGrid grid = Make(dim);

            for (int phase = 0; phase < 3; phase++)
            {
                AssertDisjointTriples(Triples(grid, phase, 1, 0), $"BendH phase {phase} on {dim}");
                AssertDisjointTriples(Triples(grid, phase, 0, 1), $"BendV phase {phase} on {dim}");
                AssertDisjointTriples(Triples(grid, phase, 1, 1), $"BendDiagA phase {phase} on {dim}");
                AssertDisjointTriples(Triples(grid, phase, 1, -1), $"BendDiagB phase {phase} on {dim}");
            }
        }

        [Test]
        public void BendingPhasesCoverEveryCentreExactlyOnce([ValueSource(nameof(Dims))] Vector2Int dim)
        {
            VelaClothGrid grid = Make(dim);
            var seen = new HashSet<int>();
            int total = 0;

            for (int phase = 0; phase < 3; phase++)
            foreach (int[] t in Triples(grid, phase, 1, 0))
            {
                total++;
                Assert.IsTrue(seen.Add(t[1]), "Centre solved twice.");
            }

            int expected = Mathf.Max(0, grid.width - 2) * grid.height;
            Assert.AreEqual(expected, total);
        }

        [Test]
        public void IndicesStayInRangeAndShareTheFixedDiagonal([ValueSource(nameof(Dims))] Vector2Int dim)
        {
            VelaClothGrid grid = Make(dim);
            var indices = new NativeArray<uint>(grid.IndexCount, Allocator.Temp);

            try
            {
                grid.FillIndices(indices);

                for (int i = 0; i < indices.Length; i++)
                    Assert.Less(indices[i], (uint)grid.VertexCount);

                for (int quad = 0; quad < grid.QuadCount; quad++)
                {
                    int x = quad % (grid.width - 1);
                    int y = quad / (grid.width - 1);
                    uint a = (uint)grid.Id(x, y);
                    uint d = (uint)grid.Id(x + 1, y + 1);

                    int o = quad * 6;
                    Assert.AreEqual(a, indices[o + 0]);
                    Assert.AreEqual(d, indices[o + 2]);
                    Assert.AreEqual(a, indices[o + 3]);
                    Assert.AreEqual(d, indices[o + 4]);
                }
            }
            finally
            {
                indices.Dispose();
            }
        }

        [Test]
        public void PinnedVerticesGetZeroInverseMass()
        {
            VelaClothGrid grid = Make(new Vector2Int(8, 6));
            var rest = new NativeArray<Vector4>(grid.VertexCount, Allocator.Temp);

            try
            {
                VelaClothPinning.FillRestState(grid, VelaClothPinMode.TopEdge, 0.2f, rest);

                for (int x = 0; x < grid.width; x++)
                    Assert.AreEqual(0f, rest[grid.Id(x, grid.height - 1)].w);

                for (int y = 0; y < grid.height - 1; y++)
                for (int x = 0; x < grid.width; x++)
                    Assert.Greater(rest[grid.Id(x, y)].w, 0f);
            }
            finally
            {
                rest.Dispose();
            }
        }

        [Test]
        public void LongRangeReturnsNullWithoutPins()
        {
            VelaClothGrid grid = Make(new Vector2Int(5, 5));
            Assert.IsNull(BuildLra(grid, VelaClothPinMode.None, 1));
        }

        [Test]
        public void LongRangeDistancesToATopEdgeAreTheRowGap()
        {
            VelaClothGrid grid = Make(new Vector2Int(5, 5));
            VelaClothLongRangeAttachment lra = BuildLra(grid, VelaClothPinMode.TopEdge, 1);

            // A full pinned row makes every geodesic a straight climb: a diagonal step costs √2 spacings
            // to cross the same one row, so it never wins.
            for (int y = 0; y < grid.height; y++)
            for (int x = 0; x < grid.width; x++)
            {
                int id = grid.Id(x, y);
                Assert.AreEqual((grid.height - 1 - y) * grid.RestDy, lra.Distances[id], 1e-5f,
                    $"vertex ({x},{y})");
                Assert.AreEqual((uint)grid.Id(x, grid.height - 1), lra.Anchors[id], $"vertex ({x},{y})");
            }
        }

        [Test]
        public void LongRangeDistancesToTwoCornersTakeTheNearer()
        {
            VelaClothGrid grid = Make(new Vector2Int(5, 5));
            VelaClothLongRangeAttachment lra = BuildLra(grid, VelaClothPinMode.TopCorners, 1);
            float s = grid.RestDx;

            for (int y = 0; y < grid.height; y++)
            for (int x = 0; x < grid.width; x++)
            {
                float expected = Mathf.Min(
                    CornerDistance(x, grid.height - 1 - y, 0, s),
                    CornerDistance(x, grid.height - 1 - y, grid.width - 1, s));

                Assert.AreEqual(expected, lra.Distances[grid.Id(x, y)], 1e-5f, $"vertex ({x},{y})");
            }
        }

        [Test]
        public void LongRangeDistancesSatisfyTheTriangleInequality(
            [ValueSource(nameof(Dims))] Vector2Int dim)
        {
            VelaClothGrid grid = Make(dim);
            VelaClothLongRangeAttachment lra = BuildLra(grid, VelaClothPinMode.TopEdge, 1);
            float diag = Mathf.Sqrt(grid.RestDx * grid.RestDx + grid.RestDy * grid.RestDy);

            for (int y = 0; y < grid.height; y++)
            for (int x = 0; x < grid.width; x++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((dx == 0 && dy == 0) || nx < 0 || nx >= grid.width || ny < 0 || ny >= grid.height)
                    continue;

                float w = dx != 0 && dy != 0 ? diag : dx != 0 ? grid.RestDx : grid.RestDy;
                Assert.LessOrEqual(lra.Distances[grid.Id(x, y)],
                    lra.Distances[grid.Id(nx, ny)] + w + 1e-5f, $"({x},{y}) via ({nx},{ny})");
            }
        }

        [Test]
        public void LongRangeSecondAnchorIsADistinctPin()
        {
            VelaClothGrid grid = Make(new Vector2Int(5, 5));
            VelaClothLongRangeAttachment lra = BuildLra(grid, VelaClothPinMode.TopCorners, 2);
            Assert.AreEqual(2, lra.AnchorCount);

            for (int y = 0; y < grid.height - 1; y++)
            for (int x = 0; x < grid.width; x++)
            {
                int slot = grid.Id(x, y) * 2;
                Assert.AreNotEqual(VelaClothLongRangeAttachment.NoAnchor, lra.Anchors[slot + 1],
                    $"vertex ({x},{y}) reached only one corner");
                Assert.AreNotEqual(lra.Anchors[slot], lra.Anchors[slot + 1], $"vertex ({x},{y})");
                Assert.LessOrEqual(lra.Distances[slot], lra.Distances[slot + 1] + 1e-5f, $"vertex ({x},{y})");
            }
        }

        static float CornerDistance(int x, int rowsDown, int cornerX, float spacing)
        {
            int a = Mathf.Abs(x - cornerX);
            int b = rowsDown;
            return Mathf.Min(a, b) * Mathf.Sqrt(2f) * spacing + Mathf.Abs(a - b) * spacing;
        }

        static VelaClothLongRangeAttachment BuildLra(VelaClothGrid grid, VelaClothPinMode mode, int anchorCount)
        {
            var rest = new NativeArray<Vector4>(grid.VertexCount, Allocator.Temp);
            try
            {
                VelaClothPinning.FillRestState(grid, mode, 0.2f, rest);
                return VelaClothLongRangeAttachment.Build(grid, rest, anchorCount);
            }
            finally
            {
                rest.Dispose();
            }
        }

        static IEnumerable<(int a, int b)> HorizontalEdges(VelaClothGrid grid, int parity)
        {
            for (int y = 0; y < grid.height; y++)
            for (int x = parity; x + 1 < grid.width; x += 2)
                yield return (grid.Id(x, y), grid.Id(x + 1, y));
        }

        static IEnumerable<(int a, int b)> VerticalEdges(VelaClothGrid grid, int parity)
        {
            for (int y = parity; y + 1 < grid.height; y += 2)
            for (int x = 0; x < grid.width; x++)
                yield return (grid.Id(x, y), grid.Id(x, y + 1));
        }

        static IEnumerable<(int a, int b)> DiagonalEdges(VelaClothGrid grid, int parity, bool familyA)
        {
            for (int y = 0; y + 1 < grid.height; y++)
            for (int x = parity; x + 1 < grid.width; x += 2)
                yield return familyA
                    ? (grid.Id(x, y), grid.Id(x + 1, y + 1))
                    : (grid.Id(x + 1, y), grid.Id(x, y + 1));
        }

        /// <summary>Mirrors the bending kernels: centres step by 3 along the phased axis, offset by (dx,dy).</summary>
        static IEnumerable<int[]> Triples(VelaClothGrid grid, int phase, int dx, int dy)
        {
            for (int cy = 0; cy < grid.height; cy++)
            for (int cx = 0; cx < grid.width; cx++)
            {
                int stepped = dx != 0 ? cx : cy;
                if (stepped < phase + 1 || (stepped - phase - 1) % 3 != 0)
                    continue;

                int x0 = cx - dx, x1 = cx + dx;
                int y0 = cy - dy, y1 = cy + dy;
                if (Mathf.Min(x0, x1) < 0 || Mathf.Max(x0, x1) >= grid.width)
                    continue;
                if (Mathf.Min(y0, y1) < 0 || Mathf.Max(y0, y1) >= grid.height)
                    continue;

                yield return new[] { grid.Id(x0, y0), grid.Id(cx, cy), grid.Id(x1, y1) };
            }
        }

        static void AssertDisjointTriples(IEnumerable<int[]> triples, string label)
        {
            var touched = new HashSet<int>();
            foreach (int[] t in triples)
            foreach (int id in t)
                Assert.IsTrue(touched.Add(id), $"{label}: vertex {id} written twice.");
        }

        static void AssertMatching(IEnumerable<(int a, int b)> edges, string label)
        {
            var touched = new HashSet<int>();
            foreach ((int a, int b) in edges)
            {
                Assert.IsTrue(touched.Add(a), $"{label}: vertex {a} written twice.");
                Assert.IsTrue(touched.Add(b), $"{label}: vertex {b} written twice.");
            }
        }
    }
}
