using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.UIElements;

using LMK.Editor.Styles;
using LMK.Editor.UIElements;
using LMK.Editor.Utilities;
using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Inspector of a registry file: its name, kind, ID and tag count, whether code generation writes a class
    /// for it, and a button to open it in the Gameplay Tag Manager. The file itself is only edited there.
    /// </summary>
    [CustomEditor(typeof(TagRegistryImporter))]
    internal sealed class TagRegistryImporterEditor : ScriptedImporterEditor
    {
        private const string USS_PATH = "Packages/com.lmk.gameplaytags/Editor/Assets/TagRegistryInspectorUSS.uss";

        private const string USS_ROOT = "tag-registry";

        protected override bool needsApplyRevert => false;

        public override bool showImportedObject => false;

        private VisualElement content;



        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            LMKTheme.Apply(root);
            root.AddToClassList(USS_ROOT);

            var styleSheet = LMKAssetUtility.Load<StyleSheet>(USS_PATH);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            content = new VisualElement();
            root.Add(content);

            // Draws nothing here (no import settings), but every importer inspector must call it.
            root.Add(new IMGUIContainer(() => ApplyRevertGUI()));

            root.RegisterCallback<AttachToPanelEvent>(_ => TagRegistryService.OnSnapshotChanged += Build);
            root.RegisterCallback<DetachFromPanelEvent>(_ => TagRegistryService.OnSnapshotChanged -= Build);

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

            string path = ((AssetImporter)target).assetPath;
            var asset = AssetDatabase.LoadAssetAtPath<TagRegistryAsset>(path);
            var registry = TagFileExport.FindRegistry(path);

            if (asset != null && !asset.IsValid)
                content.Add(HelpBox($"The file is invalid:\n{string.Join("\n", asset.Errors)}", HelpBoxMessageType.Error));
            else if (asset != null && asset.ChecksumMismatch)
                content.Add(HelpBox("The file was edited outside the Gameplay Tag Manager (checksum mismatch).", HelpBoxMessageType.Warning));

            if (registry == null)
            {
                content.Add(HelpBox("This registry isn't loaded: it is invalid, or another registry file has the same registry ID.", HelpBoxMessageType.Warning));
                return;
            }

            var rows = new FieldRowGroup { RowSpacing = 2 };
            rows.AddToClassList(USS_ROOT + "__rows");
            rows.Add(new FieldRow("Name", Value(registry.Name)));
            rows.Add(new FieldRow("Kind", Value(KindText(registry))));
            rows.Add(new FieldRow("Registry ID", Value(registry.Id.ToString(), true)));
            rows.Add(new FieldRow("Tags", Value(registry.Count.ToString())));
            content.Add(rows);

            string className = TagCodeGen.ClassNameOf(registry.Id) ?? TagCodeGen.ROOT_CLASS;
            var generate = new Toggle("Generate C# class")
            {
                name = "generate-code-toggle",
                value = GameplayTagSettings.instance.IsCodeGenerated(registry.Id),
                tooltip = $"Write {className} with a constant per tag in \"{TagCodeGen.OutputPath}\". " +
                          "Stored in Project Settings, so it works for read-only package registries too.",
            };
            generate.AddToClassList(USS_ROOT + "__toggle");
            var registryId = registry.Id;
            generate.RegisterValueChangedCallback(e => GameplayTagSettings.instance.SetCodeGenerated(registryId, e.newValue));
            content.Add(generate);

            var actions = new VisualElement();
            actions.AddToClassList(USS_ROOT + "__actions");

            var open = new Button(() => GameplayTagManagerWindow.OpenRegistry(registryId)) { name = "open-manager-button", text = "Open Tag Manager" };
            open.AddToClassList(USS_ROOT + "__button");
            actions.Add(open);
            content.Add(actions);
        }



        private static Label Value(string _Text, bool _Selectable = false)
        {
            var label = new Label(_Text) { tooltip = _Text, selection = { isSelectable = _Selectable } };
            label.AddToClassList(USS_ROOT + "__value");
            return label;
        }

        private static string KindText(TagRegistrySnapshot.Registry _Registry)
        {
            string kind = _Registry.Kind switch
            {
                TagRegistryKind.Main => "Main",
                TagRegistryKind.Package => "Package",
                TagRegistryKind.Generated => "Generated",
                _ => _Registry.Kind.ToString(),
            };

            return _Registry.IsReadOnly ? kind + ", read-only" : kind;
        }

        private static HelpBox HelpBox(string _Text, HelpBoxMessageType _Type)
        {
            var helpBox = new HelpBox(_Text, _Type);
            helpBox.AddToClassList(USS_ROOT + "__help-box");
            return helpBox;
        }
    }
}
