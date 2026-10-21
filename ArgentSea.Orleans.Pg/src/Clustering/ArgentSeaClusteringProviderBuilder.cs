using Microsoft.Extensions.Configuration;
using Orleans.Configuration;
using Orleans.Providers;

// Our providerType is named "ArgentSeaPg" (ArgentSea.Orleans.Sql registers "ArgentSeaSql") and our providerName is "Clustering".
[assembly: RegisterProvider("ArgentSeaPg", "Clustering", "Silo", typeof(ArgentSea.Orleans.Pg.ArgentSeaClusteringProviderBuilder))]
[assembly: RegisterProvider("ArgentSeaPg", "Clustering", "Client", typeof(ArgentSea.Orleans.Pg.ArgentSeaClusteringProviderBuilder))]

namespace ArgentSea.Orleans.Pg;

public sealed class ArgentSeaClusteringProviderBuilder : IProviderBuilder<ISiloBuilder>, IProviderBuilder<IClientBuilder>
{
    public void Configure(ISiloBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        builder.Configure<ClusterOptions>(configurationSection);
        builder.UseArgentSeaPgClustering(_ => { });
    }

    public void Configure(IClientBuilder builder, string? name, IConfigurationSection configurationSection)
    {
        builder.Configure<ClusterOptions>(configurationSection);
        builder.UseArgentSeaPgClustering(_ => { });
    }
}
