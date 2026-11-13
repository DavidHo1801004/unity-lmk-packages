using System.Collections.Generic;
using System.IO;

using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

using LMK.Editor.Registry;
using LMK.Runtime.Registry;

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
        Warn = MissingReferencePolicy.Warn,

        /// <summary>
        /// Fail the build with the list of references.
        /// </summary>
        Fail = MissingReferencePolicy.Fail,
    }



    /// <summary>
    /// The project settings of gameplay tags. Since 1.1.0 they are kept in the registry core's
    /// <see cref="RegistrySettings"/>, under the tag kind: <i>Project Settings → LMK → Registries</i> holds the shared
    /// ones, and <i>Project Settings → LMK → Gameplay Tags</i> the tag ones.
    /// </summary>
    /// <remarks>
    /// The 1.0.0 settings file, <c>ProjectSettings/LMKGameplayTagSettings.asset</c>, is read once when the editor loads
    /// and carried over. It is never written again, and can be deleted once the carry-over is committed.
    /// </remarks>
    internal sealed class GameplayTagSettings : ScriptableObject
    {
        public const string SETTINGS_PATH_1_0 = "ProjectSettings/LMKGameplayTagSettings.asset";

        private const string CARRY_OVER_SOURCE = "com.lmk.gameplaytags 1.0 settings";

        private static GameplayTagSettings facade;

        /// <summary>
        /// The tag settings.
        /// </summary>
        public static GameplayTagSettings instance
        {
            get
            {
                if (facade == null)
                {
                    facade = CreateInstance<GameplayTagSettings>();
                    facade.hideFlags = HideFlags.HideAndDontSave;
                }

                return facade;
            }
        }

        private static RegistrySettings Shared => RegistrySettings.instance;

        private static TagKind Kind => TagKind.Instance;



        #region Settings
        /// <summary>
        /// Whether code generation writes a class for a registry. On by default.
        /// </summary>
        public bool IsCodeGenerated(RegistryGuid _RegistryId) => Shared.IsCodeGenerated(TagKind.ID, _RegistryId);

        /// <summary>
        /// Turn code generation on or off for a registry, and generate the code again.
        /// </summary>
        /// <param name="_Save"> Save the settings and generate the code. Only tests turn it off. </param>
        public void SetCodeGenerated(RegistryGuid _RegistryId, bool _Generated, bool _Save = true)
        {
            Shared.SetCodeGenerated(Kind, _RegistryId, _Generated, _Save);
            if (_Save) GenerateCode();
        }

        /// <summary>
        /// The registry ID <paramref name="_Package"/> was last packed with, if it was packed before.
        /// </summary>
        public bool TryGetPublishedRegistry(string _Package, out RegistryGuid _RegistryId) => Shared.TryGetPublishedRegistry(_Package, TagKind.ID, out _RegistryId);

        /// <summary>
        /// Record the registry ID a package was packed with.
        /// </summary>
        /// <param name="_Save"> Save the settings. Only tests turn it off. </param>
        public void SetPublishedRegistry(string _Package, RegistryGuid _RegistryId, bool _Save = true)
            => Shared.SetPublishedRegistry(_Package, TagKind.ID, _RegistryId, _Save);

        /// <summary>
        /// Namespace of the generated <c>Tags</c> class. Empty for the global namespace.
        /// </summary>
        public string CodeNamespace
        {
            get => Shared.GetCodeNamespace(Kind);
            set
            {
                if ((value?.Trim() ?? string.Empty) == CodeNamespace) return;

                Shared.SetCodeNamespace(Kind, value);
                GenerateCode();
            }
        }

        /// <summary>
        /// What a build does with tag references that don't resolve: the tag kind's own policy, or the project's.
        /// </summary>
        public MissingTagBuildPolicy BuildPolicyOnMissingTags
        {
            get => (MissingTagBuildPolicy)Shared.GetPolicy(TagKind.ID);
            set => Shared.SetPolicy(TagKind.ID, (MissingReferencePolicy)value);
        }

        private static void GenerateCode()
        {
            var result = TagCodeGen.Generate();
            if (!result.Success)
                Debug.LogWarning($"Gameplay tag code was not generated: {result.Error}");
        }
        #endregion



        #region Carry-Over
        // The 1.0.0 settings, as their file holds them. Only read, to carry them over.
        [SerializeField] private MissingTagBuildPolicy buildPolicyOnMissingTags = MissingTagBuildPolicy.Warn;
        [SerializeField] private string codeNamespace = string.Empty;
        [SerializeField] private List<string> codeGenDisabledRegistries = new();
        [SerializeField] private List<string> publishedPackageRegistries = new();

        [InitializeOnLoadMethod]
        private static void CarryOverOnLoad()
        {
            CarryOver(SETTINGS_PATH_1_0, true);
        }

        /// <summary>
        /// Read a 1.0.0 settings file into the registry settings, once: the build policy, the code namespace, the
        /// registries without code and the published registry IDs. The 1.0.0 lock provider was always None.
        /// </summary>
        /// <param name="_Save"> Save the registry settings and record the carry-over. Only tests turn it off. </param>
        /// <returns> Whether anything was carried over. </returns>
        internal static bool CarryOver(string _Path, bool _Save)
        {
            if (Shared.IsCarriedOver(CARRY_OVER_SOURCE) || !File.Exists(_Path)) return false;

            var old = InternalEditorUtility.LoadSerializedFileAndForget(_Path);
            var settings = old != null && old.Length > 0 ? old[0] as GameplayTagSettings : null;
            if (settings == null)
            {
                Debug.LogWarning($"The gameplay tag settings \"{_Path}\" could not be read. Set them again in Project Settings → LMK.");
                return false;
            }

            try
            {
                if (settings.buildPolicyOnMissingTags != MissingTagBuildPolicy.Warn)
                    Shared.SetPolicy(TagKind.ID, (MissingReferencePolicy)settings.buildPolicyOnMissingTags, false);

                if (!string.IsNullOrWhiteSpace(settings.codeNamespace))
                    Shared.SetCodeNamespace(Kind, settings.codeNamespace, false);

                foreach (string registry in settings.codeGenDisabledRegistries ?? new List<string>())
                    if (RegistryGuid.TryParse(registry, out var id))
                        Shared.SetCodeGenerated(Kind, id, false, false);

                foreach (string entry in settings.publishedPackageRegistries ?? new List<string>())
                {
                    int separator = entry.LastIndexOf('=');
                    if (separator > 0 && RegistryGuid.TryParse(entry[(separator + 1)..], out var id))
                        Shared.SetPublishedRegistry(entry[..separator], TagKind.ID, id, false);
                }

                if (_Save)
                {
                    Shared.MarkCarriedOver(CARRY_OVER_SOURCE);
                    Debug.Log($"The gameplay tag settings of \"{_Path}\" were carried over to the registry settings. That file is no longer used.");
                }

                return true;
            }
            finally
            {
                DestroyImmediate(settings);
            }
        }
        #endregion



        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Project/LMK/Gameplay Tags", SettingsScope.Project)
            {
                label = "Gameplay Tags",
                keywords = new HashSet<string> { "gameplay", "tag", "registry", "build", "code", "namespace" },
                guiHandler = _ =>
                {
                    var settings = instance;

                    EditorGUI.BeginChangeCheck();
                    var policy = (MissingTagBuildPolicy)EditorGUILayout.EnumPopup(
                        new GUIContent("Missing Tags In Builds", "What a build does with tag references that don't resolve."),
                        settings.BuildPolicyOnMissingTags);

                    // Delayed: the code is regenerated when the field is committed, not on every keystroke.
                    string ns = EditorGUILayout.DelayedTextField(
                        new GUIContent("Code Namespace", "Namespace of the generated Tags class. Empty for the global namespace."),
                        settings.CodeNamespace);

                    if (EditorGUI.EndChangeCheck())
                    {
                        if (policy != settings.BuildPolicyOnMissingTags) settings.BuildPolicyOnMissingTags = policy;
                        settings.CodeNamespace = ns;
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Main registry", Kind.MainRegistryPath);
                    EditorGUILayout.LabelField("Baked database", TagBuildProcessor.DATABASE_PATH);
                    EditorGUILayout.LabelField("Generated code", TagCodeGen.DEFAULT_OUTPUT_PATH);

                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox("The lock provider and the settings shared by every registry kind are in Project Settings → LMK → Registries.", MessageType.Info);
                },
            };
        }
    }
}
