using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Providers;

// Our providerType is named "ArgentSeaSql" (ArgentSea.Orleans.Pg registers "ArgentSeaPg") and our providerName is "Clustering".
[assembly: RegisterProvider("ArgentSeaSql", "Clustering", "Silo", typeof(ArgentSea.Orleans.Sql.ArgentSeaClusteringProviderBuilder))]
[assembly: RegisterProvider("ArgentSeaSql", "Clustering", "Client", typeof(ArgentSea.Orleans.Sql.ArgentSeaClusteringProviderBuilder))]

namespace ArgentSea.Orleans.Sql;

public sealed class ArgentSeaClusteringProviderBuilder : IProviderBuilder<ISiloBuilder>, IProviderBuilder<IClientBuilder>
{
    public void Configure(ISiloBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        builder.Configure<ClusterOptions>(configurationSection);
        builder.UseArgentSeaClustering(_ => { });
    }

    public void Configure(IClientBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        builder.Configure<ClusterOptions>(configurationSection);
        builder.UseArgentSeaClustering(_ => { });
    }
}