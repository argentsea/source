using Xunit;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ArgentSea;
using ArgentSea.Sql;
using FluentAssertions;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// Spec sections sql-server-configuration-options and sql-server-service-registration: binding the Sql options from
    /// configuration, what AddSqlServices registers, and the SqlDatabases / SqlShardSets construction surface.
    /// </summary>
    public class ConfigurationTests
    {
        private const string InheritedFromGlobal = "this is the value inherited from global configuration settings";
        private const string BuiltFromConfiguration = "the configuration file builds this connection string";

        [Fact]
        [Trait("Spec", "blk-410b15b970ed492eaa1e9a70634ac1eb")]
        [Trait("Spec", "blk-479ae8ee49ac44b1ad14507622428a36")]
        public void TestConfigurationOptions()
        {
			var bldr = new ConfigurationBuilder()
				.AddJsonFile("configurationsettings.json");

			var config = bldr.Build();
			var services = new ServiceCollection();
			services.AddOptions();

			services.Configure<SqlGlobalPropertiesOptions>(config.GetSection("SqlGlobalSettings"));
			services.Configure<SqlDbConnectionOptions>(config);
			services.Configure<SqlShardConnectionOptions>(config);

			var serviceProvider = services.BuildServiceProvider();

			var globalOptions = serviceProvider.GetRequiredService<IOptions<SqlGlobalPropertiesOptions>>();
			var sqlDbOptions = serviceProvider.GetRequiredService<IOptions<SqlDbConnectionOptions>>();
			var sqlShardOptions = serviceProvider.GetRequiredService<IOptions<SqlShardConnectionOptions>>();

			var globalData = globalOptions.Value;
            globalData.RetryCount.Should().Be(15, "that is the first value set in the configurationsettings.json configuration file");

			var sqlDbData = sqlDbOptions.Value;
            sqlDbData.SqlDbConnections!.Length.Should().Be(2, "two conections are defined in the configuration file.");
            sqlDbData.SqlDbConnections[0].ReadConnectionInternal!.SetAmbientConfiguration(globalData, null, null, sqlDbData.SqlDbConnections[0]);
            sqlDbData.SqlDbConnections[0].WriteConnectionInternal!.SetAmbientConfiguration(globalData, null, null, sqlDbData.SqlDbConnections[0]);
            sqlDbData.SqlDbConnections[1].ReadConnectionInternal!.SetAmbientConfiguration(globalData, null, null, sqlDbData.SqlDbConnections[1]);
            sqlDbData.SqlDbConnections[1].WriteConnectionInternal!.SetAmbientConfiguration(globalData, null, null, sqlDbData.SqlDbConnections[1]);

            sqlDbData.SqlDbConnections[0].ReadConnection!.GetConnectionString(null).ShouldHaveSqlSettings(InheritedFromGlobal,
                ("Data Source", "10.10.25.1"), ("Initial Catalog", "MainDb"), ("Connect Timeout", "5"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyApp"), ("Connect Retry Count", "0"));
            sqlDbData.SqlDbConnections[0].WriteConnection!.GetConnectionString(null).ShouldHaveSqlSettings(InheritedFromGlobal,
                ("Data Source", "10.10.25.5"), ("Initial Catalog", "MainDb"), ("Connect Timeout", "5"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyApp"), ("Connect Retry Count", "0"));
            sqlDbData.SqlDbConnections[1].ReadConnection!.GetConnectionString(null).ShouldHaveSqlSettings(InheritedFromGlobal,
                ("Data Source", "MyOtherServer"), ("Initial Catalog", "OtherDb"), ("Connect Timeout", "20"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyOtherApp"), ("Current Language", "English"), ("Connect Retry Count", "0"));
            sqlDbData.SqlDbConnections[1].WriteConnection!.GetConnectionString(null).ShouldHaveSqlSettings(InheritedFromGlobal,
                ("Data Source", "MyOtherServer"), ("Initial Catalog", "OtherDb"), ("Connect Timeout", "20"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyOtherApp"), ("Current Language", "English"), ("Connect Retry Count", "0"));

            var sqlShardData = sqlShardOptions.Value;
			sqlShardData.SqlShardSets!.Length.Should().Be(2, "there are two shard sets defined");
		}
		[Fact]
		[Trait("Spec", "blk-e59218f8b90e43e9bd48acb746ef44ab")]
		[Trait("Spec", "blk-69b9fc01f44741ea8103d0bbf067c088")]
		public void TestServiceBuilder()
		{
			var bldr = new ConfigurationBuilder()
				.AddJsonFile("configurationsettings.json");

			var config = bldr.Build();
			var services = new ServiceCollection();
			services.AddOptions();

			services.AddLogging();
            services.AddSqlServices(config);

            var serviceProvider = services.BuildServiceProvider();
            var globalOptions = serviceProvider.GetRequiredService<IOptions<SqlGlobalPropertiesOptions>>();
			var sqlDbOptions = serviceProvider.GetRequiredService<IOptions<SqlDbConnectionOptions>>();
			var sqlShardOptions = serviceProvider.GetRequiredService<IOptions<SqlShardConnectionOptions>>();
			var dbLogger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<SqlDatabases>>();

			var dbService = new SqlDatabases(sqlDbOptions, globalOptions, dbLogger);
			dbService.Count.Should().Be(2, "two connections are defined in the configuration file");
            dbService["MainDb"].Read.ConnectionString.ShouldHaveSqlSettings(InheritedFromGlobal,
                ("Data Source", "10.10.25.1"), ("Initial Catalog", "MainDb"), ("Connect Timeout", "5"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyApp"), ("Connect Retry Count", "0"));
            dbService["MainDb"].Write.ConnectionString.ShouldHaveSqlSettings(InheritedFromGlobal,
                ("Data Source", "10.10.25.5"), ("Initial Catalog", "MainDb"), ("Connect Timeout", "5"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyApp"), ("Connect Retry Count", "0"));
            dbService["OtherDb"].Read.ConnectionString.ShouldHaveSqlSettings(InheritedFromGlobal,
                ("Data Source", "MyOtherServer"), ("Initial Catalog", "OtherDb"), ("Connect Timeout", "20"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyOtherApp"), ("Current Language", "English"), ("Connect Retry Count", "0"));
            var shardLogger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ArgentSea.Sql.SqlShardSets>>();

            var shardService = new ArgentSea.Sql.SqlShardSets(sqlShardOptions, globalOptions, shardLogger);

            shardService.Count.Should().Be(2, "two shard sets are defined in the configuration file");
			shardService["Inherit"].Count.Should().Be(2, "the configuration file has two shard connections defined on shard set Set1");
			shardService["Explicit"].Count.Should().Be(2, "the configuration file has two shard connections defined on shard set Set2");

            shardService["Inherit"][0].Read.ConnectionString.ShouldHaveSqlSettings(BuiltFromConfiguration,
                ("Data Source", "10.10.23.20"), ("Failover Partner", "MyMirror"), ("Initial Catalog", "dbName2"), ("Connect Timeout", "5"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyOtherApp"), ("Application Intent", "ReadOnly"), ("Connect Retry Count", "0"));
            shardService["Inherit"][0].Write.ConnectionString.ShouldHaveSqlSettings(BuiltFromConfiguration,
                ("Data Source", "10.10.23.20"), ("Failover Partner", "MyMirror"), ("Initial Catalog", "dbName2"), ("Connect Timeout", "5"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyOtherApp"), ("Application Intent", "ReadWrite"), ("Connect Retry Count", "0"));
            shardService["Inherit"][1].Read.ConnectionString.ShouldHaveSqlSettings(BuiltFromConfiguration,
                ("Data Source", "MyOtherServer"), ("Initial Catalog", "dbName4"), ("Connect Timeout", "5"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyOtherApp"), ("Application Intent", "ReadOnly"), ("Connect Retry Count", "0"));
            shardService["Inherit"][1].Write.ConnectionString.ShouldHaveSqlSettings(BuiltFromConfiguration,
                ("Data Source", "MyOtherOtherServer"), ("Initial Catalog", "dbName3"), ("Connect Timeout", "5"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyOtherApp"), ("Application Intent", "ReadWrite"), ("Connect Retry Count", "0"));
            shardService["Explicit"][0].Read.ConnectionString.ShouldHaveSqlSettings(BuiltFromConfiguration,
                ("Data Source", "localhost"),
                ("Failover Partner", ""),
                ("Initial Catalog", "MyDb1"),
                ("Integrated Security", "False"),
                ("Persist Security Info", "False"),
                ("User ID", ""),
                ("Password", ""),
                ("Pooling", "True"),
                ("Min Pool Size", "0"),
                ("Max Pool Size", "100"),
                ("Multiple Active Result Sets", "False"),
                ("Replication", "False"),
                ("Connect Timeout", "2"),
                ("Encrypt", "False"),
                ("Trust Server Certificate", "True"),
                ("Load Balance Timeout", "0"),
                ("Packet Size", "8000"),
                ("Type System Version", "Latest"),
                ("Application Name", "MyWebApp2"),
                ("Current Language", "english"),
                ("Workstation ID", ""),
                ("Application Intent", "ReadWrite"),
                ("Multi Subnet Failover", "True"),
                ("Connect Retry Count", "0"));
            shardService["Explicit"][0].Write.ConnectionString.Should().Be(shardService["Explicit"][0].Read.ConnectionString, "the read and write connections should be identical");
            shardService["Explicit"][1].Read.ConnectionString.ShouldHaveSqlSettings(BuiltFromConfiguration,
                ("Data Source", "localhost"),
                ("Failover Partner", "rorriMyM"),
                ("Initial Catalog", "MyDb2"),
                ("Integrated Security", "False"),
                ("Persist Security Info", "False"),
                ("User ID", ""),
                ("Password", ""),
                ("Pooling", "True"),
                ("Min Pool Size", "0"),
                ("Max Pool Size", "100"),
                ("Multiple Active Result Sets", "False"),
                ("Replication", "False"),
                ("Connect Timeout", "2"),
                ("Encrypt", "False"),
                ("Trust Server Certificate", "True"),
                ("Load Balance Timeout", "0"),
                ("Packet Size", "8000"),
                ("Type System Version", "Latest"),
                ("Application Name", "MyWebApp3"),
                ("Current Language", "english"),
                ("Workstation ID", ""),
                ("Application Intent", "ReadOnly"),
                ("Multi Subnet Failover", "True"),
                ("Connect Retry Count", "0"));
            shardService["Explicit"][1].Write.ConnectionString.ShouldHaveSqlSettings(BuiltFromConfiguration,
                ("Data Source", "localhost"),
                ("Failover Partner", ""),
                ("Initial Catalog", "MyDb3"),
                ("Integrated Security", "False"),
                ("Persist Security Info", "False"),
                ("User ID", ""),
                ("Password", ""),
                ("Pooling", "True"),
                ("Min Pool Size", "0"),
                ("Max Pool Size", "100"),
                ("Multiple Active Result Sets", "False"),
                ("Replication", "False"),
                ("Connect Timeout", "2"),
                ("Encrypt", "False"),
                ("Trust Server Certificate", "True"),
                ("Load Balance Timeout", "0"),
                ("Packet Size", "8000"),
                ("Type System Version", "Latest"),
                ("Application Name", "MyWebApp4"),
                ("Current Language", "english"),
                ("Workstation ID", ""),
                ("Application Intent", "ReadWrite"),
                ("Multi Subnet Failover", "True"),
                ("Connect Retry Count", "0"));
        }
        [Fact]
        [Trait("Spec", "blk-9e743fb337144464aa02d23006b2fad3")]
        [Trait("Spec", "blk-9eb49adea69742b8bec908a4cd124aa2")]
        public void TestConfigChange()
        {
            var bldr = new ConfigurationBuilder()
                .AddJsonFile("configurationsettings.json");

            var config = bldr.Build();
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging();
            services.AddSqlServices(config);

            var serviceProvider = services.BuildServiceProvider();
            var globalOptions = serviceProvider.GetRequiredService<IOptions<SqlGlobalPropertiesOptions>>();
            var sqlDbOptions = serviceProvider.GetRequiredService<IOptions<SqlDbConnectionOptions>>();
            var sqlShardOptions = serviceProvider.GetRequiredService<IOptions<SqlShardConnectionOptions>>();
            var dbLogger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<SqlDatabases>>();

            var dbService = new SqlDatabases(sqlDbOptions, globalOptions, dbLogger);
            var shardLogger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<ArgentSea.Sql.SqlShardSets>>();
            var shardService = new SqlShardSets(sqlShardOptions, globalOptions, shardLogger);

            dbService["MainDb"].Read.ConnectionString.ShouldHaveSqlSettings(InheritedFromGlobal,
                ("Data Source", "10.10.25.1"), ("Initial Catalog", "MainDb"), ("Connect Timeout", "5"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyApp"), ("Current Language", ""), ("Connect Retry Count", "0"));
            shardService["Inherit"][0].Read.ConnectionString.ShouldHaveSqlSettings(BuiltFromConfiguration,
                ("Data Source", "10.10.23.20"), ("Failover Partner", "MyMirror"), ("Initial Catalog", "dbName2"), ("Connect Timeout", "5"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyOtherApp"), ("Current Language", ""), ("Application Intent", "ReadOnly"), ("Connect Retry Count", "0"));

            globalOptions.Value.CurrentLanguage = "Pigeon";
            dbService["MainDb"].Read.ConnectionString.ShouldHaveSqlSettings("a changed global setting is rendered on the next read",
                ("Data Source", "10.10.25.1"), ("Initial Catalog", "MainDb"), ("Connect Timeout", "5"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyApp"), ("Current Language", "Pigeon"), ("Connect Retry Count", "0"));
            shardService["Inherit"][0].Read.ConnectionString.ShouldHaveSqlSettings("a changed global setting is rendered on the next read",
                ("Data Source", "10.10.23.20"), ("Failover Partner", "MyMirror"), ("Initial Catalog", "dbName2"), ("Connect Timeout", "5"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyOtherApp"), ("Current Language", "Pigeon"), ("Application Intent", "ReadOnly"), ("Connect Retry Count", "0"));

            sqlDbOptions.Value.SqlDbConnections![0].PacketSize = 16384;
            sqlShardOptions.Value.SqlShardSets![0].PacketSize = 16384;
            dbService["MainDb"].Read.ConnectionString.ShouldHaveSqlSettings("a changed database setting is rendered on the next read",
                ("Data Source", "10.10.25.1"), ("Initial Catalog", "MainDb"), ("Connect Timeout", "5"), ("Packet Size", "16384"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyApp"), ("Current Language", "Pigeon"), ("Connect Retry Count", "0"));
            shardService["Inherit"][0].Read.ConnectionString.ShouldHaveSqlSettings("a changed shard-set setting is rendered on the next read",
                ("Data Source", "10.10.23.20"), ("Failover Partner", "MyMirror"), ("Initial Catalog", "dbName2"), ("Connect Timeout", "5"), ("Packet Size", "16384"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyOtherApp"), ("Current Language", "Pigeon"), ("Application Intent", "ReadOnly"), ("Connect Retry Count", "0"));
            sqlShardOptions.Value.SqlShardSets![0].PacketSize = 4096;
            shardService["Inherit"][0].Read.ConnectionString.ShouldHaveSqlSettings("a second change to the shard-set setting is rendered on the next read",
                ("Data Source", "10.10.23.20"), ("Failover Partner", "MyMirror"), ("Initial Catalog", "dbName2"), ("Connect Timeout", "5"), ("Packet Size", "4096"), ("Type System Version", "SQL Server 2012"), ("Application Name", "MyOtherApp"), ("Current Language", "Pigeon"), ("Application Intent", "ReadOnly"), ("Connect Retry Count", "0"));

        }

        // ---- Spec section sql-server-service-registration --------------------------------------------------------------

        private static IConfiguration LoadConfiguration()
            => new ConfigurationBuilder().AddJsonFile("configurationsettings.json").Build();

        [Fact]
        [Trait("Spec", "blk-6bcb9f2f7b374b6994fa127398982411")]
        [Trait("Spec", "blk-e59218f8b90e43e9bd48acb746ef44ab")]
        [Trait("Spec", "blk-69b9fc01f44741ea8103d0bbf067c088")]
        public void AddSqlServicesRegistersTheOptionsAndSingletonsAndReturnsTheSameCollection()
        {
            var savedExtension = QueryStatement.Extension;
            var config = LoadConfiguration();
            config.GetSection("SqlGlobalSettings").Exists().Should().BeTrue("the configuration carries a SqlGlobalSettings section");
            var services = new ServiceCollection();
            services.AddLogging();

            var returned = services.AddSqlServices(config);

            returned.Should().BeSameAs(services, "the same IServiceCollection is returned");
            QueryStatement.Extension.Should().Be(savedExtension, "unlike AddPgServices, AddSqlServices leaves the shared statement extension alone");
            services.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<SqlGlobalPropertiesOptions>), "SqlGlobalPropertiesOptions is bound");
            services.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<SqlDbConnectionOptions>), "SqlDbConnectionOptions is bound");
            services.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<SqlShardConnectionOptions>), "SqlShardConnectionOptions is bound");
            services.Should().ContainSingle(d => d.ServiceType == typeof(SqlDatabases))
                .Which.Should().Match<ServiceDescriptor>(d => d.Lifetime == ServiceLifetime.Singleton && d.ImplementationType == typeof(SqlDatabases));
            services.Should().ContainSingle(d => d.ServiceType == typeof(SqlShardSets))
                .Which.Should().Match<ServiceDescriptor>(d => d.Lifetime == ServiceLifetime.Singleton && d.ImplementationType == typeof(SqlShardSets));

            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IOptions<SqlGlobalPropertiesOptions>>().Value.RetryCount.Should().Be(15, "the SqlGlobalSettings section is bound");
            provider.GetRequiredService<IOptions<SqlDbConnectionOptions>>().Value.SqlDbConnections.Should().HaveCount(2, "the SqlDbConnections array is bound from the root");
            provider.GetRequiredService<IOptions<SqlShardConnectionOptions>>().Value.SqlShardSets.Should().HaveCount(2, "the SqlShardSets array is bound from the root");
            var databases = provider.GetRequiredService<SqlDatabases>();
            databases.Should().BeSameAs(provider.GetRequiredService<SqlDatabases>(), "SqlDatabases is a singleton");
            databases.Count.Should().Be(2);
            var shardSets = provider.GetRequiredService<SqlShardSets>();
            shardSets.Should().BeSameAs(provider.GetRequiredService<SqlShardSets>(), "SqlShardSets is a singleton");
            shardSets.Count.Should().Be(2);
        }

        [Fact]
        [Trait("Spec", "blk-2c0c040b7484468aa5bf22a288b0a407")]
        [Trait("Spec", "blk-1b2c97ea8ab842168a6f728a5aff6a13")]
        public void SqlDatabasesAndSqlShardSetsHardWireTheSqlClientProviderFactoryInTheirPublicConstructor()
        {
            typeof(SqlDatabases).BaseType.Should().Be(typeof(DatabasesBase<SqlDbConnectionOptions>));
            typeof(SqlShardSets).BaseType.Should().Be(typeof(ShardSetsBase<SqlShardConnectionOptions>));

            foreach (var facade in new[] { typeof(SqlDatabases), typeof(SqlShardSets) })
            {
                var publicConstructor = facade.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().ContainSingle().Subject;
                publicConstructor.GetParameters().Select(p => p.ParameterType).Should().NotContain(typeof(IDataProviderServiceFactory),
                    "{0}'s public construction boundary takes options and a logger only; the SqlClient factory is supplied inside", facade.Name);
                // The spec block says the facade never accepts a factory; the code also has a protected constructor that takes one, which
                // exists for derived test doubles only (nothing on the public surface or in the container can reach it). The public surface
                // is what the block describes, so the test pins that and the protected hook is noted here for the spec to absorb.
                facade.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Should().ContainSingle()
                    .Which.Should().Match<ConstructorInfo>(c => c.IsFamily && c.GetParameters().Any(p => p.ParameterType == typeof(IDataProviderServiceFactory)));
            }

            var config = LoadConfiguration();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSqlServices(config);
            using var provider = services.BuildServiceProvider();

            ProviderFactoryOf(provider.GetRequiredService<SqlDatabases>(), typeof(DatabasesBase<SqlDbConnectionOptions>))
                .Should().BeOfType<ArgentSea.Sql.DataProviderServiceFactory>("every container-resolved SqlDatabases is bound to the SqlClient provider");
            ProviderFactoryOf(provider.GetRequiredService<SqlShardSets>(), typeof(ShardSetsBase<SqlShardConnectionOptions>))
                .Should().BeOfType<ArgentSea.Sql.DataProviderServiceFactory>("every container-resolved SqlShardSets is bound to the SqlClient provider");
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
