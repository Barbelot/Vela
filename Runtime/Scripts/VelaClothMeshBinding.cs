using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Vela
{
    /// <summary>Owns the cloth <see cref="UnityEngine.Mesh"/> and the raw vertex buffer the compute kernels write into.</summary>
    public sealed class VelaClothMeshBinding : IDisposable
    {
        public const int StrideWithVelocity = 60;
        public const int StrideNoVelocity = 48;

        public const int OffsetPosition = 0;
        public const int OffsetNormal = 12;
        public const int OffsetTangent = 24;
        public const int OffsetUv = 40;
        public const int OffsetVelocity = 48;

        readonly bool _hasVelocity;
        GraphicsBuffer _vertexBuffer;

        public Mesh Mesh { get; }
        public VelaClothGrid Grid { get; }
        public int Stride => _hasVelocity ? StrideWithVelocity : StrideNoVelocity;
        public bool HasVelocity => _hasVelocity;

        /// <summary>The mesh's own vertex buffer, valid for the binding's lifetime. Never dispose it from outside.</summary>
        public GraphicsBuffer VertexBuffer => _vertexBuffer;

        public VelaClothMeshBinding(VelaClothGrid grid, bool writeMotionVectors)
        {
            Grid = grid;
            _hasVelocity = writeMotionVectors;

            Mesh = new Mesh
            {
                name = $"ClothMesh_{grid.width}x{grid.height}",
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.HideAndDontSave
            };

            // Must precede SetVertexBufferParams, or the compute writes silently go nowhere.
            Mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            Mesh.SetVertexBufferParams(grid.VertexCount, BuildLayout(_hasVelocity));
            Mesh.SetIndexBufferParams(grid.IndexCount, IndexFormat.UInt32);

            UploadRestState();

            Mesh.subMeshCount = 1;
            Mesh.SetSubMesh(0, new SubMeshDescriptor(0, grid.IndexCount, MeshTopology.Triangles),
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices |
                MeshUpdateFlags.DontNotifyMeshUsers);
            // Conservative seed for the frames before the first async AABB readback lands; a flat rest AABB would cull a falling drape.
            Bounds bounds = grid.RestBounds;
            bounds.Expand(grid.size.magnitude);
            Mesh.bounds = bounds;

            _vertexBuffer = Mesh.GetVertexBuffer(0);
        }

        static VertexAttributeDescriptor[] BuildLayout(bool hasVelocity)
        {
            // One stream: SetVertexBufferParams requires stream-ordered descriptors, so TEXCOORD4 cannot precede TEXCOORD0.
            if (!hasVelocity)
            {
                return new[]
                {
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2)
                };
            }

            return new[]
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 3)
            };
        }

        public void UploadRestState()
        {
            int floatsPerVertex = Stride / 4;

            var vertices = new NativeArray<float>(Grid.VertexCount * floatsPerVertex, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            var indices = new NativeArray<uint>(Grid.IndexCount, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);

            try
            {
                Grid.FillVertices(vertices, floatsPerVertex);
                Grid.FillIndices(indices);

                const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds |
                                              MeshUpdateFlags.DontValidateIndices |
                                              MeshUpdateFlags.DontNotifyMeshUsers;

                Mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length, 0, flags);
                Mesh.SetIndexBufferData(indices, 0, 0, indices.Length, flags);
            }
            finally
            {
                vertices.Dispose();
                indices.Dispose();
            }
        }

        public void Dispose()
        {
            _vertexBuffer?.Dispose();
            _vertexBuffer = null;

            if (Mesh == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(Mesh);
            else
                UnityEngine.Object.DestroyImmediate(Mesh);
        }
    }
}
