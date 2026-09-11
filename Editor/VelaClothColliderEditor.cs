using UnityEditor;
using UnityEngine;

namespace Vela.Editor
{
    [CustomEditor(typeof(VelaClothCollider))]
    [CanEditMultipleObjects]
    public sealed class VelaClothColliderEditor : UnityEditor.Editor
    {
        public static readonly VelaClothInspectorGroup[] Groups =
        {
            new VelaClothInspectorGroup("Shape", "type", "radius", "height", "size"),
            new VelaClothInspectorGroup("Contact", "friction", "thickness"),
            new VelaClothInspectorGroup("Gizmo", "alwaysDrawGizmo", "gizmoColor")
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty type = VelaClothInspectorSections.Find(serializedObject, "type");
            VelaClothInspectorSections.Draw(serializedObject, Groups[0], () => DrawShape(type));
            VelaClothInspectorSections.Draw(serializedObject, Groups[1]);
            VelaClothInspectorSections.Draw(serializedObject, Groups[2]);

            serializedObject.ApplyModifiedProperties();

            if (targets.Length != 1)
                return;

            var shape = (VelaClothColliderType)type.enumValueIndex;

            if (shape == VelaClothColliderType.Plane)
                EditorGUILayout.HelpBox("The plane is the local Y = 0 half space facing +Y, and is infinite — the gizmo quad only shows its orientation.",
                    MessageType.None);

            if (IsRadial(shape) && !IsUniformlyScaled((VelaClothCollider)target))
                EditorGUILayout.HelpBox("Non-uniform scale: spheres and capsules use the largest axis, so the shape will not match the transform.",
                    MessageType.Warning);
        }

        // Only the fields the primitive actually reads, so nothing invites tuning a value the solver ignores.
        void DrawShape(SerializedProperty type)
        {
            EditorGUILayout.PropertyField(type);

            var shape = (VelaClothColliderType)type.enumValueIndex;

            if (IsRadial(shape))
                VelaClothInspectorSections.Fields(serializedObject, "radius");
            if (shape == VelaClothColliderType.Capsule)
                VelaClothInspectorSections.Fields(serializedObject, "height");
            if (shape == VelaClothColliderType.Box)
                VelaClothInspectorSections.Fields(serializedObject, "size");
        }

        static bool IsRadial(VelaClothColliderType shape) =>
            shape == VelaClothColliderType.Sphere || shape == VelaClothColliderType.Capsule;

        static bool IsUniformlyScaled(VelaClothCollider collider)
        {
            Vector3 s = collider.transform.lossyScale;
            return Mathf.Approximately(s.x, s.y) && Mathf.Approximately(s.y, s.z);
        }
    }
}
