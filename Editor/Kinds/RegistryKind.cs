using System;
using System.Collections.Generic;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry
{
    /// <summary>
    /// A registry kind, such as gameplay tags or gameplay attributes, as the core sees it whatever its row type. <br/>
    /// Domain packages derive from <see cref="RegistryKindEditor{TRow, TAsset}"/> and register one instance with
    /// <see cref="RegistryKinds.Register"/>.
    /// </summary>
    public abstract class RegistryKind
    {
        private string mainRegistryPath;
        private string markersPath;

        /// <summary>
        /// Stable identifier, used in session keys, the reference index and settings, e.g. "tags". Never change it.
        /// </summary>
        public abstract string Id { get; }

        /// <summary>
        /// Name shown in titles and dialogs, e.g. "Gameplay Tags".
        /// </summary>
        public abstract string DisplayName { get; }

        /// <summary>
        /// Extension of the kind's registry files, without the dot, e.g. "tagregistry".
        /// </summary>
        public abstract string FileExtension { get; }

        /// <summary>
        /// Project folder of the kind's files, e.g. "Assets/GameplayTags".
        /// </summary>
        public abstract string RootFolder { get; }

        /// <summary>
        /// File name of the main registry, without extension.
        /// </summary>
        protected virtual string MainRegistryFileName => "Main";

        /// <summary>
        /// Where the user's markers are saved, under <c>UserSettings/</c> by default.
        /// </summary>
        protected virtual string DefaultMarkersPath => $"UserSettings/LMKRegistry/{Id}/Markers.json";

        /// <summary>
        /// Where the main registry lives. Tests may change it.
        /// </summary>
        public string MainRegistryPath
        {
            get => mainRegistryPath ??= $"{RootFolder}/{MainRegistryFileName}.{FileExtension}";
            set => mainRegistryPath = value;
        }

        /// <summary>
        /// Where the user's markers are saved. Tests may change it.
        /// </summary>
        public string MarkersPath
        {
            get => markersPath ??= DefaultMarkersPath;
            set => markersPath = value;
        }

        /// <summary>
        /// Files the kind writes from its saved registries: its generated code, and the code's assembly definition when
        /// the project gives it one. They are locked with the main registry.
        /// </summary>
        public virtual IReadOnlyList<string> GeneratedFiles
        {
            get
            {
                if (CodeStyle == null) return Array.Empty<string>();

                string assembly = CodeAssemblyPath;
                return assembly != null ? new[] { CodeOutputPath, assembly } : new[] { CodeOutputPath };
            }
        }



        #region Code Generation
        private string codeOutputPath;

        /// <summary>
        /// How the kind's generated constants look, or <see langword="null"/> for a kind without generated code.
        /// </summary>
        public virtual RegistryCodeStyle CodeStyle => null;

        /// <summary>
        /// Where the kind's code is generated: <c>&lt;RootFolder&gt;/Generated/&lt;FileName&gt;</c> by default, or
        /// <see langword="null"/> without a <see cref="CodeStyle"/>. Tests may change it.
        /// </summary>
        public string CodeOutputPath
        {
            get => CodeStyle == null ? null : codeOutputPath ?? $"{RootFolder}/Generated/{CodeStyle.FileName}";
            set => codeOutputPath = value;
        }

        /// <summary>
        /// The assembly definition next to the generated code, when the project puts the code in its own assembly
        /// (<see cref="RegistrySettings.GetCodeAssembly"/>), or <see langword="null"/>.
        /// </summary>
        public string CodeAssemblyPath
        {
            get
            {
                if (CodeStyle == null) return null;

                string assembly = RegistrySettings.instance.GetCodeAssembly(this);
                if (string.IsNullOrEmpty(assembly)) return null;

                string folder = System.IO.Path.GetDirectoryName(CodeOutputPath)?.Replace('\\', '/');
                return $"{folder}/{assembly}.asmdef";
            }
        }

        /// <summary>
        /// The registries that get a class in the generated code, as saved: the main registry, and package and
        /// generated registries. Other main registries (e.g. copied in from another project) are skipped.
        /// </summary>
        internal abstract List<RegistryCodeGen.Registry> CollectCodeRegistries();
        #endregion

        /// <summary>
        /// Every file a staged edit of this kind may lead to writing: the main registry and the generated files.
        /// </summary>
        public IReadOnlyList<string> LockedPaths
        {
            get
            {
                var paths = new List<string> { MainRegistryPath };
                paths.AddRange(GeneratedFiles);
                return paths;
            }
        }

        public bool IsRegistryPath(string _Path)
            => !string.IsNullOrEmpty(_Path) && _Path.EndsWith("." + FileExtension, StringComparison.OrdinalIgnoreCase);

        private string generatedRegistryFolder;

        /// <summary>
        /// Where registries generated for packages are written, <c>&lt;RootFolder&gt;/Packages</c> by default.
        /// Tests may change it.
        /// </summary>
        public string GeneratedRegistryFolder
        {
            get => generatedRegistryFolder ??= $"{RootFolder}/Packages";
            set => generatedRegistryFolder = value;
        }



        #region References
        /// <summary>
        /// The kind's serialized reference struct, e.g. <c>GameplayTag</c>. It must implement
        /// <see cref="IRegistryReference"/> with the frozen field layout.
        /// </summary>
        public abstract Type ReferenceType { get; }

        /// <summary>
        /// Whether a registry with this ID is known to the kind: loaded, or generated for a package that isn't installed.
        /// </summary>
        public abstract bool KnowsRegistry(RegistryGuid _Registry);

        /// <summary>
        /// Whether any loaded registry of the kind has a row with this ID.
        /// </summary>
        public abstract bool HasRowAnywhere(RegistryGuid _Row);

        /// <summary>
        /// What one row is called in messages, in lower case, e.g. "tag".
        /// </summary>
        public abstract string RowNoun { get; }

        /// <summary>
        /// The tool that edits the kind's registries, for messages, e.g. "Gameplay Tag Manager".
        /// </summary>
        public abstract string EditorName { get; }

        /// <summary>
        /// Whether a stored reference resolves to no row: the row was deleted, or its registry isn't loaded. A zero
        /// registry (a prefab override that changed only the row ID) is missing if no registry has the row.
        /// </summary>
        public abstract bool IsMissing(RegistryGuid _Registry, RegistryGuid _Id);

        /// <summary>
        /// The current name of a live row, or <see langword="null"/>.
        /// </summary>
        public abstract string GetRowName(RegistryGuid _Registry, RegistryGuid _Id);

        /// <summary>
        /// The live row a missing reference named <paramref name="_Name"/> would be relinked to: the row with that name
        /// in the same registry first, then in the main registry.
        /// </summary>
        public abstract bool TryFindRelinkTarget(RegistryGuid _Registry, string _Name,
                                                 out RegistryGuid _TargetRegistry, out RegistryGuid _TargetId);

        /// <summary>
        /// ID of the main registry, or zero if there is none.
        /// </summary>
        public abstract RegistryGuid MainRegistryId { get; }

        /// <summary>
        /// Whether the project has a main registry that can be edited.
        /// </summary>
        public abstract bool HasEditableMain { get; }

        /// <summary>
        /// The row of the main registry named <paramref name="_Name"/>.
        /// </summary>
        public abstract bool TryFindInMain(string _Name, out RegistryGuid _Id);

        /// <summary>
        /// Stage rows for names that imported assets reference, in the main registry, as one operation with one Undo
        /// step. Each row keeps the incoming ID if the main registry doesn't use it yet.
        /// </summary>
        /// <param name="_Ids"> Filled with the row each name now maps to. </param>
        /// <param name="_Warnings"> Names that could not be added, and why. </param>
        internal abstract RegistryResult AddImportedRows(IReadOnlyList<(string Name, RegistryGuid Id)> _Rows,
                                                         Dictionary<string, RegistryGuid> _Ids, List<string> _Warnings);
        #endregion



        #region Events
        /// <summary>
        /// Called after a new snapshot was published.
        /// </summary>
        public event Action SnapshotChanged;

        /// <summary>
        /// Called after the staged state changed: an edit, an undo or redo, a save, a discard, or a reload from disk.
        /// </summary>
        public event Action StateChanged;

        /// <summary>
        /// Called after a marker changed. Marker edits don't reload the registries.
        /// </summary>
        public event Action MarkersChanged;

        protected void RaiseSnapshotChanged() => SnapshotChanged?.Invoke();

        protected void RaiseStateChanged() => StateChanged?.Invoke();

        protected void RaiseMarkersChanged() => MarkersChanged?.Invoke();
        #endregion



        #region Session Hooks
        /// <summary>
        /// Called after this kind's staged registry was written by a save, before <see cref="RegistrySession.Saved"/>.
        /// Generated files are written here.
        /// </summary>
        protected virtual void OnSaved() { }

        /// <summary>
        /// After the kind's registries were written: its generated code is written now, then <see cref="OnSaved"/>.
        /// </summary>
        internal void InvokeOnSaved()
        {
            RegistryCodeGen.MarkPending(this);
            RegistryCodeGen.Flush(this);
            OnSaved();
        }

        /// <summary>
        /// Called after an empty main registry was created. Its generated code is only marked pending: it is
        /// written with the next save, or when the editor next flushes pending code.
        /// </summary>
        protected virtual void OnMainRegistryCreated() { }

        internal void InvokeOnMainRegistryCreated()
        {
            RegistryCodeGen.MarkPending(this);
            OnMainRegistryCreated();
        }

        /// <summary>
        /// Load every registry file of the kind again, and publish a new snapshot and editor database.
        /// </summary>
        public abstract void Reload();

        /// <summary>
        /// Whether there are staged edits that are not saved: registry content or markers.
        /// </summary>
        public bool HasPendingChanges => HasPendingData || HasPendingMarkers;

        internal abstract bool HasPendingData { get; }

        internal abstract bool HasPendingMarkers { get; }

        /// <summary>
        /// Whether the main registry changed on disk since the staged content was loaded or saved, while that
        /// content has unsaved edits. Saving is refused then.
        /// </summary>
        public abstract bool DiskChangedWhilePending { get; }

        /// <summary>
        /// Why the staged registry can't be saved, or <see langword="null"/>.
        /// </summary>
        internal abstract string CheckSave();

        /// <summary>
        /// Write the staged registry. Returns the path written.
        /// </summary>
        internal abstract string WriteStaged();

        internal abstract void SaveMarkers();

        /// <summary>
        /// Reload the published state after a save, without touching the staged content.
        /// </summary>
        internal abstract void AfterSave();

        internal abstract void DiscardStaged();

        internal abstract void OnUndoRedo();

        internal abstract void OnMainRegistryImported();
        #endregion



        #region Build Hooks
        private string databaseFolder;

        /// <summary>
        /// Where the baked runtime database is written for builds, <c>&lt;RootFolder&gt;/Generated/Resources</c> by
        /// default. The asset is named after the database type. Tests may change it.
        /// </summary>
        public string DatabaseFolder
        {
            get => databaseFolder ??= $"{RootFolder}/Generated/Resources";
            set => databaseFolder = value;
        }

        /// <summary>
        /// How a stored reference resolves in the current snapshot. A zero registry (a prefab override that changed only
        /// the row ID) is live if any registry has the row.
        /// </summary>
        public abstract RegistryStatus GetStatus(RegistryGuid _Registry, RegistryGuid _Id);

        /// <summary>
        /// Build a runtime database from the saved registries only, for builds. The caller owns it.
        /// </summary>
        public abstract RegistryDatabase BuildSavedDatabase();

        /// <summary>
        /// Every problem with a registry file of this kind: its errors, and a checksum that doesn't match.
        /// </summary>
        public abstract List<string> CheckFile(string _Text);

        /// <summary>
        /// Called before a player build bakes the kind's database: write pending generated files here.
        /// </summary>
        protected virtual void OnBeforeBuild() { }

        internal void InvokeOnBeforeBuild()
        {
            RegistryCodeGen.Flush(this);
            OnBeforeBuild();
        }
        #endregion



        #region Editor UI
        /// <summary>
        /// The markers to show: staged ones, or the saved ones before anything was staged.
        /// </summary>
        public abstract IReadOnlyList<RegistryMarker> Markers { get; }

        /// <summary>
        /// The marker colour of a row, if it has one.
        /// </summary>
        public abstract bool TryGetMarker(RegistryGuid _Registry, RegistryGuid _Row, out UnityEngine.Color _Color);

        /// <summary>
        /// Stage a row's marker colour. A fully transparent colour removes the marker.
        /// </summary>
        public abstract void SetMarker(RegistryGuid _Registry, RegistryGuid _Row, UnityEngine.Color _Color);

        /// <summary>
        /// Every live row of every loaded registry, with the registry's name.
        /// </summary>
        public abstract IEnumerable<(RegistryGuid Registry, string RegistryName, RegistryGuid Id, string Name)> EnumerateRows();

        /// <summary>
        /// Let the user pick a live row, e.g. the target of Replace References. By default a menu of every row, grouped by
        /// registry; a kind with its own picker (such as a tag tree) overrides it.
        /// </summary>
        /// <param name="_Anchor"> Where the picker opens, in GUI coordinates. </param>
        public virtual void ShowRowPicker(UnityEngine.Rect _Anchor, Action<RegistryGuid, RegistryGuid> _OnPicked)
        {
            var menu = new UnityEditor.GenericMenu();
            foreach (var (registry, registryName, id, name) in EnumerateRows())
            {
                var pickedRegistry = registry;
                var pickedId = id;
                menu.AddItem(new UnityEngine.GUIContent($"{registryName}/{name.Replace('/', '∕')}"), false, () => _OnPicked(pickedRegistry, pickedId));
            }

            if (menu.GetItemCount() == 0)
                menu.AddDisabledItem(new UnityEngine.GUIContent($"No {RowNoun} to pick"));

            menu.DropDown(_Anchor);
        }

        /// <summary>
        /// Whether the kind has an editor window that can show a registry (see <see cref="OpenRegistry"/>).
        /// </summary>
        public virtual bool CanOpenRegistry => false;

        /// <summary>
        /// Open the kind's editor window on a registry.
        /// </summary>
        public virtual void OpenRegistry(RegistryGuid _RegistryId) { }
        #endregion



        #region Package Hooks
        /// <summary>
        /// Plan the generated registries of this kind for a package: one per registry ID its references use that
        /// no other registry has. Returns the files to write, or an error.
        /// </summary>
        internal abstract List<(string Path, string Text, int Rows)> PlanPackageRegistries(RegistryPackage _Package, List<RegistryReference> _References,
                                                                                            bool _RemoveMissing, List<string> _Warnings, out string _Error);

        /// <summary>
        /// Generated registries of the kind whose package is installed in another version than they were generated from.
        /// </summary>
        public abstract List<(string Package, string AssetPath, string GeneratedVersion, string InstalledVersion)> FindOutdatedRegistries();

        /// <summary>
        /// Check the pack rules for the kind's part of a package. Returns the package's own registry ID, or zero.
        /// </summary>
        internal abstract RegistryGuid ValidatePack(RegistryPackage _Package, List<(string Path, RegistryReference Reference)> _References,
                                                                       RegistryGuid _PublishedRegistryId, List<string> _Errors);
        #endregion
    }



    /// <summary>
    /// The registry kinds of the project. Domain packages register theirs when the editor loads.
    /// </summary>
    public static class RegistryKinds
    {
        private static readonly List<RegistryKind> kinds = new();

        /// <summary>
        /// Called after a kind was registered or unregistered.
        /// </summary>
        public static event Action Changed;

        public static IReadOnlyList<RegistryKind> All => kinds;

        /// <summary>
        /// Register a kind. Registering the same instance twice does nothing.
        /// </summary>
        /// <exception cref="ArgumentException"> Another kind has the same ID or file extension. </exception>
        public static void Register(RegistryKind _Kind)
        {
            if (_Kind == null) throw new ArgumentNullException(nameof(_Kind));
            if (kinds.Contains(_Kind)) return;

            foreach (var kind in kinds)
            {
                if (kind.Id == _Kind.Id)
                    throw new ArgumentException($"A registry kind with ID \"{_Kind.Id}\" is already registered.", nameof(_Kind));
                if (string.Equals(kind.FileExtension, _Kind.FileExtension, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"A registry kind with extension \"{_Kind.FileExtension}\" is already registered.", nameof(_Kind));
            }

            kinds.Add(_Kind);
            Changed?.Invoke();
        }

        public static void Unregister(RegistryKind _Kind)
        {
            if (kinds.Remove(_Kind))
                Changed?.Invoke();
        }

        public static RegistryKind Find(string _Id)
        {
            foreach (var kind in kinds)
                if (kind.Id == _Id)
                    return kind;

            return null;
        }

        /// <summary>
        /// The kind whose registry files have the extension of <paramref name="_Path"/>, or <see langword="null"/>.
        /// </summary>
        public static RegistryKind FindByPath(string _Path)
        {
            foreach (var kind in kinds)
                if (kind.IsRegistryPath(_Path))
                    return kind;

            return null;
        }
    }
}
