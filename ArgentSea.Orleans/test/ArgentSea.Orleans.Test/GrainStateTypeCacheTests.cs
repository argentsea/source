using ArgentSea.Sql;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Orleans.Configuration;
using Orleans.Storage;

namespace ArgentSea.Orleans.Test
{
    /// <summary>
    /// Orleans passes the state name, not the grain type, to IGrainStorage, and the state name does not identify the state type:
    /// every Grain&lt;TState&gt; uses "state", and [PersistentState] names may repeat across grains and providers.
    /// The compiled-lambda caches must therefore not hand one state type’s lambda to another.
    /// </summary>
    public class GrainStateTypeCacheTests
    {
        // Orleans 10 GrainRuntime.GetStorage<TGrainState> passes this literal for every Grain<TState>.
        private const string GrainOfTStateStateName = "state";
        private const short ShardId = 1;

        public class OrderDbState
        {
            [MapToSqlUniqueIdentifier("OrderId", true)]
            public Guid OrderId { get; set; }

            [MapToSqlNVarChar("OrderDescription", -1)]
            public string? Description { get; set; }
        }

        public class CustomerDbState
        {
            [MapToSqlBigInt("CustomerId", true)]
            public long CustomerId { get; set; }

            [MapToSqlNVarChar("CustomerName", -1)]
            public string? Name { get; set; }
        }

        public class OrderShardState
        {
            [MapSqlShardKey("OrderShard", "OrderRecordId")]
            [MapToSqlInt("OrderRecordId")]
            public ShardKey<int> OrderKey { get; set; } = ShardKey<int>.Empty;
        }

        public class CustomerShardState
        {
            [MapSqlShardKey("CustomerShard", "CustomerRecordId")]
            [MapToSqlInt("CustomerRecordId")]
            public ShardKey<int> CustomerKey { get; set; } = ShardKey<int>.Empty;
        }

        private static (ArgentSeaDbGrainPersistence<SqlDbConnectionOptions> storage, SqlDatabases dbs) BuildDbStorage(string dbKey, string stateName)
        {
            var dbOptions = new SqlDbConnectionOptions
            {
                SqlDbConnections = new[] { new SqlDbConnectionConfiguration { DatabaseKey = dbKey, DataSource = "localhost", InitialCatalog = dbKey } }
            };
            var dbs = new SqlDatabases(Options.Create(dbOptions), Options.Create(new SqlGlobalPropertiesOptions()), Substitute.For<ILogger<SqlDatabases>>());
            var definitions = new List<OrleansDbQueryDefinitions> { new(stateName, new QueryProcedure($"ws.Read{dbKey}"), QueryResultFormat.ResultSet, new QueryProcedure($"ws.Write{dbKey}"), new QueryProcedure($"ws.Clear{dbKey}")) };
            var storage = new ArgentSeaDbGrainPersistence<SqlDbConnectionOptions>(
                dbs,
                Options.Create(new OrleansDbPersistenceOptions(dbKey, definitions)),
                Options.Create(new ClusterOptions { ServiceId = "test" }),
                new DebugLogger());
            dbs[dbKey].Read.MockResults.Add($"ws.Read{dbKey}", null!);
            return (storage, dbs);
        }

        private static ArgentSeaShardGrainPersistence<SqlShardConnectionOptions> BuildShardStorage(string shardSetKey, string stateName)
        {
            var shardOptions = new SqlShardConnectionOptions
            {
                SqlShardSets = new[]
                {
                    new SqlShardConnectionOptions.SqlShardConnectionsConfiguration
                    {
                        ShardSetName = shardSetKey,
                        DefaultShardId = ShardId,
                        DataSource = "localhost",
                        Shards = new[] { new SqlShardConnectionOptions.SqlShardConnectionConfiguration { ShardId = ShardId, InitialCatalog = shardSetKey } }
                    }
                }
            };
            var shards = new SqlShardSets(Options.Create(shardOptions), Options.Create(new SqlGlobalPropertiesOptions()), Substitute.For<ILogger<SqlShardSets>>());
            var definitions = new List<OrleansShardQueryDefinitions> { new(stateName, new QueryProcedure($"ws.Read{shardSetKey}"), QueryResultFormat.ResultSet, new QueryProcedure($"ws.Write{shardSetKey}"), new QueryProcedure($"ws.Clear{shardSetKey}")) };
            shards[shardSetKey][ShardId].Read.MockResults.Add($"ws.Read{shardSetKey}", null!);
            return new ArgentSeaShardGrainPersistence<SqlShardConnectionOptions>(
                shards,
                new OrleansShardPersistenceOptions(shardSetKey, definitions),
                new ClusterOptions { ServiceId = "test" },
                Substitute.For<ILogger<ArgentSeaShardGrainPersistence<SqlShardConnectionOptions>>>());
        }

        private static GrainId DbGrainId<TModel>(string grainType, TModel source)
            => GrainId.Create(GrainType.Create(grainType), OrleansExpressionHelper.BuildGrainIdFromDbModel<TModel>(null)(source, null));

        private static GrainId ShardGrainId(string grainType, ShardKey<int> key)
            => GrainId.Create(GrainType.Create(grainType), new IdSpan(key.ToUtf8().ToArray()));

        [Fact]
        [Trait("Spec", "blk-06f6df1901f441ac99c743bb9c22dfbe")]
        public async Task DbStorageKeepsStateTypesWithTheSameStateNameApart()
        {
            var (orderStorage, _) = BuildDbStorage("OrderStateDb", GrainOfTStateStateName);
            var (customerStorage, _) = BuildDbStorage("CustomerStateDb", GrainOfTStateStateName);
            var order = new OrderDbState { OrderId = Guid.NewGuid() };
            var customer = new CustomerDbState { CustomerId = 4242L };
            var orderState = new GrainState<OrderDbState>(new OrderDbState());
            var customerState = new GrainState<CustomerDbState>(new CustomerDbState());

            await orderStorage.ReadStateAsync(GrainOfTStateStateName, DbGrainId("order", order), orderState);
            await customerStorage.ReadStateAsync(GrainOfTStateStateName, DbGrainId("customer", customer), customerState);

            orderState.State!.OrderId.Should().Be(order.OrderId);
            customerState.State!.CustomerId.Should().Be(4242L, "the customer state’s own key lambda is used, not the order state’s");
        }

        [Fact]
        [Trait("Spec", "blk-4feff7c4f860482aad1b10303a45f512")]
        [Trait("Spec", "blk-2e4e93bd74c445d8b24dcf451658f08b")]
        public async Task ShardStorageKeepsStateTypesWithTheSameStateNameApart()
        {
            var orderStorage = BuildShardStorage("OrderStateShards", GrainOfTStateStateName);
            var customerStorage = BuildShardStorage("CustomerStateShards", GrainOfTStateStateName);
            var orderKey = new ShardKey<int>(ShardId, 11);
            var customerKey = new ShardKey<int>(ShardId, 22);
            var orderState = new GrainState<OrderShardState>(new OrderShardState());
            var customerState = new GrainState<CustomerShardState>(new CustomerShardState());

            await orderStorage.ReadStateAsync(GrainOfTStateStateName, ShardGrainId("order", orderKey), orderState);
            await customerStorage.ReadStateAsync(GrainOfTStateStateName, ShardGrainId("customer", customerKey), customerState);

            orderState.State!.OrderKey.Should().Be(orderKey);
            customerState.State!.CustomerKey.Should().Be(customerKey, "the customer state’s own shard-key lambda is used, not the order state’s");
        }
    }
}
