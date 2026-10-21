using ArgentSea.Pg;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ArgentSea.Integration.Test.Fixtures;

/// <summary>
/// One PostgreSQL server for the run with three databases: one for the non-sharded tests and one per shard of a two-shard
/// shard set. ARGENTSEA_PG_TEST_CONNECTION bypasses the container; the databases are then created on that server if missing.
/// </summary>
public sealed class PostgreSqlFixture : DatabaseFixture
{
    public const string MainDatabaseName = "argentsea_main";
    public const string Shard0DatabaseName = "argentsea_shard0";
    public const string Shard1DatabaseName = "argentsea_shard1";
    public const string ShardSetName = "Set";

    protected override string DisplayName => "PostgreSQL";

    protected override string OverrideVariable => "ARGENTSEA_PG_TEST_CONNECTION";

    protected override IDatabaseContainer CreateContainer()
        => new PostgreSqlBuilder(Environment.GetEnvironmentVariable("ARGENTSEA_PG_IMAGE") is { Length: > 0 } image ? image : "postgres:16").Build();

    /// <summary>The server-level connection string (the container's default database, or the override as supplied).</summary>
    public string? ServerConnectionString { get; private set; }

    protected override async Task OnAvailableAsync()
    {
        ServerConnectionString = ConnectionString;
        await using var connection = new NpgsqlConnection(ServerConnectionString);
        await connection.OpenAsync();
        foreach (var database in new[] { MainDatabaseName, Shard0DatabaseName, Shard1DatabaseName })
        {
            await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
            exists.Parameters.AddWithValue("name", database);
            if (await exists.ExecuteScalarAsync() is null)
            {
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
                await create.ExecuteNonQueryAsync();
            }
        }
        ConnectionString = ConnectionStringFor(MainDatabaseName);
    }

    public string ConnectionStringFor(string database)
        => new NpgsqlConnectionStringBuilder(ServerConnectionString) { Database = database }.ConnectionString;

    public NpgsqlConnection Open(string database)
    {
        var connection = new NpgsqlConnection(ConnectionStringFor(database));
        connection.Open();
        return connection;
    }

    /// <summary>A PgConnectionConfiguration carrying the server's address and credentials, aimed at the named database.</summary>
    public PgConnectionConfiguration Connection(string database)
    {
        var csb = new NpgsqlConnectionStringBuilder(ServerConnectionString);
        return new PgConnectionConfiguration
        {
            Host = csb.Host,
            Port = csb.Port,
            Database = database,
            UserName = csb.Username,
            Password = csb.Password,
            Timeout = 30,
        };
    }

    /// <summary>A PgDatabases collection with a single "Main" entry on the main database.</summary>
    public PgDatabases Databases(string key = "Main")
    {
        var options = new PgDbConnectionOptions
        {
            PgDbConnections =
            [
                new PgDbConnectionConfiguration
                {
                    DatabaseKey = key,
                    ReadConnection = Connection(MainDatabaseName),
                    WriteConnection = Connection(MainDatabaseName),
                },
            ],
        };
        return new PgDatabases(Options.Create(options), Options.Create(new PgGlobalPropertiesOptions()), NullLogger<PgDatabases>.Instance);
    }

    /// <summary>A PgShardSets collection with shard set "Set" whose shards 0 and 1 map to the two shard databases.</summary>
    public PgShardSets ShardSets()
        => ShardSets(ShardSetName, (0, Shard0DatabaseName), (1, Shard1DatabaseName));

    /// <summary>A PgShardSets collection with one shard set over the given (shard id, database) pairs, optionally as another login.</summary>
    public PgShardSets ShardSets(string shardSetName, (short ShardId, string Database)[] shards, string? userName = null, string? password = null)
    {
        var options = new PgShardConnectionOptions
        {
            PgShardSets =
            [
                new PgShardConnectionOptions.PgShardConnectionsConfiguration
                {
                    ShardSetName = shardSetName,
                    DefaultShardId = shards[0].ShardId,
                    Shards = shards.Select(s => new PgShardConnectionOptions.PgShardConnectionConfiguration
                    {
                        ShardId = s.ShardId,
                        ReadConnection = Connection(s.Database, userName, password),
                        WriteConnection = Connection(s.Database, userName, password),
                    }).ToArray(),
                },
            ],
        };
        return new PgShardSets(Options.Create(options), Options.Create(new PgGlobalPropertiesOptions()), NullLogger<PgShardSets>.Instance);
    }

    private PgShardSets ShardSets(string shardSetName, params (short ShardId, string Database)[] shards)
        => ShardSets(shardSetName, shards, null, null);

    /// <summary>A PgDatabases collection with one entry, optionally connecting as another login.</summary>
    public PgDatabases Databases(string key, string database, string? userName = null, string? password = null)
    {
        var options = new PgDbConnectionOptions
        {
            PgDbConnections =
            [
                new PgDbConnectionConfiguration
                {
                    DatabaseKey = key,
                    ReadConnection = Connection(database, userName, password),
                    WriteConnection = Connection(database, userName, password),
                },
            ],
        };
        return new PgDatabases(Options.Create(options), Options.Create(new PgGlobalPropertiesOptions()), NullLogger<PgDatabases>.Instance);
    }

    private PgConnectionConfiguration Connection(string database, string? userName, string? password)
    {
        var connection = Connection(database);
        if (userName is not null)
        {
            connection.UserName = userName;
            connection.Password = password;
        }
        return connection;
    }

    #region Orleans scripts

    public const string OrleansCommonDatabaseName = "argentsea_orleans_common";
    public const string OrleansTenant0DatabaseName = "argentsea_orleans_tenant0";
    public const string OrleansTenant1DatabaseName = "argentsea_orleans_tenant1";

    /// <summary>Login roles created for the Orleans tests, each a member of one of the group roles the scripts grant to.</summary>
    public const string ReaderLogin = "argentsea_it_reader";
    public const string WriterLogin = "argentsea_it_writer";
    public const string ClientLogin = "argentsea_it_client";
    public const string LoginPassword = "Argent$ea_It_1";

    private readonly SemaphoreSlim _deployLock = new(1, 1);
    private Task? _orleansDeployment;

    /// <summary>
    /// Deploys ArgentSea.Orleans.Pg/src/common.psql and tenant.psql as shipped (each as one command, twice in a row to prove
    /// they are idempotent) into a common database and two tenant databases, and creates one login per group role.
    /// Runs once per fixture.
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
        var common = await File.ReadAllTextAsync(Path.Combine(scripts, "common.psql"));
        var tenant = await File.ReadAllTextAsync(Path.Combine(scripts, "tenant.psql"));
        await using (var server = new NpgsqlConnection(ServerConnectionString))
        {
            await server.OpenAsync();
            foreach (var database in new[] { OrleansCommonDatabaseName, OrleansTenant0DatabaseName, OrleansTenant1DatabaseName })
            {
                await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", server);
                exists.Parameters.AddWithValue("name", database);
                if (await exists.ExecuteScalarAsync() is null)
                {
                    await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", server);
                    await create.ExecuteNonQueryAsync();
                }
            }
        }
        await RunScriptTwiceAsync(OrleansCommonDatabaseName, common);
        await RunScriptTwiceAsync(OrleansTenant0DatabaseName, tenant);
        await RunScriptTwiceAsync(OrleansTenant1DatabaseName, tenant);
        await using var cnn = Open(OrleansCommonDatabaseName);
        foreach (var (login, group) in new[] { (ReaderLogin, "orleans_reader"), (WriterLogin, "orleans_writer"), (ClientLogin, "orleans_client") })
        {
            await using var cmd = new NpgsqlCommand($"""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{login}') THEN
                        CREATE ROLE {login} LOGIN PASSWORD '{LoginPassword}';
                    END IF;
                END
                $$;
                GRANT {group} TO {login};
                """, cnn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task RunScriptTwiceAsync(string database, string script)
    {
        for (var run = 1; run <= 2; run++)
        {
            await using var cnn = Open(database);
            await using var cmd = new NpgsqlCommand(script, cnn) { CommandTimeout = 120 };
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (PostgresException ex)
            {
                throw new InvalidOperationException($"Deploying the Orleans script to {database} failed on run {run} ({ex.SqlState}: {ex.MessageText}; {ex.Where}).", ex);
            }
        }
    }

    /// <summary>A connection string for a database as one of the Orleans login roles.</summary>
    public string ConnectionStringFor(string database, string login)
        => new NpgsqlConnectionStringBuilder(ServerConnectionString) { Database = database, Username = login, Password = LoginPassword }.ConnectionString;

    #endregion
}
