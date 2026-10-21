using ArgentSea.Integration.Test.Fixtures;
using ArgentSea.Orleans;
using ArgentSea.Pg;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Orleans;
using Orleans.Configuration;
using Orleans.Runtime;

namespace ArgentSea.Integration.Test.Orleans;

/// <summary>
/// Grain storage through ArgentSea over PostgreSQL, end to end: the provider-neutral ArgentSeaDbGrainPersistence and
/// ArgentSeaShardGrainPersistence, instantiated for PgDbConnectionOptions and PgShardConnectionOptions as
/// ArgentSea.Orleans.Pg's factory does, write, read and clear grain state against real tables. The grain queries are the
/// application's own, so this test creates them: a statement file for each read (a PostgreSQL procedure cannot return a
/// result set) and procedures, called as CALL, for the writes and clears.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public class PgGrainStorageIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PgGrainStorageIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public class CatalogItemState
    {
        [MapToPgBigint("item_id", true)]
        public long ItemId { get; set; }

        [MapToPgVarchar("name", 100)]
        public string? Name { get; set; }

        [MapToPgInteger("quantity")]
        public int Quantity { get; set; }
    }

    public class OrderState
    {
        [MapPgShardKey("order_id")]
        [MapToPgBigint("order_id")]
        public ShardKey<long> Key { get; set; } = ShardKey<long>.Empty;

        [MapToPgVarchar("description", 200)]
        public string? Description { get; set; }
    }

    private const string Schema = """
        CREATE SCHEMA IF NOT EXISTS it_grain;
        CREATE TABLE IF NOT EXISTS it_grain.{0} ({1});
        CREATE OR REPLACE PROCEDURE it_grain.write_{0}({2}) LANGUAGE sql AS $$ {3} $$;
        CREATE OR REPLACE PROCEDURE it_grain.clear_{0}({4}) LANGUAGE sql AS $$ DELETE FROM it_grain.{0} WHERE {5} = $1; $$;
        """;

    private static async Task DeployAsync(string connectionString, string sql)
    {
        await using var cnn = new NpgsqlConnection(connectionString);
        await cnn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, cnn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Lazy<QueryStatement> Statement(string name, string sql)
    {
        var path = Path.Combine(Path.GetTempPath(), $"argentsea-it-{name}-{Guid.NewGuid():N}.psql");
        File.WriteAllText(path, sql);
        return QueryStatement.Create(name, null, path);
    }

    public async Task InitializeAsync()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }
        await _fixture.EnsureOrleansScriptsAsync();
        await DeployAsync(_fixture.ConnectionStringFor(PostgreSqlFixture.OrleansCommonDatabaseName), string.Format(Schema,
            "catalog_item", "item_id bigint PRIMARY KEY, name varchar(100), quantity integer NOT NULL",
            "item_id bigint, name varchar, quantity integer",
            "INSERT INTO it_grain.catalog_item (item_id, name, quantity) VALUES ($1, $2, $3) ON CONFLICT (item_id) DO UPDATE SET name = EXCLUDED.name, quantity = EXCLUDED.quantity;",
            "item_id bigint", "item_id"));
        foreach (var tenant in new[] { PostgreSqlFixture.OrleansTenant0DatabaseName, PostgreSqlFixture.OrleansTenant1DatabaseName })
        {
            await DeployAsync(_fixture.ConnectionStringFor(tenant), string.Format(Schema,
                "orders", "order_id bigint PRIMARY KEY, description varchar(200)",
                "order_id bigint, description varchar",
                "INSERT INTO it_grain.orders (order_id, description) VALUES ($1, $2) ON CONFLICT (order_id) DO UPDATE SET description = EXCLUDED.description;",
                "order_id bigint", "order_id"));
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
    [SkippableFact]
    [Trait("Spec", "blk-8b4e6953a3b042338493b7f32c5fc76c")]
    public async Task DatabaseGrainStateIsWrittenReadAndCleared()
    {
        _fixture.SkipUnlessAvailable();
        const string grainType = "catalogitem";
        var definitions = new List<OrleansDbQueryDefinitions>
        {
            new(grainType,
                Statement("ReadCatalogItem", "SELECT item_id, name, quantity FROM it_grain.catalog_item WHERE item_id = @item_id;"),
                QueryResultFormat.ResultSet,
                new QueryProcedure("it_grain.write_catalog_item"),
                new QueryProcedure("it_grain.clear_catalog_item")),
        };
        var storage = new ArgentSeaDbGrainPersistence<PgDbConnectionOptions>(
            _fixture.Databases("Common", PostgreSqlFixture.OrleansCommonDatabaseName),
            Options.Create(new OrleansDbPersistenceOptions("Common", definitions)),
            Options.Create(new ClusterOptions { ServiceId = "it" }),
            NullLogger.Instance);
        var item = new CatalogItemState { ItemId = Random.Shared.NextInt64(1, long.MaxValue), Name = "widget", Quantity = 3 };
        var grainId = GrainId.Create(GrainType.Create(grainType), SpanParser.GetDbGrainId(item, NullLogger.Instance));

        var missing = new GrainState<CatalogItemState>();
        await storage.ReadStateAsync(grainType, grainId, missing);
        await storage.WriteStateAsync(grainType, grainId, new GrainState<CatalogItemState>(item));
        await using (var common = _fixture.Open(PostgreSqlFixture.OrleansCommonDatabaseName))
        await using (var count = new NpgsqlCommand($"SELECT count(*) FROM it_grain.catalog_item WHERE item_id = {item.ItemId}", common))
        {
            ((long)(await count.ExecuteScalarAsync())!).Should().Be(1, "the write procedure stored the state");
        }
        var found = new GrainState<CatalogItemState>();
        await storage.ReadStateAsync(grainType, grainId, found);
        await storage.ClearStateAsync(grainType, grainId, new GrainState<CatalogItemState>(found.State)); // Clear marks the state it is given as not existing
        var cleared = new GrainState<CatalogItemState>();
        await storage.ReadStateAsync(grainType, grainId, cleared);

        missing.RecordExists.Should().BeFalse();
        missing.State!.ItemId.Should().Be(item.ItemId, "a missing record yields a default state keyed from the grain id");
        found.RecordExists.Should().BeTrue();
        found.State.Should().BeEquivalentTo(item);
        cleared.RecordExists.Should().BeFalse();
    }

    [SkippableFact]
    [Trait("Spec", "blk-8b4e6953a3b042338493b7f32c5fc76c")]
    public async Task ShardGrainStateIsWrittenReadAndClearedOnTheShardItsKeyNames()
    {
        _fixture.SkipUnlessAvailable();
        const string grainType = "order";
        var definitions = new List<OrleansShardQueryDefinitions>
        {
            new(grainType,
                Statement("ReadOrder", "SELECT order_id, description FROM it_grain.orders WHERE order_id = @order_id;"),
                QueryResultFormat.ResultSet,
                new QueryProcedure("it_grain.write_orders"),
                new QueryProcedure("it_grain.clear_orders")),
        };
        var storage = new ArgentSeaShardGrainPersistence<PgShardConnectionOptions>(
            _fixture.ShardSets("Tenants", [(0, PostgreSqlFixture.OrleansTenant0DatabaseName), (1, PostgreSqlFixture.OrleansTenant1DatabaseName)]),
            new OrleansShardPersistenceOptions("Tenants", definitions),
            new ClusterOptions { ServiceId = "it" },
            NullLogger<ArgentSeaShardGrainPersistence<PgShardConnectionOptions>>.Instance);
        var key = new ShardKey<long>(1, Random.Shared.NextInt64(1, long.MaxValue));
        var grainId = GrainId.Create(GrainType.Create(grainType), new IdSpan(key.ToUtf8().ToArray()));
        var order = new OrderState { Key = key, Description = "two widgets" };

        await storage.WriteStateAsync(grainType, grainId, new GrainState<OrderState>(order));
        var found = new GrainState<OrderState>();
        await storage.ReadStateAsync(grainType, grainId, found);
        await using var shard1 = _fixture.Open(PostgreSqlFixture.OrleansTenant1DatabaseName);
        await using var count1 = new NpgsqlCommand($"SELECT count(*) FROM it_grain.orders WHERE order_id = {key.RecordId}", shard1);
        var rowsOnShard1 = (long)(await count1.ExecuteScalarAsync())!;
        await using var shard0 = _fixture.Open(PostgreSqlFixture.OrleansTenant0DatabaseName);
        await using var count0 = new NpgsqlCommand($"SELECT count(*) FROM it_grain.orders WHERE order_id = {key.RecordId}", shard0);
        var rowsOnShard0 = (long)(await count0.ExecuteScalarAsync())!;
        await storage.ClearStateAsync(grainType, grainId, new GrainState<OrderState>(found.State)); // Clear marks the state it is given as not existing
        var cleared = new GrainState<OrderState>();
        await storage.ReadStateAsync(grainType, grainId, cleared);

        found.RecordExists.Should().BeTrue();
        found.State!.Key.Should().Be(key, "the shard id comes from the shard that served the read");
        found.State.Description.Should().Be("two widgets");
        rowsOnShard1.Should().Be(1, "the grain key names shard 1");
        rowsOnShard0.Should().Be(0);
        cleared.RecordExists.Should().BeFalse();
    }
}
