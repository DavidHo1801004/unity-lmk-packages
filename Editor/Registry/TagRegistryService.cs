using System;
using System.Collections.Generic;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// The loaded tag registries, as a <see cref="TagRegistrySnapshot"/>, and the editor's
    /// <see cref="GameplayTagDatabase"/>. The registry core loads them (<see cref="TagKind"/>).
    /// </summary>
    /// <remarks>
    /// Read-only: loading never writes a file. The published snapshot shows the main registry's staged edits
    /// (<see cref="TagOperations"/>), so every tag view shows them before they are saved. <see cref="SavedSnapshot"/>
    /// holds only saved content, for code generation and builds.
    /// </remarks>
    internal static class TagRegistryService
    {
        public const string ROOT_FOLDER = TagKind.ROOT_FOLDER;

        public const string MAIN_REGISTRY_PATH = ROOT_FOLDER + "/Main." + TagRegistryFile.EXTENSION;

        private static TagRegistrySnapshot snapshot;
        private static TagRegistrySnapshot savedSnapshot;

        /// <summary>
        /// Where the main registry lives. Only tests change it.
        /// </summary>
        internal static string MainRegistryPath
        {
            get => TagKind.Instance.MainRegistryPath;
            set => TagKind.Instance.MainRegistryPath = value;
        }

        /// <summary>
        /// Called after a new snapshot was published.
        /// </summary>
        public static event Action OnSnapshotChanged
        {
            add => TagKind.Instance.SnapshotChanged += value;
            remove => TagKind.Instance.SnapshotChanged -= value;
        }

        /// <summary>
        /// The current snapshot. Loads the registries on first access.
        /// </summary>
        public static TagRegistrySnapshot Snapshot => Wrap(TagKind.Instance.Snapshot, ref snapshot);

        /// <summary>
        /// The registries as saved on disk, without staged edits. The same as <see cref="Snapshot"/> when
        /// nothing is staged.
        /// </summary>
        public static TagRegistrySnapshot SavedSnapshot
        {
            get
            {
                var current = Snapshot;
                var saved = TagKind.Instance.SavedSnapshot;
                return saved == current.Core ? current : Wrap(saved, ref savedSnapshot);
            }
        }

        /// <summary>
        /// Why generated registries are not loaded: one message per registry whose package isn't installed.
        /// </summary>
        public static IReadOnlyList<string> InactiveRegistries => TagKind.Instance.InactiveRegistries;

        /// <summary>
        /// IDs of the generated registries that are not loaded because their package isn't installed. References to
        /// them belong to that package, not to another project.
        /// </summary>
        public static IReadOnlyCollection<RegistryGuid> InactiveRegistryIds => TagKind.Instance.InactiveRegistryIds;

        /// <summary>
        /// The editor database built from the current snapshot.
        /// </summary>
        internal static GameplayTagDatabase EditorDatabase => (GameplayTagDatabase)TagKind.Instance.EditorDatabase;

        public static void EnsureLoaded() => TagKind.Instance.EnsureLoaded();

        /// <summary>
        /// Load every registry asset again, and publish a new snapshot and editor database.
        /// </summary>
        public static void Reload() => TagKind.Instance.Reload();

        // One wrapper per core snapshot: the tag tree is built once.
        private static TagRegistrySnapshot Wrap(RegistrySnapshot<TagRegistryRow> _Core, ref TagRegistrySnapshot _Cache)
        {
            var cache = _Cache;
            if (cache != null && cache.Core == _Core) return cache;

            cache = new TagRegistrySnapshot(_Core);
            _Cache = cache;
            return cache;
        }
    }
}
