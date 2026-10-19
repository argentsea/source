using ArgentSea.Sql;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;

namespace ArgentSea.Integration.Test.Fixtures;

/// <summary>
/// One SQL Server for the run. With a container, a dedicated <c>ArgentSeaIt</c> database is created for the TVP and mapping
/// tests; with ARGENTSEA_SQL_TEST_CONNECTION the supplied database is used as is. The Orleans scripts are deployed on first
/// request (see <see cref="EnsureOrleansScriptsAsync"/>).
/// </summary>
public sealed class SqlServerFixture : DatabaseFixture
{
    public const string TestDatabaseName = "ArgentSeaIt";
    public const string CommonDatabaseName = "Common";
    public const string Tenant0DatabaseName = "ArgentSeaTenant0";
    public const string Tenant1DatabaseName = "ArgentSeaTenant1";

    private readonly SemaphoreSlim _deployLock = new(1, 1);
    private Task? _orleansDeployment;

    protected override string DisplayName => "SQL Server";

    protected override string OverrideVariable => "ARGENTSEA_SQL_TEST_CONNECTION";

    protected override IDatabaseContainer CreateContainer()
        => new MsSqlBuilder(Environment.GetEnvironmentVariable("ARGENTSEA_MSSQL_IMAGE") is { Length: > 0 } image ? image : "mcr.microsoft.com/mssql/server:2022-latest").Build();

    /// <summary>The server-level connection string (the container's master connection, or the override as supplied).</summary>
    public string? ServerConnectionString { get; private set; }

    protected override async Task OnAvailableAsync()
    {
        ServerConnectionString = ConnectionString;
        if (!UsesContainer)
        {
            return;
        }
        await using var connection = new SqlConnection(ServerConnectionString);
        await connection.OpenAsync();
        await using (var create = new SqlCommand($"IF DB_ID('{TestDatabaseName}') IS NULL CREATE DATABASE [{TestDatabaseName}];", connection))
        {
            await create.ExecuteNonQueryAsync();
        }
        ConnectionString = ConnectionStringFor(TestDatabaseName);
    }

    /// <summary>The server connection string re-pointed at another database.</summary>
    public string ConnectionStringFor(string database)
        => new SqlConnectionStringBuilder(ServerConnectionString) { InitialCatalog = database }.ConnectionString;

    public SqlConnection Open(string? database = null)
    {
        var connection = new SqlConnection(database is null ? ConnectionString : ConnectionStringFor(database));
        connection.Open();
        return connection;
    }

    /// <summary>A SqlConnectionConfiguration carrying the server's address and credentials, aimed at the named database.</summary>
    public SqlConnectionConfiguration Connection(string database)
    {
        var csb = new SqlConnectionStringBuilder(ServerConnectionString);
        return new SqlConnectionConfiguration
        {
            DataSource = csb.DataSource,
            InitialCatalog = database,
            UserName = string.IsNullOrEmpty(csb.UserID) ? null : csb.UserID,
            Password = string.IsNullOrEmpty(csb.Password) ? null : csb.Password,
            WindowsAuth = csb.IntegratedSecurity ? true : null,
            TrustServerCertificate = csb.TrustServerCertificate ? true : null,
            Encrypt = csb.Encrypt == SqlConnectionEncryptOption.Optional ? false : null,
            ConnectTimeout = 30,
        };
    }

    /// <summary>A SqlDatabases collection with one database entry per (key, database name) pair, read and write on the same database.</summary>
    public SqlDatabases Databases(params (string key, string database)[] entries)
    {
        var options = new SqlDbConnectionOptions
        {
            SqlDbConnections = entries.Select(e => new SqlDbConnectionConfiguration
            {
                DatabaseKey = e.key,
                ReadConnection = Connection(e.database),
                WriteConnection = Connection(e.database),
            }).ToArray(),
        };
        return new SqlDatabases(Options.Create(options), Options.Create(new SqlGlobalPropertiesOptions()), NullLogger<SqlDatabases>.Instance);
    }

    /// <summary>A SqlShardSets collection with one shard set whose shards map to the given databases.</summary>
    public SqlShardSets ShardSets(string shardSetName, params (short shardId, string database)[] shards)
    {
        var options = new SqlShardConnectionOptions
        {
            SqlShardSets =
            [
                new SqlShardConnectionOptions.SqlShardConnectionsConfiguration
                {
                    ShardSetName = shardSetName,
                    DefaultShardId = shards[0].shardId,
                    Shards = shards.Select(s => new SqlShardConnectionOptions.SqlShardConnectionConfiguration
                    {
                        ShardId = s.shardId,
                        ReadConnection = Connection(s.database),
                        WriteConnection = Connection(s.database),
                    }).ToArray(),
                },
            ],
        };
        return new SqlShardSets(Options.Create(options), Options.Create(new SqlGlobalPropertiesOptions()), NullLogger<SqlShardSets>.Instance);
    }

    /// <summary>
    /// Deploys ArgentSea.Orleans.Sql/src/common.sql into the Common database and tenant.sql into two tenant databases, once per
    /// run. The scripts run as shipped, with their password and tenant-name placeholders substituted; a CREATE DATABASE batch is
    /// skipped when that database already exists so an override server can be reused.
    /// </summary>
    public Task EnsureOrleansScriptsAsync()
    {
        if (_orleansDeployment is not null)
        {
            return _orleansDeployment;
        }
        _deployLock.Wait();
        try
        {
            _orleansDeployment ??= DeployOrleansScriptsAsync();
            return _orleansDeployment;
        }
        finally
        {
            _deployLock.Release();
        }
    }

    private async Task DeployOrleansScriptsAsync()
    {
        var scripts = Path.Combine(AppContext.BaseDirectory, "Scripts");
        var substitutions = new Dictionary<string, string>
        {
            { "$ReaderPassword", "Argent$ea_Reader_1!" },
            { "$WriterPassword", "Argent$ea_Writer_1!" },
            { "$ClientPassword", "Argent$ea_Client_1!" },
        };
        await using var connection = new SqlConnection(ServerConnectionString);
        await connection.OpenAsync();
        await SqlScriptRunner.RunAsync(connection, Path.Combine(scripts, "common.sql"), substitutions);
        foreach (var tenant in new[] { Tenant0DatabaseName, Tenant1DatabaseName })
        {
            var tenantSubstitutions = new Dictionary<string, string>(substitutions) { { "$TenantName", tenant } };
            await SqlScriptRunner.RunAsync(connection, Path.Combine(scripts, "tenant.sql"), tenantSubstitutions);
        }
    }
}
