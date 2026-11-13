using System;
using System.Collections.Generic;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// Immutable view of every loaded registry of one kind. A new snapshot is built whenever a registry changes,
    /// and swapped in whole, so a snapshot can be read from any thread.
    /// </summary>
    public sealed class RegistrySnapshot<TRow> where TRow : struct
    {
        /// <summary>
        /// Input of <see cref="Build"/>: one imported registry file.
        /// </summary>
        public sealed class Source
        {
            public string AssetPath;
            public RegistryData<TRow> Data;
            public string FileHash;
            public bool ChecksumMismatch;
            public bool UsingLastGood;
            public IReadOnlyList<string> Errors = Array.Empty<string>();
            public IReadOnlyList<string> Warnings = Array.Empty<string>();
        }



        /// <summary>
        /// One loaded registry, indexed by row ID and by name.
        /// </summary>
        public sealed class Registry
        {
            private readonly Dictionary<RegistryGuid, TRow> rows = new();
            private readonly Dictionary<RegistryGuid, string> names = new();
            private readonly Dictionary<string, RegistryGuid> idsByName = new(StringComparer.Ordinal);

            public RegistryGuid Id => Data.registryId;
            public string Name => string.IsNullOrEmpty(Data.name) ? Id.ToString() : Data.name;
            public RegistryOrigin Origin => Data.origin;
            public string AssetPath { get; }
            public string FileHash { get; }

            /// <summary>
            /// The registry content. Never modify it: copy it with <see cref="RegistryData{TRow}.Clone"/>.
            /// </summary>
            public RegistryData<TRow> Data { get; }

            /// <summary>
            /// Only the main registry is editable, and only when it lives in the project.
            /// </summary>
            public bool IsReadOnly => Origin != RegistryOrigin.Main || !AssetPath.StartsWith("Assets/", StringComparison.Ordinal);

            public bool ChecksumMismatch { get; }

            /// <summary>
            /// Whether the file is currently rejected, and the last version that loaded is used instead.
            /// </summary>
            public bool UsingLastGood { get; }

            public IReadOnlyList<string> Errors { get; }
            public IReadOnlyList<string> Warnings { get; }

            public int Count => rows.Count;

            public IEnumerable<RegistryGuid> Ids => rows.Keys;

            internal Registry(Source _Source, Func<RegistryData<TRow>, IReadOnlyDictionary<RegistryGuid, string>> _GetNames)
            {
                AssetPath = _Source.AssetPath ?? string.Empty;
                FileHash = _Source.FileHash ?? string.Empty;
                Data = _Source.Data;
                ChecksumMismatch = _Source.ChecksumMismatch;
                UsingLastGood = _Source.UsingLastGood;
                Errors = _Source.Errors ?? Array.Empty<string>();
                Warnings = _Source.Warnings ?? Array.Empty<string>();

                foreach (var row in Data.rows)
                    rows[row.id] = row.data;

                foreach (var (id, name) in _GetNames(Data))
                {
                    if (name == null || !rows.ContainsKey(id)) continue;

                    names[id] = name;
                    idsByName[name] = id;
                }
            }

            public bool TryGetRow(RegistryGuid _Id, out TRow _Row) => rows.TryGetValue(_Id, out _Row);

            public bool Contains(RegistryGuid _Id) => rows.ContainsKey(_Id);

            /// <summary>
            /// Name of a row, e.g. a tag's full path, or <see langword="null"/>.
            /// </summary>
            public string GetName(RegistryGuid _Id) => names.TryGetValue(_Id, out string name) ? name : null;

            public bool TryFind(string _Name, out RegistryGuid _Id) => idsByName.TryGetValue(_Name ?? string.Empty, out _Id);
        }



        private readonly Dictionary<RegistryGuid, Registry> registriesById = new();

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

        public bool IsEmpty => Registries.Count == 0;



        private RegistrySnapshot(List<Registry> _Registries, Registry _Main, List<string> _Issues)
        {
            Registries = _Registries;
            Main = _Main;
            Issues = _Issues;

            foreach (var registry in _Registries)
                registriesById[registry.Id] = registry;
        }

        /// <summary>
        /// Build a snapshot. When several files share a registry ID, the first one by path is used.
        /// </summary>
        /// <param name="_GetNames"> The kind's names of every row of a registry that has one. </param>
        /// <param name="_MainRegistryPath"> Where the main registry is expected. A main registry elsewhere is still used if there is no other. </param>
        public static RegistrySnapshot<TRow> Build(IEnumerable<Source> _Sources, Func<RegistryData<TRow>, IReadOnlyDictionary<RegistryGuid, string>> _GetNames,
                                                   string _MainRegistryPath)
        {
            if (_GetNames == null) throw new ArgumentNullException(nameof(_GetNames));

            var sources = new List<Source>();
            foreach (var source in _Sources)
                if (source?.Data != null)
                    sources.Add(source);

            sources.Sort((a, b) => string.CompareOrdinal(a.AssetPath, b.AssetPath));

            var issues = new List<string>();
            var registries = new List<Registry>();
            var seen = new Dictionary<RegistryGuid, string>();

            foreach (var source in sources)
            {
                if (seen.TryGetValue(source.Data.registryId, out string firstPath))
                {
                    issues.Add($"\"{source.AssetPath}\" has the same registry ID as \"{firstPath}\" and is ignored.");
                    continue;
                }

                seen.Add(source.Data.registryId, source.AssetPath);
                registries.Add(new Registry(source, _GetNames));
            }

            Registry main = registries.Find(e => e.Origin == RegistryOrigin.Main && e.AssetPath == _MainRegistryPath)
                         ?? registries.Find(e => e.Origin == RegistryOrigin.Main);

            registries.Sort((a, b) =>
            {
                if (a == main) return -1;
                if (b == main) return 1;

                int compare = string.CompareOrdinal(a.Name, b.Name);
                return compare != 0 ? compare : a.Id.CompareTo(b.Id);
            });

            return new RegistrySnapshot<TRow>(registries, main, issues);
        }

        public static RegistrySnapshot<TRow> Empty(Func<RegistryData<TRow>, IReadOnlyDictionary<RegistryGuid, string>> _GetNames)
            => Build(Array.Empty<Source>(), _GetNames, null);



        public Registry Find(RegistryGuid _RegistryId)
            => registriesById.TryGetValue(_RegistryId, out var registry) ? registry : null;

        /// <summary>
        /// Find a row by name, in the main registry first, then in the others.
        /// </summary>
        public bool TryFindByName(string _Name, out Registry _Registry, out RegistryGuid _Id)
        {
            foreach (var registry in Registries)
            {
                if (!registry.TryFind(_Name, out _Id)) continue;

                _Registry = registry;
                return true;
            }

            _Registry = null;
            _Id = RegistryGuid.Zero;
            return false;
        }
    }
}
