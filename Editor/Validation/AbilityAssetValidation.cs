using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

using UnityEngine;

using LMK.Runtime.AbilitySystem;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// Runs <see cref="EffectValidator"/> and <see cref="AbilityValidator"/> over every effect and ability asset of the
    /// project: from the menu, and before each player build, which an error stops.
    /// </summary>
    internal static class AbilityAssetValidation
    {
        private const string MENU = "Tools/LMK/Ability System/";

        internal readonly struct Problem
        {
            public readonly Object Asset;
            public readonly bool IsError;
            public readonly string Message;

            public Problem(Object _Asset, bool _IsError, string _Message)
            {
                Asset = _Asset;
                IsError = _IsError;
                Message = _Message;
            }

            public override string ToString() => $"{(Asset != null ? AssetDatabase.GetAssetPath(Asset) : "?")}: {Message}";
        }

        [MenuItem(MENU + "Validate Effects and Abilities", priority = 45)]
        private static void ValidateFromMenu()
        {
            var problems = ValidateAll();
            Log(problems);

            int errors = problems.Count(e => e.IsError);
            Debug.Log(problems.Count == 0
                ? "Ability System: every effect and ability is valid."
                : $"Ability System: {errors} errors and {problems.Count - errors} warnings in effects and abilities. See the console.");
        }

        /// <summary>
        /// The problems of every effect and ability asset of the project.
        /// </summary>
        internal static List<Problem> ValidateAll()
        {
            var problems = new List<Problem>();
            var settings = FindSettings();

            foreach (var effect in LoadAll<GameplayEffect>())
                foreach (var issue in EffectValidator.Validate(effect, settings).Issues)
                    problems.Add(new Problem(effect, issue.Severity == EffectIssueSeverity.Error, issue.Message));

            foreach (var ability in LoadAll<GameplayAbility>())
                foreach (var issue in AbilityValidator.Validate(ability).Issues)
                    problems.Add(new Problem(ability, issue.Severity == EffectIssueSeverity.Error, issue.Message));

            return problems;
        }

        internal static void Log(IEnumerable<Problem> _Problems)
        {
            foreach (var problem in _Problems)
            {
                string message = $"[Ability System] {problem}";
                if (problem.IsError) Debug.LogError(message, problem.Asset);
                else Debug.LogWarning(message, problem.Asset);
            }
        }

        private static IEnumerable<T> LoadAll<T>() where T : Object
            => AssetDatabase.FindAssets("t:" + typeof(T).Name)
                            .Select(AssetDatabase.GUIDToAssetPath)
                            .SelectMany(AssetDatabase.LoadAllAssetsAtPath)
                            .OfType<T>();

        private static AbilitySystemSettings FindSettings() => LoadAll<AbilitySystemSettings>().FirstOrDefault();
    }



    /// <summary>
    /// Stops a player build when an effect or ability asset has an error.
    /// </summary>
    internal sealed class AbilityAssetBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport _Report)
        {
            var problems = AbilityAssetValidation.ValidateAll();
            AbilityAssetValidation.Log(problems);

            int errors = problems.Count(e => e.IsError);
            if (errors > 0)
                throw new BuildFailedException($"Ability System: {errors} effect or ability assets have errors. Fix them, or run " +
                                               "Tools > LMK > Ability System > Validate Effects and Abilities to list them.");
        }
    }
}
