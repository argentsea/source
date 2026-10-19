using ArgentSea.Integration.Test.Fixtures;
using ArgentSea.Pg;
using FluentAssertions;
using Npgsql;

namespace ArgentSea.Integration.Test.PostgreSql;

/// <summary>
/// Spec section copy-load, integration part: CopyMapper's binary COPY actually loads rows into PostgreSQL. The model-collection
/// and ShardKey Add overloads load into a transaction-local table (gone after commit) and into a persistent table, the mapped
/// column types round-trip, every shard of a shard-set batch is loaded, and a failing later step rolls the load back.
/// The unit half (DDL text, COPY text, caching, logging) lives in ArgentSea.Pg.Test/CopyLoadTests.cs.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public class CopyLoadIntegrationTests
{
    private readonly PostgreSqlFixture _fixture;
    private static readonly CancellationToken None = CancellationToken.None;

    public CopyLoadIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public class CopyRow
    {
        [MapToPgInteger("id")]
        public int Id { get; set; }

        [MapToPgBigint("big")]
        public long Big { get; set; }

        [MapToPgSmallint("small")]
        public short Small { get; set; }

        [MapToPgVarchar("name", 50)]
        public string? Name { get; set; }

        [MapToPgText("notes")]
        public string? Notes { get; set; }

        [MapToPgBoolean("flag")]
        public bool Flag { get; set; }

        [MapToPgDouble("score")]
        public double Score { get; set; }

        [MapToPgNumeric("amount", 18, 4)]
        public decimal Amount { get; set; }

        [MapToPgUuid("guid")]
        public Guid Guid { get; set; }

        [MapToPgTimestamp("stamp")]
        public DateTime Stamp { get; set; }

        [MapToPgDate("day")]
        public DateTime Day { get; set; }

        [MapToPgBytea("blob", 50)]
        public byte[]? Blob { get; set; }

        [MapToPgInteger("maybe")]
        public int? Maybe { get; set; }
    }

    public class KeyedRow
    {
        [MapPgShardKey("shard_id", "record_id")]
        [MapToPgInteger("record_id")]
        public ShardKey<int> Key { get; set; }

        [MapToPgVarchar("label", 20)]
        public string? Label { get; set; }
    }

    private static List<CopyRow> SampleRows() =>
    [
        new CopyRow
        {
            Id = 1, Big = long.MaxValue, Small = -7, Name = "Ada", Notes = "first", Flag = true, Score = 1.5, Amount = 12.3456m,
            Guid = Guid.Parse("6f1f5a8e-3c4d-4a7b-9a2f-0e1d2c3b4a59"), Stamp = new DateTime(2026, 10, 18, 9, 30, 15, 250, DateTimeKind.Unspecified),
            Day = new DateTime(2026, 10, 18), Blob = [1, 2, 3], Maybe = 42,
        },
        new CopyRow
        {
            Id = 2, Big = long.MinValue, Small = 7, Name = null, Notes = null, Flag = false, Score = -2.25, Amount = -0.0001m,
            Guid = Guid.Parse("00000000-0000-0000-0000-000000000001"), Stamp = new DateTime(1999, 12, 31, 23, 59, 59, DateTimeKind.Unspecified),
            Day = new DateTime(1999, 12, 31), Blob = null, Maybe = null,
        },
    ];

    private static string UniqueName(string prefix) => prefix + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private static TextQuery CountOf(string table) => new TextQuery($"SELECT COUNT(*)::int AS n FROM {table}", "count_" + table);

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string qualifiedName)
    {
        await using var cmd = new NpgsqlCommand("SELECT to_regclass(@name) IS NOT NULL", connection);
        cmd.Parameters.AddWithValue("name", qualifiedName);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    #region Model-collection loads

    [SkippableFact]
    [Trait("Spec", "blk-0c043264e46f44f28dbea6dd06451a09")]
    [Trait("Spec", "blk-969d29c8d4bc4914a15e26847f8a326f")]
    [Trait("Spec", "blk-7cee1125d4324f8cb4978303e9a3e1f9")]
    public async Task AnUnqualifiedNameLoadsATransactionLocalTableThatIsGoneAfterCommit()
    {
        _fixture.SkipUnlessAvailable();
        var databases = _fixture.Databases();
        var table = UniqueName("stage_rows");
        var rows = SampleRows();
        var batch = new DatabaseBatch<int>().Add(rows, table).Add(CountOf($"\"{table}\""), "n");

        var count = await databases["Main"].Write.RunAsync(batch, None);

        count.Should().Be(rows.Count, "the temp table is visible to a later step of the same transaction and holds one row per model");
        await using var connection = _fixture.Open(PostgreSqlFixture.MainDatabaseName);
        (await TableExistsAsync(connection, table)).Should().BeFalse("ON COMMIT DROP removes the transaction-local table when the batch commits");
    }

    [SkippableFact]
    [Trait("Spec", "blk-0c043264e46f44f28dbea6dd06451a09")]
    [Trait("Spec", "blk-7cee1125d4324f8cb4978303e9a3e1f9")]
    public async Task ASchemaQualifiedNameLoadsAPersistentTableAndEveryMappedTypeRoundTrips()
    {
        _fixture.SkipUnlessAvailable();
        var databases = _fixture.Databases();
        var table = "public." + UniqueName("copy_rows");
        var rows = SampleRows();
        var batch = new DatabaseBatch<int>().Add(rows, table).Add(CountOf(table), "n");

        var count = await databases["Main"].Write.RunAsync(batch, None);
        var readBack = await databases["Main"].Read.MapListAsync<CopyRow>(new TextQuery($"SELECT * FROM {table} ORDER BY id", "read_" + table), new ParameterCollection(), None);

        count.Should().Be(2);
        readBack.Should().HaveCount(2);
        readBack.Should().BeEquivalentTo(rows, options => options.WithStrictOrdering(), "every mapped column of every row round-trips through binary COPY and the reader");
        await using var connection = _fixture.Open(PostgreSqlFixture.MainDatabaseName);
        (await TableExistsAsync(connection, table)).Should().BeTrue("a schema-qualified destination is a persistent table");
    }

    [SkippableFact]
    [Trait("Spec", "blk-0c043264e46f44f28dbea6dd06451a09")]
    public async Task ASecondLoadIntoAnExistingPersistentTableAppendsRows()
    {
        _fixture.SkipUnlessAvailable();
        var databases = _fixture.Databases();
        var table = "public." + UniqueName("copy_append");

        await databases["Main"].Write.RunAsync(new DatabaseBatch<int>().Add(SampleRows(), table), None);
        var count = await databases["Main"].Write.RunAsync(new DatabaseBatch<int>().Add(SampleRows().Select(r => { r.Id += 10; return r; }).ToList(), table).Add(CountOf(table), "n"), None);

        count.Should().Be(4, "CREATE TABLE IF NOT EXISTS leaves the existing table and its rows in place");
    }

    [SkippableFact]
    [Trait("Spec", "blk-0c043264e46f44f28dbea6dd06451a09")]
    public async Task AFailingLaterStepRollsTheLoadBack()
    {
        _fixture.SkipUnlessAvailable();
        var databases = _fixture.Databases();
        var table = "public." + UniqueName("rolled_back");
        var batch = new DatabaseBatch<int>().Add(SampleRows(), table).Add(new TextQuery("SELECT 1/0 AS n", "divide_by_zero"), "n");

        Func<Task> act = () => databases["Main"].Write.RunAsync(batch, None);

        await act.Should().ThrowAsync<PostgresException>();
        await using var connection = _fixture.Open(PostgreSqlFixture.MainDatabaseName);
        (await TableExistsAsync(connection, table)).Should().BeFalse("the table creation and the COPY ran inside the batch transaction, which was rolled back");
    }

    [SkippableFact]
    [Trait("Spec", "blk-0c043264e46f44f28dbea6dd06451a09")]
    public async Task AModelWithAShardKeyPropertyLoadsItsComponentColumns()
    {
        _fixture.SkipUnlessAvailable();
        var databases = _fixture.Databases();
        var table = "public." + UniqueName("keyed_rows");
        var rows = new List<KeyedRow> { new KeyedRow { Key = new ShardKey<int>(1, 100), Label = "a" }, new KeyedRow { Key = new ShardKey<int>(2, 200), Label = "b" } };

        await databases["Main"].Write.RunAsync(new DatabaseBatch<int>().Add(rows, table), None);
        var readBack = await databases["Main"].Read.MapListAsync<KeyedRow>(new TextQuery($"SELECT * FROM {table} ORDER BY record_id", "read_" + table), new ParameterCollection(), None);

        readBack.Select(r => r.Key).Should().Equal(new ShardKey<int>(1, 100), new ShardKey<int>(2, 200));
        readBack.Select(r => r.Label).Should().Equal("a", "b");
    }

    #endregion

    #region ShardKey loads

    [SkippableFact]
    [Trait("Spec", "blk-33784e6261c14c6dbcbf461f47ecd8e6")]
    [Trait("Spec", "blk-7cee1125d4324f8cb4978303e9a3e1f9")]
    public async Task ARecordKeyListLoadsShardAndRecordColumnsIntoATransactionLocalTable()
    {
        _fixture.SkipUnlessAvailable();
        var shardSets = _fixture.ShardSets();
        var table = UniqueName("tmp_keys");
        var keys = new List<ShardKey<int>> { new ShardKey<int>(1, 10), new ShardKey<int>(1, 11), new ShardKey<int>(0, 12) };
        var batch = new ShardBatch<int>()
            .Add(keys, table, new MapToPgSmallintAttribute("shard_id"), new MapToPgIntegerAttribute("record_id"))
            .Add(new TextQuery($"SELECT SUM(\"record_id\")::int AS n FROM \"{table}\" WHERE \"shard_id\" = 1", "sum_" + table), "n");

        var sum = await shardSets[PostgreSqlFixture.ShardSetName][1].Write.RunAsync(batch, None);

        sum.Should().Be(21, "both shard-1 keys were written with their shard and record identities");
        await using var connection = _fixture.Open(PostgreSqlFixture.Shard1DatabaseName);
        (await TableExistsAsync(connection, table)).Should().BeFalse("the unqualified name is a transaction-local table");
    }

    [SkippableFact]
    [Trait("Spec", "blk-33784e6261c14c6dbcbf461f47ecd8e6")]
    [Trait("Spec", "blk-bf71333b9ab14cc3bceccfb12dfebb10")]
    public async Task AChildKeyListLoadsAPersistentTableInShardRecordChildOrder()
    {
        _fixture.SkipUnlessAvailable();
        var shardSets = _fixture.ShardSets();
        var table = "public." + UniqueName("copy_keys");
        var keys = new List<ShardKey<int, int>> { new ShardKey<int, int>(1, 42, 9), new ShardKey<int, int>(1, 43, 10) };

        await shardSets[PostgreSqlFixture.ShardSetName][1].Write.RunAsync(
            new ShardBatch<int>().Add(keys, table, new MapToPgSmallintAttribute("shard_id"), new MapToPgIntegerAttribute("record_id"), new MapToPgIntegerAttribute("child_id")), None);

        await using var connection = _fixture.Open(PostgreSqlFixture.Shard1DatabaseName);
        await using var cmd = new NpgsqlCommand($"SELECT shard_id, record_id, child_id FROM {table} ORDER BY record_id", connection);
        await using var rdr = await cmd.ExecuteReaderAsync();
        var read = new List<(short, int, int)>();
        while (await rdr.ReadAsync())
        {
            read.Add((rdr.GetInt16(0), rdr.GetInt32(1), rdr.GetInt32(2)));
        }
        read.Should().Equal(((short)1, 42, 9), ((short)1, 43, 10));
    }

    [SkippableFact]
    [Trait("Spec", "blk-33784e6261c14c6dbcbf461f47ecd8e6")]
    public async Task GrandChildAndGreatGrandChildKeyListsLoadEveryIdentityColumn()
    {
        _fixture.SkipUnlessAvailable();
        var shardSets = _fixture.ShardSets();
        var grand = UniqueName("g");
        var great = UniqueName("gg");
        var grandBatch = new ShardBatch<int>()
            .Add(new List<ShardKey<int, short, long>> { new ShardKey<int, short, long>(0, 1, 2, 3L) }, grand, new MapToPgSmallintAttribute("s"), new MapToPgIntegerAttribute("r"), new MapToPgSmallintAttribute("c"), new MapToPgBigintAttribute("g"))
            .Add(new TextQuery($"SELECT (\"s\" + \"r\" + \"c\" + \"g\")::int AS n FROM \"{grand}\"", "sum_" + grand), "n");
        var greatBatch = new ShardBatch<int>()
            .Add(new List<ShardKey<int, short, long, int>> { new ShardKey<int, short, long, int>(0, 1, 2, 3L, 4) }, great, new MapToPgSmallintAttribute("s"), new MapToPgIntegerAttribute("r"), new MapToPgSmallintAttribute("c"), new MapToPgBigintAttribute("g"), new MapToPgIntegerAttribute("gg"))
            .Add(new TextQuery($"SELECT (\"s\" + \"r\" + \"c\" + \"g\" + \"gg\")::int AS n FROM \"{great}\"", "sum_" + great), "n");

        (await shardSets[PostgreSqlFixture.ShardSetName][0].Write.RunAsync(grandBatch, None)).Should().Be(0 + 1 + 2 + 3);
        (await shardSets[PostgreSqlFixture.ShardSetName][0].Write.RunAsync(greatBatch, None)).Should().Be(0 + 1 + 2 + 3 + 4);
    }

    [SkippableFact]
    [Trait("Spec", "blk-0c043264e46f44f28dbea6dd06451a09")]
    [Trait("Spec", "blk-33784e6261c14c6dbcbf461f47ecd8e6")]
    [Trait("Spec", "blk-7cee1125d4324f8cb4978303e9a3e1f9")]
    public async Task AShardSetBatchLoadsModelsAndKeysOnEveryShard()
    {
        _fixture.SkipUnlessAvailable();
        var shardSets = _fixture.ShardSets();
        var models = "public." + UniqueName("set_rows");
        var keys = "public." + UniqueName("set_keys");
        var batch = new ShardSetBatch()
            .Add(SampleRows(), models)
            .Add(new List<ShardKey<int>> { new ShardKey<int>(0, 1), new ShardKey<int>(1, 2) }, keys, new MapToPgSmallintAttribute("shard_id"), new MapToPgIntegerAttribute("record_id"));

        await shardSets[PostgreSqlFixture.ShardSetName].Write.RunAsync(batch, None);

        foreach (var shardId in new short[] { 0, 1 })
        {
            var rowCount = await shardSets[PostgreSqlFixture.ShardSetName][shardId].Read.ListAsync<int>(CountOf(models), new ParameterCollection(), "n", None);
            var keyCount = await shardSets[PostgreSqlFixture.ShardSetName][shardId].Read.ListAsync<int>(CountOf(keys), new ParameterCollection(), "n", None);
            rowCount.Should().ContainSingle($"shard {shardId} receives every model").Which.Should().Be(2);
            keyCount.Should().ContainSingle($"shard {shardId} receives every key, whatever shard the key names").Which.Should().Be(2);
        }
    }

    #endregion
}
