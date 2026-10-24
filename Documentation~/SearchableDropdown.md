# Searchable dropdown

`LMK.Editor.Windows.SearchableDropdown` is a `PopupWindowContent` that shows a list of items as a folder hierarchy, with a search bar and an expand/collapse all menu.

## Items

```csharp
var dropdown = new SearchableDropdown(
    new GUIContent("<alpha=#4C>None"),
    new GUIContent("Shapes/Circle", "Tooltip"),
    new GUIContent("Shapes/<color=#ff3434>Red</color>/Square", "Tooltip", icon));
dropdown.OnSelectedItemChanged += index => { /* index into the items given above */ };
UnityEditor.PopupWindow.Show(buttonRect, dropdown);
```

- `GUIContent.text` is the item path. Each `/` starts a folder. A `/` right after `<` is a rich text closing tag, not a separator.
- Rich text is supported in names. The tooltip and image are shown on the item itself, not its folders.
- An item that can't be shown (empty or duplicate path) still keeps its index, so indices always match the items you passed.
- Optional `@` search text per item: `new SearchableDropdown(items, namespaces)` or `AddItems(items, namespaces)`.

## Search syntax

| Query | Matches |
|---|---|
| `a b` | both terms, in any order (AND) |
| `a\|b` | either group (OR) |
| `-a` | items without the term (NOT) |
| `"a b"` | the exact phrase, space included |
| `@a` | namespace/assembly text contains `a` |
| `#a` | tooltip contains `a` |

- Terms without a prefix search the item's full path, so a folder name matches every item inside it.
- Matching ignores case and rich text tags.
- A folder is hidden when none of its items match.

The parser is `LMK.Editor.Utilities.SearchQuery`, and can be used on its own: `SearchQuery.Parse(text).Matches(provider)`, where the provider implements `ISearchFieldProvider`.

## HierarchyItem

`LMK.Editor.UIElements.HierarchyItem` is the base class of the dropdown's items (`StructuredMenuItem`). It keeps parent/child links separate from the visual tree, and distributes events to callbacks registered on each item. Adding items and placing them in the visual tree is left to subclasses.

```csharp
public class MyItem : HierarchyItem, ISearchFieldProvider
{
    // Return normalized text. Cache it in real code: this runs for every item on each keystroke.
    public string GetSearchField(SearchField field) => field == SearchField.Name ? SearchQuery.Normalize(name) : null;

    public MyItem()
    {
        RegisterHierarchyCallback<SetExpandedEvent>(e => foldout.value = e.expanded);
        RegisterHierarchyCallback<FilterEvent>(OnFilterMatch);                                    // before children
        RegisterHierarchyCallback<FilterEvent>(OnFilterApply, HierarchyEventPhase.AfterChildren); // after all descendants
    }

    void OnFilterMatch(FilterEvent e) { if (ChildItems.Count == 0 && e.query.Matches(this)) e.MarkMatched(this); }
    void OnFilterApply(FilterEvent e) { style.display = e.IsKept(this) ? DisplayStyle.Flex : DisplayStyle.None; }
}

root.DispatchHierarchyEvent(new SetExpandedEvent(false)); // this item and its descendants
item.BroadcastHierarchyEvent(new FilterEvent("query"));   // the whole hierarchy, from its root
```

- Each item runs its `BeforeChildren` callbacks, then its children, then its `AfterChildren` callbacks.
- `StopPropagation()` stops the event for the remaining items and phases.
- Custom events derive from `HierarchyItemEvent<TEvent>`. Callbacks are matched to the exact event type.
- `FilterEvent.MarkMatched(item)` keeps the item and all of its ancestors. Read `IsKept(item)` in `AfterChildren`.
