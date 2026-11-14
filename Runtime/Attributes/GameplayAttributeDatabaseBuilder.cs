using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using UnityEngine;

using LMK.Runtime.Registry;

namespace LMK.Runtime.AbilitySystem
{
    /// <summary>
    /// The clamp bounds of a database's content form a cycle, so it can't be baked.
    /// </summary>
    public sealed class AttributeClampCycleException : InvalidOperationException
    {
        /// <summary>
        /// The names of the attributes on the cycle.
        /// </summary>
        public IReadOnlyList<string> Attributes { get; }

        public AttributeClampCycleException(IReadOnlyList<string> _Attributes)
            : base($"Attribute clamp bounds form a cycle: {string.Join(", ", _Attributes)}. A bound can't depend on itself.")
        {
            Attributes = _Attributes;
        }
    }



    /// <summary>
    /// Builds a <see cref="GameplayAttributeDatabase"/> from registry rows. <br/>
    /// Registries are ordered by ID and attributes by name, whatever the input order, so the same content always gives
    /// the same handles and content hash.
    /// </summary>
    /// <remarks>
    /// Registry files are validated before they reach the builder. <see cref="Build"/> still refuses content whose
    /// bounds form a cycle, even across registries, so a hand-edited file can't reach a player.
    /// </remarks>
    public sealed class GameplayAttributeDatabaseBuilder
    {
        /// <summary>
        /// One attribute of a registry.
        /// </summary>
        public readonly struct Row
        {
            public readonly RegistryGuid Id;
            public readonly string Name;
            public readonly AttributeRow Data;

            public Row(RegistryGuid _Id, string _Name, AttributeRow _Data)
            {
                Id = _Id;
                Name = _Name;
                Data = _Data;
            }

            public Row(RegistryGuid _Id, string _Name, float _DefaultValue = 0f, AttributeBound _Min = default, AttributeBound _Max = default,
                       AttributeMaxChange _OnMaxChange = AttributeMaxChange.Clamp, AttributeFlags _Flags = AttributeFlags.None)
                : this(_Id, _Name, new AttributeRow(_DefaultValue, _Min, _Max, _OnMaxChange, _Flags)) { }
        }

        private readonly RegistryDatabaseBuilder<AttributeRow> builder = new(AppendHash);



        /// <summary>
        /// Add a registry and its attributes.
        /// </summary>
        /// <exception cref="ArgumentException"> If the registry ID is zero or already added. </exception>
        public GameplayAttributeDatabaseBuilder AddRegistry(RegistryGuid _Id, string _Name, RegistryOrigin _Origin, IEnumerable<Row> _Rows)
        {
            var rows = _Rows?.Select(e => new RegistryDatabaseBuilder<AttributeRow>.Row(e.Id, e.Name, e.Data));
            builder.AddRegistry(_Id, _Name, _Origin, rows);
            return this;
        }

        /// <summary>
        /// Create a new database instance. Used for builds and tests.
        /// </summary>
        /// <exception cref="AttributeClampCycleException"> If clamp bounds form a cycle. </exception>
        public GameplayAttributeDatabase Build()
        {
            var database = BuildLenient();
            if (!database.HasClampCycle) return database;

            var names = database.ClampCycle.Select(database.GetName).ToList();
            if (Application.isPlaying) UnityEngine.Object.Destroy(database);
            else UnityEngine.Object.DestroyImmediate(database);

            throw new AttributeClampCycleException(names);
        }

        /// <summary>
        /// Create a new database instance even if clamp bounds form a cycle: the bounds that close the cycle are
        /// ignored. The editor uses it, so a hand-edited registry doesn't stop every attribute from resolving.
        /// </summary>
        public GameplayAttributeDatabase BuildLenient() => builder.Build<GameplayAttributeDatabase>();

        /// <summary>
        /// Replace the content of an existing database instance.
        /// </summary>
        public void BuildInto(GameplayAttributeDatabase _Database) => builder.BuildInto(_Database);



        // The core hashes registries, IDs, registry indices and names.
        private static void AppendHash(AttributeRow _Row, StringBuilder _Builder)
            => _Builder.Append(_Row.defaultValue.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                       .Append(_Row.min).Append('|')
                       .Append(_Row.max).Append('|')
                       .Append((int)_Row.onMaxChange).Append('|')
                       .Append((int)_Row.flags);
    }
}
