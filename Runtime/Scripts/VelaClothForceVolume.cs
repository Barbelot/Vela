using UnityEngine;

namespace Vela
{
    public enum VelaClothVolumeShape
    {
        Box,
        Sphere
    }

    /// <summary>How the field reaches the cloth: as air the fabric's drag and lift answer, or as a mass-independent acceleration beside gravity.</summary>
    public enum VelaClothForceMode
    {
        Wind,
        Acceleration
    }

    public enum VelaClothForceField
    {
        Directional,
        Radial,
        Vortex,
        Turbulence
    }

    /// <summary>A region of space carrying a wind or an acceleration field, authored independently of any cloth. Every cloth samples the volumes it overlaps, additively, each faded by its own falloff.</summary>
    [ExecuteAlways]
    [AddComponentMenu("Vela/Cloth Force Volume")]
    public sealed class VelaClothForceVolume : MonoBehaviour
    {
        [Tooltip("Reach every cloth in the scene at full weight, ignoring shape, size and blend. The transform still orients the field.")]
        [SerializeField] bool global;

        [Tooltip("Box or sphere, centred on the transform. Scale is folded into the size so the blend distance stays metric.")]
        [SerializeField] VelaClothVolumeShape shape = VelaClothVolumeShape.Box;

        [Tooltip("Box dimensions in local metres.")]
        [SerializeField] Vector3 size = new Vector3(2f, 2f, 2f);

        [Tooltip("Sphere radius in local metres.")]
        [SerializeField, Min(0f)] float radius = 1f;

        [Tooltip("Metres over which the field fades in from the surface towards the centre. 0 is a hard edge.")]
        [SerializeField, Min(0f)] float blendDistance = 0.5f;

        [Tooltip("Scales this volume's contribution where several overlap. Volumes add; there is no priority.")]
        [SerializeField, Range(0f, 1f)] float weight = 1f;

        [Tooltip("Wind is a velocity in m/s the cloth's drag and lift answer — orientation-dependent, it saturates once the sheet moves with it and needs the profile's aerodynamics. Acceleration is m/s² added beside gravity: mass-independent and always on.")]
        [SerializeField] VelaClothForceMode mode = VelaClothForceMode.Wind;

        [Tooltip("Directional blows along local +Z. Radial pushes out from the centre (negative strength pulls in). Vortex swirls around local +Y. Turbulence is curl noise, the swirl that makes a large drape flow rather than vibrate.")]
        [SerializeField] VelaClothForceField field = VelaClothForceField.Directional;

        [Tooltip("Scales strength, inward pull and axial lift together, so the whole volume fades from one slider. 0 switches it off.")]
        [SerializeField, Min(0f)] float intensity = 1f;

        [Tooltip("Field magnitude: m/s in Wind mode, m/s² in Acceleration mode. As wind, a flag lifts around 3 and snaps taut past 12. For Turbulence it is the swirl amplitude.")]
        [SerializeField] float strength = 4f;

        [Tooltip("Gust depth as a fraction of strength: 0.5 swings between half and one and a half. Gusts travel along local +Z, crossing the sheet instead of pulsing all of it at once.")]
        [SerializeField, Range(0f, 1f)] float gustAmplitude;

        [Tooltip("Gusts per second. Below 0.3 reads as weather, above 2 as a fan.")]
        [SerializeField, Min(0f)] float gustFrequency = 0.15f;

        [Tooltip("Vortex only: pull towards the axis, in the same unit as strength. Negative flings outward.")]
        [SerializeField] float inwardPull;

        [Tooltip("Vortex only: push along local +Y, in the same unit as strength. Negative sinks.")]
        [SerializeField] float axialLift;

        [Tooltip("Turbulence only: eddies per metre. 0.2 gives rolls the size of a whole sheet, 3 gives ripples.")]
        [SerializeField, Min(0.001f)] float noiseScale = 0.5f;

        [Tooltip("Turbulence only: how fast the eddies drift along local +Z, in m/s. 0 freezes them in place and the sheet settles into them.")]
        [SerializeField, Min(0f)] float scrollSpeed = 2f;

        [Tooltip("Draw the volume even when the object is not selected. It has no renderer, so without this an unselected volume is invisible in the scene view.")]
        [SerializeField] bool alwaysDrawGizmo = true;

        [Tooltip("Wireframe colour. Wind mode draws it lighter than Acceleration mode.")]
        [SerializeField] Color gizmoColor = new Color(0.55f, 1f, 0.45f, 0.9f);

        public bool Global => global;
        public VelaClothVolumeShape Shape => shape;
        public float BlendDistance => blendDistance;
        public float Weight => weight;
        public VelaClothForceMode Mode => mode;
        public VelaClothForceField Field => field;
        public float Intensity => intensity;
        public float Strength => strength;
        public float GustAmplitude => gustAmplitude;
        public float GustFrequency => gustFrequency;
        public float InwardPull => inwardPull;
        public float AxialLift => axialLift;
        public float NoiseScale => noiseScale;
        public float ScrollSpeed => scrollSpeed;

        /// <summary>False when the volume can add nothing, whatever it overlaps.</summary>
        public bool HasEffect =>
            intensity > 0f &&
            (strength != 0f || (field == VelaClothForceField.Vortex && (inwardPull != 0f || axialLift != 0f)));

        /// <summary>Half extents (box) or radius in x (sphere) with the transform's scale folded in, so the volume matrix stays rigid and the blend distance metric.</summary>
        public Vector3 ScaledExtents
        {
            get
            {
                Vector3 s = transform.lossyScale;

                if (shape == VelaClothVolumeShape.Sphere)
                {
                    float uniform = Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
                    return new Vector3(radius * uniform, 0f, 0f);
                }

                return new Vector3(
                    0.5f * Mathf.Abs(size.x * s.x),
                    0.5f * Mathf.Abs(size.y * s.y),
                    0.5f * Mathf.Abs(size.z * s.z));
            }
        }

        void OnEnable() => VelaClothVolumeRegistry.Register(this);

        void OnDisable() => VelaClothVolumeRegistry.Unregister(this);

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
            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;

            Gizmos.color = mode == VelaClothForceMode.Wind ? Color.Lerp(gizmoColor, Color.white, 0.4f) : gizmoColor;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

            Vector3 e = ScaledExtents;
            float hint = 1f;

            if (!global)
            {
                if (shape == VelaClothVolumeShape.Sphere)
                {
                    Gizmos.DrawWireSphere(Vector3.zero, e.x);
                    if (blendDistance > 0f && e.x > blendDistance)
                        Gizmos.DrawWireSphere(Vector3.zero, e.x - blendDistance);
                    hint = e.x;
                }
                else
                {
                    Gizmos.DrawWireCube(Vector3.zero, 2f * e);
                    Vector3 inner = e - Vector3.one * blendDistance;
                    if (blendDistance > 0f && inner.x > 0f && inner.y > 0f && inner.z > 0f)
                        Gizmos.DrawWireCube(Vector3.zero, 2f * inner);
                    hint = Mathf.Min(e.x, Mathf.Min(e.y, e.z));
                }
            }

            DrawFieldHint(Mathf.Max(0.1f, hint * 0.8f));

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        void DrawFieldHint(float r)
        {
            switch (field)
            {
                case VelaClothForceField.Directional:
                    DrawArrow(new Vector3(0f, 0f, -r), new Vector3(0f, 0f, r));
                    break;
                case VelaClothForceField.Radial:
                    foreach (Vector3 d in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
                    {
                        if (strength >= 0f)
                            DrawArrow(d * r * 0.3f, d * r);
                        else
                            DrawArrow(d * r, d * r * 0.3f);
                    }
                    break;
                case VelaClothForceField.Vortex:
                    DrawRing(r);
                    DrawArrow(new Vector3(0f, -r * 0.6f, 0f), new Vector3(0f, r * 0.6f, 0f));
                    break;
                case VelaClothForceField.Turbulence:
                    for (int i = -1; i <= 1; i++)
                        DrawWave(new Vector3(i * r * 0.5f, 0f, 0f), r);
                    break;
            }
        }

        static void DrawArrow(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1e-5f)
                return;

            Vector3 dir = d / len;
            Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            float head = len * 0.2f;

            Gizmos.DrawLine(from, to);
            Gizmos.DrawLine(to, to - dir * head + side * head * 0.5f);
            Gizmos.DrawLine(to, to - dir * head - side * head * 0.5f);
        }

        static void DrawRing(float r)
        {
            const int segments = 24;
            Vector3 prev = new Vector3(r, 0f, 0f);

            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var next = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }

            DrawArrow(new Vector3(r, 0f, -r * 0.05f), new Vector3(r, 0f, r * 0.25f));
        }

        static void DrawWave(Vector3 origin, float r)
        {
            const int segments = 12;
            Vector3 prev = origin + new Vector3(0f, 0f, -r);

            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                var next = origin + new Vector3(0f, Mathf.Sin(t * Mathf.PI * 2f) * r * 0.15f, (t * 2f - 1f) * r);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }

            DrawArrow(prev - new Vector3(0f, 0f, r * 0.2f), prev);
        }
    }
}
