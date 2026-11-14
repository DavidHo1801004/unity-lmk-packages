using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Registry;
using LMK.Editor.Styles;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

using ToolbarButton = LMK.Editor.UIElements.ToolbarButton;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Shows every loaded attribute registry and edits the main one: a toolbar (Save, Discard, registry, search, Add),
    /// a flat list grouped by name prefix, and an editor for the selected attribute's name and metadata. Edits are
    /// staged until saved, and Unity asks to save or discard them when the window closes (<see cref="RegistryWindow"/>).
    /// Offers to create the main registry when the project has none.
    /// </summary>
    internal sealed class AttributeManagerWindow : RegistryWindow
    {
        public const string TITLE = "Gameplay Attributes";
        private const string MENU = "Tools/LMK/Ability System/";
        private const string USS_PATH = "Packages/com.lmk.abilitysystem/Editor/Assets/AttributeManagerUSS.uss";
        private const string USS_ROOT = "attribute-manager";
        private const string NO_GROUP = "(no group)";

        // Kept across domain reloads.
        [SerializeField] private string selectedRegistry = string.Empty;
        [SerializeField] private string selectedAttribute = string.Empty;
        [SerializeField] private string search = string.Empty;

        private RegistryToolbar toolbar;
        private PopupField<string> registryPopup;
        private VisualElement createPanel;
        private TwoPaneSplitView content;
        private ScrollView list;
        private VisualElement details;
        private HelpBox message;

        private List<RegistrySnapshot<AttributeRegistryRow>.Registry> registries = new();

        private static AttributeKind Kind => AttributeKind.Instance;



        [MenuItem(MENU + "Attribute Manager", priority = 0)]
        public static AttributeManagerWindow Open() => GetWindow<AttributeManagerWindow>(TITLE);

        /// <summary>
        /// Open the window on a registry.
        /// </summary>
        public static void OpenRegistry(RegistryGuid _RegistryId)
        {
            var window = Open();
            window.selectedRegistry = _RegistryId.ToString();
            window.selectedAttribute = string.Empty;
            window.Rebuild();
        }

        /// <summary>
        /// Open the window with an attribute selected.
        /// </summary>
        public static void OpenAttribute(RegistryGuid _RegistryId, RegistryGuid _Id)
        {
            var window = Open();
            window.selectedRegistry = _RegistryId.ToString();
            window.selectedAttribute = _Id.ToString();
            window.Rebuild();
        }

        protected override void OnEnable()
        {
            Kind.SnapshotChanged += Rebuild;
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            Kind.SnapshotChanged -= Rebuild;
            base.OnDisable();
        }

        protected override void OnStateChanged() => toolbar?.Refresh();



        #region Layout
        private void CreateGUI()
        {
            var root = rootVisualElement;
            LMKTheme.Apply(root);
            root.AddToClassList(USS_ROOT);

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(USS_PATH);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            toolbar = new RegistryToolbar();
            root.Add(toolbar);

            registryPopup = new PopupField<string>(new List<string> { string.Empty }, 0, FormatRegistry, FormatRegistry)
            {
                name = "registry-popup",
                tooltip = "The registry to show. Only the main registry can be edited.",
            };
            registryPopup.AddToClassList(USS_ROOT + "__registry");
            registryPopup.RegisterValueChangedCallback(e =>
            {
                selectedRegistry = e.newValue;
                selectedAttribute = string.Empty;
                Rebuild();
            });
            toolbar.AddControl(registryPopup);

            var searchField = new ToolbarSearchField { name = "search-field", value = search };
            searchField.AddToClassList(USS_ROOT + "__search");
            searchField.RegisterValueChangedCallback(e =>
            {
                search = e.newValue ?? string.Empty;
                RebuildList();
            });
            toolbar.AddControl(searchField);

            var addButton = new ToolbarButton(false, ShowAdd) { name = "add-button", text = "Add", tooltip = "Add an attribute to the main registry." };
            toolbar.AddControl(addButton);

            message = new HelpBox(string.Empty, HelpBoxMessageType.Error) { name = "message" };
            message.style.display = DisplayStyle.None;
            message.RegisterCallback<PointerDownEvent>(_ => message.style.display = DisplayStyle.None);
            root.Add(message);

            createPanel = new VisualElement { name = "create-panel" };
            createPanel.AddToClassList(USS_ROOT + "__create");
            createPanel.Add(new Label("This project has no main attribute registry yet.") { name = "create-label" });
            createPanel.Add(new Button(CreateMainRegistry) { name = "create-registry-button", text = "Create Main Attribute Registry" });
            root.Add(createPanel);

            content = new TwoPaneSplitView(0, 260f, TwoPaneSplitViewOrientation.Horizontal) { name = "content" };
            content.style.flexGrow = 1;

            list = new ScrollView(ScrollViewMode.Vertical) { name = "attribute-list" };
            list.AddToClassList(USS_ROOT + "__list");
            content.Add(list);

            var detailsScroll = new ScrollView(ScrollViewMode.Vertical) { name = "details-scroll" };
            details = new VisualElement { name = "details" };
            details.AddToClassList(USS_ROOT + "__details");
            detailsScroll.Add(details);
            content.Add(detailsScroll);
            root.Add(content);

            Rebuild();
        }

        private void Rebuild()
        {
            if (registryPopup == null) return;

            var snapshot = Kind.Snapshot;
            registries = snapshot.Registries
                .OrderBy(e => e != snapshot.Main)
                .ThenBy(e => e.Name, StringComparer.Ordinal)
                .ToList();

            bool hasMain = snapshot.Main != null;
            createPanel.style.display = hasMain ? DisplayStyle.None : DisplayStyle.Flex;
            content.style.display = registries.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            var choices = registries.Select(e => e.Id.ToString()).ToList();
            if (choices.Count == 0) choices.Add(string.Empty);
            registryPopup.choices = choices;

            if (!choices.Contains(selectedRegistry))
                selectedRegistry = choices[0];
            registryPopup.SetValueWithoutNotify(selectedRegistry);

            toolbar.Refresh();
            RebuildList();
        }

        private string FormatRegistry(string _Id)
        {
            var registry = registries.FirstOrDefault(e => e.Id.ToString() == _Id);
            if (registry == null) return "No registry";

            return registry.IsReadOnly ? $"{registry.Name} (read-only)" : registry.Name;
        }

        private RegistrySnapshot<AttributeRegistryRow>.Registry SelectedRegistry
            => registries.FirstOrDefault(e => e.Id.ToString() == selectedRegistry);
        #endregion



        #region List
        private void RebuildList()
        {
            if (list == null) return;

            list.Clear();
            var registry = SelectedRegistry;
            if (registry == null)
            {
                RebuildDetails();
                return;
            }

            var rows = registry.Ids
                .Select(e => (Id: e, Name: registry.GetName(e) ?? string.Empty))
                .Where(e => string.IsNullOrEmpty(search) || e.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(e => e.Name, StringComparer.Ordinal)
                .ToList();

            if (rows.Count == 0)
                list.Add(new Label(registry.Count == 0 ? "No attributes yet. Add one with the Add button." : "No attribute matches the search.")
                {
                    name = "empty-label",
                });

            foreach (var group in rows.GroupBy(e => AttributeName.GetGroup(e.Name)))
            {
                var foldout = new Foldout { text = string.IsNullOrEmpty(group.Key) ? NO_GROUP : group.Key, value = true };
                foldout.AddToClassList(USS_ROOT + "__group");

                foreach (var (id, name) in group)
                    foldout.Add(CreateListItem(registry, id, name));

                list.Add(foldout);
            }

            RebuildDetails();
        }

        private VisualElement CreateListItem(RegistrySnapshot<AttributeRegistryRow>.Registry _Registry, RegistryGuid _Id, string _Name)
        {
            var item = new VisualElement { name = "item-" + _Id };
            item.AddToClassList(USS_ROOT + "__item");
            item.EnableInClassList(USS_ROOT + "__item--selected", _Id.ToString() == selectedAttribute);

            item.Add(new Label(AttributeName.GetShortName(_Name)) { tooltip = _Name });

            if (_Registry.TryGetRow(_Id, out var row))
            {
                var summary = new Label(Summary(row));
                summary.AddToClassList(USS_ROOT + "__summary");
                item.Add(summary);
            }

            item.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0)
                {
                    selectedAttribute = _Id.ToString();
                    RebuildList();
                }
            });

            item.AddManipulator(new ContextualMenuManipulator(e =>
            {
                bool editable = !_Registry.IsReadOnly;
                e.menu.AppendAction("Delete…", _ => DeleteWithDialog(_Id), editable ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                e.menu.AppendAction("Find References…", _ => RegistryReferenceWindow.ShowReferences(Kind, (_Registry.Id, _Id)));
                e.menu.AppendAction("Replace References…", _ => RegistryReferenceWindow.ShowReplace(Kind, (_Registry.Id, _Id)));
                e.menu.AppendAction("Copy Attribute ID", _ => EditorGUIUtility.systemCopyBuffer = _Id.ToString());
            }));

            return item;
        }

        private static string Summary(in AttributeRegistryRow _Row)
        {
            var parts = new List<string> { AttributeKind.FormatNumber(_Row.defaultValue) };
            if (!_Row.min.IsNone || !_Row.max.IsNone)
                parts.Add($"[{BoundText(_Row.min)}, {BoundText(_Row.max)}]");
            if ((_Row.flags & AttributeFlags.Meta) != 0) parts.Add("meta");
            if ((_Row.flags & AttributeFlags.Integer) != 0) parts.Add("integer");

            return string.Join("  ", parts);
        }

        private static string BoundText(in AttributeBound _Bound) => _Bound.Kind switch
        {
            AttributeBoundKind.Constant => AttributeKind.FormatNumber(_Bound.ConstantValue),
            AttributeBoundKind.Attribute => Kind.GetRowName(_Bound.RegistryId, _Bound.AttributeId) ?? "(deleted)",
            _ => "–",
        };
        #endregion



        #region Details
        private void RebuildDetails()
        {
            if (details == null) return;

            details.Clear();
            var registry = SelectedRegistry;
            if (registry == null || !RegistryGuid.TryParse(selectedAttribute, out var id) || !registry.TryGetRow(id, out var row))
            {
                details.Add(new Label("Select an attribute to edit it.") { name = "no-selection-label" });
                return;
            }

            bool editable = !registry.IsReadOnly;
            string name = registry.GetName(id) ?? row.name;

            var title = new Label(name) { name = "title" };
            title.AddToClassList(USS_ROOT + "__title");
            details.Add(title);

            if (!editable)
                details.Add(new HelpBox($"\"{registry.Name}\" is a {(registry.Origin == RegistryOrigin.Main ? "main" : "package")} registry and is read-only here.",
                                        HelpBoxMessageType.Info));

            var nameField = new TextField("Name") { value = name, isDelayed = true, name = "name-field" };
            nameField.RegisterValueChangedCallback(e => Report(AttributeOperations.Rename(id, e.newValue?.Trim())));
            details.Add(nameField);

            var defaultField = new FloatField("Default") { value = row.defaultValue, isDelayed = true, name = "default-field",
                                                           tooltip = "The base value a unit starts with when its set definition gives none." };
            defaultField.RegisterValueChangedCallback(e => Edit(id, row, (ref AttributeMetadata m) => m.defaultValue = e.newValue));
            details.Add(defaultField);

            details.Add(CreateBoundEditor("Min", registry, id, row, row.min, (ref AttributeMetadata m, AttributeBound b) => m.min = b));
            details.Add(CreateBoundEditor("Max", registry, id, row, row.max, (ref AttributeMetadata m, AttributeBound b) => m.max = b));

            var onMaxChange = new EnumField("On Max Change", row.onMaxChange)
            {
                name = "on-max-change-field",
                tooltip = "What happens to this attribute when the attribute that is its max changes: clamp to it, or keep the ratio.",
            };
            onMaxChange.SetEnabled(editable && row.max.IsAttribute);
            onMaxChange.RegisterValueChangedCallback(e => Edit(id, row, (ref AttributeMetadata m) => m.onMaxChange = (AttributeMaxChange)e.newValue));
            details.Add(onMaxChange);

            var meta = new Toggle("Meta") { value = (row.flags & AttributeFlags.Meta) != 0, name = "meta-toggle",
                                            tooltip = "A scratch value, e.g. incoming damage: no bounds, no change events, reset after each execution." };
            meta.RegisterValueChangedCallback(e => Edit(id, row, (ref AttributeMetadata m) =>
                m.flags = e.newValue ? m.flags | AttributeFlags.Meta : m.flags & ~AttributeFlags.Meta));
            details.Add(meta);

            var integer = new Toggle("Integer") { value = (row.flags & AttributeFlags.Integer) != 0, name = "integer-toggle",
                                                  tooltip = "Rounded to the nearest integer after evaluation." };
            integer.RegisterValueChangedCallback(e => Edit(id, row, (ref AttributeMetadata m) =>
                m.flags = e.newValue ? m.flags | AttributeFlags.Integer : m.flags & ~AttributeFlags.Integer));
            details.Add(integer);

            var description = new TextField("Description") { value = row.description, multiline = true, isDelayed = true, name = "description-field" };
            description.AddToClassList(USS_ROOT + "__description");
            description.RegisterValueChangedCallback(e => Edit(id, row, (ref AttributeMetadata m) => m.description = e.newValue));
            details.Add(description);

            foreach (var field in new VisualElement[] { nameField, defaultField, meta, integer, description })
                field.SetEnabled(editable);

            var dependents = AttributeOperations.FindDependents(registry.Id, id);
            if (dependents.Count > 0)
            {
                string names = string.Join(", ", dependents.Select(e => Kind.GetRowName(e.Registry, e.Id)));
                details.Add(new Label($"Bound of: {names}") { name = "dependents-label" });
            }

            var info = new Label($"Registry: {registry.Name}\nAttribute ID: {id}") { name = "id-label", selection = { isSelectable = true } };
            info.AddToClassList(USS_ROOT + "__info");
            details.Add(info);

            var actions = new VisualElement { name = "actions" };
            actions.AddToClassList(USS_ROOT + "__actions");
            actions.Add(new Button(() => RegistryReferenceWindow.ShowReferences(Kind, (registry.Id, id))) { text = "Find References" });
            var delete = new Button(() => DeleteWithDialog(id)) { text = "Delete…", name = "delete-button" };
            delete.SetEnabled(editable);
            actions.Add(delete);
            details.Add(actions);
        }

        private VisualElement CreateBoundEditor(string _Label, RegistrySnapshot<AttributeRegistryRow>.Registry _Registry, RegistryGuid _Id,
                                                AttributeRegistryRow _Row, AttributeBound _Bound, SetBound _Set)
        {
            bool editable = !_Registry.IsReadOnly;
            var field = new VisualElement { name = _Label.ToLowerInvariant() + "-bound" };
            field.AddToClassList(USS_ROOT + "__bound");

            var kind = new EnumField(_Label, _Bound.Kind) { name = "kind" };
            kind.AddToClassList(USS_ROOT + "__bound-kind");
            kind.SetEnabled(editable);
            field.Add(kind);

            void Set(AttributeBound _New) => Edit(_Id, _Row, (ref AttributeMetadata m) => _Set(ref m, _New));

            if (_Bound.IsConstant)
            {
                var value = new FloatField { value = _Bound.ConstantValue, isDelayed = true, name = "constant" };
                value.AddToClassList(USS_ROOT + "__bound-value");
                value.SetEnabled(editable);
                value.RegisterValueChangedCallback(e => Set(AttributeBound.FromConstant(e.newValue)));
                field.Add(value);
            }
            else if (_Bound.IsAttribute)
            {
                string text = Kind.GetRowName(_Bound.RegistryId, _Bound.AttributeId) ?? "(deleted)";
                var pick = new Button { text = text, name = "attribute" };
                pick.AddToClassList(USS_ROOT + "__bound-value");
                pick.SetEnabled(editable);
                pick.clicked += () => AttributePicker.Show(pick.worldBound, _Bound.Attribute, false,
                    picked => Set(AttributeBound.FromAttribute(picked)),
                    (RegistryGuid registry, RegistryGuid id, in AttributeRegistryRow row) => CanBound(_Registry, _Id, registry, id, row));
                field.Add(pick);
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
                        AttributePicker.Show(field.worldBound, GameplayAttribute.None, false,
                            picked => Set(AttributeBound.FromAttribute(picked)),
                            (RegistryGuid registry, RegistryGuid id, in AttributeRegistryRow row) => CanBound(_Registry, _Id, registry, id, row));
                        // Until something is picked, the bound stays as it is.
                        kind.SetValueWithoutNotify(_Bound.Kind);
                        break;
                }
            });

            return field;
        }

        /// <summary>
        /// Attributes offered as a bound: not the attribute itself, not a meta attribute, and from this registry or a
        /// package or generated registry.
        /// </summary>
        private static bool CanBound(RegistrySnapshot<AttributeRegistryRow>.Registry _Owner, RegistryGuid _Self,
                                     RegistryGuid _Registry, RegistryGuid _Id, in AttributeRegistryRow _Row)
        {
            if (_Row.IsMeta) return false;
            if (_Registry == _Owner.Id) return _Id != _Self;

            var registry = Kind.Snapshot.Find(_Registry);
            return registry != null && registry.Origin != RegistryOrigin.Main;
        }
        #endregion



        #region Commands
        private delegate void ModifyMetadata(ref AttributeMetadata _Metadata);

        private delegate void SetBound(ref AttributeMetadata _Metadata, AttributeBound _Bound);

        /// <summary>
        /// Stage the row's metadata with one change.
        /// </summary>
        private void Edit(RegistryGuid _Id, in AttributeRegistryRow _Row, ModifyMetadata _Change)
        {
            var metadata = AttributeMetadata.Of(_Row);
            _Change(ref metadata);
            Report(AttributeOperations.EditMetadata(_Id, metadata));
        }

        private void ShowAdd()
        {
            if (Kind.Snapshot.Main == null)
            {
                Report(RegistryResult.Fail("Create the main attribute registry first."));
                return;
            }

            var registry = SelectedRegistry;
            string group = RegistryGuid.TryParse(selectedAttribute, out var id) && registry != null
                ? AttributeName.GetGroup(registry.GetName(id))
                : string.Empty;

            AddAttributePopup.Show(toolbar.worldBound, string.IsNullOrEmpty(group) ? "NewAttribute" : group + ".NewAttribute", name =>
            {
                // A refused name is shown in the popup, which stays open.
                var result = AttributeOperations.Add(name);
                if (result.Success)
                {
                    selectedRegistry = Kind.Snapshot.Main.Id.ToString();
                    selectedAttribute = result.Id.ToString();
                    Rebuild();
                }

                return result;
            });
        }

        private void DeleteWithDialog(RegistryGuid _Id)
        {
            var plan = AttributeOperations.PlanDelete(_Id, true);
            if (plan == null) return;

            string ok = plan.ReferencingAssets.Count > 0 || plan.Dependents.Count > 0 ? "Delete Anyway" : "Delete";
            if (!EditorUtility.DisplayDialog("Delete Gameplay Attribute", plan.Message, ok, "Cancel")) return;

            if (Report(AttributeOperations.Delete(_Id)))
                selectedAttribute = string.Empty;
        }

        private void CreateMainRegistry()
        {
            var result = AttributeOperations.CreateMainRegistry();
            if (Report(result))
                selectedRegistry = result.Id.ToString();

            Rebuild();
        }

        /// <summary>
        /// Show a failed result's error above the content. A refused edit leaves the staged state as it was, so the
        /// fields are rebuilt from it.
        /// </summary>
        private bool Report(RegistryResult _Result)
        {
            if (message != null)
            {
                message.text = _Result.Success ? string.Empty : _Result.Error;
                message.style.display = _Result.Success ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (!_Result.Success)
                RebuildDetails();

            return _Result.Success;
        }
        #endregion
    }
}
