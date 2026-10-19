using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec sections model-mapping and mapping-expression-builders: the Mapper's result handlers, null semantics, nested
    /// MapToModel flattening, diagnostics and caching, and the ExpressionHelpers builder families, all driven through the
    /// TestKit's provider-neutral mapping attributes and scripted data reader.
    /// </summary>
    public class ModelMappingTests
    {
        private const string Proc = "rd.Test";
        private const string Conn = "test connection";

        #region Models

        public enum OrderStatus { Pending = 0, Active = 1, Shipped = 2 }

        public enum Priority : byte { Low = 1, High = 2 }

        public class RowBase
        {
            [FakeMap("Id", true)]
            public int Id { get; set; }
        }

        public class Row0 : RowBase { }
        public class Row1 : RowBase { }
        public class Row2 : RowBase { }
        public class Row3 : RowBase { }
        public class Row4 : RowBase { }
        public class Row5 : RowBase { }
        public class Row6 : RowBase { }
        public class Row7 : RowBase { }

        /// <summary>The list properties are declared in scrambled order so that assignment is proven to follow the type argument, not the declaration order.</summary>
        public class Master
        {
            [FakeMap("Id", true)]
            public int Id { get; set; }

            public List<Row5>? Fives { get; set; }
            public List<Row2>? Twos { get; set; }
            public List<Row7>? Sevens { get; set; }
            public List<Row0>? Zeros { get; set; }
            public List<Row3>? Threes { get; set; }
            public List<Row6>? Sixes { get; set; }
            public List<Row1>? Ones { get; set; }
            public List<Row4>? Fours { get; set; }
        }

        public class Order
        {
            [FakeMap("Id", true)]
            public int Id { get; set; }

            [FakeMap("Name")]
            public string? Name { get; set; }
        }

        public class OrderWithTwoCollections
        {
            [FakeMap("Id", true)]
            public int Id { get; set; }

            [FakeCollectionMap("Items")]
            public List<TestModels.OrderLine>? Items { get; set; }

            [FakeCollectionMap("Notes")]
            public List<TestModels.Note>? Notes { get; set; }
        }

        public class NotACollection
        {
            [FakeMap("Id")]
            public int Id { get; set; }

            [FakeCollectionMap("Oops")]
            public int Oops { get; set; }
        }

        public class CollectionOnly
        {
            [FakeCollectionMap("Items")]
            public List<TestModels.OrderLine>? Items { get; set; }
        }

        public class Unmapped
        {
            public int Id { get; set; }
        }

        public class Sentinels
        {
            [FakeMapDouble("Score")]
            public double Score { get; set; } = 1.5;

            [FakeMapDouble("Ratio")]
            public double? Ratio { get; set; } = 2.5;

            [FakeMapFloat("Temp")]
            public float Temp { get; set; } = 3.5f;

            [FakeMapGuid("Key")]
            public Guid Key { get; set; } = Guid.NewGuid();

            [FakeMapGuid("OptionalKey")]
            public Guid? OptionalKey { get; set; } = Guid.NewGuid();

            [FakeMapInteger("IntStatus")]
            public OrderStatus? IntStatus { get; set; } = OrderStatus.Shipped;

            [FakeMapString("TextStatus", 20)]
            public OrderStatus? TextStatus { get; set; } = OrderStatus.Shipped;

            [FakeMap("Maybe")]
            public int? Maybe { get; set; } = 9;

            [FakeMapString("Label", 20)]
            public string? Label { get; set; } = "x";
        }

        public class ReaderShapes
        {
            [FakeMap("Count")]
            public int Count { get; set; }

            [FakeMap("Maybe")]
            public int? Maybe { get; set; }

            [FakeMapArray("Tags")]
            public string[]? Tags { get; set; }

            [FakeMapInteger("IntStatus")]
            public OrderStatus IntStatus { get; set; }

            [FakeMapString("TextStatus", 20)]
            public OrderStatus TextStatus { get; set; }

            [FakeMapString("MiddleName", 50)]
            public string? MiddleName { get; set; } = "unchanged";
        }

        public class Address
        {
            [FakeMap("Street")]
            public string? Street { get; set; }

            [FakeMap("City")]
            public string? City { get; set; }

            [MapToModel]
            public GeoPoint? Point { get; set; }
        }

        public class GeoPoint
        {
            [FakeMapDouble("Lat")]
            public double Lat { get; set; }
        }

        public struct Coordinates
        {
            [FakeMapDouble("Lon")]
            public double Lon { get; set; }
        }

        public class Customer
        {
            [FakeMap("CustomerId", true)]
            public int CustomerId { get; set; }

            [MapToModel]
            public Address? Address { get; set; }

            [MapToModel]
            public Coordinates Where { get; set; }
        }

        public class CustomerWithFixedAddress
        {
            [FakeMap("CustomerId", true)]
            public int CustomerId { get; set; }

            [MapToModel]
            public Address Shipping { get; } = new Address();
        }

        public class InputShapes
        {
            [FakeMapString("Status", 20)]
            public OrderStatus Status { get; set; } = OrderStatus.Shipped;

            [FakeMapString("OptionalStatus", 20)]
            public OrderStatus? OptionalStatus { get; set; }

            [FakeMapString("Name", 50)]
            public string? Name { get; set; } = "abc";

            [FakeMapByte("Level")]
            public Priority Level { get; set; } = Priority.High;

            [FakeMapByte("OptionalLevel")]
            public Priority? OptionalLevel { get; set; }

            [FakeMapInteger("Quantity")]
            public int Quantity { get; set; } = 5;

            [FakeMapBinary("Payload")]
            public byte[]? Payload { get; set; } = new byte[] { 0x01, 0x02 };
        }

        public class OutputShapes
        {
            [FakeMapBinary("Thumbnail")]
            public byte[]? Thumbnail { get; set; }

            [FakeMapArray("Tags")]
            public int[]? Tags { get; set; }

            [FakeMapString("Status", 20)]
            public OrderStatus Status { get; set; }

            [FakeMapString("OptionalStatus", 20)]
            public OrderStatus? OptionalStatus { get; set; } = OrderStatus.Pending;

            [FakeMapString("Name", 50)]
            public string? Name { get; set; }

            [FakeMapInteger("IntStatus")]
            public OrderStatus IntStatus { get; set; }

            [FakeMap("Total")]
            public decimal Total { get; set; } = 5m;
        }

        public class NullableCount
        {
            [FakeMapInteger("Count")]
            public int? Count { get; set; } = 1;
        }

        public class RequiredCount
        {
            [FakeMapInteger("Count")]
            public int Count { get; set; } = 1;
        }

        public class RequiredTotal
        {
            [FakeMap("Name")]
            public string? Name { get; set; } = "untouched";

            [FakeMap("Total", true)]
            public decimal Total { get; set; }
        }

        public class RequiredSecondColumn
        {
            [FakeMap("Name")]
            public string? Name { get; set; }

            [FakeMap("OrderId", true)]
            public int OrderId { get; set; }
        }

        public class DoubleMapped
        {
            [FakeMap("A")]
            [FakeMap("B")]
            public int X { get; set; }
        }

        public class WrongType
        {
            [FakeMap("First")]
            public int First { get; set; }

            [FakeMapInteger("Name")]
            public string? Name { get; set; }
        }

        public class CachedModel
        {
            [FakeMap("Id")]
            public int Id { get; set; }
        }

        public class CachedInputModel
        {
            [FakeMap("Id")]
            public int Id { get; set; }
        }

        public class KeyWithoutShardId
        {
            [FakeMapShardKey("Id")]
            [FakeMap("Id", true)]
            public ShardKey<int> Key { get; set; }

            [FakeMap("Name")]
            public string? Name { get; set; }
        }

        public class ChildKeyWithShardId
        {
            [FakeMapShardKey("ShardId", "RecordId", "ChildId")]
            [FakeMap("RecordId", true)]
            [FakeMap("ChildId")]
            public ShardKey<int, short> Key { get; set; }
        }

        public class NullableChildKeyWithShardId
        {
            [FakeMapShardKey("ShardId", "RecordId", "ChildId")]
            [FakeMap("RecordId", true)]
            [FakeMap("ChildId")]
            public ShardKey<int, short>? Key { get; set; }
        }

        public class MissingRecordIdName
        {
            [FakeMapShardKey("ShardId", "OrderId")]
            [FakeMap("OrderKey", true)]
            public ShardKey<int> Key { get; set; }
        }

        public class MissingChildIdName
        {
            [FakeMapShardKey("ShardId", "Id", "Kid")]
            [FakeMap("Id", true)]
            public ShardKey<int, int> Key { get; set; }
        }

        public class MissingGrandChildIdName
        {
            [FakeMapShardKey("ShardId", "Id", "Kid", "GrandKid")]
            [FakeMap("Id", true)]
            [FakeMap("Kid")]
            public ShardKey<int, int, int> Key { get; set; }
        }

        public class MissingGreatGrandChildIdName
        {
            [FakeMapShardKey("ShardId", "Id", "Kid", "GrandKid", "GreatGrandKid", true)]
            [FakeMap("Id", true)]
            [FakeMap("Kid")]
            [FakeMap("GrandKid")]
            public ShardKey<int, int, int, int> Key { get; set; }
        }

        public class KeyShapes
        {
            [FakeMapShardKey("ShardId", "RecordId", "ChildId")]
            public ShardKey<short, int>? NullableChild { get; set; }

            public ShardKey<int> Unattributed { get; set; }

            public int Plain { get; set; }
        }

        #endregion

        #region Helpers

        private static FakeDbDataReader Reader(params FakeResultSet[] resultSets) => new FakeDbDataReader(resultSets);

        private static FakeResultSet Rows(string[] columns, params object?[][] rows) => new FakeResultSet(columns, rows);

        private static FakeResultSet IdRows(params int[] ids) => Rows(new[] { "Id" }, ids.Select(id => new object?[] { id }).ToArray());

        private static ParameterCollection Outputs(params (string Name, object? Value)[] values)
        {
            var prms = new ParameterCollection();
            foreach (var (name, value) in values)
            {
                prms.Add(new FakeDbParameter(name, value ?? DBNull.Value, ParameterDirection.Output));
            }
            return prms;
        }

        /// <summary>Invokes the reader-results handler overload with <paramref name="arity"/> list type arguments, Row0 through Row(n-1).</summary>
        private static Master? ReaderHandler(int arity, DbDataReader rdr) => arity switch
        {
            1 => Mapper.ModelFromReaderResultsHandler<Master, Row0>(0, Proc, null, rdr, new ParameterCollection(), Conn, null),
            2 => Mapper.ModelFromReaderResultsHandler<Master, Row0, Row1>(0, Proc, null, rdr, new ParameterCollection(), Conn, null),
            3 => Mapper.ModelFromReaderResultsHandler<Master, Row0, Row1, Row2>(0, Proc, null, rdr, new ParameterCollection(), Conn, null),
            4 => Mapper.ModelFromReaderResultsHandler<Master, Row0, Row1, Row2, Row3>(0, Proc, null, rdr, new ParameterCollection(), Conn, null),
            5 => Mapper.ModelFromReaderResultsHandler<Master, Row0, Row1, Row2, Row3, Row4>(0, Proc, null, rdr, new ParameterCollection(), Conn, null),
            6 => Mapper.ModelFromReaderResultsHandler<Master, Row0, Row1, Row2, Row3, Row4, Row5>(0, Proc, null, rdr, new ParameterCollection(), Conn, null),
            7 => Mapper.ModelFromReaderResultsHandler<Master, Row0, Row1, Row2, Row3, Row4, Row5, Row6>(0, Proc, null, rdr, new ParameterCollection(), Conn, null),
            8 => Mapper.ModelFromReaderResultsHandler<Master, Row0, Row1, Row2, Row3, Row4, Row5, Row6, Row7>(0, Proc, null, rdr, new ParameterCollection(), Conn, null),
            _ => throw new ArgumentOutOfRangeException(nameof(arity))
        };

        private static Master? OutHandler(int arity, DbDataReader rdr, DbParameterCollection prms) => arity switch
        {
            1 => Mapper.ModelFromOutResultsHandler<Master, Row0>(0, Proc, null, rdr, prms, Conn, null),
            2 => Mapper.ModelFromOutResultsHandler<Master, Row0, Row1>(0, Proc, null, rdr, prms, Conn, null),
            3 => Mapper.ModelFromOutResultsHandler<Master, Row0, Row1, Row2>(0, Proc, null, rdr, prms, Conn, null),
            4 => Mapper.ModelFromOutResultsHandler<Master, Row0, Row1, Row2, Row3>(0, Proc, null, rdr, prms, Conn, null),
            5 => Mapper.ModelFromOutResultsHandler<Master, Row0, Row1, Row2, Row3, Row4>(0, Proc, null, rdr, prms, Conn, null),
            6 => Mapper.ModelFromOutResultsHandler<Master, Row0, Row1, Row2, Row3, Row4, Row5>(0, Proc, null, rdr, prms, Conn, null),
            7 => Mapper.ModelFromOutResultsHandler<Master, Row0, Row1, Row2, Row3, Row4, Row5, Row6>(0, Proc, null, rdr, prms, Conn, null),
            8 => Mapper.ModelFromOutResultsHandler<Master, Row0, Row1, Row2, Row3, Row4, Row5, Row6, Row7>(0, Proc, null, rdr, prms, Conn, null),
            _ => throw new ArgumentOutOfRangeException(nameof(arity))
        };

        private static List<RowBase>?[] Lists(Master master) => new List<RowBase>?[]
        {
            master.Zeros?.Cast<RowBase>().ToList(), master.Ones?.Cast<RowBase>().ToList(), master.Twos?.Cast<RowBase>().ToList(), master.Threes?.Cast<RowBase>().ToList(),
            master.Fours?.Cast<RowBase>().ToList(), master.Fives?.Cast<RowBase>().ToList(), master.Sixes?.Cast<RowBase>().ToList(), master.Sevens?.Cast<RowBase>().ToList()
        };

        #endregion

        #region Result handlers

        [Theory]
        [Trait("Spec", "blk-40911a1576f34b67a6e64365e127bb0c")]
        [Trait("Spec", "blk-a38a52ef90b4429487c09695c038a5d4")]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void ModelFromReaderResultsHandlerAssignsEachDependentResultSetToTheListOfItsTypeArgument(int arity)
        {
            var resultSets = new List<FakeResultSet> { IdRows(42) };
            for (var i = 0; i < arity; i++)
            {
                resultSets.Add(IdRows(100 + i, 200 + i));
            }

            var master = ReaderHandler(arity, Reader(resultSets.ToArray()));

            master.Should().NotBeNull();
            master!.Id.Should().Be(42, "the root comes from the first result set");
            var lists = Lists(master);
            for (var i = 0; i < 8; i++)
            {
                if (i < arity)
                {
                    lists[i].Should().NotBeNull($"result set {i + 2} maps to type argument {i}");
                    lists[i]!.Select(r => r.Id).Should().Equal(100 + i, 200 + i);
                }
                else
                {
                    lists[i].Should().BeNull("no type argument names this list");
                }
            }
        }

        [Theory]
        [Trait("Spec", "blk-40911a1576f34b67a6e64365e127bb0c")]
        [Trait("Spec", "blk-67bd84f83fc54d578c2a0f333a0a0c2b")]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void ModelFromOutResultsHandlerMaterializesTheRootFromOutputParametersAndEachListFromTheReader(int arity)
        {
            var resultSets = new List<FakeResultSet>();
            for (var i = 0; i < arity; i++)
            {
                resultSets.Add(IdRows(100 + i));
            }

            var master = OutHandler(arity, Reader(resultSets.ToArray()), Outputs(("@Id", 42)));

            master.Should().NotBeNull();
            master!.Id.Should().Be(42, "the root comes from the output parameters");
            var lists = Lists(master);
            for (var i = 0; i < 8; i++)
            {
                if (i < arity)
                {
                    lists[i]!.Select(r => r.Id).Should().Equal(100 + i);
                }
                else
                {
                    lists[i].Should().BeNull();
                }
            }
        }

        [Fact]
        [Trait("Spec", "blk-67bd84f83fc54d578c2a0f333a0a0c2b")]
        [Trait("Spec", "blk-a38a52ef90b4429487c09695c038a5d4")]
        public void ModelFromOutResultsHandlerWithNoListsPopulatesTheSuppliedInstanceFromOutputParameters()
        {
            var instance = new Order { Name = "before" };
            var prms = Outputs(("@Id", 10), ("@Name", "Test2"));

            var result = Mapper.ModelFromOutResultsHandler<Order>(instance, 5, Proc, null, Reader(), prms, Conn, null);
            var created = Mapper.ModelFromOutResultsHandler<Order>(5, Proc, null, Reader(), prms, Conn, null);

            result.Should().BeSameAs(instance);
            result!.Id.Should().Be(10);
            result.Name.Should().Be("Test2");
            created.Should().NotBeNull().And.NotBeSameAs(instance, "the no-instance overload constructs a new model");
            created!.Id.Should().Be(10);
        }

        [Fact]
        [Trait("Spec", "blk-4f7269c1fdea4340b30fd062cff97b7c")]
        [Trait("Spec", "blk-40911a1576f34b67a6e64365e127bb0c")]
        public void OutResultsHandlerAssignsAnEmptyListWhenTheDependentResultSetHasNoRows()
        {
            var rdr = Reader(new FakeResultSet(new[] { "Id" }, Array.Empty<object?[]>()));

            var master = Mapper.ModelFromOutResultsHandler<Master, Row0>(0, Proc, null, rdr, Outputs(("@Id", 42)), Conn, null);

            master!.Id.Should().Be(42);
            master.Zeros.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-e9ba8b8920bd460aabc904deb4ea0cc1")]
        [Trait("Spec", "blk-40911a1576f34b67a6e64365e127bb0c")]
        public void ReaderResultsHandlerToleratesAnAbsentDependentResultSetWithAnEmptyList()
        {
            var master = Mapper.ModelFromReaderResultsHandler<Master, Row0, Row1>(0, Proc, null, Reader(IdRows(42), IdRows(7)), new ParameterCollection(), Conn, null);

            master!.Id.Should().Be(42);
            master.Zeros!.Select(r => r.Id).Should().Equal(7);
            master.Ones.Should().NotBeNull().And.BeEmpty("the third result set is absent, so the typed handler substitutes an empty list");
        }

        [Fact]
        [Trait("Spec", "blk-94064726580947bd9c3dc9cb6ea219d0")]
        public void ReaderResultsHandlerReturnsNullWhenTheRootResultSetHasNoRows()
        {
            var logger = new RecordingLogger();

            var master = Mapper.ModelFromReaderResultsHandler<Master, Row0>(0, Proc, null, Reader(new FakeResultSet(new[] { "Id" }, Array.Empty<object?[]>()), IdRows(7)), new ParameterCollection(), Conn, logger);

            master.Should().BeNull();
            logger.EntriesNamed(nameof(LoggingExtensions.EmptyResult)).Should().ContainSingle().Which.Message.Should().Contain(Proc);
        }

        [Fact]
        [Trait("Spec", "blk-2393e409dce94495b1ce53327c3aebde")]
        [Trait("Spec", "blk-94064726580947bd9c3dc9cb6ea219d0")]
        public void ATopOneReadOfTwoRowsThrowsUnexpectedMultiRowResultException()
        {
            Action toModel = () => Reader(IdRows(1, 2)).ToModel<Order>(null);
            Action handler = () => Mapper.ModelFromReaderResultsHandler<Order>(0, Proc, null, Reader(IdRows(1, 2)), new ParameterCollection(), Conn, null);

            toModel.Should().Throw<UnexpectedMultiRowResultException>().WithMessage("The database returned multiple records when only one was expected.");
            handler.Should().Throw<UnexpectedMultiRowResultException>();
            Reader(IdRows(3)).ToModel<Order>(null)!.Id.Should().Be(3, "exactly one row maps");
            Reader(new FakeResultSet(new[] { "Id" }, Array.Empty<object?[]>())).ToModel<Order>(null).Should().BeNull("no row yields the default");
        }

        [Fact]
        [Trait("Spec", "blk-24b13dadb4864807854d811fe54b0fe5")]
        public void UnexpectedMultiRowResultExceptionSingleStringConstructorTakesAProcedureName()
        {
            new UnexpectedMultiRowResultException("rd.GetOne").Message.Should().Be("Procedure rd.GetOne returned multiple records when only one was expected.");
            typeof(UnexpectedMultiRowResultException).Should().BeSealed().And.BeDerivedFrom<ApplicationException>();
            typeof(InvalidMapTypeException).Should().BeSealed();
            typeof(MultipleMapAttributesException).Should().BeSealed();
            typeof(MapAttributeMissingException).Should().NotBeSealed();
        }

        [Fact]
        [Trait("Spec", "blk-30c5f005122d42c08c82c9f63a1e4ce4")]
        public void ToListProducesOneModelPerRowAndAnEmptyListForNoRows()
        {
            var rdr = Reader(Rows(new[] { "Id", "Name" }, new object?[] { 1, "a" }, new object?[] { 2, "b" }));

            var list = rdr.ToList<Order>(32, null);
            var empty = Reader(new FakeResultSet(new[] { "Id" }, Array.Empty<object?[]>())).ToList<Order>(32, null);
            var handled = Mapper.ListFromReaderResultsHandler<Order>(32, Proc, null, Reader(IdRows(5)), new ParameterCollection(), Conn, null);

            list.Select(o => (o.Id, o.Name)).Should().Equal((1, "a"), (2, "b"));
            empty.Should().BeEmpty();
            handled.Select(o => o.Id).Should().Equal(5);
        }

        [Fact]
        [Trait("Spec", "blk-adc5c5fc2491433ea6e82baf99a1da9f")]
        [Trait("Spec", "blk-5cc65e21736d4dd687100b985b631fe3")]
        public void CollectionsHandlerHydratesEachCollectionPropertyInDeclarationOrderFromTheTrailingResultSets()
        {
            var rdr = Reader(
                IdRows(1),
                Rows(new[] { "LineId", "Qty" }, new object?[] { 10, 2 }, new object?[] { 11, 3 }),
                Rows(new[] { "Text" }, new object?[] { "n1" }));

            var order = Mapper.ModelFromReaderWithCollectionsHandler(new OrderWithTwoCollections(), 0, Proc, null, rdr, new ParameterCollection(), Conn, null);

            order!.Id.Should().Be(1);
            order.Items!.Select(l => (l.LineId, l.Qty)).Should().Equal((10, 2), (11, 3));
            order.Notes!.Select(n => n.Text).Should().Equal("n1");
        }

        [Fact]
        [Trait("Spec", "blk-adc5c5fc2491433ea6e82baf99a1da9f")]
        public void CollectionsHandlerReturnsNullWithoutAdvancingWhenTheRootRowIsMissing()
        {
            var rdr = Reader(new FakeResultSet(new[] { "Id" }, Array.Empty<object?[]>()), Rows(new[] { "LineId", "Qty" }, new object?[] { 10, 2 }));

            var order = Mapper.ModelFromReaderWithCollectionsHandler(new OrderWithTwoCollections(), 0, Proc, null, rdr, new ParameterCollection(), Conn, null);

            order.Should().BeNull();
            rdr.NextResultCalls.Should().Be(0, "no further result set is read when the root row does not exist");
            rdr.CurrentResultSet.Should().Be(0);
        }

        [Fact]
        [Trait("Spec", "blk-0e608bc7df6c471584c254f26914a81c")]
        [Trait("Spec", "blk-e266f5473fcb43a9a8cb6cdf6b18fa18")]
        [Trait("Spec", "blk-5cc65e21736d4dd687100b985b631fe3")]
        public void CollectionsHandlerThrowsNamingThePropertyAndPositionWhenAResultSetIsMissing()
        {
            var rdr = Reader(IdRows(1), Rows(new[] { "LineId", "Qty" }, new object?[] { 10, 2 }));
            var instance = new OrderWithTwoCollections();

            Action act = () => Mapper.ModelFromReaderWithCollectionsHandler(instance, 0, Proc, null, rdr, new ParameterCollection(), Conn, null);

            act.Should().Throw<UnexpectedSqlResultException>()
                .WithMessage("*'OrderWithTwoCollections'*")
                .WithMessage("*'Notes'*")
                .WithMessage("*result set #3*");
            instance.Items!.Select(l => l.LineId).Should().Equal(new[] { 10 }, "the first collection was hydrated from the one available result set");
            instance.Notes.Should().BeNull("the missing result set is not substituted with an empty list");
        }

        [Fact]
        [Trait("Spec", "blk-e266f5473fcb43a9a8cb6cdf6b18fa18")]
        public void CollectionsHandlerHydratesAnEmptyCollectionFromAPresentButEmptyResultSet()
        {
            var rdr = Reader(IdRows(1), new FakeResultSet(new[] { "LineId", "Qty" }, Array.Empty<object?[]>()), new FakeResultSet(new[] { "Text" }, Array.Empty<object?[]>()));

            var order = Mapper.ModelFromReaderWithCollectionsHandler(new OrderWithTwoCollections(), 0, Proc, null, rdr, new ParameterCollection(), Conn, null);

            order!.Items.Should().NotBeNull().And.BeEmpty();
            order.Notes.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-265a9853d123482b881fe9a66e17e8eb")]
        [Trait("Spec", "blk-c997b2e4dfb44da38e6be31ec2850da7")]
        public void HasCollectionMapPropertiesReportsPresenceAndRejectsANonCollectionProperty()
        {
            Mapper.HasCollectionMapProperties(typeof(OrderWithTwoCollections)).Should().BeTrue();
            Mapper.HasCollectionMapProperties(typeof(Order)).Should().BeFalse();
            Action act = () => Mapper.HasCollectionMapProperties(typeof(NotACollection));
            act.Should().Throw<InvalidOperationException>().WithMessage("*'Oops'*").WithMessage("*'NotACollection'*");
        }

        [Fact]
        [Trait("Spec", "blk-a38a52ef90b4429487c09695c038a5d4")]
        public void OnlyTheInstanceSuppliedHandlerOverloadsFitTheQueryResultModelHandlerDelegate()
        {
            QueryResultModelHandler<object?, Order> fromReader = Mapper.ModelFromReaderResultsHandler<Order>;
            QueryResultModelHandler<object?, Order> fromOut = Mapper.ModelFromOutResultsHandler<Order>;
            QueryResultModelHandler<object?, OrderWithTwoCollections> withCollections = Mapper.ModelFromReaderWithCollectionsHandler<OrderWithTwoCollections>;

            fromReader(new Order(), 0, Proc, null, Reader(IdRows(1)), new ParameterCollection(), Conn, null)!.Id.Should().Be(1);
            fromOut(new Order(), 0, Proc, null, Reader(), Outputs(("@Id", 2)), Conn, null)!.Id.Should().Be(2);
            withCollections(new OrderWithTwoCollections(), 0, Proc, null, Reader(IdRows(3), Rows(new[] { "LineId", "Qty" }), Rows(new[] { "Text" })), new ParameterCollection(), Conn, null)!.Id.Should().Be(3);

            var listHandler = typeof(Mapper).GetMethods().Single(m => m.Name == nameof(Mapper.ListFromReaderResultsHandler));
            listHandler.GetParameters().Should().HaveCount(7, "the list handler has no leading instance parameter");
            typeof(Mapper).GetMethods().Where(m => m.Name == nameof(Mapper.ModelFromReaderResultsHandler)).Select(m => m.GetGenericArguments().Length).Should().BeEquivalentTo(Enumerable.Range(1, 9).SelectMany(n => new[] { n, n }), "zero through eight list type arguments, each with and without an instance parameter");
        }

        #endregion

        #region Null semantics

        [Fact]
        [Trait("Spec", "blk-3d44c2d1a05c4aef91153f233526599d")]
        [Trait("Spec", "blk-df1ac07fd6d0473db0a53ccd85a4e5db")]
        public void ARequiredNullColumnYieldsANullModelWithoutReadingLaterColumns()
        {
            var logger = new RecordingLogger();
            var rdr = Reader(Rows(new[] { "Id", "Name" }, new object?[] { null, "later" }));

            var result = rdr.ToModel<Order>(logger);

            result.Should().BeNull();
            rdr.FieldValueRequests.Should().NotContain(r => r.Ordinal == 1, "the delegate exits before the non-required Name column is read");
            logger.EntriesNamed(nameof(LoggingExtensions.RequiredPropertyIsDbNull)).Should().ContainSingle().Which.Level.Should().Be(LogLevel.Debug);
        }

        [Fact]
        [Trait("Spec", "blk-30c5f005122d42c08c82c9f63a1e4ce4")]
        [Trait("Spec", "blk-3d44c2d1a05c4aef91153f233526599d")]
        public void AListOmitsRowsWhoseRequiredColumnIsNull()
        {
            var rdr = Reader(Rows(new[] { "Id", "Name" }, new object?[] { 1, "a" }, new object?[] { null, "b" }, new object?[] { 3, "c" }));

            var list = rdr.ToList<Order>(0, null);

            list.Select(o => o.Id).Should().Equal(1, 3);
        }

        [Fact]
        [Trait("Spec", "blk-3d44c2d1a05c4aef91153f233526599d")]
        public void ARequiredNullColumnIsDetectedWhenItIsNotTheFirstColumnRead()
        {
            var rdr = Reader(Rows(new[] { "Name", "OrderId" }, new object?[] { "present", null }, new object?[] { "kept", 2 }));

            var list = rdr.ToList<RequiredSecondColumn>(0, null);
            var single = Reader(Rows(new[] { "Name", "OrderId" }, new object?[] { "present", null })).ToModel<RequiredSecondColumn>(null);

            list.Select(o => o.OrderId).Should().Equal(new[] { 2 }, "the row whose required column is null is omitted, whichever ordinal the column has");
            single.Should().BeNull();
        }

        [Fact]
        [Trait("Spec", "blk-219fd7bf7e59483292c731155f9c80b4")]
        [Trait("Spec", "blk-3d44c2d1a05c4aef91153f233526599d")]
        public void ARequiredNullOutputParameterYieldsANullModelWithoutAssigningLaterProperties()
        {
            var instance = new RequiredTotal();
            var prms = Outputs(("@Name", "changed"), ("@Total", null));

            var result = prms.SetModel(instance, 0, null);

            result.Should().BeNull();
            instance.Name.Should().Be("untouched", "the delegate returns before the non-required Name parameter is read");
        }

        [Fact]
        [Trait("Spec", "blk-1d3850ad10ae44aab00bb63f238ad6ba")]
        [Trait("Spec", "blk-d0409f226e62480eab0a8df308bebefd")]
        [Trait("Spec", "blk-81272a256dda480ea83d7417c3f74d97")]
        [Trait("Spec", "blk-130c78e66f044eabace55cf22d8f57f9")]
        public void DbNullColumnsAssignTheSentinelToValueTypesAndNullToNullableProperties()
        {
            var columns = new[] { "Score", "Ratio", "Temp", "Key", "OptionalKey", "IntStatus", "TextStatus", "Maybe", "Label" };
            var rdr = Reader(Rows(columns, new object?[] { null, null, null, null, null, null, null, null, null }));

            var model = rdr.ToModel<Sentinels>(null);

            model!.Score.Should().Be(double.NaN);
            model.Ratio.Should().BeNull();
            model.Temp.Should().Be(float.NaN);
            model.Key.Should().Be(Guid.Empty);
            model.OptionalKey.Should().BeNull();
            model.IntStatus.Should().BeNull("a nullable enum read through the integral path is null, not parsed");
            model.TextStatus.Should().BeNull("a nullable enum read through the string path is null, not parsed");
            model.Maybe.Should().BeNull();
            model.Label.Should().BeNull();
            rdr.FieldValueRequests.Should().BeEmpty("no database value is read or converted for a null column");
        }

        [Fact]
        [Trait("Spec", "blk-1d3850ad10ae44aab00bb63f238ad6ba")]
        public void NonNullColumnsAssignTheDatabaseValueOnTheSentinelPath()
        {
            var guid = Guid.NewGuid();
            var columns = new[] { "Score", "Ratio", "Temp", "Key", "OptionalKey", "IntStatus", "TextStatus" };
            var rdr = Reader(Rows(columns, new object?[] { 1.25, 2.5, 3.5f, guid, guid, 2, "Active" }));

            var model = rdr.ToModel<Sentinels>(null);

            model!.Score.Should().Be(1.25);
            model.Ratio.Should().Be(2.5);
            model.Temp.Should().Be(3.5f);
            model.Key.Should().Be(guid);
            model.OptionalKey.Should().Be(guid);
            model.IntStatus.Should().Be(OrderStatus.Shipped);
            model.TextStatus.Should().Be(OrderStatus.Active);
        }

        #endregion

        #region Nested models

        [Fact]
        [Trait("Spec", "blk-35366bc482dc45c3827746021836fc8c")]
        [Trait("Spec", "blk-48b74c836b7a45bca710fee937fe54c9")]
        [Trait("Spec", "blk-7d91b0059e274553b8536d28c3a8f390")]
        [Trait("Spec", "blk-967fa79ea9d74f6e8faa5b9f2588f843")]
        public void NestedMapToModelPropertiesFlattenIntoOneParameterSetAndANullNestedInstanceIsCreated()
        {
            var customer = new Customer { CustomerId = 7, Address = null };
            var prms = new ParameterCollection();

            prms.CreateInputParameters(customer, null);

            prms.Cast<DbParameter>().Select(p => p.ParameterName).Should().Equal("@CustomerId", "@Street", "@City", "@Lat");
            customer.Address.Should().NotBeNull("a null writable nested instance is created so its properties can be read");
            customer.Address!.Point.Should().NotBeNull("instantiation applies at any depth");
            prms["@Street"].Value.Should().Be(DBNull.Value);
            prms.Cast<DbParameter>().Select(p => p.ParameterName).Should().NotContain("@Lon", "a MapToModel property whose type is a value type is skipped");
        }

        [Fact]
        [Trait("Spec", "blk-35366bc482dc45c3827746021836fc8c")]
        [Trait("Spec", "blk-48b74c836b7a45bca710fee937fe54c9")]
        public void NestedMapToModelPropertiesAreReadFromTheSameRowOnEveryMappingDirection()
        {
            var rdr = Reader(Rows(new[] { "CustomerId", "Street", "City", "Lat" }, new object?[] { 7, "1 Main", "Springfield", 1.5 }));
            var prms = Outputs(("@CustomerId", 8), ("@Street", "2 Side"), ("@City", "Shelbyville"), ("@Lat", 2.5));
            var outputs = new ParameterCollection();

            var fromReader = rdr.ToModel<Customer>(null);
            var fromOutputs = prms.ToModel<Customer>(null);
            outputs.CreateOutputParameters<Customer>(null);

            fromReader!.Address!.Street.Should().Be("1 Main");
            fromReader.Address.City.Should().Be("Springfield");
            fromReader.Address.Point!.Lat.Should().Be(1.5);
            fromOutputs!.CustomerId.Should().Be(8);
            fromOutputs.Address!.City.Should().Be("Shelbyville");
            fromOutputs.Address.Point!.Lat.Should().Be(2.5);
            outputs.Cast<DbParameter>().Select(p => p.ParameterName).Should().Equal("@CustomerId", "@Street", "@City", "@Lat");
        }

        [Fact]
        [Trait("Spec", "blk-967fa79ea9d74f6e8faa5b9f2588f843")]
        public void AnAlreadyPopulatedOrReadOnlyNestedPropertyKeepsItsInstance()
        {
            var existing = new Address { Street = "keep" };
            var customer = new Customer { Address = existing };
            var fixedCustomer = new CustomerWithFixedAddress();
            var shipping = fixedCustomer.Shipping;

            new ParameterCollection().CreateInputParameters(customer, null);
            var read = Reader(Rows(new[] { "CustomerId", "Street" }, new object?[] { 1, "read" })).SetModel(fixedCustomer, null);

            customer.Address.Should().BeSameAs(existing);
            read!.Shipping.Should().BeSameAs(shipping, "a non-writable nested property is not re-assigned");
            read.Shipping.Street.Should().Be("read", "its mapped properties are still populated");
        }

        [Fact]
        [Trait("Spec", "blk-a5bc17a424e4439e948e48d92ce0d54f")]
        public void MapToModelIsAParameterlessMarkerAttribute()
        {
            typeof(MapToModel).GetConstructors().Should().ContainSingle().Which.GetParameters().Should().BeEmpty();
            typeof(MapToModel).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Should().BeEmpty();
            var usage = typeof(MapToModel).GetCustomAttribute<AttributeUsageAttribute>()!;
            usage.ValidOn.Should().Be(AttributeTargets.Property);
            usage.AllowMultiple.Should().BeFalse();
            usage.Inherited.Should().BeFalse();
        }

        #endregion

        #region Parameter creation

        [Fact]
        [Trait("Spec", "blk-335b328a794c47a0b650e504c88e0109")]
        [Trait("Spec", "blk-39b5f4e3d08a457e9bbdbab3d455030a")]
        public void CreateInputParametersMergesPreExistingNamesIntoTheIgnoreSetAndReturnsTheSameCollection()
        {
            var prms = new ParameterCollection { new FakeDbParameter("@ExistingId", 1), new FakeDbParameter("@Id", 99) };
            var ignore = new HashSet<string> { "@Name" };

            var returned = prms.CreateInputParameters(new Order { Id = 5, Name = "n" }, ignore, null);

            returned.Should().BeSameAs(prms);
            ignore.Should().Contain("@ExistingId");
            ignore.Should().Contain("@Id");
            prms.Count.Should().Be(2, "the pre-existing @Id is not appended again and @Name is explicitly ignored");
            prms["@Id"].Value.Should().Be(99, "the pre-existing parameter keeps its value");
        }

        [Fact]
        [Trait("Spec", "blk-0cf756c7e36349549c39e2cd709e9dc5")]
        [Trait("Spec", "blk-fa098f31c5d548d789a47a63aa3cf838")]
        public void CreateOutputParametersMergesPreExistingNamesIntoTheIgnoreSetAndReturnsTheSameCollection()
        {
            var prms = new ParameterCollection { new FakeDbParameter("@ExistingId", 1) };
            var ignore = new HashSet<string> { "@Name" };

            var returned = prms.CreateOutputParameters<Order>(ignore, null);

            returned.Should().BeSameAs(prms);
            ignore.Should().Contain("@ExistingId");
            prms.Cast<DbParameter>().Select(p => p.ParameterName).Should().Equal("@ExistingId", "@Id");
            prms["@Id"].Direction.Should().Be(ParameterDirection.Output);
        }

        [Fact]
        [Trait("Spec", "blk-74cb7f841ec74553849e26e3d9785a41")]
        public void MapperFamiliesAreCallableAsExtensionsAndAsStaticMethods()
        {
            var model = new Order { Id = 1, Name = "n" };
            var asExtension = new ParameterCollection().CreateInputParameters(model, null);
            var asStatic = Mapper.CreateInputParameters(new ParameterCollection(), model, null);
            var outExtension = new ParameterCollection().CreateOutputParameters<Order>(null);
            var outStatic = Mapper.CreateOutputParameters(new ParameterCollection(), typeof(Order), null);

            asExtension.Cast<DbParameter>().Select(p => p.ParameterName).Should().Equal(asStatic.Cast<DbParameter>().Select(p => p.ParameterName));
            outExtension.Cast<DbParameter>().Select(p => p.ParameterName).Should().Equal(outStatic.Cast<DbParameter>().Select(p => p.ParameterName));
            Mapper.ToModel<Order>(Reader(IdRows(4)), null)!.Id.Should().Be(4);
            Mapper.ToList<Order>(Reader(IdRows(4)), null).Should().ContainSingle();
            Mapper.ToModel<Order>(Outputs(("@Id", 6)), null)!.Id.Should().Be(6);
        }

        [Fact]
        [Trait("Spec", "blk-953dd93d5e8346bb92dc455a25e5da93")]
        public void DummyTypeIsAnEmptyPublicMarker()
        {
            new Mapper.DummyType().Should().NotBeNull();
            typeof(Mapper.DummyType).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Should().ContainSingle().Which.Should().BeAssignableTo<ConstructorInfo>();
        }

        #endregion

        #region Diagnostics

        [Fact]
        [Trait("Spec", "blk-a0a30c51c4794b4fb52c49ef99eb5512")]
        [Trait("Spec", "blk-bfa1d396061847979c2452d3680c0d8c")]
        public void TwoMappingAttributesOnOnePropertyThrowMultipleMapAttributesExceptionNamingTheClassAndProperty()
        {
            Action input = () => new ParameterCollection().CreateInputParameters(new DoubleMapped(), null);
            Action output = () => new ParameterCollection().CreateOutputParameters<DoubleMapped>(null);
            Action read = () => Reader(Rows(new[] { "A" }, new object?[] { 1 })).ToModel<DoubleMapped>(null);

            var ex = input.Should().Throw<MultipleMapAttributesException>().Which;
            ex.Property!.Name.Should().Be("X");
            ex.Message.Should().Contain(typeof(DoubleMapped).FullName!.Replace('+', '+')).And.Contain("property X");
            output.Should().Throw<MultipleMapAttributesException>().Which.Property!.DeclaringType.Should().Be(typeof(DoubleMapped));
            read.Should().Throw<MultipleMapAttributesException>();
            input.Should().Throw<MultipleMapAttributesException>("no delegate was compiled, so the second use fails the same way");
        }

        [Fact]
        [Trait("Spec", "blk-6d982b581a27489d8de62bb7f9401ec6")]
        [Trait("Spec", "blk-a45e777a573d4080ac3c3b785c8c4f9e")]
        public void AnIncompatiblePropertyTypeThrowsInvalidMapTypeExceptionBeforeAnyParameterIsAppended()
        {
            var prms = new ParameterCollection();

            Action act = () => prms.CreateInputParameters(new WrongType { First = 1, Name = "n" }, null);

            var ex = act.Should().Throw<InvalidMapTypeException>().Which;
            ex.VariableName.Should().Be("Name");
            ex.VariableType.Should().Be(typeof(string));
            ex.SqlType.Should().Be((int)DbType.Int32);
            ex.Message.Should().Be($"Sql type mismatch: Class {typeof(WrongType)} cannot map property “Name” of type “System.String” to database type fake-int ({(int)DbType.Int32}).");
            prms.Count.Should().Be(0, "the type check happens while compiling, before the First parameter could be appended");
            new Action(() => new ParameterCollection().CreateOutputParameters<WrongType>(null)).Should().Throw<InvalidMapTypeException>();
            new Action(() => Reader(IdRows(1)).ToModel<WrongType>(null)).Should().Throw<InvalidMapTypeException>();
        }

        [Fact]
        [Trait("Spec", "blk-24b13dadb4864807854d811fe54b0fe5")]
        public void InvalidMapTypeExceptionConstructorsCarryTheVariableNameTypeAndSqlType()
        {
            var prop = typeof(WrongType).GetProperty(nameof(WrongType.Name))!;

            var withName = new InvalidMapTypeException(prop, 7, "nvarchar");
            var numberOnly = new InvalidMapTypeException(prop, 7);
            var variable = new InvalidMapTypeException("key_RecordId", typeof(long), 7, "bigint");
            var variableNumberOnly = new InvalidMapTypeException("key_RecordId", typeof(long), 7);

            withName.Message.Should().Be($"Sql type mismatch: Class {typeof(WrongType)} cannot map property “Name” of type “System.String” to database type nvarchar (7).");
            numberOnly.Message.Should().Be($"Sql type mismatch: Class {typeof(WrongType)} cannot map property “Name” of type “System.String” to database type enumeration with numeric value of 7.");
            variable.Message.Should().Be("Sql type mismatch: key_RecordId cannot be mapped because type System.Int64 does not map to database type bigint (7).");
            variableNumberOnly.Message.Should().Be("Sql type mismatch: key_RecordId cannot be mapped because type System.Int64 does not map to database type enumeration with numeric value of 7.");
            new[] { withName, numberOnly }.Should().OnlyContain(e => e.VariableName == "Name" && e.VariableType == typeof(string) && e.SqlType == 7);
            new[] { variable, variableNumberOnly }.Should().OnlyContain(e => e.VariableName == "key_RecordId" && e.VariableType == typeof(long) && e.SqlType == 7);
            new InvalidMapTypeException("plain").SqlType.Should().Be(0);
            new InvalidMapTypeException("plain").VariableName.Should().BeNull();
        }

        [Fact]
        [Trait("Spec", "blk-d6855156da99437c86161c764dceebb3")]
        [Trait("Spec", "blk-425f912b5f334f3bb2e0a7f3031e5a5a")]
        public void AModelWithoutAnyMappingAttributeThrowsNoMappingAttributesFoundExceptionFromEveryBuilder()
        {
            new Action(() => new ParameterCollection().CreateInputParameters(new Unmapped(), null)).Should().Throw<NoMappingAttributesFoundException>();
            new Action(() => new ParameterCollection().CreateOutputParameters<Unmapped>(null)).Should().Throw<NoMappingAttributesFoundException>();
            new Action(() => Outputs(("@Id", 1)).ToModel<Unmapped>(null)).Should().Throw<NoMappingAttributesFoundException>();
            new Action(() => Reader(IdRows(1)).ToModel<Unmapped>(null)).Should().Throw<NoMappingAttributesFoundException>();
            new Action(() => Reader(IdRows(1)).ToList<Unmapped>(null)).Should().Throw<NoMappingAttributesFoundException>();
        }

        [Fact]
        [Trait("Spec", "blk-425f912b5f334f3bb2e0a7f3031e5a5a")]
        public void ACollectionMapPropertyCountsAsAMappingForInputParametersOnly()
        {
            var prms = new ParameterCollection();

            prms.CreateInputParameters(new CollectionOnly { Items = new List<TestModels.OrderLine>() }, null);

            prms.Cast<DbParameter>().Select(p => p.ParameterName).Should().Equal("@Items");
            new Action(() => new ParameterCollection().CreateOutputParameters<CollectionOnly>(null)).Should().Throw<NoMappingAttributesFoundException>();
            new Action(() => Reader(IdRows(1)).ToModel<CollectionOnly>(null)).Should().Throw<NoMappingAttributesFoundException>();
        }

        [Fact]
        [Trait("Spec", "blk-5c0b0a84efb9437bb74667b0237a39e0")]
        [Trait("Spec", "blk-f114f292160a44c98ae561e2802ff50a")]
        public void AShardKeyWhoseRecordIdNameMatchesNoAttributeThrowsMapAttributeMissingExceptionForRecordId()
        {
            Action output = () => new ParameterCollection().CreateOutputParameters<MissingRecordIdName>(null);
            Action input = () => new ParameterCollection().CreateInputParameters(new MissingRecordIdName(), null);

            var ex = output.Should().Throw<MapAttributeMissingException>().Which;
            ex.Element.Should().Be(MapAttributeMissingException.ShardElement.RecordId);
            ex.AttributeName.Should().Be("OrderId");
            input.Should().Throw<MapAttributeMissingException>().Which.Element.Should().Be(MapAttributeMissingException.ShardElement.RecordId);
        }

        [Fact]
        [Trait("Spec", "blk-5c0b0a84efb9437bb74667b0237a39e0")]
        [Trait("Spec", "blk-74f0d7480cb745a09f617f02fd44ed40")]
        public void EachMissingChildElementIsReportedWithItsElementAndDeclaredName()
        {
            var child = new Action(() => new ParameterCollection().CreateOutputParameters<MissingChildIdName>(null)).Should().Throw<MapAttributeMissingException>().Which;
            var grandChild = new Action(() => new ParameterCollection().CreateOutputParameters<MissingGrandChildIdName>(null)).Should().Throw<MapAttributeMissingException>().Which;
            var greatGrandChild = new Action(() => new ParameterCollection().CreateOutputParameters<MissingGreatGrandChildIdName>(null)).Should().Throw<MapAttributeMissingException>().Which;

            child.Element.Should().Be(MapAttributeMissingException.ShardElement.ChildId);
            child.AttributeName.Should().Be("Kid");
            child.Message.Should().Be("The ShardChild attribute specified a child id attribute named “Kid”, but the attribute was not found. Ensure that the name specified in the ShardChild exactly matches the attribute name.");
            grandChild.Element.Should().Be(MapAttributeMissingException.ShardElement.GrandChildId);
            grandChild.AttributeName.Should().Be("GrandKid");
            greatGrandChild.Element.Should().Be(MapAttributeMissingException.ShardElement.GreatGrandChildId);
            greatGrandChild.AttributeName.Should().Be("GreatGrandKid");
            grandChild.Message.Should().Be("The shard attribute specified a child id attribute named “GrandKid”, but the attribute was not found. Ensure that the name specified exactly matches the attribute name.");
            greatGrandChild.Message.Should().Be(grandChild.Message.Replace("GrandKid", "GreatGrandKid"), "the two deeper elements share one generic message");
        }

        [Fact]
        [Trait("Spec", "blk-74f0d7480cb745a09f617f02fd44ed40")]
        [Trait("Spec", "blk-304e56fc40d845c0b5317d4916301c29")]
        [Trait("Spec", "blk-829c21fd121145b38a08d7ea612e570d")]
        [Trait("Spec", "blk-24b13dadb4864807854d811fe54b0fe5")]
        public void MapAttributeMissingExceptionMessagesAndShardElementLadderMatchTheContract()
        {
            Enum.GetNames(typeof(MapAttributeMissingException.ShardElement)).Should().Equal("ShardId", "RecordId", "ChildId", "GrandChildId", "GreatGrandChildId");
            new MapAttributeMissingException(MapAttributeMissingException.ShardElement.ShardId, "S").Message
                .Should().Be("The shard attribute specified a ShardId argument attribute named “S”, but the attribute was not found. Remove this argument if you do not have a Shard Id parameter or column, or ensure that the specified name exactly matches the attribute name.");
            new MapAttributeMissingException(MapAttributeMissingException.ShardElement.RecordId, "R").Message
                .Should().Be("The shard attribute specified a RecordId argument attribute named “R”, but the attribute was not found. Ensure that the specified name exactly matches the attribute name.");
            var plain = new MapAttributeMissingException();
            plain.Message.Should().Be("No mapping attributes could be found on the model class provided to the mapper.");
            plain.Element.Should().Be(MapAttributeMissingException.ShardElement.ShardId);
            plain.AttributeName.Should().BeNull();
            new MultipleMapAttributesException().Message.Should().Be("Multiple mapping attributes were found on the same property.");
            new MultipleMapAttributesException().Property.Should().BeNull();
        }

        [Fact]
        [Trait("Spec", "blk-5c0b0a84efb9437bb74667b0237a39e0")]
        [Trait("Spec", "blk-07a265867fe344829eb3941e63ff0199")]
        public void AShardKeyWithoutADeclaredShardIdTakesItsShardIdFromTheConnectionAndCreatesNoShardParameter()
        {
            var attribute = typeof(KeyWithoutShardId).GetProperty(nameof(KeyWithoutShardId.Key))!.GetCustomAttribute<MapShardKeyAttribute>()!;
            var outputs = new ParameterCollection().CreateOutputParameters<KeyWithoutShardId>(null);

            var fromReader = Reader(Rows(new[] { "Id", "Name" }, new object?[] { 12, "n" })).ToModel<KeyWithoutShardId>(7, null);
            var fromOutputs = Outputs(("@Id", 13)).ToModel<KeyWithoutShardId>(9, null);
            var list = Reader(IdRows(14)).ToList<KeyWithoutShardId>(11, null);

            attribute.ShardParameter.Should().BeNull();
            attribute.ShardIdName.Should().BeNull();
            outputs.Cast<DbParameter>().Select(p => p.ParameterName).Should().Equal("@Id", "@Name");
            fromReader!.Key.Should().Be(new ShardKey<int>(7, 12));
            fromOutputs!.Key.Should().Be(new ShardKey<int>(9, 13));
            list.Single().Key.Should().Be(new ShardKey<int>(11, 14));
        }

        [Fact]
        [Trait("Spec", "blk-07a265867fe344829eb3941e63ff0199")]
        [Trait("Spec", "blk-d4b2effd5ead43238d5f127b11986fc9")]
        public void MapShardKeyAttributeExposesItsRetainedNamesAndShardParameter()
        {
            var attribute = typeof(ChildKeyWithShardId).GetProperty(nameof(ChildKeyWithShardId.Key))!.GetCustomAttribute<MapShardKeyAttribute>()!;

            attribute.ShardIdName.Should().Be("ShardId");
            attribute.RecordIdName.Should().Be("RecordId");
            attribute.ChildIdName.Should().Be("ChildId");
            attribute.GrandChildIdName.Should().BeNull();
            attribute.GreatGrandChildIdName.Should().BeNull();
            attribute.IsRecordIdentifier.Should().BeTrue();
            attribute.ShardParameter.Should().BeOfType<FakeMapAttribute>().Which.ColumnName.Should().Be("ShardId");
            attribute.ShardParameter.Should().BeSameAs(attribute.ShardParameter, "the retained parameter is exposed without mutation");
            foreach (var name in new[] { "ShardIdName", "RecordIdName", "ChildIdName", "GrandChildIdName", "GreatGrandChildIdName", "IsRecordIdentifier" })
            {
                typeof(MapShardKeyAttribute).GetProperty(name)!.GetGetMethod()!.IsVirtual.Should().BeTrue($"{name} is overridable");
            }
            typeof(MapShardKeyAttribute).GetProperty(nameof(MapShardKeyAttribute.ShardParameter))!.GetGetMethod()!.IsVirtual.Should().BeFalse();
        }

        [Fact]
        [Trait("Spec", "blk-d6d9747f251a48e5a061da41ff7c9995")]
        [Trait("Spec", "blk-7cc0f1eb547544479693400fcf368edd")]
        public void ParameterMapAttributeBaseExposesFixedAndOverridableMetadata()
        {
            var twoArgument = new FakeMapAttribute("Name");
            var threeArgument = new FakeMapAttribute("Name", true);

            twoArgument.IsRecordIdentifier.Should().BeFalse("the two-argument constructor defaults the flag");
            threeArgument.IsRecordIdentifier.Should().BeTrue();
            twoArgument.Name.Should().Be("Name");
            twoArgument.ParameterName.Should().Be("@Name");
            twoArgument.ColumnName.Should().Be("Name");
            twoArgument.SqlType.Should().Be(0);
            foreach (var name in new[] { "ParameterName", "ColumnName", "SqlTypeName" })
            {
                typeof(ParameterMapAttributeBase).GetProperty(name)!.GetGetMethod()!.IsVirtual.Should().BeTrue($"{name} may be overridden by a provider attribute");
            }
            foreach (var name in new[] { "Name", "SqlType", "IsRecordIdentifier" })
            {
                typeof(ParameterMapAttributeBase).GetProperty(name)!.GetGetMethod()!.IsVirtual.Should().BeFalse($"{name} is fixed by the base constructor");
                typeof(ParameterMapAttributeBase).GetProperty(name)!.GetSetMethod(true)!.IsPrivate.Should().BeTrue();
            }
            typeof(ParameterMapAttributeBase).IsAbstract.Should().BeTrue();
        }

        [Fact]
        [Trait("Spec", "blk-f4cedcbbe01845b2b66eb88af9cc5685")]
        [Trait("Spec", "blk-81e4b717b5f1407fa0ca58cf06ff93c4")]
        public void CollectionMapAttributeBaseIsConstructedOnlyByDerivedAttributesAndExposesItsNameReadOnly()
        {
            typeof(CollectionMapAttributeBase).IsAbstract.Should().BeTrue();
            typeof(CollectionMapAttributeBase).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty();
            typeof(CollectionMapAttributeBase).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Should().ContainSingle().Which.IsFamily.Should().BeTrue();
            var parameterName = typeof(CollectionMapAttributeBase).GetProperty(nameof(CollectionMapAttributeBase.ParameterName))!;
            parameterName.CanWrite.Should().BeFalse();
            new FakeCollectionMapAttribute("@Items").ParameterName.Should().Be("@Items");
            typeof(CollectionMapAttributeBase).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.IsAbstract)
                .Select(m => m.Name).Should().Equal(nameof(CollectionMapAttributeBase.AppendCollectionInParameterExpressions));
        }

        #endregion

        #region Presence tolerance and caching

        [Fact]
        [Trait("Spec", "blk-3e901a3ab31d42e18a13322da5b7edbc")]
        [Trait("Spec", "blk-7c59d390938f40c3b1ea467f2874476f")]
        [Trait("Spec", "blk-a4e5d704d059402183a36ee6efa79939")]
        public void AMissingColumnIsSkippedAndLoggedAtDebugWhileATypeMismatchThrows()
        {
            var logger = new RecordingLogger();
            var rdr = Reader(Rows(new[] { "Count" }, new object?[] { 7 }));

            var model = rdr.ToModel<ReaderShapes>(logger);
            Action mismatch = () => Reader(Rows(new[] { "Count" }, new object?[] { "seven" })).ToModel<ReaderShapes>(logger);

            model!.Count.Should().Be(7);
            model.MiddleName.Should().Be("unchanged", "no read or assignment runs for a column that is absent");
            rdr.FieldValueRequests.Should().ContainSingle().Which.Ordinal.Should().Be(0);
            var missing = logger.EntriesFor(LoggingExtensions.EventIdentifier.MapperSqlColumnNotFound);
            missing.Should().OnlyContain(e => e.Level == LogLevel.Debug);
            missing.Select(e => e.Message).Should().Contain(m => m!.Contains("MiddleName"));
            mismatch.Should().Throw<InvalidCastException>();
        }

        [Fact]
        [Trait("Spec", "blk-74cb7f841ec74553849e26e3d9785a41")]
        [Trait("Spec", "blk-24b13dadb4864807854d811fe54b0fe5")]
        public void TheSecondUseOfAModelTypeIsACacheHitThatDoesNotRecompile()
        {
            var logger = new RecordingLogger();

            Reader(IdRows(1)).ToModel<CachedModel>(logger);
            var compiled = logger.EntriesFor(LoggingExtensions.EventIdentifier.ExpressionTreeCreation).Count;
            logger.Clear();
            Reader(IdRows(2)).ToModel<CachedModel>(logger);

            compiled.Should().BeGreaterThan(0, "the first use compiles the reader delegates");
            logger.EntriesFor(LoggingExtensions.EventIdentifier.ExpressionTreeCreation).Should().BeEmpty("nothing is recompiled");
            logger.EntriesNamed(nameof(LoggingExtensions.SqlReaderCacheHit)).Should().ContainSingle().Which.Level.Should().Be(LogLevel.Trace);
        }

        [Fact]
        [Trait("Spec", "blk-74cb7f841ec74553849e26e3d9785a41")]
        public void TheSecondUseOfAModelTypeForInputParametersIsACacheHit()
        {
            var logger = new RecordingLogger();

            new ParameterCollection().CreateInputParameters(new CachedInputModel(), logger);
            logger.EntriesNamed(nameof(LoggingExtensions.SqlInParametersCacheMiss)).Should().ContainSingle();
            logger.Clear();
            new ParameterCollection().CreateInputParameters(new CachedInputModel(), logger);

            logger.EntriesNamed(nameof(LoggingExtensions.SqlInParametersCacheHit)).Should().ContainSingle();
            logger.EntriesFor(LoggingExtensions.EventIdentifier.ExpressionTreeCreation).Should().BeEmpty();
        }

        #endregion

        #region Expression builders: input parameters

        [Fact]
        [Trait("Spec", "blk-591fd4f0b5014359b62012a80836bc31")]
        [Trait("Spec", "blk-91dad35ba01f4212b1482f27a7dfff8a")]
        public void AnEnumWrittenToAStringParameterIsItsNameAndANullNullableEnumIsNull()
        {
            var prms = new ParameterCollection();

            prms.CreateInputParameters(new InputShapes { Status = OrderStatus.Shipped, OptionalStatus = null, Name = "abc" }, null);

            prms["@Status"].Value.Should().Be("Shipped", "the enum is converted with ToString, not to its numeric value");
            prms["@Status"].Size.Should().Be(20);
            prms["@OptionalStatus"].Value.Should().Be(DBNull.Value);
            prms["@Name"].Value.Should().Be("abc", "a plain string passes through unconverted");
            new ParameterCollection().CreateInputParameters(new InputShapes { OptionalStatus = OrderStatus.Active }, null)["@OptionalStatus"].Value.Should().Be("Active");
        }

        [Fact]
        [Trait("Spec", "blk-b8ec66d09d614cf1be1cd9bf5aa02f4b")]
        [Trait("Spec", "blk-d7f44139e4f146e3b30bf9b8ca8660d2")]
        [Trait("Spec", "blk-c3374db5eda5474b951a1d35559cfd30")]
        [Trait("Spec", "blk-ce2c34a1fd404ccfad65607abeabd381")]
        public void AnEnumWrittenToAnIntegralParameterIsItsUnderlyingValueAndPlainValuesPassThrough()
        {
            var prms = new ParameterCollection();

            prms.CreateInputParameters(new InputShapes { Level = Priority.High, OptionalLevel = null, Quantity = 5 }, null);

            prms["@Level"].Value.Should().Be((byte)2).And.BeOfType<byte>();
            prms["@Level"].DbType.Should().Be(DbType.Byte);
            prms["@OptionalLevel"].Value.Should().Be(DBNull.Value);
            prms["@Quantity"].Value.Should().Be(5).And.BeOfType<int>();
            new ParameterCollection().CreateInputParameters(new InputShapes { OptionalLevel = Priority.Low }, null)["@OptionalLevel"].Value.Should().Be((byte)1);
        }

        [Fact]
        [Trait("Spec", "blk-c3374db5eda5474b951a1d35559cfd30")]
        [Trait("Spec", "blk-db96a707c5ef45018572db8e7930ec24")]
        public void AByteArrayIsPassedToTheBinaryParameterWithoutCopying()
        {
            var payload = new byte[] { 0x01, 0x02 };
            var prms = new ParameterCollection();

            prms.CreateInputParameters(new InputShapes { Payload = payload }, null);

            prms["@Payload"].Value.Should().BeSameAs(payload);
            new ParameterCollection().CreateInputParameters(new InputShapes { Payload = null }, null)["@Payload"].Value.Should().Be(DBNull.Value);
        }

        [Fact]
        [Trait("Spec", "blk-1ad7e1657ba540d0b0c4f26504453189")]
        [Trait("Spec", "blk-80766e21ca314079884c4f05707eee1b")]
        [Trait("Spec", "blk-9ee50cdec49c47d4840beb945cd1015a")]
        public void AnInputParameterExpressionIsBuiltOnceAndHonoursTheIgnoreSet()
        {
            var expressions = new List<Expression>();
            var expPrms = Expression.Parameter(typeof(DbParameterCollection), "parameters");
            var expIgnore = Expression.Parameter(typeof(HashSet<string>), "ignoreParameters");
            var expLogger = Expression.Parameter(typeof(ILogger), "logger");
            var names = new HashSet<string>();

            ExpressionHelpers.InParameterStringExpressionBuilder("name", 50, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeStringInputParameter), null, expressions, expPrms, expIgnore, names, Expression.Constant("Alice"), typeof(string), expLogger, null);
            ExpressionHelpers.InParameterStringExpressionBuilder("name", 50, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeStringInputParameter), null, expressions, expPrms, expIgnore, names, Expression.Constant("Alice"), typeof(string), expLogger, null);

            expressions.Should().ContainSingle("a second build for the same name adds no second call");
            var run = Expression.Lambda<Action<DbParameterCollection, HashSet<string>, ILogger?>>(Expression.Block(expressions), expPrms, expIgnore, expLogger).Compile();
            var added = new ParameterCollection();
            run(added, new HashSet<string> { "id" }, null);
            var ignored = new ParameterCollection();
            run(ignored, new HashSet<string> { "name" }, null);
            var noIgnoreSet = new ParameterCollection();
            run(noIgnoreSet, null!, null);
            added.Cast<DbParameter>().Should().ContainSingle().Which.Size.Should().Be(50);
            added["@name"].Value.Should().Be("Alice");
            ignored.Count.Should().Be(0, "the name is in the ignore set");
            noIgnoreSet.Count.Should().Be(0, "without an ignore set no add call is made");
        }

        [Fact]
        [Trait("Spec", "blk-1ad7e1657ba540d0b0c4f26504453189")]
        public void AnOutputParameterExpressionIsBuiltOnceForAName()
        {
            var expressions = new List<Expression>();
            var expPrms = Expression.Parameter(typeof(DbParameterCollection), "parameters");
            var expIgnore = Expression.Parameter(typeof(HashSet<string>), "ignoreParameters");
            var names = new HashSet<string>();

            ExpressionHelpers.OutParameterBuilder("@name", expPrms, expressions, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeOutputParameter), null, null, names, expIgnore, null);
            ExpressionHelpers.OutParameterBuilder("@name", expPrms, expressions, typeof(FakeParameterExtensions), nameof(FakeParameterExtensions.AddFakeOutputParameter), null, null, names, expIgnore, null);

            expressions.Should().ContainSingle();
            var run = Expression.Lambda<Action<DbParameterCollection, HashSet<string>>>(Expression.Block(expressions), expPrms, expIgnore).Compile();
            var prms = new ParameterCollection();
            run(prms, new HashSet<string>());
            prms.Cast<DbParameter>().Should().ContainSingle().Which.Direction.Should().Be(ParameterDirection.Output);
        }

        [Fact]
        [Trait("Spec", "blk-55eaf0457ee641f4a640283c9b3bc934")]
        [Trait("Spec", "blk-aa4ff4d93c1b4ef69214505cf5a08bca")]
        public void ACompoundKeyWritesItsShardRecordAndChildValuesToTheConfiguredParameters()
        {
            var populated = new ParameterCollection().CreateInputParameters(new ChildKeyWithShardId { Key = new ShardKey<int, short>(4, 12, 8) }, null);
            var empty = new ParameterCollection().CreateInputParameters(new ChildKeyWithShardId { Key = ShardKey<int, short>.Empty }, null);
            var absent = new ParameterCollection().CreateInputParameters(new NullableChildKeyWithShardId { Key = null }, null);

            populated.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value)).Should().Equal(("@ShardId", (object)(short)4), ("@RecordId", 12), ("@ChildId", (short)8));
            empty.Cast<DbParameter>().Select(p => p.Value).Should().OnlyContain(v => v == DBNull.Value, "an empty key supplies null for each configured element");
            absent.Cast<DbParameter>().Select(p => p.Value).Should().OnlyContain(v => v == DBNull.Value, "a nullable absent key supplies null for each configured element");
        }

        #endregion

        #region Expression builders: output parameters

        [Fact]
        [Trait("Spec", "blk-e1839043f5bd41bfb4b9f11cad4bfdcc")]
        [Trait("Spec", "blk-f5019f8a6fc84909a6151d0797f755a0")]
        public void ANullableOutputPropertyUsesTheNullableGetterWhileANonNullableOneUsesTheStandardGetter()
        {
            var nullable = Outputs(("@Count", null)).ToModel<NullableCount>(null);
            Action required = () => Outputs(("@Count", null)).ToModel<RequiredCount>(null);

            nullable!.Count.Should().BeNull("GetNullableInteger tolerates DBNull");
            required.Should().Throw<UnexpectedNullException>("GetInteger does not").Which.ColumnName.Should().Be("@Count");
            Outputs(("@Count", 3)).ToModel<NullableCount>(null)!.Count.Should().Be(3);
            Outputs(("@Count", 3)).ToModel<RequiredCount>(null)!.Count.Should().Be(3);
        }

        [Fact]
        [Trait("Spec", "blk-332ca5f1a36444298f1c4b44153dfedf")]
        [Trait("Spec", "blk-827404c131824147871b77eff71119a4")]
        public void AnOutputPropertyWhoseParameterIsAbsentIsLeftUnchanged()
        {
            var logger = new RecordingLogger();
            var instance = new OutputShapes { Total = 5m, OptionalStatus = OrderStatus.Pending };

            var result = Outputs(("@Name", "n")).SetModel(instance, 0, logger);

            result!.Total.Should().Be(5m);
            result.OptionalStatus.Should().Be(OrderStatus.Pending);
            result.Name.Should().Be("n");
            logger.EntriesFor(LoggingExtensions.EventIdentifier.MapperSqlParameterNotFound).Should().OnlyContain(e => e.Level == LogLevel.Debug)
                .And.Contain(e => e.Message!.Contains("@Total"));
        }

        [Fact]
        [Trait("Spec", "blk-02f1281b198e42019b5557a70c4ec5b5")]
        [Trait("Spec", "blk-81682c1abddc4a46b87f0c35772d060c")]
        [Trait("Spec", "blk-e282009861f047988f2d77daf2c6058f")]
        public void BinaryAndArrayOutputParametersAreAssignedFromTheirGetMethods()
        {
            var thumbnail = new byte[] { 1, 2, 3 };
            var tags = new[] { 4, 5 };

            var model = Outputs(("@Thumbnail", thumbnail), ("@Tags", tags)).ToModel<OutputShapes>(null);

            model!.Thumbnail.Should().BeSameAs(thumbnail);
            model.Tags.Should().BeSameAs(tags, "the array getter is made generic over the element type and returns the array");
        }

        [Fact]
        [Trait("Spec", "blk-af339fd8d27e489693f5d611691f56cd")]
        [Trait("Spec", "blk-c73ea90a280f4d32a19b52280050699b")]
        [Trait("Spec", "blk-9a0c17b8e68d4dcc832be394ab308c5e")]
        public void StringOutputParametersParseEnumsAndNullStringsYieldNullNullableEnums()
        {
            var model = Outputs(("@Status", "Shipped"), ("@OptionalStatus", null), ("@Name", "plain"), ("@IntStatus", 1)).ToModel<OutputShapes>(null);

            model!.Status.Should().Be(OrderStatus.Shipped);
            model.OptionalStatus.Should().BeNull("a null string is not passed to Enum.Parse");
            model.Name.Should().Be("plain");
            model.IntStatus.Should().Be(OrderStatus.Active, "an integral output converts to the enum");
            Outputs(("@OptionalStatus", "Active")).ToModel<OutputShapes>(null)!.OptionalStatus.Should().Be(OrderStatus.Active);
        }

        #endregion

        #region Expression builders: reader columns

        [Fact]
        [Trait("Spec", "blk-374b6c0991434c418060f0f7df97ab00")]
        [Trait("Spec", "blk-fb854e0921ae4ec5928680d3d8d08c9f")]
        [Trait("Spec", "blk-8cd3df53681244348e86b5c8fe64d28d")]
        public void SimpleValueColumnsAreReadWithGetFieldValueOverTheUnderlyingTypeAndOnlyNullableShapesAreNullGuarded()
        {
            var tags = new[] { "a", "b" };
            var rdr = Reader(Rows(new[] { "Count", "Maybe", "Tags" }, new object?[] { 7, 8, tags }));

            var model = rdr.ToModel<ReaderShapes>(null);

            model!.Count.Should().Be(7);
            model.Maybe.Should().Be(8);
            model.Tags.Should().BeSameAs(tags);
            rdr.FieldValueRequests.Select(r => (r.Ordinal, r.RequestedType)).Should().BeEquivalentTo(new[] { (0, typeof(int)), (1, typeof(int)), (2, typeof(string[])) },
                "a non-nullable int, a nullable int (over its underlying type) and an array are all read through GetFieldValue<T>");
            rdr.IsDBNullRequests.Should().NotContain(0, "a non-nullable, non-array property has no DBNull guard");
            rdr.IsDBNullRequests.Should().Contain(1).And.Contain(2);
        }

        [Fact]
        [Trait("Spec", "blk-0ed027f772ae40bcb6b0cf1f0dfe843b")]
        [Trait("Spec", "blk-9a0c17b8e68d4dcc832be394ab308c5e")]
        [Trait("Spec", "blk-4dc64af4779243758c0fc26b1fb68f6f")]
        [Trait("Spec", "blk-82f5b43a6961481b8c654f0c113f0c2e")]
        public void EnumColumnsAreReadAsTheirIntegralBaseTypeOrParsedFromStrings()
        {
            var rdr = Reader(Rows(new[] { "IntStatus", "TextStatus" }, new object?[] { 2, "Active" }));

            var model = rdr.ToModel<ReaderShapes>(null);

            model!.IntStatus.Should().Be(OrderStatus.Shipped);
            model.TextStatus.Should().Be(OrderStatus.Active);
            rdr.FieldValueRequests.Should().Contain(r => r.Ordinal == 0 && r.RequestedType == typeof(int), "the integral enum column is read as int and converted");
            rdr.FieldValueRequests.Should().Contain(r => r.Ordinal == 1 && r.RequestedType == typeof(string), "the string enum column is read as a string and parsed");
        }

        [Fact]
        [Trait("Spec", "blk-7847d645929143d4a106e85cc725d0cd")]
        [Trait("Spec", "blk-a4e5d704d059402183a36ee6efa79939")]
        public void FieldAndParameterLookupsMatchExactlyThenCaseInsensitivelyThenIgnoringWidth()
        {
            var logger = new RecordingLogger();
            var rdr = Reader(Rows(new[] { "Id", "name" }, new object?[] { 1, "n" }));
            var prms = new ParameterCollection { new FakeDbParameter("@Id", 1), new FakeDbParameter("@name", "n") };

            ExpressionHelpers.GetFieldOrdinal(rdr, "NAME", "model", logger).Should().Be(1);
            ExpressionHelpers.GetFieldOrdinal(rdr, "Id", "model", logger).Should().Be(0);
            ExpressionHelpers.GetFieldOrdinal(rdr, "ｎａｍｅ", "model", logger).Should().Be(1, "full-width letters match when width is ignored");
            ExpressionHelpers.GetFieldOrdinal(rdr, "missing", "model", logger).Should().Be(-1);
            ExpressionHelpers.GetParameter(prms, "@NAME")!.ParameterName.Should().Be("@name");
            ExpressionHelpers.GetParameter(prms, "@ｎａｍｅ")!.ParameterName.Should().Be("@name");
            ExpressionHelpers.GetParameter(prms, "@missing").Should().BeNull();
            logger.EntriesFor(LoggingExtensions.EventIdentifier.MapperSqlColumnNotFound).Should().ContainSingle().Which.Message.Should().Contain("missing");
        }

        [Fact]
        [Trait("Spec", "blk-ecbfacfeb1d3474f9b4e87e093282ae9")]
        public void EachReaderBuilderAppendsOneOrdinalLookupAndAdvancesThePropertyIndexByOne()
        {
            var lookups = new List<MethodCallExpression>();
            var expressions = new List<Expression>();
            var expRdr = Expression.Parameter(typeof(DbDataReader), "rdr");
            var expOrdinals = Expression.Parameter(typeof(int[]), "ordinals");
            var expOrdinal = Expression.Variable(typeof(int), "ordinal");
            var expLogger = Expression.Parameter(typeof(ILogger), "logger");
            var expModel = Expression.Variable(typeof(ReaderShapes), "model");
            var propIndex = 0;

            ExpressionHelpers.ReaderStringExpressions("A", Expression.Property(expModel, nameof(ReaderShapes.MiddleName)), lookups, expressions, expRdr, expOrdinals, expOrdinal, ref propIndex, typeof(string), expLogger, null);
            propIndex.Should().Be(1);
            lookups.Should().HaveCount(1);
            ExpressionHelpers.ReaderEnumXIntExpressions("B", Expression.Property(expModel, nameof(ReaderShapes.IntStatus)), typeof(int), lookups, expressions, expRdr, expOrdinals, expOrdinal, ref propIndex, typeof(OrderStatus), expLogger, null);
            propIndex.Should().Be(2);
            lookups.Should().HaveCount(2);
            ExpressionHelpers.ReaderSimpleValueExpressions("C", Expression.Property(expModel, nameof(ReaderShapes.Count)), lookups, expressions, expRdr, expOrdinals, expOrdinal, ref propIndex, typeof(int), expLogger, null);
            propIndex.Should().Be(3);
            lookups.Should().HaveCount(3);
            ExpressionHelpers.ReaderNullableValueTypeExpressions("D", Expression.Property(expModel, nameof(ReaderShapes.Count)), Expression.Constant(0), lookups, expressions, expRdr, expOrdinals, expOrdinal, ref propIndex, typeof(int), expLogger, null);
            propIndex.Should().Be(4);
            lookups.Should().HaveCount(4);
            lookups.Select(l => ((ConstantExpression)l.Arguments[1]).Value).Should().Equal("A", "B", "C", "D");
        }

        [Fact]
        [Trait("Spec", "blk-3690875337ec40939949857346d550f6")]
        [Trait("Spec", "blk-c010ddd73d7646ee971aab78a71a7147")]
        public void GetMapShardKeyAttributeReportsTheArityAndNullabilityOfAttributedShardKeyProperties()
        {
            var nullableChild = typeof(KeyShapes).GetProperty(nameof(KeyShapes.NullableChild))!;
            var unattributed = typeof(KeyShapes).GetProperty(nameof(KeyShapes.Unattributed))!;
            var plain = typeof(KeyShapes).GetProperty(nameof(KeyShapes.Plain))!;
            var greatGrandChild = typeof(MissingGreatGrandChildIdName).GetProperty(nameof(MissingGreatGrandChildIdName.Key))!;

            var attribute = ExpressionHelpers.GetMapShardKeyAttribute(nullableChild, nullableChild.PropertyType, out var isNullable, out var isKey, out var isChild, out var isGrandChild, out var isGreatGrandChild);
            attribute.Should().NotBeNull();
            (isNullable, isKey, isChild, isGrandChild, isGreatGrandChild).Should().Be((true, false, true, false, false));

            ExpressionHelpers.GetMapShardKeyAttribute(greatGrandChild, greatGrandChild.PropertyType, out isNullable, out isKey, out isChild, out isGrandChild, out isGreatGrandChild).Should().NotBeNull();
            (isNullable, isKey, isChild, isGrandChild, isGreatGrandChild).Should().Be((false, false, false, false, true));

            ExpressionHelpers.GetMapShardKeyAttribute(unattributed, unattributed.PropertyType, out isNullable, out isKey, out isChild, out isGrandChild, out isGreatGrandChild).Should().BeNull("a ShardKey without the attribute is not a mapped key");
            (isKey, isChild, isGrandChild, isGreatGrandChild).Should().Be((false, false, false, false));

            ExpressionHelpers.GetMapShardKeyAttribute(plain, plain.PropertyType, out isNullable, out isKey, out isChild, out isGrandChild, out isGreatGrandChild).Should().BeNull("a non-generic type is not a key");
            (isNullable, isKey, isChild, isGrandChild, isGreatGrandChild).Should().Be((false, false, false, false, false));
        }

        [Fact]
        [Trait("Spec", "blk-8e2f186ee1c548da9f9dfec5bec78111")]
        [Trait("Spec", "blk-2c9db9b9218740b1bb0e52c3e1f8f95e")]
        public void ExpressionHelpersExposesItsBuilderFamiliesAndMarksShardKeyInMapPropertiesAsNotForApplicationCode()
        {
            var names = typeof(ExpressionHelpers).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).ToHashSet();

            names.Should().Contain(new[]
            {
                nameof(ExpressionHelpers.InParameterSimpleBuilder), nameof(ExpressionHelpers.InParameterStringExpressionBuilder), nameof(ExpressionHelpers.InParameterEnumXIntExpressionBuilder), nameof(ExpressionHelpers.InParameterBinaryBuilder),
                nameof(ExpressionHelpers.OutParameterBuilder),
                nameof(ExpressionHelpers.ReadOutParameterStringExpressions), nameof(ExpressionHelpers.ReadOutParameterEnumXIntExpressions), nameof(ExpressionHelpers.ReadOutParameterSimpleValueExpressions), nameof(ExpressionHelpers.ReadOutParameterBinaryExpressions), nameof(ExpressionHelpers.ReadOutParameterArrayExpressions),
                nameof(ExpressionHelpers.ReaderStringExpressions), nameof(ExpressionHelpers.ReaderEnumXIntExpressions), nameof(ExpressionHelpers.ReaderSimpleValueExpressions), nameof(ExpressionHelpers.ReaderNullableValueTypeExpressions),
                nameof(ExpressionHelpers.GetMapShardKeyAttribute), nameof(ExpressionHelpers.ShardKeyInMapProperties)
            });
            var shardKeyInMap = typeof(ExpressionHelpers).GetMethod(nameof(ExpressionHelpers.ShardKeyInMapProperties))!;
            shardKeyInMap.IsPublic.Should().BeTrue("other ArgentSea packages call it directly");
            shardKeyInMap.GetCustomAttribute<EditorBrowsableAttribute>()!.State.Should().Be(EditorBrowsableState.Never, "it is not part of the surface offered to application code");
        }

        #endregion
    }
}
