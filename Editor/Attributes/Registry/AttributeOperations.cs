using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using LMK.Editor.Registry;
using LMK.Runtime.AbilitySystem;
using LMK.Runtime.Registry;

namespace LMK.Editor.AbilitySystem
{
    /// <summary>
    /// The editable data of an attribute, besides its name.
    /// </summary>
    internal struct AttributeMetadata
    {
        public float defaultValue;
        public AttributeBound min;
        public AttributeBound max;
        public AttributeMaxChange onMaxChange;
        public AttributeFlags flags;
        public string description;

        public static AttributeMetadata Of(in AttributeRegistryRow _Row) => new()
        {
            defaultValue = _Row.defaultValue,
            min = _Row.min,
            max = _Row.max,
            onMaxChange = _Row.onMaxChange,
            flags = _Row.flags,
            description = _Row.description,
        };
    }



    /// <summary>
    /// What deleting an attribute would affect: the attributes it bounds, and the assets that reference it.
    /// </summary>
    internal sealed class AttributeDeletePlan
    {
        public string Name;

        /// <summary>
        /// Attributes that use it as a bound, as "Registry: Name". Those of its own registry lose that bound.
        /// </summary>
        public List<string> Dependents = new();

        /// <summary>
        /// Saved assets referencing it, from the reference index.
        /// </summary>
        public List<string> ReferencingAssets = new();

        /// <summary>
        /// False if the user cancelled the reference index scan: assets may be missing from the list.
        /// </summary>
        public bool ReferenceCheckComplete = true;

        public string Message;
    }



    /// <summary>
    /// The attribute operations on the main registry: Add, Rename, Edit metadata and Delete. <br/>
    /// Every edit is staged through the registry core (<see cref="RegistryEdit.Apply{TRow, TAsset}"/>): it checks
    /// the lock, validates a copy, records Undo and changes only the staged state. Save writes every
    /// kind's staged edits, and Discard drops them. Package and generated registries are read-only here.
    /// </summary>
    /// <remarks>
    /// Metadata changes apply to units initialised afterwards; live units keep their values.
    /// </remarks>
    internal static class AttributeOperations
    {
        private const string NOT_IN_MAIN = "This attribute is not in the main registry. Package and generated registries are read-only.";
        private const int MAX_LISTED = 10;

        private static AttributeKind Kind => AttributeKind.Instance;



        #region Session
        public static bool HasPendingChanges => Kind.HasPendingChanges;

        /// <summary>
        /// Write the staged edits of every registry kind. Refused if a file changed on disk since the edits began, if
        /// any registry is invalid, or without the lock.
        /// </summary>
        public static RegistryResult Save() => RegistrySession.Save();

        /// <summary>
        /// Drop the staged edits of every registry kind.
        /// </summary>
        public static void Discard() => RegistrySession.Discard();

        /// <summary>
        /// The problems with registry content, by the attribute registry rules.
        /// </summary>
        public static List<AttributeIssue> Validate(RegistryData<AttributeRegistryRow> _Data) => AttributeValidation.Check(_Data);
        #endregion



        #region Operations
        /// <summary>
        /// Create an empty main registry with a new registry ID. Written immediately. Not undoable.
        /// </summary>
        public static RegistryResult CreateMainRegistry() => Kind.CreateMainRegistry();

        /// <summary>
        /// Add an attribute with no bounds.
        /// </summary>
        public static RegistryResult Add(string _Name, float _DefaultValue = 0f, string _Description = null)
        {
            if (!AttributeName.IsValid(_Name, out string error))
                return RegistryResult.Fail(error);

            return RegistryEdit.Apply(Kind, "Add Gameplay Attribute", data =>
            {
                if (data.rows.Any(e => e.data.name == _Name))
                    return RegistryResult.Fail($"Attribute \"{_Name}\" already exists.");

                var id = NewUniqueId(data);
                data.rows.Add(AttributeRegistryRow.Create(id, _Name, _DefaultValue, _Description: _Description));
                return RegistryResult.Ok(id);
            });
        }

        /// <summary>
        /// Rename an attribute, group included. References, bounds and generated constants follow, because they hold
        /// IDs.
        /// </summary>
        public static RegistryResult Rename(RegistryGuid _Id, string _NewName)
        {
            if (!AttributeName.IsValid(_NewName, out string error))
                return RegistryResult.Fail(error);

            return RegistryEdit.Apply(Kind, "Rename Gameplay Attribute", data =>
            {
                int index = data.IndexOf(_Id);
                if (index < 0) return RegistryResult.Fail(NOT_IN_MAIN);

                var row = data.rows[index];
                if (row.data.name == _NewName) return RegistryResult.Ok(_Id);

                if (data.rows.Any(e => e.data.name == _NewName))
                    return RegistryResult.Fail($"Another attribute is already named \"{_NewName}\".");

                row.data.name = _NewName;
                data.rows[index] = row;
                return RegistryResult.Ok(_Id);
            });
        }

        /// <summary>
        /// Change an attribute's default, bounds, max-change policy, flags and description as one staged operation.
        /// A bound may name an attribute of this registry or of another loaded registry, such as a package's.
        /// </summary>
        public static RegistryResult EditMetadata(RegistryGuid _Id, AttributeMetadata _Metadata)
        {
            return RegistryEdit.Apply(Kind, "Edit Gameplay Attribute", data =>
            {
                int index = data.IndexOf(_Id);
                if (index < 0) return RegistryResult.Fail(NOT_IN_MAIN);

                var row = data.rows[index];
                foreach (var (bound, which) in new[] { (_Metadata.min, "min"), (_Metadata.max, "max") })
                {
                    string problem = CheckForeignBound(data, bound);
                    if (problem != null)
                        return RegistryResult.Fail($"The {which} of \"{row.data.name}\" {problem}");
                }

                row.data.defaultValue = _Metadata.defaultValue;
                row.data.min = _Metadata.min;
                row.data.max = _Metadata.max;
                row.data.onMaxChange = _Metadata.onMaxChange;
                row.data.flags = _Metadata.flags;
                row.data.description = _Metadata.description ?? string.Empty;
                data.rows[index] = row;
                return RegistryResult.Ok(_Id);
            });
        }

        /// <summary>
        /// Delete an attribute. Attributes of the main registry that use it as a bound lose that bound.
        /// </summary>
        public static RegistryResult Delete(RegistryGuid _Id)
        {
            return RegistryEdit.Apply(Kind, "Delete Gameplay Attribute", data =>
            {
                int index = data.IndexOf(_Id);
                if (index < 0) return RegistryResult.Fail(NOT_IN_MAIN);

                data.rows.RemoveAt(index);

                for (int i = 0; i < data.rows.Count; i++)
                {
                    var row = data.rows[i];
                    bool changed = false;

                    if (row.data.min.Names(data.registryId, _Id)) { row.data.min = AttributeBound.None; changed = true; }
                    if (row.data.max.Names(data.registryId, _Id)) { row.data.max = AttributeBound.None; changed = true; }

                    if (changed) data.rows[i] = row;
                }

                return RegistryResult.Ok(_Id);
            });
        }
        #endregion



        #region Queries
        /// <summary>
        /// The attributes of every loaded registry that use (<paramref name="_Registry"/>, <paramref name="_Id"/>) as a
        /// bound, as (registry, attribute ID).
        /// </summary>
        public static List<(RegistryGuid Registry, RegistryGuid Id)> FindDependents(RegistryGuid _Registry, RegistryGuid _Id)
        {
            var result = new List<(RegistryGuid, RegistryGuid)>();
            foreach (var registry in Kind.Snapshot.Registries)
                foreach (var row in registry.Data.rows)
                    if (row.data.min.Names(_Registry, _Id) || row.data.max.Names(_Registry, _Id))
                        result.Add((registry.Id, row.id));

            return result;
        }

        /// <summary>
        /// Plan the deletion of a main registry attribute, or <see langword="null"/> if it isn't there. Lists the
        /// attributes that lose it as a bound first, then the saved assets that reference it.
        /// </summary>
        public static AttributeDeletePlan PlanDelete(RegistryGuid _Id, bool _ShowProgress)
        {
            var main = Kind.Snapshot.Main;
            if (main == null || main.IsReadOnly || !main.Contains(_Id)) return null;

            var plan = new AttributeDeletePlan { Name = main.GetName(_Id) };
            var snapshot = Kind.Snapshot;
            foreach (var (registry, id) in FindDependents(main.Id, _Id))
            {
                var owner = snapshot.Find(registry);
                string name = owner?.GetName(id) ?? id.ToString();
                plan.Dependents.Add(owner == main ? name : $"{owner?.Name}: {name}");
            }

            plan.Dependents.Sort(StringComparer.Ordinal);
            plan.ReferenceCheckComplete = RegistryReferenceIndex.TryFindAssets(Kind, new[] { (main.Id, _Id) }, _ShowProgress, out plan.ReferencingAssets);
            plan.Message = BuildDeleteMessage(plan);
            return plan;
        }

        private static string BuildDeleteMessage(AttributeDeletePlan _Plan)
        {
            var message = new StringBuilder($"Delete \"{_Plan.Name}\"?");

            if (_Plan.Dependents.Count > 0)
                message.Append($"\n\nThese attributes use it as a bound and lose that bound:\n\n{RegistryCommands.ListItems(_Plan.Dependents, MAX_LISTED)}");

            if (_Plan.ReferencingAssets.Count > 0)
                message.Append($"\n\nReferenced by {_Plan.ReferencingAssets.Count} saved asset(s):\n\n{RegistryCommands.ListItems(_Plan.ReferencingAssets, MAX_LISTED)}")
                       .Append("\n\nTheir references will show as (deleted) and do nothing at runtime. Undo restores them, and Relink Missing " +
                               "References can point them to an attribute with the same name later.");
            else
                message.Append("\n\nNo saved asset references it.");

            if (!_Plan.ReferenceCheckComplete)
                message.Append("\n\nThe reference check was cancelled, so this list may be incomplete.");

            message.Append("\n\nUnsaved scenes and assets are not checked.");
            return message.ToString();
        }
        #endregion



        #region Helpers
        /// <summary>
        /// Why a bound naming another registry's attribute can't be set, or <see langword="null"/>. Bounds within the
        /// registry are checked by <see cref="AttributeValidation"/>.
        /// </summary>
        private static string CheckForeignBound(RegistryData<AttributeRegistryRow> _Data, in AttributeBound _Bound)
        {
            if (!_Bound.IsAttribute || _Bound.RegistryId == _Data.registryId) return null;

            var registry = Kind.Snapshot.Find(_Bound.RegistryId);
            if (registry == null)
                return $"names an attribute of registry {_Bound.RegistryId}, which isn't loaded.";

            if (registry.Origin == RegistryOrigin.Main)
                return $"names an attribute of \"{registry.Name}\", another project's main registry. Bounds may name attributes of this " +
                       "registry or of package registries only.";

            if (!registry.TryGetRow(_Bound.AttributeId, out var target))
                return $"names attribute {_Bound.AttributeId}, which isn't in \"{registry.Name}\".";

            if (target.IsMeta)
                return $"names the meta attribute \"{target.name}\", which only holds a scratch value.";

            return null;
        }

        internal static RegistryGuid NewUniqueId(RegistryData<AttributeRegistryRow> _Data)
        {
            RegistryGuid id;
            do id = RegistryGuid.NewGuid();
            while (_Data.IndexOf(id) >= 0);

            return id;
        }
        #endregion
    }
}
