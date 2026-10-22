using System.Data;
using ArgentSea.Integration.Test.Fixtures;
using ArgentSea.Sql;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace ArgentSea.Integration.Test.SqlServer;

/// <summary>
/// Spec section table-valued-parameters, integration part: a mapped model collection travels to SQL Server as a table-valued
/// parameter through the real provider pipeline (SqlDatabases, the SQL Server DataProviderServiceFactory, a stored procedure
/// with a user-defined table type) and its rows come back through the reader mapper. Covers every mapped column type in the
/// model, the ShardKey component columns, the ShardKey list helpers, and the empty collection sent as a null parameter.
/// The unit half (record shape, caching, column selection) is in ArgentSea.Sql.Test/TvpAndTypeMappingTests.cs.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class TvpIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private static readonly CancellationToken None = CancellationToken.None;
    private const string Db = "Main";

    public TvpIntegrationTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    public class TvpRow
    {
        [MapSqlShardKey("ShardId", "RecordId")]
        [MapToSqlInt("RecordId")]
        public ShardKey<int> Key { get; set; }

        [MapToSqlNVarChar("Name", 50)]
        public string? Name { get; set; }

        [MapToSqlBigInt("Big")]
        public long Big { get; set; }

        [MapToSqlSmallInt("Small")]
        public short Small { get; set; }

        [MapToSqlTinyInt("Tiny")]
        public byte Tiny { get; set; }

        [MapToSqlBit("Flag")]
        public bool Flag { get; set; }

        [MapToSqlDecimal("Amount", 18, 4)]
        public decimal Amount { get; set; }

        [MapToSqlFloat("Score")]
        public double Score { get; set; }

        [MapToSqlUniqueIdentifier("Id")]
        public Guid Id { get; set; }

        [MapToSqlDateTime2("Stamp")]
        public DateTime Stamp { get; set; }

        [MapToSqlVarBinary("Blob", 50)]
        public byte[]? Blob { get; set; }

        [MapToSqlInt("Maybe")]
        public int? Maybe { get; set; }
    }

    public class KeyRow
    {
        [MapSqlShardKey("ShardId", "RecordId")]
        [MapToSqlInt("RecordId")]
        public ShardKey<int> Key { get; set; }
    }

    private static readonly QueryProcedure Echo = new QueryProcedure("dbo.ArgentSeaItTvpEcho", new[] { "@Rows" });
    private static readonly QueryProcedure EchoKeys = new QueryProcedure("dbo.ArgentSeaItKeyEcho", new[] { "@Keys" });

    public async Task InitializeAsync()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }
        await using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        const string setup = """
            IF TYPE_ID('dbo.ArgentSeaItTvpRow') IS NULL
                CREATE TYPE dbo.ArgentSeaItTvpRow AS TABLE (
                    ShardId smallint NULL, RecordId int NULL, Name nvarchar(50) NULL, Big bigint NULL, Small smallint NULL, Tiny tinyint NULL,
                    Flag bit NULL, Amount decimal(18, 4) NULL, Score float NULL, Id uniqueidentifier NULL, Stamp datetime2 NULL, Blob varbinary(50) NULL, Maybe int NULL);
            IF TYPE_ID('dbo.ArgentSeaItKeyRow') IS NULL
                CREATE TYPE dbo.ArgentSeaItKeyRow AS TABLE (ShardId smallint NULL, RecordId int NULL);
            """;
        await using (var create = new SqlCommand(setup, connection))
        {
            await create.ExecuteNonQueryAsync();
        }
        await using (var echo = new SqlCommand("CREATE OR ALTER PROCEDURE dbo.ArgentSeaItTvpEcho @Rows dbo.ArgentSeaItTvpRow READONLY AS SELECT ShardId, RecordId, Name, Big, Small, Tiny, Flag, Amount, Score, Id, Stamp, Blob, Maybe FROM @Rows ORDER BY RecordId;", connection))
        {
            await echo.ExecuteNonQueryAsync();
        }
        await using (var echoKeys = new SqlCommand("CREATE OR ALTER PROCEDURE dbo.ArgentSeaItKeyEcho @Keys dbo.ArgentSeaItKeyRow READONLY AS SELECT ShardId, RecordId FROM @Keys ORDER BY RecordId;", connection))
        {
            await echoKeys.ExecuteNonQueryAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private SqlDatabases Databases() => _fixture.Databases((Db, new SqlConnectionStringBuilder(_fixture.ConnectionString).InitialCatalog));

    private static List<TvpRow> SampleRows() =>
    [
        new TvpRow
        {
            Key = new ShardKey<int>(3, 41), Name = "Ada", Big = long.MaxValue, Small = -7, Tiny = 255, Flag = true, Amount = 12.3456m, Score = 1.5,
            Id = Guid.Parse("6f1f5a8e-3c4d-4a7b-9a2f-0e1d2c3b4a59"), Stamp = new DateTime(2026, 10, 18, 9, 30, 15, 250), Blob = [1, 2, 3], Maybe = 42,
        },
        new TvpRow
        {
            Key = new ShardKey<int>(4, 42), Name = null, Big = long.MinValue, Small = 7, Tiny = 0, Flag = false, Amount = -0.0001m, Score = -2.25,
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Stamp = new DateTime(1999, 12, 31, 23, 59, 59), Blob = null, Maybe = null,
        },
    ];

    [SkippableFact]
    [Trait("Spec", "blk-a66adaef2f1d429bb26b2e05e419f643")]
    [Trait("Spec", "blk-fbdf64e03c074d3897c44271174d5f91")]
    [Trait("Spec", "blk-0539e7af93cc455ba620a90d60dd1467")]
    public async Task AMappedCollectionRoundTripsEveryColumnTypeThroughAUserDefinedTableType()
    {
        _fixture.SkipUnlessAvailable();
        var databases = Databases();
        var rows = SampleRows();
        var prms = new ParameterCollection().AddSqlTableValuedParameter("@Rows", rows, null);

        var readBack = await databases[Db].Read.MapListAsync<TvpRow>(Echo, prms, None);

        readBack.Should().HaveCount(2);
        readBack.Should().BeEquivalentTo(rows, options => options.WithStrictOrdering(), "every mapped column, the ShardKey components included, survives the TVP and the reader");
        readBack[0].Key.Should().Be(new ShardKey<int>(3, 41));
        readBack[1].Maybe.Should().BeNull("a null nullable property is sent as a NULL cell");
    }

    [SkippableFact]
    [Trait("Spec", "blk-74941f3ac3184613b6ef9b24cf5e7257")]
    [Trait("Spec", "blk-fbdf64e03c074d3897c44271174d5f91")]
    public async Task AnEmptyShardKeyIsSentAsNullComponentColumns()
    {
        _fixture.SkipUnlessAvailable();
        var databases = Databases();
        var prms = new ParameterCollection().AddSqlTableValuedParameter("@Rows", new List<TvpRow> { new TvpRow { Key = ShardKey<int>.Empty, Name = "empty-key" } }, null);

        await using var connection = _fixture.Open();
        await using var cmd = new SqlCommand("dbo.ArgentSeaItTvpEcho", connection) { CommandType = CommandType.StoredProcedure };
        var source = (SqlParameter)prms["@Rows"];
        cmd.Parameters.Add(new SqlParameter("@Rows", SqlDbType.Structured) { TypeName = "dbo.ArgentSeaItTvpRow", Value = source.Value });
        await using var rdr = await cmd.ExecuteReaderAsync();
        (await rdr.ReadAsync()).Should().BeTrue();
        rdr.IsDBNull(0).Should().BeTrue("ShardId is NULL for an Empty key");
        rdr.IsDBNull(1).Should().BeTrue("RecordId is NULL for an Empty key");
        rdr.GetString(2).Should().Be("empty-key");
    }

    [SkippableFact]
    [Trait("Spec", "blk-110eeb9f9d5a4351b53aa654d13c9a06")]
    [Trait("Spec", "blk-b4e0ec89a80e4a8098bbb175a16a3743")]
    public async Task AnEmptyCollectionTravelsAsANullParameterAndYieldsNoRows()
    {
        _fixture.SkipUnlessAvailable();
        var databases = Databases();
        var prms = new ParameterCollection().AddSqlTableValuedParameter("@Rows", new List<TvpRow>(), null);

        ((SqlParameter)prms["@Rows"]).Value.Should().BeNull("an empty collection is represented by a null table-valued parameter value");
        var readBack = await databases[Db].Read.MapListAsync<TvpRow>(Echo, prms, None);

        readBack.Should().BeEmpty("SQL Server treats the null table-valued parameter as an empty table");
    }

    [SkippableFact]
    [Trait("Spec", "blk-fbdf64e03c074d3897c44271174d5f91")]
    public async Task AShardKeyListHelperSendsShardAndRecordColumnsInThatOrder()
    {
        _fixture.SkipUnlessAvailable();
        var databases = Databases();
        var keys = new[] { new ShardKey<int>(7, 101), new ShardKey<int>(8, 102) };
        var prms = new ParameterCollection().AddSqlTableValuedParameter("@Keys", keys, "ShardId", SqlDbType.SmallInt, "RecordId", SqlDbType.Int);

        var readBack = await databases[Db].Read.MapListAsync<KeyRow>(EchoKeys, prms, None);

        readBack.Select(r => r.Key).Should().Equal(keys);
    }

    [SkippableFact]
    [Trait("Spec", "blk-b4e0ec89a80e4a8098bbb175a16a3743")]
    [Trait("Spec", "blk-228687eae22e43e29513484279c12650")]
    public async Task TheTypeNameDeclaredOnTheAttributeIsWhatTheServerResolves()
    {
        _fixture.SkipUnlessAvailable();
        var rows = SampleRows();
        var prms = new ParameterCollection();
        prms.CreateInputParameters(new Parent { Rows = rows }, null);
        var source = (SqlParameter)prms["@Rows"];

        source.SqlDbType.Should().Be(SqlDbType.Structured);
        source.TypeName.Should().Be("dbo.ArgentSeaItTvpRow");
        // A text command needs the type name the attribute declared; the server rejects the batch if it does not resolve.
        await using var connection = _fixture.Open();
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM @Rows", connection);
        cmd.Parameters.Add(new SqlParameter("@Rows", SqlDbType.Structured) { TypeName = source.TypeName, Value = source.Value });
        (await cmd.ExecuteScalarAsync()).Should().Be(2);
    }

    public class Parent
    {
        [MapToSqlInt("ParentId")]
        public int ParentId { get; set; }

        [MapToSqlTableValuedParameter("@Rows", "dbo.ArgentSeaItTvpRow")]
        public List<TvpRow>? Rows { get; set; }
    }
}
