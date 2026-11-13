using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.UIElements;
using LMK.Editor.Utilities;
using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Base of a kind's registry file Inspector: the file's problems, the registry's name, kind, ID and row count,
    /// whether code generation writes a class for it, and a button that opens it in the kind's editor. The file itself
    /// is only edited there.
    /// </summary>
    /// <remarks>
    /// A kind derives a non-generic class marked <c>[CustomEditor(typeof(&lt;its importer&gt;))]</c>, and may add rows
    /// with <see cref="AddRows"/>.
    /// </remarks>
    public abstract class RegistryImporterEditor<TRow, TAsset> : ScriptedImporterEditor
        where TRow : struct
        where TAsset : RegistryAsset<TRow>
    {
        public const string USS_ROOT = "registry-inspector";

        protected override bool needsApplyRevert => false;

        public override bool showImportedObject => false;

        private VisualElement content;

        /// <summary>
        /// The kind the registry file belongs to.
        /// </summary>
        protected abstract RegistryKindEditor<TRow, TAsset> Kind { get; }

        /// <summary>
        /// Add the kind's own rows after the core ones.
        /// </summary>
        protected virtual void AddRows(FieldRowGroup _Rows, RegistrySnapshot<TRow>.Registry _Registry) { }



        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            LMKTheme.Apply(root);
            root.AddToClassList(USS_ROOT);

            var styleSheet = LMKAssetUtility.Load<StyleSheet>(RegistryToolbar.USS_PATH);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            content = new VisualElement { name = "content" };
            root.Add(content);

            // Draws nothing here (no import settings), but every importer inspector must call it.
            root.Add(new IMGUIContainer(() => ApplyRevertGUI()));

            root.RegisterCallback<AttachToPanelEvent>(_ => Kind.SnapshotChanged += Build);
            root.RegisterCallback<DetachFromPanelEvent>(_ => Kind.SnapshotChanged -= Build);

            Build();
            return root;
        }

        private void Build()
        {
            if (content == null || target == null) return;

            content.Clear();

            if (targets.Length > 1)
            {
                content.Add(HelpBox("Select one registry file to see its details.", HelpBoxMessageType.Info));
                return;
            }

            var kind = Kind;
            string path = ((AssetImporter)target).assetPath;
            var asset = AssetDatabase.LoadAssetAtPath<TAsset>(path);
            RegistrySnapshot<TRow>.Registry registry = null;
            foreach (var loaded in kind.Snapshot.Registries)
                if (loaded.AssetPath == path)
                    registry = loaded;

            if (asset != null && !asset.IsValid)
                content.Add(HelpBox($"The file is invalid:\n{string.Join("\n", asset.Errors)}", HelpBoxMessageType.Error));
            else if (asset != null && asset.ChecksumMismatch)
                content.Add(HelpBox($"The file was edited outside the {kind.EditorName} (checksum mismatch).", HelpBoxMessageType.Warning));

            if (registry == null)
            {
                content.Add(HelpBox("This registry isn't loaded: it is invalid, its package isn't installed, or another registry file has the same registry ID.",
                                    HelpBoxMessageType.Warning));
                return;
            }

            var rows = new FieldRowGroup { RowSpacing = 2 };
            rows.AddToClassList(USS_ROOT + "__rows");
            rows.Add(new FieldRow("Name", Value(registry.Name)));
            rows.Add(new FieldRow("Kind", Value(OriginText(registry))));
            rows.Add(new FieldRow("Registry ID", Value(registry.Id.ToString(), true)));
            rows.Add(new FieldRow(char.ToUpperInvariant(kind.RowNoun[0]) + kind.RowNoun[1..] + "s", Value(registry.Count.ToString())));
            if (registry.Origin == RegistryOrigin.Generated)
                rows.Add(new FieldRow("Package", Value($"{registry.Data.package} {registry.Data.version}".Trim())));
            AddRows(rows, registry);
            content.Add(rows);

            if (kind.CodeStyle != null)
            {
                string className = RegistryCodeGen.ClassNameOf(kind, registry.Id) ?? kind.CodeStyle.RootClass;
                var generate = new Toggle("Generate C# class")
                {
                    name = "generate-code-toggle",
                    value = RegistrySettings.instance.IsCodeGenerated(kind.Id, registry.Id),
                    tooltip = $"Write {className} with a constant per {kind.RowNoun} in \"{kind.CodeOutputPath}\". " +
                              "Stored in Project Settings, so it works for read-only package registries too.",
                };
                generate.AddToClassList(USS_ROOT + "__toggle");
                var registryId = registry.Id;
                generate.RegisterValueChangedCallback(e => RegistrySettings.instance.SetCodeGenerated(kind, registryId, e.newValue));
                content.Add(generate);
            }

            if (kind.CanOpenRegistry)
            {
                var actions = new VisualElement();
                actions.AddToClassList(USS_ROOT + "__actions");

                var registryId = registry.Id;
                var open = new Button(() => kind.OpenRegistry(registryId)) { name = "open-editor-button", text = $"Open {kind.EditorName}" };
                open.AddToClassList(USS_ROOT + "__button");
                actions.Add(open);
                content.Add(actions);
            }
        }

        private static Label Value(string _Text, bool _Selectable = false)
        {
            var label = new Label(_Text) { tooltip = _Text, selection = { isSelectable = _Selectable } };
            label.AddToClassList(USS_ROOT + "__value");
            return label;
        }

        private static string OriginText(RegistrySnapshot<TRow>.Registry _Registry)
        {
            string origin = _Registry.Origin switch
            {
                RegistryOrigin.Main => "Main",
                RegistryOrigin.Package => "Package",
                RegistryOrigin.Generated => "Generated",
                _ => _Registry.Origin.ToString(),
            };

            return _Registry.IsReadOnly ? origin + ", read-only" : origin;
        }

        private static HelpBox HelpBox(string _Text, HelpBoxMessageType _Type)
        {
            var helpBox = new HelpBox(_Text, _Type);
            helpBox.AddToClassList(USS_ROOT + "__help-box");
            return helpBox;
        }
    }
}
