using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using Xunit;

namespace ArgentSea.Pg.Test
{
    /// <summary>
    /// Spec section postgres-connection-settings: the precedence of the global, shard-set, read/write, shard and endpoint
    /// levels for Read and for Write (asserted through NpgsqlConnectionStringBuilder), the connection description, cache
    /// invalidation, PropertyChanged on every PgConnectionPropertiesBase setting, the option shapes, and the connect timeout default.
    /// </summary>
    public class ConnectionSettingsTests
    {
        [Flags]
        public enum Levels { None = 0, Global = 1, ShardSet = 2, Read = 4, Write = 8, Shard = 16, ReadEndpoint = 32, WriteEndpoint = 64 }

        private static PgShardSets BuildShardSets(PgGlobalPropertiesOptions global, PgShardConnectionOptions.PgShardConnectionsConfiguration shardSet)
            => new PgShardSets(Options.Create(new PgShardConnectionOptions { PgShardSets = new[] { shardSet } }), Options.Create(global), Substitute.For<ILogger<PgShardSets>>());

        private static NpgsqlConnectionStringBuilder Parse(string connectionString) => new NpgsqlConnectionStringBuilder(connectionString);

        #region Precedence

        [Theory]
        [Trait("Spec", "blk-6da4600392654036b9a6be1ea0c48f12")]
        [Trait("Spec", "blk-e01e74190009483f90b1ef64b92e45db")]
        [Trait("Spec", "blk-51ae255c144c4e64ab5a98767536236a")]
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
            var global = new PgGlobalPropertiesOptions { Host = "server", Database = "db" };
            var shardSet = new PgShardConnectionOptions.PgShardConnectionsConfiguration { ShardSetName = "Set" };
            var shard = new PgShardConnectionOptions.PgShardConnectionConfiguration { ShardId = 1 };
            shardSet.Shards = new[] { shard };
            if (levels.HasFlag(Levels.Global)) global.ApplicationName = "global";
            if (levels.HasFlag(Levels.ShardSet)) shardSet.ApplicationName = "shardset";
            if (levels.HasFlag(Levels.Read)) shardSet.Read = new PgShardConnectionOptions.PgShardConnectionConfiguration { ApplicationName = "read" };
            if (levels.HasFlag(Levels.Write)) shardSet.Write = new PgShardConnectionOptions.PgShardConnectionConfiguration { ApplicationName = "write" };
            if (levels.HasFlag(Levels.Shard)) shard.ApplicationName = "shard";
            if (levels.HasFlag(Levels.ReadEndpoint)) shard.ReadConnection!.ApplicationName = "readendpoint";
            if (levels.HasFlag(Levels.WriteEndpoint)) shard.WriteConnection!.ApplicationName = "writeendpoint";

            var shardSets = BuildShardSets(global, shardSet);

            Parse(shardSets["Set"][1].Read.ConnectionString).ApplicationName.Should().Be(expectedRead);
            Parse(shardSets["Set"][1].Write.ConnectionString).ApplicationName.Should().Be(expectedWrite);
            Parse(shardSets["Set"][1].Read.ConnectionString).Host.Should().Be("server", "a setting given only globally reaches every endpoint");
        }

        [Fact]
        [Trait("Spec", "blk-fcdaf3764077419aad4f081d0caa3e8e")]
        [Trait("Spec", "blk-ccbc4bbd4e874666b160415ea89c99e3")]
        [Trait("Spec", "blk-9f5a07d6d9304446bd4e51fe2fd8f994")]
        [Trait("Spec", "blk-6da4600392654036b9a6be1ea0c48f12")]
        public void DatabaseLevelSettingsSitBetweenGlobalAndEndpointForBothEndpoints()
        {
            var global = new PgGlobalPropertiesOptions { ApplicationName = "MyApp" };
            var main = new PgDbConnectionConfiguration { DatabaseKey = "MainDb", Host = "10.10.25.1", Database = "MainDb" };
            var other = new PgDbConnectionConfiguration { DatabaseKey = "OtherDb", ApplicationName = "MyOtherApp", Host = "10.10.25.2", Database = "OtherDb" };
            var databases = new PgDatabases(Options.Create(new PgDbConnectionOptions { PgDbConnections = new[] { main, other } }), Options.Create(global), Substitute.For<ILogger<PgDatabases>>());

            foreach (var connectionString in new[] { databases["MainDb"].Read.ConnectionString, databases["MainDb"].Write.ConnectionString })
            {
                var csb = Parse(connectionString);
                (csb.ApplicationName, csb.Database, csb.Host).Should().Be(("MyApp", "MainDb", "10.10.25.1"));
            }
            foreach (var connectionString in new[] { databases["OtherDb"].Read.ConnectionString, databases["OtherDb"].Write.ConnectionString })
            {
                var csb = Parse(connectionString);
                (csb.ApplicationName, csb.Database, csb.Host).Should().Be(("MyOtherApp", "OtherDb", "10.10.25.2"), "the database level overrides the global application name");
            }
        }

        [Fact]
        [Trait("Spec", "blk-ea77cfefbac44660a3cae162e0a78e5a")]
        public void DiagnosticSettingsAreAppliedWhenSupplied()
        {
            var connection = new PgConnectionConfiguration { Host = "h", IncludeErrorDetails = true, LogParameters = true };

            var csb = Parse(connection.GetConnectionString(null));

            csb.IncludeErrorDetail.Should().BeTrue();
            csb.LogParameters.Should().BeTrue();
        }

        [Fact]
        public void TheConnectTimeoutDefaultsToTwoSecondsUnlessALevelSetsIt()
        {
            var defaulted = new PgConnectionConfiguration { Host = "h" };
            var global = new PgGlobalPropertiesOptions { Timeout = 30 };
            var inherited = new PgConnectionConfiguration { Host = "h" };
            inherited.SetAmbientConfiguration(global, null, null, null);

            Parse(defaulted.GetConnectionString(null)).Timeout.Should().Be(2, "PgConnectionConfiguration declares a two-second default connect timeout, as the SQL Server configuration applies its own");
            Parse(inherited.GetConnectionString(null)).Timeout.Should().Be(30);
        }

        #endregion

        #region Description and invalidation

        [Fact]
        [Trait("Spec", "blk-56a08fb9534c49cc9aee62249510a730")]
        [Trait("Spec", "blk-900c98e61bb14a6d9dfc0f381df9729e")]
        [Trait("Spec", "blk-192c2f136e7a42cdbd93d8ed1fd79ee2")]
        public void TheConnectionDescriptionResolvesDatabaseAndHostThroughTheLevelsIgnoringEmptyValues()
        {
            var global = new PgGlobalPropertiesOptions { Database = "MainDb", Host = "10.10.25.1" };
            var connection = new PgConnectionConfiguration();
            connection.SetAmbientConfiguration(global, null, null, null);
            connection.ConnectionDescription.Should().Be("database MainDb on server 10.10.25.1");

            var shard = new PgShardConnectionOptions.PgShardConnectionConfiguration { Host = "shard-host", Database = "" };
            var overridden = new PgConnectionConfiguration { Database = "Tenant42" };
            overridden.SetAmbientConfiguration(global, null, null, shard);

            overridden.ConnectionDescription.Should().Be("database Tenant42 on server shard-host", "an empty Database at the shard level does not override the global value");
        }

        [Fact]
        [Trait("Spec", "blk-e01e74190009483f90b1ef64b92e45db")]
        [Trait("Spec", "blk-192c2f136e7a42cdbd93d8ed1fd79ee2")]
        public void AChangeToAnAmbientLayerOrALocalPropertyInvalidatesTheCachedStringAndDescription()
        {
            var global = new PgGlobalPropertiesOptions { Host = "host-a", Database = "db" };
            var connection = new PgConnectionConfiguration();
            connection.SetAmbientConfiguration(global, null, null, null);
            Parse(connection.GetConnectionString(null)).Host.Should().Be("host-a");
            connection.ConnectionDescription.Should().Be("database db on server host-a");

            global.Host = "host-b";
            Parse(connection.GetConnectionString(null)).Host.Should().Be("host-b");
            connection.ConnectionDescription.Should().Be("database db on server host-b");
            connection.Database = "local";
            Parse(connection.GetConnectionString(null)).Database.Should().Be("local");
            connection.ConnectionDescription.Should().Be("database local on server host-b");

            var replacement = new PgGlobalPropertiesOptions { Host = "host-c", Database = "db" };
            connection.SetAmbientConfiguration(replacement, null, null, null);
            Parse(connection.GetConnectionString(null)).Host.Should().Be("host-c");
            global.Host = "host-d";
            Parse(connection.GetConnectionString(null)).Host.Should().Be("host-c", "a layer no longer supplied has no effect");
        }

        public static IEnumerable<object[]> Settings()
            => typeof(PgConnectionPropertiesBase).GetProperties().Where(p => p.DeclaringType == typeof(PgConnectionPropertiesBase) && p.CanWrite).Select(p => new object[] { p.Name });

        [Theory]
        [Trait("Spec", "blk-6b1d2e8ff89840dca3ea01f1d99b52bf")]
        [Trait("Spec", "blk-dc178c0efbcf49e2ab40d9ebdb586181")]
        [Trait("Spec", "blk-34f279aa20204edca7179e8656bb9ec4")]
        [Trait("Spec", "blk-dab9facb7eef45fb9fa26a1aeae0fcd6")]
        [MemberData(nameof(Settings))]
        public void EverySettingRaisesPropertyChangedOnlyWhenTheValueDiffers(string propertyName)
        {
            var options = new PgGlobalPropertiesOptions();
            var property = typeof(PgConnectionPropertiesBase).GetProperty(propertyName)!;
            var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object value = underlying == typeof(string) ? "db.example.test" : underlying == typeof(int) ? 7 : underlying == typeof(bool) ? true : Enum.GetValues(underlying).GetValue(1)!;
            var raised = new List<string>();
            options.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

            property.SetValue(options, value);
            property.SetValue(options, value);

            raised.Should().Equal(propertyName);
            property.GetValue(options).Should().Be(value);
        }

        [Fact]
        [Trait("Spec", "blk-58a34b25eb0c4d1b920abbd651e4dfa9")]
        [Trait("Spec", "blk-7bf112c19c544d588890b45d7a9e8e65")]
        [Trait("Spec", "blk-60c9cadbd70b4c0981b30ea3a9226eae")]
        public void TheSettingSurfaceIsNullableAndTheGlobalOptionsAddNothing()
        {
            var declared = typeof(PgConnectionPropertiesBase).GetProperties().Where(p => p.DeclaringType == typeof(PgConnectionPropertiesBase)).ToList();

            declared.Select(p => p.Name).Should().Contain(new[] { "ApplicationName", "Database", "Host", "Port", "Timeout", "CommandTimeout", "MaxPoolSize", "Multiplexing", "SslMode", "ServerCompatibilityMode", "IncludeErrorDetails", "LogParameters" });
            declared.Should().OnlyContain(p => !p.PropertyType.IsValueType || Nullable.GetUnderlyingType(p.PropertyType) != null, "a null member is unspecified by the level");
            typeof(PgConnectionPropertiesBase).IsAbstract.Should().BeTrue();
            typeof(PgGlobalPropertiesOptions).GetProperties().Where(p => p.DeclaringType == typeof(PgGlobalPropertiesOptions)).Should().BeEmpty();
        }

        #endregion

        #region Option shapes

        [Fact]
        [Trait("Spec", "blk-31b4d9b5832743b0840f59fd89939886")]
        [Trait("Spec", "blk-724161b41bdb40ff84491716cf6d0ca9")]
        [Trait("Spec", "blk-a66606d46e7549218536082e5f1334b4")]
        [Trait("Spec", "blk-5e92a409b8d2468bb5a5233c5b3a5f23")]
        [Trait("Spec", "blk-6989f98e233742dc8b6704a4fb640fe3")]
        [Trait("Spec", "blk-ad5ce644b591498997c8ca4bf7e7b195")]
        [Trait("Spec", "blk-78027f6b8bfc4ef18f919a327340dca7")]
        [Trait("Spec", "blk-70f3f96071504a1991c99bd60fe9178b")]
        public void ShardSetOptionsExposeTheConfiguredArrayAndObjectsWithoutCopying()
        {
            var s1 = new PgShardConnectionOptions.PgShardConnectionConfiguration { ShardId = 1 };
            var r1 = new PgShardConnectionOptions.PgShardConnectionConfiguration();
            var w1 = new PgShardConnectionOptions.PgShardConnectionConfiguration();
            var orders = new PgShardConnectionOptions.PgShardConnectionsConfiguration { ShardSetName = "orders", DefaultShardId = 1, Shards = new[] { s1 }, Read = r1, Write = w1 };
            var inherit = new PgShardConnectionOptions.PgShardConnectionsConfiguration { ShardSetName = "Inherit" };
            var options = new PgShardConnectionOptions { PgShardSets = new[] { inherit, orders } };

            var view = ((IShardSetsConfigurationOptions)options).ShardSetsConfigInternal;

            view.Should().BeSameAs(options.PgShardSets);
            view!.Select(s => s!.ShardSetName).Should().Equal("Inherit", "orders");
            ((IShardSetConnectionsConfiguration)orders).ShardsConfigInternal.Should().BeSameAs(orders.Shards);
            ((IShardSetConnectionsConfiguration)orders).ReadConfigInternal.Should().BeSameAs(r1);
            ((IShardSetConnectionsConfiguration)orders).WriteConfigInternal.Should().BeSameAs(w1);
            ((IShardSetConnectionsConfiguration)orders).DefaultShardId.Should().Be(1);
        }

        [Fact]
        [Trait("Spec", "blk-5b269d324c6b48c4a40f596ae60a27e3")]
        [Trait("Spec", "blk-fc40e528574d4e4e955a844df31dcd7f")]
        [Trait("Spec", "blk-1db453c809c44182aeae2e188b28f2ee")]
        [Trait("Spec", "blk-7da052ec763441d8ba829f64bde519af")]
        [Trait("Spec", "blk-77629f39d38b451582ac97e64b2b88d8")]
        public void AShardConfigurationExposesItsAssignedEndpointsAndStartsWithTwoDistinctOnes()
        {
            var fresh = new PgShardConnectionOptions.PgShardConnectionConfiguration { ShardId = 7 };
            fresh.ReadConnection.Should().NotBeNull().And.NotBeSameAs(fresh.WriteConnection);
            fresh.WriteConnection.Should().NotBeNull();

            var read = new PgConnectionConfiguration();
            var write = new PgConnectionConfiguration();
            fresh.ReadConnection = read;
            fresh.WriteConnection = write;

            ((IShardConnectionConfiguration)fresh).ReadConnectionInternal.Should().BeSameAs(read);
            ((IShardConnectionConfiguration)fresh).WriteConnectionInternal.Should().BeSameAs(write);
            ((IShardConnectionConfiguration)fresh).ShardId.Should().Be(7);
        }

        [Fact]
        [Trait("Spec", "blk-3ebafd328e244ca783b773ec5461d015")]
        [Trait("Spec", "blk-87a90dc9f11b45ad845e05deb8b062f7")]
        [Trait("Spec", "blk-9d6f0b2fe756400d83e15d1808d6d774")]
        [Trait("Spec", "blk-dbf4e37dd8e4466391df45f277524793")]
        [Trait("Spec", "blk-b32f0bfb763d4197854f42ca24d44e64")]
        [Trait("Spec", "blk-66468152fe5c430b938abc9d20ee19e7")]
        public void DatabaseOptionsExposeTheSameEntriesAndDistinctDefaultEndpoints()
        {
            var entry = new PgDbConnectionConfiguration { DatabaseKey = "MainDb" };
            var options = new PgDbConnectionOptions { PgDbConnections = new[] { entry } };

            ((IDatabaseConfigurationOptions)options).DbConnectionsInternal.Should().BeSameAs(options.PgDbConnections);
            ReferenceEquals(entry.ReadConnection, entry.WriteConnection).Should().BeFalse();
            ReferenceEquals(entry.ReadConnection, entry.ReadConnectionInternal).Should().BeTrue();
            ReferenceEquals(entry.WriteConnection, entry.WriteConnectionInternal).Should().BeTrue();
            var replacement = new PgConnectionConfiguration();
            entry.WriteConnection = replacement;
            entry.WriteConnectionInternal.Should().BeSameAs(replacement);
            ((IDatabaseConnectionConfiguration)entry).DatabaseKey.Should().Be("MainDb");
        }

        #endregion
    }
}
