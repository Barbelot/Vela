using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vela.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Vela.Tests
{
    /// <summary>M7's two authorability claims, as assertions: every serialized field is documented, and every
    /// one of them is reachable in a grouped inspector. Both rot silently the next time a field is added,
    /// which is why they are tested rather than eyeballed.</summary>
    public sealed class VelaClothInspectorCoverageTests
    {
        // Unity's own, and nothing an artist can act on.
        static readonly HashSet<string> Ignored = new HashSet<string>
        {
            "m_Script", "m_ObjectHideFlags", "m_Name", "m_EditorClassIdentifier"
        };

        static IEnumerable<TestCaseData> Inspectors()
        {
            yield return new TestCaseData(typeof(VelaClothSimulation), VelaClothSimulationEditor.Groups)
                .SetName("VelaClothSimulation");
            yield return new TestCaseData(typeof(VelaClothProfile), VelaClothQualityProfileEditor.Groups)
                .SetName("VelaClothProfile");
            yield return new TestCaseData(typeof(VelaClothCollider), VelaClothColliderEditor.Groups)
                .SetName("VelaClothCollider");
        }

        [Test]
        [TestCaseSource(nameof(Inspectors))]
        public void EveryFieldBelongsToExactlyOneGroup(Type type, VelaClothInspectorGroup[] groups)
        {
            string[] drawn = groups.SelectMany(g => g.Fields).ToArray();
            string[] serialized = SerializedFieldNames(type);

            CollectionAssert.AllItemsAreUnique(drawn, $"{type.Name}: a field is drawn by two groups.");

            foreach (string name in serialized)
                CollectionAssert.Contains(drawn, name,
                    $"{type.Name}.{name} is serialized but no inspector group draws it.");

            foreach (string name in drawn)
                CollectionAssert.Contains(serialized, name,
                    $"{type.Name}: a group draws '{name}', which the type does not serialize.");
        }

        [Test]
        [TestCaseSource(nameof(Inspectors))]
        public void EveryFieldHasATooltip(Type type, VelaClothInspectorGroup[] groups)
        {
            foreach (FieldInfo field in SerializedFields(type))
            {
                // A [Serializable] settings struct is a heading for the fields inside it, which carry the text.
                if (IsNestedSettings(field.FieldType))
                {
                    foreach (FieldInfo inner in SerializedFields(field.FieldType))
                        AssertTooltip(field.FieldType, inner);

                    continue;
                }

                AssertTooltip(type, field);
            }
        }

        static void AssertTooltip(Type owner, FieldInfo field)
        {
            var tooltip = field.GetCustomAttribute<TooltipAttribute>();

            Assert.IsNotNull(tooltip, $"{owner.Name}.{field.Name} has no [Tooltip].");
            Assert.IsNotEmpty(tooltip.tooltip.Trim(), $"{owner.Name}.{field.Name} has an empty [Tooltip].");
        }

        static bool IsNestedSettings(Type type) =>
            type.IsValueType && !type.IsPrimitive && !type.IsEnum &&
            type.GetCustomAttribute<SerializableAttribute>() != null &&
            type.Namespace == typeof(VelaClothSimulation).Namespace;

        static IEnumerable<FieldInfo> SerializedFields(Type type) =>
            type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => !f.IsInitOnly && !f.IsLiteral)
                .Where(f => f.IsPublic || f.GetCustomAttribute<SerializeField>() != null)
                .Where(f => f.GetCustomAttribute<NonSerializedAttribute>() == null);

        /// <summary>Depth 0 only — a nested settings struct is drawn whole by the group that names it.</summary>
        static string[] SerializedFieldNames(Type type)
        {
            UnityEngine.Object probe = null;
            GameObject host = null;

            try
            {
                if (typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    probe = ScriptableObject.CreateInstance(type);
                }
                else
                {
                    host = new GameObject("VelaClothCoverageProbe") { hideFlags = HideFlags.HideAndDontSave };
                    probe = host.AddComponent(type);
                }

                var names = new List<string>();
                SerializedProperty iterator = new SerializedObject(probe).GetIterator();

                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (!Ignored.Contains(iterator.name))
                        names.Add(iterator.name);
                }

                return names.ToArray();
            }
            finally
            {
                if (host != null)
                    UnityEngine.Object.DestroyImmediate(host);
                else if (probe != null)
                    UnityEngine.Object.DestroyImmediate(probe);
            }
        }
    }
}
