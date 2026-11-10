using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;

namespace LMK.Editor.Tests
{
    public class PanelLayoutTests : WindowTestFixture
    {
        private const float LAYOUT_WIDTH = 400f;

        private const float PANEL_WIDTH = 100f;

        private const string PERSISTENCE_KEY = "LMK.Editor.Tests.PanelLayout";

        private PanelLayout layout;

        private CollapsiblePanel left;

        private VisualElement fill;

        private CollapsiblePanel right;

        [TearDown]
        public void ClearSavedLayout()
        {
            EditorUserSettings.SetConfigValue(PERSISTENCE_KEY, null);
        }

        /// <summary>
        /// [left | fill | right], each side panel 100 wide, limited to 50-150 (left) and 50-unlimited (right).
        /// </summary>
        private void CreateLayout(bool _WithLeft = true)
        {
            layout = new PanelLayout();
            layout.style.position = Position.Absolute;
            layout.style.left = 0f;
            layout.style.top = 0f;
            layout.style.width = LAYOUT_WIDTH;
            layout.style.height = 200f;

            if (_WithLeft)
            {
                left = new CollapsiblePanel("Left") { name = "left", MinSize = 50f, MaxSize = 150f };
                left.style.width = PANEL_WIDTH;
                layout.Add(left);
            }

            fill = new VisualElement { name = "fill" };
            layout.Add(fill);
            PanelLayout.SetFill(fill, true);

            right = new CollapsiblePanel("Right") { name = "right", MinSize = 50f };
            right.style.width = PANEL_WIDTH;
            right.Add(new Label("Right content") { name = "right-content" });
            layout.Add(right);
        }

        private IEnumerator CreateLayoutInWindow(bool _WithLeft = true)
        {
            CreateLayout(_WithLeft);
            Root.Add(layout);
            yield return WaitForLayout(right.Header);
            yield return WaitFrames();
        }

        private ResizeHandle[] VisibleHandles => layout.Query<ResizeHandle>().ToList()
            .Where(_Handle => _Handle.resolvedStyle.display == DisplayStyle.Flex)
            .OrderBy(_Handle => _Handle.worldBound.x)
            .ToArray();

        private ResizeHandle HandleAt(float _X)
        {
            return VisibleHandles.OrderBy(_Handle => Mathf.Abs(_Handle.worldBound.center.x - _X)).First();
        }

        private Vector2 HeaderPoint(CollapsiblePanel _Panel)
        {
            return new Vector2(_Panel.Header.worldBound.xMin + 10f, _Panel.Header.worldBound.center.y);
        }

        #region Resize
        [UnityTest]
        public IEnumerator Handles_OnePerGap_OnTheEdgeFacingTheFill()
        {
            yield return CreateLayoutInWindow();

            var handles = VisibleHandles;

            Assert.That(handles.Length, Is.EqualTo(2));
            Assert.That(handles[0].worldBound.center.x, Is.EqualTo(left.worldBound.xMax).Within(1f));
            Assert.That(handles[1].worldBound.center.x, Is.EqualTo(right.worldBound.xMin).Within(1f));
        }

        [UnityTest]
        public IEnumerator HandleDrag_PanelBeforeFill_GrowsRightwards()
        {
            yield return CreateLayoutInWindow();

            var start = HandleAt(left.worldBound.xMax).worldBound.center;
            DragFromTo(start, start + new Vector2(30f, 0f));
            yield return WaitFrames();

            Assert.That(left.layout.width, Is.EqualTo(130f).Within(0.5f));
            Assert.That(right.layout.width, Is.EqualTo(PANEL_WIDTH).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator HandleDrag_PanelAfterFill_GrowsLeftwards()
        {
            yield return CreateLayoutInWindow();

            var start = HandleAt(right.worldBound.xMin).worldBound.center;
            DragFromTo(start, start - new Vector2(30f, 0f));
            yield return WaitFrames();

            Assert.That(right.layout.width, Is.EqualTo(130f).Within(0.5f));
            Assert.That(left.layout.width, Is.EqualTo(PANEL_WIDTH).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator HandleDrag_ClampsToMinAndMax()
        {
            yield return CreateLayoutInWindow();

            var start = HandleAt(left.worldBound.xMax).worldBound.center;
            DragFromTo(start, start + new Vector2(150f, 0f));
            yield return WaitFrames();

            Assert.That(left.layout.width, Is.EqualTo(150f).Within(0.5f));

            start = HandleAt(left.worldBound.xMax).worldBound.center;
            DragFromTo(start, start - new Vector2(150f, 0f));
            yield return WaitFrames();

            Assert.That(left.layout.width, Is.EqualTo(50f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator HandleDrag_StopsWhereTheFillHasNoSpaceLeft()
        {
            yield return CreateLayoutInWindow();
            float fillWidth = fill.layout.width;

            var start = HandleAt(right.worldBound.xMin).worldBound.center;
            DragFromTo(start, start - new Vector2(LAYOUT_WIDTH, 0f));
            yield return WaitFrames();

            Assert.That(right.layout.width, Is.EqualTo(PANEL_WIDTH + fillWidth).Within(0.5f));
            Assert.That(fill.layout.width, Is.EqualTo(0f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator Vertical_HandleDragResizesHeight_AndCollapseShrinksToHeader()
        {
            CreateLayout(_WithLeft: false);
            layout.Orientation = TwoPaneSplitViewOrientation.Vertical;
            layout.style.height = 300f;
            right.style.width = StyleKeyword.Null;
            right.style.height = PANEL_WIDTH;
            Root.Add(layout);
            yield return WaitForLayout(right.Header);
            yield return WaitFrames();

            Assert.That(right.CollapseAxis, Is.EqualTo(TwoPaneSplitViewOrientation.Vertical));
            Assert.That(right.layout.height, Is.EqualTo(PANEL_WIDTH).Within(0.5f));

            var handle = VisibleHandles.Single();
            Assert.That(handle.worldBound.center.y, Is.EqualTo(right.worldBound.yMin).Within(1f));

            var start = handle.worldBound.center;
            DragFromTo(start, start - new Vector2(0f, 40f));
            yield return WaitFrames();

            Assert.That(right.layout.height, Is.EqualTo(140f).Within(0.5f));

            right.Collapsed = true;
            yield return WaitFrames();

            Assert.That(right.layout.height, Is.EqualTo(right.Header.layout.height).Within(2f));
        }

        [UnityTest]
        public IEnumerator HandleDrag_RaisesLayoutChangedOnce()
        {
            yield return CreateLayoutInWindow();
            int changes = 0;
            layout.LayoutChanged += () => changes++;

            var start = HandleAt(left.worldBound.xMax).worldBound.center;
            DragFromTo(start, start + new Vector2(30f, 0f));

            Assert.That(changes, Is.EqualTo(1));
        }
        #endregion

        #region Reorder
        [UnityTest]
        public IEnumerator HeaderDrag_ToOtherSide_MovesPanelAndKeepsFill()
        {
            yield return CreateLayoutInWindow(_WithLeft: false);

            DragFromTo(HeaderPoint(right), new Vector2(fill.worldBound.xMin + 1f, fill.worldBound.center.y));
            yield return WaitFrames();

            Assert.That(layout.Children().ToArray(), Is.EqualTo(new VisualElement[] { right, fill }));
            Assert.That(fill.ClassListContains(PanelLayout.fillUssClassName), Is.True);
            Assert.That(right.CollapsesTowardsStart, Is.True);

            // The handle is now on the right panel's right edge, and dragging right grows it.
            var start = HandleAt(right.worldBound.xMax).worldBound.center;
            Assert.That(start.x, Is.EqualTo(right.worldBound.xMax).Within(1f));

            DragFromTo(start, start + new Vector2(30f, 0f));
            yield return WaitFrames();

            Assert.That(right.layout.width, Is.EqualTo(130f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator FillDrag_MovesNothing()
        {
            yield return CreateLayoutInWindow();
            int presses = 0;
            fill.RegisterCallback<PointerDownEvent>(_ => presses++);

            DragFromTo(fill.worldBound.center, new Vector2(right.worldBound.xMax - 1f, fill.worldBound.center.y));

            Assert.That(presses, Is.EqualTo(1), "The press didn't reach the fill child.");
            Assert.That(layout.Children().ToArray(), Is.EqualTo(new VisualElement[] { left, fill, right }));
        }

        [UnityTest]
        public IEnumerator ContentDrag_MovesNothing()
        {
            yield return CreateLayoutInWindow();
            var content = right.Q<Label>("right-content");
            int presses = 0;
            content.RegisterCallback<PointerDownEvent>(_ => presses++);

            DragFromTo(content.worldBound.center, new Vector2(left.worldBound.xMin + 1f, content.worldBound.center.y));

            Assert.That(presses, Is.EqualTo(1), "The press didn't reach the panel content.");
            Assert.That(layout.Children().ToArray(), Is.EqualTo(new VisualElement[] { left, fill, right }));
        }

        [UnityTest]
        public IEnumerator NotReorderable_HeaderDrag_MovesNothing()
        {
            yield return CreateLayoutInWindow();
            layout.Reorderable = false;
            int presses = 0;
            right.Header.RegisterCallback<PointerDownEvent>(_ => presses++);

            DragFromTo(HeaderPoint(right), new Vector2(left.worldBound.xMin + 1f, left.worldBound.center.y));

            Assert.That(presses, Is.EqualTo(1), "The press didn't reach the header.");
            Assert.That(layout.Children().ToArray(), Is.EqualTo(new VisualElement[] { left, fill, right }));
        }
        #endregion

        #region Collapse
        [UnityTest]
        public IEnumerator Collapse_GivesWidthToFill_ExpandRestoresIt()
        {
            yield return CreateLayoutInWindow();
            float fillWidth = fill.layout.width;

            var start = HandleAt(right.worldBound.xMin).worldBound.center;
            DragFromTo(start, start - new Vector2(30f, 0f));
            yield return WaitFrames();

            right.Collapsed = true;
            yield return WaitFrames();

            float stripWidth = right.layout.width;
            Assert.That(stripWidth, Is.LessThan(30f));
            Assert.That(fill.layout.width, Is.EqualTo(fillWidth + PANEL_WIDTH - stripWidth).Within(0.5f));
            Assert.That(VisibleHandles.Length, Is.EqualTo(1), "A collapsed panel has no handle.");

            right.Collapsed = false;
            yield return WaitFrames();

            Assert.That(right.layout.width, Is.EqualTo(130f).Within(0.5f));
            Assert.That(VisibleHandles.Length, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Collapse_SetsSidewaysAxis()
        {
            yield return CreateLayoutInWindow();

            Assert.That(right.CollapseAxis, Is.EqualTo(TwoPaneSplitViewOrientation.Horizontal));
            Assert.That(left.CollapsesTowardsStart, Is.True);
            Assert.That(right.CollapsesTowardsStart, Is.False);
        }

        [UnityTest]
        public IEnumerator Collapse_RaisesLayoutChanged()
        {
            yield return CreateLayoutInWindow();
            int changes = 0;
            layout.LayoutChanged += () => changes++;

            right.Collapsed = true;

            Assert.That(changes, Is.EqualTo(1));
        }
        #endregion

        #region Persistence
        [Test]
        public void ApplyStateJson_RestoresOrderSizesAndCollapse()
        {
            CreateLayout();
            var extra = new CollapsiblePanel("Extra") { name = "extra" };
            layout.Add(extra);

            // "left" is missing and "unknown" doesn't exist: left and extra keep their index.
            layout.ApplyStateJson(
                "{\"panels\":[" +
                "{\"name\":\"unknown\",\"size\":10,\"collapsed\":true}," +
                "{\"name\":\"right\",\"size\":120,\"collapsed\":true}," +
                "{\"name\":\"fill\",\"size\":-1,\"collapsed\":false}]}");

            Assert.That(layout.Children().ToArray(), Is.EqualTo(new VisualElement[] { left, right, fill, extra }));
            Assert.That(right.Collapsed, Is.True);
            Assert.That(left.Collapsed, Is.False);

            right.Collapsed = false;

            Assert.That(right.style.width.value.value, Is.EqualTo(120f));
        }

        [Test]
        public void StateJson_RoundTrips()
        {
            CreateLayout();
            layout.ApplyStateJson("{\"panels\":[{\"name\":\"right\",\"size\":120,\"collapsed\":true},{\"name\":\"fill\",\"size\":-1,\"collapsed\":false},{\"name\":\"left\",\"size\":60,\"collapsed\":false}]}");
            string json = layout.GetStateJson();

            CreateLayout();
            layout.ApplyStateJson(json);

            Assert.That(layout.GetStateJson(), Is.EqualTo(json));
            Assert.That(layout.Children().ToArray(), Is.EqualTo(new VisualElement[] { right, fill, left }));
            Assert.That(left.style.width.value.value, Is.EqualTo(60f));
        }

        [Test]
        public void ApplyStateJson_ClampsSizes()
        {
            CreateLayout();

            layout.ApplyStateJson("{\"panels\":[{\"name\":\"left\",\"size\":500,\"collapsed\":false}]}");

            Assert.That(left.style.width.value.value, Is.EqualTo(150f));
        }

        [Test]
        public void ApplyStateJson_IgnoresInvalidJson()
        {
            CreateLayout();

            Assert.DoesNotThrow(() => layout.ApplyStateJson("not json"));
            Assert.That(layout.Children().ToArray(), Is.EqualTo(new VisualElement[] { left, fill, right }));
        }

        [UnityTest]
        public IEnumerator PersistenceKey_SavesOnChange_AndRestoresInANewLayout()
        {
            yield return CreateLayoutInWindow();
            layout.PersistenceKey = PERSISTENCE_KEY;

            right.Collapsed = true;
            DragFromTo(HeaderPoint(left), new Vector2(right.worldBound.xMax - 1f, right.worldBound.center.y));
            yield return WaitFrames();

            Assert.That(layout.Children().ToArray(), Is.EqualTo(new VisualElement[] { fill, right, left }), "The drag didn't reorder.");

            layout.RemoveFromHierarchy();
            CreateLayout();
            layout.PersistenceKey = PERSISTENCE_KEY;
            Root.Add(layout);
            yield return WaitFrames();

            Assert.That(layout.Children().ToArray(), Is.EqualTo(new VisualElement[] { fill, right, left }));
            Assert.That(right.Collapsed, Is.True);
        }

        [Test]
        public void PersistenceKey_ChildWithoutName_LogsWarning()
        {
            CreateLayout();
            layout.Add(new VisualElement());

            LogAssert.Expect(LogType.Warning, new Regex("no unique name"));
            layout.PersistenceKey = PERSISTENCE_KEY;
            layout.Refresh();
        }
        #endregion

        [Test]
        public void ResetLayout_RestoresDefaultOrderSizesAndCollapse()
        {
            CreateLayout();
            layout.ApplyStateJson("{\"panels\":[{\"name\":\"right\",\"size\":120,\"collapsed\":true},{\"name\":\"fill\",\"size\":-1,\"collapsed\":false},{\"name\":\"left\",\"size\":60,\"collapsed\":false}]}");

            layout.ResetLayout();

            Assert.That(layout.Children().ToArray(), Is.EqualTo(new VisualElement[] { left, fill, right }));
            Assert.That(right.Collapsed, Is.False);
            Assert.That(left.style.width.value.value, Is.EqualTo(PANEL_WIDTH));
            Assert.That(right.style.width.value.value, Is.EqualTo(PANEL_WIDTH));
        }

        [Test]
        public void CanStartDrag_OnlyFromHeadersOfMovablePanels()
        {
            CreateLayout();
            layout.Refresh();

            Assert.That(layout.CanStartDrag(right.Header), Is.True);
            Assert.That(layout.CanStartDrag(right.Header.Q<Label>()), Is.True);
            Assert.That(layout.CanStartDrag(right.CollapseButton), Is.False);
            Assert.That(layout.CanStartDrag(right.Q<Label>("right-content")), Is.False);
            Assert.That(layout.CanStartDrag(fill), Is.False);

            var handle = new VisualElement();
            handle.AddToClassList(PanelLayout.dragHandleUssClassName);
            fill.Add(handle);

            Assert.That(layout.CanStartDrag(handle), Is.False, "The fill child can't be dragged, even by a drag handle.");
        }
    }
}
