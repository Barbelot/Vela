using Unity.Collections;
using UnityEngine;

namespace Vela
{
    public enum VelaClothPinMode
    {
        None,
        TopEdge,
        TopCorners,
        LeftEdge,
        Custom
    }

    /// <summary>Turns a pin mode into the per-vertex inverse mass that rides in <c>_Pos.w</c>.</summary>
    public static class VelaClothPinning
    {
        public static bool IsPinned(in VelaClothGrid grid, VelaClothPinMode mode, int x, int y)
        {
            int top = grid.height - 1;
            switch (mode)
            {
                case VelaClothPinMode.TopEdge: return y == top;
                case VelaClothPinMode.TopCorners: return y == top && (x == 0 || x == grid.width - 1);
                case VelaClothPinMode.LeftEdge: return x == 0;
                default: return false;
            }
        }

        /// <summary>Writes rest positions with invMass packed into w.</summary>
        public static void FillRestState(in VelaClothGrid grid, VelaClothPinMode mode, float areaDensity,
            NativeArray<Vector4> rest)
        {
            float invMass = 1f / VelaClothConstraintScale.VertexMass(grid, areaDensity);

            for (int y = 0; y < grid.height; y++)
            for (int x = 0; x < grid.width; x++)
            {
                Vector3 p = grid.RestPosition(x, y);
                float w = IsPinned(grid, mode, x, y) ? 0f : invMass;
                rest[grid.Id(x, y)] = new Vector4(p.x, p.y, p.z, w);
            }
        }
    }
}
