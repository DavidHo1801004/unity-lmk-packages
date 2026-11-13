using System;
using System.Collections.Generic;

using LMK.Editor.Registry;
using LMK.Runtime.GameplayTags;
using LMK.Runtime.Registry;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Immutable view of every loaded tag registry: a <see cref="RegistrySnapshot{TRow}"/> of the tag kind, with
    /// the tag tree on top. A new snapshot is built whenever a registry changes, and swapped in whole, so a
    /// snapshot can be read from any thread.
    /// </summary>
    internal sealed class TagRegistrySnapshot
    {
        /// <summary>
        /// One loaded registry, indexed by ID, by path and by parent.
        /// </summary>
        public sealed class Registry
        {
            private readonly Dictionary<RegistryGuid, List<RegistryGuid>> children = new();

            /// <summary>
            /// The registry as the registry core loaded it.
            /// </summary>
            public RegistrySnapshot<TagRegistryRow>.Registry Core { get; }

            public RegistryGuid Id => Core.Id;
            public string Name => Core.Name;
            public RegistryOrigin Origin => Core.Origin;
            public TagRegistryKind Kind => (TagRegistryKind)Core.Origin;
            public string AssetPath => Core.AssetPath;
            public string FileHash => Core.FileHash;

            /// <summary>
            /// The registry content. Never modify it: copy it with <see cref="RegistryData{TRow}.Clone"/>.
            /// </summary>
            public RegistryData<TagRegistryRow> Data => Core.Data;

            /// <summary>
            /// Only the main registry is editable, and only when it lives in the project.
            /// </summary>
            public bool IsReadOnly => Core.IsReadOnly;

            public bool ChecksumMismatch => Core.ChecksumMismatch;

            /// <summary>
            /// Whether the file is currently rejected, and the last version that loaded is used instead.
            /// </summary>
            public bool UsingLastGood => Core.UsingLastGood;

            public IReadOnlyList<string> Errors => Core.Errors;
            public IReadOnlyList<string> Warnings => Core.Warnings;

            public int Count => Core.Count;

            /// <summary>
            /// Every row, once per ID.
            /// </summary>
            public IEnumerable<RegistryRow<TagRegistryRow>> Rows
            {
                get
                {
                    foreach (var id in Core.Ids)
                        if (Core.TryGetRow(id, out var row))
                            yield return new RegistryRow<TagRegistryRow>(id, row);
                }
            }

            internal Registry(RegistrySnapshot<TagRegistryRow>.Registry _Core)
            {
                Core = _Core;

                foreach (var id in Core.Ids)
                {
                    if (!Core.TryGetRow(id, out var row)) continue;

                    if (!children.TryGetValue(row.parent, out var list))
                        children.Add(row.parent, list = new List<RegistryGuid>());

                    list.Add(id);
                }

                foreach (var list in children.Values)
                    list.Sort((a, b) =>
                    {
                        Core.TryGetRow(a, out var rowA);
                        Core.TryGetRow(b, out var rowB);
                        return string.CompareOrdinal(rowA.segment, rowB.segment);
                    });
            }

            public bool TryGetRow(RegistryGuid _Id, out TagRegistryRow _Row) => Core.TryGetRow(_Id, out _Row);

            public bool Contains(RegistryGuid _Id) => Core.Contains(_Id);

            /// <summary>
            /// Full path of a row, or <see langword="null"/>.
            /// </summary>
            public string GetPath(RegistryGuid _Id) => Core.GetName(_Id);

            public bool TryFind(string _Path, out RegistryGuid _Id) => Core.TryFind(_Path, out _Id);

            /// <summary>
            /// Direct children of a row, sorted by segment. Use <see cref="RegistryGuid.Zero"/> for the top level tags.
            /// </summary>
            public IReadOnlyList<RegistryGuid> GetChildren(RegistryGuid _Parent)
                => children.TryGetValue(_Parent, out var list) ? list : (IReadOnlyList<RegistryGuid>)Array.Empty<RegistryGuid>();
        }



        private readonly Dictionary<RegistryGuid, Registry> registriesById = new();
        private GameplayTagNode root;

        /// <summary>
        /// The snapshot as the registry core built it.
        /// </summary>
        public RegistrySnapshot<TagRegistryRow> Core { get; }

        /// <summary>
        /// Loaded registries: the main registry first, then by name.
        /// </summary>
        public IReadOnlyList<Registry> Registries { get; }

        /// <summary>
        /// The project's main registry, or <see langword="null"/> if none exists yet.
        /// </summary>
        public Registry Main { get; }

        /// <summary>
        /// Problems across registries, e.g. two files with the same registry ID.
        /// </summary>
        public IReadOnlyList<string> Issues => Core.Issues;

        /// <summary>
        /// Tag tree for the editor UI: one header node per registry, with its tags below. Built on first use.
        /// </summary>
        public GameplayTagNode Root => root ??= GameplayTagNode.CreateTree(this);

        public bool IsEmpty => Registries.Count == 0;



        internal TagRegistrySnapshot(RegistrySnapshot<TagRegistryRow> _Core)
        {
            Core = _Core;

            var registries = new List<Registry>(_Core.Registries.Count);
            foreach (var core in _Core.Registries)
            {
                var registry = new Registry(core);
                registries.Add(registry);
                registriesById[registry.Id] = registry;

                if (core == _Core.Main)
                    Main = registry;
            }

            Registries = registries;
        }



        public Registry Find(RegistryGuid _RegistryId)
            => registriesById.TryGetValue(_RegistryId, out var registry) ? registry : null;

        /// <summary>
        /// Find a live tag by path, in the main registry first, then in the others.
        /// </summary>
        public bool TryFindByPath(string _Path, out Registry _Registry, out RegistryGuid _Id)
        {
            foreach (var registry in Registries)
            {
                if (!registry.TryFind(_Path, out _Id)) continue;

                _Registry = registry;
                return true;
            }

            _Registry = null;
            _Id = RegistryGuid.Zero;
            return false;
        }

        /// <summary>
        /// Build the runtime database for this snapshot. The caller owns it.
        /// </summary>
        public GameplayTagDatabase BuildDatabase() => TagKind.Instance.BuildTagDatabase(Core);
    }
}
