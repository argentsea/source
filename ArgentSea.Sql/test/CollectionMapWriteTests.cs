using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using Xunit;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using FluentAssertions;

namespace ArgentSea.Sql.Test
{
    internal class CollectionWriteChild
    {
        [MapToSqlInt("ChildId", true)]
        public int ChildId { get; set; }

        [MapToSqlNVarChar("ChildName", 100)]
        public string ChildName { get; set; }
    }

    internal class CollectionWriteParent
    {
        [MapToSqlInt("Id", true)]
        public int Id { get; set; }

        [MapToSqlNVarChar("Name", 255)]
        public string Name { get; set; }

        [MapToSqlTableValuedParameter("@Children", "ChildTableType")]
        public List<CollectionWriteChild> Children { get; set; }
    }

    internal class CollectionWriteParentImmutableArray
    {
        [MapToSqlInt("Id", true)]
        public int Id { get; set; }

        [MapToSqlNVarChar("Name", 255)]
        public string Name { get; set; }

        [MapToSqlTableValuedParameter("@Children", "ChildTableType")]
        public ImmutableArray<CollectionWriteChild> Children { get; set; }
    }

    internal class CollectionWriteKeyedChild : IKeyedModel<int>
    {
        public ShardKey<int> Key { get; set; }
    }

    internal class CollectionWriteKeyedChild2 : IKeyedModel<int, int>
    {
        public ShardKey<int, int> Key { get; set; }
    }

    internal class CollectionWriteKeyedChild3 : IKeyedModel<int, int, int>
    {
        public ShardKey<int, int, int> Key { get; set; }
    }

    internal class CollectionWriteKeyedChild4 : IKeyedModel<int, int, int, int>
    {
        public ShardKey<int, int, int, int> Key { get; set; }
    }

    /// <summary>
    /// A lazy <see cref="IEnumerable{SqlDataRecord}"/> that records whether it was ever enumerated, used to prove
    /// that the raw AddSqlTableValuedParameter overload passes a lazy sequence through without consuming it.
    /// </summary>
    internal class FlaggingSqlDataRecordSequence : IEnumerable<SqlDataRecord>
    {
        public bool WasEnumerated { get; private set; }

        public IEnumerator<SqlDataRecord> GetEnumerator()
        {
            WasEnumerated = true;
            return System.Linq.Enumerable.Empty<SqlDataRecord>().GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class CollectionMapWriteTests
    {
        [Fact]
        public void AddSqlTableValuedParameter_NonEmptyCollection_ProducesRecordList()
        {
            // Arrange
            var dbLogger = new DebugLogger();
            var prms = new ParameterCollection();
            var values = new List<CollectionWriteChild>
            {
                new CollectionWriteChild { ChildId = 1, ChildName = "One" },
                new CollectionWriteChild { ChildId = 2, ChildName = "Two" },
            };

            // Act
            prms.AddSqlTableValuedParameter<CollectionWriteChild>("@Children", values, dbLogger);

            // Assert
            var prm = (SqlParameter)prms["@Children"];
            prm.SqlDbType.Should().Be(System.Data.SqlDbType.Structured, "table-valued parameters use the Structured data type");
            prm.Value.Should().BeAssignableTo<IEnumerable<SqlDataRecord>>("a populated collection is sent as a list of records");
            ((IReadOnlyCollection<SqlDataRecord>)prm.Value).Count.Should().Be(2, "two child rows were provided");
        }

        [Fact]
        public void AddSqlTableValuedParameter_EmptyCollection_ProducesNullReferenceNotAnEmptyEnumeration()
        {
            // Arrange
            var dbLogger = new DebugLogger();
            var prms = new ParameterCollection();
            var values = new List<CollectionWriteChild>();

            // Act
            prms.AddSqlTableValuedParameter<CollectionWriteChild>("@Children", values, dbLogger);

            // Assert
            // Microsoft.Data.SqlClient rejects both DBNull and a zero-record enumeration for a Structured parameter;
            // a null reference is the driver-defined representation of a table-valued parameter with no rows.
            var prm = (SqlParameter)prms["@Children"];
            prm.SqlDbType.Should().Be(System.Data.SqlDbType.Structured);
            prm.Value.Should().BeNull("the driver represents a zero-row table-valued parameter as a null reference, not DbNull or an empty enumeration");
        }

        [Fact]
        public void AddSqlTableValuedParameter_WithColumnList_EmptyCollection_SendsNullTvpValue()
        {
            // Arrange
            var dbLogger = new DebugLogger();
            var prms = new ParameterCollection();
            var values = new List<CollectionWriteChild>();

            // Act
            prms.AddSqlTableValuedParameter<CollectionWriteChild>("@Children", values, new List<string> { "ChildId", "ChildName" }, dbLogger);

            // Assert
            var prm = (SqlParameter)prms["@Children"];
            prm.Value.Should().BeNull("the column-list overload must apply the same empty-collection rule as the default overload: a null reference, not DbNull");
        }

        [Fact]
        public void CreateInputParameters_NonEmptyCollectionProperty_SetsTypeNameAndRows()
        {
            // Arrange
            var dbLogger = new DebugLogger();
            var prms = new ParameterCollection();
            var model = new CollectionWriteParent
            {
                Id = 1,
                Name = "Parent",
                Children = new List<CollectionWriteChild>
                {
                    new CollectionWriteChild { ChildId = 10, ChildName = "Ten" },
                    new CollectionWriteChild { ChildId = 20, ChildName = "Twenty" },
                    new CollectionWriteChild { ChildId = 30, ChildName = "Thirty" },
                }
            };

            // Act
            prms.CreateInputParameters<CollectionWriteParent>(model, dbLogger);

            // Assert
            var prm = (SqlParameter)prms["@Children"];
            prm.TypeName.Should().Be("ChildTableType", "the attribute declares this as the table type name");
            prm.Value.Should().BeAssignableTo<IEnumerable<SqlDataRecord>>();
            ((IReadOnlyCollection<SqlDataRecord>)prm.Value).Count.Should().Be(3, "three child rows were provided");
        }

        [Fact]
        public void CreateInputParameters_EmptyCollectionProperty_SendsNullTvpValue()
        {
            // Arrange
            var dbLogger = new DebugLogger();
            var prms = new ParameterCollection();
            var model = new CollectionWriteParent
            {
                Id = 1,
                Name = "Parent",
                Children = new List<CollectionWriteChild>()
            };

            // Act
            prms.CreateInputParameters<CollectionWriteParent>(model, dbLogger);

            // Assert
            var prm = (SqlParameter)prms["@Children"];
            prm.TypeName.Should().Be("ChildTableType");
            prm.Value.Should().BeNull("the driver represents a zero-row table-valued parameter as a null reference, not DbNull");
        }

        [Fact]
        public void CreateInputParameters_NonEmptyImmutableArrayCollectionProperty_SetsTypeNameAndRows()
        {
            // Arrange
            // ImmutableArray<T> is a value type, so the collection property's static expression type is not
            // reference-assignable to the IEnumerable<TElement> parameter of AddSqlTableValuedParameter without
            // an explicit conversion in the expression tree.
            var dbLogger = new DebugLogger();
            var prms = new ParameterCollection();
            var model = new CollectionWriteParentImmutableArray
            {
                Id = 1,
                Name = "Parent",
                Children = ImmutableArray.Create(
                    new CollectionWriteChild { ChildId = 10, ChildName = "Ten" },
                    new CollectionWriteChild { ChildId = 20, ChildName = "Twenty" },
                    new CollectionWriteChild { ChildId = 30, ChildName = "Thirty" })
            };

            // Act
            prms.CreateInputParameters<CollectionWriteParentImmutableArray>(model, dbLogger);

            // Assert
            var prm = (SqlParameter)prms["@Children"];
            prm.TypeName.Should().Be("ChildTableType", "the attribute declares this as the table type name");
            prm.Value.Should().BeAssignableTo<IEnumerable<SqlDataRecord>>();
            ((IReadOnlyCollection<SqlDataRecord>)prm.Value).Count.Should().Be(3, "three child rows were provided");
        }

        [Fact]
        public void CreateInputParameters_EmptyImmutableArrayCollectionProperty_SendsNullTvpValue()
        {
            // Arrange
            var dbLogger = new DebugLogger();
            var prms = new ParameterCollection();
            var model = new CollectionWriteParentImmutableArray
            {
                Id = 1,
                Name = "Parent",
                Children = ImmutableArray<CollectionWriteChild>.Empty
            };

            // Act
            prms.CreateInputParameters<CollectionWriteParentImmutableArray>(model, dbLogger);

            // Assert
            var prm = (SqlParameter)prms["@Children"];
            prm.TypeName.Should().Be("ChildTableType");
            prm.Value.Should().BeNull("the driver represents a zero-row table-valued parameter as a null reference, not DbNull");
        }

        [Fact]
        public void CreateInputParameters_DefaultImmutableArrayCollectionProperty_DoesNotThrowAndSendsNullTvpValue()
        {
            // Arrange
            // A default (uninitialized) ImmutableArray<T> - as opposed to ImmutableArray<T>.Empty - throws
            // InvalidOperationException when enumerated. Since the model never distinguishes "never assigned"
            // from "assigned as empty", it must be treated the same as an empty collection rather than throwing.
            var dbLogger = new DebugLogger();
            var prms = new ParameterCollection();
            var model = new CollectionWriteParentImmutableArray
            {
                Id = 1,
                Name = "Parent",
                Children = default
            };
            model.Children.IsDefault.Should().BeTrue("the test must exercise the uninitialized struct, not ImmutableArray<T>.Empty");

            // Act
            Action act = () => prms.CreateInputParameters<CollectionWriteParentImmutableArray>(model, dbLogger);

            // Assert
            act.Should().NotThrow("a default ImmutableArray<T> must be treated as an empty collection, not enumerated directly");
            var prm = (SqlParameter)prms["@Children"];
            prm.TypeName.Should().Be("ChildTableType");
            prm.Value.Should().BeNull("the driver represents a zero-row table-valued parameter as a null reference, not DbNull");
        }

        [Fact]
        public void CreateInputParameters_NullCollectionProperty_SendsNullTvpValue()
        {
            // Arrange
            var dbLogger = new DebugLogger();
            var prms = new ParameterCollection();
            var model = new CollectionWriteParent
            {
                Id = 1,
                Name = "Parent",
                Children = null
            };

            // Act
            Action act = () => prms.CreateInputParameters<CollectionWriteParent>(model, dbLogger);

            // Assert
            act.Should().NotThrow("a null collection property must be treated as empty, not enumerated directly");
            prms.Contains("@Children").Should().BeTrue("the table-valued parameter is still added for a null collection");
            var prm = (SqlParameter)prms["@Children"];
            prm.SqlDbType.Should().Be(SqlDbType.Structured);
            prm.TypeName.Should().Be("ChildTableType");
            prm.Value.Should().BeNull("the driver represents a zero-row table-valued parameter as a null reference, not DbNull");
        }

        [Fact]
        public void AddSqlTableValuedParameter_KeyedModel_EmptyCollection_SendsNullTvpValue()
        {
            // Arrange
            var prms = new ParameterCollection();
            var values = new List<CollectionWriteKeyedChild>();

            // Act
            prms.AddSqlTableValuedParameter<CollectionWriteKeyedChild, int>("Children", values, "ShardId", SqlDbType.SmallInt, "RecordId", SqlDbType.Int);

            // Assert
            var prm = (SqlParameter)prms["@Children"];
            prm.SqlDbType.Should().Be(SqlDbType.Structured);
            prm.ParameterName.Should().Be("@Children");
            prm.Value.Should().BeNull("an empty keyed-model sequence must be sent as a null reference, not DbNull or an empty enumeration");
        }

        [Fact]
        public void AddSqlTableValuedParameter_ShardKey_EmptyCollection_SendsNullTvpValue()
        {
            // Arrange
            var prms = new ParameterCollection();
            var values = new List<ShardKey<int>>();

            // Act
            prms.AddSqlTableValuedParameter<int>("Children", values, "ShardId", SqlDbType.SmallInt, "RecordId", SqlDbType.Int);

            // Assert
            var prm = (SqlParameter)prms["@Children"];
            prm.SqlDbType.Should().Be(SqlDbType.Structured);
            prm.ParameterName.Should().Be("@Children");
            prm.Value.Should().BeNull("an empty ShardKey sequence must be sent as a null reference, not DbNull or an empty enumeration");
        }

        public static TheoryData<string, Action<ParameterCollection>> KeyedAndShardKeyOverloadsEmptySequenceTestData => new TheoryData<string, Action<ParameterCollection>>
        {
            {
                "AddSqlTableValuedParameter<TModel, TRecord> (keyed model, 1 key dimension)",
                prms => prms.AddSqlTableValuedParameter<CollectionWriteKeyedChild, int>(
                    "Children", new List<CollectionWriteKeyedChild>(), "ShardId", SqlDbType.Int, "RecordId", SqlDbType.Int)
            },
            {
                "AddSqlTableValuedParameter<TModel, TRecord, TChild>",
                prms => prms.AddSqlTableValuedParameter<CollectionWriteKeyedChild2, int, int>(
                    "Children", new List<CollectionWriteKeyedChild2>(), "ShardId", SqlDbType.Int, "RecordId", SqlDbType.Int, "ChildId", SqlDbType.Int)
            },
            {
                "AddSqlTableValuedParameter<TModel, TRecord, TChild, TGrandChild>",
                prms => prms.AddSqlTableValuedParameter<CollectionWriteKeyedChild3, int, int, int>(
                    "Children", new List<CollectionWriteKeyedChild3>(), "ShardId", SqlDbType.Int, "RecordId", SqlDbType.Int, "ChildId", SqlDbType.Int, "GrandChildId", SqlDbType.Int)
            },
            {
                "AddSqlTableValuedParameter<TModel, TRecord, TChild, TGrandChild, TGreatGrandChild>",
                prms => prms.AddSqlTableValuedParameter<CollectionWriteKeyedChild4, int, int, int, int>(
                    "Children", new List<CollectionWriteKeyedChild4>(), "ShardId", SqlDbType.Int, "RecordId", SqlDbType.Int, "ChildId", SqlDbType.Int, "GrandChildId", SqlDbType.Int, "GreatGrandChildId", SqlDbType.Int)
            },
            {
                "AddSqlTableValuedParameter<TRecord>(IEnumerable<ShardKey<TRecord>> ...)",
                prms => prms.AddSqlTableValuedParameter<int>(
                    "Children", new List<ShardKey<int>>(), "ShardId", SqlDbType.Int, "RecordId", SqlDbType.Int)
            },
            {
                "AddSqlTableValuedParameter<TRecord, TChild>(IEnumerable<ShardKey<TRecord, TChild>> ...)",
                prms => prms.AddSqlTableValuedParameter<int, int>(
                    "Children", new List<ShardKey<int, int>>(), "ShardId", SqlDbType.Int, "RecordId", SqlDbType.Int, "ChildId", SqlDbType.Int)
            },
            {
                "AddSqlTableValuedParameter<TRecord, TChild, TGrandChild>(IEnumerable<ShardKey<TRecord, TChild, TGrandChild>> ...)",
                prms => prms.AddSqlTableValuedParameter<int, int, int>(
                    "Children", new List<ShardKey<int, int, int>>(), "ShardId", SqlDbType.Int, "RecordId", SqlDbType.Int, "ChildId", SqlDbType.Int, "GrandChildId", SqlDbType.Int)
            },
            {
                "AddSqlTableValuedParameter<TRecord, TChild, TGrandChild, TGreatGrandChild>(IEnumerable<ShardKey<TRecord, TChild, TGrandChild, TGreatGrandChild>> ...)",
                prms => prms.AddSqlTableValuedParameter<int, int, int, int>(
                    "Children", new List<ShardKey<int, int, int, int>>(), "ShardId", SqlDbType.Int, "RecordId", SqlDbType.Int, "ChildId", SqlDbType.Int, "GrandChildId", SqlDbType.Int, "GreatGrandChildId", SqlDbType.Int)
            },
        };

        [Theory]
        [MemberData(nameof(KeyedAndShardKeyOverloadsEmptySequenceTestData))]
        public void AddSqlTableValuedParameter_KeyedAndShardKeyOverloads_EmptySequence_SendsNullTvpValue(string caseName, Action<ParameterCollection> act)
        {
            // Arrange
            var prms = new ParameterCollection();

            // Act
            act(prms);

            // Assert
            prms.Count.Should().Be(1, $"{caseName} must append exactly one parameter for the empty sequence");
            var prm = (SqlParameter)prms["@Children"];
            prm.ParameterName.Should().Be("@Children", $"{caseName} must normalize the unprefixed parameter name");
            prm.SqlDbType.Should().Be(SqlDbType.Structured, $"{caseName} must produce a Structured parameter");
            prm.Value.Should().BeNull($"{caseName} must send a null reference for an empty sequence, not DbNull or an empty enumeration");
        }

        [Fact]
        public void AddSqlTableValuedParameter_RawOverload_EmptyList_SendsNullTvpValue()
        {
            // Arrange
            var prms = new ParameterCollection();
            var records = new List<SqlDataRecord>();

            // Act
            prms.AddSqlTableValuedParameter("@Children", records);

            // Assert
            var prm = (SqlParameter)prms["@Children"];
            prm.SqlDbType.Should().Be(SqlDbType.Structured);
            prm.Value.Should().BeNull("the raw overload must apply the same empty-collection rule: a null reference, not DbNull");
        }

        [Fact]
        public void AddSqlTableValuedParameter_RawOverload_NonEmptyList_KeepsSameListInstance()
        {
            // Arrange
            var prms = new ParameterCollection();
            var metaData = new SqlMetaData[] { new SqlMetaData("ChildId", SqlDbType.Int) };
            var record = new SqlDataRecord(metaData);
            record.SetValue(0, 1);
            var records = new List<SqlDataRecord> { record };

            // Act
            prms.AddSqlTableValuedParameter("@Children", records);

            // Assert
            var prm = (SqlParameter)prms["@Children"];
            prm.Value.Should().BeSameAs(records, "a non-empty collection is passed through to the driver unchanged");
        }

        [Fact]
        public void AddSqlTableValuedParameter_RawOverload_LazySequence_PassesThroughWithoutEnumerating()
        {
            // Arrange
            var prms = new ParameterCollection();
            var lazySequence = new FlaggingSqlDataRecordSequence();

            // Act
            prms.AddSqlTableValuedParameter("@Children", lazySequence);

            // Assert
            var prm = (SqlParameter)prms["@Children"];
            prm.Value.Should().BeSameAs(lazySequence, "a lazy sequence that is not an ICollection<SqlDataRecord>/IReadOnlyCollection<SqlDataRecord> must be passed through as-is");
            lazySequence.WasEnumerated.Should().BeFalse("the raw overload must not enumerate a lazy sequence to determine whether it is empty");
        }

        [Fact]
        public void AddSqlTableValuedParameter_NullValues_SendsNullTvpValue()
        {
            // Arrange
            var dbLogger = new DebugLogger();
            var prms = new ParameterCollection();

            // Act
            Action act = () => prms.AddSqlTableValuedParameter<CollectionWriteChild>("@Children", null, dbLogger);

            // Assert
            act.Should().NotThrow("a null values argument must be treated as an empty sequence, not enumerated directly");
            var prm = (SqlParameter)prms["@Children"];
            prm.Value.Should().BeNull("a null values argument is sent as a table-valued parameter with no rows");
        }
    }
}