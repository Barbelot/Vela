using Unity.Collections;
using UnityEngine;

namespace Vela
{
    /// <summary>Rest distance from every vertex to its nearest pins, built on the CPU behind a dirty flag.</summary>
    public sealed class VelaClothLongRangeAttachment
    {
        /// <summary>Fills a slot a vertex could not reach — fewer distinct pins exist than the anchor count asks for.</summary>
        public const uint NoAnchor = 0xFFFFFFFFu;

        public const int MaxAnchorCount = 4;

        struct Entry
        {
            public float distance;
            public int node;
            public int anchor;
        }

        readonly int _anchorCount;
        readonly uint[] _anchors;
        readonly float[] _distances;

        public int AnchorCount => _anchorCount;
        public uint[] Anchors => _anchors;
        public float[] Distances => _distances;

        VelaClothLongRangeAttachment(int anchorCount, uint[] anchors, float[] distances)
        {
            _anchorCount = anchorCount;
            _anchors = anchors;
            _distances = distances;
        }

        /// <summary>Multi-source Dijkstra over the 8-neighbour grid, weighted by rest lengths, selecting each vertex's nearest distinct pins. Returns null when nothing is pinned, which is what auto-disables the constraint.</summary>
        public static VelaClothLongRangeAttachment Build(in VelaClothGrid grid, NativeArray<Vector4> rest, int anchorCount)
        {
            int n = grid.VertexCount;
            if (rest.Length != n)
                return null;

            int pins = 0;
            for (int i = 0; i < n; i++)
                if (rest[i].w <= 0f)
                    pins++;

            if (pins == 0)
                return null;

            int k = Mathf.Clamp(anchorCount, 1, MaxAnchorCount);

            // Holding a vertex to its nearest pin alone creases the sheet wherever the nearest one changes:
            // the cap is continuous across that boundary but its pull direction is not, so `Corners` folds
            // along the two midlines and `TopCorners` along the centre. The dial exists to bound cost on a
            // pinned edge; with every pin in reach there is no cost to bound, so take them all.
            if (pins <= MaxAnchorCount)
                k = Mathf.Max(k, pins);

            var anchors = new uint[n * k];
            var distances = new float[n * k];
            var filled = new int[n];
            for (int i = 0; i < anchors.Length; i++)
                anchors[i] = NoAnchor;

            float dx = grid.RestDx;
            float dy = grid.RestDy;
            float diag = Mathf.Sqrt(dx * dx + dy * dy);
            var weights = new[] { dx, dx, dy, dy, diag, diag, diag, diag };
            var offsetX = new[] { -1, 1, 0, 0, -1, 1, -1, 1 };
            var offsetY = new[] { 0, 0, -1, 1, -1, -1, 1, 1 };

            var heap = new Heap(n * k + 1);

            for (int y = 0; y < grid.height; y++)
            for (int x = 0; x < grid.width; x++)
            {
                int id = grid.Id(x, y);
                if (rest[id].w > 0f)
                    continue;

                anchors[id * k] = (uint)id;
                distances[id * k] = 0f;
                // A path through a pinned vertex is always dominated by one starting there, so seeds are
                // closed immediately and never relaxed into again.
                filled[id] = k;

                for (int d = 0; d < 8; d++)
                {
                    int nx = x + offsetX[d];
                    int ny = y + offsetY[d];
                    if (nx < 0 || nx >= grid.width || ny < 0 || ny >= grid.height)
                        continue;

                    int nid = grid.Id(nx, ny);
                    if (filled[nid] < k)
                        heap.Push(new Entry { distance = weights[d], node = nid, anchor = id });
                }
            }

            while (heap.TryPop(out Entry e))
            {
                int slot = filled[e.node];
                if (slot >= k)
                    continue;

                bool duplicate = false;
                for (int j = 0; j < slot; j++)
                    duplicate |= anchors[e.node * k + j] == (uint)e.anchor;
                if (duplicate)
                    continue;

                anchors[e.node * k + slot] = (uint)e.anchor;
                // The path length only ranks the anchors. Storing it as the cap would print the graph
                // metric's anisotropy on the cloth — it is exact along the axes and the 45° diagonals and
                // 8% long at 22.5°, four times the authored slack. The rest sheet is a flat convex
                // rectangle, so the straight line is the geodesic.
                distances[e.node * k + slot] = Vector3.Distance(rest[e.node], rest[e.anchor]);
                filled[e.node] = slot + 1;

                int x = e.node % grid.width;
                int y = e.node / grid.width;

                for (int d = 0; d < 8; d++)
                {
                    int nx = x + offsetX[d];
                    int ny = y + offsetY[d];
                    if (nx < 0 || nx >= grid.width || ny < 0 || ny >= grid.height)
                        continue;

                    int nid = grid.Id(nx, ny);
                    if (filled[nid] < k)
                        heap.Push(new Entry { distance = e.distance + weights[d], node = nid, anchor = e.anchor });
                }
            }

            return new VelaClothLongRangeAttachment(k, anchors, distances);
        }

        sealed class Heap
        {
            Entry[] _items;
            int _count;

            public Heap(int capacity) => _items = new Entry[Mathf.Max(16, capacity)];

            public void Push(Entry e)
            {
                if (_count == _items.Length)
                    System.Array.Resize(ref _items, _items.Length * 2);

                int i = _count++;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_items[parent].distance <= e.distance)
                        break;

                    _items[i] = _items[parent];
                    i = parent;
                }

                _items[i] = e;
            }

            public bool TryPop(out Entry e)
            {
                if (_count == 0)
                {
                    e = default;
                    return false;
                }

                e = _items[0];
                Entry last = _items[--_count];

                int i = 0;
                while (true)
                {
                    int child = 2 * i + 1;
                    if (child >= _count)
                        break;

                    if (child + 1 < _count && _items[child + 1].distance < _items[child].distance)
                        child++;
                    if (_items[child].distance >= last.distance)
                        break;

                    _items[i] = _items[child];
                    i = child;
                }

                _items[i] = last;
                return true;
            }
        }
    }
}
