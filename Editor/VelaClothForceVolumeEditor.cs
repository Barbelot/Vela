using UnityEditor;
using UnityEngine;

namespace Vela.Editor
{
    [CustomEditor(typeof(VelaClothForceVolume))]
    [CanEditMultipleObjects]
    public sealed class VelaClothForceVolumeEditor : UnityEditor.Editor
    {
        public static readonly VelaClothInspectorGroup[] Groups =
        {
            new VelaClothInspectorGroup("Shape", "global", "shape", "size", "radius", "blendDistance", "weight"),
            new VelaClothInspectorGroup("Field",
                "mode", "field", "intensity", "strength", "gustAmplitude", "gustFrequency",
                "inwardPull", "axialLift", "noiseScale", "scrollSpeed"),
            new VelaClothInspectorGroup("Gizmo", "alwaysDrawGizmo", "gizmoColor")
        };

        [MenuItem("GameObject/Vela/Force Volume", false, 10)]
        static void CreateVolume(MenuCommand command)
        {
            var go = new GameObject("Cloth Force Volume", typeof(VelaClothForceVolume));
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create Cloth Force Volume");
            Selection.activeObject = go;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            VelaClothInspectorSections.Draw(serializedObject, Groups[0], DrawShape);
            VelaClothInspectorSections.Draw(serializedObject, Groups[1], DrawField);
            VelaClothInspectorSections.Draw(serializedObject, Groups[2]);

            serializedObject.ApplyModifiedProperties();

            if (targets.Length != 1)
                return;

            var volume = (VelaClothForceVolume)target;
            EditorGUILayout.HelpBox(Readout(volume), MessageType.None);

            if (!volume.Global && volume.Shape == VelaClothVolumeShape.Sphere && !IsUniformlyScaled(volume))
                EditorGUILayout.HelpBox("Non-uniform scale: a sphere uses the largest axis, so the shape will not match the transform.",
                    MessageType.Warning);
        }

        // Only the fields the current shape and field actually read, so nothing invites tuning a value the solver ignores.
        void DrawShape()
        {
            SerializedProperty global = VelaClothInspectorSections.Find(serializedObject, "global");
            SerializedProperty shape = VelaClothInspectorSections.Find(serializedObject, "shape");

            EditorGUILayout.PropertyField(global);

            if (!global.boolValue)
            {
                EditorGUILayout.PropertyField(shape);
                VelaClothInspectorSections.Fields(serializedObject,
                    (VelaClothVolumeShape)shape.enumValueIndex == VelaClothVolumeShape.Box ? "size" : "radius");
                VelaClothInspectorSections.Fields(serializedObject, "blendDistance");
            }

            VelaClothInspectorSections.Fields(serializedObject, "weight");
        }

        void DrawField()
        {
            SerializedProperty field = VelaClothInspectorSections.Find(serializedObject, "field");

            VelaClothInspectorSections.Fields(serializedObject, "mode");
            EditorGUILayout.PropertyField(field);
            VelaClothInspectorSections.Fields(serializedObject, "intensity", "strength", "gustAmplitude", "gustFrequency");

            switch ((VelaClothForceField)field.enumValueIndex)
            {
                case VelaClothForceField.Vortex:
                    VelaClothInspectorSections.Fields(serializedObject, "inwardPull", "axialLift");
                    break;
                case VelaClothForceField.Turbulence:
                    VelaClothInspectorSections.Fields(serializedObject, "noiseScale", "scrollSpeed");
                    break;
            }
        }

        // Pressure at the peak speed is what an artist can compare against a sheet's weight per area.
        static string Readout(VelaClothForceVolume volume)
        {
            float peak = Mathf.Abs(volume.Strength) * volume.Intensity * (1f + volume.GustAmplitude);

            if (volume.Mode == VelaClothForceMode.Acceleration)
                return $"strength in m/s², peak {peak:0.#} m/s² ({peak / 9.81f:0.##} g)";

            float pressure = 0.5f * 1.225f * peak * peak;
            return $"strength in m/s, peak {peak:0.#} m/s, {pressure:0.#} Pa in sea-level air";
        }

        static bool IsUniformlyScaled(VelaClothForceVolume volume)
        {
            Vector3 s = volume.transform.lossyScale;
            return Mathf.Approximately(s.x, s.y) && Mathf.Approximately(s.y, s.z);
        }
    }
}
