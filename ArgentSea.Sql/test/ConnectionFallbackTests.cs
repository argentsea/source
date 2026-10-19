using System;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// A database or shard configured with only a write (or only a read) connection is meant to use that connection for both,
    /// but the collection constructors pushed ambient configuration into both connections first, so the missing one threw
    /// NullReferenceException before the fallback was reached.
    /// </summary>
    public class ConnectionFallbackTests
    {
        private static SqlConnectionConfiguration WriteConnection() => new SqlConnectionConfiguration { DataSource = "WriteServer", InitialCatalog = "WriteDb" };

        [Fact]
        public void ShardWithOnlyWriteConnectionReadsFromIt()
        {
            var options = new SqlShardConnectionOptions
            {
                SqlShardSets = new[]
                {
                    new SqlShardConnectionOptions.SqlShardConnectionsConfiguration
                    {
                        ShardSetName = "WriteOnly",
                        Shards = new[]
                        {
                            new SqlShardConnectionOptions.SqlShardConnectionConfiguration { ShardId = 0, ReadConnection = null, WriteConnection = WriteConnection() }
                        }
                    }
                }
            };

            var shardSets = new SqlShardSets(Options.Create(options), Options.Create(new SqlGlobalPropertiesOptions()), null);

            var shard = shardSets["WriteOnly"][0];
            shard.Read.ConnectionString.Should().Contain("WriteServer").And.Be(shard.Write.ConnectionString);
        }

        [Fact]
        public void ShardWithNoConnectionsThrowsDescriptiveError()
        {
            var options = new SqlShardConnectionOptions
            {
                SqlShardSets = new[]
                {
                    new SqlShardConnectionOptions.SqlShardConnectionsConfiguration
                    {
                        ShardSetName = "None",
                        Shards = new[]
                        {
                            new SqlShardConnectionOptions.SqlShardConnectionConfiguration { ShardId = 0, ReadConnection = null, WriteConnection = null }
                        }
                    }
                }
            };

            Action act = () => new SqlShardSets(Options.Create(options), Options.Create(new SqlGlobalPropertiesOptions()), null);

            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void DatabaseWithOnlyWriteConnectionReadsFromIt()
        {
            var options = new SqlDbConnectionOptions
            {
                SqlDbConnections = new[]
                {
                    new SqlDbConnectionConfiguration { DatabaseKey = "WriteOnly", ReadConnection = null, WriteConnection = WriteConnection() }
                }
            };

            var databases = new SqlDatabases(Options.Create(options), Options.Create(new SqlGlobalPropertiesOptions()), null);

            var database = databases["WriteOnly"];
            database.Read.ConnectionString.Should().Contain("WriteServer").And.Be(database.Write.ConnectionString);
        }
    }
}
