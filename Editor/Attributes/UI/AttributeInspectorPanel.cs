using System;
using System.Linq;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Registry;
using LMK.Editor.UIElements;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

using ToolbarButton = LMK.Editor.UIElements.ToolbarButton;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Details of the attribute selected in the Attribute Manager: its name (editable), its registry, then the rows
    /// Group, Default, Min, Max, On Max Change, Meta, Integer, Description and Bound Of. Edits are staged through
    /// <see cref="AttributeOperations"/>. Uses the tag inspector's USS classes, so both look the same.
    /// </summary>
    /// <remarks>
    /// Attributes of read-only registries show their values disabled.
    /// </remarks>
    internal sealed class AttributeInspectorPanel : VisualElement
    {
        private const string USS_CLASS = "tag-inspector";
        private const string USS_CHIP = USS_CLASS + "__chip";
        private const string USS_ATTRIBUTE = "attribute-inspector";

        private const string SEARCH_ICON = "Search Icon";

        /// <summary>
        /// Called with the outcome of an edit, for the host to show.
        /// </summary>
        public event Action<string, HelpBoxMessageType> Report;

        /// <summary>
        /// Called when a link to another attribute is clicked: (registry, attribute).
        /// </summary>
        public event Action<RegistryGuid, RegistryGuid> AttributeClicked;

        private AttributeTreeNode node;

        private static AttributeKind Kind => AttributeKind.Instance;



        public AttributeInspectorPanel()
        {
            name = "attribute-inspector";
            AddToClassList(USS_CLASS);
            AddToClassList(USS_ATTRIBUTE);

            SetNode(null);
        }

        /// <summary>
        /// Show a node: an attribute's details, or a placeholder for nothing, a registry header or a group.
        /// </summary>
        public void SetNode(AttributeTreeNode _Node)
        {
            node = _Node;
            Clear();

            if (_Node == null || !_Node.IsAttribute)
            {
                var placeholder = new Label("Select an attribute to see its details.") { name = "no-selection-label" };
                placeholder.AddToClassList(USS_CLASS + "__placeholder");
                Add(placeholder);
                return;
            }

            Add(CreateTitleRow(_Node));
            Add(CreateSubheading(_Node));

            // A line between the attribute's identity and its fields.
            var separator = new NamedSeparator { name = "details-separator" };
            separator.AddToClassList(USS_CLASS + "__separator");
            Add(separator);

            var row = _Node.Row;
            var rows = new FieldRowGroup { RowSpacing = 4 };
            rows.AddToClassList(USS_CLASS + "__rows");
            rows.Add(new FieldRow("Group", CreateGroupValue(_Node)));
            rows.Add(new FieldRow("Default", CreateDefaultValue(_Node)));
            rows.Add(new FieldRow("Min", new AttributeBoundField(_Node, row.min, "min", (ref AttributeMetadata m, AttributeBound b) => m.min = b, Edit)));
            rows.Add(new FieldRow("Max", new AttributeBoundField(_Node, row.max, "max", (ref AttributeMetadata m, AttributeBound b) => m.max = b, Edit)));
            rows.Add(new FieldRow("On Max Change", CreateOnMaxChangeValue(_Node)));
            rows.Add(new FieldRow("Meta", CreateFlagValue(_Node, AttributeFlags.Meta, "meta-toggle",
                "A scratch value, e.g. incoming damage: no bounds, no change events, reset after each execution.")));
            rows.Add(new FieldRow("Integer", CreateFlagValue(_Node, AttributeFlags.Integer, "integer-toggle",
                "Rounded to the nearest integer after evaluation.")));
            rows.Add(new FieldRow("Description", CreateDescriptionValue(_Node)) { AlignTop = true });
            rows.Add(new FieldRow("Bound Of", CreateDependentsValue(_Node)) { AlignTop = true });
            Add(rows);
        }



        #region Identity
        /// <summary>
        /// The editable title, with the item's actions as icons at the end.
        /// </summary>
        private VisualElement CreateTitleRow(AttributeTreeNode _Node)
        {
            var titleRow = new VisualElement { name = "title-row" };
            titleRow.AddToClassList(USS_CLASS + "__title-row");

            var title = new EditableLabel
            {
                name = "attribute-title",
                value = _Node.ShortName,
                Editable = !_Node.IsReadOnly,
                IsValidChar = IsSegmentChar,
                Validate = text => ValidateShortName(_Node, text),
                tooltip = _Node.FullName,
            };
            title.AddToClassList(EditableLabel.titleVariantUssClassName);
            title.AddToClassList(USS_CLASS + "__title");

            var id = _Node.Id;
            string oldName = _Node.FullName;
            string group = _Node.Group;
            title.RegisterValueChangedCallback(e =>
            {
                string newName = Combine(group, e.newValue);
                Defer(() => Notify(AttributeOperations.Rename(id, newName), $"Attribute renamed: \"{oldName}\" to \"{newName}\". Not saved yet."));
            });
            titleRow.Add(title);

            var registryId = _Node.RegistryId;
            var findReferences = CreateIconButton("find-references-button", SEARCH_ICON, "Find References",
                () => RegistryReferenceWindow.ShowReferences(Kind, (registryId, id)));
            titleRow.Add(findReferences);

            return titleRow;
        }

        /// <summary>
        /// "from [Registry] main registry": the registry name is a link to its file.
        /// </summary>
        private VisualElement CreateSubheading(AttributeTreeNode _Node)
        {
            var subheading = new VisualElement { name = "attribute-subheading" };
            subheading.AddToClassList(USS_CLASS + "__subheading");

            var from = new Label("from");
            from.AddToClassList(USS_CLASS + "__muted");
            subheading.Add(from);

            string assetPath = Kind.Snapshot.Find(_Node.RegistryId)?.AssetPath;

            var link = new LinkLabel(_Node.RegistryName) { name = "registry-link", tooltip = $"Select \"{assetPath}\" in the Project window." };
            link.AddToClassList(USS_CHIP);
            link.clicked += () => SelectAsset(assetPath);
            link.SetEnabled(!string.IsNullOrEmpty(assetPath));
            subheading.Add(link);

            var origin = new Label($"{AttributeNodeElement.OriginLabel(_Node.Origin)} registry{(_Node.IsReadOnly ? ", read-only" : string.Empty)}")
            {
                name = "registry-kind",
            };
            origin.AddToClassList(USS_CLASS + "__muted");
            subheading.Add(origin);

            return subheading;
        }
        #endregion



        #region Fields
        /// <summary>
        /// The group, e.g. "Vitals". Only letters and digits can be typed or pasted; Enter or leaving the field
        /// moves the attribute, Esc puts the group back.
        /// </summary>
        private VisualElement CreateGroupValue(AttributeTreeNode _Node)
        {
            var field = new TextField { name = "group-field", value = _Node.Group };
            field.AddToClassList(USS_ATTRIBUTE + "__field");
            field.SetEnabled(!_Node.IsReadOnly);

            var id = _Node.Id;
            string oldName = _Node.FullName;
            string shortName = _Node.ShortName;
            string oldGroup = _Node.Group;
            bool done = false;

            void Commit()
            {
                if (done) return;

                string group = field.value.Trim();
                if (group == oldGroup) return;

                if (!IsValidGroup(group))
                {
                    Report?.Invoke("A group can only contain letters (a-z, A-Z) and digits (0-9).", HelpBoxMessageType.Error);
                    field.SetValueWithoutNotify(oldGroup);
                    return;
                }

                string newName = Combine(group, shortName);
                if (!AttributeName.IsValid(newName, out string error))
                {
                    Report?.Invoke(error, HelpBoxMessageType.Error);
                    field.SetValueWithoutNotify(oldGroup);
                    return;
                }

                done = true;
                Defer(() => Notify(AttributeOperations.Rename(id, newName), $"Attribute moved: \"{oldName}\" to \"{newName}\". Not saved yet."));
            }

            field.RegisterCallback<KeyDownEvent>(e =>
            {
                switch (e.keyCode)
                {
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        e.StopImmediatePropagation();
                        Commit();
                        return;

                    case KeyCode.Escape:
                        e.StopImmediatePropagation();
                        field.SetValueWithoutNotify(oldGroup);
                        return;
                }

                if (e.character == '\0' || char.IsControl(e.character) || IsGroupChar(e.character)) return;

                e.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            // Pasted text keeps only letters and digits. A group written before this rule keeps its '.' and '_'
            // until it is changed, and Commit rejects it then.
            field.RegisterValueChangedCallback(e =>
            {
                string filtered = FilterInserted(e.previousValue, e.newValue, IsGroupChar);
                if (filtered != e.newValue)
                    field.SetValueWithoutNotify(filtered);
            });

            field.RegisterCallback<FocusOutEvent>(_ => Commit());

            return field;
        }

        private VisualElement CreateDefaultValue(AttributeTreeNode _Node)
        {
            var field = new FloatField
            {
                name = "default-field",
                value = _Node.Row.defaultValue,
                isDelayed = true,
                tooltip = "The base value a unit starts with when its set definition gives none.",
            };
            field.AddToClassList(USS_ATTRIBUTE + "__field");
            field.SetEnabled(!_Node.IsReadOnly);
            field.RegisterValueChangedCallback(e => Edit(_Node, (ref AttributeMetadata m) => m.defaultValue = e.newValue));
            return field;
        }

        private VisualElement CreateOnMaxChangeValue(AttributeTreeNode _Node)
        {
            var field = new EnumField(_Node.Row.onMaxChange)
            {
                name = "on-max-change-field",
                tooltip = "What happens to this attribute when the attribute that is its max changes: clamp to it, or keep the ratio.",
            };
            field.AddToClassList(USS_ATTRIBUTE + "__field");
            field.SetEnabled(!_Node.IsReadOnly && _Node.Row.max.IsAttribute);
            field.RegisterValueChangedCallback(e => Edit(_Node, (ref AttributeMetadata m) => m.onMaxChange = (AttributeMaxChange)e.newValue));
            return field;
        }

        private VisualElement CreateFlagValue(AttributeTreeNode _Node, AttributeFlags _Flag, string _Name, string _Tooltip)
        {
            var toggle = new Toggle { name = _Name, value = (_Node.Row.flags & _Flag) != 0, tooltip = _Tooltip };
            toggle.SetEnabled(!_Node.IsReadOnly);
            toggle.RegisterValueChangedCallback(e => Edit(_Node, (ref AttributeMetadata m) =>
                m.flags = e.newValue ? m.flags | _Flag : m.flags & ~_Flag));
            return toggle;
        }

        private VisualElement CreateDescriptionValue(AttributeTreeNode _Node)
        {
            // Committed when it loses focus.
            var description = new TextField
            {
                name = "description-field",
                multiline = true,
                isDelayed = true,
                value = _Node.Row.description,
            };
            description.AddToClassList(USS_CLASS + "__description");
            description.SetEnabled(!_Node.IsReadOnly);
            description.RegisterValueChangedCallback(e => Edit(_Node, (ref AttributeMetadata m) => m.description = e.newValue));
            return description;
        }

        /// <summary>
        /// The attributes that use this one as a bound, as links that select them.
        /// </summary>
        private VisualElement CreateDependentsValue(AttributeTreeNode _Node)
        {
            var dependents = AttributeOperations.FindDependents(_Node.RegistryId, _Node.Id);
            if (dependents.Count == 0)
            {
                var none = new Label("None") { name = "dependents-value", tooltip = "No attribute uses this one as its min or max." };
                none.AddToClassList(USS_CLASS + "__muted");
                return none;
            }

            var value = new VisualElement { name = "dependents-value" };
            value.AddToClassList(USS_ATTRIBUTE + "__links");

            foreach (var (registry, id) in dependents.OrderBy(e => Kind.GetRowName(e.Registry, e.Id), StringComparer.Ordinal))
            {
                string name = Kind.GetRowName(registry, id) ?? id.ToString();
                if (registry != _Node.RegistryId)
                    name = $"{Kind.Snapshot.Find(registry)?.Name}: {name}";

                var link = new LinkLabel(name) { tooltip = "Select this attribute." };
                link.AddToClassList(USS_CHIP);
                link.clicked += () => AttributeClicked?.Invoke(registry, id);
                value.Add(link);
            }

            return value;
        }
        #endregion



        #region Helpers
        internal delegate void ModifyMetadata(ref AttributeMetadata _Metadata);

        /// <summary>
        /// Stage the attribute's metadata with one change, after the current event.
        /// </summary>
        private void Edit(AttributeTreeNode _Node, ModifyMetadata _Change)
        {
            var id = _Node.Id;
            var metadata = AttributeMetadata.Of(_Node.Row);
            _Change(ref metadata);

            string name = _Node.FullName;
            Defer(() => Notify(AttributeOperations.EditMetadata(id, metadata), $"\"{name}\" changed. Not saved yet."));
        }

        /// <summary>
        /// A small toolbar button showing a built-in editor icon.
        /// </summary>
        internal static ToolbarButton CreateIconButton(string _Name, string _Icon, string _Tooltip, Action _Clicked)
        {
            var button = new ToolbarButton(false, _Clicked)
            {
                name = _Name,
                iconImage = EditorGUIUtility.IconContent(_Icon).image as Texture2D,
                tooltip = _Tooltip,
            };
            button.AddToClassList(USS_CLASS + "__icon");
            return button;
        }

        /// <summary>
        /// The last segment is valid, and no other attribute of the registry has the resulting name.
        /// </summary>
        private static string ValidateShortName(AttributeTreeNode _Node, string _ShortName)
        {
            if (string.IsNullOrEmpty(_ShortName)) return "An attribute needs a name.";

            string name = Combine(_Node.Group, _ShortName);
            if (!AttributeName.IsValid(name, out string error)) return error;

            var registry = Kind.Snapshot.Find(_Node.RegistryId);
            if (registry != null && registry.TryFind(name, out var other) && other != _Node.Id)
                return $"Another attribute is already named \"{name}\".";

            return null;
        }

        internal static string Combine(string _Group, string _ShortName)
            => string.IsNullOrEmpty(_Group) ? _ShortName : _Group + GameplayAttribute.SEPARATOR + _ShortName;

        /// <summary>
        /// A character of a name segment: a letter, a digit or '_'.
        /// </summary>
        internal static bool IsSegmentChar(char _Char)
            => (_Char >= 'a' && _Char <= 'z') || (_Char >= 'A' && _Char <= 'Z') || (_Char >= '0' && _Char <= '9') || _Char == '_';

        /// <summary>
        /// A character the Group field accepts: a letter or a digit.
        /// </summary>
        internal static bool IsGroupChar(char _Char)
            => (_Char >= 'a' && _Char <= 'z') || (_Char >= 'A' && _Char <= 'Z') || (_Char >= '0' && _Char <= '9');

        /// <summary>
        /// A group set through the Group field: empty, or letters and digits only.
        /// </summary>
        internal static bool IsValidGroup(string _Group) => string.IsNullOrEmpty(_Group) || _Group.All(IsGroupChar);

        /// <summary>
        /// <paramref name="_New"/> with the characters <paramref name="_Old"/> didn't have filtered: the text
        /// between their common start and end, e.g. a paste. What was there already is kept.
        /// </summary>
        internal static string FilterInserted(string _Old, string _New, Func<char, bool> _IsValidChar)
        {
            _Old ??= string.Empty;
            _New ??= string.Empty;

            int prefix = 0;
            while (prefix < _Old.Length && prefix < _New.Length && _Old[prefix] == _New[prefix]) prefix++;

            int suffix = 0;
            while (suffix < _Old.Length - prefix && suffix < _New.Length - prefix
                && _Old[_Old.Length - 1 - suffix] == _New[_New.Length - 1 - suffix]) suffix++;

            string inserted = _New.Substring(prefix, _New.Length - prefix - suffix);
            return _New.Substring(0, prefix) + new string(inserted.Where(_IsValidChar).ToArray()) + _New.Substring(_New.Length - suffix);
        }

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



    /// <summary>
    /// The value of a Min or Max row: the bound's kind (None, Constant, Attribute), then a number field for a
    /// constant or a chip showing the attribute, which opens the attribute picker.
    /// </summary>
    internal sealed class AttributeBoundField : VisualElement
    {
        private const string USS_CLASS = "attribute-bound";

        public delegate void SetBound(ref AttributeMetadata _Metadata, AttributeBound _Bound);

        public delegate void EditMetadata(AttributeTreeNode _Node, AttributeInspectorPanel.ModifyMetadata _Change);

        private static AttributeKind Kind => AttributeKind.Instance;

        public AttributeBoundField(AttributeTreeNode _Node, AttributeBound _Bound, string _Name, SetBound _Set, EditMetadata _Edit)
        {
            name = _Name + "-bound";
            AddToClassList(USS_CLASS);

            bool editable = !_Node.IsReadOnly;
            void Set(AttributeBound _New) => _Edit(_Node, (ref AttributeMetadata m) => _Set(ref m, _New));

            AttributeDropdown.Filter filter = (RegistryGuid registry, RegistryGuid id, in AttributeRegistryRow row) => CanBound(_Node, registry, id, row);

            var kind = new EnumField(_Bound.Kind) { name = "kind" };
            kind.AddToClassList(USS_CLASS + "__kind");
            kind.SetEnabled(editable);
            Add(kind);

            if (_Bound.IsConstant)
            {
                var value = new FloatField { value = _Bound.ConstantValue, isDelayed = true, name = "constant" };
                value.AddToClassList(USS_CLASS + "__value");
                value.SetEnabled(editable);
                value.RegisterValueChangedCallback(e => Set(AttributeBound.FromConstant(e.newValue)));
                Add(value);
            }
            else if (_Bound.IsAttribute)
            {
                var pick = new Button { text = AttributeNodeElement.BoundText(_Bound), name = "attribute", tooltip = "Pick the attribute." };
                pick.AddToClassList(USS_CLASS + "__value");
                pick.AddToClassList("tag-inspector__chip");
                pick.SetEnabled(editable);
                pick.clicked += () => AttributeDropdown.Show(pick.worldBound, _Bound.Attribute, false, picked => Set(AttributeBound.FromAttribute(picked)), filter);
                Add(pick);
            }

            kind.RegisterValueChangedCallback(e =>
            {
                switch ((AttributeBoundKind)e.newValue)
                {
                    case AttributeBoundKind.None:
                        Set(AttributeBound.None);
                        break;

                    case AttributeBoundKind.Constant:
                        Set(AttributeBound.FromConstant(0f));
                        break;

                    case AttributeBoundKind.Attribute:
                        AttributeDropdown.Show(worldBound, GameplayAttribute.None, false, picked => Set(AttributeBound.FromAttribute(picked)), filter);
                        // Until something is picked, the bound stays as it is.
                        kind.SetValueWithoutNotify(_Bound.Kind);
                        break;
                }
            });
        }

        /// <summary>
        /// Attributes offered as a bound: not the attribute itself, not a meta attribute, and from its own registry or
        /// a package or generated registry.
        /// </summary>
        private static bool CanBound(AttributeTreeNode _Owner, RegistryGuid _Registry, RegistryGuid _Id, in AttributeRegistryRow _Row)
        {
            if (_Row.IsMeta) return false;
            if (_Registry == _Owner.RegistryId) return _Id != _Owner.Id;

            var registry = Kind.Snapshot.Find(_Registry);
            return registry != null && registry.Origin != RegistryOrigin.Main;
        }
    }
}
