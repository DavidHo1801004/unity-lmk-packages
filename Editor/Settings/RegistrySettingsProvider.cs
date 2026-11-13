using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEngine;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// <i>Project Settings → LMK → Registries</i>: the settings shared by every registry kind. Kind-specific settings
    /// live on the kind's own page.
    /// </summary>
    internal static class RegistrySettingsProvider
    {
        public const string PATH = "Project/LMK/Registries";

        private static readonly string[] POLICY_LABELS = { "Project Default", "Warn", "Fail" };

        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider(PATH, SettingsScope.Project)
            {
                label = "Registries",
                keywords = new HashSet<string> { "registry", "lock", "build", "missing", "references", "code", "namespace", "assembly" },
                guiHandler = _ => Draw(),
            };
        }

        private static void Draw()
        {
            var settings = RegistrySettings.instance;

            EditorGUILayout.LabelField("Lock", EditorStyles.boldLabel);
            var types = RegistrySettings.LockProviderTypes;
            int current = Math.Max(0, types.ToList().IndexOf(settings.LockProviderType));
            int chosen = EditorGUILayout.Popup(
                new GUIContent("Lock Provider", "Version control lock that every registry edit, save, generation and code write asks first."),
                current, types.Select(e => new GUIContent(ProviderName(e))).ToArray());
            if (chosen != current)
                settings.SetLockProvider(types[chosen]);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Builds", EditorStyles.boldLabel);
            var policy = (MissingReferencePolicy)EditorGUILayout.EnumPopup(
                new GUIContent("Missing References In Builds", "What a build does with references that don't resolve, for every kind without its own policy."),
                settings.MissingReferencesInBuilds);
            if (policy != settings.MissingReferencesInBuilds)
                settings.MissingReferencesInBuilds = policy;

            foreach (var kind in RegistryKinds.All)
            {
                int index = settings.HasOwnPolicy(kind.Id) ? (int)settings.GetPolicy(kind.Id) + 1 : 0;
                int picked = EditorGUILayout.Popup(new GUIContent($"    {kind.DisplayName}", $"The policy of {kind.DisplayName} references."), index,
                                                   POLICY_LABELS.Select(e => new GUIContent(e)).ToArray());
                if (picked != index)
                    settings.SetPolicy(kind.Id, picked == 0 ? null : (MissingReferencePolicy)(picked - 1));
            }

            foreach (var kind in RegistryKinds.All)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(kind.DisplayName, EditorStyles.boldLabel);

                if (kind.CodeStyle != null)
                {
                    // Delayed: the code is regenerated when the field is committed, not on every keystroke.
                    string codeNamespace = EditorGUILayout.DelayedTextField(
                        new GUIContent("Code Namespace", $"Namespace of the generated {kind.CodeStyle.RootClass} class. Empty for the global namespace."),
                        settings.GetCodeNamespace(kind));
                    if (codeNamespace != settings.GetCodeNamespace(kind))
                        settings.SetCodeNamespace(kind, codeNamespace);

                    string assembly = EditorGUILayout.DelayedTextField(
                        new GUIContent("Code Assembly", "Assembly the generated code is compiled into, through an assembly definition written next to it. " +
                                                        "Empty for the project's default assembly (Assembly-CSharp), which code in assembly definitions can't use."),
                        settings.GetCodeAssembly(kind));
                    if (assembly != settings.GetCodeAssembly(kind))
                        settings.SetCodeAssembly(kind, assembly);
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Main Registry", kind.MainRegistryPath);
                    EditorGUILayout.TextField("Baked Database Folder", kind.DatabaseFolder);
                    if (kind.CodeStyle != null)
                        EditorGUILayout.TextField("Generated Code", kind.CodeOutputPath);
                }
            }

            if (RegistryKinds.All.Count == 0)
                EditorGUILayout.HelpBox("No registry kind is registered. Install a package that adds one, such as LMK Gameplay Tags.", MessageType.Info);
        }

        private static string ProviderName(Type _Type)
        {
            try
            {
                return ((RegistryLockProvider)Activator.CreateInstance(_Type)).Name;
            }
            catch (Exception)
            {
                return _Type.Name;
            }
        }
    }
}
