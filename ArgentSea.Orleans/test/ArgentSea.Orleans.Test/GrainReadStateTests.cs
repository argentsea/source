using ArgentSea.Sql;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Orleans.Configuration;
using Orleans.Storage;
using Orleans.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace ArgentSea.Orleans.Test
{
    public class GrainReadStateTests
    {
        private const string DbKey = "ReadStateDb";
        private const string ShardSetKey = "ReadStateShards";
        private const short ShardId = 1;

        public class DbModel
        {
            [MapToSqlUniqueIdentifier("ReadStateId1", true)]
            public Guid Id1 { get; set; } = Guid.NewGuid();

            [MapToSqlBigInt("ReadStateId2", true)]
            public long Id2 { get; set; } = 1L;

            [MapToSqlNVarChar("ReadStateDescription", -1)]
            public string? Description { get; set; }
        }

        public class ActivatedDbModel : IActivator<ActivatedDbModel>
        {
            [MapToSqlUniqueIdentifier("ReadStateId1", true)]
            public Guid Id1 { get; set; } = Guid.NewGuid();

            [MapToSqlBigInt("ReadStateId2", true)]
            public long Id2 { get; set; } = 1L;

            public string? ActivatedFor { get; set; }

            public static ActivatedDbModel Activatate<Tmodel>(string grainType, ReadOnlyMemory<byte> key)
                => new ActivatedDbModel { ActivatedFor = grainType };
        }

        public class ShardModel
        {
            [MapSqlShardKey("ReadStateShard", "ReadStateRecordId")]
            [MapToSqlInt("ReadStateRecordId")]
            public ShardKey<int> RecordKey { get; set; } = ShardKey<int>.Empty;

            [MapToSqlNVarChar("ReadStateDescription", -1)]
            public string? Description { get; set; }
        }

        public class ActivatedShardModel : IActivator<ActivatedShardModel>
        {
            [MapSqlShardKey("ReadStateShard", "ReadStateRecordId")]
            [MapToSqlInt("ReadStateRecordId")]
            public ShardKey<int> RecordKey { get; set; } = ShardKey<int>.Empty;

            public string? ActivatedFor { get; set; }

            public static ActivatedShardModel Activatate<Tmodel>(string grainType, ReadOnlyMemory<byte> key)
                => new ActivatedShardModel { ActivatedFor = grainType };
        }

        /// <summary>
        /// A grain state with no parameterless constructor, created only by its IActivator factory.
        /// </summary>
        public class ConstructorlessShardModel : IActivator<ConstructorlessShardModel>
        {
            public ConstructorlessShardModel(string activatedFor)
            {
                this.ActivatedFor = activatedFor;
            }

            [MapSqlShardKey("ReadStateShard", "ReadStateRecordId")]
            [MapToSqlInt("ReadStateRecordId")]
            public ShardKey<int> RecordKey { get; set; } = ShardKey<int>.Empty;

            public string? ActivatedFor { get; }

            public static ConstructorlessShardModel Activatate<Tmodel>(string grainType, ReadOnlyMemory<byte> key)
                => new ConstructorlessShardModel(grainType);
        }

        private static QueryProcedure ReadQuery(string grainType) => new QueryProcedure($"ws.Read{grainType}");

        private static (ArgentSeaDbGrainPersistence<SqlDbConnectionOptions> storage, SqlDatabases dbs) BuildDbStorage(string grainType)
        {
            var dbOptions = new SqlDbConnectionOptions
            {
                SqlDbConnections = new[] { new SqlDbConnectionConfiguration { DatabaseKey = DbKey, DataSource = "localhost", InitialCatalog = "ReadStateDb" } }
            };
            var dbs = new SqlDatabases(Options.Create(dbOptions), Options.Create(new SqlGlobalPropertiesOptions()), Substitute.For<ILogger<SqlDatabases>>());
            var definitions = new List<OrleansDbQueryDefinitions> { new(grainType, ReadQuery(grainType), QueryResultFormat.ResultSet, new QueryProcedure($"ws.Write{grainType}"), new QueryProcedure($"ws.Clear{grainType}")) };
            var storage = new ArgentSeaDbGrainPersistence<SqlDbConnectionOptions>(
                dbs,
                Options.Create(new OrleansDbPersistenceOptions(DbKey, definitions)),
                Options.Create(new ClusterOptions { ServiceId = "test" }),
                new DebugLogger());
            return (storage, dbs);
        }

        private static (ArgentSeaShardGrainPersistence<SqlShardConnectionOptions> storage, SqlShardSets shards) BuildShardStorage(string grainType)
        {
            var shardOptions = new SqlShardConnectionOptions
            {
                SqlShardSets = new[]
                {
                    new SqlShardConnectionOptions.SqlShardConnectionsConfiguration
                    {
                        ShardSetName = ShardSetKey,
                        DefaultShardId = ShardId,
                        DataSource = "localhost",
                        Shards = new[] { new SqlShardConnectionOptions.SqlShardConnectionConfiguration { ShardId = ShardId, InitialCatalog = "ReadStateShard1" } }
                    }
                }
            };
            var shards = new SqlShardSets(Options.Create(shardOptions), Options.Create(new SqlGlobalPropertiesOptions()), Substitute.For<ILogger<SqlShardSets>>());
            var definitions = new List<OrleansShardQueryDefinitions> { new(grainType, ReadQuery(grainType), QueryResultFormat.ResultSet, new QueryProcedure($"ws.Write{grainType}"), new QueryProcedure($"ws.Clear{grainType}")) };
            var storage = new ArgentSeaShardGrainPersistence<SqlShardConnectionOptions>(
                shards,
                new OrleansShardPersistenceOptions(ShardSetKey, definitions),
                new ClusterOptions { ServiceId = "test" },
                Substitute.For<ILogger<ArgentSeaShardGrainPersistence<SqlShardConnectionOptions>>>());
            return (storage, shards);
        }

        private static GrainId DbGrainId<TModel>(string grainType, TModel source)
            => GrainId.Create(GrainType.Create(grainType), OrleansExpressionHelper.BuildGrainIdFromDbModel<TModel>(null)(source, null));

        private static GrainId ShardGrainId(string grainType, ShardKey<int> key)
            => GrainId.Create(GrainType.Create(grainType), new IdSpan(key.ToUtf8().ToArray()));

        [Fact]
        [Trait("Spec", "blk-ea747f3d72834d758567f1684fcd03f6")]
        public async Task WriteAndClearForUndefinedGrainTypeNameTheGrainTypeOnce()
        {
            var (dbStorage, _) = BuildDbStorage("DefinedDbGrain");
            var (shardStorage, _) = BuildShardStorage("DefinedShardGrain");
            var dbState = new GrainState<DbModel>(new DbModel());
            var shardState = new GrainState<ShardModel>(new ShardModel());
            var dbId = DbGrainId("UndefinedGrain", new DbModel());
            var shardId = ShardGrainId("UndefinedGrain", new ShardKey<int>(ShardId, 1));

            var thrown = new List<OrleansQueryNotProvidedException>
            {
                (await FluentActions.Awaiting(() => dbStorage.WriteStateAsync("UndefinedGrain", dbId, dbState)).Should().ThrowAsync<OrleansQueryNotProvidedException>()).Which,
                (await FluentActions.Awaiting(() => dbStorage.ClearStateAsync("UndefinedGrain", dbId, dbState)).Should().ThrowAsync<OrleansQueryNotProvidedException>()).Which,
                (await FluentActions.Awaiting(() => shardStorage.WriteStateAsync("UndefinedGrain", shardId, shardState)).Should().ThrowAsync<OrleansQueryNotProvidedException>()).Which,
                (await FluentActions.Awaiting(() => shardStorage.ClearStateAsync("UndefinedGrain", shardId, shardState)).Should().ThrowAsync<OrleansQueryNotProvidedException>()).Which,
            };

            thrown.Should().AllSatisfy(ex =>
            {
                ex.Message.Should().Contain("“UndefinedGrain”");
                ex.Message.Should().NotContain("is not defined in the configuration");
            });
            thrown[0].Message.Should().Contain("Write");
            thrown[1].Message.Should().Contain("Clear");
        }

        [Fact]
        [Trait("Spec", "blk-44544fdf39714664a511cf1b0ec33aa8")]
        [Trait("Spec", "blk-06f6df1901f441ac99c743bb9c22dfbe")]
        public async Task DbReadWithNoRowsReportsRecordMissingAndSetsKeys()
        {
            const string grainType = "ReadStateDbMiss";
            var (storage, dbs) = BuildDbStorage(grainType);
            dbs[DbKey].Read.MockResults.Add(ReadQuery(grainType).Name, null!);
            var source = new DbModel { Id1 = Guid.NewGuid(), Id2 = 42L };
            var stale = new DbModel { Description = "stale" };
            var grainState = new GrainState<DbModel>(stale);

            await storage.ReadStateAsync(grainType, DbGrainId(grainType, source), grainState);

            grainState.RecordExists.Should().BeFalse("the read returned no rows");
            grainState.State.Should().NotBeNull().And.NotBeSameAs(stale);
            grainState.State.Description.Should().BeNull();
            grainState.State.Id1.Should().Be(source.Id1, "the key values are set from the grain id");
            grainState.State.Id2.Should().Be(source.Id2, "the key values are set from the grain id");
        }

        [Fact]
        [Trait("Spec", "blk-06f6df1901f441ac99c743bb9c22dfbe")]
        [Trait("Spec", "blk-a8e6f9304aca4ad89e16aef95a905c67")]
        public async Task DbReadWithRowReportsRecordExists()
        {
            const string grainType = "ReadStateDbHit";
            var (storage, dbs) = BuildDbStorage(grainType);
            var found = new DbModel { Id1 = Guid.NewGuid(), Id2 = 7L, Description = "found" };
            dbs[DbKey].Read.MockResults.Add(ReadQuery(grainType).Name, found);
            var grainState = new GrainState<DbModel>(new DbModel());

            await storage.ReadStateAsync(grainType, DbGrainId(grainType, found), grainState);

            grainState.RecordExists.Should().BeTrue();
            grainState.State.Should().BeSameAs(found);
        }

        [Fact]
        [Trait("Spec", "blk-c2a686aa169a458888cf973d1649f118")]
        [Trait("Spec", "blk-44544fdf39714664a511cf1b0ec33aa8")]
        public async Task DbReadWithNoRowsUsesActivator()
        {
            const string grainType = "ReadStateDbActivator";
            var (storage, dbs) = BuildDbStorage(grainType);
            dbs[DbKey].Read.MockResults.Add(ReadQuery(grainType).Name, null!);
            var grainState = new GrainState<ActivatedDbModel>(new ActivatedDbModel());

            await storage.ReadStateAsync(grainType, DbGrainId(grainType, new ActivatedDbModel()), grainState);

            grainState.RecordExists.Should().BeFalse();
            grainState.State!.ActivatedFor.Should().Be(grainType, "the IActivator factory creates the state when no record exists");
        }

        [Fact]
        [Trait("Spec", "blk-f620591363cf42b185bc01c0c89314b0")]
        [Trait("Spec", "blk-4feff7c4f860482aad1b10303a45f512")]
        public async Task ShardReadWithNoRowsReportsRecordMissingAndSetsShardKey()
        {
            const string grainType = "ReadStateShardMiss";
            var (storage, shards) = BuildShardStorage(grainType);
            shards[ShardSetKey][ShardId].Read.MockResults.Add(ReadQuery(grainType).Name, null!);
            var key = new ShardKey<int>(ShardId, 1234);
            var stale = new ShardModel { Description = "stale" };
            var grainState = new GrainState<ShardModel>(stale);

            await storage.ReadStateAsync(grainType, ShardGrainId(grainType, key), grainState);

            grainState.RecordExists.Should().BeFalse("the read returned no rows");
            grainState.State.Should().NotBeNull().And.NotBeSameAs(stale);
            grainState.State.Description.Should().BeNull();
            grainState.State.RecordKey.Should().Be(key, "the shard key is set from the grain id");
        }

        [Fact]
        [Trait("Spec", "blk-4feff7c4f860482aad1b10303a45f512")]
        [Trait("Spec", "blk-0cecdb69369448b39fda18cab835ecd8")]
        public async Task ShardReadWithRowReportsRecordExists()
        {
            const string grainType = "ReadStateShardHit";
            var (storage, shards) = BuildShardStorage(grainType);
            var key = new ShardKey<int>(ShardId, 99);
            var found = new ShardModel { RecordKey = key, Description = "found" };
            shards[ShardSetKey][ShardId].Read.MockResults.Add(ReadQuery(grainType).Name, found);
            var grainState = new GrainState<ShardModel>(new ShardModel());

            await storage.ReadStateAsync(grainType, ShardGrainId(grainType, key), grainState);

            grainState.RecordExists.Should().BeTrue();
            grainState.State.Should().BeSameAs(found);
        }

        [Fact]
        [Trait("Spec", "blk-c2a686aa169a458888cf973d1649f118")]
        public async Task ShardReadWithNoRowsUsesActivator()
        {
            const string grainType = "ReadStateShardActivator";
            var (storage, shards) = BuildShardStorage(grainType);
            shards[ShardSetKey][ShardId].Read.MockResults.Add(ReadQuery(grainType).Name, null!);
            var grainState = new GrainState<ActivatedShardModel>(new ActivatedShardModel());

            await storage.ReadStateAsync(grainType, ShardGrainId(grainType, new ShardKey<int>(ShardId, 5)), grainState);

            grainState.RecordExists.Should().BeFalse();
            grainState.State!.ActivatedFor.Should().Be(grainType, "the IActivator factory creates the state when no record exists");
        }

        [Fact]
        [Trait("Spec", "blk-c2a686aa169a458888cf973d1649f118")]
        public async Task ShardReadOfStateWithoutParameterlessConstructorUsesActivator()
        {
            const string grainType = "ReadStateShardNoCtor";
            var (storage, shards) = BuildShardStorage(grainType);
            shards[ShardSetKey][ShardId].Read.MockResults.Add(ReadQuery(grainType).Name, null!);
            // Orleans' StateStorageBridge creates the state with its serialization activator before calling ReadStateAsync;
            // for a type without a parameterless constructor that activator returns an uninitialized object rather than throwing.
            var orleansActivator = new ServiceCollection()
                .AddSerializer(_ => { })
                .BuildServiceProvider()
                .GetRequiredService<global::Orleans.Serialization.Serializers.IActivatorProvider>()
                .GetActivator<ConstructorlessShardModel>();
            var grainState = new GrainState<ConstructorlessShardModel>(orleansActivator.Create());

            await storage.ReadStateAsync(grainType, ShardGrainId(grainType, new ShardKey<int>(ShardId, 5)), grainState);

            grainState.RecordExists.Should().BeFalse();
            grainState.State!.ActivatedFor.Should().Be(grainType, "no parameterless constructor is needed when Orleans supplies the initial state and the type implements IActivator");
        }
    }
}
