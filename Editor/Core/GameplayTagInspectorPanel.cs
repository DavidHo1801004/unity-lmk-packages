using System;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.UIElements;
using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;

using ToolbarButton = LMK.Editor.UIElements.ToolbarButton;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Details of the tag selected in the Gameplay Tag Manager: its name (editable), its registry, and the rows
    /// Explicit, Parent, Description and Marker. Edits are staged through <see cref="TagOperations"/>.
    /// </summary>
    /// <remarks>
    /// Tags of read-only registries show their name, explicit state and description disabled. Markers are per
    /// user, so they can be set on any tag.
    /// </remarks>
    internal sealed class GameplayTagInspectorPanel : VisualElement
    {
        private const string USS_CLASS = "tag-inspector";
        private const string USS_CHIP = USS_CLASS + "__chip";

        /// <summary>
        /// Called when the parent tag link is clicked.
        /// </summary>
        public event Action<GameplayTagNode> ParentClicked;

        /// <summary>
        /// Called with the outcome of an edit, for the host to show.
        /// </summary>
        public event Action<string, HelpBoxMessageType> Report;

        private GameplayTagNode node;



        public GameplayTagInspectorPanel()
        {
            name = "tag-inspector";
            AddToClassList(USS_CLASS);

            SetNode(null);
        }

        /// <summary>
        /// Show a node: a tag's details, or a placeholder for nothing or a registry header.
        /// </summary>
        public void SetNode(GameplayTagNode _Node)
        {
            node = _Node;
            Clear();

            if (_Node == null || _Node.IsRegistryHeader || _Node.IsRoot)
            {
                var placeholder = new Label("Select a tag to see its details.");
                placeholder.AddToClassList(USS_CLASS + "__placeholder");
                Add(placeholder);
                return;
            }

            Add(CreateTitle(_Node));
            Add(CreateSubheading(_Node));

            var rows = new FieldRowGroup { RowSpacing = 4 };
            rows.AddToClassList(USS_CLASS + "__rows");
            rows.Add(new FieldRow("Is Explicit", CreateExplicitValue(_Node)));
            rows.Add(new FieldRow("Parent", CreateParentValue(_Node)));
            rows.Add(new FieldRow("Description", CreateDescriptionValue(_Node)) { AlignTop = true });
            rows.Add(new FieldRow("Marker", CreateMarkerValue(_Node)));
            Add(rows);
        }



        #region Elements
        private EditableLabel CreateTitle(GameplayTagNode _Node)
        {
            var title = new EditableLabel
            {
                name = "tag-title",
                value = _Node.ShortTag,
                Editable = !_Node.IsReadOnly,
                IsValidChar = TagPath.IsValidSegmentChar,
                Validate = text => ValidateSegment(_Node, text),
                tooltip = _Node.FullTag,
            };
            title.AddToClassList(EditableLabel.titleVariantUssClassName);
            title.AddToClassList(USS_CLASS + "__title");

            var id = _Node.Id;
            string oldPath = _Node.FullTag;
            title.RegisterValueChangedCallback(e =>
            {
                string segment = e.newValue;
                Defer(() => Notify(
                    TagOperations.Rename(id, segment),
                    $"Tag renamed: \"{oldPath}\" to \"{TagPath.Combine(TagPath.GetParent(oldPath), segment)}\". Not saved yet."));
            });

            return title;
        }

        /// <summary>
        /// "from [Registry] main registry": the registry name is a link to its file.
        /// </summary>
        private VisualElement CreateSubheading(GameplayTagNode _Node)
        {
            var subheading = new VisualElement { name = "tag-subheading" };
            subheading.AddToClassList(USS_CLASS + "__subheading");

            var from = new Label("from");
            from.AddToClassList(USS_CLASS + "__muted");
            subheading.Add(from);

            var registry = TagRegistryService.Snapshot.Find(_Node.RegistryId);
            string assetPath = registry?.AssetPath;

            var link = new LinkLabel(_Node.RegistryName) { name = "registry-link", tooltip = $"Select \"{assetPath}\" in the Project window." };
            link.AddToClassList(USS_CHIP);
            link.clicked += () => SelectAsset(assetPath);
            link.SetEnabled(!string.IsNullOrEmpty(assetPath));
            subheading.Add(link);

            var kind = new Label($"{KindLabel(_Node.RegistryKind)} registry{(_Node.IsReadOnly ? ", read-only" : string.Empty)}") { name = "registry-kind" };
            kind.AddToClassList(USS_CLASS + "__muted");
            subheading.Add(kind);

            return subheading;
        }

        private VisualElement CreateExplicitValue(GameplayTagNode _Node)
        {
            var value = new VisualElement();
            value.AddToClassList(USS_CLASS + "__inline");

            var state = new Label(_Node.IsExplicit ? "Yes" : "No")
            {
                name = "explicit-value",
                tooltip = _Node.IsExplicit ? "Added explicitly." : "Implicit: exists only as the parent of added tags.",
            };
            value.Add(state);

            if (!_Node.IsExplicit)
            {
                var id = _Node.Id;
                string path = _Node.FullTag;
                var makeExplicit = new Button(() => Defer(() => Notify(TagOperations.MakeExplicit(id), $"Tag made explicit: \"{path}\". Not saved yet.")))
                {
                    name = "make-explicit-button",
                    text = "Make Explicit",
                };
                makeExplicit.AddToClassList(USS_CLASS + "__button");
                makeExplicit.AddToClassList(USS_CHIP);
                makeExplicit.SetEnabled(!_Node.IsReadOnly);
                value.Add(makeExplicit);
            }

            return value;
        }

        private VisualElement CreateParentValue(GameplayTagNode _Node)
        {
            var parent = _Node.Parent;
            if (parent == null || parent.IsRegistryHeader || parent.IsRoot)
            {
                var none = new Label("None") { name = "parent-value", tooltip = "A top level tag." };
                none.AddToClassList(USS_CLASS + "__muted");
                return none;
            }

            var link = new LinkLabel(parent.FullTag) { name = "parent-value", tooltip = "Select the parent tag." };
            link.AddToClassList(USS_CHIP);
            link.clicked += () => ParentClicked?.Invoke(parent);
            return link;
        }

        private VisualElement CreateDescriptionValue(GameplayTagNode _Node)
        {
            // Committed when it loses focus.
            var description = new TextField
            {
                name = "description-field",
                multiline = true,
                isDelayed = true,
                value = _Node.Comment,
            };
            description.AddToClassList(USS_CLASS + "__description");
            description.SetEnabled(!_Node.IsReadOnly);

            var id = _Node.Id;
            string path = _Node.FullTag;
            description.RegisterValueChangedCallback(e =>
            {
                string text = e.newValue;
                Defer(() => Notify(TagOperations.EditDescription(id, text), $"Description of \"{path}\" changed. Not saved yet."));
            });

            return description;
        }

        /// <summary>
        /// The registry core's marker field: a colour field, empty (transparent) when the tag has no marker, and a
        /// button to remove it.
        /// </summary>
        private VisualElement CreateMarkerValue(GameplayTagNode _Node)
        {
            var value = new RegistryMarkerField(TagKind.Instance, _Node.RegistryId, _Node.Id);
            value.AddToClassList(USS_CLASS + "__inline");
            value.Field.AddToClassList(USS_CLASS + "__marker-field");
            value.ClearButton.AddToClassList(USS_CLASS + "__button");

            return value;
        }
        #endregion



        #region Helpers
        /// <summary>
        /// A segment is valid, and no sibling has it.
        /// </summary>
        private static string ValidateSegment(GameplayTagNode _Node, string _Segment)
        {
            if (!TagPath.IsValidSegment(_Segment, out string error)) return error;

            if (_Node.Parent != null)
                foreach (var sibling in _Node.Parent.Children)
                    if (sibling != _Node && sibling.ShortTag == _Segment)
                        return $"A sibling tag is already named \"{_Segment}\".";

            return null;
        }

        private static string KindLabel(TagRegistryKind _Kind) => _Kind switch
        {
            TagRegistryKind.Main => "main",
            TagRegistryKind.Package => "package",
            TagRegistryKind.Generated => "generated",
            _ => string.Empty,
        };

        private static void SelectAsset(string _AssetPath)
        {
            var asset = string.IsNullOrEmpty(_AssetPath) ? null : AssetDatabase.LoadMainAssetAtPath(_AssetPath);
            if (asset == null) return;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        /// Run an edit after the current event: it reloads the registries, which rebuilds the tree and this panel.
        /// </summary>
        private static void Defer(Action _Edit) => EditorApplication.delayCall += () => _Edit();

        private void Notify(RegistryResult _Result, string _SuccessMessage)
        {
            if (_Result.Success)
                Report?.Invoke(_SuccessMessage, HelpBoxMessageType.Info);
            else
            {
                Report?.Invoke(_Result.Error, HelpBoxMessageType.Error);

                // Show the current values again, e.g. the name the rename didn't change.
                SetNode(node);
            }
        }
        #endregion
    }
}
