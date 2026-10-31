using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using UnityEngine.UIElements;

using LMK.Editor.UIElements;

namespace LMK.Editor.Tests
{
    public class DragReorderManipulatorTests
    {
        private static readonly float[] CENTERS = { 10f, 30f, 50f };

        [TestCase(0f, 0)] // before the first slot
        [TestCase(10f, 1)] // on a center counts as past it
        [TestCase(20f, 1)]
        [TestCase(40f, 2)]
        [TestCase(60f, 3)] // after the last slot
        public void GetDropSlot_ReturnsGap(float _Position, int _Expected)
        {
            Assert.That(DragReorderManipulator.GetDropSlot(CENTERS, _Position), Is.EqualTo(_Expected));
        }

        [TestCase(60f, 0)] // slots run right to left, so far right is before the first slot
        [TestCase(40f, 1)]
        [TestCase(20f, 2)]
        [TestCase(0f, 3)]
        public void GetDropSlot_Reversed_ReturnsGap(float _Position, int _Expected)
        {
            float[] reversedCenters = { 50f, 30f, 10f };

            Assert.That(DragReorderManipulator.GetDropSlot(reversedCenters, _Position, -1f), Is.EqualTo(_Expected));
        }

        [TestCase(1, 1)]
        [TestCase(1, 2)]
        public void GetTargetIndex_GapsBesideDraggedSlot_KeepOrder(int _DraggedSlot, int _DropSlot)
        {
            int[] indices = { 0, 1, 2, 3 };

            Assert.That(DragReorderManipulator.GetTargetIndex(indices, _DraggedSlot, _DropSlot), Is.EqualTo(-1));
        }

        // Applied the way the Reordered documentation says to: remove at from, then insert at to.
        [TestCase(0, 4, "BCDA")] // first to the end
        [TestCase(3, 0, "DABC")] // last to the start
        [TestCase(0, 2, "BACD")] // forward by one
        [TestCase(2, 1, "ACBD")] // back by one
        public void GetTargetIndex_MovesChildIntoGap(int _DraggedSlot, int _DropSlot, string _Expected)
        {
            List<char> order = "ABCD".ToList();
            int[] indices = { 0, 1, 2, 3 };

            int to = DragReorderManipulator.GetTargetIndex(indices, _DraggedSlot, _DropSlot);
            char moved = order[_DraggedSlot];
            order.RemoveAt(_DraggedSlot);
            order.Insert(to, moved);

            Assert.That(new string(order.ToArray()), Is.EqualTo(_Expected));
        }

        [Test]
        public void GetTargetIndex_SkippedChildrenKeepTheirPlace()
        {
            // Child 1 ('B') can't be reordered, so the slots are A, C, D.
            List<char> order = "ABCD".ToList();
            int[] indices = { 0, 2, 3 };

            int to = DragReorderManipulator.GetTargetIndex(indices, 2, 1); // D into the gap before C
            char moved = order[3];
            order.RemoveAt(3);
            order.Insert(to, moved);

            Assert.That(new string(order.ToArray()), Is.EqualTo("ABDC"));
        }

        [Test]
        public void CollectSlots_SkipsHiddenChildren()
        {
            var container = new VisualElement();
            var shown = new VisualElement();
            var hidden = new VisualElement();
            hidden.style.display = DisplayStyle.None;
            container.Add(shown);
            container.Add(hidden);

            List<VisualElement> slots = new();
            DragReorderManipulator.CollectSlots(container, null, slots);

            Assert.That(slots, Is.EqualTo(new[] { shown }));
        }

        [Test]
        public void CollectSlots_SkipsRejectedChildren()
        {
            var container = new VisualElement();
            var pinned = new VisualElement();
            var movable = new VisualElement();
            container.Add(pinned);
            container.Add(movable);

            List<VisualElement> slots = new();
            DragReorderManipulator.CollectSlots(container, _Child => _Child != pinned, slots);

            Assert.That(slots, Is.EqualTo(new[] { movable }));
        }

        [Test]
        public void CollectSlots_ClearsPreviousSlots()
        {
            var container = new VisualElement();
            List<VisualElement> slots = new() { new VisualElement() };

            DragReorderManipulator.CollectSlots(container, null, slots);

            Assert.That(slots, Is.Empty);
        }
    }
}
