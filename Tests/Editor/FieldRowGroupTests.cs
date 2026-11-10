using System.Collections;

using NUnit.Framework;

using UnityEngine.TestTools;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;

namespace LMK.Editor.Tests
{
    public class FieldRowGroupTests : WindowTestFixture
    {
        private const string LONG_LABEL = "A much longer label";

        private FieldRowGroup group;

        private IEnumerator CreateGroup(params FieldRow[] _Rows)
        {
            group = new FieldRowGroup();
            group.style.width = 400f;
            foreach (var row in _Rows)
                group.Add(row);

            Root.Add(group);
            yield return WaitForLayout(group);
            yield return WaitFrames();
        }

        [UnityTest]
        public IEnumerator DifferentLabels_GetTheSameWidth()
        {
            var shortRow = new FieldRow("A", new TextField());
            var longRow = new FieldRow(LONG_LABEL, new TextField());
            var midRow = new FieldRow("Middle", new Label("Value"));
            yield return CreateGroup(shortRow, longRow, midRow);

            float width = longRow.LabelElement.layout.width;

            Assert.That(width, Is.GreaterThan(50f));
            Assert.That(shortRow.LabelElement.layout.width, Is.EqualTo(width).Within(0.5f));
            Assert.That(midRow.LabelElement.layout.width, Is.EqualTo(width).Within(0.5f));
            Assert.That(shortRow.contentContainer.worldBound.xMin, Is.EqualTo(longRow.contentContainer.worldBound.xMin).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator LabelWidth_Overrides()
        {
            var shortRow = new FieldRow("A");
            var longRow = new FieldRow(LONG_LABEL);
            yield return CreateGroup(shortRow, longRow);

            group.LabelWidth = 200f;
            yield return WaitFrames();

            Assert.That(shortRow.LabelElement.layout.width, Is.EqualTo(200f).Within(0.5f));
            Assert.That(longRow.LabelElement.layout.width, Is.EqualTo(200f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator RowAddedLater_Realigns()
        {
            var shortRow = new FieldRow("A");
            yield return CreateGroup(shortRow);
            float initialWidth = shortRow.LabelElement.layout.width;

            var longRow = new FieldRow(LONG_LABEL);
            group.Add(longRow);
            yield return WaitFrames(5);

            Assert.That(longRow.LabelElement.layout.width, Is.GreaterThan(initialWidth));
            Assert.That(shortRow.LabelElement.layout.width, Is.EqualTo(longRow.LabelElement.layout.width).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator LabelShortened_Realigns()
        {
            var shortRow = new FieldRow("A");
            var longRow = new FieldRow(LONG_LABEL);
            yield return CreateGroup(shortRow, longRow);
            float longWidth = shortRow.LabelElement.layout.width;

            longRow.Label = "AB";
            yield return WaitFrames(5);

            Assert.That(shortRow.LabelElement.layout.width, Is.LessThan(longWidth));
            Assert.That(shortRow.LabelElement.layout.width, Is.EqualTo(longRow.LabelElement.layout.width).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator RowSpacing_SeparatesRowsButNotTheFirst()
        {
            var first = new FieldRow("A");
            var second = new FieldRow("B");
            yield return CreateGroup(first, second);

            group.RowSpacing = 6f;
            yield return WaitFrames();

            Assert.That(first.resolvedStyle.marginTop, Is.EqualTo(0f));
            Assert.That(second.resolvedStyle.marginTop, Is.EqualTo(6f));
        }

        [UnityTest]
        public IEnumerator RowMovedOutOfGroup_KeepsItsOwnWidth()
        {
            var shortRow = new FieldRow("A");
            var longRow = new FieldRow(LONG_LABEL);
            yield return CreateGroup(shortRow, longRow);
            float alignedWidth = shortRow.LabelElement.layout.width;

            Root.Add(shortRow);
            yield return WaitFrames();

            Assert.That(shortRow.LabelElement.layout.width, Is.LessThan(alignedWidth));
        }

        [Test]
        public void Constructor_AddsValueToValueArea()
        {
            var value = new Label("Value");
            var row = new FieldRow("Label", value);

            Assert.That(row.Label, Is.EqualTo("Label"));
            Assert.That(value.hierarchy.parent, Is.SameAs(row.contentContainer));
        }

        [Test]
        public void AlignTop_SetsClass()
        {
            var row = new FieldRow("Description") { AlignTop = true };

            Assert.That(row.ClassListContains(FieldRow.USS_CLASS_ALIGN_TOP), Is.True);
        }
    }
}
