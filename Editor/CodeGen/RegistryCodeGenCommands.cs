using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEngine;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Menu commands of code generation, for every kind with generated code.
    /// </summary>
    internal static class RegistryCodeGenCommands
    {
        private const string GENERATE_MENU = "Tools/LMK/Registries/Generate Code";
        private const string PRUNE_MENU = "Tools/LMK/Registries/Prune Unused Code Aliases";

        private static IEnumerable<RegistryKind> KindsWithCode => RegistryKinds.All.Where(e => e.CodeStyle != null);

        [MenuItem(GENERATE_MENU, false, 100)]
        private static void GenerateAll()
        {
            foreach (var kind in KindsWithCode)
            {
                var result = RegistryCodeGen.Generate(kind);
                if (!result.Success)
                    EditorUtility.DisplayDialog("Generate Code", $"{kind.DisplayName} code was not generated:\n{result.Error}", "OK");
            }
        }

        [MenuItem(PRUNE_MENU, false, 101)]
        private static void PruneAll()
        {
            var lines = new List<string>();
            foreach (var kind in KindsWithCode)
            {
                var result = RegistryCodeGen.PruneAliases(kind, out var pruned);
                if (!result.Success) lines.Add($"{kind.DisplayName}: {result.Error}");
                else if (pruned.Count > 0) lines.Add($"{kind.DisplayName}: removed {string.Join(", ", pruned)}.");
            }

            string message = lines.Count > 0 ? string.Join("\n", lines) : "No unused alias was found.";
            Debug.Log($"Prune Unused Code Aliases: {message}");
            EditorUtility.DisplayDialog("Prune Unused Code Aliases", message, "OK");
        }

        [MenuItem(GENERATE_MENU, true)]
        [MenuItem(PRUNE_MENU, true)]
        private static bool HasKindsWithCode() => KindsWithCode.Any();
    }
}
