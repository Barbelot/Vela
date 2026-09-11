using UnityEngine;

namespace Vela
{
    public enum VelaClothColliderType
    {
        Sphere,
        Capsule,
        Box,
        Plane
    }

    /// <summary>An analytic collision primitive, self-registering and independent of Unity Physics so the plugin stays standalone and an artist can author volumes that do not exist for physics.</summary>
    [ExecuteAlways]
    [AddComponentMenu("Vela/Cloth Collider")]
    public sealed class VelaClothCollider : MonoBehaviour
    {
        [Tooltip("Which primitive this is. Only the fields the shape uses are shown, and the transform supplies its position and rotation — scale is folded into the parameters below so thickness stays metric.")]
        [SerializeField] VelaClothColliderType type = VelaClothColliderType.Sphere;

        [Tooltip("Sphere and capsule radius, in local metres.")]
        [SerializeField, Min(0f)] float radius = 0.5f;
        [Tooltip("Total capsule height along local Y, caps included.")]
        [SerializeField, Min(0f)] float height = 2f;
        [Tooltip("Box dimensions in local metres. The plane is the local Y = 0 half space, facing +Y.")]
        [SerializeField] Vector3 size = Vector3.one;

        [Tooltip("How much of a tangential slide the contact cancels. 0 slips freely, 1 sticks.")]
        [SerializeField, Range(0f, 1f)] float friction = 0.3f;
        [Tooltip("Gap kept between the surface and the cloth. Below about half the rest spacing the sheet visibly bites into the primitive.")]
        [SerializeField, Min(0f)] float thickness = 0.01f;

        [Tooltip("Draw the shape even when the object is not selected. These volumes have no renderer, so without this an unselected collider is invisible in the scene view.")]
        [SerializeField] bool alwaysDrawGizmo = true;

        [Tooltip("Wireframe colour, so several colliders on one rig stay tellable apart.")]
        [SerializeField] Color gizmoColor = new Color(0.35f, 0.85f, 1f, 0.9f);

        public VelaClothColliderType Type => type;
        public float Friction => friction;
        public float Thickness => thickness;

        /// <summary>Scale is folded in here rather than into the transform matrix, which keeps that matrix rigid so thickness stays metric.</summary>
        public Vector4 PackedParams
        {
            get
            {
                Vector3 s = transform.lossyScale;
                float uniform = Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));

                switch (type)
                {
                    case VelaClothColliderType.Sphere:
                        return new Vector4(0f, 0f, 0f, radius * uniform);
                    case VelaClothColliderType.Capsule:
                        float r = radius * uniform;
                        return new Vector4(0f, Mathf.Max(0f, height * 0.5f * uniform - r), 0f, r);
                    case VelaClothColliderType.Box:
                        return new Vector4(
                            0.5f * Mathf.Abs(size.x * s.x),
                            0.5f * Mathf.Abs(size.y * s.y),
                            0.5f * Mathf.Abs(size.z * s.z), 0f);
                    default:
                        return Vector4.zero;
                }
            }
        }

        void OnEnable() => VelaClothColliderRegistry.Register(this);

        void OnDisable() => VelaClothColliderRegistry.Unregister(this);

        void OnDrawGizmos()
        {
            if (alwaysDrawGizmo)
                DrawGizmo();
        }

        void OnDrawGizmosSelected()
        {
            if (!alwaysDrawGizmo)
                DrawGizmo();
        }

        void DrawGizmo()
        {
            Vector4 p = PackedParams;
            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;

            Gizmos.color = gizmoColor;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

            switch (type)
            {
                case VelaClothColliderType.Sphere:
                    Gizmos.DrawWireSphere(Vector3.zero, p.w);
                    break;
                case VelaClothColliderType.Capsule:
                    DrawWireCapsule(p.y, p.w);
                    break;
                case VelaClothColliderType.Box:
                    Gizmos.DrawWireCube(Vector3.zero, 2f * new Vector3(p.x, p.y, p.z));
                    break;
                case VelaClothColliderType.Plane:
                    DrawWirePlane();
                    break;
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        static void DrawWireCapsule(float halfHeight, float radius)
        {
            var top = new Vector3(0f, halfHeight, 0f);
            Vector3 bottom = -top;

            Gizmos.DrawWireSphere(top, radius);
            Gizmos.DrawWireSphere(bottom, radius);

            foreach (Vector3 offset in new[]
                     {
                         new Vector3(radius, 0f, 0f), new Vector3(-radius, 0f, 0f),
                         new Vector3(0f, 0f, radius), new Vector3(0f, 0f, -radius)
                     })
                Gizmos.DrawLine(top + offset, bottom + offset);
        }

        static void DrawWirePlane()
        {
            const float extent = 2f;

            Gizmos.DrawWireCube(Vector3.zero, new Vector3(2f * extent, 0f, 2f * extent));
            Gizmos.DrawLine(Vector3.zero, Vector3.up * 0.5f);

            for (int i = -1; i <= 1; i++)
            {
                Gizmos.DrawLine(new Vector3(i * extent * 0.5f, 0f, -extent), new Vector3(i * extent * 0.5f, 0f, extent));
                Gizmos.DrawLine(new Vector3(-extent, 0f, i * extent * 0.5f), new Vector3(extent, 0f, i * extent * 0.5f));
            }
        }
    }
}
