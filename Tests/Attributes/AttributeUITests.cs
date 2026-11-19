using System.Linq;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.AbilitySystem.Tests;
using LMK.Editor.UIElements;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

namespace LMK.Editor.AbilitySystem.Tests
{
    public sealed class AttributeUITests : AttributeKindTestBase
    {
        private AttributeHolder holder;

        protected override void OnSetUp() => holder = ScriptableObject.CreateInstance<AttributeHolder>();

        [TearDown]
        public void DestroyHolder() => Object.DestroyImmediate(holder);



        #region Drawer
        [Test]
        public void Drawer_ShowsTheCurrentName_DeletedOrNone()
        {
            var id = AddAttribute("Vitals.Health");
            holder.attribute = GameplayAttribute.FromId(AttributeRegistryId, id, "Old.Name");
            holder.attributes.Add(GameplayAttribute.FromId(AttributeRegistryId, RegistryGuid.NewGuid(), "Vitals.Gone"));
            holder.attributes.Add(GameplayAttribute.None);
            using var serialized = new SerializedObject(holder);

            var live = GameplayAttributeDrawer.Describe(serialized.FindProperty("attribute"));
            Assert.AreEqual("Vitals.Health", live.Text);
            Assert.IsFalse(live.Missing);

            var missing = GameplayAttributeDrawer.Describe(serialized.FindProperty("attributes.Array.data[0]"));
            Assert.AreEqual("Vitals.Gone (deleted)", missing.Text);
            Assert.IsTrue(missing.Missing);
            StringAssert.Contains("deleted", missing.Tooltip);

            Assert.AreEqual("None", GameplayAttributeDrawer.Describe(serialized.FindProperty("attributes.Array.data[1]")).Text);
        }

        [Test]
        public void Drawer_BuildsAnAlignedFieldWithTheName()
        {
            var id = AddAttribute("Vitals.Health");
            holder.attribute = GameplayAttribute.FromId(AttributeRegistryId, id, "Vitals.Health");
            using var serialized = new SerializedObject(holder);

            var field = new GameplayAttributeDrawer().CreatePropertyGUI(serialized.FindProperty("attribute"));

            Assert.IsTrue(field.ClassListContains(GameplayAttributeDrawer.USS_CLASS));
            Assert.AreEqual("Vitals.Health", field.Q<Button>("attribute-button").text);
            Assert.AreEqual("Attribute", field.Q<Label>().text);
        }
        #endregion



        #region Window
        [Test]
        public void Tree_RegistryHeader_ThenOneGroupLevel_ThenAttributes()
        {
            AddAttribute("Vitals.Health");
            AddAttribute("Vitals.MaxHealth");
            AddAttribute("Stats.Core.Speed");
            AddAttribute("Armor");

            var root = AttributeTreeNode.Build(Attributes.Snapshot);
            var header = root.Children.Single(e => e.RegistryId == AttributeRegistryId);
            Assert.IsTrue(header.IsRegistryHeader);
            Assert.AreEqual(AttributeTreeNode.KeyOfRegistry(AttributeRegistryId), header.Key);

            // Groups and ungrouped attributes are sorted together by name.
            CollectionAssert.AreEqual(new[] { "Armor", "Stats.Core", "Vitals" }, header.Children.Select(e => e.ShortName));
            Assert.IsTrue(header.Children[0].IsAttribute, "an attribute without a group sits under the registry");

            var statsCore = header.Children[1];
            Assert.IsTrue(statsCore.IsGroup, "everything before the last segment is a single group");
            var speed = statsCore.Children.Single();
            Assert.AreEqual("Speed", speed.ShortName);
            Assert.AreEqual("Stats.Core", speed.Group);
            Assert.AreEqual("Stats.Core.Speed", speed.FullName);
            Assert.AreEqual(AttributeTreeNode.KeyOf(AttributeRegistryId, AttributeId("Stats.Core.Speed")), speed.Key);

            CollectionAssert.AreEqual(new[] { "Health", "MaxHealth" }, header.Children[2].Children.Select(e => e.ShortName));
        }

        [Test]
        public void Window_ShowsTheTree_AndTheSelectionInTheInspector()
        {
            AddAttribute("Vitals.Health", 100f);
            AddAttribute("Armor");

            var window = ScriptableObject.CreateInstance<AttributeManagerWindow>();
            try
            {
                Invoke(window, "CreateGUI");
                var root = window.rootVisualElement;

                Assert.AreEqual(DisplayStyle.None, root.Q("create-panel").style.display.value, "the main registry exists");
                Assert.IsNull(root.Q("registry-popup"), "every registry is a branch of the tree");
                Assert.IsNotNull(root.Q<CollapsiblePanel>("inspector"));
                Assert.IsNotNull(window.Tree.Find(AttributeTreeNode.KeyOfRegistry(AttributeRegistryId)));
                Assert.IsNotNull(window.Tree.Find(AttributeTreeNode.KeyOfGroup(AttributeRegistryId, "Vitals")));
                Assert.IsNotNull(root.Q<Label>("no-selection-label"));

                Select(window, AttributeTreeNode.KeyOf(AttributeRegistryId, AttributeId("Vitals.Health")));

                Assert.AreEqual("Health", root.Q<EditableLabel>("attribute-title").value);
                Assert.IsTrue(root.Q<EditableLabel>("attribute-title").Editable, "the main registry is editable");
                Assert.IsNotNull(root.Q("title-row").Q<Button>("find-references-button"));
                Assert.AreEqual(MainAttributes.Name, root.Q<LinkLabel>("registry-link").text);
                CollectionAssert.AreEqual(
                    new[] { "Group", "Default", "Min", "Max", "On Max Change", "Meta", "Integer", "Description", "Bound Of" },
                    root.Q("attribute-inspector").Query<FieldRow>().ToList().Select(e => e.Label));
                Assert.AreEqual("Vitals", root.Q<TextField>("group-field").value);
                Assert.AreEqual(100f, root.Q<FloatField>("default-field").value);
                Assert.IsNull(root.Q("delete-button"), "Delete stays in the context menu");
                Assert.IsTrue(window.hasUnsavedChanges, "the window follows the staging session");

                // A group has no details.
                Select(window, AttributeTreeNode.KeyOfGroup(AttributeRegistryId, "Vitals"));
                Assert.IsNotNull(root.Q<Label>("no-selection-label"));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Window_KeepsTheSelection_AcrossARename()
        {
            var id = AddAttribute("Vitals.Health");

            var window = ScriptableObject.CreateInstance<AttributeManagerWindow>();
            try
            {
                Invoke(window, "CreateGUI");
                Select(window, AttributeTreeNode.KeyOf(AttributeRegistryId, id));

                Assert.IsTrue(AttributeOperations.Rename(id, "Stats.Life").Success);

                Assert.AreEqual("Stats.Life", window.SelectedNode.FullName, "the same attribute, rebuilt under its new group");
                Assert.AreEqual("Stats", window.rootVisualElement.Q<TextField>("group-field").value);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Inspector_BoundOf_LinksSelectTheDependent()
        {
            AddAttribute("Vitals.MaxHealth", 100f);
            AddAttribute("Vitals.Health", 100f);
            SetBounds("Vitals.Health", AttributeBound.FromConstant(0f), BoundTo("Vitals.MaxHealth"));

            var window = ScriptableObject.CreateInstance<AttributeManagerWindow>();
            try
            {
                Invoke(window, "CreateGUI");
                var root = window.rootVisualElement;
                Select(window, AttributeTreeNode.KeyOf(AttributeRegistryId, AttributeId("Vitals.MaxHealth")));

                var link = root.Q("dependents-value").Q<LinkLabel>();
                Assert.AreEqual("Vitals.Health", link.text);
                Assert.IsFalse(root.Q<EnumField>("on-max-change-field").enabledSelf, "MaxHealth has no attribute as its max");

                // The window isn't shown, so the link's handler is called rather than a pointer click sent.
                var clicked = (System.Action)typeof(LinkLabel).GetField("clicked", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link);
                clicked.Invoke();

                Assert.AreEqual("Vitals.Health", window.SelectedNode?.FullName);
                Assert.IsTrue(root.Q<EnumField>("on-max-change-field").enabledSelf, "Health's max is an attribute");
                Assert.AreEqual("Vitals.MaxHealth", root.Q("max-bound").Q<Button>("attribute").text);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void GroupField_AcceptsOnlyLettersAndDigits()
        {
            Assert.IsTrue(AttributeInspectorPanel.IsGroupChar('a'));
            Assert.IsTrue(AttributeInspectorPanel.IsGroupChar('Z'));
            Assert.IsTrue(AttributeInspectorPanel.IsGroupChar('7'));
            Assert.IsFalse(AttributeInspectorPanel.IsGroupChar('.'));
            Assert.IsFalse(AttributeInspectorPanel.IsGroupChar('_'));
            Assert.IsFalse(AttributeInspectorPanel.IsGroupChar(' '));
            Assert.IsFalse(AttributeInspectorPanel.IsGroupChar('é'));

            Assert.IsTrue(AttributeInspectorPanel.IsValidGroup(string.Empty), "no group");
            Assert.IsTrue(AttributeInspectorPanel.IsValidGroup("Vitals2"));
            Assert.IsFalse(AttributeInspectorPanel.IsValidGroup("Stats.Core"));
            Assert.IsFalse(AttributeInspectorPanel.IsValidGroup("My_Group"));

            // A paste keeps its letters and digits.
            Assert.AreEqual("VitalsCore", AttributeInspectorPanel.FilterInserted("Vitals", "Vitals.Co_re", AttributeInspectorPanel.IsGroupChar));
            Assert.AreEqual("AbXc", AttributeInspectorPanel.FilterInserted("Ac", "A.b_X c", AttributeInspectorPanel.IsGroupChar));

            // What an older group already had stays until the commit rejects it.
            Assert.AreEqual("Stats.Cores", AttributeInspectorPanel.FilterInserted("Stats.Core", "Stats.Cores", AttributeInspectorPanel.IsGroupChar));
        }

        [Test]
        public void AddPopup_GroupIsLettersAndDigits_NameAddsUnderscore()
        {
            Assert.IsTrue(AddAttributePopup.IsPartialName(string.Empty));
            Assert.IsTrue(AddAttributePopup.IsPartialName("Vitals."));
            Assert.IsTrue(AddAttributePopup.IsPartialName("Vitals.Max_Health"));
            Assert.IsTrue(AddAttributePopup.IsPartialName("Max_Health"), "no group");
            Assert.IsFalse(AddAttributePopup.IsPartialName("My_Group.Health"));
            Assert.IsFalse(AddAttributePopup.IsPartialName("Stats.Core.Speed"), "one group level");
            Assert.IsFalse(AddAttributePopup.IsPartialName(".Health"));
            Assert.IsFalse(AddAttributePopup.IsPartialName("Vitals.He alth"));

            // Typing at the caret, or over a selection.
            Assert.IsTrue(AddAttributePopup.CanType("Vitals", 6, 6, '.'));
            Assert.IsFalse(AddAttributePopup.CanType("Vitals.Health", 13, 13, '.'), "a second '.'");
            Assert.IsTrue(AddAttributePopup.CanType("Vitals.Health", 6, 7, '_'), "replacing the '.'");
            Assert.IsFalse(AddAttributePopup.CanType("Vitals.Health", 3, 3, '_'), "'_' in the group");
            Assert.IsTrue(AddAttributePopup.CanType("Vitals.Health", 9, 9, '_'));
            Assert.IsFalse(AddAttributePopup.CanType("Max_Health", 4, 4, '.'), "'.' would make Max_ a group");
            Assert.IsFalse(AddAttributePopup.CanType(string.Empty, 0, 0, '.'));
        }

        [Test]
        public void AddPopup_Check_IncompleteHasNoError_DuplicateHasOne()
        {
            AddAttribute("Vitals.Health");

            Assert.IsNull(AddAttributePopup.Check(string.Empty, out bool complete));
            Assert.IsFalse(complete);
            Assert.IsNull(AddAttributePopup.Check("Vitals.", out complete), "still typing");
            Assert.IsFalse(complete);

            StringAssert.Contains("already exists", AddAttributePopup.Check("Vitals.Health", out complete));
            Assert.IsFalse(complete);
            StringAssert.Contains("letters and digits", AddAttributePopup.Check("A.B.C", out complete), "a pasted name");
            Assert.IsFalse(complete);

            Assert.IsNull(AddAttributePopup.Check("Vitals.Shield", out complete));
            Assert.IsTrue(complete);
        }

        [Test]
        public void Window_OffersToCreateTheMainRegistry_WhenThereIsNone()
        {
            // A main registry anywhere would do, so the test one goes.
            AssetDatabase.DeleteAsset(Attributes.MainRegistryPath);
            Attributes.Reload();
            Assume.That(Attributes.Snapshot.Main, Is.Null, "the project has no attribute registry of its own");

            var window = ScriptableObject.CreateInstance<AttributeManagerWindow>();
            try
            {
                Invoke(window, "CreateGUI");
                Assert.AreEqual(DisplayStyle.Flex, window.rootVisualElement.Q("create-panel").style.display.value);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        private static void Invoke(object _Target, string _Method)
            => _Target.GetType().GetMethod(_Method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_Target, null);

        private static void Select(AttributeManagerWindow _Window, string _Key)
            => typeof(AttributeManagerWindow).GetMethod("SelectKey", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_Window, new object[] { _Key });
        #endregion
    }
}
