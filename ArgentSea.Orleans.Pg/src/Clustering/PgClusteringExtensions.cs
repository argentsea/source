using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Messaging;

namespace ArgentSea.Orleans.Pg;

/// <summary>
/// Registers the PostgreSQL membership table (and, for clients, the gateway list provider).
/// The method names carry “Pg” so they never collide with ArgentSea.Orleans.Sql's UseArgentSeaClustering.
/// </summary>
public static class PgClusteringExtensions
{
    /// <summary>
    /// Configures ArgentSea over PostgreSQL as the clustering provider.
    /// </summary>
    public static ISiloBuilder UseArgentSeaPgClustering(this ISiloBuilder builder, Action<IConfiguration> configuration)
    {
        return builder.ConfigureServices(services =>
        {
            if (configuration != null)
            {
                services.Configure(configuration);
            }

            services.AddArgentSeaPgClustering();
        });
    }

    /// <summary>
    /// Configures ArgentSea over PostgreSQL as the clustering provider, binding ClusterOptions from the builder configuration.
    /// </summary>
    public static ISiloBuilder UseArgentSeaPgClustering(this ISiloBuilder builder)
    {
        return builder.ConfigureServices(services => services
            .Configure<ClusterOptions>(builder.Configuration)
            .AddArgentSeaPgClustering());
    }

    internal static IServiceCollection AddArgentSeaPgClustering(this IServiceCollection services)
    {
        services.AddSingleton<IMembershipTable, ArgentSeaOrleansMembershipTable>();
        services.AddSingleton<IConfigurationValidator, ClusterOptionsValidator>();
        return services;
    }

    /// <summary>
    /// Configures ArgentSea over PostgreSQL as the clustering provider for a client, which locates the cluster's gateways
    /// through <see cref="ArgentSeaGatewayListProvider"/>.
    /// </summary>
    public static IClientBuilder UseArgentSeaPgClustering(this IClientBuilder builder, Action<IConfiguration> configuration)
    {
        return builder.ConfigureServices(services =>
        {
            if (configuration != null)
            {
                services.Configure(configuration);
            }

            services.AddArgentSeaPgClustering();
            services.AddSingleton<IGatewayListProvider, ArgentSeaGatewayListProvider>();
        });
    }
}
