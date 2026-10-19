using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Pg.Test
{
    /// <summary>
    /// Retry and circuit-breaker settings inherit through the same hierarchy as the connection string:
    /// global, shard set, shard-set read/write, shard (or database), then the connection itself.
    /// </summary>
    public class ResilienceInheritanceTests
    {
        private static PgShardConnectionOptions.PgShardConnectionConfiguration Shard(short shardId)
            => new PgShardConnectionOptions.PgShardConnectionConfiguration { ShardId = shardId, Host = "server", Database = $"db{shardId}" };

        [Fact]
        [Trait("Spec", "blk-e762b989990a42f6a1bca7346f9c068d")]
        [Trait("Spec", "blk-51ae255c144c4e64ab5a98767536236a")]
        public void ShardConnectionsInheritResilienceSettings()
        {
            var global = new PgGlobalPropertiesOptions { RetryCount = 7, RetryInterval = 3, RetryLengthening = SequenceLengthening.Linear, CircuitBreakerFailureCount = 11, CircuitBreakerTestInterval = 1234 };
            var inheriting = Shard(0);
            var overriding = Shard(1);
            overriding.RetryCount = 4;
            overriding.WriteConnection!.RetryCount = 5;
            var shardSet = new PgShardConnectionOptions.PgShardConnectionsConfiguration
            {
                ShardSetName = "Set",
                CircuitBreakerTestInterval = 2000,
                Read = new PgShardConnectionOptions.PgShardConnectionConfiguration { RetryCount = 3 },
                Shards = new[] { inheriting, overriding }
            };
            new PgShardSets(Options.Create(new PgShardConnectionOptions { PgShardSets = new[] { shardSet } }), Options.Create(global), Substitute.For<ILogger<PgShardSets>>());

            IDataConnection write = inheriting.WriteConnection!;
            write.RetryCount.Should().Be(7, "inherited from global");
            write.CircuitBreakerFailureCount.Should().Be(11);
            write.CircuitBreakerTestInterval.Should().Be(2000, "the shard set overrides global");
            write.GetRetryTimespan(2).Should().Be(TimeSpan.FromMilliseconds(6), "linear lengthening of the inherited 3 ms interval");
            ((IDataConnection)inheriting.ReadConnection!).RetryCount.Should().Be(3, "the shard set’s read settings override global");
            ((IDataConnection)overriding.ReadConnection!).RetryCount.Should().Be(4, "the shard overrides the shard set’s read settings");
            ((IDataConnection)overriding.WriteConnection!).RetryCount.Should().Be(5, "the connection’s own value wins");
            inheriting.WriteConnection!.RetryCount.Should().BeNull("the bound configuration value of the connection itself is unchanged");
        }

        [Fact]
        public void DatabaseConnectionsInheritGlobalAndDatabaseResilienceSettings()
        {
            var global = new PgGlobalPropertiesOptions { RetryCount = 7, CircuitBreakerFailureCount = 11 };
            var db = new PgDbConnectionConfiguration { DatabaseKey = "Db", Host = "server", Database = "db", CircuitBreakerFailureCount = 12 };
            new PgDatabases(Options.Create(new PgDbConnectionOptions { PgDbConnections = new[] { db } }), Options.Create(global), Substitute.For<ILogger<PgDatabases>>());

            ((IDataConnection)db.ReadConnection!).RetryCount.Should().Be(7);
            ((IDataConnection)db.WriteConnection!).CircuitBreakerFailureCount.Should().Be(12);

            global.RetryCount = 9;
            ((IDataConnection)db.ReadConnection!).RetryCount.Should().Be(9, "later changes to an inherited level are visible");
        }
    }
}
