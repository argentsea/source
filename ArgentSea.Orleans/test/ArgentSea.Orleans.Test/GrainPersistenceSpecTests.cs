using System.Data;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;
using Orleans.Storage;

namespace ArgentSea.Orleans.Test
{
    /// <summary>
    /// Spec section grain-persistence: the three options validators, the database-backed and shard-backed grain storage
    /// providers (read / write / clear through the TestKit's scripted provider), the options classes and the log events.
    /// </summary>
    public class GrainPersistenceSpecTests
    {
        private const string DbKey = "OrdersDb";
        private const string ShardSetKey = "OrderShards";

        #region Models

        public class OrderDbState
        {
            [FakeMapGuid("OrderId", true)]
            public Guid OrderId { get; set; }

            [FakeMapInteger("Sequence", true)]
            public int Sequence { get; set; }

            [FakeMapString("Description", 200)]
            public string? Description { get; set; }
        }

        public class ActivatedOrderDbState : IActivator<ActivatedOrderDbState>
        {
            [FakeMapGuid("OrderId", true)]
            public Guid OrderId { get; set; }

            [FakeMapInteger("Sequence", true)]
            public int Sequence { get; set; }

            public string? ActivatedFor { get; set; }

            public static ActivatedOrderDbState Activatate<Tmodel>(string grainType, ReadOnlyMemory<byte> key)
                => new ActivatedOrderDbState { ActivatedFor = grainType, Sequence = key.Length };
        }

        public class OrderShardState
        {
            [FakeMapShardKey("OrderShard", "OrderId")]
            [FakeMapInteger("OrderId")]
            public ShardKey<int> Key { get; set; } = ShardKey<int>.Empty;

            [FakeMapString("Description", 200)]
            public string? Description { get; set; }
        }

        public class ActivatedOrderShardState : IActivator<ActivatedOrderShardState>
        {
            [FakeMapShardKey("OrderShard", "OrderId")]
            [FakeMapInteger("OrderId")]
            public ShardKey<int> Key { get; set; } = ShardKey<int>.Empty;

            public string? ActivatedFor { get; set; }

            public static ActivatedOrderShardState Activatate<Tmodel>(string grainType, ReadOnlyMemory<byte> key)
                => new ActivatedOrderShardState { ActivatedFor = grainType };
        }

        #endregion

        #region Harness

        private static QueryProcedure ReadQuery(string grainType) => new($"rdr.Read{grainType}");
        private static QueryProcedure WriteQuery(string grainType) => new($"wtr.Write{grainType}");
        private static QueryProcedure ClearQuery(string grainType) => new($"wtr.Clear{grainType}");

        private static GrainId DbGrainId<TModel>(string grainType, TModel model)
            => GrainId.Create(GrainType.Create(grainType), SpanParser.GetDbGrainId(model, NullLogger.Instance));

        private static GrainId ShardGrainId(string grainType, ShardKey<int> key)
            => GrainId.Create(GrainType.Create(grainType), new IdSpan(key.ToUtf8().ToArray()));

        private sealed class DbHarness
        {
            public FakeProviderServices Provider { get; } = new();
            public RecordingLogger Logger { get; } = new();
            public FakeDatabases Dbs { get; }
            public ArgentSeaDbGrainPersistence<FakeDatabasesOptions> Storage { get; }

            public DbHarness(string grainType, QueryResultFormat format = QueryResultFormat.ResultSet)
            {
                Dbs = FakeDatabases.Builder(Provider).AddDatabase(DbKey).Build();
                var definitions = new List<OrleansDbQueryDefinitions> { new(grainType, ReadQuery(grainType), format, WriteQuery(grainType), ClearQuery(grainType)) };
                Storage = new ArgentSeaDbGrainPersistence<FakeDatabasesOptions>(
                    Dbs,
                    Options.Create(new OrleansDbPersistenceOptions(DbKey, definitions)),
                    Options.Create(new ClusterOptions { ServiceId = "test" }),
                    Logger);
            }

            public IReadOnlyList<RecordedCommand> Commands => Provider.Recorder.Commands;
        }

        private sealed class ShardHarness
        {
            public FakeProviderServices Provider { get; }
            public RecordingLogger<ArgentSeaShardGrainPersistence<FakeShardSetsOptions>> Logger { get; } = new();
            public FakeShardSets Shards { get; }
            public ArgentSeaShardGrainPersistence<FakeShardSetsOptions> Storage { get; }

            public ShardHarness(string grainType, QueryResultFormat format = QueryResultFormat.ResultSet, bool validateGrainKeys = true, FakeProviderServices? provider = null)
            {
                Provider = provider ?? new FakeProviderServices();
                Shards = FakeShardSets.Builder(Provider).AddShardSet(ShardSetKey, 1, 2).Build();
                var definitions = new List<OrleansShardQueryDefinitions> { new(grainType, ReadQuery(grainType), format, WriteQuery(grainType), ClearQuery(grainType)) };
                Storage = new ArgentSeaShardGrainPersistence<FakeShardSetsOptions>(
                    Shards,
                    new OrleansShardPersistenceOptions(ShardSetKey, definitions) { ValidateGrainKeys = validateGrainKeys },
                    new ClusterOptions { ServiceId = "test" },
                    Logger);
            }

            public IReadOnlyList<RecordedCommand> Commands => Provider.Recorder.Commands;
        }

        private static readonly string[] OrderColumns = ["OrderId", "Sequence", "Description"];
        private static readonly string[] ShardOrderColumns = ["OrderShard", "OrderId", "Description"];

        #endregion

        #region Database-backed read

        [Fact]
        [Trait("Spec", "blk-06f6df1901f441ac99c743bb9c22dfbe")]
        [Trait("Spec", "blk-725fe0c21f064dafb817cd89e040353c")]
        [Trait("Spec", "blk-5ee16a07df1c4791b6a1b9c92ea4f478")]
        [Trait("Spec", "blk-a8e6f9304aca4ad89e16aef95a905c67")]
        [Trait("Spec", "blk-10a87b096ba148f995c4a86656e6f08f")]
        public async Task DbReadWithResultSetRowMapsIntoTheSuppliedStateAndRecordsItExists()
        {
            const string grainType = "DbReadHit";
            var h = new DbHarness(grainType);
            var key = new OrderDbState { OrderId = Guid.NewGuid(), Sequence = 7 };
            h.Provider.Script.ForQuery(ReadQuery(grainType)).Returns(OrderColumns, [key.OrderId, 7, "found"]);
            var supplied = new OrderDbState();
            var grainState = new GrainState<OrderDbState>(supplied);

            await h.Storage.ReadStateAsync(grainType, DbGrainId(grainType, key), grainState);

            grainState.RecordExists.Should().BeTrue();
            grainState.State.Should().BeSameAs(supplied, "the result-set row is mapped into the supplied state");
            grainState.State.OrderId.Should().Be(key.OrderId);
            grainState.State.Sequence.Should().Be(7);
            grainState.State.Description.Should().Be("found");
            var cmd = h.Commands.Should().ContainSingle().Which;
            cmd.CommandText.Should().Be(ReadQuery(grainType).Sql);
            cmd.Connection.Access.Should().Be(FakeConnectionAccess.Read);
            cmd.Parameter("@OrderId").Should().Be(key.OrderId, "the read parameters are decoded from the grain id");
            cmd.Parameter("@Sequence").Should().Be(7);
        }

        [Fact]
        [Trait("Spec", "blk-08b7ab4d87974608b7e4ce219891b521")]
        [Trait("Spec", "blk-06f6df1901f441ac99c743bb9c22dfbe")]
        public async Task DbReadWithNullSuppliedStateMapsTheRowIntoANewInstance()
        {
            const string grainType = "DbReadNullState";
            var h = new DbHarness(grainType);
            var key = new OrderDbState { OrderId = Guid.NewGuid(), Sequence = 1 };
            h.Provider.Script.ForQuery(ReadQuery(grainType)).Returns(OrderColumns, [key.OrderId, 1, "new"]);
            var grainState = new GrainState<OrderDbState>();
            grainState.State.Should().BeNull();

            await h.Storage.ReadStateAsync(grainType, DbGrainId(grainType, key), grainState);

            grainState.RecordExists.Should().BeTrue();
            grainState.State.Should().NotBeNull();
            grainState.State!.Description.Should().Be("new");
        }

        [Fact]
        [Trait("Spec", "blk-44544fdf39714664a511cf1b0ec33aa8")]
        [Trait("Spec", "blk-c2a686aa169a458888cf973d1649f118")]
        [Trait("Spec", "blk-06f6df1901f441ac99c743bb9c22dfbe")]
        public async Task DbReadWithNoRowsCreatesADefaultInstanceWithKeysFromTheGrainIdAndRecordsItMissing()
        {
            const string grainType = "DbReadMiss";
            var h = new DbHarness(grainType);
            var key = new OrderDbState { OrderId = Guid.NewGuid(), Sequence = 42 };
            h.Provider.Script.ForQuery(ReadQuery(grainType)).ReturnsNoRows(OrderColumns);
            var stale = new OrderDbState { Description = "stale" };
            var grainState = new GrainState<OrderDbState>(stale) { RecordExists = true };

            await h.Storage.ReadStateAsync(grainType, DbGrainId(grainType, key), grainState);

            grainState.RecordExists.Should().BeFalse();
            grainState.State.Should().NotBeNull().And.NotBeSameAs(stale);
            grainState.State.OrderId.Should().Be(key.OrderId);
            grainState.State.Sequence.Should().Be(42);
            grainState.State.Description.Should().BeNull();
        }

        [Fact]
        [Trait("Spec", "blk-44544fdf39714664a511cf1b0ec33aa8")]
        [Trait("Spec", "blk-c2a686aa169a458888cf973d1649f118")]
        public async Task DbReadWithNoRowsUsesTheIActivatorFactoryWhenTheStateTypeImplementsIt()
        {
            const string grainType = "DbReadActivator";
            var h = new DbHarness(grainType);
            var key = new ActivatedOrderDbState { OrderId = Guid.NewGuid(), Sequence = 3 };
            h.Provider.Script.ForQuery(ReadQuery(grainType)).ReturnsNoRows("OrderId", "Sequence");
            var grainState = new GrainState<ActivatedOrderDbState>(new ActivatedOrderDbState());

            await h.Storage.ReadStateAsync(grainType, DbGrainId(grainType, key), grainState);

            grainState.RecordExists.Should().BeFalse();
            grainState.State!.ActivatedFor.Should().Be(grainType, "the factory receives the state name the provider was called with");
            grainState.State.Sequence.Should().Be(DbGrainId(grainType, key).Key.Value.Length, "the factory receives the grain's binary key");
        }

        [Fact]
        [Trait("Spec", "blk-5ee16a07df1c4791b6a1b9c92ea4f478")]
        [Trait("Spec", "blk-10a87b096ba148f995c4a86656e6f08f")]
        [Trait("Spec", "blk-06f6df1901f441ac99c743bb9c22dfbe")]
        public async Task DbReadWithOutputArgsFormatMapsOutputParametersIntoTheState()
        {
            const string grainType = "DbReadOutput";
            var h = new DbHarness(grainType, QueryResultFormat.OutputArgs);
            var key = new OrderDbState { OrderId = Guid.NewGuid(), Sequence = 5 };
            h.Provider.Script.ForQuery(ReadQuery(grainType)).WithOutput("@Description", "from output");
            var grainState = new GrainState<OrderDbState>(new OrderDbState());

            await h.Storage.ReadStateAsync(grainType, DbGrainId(grainType, key), grainState);

            grainState.RecordExists.Should().BeTrue();
            grainState.State.OrderId.Should().Be(key.OrderId);
            grainState.State.Description.Should().Be("from output", "output-argument mapping reads the procedure's output parameters");
            var cmd = h.Commands.Should().ContainSingle().Which;
            cmd.Parameters.Should().Contain(p => p.ParameterName == "@Description" && p.Direction == ParameterDirection.Output,
                "the provider must declare the non-key output parameters before executing an OutputArgs read");
        }

        [Fact]
        [Trait("Spec", "blk-9f04dc69da554a69a81be0fc415dfdd7")]
        [Trait("Spec", "blk-ea747f3d72834d758567f1684fcd03f6")]
        public async Task DbReadForAnUnregisteredGrainTypeThrowsBeforeTouchingTheState()
        {
            var h = new DbHarness("DbReadRegistered");
            var grainState = new GrainState<OrderDbState>();

            var act = () => h.Storage.ReadStateAsync("orders", DbGrainId("orders", new OrderDbState()), grainState);

            (await act.Should().ThrowAsync<OrleansQueryNotProvidedException>()).Which.Message.Should().Contain("“orders”").And.Contain("Read");
            grainState.State.Should().BeNull();
            h.Commands.Should().BeEmpty();
        }

        #endregion

        #region Database-backed write and clear

        [Fact]
        [Trait("Spec", "blk-a117ee43445c4c06ae09183298b2d0c8")]
        [Trait("Spec", "blk-d163967ff05849dabf2955edbbe51420")]
        [Trait("Spec", "blk-cfd91c5afeba4626ba95216656d8baf8")]
        [Trait("Spec", "blk-725fe0c21f064dafb817cd89e040353c")]
        public async Task DbWriteExecutesTheWriteQueryWithParametersFromTheStateAndRecordsItExists()
        {
            const string grainType = "DbWrite";
            var h = new DbHarness(grainType);
            var state = new OrderDbState { OrderId = Guid.NewGuid(), Sequence = 9, Description = "written" };
            var grainState = new GrainState<OrderDbState>(state) { RecordExists = false };

            await h.Storage.WriteStateAsync(grainType, DbGrainId(grainType, state), grainState);

            grainState.RecordExists.Should().BeTrue();
            var cmd = h.Commands.Should().ContainSingle().Which;
            cmd.CommandText.Should().Be(WriteQuery(grainType).Sql);
            cmd.Connection.Access.Should().Be(FakeConnectionAccess.Write);
            cmd.Parameter("@OrderId").Should().Be(state.OrderId);
            cmd.Parameter("@Sequence").Should().Be(9);
            cmd.Parameter("@Description").Should().Be("written");
        }

        [Fact]
        [Trait("Spec", "blk-32b362ab7a0647eca1e54b12d08f7564")]
        [Trait("Spec", "blk-83a60b038c0d418bb56e8bec9eb59607")]
        public async Task DbWriteWithNullStateThrowsNoNullAllowedBeforeTheQueryRegistrationCheck()
        {
            var h = new DbHarness("DbWriteNull");
            var grainState = new GrainState<OrderDbState>();
            var id = DbGrainId("orders", new OrderDbState());

            await FluentActions.Awaiting(() => h.Storage.WriteStateAsync("orders", id, grainState)).Should().ThrowAsync<NoNullAllowedException>("the null-state check runs before the registration check");
            await FluentActions.Awaiting(() => h.Storage.WriteStateAsync("DbWriteNull", id, grainState)).Should().ThrowAsync<NoNullAllowedException>();
            h.Commands.Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-ab32f9e8402c41ae941d2377b3b08969")]
        [Trait("Spec", "blk-ea747f3d72834d758567f1684fcd03f6")]
        public async Task DbWriteAndClearForAnUnregisteredGrainTypeThrowQueryNotProvidedNamingTheOperation()
        {
            var h = new DbHarness("DbRegistered");
            var grainState = new GrainState<OrderDbState>(new OrderDbState());
            var id = DbGrainId("orders", grainState.State);

            var write = await FluentActions.Awaiting(() => h.Storage.WriteStateAsync("orders", id, grainState)).Should().ThrowAsync<OrleansQueryNotProvidedException>();
            var clear = await FluentActions.Awaiting(() => h.Storage.ClearStateAsync("orders", id, grainState)).Should().ThrowAsync<OrleansQueryNotProvidedException>();

            write.Which.Message.Should().Contain("“orders”").And.Contain("Write");
            clear.Which.Message.Should().Contain("“orders”").And.Contain("Clear");
            h.Commands.Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-db026e4c63764dbba2c1b60a617bb62f")]
        [Trait("Spec", "blk-161e2926173c4515aebe1aafd05078c1")]
        [Trait("Spec", "blk-cfd91c5afeba4626ba95216656d8baf8")]
        public async Task DbClearExecutesTheClearQueryWithParametersFromTheGrainIdAndRecordsItMissing()
        {
            const string grainType = "DbClear";
            var h = new DbHarness(grainType);
            var keySource = new OrderDbState { OrderId = Guid.NewGuid(), Sequence = 11 };
            var state = new OrderDbState { OrderId = Guid.NewGuid(), Sequence = 99, Description = "ignored" };
            var grainState = new GrainState<OrderDbState>(state) { RecordExists = true };

            await h.Storage.ClearStateAsync(grainType, DbGrainId(grainType, keySource), grainState);

            grainState.RecordExists.Should().BeFalse();
            var cmd = h.Commands.Should().ContainSingle().Which;
            cmd.CommandText.Should().Be(ClearQuery(grainType).Sql);
            cmd.Connection.Access.Should().Be(FakeConnectionAccess.Write);
            cmd.Parameter("@OrderId").Should().Be(keySource.OrderId, "clear parameters derive from the grain identifier, not the state");
            cmd.Parameter("@Sequence").Should().Be(11);
            cmd.ParameterValues.Should().NotContainKey("@Description");
        }

        [Fact]
        [Trait("Spec", "blk-28a1ba07ee79467ea47de51da13c6f2f")]
        [Trait("Spec", "blk-32b362ab7a0647eca1e54b12d08f7564")]
        public async Task DbClearWithNullStateThrowsNoNullAllowedAndRunsNoQuery()
        {
            const string grainType = "DbClearNull";
            var h = new DbHarness(grainType);
            var grainState = new GrainState<OrderDbState>();

            await FluentActions.Awaiting(() => h.Storage.ClearStateAsync(grainType, DbGrainId(grainType, new OrderDbState()), grainState)).Should().ThrowAsync<NoNullAllowedException>();
            h.Commands.Should().BeEmpty();
        }

        #endregion

        #region Shard-backed read, write and clear

        [Fact]
        [Trait("Spec", "blk-4feff7c4f860482aad1b10303a45f512")]
        [Trait("Spec", "blk-0cecdb69369448b39fda18cab835ecd8")]
        [Trait("Spec", "blk-8af883d10b5445ef8b1936a01bcbf999")]
        [Trait("Spec", "blk-f8d2890b7423448daa30a5984d350f47")]
        public async Task ShardReadRoutesToTheKeyShardMapsTheRowAndRecordsItExists()
        {
            const string grainType = "ShardReadHit";
            var h = new ShardHarness(grainType);
            var key = new ShardKey<int>(2, 1234);
            h.Provider.Script.ForQuery(ReadQuery(grainType), 2).Returns(ShardOrderColumns, [(short)2, 1234, "found"]);
            var grainState = new GrainState<OrderShardState>();

            await h.Storage.ReadStateAsync(grainType, ShardGrainId(grainType, key), grainState);

            grainState.RecordExists.Should().BeTrue();
            grainState.State.Should().NotBeNull("a null supplied state is replaced by a new instance");
            grainState.State!.Key.Should().Be(key);
            grainState.State.Description.Should().Be("found");
            var cmd = h.Commands.Should().ContainSingle().Which;
            cmd.ShardId.Should().Be(2, "the shard id is derived from the grain identity");
            cmd.Connection.Access.Should().Be(FakeConnectionAccess.Read);
            cmd.Parameter("@OrderShard").Should().Be((short)2);
            cmd.Parameter("@OrderId").Should().Be(1234);
        }

        [Fact]
        [Trait("Spec", "blk-f620591363cf42b185bc01c0c89314b0")]
        [Trait("Spec", "blk-4feff7c4f860482aad1b10303a45f512")]
        [Trait("Spec", "blk-c2a686aa169a458888cf973d1649f118")]
        public async Task ShardReadWithNoRowsCreatesADefaultInstanceWithTheShardKeyFromTheGrainId()
        {
            const string grainType = "ShardReadMiss";
            var h = new ShardHarness(grainType);
            var key = new ShardKey<int>(1, 77);
            h.Provider.Script.ForQuery(ReadQuery(grainType)).ReturnsNoRows(ShardOrderColumns);
            var grainState = new GrainState<OrderShardState>();

            await h.Storage.ReadStateAsync(grainType, ShardGrainId(grainType, key), grainState);

            grainState.RecordExists.Should().BeFalse();
            grainState.State.Should().NotBeNull();
            grainState.State!.Key.Should().Be(key);
            grainState.State.Description.Should().BeNull();
            h.Commands.Should().ContainSingle().Which.ShardId.Should().Be(1);
        }

        [Fact]
        [Trait("Spec", "blk-f620591363cf42b185bc01c0c89314b0")]
        [Trait("Spec", "blk-c2a686aa169a458888cf973d1649f118")]
        public async Task ShardReadWithNoRowsUsesTheIActivatorFactory()
        {
            const string grainType = "ShardReadActivator";
            var h = new ShardHarness(grainType);
            h.Provider.Script.ForQuery(ReadQuery(grainType)).ReturnsNoRows("OrderShard", "OrderId");
            var grainState = new GrainState<ActivatedOrderShardState>(new ActivatedOrderShardState());

            await h.Storage.ReadStateAsync(grainType, ShardGrainId(grainType, new ShardKey<int>(2, 5)), grainState);

            grainState.RecordExists.Should().BeFalse();
            grainState.State!.ActivatedFor.Should().Be(grainType);
        }

        [Fact]
        [Trait("Spec", "blk-a9ecda1bfe1e42e1acccf2008d9c261b")]
        [Trait("Spec", "blk-5ee16a07df1c4791b6a1b9c92ea4f478")]
        [Trait("Spec", "blk-4feff7c4f860482aad1b10303a45f512")]
        public async Task ShardReadWithOutputArgsFormatMapsOutputParametersIntoTheState()
        {
            const string grainType = "ShardReadOutput";
            var h = new ShardHarness(grainType, QueryResultFormat.OutputArgs);
            var key = new ShardKey<int>(2, 8);
            h.Provider.Script.ForQuery(ReadQuery(grainType), 2).WithOutput("@Description", "from output");
            var grainState = new GrainState<OrderShardState>(new OrderShardState());

            await h.Storage.ReadStateAsync(grainType, ShardGrainId(grainType, key), grainState);

            grainState.RecordExists.Should().BeTrue();
            grainState.State.Key.Should().Be(key);
            grainState.State.Description.Should().Be("from output");
            h.Commands.Should().ContainSingle().Which.ShardId.Should().Be(2);
        }

        [Fact]
        [Trait("Spec", "blk-b05fcc9d29534254923682c2a9708a76")]
        [Trait("Spec", "blk-1fc0a26e635542f3b33c22863ee5021e")]
        [Trait("Spec", "blk-8af883d10b5445ef8b1936a01bcbf999")]
        public async Task ShardWriteExecutesTheWriteQueryOnTheStateKeyShardAndRecordsItExists()
        {
            const string grainType = "ShardWrite";
            var h = new ShardHarness(grainType);
            var key = new ShardKey<int>(2, 500);
            var grainState = new GrainState<OrderShardState>(new OrderShardState { Key = key, Description = "written" });

            await h.Storage.WriteStateAsync(grainType, ShardGrainId(grainType, key), grainState);

            grainState.RecordExists.Should().BeTrue();
            var cmd = h.Commands.Should().ContainSingle().Which;
            cmd.CommandText.Should().Be(WriteQuery(grainType).Sql);
            cmd.ShardId.Should().Be(2);
            cmd.Connection.Access.Should().Be(FakeConnectionAccess.Write);
            cmd.Parameter("@OrderShard").Should().Be((short)2);
            cmd.Parameter("@OrderId").Should().Be(500);
            cmd.Parameter("@Description").Should().Be("written");
        }

        [Fact]
        [Trait("Spec", "blk-b5ab7a1ea84d42cbb5ad252bb4ac5628")]
        [Trait("Spec", "blk-9b300f0d2d1842fa85be15b06c74f057")]
        [Trait("Spec", "blk-8af883d10b5445ef8b1936a01bcbf999")]
        public async Task ShardClearExecutesTheClearQueryOnTheKeyShardAndRecordsItMissing()
        {
            const string grainType = "ShardClear";
            var h = new ShardHarness(grainType);
            var key = new ShardKey<int>(2, 600);
            var grainState = new GrainState<OrderShardState>(new OrderShardState { Key = key, Description = "ignored" }) { RecordExists = true };

            await h.Storage.ClearStateAsync(grainType, ShardGrainId(grainType, key), grainState);

            grainState.RecordExists.Should().BeFalse();
            var cmd = h.Commands.Should().ContainSingle().Which;
            cmd.CommandText.Should().Be(ClearQuery(grainType).Sql);
            cmd.ShardId.Should().Be(2);
            cmd.Connection.Access.Should().Be(FakeConnectionAccess.Write);
            cmd.Parameter("@OrderShard").Should().Be((short)2);
            cmd.Parameter("@OrderId").Should().Be(600);
            cmd.ParameterValues.Should().NotContainKey("@Description", "clear parameters are the key parameters only");
        }

        [Fact]
        [Trait("Spec", "blk-32b362ab7a0647eca1e54b12d08f7564")]
        public async Task ShardWriteAndClearWithNullStateThrowNoNullAllowedAndRunNoQuery()
        {
            const string grainType = "ShardNullState";
            var h = new ShardHarness(grainType);
            var grainState = new GrainState<OrderShardState>();
            var id = ShardGrainId(grainType, new ShardKey<int>(1, 1));

            await FluentActions.Awaiting(() => h.Storage.WriteStateAsync(grainType, id, grainState)).Should().ThrowAsync<NoNullAllowedException>();
            await FluentActions.Awaiting(() => h.Storage.ClearStateAsync(grainType, id, grainState)).Should().ThrowAsync<NoNullAllowedException>();
            await FluentActions.Awaiting(() => h.Storage.WriteStateAsync("orders", id, grainState)).Should().ThrowAsync<NoNullAllowedException>("the null check precedes the registration check");
            h.Commands.Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-ea747f3d72834d758567f1684fcd03f6")]
        public async Task ShardReadWriteAndClearForAnUnregisteredGrainTypeThrowQueryNotProvided()
        {
            var h = new ShardHarness("ShardRegistered");
            var grainState = new GrainState<OrderShardState>(new OrderShardState { Key = new ShardKey<int>(1, 1) });
            var id = ShardGrainId("orders", grainState.State.Key);

            var read = await FluentActions.Awaiting(() => h.Storage.ReadStateAsync("orders", id, grainState)).Should().ThrowAsync<OrleansQueryNotProvidedException>();
            var write = await FluentActions.Awaiting(() => h.Storage.WriteStateAsync("orders", id, grainState)).Should().ThrowAsync<OrleansQueryNotProvidedException>();
            var clear = await FluentActions.Awaiting(() => h.Storage.ClearStateAsync("orders", id, grainState)).Should().ThrowAsync<OrleansQueryNotProvidedException>();

            read.Which.Message.Should().Contain("“orders”").And.Contain("Read");
            write.Which.Message.Should().Contain("“orders”").And.Contain("Write");
            clear.Which.Message.Should().Contain("“orders”").And.Contain("Clear");
            h.Commands.Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-3df38bc9d6944e08821268fc06f74093")]
        [Trait("Spec", "blk-2e4e93bd74c445d8b24dcf451658f08b")]
        [Trait("Spec", "blk-40522fbb1e094333bc11559ce22536b9")]
        [Trait("Spec", "blk-423b79d3955d40e883835d9cc1f8b854")]
        [Trait("Spec", "blk-b05fcc9d29534254923682c2a9708a76")]
        public async Task ShardWriteDelegatesAreCachedPerValidateGrainKeysSetting()
        {
            const string grainType = "ShardWriteValidateCache";
            var provider = new FakeProviderServices();
            var validating = new ShardHarness(grainType, validateGrainKeys: true, provider: provider);
            var lenient = new ShardHarness(grainType, validateGrainKeys: false, provider: provider);
            var modelKey = new ShardKey<int>(2, 100);
            var otherKey = new ShardKey<int>(1, 200);

            // A compiles and caches its validating delegate under (grain type, true) and accepts a matching key.
            await validating.Storage.WriteStateAsync(grainType, ShardGrainId(grainType, modelKey), new GrainState<OrderShardState>(new OrderShardState { Key = modelKey }));

            // B must not reuse it: with validation off a mismatched grain id is accepted and the write routes by the model's key.
            await lenient.Storage.WriteStateAsync(grainType, ShardGrainId(grainType, otherKey), new GrainState<OrderShardState>(new OrderShardState { Key = modelKey }));
            provider.Recorder.Commands.Should().HaveCount(2).And.OnlyContain(c => c.ShardId == 2);

            // And A keeps validating.
            await FluentActions.Awaiting(() => validating.Storage.WriteStateAsync(grainType, ShardGrainId(grainType, otherKey), new GrainState<OrderShardState>(new OrderShardState { Key = modelKey })))
                .Should().ThrowAsync<OrleansIdKeyMismatchException>();
            provider.Recorder.Commands.Should().HaveCount(2);
        }

        [Fact]
        [Trait("Spec", "blk-fa6d6016c80b49228ad69563cd1d2ced")]
        [Trait("Spec", "blk-b5ab7a1ea84d42cbb5ad252bb4ac5628")]
        [Trait("Spec", "blk-2e4e93bd74c445d8b24dcf451658f08b")]
        public async Task ShardClearDelegateCachedWithoutValidationIsNotReusedByAValidatingProvider()
        {
            const string grainType = "ShardClearValidateCache";
            var provider = new FakeProviderServices();
            var lenient = new ShardHarness(grainType, validateGrainKeys: false, provider: provider);
            var validating = new ShardHarness(grainType, validateGrainKeys: true, provider: provider);
            var modelKey = new ShardKey<int>(2, 100);
            var otherKey = new ShardKey<int>(1, 200);

            await lenient.Storage.ClearStateAsync(grainType, ShardGrainId(grainType, otherKey), new GrainState<OrderShardState>(new OrderShardState { Key = modelKey }));
            provider.Recorder.Commands.Should().ContainSingle().Which.ShardId.Should().Be(2);

            await FluentActions.Awaiting(() => validating.Storage.ClearStateAsync(grainType, ShardGrainId(grainType, otherKey), new GrainState<OrderShardState>(new OrderShardState { Key = modelKey })))
                .Should().ThrowAsync<OrleansIdKeyMismatchException>("the later instance clears with key validation on");
            await validating.Storage.ClearStateAsync(grainType, ShardGrainId(grainType, modelKey), new GrainState<OrderShardState>(new OrderShardState { Key = modelKey }));
            provider.Recorder.Commands.Should().HaveCount(2);
        }

        #endregion

        #region Options and exceptions

        [Fact]
        [Trait("Spec", "blk-bf4daea8f68d46a1a987aad65e7c6d2c")]
        [Trait("Spec", "blk-2e4e93bd74c445d8b24dcf451658f08b")]
        [Trait("Spec", "blk-40522fbb1e094333bc11559ce22536b9")]
        [Trait("Spec", "blk-423b79d3955d40e883835d9cc1f8b854")]
        public void ShardPersistenceOptionsDefaultToTheDefaultProviderNameValidatingKeysAndNoQueries()
        {
            var options = new OrleansShardPersistenceOptions();

            options.ShardSetKey.Should().Be(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME);
            options.ValidateGrainKeys.Should().BeTrue();
            options.Queries.Should().NotBeNull().And.BeEmpty();
            options.InitStage.Should().Be(ServiceLifecycleStage.ApplicationServices);
            ((IStorageProviderSerializerOptions)options).GrainStorageSerializer.Should().BeOfType<FakeGrainStorageSerializer>();
        }

        [Fact]
        [Trait("Spec", "blk-d992dcdc96ea4a01a8a70621985905b8")]
        [Trait("Spec", "blk-ec92d9f94a684563994dd78261169aa2")]
        [Trait("Spec", "blk-40522fbb1e094333bc11559ce22536b9")]
        public void ShardPersistenceOptionsTwoArgumentConstructorKeysTheDefinitionsByGrainType()
        {
            var order = new OrleansShardQueryDefinitions("Order", ReadQuery("Order"), QueryResultFormat.ResultSet, WriteQuery("Order"), ClearQuery("Order"));
            var invoice = new OrleansShardQueryDefinitions("Invoice", ReadQuery("Invoice"), QueryResultFormat.OutputArgs, WriteQuery("Invoice"), ClearQuery("Invoice"));

            var options = new OrleansShardPersistenceOptions("orders", [order, invoice]);

            options.ShardSetKey.Should().Be("orders");
            options.Queries.Should().HaveCount(2);
            options.Queries["Order"].ReadQuery.Should().BeSameAs(order.ReadQuery);
            options.Queries["Invoice"].ResultFormat.Should().Be(QueryResultFormat.OutputArgs);
        }

        [Fact]
        [Trait("Spec", "blk-1b90d7630d384cc0a907f1be231869a1")]
        [Trait("Spec", "blk-8f0a11f25afd4fbe91a23f21f80c8224")]
        public void DbPersistenceOptionsDefaultToCommonAndTheConstructorKeysDefinitionsByGrainType()
        {
            var defaults = new OrleansDbPersistenceOptions();
            defaults.DatabaseKey.Should().Be("Common");
            defaults.Queries.Should().NotBeNull().And.BeEmpty();
            defaults.InitStage.Should().Be(ServiceLifecycleStage.ApplicationServices);
            ((IStorageProviderSerializerOptions)defaults).GrainStorageSerializer.Should().BeOfType<FakeGrainStorageSerializer>();

            var order = new OrleansDbQueryDefinitions("Order", ReadQuery("Order"), QueryResultFormat.ResultSet, WriteQuery("Order"), ClearQuery("Order"));
            var built = new OrleansDbPersistenceOptions("archive", [order]);
            built.DatabaseKey.Should().Be("archive");
            built.Queries.Should().ContainKey("Order").WhoseValue.WriteQuery.Should().BeSameAs(order.WriteQuery);
        }

        [Fact]
        [Trait("Spec", "blk-01161845aae94b10bb90bad566c6ac99")]
        public void GrainPersistenceExceptionPreservesMessageAndInnerException()
        {
            var inner = new InvalidOperationException("inner");

            new ArgentSeaGrainPersistenceException().Should().BeAssignableTo<Exception>();
            new ArgentSeaGrainPersistenceException("diag").Message.Should().Be("diag");
            var withInner = new ArgentSeaGrainPersistenceException("diag", inner);
            withInner.Message.Should().Be("diag");
            withInner.InnerException.Should().BeSameAs(inner);
        }

        #endregion

        #region Database options validator

        private static DatabasesBase<FakeDatabasesOptions>.Database BuildDatabase(string key, string readConnectionString = "Server=read", string writeConnectionString = "Server=write")
        {
            var dbs = FakeDatabases.Builder(new FakeProviderServices())
                .AddDatabase(key)
                .ConfigureConnections((c, info) => c.ConnectionString = info.Access == FakeConnectionAccess.Read ? readConnectionString : writeConnectionString)
                .Build();
            return dbs[key];
        }

        private static OrleansDbPersistenceOptions ValidDbOptions(string key = "orders")
            => new(key, [new OrleansDbQueryDefinitions("orders", ReadQuery("orders"), QueryResultFormat.ResultSet, WriteQuery("orders"), ClearQuery("orders"))]);

        [Fact]
        [Trait("Spec", "blk-aa7cc9ee131649ceb45931ad3db49fd1")]
        [Trait("Spec", "blk-0a22b11d573f45fab9691a26832bd2f1")]
        [Trait("Spec", "blk-6ad2f36106954d20ad47c3f6608b0def")]
        public void DbValidatorRejectsNullOptions()
        {
            var validator = new ArgentSeaDbPersistenceOptionsValidator<FakeDatabasesOptions>("orders", null!, BuildDatabase("orders"));
            validator.Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*OrleansDbPersistenceOptions cannot be null*");
        }

        [Fact]
        [Trait("Spec", "blk-0a22b11d573f45fab9691a26832bd2f1")]
        [Trait("Spec", "blk-6ad2f36106954d20ad47c3f6608b0def")]
        public void DbValidatorRejectsAnEmptyDatabaseKey()
        {
            var options = ValidDbOptions();
            options.DatabaseKey = "";
            var validator = new ArgentSeaDbPersistenceOptionsValidator<FakeDatabasesOptions>("orders", options, BuildDatabase("orders"));
            validator.Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*DatabaseKey cannot be null*");
        }

        [Fact]
        [Trait("Spec", "blk-7f58322259d7486baa0c09cf0703dae4")]
        [Trait("Spec", "blk-0a22b11d573f45fab9691a26832bd2f1")]
        public void DbValidatorRejectsADatabaseKeyThatDiffersFromTheRegistrationName()
        {
            var validator = new ArgentSeaDbPersistenceOptionsValidator<FakeDatabasesOptions>("orders", ValidDbOptions("archive"), BuildDatabase("orders"));
            validator.Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*DatabaseKey must be “orders”*");
        }

        [Fact]
        [Trait("Spec", "blk-48bd02d83dab48e791b33b4186b6888d")]
        [Trait("Spec", "blk-0a22b11d573f45fab9691a26832bd2f1")]
        public void DbValidatorRejectsNullOrEmptyQueries()
        {
            var empty = new OrleansDbPersistenceOptions { DatabaseKey = "orders" };
            new ArgentSeaDbPersistenceOptionsValidator<FakeDatabasesOptions>("orders", empty, BuildDatabase("orders"))
                .Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*Queries cannot be empty*");

            var nullQueries = new OrleansDbPersistenceOptions { DatabaseKey = "orders", Queries = null! };
            new ArgentSeaDbPersistenceOptionsValidator<FakeDatabasesOptions>("orders", nullQueries, BuildDatabase("orders"))
                .Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*Queries cannot be null*");
        }

        [Fact]
        [Trait("Spec", "blk-cd715585bc134855a0982965e21c4aae")]
        [Trait("Spec", "blk-0a22b11d573f45fab9691a26832bd2f1")]
        public void DbValidatorRejectsAMissingDatabaseEntry()
        {
            var validator = new ArgentSeaDbPersistenceOptionsValidator<FakeDatabasesOptions>("orders", ValidDbOptions(), null!);
            validator.Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*must exist in the database service dictionary with a key of “orders”*");
        }

        [Fact]
        [Trait("Spec", "blk-ee247b08d670495c8935a52d8bc77de8")]
        [Trait("Spec", "blk-0a22b11d573f45fab9691a26832bd2f1")]
        public void DbValidatorRejectsAnEmptyReadOrWriteConnectionString()
        {
            new ArgentSeaDbPersistenceOptionsValidator<FakeDatabasesOptions>("orders", ValidDbOptions(), BuildDatabase("orders", writeConnectionString: ""))
                .Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*no Write connection information*");
            new ArgentSeaDbPersistenceOptionsValidator<FakeDatabasesOptions>("orders", ValidDbOptions(), BuildDatabase("orders", readConnectionString: ""))
                .Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*no Read connection information*");
        }

        [Fact]
        [Trait("Spec", "blk-6ad2f36106954d20ad47c3f6608b0def")]
        [Trait("Spec", "blk-0a22b11d573f45fab9691a26832bd2f1")]
        public void DbValidatorAcceptsACompleteConfiguration()
        {
            var validator = new ArgentSeaDbPersistenceOptionsValidator<FakeDatabasesOptions>("orders", ValidDbOptions(), BuildDatabase("orders"));
            validator.Invoking(v => v.ValidateConfiguration()).Should().NotThrow();
        }

        #endregion

        #region Shard options validator

        private static ShardSetsBase<FakeShardSetsOptions>.ShardSet BuildShardSet(string name, short[] shardIds, string readConnectionString = "Server=read", string writeConnectionString = "Server=write")
        {
            var builder = FakeShardSets.Builder(new FakeProviderServices())
                .ConfigureConnections((c, info) => c.ConnectionString = info.Access == FakeConnectionAccess.Read ? readConnectionString : writeConnectionString);
            builder.AddShardSet(name, shardIds.Length == 0 ? (short)0 : shardIds[0], shardIds);
            return builder.Build()[name];
        }

        private static OrleansShardPersistenceOptions ValidShardOptions(string key = "Orders")
            => new(key, [new OrleansShardQueryDefinitions("orders", ReadQuery("orders"), QueryResultFormat.ResultSet, WriteQuery("orders"), ClearQuery("orders"))]);

        [Fact]
        [Trait("Spec", "blk-06037c0b10c24decbb624268e2a1a750")]
        [Trait("Spec", "blk-728fba5bc7654f129c75436bb7c21949")]
        [Trait("Spec", "blk-8718d41557c2497892dc9fdec2ef2f74")]
        public void ShardValidatorRejectsNullOptions()
        {
            new ArgentSeaShardPersistenceOptionsValidator<FakeShardSetsOptions>(null!, BuildShardSet("Orders", [1]), "Orders")
                .Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*OrleansShardPersistenceOptions must be defined*");
        }

        [Fact]
        [Trait("Spec", "blk-1db99450208f45eba4f72d5922358240")]
        [Trait("Spec", "blk-728fba5bc7654f129c75436bb7c21949")]
        public void ShardValidatorRejectsAnEmptyShardSetKey()
        {
            new ArgentSeaShardPersistenceOptionsValidator<FakeShardSetsOptions>(ValidShardOptions(""), BuildShardSet("Orders", [1]), "Orders")
                .Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*ShardSetKey must be defined*");
        }

        [Fact]
        [Trait("Spec", "blk-2bbd6a78aced4f4f97b815cc34503632")]
        [Trait("Spec", "blk-728fba5bc7654f129c75436bb7c21949")]
        public void ShardValidatorRejectsAShardSetKeyThatDiffersFromTheProviderName()
        {
            new ArgentSeaShardPersistenceOptionsValidator<FakeShardSetsOptions>(ValidShardOptions("Billing"), BuildShardSet("Orders", [1]), "Orders")
                .Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*ShardSetKey must be “Orders”*");
        }

        [Fact]
        [Trait("Spec", "blk-e863f7675718481fb644a9973eea8ea7")]
        [Trait("Spec", "blk-e105e5ba41574301a25d2927f8f3ba98")]
        [Trait("Spec", "blk-728fba5bc7654f129c75436bb7c21949")]
        public void ShardValidatorRejectsNullOrEmptyQueries()
        {
            new ArgentSeaShardPersistenceOptionsValidator<FakeShardSetsOptions>(new OrleansShardPersistenceOptions { ShardSetKey = "Orders", Queries = null! }, BuildShardSet("Orders", [1]), "Orders")
                .Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*Queries cannot be null*");
            new ArgentSeaShardPersistenceOptionsValidator<FakeShardSetsOptions>(new OrleansShardPersistenceOptions { ShardSetKey = "Orders" }, BuildShardSet("Orders", [1]), "Orders")
                .Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*Queries cannot be empty*");
        }

        [Fact]
        [Trait("Spec", "blk-54081d7cd28c4307a20fc5203a1d8e0f")]
        [Trait("Spec", "blk-728fba5bc7654f129c75436bb7c21949")]
        public void ShardValidatorRejectsAMissingShardSet()
        {
            new ArgentSeaShardPersistenceOptionsValidator<FakeShardSetsOptions>(ValidShardOptions(), null!, "Orders")
                .Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*must exist in the shardset service dictionary with a key of “Orders”*");
        }

        [Fact]
        [Trait("Spec", "blk-b129fbea8b3c405e9efe262a739d2569")]
        [Trait("Spec", "blk-728fba5bc7654f129c75436bb7c21949")]
        public void ShardValidatorRejectsAShardSetWithNoShards()
        {
            new ArgentSeaShardPersistenceOptionsValidator<FakeShardSetsOptions>(ValidShardOptions(), BuildShardSet("Orders", []), "Orders")
                .Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*cannot have no defined shards*");
        }

        [Fact]
        [Trait("Spec", "blk-4497438b000c42eca2f67d00a0241b14")]
        [Trait("Spec", "blk-70a58e25c537430ebeec81f264dd624c")]
        [Trait("Spec", "blk-728fba5bc7654f129c75436bb7c21949")]
        public void ShardValidatorRejectsAShardWithAnEmptyReadOrWriteConnectionString()
        {
            new ArgentSeaShardPersistenceOptionsValidator<FakeShardSetsOptions>(ValidShardOptions(), BuildShardSet("Orders", [1, 2], readConnectionString: ""), "Orders")
                .Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*no Read connection information*");
            new ArgentSeaShardPersistenceOptionsValidator<FakeShardSetsOptions>(ValidShardOptions(), BuildShardSet("Orders", [1, 2], writeConnectionString: ""), "Orders")
                .Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>().WithMessage("*no Write connection information*");
        }

        [Fact]
        [Trait("Spec", "blk-3555b4b0a3554ed5adf46f6d27002c5c")]
        [Trait("Spec", "blk-8718d41557c2497892dc9fdec2ef2f74")]
        [Trait("Spec", "blk-728fba5bc7654f129c75436bb7c21949")]
        public void ShardValidatorAcceptsACompleteConfigurationWhateverTheShardIds()
        {
            // Shard ids need not start at zero or be contiguous; the validator must check every configured shard, not shard[0..Count).
            new ArgentSeaShardPersistenceOptionsValidator<FakeShardSetsOptions>(ValidShardOptions(), BuildShardSet("Orders", [1, 2]), "Orders")
                .Invoking(v => v.ValidateConfiguration()).Should().NotThrow();
            new ArgentSeaShardPersistenceOptionsValidator<FakeShardSetsOptions>(ValidShardOptions(), BuildShardSet("Orders", [10, 20]), "Orders")
                .Invoking(v => v.ValidateConfiguration()).Should().NotThrow();
        }

        #endregion

        #region Log events

        [Fact]
        [Trait("Spec", "blk-48b5d1a5f3ea4a94a1531ea0a58547f0")]
        [Trait("Spec", "blk-d0cc6db246764e9eb27c2cf3f1941f24")]
        public async Task DbDelegateCacheLookupsLogAMissAtDebugThenAHitAtTrace()
        {
            const string grainType = "DbCacheEvents";
            var h = new DbHarness(grainType);
            h.Provider.Script.ForQuery(ReadQuery(grainType)).ReturnsNoRows(OrderColumns);
            var key = new OrderDbState { OrderId = Guid.NewGuid(), Sequence = 1 };

            await h.Storage.ReadStateAsync(grainType, DbGrainId(grainType, key), new GrainState<OrderDbState>(new OrderDbState()));
            var first = h.Logger.Entries.Where(e => e.Id == (int)LoggingExtensions.EventIdentifier.MapperCacheStatus).ToList();
            h.Logger.Clear();
            await h.Storage.ReadStateAsync(grainType, DbGrainId(grainType, key), new GrainState<OrderDbState>(new OrderDbState()));
            var second = h.Logger.Entries.Where(e => e.Id == (int)LoggingExtensions.EventIdentifier.MapperCacheStatus).ToList();

            first.Should().Contain(e => e.Name == nameof(LoggingExtensions.OrleansDbCacheMiss) && e.Level == LogLevel.Debug && e.Message!.Contains(grainType));
            first.Should().NotContain(e => e.Name == nameof(LoggingExtensions.OrleansDbCacheHit));
            second.Should().Contain(e => e.Name == nameof(LoggingExtensions.OrleansDbCacheHit) && e.Level == LogLevel.Trace && e.Message!.Contains(grainType));
            second.Should().NotContain(e => e.Name == nameof(LoggingExtensions.OrleansDbCacheMiss));
            h.Logger.Entries.Should().NotContain(e => e.Name == nameof(LoggingExtensions.OrleansShardCacheHit) || e.Name == nameof(LoggingExtensions.OrleansShardCacheMiss));
        }

        [Fact]
        [Trait("Spec", "blk-48b5d1a5f3ea4a94a1531ea0a58547f0")]
        public async Task ShardDelegateCacheLookupsIncludingTheSetShardDelegateLogShardEvents()
        {
            const string grainType = "ShardCacheEvents";
            var h = new ShardHarness(grainType);
            h.Provider.Script.ForQuery(ReadQuery(grainType)).ReturnsNoRows(ShardOrderColumns);
            var id = ShardGrainId(grainType, new ShardKey<int>(1, 1));

            await h.Storage.ReadStateAsync(grainType, id, new GrainState<OrderShardState>(new OrderShardState()));
            var first = h.Logger.Entries.Where(e => e.Id == (int)LoggingExtensions.EventIdentifier.MapperCacheStatus).ToList();
            h.Logger.Clear();
            await h.Storage.ReadStateAsync(grainType, id, new GrainState<OrderShardState>(new OrderShardState()));
            var second = h.Logger.Entries.Where(e => e.Id == (int)LoggingExtensions.EventIdentifier.MapperCacheStatus).ToList();

            first.Should().HaveCount(2, "the read delegate and the set-shard delegate are both looked up when no row is found")
                .And.OnlyContain(e => e.Name == nameof(LoggingExtensions.OrleansShardCacheMiss) && e.Level == LogLevel.Debug);
            second.Should().HaveCount(2).And.OnlyContain(e => e.Name == nameof(LoggingExtensions.OrleansShardCacheHit) && e.Level == LogLevel.Trace);
        }

        [Fact]
        [Trait("Spec", "blk-e62546e07b5e474cafcee04228744da9")]
        [Trait("Spec", "blk-d0cc6db246764e9eb27c2cf3f1941f24")]
        public async Task CompletedCommandsLogTheirTimingAtTraceUnderCmdExecuted()
        {
            const string grainType = "DbTiming";
            var h = new DbHarness(grainType);
            h.Provider.Script.ForQuery(ReadQuery(grainType)).ReturnsNoRows(OrderColumns);
            var state = new OrderDbState { OrderId = Guid.NewGuid(), Sequence = 1 };
            var grainState = new GrainState<OrderDbState>(state);
            var id = DbGrainId(grainType, state);

            await h.Storage.ReadStateAsync(grainType, id, grainState);
            await h.Storage.WriteStateAsync(grainType, id, grainState);
            await h.Storage.ClearStateAsync(grainType, id, grainState);

            var timing = h.Logger.Entries.Where(e => e.Id == (int)LoggingExtensions.EventIdentifier.CmdExecuted).ToList();
            timing.Select(e => e.Name).Should().Equal(nameof(LoggingExtensions.TraceDbReadCmdExecuted), nameof(LoggingExtensions.TraceDbWriteCmdExecuted), nameof(LoggingExtensions.TraceDbClearCmdExecuted));
            timing.Should().OnlyContain(e => e.Level == LogLevel.Trace && e.Message!.Contains(grainType) && e.Message.Contains("milliseconds"));

            const string shardGrainType = "ShardTiming";
            var s = new ShardHarness(shardGrainType);
            s.Provider.Script.ForQuery(ReadQuery(shardGrainType)).ReturnsNoRows(ShardOrderColumns);
            var key = new ShardKey<int>(2, 3);
            var shardState = new GrainState<OrderShardState>(new OrderShardState { Key = key });
            var shardId = ShardGrainId(shardGrainType, key);
            await s.Storage.ReadStateAsync(shardGrainType, shardId, shardState);
            await s.Storage.WriteStateAsync(shardGrainType, shardId, shardState);
            await s.Storage.ClearStateAsync(shardGrainType, shardId, shardState);
            var shardTiming = s.Logger.Entries.Where(e => e.Id == (int)LoggingExtensions.EventIdentifier.CmdExecuted).ToList();
            shardTiming.Select(e => e.Name).Should().Equal(nameof(LoggingExtensions.TraceShardReadCmdExecuted), nameof(LoggingExtensions.TraceShardWriteCmdExecuted), nameof(LoggingExtensions.TraceShardClearCmdExecuted));
            shardTiming.Should().OnlyContain(e => e.Level == LogLevel.Trace && e.Message!.Contains("shard 2") && e.Message.Contains(shardGrainType));
        }

        [Fact]
        [Trait("Spec", "blk-e62546e07b5e474cafcee04228744da9")]
        public async Task NoTimingEventIsLoggedWhenTraceIsDisabledOrTheCallThrowsBeforeItsQuery()
        {
            const string grainType = "DbTimingOff";
            var h = new DbHarness(grainType);
            h.Logger.MinimumLevel = LogLevel.Debug;
            h.Provider.Script.ForQuery(ReadQuery(grainType)).ReturnsNoRows(OrderColumns);
            var state = new OrderDbState { OrderId = Guid.NewGuid(), Sequence = 1 };

            await h.Storage.ReadStateAsync(grainType, DbGrainId(grainType, state), new GrainState<OrderDbState>(state));
            h.Logger.Entries.Should().NotContain(e => e.Id == (int)LoggingExtensions.EventIdentifier.CmdExecuted);

            h.Logger.MinimumLevel = LogLevel.Trace;
            await FluentActions.Awaiting(() => h.Storage.WriteStateAsync("orders", DbGrainId("orders", state), new GrainState<OrderDbState>(state))).Should().ThrowAsync<OrleansQueryNotProvidedException>();
            h.Logger.Entries.Should().NotContain(e => e.Id == (int)LoggingExtensions.EventIdentifier.CmdExecuted);
        }

        [Fact]
        [Trait("Spec", "blk-50b608096168489f837b63331c85832e")]
        public async Task LifecycleEventsAreDefinedButNeverEmittedByTheProviders()
        {
            const string grainType = "DbLifecycle";
            var h = new DbHarness(grainType);
            h.Provider.Script.ForQuery(ReadQuery(grainType)).ReturnsNoRows(OrderColumns);
            var state = new OrderDbState { OrderId = Guid.NewGuid(), Sequence = 1 };
            var grainState = new GrainState<OrderDbState>(state);
            var id = DbGrainId(grainType, state);

            await h.Storage.ReadStateAsync(grainType, id, grainState);
            await h.Storage.WriteStateAsync(grainType, id, grainState);
            await h.Storage.ClearStateAsync(grainType, id, grainState);

            h.Logger.Entries.Should().NotContain(e => e.Id == (int)LoggingExtensions.EventIdentifier.PersistanceStart || e.Id == (int)LoggingExtensions.EventIdentifier.PersistanceStop);
            h.Storage.Should().NotBeAssignableTo<ILifecycleParticipant<ISiloLifecycle>>("the lifecycle hooks that would raise the events are not implemented");
        }

        [Fact]
        [Trait("Spec", "blk-60264363d5764142a958f490ae9c643c")]
        public async Task TheCompiledShardKeyDelegateTracesEachMappedPropertyUnderMapperProcessTrace()
        {
            const string grainType = "ShardMapperTrace";
            var h = new ShardHarness(grainType);
            h.Provider.Script.ForQuery(ReadQuery(grainType)).ReturnsNoRows(ShardOrderColumns);

            await h.Storage.ReadStateAsync(grainType, ShardGrainId(grainType, new ShardKey<int>(1, 1)), new GrainState<OrderShardState>(new OrderShardState()));

            var trace = h.Logger.Entries.Where(e => e.Id == (int)LoggingExtensions.EventIdentifier.MapperProcessTrace).ToList();
            trace.Should().NotBeEmpty();
            trace.Should().OnlyContain(e => e.Level == LogLevel.Trace && e.Name == nameof(LoggingExtensions.TraceMapperGrainPersistance));
            trace.Should().Contain(e => e.Message!.Contains(nameof(OrderShardState.Key)));
        }

        [Fact]
        [Trait("Spec", "blk-8b1b32df08e148fa832435354c7a3e5b")]
        [Trait("Spec", "blk-d0cc6db246764e9eb27c2cf3f1941f24")]
        public async Task CompilingADelegateLogsItsExpressionTreeAtDebugOnly()
        {
            const string dbGrainType = "DbTreeEvents";
            var h = new DbHarness(dbGrainType);
            h.Provider.Script.ForQuery(ReadQuery(dbGrainType)).ReturnsNoRows(OrderColumns);
            var state = new OrderDbState { OrderId = Guid.NewGuid(), Sequence = 1 };
            await h.Storage.ReadStateAsync(dbGrainType, DbGrainId(dbGrainType, state), new GrainState<OrderDbState>(state));

            var trees = h.Logger.Entries.Where(e => e.Id == (int)LoggingExtensions.EventIdentifier.ExpressionTreeCreation).ToList();
            trees.Should().OnlyContain(e => e.Level == LogLevel.Debug && e.Message!.Contains(dbGrainType));
            trees.Select(e => e.Name).Should().Contain(nameof(LoggingExtensions.CreatedExpressionTreeForDatabaseKey))
                .And.Contain(nameof(LoggingExtensions.CreatedExpressionTreeForActivation), "the no-row read compiles the activation delegate");
            trees.Single(e => e.Name == nameof(LoggingExtensions.CreatedExpressionTreeForActivation)).Message.Should().Contain("activation");

            const string shardGrainType = "ShardTreeEvents";
            var s = new ShardHarness(shardGrainType);
            s.Provider.Script.ForQuery(ReadQuery(shardGrainType)).ReturnsNoRows(ShardOrderColumns);
            await s.Storage.ReadStateAsync(shardGrainType, ShardGrainId(shardGrainType, new ShardKey<int>(1, 1)), new GrainState<OrderShardState>(new OrderShardState()));
            s.Logger.Entries.Where(e => e.Id == (int)LoggingExtensions.EventIdentifier.ExpressionTreeCreation)
                .Should().NotBeEmpty().And.OnlyContain(e => e.Name == nameof(LoggingExtensions.CreatedExpressionTreeForShardKey) && e.Level == LogLevel.Debug);

            const string quietGrainType = "DbTreeEventsQuiet";
            var q = new DbHarness(quietGrainType);
            q.Logger.MinimumLevel = LogLevel.Information;
            q.Provider.Script.ForQuery(ReadQuery(quietGrainType)).ReturnsNoRows(OrderColumns);
            await q.Storage.ReadStateAsync(quietGrainType, DbGrainId(quietGrainType, state), new GrainState<OrderDbState>(state));
            q.Logger.Entries.Should().NotContain(e => e.Id == (int)LoggingExtensions.EventIdentifier.ExpressionTreeCreation);
        }

        public static TheoryData<string, LogLevel, int, string, Action<ILogger?>> LogEventContract => new()
        {
            { nameof(LoggingExtensions.PersistanceLifecycleOnStart), LogLevel.Debug, 13, "is initializing", l => l.PersistanceLifecycleOnStart("Orders") },
            { nameof(LoggingExtensions.PersistanceLifecycleOnStop), LogLevel.Debug, 14, "is terminating", l => l.PersistanceLifecycleOnStop("Orders") },
            { nameof(LoggingExtensions.OrleansDbCacheMiss), LogLevel.Debug, 4, "orleans database keys", l => l.OrleansDbCacheMiss("Orders") },
            { nameof(LoggingExtensions.OrleansDbCacheHit), LogLevel.Trace, 4, "orleans database keys", l => l.OrleansDbCacheHit("Orders") },
            { nameof(LoggingExtensions.OrleansShardCacheMiss), LogLevel.Debug, 4, "orleans sharded keys", l => l.OrleansShardCacheMiss("Orders") },
            { nameof(LoggingExtensions.OrleansShardCacheHit), LogLevel.Trace, 4, "orleans sharded keys", l => l.OrleansShardCacheHit("Orders") },
            { nameof(LoggingExtensions.TraceMapperGrainPersistance), LogLevel.Trace, 5, "processing property Orders", l => l.TraceMapperGrainPersistance("Orders") },
            { nameof(LoggingExtensions.CreatedExpressionTreeForDatabaseKey), LogLevel.Debug, 1, "key mapping for database", l => l.CreatedExpressionTreeForDatabaseKey("Orders", System.Linq.Expressions.Expression.Constant(1)) },
            { nameof(LoggingExtensions.CreatedExpressionTreeForShardKey), LogLevel.Debug, 1, "key mapping for shards", l => l.CreatedExpressionTreeForShardKey("Orders", System.Linq.Expressions.Expression.Constant(1)) },
            { nameof(LoggingExtensions.CreatedExpressionTreeForActivation), LogLevel.Debug, 1, "activation", l => l.CreatedExpressionTreeForActivation("Orders", System.Linq.Expressions.Expression.Constant(1)) },
            { nameof(LoggingExtensions.TraceDbReadCmdExecuted), LogLevel.Trace, 8, "Executed read Db command", l => l.TraceDbReadCmdExecuted("Orders", 5) },
            { nameof(LoggingExtensions.TraceDbWriteCmdExecuted), LogLevel.Trace, 8, "Executed write Db command", l => l.TraceDbWriteCmdExecuted("Orders", 5) },
            { nameof(LoggingExtensions.TraceDbClearCmdExecuted), LogLevel.Trace, 8, "Executed clear Db command", l => l.TraceDbClearCmdExecuted("Orders", 5) },
            { nameof(LoggingExtensions.TraceShardReadCmdExecuted), LogLevel.Trace, 8, "Executed read command on shard 3", l => l.TraceShardReadCmdExecuted(3, "Orders", 5) },
            { nameof(LoggingExtensions.TraceShardWriteCmdExecuted), LogLevel.Trace, 8, "Executed write command on shard 3", l => l.TraceShardWriteCmdExecuted(3, "Orders", 5) },
            { nameof(LoggingExtensions.TraceShardClearCmdExecuted), LogLevel.Trace, 8, "Executed clear command on shard 3", l => l.TraceShardClearCmdExecuted(3, "Orders", 5) },
        };

        [Theory, MemberData(nameof(LogEventContract))]
        [Trait("Spec", "blk-d0cc6db246764e9eb27c2cf3f1941f24")]
        public void EveryLogEventWrapperEmitsItsDeclaredIdLevelAndName(string eventName, LogLevel level, int id, string messageFragment, Action<ILogger?> emit)
        {
            var logger = new RecordingLogger();
            emit(logger);

            var entry = logger.Entries.Should().ContainSingle().Which;
            entry.Name.Should().Be(eventName);
            entry.Level.Should().Be(level);
            entry.Id.Should().Be(id);
            entry.Message.Should().Contain(messageFragment).And.Contain("Orders");

            // Every wrapper tolerates a null logger.
            FluentActions.Invoking(() => emit(null)).Should().NotThrow();
        }

        #endregion
    }
}
