using UnityEditor;
using UnityEngine;

using LMK.Editor.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Attribute commands in the Tools menu. Save, Discard, Find References and package registries are the registry
    /// core's commands, under Tools > LMK > Registries, and cover every kind.
    /// </summary>
    internal static class AttributeCommands
    {
        private const string MENU = "Tools/LMK/Ability System/";

        [MenuItem(MENU + "Generate Attribute Code", priority = 40)]
        public static void GenerateCode()
        {
            var result = AttributeCodeGen.Generate();
            if (!result.Success)
                EditorUtility.DisplayDialog("Generate Gameplay Attribute Code", result.Error, "OK");
        }

        [MenuItem(MENU + "Prune Unused Attribute Code Aliases", priority = 41)]
        public static void PruneCodeAliases()
        {
            var result = AttributeCodeGen.PruneAliases(out var pruned);
            if (!result.Success)
            {
                EditorUtility.DisplayDialog("Prune Unused Attribute Code Aliases", result.Error, "OK");
                return;
            }

            Debug.Log(pruned.Count == 0
                ? "Gameplay attribute code: no unused aliases."
                : $"Gameplay attribute code: removed {pruned.Count} unused alias(es): {string.Join(", ", pruned)}.");
        }

        [MenuItem(MENU + "Relink Missing Attribute References...", priority = 60)]
        public static void RelinkMissingReferences() => RegistryReferenceWindow.ShowRelink(AttributeKind.Instance);
    }
}
