using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ArgentSea.Sql;
using Orleans.Configuration;
using System.Reflection;

namespace ArgentSea.Orleans.Sql.Test;

public class GatewayListProviderTests
{
    private static string GetReadConnectionString(ArgentSeaGatewayListProvider provider)
        => (string)typeof(ArgentSeaGatewayListProvider)
            .GetField("_readConnectionString", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(provider)!;

    private static ArgentSeaGatewayListProvider CreateProvider(SqlDbConnectionConfiguration dbConfig, SqlGlobalPropertiesOptions globalOptions)
    {
        var dbOptions = Options.Create(new SqlDbConnectionOptions { SqlDbConnections = [dbConfig] });
        var clientOptions = Options.Create(new ClusterClientDbOptions
        {
            DatabaseKey = "Common",
            GatewayListQuery = new QueryProcedure("rdr.OrleansClusterGatewayListV1", ["@ClusterId"])
        });
        var clusterOptions = Options.Create(new ClusterOptions { ClusterId = "TestCluster" });
        return new ArgentSeaGatewayListProvider(dbOptions, Options.Create(globalOptions), clientOptions, clusterOptions, NullLogger<ArgentSeaGatewayListProvider>.Instance);
    }

    [Fact]
    [Trait("Spec", "blk-878290f0225f42e2a0918be1cc8c6693")]
    [Trait("Spec", "blk-566c476d46b8403da27b52d87ef4263e")]
    public void ReadConnectionStringAppliesGlobalDatabaseAndReadSettings()
    {
        var globalOptions = new SqlGlobalPropertiesOptions
        {
            DataSource = "global-server",
            ApplicationName = "GlobalApp",
            UserName = "globalUser",
            Password = "globalPwd"
        };
        var dbConfig = new SqlDbConnectionConfiguration
        {
            DatabaseKey = "Common",
            InitialCatalog = "OrleansDb",
            ReadConnection = new SqlConnectionConfiguration { DataSource = "read-replica" }
        };

        var provider = CreateProvider(dbConfig, globalOptions);
        var connectionString = GetReadConnectionString(provider);

        connectionString.Should().NotBe(typeof(SqlConnectionConfiguration).FullName, "the gateway must use a real connection string, not the configuration type name");
        var csb = new SqlConnectionStringBuilder(connectionString);
        csb.DataSource.Should().Be("read-replica", "the read connection overrides the global data source");
        csb.InitialCatalog.Should().Be("OrleansDb", "the database-level setting is inherited");
        csb.ApplicationName.Should().Be("GlobalApp", "the global setting is inherited");
        csb.UserID.Should().Be("globalUser", "the global credentials are inherited");
        csb.Password.Should().Be("globalPwd", "the global credentials are inherited");
    }

    [Fact]
    [Trait("Spec", "blk-566c476d46b8403da27b52d87ef4263e")]
    public void ExplicitConnectionStringIsUsedVerbatim()
    {
        var dbOptions = Options.Create(new SqlDbConnectionOptions { SqlDbConnections = [] });
        var clientOptions = Options.Create(new ClusterClientDbOptions
        {
            DatabaseKey = "Common",
            ConnectionString = "Data Source=explicit;Initial Catalog=Explicit",
            GatewayListQuery = new QueryProcedure("rdr.OrleansClusterGatewayListV1", ["@ClusterId"])
        });
        var clusterOptions = Options.Create(new ClusterOptions { ClusterId = "TestCluster" });
        var provider = new ArgentSeaGatewayListProvider(dbOptions, Options.Create(new SqlGlobalPropertiesOptions()), clientOptions, clusterOptions, NullLogger<ArgentSeaGatewayListProvider>.Instance);

        GetReadConnectionString(provider).Should().Be("Data Source=explicit;Initial Catalog=Explicit");
    }
}
