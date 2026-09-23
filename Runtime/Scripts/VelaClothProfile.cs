using UnityEngine;

namespace Vela
{
    public enum VelaClothBendingMode
    {
        Curvature,
        Dihedral,
        CrossQuad
    }

    public enum VelaClothBendingDirections
    {
        RowAndColumn = 2,
        RowColumnAndDiagonals = 4
    }

    public enum VelaClothThicknessMode
    {
        RestSpacingFraction,
        Metres
    }

    /// <summary>Solver quality dial. Stiffness is authored as 0 (limp) to 1 (rigid) and resolves to a compliance against a fixed reference sheet, so one asset means one material at any resolution, sheet size, density or substep count.</summary>
    [CreateAssetMenu(menuName = "Vela/Cloth Profile", fileName = "ClothQuality")]
    public sealed class VelaClothProfile : ScriptableObject
    {
        /// <summary>60 Hz x 8 substeps. Anchors the slider in time the way <see cref="VelaClothConstraintScale.ReferenceSpacing"/> anchors it in space, so a profile means one physical stiffness and α̃ = α / h² tracks it across substep counts.</summary>
        public const float ReferenceSubstepDt = 1f / 480f;

        // Slider to compliance ratio α̃ / Σw|∇C|², as log10 at stiffness 0 and 1. The two families need
        // separate ranges because equal ratios do not read as equal stiffness: a drape sits near 0.7 for
        // distance and near 30 for curvature.
        static readonly Vector2 DistanceRatioRange = new Vector2(1.5f, -3.5f);
        static readonly Vector2 BendingRatioRange = new Vector2(4.5f, -1.5f);

        [Tooltip("Mass per square metre, in kg. Silk is around 0.06, cotton 0.2, denim 0.45, leather 1. It is what makes wind push a heavy sheet less than a light one.")]
        [Min(0.001f)] public float areaDensity = 0.2f;

        [Tooltip("Constraint iterations per simulation step — the main cost dial. Raising it converges harder without changing the material.")]
        [Min(1)] public int substeps = 8;

        [Tooltip("Speed clamp in m/s, applied every substep. A vertex crossing more than the contact distance in one substep tunnels through a layer undetected.")]
        [Min(0.01f)] public float maxVelocity = 20f;

        [Tooltip("Resistance to stretching. 1 is inextensible; below ~0.4 the sheet sags visibly under its own weight.")]
        [Range(0f, 1f)] public float structuralStiffness = 1f;

        [Tooltip("Resistance to diagonal shearing. Four extra dispatches per substep, and it reads paper-like, so drapes usually leave it off.")]
        public bool useShear;
        [Tooltip("How hard the diagonals resist shearing. 1 is paper or leather, 0.6 is denim.")]
        [Range(0f, 1f)] public float shearStiffness = 0.9f;

        [Tooltip("Resistance to folding. 0 is a plastic bag, 1 is sheet metal, and cloth lives around 0.5.")]
        [Range(0f, 1f)] public float bendingStiffness = 0.5f;

        [Tooltip("Curvature is the only mode the solver implements; Dihedral and CrossQuad are reserved.")]
        public VelaClothBendingMode bendingMode = VelaClothBendingMode.Curvature;
        [Tooltip("Adding the diagonals removes the axis-aligned preference in folds, for six more dispatches per substep.")]
        public VelaClothBendingDirections bendingDirections = VelaClothBendingDirections.RowAndColumn;

        [Tooltip("Air drag on the sheet as a whole, in 1/s: 1 leaves 37 % of a velocity after a second. Too much reads as syrup rather than weight — reach for local damping first.")]
        [Min(0f)] public float globalDamping = 1f;

        [Tooltip("Removes velocity differences between neighbouring vertices, in 1/s — the weight dial, killing ripple without touching bulk motion. 30 reads as silk, 100 as denim, 160 as leather; the inspector says when it saturates.")]
        [Min(0f)] public float localDamping = 5f;

        [Tooltip("Caps a vertex's distance from its pins at its rest distance, in one dispatch per substep — cheaper inextensibility than more substeps. Disables itself when nothing is pinned.")]
        public bool useLongRangeAttachment = true;
        [Tooltip("Pins each vertex is held by, for a pinned edge — a sheet pinned at up to four points uses all of them whatever this says, or it creases where the nearest pin changes. Changing it forces a Dijkstra rebuild, about a second at a million vertices.")]
        [Range(1, 4)] public int lraAnchorCount = 1;
        [Tooltip("How far past its rest distance from a pin a vertex may travel.")]
        [Range(0f, 0.1f)] public float lraStretchAllowance = 0.02f;

        [Tooltip("Stops the sheet passing through itself, for nine dispatches per solved substep and its own buffers.")]
        public bool useSelfCollision;
        [Tooltip("Whether half-thickness follows the grid or is a fixed metric value. The fraction keeps contact coverage right at any resolution but thins the drape as resolution rises; metres holds the thickness an artist sees.")]
        public VelaClothThicknessMode selfCollisionThicknessMode = VelaClothThicknessMode.RestSpacingFraction;

        [Tooltip("Half-thickness as a fraction of rest spacing. Below ~0.5 the contact spheres no longer cover the surface and the sheet crosses itself between vertices.")]
        [Range(0.1f, 0.95f)] public float selfCollisionRadiusScale = 0.8f;

        [Tooltip("Half-thickness in metres. Clamped below one rest spacing, so a grid too coarse to carry it thins the cloth — the inspector says when that bites.")]
        [Min(0f)] public float selfCollisionThickness = 0.015f;
        [Tooltip("Solve self-collision every Nth substep — the feature's cost dial. Raising it drops both dispatches and the contacts a fast fold gets.")]
        [Min(1)] public int selfCollisionStride = 2;
        [Tooltip("Contacts a vertex resolves before it stops looking. Bounds the cost of a crumple, and costs a pile its remaining contacts when it binds.")]
        [Min(1)] public int maxContactsPerVertex = 16;
        [Tooltip("Coulomb friction at a self contact: 0 lets folds slide over each other, 1 makes them grip.")]
        [Range(0f, 1f)] public float selfCollisionFriction = 0.3f;

        [Tooltip("Per-triangle drag and lift. The only path wind volumes have into the cloth, so off makes every one of them inert (acceleration volumes still act) — and drops the heaviest ALU in the solver.")]
        public bool useAerodynamics = true;

        [Tooltip("Force along the flow. Drag alone gives a limp, wet look. Around 1 for cloth.")]
        [Min(0f)] public float dragCoefficient = 1f;

        [Tooltip("Force across the flow, what makes a flag billow instead of streaming flat. Silk sits near 0.35 and leather near 0.05.")]
        [Min(0f)] public float liftCoefficient = 0.2f;

        public int EffectiveBendingDirections => (int)bendingDirections;

        public float LraSlack => 1f + lraStretchAllowance;

        /// <summary>False when no coefficient can turn the wind into force, which is what lets the solver drop the aerodynamic variant whole.</summary>
        public bool HasAerodynamicResponse =>
            useAerodynamics && (dragCoefficient > 0f || liftCoefficient > 0f);

        /// <summary>Compliance in m/N for the structural and shear distance constraints.</summary>
        public float DistanceCompliance(float stiffness) =>
            Compliance(stiffness, DistanceRatioRange, VelaClothConstraintScale.ReferenceDistance(areaDensity));

        /// <summary>Compliance for the curvature triples, whose gradients carry an extra 1 / restSpacing.</summary>
        public float BendingCompliance() =>
            Compliance(bendingStiffness, BendingRatioRange, VelaClothConstraintScale.ReferenceBending(areaDensity));

        static float Compliance(float stiffness, Vector2 range, float referenceGradientSum)
        {
            if (stiffness >= 1f)
                return 0f;

            float ratio = Mathf.Pow(10f, Mathf.Lerp(range.x, range.y, Mathf.Clamp01(stiffness)));
            return ratio * referenceGradientSum * ReferenceSubstepDt * ReferenceSubstepDt;
        }

        static VelaClothProfile _fallback;

        /// <summary>Used when no profile is assigned, so the component is never dead on arrival.</summary>
        public static VelaClothProfile Fallback
        {
            get
            {
                if (_fallback == null)
                {
                    _fallback = CreateInstance<VelaClothProfile>();
                    _fallback.name = "ClothQuality_Fallback";
                    _fallback.hideFlags = HideFlags.HideAndDontSave;
                }

                return _fallback;
            }
        }
    }

    /// <summary>Σw|∇C|² per constraint family — the scale a compliance has to be comparable to before it bites.</summary>
    public static class VelaClothConstraintScale
    {
        /// <summary>The sheet a profile is authored against: 5 m at resolution 128, which is the sample scene's grid. Every slider means what it means on this sheet, and the reference spacing below carries that meaning to every other grid.</summary>
        public const int ReferenceResolution = 128;

        public const float ReferenceSizeMeters = 5f;

        /// <summary>Rest spacing of the reference sheet, in metres — the one length compliance is allowed to see. Taking Σw|∇C|² from the live grid instead ties α to the cell area, which softens a sheet as 1/res² in stretch and 1/res⁴ in bending and inverts its size dependence.</summary>
        public const float ReferenceSpacing = ReferenceSizeMeters / (ReferenceResolution - 1);

        public static float Spacing(in VelaClothGrid grid) =>
            Mathf.Max(1e-6f, Mathf.Min(grid.RestDx, grid.RestDy));

        public static float VertexMass(in VelaClothGrid grid, float areaDensity) =>
            Mathf.Max(1e-6f, areaDensity * grid.RestDx * grid.RestDy);

        public static float ReferenceVertexMass(float areaDensity) =>
            Mathf.Max(1e-6f, areaDensity * ReferenceSpacing * ReferenceSpacing);

        public static float Distance(in VelaClothGrid grid, float areaDensity) =>
            2f / VertexMass(grid, areaDensity);

        public static float Bending(in VelaClothGrid grid, float areaDensity)
        {
            float spacing = Spacing(grid);
            return 6f / (VertexMass(grid, areaDensity) * spacing * spacing);
        }

        public static float ReferenceDistance(float areaDensity) =>
            2f / ReferenceVertexMass(areaDensity);

        public static float ReferenceBending(float areaDensity) =>
            6f / (ReferenceVertexMass(areaDensity) * ReferenceSpacing * ReferenceSpacing);

        /// <summary>Corrects the Laplacian velocity smoothing for spacing: the neighbour mean is a discrete Laplacian carrying dx²/4, so a rate in 1/s diffuses over four times less cloth at twice the resolution without this.</summary>
        public static float SmoothingScale(in VelaClothGrid grid)
        {
            float ratio = ReferenceSpacing / Spacing(grid);
            return ratio * ratio;
        }
    }
}
