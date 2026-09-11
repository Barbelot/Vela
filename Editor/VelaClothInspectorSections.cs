using System;
using UnityEditor;
using UnityEngine;

namespace Vela.Editor
{
    /// <summary>One labelled foldout and the fields it owns. The three inspectors declare an array of these, which is both what they draw and what the coverage test reads to prove no serialized field is orphaned.</summary>
    public readonly struct VelaClothInspectorGroup
    {
        public readonly string Label;
        public readonly string[] Fields;

        public VelaClothInspectorGroup(string label, params string[] fields)
        {
            Label = label;
            Fields = fields;
        }
    }

    /// <summary>Foldout groups shared by the three cloth inspectors, with their open state remembered per user.</summary>
    public static class VelaClothInspectorSections
    {
        public static void Draw(SerializedObject serialized, in VelaClothInspectorGroup group,
            bool openByDefault = true, bool enabled = true)
        {
            VelaClothInspectorGroup captured = group;
            Draw(serialized, group, () => Fields(serialized, captured.Fields), openByDefault, enabled);
        }

        public static void Draw(SerializedObject serialized, in VelaClothInspectorGroup group, Action body,
            bool openByDefault = true, bool enabled = true)
        {
            string prefsKey = $"Vela.Section.{serialized.targetObject.GetType().Name}.{group.Label}";
            bool open = EditorPrefs.GetBool(prefsKey, openByDefault);
            bool nowOpen = EditorGUILayout.BeginFoldoutHeaderGroup(open, group.Label);

            if (nowOpen != open)
                EditorPrefs.SetBool(prefsKey, nowOpen);

            if (nowOpen)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUI.DisabledScope(!enabled))
                    body();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        public static void Fields(SerializedObject serialized, params string[] names)
        {
            foreach (string name in names)
                EditorGUILayout.PropertyField(Find(serialized, name), true);
        }

        /// <summary>Throws rather than returning null, so a renamed field fails loudly instead of quietly vanishing from the inspector.</summary>
        public static SerializedProperty Find(SerializedObject serialized, string name)
        {
            SerializedProperty property = serialized.FindProperty(name);

            if (property == null)
                throw new ArgumentException(
                    $"{serialized.targetObject.GetType().Name} has no serialized field '{name}'.", nameof(name));

            return property;
        }
    }
}
