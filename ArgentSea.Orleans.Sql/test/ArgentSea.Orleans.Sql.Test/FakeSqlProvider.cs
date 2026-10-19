using System.Data.Common;
using System.Reflection;
using ArgentSea.Sql;
using ArgentSea.TestKit;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArgentSea.Orleans.Sql.Test;

/// <summary>
/// An <see cref="IDataProviderServiceFactory"/> that hands the TestKit's scripted provider the shard / database identity a
/// SQL Server configuration carries in its Initial Catalog (see <see cref="FakeSqlDatabases.Create"/> and
/// <see cref="FakeSqlShardSets.Create"/>), so <see cref="ResultScript"/> can script per shard and <see cref="CommandRecorder"/>
/// reports which shard and endpoint ran each command.
/// </summary>
internal sealed class FakeSqlProviderServices : IDataProviderServiceFactory
{
    public FakeProviderServices Inner { get; } = new();

    public ResultScript Script => Inner.Script;

    public CommandRecorder Recorder => Inner.Recorder;

    public DbConnection NewConnection(string connectionString)
    {
        var catalog = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        return Inner.NewConnection(catalog.StartsWith(FakeConnectionInfo.Prefix, StringComparison.Ordinal) ? catalog : connectionString);
    }

    public DbCommand NewCommand(string storedProcedureName, DbConnection connection) => Inner.NewCommand(storedProcedureName, connection);

    public bool GetIsErrorTransient(Exception exception) => Inner.GetIsErrorTransient(exception);

    public void SetParameters(DbCommand cmd, string[]? parmeterNames, DbParameterCollection parameters, IDictionary<string, object>? parameterValues)
        => Inner.SetParameters(cmd, parmeterNames, parameters, parameterValues);
}

/// <summary>A <see cref="SqlDatabases"/> collection over the fake provider.</summary>
internal sealed class FakeSqlDatabases : SqlDatabases
{
    private FakeSqlDatabases(SqlDbConnectionOptions options, FakeSqlProviderServices provider)
        : base(Options.Create(options), provider, Options.Create(new SqlGlobalPropertiesOptions()), NullLogger<SqlDatabases>.Instance)
    {
        Provider = provider;
    }

    public FakeSqlProviderServices Provider { get; }

    public static FakeSqlDatabases Create(params string[] databaseKeys)
    {
        var options = new SqlDbConnectionOptions
        {
            SqlDbConnections = databaseKeys.Select(key => new SqlDbConnectionConfiguration
            {
                DatabaseKey = key,
                DataSource = "localhost",
                RetryCount = 0,
                CircuitBreakerFailureCount = 0,
                ReadConnection = new SqlConnectionConfiguration { InitialCatalog = FakeConnectionInfo.ForDatabase(key, FakeConnectionAccess.Read) },
                WriteConnection = new SqlConnectionConfiguration { InitialCatalog = FakeConnectionInfo.ForDatabase(key, FakeConnectionAccess.Write) },
            }).ToArray()
        };
        return new FakeSqlDatabases(options, new FakeSqlProviderServices());
    }
}

/// <summary>A <see cref="SqlShardSets"/> collection over the fake provider.</summary>
internal sealed class FakeSqlShardSets : SqlShardSets
{
    private FakeSqlShardSets(SqlShardConnectionOptions options, FakeSqlProviderServices provider)
        : base(Options.Create(options), provider, Options.Create(new SqlGlobalPropertiesOptions()), NullLogger<SqlShardSets>.Instance)
    {
        Provider = provider;
    }

    public FakeSqlProviderServices Provider { get; }

    public static FakeSqlShardSets Create(string shardSetName, params short[] shardIds)
    {
        var options = new SqlShardConnectionOptions
        {
            SqlShardSets =
            [
                new SqlShardConnectionOptions.SqlShardConnectionsConfiguration
                {
                    ShardSetName = shardSetName,
                    DefaultShardId = shardIds[0],
                    DataSource = "localhost",
                    RetryCount = 0,
                    CircuitBreakerFailureCount = 0,
                    Shards = shardIds.Select(id => new SqlShardConnectionOptions.SqlShardConnectionConfiguration
                    {
                        ShardId = id,
                        ReadConnection = new SqlConnectionConfiguration { InitialCatalog = FakeConnectionInfo.ForShard(shardSetName, id, FakeConnectionAccess.Read) },
                        WriteConnection = new SqlConnectionConfiguration { InitialCatalog = FakeConnectionInfo.ForShard(shardSetName, id, FakeConnectionAccess.Write) },
                    }).ToArray()
                }
            ]
        };
        return new FakeSqlShardSets(options, new FakeSqlProviderServices());
    }
}

/// <summary>Builds <see cref="SqlException"/> instances (which have no public constructor) with a chosen error number.</summary>
internal static class SqlExceptions
{
    public static SqlException WithNumber(int number, string message = "fake sql failure")
    {
        var errorCtor = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        var usedNumber = false;
        var args = errorCtor.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(int) && !usedNumber)
            {
                usedNumber = true;
                return (object?)number;
            }
            if (p.ParameterType == typeof(string))
            {
                return p.Name == "errorMessage" ? message : string.Empty;
            }
            if (p.ParameterType == typeof(Exception))
            {
                return null;
            }
            return p.HasDefaultValue && p.DefaultValue is not null ? p.DefaultValue : Activator.CreateInstance(p.ParameterType);
        }).ToArray();
        var error = errorCtor.Invoke(args);

        var collection = Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(collection, [error]);

        var create = typeof(SqlException).GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static, [typeof(SqlErrorCollection), typeof(string)])!;
        var exception = (SqlException)create.Invoke(null, [collection, "16.0.0"])!;
        if (exception.Number != number)
        {
            throw new InvalidOperationException($"Could not build a SqlException with number {number} (got {exception.Number}).");
        }
        return exception;
    }
}
