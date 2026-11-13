using System;
using System.Collections.Generic;

using UnityEngine;

namespace LMK.Runtime.Registry
{
    /// <summary>
    /// The database of one registry kind: the core lookups of <see cref="RegistryDatabase"/>, the kind's own
    /// row data, and the kind's active database.
    /// </summary>
    /// <typeparam name="TSelf"> The kind's concrete database type, e.g. <c>GameplayTagDatabase</c>. </typeparam>
    /// <typeparam name="TRow"> The kind's row data, stored parallel to the core's row columns. </typeparam>
    /// <remarks>
    /// Each kind has its own active database, generation and events. <br/>
    /// In a player, the baked database is preloaded and activates itself; a <see cref="Resources"/> asset named
    /// after <typeparamref name="TSelf"/> is the fallback. In the editor, the editor tooling builds and activates it.
    /// </remarks>
    public abstract class RegistryDatabase<TSelf, TRow> : RegistryDatabase
        where TSelf : RegistryDatabase<TSelf, TRow>
        where TRow : struct
    {
        /// <summary>
        /// Name of the baked database asset inside a <c>Resources</c> folder: the database type's name.
        /// </summary>
        public static string ResourceName => typeof(TSelf).Name;

        [SerializeField]
        private TRow[] rows = Array.Empty<TRow>();

        /// <summary>
        /// The kind's data of every row, indexed like the core's row columns.
        /// </summary>
        protected TRow[] Rows => rows;

        /// <summary>
        /// The kind's data of a row, or <see langword="default"/> if <paramref name="_Row"/> is out of range.
        /// </summary>
        public TRow GetRowData(int _Row) => IsRowInRange(_Row) ? rows[_Row] : default;

        internal void Initialize(RegistryRecord[] _Registries, RegistryGuid[] _RowIds, int[] _RowRegistries,
                                 string[] _RowNames, TRow[] _Rows, string _ContentHash)
        {
            if (_Rows.Length != _RowIds.Length)
                throw new ArgumentException("Kind rows must match the core rows.", nameof(_Rows));

            InitializeCore(_Registries, _RowIds, _RowRegistries, _RowNames, _ContentHash, () => rows = _Rows);
        }



        #region Active Database
        private static TSelf active;
        private static int generation;
        private static int generationCounter;

        /// <summary>
        /// Called after <see cref="Active"/> changed, with the previous and the new database. <br/>
        /// A previous database that is being replaced stays alive until every listener has returned,
        /// so listeners may still read it, e.g. to translate row indices into IDs.
        /// </summary>
        public static event Action<TSelf, TSelf> ActiveChanged;

#if UNITY_EDITOR
        /// <summary>
        /// Set by the editor tooling: makes sure an editor database is active.
        /// </summary>
        protected internal static Action EditorEnsureActive;
#endif

        /// <summary>
        /// The database every reference of this kind resolves against. <see langword="null"/> until one is loaded.
        /// </summary>
        public static TSelf Active
        {
            get
            {
                if (active == null)
                    LoadDefault();

                return active;
            }
        }

        /// <summary>
        /// Changes every time <see cref="Active"/> changes. 0 means no database is active yet,
        /// and nothing may be cached under it.
        /// </summary>
        public static int Generation => Active != null ? generation : 0;

        /// <summary>
        /// Make <paramref name="_Database"/> the active database and start a new generation.
        /// </summary>
        /// <param name="_DestroyPrevious">
        /// Destroy the database it replaces, after <see cref="ActiveChanged"/> has returned.
        /// </param>
        protected internal static void SetActive(TSelf _Database, bool _DestroyPrevious = false)
        {
            var previous = active;
            active = _Database;

            if (_Database == null)
            {
                generation = 0;
            }
            else
            {
                generationCounter = generationCounter == int.MaxValue ? 1 : generationCounter + 1;
                generation = generationCounter;
            }

            try
            {
                ActiveChanged?.Invoke(previous, _Database);
            }
            finally
            {
                if (_DestroyPrevious && previous != null && previous != _Database)
                {
                    if (Application.isPlaying) Destroy(previous);
                    else DestroyImmediate(previous);
                }
            }
        }

        private static void LoadDefault()
        {
#if UNITY_EDITOR
            EditorEnsureActive?.Invoke();
#else
            var database = Resources.Load<TSelf>(ResourceName);
            if (database != null)
                SetActive(database);
#endif
        }

        /// <summary>
        /// Resolve a reference against <see cref="Active"/>. Without an active database, any assigned
        /// reference is <see cref="RegistryStatus.MissingRegistry"/>.
        /// </summary>
        public static RegistryResolution ResolveActive(RegistryGuid _Registry, RegistryGuid _Id)
        {
            if (_Id.IsZero) return RegistryResolution.None;

            var database = Active;
            return database != null ? database.Resolve(_Registry, _Id) : RegistryResolution.MissingRegistry;
        }

#if !UNITY_EDITOR
        // The baked database is preloaded, so it becomes active as soon as the player starts.
        protected virtual void OnEnable()
        {
            if (active == null)
                SetActive((TSelf)this);
        }
#endif
        #endregion



        #region Diagnostics
        private static readonly HashSet<(RegistryGuid, RegistryGuid)> reportedUnresolved = new();

        /// <summary>
        /// Log one warning per unresolved reference of this kind, in Play Mode and in development builds.
        /// Release builds stay silent.
        /// </summary>
        /// <param name="_Message"> Builds the warning. Only called when it is logged. </param>
        public static void ReportUnresolved(RegistryGuid _Registry, RegistryGuid _Id, Func<string> _Message)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Application.isPlaying) return;
            if (!reportedUnresolved.Add((_Registry, _Id))) return;

            Debug.LogWarning(_Message?.Invoke() ?? $"Reference to registry {_Registry}, id {_Id} does not resolve.");
#endif
        }
        #endregion
    }
}
