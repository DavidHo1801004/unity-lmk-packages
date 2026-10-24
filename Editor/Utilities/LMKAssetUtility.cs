using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

namespace LMK.Editor.Utilities
{
    /// <summary>
    /// Loads editor assets by asset path, e.g. from a package folder. <br/>
    /// Used instead of <c>Resources</c>, whose folders share one global namespace across the whole project.
    /// </summary>
    public static class LMKAssetUtility
    {
        /// <summary>
        /// Asset path of the <c>com.lmk.editor</c> package root.
        /// </summary>
        public const string EDITOR_PACKAGE_ROOT = "Packages/com.lmk.editor";

        // Loaded assets by path. Cleared on domain reload.
        private static readonly Dictionary<string, Object> assetCache = new();

        // Paths already reported as missing, so each is logged once.
        private static readonly HashSet<string> missingPaths = new();

        /// <summary>
        /// Load the asset of type <typeparamref name="T"/> at <paramref name="_AssetPath"/>,
        /// e.g. <c>"Packages/com.lmk.editor/Editor/Assets/LMK/LMKThemeDark.uss"</c>.
        /// </summary>
        /// <returns> The asset, or <see langword="null"/> (with a warning logged once) if it can't be found. </returns>
        public static T Load<T>(string _AssetPath) where T : Object
        {
            if (string.IsNullOrEmpty(_AssetPath)) return null;

            // A cached asset becomes null after it is deleted or reimported as another object, so load it again.
            if (assetCache.TryGetValue(_AssetPath, out var cached) && cached && cached is T typed)
                return typed;

            var asset = AssetDatabase.LoadAssetAtPath<T>(_AssetPath);
            if (asset)
            {
                assetCache[_AssetPath] = asset;
                missingPaths.Remove(_AssetPath);
            }
            else if (missingPaths.Add(_AssetPath))
            {
                Debug.LogWarning($"Missing {typeof(T).Name} at \"{_AssetPath}\".");
            }
            return asset;
        }
    }
}
