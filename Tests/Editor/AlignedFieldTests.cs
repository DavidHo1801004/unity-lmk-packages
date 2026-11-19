using System.Collections;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;

namespace LMK.Editor.Tests
{
    public class AlignedFieldTests : WindowTestFixture
    {
        [Test]
        public void Constructor_HasLabelInputAndBaseFieldClasses()
        {
            var field = new AlignedField("Attribute");

            Assert.That(field.label, Is.EqualTo("Attribute"));
            Assert.That(field.labelElement.hierarchy.parent, Is.SameAs(field));
            Assert.That(field.Input.hierarchy.parent, Is.SameAs(field));
            Assert.That(field.ClassListContains(BaseField<object>.ussClassName), Is.True);
            Assert.That(field.ClassListContains(BaseField<object>.alignedFieldUssClassName), Is.True);
            Assert.That(field.ClassListContains(AlignedField.ussClassName), Is.True);
            Assert.That(field.labelElement.ClassListContains(BaseField<object>.labelUssClassName), Is.True);
            Assert.That(field.Input.ClassListContains(BaseField<object>.inputUssClassName), Is.True);
        }

        [Test]
        public void Add_PutsChildrenInTheInput()
        {
            var field = new AlignedField("Attribute");
            var button = new Button();

            field.Add(button);

            Assert.That(button.hierarchy.parent, Is.SameAs(field.Input));
            Assert.That(field.contentContainer, Is.SameAs(field.Input));
            Assert.That(field.hierarchy.childCount, Is.EqualTo(2), "only the label and the input");
        }

        [Test]
        public void NullLabel_HasNoLabelInTheHierarchy()
        {
            var field = new AlignedField();

            Assert.That(field.labelElement.hierarchy.parent, Is.Null);
            Assert.That(field.Input.hierarchy.parent, Is.SameAs(field));
        }

        [Test]
        public void AlignLabelToTop_TogglesClass()
        {
            var field = new AlignedField("Tags") { AlignLabelToTop = true };
            Assert.That(field.ClassListContains(AlignedField.USS_CLASS_LABEL_TOP), Is.True);

            field.AlignLabelToTop = false;
            Assert.That(field.ClassListContains(AlignedField.USS_CLASS_LABEL_TOP), Is.False);
        }

        [UnityTest]
        public IEnumerator InInspector_LabelLinesUpWithBuiltInFields()
        {
            var inspector = new InspectorElement();
            Root.Add(inspector);

            var builtIn = new TextField("Name");
            builtIn.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            inspector.Add(builtIn);

            var field = new AlignedField("Attribute");
            field.Add(new Button { text = "None" });
            inspector.Add(field);

            yield return WaitForLayout(field);
            yield return WaitUntil(() => Mathf.Approximately(field.labelElement.layout.width, builtIn.labelElement.layout.width)
                && field.labelElement.layout.width > 0f);

            // In a 500px window, alignment widens labels past their 120px minimum: equal widths alone could be two unaligned labels.
            Assert.That(builtIn.labelElement.layout.width, Is.GreaterThan(130f), "the inspector aligned the built-in field");
            Assert.That(field.labelElement.layout.width, Is.EqualTo(builtIn.labelElement.layout.width).Within(0.5f));
            Assert.That(field.labelElement.worldBound.xMax, Is.EqualTo(builtIn.labelElement.worldBound.xMax).Within(0.5f));
            Assert.That(field.Input.worldBound.xMin, Is.EqualTo(builtIn.Q(className: TextField.inputUssClassName).worldBound.xMin).Within(2f));
        }

        [UnityTest]
        public IEnumerator AlignLabelToTop_KeepsLabelOnTheFirstLineOfTallContent()
        {
            var field = new AlignedField("Tags") { AlignLabelToTop = true };
            var tall = new VisualElement();
            tall.style.height = 80f;
            tall.style.flexGrow = 1f;
            field.Add(tall);
            Root.Add(field);

            yield return WaitForLayout(field);
            yield return WaitUntil(() => field.worldBound.height >= 80f);

            Assert.That(field.worldBound.height, Is.GreaterThanOrEqualTo(80f));
            Assert.That(field.labelElement.worldBound.height, Is.LessThan(30f), "the label isn't stretched to the content");
            Assert.That(field.labelElement.worldBound.yMin, Is.EqualTo(field.Input.worldBound.yMin).Within(3f));
        }

        #region In property lists, nested structs and a scroll view
        // A built-in float field at the top level, and AlignedFields drawn by a property drawer: at the top level, in a
        // nested struct and its inner struct, as PropertyList rows (by the drawer and by makeItem), and in a nested
        // struct inside a PropertyList row. All in an inspector inside a vertical scroll view.
        private const int ALIGNED_FIELD_COUNT = 9;

        private readonly List<Object> hosts = new();

        private ScrollView scrollView;

        private VisualElement spacer;

        private FloatField plainField;

        private List<AlignedField> AlignedFields => scrollView.Query<AlignedField>().ToList();

        [TearDown]
        public void DestroyHosts()
        {
            foreach (var host in hosts)
            {
                if (host) Object.DestroyImmediate(host);
            }
            hosts.Clear();
        }

        // _InMainContainer: inside an element with Unity's "unity-inspector-main-container" class, as in the Inspector
        // window, whose width Unity then uses for label widths, scroll bar included.
        private IEnumerator ShowHostInScrollingInspector(bool _InMainContainer = false)
        {
            plainField = null; // a field of the previous test's window otherwise
            var host = ScriptableObject.CreateInstance<AlignedFieldTestHost>();
            hosts.Add(host);
            var serializedObject = new SerializedObject(host);
            foreach (string path in new[] { "nested", "nested.inner", "nestedList.Array.data[0]", "nestedList.Array.data[0].inner" })
                serializedObject.FindProperty(path).isExpanded = true;

            // Tall enough that the content fits: the scroll bar test adds height to make one appear.
            window.position = new Rect(100, 100, 500, 1000);

            scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1f;
            if (_InMainContainer)
            {
                var mainContainer = new VisualElement { style = { flexGrow = 1f } };
                mainContainer.AddToClassList(INSPECTOR_MAIN_CONTAINER_CLASS);
                mainContainer.Add(scrollView);
                Root.Add(mainContainer);
            }
            else
            {
                Root.Add(scrollView);
            }

            var inspector = new InspectorElement();
            scrollView.Add(inspector);

            // Bound one by one: binding the InspectorElement itself would replace its children with a default inspector.
            foreach (string path in new[] { "plain", "picked", "nested" })
            {
                var propertyField = new PropertyField(serializedObject.FindProperty(path));
                propertyField.Bind(serializedObject);
                inspector.Add(propertyField);
            }

            inspector.Add(new PropertyList(serializedObject.FindProperty("pickedList"), "Drawer rows"));
            inspector.Add(new PropertyList(serializedObject.FindProperty("pickedList"), "Custom rows")
            {
                makeItem = () =>
                {
                    var field = new AlignedField("Custom");
                    field.Add(new Button { text = "Pick", style = { flexGrow = 1f } });
                    return field;
                },
                bindItem = (_Item, _Element) => ((AlignedField)_Item).label = _Element.displayName,
            });
            inspector.Add(new PropertyList(serializedObject.FindProperty("nestedList"), "Nested rows"));

            spacer = new VisualElement();
            inspector.Add(spacer);

            yield return WaitForLayout(scrollView);
            yield return WaitUntil(() =>
            {
                plainField ??= scrollView.Query<FloatField>().Where(_Field => _Field.label == "Plain").First();
                return plainField != null && AlignedFields.Count == ALIGNED_FIELD_COUNT && LabelsLineUp();
            }, 300);

            Assert.That(plainField, Is.Not.Null, "the built-in reference field");
            Assert.That(AlignedFields.Count, Is.EqualTo(ALIGNED_FIELD_COUNT));
        }

        private bool LabelsLineUp()
        {
            float reference = plainField.labelElement.worldBound.xMax;
            return AlignedFields.All(_Field => Mathf.Abs(_Field.labelElement.worldBound.xMax - reference) < 0.5f);
        }

        // Unity's inspector style gives fields margin-right: -2px, so built-in fields end 2px past the inspector's edge.
        private const float INSPECTOR_FIELD_OVERHANG = 2f;

        private const string INSPECTOR_MAIN_CONTAINER_CLASS = "unity-inspector-main-container";

        // Every AlignedField's label ends where the built-in field's does, whatever its depth. The field ends where the
        // built-in one does, which follows the scroll view's viewport, or left of its PropertyList row's remove button.
        private void AssertAlignedInsideTheView(string _When)
        {
            var viewport = scrollView.contentViewport.worldBound;
            float reference = plainField.labelElement.worldBound.xMax;

            Assert.That(plainField.labelElement.layout.width, Is.GreaterThan(130f), $"{_When}: the inspector aligned the built-in field");
            Assert.That(plainField.worldBound.xMax, Is.EqualTo(viewport.xMax + INSPECTOR_FIELD_OVERHANG).Within(0.5f),
                $"{_When}: the built-in field follows the viewport");

            foreach (var field in AlignedFields)
            {
                string name = $"{_When}: \"{field.label}\" at depth {Depth(field)}";
                Assert.That(field.labelElement.worldBound.xMax, Is.EqualTo(reference).Within(0.5f), name + ", label end");

                var row = Ancestor(field, PropertyList.USS_CLASS_ROW);
                var remove = row?.Q(className: PropertyList.USS_CLASS_REMOVE_BUTTON);
                if (remove != null)
                    Assert.That(field.worldBound.xMax, Is.LessThanOrEqualTo(remove.worldBound.xMin + 0.5f), name + ", left of the remove button");
                else
                    Assert.That(field.worldBound.xMax, Is.EqualTo(plainField.worldBound.xMax).Within(0.5f), name + ", ends with the built-in field");
            }
        }

        private static VisualElement Ancestor(VisualElement _Element, string _ClassName)
        {
            for (var element = _Element.hierarchy.parent; element != null; element = element.hierarchy.parent)
            {
                if (element.ClassListContains(_ClassName)) return element;
            }
            return null;
        }

        private static int Depth(VisualElement _Element)
        {
            int depth = 0;
            for (var element = _Element.hierarchy.parent; element != null; element = element.hierarchy.parent)
            {
                if (element is Foldout || element.ClassListContains(PropertyList.USS_CLASS_ROW)) depth++;
            }
            return depth;
        }

        [UnityTest]
        public IEnumerator InPropertyListsAndNestedStructs_LabelsLineUp_InsideTheView()
        {
            yield return ShowHostInScrollingInspector();

            AssertAlignedInsideTheView("without a scroll bar");
        }

        // When a vertical scroll bar appears, the viewport narrows: the fields follow it, not the window.
        [UnityTest]
        public IEnumerator ScrollBarAppears_FieldsFollowTheViewport()
        {
            yield return ShowHostInScrollingInspector();
            float viewportBefore = scrollView.contentViewport.worldBound.width;
            float labelBefore = plainField.labelElement.layout.width;

            spacer.style.height = 2000f;
            yield return WaitUntil(() => scrollView.contentViewport.worldBound.width < viewportBefore - 1f, 300);
            Assert.That(scrollView.contentViewport.worldBound.width, Is.LessThan(viewportBefore - 1f), "the scroll bar takes room");

            yield return WaitUntil(() => plainField.labelElement.layout.width < labelBefore - 0.5f && LabelsLineUp(), 300);

            Debug.Log($"[AlignedField] viewport {viewportBefore:0.#} -> {scrollView.contentViewport.worldBound.width:0.#}, "
                + $"built-in label {labelBefore:0.#} -> {plainField.labelElement.layout.width:0.#}");
            Assert.That(plainField.labelElement.layout.width, Is.LessThan(labelBefore - 0.5f), "labels follow the narrower viewport");
            AssertAlignedInsideTheView("with a scroll bar");

            spacer.style.height = 0f;
            yield return WaitUntil(() => plainField.labelElement.layout.width > labelBefore - 0.5f && LabelsLineUp(), 300);
            AssertAlignedInsideTheView("after the scroll bar is gone");
        }

        // As in the Inspector window: Unity sizes labels from the main container, which includes the scroll bar, so
        // labels keep their width when one appears, while fields still end at the viewport. AlignedFields do the same.
        [UnityTest]
        public IEnumerator InInspectorMainContainer_ScrollBarAppears_LabelsKeepTheirWidth_FieldsFollowTheViewport()
        {
            yield return ShowHostInScrollingInspector(true);
            float viewportBefore = scrollView.contentViewport.worldBound.width;
            float labelBefore = plainField.labelElement.layout.width;
            AssertAlignedInsideTheView("without a scroll bar");

            spacer.style.height = 2000f;
            yield return WaitUntil(() => scrollView.contentViewport.worldBound.width < viewportBefore - 1f, 300);
            yield return WaitFrames(10);
            yield return WaitUntil(LabelsLineUp, 300);

            Assert.That(scrollView.contentViewport.worldBound.width, Is.LessThan(viewportBefore - 1f), "the scroll bar takes room");
            Assert.That(plainField.labelElement.layout.width, Is.EqualTo(labelBefore).Within(0.5f), "Unity keeps the label width");
            AssertAlignedInsideTheView("with a scroll bar");
        }
        #endregion
    }

    /// <summary>
    /// Draws <see cref="AlignedFieldTestHost.Picked"/> as an <see cref="AlignedField"/> holding a button, as a custom
    /// drawer for a picker would.
    /// </summary>
    [CustomPropertyDrawer(typeof(AlignedFieldTestHost.Picked))]
    public class AlignedFieldTestDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty _Property)
        {
            var field = new AlignedField(_Property.displayName);
            field.Add(new Button { text = "Pick", style = { flexGrow = 1f } });
            return field;
        }
    }
}
