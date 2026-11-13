using System;
using System.Globalization;

using LMK.Runtime.Registry;

namespace LMK.Editor.Registry.Tests
{
    /// <summary>
    /// Row data of the test kind: a value, and an optional reference to another row of the same registry,
    /// linked to that row's index when the database is built.
    /// </summary>
    [Serializable]
    public struct TestRow
    {
        public float value;
        public RegistryGuid bound;
        public int boundRow;

        public TestRow(float _Value, RegistryGuid _Bound = default)
        {
            value = _Value;
            bound = _Bound;
            boundRow = RegistryResolution.NO_ROW;
        }
    }



    /// <summary>
    /// Database of the test kind. In its own file so it has a script asset.
    /// </summary>
    public sealed class TestDatabase : RegistryDatabase<TestDatabase, TestRow>
    {
        /// <summary>
        /// How many times the kind's derived tables were built.
        /// </summary>
        [NonSerialized] public int lookupsBuilt;

        /// <summary>
        /// Resolution of the first row, made while the derived tables were built.
        /// </summary>
        [NonSerialized] public RegistryResolution firstRowDuringBuild;

        protected override void OnLookupsBuilt()
        {
            lookupsBuilt++;
            if (RowCount > 0)
                firstRowDuringBuild = Resolve(GetRowRegistryId(0), GetRowId(0));
        }

        /// <summary>
        /// A builder that links <see cref="TestRow.bound"/> to <see cref="TestRow.boundRow"/>.
        /// </summary>
        public static RegistryDatabaseBuilder<TestRow> NewBuilder() => new((_Row, _Hash) =>
            _Hash.Append(_Row.value.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(_Row.bound))
        {
            Link = (TestRow _Data, in RegistryDatabaseBuilder<TestRow>.LinkContext _Context) =>
            {
                _Data.boundRow = _Context.IndexOf(_Data.bound);
                return _Data;
            },
        };
    }
}
