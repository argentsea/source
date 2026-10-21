using System.Data.Common;
using ArgentSea.Pg;
using ArgentSea.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArgentSea.Orleans.Pg.Test;

/// <summary>
/// An <see cref="IDataProviderServiceFactory"/> that hands the TestKit's scripted provider the shard / database identity a
/// PostgreSQL configuration carries in its Database setting (see <see cref="FakePgDatabases.Create"/> and
/// <see cref="FakePgShardSets.Create"/>), so <see cref="ResultScript"/> can script per shard and <see cref="CommandRecorder"/>
/// reports which shard and endpoint ran each command.
/// </summary>
internal sealed class FakePgProviderServices : IDataProviderServiceFactory
{
    public FakeProviderServices Inner { get; } = new();

    public ResultScript Script => Inner.Script;

    public CommandRecorder Recorder => Inner.Recorder;

    public DbConnection NewConnection(string connectionString)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database ?? string.Empty;
        return Inner.NewConnection(database.StartsWith(FakeConnectionInfo.Prefix, StringComparison.Ordinal) ? database : connectionString);
    }

    public DbCommand NewCommand(string storedProcedureName, DbConnection connection) => Inner.NewCommand(storedProcedureName, connection);

    public bool GetIsErrorTransient(Exception exception) => Inner.GetIsErrorTransient(exception);

    public void SetParameters(DbCommand cmd, string[]? parmeterNames, DbParameterCollection parameters, IDictionary<string, object>? parameterValues)
        => Inner.SetParameters(cmd, parmeterNames, parameters, parameterValues);
}

/// <summary>A <see cref="PgDatabases"/> collection over the fake provider.</summary>
internal sealed class FakePgDatabases : PgDatabases
{
    private FakePgDatabases(PgDbConnectionOptions options, FakePgProviderServices provider)
        : base(Options.Create(options), provider, Options.Create(new PgGlobalPropertiesOptions()), NullLogger<PgDatabases>.Instance)
    {
        Provider = provider;
    }

    public FakePgProviderServices Provider { get; }

    public static FakePgDatabases Create(params string[] databaseKeys)
    {
        var options = new PgDbConnectionOptions
        {
            PgDbConnections = databaseKeys.Select(key => new PgDbConnectionConfiguration
            {
                DatabaseKey = key,
                Host = "localhost",
                RetryCount = 0,
                CircuitBreakerFailureCount = 0,
                ReadConnection = new PgConnectionConfiguration { Database = FakeConnectionInfo.ForDatabase(key, FakeConnectionAccess.Read) },
                WriteConnection = new PgConnectionConfiguration { Database = FakeConnectionInfo.ForDatabase(key, FakeConnectionAccess.Write) },
            }).ToArray()
        };
        return new FakePgDatabases(options, new FakePgProviderServices());
    }
}

/// <summary>A <see cref="PgShardSets"/> collection over the fake provider.</summary>
internal sealed class FakePgShardSets : PgShardSets
{
    private FakePgShardSets(PgShardConnectionOptions options, FakePgProviderServices provider)
        : base(Options.Create(options), provider, Options.Create(new PgGlobalPropertiesOptions()), NullLogger<PgShardSets>.Instance)
    {
        Provider = provider;
    }

    public FakePgProviderServices Provider { get; }

    public static FakePgShardSets Create(string shardSetName, params short[] shardIds)
    {
        var options = new PgShardConnectionOptions
        {
            PgShardSets =
            [
                new PgShardConnectionOptions.PgShardConnectionsConfiguration
                {
                    ShardSetName = shardSetName,
                    DefaultShardId = shardIds[0],
                    Host = "localhost",
                    RetryCount = 0,
                    CircuitBreakerFailureCount = 0,
                    Shards = shardIds.Select(id => new PgShardConnectionOptions.PgShardConnectionConfiguration
                    {
                        ShardId = id,
                        ReadConnection = new PgConnectionConfiguration { Database = FakeConnectionInfo.ForShard(shardSetName, id, FakeConnectionAccess.Read) },
                        WriteConnection = new PgConnectionConfiguration { Database = FakeConnectionInfo.ForShard(shardSetName, id, FakeConnectionAccess.Write) },
                    }).ToArray()
                }
            ]
        };
        return new FakePgShardSets(options, new FakePgProviderServices());
    }
}

/// <summary>Builds <see cref="PostgresException"/> instances with a chosen SQLSTATE.</summary>
internal static class PostgresExceptions
{
    public static PostgresException WithSqlState(string sqlState, string message = "fake postgres failure")
        => new(message, "ERROR", "ERROR", sqlState);
}
