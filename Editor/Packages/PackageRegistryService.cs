using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

using LMK.Runtime.GameplayTags;

using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace LMK.Editor.GameplayTags
{
    /// <summary>
    /// A package, as the package registry commands see it.
    /// </summary>
    internal sealed class TagPackage
    {
        public string Name;
        public string Version;
        public string DisplayName;

        /// <summary>
        /// Asset path of the package folder, e.g. <c>Packages/com.vendor.combat</c>.
        /// </summary>
        public string Root;

        /// <summary>
        /// Names of the packages it lists as dependencies in <c>package.json</c>.
        /// </summary>
        public List<string> Dependencies = new();

        public static TagPackage From(PackageInfo _Info) => _Info == null ? null : new TagPackage
        {
            Name = _Info.name,
            Version = _Info.version,
            DisplayName = string.IsNullOrEmpty(_Info.displayName) ? _Info.name : _Info.displayName,
            Root = _Info.assetPath,
            Dependencies = _Info.dependencies?.Select(e => e.name).ToList() ?? new List<string>(),
        };

        /// <summary>
        /// The package that holds an asset or folder, or <see langword="null"/>.
        /// </summary>
        public static TagPackage ForAssetPath(string _AssetPath)
            => string.IsNullOrEmpty(_AssetPath) ? null : From(PackageInfo.FindForAssetPath(_AssetPath));

        /// <summary>
        /// The asset paths under <see cref="Root"/> that can hold tag references.
        /// </summary>
        public List<string> GetTagHolderAssets()
            => AssetPathsUnder(Root).Where(TagAssetScanner.CanHoldTags).ToList();

        public static IEnumerable<string> AssetPathsUnder(string _Root)
        {
            string prefix = _Root.TrimEnd('/') + "/";
            return AssetDatabase.GetAllAssetPaths()
                .Where(e => e.StartsWith(prefix, StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(e))
                .OrderBy(e => e, StringComparer.Ordinal);
        }
    }



    /// <summary>
    /// Outcome of <see cref="PackageRegistryService.Generate"/>.
    /// </summary>
    internal sealed class PackageRegistryResult
    {
        public bool Success = true;
        public string Error;

        /// <summary> Registry files written. </summary>
        public readonly List<string> Written = new();

        public readonly List<string> Warnings = new();

        public string Message;

        public static PackageRegistryResult Fail(string _Error) => new() { Success = false, Error = _Error, Message = _Error };
    }



    /// <summary>
    /// Registries of packages: shipped registries load like any other. Packages without one get a generated
    /// registry on an explicit command, kept in <c>Assets/GameplayTags/Packages/</c>. A generated registry is
    /// loaded only while its package is installed, so references to an uninstalled package resolve as
    /// <c>MissingRegistry</c>.
    /// </summary>
    /// <remarks>
    /// Package events never write: a changed package only reloads the registries, and a package updated past
    /// the version its registry was generated from gets a Regenerate prompt.
    /// </remarks>
    [InitializeOnLoad]
    internal static class PackageRegistryService
    {
        public const string GENERATED_FOLDER = TagRegistryService.ROOT_FOLDER + "/Packages";

        /// <summary>
        /// Where generated registries are written. Only tests change it.
        /// </summary>
        internal static string GeneratedFolder { get; set; } = GENERATED_FOLDER;

        /// <summary>
        /// Replaces the installed packages (name → version), for tests.
        /// </summary>
        internal static Func<IReadOnlyDictionary<string, string>> InstalledPackagesOverride { get; set; }



        static PackageRegistryService()
        {
            Events.registeredPackages += e =>
            {
                var changed = e.added.Concat(e.changedTo).Select(p => p.name).Concat(e.removed.Select(p => p.name));
                OnPackagesChanged(changed);
            };
        }

        /// <summary>
        /// Installed packages: name → version.
        /// </summary>
        public static IReadOnlyDictionary<string, string> GetInstalledPackages()
        {
            if (InstalledPackagesOverride != null)
                return InstalledPackagesOverride();

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
                result[package.name] = package.version;

            return result;
        }

        /// <summary>
        /// Whether a registry loads: always, except a generated registry whose package isn't installed.
        /// </summary>
        public static bool IsActive(TagRegistryData _Data, IReadOnlyDictionary<string, string> _Installed)
            => _Data.kind != TagRegistryKind.Generated
            || string.IsNullOrEmpty(_Data.package)
            || _Installed.ContainsKey(_Data.package);



        #region Package Events
        /// <summary>
        /// Packages were added, updated or removed (an install, or a teammate's pull). Reload, so generated
        /// registries follow their packages, and prompt to regenerate the ones that are out of date. Writes nothing.
        /// </summary>
        /// <returns> The prompts shown. </returns>
        internal static List<string> OnPackagesChanged(IEnumerable<string> _Packages)
        {
            TagRegistryService.Reload();

            var changed = new HashSet<string>(_Packages ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var prompts = new List<string>();

            foreach (var (registry, installedVersion) in FindOutdated(TagRegistryService.Snapshot))
            {
                if (!changed.Contains(registry.Data.package)) continue;

                string prompt = $"Package \"{registry.Data.package}\" is now {installedVersion}, but its gameplay tag registry " +
                                $"\"{registry.AssetPath}\" was generated from {registry.Data.version}. Regenerate it from the Gameplay " +
                                "Tag Manager (right-click the registry) to add its new tags.";
                prompts.Add(prompt);
                Debug.LogWarning(prompt);
            }

            return prompts;
        }

        /// <summary>
        /// Generated registries whose package is installed in another version than they were generated from.
        /// </summary>
        public static List<(TagRegistrySnapshot.Registry Registry, string InstalledVersion)> FindOutdated(TagRegistrySnapshot _Snapshot)
        {
            var installed = GetInstalledPackages();
            var result = new List<(TagRegistrySnapshot.Registry, string)>();

            foreach (var registry in _Snapshot.Registries)
            {
                if (registry.Kind != TagRegistryKind.Generated || string.IsNullOrEmpty(registry.Data.package)) continue;
                if (!installed.TryGetValue(registry.Data.package, out string version)) continue;

                if (!string.IsNullOrEmpty(registry.Data.version) && registry.Data.version != version)
                    result.Add((registry, version));
            }

            return result;
        }
        #endregion



        #region Generate
        /// <summary>
        /// Generate the registries a package needs, or regenerate them. Requires the lock. <br/>
        /// The package's assets are scanned for references, grouped by registry ID. Each registry ID that no
        /// loaded registry has gets a generated registry with that ID, and a row per referenced tag with the
        /// referenced ID, so the package's references resolve without changing it. Tags of the package's tag files
        /// that no asset uses are added with new IDs.
        /// </summary>
        /// <param name="_RemoveMissing"> Remove rows that the package no longer references or lists. Otherwise existing rows are kept. </param>
        public static PackageRegistryResult Generate(TagPackage _Package, bool _RemoveMissing)
        {
            if (_Package == null || string.IsNullOrEmpty(_Package.Name))
                return PackageRegistryResult.Fail("Select a folder of an installed package.");

            var snapshot = TagRegistryService.Snapshot;
            var result = new PackageRegistryResult();

            // This package's generated registries: regenerated in place. Every other registry is known.
            var existing = snapshot.Registries
                .Where(e => e.Kind == TagRegistryKind.Generated && e.Data.package == _Package.Name)
                .ToDictionary(e => e.Id);

            var known = new HashSet<TagGuid>(snapshot.Registries.Select(e => e.Id).Where(e => !existing.ContainsKey(e)));
            known.UnionWith(TagRegistryService.InactiveRegistryIds);

            // References, by registry.
            var groups = new Dictionary<TagGuid, List<GameplayTag>>();
            int unplaced = 0;

            foreach (string path in _Package.GetTagHolderAssets())
            {
                var tags = new List<GameplayTag>();
                if (!TagAssetScanner.Scan(path, tags))
                    result.Warnings.Add($"\"{path}\" could not be read completely.");

                foreach (var tag in tags)
                {
                    var registry = tag.RegistryId.IsZero ? FindRegistryOf(snapshot, tag.Id) : tag.RegistryId;
                    if (registry.IsZero)
                    {
                        unplaced++;
                        continue;
                    }

                    if (known.Contains(registry)) continue;

                    if (!groups.TryGetValue(registry, out var list))
                        groups.Add(registry, list = new List<GameplayTag>());

                    list.Add(tag);
                }
            }

            if (unplaced > 0)
                result.Warnings.Add($"{unplaced} prefab override(s) changed only a tag ID that no registry has, so their registry is unknown. They were skipped.");

            // Existing generated registries stay, even if nothing references them anymore.
            foreach (var id in existing.Keys)
                if (!groups.ContainsKey(id))
                    groups.Add(id, new List<GameplayTag>());

            var tagFileEntries = ReadTagFiles(_Package, result);

            if (groups.Count == 0 && tagFileEntries.Count == 0)
            {
                result.Message = $"\"{_Package.Name}\" needs no generated registry: all of its references resolve to loaded registries.";
                return result;
            }

            // Tag files go to the registry with the most references, or to a new one.
            var tagFileRegistry = groups.Count > 0
                ? groups.OrderByDescending(e => e.Value.Count).ThenBy(e => e.Key).First().Key
                : TagGuid.NewGuid();
            if (!groups.ContainsKey(tagFileRegistry))
                groups.Add(tagFileRegistry, new List<GameplayTag>());

            // Build every registry before writing any.
            var files = new List<(string Path, TagRegistryData Data)>();
            foreach (var (registryId, tags) in groups.OrderBy(e => e.Key))
            {
                existing.TryGetValue(registryId, out var current);

                var data = current?.Data.Clone() ?? new TagRegistryData
                {
                    registryId = registryId,
                    name = _Package.DisplayName,
                    kind = TagRegistryKind.Generated,
                };
                data.package = _Package.Name;
                data.version = _Package.Version ?? string.Empty;

                var keep = new HashSet<TagGuid>();
                AddReferencedRows(data, tags, keep, result.Warnings);

                if (registryId == tagFileRegistry)
                    foreach (var entry in tagFileEntries)
                        keep.Add(TagOperations.AddPath(data, entry.Path, entry.Description, out _));

                if (_RemoveMissing)
                    RemoveRowsNotKept(data, keep);

                var errors = new List<string>();
                TagRegistryFile.Validate(data, errors);
                if (errors.Count > 0)
                    return PackageRegistryResult.Fail($"The registry generated for {registryId} is invalid:\n{string.Join("\n", errors)}");

                string path = current?.AssetPath ?? GeneratedPath(_Package.Name, registryId, groups.Count > 1, files.Select(e => e.Path));
                files.Add((path, data));
            }

            var lockedPaths = files.Select(e => e.Path).Append(TagCodeGen.OutputPath).ToList();
            if (!TagLockProvider.Current.TryAcquire(lockedPaths, out string lockError))
                return PackageRegistryResult.Fail($"The gameplay tag lock ({TagLockProvider.Current.Name}) is not available. {lockError}".TrimEnd());

            foreach (var (path, data) in files)
            {
                TagOperations.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                TagOperations.WriteAtomic(path, TagRegistryFile.Write(data));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                result.Written.Add(path);
            }

            TagRegistryService.Reload();

            // Registry first, code last.
            TagCodeGen.MarkPending();

            result.Message = $"Generated {files.Count} gameplay tag registr{(files.Count == 1 ? "y" : "ies")} for \"{_Package.Name}\": " +
                             string.Join(", ", files.Select(e => $"\"{e.Path}\" ({e.Data.rows.Count} tags)")) + ".";
            if (result.Warnings.Count > 0)
                result.Message += "\n" + string.Join("\n", result.Warnings);

            return result;
        }

        /// <summary>
        /// A row per referenced tag, with the referenced ID. Shorter paths first, so referenced parents keep their IDs.
        /// </summary>
        private static void AddReferencedRows(TagRegistryData _Data, List<GameplayTag> _Tags, HashSet<TagGuid> _Keep, List<string> _Warnings)
        {
            // One path per ID: the name most references use.
            var paths = _Tags
                .GroupBy(e => e.Id)
                .Select(group =>
                {
                    var names = group.Select(e => e.LastKnownName).Where(e => !string.IsNullOrEmpty(e))
                        .GroupBy(e => e).OrderByDescending(e => e.Count()).ThenBy(e => e.Key, StringComparer.Ordinal)
                        .Select(e => e.Key).ToList();

                    if (names.Count > 1)
                        _Warnings.Add($"Tag {group.Key} is referenced as {string.Join(", ", names.Select(e => $"\"{e}\""))}. \"{names[0]}\" is used.");

                    return (Id: group.Key, Path: names.FirstOrDefault());
                })
                .ToList();

            foreach (var (id, path) in paths.OrderBy(e => Depth(e.Path)).ThenBy(e => e.Path, StringComparer.Ordinal))
            {
                // Already a row: kept where it is.
                if (_Data.IndexOf(id) >= 0)
                {
                    _Keep.Add(id);
                    continue;
                }

                if (string.IsNullOrEmpty(path) || !TagPath.IsValidPath(path, out _))
                {
                    _Warnings.Add($"Tag {id} has no valid name (\"{path}\"), so it can't be added. Its references stay unresolved.");
                    continue;
                }

                var added = TagOperations.AddPath(_Data, path, null, out var outcome, id);
                _Keep.Add(added);

                if (outcome != TagOperations.AddPathOutcome.Created || added != id)
                    _Warnings.Add($"\"{path}\" is already tag {added}, so references to tag {id} with that name stay unresolved.");
            }
        }

        private static void RemoveRowsNotKept(TagRegistryData _Data, HashSet<TagGuid> _Keep)
        {
            // Parents of kept rows stay too.
            var parents = _Data.rows.ToDictionary(e => e.id, e => e.parent);
            foreach (var id in _Keep.ToList())
                for (var parent = parents.GetValueOrDefault(id); !parent.IsZero && _Keep.Add(parent); parent = parents.GetValueOrDefault(parent)) { }

            _Data.rows.RemoveAll(e => !_Keep.Contains(e.id));
        }

        private static List<TagFileEntry> ReadTagFiles(TagPackage _Package, PackageRegistryResult _Result)
        {
            var files = TagPackage.AssetPathsUnder(_Package.Root)
                .Where(e => e.EndsWith("." + TagFile.EXTENSION, StringComparison.OrdinalIgnoreCase))
                .Select(e => (e, File.ReadAllText(e)))
                .ToList();

            if (files.Count == 0) return new List<TagFileEntry>();

            var parsed = TagFile.Parse(files);
            _Result.Warnings.AddRange(parsed.Errors);

            return parsed.Entries.OrderBy(e => Depth(e.Path)).ThenBy(e => e.Path, StringComparer.Ordinal).ToList();
        }

        private static TagGuid FindRegistryOf(TagRegistrySnapshot _Snapshot, TagGuid _Id)
        {
            foreach (var registry in _Snapshot.Registries)
                if (registry.Contains(_Id))
                    return registry.Id;

            return TagGuid.Zero;
        }

        private static string GeneratedPath(string _PackageName, TagGuid _RegistryId, bool _Several, IEnumerable<string> _Taken)
        {
            string folder = GeneratedFolder.TrimEnd('/');
            string path = _Several
                ? $"{folder}/{_PackageName}.{_RegistryId.ToString()[..8]}.{TagRegistryFile.EXTENSION}"
                : $"{folder}/{_PackageName}.{TagRegistryFile.EXTENSION}";

            // Another registry already uses that file name.
            if (_Taken.Contains(path) || File.Exists(path))
                path = $"{folder}/{_PackageName}.{_RegistryId}.{TagRegistryFile.EXTENSION}";

            return path;
        }

        private static int Depth(string _Path) => string.IsNullOrEmpty(_Path) ? 0 : _Path.Count(e => e == TagPath.SEPARATOR);
        #endregion
    }
}
