﻿using Xunit;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ArgentSea;
using ArgentSea.Pg;
using FluentAssertions;

namespace ArgentSea.Pg.Test
{
    /// <summary>
    /// Spec sections postgres-configuration-options and postgres-service-registration: binding the Pg options from configuration,
    /// what AddPgServices registers, and the PgDatabases / PgShardSets construction surface. AddPgServices writes the shared
    /// QueryStatement.Extension static, so the class shares a collection with <see cref="PgQueryStatementTests"/>.
    /// </summary>
    [Collection("QueryStatement statics")]
    public class ConfigurationTests
    {
        private const string InheritedFromGlobal = "this is the value inherited from global configuration settings";
        private const string OverridesGlobal = "this is the value that overrides the global setting";
        private const string BuiltFromConfiguration = "the configuration file builds this connection string";

        [Fact]
        [Trait("Spec", "blk-fcdaf3764077419aad4f081d0caa3e8e")]
        [Trait("Spec", "blk-6da4600392654036b9a6be1ea0c48f12")]
        public void TestConfigurationOptions()
        {
            var bldr = new ConfigurationBuilder()
                .AddJsonFile("configurationsettings.json");

            var config = bldr.Build();
            var services = new ServiceCollection();
            services.AddOptions();

            services.Configure<PgGlobalPropertiesOptions>(config.GetSection("PgGlobalSettings"));
            services.Configure<PgDbConnectionOptions>(config);
            services.Configure<PgShardConnectionOptions>(config);

            var serviceProvider = services.BuildServiceProvider();

            var globalOptions = serviceProvider.GetRequiredService<IOptions<PgGlobalPropertiesOptions>>();
            var sqlDbOptions = serviceProvider.GetRequiredService<IOptions<PgDbConnectionOptions>>();
            var sqlShardOptions = serviceProvider.GetRequiredService<IOptions<PgShardConnectionOptions>>();

            var globalData = globalOptions.Value;
            globalData.RetryCount.Should().Be(15, "that is the first value set in the configurationsettings.json configuration file");

            var pgDbData = sqlDbOptions.Value;
            pgDbData.PgDbConnections!.Length.Should().Be(2, "two conections are defined in the configuration file.");
            pgDbData.PgDbConnections[0].ReadConnectionInternal!.SetAmbientConfiguration(globalData, null, null, pgDbData.PgDbConnections[0]);
            pgDbData.PgDbConnections[0].WriteConnectionInternal!.SetAmbientConfiguration(globalData, null, null, pgDbData.PgDbConnections[0]);
            pgDbData.PgDbConnections[1].ReadConnectionInternal!.SetAmbientConfiguration(globalData, null, null, pgDbData.PgDbConnections[1]);
            pgDbData.PgDbConnections[1].WriteConnectionInternal!.SetAmbientConfiguration(globalData, null, null, pgDbData.PgDbConnections[1]);

            pgDbData.PgDbConnections[0].ReadConnection!.GetConnectionString(null).ShouldHaveNpgsqlSettings(InheritedFromGlobal,
                ("Application Name", "MyApp"), ("Database", "MainDb"), ("Host", "10.10.25.1"));
            pgDbData.PgDbConnections[0].WriteConnection!.GetConnectionString(null).ShouldHaveNpgsqlSettings(InheritedFromGlobal,
                ("Application Name", "MyApp"), ("Database", "MainDb"), ("Host", "10.10.25.1"));
            pgDbData.PgDbConnections[1].ReadConnection!.GetConnectionString(null).ShouldHaveNpgsqlSettings(OverridesGlobal,
                ("Application Name", "MyOtherApp"), ("Database", "OtherDb"), ("Host", "10.10.25.2"));
            pgDbData.PgDbConnections[1].WriteConnection!.GetConnectionString(null).ShouldHaveNpgsqlSettings(OverridesGlobal,
                ("Application Name", "MyOtherApp"), ("Database", "OtherDb"), ("Host", "10.10.25.2"));

            var pgShardData = sqlShardOptions.Value;
            pgShardData.PgShardSets!.Length.Should().Be(2, "there are two shard sets defined");
        }
        [Fact]
        [Trait("Spec", "blk-2459809b36504a51877b8bef73dbdd35")]
        [Trait("Spec", "blk-6ceac425143f4505b29c166145cc23f5")]
        [Trait("Spec", "blk-d9b43f91c5724d29a6d2a02c160b7924")]
        public void TestServiceBuilder()
        {
            var bldr = new ConfigurationBuilder()
                .AddJsonFile("configurationsettings.json");

            var config = bldr.Build();
            var services = new ServiceCollection();
            services.AddOptions();

            services.AddLogging();
            services.AddPgServices(config);

            var serviceProvider = services.BuildServiceProvider();
            var globalOptions = serviceProvider.GetRequiredService<IOptions<PgGlobalPropertiesOptions>>();
            var pgDbOptions = serviceProvider.GetRequiredService<IOptions<PgDbConnectionOptions>>();
            var pgShardOptions = serviceProvider.GetRequiredService<IOptions<PgShardConnectionOptions>>();
            var dbLogger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<PgDatabases>>();

            var dbService = new PgDatabases(pgDbOptions, globalOptions, dbLogger);
            dbService.Count.Should().Be(2, "two connections are defined in the configuration file");
            dbService["MainDb"].Read.ConnectionString.ShouldHaveNpgsqlSettings(InheritedFromGlobal,
                ("Application Name", "MyApp"), ("Database", "MainDb"), ("Host", "10.10.25.1"));
            dbService["OtherDb"].Read.ConnectionString.ShouldHaveNpgsqlSettings(OverridesGlobal,
                ("Application Name", "MyOtherApp"), ("Database", "OtherDb"), ("Host", "10.10.25.2"));
            var shardLogger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ArgentSea.Pg.PgShardSets>>();

            var shardService = new ArgentSea.Pg.PgShardSets(pgShardOptions, globalOptions, shardLogger);

            shardService.Count.Should().Be(2, "two shard sets are defined in the configuration file");
            shardService["Inherit"].Count.Should().Be(2, "the configuration file has two shard connections defined on shard set Set1");
            shardService["Explicit"].Count.Should().Be(2, "the configuration file has two shard connections defined on shard set Set2");

            shardService["Inherit"][0].Read.ConnectionString.ShouldHaveNpgsqlSettings(BuiltFromConfiguration,
                ("Application Name", "Inheritance"), ("Host", "10.10.25.3"));
            shardService["Inherit"][0].Write.ConnectionString.ShouldHaveNpgsqlSettings(BuiltFromConfiguration,
                ("Application Name", "Inheritance"), ("Host", "10.10.25.3"));
            shardService["Inherit"][1].Read.ConnectionString.ShouldHaveNpgsqlSettings(BuiltFromConfiguration,
                ("Application Name", "Inheritance"), ("Host", "10.10.25.4"));
            shardService["Inherit"][1].Write.ConnectionString.ShouldHaveNpgsqlSettings(BuiltFromConfiguration,
                ("Application Name", "Inheritance"), ("Host", "10.10.1.5"));
            shardService["Explicit"][0].Read.ConnectionString.ShouldHaveNpgsqlSettings(BuiltFromConfiguration,
                ("Application Name", "MyWebApp"),
                ("Password", "pwd2345"),
                ("Username", "user"),
                ("Auto Prepare Min Usages", "6"),
                ("Cancellation Timeout", "22"),
                ("Check Certificate Revocation", "True"),
                ("Client Encoding", "UTF16"),
                ("Command Timeout", "16"),
                ("Connection Idle Lifetime", "301"),
                ("Connection Lifetime", "30"),
                ("Connection Pruning Interval", "11"),
                ("Database", "MyDb1"),
                ("Encoding", "UTF16"),
                ("Enlist", "False"),
                ("Host", "10.10.25.6"),
                ("Include Error Detail", "False"),
                ("Include Realm", "False"),
                ("Keepalive", "100"),
                ("Kerberos Service Name", "test"),
                ("Load Table Composites", "True"),
                ("Log Parameters", "False"),
                ("Max Auto Prepare", "1"),
                ("Maximum Pool Size", "99"),
                ("Minimum Pool Size", "2"),
                ("No Reset On Close", "True"),
                ("Options", "123"),
                ("Passfile", @"c:\passfile.pwd"),
                ("Persist Security Info", "False"),
                ("Pooling", "False"),
                ("Port", "5433"),
                ("Read Buffer Size", "4096"),
                ("Root Certificate", "cert1"),
                ("Search Path", ""),
                ("Server Compatibility Mode", "None"),
                ("Socket Receive Buffer Size", "4096"),
                ("Socket Send Buffer Size", "4096"),
                ("SSL Mode", "Disable"),
                ("TCP Keepalive", "False"),
                ("TCP Keepalive Interval", "102"),
                ("TCP Keepalive Time", "100"),
                ("Timeout", "16"),
                ("Timezone", "America/Chicago"),
                ("Write Buffer Size", "4096"),
                ("Write Coalescing Buffer Threshold Bytes", "2000"));
            shardService["Explicit"][0].Write.ConnectionString.Should().Be(shardService["Explicit"][0].Read.ConnectionString, "the read and write connections should be the same");
            shardService["Explicit"][1].Read.ConnectionString.ShouldHaveNpgsqlSettings(BuiltFromConfiguration,
                ("Application Name", "MyWebApp3"),
                ("Password", "pwd2345"),
                ("Username", "user1"),
                ("Auto Prepare Min Usages", "6"),
                ("Cancellation Timeout", "23"),
                ("Check Certificate Revocation", "True"),
                ("Client Encoding", "UTF16"),
                ("Command Timeout", "16"),
                ("Connection Idle Lifetime", "302"),
                ("Connection Lifetime", "31"),
                ("Connection Pruning Interval", "11"),
                ("Database", "MyDb1"),
                ("Encoding", "UTF16"),
                ("Enlist", "False"),
                ("Host", "10.10.25.6"),
                ("Include Error Detail", "True"),
                ("Include Realm", "False"),
                ("Keepalive", "30"),
                ("Kerberos Service Name", "test"),
                ("Load Table Composites", "True"),
                ("Log Parameters", "True"),
                ("Max Auto Prepare", "1"),
                ("Maximum Pool Size", "99"),
                ("Minimum Pool Size", "2"),
                ("Multiplexing", "False"),
                ("No Reset On Close", "True"),
                ("Options", "345"),
                ("Passfile", @"c:\passfile.pwd"),
                ("Persist Security Info", "False"),
                ("Pooling", "False"),
                ("Port", "5433"),
                ("Read Buffer Size", "4096"),
                ("Root Certificate", "cert1"),
                ("Search Path", ""),
                ("Server Compatibility Mode", "NoTypeLoading"),
                ("Socket Receive Buffer Size", "4096"),
                ("Socket Send Buffer Size", "4096"),
                ("SSL Mode", "Disable"),
                ("TCP Keepalive", "False"),
                ("TCP Keepalive Interval", "101"),
                ("TCP Keepalive Time", "101"),
                ("Timeout", "16"),
                ("Timezone", "America/Chicago"),
                ("Write Buffer Size", "4096"),
                ("Write Coalescing Buffer Threshold Bytes", "3000"));
            shardService["Explicit"][1].Write.ConnectionString.ShouldHaveNpgsqlSettings(BuiltFromConfiguration,
                ("Application Name", "MyWebApp5"),
                ("Password", "pwd3456"),
                ("Username", "user2"),
                ("Auto Prepare Min Usages", "7"),
                ("Cancellation Timeout", "22"),
                ("Check Certificate Revocation", "False"),
                ("Client Encoding", "UTF8"),
                ("Command Timeout", "17"),
                ("Connection Idle Lifetime", "302"),
                ("Connection Lifetime", "33"),
                ("Connection Pruning Interval", "12"),
                ("Database", "MyDb3"),
                ("Encoding", "UTF8"),
                ("Enlist", "True"),
                ("Host", "10.10.25.7"),
                ("Include Error Detail", "False"),
                ("Include Realm", "True"),
                ("Keepalive", "20"),
                ("Kerberos Service Name", "test2"),
                ("Load Table Composites", "False"),
                ("Log Parameters", "False"),
                ("Max Auto Prepare", "2"),
                ("Maximum Pool Size", "98"),
                ("Minimum Pool Size", "3"),
                ("No Reset On Close", "False"),
                ("Options", "345"),
                ("Passfile", @"c:\passfile.pwd"),
                ("Persist Security Info", "True"),
                ("Pooling", "True"),
                ("Port", "5434"),
                ("Read Buffer Size", "8192"),
                ("Root Certificate", "cert1"),
                ("Search Path", ""),
                ("Server Compatibility Mode", "Redshift"),
                ("Socket Receive Buffer Size", "8192"),
                ("Socket Send Buffer Size", "8192"),
                ("SSL Mode", "Prefer"),
                ("TCP Keepalive", "True"),
                ("TCP Keepalive Interval", "100"),
                ("TCP Keepalive Time", "102"),
                ("Timeout", "17"),
                ("Timezone", "America/Los_Angeles"),
                ("Write Buffer Size", "8192"),
                ("Write Coalescing Buffer Threshold Bytes", "3000"));
        }

        // ---- Spec section postgres-service-registration ----------------------------------------------------------------

        private static IConfiguration LoadConfiguration()
            => new ConfigurationBuilder().AddJsonFile("configurationsettings.json").Build();

        [Fact]
        [Trait("Spec", "blk-2459809b36504a51877b8bef73dbdd35")]
        [Trait("Spec", "blk-4c16c1882a834cd79a5b0690e2cd911d")]
        public void AddPgServicesRegistersTheOptionsAndSingletonsAndReturnsTheSameCollection()
        {
            var savedExtension = QueryStatement.Extension;
            try
            {
                var config = LoadConfiguration();
                var services = new ServiceCollection();
                services.AddLogging();

                var returned = services.AddPgServices(config);

                returned.Should().BeSameAs(services, "the same IServiceCollection is returned");
                QueryStatement.Extension.Should().Be("psql");
                services.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<PgGlobalPropertiesOptions>), "PgGlobalPropertiesOptions is bound");
                services.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<PgDbConnectionOptions>), "PgDbConnectionOptions is bound");
                services.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<PgShardConnectionOptions>), "PgShardConnectionOptions is bound");
                services.Should().ContainSingle(d => d.ServiceType == typeof(PgDatabases))
                    .Which.Should().Match<ServiceDescriptor>(d => d.Lifetime == ServiceLifetime.Singleton && d.ImplementationType == typeof(PgDatabases));
                services.Should().ContainSingle(d => d.ServiceType == typeof(PgShardSets))
                    .Which.Should().Match<ServiceDescriptor>(d => d.Lifetime == ServiceLifetime.Singleton && d.ImplementationType == typeof(PgShardSets));

                using var provider = services.BuildServiceProvider();
                provider.GetRequiredService<IOptions<PgGlobalPropertiesOptions>>().Value.RetryCount.Should().Be(15, "the PgGlobalSettings section is bound");
                provider.GetRequiredService<IOptions<PgDbConnectionOptions>>().Value.PgDbConnections.Should().HaveCount(2, "the PgDbConnections array is bound from the root");
                provider.GetRequiredService<IOptions<PgShardConnectionOptions>>().Value.PgShardSets.Should().HaveCount(2, "the PgShardSets array is bound from the root");
                var databases = provider.GetRequiredService<PgDatabases>();
                databases.Should().BeSameAs(provider.GetRequiredService<PgDatabases>(), "PgDatabases is a singleton");
                databases.Count.Should().Be(2);
                var shardSets = provider.GetRequiredService<PgShardSets>();
                shardSets.Should().BeSameAs(provider.GetRequiredService<PgShardSets>(), "PgShardSets is a singleton");
                shardSets.Count.Should().Be(2);
            }
            finally
            {
                QueryStatement.Extension = savedExtension;
            }
        }

        [Fact]
        [Trait("Spec", "blk-4c16c1882a834cd79a5b0690e2cd911d")]
        [Trait("Spec", "blk-52337c098da14e98a3542a9647ac606c")]
        [Trait("Spec", "blk-8dc82c304f2f4369920bc58b7d1c98a3")]
        public void AddPgServicesAlwaysResetsTheSharedQueryStatementExtensionToPsql()
        {
            var savedExtension = QueryStatement.Extension;
            try
            {
                var config = LoadConfiguration();
                QueryStatement.Extension = "left-by-an-earlier-caller";

                new ServiceCollection().AddPgServices(config);
                QueryStatement.Extension.Should().Be("psql", "the shared static is overwritten regardless of the value it held before the call");

                QueryStatement.Extension = "sql";
                new ServiceCollection().AddPgServices(config);
                QueryStatement.Extension.Should().Be("psql", "every call sets it again");

                PgQueryStatement.Extension.Should().Be("psql", "the provider-scoped extension is separate state");
                SharedExtensionIsAStaticOf(typeof(QueryStatement));
            }
            finally
            {
                QueryStatement.Extension = savedExtension;
            }
        }

        private static void SharedExtensionIsAStaticOf(Type owner)
        {
            owner.GetProperty("Extension", BindingFlags.Public | BindingFlags.Static).Should().NotBeNull("Extension is a process-wide static of the QueryStatement type");
            typeof(PgDatabases).GetProperty("Extension").Should().BeNull("the shared value is not scoped to PgDatabases");
            typeof(PgShardSets).GetProperty("Extension").Should().BeNull("the shared value is not scoped to PgShardSets");
        }

        [Fact]
        [Trait("Spec", "blk-6ceac425143f4505b29c166145cc23f5")]
        [Trait("Spec", "blk-d9b43f91c5724d29a6d2a02c160b7924")]
        public void PgDatabasesAndPgShardSetsCloseTheBaseCollectionsOverThePostgresProviderFactoryAndAddNoMembers()
        {
            typeof(PgDatabases).BaseType.Should().Be(typeof(DatabasesBase<PgDbConnectionOptions>));
            typeof(PgShardSets).BaseType.Should().Be(typeof(ShardSetsBase<PgShardConnectionOptions>));

            foreach (var facade in new[] { typeof(PgDatabases), typeof(PgShardSets) })
            {
                var declared = facade.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                declared.Should().OnlyContain(m => m is ConstructorInfo, "{0} adds no members of its own beyond its constructors", facade.Name);
                var constructors = declared.OfType<ConstructorInfo>().ToList();
                constructors.Where(c => c.IsPublic).Should().ContainSingle()
                    .Which.GetParameters().Select(p => p.ParameterType).Should().NotContain(typeof(IDataProviderServiceFactory),
                        "{0}'s public constructor supplies the provider factory itself instead of taking one", facade.Name);
                // As for SqlDatabases/SqlShardSets, a protected constructor lets derived classes and tests substitute the provider.
                constructors.Where(c => !c.IsPublic).Should().ContainSingle()
                    .Which.Should().Match<ConstructorInfo>(c => c.IsFamily && c.GetParameters().Any(p => p.ParameterType == typeof(IDataProviderServiceFactory)),
                        "{0}'s only other constructor is the protected provider-substitution seam", facade.Name);
            }

            var config = LoadConfiguration();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPgServices(config);
            using var provider = services.BuildServiceProvider();

            var databases = provider.GetRequiredService<PgDatabases>();
            ProviderFactoryOf(databases, typeof(DatabasesBase<PgDbConnectionOptions>)).Should().BeOfType<ArgentSea.Pg.DataProviderServiceFactory>();
            var shardSets = provider.GetRequiredService<PgShardSets>();
            ProviderFactoryOf(shardSets, typeof(ShardSetsBase<PgShardConnectionOptions>)).Should().BeOfType<ArgentSea.Pg.DataProviderServiceFactory>();
        }

        /// <summary>The IDataProviderServiceFactory a facade handed to its base constructor (the base keeps it in a private field).</summary>
        private static object? ProviderFactoryOf(object facade, Type baseType)
        {
            var field = baseType.GetField("_dataProviderServices", BindingFlags.NonPublic | BindingFlags.Instance);
            field.Should().NotBeNull("{0} keeps the provider factory in _dataProviderServices", baseType.Name);
            return field!.GetValue(facade);
        }
    }
}
