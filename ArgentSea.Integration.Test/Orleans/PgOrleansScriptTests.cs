using ArgentSea.Integration.Test.Fixtures;
using FluentAssertions;
using Npgsql;

namespace ArgentSea.Integration.Test.Orleans;

/// <summary>
/// The permission contract of ArgentSea.Orleans.Pg/src/common.psql and tenant.psql, deployed as shipped (twice, by the
/// fixture, to prove they are idempotent): every routine is SECURITY DEFINER with a pinned search_path, PUBLIC holds no
/// EXECUTE right, and each group role can call exactly what it is granted — shown by connecting as a login of each role
/// and being refused everything else, the tables included.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public class PgOrleansScriptTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PgOrleansScriptTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        if (_fixture.IsAvailable)
        {
            await _fixture.EnsureOrleansScriptsAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private const string InsufficientPrivilege = "42501";

    private async Task<string?> RunAs(string database, string login, string sql)
    {
        await using var cnn = new NpgsqlConnection(_fixture.ConnectionStringFor(database, login));
        await cnn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, cnn);
        try
        {
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
            }
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.SqlState;
        }
    }

    private const string CommonRead = "SELECT * FROM rdr.orleans_cluster_version_read_v1('no-such-cluster');";
    private const string CommonGateway = "SELECT * FROM rdr.orleans_cluster_gateway_list_v1('no-such-cluster');";
    private const string CommonWrite = "CALL wtr.orleans_cluster_delete_v1('no-such-cluster');";
    private const string CommonTable = "SELECT * FROM orleans.orleans_cluster_member;";
    private const string TenantRead = "SELECT * FROM rdr.orleans_reminder_read_rows_v1('\\x00'::bytea, 'none');";
    private const string TenantWrite = "CALL wtr.orleans_reminder_delete_row_v1('\\x00'::bytea, 'none', 'none', 0);";
    private const string TenantTable = "SELECT * FROM orleans.orleans_reminder;";

    [SkippableFact]
    [Trait("Spec", "blk-f281557d20c9483597c3002b971f632e")]
    [Trait("Spec", "blk-8603d3bd15fe43e383af28d4a4c2fdd2")]
    public async Task TheReaderCanCallOnlyTheReadFunctions()
    {
        _fixture.SkipUnlessAvailable();
        const string login = PostgreSqlFixture.ReaderLogin;
        const string common = PostgreSqlFixture.OrleansCommonDatabaseName;
        const string tenant = PostgreSqlFixture.OrleansTenant0DatabaseName;

        (await RunAs(common, login, CommonRead)).Should().BeNull();
        (await RunAs(common, login, CommonGateway)).Should().BeNull();
        (await RunAs(tenant, login, TenantRead)).Should().BeNull();
        (await RunAs(common, login, CommonWrite)).Should().Be(InsufficientPrivilege, "orleans_reader has no right on the wtr procedures");
        (await RunAs(tenant, login, TenantWrite)).Should().Be(InsufficientPrivilege);
        (await RunAs(common, login, CommonTable)).Should().Be(InsufficientPrivilege, "the tables are reachable only through the routines");
        (await RunAs(tenant, login, TenantTable)).Should().Be(InsufficientPrivilege);
    }

    [SkippableFact]
    [Trait("Spec", "blk-f281557d20c9483597c3002b971f632e")]
    [Trait("Spec", "blk-8603d3bd15fe43e383af28d4a4c2fdd2")]
    public async Task TheWriterCanCallTheReadAndWriteRoutinesButNotTheTables()
    {
        _fixture.SkipUnlessAvailable();
        const string login = PostgreSqlFixture.WriterLogin;
        const string common = PostgreSqlFixture.OrleansCommonDatabaseName;
        const string tenant = PostgreSqlFixture.OrleansTenant1DatabaseName;

        (await RunAs(common, login, CommonRead)).Should().BeNull();
        (await RunAs(common, login, CommonWrite)).Should().BeNull();
        (await RunAs(tenant, login, TenantRead)).Should().BeNull();
        (await RunAs(tenant, login, TenantWrite)).Should().BeNull();
        (await RunAs(common, login, CommonTable)).Should().Be(InsufficientPrivilege);
        (await RunAs(tenant, login, TenantTable)).Should().Be(InsufficientPrivilege);
    }

    [SkippableFact]
    [Trait("Spec", "blk-f281557d20c9483597c3002b971f632e")]
    [Trait("Spec", "blk-8603d3bd15fe43e383af28d4a4c2fdd2")]
    public async Task TheClientCanCallOnlyTheGatewayListFunction()
    {
        _fixture.SkipUnlessAvailable();
        const string login = PostgreSqlFixture.ClientLogin;
        const string common = PostgreSqlFixture.OrleansCommonDatabaseName;
        const string tenant = PostgreSqlFixture.OrleansTenant0DatabaseName;

        (await RunAs(common, login, CommonGateway)).Should().BeNull();
        (await RunAs(common, login, CommonRead)).Should().Be(InsufficientPrivilege, "orleans_client is granted the gateway list alone");
        (await RunAs(common, login, CommonWrite)).Should().Be(InsufficientPrivilege);
        (await RunAs(common, login, CommonTable)).Should().Be(InsufficientPrivilege);
        (await RunAs(tenant, login, TenantRead)).Should().Be(InsufficientPrivilege);
    }

    [SkippableFact]
    [Trait("Spec", "blk-f281557d20c9483597c3002b971f632e")]
    [Trait("Spec", "blk-8603d3bd15fe43e383af28d4a4c2fdd2")]
    public async Task EveryRoutineIsSecurityDefinerWithAPinnedSearchPathAndNoPublicExecuteRight()
    {
        _fixture.SkipUnlessAvailable();
        foreach (var database in new[] { PostgreSqlFixture.OrleansCommonDatabaseName, PostgreSqlFixture.OrleansTenant0DatabaseName })
        {
            await using var cnn = _fixture.Open(database);
            await using var cmd = new NpgsqlCommand("""
                SELECT n.nspname || '.' || p.proname, p.prosecdef, coalesce(array_to_string(p.proconfig, ','), ''),
                    has_function_privilege('public', p.oid, 'EXECUTE')
                FROM pg_proc AS p JOIN pg_namespace AS n ON n.oid = p.pronamespace
                WHERE n.nspname IN ('rdr', 'wtr') AND p.proname LIKE 'orleans\_%';
                """, cnn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            var routines = new List<(string Name, bool Definer, string Config, bool PublicExecute)>();
            while (await rdr.ReadAsync())
            {
                routines.Add((rdr.GetString(0), rdr.GetBoolean(1), rdr.GetString(2), rdr.GetBoolean(3)));
            }

            routines.Should().NotBeEmpty();
            routines.Should().OnlyContain(r => r.Definer, $"every routine in {database} runs with its owner's rights");
            routines.Should().OnlyContain(r => r.Config == "search_path=pg_catalog, pg_temp", "a SECURITY DEFINER routine must not resolve names through the caller's search_path");
            routines.Should().OnlyContain(r => !r.PublicExecute, "PUBLIC's default EXECUTE right is revoked");
        }
    }
}
