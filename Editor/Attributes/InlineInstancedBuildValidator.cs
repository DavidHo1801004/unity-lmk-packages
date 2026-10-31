using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Mono.Cecil;

using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace LMK.Editor.Attributes
{
    /// <summary>
    /// Fails player builds when a class listed by <see cref="InlineInstancedAttributeDrawer"/> doesn't exist in the player.
    /// </summary>
    /// <remarks>
    /// Saved data using such a class, e.g. one wrapped in <c>#if UNITY_EDITOR</c>, can't be skipped when read:
    /// the player loses the whole component holding it, without any build error. <br/>
    /// The editor can't see this from reflection since the class exists there, so the compiled player scripts are read.
    /// Only assemblies compiled from source are checked, not precompiled plugins.
    /// </remarks>
    public sealed class InlineInstancedBuildValidator : IPostBuildPlayerScriptDLLs
    {
        private const string PLAYER_SCRIPTS_FOLDER = "Library/Bee/PlayerScriptAssemblies";

        public int callbackOrder => 0;

        public void OnPostBuildPlayerScriptDLLs(BuildReport _Report)
        {
            var candidates = InlineInstancedReflectionCache.GetExposedCandidates();
            if (candidates.Count == 0) return;

            if (!Directory.Exists(PLAYER_SCRIPTS_FOLDER))
            {
                Debug.LogWarning($"[InlineInstanced] Build check skipped: \"{PLAYER_SCRIPTS_FOLDER}\" doesn't exist.");
                return;
            }

            string[] playerDlls = Directory.GetFiles(PLAYER_SCRIPTS_FOLDER, "*.dll");
            if (playerDlls.Length == 0)
            {
                Debug.LogWarning($"[InlineInstanced] Build check skipped: no assembly found in \"{PLAYER_SCRIPTS_FOLDER}\".");
                return;
            }

            var sourceAssemblies = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(e => e.name));
            var checkedTypes = candidates.Where(e => sourceAssemblies.Contains(e.Assembly.GetName().Name));

            var missingTypes = FindMissingFromPlayer(checkedTypes, ReadTypeKeys(playerDlls));
            if (missingTypes.Count == 0) return;

            throw new BuildFailedException(GetFailureMessage(missingTypes));
        }

        /// <summary>
        /// Types of <paramref name="_ExposedTypes"/> that aren't in <paramref name="_PlayerTypeKeys"/>.
        /// </summary>
        /// <param name="_PlayerTypeKeys"> Types of the player, as returned by <see cref="GetTypeKey(string, string)"/>. </param>
        internal static IReadOnlyList<Type> FindMissingFromPlayer(IEnumerable<Type> _ExposedTypes, ISet<string> _PlayerTypeKeys)
        {
            return _ExposedTypes
                .Where(e => !_PlayerTypeKeys.Contains(GetTypeKey(e.Assembly.GetName().Name, e.FullName)))
                .OrderBy(e => e.FullName, StringComparer.Ordinal)
                .ToArray();
        }

        // Cecil separates nested types with '/', reflection with '+'.
        internal static string GetTypeKey(string _AssemblyName, string _TypeFullName)
        {
            return $"{_AssemblyName}|{_TypeFullName.Replace('/', '+')}";
        }

        internal static string GetFailureMessage(IReadOnlyList<Type> _MissingTypes)
        {
            string typeNames = string.Join(", ", _MissingTypes.Select(e => e.FullName));
            return $"[InlineInstanced] {typeNames} can be picked in the Inspector but doesn't exist in the player, "
                + "so any component using it would fail to load. Move it out of #if UNITY_EDITOR or into an Editor assembly, "
                + "or add [CanNotReferenceInline] to remove it from the picker.";
        }

        private static HashSet<string> ReadTypeKeys(IEnumerable<string> _DllPaths)
        {
            var typeKeys = new HashSet<string>();
            foreach (string path in _DllPaths)
            {
                using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters(ReadingMode.Deferred));

                string assemblyName = assembly.Name.Name;
                foreach (var type in assembly.MainModule.GetTypes())
                    typeKeys.Add(GetTypeKey(assemblyName, type.FullName));
            }
            return typeKeys;
        }
    }
}
