using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine.UIElements;

using LMK.Editor.UIElements;

namespace LMK.Editor.Tests
{
    public class ResizeHandleTests
    {
        private const string USS_CLASS_HORIZONTAL = "resize-handle--horizontal";
        private const string USS_CLASS_VERTICAL = "resize-handle--vertical";

        private static (VisualElement parent, ResizeHandle handle) CreateInParent(TwoPaneSplitViewOrientation _Orientation)
        {
            var parent = new VisualElement();
            var handle = new ResizeHandle(_Orientation) { ResizeParent = true };
            parent.Add(handle);
            return (parent, handle);
        }

        [Test]
        public void Orientation_SetsItsClassOnly()
        {
            var handle = new ResizeHandle(TwoPaneSplitViewOrientation.Vertical);

            Assert.That(handle.ClassListContains(USS_CLASS_VERTICAL), Is.True);
            Assert.That(handle.ClassListContains(USS_CLASS_HORIZONTAL), Is.False);

            handle.Orientation = TwoPaneSplitViewOrientation.Horizontal;

            Assert.That(handle.ClassListContains(USS_CLASS_VERTICAL), Is.False);
            Assert.That(handle.ClassListContains(USS_CLASS_HORIZONTAL), Is.True);
        }

        [Test]
        public void Dragged_ReportsOffsetFromDragStart()
        {
            var handle = new ResizeHandle();
            List<float> offsets = new();
            handle.Dragged += offsets.Add;

            handle.BeginDrag(100f, 0f);
            handle.UpdateDrag(130f);
            handle.UpdateDrag(80f);

            Assert.That(offsets, Is.EqualTo(new[] { 30f, -20f }));
        }

        [Test]
        public void Drag_RaisesStartedAndFinishedOnce()
        {
            var handle = new ResizeHandle();
            int started = 0;
            int finished = 0;
            handle.DragStarted += () => started++;
            handle.DragFinished += () => finished++;

            handle.BeginDrag(0f, 0f);
            handle.EndDrag();
            handle.EndDrag(); // pointer capture loss arrives after pointer up

            Assert.That(started, Is.EqualTo(1));
            Assert.That(finished, Is.EqualTo(1));
        }

        [Test]
        public void ResizeParent_Horizontal_SetsParentWidthOnly()
        {
            var (parent, handle) = CreateInParent(TwoPaneSplitViewOrientation.Horizontal);

            handle.BeginDrag(100f, 200f);
            handle.UpdateDrag(150f);

            Assert.That(parent.style.width.value.value, Is.EqualTo(250f));
            Assert.That(parent.style.height.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void ResizeParent_Vertical_SetsParentHeightOnly()
        {
            var (parent, handle) = CreateInParent(TwoPaneSplitViewOrientation.Vertical);

            handle.BeginDrag(100f, 200f);
            handle.UpdateDrag(150f);

            Assert.That(parent.style.height.value.value, Is.EqualTo(250f));
            Assert.That(parent.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void ResizeParent_Off_LeavesParentAndStillRaisesDragged()
        {
            var (parent, handle) = CreateInParent(TwoPaneSplitViewOrientation.Horizontal);
            handle.ResizeParent = false;
            int dragged = 0;
            handle.Dragged += _ => dragged++;

            handle.BeginDrag(100f, 200f);
            handle.UpdateDrag(150f);

            Assert.That(parent.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
            Assert.That(dragged, Is.EqualTo(1));
        }

        [TestCase(0f, 100f, 50f)] // below min
        [TestCase(1000f, 300f, 300f)] // above max
        [TestCase(1000f, 0f, 1000f)] // no max
        public void ResizeParent_ClampsToMinAndMax(float _Position, float _MaxSize, float _Expected)
        {
            var (parent, handle) = CreateInParent(TwoPaneSplitViewOrientation.Horizontal);
            handle.MinSize = 50f;
            handle.MaxSize = _MaxSize;

            handle.BeginDrag(0f, 0f);
            handle.UpdateDrag(_Position);

            Assert.That(parent.style.width.value.value, Is.EqualTo(_Expected));
        }

        [Test]
        public void ResizeParent_FollowsPointerAgainAfterOvershootingClamp()
        {
            var (parent, handle) = CreateInParent(TwoPaneSplitViewOrientation.Horizontal);
            handle.MinSize = 50f;

            handle.BeginDrag(100f, 100f);
            handle.UpdateDrag(-500f); // far past the minimum
            handle.UpdateDrag(90f); // back near the start

            Assert.That(parent.style.width.value.value, Is.EqualTo(90f));
        }
    }
}
