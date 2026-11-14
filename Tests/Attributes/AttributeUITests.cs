using System.Linq;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.AbilitySystem.Tests;
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
        public void Window_ListsAttributesByGroup_AndShowsTheSelection()
        {
            AddAttribute("Vitals.Health", 100f);
            AddAttribute("Vitals.MaxHealth", 100f);
            AddAttribute("Armor");

            var window = ScriptableObject.CreateInstance<AttributeManagerWindow>();
            try
            {
                Invoke(window, "CreateGUI");
                var root = window.rootVisualElement;

                Assert.AreEqual(DisplayStyle.None, root.Q("create-panel").style.display.value, "the main registry exists");
                CollectionAssert.AreEquivalent(new[] { "(no group)", "Vitals" }, root.Query<Foldout>().ToList().Select(e => e.text));
                Assert.AreEqual(3, root.Query(className: "attribute-manager__item").ToList().Count);
                Assert.IsNotNull(root.Q<Label>("no-selection-label"));

                typeof(AttributeManagerWindow).GetField("selectedAttribute", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(window, AttributeId("Vitals.Health").ToString());
                Invoke(window, "Rebuild");

                Assert.AreEqual("Vitals.Health", root.Q<TextField>("name-field").value);
                Assert.AreEqual(100f, root.Q<FloatField>("default-field").value);
                Assert.IsTrue(root.Q<TextField>("name-field").enabledSelf, "the main registry is editable");
                Assert.IsTrue(window.hasUnsavedChanges, "the window follows the staging session");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
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
        #endregion
    }
}
