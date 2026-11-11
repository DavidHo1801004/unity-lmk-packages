using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// What a build does when it finds tag references that don't resolve.
    /// </summary>
    internal enum MissingTagBuildPolicy
    {
        /// <summary>
        /// Log a warning per reference. Built scenes get None in their place.
        /// </summary>
        Warn,

        /// <summary>
        /// Fail the build with the list of references.
        /// </summary>
        Fail,
    }

    /// <summary>
    /// Which version control lock guards tag edits.
    /// </summary>
    internal enum TagLockProviderKind
    {
        None,
    }



    /// <summary>
    /// Project settings of the gameplay tag system, in <c>ProjectSettings/LMKGameplayTagSettings.asset</c>.
    /// </summary>
    [FilePath("ProjectSettings/LMKGameplayTagSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class GameplayTagSettings : ScriptableSingleton<GameplayTagSettings>
    {
        [SerializeField]
        private MissingTagBuildPolicy buildPolicyOnMissingTags = MissingTagBuildPolicy.Warn;

        [SerializeField]
        private TagLockProviderKind lockProvider = TagLockProviderKind.None;

        [SerializeField]
        private string codeNamespace = string.Empty;

        // IDs of the registries that get no generated class. Stored here rather than in the registry files, so
        // read-only package registries can be turned off too.
        [SerializeField]
        private List<string> codeGenDisabledRegistries = new();

        /// <summary>
        /// Whether code generation writes a class for a registry. On by default.
        /// </summary>
        public bool IsCodeGenerated(TagGuid _RegistryId)
            => codeGenDisabledRegistries == null || !codeGenDisabledRegistries.Contains(_RegistryId.ToString());

        /// <summary>
        /// Turn code generation on or off for a registry, and generate the code again.
        /// </summary>
        /// <param name="_Save"> Save the settings and generate the code. Only tests turn it off. </param>
        public void SetCodeGenerated(TagGuid _RegistryId, bool _Generated, bool _Save = true)
        {
            if (IsCodeGenerated(_RegistryId) == _Generated) return;

            codeGenDisabledRegistries ??= new List<string>();
            string id = _RegistryId.ToString();
            if (_Generated) codeGenDisabledRegistries.Remove(id);
            else codeGenDisabledRegistries.Add(id);
            if (!_Save) return;

            Save(true);

            var result = TagCodeGen.Generate();
            if (!result.Success)
                Debug.LogWarning($"Gameplay tag code was not generated: {result.Error}");
        }

        /// <summary>
        /// The registry ID each package you author was last packed with, as "name=registry ID". Pack validation
        /// refuses a changed ID. Committed, so the whole team packs against the same record.
        /// </summary>
        [SerializeField]
        private List<string> publishedPackageRegistries = new();

        /// <summary>
        /// The registry ID <paramref name="_Package"/> was last packed with, if it was packed before.
        /// </summary>
        public bool TryGetPublishedRegistry(string _Package, out TagGuid _RegistryId)
        {
            _RegistryId = TagGuid.Zero;
            string prefix = _Package + "=";

            string entry = publishedPackageRegistries?.Find(e => e.StartsWith(prefix, System.StringComparison.Ordinal));
            return entry != null && TagGuid.TryParse(entry[prefix.Length..], out _RegistryId);
        }

        /// <summary>
        /// Record the registry ID a package was packed with.
        /// </summary>
        /// <param name="_Save"> Save the settings. Only tests turn it off. </param>
        public void SetPublishedRegistry(string _Package, TagGuid _RegistryId, bool _Save = true)
        {
            publishedPackageRegistries ??= new List<string>();
            publishedPackageRegistries.RemoveAll(e => e.StartsWith(_Package + "=", System.StringComparison.Ordinal));
            if (!_RegistryId.IsZero)
                publishedPackageRegistries.Add($"{_Package}={_RegistryId}");

            if (_Save)
                Save(true);
        }

        /// <summary>
        /// Namespace of the generated <c>Tags</c> class. Empty for the global namespace.
        /// </summary>
        public string CodeNamespace
        {
            get => codeNamespace ?? string.Empty;
            set
            {
                value = value?.Trim() ?? string.Empty;
                if (value == CodeNamespace) return;

                codeNamespace = value;
                Save(true);

                var result = TagCodeGen.Generate();
                if (!result.Success)
                    Debug.LogWarning($"Gameplay tag code was not generated: {result.Error}");
            }
        }

        public MissingTagBuildPolicy BuildPolicyOnMissingTags
        {
            get => buildPolicyOnMissingTags;
            set
            {
                buildPolicyOnMissingTags = value;
                Save(true);
            }
        }

        public TagLockProviderKind LockProvider
        {
            get => lockProvider;
            set
            {
                lockProvider = value;
                ApplyLockProvider();
                Save(true);
            }
        }

        private void OnEnable() => ApplyLockProvider();

        private void ApplyLockProvider()
        {
            TagLockProvider.Current = lockProvider switch
            {
                _ => new NoneLockProvider(),
            };
        }



        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Project/LMK/Gameplay Tags", SettingsScope.Project)
            {
                label = "Gameplay Tags",
                keywords = new HashSet<string> { "gameplay", "tag", "registry", "build", "lock", "code", "namespace" },
                guiHandler = _ =>
                {
                    var settings = instance;

                    EditorGUI.BeginChangeCheck();
                    var policy = (MissingTagBuildPolicy)EditorGUILayout.EnumPopup(
                        new GUIContent("Missing Tags In Builds", "What a build does with tag references that don't resolve."),
                        settings.BuildPolicyOnMissingTags);
                    var provider = (TagLockProviderKind)EditorGUILayout.EnumPopup(
                        new GUIContent("Lock Provider", "Version control lock that guards edits of the main registry."),
                        settings.LockProvider);

                    // Delayed: the code is regenerated when the field is committed, not on every keystroke.
                    string codeNamespace = EditorGUILayout.DelayedTextField(
                        new GUIContent("Code Namespace", "Namespace of the generated Tags class. Empty for the global namespace."),
                        settings.CodeNamespace);

                    if (EditorGUI.EndChangeCheck())
                    {
                        settings.BuildPolicyOnMissingTags = policy;
                        settings.LockProvider = provider;
                        settings.CodeNamespace = codeNamespace;
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Main registry", TagRegistryService.MAIN_REGISTRY_PATH);
                    EditorGUILayout.LabelField("Baked database", TagBuildProcessor.DATABASE_PATH);
                    EditorGUILayout.LabelField("Generated code", TagCodeGen.DEFAULT_OUTPUT_PATH);
                },
            };
        }
    }
}
