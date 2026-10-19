using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// Retry and circuit-breaker settings inherit through the same hierarchy as the connection string:
    /// global, shard set, shard-set read/write, shard (or database), then the connection itself.
    /// </summary>
    public class ResilienceInheritanceTests
    {
        private static SqlShardSets BuildShardSets(SqlGlobalPropertiesOptions global, SqlShardConnectionOptions.SqlShardConnectionsConfiguration shardSet)
            => new SqlShardSets(Options.Create(new SqlShardConnectionOptions { SqlShardSets = new[] { shardSet } }), Options.Create(global), Substitute.For<ILogger<SqlShardSets>>());

        private static SqlShardConnectionOptions.SqlShardConnectionConfiguration Shard(short shardId)
            => new SqlShardConnectionOptions.SqlShardConnectionConfiguration { ShardId = shardId, DataSource = "server", InitialCatalog = $"db{shardId}" };

        [Fact]
        [Trait("Spec", "blk-fed77f502e254db4a2f5fb302b615cb4")]
        public void ShardConnectionsInheritGlobalResilienceSettings()
        {
            var global = new SqlGlobalPropertiesOptions { RetryCount = 7, RetryInterval = 3, RetryLengthening = SequenceLengthening.Linear, CircuitBreakerFailureCount = 11, CircuitBreakerTestInterval = 1234 };
            var shard = Shard(0);
            BuildShardSets(global, new SqlShardConnectionOptions.SqlShardConnectionsConfiguration { ShardSetName = "Set", Shards = new[] { shard } });

            foreach (IDataConnection connection in new IDataConnection[] { shard.ReadConnection!, shard.WriteConnection! })
            {
                connection.RetryCount.Should().Be(7);
                connection.CircuitBreakerFailureCount.Should().Be(11);
                connection.CircuitBreakerTestInterval.Should().Be(1234);
                connection.GetRetryTimespan(2).Should().Be(TimeSpan.FromMilliseconds(6), "linear lengthening of the inherited 3 ms interval");
            }
            shard.ReadConnection!.RetryCount.Should().BeNull("the bound configuration value of the connection itself is unchanged");
        }

        [Fact]
        [Trait("Spec", "blk-e762b989990a42f6a1bca7346f9c068d")]
        public void MoreSpecificLevelsOverrideLessSpecificLevels()
        {
            var global = new SqlGlobalPropertiesOptions { RetryCount = 1, RetryInterval = 1, CircuitBreakerFailureCount = 1, CircuitBreakerTestInterval = 1 };
            var shardSetOnly = Shard(0);
            var shardLevel = Shard(1);
            shardLevel.RetryCount = 4;
            var connectionLevel = Shard(2);
            connectionLevel.RetryCount = 4;
            connectionLevel.ReadConnection!.RetryCount = 5;
            var shardSet = new SqlShardConnectionOptions.SqlShardConnectionsConfiguration
            {
                ShardSetName = "Set",
                RetryCount = 2,
                RetryInterval = 20,
                CircuitBreakerTestInterval = 2000,
                Read = new SqlShardConnectionOptions.SqlShardConnectionConfiguration { RetryCount = 3 },
                Shards = new[] { shardSetOnly, shardLevel, connectionLevel }
            };
            BuildShardSets(global, shardSet);

            ((IDataConnection)shardSetOnly.WriteConnection!).RetryCount.Should().Be(2, "the shard set overrides global");
            ((IDataConnection)shardSetOnly.ReadConnection!).RetryCount.Should().Be(3, "the shard set’s read settings override the shard set");
            ((IDataConnection)shardSetOnly.ReadConnection!).CircuitBreakerTestInterval.Should().Be(2000);
            ((IDataConnection)shardSetOnly.ReadConnection!).CircuitBreakerFailureCount.Should().Be(1, "unset levels fall through to global");
            ((IDataConnection)shardSetOnly.ReadConnection!).GetRetryTimespan(1).Should().Be(TimeSpan.FromMilliseconds(20));
            ((IDataConnection)shardLevel.ReadConnection!).RetryCount.Should().Be(4, "the shard overrides the shard set’s read settings");
            ((IDataConnection)shardLevel.WriteConnection!).RetryCount.Should().Be(4);
            ((IDataConnection)connectionLevel.ReadConnection!).RetryCount.Should().Be(5, "the connection’s own value wins");
            ((IDataConnection)connectionLevel.WriteConnection!).RetryCount.Should().Be(4);
        }

        [Fact]
        public void DatabaseConnectionsInheritGlobalAndDatabaseResilienceSettings()
        {
            var global = new SqlGlobalPropertiesOptions { RetryCount = 7, CircuitBreakerFailureCount = 11 };
            var db = new SqlDbConnectionConfiguration { DatabaseKey = "Db", DataSource = "server", InitialCatalog = "db", CircuitBreakerFailureCount = 12 };
            new SqlDatabases(Options.Create(new SqlDbConnectionOptions { SqlDbConnections = new[] { db } }), Options.Create(global), Substitute.For<ILogger<SqlDatabases>>());

            ((IDataConnection)db.ReadConnection!).RetryCount.Should().Be(7);
            ((IDataConnection)db.ReadConnection!).CircuitBreakerFailureCount.Should().Be(12);
            ((IDataConnection)db.WriteConnection!).CircuitBreakerFailureCount.Should().Be(12);
        }

        [Fact]
        [Trait("Spec", "blk-e762b989990a42f6a1bca7346f9c068d")]
        public void LaterChangesToAnInheritedLevelAreVisible()
        {
            var global = new SqlGlobalPropertiesOptions { RetryCount = 7 };
            var shard = Shard(0);
            BuildShardSets(global, new SqlShardConnectionOptions.SqlShardConnectionsConfiguration { ShardSetName = "Set", Shards = new[] { shard } });

            global.RetryCount = 9;

            ((IDataConnection)shard.ReadConnection!).RetryCount.Should().Be(9);
        }
    }
}
