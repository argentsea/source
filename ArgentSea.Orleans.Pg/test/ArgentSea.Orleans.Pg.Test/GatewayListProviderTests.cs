using System.Net;
using System.Reflection;
using ArgentSea.Pg;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Orleans.Configuration;

namespace ArgentSea.Orleans.Pg.Test;

/// <summary>The PostgreSQL ArgentSeaGatewayListProvider: connection resolution, its default query and the row mapping.</summary>
public class GatewayListProviderTests
{
    private static T Field<T>(ArgentSeaGatewayListProvider provider, string name)
        => (T)typeof(ArgentSeaGatewayListProvider).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(provider)!;

    private static ArgentSeaGatewayListProvider Create(PgDbConnectionOptions dbOptions, ClusterClientDbOptions clientOptions, PgGlobalPropertiesOptions? globals = null)
        => new(Options.Create(dbOptions), Options.Create(globals ?? new PgGlobalPropertiesOptions()), Options.Create(clientOptions),
            Options.Create(new ClusterOptions { ClusterId = "TestCluster" }), NullLogger<ArgentSeaGatewayListProvider>.Instance);

    [Fact]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-cbb4c09c00364a0cbf014fcf1c25a56e")]
    public void TheReadConnectionOfTheConfiguredDatabaseKeyIsUsedWithGlobalSettingsApplied()
    {
        var dbOptions = new PgDbConnectionOptions
        {
            PgDbConnections =
            [
                new PgDbConnectionConfiguration
                {
                    DatabaseKey = "Common",
                    Database = "orleans_common",
                    ReadConnection = new PgConnectionConfiguration { Host = "read-replica" },
                    WriteConnection = new PgConnectionConfiguration { Host = "primary" },
                },
            ],
        };

        var provider = Create(dbOptions, new ClusterClientDbOptions { DatabaseKey = "Common" }, new PgGlobalPropertiesOptions { UserName = "globalUser", Password = "globalPwd" });

        var csb = new NpgsqlConnectionStringBuilder(Field<string>(provider, "_readConnectionString"));
        csb.Host.Should().Be("read-replica", "the read connection is used");
        csb.Database.Should().Be("orleans_common", "the database-level setting is inherited");
        csb.Username.Should().Be("globalUser", "the global credentials are inherited");
    }

    [Fact]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-cbb4c09c00364a0cbf014fcf1c25a56e")]
    public void AnExplicitConnectionStringIsUsedVerbatimAndAnUnknownKeyFails()
    {
        var provider = Create(new PgDbConnectionOptions { PgDbConnections = [] }, new ClusterClientDbOptions { DatabaseKey = "Common", ConnectionString = "Host=explicit;Database=Explicit" });
        Field<string>(provider, "_readConnectionString").Should().Be("Host=explicit;Database=Explicit");

        var act = () => Create(new PgDbConnectionOptions { PgDbConnections = [] }, new ClusterClientDbOptions { DatabaseKey = "Missing" });
        act.Should().Throw<ArgumentException>().WithMessage("*Missing*");
    }

    [Fact]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-cbb4c09c00364a0cbf014fcf1c25a56e")]
    public void WithoutAConfiguredQueryThePackagesGatewayListFunctionIsUsed()
    {
        var provider = Create(new PgDbConnectionOptions(), new ClusterClientDbOptions { DatabaseKey = "Common", ConnectionString = "Host=x", MaxRefreshInterval = TimeSpan.FromSeconds(42) });

        Field<Query>(provider, "_query").Sql.Should().Be(Queries.OrleansClusterGatewayList.Sql);
        provider.MaxStaleness.Should().Be(TimeSpan.FromSeconds(42));
        provider.IsUpdatable.Should().BeTrue();

        var custom = new PgFunctionQuery("Custom", "SELECT * FROM rdr.my_gateways(@p_cluster_id);");
        Field<Query>(Create(new PgDbConnectionOptions(), new ClusterClientDbOptions { DatabaseKey = "Common", ConnectionString = "Host=x", GatewayListQuery = custom }), "_query")
            .Should().BeSameAs(custom, "a configured query wins");
    }

    [Fact]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-cbb4c09c00364a0cbf014fcf1c25a56e")]
    public void GatewayUrisAreBuiltFromEveryAddressPortGenerationRowInOrder()
    {
        using var two = new FakeDbDataReader([new FakeResultSet(["address", "proxy_port", "generation"],
        [
            [IPAddress.Parse("10.0.0.2").GetAddressBytes(), 30001, 8],
            [IPAddress.Parse("fe80::1").GetAddressBytes(), 30002, 9],
        ])]);

        ArgentSeaGatewayListProvider.ReadGatewayUris(two).Should().Equal(
            SiloAddress.New(new IPEndPoint(IPAddress.Parse("10.0.0.2"), 30001), 8).ToGatewayUri(),
            SiloAddress.New(new IPEndPoint(IPAddress.Parse("fe80::1"), 30002), 9).ToGatewayUri());

        using var none = new FakeDbDataReader([new FakeResultSet(["address", "proxy_port", "generation"], [])]);
        ArgentSeaGatewayListProvider.ReadGatewayUris(none).Should().BeEmpty();
    }
}
