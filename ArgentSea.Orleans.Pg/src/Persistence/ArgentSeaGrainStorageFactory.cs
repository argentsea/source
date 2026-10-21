using ArgentSea.Pg;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Storage;

namespace ArgentSea.Orleans.Pg;

public static class ArgentSeaGrainStorageFactory
{
    public static IGrainStorage CreateDb(IServiceProvider services, string name)
    {
        var optOrleans = services.GetOptionsByName<OrleansDbPersistenceOptions>(name);
        var optCluster = services.GetRequiredService<IOptions<ClusterOptions>>();
        var svcDatabases = services.GetRequiredService<PgDatabases>();
        var svcLogger = services.GetRequiredService<ILogger<ArgentSeaDbGrainPersistence<PgDbConnectionOptions>>>();
        return new ArgentSeaDbGrainPersistence<PgDbConnectionOptions>(svcDatabases, Options.Create(optOrleans), optCluster, svcLogger);
    }

    public static IGrainStorage CreateShards(IServiceProvider services, string providerName)
    {
        var optOrleans = services.GetOptionsByName<OrleansShardPersistenceOptions>(providerName);
        var optCluster = services.GetRequiredService<IOptions<ClusterOptions>>();
        var svcShards = services.GetRequiredService<PgShardSets>();
        var svcLogger = services.GetRequiredService<ILogger<ArgentSeaShardGrainPersistence<PgShardConnectionOptions>>>();
        return new ArgentSeaShardGrainPersistence<PgShardConnectionOptions>(svcShards, optOrleans, optCluster.Value, svcLogger);
    }
}
