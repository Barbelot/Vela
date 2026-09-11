using System;
using Unity.Collections;
using UnityEngine;

namespace Vela
{
    /// <summary>Regular quad grid in the local XY plane, facing -Z. Vertex id is <c>y * W + x</c>.</summary>
    [Serializable]
    public struct VelaClothGrid : IEquatable<VelaClothGrid>
    {
        public const int MinResolution = 2;
        public const int MaxResolution = 1024;

        public int width;
        public int height;
        public Vector2 size;
        public Vector2 pivot;

        public int VertexCount => width * height;
        public int QuadCount => (width - 1) * (height - 1);
        public int IndexCount => QuadCount * 6;
        public float RestDx => width > 1 ? size.x / (width - 1) : 0f;
        public float RestDy => height > 1 ? size.y / (height - 1) : 0f;

        public int Id(int x, int y) => y * width + x;

        /// <summary>Derives the two axis counts from the single <paramref name="resolution"/> dial, keeping quads square.</summary>
        public static VelaClothGrid FromResolution(Vector2 size, int resolution, Vector2 pivot)
        {
            size = new Vector2(Mathf.Max(1e-4f, size.x), Mathf.Max(1e-4f, size.y));
            resolution = Mathf.Clamp(resolution, MinResolution, MaxResolution);

            int w, h;
            if (size.x >= size.y)
            {
                w = resolution;
                h = Mathf.Clamp(Mathf.RoundToInt(resolution * size.y / size.x), MinResolution, MaxResolution);
            }
            else
            {
                h = resolution;
                w = Mathf.Clamp(Mathf.RoundToInt(resolution * size.x / size.y), MinResolution, MaxResolution);
            }

            return new VelaClothGrid { width = w, height = h, size = size, pivot = pivot };
        }

        public Vector3 RestPosition(int x, int y)
        {
            return new Vector3(x * RestDx - pivot.x * size.x, y * RestDy - pivot.y * size.y, 0f);
        }

        public Bounds RestBounds
        {
            get
            {
                Vector3 min = RestPosition(0, 0);
                Vector3 max = RestPosition(width - 1, height - 1);
                var b = new Bounds();
                b.SetMinMax(min, max);
                return b;
            }
        }

        /// <summary>Fixed diagonal (x,y)-(x+1,y+1) on every quad, matching the DiagA shear colour family.</summary>
        public void FillIndices(NativeArray<uint> indices)
        {
            int i = 0;
            for (int y = 0; y < height - 1; y++)
            for (int x = 0; x < width - 1; x++)
            {
                uint a = (uint)Id(x, y);
                uint b = (uint)Id(x + 1, y);
                uint c = (uint)Id(x, y + 1);
                uint d = (uint)Id(x + 1, y + 1);

                indices[i++] = a;
                indices[i++] = c;
                indices[i++] = d;

                indices[i++] = a;
                indices[i++] = d;
                indices[i++] = b;
            }
        }

        /// <summary>Writes the flat rest sheet into an interleaved vertex stream of <paramref name="floatsPerVertex"/> floats.</summary>
        public void FillVertices(NativeArray<float> stream, int floatsPerVertex)
        {
            float invW = width > 1 ? 1f / (width - 1) : 0f;
            float invH = height > 1 ? 1f / (height - 1) : 0f;

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int o = Id(x, y) * floatsPerVertex;
                Vector3 p = RestPosition(x, y);

                stream[o + 0] = p.x;
                stream[o + 1] = p.y;
                stream[o + 2] = p.z;

                stream[o + 3] = 0f;
                stream[o + 4] = 0f;
                stream[o + 5] = -1f;

                stream[o + 6] = 1f;
                stream[o + 7] = 0f;
                stream[o + 8] = 0f;
                stream[o + 9] = -1f;

                stream[o + 10] = x * invW;
                stream[o + 11] = y * invH;

                for (int f = 12; f < floatsPerVertex; f++)
                    stream[o + f] = 0f;
            }
        }

        public bool Equals(VelaClothGrid other) =>
            width == other.width && height == other.height && size == other.size && pivot == other.pivot;

        public override bool Equals(object obj) => obj is VelaClothGrid other && Equals(other);

        public override int GetHashCode() => (width, height, size, pivot).GetHashCode();
    }
}
