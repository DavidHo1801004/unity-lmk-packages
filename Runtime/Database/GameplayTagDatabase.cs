using System;
using System.Collections.Generic;

using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Runtime.GameplayTags
{
    /// <summary>
    /// Kind of a tag registry.
    /// </summary>
    public enum TagRegistryKind
    {
        /// <summary>
        /// The project's main registry. One per project.
        /// </summary>
        Main,

        /// <summary>
        /// A registry shipped inside a package. Read-only.
        /// </summary>
        Package,

        /// <summary>
        /// A registry generated in the project for a package that ships none.
        /// </summary>
        Generated,
    }



    /// <summary>
    /// Identity and display data of one registry in a <see cref="GameplayTagDatabase"/>.
    /// </summary>
    public readonly struct TagRegistryInfo
    {
        public readonly RegistryGuid Id;
        public readonly string Name;
        public readonly TagRegistryKind Kind;

        public TagRegistryInfo(RegistryGuid _Id, string _Name, TagRegistryKind _Kind)
        {
            Id = _Id;
            Name = _Name;
            Kind = _Kind;
        }
    }



    /// <summary>
    /// A tag's own data in a <see cref="GameplayTagDatabase"/>, stored parallel to the registry rows.
    /// The tag's ID, path and registry are the row's ID, name and registry.
    /// </summary>
    [Serializable]
    public struct TagRow
    {
        [SerializeField] internal int parent;
        [SerializeField] internal int depth;
        [SerializeField] internal string segment;
        [SerializeField] internal bool isImplicit;

        // Only while building: the parent's ID, turned into its index once indices are final.
        [NonSerialized] internal RegistryGuid parentId;

        /// <summary>
        /// Index of the parent row, or -1 for a top level tag.
        /// </summary>
        public int Parent => parent;

        /// <summary>
        /// Number of ancestors: 0 for a top level tag.
        /// </summary>
        public int Depth => depth;

        /// <summary>
        /// Last segment of the tag's path.
        /// </summary>
        public string Segment => segment ?? string.Empty;

        /// <summary>
        /// Whether the tag exists only as the parent of other tags.
        /// </summary>
        public bool IsImplicit => isImplicit;
    }



    /// <summary>
    /// Read-only lookup and hierarchy data for every live tag of every loaded registry. <br/>
    /// Resolves a reference (registry ID, tag ID) to a <see cref="TagHandle"/>, with no name fallback.
    /// </summary>
    /// <remarks>
    /// The registry database of the tag kind: a row is a tag, and a row's name is the tag's path. <br/>
    /// In a player, the database is baked at build time, preloaded, and found through
    /// <see cref="Resources"/> as a fallback. In the editor, it is built from the registry files and kept
    /// up to date by the editor tooling. <br/>
    /// Use <see cref="GameplayTagDatabaseBuilder"/> to create one.
    /// </remarks>
    public sealed class GameplayTagDatabase : RegistryDatabase<GameplayTagDatabase, TagRow>
    {
        /// <summary>
        /// Name of the baked database asset inside a <c>Resources</c> folder.
        /// </summary>
        public const string RESOURCE_NAME = nameof(GameplayTagDatabase);



        #region Active Database
        static GameplayTagDatabase()
        {
            RegistryDatabase<GameplayTagDatabase, TagRow>.ActiveChanged += (_, _) => ActiveChanged?.Invoke();
        }

        /// <summary>
        /// Called after <see cref="RegistryDatabase{TSelf, TRow}.Active"/> changed. <br/>
        /// <see cref="ActiveDatabaseChanged"/> also passes the previous and the new database.
        /// </summary>
        public new static event Action ActiveChanged;

        /// <summary>
        /// Called after <see cref="RegistryDatabase{TSelf, TRow}.Active"/> changed, with the previous and the new
        /// database. A previous database that is being replaced stays alive until every listener has returned.
        /// </summary>
        public static event Action<GameplayTagDatabase, GameplayTagDatabase> ActiveDatabaseChanged
        {
            add => RegistryDatabase<GameplayTagDatabase, TagRow>.ActiveChanged += value;
            remove => RegistryDatabase<GameplayTagDatabase, TagRow>.ActiveChanged -= value;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Set by the editor tooling: makes sure an editor database is active.
        /// </summary>
        internal new static Action EditorEnsureActive
        {
            get => RegistryDatabase<GameplayTagDatabase, TagRow>.EditorEnsureActive;
            set => RegistryDatabase<GameplayTagDatabase, TagRow>.EditorEnsureActive = value;
        }
#endif

        /// <summary>
        /// Make <paramref name="_Database"/> the active database and start a new generation.
        /// </summary>
        internal new static void SetActive(GameplayTagDatabase _Database, bool _DestroyPrevious = false)
            => RegistryDatabase<GameplayTagDatabase, TagRow>.SetActive(_Database, _DestroyPrevious);

        /// <summary>
        /// Resolve a reference against <see cref="RegistryDatabase{TSelf, TRow}.Active"/>. Without an active
        /// database, any assigned reference is <see cref="TagStatus.MissingRegistry"/>.
        /// </summary>
        public new static TagResolution ResolveActive(RegistryGuid _Registry, RegistryGuid _Id)
            => TagResolution.From(RegistryDatabase<GameplayTagDatabase, TagRow>.ResolveActive(_Registry, _Id));
        #endregion



        /// <summary>
        /// Number of live tags across all registries. Handles range from 0 to <c>TagCount - 1</c>.
        /// </summary>
        public int TagCount => RowCount;



        #region Resolution
        /// <summary>
        /// Resolve a reference. Exact: no name check and no fallback.
        /// </summary>
        public new TagResolution Resolve(RegistryGuid _Registry, RegistryGuid _Id)
            => TagResolution.From(base.Resolve(_Registry, _Id));

        /// <summary>
        /// Find the live tag at <paramref name="_Path"/> in a registry. Path lookups do not survive renames:
        /// use them for debugging and console commands only.
        /// </summary>
        public TagHandle FindByPath(RegistryGuid _Registry, string _Path)
            => new(FindByName(_Registry, _Path));
        #endregion



        #region Tag Data
        /// <summary>
        /// Full path of a live tag, e.g. "Status.Debuff.Stun".
        /// </summary>
        public string GetPath(TagHandle _Handle) => GetRowName(_Handle.Index);

        /// <summary>
        /// Last segment of a live tag's path, e.g. "Stun".
        /// </summary>
        public string GetSegment(TagHandle _Handle) => IsRowInRange(_Handle.Index) ? Rows[_Handle.Index].Segment : string.Empty;

        /// <summary>
        /// Parent of a live tag, or <see cref="TagHandle.None"/> for a top level tag.
        /// </summary>
        public TagHandle GetParent(TagHandle _Handle) => IsRowInRange(_Handle.Index) ? new TagHandle(Rows[_Handle.Index].parent) : TagHandle.None;

        /// <summary>
        /// Number of ancestors: 0 for a top level tag.
        /// </summary>
        public int GetDepth(TagHandle _Handle) => IsRowInRange(_Handle.Index) ? Rows[_Handle.Index].depth : -1;

        /// <summary>
        /// Whether the tag exists only as the parent of other tags.
        /// </summary>
        public bool IsImplicit(TagHandle _Handle) => IsRowInRange(_Handle.Index) && Rows[_Handle.Index].isImplicit;

        public RegistryGuid GetId(TagHandle _Handle) => GetRowId(_Handle.Index);

        public RegistryGuid GetRegistryId(TagHandle _Handle) => GetRowRegistryId(_Handle.Index);

        /// <summary>
        /// A reference to a live tag, as an assignment would write it.
        /// </summary>
        public GameplayTag GetTag(TagHandle _Handle)
        {
            if (!IsRowInRange(_Handle.Index)) return GameplayTag.None;

            return GameplayTag.FromId(GetRowRegistryId(_Handle.Index), GetRowId(_Handle.Index), GetRowName(_Handle.Index));
        }

        /// <summary>
        /// Whether <paramref name="_Handle"/> is <paramref name="_Ancestor"/> or one of its descendants.
        /// Never true across registries.
        /// </summary>
        public bool IsSameOrDescendantOf(TagHandle _Handle, TagHandle _Ancestor)
        {
            if (!IsRowInRange(_Handle.Index) || !IsRowInRange(_Ancestor.Index)) return false;

            var rows = Rows;
            int ancestorDepth = rows[_Ancestor.Index].depth;
            for (int index = _Handle.Index; index >= 0 && rows[index].depth >= ancestorDepth; index = rows[index].parent)
                if (index == _Ancestor.Index)
                    return true;

            return false;
        }

        /// <summary>
        /// Add the direct children of a live tag to <paramref name="_Result"/>.
        /// </summary>
        public void GetChildren(TagHandle _Handle, List<TagHandle> _Result)
        {
            if (!IsRowInRange(_Handle.Index) || _Result == null) return;

            var rows = Rows;
            var (firstRow, rowCount) = GetRegistryRows(GetRowRegistryIndex(_Handle.Index));
            for (int i = _Handle.Index + 1; i < firstRow + rowCount; i++)
                if (rows[i].parent == _Handle.Index)
                    _Result.Add(new TagHandle(i));
        }
        #endregion



        #region Registries
        public new TagRegistryInfo GetRegistry(int _Index)
        {
            var info = base.GetRegistry(_Index);
            return new TagRegistryInfo(info.Id, info.Name, (TagRegistryKind)info.Origin);
        }
        #endregion



        #region Diagnostics
        /// <summary>
        /// Log one warning per unresolved reference, in Play Mode and in development builds.
        /// </summary>
        internal static void ReportUnresolved(in GameplayTag _Tag, TagStatus _Status)
        {
            var tag = _Tag;
            ReportUnresolved(tag.RegistryId, tag.Id, () =>
                $"Gameplay tag \"{tag.LastKnownName}\" does not resolve ({_Status}, registry {tag.RegistryId}, id {tag.Id}). " +
                "It matches nothing.");
        }
        #endregion
    }
}
