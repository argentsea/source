using ArgentSea.Orleans;
using ArgentSea.Orleans.Pg;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime.Hosting;

namespace Orleans.Hosting;

/// <summary>
/// Registers ArgentSea grain storage over PostgreSQL. The method names carry “Pg” so they never collide with
/// ArgentSea.Orleans.Sql's extensions, which share this namespace.
/// </summary>
public static class ArgentSeaPgSiloBuilderExtensions
{
    public static ISiloBuilder AddArgentSeaPgDbOrleansGrainStorage(
        this ISiloBuilder builder,
        string providerName) => builder.ConfigureServices(
            services => services.AddArgentSeaPgDbOrleansGrainStorage(providerName, _ => { }));

    public static ISiloBuilder AddArgentSeaPgDbOrleansGrainStorage(
        this ISiloBuilder builder,
        string providerName,
        Action<OrleansDbPersistenceOptions> options) => builder.ConfigureServices(
            services => services.AddArgentSeaPgDbOrleansGrainStorage(providerName, ob => ob.Configure(options)));

    public static IServiceCollection AddArgentSeaPgDbOrleansGrainStorage(
        this IServiceCollection services,
        string providerName,
        Action<OptionsBuilder<OrleansDbPersistenceOptions>> options)
    {
        options?.Invoke(services.AddOptions<OrleansDbPersistenceOptions>(providerName));
        services.ConfigureNamedOptionForLogging<OrleansDbPersistenceOptions>(providerName);
        services.AddGrainStorage(providerName, ArgentSeaGrainStorageFactory.CreateDb);
        return services;
    }

    public static ISiloBuilder AddArgentSeaPgShardOrleansGrainStorage(
        this ISiloBuilder builder,
        string providerName,
        Action<OrleansShardPersistenceOptions> options) => builder.ConfigureServices(
            services => services.AddArgentSeaPgShardOrleansGrainStorage(providerName, ob => ob.Configure(options)));

    public static ISiloBuilder AddArgentSeaPgShardOrleansGrainStorage(
        this ISiloBuilder builder,
        string providerName) => builder.ConfigureServices(
            services => services.AddArgentSeaPgShardOrleansGrainStorage(providerName, _ => { }));

    public static IServiceCollection AddArgentSeaPgShardOrleansGrainStorage(
        this IServiceCollection services,
        string providerName,
        Action<OptionsBuilder<OrleansShardPersistenceOptions>> options)
    {
        options?.Invoke(services.AddOptions<OrleansShardPersistenceOptions>(providerName));
        services.ConfigureNamedOptionForLogging<OrleansShardPersistenceOptions>(providerName);
        services.AddGrainStorage(providerName, ArgentSeaGrainStorageFactory.CreateShards);
        return services;
    }
}
