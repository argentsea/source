using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// Spec section sql-server-connection-settings: the precedence of the global, shard-set, read/write, shard and endpoint
    /// levels for Read and for Write (asserted through SqlConnectionStringBuilder, not rendered strings), the connection
    /// description, cache invalidation, PropertyChanged on every SqlConnectionPropertiesBase setting, and the option shapes.
    /// </summary>
    public class ConnectionSettingsTests
    {
        [Flags]
        public enum Levels { None = 0, Global = 1, ShardSet = 2, Read = 4, Write = 8, Shard = 16, ReadEndpoint = 32, WriteEndpoint = 64 }

        private static SqlShardSets BuildShardSets(SqlGlobalPropertiesOptions global, SqlShardConnectionOptions.SqlShardConnectionsConfiguration shardSet)
            => new SqlShardSets(Options.Create(new SqlShardConnectionOptions { SqlShardSets = new[] { shardSet } }), Options.Create(global), Substitute.For<ILogger<SqlShardSets>>());

        private static SqlConnectionStringBuilder Parse(string connectionString) => new SqlConnectionStringBuilder(connectionString);

        #region Precedence

        [Theory]
        [Trait("Spec", "blk-410b15b970ed492eaa1e9a70634ac1eb")]
        [Trait("Spec", "blk-10caf47e852e4a28b7d69a8da1ea358b")]
        [Trait("Spec", "blk-fed77f502e254db4a2f5fb302b615cb4")]
        [InlineData(Levels.Global, "global", "global")]
        [InlineData(Levels.Global | Levels.ShardSet, "shardset", "shardset")]
        [InlineData(Levels.Global | Levels.ShardSet | Levels.Read, "read", "shardset")]
        [InlineData(Levels.Global | Levels.ShardSet | Levels.Write, "shardset", "write")]
        [InlineData(Levels.Global | Levels.ShardSet | Levels.Read | Levels.Write, "read", "write")]
        [InlineData(Levels.Global | Levels.ShardSet | Levels.Read | Levels.Write | Levels.Shard, "shard", "shard")]
        [InlineData(Levels.Global | Levels.Shard, "shard", "shard")]
        [InlineData(Levels.Global | Levels.ShardSet | Levels.Read | Levels.Write | Levels.Shard | Levels.ReadEndpoint, "readendpoint", "shard")]
        [InlineData(Levels.Global | Levels.ShardSet | Levels.Read | Levels.Write | Levels.Shard | Levels.ReadEndpoint | Levels.WriteEndpoint, "readendpoint", "writeendpoint")]
        [InlineData(Levels.Global | Levels.WriteEndpoint, "global", "writeendpoint")]
        public void TheMostSpecificLevelThatSetsAPropertyWinsForEachEndpoint(Levels levels, string expectedRead, string expectedWrite)
        {
            var global = new SqlGlobalPropertiesOptions { DataSource = "server", InitialCatalog = "db" };
            var shardSet = new SqlShardConnectionOptions.SqlShardConnectionsConfiguration { ShardSetName = "Set" };
            var shard = new SqlShardConnectionOptions.SqlShardConnectionConfiguration { ShardId = 1 };
            shardSet.Shards = new[] { shard };
            if (levels.HasFlag(Levels.Global)) global.ApplicationName = "global";
            if (levels.HasFlag(Levels.ShardSet)) shardSet.ApplicationName = "shardset";
            if (levels.HasFlag(Levels.Read)) shardSet.Read = new SqlShardConnectionOptions.SqlShardConnectionConfiguration { ApplicationName = "read" };
            if (levels.HasFlag(Levels.Write)) shardSet.Write = new SqlShardConnectionOptions.SqlShardConnectionConfiguration { ApplicationName = "write" };
            if (levels.HasFlag(Levels.Shard)) shard.ApplicationName = "shard";
            if (levels.HasFlag(Levels.ReadEndpoint)) shard.ReadConnection!.ApplicationName = "readendpoint";
            if (levels.HasFlag(Levels.WriteEndpoint)) shard.WriteConnection!.ApplicationName = "writeendpoint";

            var shardSets = BuildShardSets(global, shardSet);

            Parse(shardSets["Set"][1].Read.ConnectionString).ApplicationName.Should().Be(expectedRead);
            Parse(shardSets["Set"][1].Write.ConnectionString).ApplicationName.Should().Be(expectedWrite);
            Parse(shardSets["Set"][1].Read.ConnectionString).DataSource.Should().Be("server", "a setting given only globally reaches every endpoint");
        }

        [Fact]
        [Trait("Spec", "blk-479ae8ee49ac44b1ad14507622428a36")]
        [Trait("Spec", "blk-410b15b970ed492eaa1e9a70634ac1eb")]
        [Trait("Spec", "blk-f2b6b873ef204169bb5f81282b3844f1")]
        public void GlobalAndDatabaseLevelsCombineIntoOneConnectionString()
        {
            var global = new SqlGlobalPropertiesOptions { ApplicationName = "MyApp", TypeSystemVersion = "SQL Server 2012" };
            var database = new SqlDbConnectionConfiguration { DatabaseKey = "MainDb", DataSource = "10.10.25.1", InitialCatalog = "MainDb" };
            var databases = new SqlDatabases(Options.Create(new SqlDbConnectionOptions { SqlDbConnections = new[] { database } }), Options.Create(global), Substitute.For<ILogger<SqlDatabases>>());

            var read = Parse(databases["MainDb"].Read.ConnectionString);
            var write = Parse(databases["MainDb"].Write.ConnectionString);

            foreach (var csb in new[] { read, write })
            {
                csb.DataSource.Should().Be("10.10.25.1");
                csb.InitialCatalog.Should().Be("MainDb");
                csb.ConnectTimeout.Should().Be(5, "the default connect timeout applies when no level sets one");
                csb.TypeSystemVersion.Should().Be("SQL Server 2012");
                csb.ApplicationName.Should().Be("MyApp");
                csb.ConnectRetryCount.Should().Be(0);
            }
            database.ReadConnectionInternal.Should().BeSameAs(database.ReadConnection);
            database.WriteConnectionInternal.Should().BeSameAs(database.WriteConnection);
            database.ReadConnection.Should().NotBeSameAs(database.WriteConnection, "a new entry gets two distinct endpoints");
        }

        [Fact]
        [Trait("Spec", "blk-458dcbfc50f94e28b3a1263c9a2971eb")]
        [Trait("Spec", "blk-d7adb7c4e8e94d9c8680a8919c52ba50")]
        public void TheConnectionDescriptionUsesTheLastNonEmptyCatalogAndSourceAcrossTheLevels()
        {
            var global = new SqlGlobalPropertiesOptions { InitialCatalog = "MainDb", DataSource = "sql-a" };
            var shard = new SqlShardConnectionOptions.SqlShardConnectionConfiguration { ShardId = 1, DataSource = "sql-b" };
            shard.ReadConnection!.InitialCatalog = "Tenant42";
            var shardSet = new SqlShardConnectionOptions.SqlShardConnectionsConfiguration { ShardSetName = "Set", Shards = new[] { shard } };
            BuildShardSets(global, shardSet);

            ((IDataConnection)shard.ReadConnection).ConnectionDescription.Should().Be("database Tenant42 on server sql-b");
            ((IDataConnection)shard.WriteConnection!).ConnectionDescription.Should().Be("database MainDb on server sql-b");
        }

        #endregion

        #region Invalidation

        [Fact]
        [Trait("Spec", "blk-9e743fb337144464aa02d23006b2fad3")]
        [Trait("Spec", "blk-9eb49adea69742b8bec908a4cd124aa2")]
        [Trait("Spec", "blk-10caf47e852e4a28b7d69a8da1ea358b")]
        public void AChangeToAnAmbientLayerOrALocalPropertyInvalidatesTheCachedStringAndDescription()
        {
            var global = new SqlGlobalPropertiesOptions { DataSource = "sql-a", InitialCatalog = "db" };
            var connection = new SqlConnectionConfiguration();
            connection.SetAmbientConfiguration(global, null, null, null);
            Parse(connection.GetConnectionString(null)).DataSource.Should().Be("sql-a");
            connection.ConnectionDescription.Should().Be("database db on server sql-a");

            global.DataSource = "sql-b";
            Parse(connection.GetConnectionString(null)).DataSource.Should().Be("sql-b");
            connection.ConnectionDescription.Should().Be("database db on server sql-b");
            connection.InitialCatalog = "local";
            Parse(connection.GetConnectionString(null)).InitialCatalog.Should().Be("local");
            connection.ConnectionDescription.Should().Be("database local on server sql-b");
        }

        [Fact]
        [Trait("Spec", "blk-9eb49adea69742b8bec908a4cd124aa2")]
        [Trait("Spec", "blk-10caf47e852e4a28b7d69a8da1ea358b")]
        public void ARepeatedSetAmbientConfigurationDetachesFromThePreviousLayers()
        {
            var first = new SqlGlobalPropertiesOptions { DataSource = "first" };
            var second = new SqlGlobalPropertiesOptions { DataSource = "second" };
            var connection = new SqlConnectionConfiguration();
            connection.SetAmbientConfiguration(first, null, null, null);
            connection.GetConnectionString(null);

            connection.SetAmbientConfiguration(second, null, null, null);
            Parse(connection.GetConnectionString(null)).DataSource.Should().Be("second", "the new layers are used as soon as they are supplied");
            first.DataSource = "first-changed";
            Parse(connection.GetConnectionString(null)).DataSource.Should().Be("second", "a layer no longer supplied has no effect");
            second.DataSource = "second-changed";
            Parse(connection.GetConnectionString(null)).DataSource.Should().Be("second-changed");
        }

        public static IEnumerable<object[]> Settings()
            => typeof(SqlConnectionPropertiesBase).GetProperties().Where(p => p.DeclaringType == typeof(SqlConnectionPropertiesBase) && p.CanWrite).Select(p => new object[] { p.Name });

        [Theory]
        [Trait("Spec", "blk-ae1b6c75740c4fce8cdf7b5301550d2a")]
        [MemberData(nameof(Settings))]
        public void EverySettingRaisesPropertyChangedOnlyWhenTheValueDiffers(string propertyName)
        {
            var options = new SqlGlobalPropertiesOptions();
            var property = typeof(SqlConnectionPropertiesBase).GetProperty(propertyName)!;
            var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object value = underlying == typeof(string) ? "value" : underlying == typeof(int) ? 7 : underlying == typeof(bool) ? true : Enum.GetValues(underlying).GetValue(1)!;
            var raised = new List<string>();
            options.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

            property.SetValue(options, value);
            property.SetValue(options, value);

            raised.Should().Equal(propertyName);
            property.GetValue(options).Should().Be(value);
        }

        [Fact]
        [Trait("Spec", "blk-ae1b6c75740c4fce8cdf7b5301550d2a")]
        [Trait("Spec", "blk-bcf40cddec984e70be95a1fe48b962e0")]
        [Trait("Spec", "blk-ee15a8daa2a64336be7fcd2006ca9a0c")]
        public void TheSettingSurfaceIsTheDocumentedTwentyTwoOptionalAttributes()
        {
            var expected = new[] { "ApplicationIntent", "ApplicationName", "ConnectTimeout", "CurrentLanguage", "DataSource", "Encrypt", "FailoverPartner", "InitialCatalog", "LoadBalanceTimeout", "MaxPoolSize", "MinPoolSize", "MultipleActiveResultSets", "MultiSubnetFailover", "PacketSize", "PersistSecurityInfo", "Pooling", "Replication", "TransactionBinding", "TrustServerCertificate", "TypeSystemVersion", "UserInstance", "WorkstationID" };

            var declared = typeof(SqlConnectionPropertiesBase).GetProperties().Where(p => p.DeclaringType == typeof(SqlConnectionPropertiesBase)).ToList();

            declared.Select(p => p.Name).Should().BeEquivalentTo(expected);
            declared.Should().OnlyContain(p => !p.PropertyType.IsValueType || Nullable.GetUnderlyingType(p.PropertyType) != null, "every attribute may remain unspecified");
            typeof(SqlConnectionPropertiesBase).IsAbstract.Should().BeTrue();
            typeof(SqlGlobalPropertiesOptions).GetProperties().Where(p => p.DeclaringType == typeof(SqlGlobalPropertiesOptions)).Should().BeEmpty("the global options add no members of their own");
        }

        #endregion

        #region Option shapes

        [Fact]
        [Trait("Spec", "blk-e6191e65523646849af2a6c3f4c78d6b")]
        [Trait("Spec", "blk-eef589a1bd1d4383be32c801507ef3b3")]
        [Trait("Spec", "blk-a13f0c353d17427db836e5c29d591830")]
        [Trait("Spec", "blk-fc813402dab8486dbb8bb81c1308a70f")]
        [Trait("Spec", "blk-f19df31ab55d4d93b792f24b8130b39e")]
        public void ShardOptionsExposeTheConfiguredObjectsThroughTheProviderNeutralViews()
        {
            var shard = new SqlShardConnectionOptions.SqlShardConnectionConfiguration { ShardId = 7 };
            var read = new SqlShardConnectionOptions.SqlShardConnectionConfiguration();
            var write = new SqlShardConnectionOptions.SqlShardConnectionConfiguration();
            var shardSet = new SqlShardConnectionOptions.SqlShardConnectionsConfiguration { ShardSetName = "orders", DefaultShardId = 7, Shards = new[] { shard }, Read = read, Write = write };
            var options = new SqlShardConnectionOptions { SqlShardSets = new[] { shardSet } };

            ((IShardSetsConfigurationOptions)options).ShardSetsConfigInternal.Should().BeSameAs(options.SqlShardSets);
            ((IShardSetConnectionsConfiguration)shardSet).ShardsConfigInternal.Should().BeSameAs(shardSet.Shards);
            ((IShardSetConnectionsConfiguration)shardSet).ReadConfigInternal.Should().BeSameAs(read);
            ((IShardSetConnectionsConfiguration)shardSet).WriteConfigInternal.Should().BeSameAs(write);
            new SqlShardConnectionOptions.SqlShardConnectionsConfiguration().Read.Should().BeNull("the shard-set read/write slots have no default instance");
            ((IShardConnectionConfiguration)shard).ShardId.Should().Be(7);
            ((IShardConnectionConfiguration)shard).ReadConnectionInternal.Should().BeSameAs(shard.ReadConnection);
            ((IShardConnectionConfiguration)shard).WriteConnectionInternal.Should().BeSameAs(shard.WriteConnection);
            shard.ReadConnection.Should().NotBeNull().And.NotBeSameAs(shard.WriteConnection, "a new shard initializes two distinct endpoints");
        }

        [Fact]
        [Trait("Spec", "blk-f2b6b873ef204169bb5f81282b3844f1")]
        [Trait("Spec", "blk-5a0dc5cc8524446991962a1836842293")]
        [Trait("Spec", "blk-c6c1b3ed2c4f42ef93d90e9eec984cc2")]
        [Trait("Spec", "blk-7b4446fda6c54187b28306e0c1a12746")]
        public void DatabaseOptionsExposeTheSameEntriesAndDistinctDefaultEndpoints()
        {
            var entry = new SqlDbConnectionConfiguration { DatabaseKey = "MainDb" };
            var options = new SqlDbConnectionOptions { SqlDbConnections = new[] { entry } };

            ((IDatabaseConfigurationOptions)options).DbConnectionsInternal.Should().BeSameAs(options.SqlDbConnections);
            ((IDatabaseConnectionConfiguration)entry).DatabaseKey.Should().Be("MainDb");
            entry.ReadConnection.Should().NotBeNull().And.NotBeSameAs(entry.WriteConnection);
            entry.ReadConnectionInternal.Should().BeSameAs(entry.ReadConnection);
            entry.WriteConnectionInternal.Should().BeSameAs(entry.WriteConnection);
            entry.Should().BeAssignableTo<SqlConnectionPropertiesBase>("the entry itself is the database level of the hierarchy");
            new SqlGlobalPropertiesOptions().Should().BeAssignableTo<SqlConnectionPropertiesBase>();
        }

        #endregion
    }
}
