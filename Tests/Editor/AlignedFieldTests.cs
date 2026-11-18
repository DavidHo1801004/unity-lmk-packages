using System.Collections;

using NUnit.Framework;

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
    }
}
