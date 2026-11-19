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
    public class PropertyListTests : WindowTestFixture
    {
        private const string PERSISTENCE_KEY = "LMK.Editor.Tests.PropertyList";

        private readonly List<Object> hosts = new();

        [TearDown]
        public void DestroyHosts()
        {
            foreach (var host in hosts)
            {
                if (host)
                    Object.DestroyImmediate(host);
            }
            hosts.Clear();

            EditorUserSettings.SetConfigValue(PERSISTENCE_KEY, null);
        }

        private PropertyListTestHost CreateHost(params int[] _Numbers)
        {
            var host = ScriptableObject.CreateInstance<PropertyListTestHost>();
            host.numbers.AddRange(_Numbers);
            hosts.Add(host);
            return host;
        }

        private static PropertyList CreateList(PropertyListTestHost _Host, out SerializedObject _SerializedObject)
        {
            _SerializedObject = new SerializedObject(_Host);
            return new PropertyList(_SerializedObject.FindProperty(nameof(PropertyListTestHost.numbers)));
        }

        // Rows show plain labels, so presses on the content don't start a field's own drag.
        private static void UseLabels(PropertyList _List)
        {
            _List.makeItem = () => new Label();
            _List.bindItem = (_Item, _Element) => ((Label)_Item).text = $"Item {_Element.intValue}";
            _List.Refresh();
        }

        private IEnumerator ShowInWindow(PropertyList _List)
        {
            _List.style.width = 300f;
            Root.Add(_List);
            yield return WaitForLayout(_List.Panel.Header);
            if (_List.Rows.childCount > 0)
                yield return WaitForLayout(_List.Rows[0]);
        }

        private static VisualElement Handle(PropertyList _List, int _Row)
            => _List.Rows[_Row].Q(className: PropertyList.USS_CLASS_HANDLE);

        private static VisualElement RemoveButton(PropertyList _List, int _Row)
            => _List.Rows[_Row].Q(className: PropertyList.USS_CLASS_REMOVE_BUTTON);

        private static bool IsShown(VisualElement _Element) => _Element.style.display != DisplayStyle.None;

        // The list polls its targets on a timer, and batch mode can run 120 frames in less than one interval.
        private IEnumerator WaitForPoll(System.Func<bool> _Condition, double _Seconds = 2.0)
        {
            double end = EditorApplication.timeSinceStartup + _Seconds;
            while (!_Condition() && EditorApplication.timeSinceStartup < end)
            {
                window.Repaint();
                yield return null;
            }
        }

        #region Binding and display
        [Test]
        public void Bind_ShowsTitleCountAndRows()
        {
            var list = CreateList(CreateHost(1, 2, 3), out _);

            Assert.That(list.Title, Is.EqualTo("Numbers"));
            Assert.That(list.CountLabel.text, Is.EqualTo("3 items"));
            Assert.That(list.Rows.childCount, Is.EqualTo(3));
            Assert.That(list.Rows[0].Q<PropertyField>(), Is.Not.Null, "default content is a PropertyField");
            Assert.That(IsShown(list.EmptyLabel), Is.False);
            Assert.That(IsShown(list.MessageLabel), Is.False);
        }

        [Test]
        public void Title_OverridesDisplayName_AndSurvivesRebind()
        {
            var host = CreateHost(1);
            var serializedObject = new SerializedObject(host);
            var list = new PropertyList(serializedObject.FindProperty("numbers"), "Values");

            list.BindProperty(serializedObject.FindProperty("entries"));

            Assert.That(list.Title, Is.EqualTo("Values"));
        }

        [Test]
        public void Empty_ShowsEmptyText()
        {
            var list = CreateList(CreateHost(), out _);
            list.EmptyText = "No numbers";

            Assert.That(list.CountLabel.text, Is.EqualTo("0 items"));
            Assert.That(list.Rows.childCount, Is.Zero);
            Assert.That(IsShown(list.EmptyLabel), Is.True);
            Assert.That(list.EmptyLabel.text, Is.EqualTo("No numbers"));
        }

        [Test]
        public void Rows_Alternate()
        {
            var list = CreateList(CreateHost(1, 2, 3), out _);

            Assert.That(list.Rows[0].ClassListContains(PropertyList.USS_CLASS_ROW_ALTERNATE), Is.False);
            Assert.That(list.Rows[1].ClassListContains(PropertyList.USS_CLASS_ROW_ALTERNATE), Is.True);
            Assert.That(list.Rows[2].ClassListContains(PropertyList.USS_CLASS_ROW_ALTERNATE), Is.False);
        }

        [Test]
        public void Rows_HaveAHeaderBand_ThatIgnoresThePointer()
        {
            var list = CreateList(CreateHost(1, 2), out _);

            foreach (var row in list.Rows.Children())
            {
                var band = row.hierarchy[0];
                Assert.That(band.ClassListContains(PropertyList.USS_CLASS_ROW_HEADER), Is.True);
                Assert.That(band.pickingMode, Is.EqualTo(PickingMode.Ignore));
            }
        }

        // The header colour and the separators belong to the first line, not to an expanded element's content.
        // Rows are divided by a line in the outline colour, which the separators share.
        [UnityTest]
        public IEnumerator ExpandedRow_HeaderBandAndSeparators_CoverTheFirstLineOnly()
        {
            var host = CreateHost();
            host.entries = new[] { new PropertyListTestHost.Entry { name = "A" }, new PropertyListTestHost.Entry { name = "B" } };
            var serializedObject = new SerializedObject(host);
            var entries = serializedObject.FindProperty(nameof(PropertyListTestHost.entries));
            entries.GetArrayElementAtIndex(1).isExpanded = true;

            var list = new PropertyList(entries);
            yield return ShowInWindow(list);
            var row = list.Rows[1];
            yield return WaitUntil(() => row.worldBound.height > 30f);

            var band = row.Q(className: PropertyList.USS_CLASS_ROW_HEADER);
            var handle = Handle(list, 1);
            var remove = RemoveButton(list, 1);

            Assert.That(row.worldBound.height, Is.GreaterThan(30f), "the element is expanded");
            Assert.That(list.Rows[0].resolvedStyle.borderTopWidth, Is.Zero, "no line above the first row");
            Assert.That(row.resolvedStyle.borderTopWidth, Is.EqualTo(1f), "a line between rows");
            Assert.That(band.worldBound.yMin, Is.EqualTo(row.worldBound.yMin + 1f).Within(0.5f), "the band starts below the line");
            Assert.That(band.worldBound.height, Is.EqualTo(20f).Within(0.5f));
            Assert.That(band.resolvedStyle.backgroundColor.a, Is.GreaterThan(0f), "every row's header is tinted");
            Assert.That(list.Rows[0].Q(className: PropertyList.USS_CLASS_ROW_HEADER).resolvedStyle.backgroundColor,
                Is.EqualTo(band.resolvedStyle.backgroundColor), "no alternating colour");
            Assert.That(handle.resolvedStyle.borderRightColor, Is.EqualTo(row.resolvedStyle.borderTopColor), "separators share the outline colour");
            Assert.That(remove.resolvedStyle.borderLeftColor, Is.EqualTo(row.resolvedStyle.borderTopColor));
            Assert.That(band.worldBound.width, Is.EqualTo(row.worldBound.width).Within(0.5f));
            Assert.That(handle.worldBound.height, Is.EqualTo(20f).Within(0.5f));
            Assert.That(remove.worldBound.height, Is.EqualTo(20f).Within(0.5f));
            Assert.That(remove.worldBound.xMax, Is.EqualTo(row.worldBound.xMax).Within(0.5f), "the remove button fills the end of the line");
            Assert.That(handle.resolvedStyle.borderRightWidth, Is.EqualTo(1f));
            Assert.That(remove.resolvedStyle.borderLeftWidth, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator ShortContent_IsCentredOnTheFirstLine()
        {
            var list = CreateList(CreateHost(1, 2), out _);
            list.makeItem = () => new VisualElement { style = { height = 10f } };
            list.bindItem = (_, _) => { };
            list.Refresh();
            yield return ShowInWindow(list);

            var content = list.Rows[1].Q(className: PropertyList.USS_CLASS_ROW_CONTENT);
            Assert.That(content.worldBound.height, Is.EqualTo(10f).Within(0.5f));
            Assert.That(content.worldBound.center.y, Is.EqualTo(Handle(list, 1).worldBound.center.y).Within(0.5f));
        }

        // The header's buttons fit inside it, and the rows' handle and remove button sit in the same columns as the
        // header's collapse and add buttons, so their separators line up.
        [UnityTest]
        public IEnumerator HeaderButtonsFitTheHeader_RowButtonsLineUpWithThem()
        {
            var list = CreateList(CreateHost(1, 2), out _);
            yield return ShowInWindow(list);
            var header = list.Panel.Header;
            var collapse = list.Panel.CollapseButton;
            var add = list.AddButton;
            float headerBottom = header.worldBound.yMax - header.resolvedStyle.borderBottomWidth;

            foreach (var button in new[] { collapse, add })
            {
                Assert.That(button.worldBound.yMin, Is.EqualTo(header.worldBound.yMin).Within(0.5f), "expanded: a header button's top");
                Assert.That(button.worldBound.yMax, Is.EqualTo(headerBottom).Within(0.5f), "expanded: a header button fills the header, inside its bottom line");
            }

            // Collapsed, the header has no bottom line: the buttons grow by its pixel.
            list.Collapsed = true;
            yield return WaitUntil(() => Mathf.Abs(add.worldBound.yMax - header.worldBound.yMax) < 0.5f);
            Assert.That(header.resolvedStyle.borderBottomWidth, Is.Zero);
            foreach (var button in new[] { collapse, add })
            {
                Assert.That(button.worldBound.yMin, Is.EqualTo(header.worldBound.yMin).Within(0.5f), "collapsed: a header button's top");
                Assert.That(button.worldBound.yMax, Is.EqualTo(header.worldBound.yMax).Within(0.5f), "collapsed: a header button fills the header");
            }
            list.Collapsed = false;
            yield return WaitUntil(() => Handle(list, 0).worldBound.width > 0f && Mathf.Abs(add.worldBound.yMax - headerBottom) < 0.5f);

            for (int row = 0; row < 2; row++)
            {
                var handle = Handle(list, row);
                var remove = RemoveButton(list, row);
                Assert.That(handle.worldBound.xMin, Is.EqualTo(collapse.worldBound.xMin).Within(0.5f), "handle under the arrow");
                Assert.That(handle.worldBound.xMax, Is.EqualTo(collapse.worldBound.xMax).Within(0.5f), "handle separator under the arrow's");
                Assert.That(remove.worldBound.xMin, Is.EqualTo(add.worldBound.xMin).Within(0.5f), "remove under add");
                Assert.That(remove.worldBound.xMax, Is.EqualTo(add.worldBound.xMax).Within(0.5f));
            }
        }

        [Test]
        public void FormatCount_UsesItemOrItems()
        {
            Assert.That(PropertyList.FormatCount(0), Is.EqualTo("0 items"));
            Assert.That(PropertyList.FormatCount(1), Is.EqualTo("1 item"));
            Assert.That(PropertyList.FormatCount(2), Is.EqualTo("2 items"));
            Assert.That(CreateList(CreateHost(7), out _).CountLabel.text, Is.EqualTo("1 item"));
        }

        [Test]
        public void IconAssets_ImportAsVectorImages()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<VectorImage>(PropertyList.ADD_ICON_PATH), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<VectorImage>(PropertyList.REMOVE_ICON_PATH), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator AddAndRemoveButtons_ShowIcons_RemoveIsRed()
        {
            var list = CreateList(CreateHost(1), out _);
            yield return ShowInWindow(list);
            var add = (Button)list.AddButton;
            var remove = (Button)RemoveButton(list, 0);

            Assert.That(add.text, Is.Empty);
            Assert.That(remove.text, Is.Empty);
            Assert.That(add.iconImage.vectorImage, Is.Not.Null);
            Assert.That(remove.iconImage.vectorImage, Is.Not.Null);

            var addIcon = add.Q<Image>(className: Button.imageUSSClassName);
            Assert.That(addIcon, Is.Not.Null);
            Assert.That(addIcon.pickingMode, Is.EqualTo(PickingMode.Ignore), "presses and the disabled tooltip go past the icon");
            Assert.That(addIcon.worldBound.width, Is.EqualTo(7f).Within(0.5f));
            Assert.That(addIcon.worldBound.center.x, Is.EqualTo(add.worldBound.center.x).Within(1f), "the icon is centred");

            var red = remove.resolvedStyle.backgroundColor;
            Assert.That(red.r, Is.GreaterThan(red.g + 0.1f), "the remove button is red");
            Assert.That(red.r, Is.GreaterThan(red.b + 0.1f));

            Assert.That(add.resolvedStyle.borderLeftWidth, Is.EqualTo(1f), "a line between the count and the add button");
            Assert.That(list.Panel.Header.resolvedStyle.height, Is.EqualTo(18f).Within(0.5f));
        }

        [Test]
        public void MakeItemAndBindItem_BuildRowContent()
        {
            var list = CreateList(CreateHost(4, 5), out _);

            UseLabels(list);

            Assert.That(list.Rows[0].Q<PropertyField>(), Is.Null);
            Assert.That(list.Rows.Children().Select(_Row => _Row.Q<Label>(className: PropertyList.USS_CLASS_ROW_CONTENT).text),
                Is.EqualTo(new[] { "Item 4", "Item 5" }));
        }

        [Test]
        public void StructElements_GetARowEach()
        {
            var host = CreateHost();
            host.entries = new[] { new PropertyListTestHost.Entry { name = "a" }, new PropertyListTestHost.Entry { name = "b" } };

            var list = new PropertyList(new SerializedObject(host).FindProperty("entries"));

            Assert.That(list.Rows.childCount, Is.EqualTo(2));
        }

        [Test]
        public void NotAnArray_ShowsNothing()
        {
            var list = new PropertyList(new SerializedObject(CreateHost()).FindProperty("text"));

            Assert.That(list.Rows.childCount, Is.Zero);
            Assert.That(list.CountLabel.text, Is.Empty);
            Assert.That(list.AddButton.enabledSelf, Is.False);
        }

        [Test]
        public void PersistenceKey_IsThePanels()
        {
            var list = CreateList(CreateHost(1), out _);

            list.PersistenceKey = PERSISTENCE_KEY;
            list.Collapsed = true;

            Assert.That(list.Panel.PersistenceKey, Is.EqualTo(PERSISTENCE_KEY));
            Assert.That(EditorUserSettings.GetConfigValue(PERSISTENCE_KEY), Is.EqualTo(bool.TrueString));
        }

        [Test]
        public void BindProperty_Null_Unbinds()
        {
            var list = CreateList(CreateHost(1, 2), out _);

            list.BindProperty(null);

            Assert.That(list.Property, Is.Null);
            Assert.That(list.Rows.childCount, Is.Zero);
            Assert.That(list.CountLabel.text, Is.Empty);
        }

        [UnityTest]
        public IEnumerator BoundList_UnderABoundParent_KeepsWorking()
        {
            var host = CreateHost(1, 2);
            var list = CreateList(host, out var serializedObject);
            var container = new VisualElement();
            container.Add(list);
            container.Bind(serializedObject);
            Root.Add(container);
            yield return WaitForLayout(list.Rows[1]);

            var field = list.Rows[1].Q<IntegerField>();
            yield return WaitUntil(() => field != null && field.value == 2);

            Assert.That(list.Rows.childCount, Is.EqualTo(2));
            Assert.That(field.value, Is.EqualTo(2));

            field.value = 7;
            yield return WaitUntil(() => host.numbers[1] == 7);

            Assert.That(host.numbers, Is.EqualTo(new[] { 1, 7 }));
        }
        #endregion

        #region Add and remove
        [UnityTest]
        public IEnumerator AddButton_AppendsLikeUnity_AndUndoes()
        {
            var host = CreateHost(5);
            var list = CreateList(host, out _);
            yield return ShowInWindow(list);
            Undo.IncrementCurrentGroup();

            Click(list.AddButton.worldBound.center);

            Assert.That(host.numbers, Is.EqualTo(new[] { 5, 5 }), "Unity copies the last element");
            Assert.That(list.Rows.childCount, Is.EqualTo(2));
            Assert.That(list.CountLabel.text, Is.EqualTo("2 items"));

            Undo.PerformUndo();
            yield return WaitUntil(() => list.Rows.childCount == 1);

            Assert.That(host.numbers, Is.EqualTo(new[] { 5 }));
            Assert.That(list.Rows.childCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AddRequested_GetsTheAnchor_AndNothingIsAdded()
        {
            var host = CreateHost(5);
            var list = CreateList(host, out _);
            Rect? anchor = null;
            list.AddRequested = _Anchor => anchor = _Anchor;
            yield return ShowInWindow(list);

            Click(list.AddButton.worldBound.center);

            Assert.That(anchor, Is.EqualTo(list.AddButton.worldBound));
            Assert.That(host.numbers, Is.EqualTo(new[] { 5 }));
        }

        [UnityTest]
        public IEnumerator SetAddEnabled_False_DisablesAdd_AndTheWrapperHasTheTooltip()
        {
            var host = CreateHost(5);
            var list = CreateList(host, out _);
            yield return ShowInWindow(list);

            list.SetAddEnabled(false, "Every number is already added");
            var center = list.AddButton.worldBound.center;

            Assert.That(list.AddButton.enabledSelf, Is.False);
            Assert.That(list.AddWrapper.tooltip, Is.EqualTo("Every number is already added"));
            Assert.That(Root.panel.Pick(center), Is.SameAs(list.AddWrapper), "the tooltip's element is under the pointer");

            Click(center);
            Assert.That(host.numbers, Is.EqualTo(new[] { 5 }));

            list.SetAddEnabled(true);
            Assert.That(list.AddButton.enabledSelf, Is.True);
            Assert.That(list.AddWrapper.tooltip, Is.Null.Or.Empty);
            Assert.That(Root.panel.Pick(center), Is.SameAs(list.AddButton));
        }

        [UnityTest]
        public IEnumerator RemoveButton_RemovesItsElement()
        {
            var host = CreateHost(1, 2, 3);
            var list = CreateList(host, out _);
            yield return ShowInWindow(list);

            Click(RemoveButton(list, 1).worldBound.center);

            Assert.That(host.numbers, Is.EqualTo(new[] { 1, 3 }));
            Assert.That(list.Rows.childCount, Is.EqualTo(2));
        }

        [Test]
        public void ItemsChanged_IsRaisedOnRebuild()
        {
            var list = CreateList(CreateHost(1), out _);
            int changes = 0;
            list.ItemsChanged += () => changes++;

            list.Refresh();

            Assert.That(changes, Is.EqualTo(1));
        }
        #endregion

        #region Reorder
        [UnityTest]
        public IEnumerator DragByHandle_Reorders_AndUndoes()
        {
            var host = CreateHost(1, 2, 3);
            var list = CreateList(host, out _);
            UseLabels(list);
            yield return ShowInWindow(list);
            Undo.IncrementCurrentGroup();

            var lastRow = list.Rows[2].worldBound;
            DragFromTo(Handle(list, 0).worldBound.center, new Vector2(lastRow.center.x, lastRow.yMax - 1f));

            Assert.That(host.numbers, Is.EqualTo(new[] { 2, 3, 1 }));
            Assert.That(list.Rows[0].Q<Label>(className: PropertyList.USS_CLASS_ROW_CONTENT).text, Is.EqualTo("Item 2"));

            Undo.PerformUndo();
            yield return WaitUntil(() => list.Rows[0].Q<Label>(className: PropertyList.USS_CLASS_ROW_CONTENT).text == "Item 1");

            Assert.That(host.numbers, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(list.Rows[0].Q<Label>(className: PropertyList.USS_CLASS_ROW_CONTENT).text, Is.EqualTo("Item 1"),
                "rows rebuilt after an undone move");
        }

        [UnityTest]
        public IEnumerator DragFromContent_DoesNotReorder()
        {
            var host = CreateHost(1, 2, 3);
            var list = CreateList(host, out _);
            UseLabels(list);
            yield return ShowInWindow(list);

            var content = list.Rows[0].Q<Label>(className: PropertyList.USS_CLASS_ROW_CONTENT);
            Assume.That(Root.panel.Pick(content.worldBound.center), Is.SameAs(content));

            var lastRow = list.Rows[2].worldBound;
            DragFromTo(content.worldBound.center, new Vector2(lastRow.center.x, lastRow.yMax - 1f));

            Assert.That(host.numbers, Is.EqualTo(new[] { 1, 2, 3 }));
        }
        #endregion

        #region External changes
        [Test]
        public void IsReordering_TellsMovesFromEdits()
        {
            Assert.That(PropertyList.IsReordering(new uint[] { 1, 2, 3 }, new uint[] { 2, 3, 1 }), Is.True);
            Assert.That(PropertyList.IsReordering(new uint[] { 1, 2, 3 }, new uint[] { 1, 2, 3 }), Is.False, "unchanged");
            Assert.That(PropertyList.IsReordering(new uint[] { 1, 2, 3 }, new uint[] { 1, 9, 3 }), Is.False, "an edit");
            Assert.That(PropertyList.IsReordering(new uint[] { 1, 1, 2 }, new uint[] { 1, 2, 2 }), Is.False, "same values, other counts");
        }

        [UnityTest]
        public IEnumerator ExternalMove_RebuildsRows()
        {
            var host = CreateHost(1, 2, 3);
            var list = CreateList(host, out _);
            UseLabels(list);
            yield return ShowInWindow(list);

            var other = new SerializedObject(host);
            other.FindProperty("numbers").MoveArrayElement(2, 0);
            other.ApplyModifiedProperties();
            yield return WaitUntil(() => list.Rows[0].Q<Label>(className: PropertyList.USS_CLASS_ROW_CONTENT).text == "Item 3");

            Assert.That(list.Rows[0].Q<Label>(className: PropertyList.USS_CLASS_ROW_CONTENT).text, Is.EqualTo("Item 3"));
        }

        [UnityTest]
        public IEnumerator ExternalSizeChange_RebuildsRows_ValueEditDoesNot()
        {
            var host = CreateHost(1, 2);
            var list = CreateList(host, out _);
            yield return ShowInWindow(list);
            var firstRow = list.Rows[0];

            var other = new SerializedObject(host);
            other.FindProperty("numbers.Array.data[0]").intValue = 9;
            other.ApplyModifiedProperties();
            yield return WaitFrames(10);

            Assert.That(list.Rows[0], Is.SameAs(firstRow), "a value edit keeps the rows");

            other.Update();
            other.FindProperty("numbers").arraySize = 3;
            other.ApplyModifiedProperties();
            yield return WaitUntil(() => list.Rows.childCount == 3);

            Assert.That(list.Rows.childCount, Is.EqualTo(3));
            Assert.That(list.CountLabel.text, Is.EqualTo("3 items"));
        }
        #endregion

        #region Multi-object
        [Test]
        public void MultiObject_DifferentSizes_IsReadOnlyWithAMessage()
        {
            var serializedObject = new SerializedObject(new Object[] { CreateHost(1), CreateHost(1, 2) });

            var list = new PropertyList(serializedObject.FindProperty("numbers"));

            Assert.That(list.CountLabel.text, Is.EqualTo(PropertyList.MIXED_COUNT));
            Assert.That(IsShown(list.MessageLabel), Is.True);
            Assert.That(IsShown(list.EmptyLabel), Is.False);
            Assert.That(list.Rows.childCount, Is.Zero);
            Assert.That(list.AddButton.enabledSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator MultiObject_SameSizes_EditsEveryObject()
        {
            var first = CreateHost(1, 2);
            var second = CreateHost(3, 4);
            var list = new PropertyList(new SerializedObject(new Object[] { first, second }).FindProperty("numbers"));
            yield return ShowInWindow(list);

            Assert.That(list.CountLabel.text, Is.EqualTo("2 items"));
            Assert.That(IsShown(RemoveButton(list, 0)), Is.True);

            Click(RemoveButton(list, 0).worldBound.center);

            Assert.That(first.numbers, Is.EqualTo(new[] { 2 }));
            Assert.That(second.numbers, Is.EqualTo(new[] { 4 }));
        }

        // Unity's multi-object SerializedObject doesn't notice edits to a target other than the first on its own.
        [UnityTest]
        public IEnumerator MultiObject_SecondTargetEdited_SizesFollow()
        {
            var first = CreateHost(1, 2);
            var second = CreateHost(3);
            var list = new PropertyList(new SerializedObject(new Object[] { first, second }).FindProperty("numbers"));
            yield return ShowInWindow(list);
            Assert.That(list.CountLabel.text, Is.EqualTo(PropertyList.MIXED_COUNT));

            var other = new SerializedObject(second);
            other.FindProperty("numbers").arraySize = 2;
            other.ApplyModifiedProperties();
            yield return WaitForPoll(() => list.CountLabel.text == "2 items");

            Assert.That(list.CountLabel.text, Is.EqualTo("2 items"), "same sizes again: editable");
            Assert.That(IsShown(list.MessageLabel), Is.False);
            Assert.That(list.Rows.childCount, Is.EqualTo(2));

            other.Update();
            other.FindProperty("numbers").arraySize = 3;
            other.ApplyModifiedProperties();
            yield return WaitForPoll(() => list.CountLabel.text == PropertyList.MIXED_COUNT);

            Assert.That(list.CountLabel.text, Is.EqualTo(PropertyList.MIXED_COUNT), "different sizes: read-only");
            Assert.That(list.Rows.childCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator MultiObject_SecondTargetChangedByScript_SizesFollow()
        {
            var first = CreateHost(1, 2);
            var second = CreateHost(3);
            var list = new PropertyList(new SerializedObject(new Object[] { first, second }).FindProperty("numbers"));
            yield return ShowInWindow(list);

            second.numbers.Add(4);
            EditorUtility.SetDirty(second);
            yield return WaitForPoll(() => list.CountLabel.text == "2 items");

            Assert.That(list.CountLabel.text, Is.EqualTo("2 items"));
        }

        [Test]
        public void MultiObject_Refresh_ReadsTheSecondTarget()
        {
            var first = CreateHost(1, 2);
            var second = CreateHost(3);
            var list = new PropertyList(new SerializedObject(new Object[] { first, second }).FindProperty("numbers"));

            var other = new SerializedObject(second);
            other.FindProperty("numbers").arraySize = 2;
            other.ApplyModifiedProperties();
            list.Refresh();

            Assert.That(list.CountLabel.text, Is.EqualTo("2 items"));
        }

        [UnityTest]
        public IEnumerator MultiObject_SecondTargetDestroyed_ClearsRowsWithoutErrors()
        {
            var first = CreateHost(1, 2);
            var second = CreateHost(3, 4);
            var list = new PropertyList(new SerializedObject(new Object[] { first, second }).FindProperty("numbers"));
            yield return ShowInWindow(list);
            Assert.That(list.Rows.childCount, Is.EqualTo(2));

            Object.DestroyImmediate(second);
            yield return WaitForPoll(() => list.Rows.childCount == 0);

            Assert.That(list.Rows.childCount, Is.Zero);
            Assert.That(list.CountLabel.text, Is.Empty);
            list.Refresh();
        }
        #endregion

        #region Destroyed target
        [Test]
        public void TargetDestroyed_Refresh_LogsNothing_AndClearsRows()
        {
            var host = CreateHost(1, 2);
            var list = CreateList(host, out _);
            Assert.That(list.Rows.childCount, Is.EqualTo(2));

            Object.DestroyImmediate(host);
            list.Refresh();

            Assert.That(list.Rows.childCount, Is.Zero);
            Assert.That(list.CountLabel.text, Is.Empty);
            Assert.That(list.AddButton.enabledSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator TargetDestroyed_ClearsRows_WithoutRefresh()
        {
            var host = CreateHost(1, 2);
            var list = CreateList(host, out _);
            yield return ShowInWindow(list);

            Object.DestroyImmediate(host);
            yield return WaitForPoll(() => list.Rows.childCount == 0);

            Assert.That(list.Rows.childCount, Is.Zero);
        }
        #endregion
    }
}
