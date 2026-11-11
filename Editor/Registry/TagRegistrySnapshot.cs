using System;
using System.Collections.Generic;

using LMK.Runtime.GameplayTags;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// Immutable view of every loaded registry. A new snapshot is built whenever a registry changes, and
    /// swapped in whole, so a snapshot can be read from any thread.
    /// </summary>
    internal sealed class TagRegistrySnapshot
    {
        /// <summary>
        /// Input of <see cref="Build"/>: one imported registry file.
        /// </summary>
        public sealed class Source
        {
            public string AssetPath;
            public TagRegistryData Data;
            public string FileHash;
            public bool ChecksumMismatch;
            public bool UsingLastGood;
            public IReadOnlyList<string> Errors = Array.Empty<string>();
            public IReadOnlyList<string> Warnings = Array.Empty<string>();
        }



        /// <summary>
        /// One loaded registry, indexed by ID and by path.
        /// </summary>
        public sealed class Registry
        {
            private readonly Dictionary<TagGuid, TagRegistryRow> rows = new();
            private readonly Dictionary<TagGuid, string> paths = new();
            private readonly Dictionary<string, TagGuid> idsByPath = new(StringComparer.Ordinal);
            private readonly Dictionary<TagGuid, List<TagGuid>> children = new();

            public TagGuid Id => Data.registryId;
            public string Name => string.IsNullOrEmpty(Data.name) ? Id.ToString() : Data.name;
            public TagRegistryKind Kind => Data.kind;
            public string AssetPath { get; }
            public string FileHash { get; }

            /// <summary>
            /// The registry content. Never modify it: copy it with <see cref="TagRegistryData.Clone"/>.
            /// </summary>
            public TagRegistryData Data { get; }

            /// <summary>
            /// Only the main registry is editable, and only when it lives in the project.
            /// </summary>
            public bool IsReadOnly => Kind != TagRegistryKind.Main || !AssetPath.StartsWith("Assets/", StringComparison.Ordinal);

            public bool ChecksumMismatch { get; }

            /// <summary>
            /// Whether the file is currently rejected, and the last version that loaded is used instead.
            /// </summary>
            public bool UsingLastGood { get; }

            public IReadOnlyList<string> Errors { get; }
            public IReadOnlyList<string> Warnings { get; }

            public int Count => rows.Count;

            public IEnumerable<TagRegistryRow> Rows => rows.Values;

            internal Registry(Source _Source)
            {
                AssetPath = _Source.AssetPath ?? string.Empty;
                FileHash = _Source.FileHash ?? string.Empty;
                Data = _Source.Data;
                ChecksumMismatch = _Source.ChecksumMismatch;
                UsingLastGood = _Source.UsingLastGood;
                Errors = _Source.Errors ?? Array.Empty<string>();
                Warnings = _Source.Warnings ?? Array.Empty<string>();

                foreach (var row in Data.rows)
                    rows[row.id] = row;

                foreach (var row in rows.Values)
                {
                    if (!children.TryGetValue(row.parent, out var list))
                        children.Add(row.parent, list = new List<TagGuid>());

                    list.Add(row.id);
                }

                foreach (var list in children.Values)
                    list.Sort((a, b) => string.CompareOrdinal(rows[a].segment, rows[b].segment));

                foreach (var id in rows.Keys)
                {
                    string path = ComputePath(id);
                    if (path == null) continue;

                    paths[id] = path;
                    idsByPath[path] = id;
                }
            }

            public bool TryGetRow(TagGuid _Id, out TagRegistryRow _Row) => rows.TryGetValue(_Id, out _Row);

            public bool Contains(TagGuid _Id) => rows.ContainsKey(_Id);

            /// <summary>
            /// Full path of a row, or <see langword="null"/>.
            /// </summary>
            public string GetPath(TagGuid _Id) => paths.TryGetValue(_Id, out string path) ? path : null;

            public bool TryFind(string _Path, out TagGuid _Id) => idsByPath.TryGetValue(_Path ?? string.Empty, out _Id);

            /// <summary>
            /// Direct children of a row, sorted by segment. Use <see cref="TagGuid.Zero"/> for the top level tags.
            /// </summary>
            public IReadOnlyList<TagGuid> GetChildren(TagGuid _Parent)
                => children.TryGetValue(_Parent, out var list) ? list : (IReadOnlyList<TagGuid>)Array.Empty<TagGuid>();

            private string ComputePath(TagGuid _Id)
            {
                var segments = new List<string>();
                var current = _Id;

                // Validated registries have no cycles, but never loop forever.
                for (int steps = 0; !current.IsZero; steps++)
                {
                    if (steps > rows.Count || !rows.TryGetValue(current, out var row)) return null;

                    segments.Add(row.segment);
                    current = row.parent;
                }

                segments.Reverse();
                return string.Join(TagPath.SEPARATOR.ToString(), segments);
            }
        }



        private readonly Dictionary<TagGuid, Registry> registriesById = new();

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
        public IReadOnlyList<string> Issues { get; }

        /// <summary>
        /// Tag tree for the editor UI: one header node per registry, with its tags below.
        /// </summary>
        public GameplayTagNode Root { get; }

        public bool IsEmpty => Registries.Count == 0;

        public static readonly TagRegistrySnapshot Empty = Build(Array.Empty<Source>(), null);



        private TagRegistrySnapshot(List<Registry> _Registries, Registry _Main, List<string> _Issues)
        {
            Registries = _Registries;
            Main = _Main;
            Issues = _Issues;

            foreach (var registry in _Registries)
                registriesById[registry.Id] = registry;

            Root = GameplayTagNode.CreateTree(this);
        }

        /// <summary>
        /// Build a snapshot. When several files share a registry ID, the first one by path is used.
        /// </summary>
        /// <param name="_MainRegistryPath"> Where the main registry is expected. A main registry elsewhere is still used if there is no other. </param>
        public static TagRegistrySnapshot Build(IEnumerable<Source> _Sources, string _MainRegistryPath)
        {
            var sources = new List<Source>();
            foreach (var source in _Sources)
                if (source?.Data != null)
                    sources.Add(source);

            sources.Sort((a, b) => string.CompareOrdinal(a.AssetPath, b.AssetPath));

            var issues = new List<string>();
            var registries = new List<Registry>();
            var seen = new Dictionary<TagGuid, string>();

            foreach (var source in sources)
            {
                if (seen.TryGetValue(source.Data.registryId, out string firstPath))
                {
                    issues.Add($"\"{source.AssetPath}\" has the same registry ID as \"{firstPath}\" and is ignored.");
                    continue;
                }

                seen.Add(source.Data.registryId, source.AssetPath);
                registries.Add(new Registry(source));
            }

            Registry main = registries.Find(e => e.Kind == TagRegistryKind.Main && e.AssetPath == _MainRegistryPath)
                         ?? registries.Find(e => e.Kind == TagRegistryKind.Main);

            registries.Sort((a, b) =>
            {
                if (a == main) return -1;
                if (b == main) return 1;

                int compare = string.CompareOrdinal(a.Name, b.Name);
                return compare != 0 ? compare : a.Id.CompareTo(b.Id);
            });

            return new TagRegistrySnapshot(registries, main, issues);
        }



        public Registry Find(TagGuid _RegistryId)
            => registriesById.TryGetValue(_RegistryId, out var registry) ? registry : null;

        /// <summary>
        /// Find a live tag by path, in the main registry first, then in the others.
        /// </summary>
        public bool TryFindByPath(string _Path, out Registry _Registry, out TagGuid _Id)
        {
            foreach (var registry in Registries)
            {
                if (!registry.TryFind(_Path, out _Id)) continue;

                _Registry = registry;
                return true;
            }

            _Registry = null;
            _Id = TagGuid.Zero;
            return false;
        }

        /// <summary>
        /// Build the runtime database for this snapshot.
        /// </summary>
        public GameplayTagDatabase BuildDatabase()
        {
            var builder = new GameplayTagDatabaseBuilder();
            foreach (var registry in Registries)
            {
                var rows = new List<GameplayTagDatabaseBuilder.Row>(registry.Count);
                foreach (var row in registry.Rows)
                    rows.Add(new GameplayTagDatabaseBuilder.Row(row.id, row.parent, row.segment, row.isImplicit));

                builder.AddRegistry(registry.Id, registry.Name, registry.Kind, rows);
            }

            return builder.Build();
        }
    }
}
