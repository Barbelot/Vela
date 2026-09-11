using UnityEditor;
using UnityEngine;

namespace Vela.Editor
{
    [CustomEditor(typeof(VelaClothProfile))]
    [CanEditMultipleObjects]
    public sealed class VelaClothQualityProfileEditor : UnityEditor.Editor
    {
        public static readonly VelaClothInspectorGroup[] Groups =
        {
            new VelaClothInspectorGroup("Fabric", "areaDensity"),
            new VelaClothInspectorGroup("Stepping", "substeps", "maxVelocity"),
            new VelaClothInspectorGroup("Stiffness",
                "structuralStiffness", "useShear", "shearStiffness", "bendingStiffness"),
            new VelaClothInspectorGroup("Bending", "bendingMode", "bendingDirections"),
            new VelaClothInspectorGroup("Damping", "globalDamping", "localDamping"),
            new VelaClothInspectorGroup("Long-range attachment",
                "useLongRangeAttachment", "lraAnchorCount", "lraStretchAllowance"),
            new VelaClothInspectorGroup("Self-collision",
                "useSelfCollision", "selfCollisionThicknessMode", "selfCollisionRadiusScale",
                "selfCollisionThickness", "selfCollisionStride", "maxContactsPerVertex",
                "selfCollisionFriction"),
            new VelaClothInspectorGroup("Aerodynamics",
                "useAerodynamics", "dragCoefficient", "liftCoefficient")
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            VelaClothInspectorSections.Draw(serializedObject, Groups[0]);
            VelaClothInspectorSections.Draw(serializedObject, Groups[1]);
            VelaClothInspectorSections.Draw(serializedObject, Groups[2], DrawStiffness);
            VelaClothInspectorSections.Draw(serializedObject, Groups[3]);
            VelaClothInspectorSections.Draw(serializedObject, Groups[4]);
            VelaClothInspectorSections.Draw(serializedObject, Groups[5], DrawLongRange);
            VelaClothInspectorSections.Draw(serializedObject, Groups[6], DrawSelfCollision);
            VelaClothInspectorSections.Draw(serializedObject, Groups[7], DrawAerodynamics);

            serializedObject.ApplyModifiedProperties();
        }

        void DrawStiffness()
        {
            VelaClothInspectorSections.Fields(serializedObject, "structuralStiffness");

            SerializedProperty useShear = VelaClothInspectorSections.Find(serializedObject, "useShear");
            EditorGUILayout.PropertyField(useShear);

            using (new EditorGUI.DisabledScope(!useShear.boolValue))
                VelaClothInspectorSections.Fields(serializedObject, "shearStiffness");

            VelaClothInspectorSections.Fields(serializedObject, "bendingStiffness");
        }

        void DrawLongRange()
        {
            SerializedProperty use =
                VelaClothInspectorSections.Find(serializedObject, "useLongRangeAttachment");
            EditorGUILayout.PropertyField(use);

            using (new EditorGUI.DisabledScope(!use.boolValue))
                VelaClothInspectorSections.Fields(serializedObject, "lraAnchorCount", "lraStretchAllowance");
        }

        void DrawSelfCollision()
        {
            SerializedProperty use = VelaClothInspectorSections.Find(serializedObject, "useSelfCollision");
            EditorGUILayout.PropertyField(use);

            using (new EditorGUI.DisabledScope(!use.boolValue))
            {
                SerializedProperty mode =
                    VelaClothInspectorSections.Find(serializedObject, "selfCollisionThicknessMode");
                EditorGUILayout.PropertyField(mode);

                // The two thickness fields are alternatives, not a pair — showing both invites tuning the
                // one the mode is ignoring.
                VelaClothInspectorSections.Fields(serializedObject,
                    (VelaClothThicknessMode)mode.enumValueIndex == VelaClothThicknessMode.Metres
                        ? "selfCollisionThickness"
                        : "selfCollisionRadiusScale");

                VelaClothInspectorSections.Fields(serializedObject,
                    "selfCollisionStride", "maxContactsPerVertex", "selfCollisionFriction");
            }
        }

        void DrawAerodynamics()
        {
            SerializedProperty use = VelaClothInspectorSections.Find(serializedObject, "useAerodynamics");
            EditorGUILayout.PropertyField(use);

            using (new EditorGUI.DisabledScope(!use.boolValue))
                VelaClothInspectorSections.Fields(serializedObject, "dragCoefficient", "liftCoefficient");

            if (targets.Length != 1)
                return;

            var profile = (VelaClothProfile)target;
            if (use.boolValue && !profile.HasAerodynamicResponse)
                EditorGUILayout.HelpBox(
                    "Both coefficients are 0, so wind cannot reach the cloth and the aerodynamic variant is compiled out anyway.",
                    MessageType.Warning);
        }
    }
}
