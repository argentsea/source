using ArgentSea.Orleans;
using ArgentSea.Orleans.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers;
using Orleans.Runtime.Hosting;

// Orleans resolves one builder per provider type, so each storage kind has its own: shard sets (the common case) take the
// plain name, single-database storage the "Db" suffix. ArgentSea.Orleans.Pg registers "ArgentSeaPg" and "ArgentSeaPgDb".
[assembly: RegisterProvider("ArgentSeaSqlDb", "GrainStorage", "Silo", typeof(ArgentSeaGrainStorageDbProviderBuilder))]
[assembly: RegisterProvider("ArgentSeaSql", "GrainStorage", "Silo", typeof(ArgentSeaGrainStorageShardProviderBuilder))]

namespace Orleans.Hosting;

internal static class ArgentSeaGrainStorageProviderConfiguration
{
    /// <summary>The legacy, application-wide configuration section documented in the readme.</summary>
    internal const string LegacySectionName = "OrleansData";

    /// <summary>The value used when neither configuration source supplies the key.</summary>
    internal const string DefaultKey = "Default";

    /// <summary>
    /// Resolves a provider setting. The provider's own configuration section (the one Orleans passes to
    /// <see cref="IProviderBuilder{TBuilder}.Configure"/>, e.g. Orleans:GrainStorage:{name}) wins when it has the key;
    /// otherwise the legacy top-level "OrleansData" section is required, as before; if that lacks the key too, "Default" is used.
    /// </summary>
    internal static string ResolveKey(IConfiguration configuration, IConfigurationSection? providerSection, string settingName)
    {
        var fromProvider = providerSection?[settingName];
        if (!string.IsNullOrEmpty(fromProvider))
        {
            return fromProvider;
        }
        return configuration.GetRequiredSection(LegacySectionName)[settingName] ?? DefaultKey;
    }
}

internal sealed class ArgentSeaGrainStorageDbProviderBuilder : IProviderBuilder<ISiloBuilder>
{
    public void Configure(ISiloBuilder builder, string? dataProviderName, IConfigurationSection configurationSection) => 
        builder.ConfigureServices(services =>
    {
        var name = dataProviderName ?? ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME;
        var databaseKey = ArgentSeaGrainStorageProviderConfiguration.ResolveKey(builder.Configuration, configurationSection, "DatabaseKey");
        builder.Services.Configure<OrleansDbPersistenceOptions>(name, opts =>
        {
            opts.DatabaseKey = databaseKey;
        });
        services.AddGrainStorage(name, ArgentSeaGrainStorageFactory.CreateDb);
        return;
    });
}

internal sealed class ArgentSeaGrainStorageShardProviderBuilder : IProviderBuilder<ISiloBuilder>
{
    public void Configure(ISiloBuilder builder, string? dataProviderName, IConfigurationSection configurationSection) => 
        builder.ConfigureServices(services =>
    {
        var name = dataProviderName ?? ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME;
        var shardSetKey = ArgentSeaGrainStorageProviderConfiguration.ResolveKey(builder.Configuration, configurationSection, "ShardSetKey");
        builder.Services.Configure<OrleansShardPersistenceOptions>(name, opts =>
        {
            opts.ShardSetKey = shardSetKey;
        });
        services.AddGrainStorage(name, ArgentSeaGrainStorageFactory.CreateShards);
        return;
    });
}
