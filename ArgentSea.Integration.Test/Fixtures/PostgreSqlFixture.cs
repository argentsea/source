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
    {
        var options = new PgShardConnectionOptions
        {
            PgShardSets =
            [
                new PgShardConnectionOptions.PgShardConnectionsConfiguration
                {
                    ShardSetName = ShardSetName,
                    DefaultShardId = 0,
                    Shards =
                    [
                        new PgShardConnectionOptions.PgShardConnectionConfiguration { ShardId = 0, ReadConnection = Connection(Shard0DatabaseName), WriteConnection = Connection(Shard0DatabaseName) },
                        new PgShardConnectionOptions.PgShardConnectionConfiguration { ShardId = 1, ReadConnection = Connection(Shard1DatabaseName), WriteConnection = Connection(Shard1DatabaseName) },
                    ],
                },
            ],
        };
        return new PgShardSets(Options.Create(options), Options.Create(new PgGlobalPropertiesOptions()), NullLogger<PgShardSets>.Instance);
    }
}
