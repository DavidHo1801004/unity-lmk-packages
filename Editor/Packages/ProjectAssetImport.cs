using System.Collections.Generic;

using LMK.Editor.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Outcome of <see cref="ProjectAssetImport.Map"/>: a <see cref="RegistryImportResult"/>, with its added rows as tags.
    /// </summary>
    internal sealed class TagImportResult
    {
        /// <summary>
        /// The result as the registry core returned it.
        /// </summary>
        public RegistryImportResult Core { get; }

        public bool Success => Core.Success;

        /// <summary>
        /// The lock wasn't available, so the assets wait for <see cref="ProjectAssetImport.MapPending"/>.
        /// </summary>
        public bool Postponed => Core.Postponed;

        public string Error => Core.Error;

        /// <summary> Rows added to main registries, as "name (ID)". </summary>
        public List<string> AddedTags => Core.AddedRows;

        /// <summary> References rewritten, one line each. </summary>
        public List<string> Rewritten => Core.Rewritten;

        /// <summary> References left as they are because they can't be mapped, one line each. </summary>
        public List<string> Unmapped => Core.Unmapped;

        public List<string> Warnings => Core.Warnings;

        /// <summary> Assets checked. </summary>
        public int Assets => Core.Assets;

        public double Seconds => Core.Seconds;

        /// <summary> The import log file, if one was written. </summary>
        public string LogPath => Core.LogPath;

        public string Message => Core.Message;

        public TagImportResult(RegistryImportResult _Core)
        {
            Core = _Core;
        }

        public static TagImportResult From(RegistryImportResult _Core) => _Core == null ? null : new TagImportResult(_Core);
    }



    /// <summary>
    /// Maps the references of assets brought in from another project, through the registry core's
    /// <see cref="RegistryImportMapping"/>: references into that project's main registry, which this project doesn't
    /// have, are rewritten to tags of this project's main registry, by name.
    /// </summary>
    /// <remarks>
    /// Runs only for explicit imports: a <c>.unitypackage</c> import, or the Map command. Pulls, file copies and domain
    /// reloads never map anything. Requires the lock: without it, the assets wait until Map Imported Assets runs.
    /// </remarks>
    internal static class ProjectAssetImport
    {
        public const string PENDING_PATH = RegistryImportMapping.PENDING_PATH;
        public const string UNDO_NAME = RegistryImportMapping.UNDO_NAME;

        /// <summary>
        /// Where postponed assets wait for the lock. Only tests change it.
        /// </summary>
        internal static string PendingPath
        {
            get => RegistryImportMapping.PendingPath;
            set => RegistryImportMapping.PendingPath = value;
        }

        /// <summary>
        /// Where import logs are written. Only tests change it.
        /// </summary>
        internal static string LogFolder
        {
            get => RegistryImportMapping.LogFolder;
            set => RegistryImportMapping.LogFolder = value;
        }

        /// <summary>
        /// Whether a <c>.unitypackage</c> import is under way, and its assets are being collected.
        /// </summary>
        public static bool IsCollecting => RegistryImportMapping.IsCollecting;

        internal static void OnImportStarted(string _Package) => RegistryImportMapping.OnImportStarted(_Package);

        internal static void OnAssetsImported(IEnumerable<string> _Paths) => RegistryImportMapping.OnAssetsImported(_Paths);

        internal static TagImportResult OnImportCompleted(string _Package) => TagImportResult.From(RegistryImportMapping.OnImportCompleted(_Package));

        internal static void OnImportCancelled(string _Package) => RegistryImportMapping.OnImportCancelled(_Package);

        /// <summary>
        /// Assets imported while the lock or a main registry wasn't available, waiting to be mapped.
        /// </summary>
        public static List<string> PendingAssets => RegistryImportMapping.PendingAssets;

        public static bool HasPending => RegistryImportMapping.HasPending;

        internal static void ClearPending() => RegistryImportMapping.ClearPending();

        /// <summary>
        /// Map the assets that waited.
        /// </summary>
        public static TagImportResult MapPending(bool _ShowProgress) => TagImportResult.From(RegistryImportMapping.MapPending(_ShowProgress));

        /// <summary>
        /// Map the references of incoming assets.
        /// </summary>
        public static TagImportResult Map(IReadOnlyCollection<string> _Paths, string _Source, bool _ShowProgress)
            => TagImportResult.From(RegistryImportMapping.Map(_Paths, _Source, _ShowProgress));

        public static void MapPendingWithDialog() => RegistryImportMapping.MapPendingWithDialog();
    }
}
