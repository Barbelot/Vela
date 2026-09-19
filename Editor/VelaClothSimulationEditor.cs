using Unity.Profiling;
using UnityEditor;
using UnityEngine;

namespace Vela.Editor
{
    [CustomEditor(typeof(VelaClothSimulation))]
    [CanEditMultipleObjects]
    public sealed class VelaClothSimulationEditor : UnityEditor.Editor
    {
        public static readonly VelaClothInspectorGroup[] Groups =
        {
            new VelaClothInspectorGroup("Geometry", "sizeMeters", "resolution", "pivot"),
            new VelaClothInspectorGroup("Quality",
                "qualityProfile", "simulationRate", "maxStepsPerFrame", "preRollSteps"),
            new VelaClothInspectorGroup("Pinning", "pinMode"),
            new VelaClothInspectorGroup("Environment", "forces", "wind", "transformInertia"),
            new VelaClothInspectorGroup("Rendering", "material", "castShadows", "writeMotionVectors")
        };

        /// <summary>Owns no serialized field — it reports what the profile and this grid resolve to together, which is the only place either can be read.</summary>
        static readonly VelaClothInspectorGroup Diagnostics = new VelaClothInspectorGroup("Diagnostics");

        ProfilerRecorder _hashRecorder, _resolveRecorder;

        void OnEnable()
        {
            _hashRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts, VelaClothSelfCollision.HashMarkerName, 15);
            _resolveRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts, VelaClothSelfCollision.ResolveMarkerName, 15);
        }

        void OnDisable()
        {
            _hashRecorder.Dispose();
            _resolveRecorder.Dispose();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            foreach (VelaClothInspectorGroup group in Groups)
                VelaClothInspectorSections.Draw(serializedObject, group);

            serializedObject.ApplyModifiedProperties();

            if (targets.Length != 1)
                return;

            VelaClothInspectorSections.Draw(serializedObject, Diagnostics, DrawDiagnostics, false);
        }

        void DrawDiagnostics()
        {
            var cloth = (VelaClothSimulation)target;
            VelaClothGrid grid = cloth.Grid;

            VelaClothProfile profile = cloth.Profile;
            int substeps = Mathf.Max(1, profile.substeps);
            float rate = Mathf.Max(1f, cloth.SimulationRate);
            float h = 1f / (rate * substeps);
            float invH2 = 1f / (h * h);

            EditorGUILayout.HelpBox(
                $"Grid {grid.width} x {grid.height}\n" +
                $"{grid.VertexCount:N0} vertices, {grid.QuadCount * 2:N0} triangles\n" +
                $"Rest spacing {grid.RestDx * 1000f:0.##} x {grid.RestDy * 1000f:0.##} mm",
                MessageType.None);

            float distanceScale = VelaClothConstraintScale.Distance(grid, profile.areaDensity);
            float bendingScale = VelaClothConstraintScale.Bending(grid, profile.areaDensity);
            int colliders = cloth.ColliderCount;
            VelaClothSelfCollision self = cloth.SelfCollision;
            int dispatches = DispatchesPerSubstep(profile, colliders, cloth.LongRangeActive);
            int perStep = dispatches * substeps + SelfCollisionDispatches(profile, self, substeps);

            EditorGUILayout.HelpBox(
                $"{substeps * rate:N0} substeps/s ({substeps} x {rate:0.#} Hz), {h * 1000f:0.###} ms each\n" +
                Line("structural", profile.structuralStiffness,
                    profile.DistanceCompliance(profile.structuralStiffness), invH2, distanceScale) +
                (profile.useShear
                    ? Line("shear", profile.shearStiffness,
                        profile.DistanceCompliance(profile.shearStiffness), invH2, distanceScale)
                    : "shear       off\n") +
                Line("bending", profile.bendingStiffness, profile.BendingCompliance(), invH2,
                    bendingScale) +
                Damping("damping", profile, grid, h) +
                Wind(profile, cloth.Wind) +
                LongRange(profile, cloth.LongRangeActive, cloth.LongRangeAnchorCount) +
                SelfCollisionLines(profile, grid, self, substeps, _hashRecorder, _resolveRecorder) +
                $"{colliders} collider{(colliders == 1 ? "" : "s")}\n" +
                $"{dispatches} dispatches/substep, {perStep} per step",
                MessageType.None);

            if (GUILayout.Button("Rebuild"))
                cloth.Rebuild();
        }

        // Response is the fraction of a violation the constraint removes per substep, which is the one number
        // that says whether a stiffness setting is doing anything in this particular scene.
        static string Line(string label, float stiffness, float compliance, float invH2, float gradientSum)
        {
            float alphaTilde = compliance * invH2;
            float response = gradientSum / (gradientSum + alphaTilde);

            return $"{label,-11} {stiffness:0.00}  a {compliance:0.###e+0}  " +
                   $"a~ {alphaTilde:0.###e+0} vs {gradientSum:0.###e+0}  response {response * 100f:0.#}%\n";
        }

        // Both are rates in 1/s, so the number an artist can actually feel is the time constant.
        static string Damping(string label, VelaClothProfile profile, in VelaClothGrid grid, float h)
        {
            float global = profile.globalDamping;
            float local = profile.localDamping;

            string swing = global > 0.001f ? $"swing t {1f / global:0.##} s" : "swing none";
            string ripple = local > 0.001f ? $"ripple t {1f / local:0.##} s" : "ripple none";
            string line = $"{label,-11} {swing}, {ripple}\n";

            // The spacing correction is unbounded but the lerp factor is not, so a heavy local damping on a
            // fine grid silently stops tracking the rate it is authored as.
            float smooth = local * h * VelaClothConstraintScale.SmoothingScale(grid);
            if (smooth <= 1f)
                return line;

            return line +
                   $"            local damping saturated: {local:0.#} /s needs {smooth:0.##}x the per-substep " +
                   $"smoothing this grid can carry, so it acts as {1f / (h * VelaClothConstraintScale.SmoothingScale(grid)):0.#} /s\n";
        }

        // Pressure at the quoted speed is what an artist can compare against the sheet's weight per area; the
        // coefficients and the density on their own say nothing about whether the wind will lift this drape.
        static string Wind(VelaClothProfile profile, VelaClothWindSettings wind)
        {
            if (!profile.useAerodynamics)
                return "wind        aerodynamics off\n";

            if (!profile.HasAerodynamicResponse)
                return "wind        fabric has no drag and no lift, so the field cannot reach it\n";

            if (!wind.HasEffect)
                return "wind        still air (no intensity, no speed, no turbulence, or no air density)\n";

            float speed = wind.EffectiveSpeed;
            float turbulence = wind.EffectiveTurbulence;
            float peak = speed * (1f + wind.gustAmplitude) + turbulence;
            float pressure = 0.5f * wind.airDensity * peak * peak;

            return $"wind        {speed:0.#} m/s +{wind.gustAmplitude * 100f:0}% gust " +
                   $"+{turbulence:0.#} swirl, peak {pressure:0.#} Pa\n";
        }

        static string LongRange(VelaClothProfile profile, bool active, int anchors)
        {
            if (!profile.useLongRangeAttachment)
                return "long range  off\n";

            if (!active)
                return "long range  no pins, disabled\n";

            return $"long range  {anchors} anchor{(anchors == 1 ? "" : "s")}, " +
                   $"{profile.lraStretchAllowance * 100f:0.#}% stretch allowed\n";
        }

        // Everything self-collision adds, so the milestone's "toggling it off restores the prior cost exactly"
        // is a line that disappears rather than a claim. The times are CPU submission, which is what a
        // ProfilerMarker can measure — the GPU cost has to be read off a frame capture.
        static string SelfCollisionLines(VelaClothProfile profile, in VelaClothGrid grid, VelaClothSelfCollision self,
            int substeps, ProfilerRecorder hash, ProfilerRecorder resolve)
        {
            if (!profile.useSelfCollision || self == null)
                return "self coll.  off\n";

            float radius = VelaClothSelfCollision.ContactRadius(grid, profile);
            int solved = SolvedSubsteps(profile, substeps);

            // A metric thickness the grid cannot carry is clamped, which thins the cloth without any other sign.
            string clamped = VelaClothSelfCollision.IsThicknessClamped(grid, profile)
                ? $"            thickness clamped: {profile.selfCollisionThickness * 1000f:0.##} mm needs a " +
                  $"rest spacing over {profile.selfCollisionThickness / VelaClothSelfCollision.MaxRadiusScale * 1000f:0.##} mm\n"
                : string.Empty;

            return $"self coll.  radius {radius * 1000f:0.##} mm " +
                   $"({radius / Mathf.Max(1e-6f, Mathf.Min(grid.RestDx, grid.RestDy)):0.##} spacings), " +
                   $"{profile.maxContactsPerVertex} contacts/vertex, " +
                   $"every {profile.selfCollisionStride} substep{(profile.selfCollisionStride == 1 ? "" : "s")}\n" +
                   clamped +
                   $"            {self.CellCount:N0} cells, {self.MemoryBytes / (1024f * 1024f):0.#} MB, " +
                   $"{solved * VelaClothSelfCollision.SolveDispatches} dispatches/step\n" +
                   $"            submit {Microseconds(hash)} hash + {Microseconds(resolve)} resolve, CPU\n";
        }

        static string Microseconds(ProfilerRecorder recorder)
        {
            if (!recorder.Valid || recorder.Count == 0)
                return "-";

            double total = 0.0;
            for (int i = 0; i < recorder.Count; i++)
                total += recorder.GetSample(i).Value;

            return $"{total / recorder.Count / 1000.0:0.##} us";
        }

        static int SolvedSubsteps(VelaClothProfile profile, int substeps)
        {
            int stride = Mathf.Max(1, profile.selfCollisionStride);
            return (substeps + stride - 1) / stride;
        }

        static int SelfCollisionDispatches(VelaClothProfile profile, VelaClothSelfCollision self, int substeps)
        {
            if (self == null)
                return 0;

            return VelaClothSelfCollision.SolveDispatches * SolvedSubsteps(profile, substeps);
        }

        static int DispatchesPerSubstep(VelaClothProfile profile, int colliders, bool longRange)
        {
            int n = 1 + 4 + 1;
            if (profile.useShear)
                n += 4;
            if (colliders > 0)
                n += 1;
            if (longRange)
                n += 1;

            return n + 3 * profile.EffectiveBendingDirections;
        }
    }
}
