using System.Collections;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;

namespace LMK.Editor.Tests
{
    public class EditableLabelTests : WindowTestFixture
    {
        private EditableLabel label;

        private TextField other;

        private List<string> changes;

        private IEnumerator CreateLabel(string _Value = "Stun")
        {
            label = new EditableLabel { value = _Value };
            label.style.width = 200f;
            changes = new();
            label.RegisterValueChangedCallback(_Event => changes.Add(_Event.newValue));
            Root.Add(label);

            // Somewhere else for focus to go.
            other = new TextField();
            Root.Add(other);

            yield return WaitForLayout(label);
        }

        private IEnumerator BeginEdit()
        {
            label.BeginEdit();
            yield return WaitUntil(() => IsWithin(FocusedElement, label.Field));

            Assert.That(label.IsEditing, Is.True);
            Assert.That(IsWithin(FocusedElement, label.Field), Is.True, $"The field didn't get focus. Focused: {FocusedElement}");
        }

        private VisualElement FocusedInField => IsWithin(FocusedElement, label.Field) ? FocusedElement : label.Field;

        // Typed characters go to the leaf that edits the text, as real key events do.
        private VisualElement TextInput => label.Field.Q(TextField.textInputUssName).Q<TextElement>();

        [UnityTest]
        public IEnumerator Enter_CommitsAndRaisesOneChangeEvent()
        {
            yield return CreateLabel();
            yield return BeginEdit();

            label.Field.value = "Stunned";

            Assert.That(changes, Is.Empty, "Typing raised a change on the label.");

            KeyDown(FocusedInField, KeyCode.Return);

            Assert.That(label.IsEditing, Is.False);
            Assert.That(label.value, Is.EqualTo("Stunned"));
            Assert.That(changes, Is.EqualTo(new[] { "Stunned" }));
        }

        [UnityTest]
        public IEnumerator KeypadEnter_Commits()
        {
            yield return CreateLabel();
            yield return BeginEdit();

            label.Field.value = "Stunned";
            KeyDown(FocusedInField, KeyCode.KeypadEnter);

            Assert.That(label.value, Is.EqualTo("Stunned"));
        }

        [UnityTest]
        public IEnumerator Escape_Cancels()
        {
            yield return CreateLabel();
            yield return BeginEdit();

            label.Field.value = "Stunned";
            KeyDown(FocusedInField, KeyCode.Escape);

            Assert.That(label.IsEditing, Is.False);
            Assert.That(label.value, Is.EqualTo("Stun"));
            Assert.That(changes, Is.Empty);
        }

        [UnityTest]
        public IEnumerator UnchangedCommit_RaisesNothing()
        {
            yield return CreateLabel();
            yield return BeginEdit();

            KeyDown(FocusedInField, KeyCode.Return);

            Assert.That(label.IsEditing, Is.False);
            Assert.That(changes, Is.Empty);
        }

        [UnityTest]
        public IEnumerator Blur_CancelsByDefault()
        {
            yield return CreateLabel();
            yield return BeginEdit();

            label.Field.value = "Stunned";
            other.Focus();

            Assert.That(label.IsEditing, Is.False);
            Assert.That(label.value, Is.EqualTo("Stun"));
            Assert.That(IsWithin(FocusedElement, other), Is.True, "The blur moved focus back.");
        }

        [UnityTest]
        public IEnumerator Blur_CommitsWithCommitOnBlur()
        {
            yield return CreateLabel();
            label.CommitOnBlur = true;
            yield return BeginEdit();

            label.Field.value = "Stunned";
            other.Focus();

            Assert.That(label.IsEditing, Is.False);
            Assert.That(label.value, Is.EqualTo("Stunned"));
            Assert.That(changes, Is.EqualTo(new[] { "Stunned" }));
            Assert.That(IsWithin(FocusedElement, other), Is.True, "The blur moved focus back.");
        }

        [UnityTest]
        public IEnumerator Validate_ErrorKeepsEditingOpen()
        {
            yield return CreateLabel();
            label.Validate = _Text => _Text.Length > 0 ? null : "Name is empty.";
            yield return BeginEdit();

            label.Field.value = string.Empty;
            KeyDown(FocusedInField, KeyCode.Return);

            Assert.That(label.IsEditing, Is.True);
            Assert.That(label.ClassListContains(EditableLabel.USS_CLASS_INVALID), Is.True);
            Assert.That(label.Field.tooltip, Is.EqualTo("Name is empty."));
            Assert.That(changes, Is.Empty);

            label.Field.value = "Daze";

            Assert.That(label.ClassListContains(EditableLabel.USS_CLASS_INVALID), Is.False, "Editing didn't clear the error.");

            KeyDown(FocusedInField, KeyCode.Return);

            Assert.That(label.value, Is.EqualTo("Daze"));
        }

        [UnityTest]
        public IEnumerator IsValidChar_BlocksTypedCharacters()
        {
            yield return CreateLabel(string.Empty);
            label.IsValidChar = char.IsLetter;
            yield return BeginEdit();

            Type(TextInput, 'a');
            Type(TextInput, '.');
            Type(TextInput, 'b');

            Assert.That(label.Field.value, Is.EqualTo("ab"));
        }

        [UnityTest]
        public IEnumerator IsValidChar_FiltersPastedText()
        {
            yield return CreateLabel();
            label.IsValidChar = char.IsLetter;
            yield return BeginEdit();

            label.Field.value = "Status.Debuff";

            Assert.That(label.Field.value, Is.EqualTo("StatusDebuff"));
        }

        [UnityTest]
        public IEnumerator DoubleClick_StartsEditing()
        {
            yield return CreateLabel();

            DoubleClick(label.worldBound.center);

            Assert.That(label.IsEditing, Is.True);
        }

        [UnityTest]
        public IEnumerator F2_DoesNothing()
        {
            yield return CreateLabel();

            Assert.That(label.focusable, Is.False, "The label itself takes no focus, so no key starts an edit.");

            KeyDown(label, KeyCode.F2);

            Assert.That(label.IsEditing, Is.False);
        }

        [UnityTest]
        public IEnumerator PencilClick_StartsEditingAndFocusesField()
        {
            yield return CreateLabel();

            // The pencil is hidden, so unpickable, until the label is hovered.
            Hover(label.PencilIcon.worldBound.center);
            yield return WaitFrames();
            Click(label.PencilIcon.worldBound.center);

            Assert.That(label.IsEditing, Is.True);

            yield return WaitUntil(() => IsWithin(FocusedElement, label.Field));

            Assert.That(IsWithin(FocusedElement, label.Field), Is.True, $"Focused: {FocusedElement}");
        }

        [UnityTest]
        public IEnumerator Hover_HighlightsOnlyThePencil()
        {
            yield return CreateLabel();
            var icon = label.PencilIcon;

            // Hovering the label shows the pencil without highlighting anything.
            Hover(new Vector2(label.LabelElement.worldBound.center.x, label.LabelElement.worldBound.center.y));
            yield return WaitFrames();

            Assert.That(icon.resolvedStyle.visibility, Is.EqualTo(Visibility.Visible), "Hovering the label didn't show the pencil.");
            Assert.That(label.resolvedStyle.backgroundColor.a, Is.Zero);
            Assert.That(icon.resolvedStyle.backgroundColor.a, Is.Zero);

            Hover(icon.worldBound.center);
            yield return WaitFrames();

            Assert.That(icon.resolvedStyle.backgroundColor.a, Is.GreaterThan(0f), "Hovering the pencil didn't highlight it.");
            Assert.That(label.resolvedStyle.backgroundColor.a, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Editing_TextStaysWhereTheLabelTextWas()
        {
            yield return CreateLabel();
            yield return AssertTextAligned();
        }

        [UnityTest]
        public IEnumerator Editing_TitleVariant_TextStaysWhereTheLabelTextWas()
        {
            yield return CreateLabel();
            label.AddToClassList(EditableLabel.titleVariantUssClassName);
            yield return WaitFrames();
            yield return AssertTextAligned();
        }

        private IEnumerator AssertTextAligned()
        {
            float labelTextX = ContentX(label.LabelElement);

            yield return BeginEdit();

            var textElement = TextInput;
            float fieldTextX = ContentX(textElement);

            string chain = "";
            for (var element = textElement; element != null && element != label; element = element.hierarchy.parent)
            {
                var style = element.resolvedStyle;
                chain += $"\n  {element.GetType().Name}[{string.Join(",", element.GetClasses())}] x={element.worldBound.x} " +
                    $"margin={style.marginLeft} border={style.borderLeftWidth} padding={style.paddingLeft}";
            }

            Assert.That(fieldTextX, Is.EqualTo(labelTextX).Within(0.5f),
                $"Label text at {labelTextX}, field text at {fieldTextX}. Label x {label.LabelElement.worldBound.x}, padding {label.LabelElement.resolvedStyle.paddingLeft}, " +
                $"container x {label.worldBound.x}.{chain}");
        }

        // Left edge of the element's content box, where its text starts.
        private static float ContentX(VisualElement _Element)
        {
            var style = _Element.resolvedStyle;
            return _Element.worldBound.x + style.borderLeftWidth + style.paddingLeft;
        }

        [UnityTest]
        public IEnumerator NotEditable_DoubleClickDoesNothing()
        {
            yield return CreateLabel();
            label.Editable = false;
            int presses = 0;
            label.RegisterCallback<PointerDownEvent>(_ => presses++, TrickleDown.TrickleDown);

            DoubleClick(label.worldBound.center);

            Assert.That(presses, Is.EqualTo(2), "The presses didn't reach the label.");
            Assert.That(label.IsEditing, Is.False);
        }

        // In a panel: events sent outside one are dropped.
        [UnityTest]
        public IEnumerator Value_RaisesChangeEventOnlyWhenChanged()
        {
            yield return CreateLabel(string.Empty);

            label.value = "Stun";
            label.value = "Stun";
            label.SetValueWithoutNotify("Daze");

            Assert.That(changes, Is.EqualTo(new[] { "Stun" }));
            Assert.That(label.value, Is.EqualTo("Daze"));
        }

        [Test]
        public void Filter_RemovesRejectedCharacters()
        {
            Assert.That(EditableLabel.Filter("a.b c", char.IsLetter), Is.EqualTo("abc"));
            Assert.That(EditableLabel.Filter("abc", char.IsLetter), Is.EqualTo("abc"));
            Assert.That(EditableLabel.Filter(null, char.IsLetter), Is.EqualTo(string.Empty));
        }
    }
}
