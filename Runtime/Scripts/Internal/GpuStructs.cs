using System.Runtime.InteropServices;
using UnityEngine;

namespace Vela.Internal
{
    /// <summary>One analytic primitive as the solver sees it. Matrices map to and from *cloth object space*, not world, because that is the space the solver runs in.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuCollider
    {
        public const int Stride = 4 * 64 + 3 * 16;

        public Matrix4x4 clothToCollider;
        public Matrix4x4 colliderToCloth;

        // Last step's pair. The solver lerps between the two so the collider sweeps across the substeps;
        // without that its whole step of motion lands in substep 1 and the cloth reads it as a huge impulse.
        public Matrix4x4 prevClothToCollider;
        public Matrix4x4 prevColliderToCloth;

        /// <summary>Sphere: w = radius. Capsule: y = half height of the segment, w = radius. Box: xyz = half extents. Plane: unused.</summary>
        public Vector4 paramsA;

        /// <summary>x = friction, y = thickness.</summary>
        public Vector4 paramsB;

        public uint type;
        public uint pad0, pad1, pad2;
    }

    /// <summary>One force volume as the solver sees it, in cloth object space like <see cref="GpuCollider"/>. Fields are evaluated in volume space so a gust or an eddy stays put in the world while the cloth moves through it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GpuVolume
    {
        public const int Stride = 2 * 64 + 3 * 16 + 16;

        public Matrix4x4 clothToVolume;
        public Matrix4x4 volumeToCloth;

        /// <summary>Box: xyz = half extents. Sphere: x = radius. w = blend distance in metres, fading inward from the surface.</summary>
        public Vector4 shapeParams;

        /// <summary>x = strength × intensity, y = gust amplitude, z = gust frequency, w = weight.</summary>
        public Vector4 paramsA;

        /// <summary>Vortex: x = inward pull × intensity, y = axial lift × intensity. Turbulence: x = scroll speed × intensity, z = noise scale, w = metres already scrolled along +Z.</summary>
        public Vector4 paramsB;

        public uint shape;
        public uint field;
        public uint pad0, pad1;
    }
}
