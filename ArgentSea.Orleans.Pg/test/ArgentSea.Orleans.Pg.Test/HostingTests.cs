using ArgentSea.Pg;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Messaging;
using Orleans.Storage;

namespace ArgentSea.Orleans.Pg.Test;

/// <summary>
/// Registration of the PostgreSQL providers: the Pg-named extension methods (grain storage, clustering, reminders), the grain
/// storage factory, the declarative provider builders, and the provider types as a real Orleans host resolves them from
/// configuration — ArgentSeaPg for shard-set storage, clustering and reminders, ArgentSeaPgDb for single-database storage.
/// </summary>
public class HostingTests
{
    public class StoredOrder
    {
        [MapToPgInteger("order_id", true)]
        public int OrderId { get; set; }
    }

    public class ShardedOrder
    {
        [MapPgShardKey("order_id")]
        [MapToPgInteger("order_id")]
        public ShardKey<int> Key { get; set; } = ShardKey<int>.Empty;
    }

    private static ServiceCollection CoreServices(FakePgDatabases? dbs = null, FakePgShardSets? shards = null)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.Configure<ClusterOptions>(o => { o.ClusterId = "c"; o.ServiceId = "s"; });
        if (dbs is not null)
        {
            services.AddSingleton<PgDatabases>(dbs);
        }
        if (shards is not null)
        {
            services.AddSingleton<PgShardSets>(shards);
        }
        return services;
    }

    private static (ISiloBuilder builder, ServiceCollection services) SiloBuilder(IConfiguration? configuration = null)
    {
        var services = CoreServices();
        var builder = Substitute.For<ISiloBuilder>();
        builder.Services.Returns(services);
        builder.Configuration.Returns(configuration ?? new ConfigurationBuilder().Build());
        return (builder, services);
    }

    private static (IClientBuilder builder, ServiceCollection services) ClientBuilder()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<IClientBuilder>();
        builder.Services.Returns(services);
        builder.Configuration.Returns(new ConfigurationBuilder().Build());
        return (builder, services);
    }

    #region Grain storage

    [Fact]
    [Trait("Spec", "blk-8b4e6953a3b042338493b7f32c5fc76c")]
    public void TheDbAndShardGrainStorageExtensionsRegisterTheirFactoryUnderTheProviderName()
    {
        var (dbBuilder, dbServices) = SiloBuilder();
        dbServices.AddSingleton<PgDatabases>(FakePgDatabases.Create("Archive"));
        dbBuilder.AddArgentSeaPgDbOrleansGrainStorage("orders-db", o => o.DatabaseKey = "Archive").Should().BeSameAs(dbBuilder);
        var dbProvider = dbServices.BuildServiceProvider();
        dbProvider.GetRequiredService<IOptionsMonitor<OrleansDbPersistenceOptions>>().Get("orders-db").DatabaseKey.Should().Be("Archive");
        dbProvider.GetRequiredKeyedService<IGrainStorage>("orders-db").Should().BeOfType<ArgentSeaDbGrainPersistence<PgDbConnectionOptions>>();

        var (shardBuilder, shardServices) = SiloBuilder();
        shardServices.AddSingleton<PgShardSets>(FakePgShardSets.Create("Orders", 1));
        shardBuilder.AddArgentSeaPgShardOrleansGrainStorage("orders-shard", o => o.ShardSetKey = "Orders").Should().BeSameAs(shardBuilder);
        var shardProvider = shardServices.BuildServiceProvider();
        shardProvider.GetRequiredService<IOptionsMonitor<OrleansShardPersistenceOptions>>().Get("orders-shard").ShardSetKey.Should().Be("Orders");
        shardProvider.GetRequiredKeyedService<IGrainStorage>("orders-shard").Should().BeOfType<ArgentSeaShardGrainPersistence<PgShardConnectionOptions>>();

        var (plain, plainServices) = SiloBuilder();
        plainServices.AddSingleton<PgDatabases>(FakePgDatabases.Create("Common"));
        plain.AddArgentSeaPgDbOrleansGrainStorage("plain").Should().BeSameAs(plain);
        plainServices.BuildServiceProvider().GetRequiredKeyedService<IGrainStorage>("plain").Should().BeOfType<ArgentSeaDbGrainPersistence<PgDbConnectionOptions>>();
    }

    [Fact]
    [Trait("Spec", "blk-8b4e6953a3b042338493b7f32c5fc76c")]
    public async Task TheFactoryBuildsStorageFromTheOptionsNamedForTheProvider()
    {
        var dbs = FakePgDatabases.Create("Common");
        var shards = FakePgShardSets.Create("Catalog", 1, 2);
        var services = CoreServices(dbs, shards);
        services.Configure<OrleansDbPersistenceOptions>("orders-db", o => { o.DatabaseKey = "Common"; o.Queries["orders"] = new("orders", new QueryProcedure("rdr.read_orders"), QueryResultFormat.ResultSet, new QueryProcedure("wtr.write_orders"), new QueryProcedure("wtr.clear_orders")); });
        services.Configure<OrleansShardPersistenceOptions>("catalog", o => { o.ShardSetKey = "Catalog"; o.Queries["orders"] = new("orders", new QueryProcedure("rdr.read_orders"), QueryResultFormat.ResultSet, new QueryProcedure("wtr.write_orders"), new QueryProcedure("wtr.clear_orders")); });
        var provider = services.BuildServiceProvider();
        dbs.Provider.Script.ForQuery("rdr.read_orders").ReturnsNoRows("order_id");
        shards.Provider.Script.ForQuery("rdr.read_orders").ReturnsNoRows("order_id");

        var db = ArgentSeaGrainStorageFactory.CreateDb(provider, "orders-db");
        var sharded = ArgentSeaGrainStorageFactory.CreateShards(provider, "catalog");

        db.Should().BeOfType<ArgentSeaDbGrainPersistence<PgDbConnectionOptions>>();
        await db.ReadStateAsync("orders", GrainId.Create(GrainType.Create("orders"), SpanParser.GetDbGrainId(new StoredOrder { OrderId = 5 }, NullLogger.Instance)), new GrainState<StoredOrder>(new StoredOrder()));
        dbs.Provider.Recorder.Commands.Should().ContainSingle().Which.Connection.DatabaseKey.Should().Be("Common");

        sharded.Should().BeOfType<ArgentSeaShardGrainPersistence<PgShardConnectionOptions>>();
        var key = new ShardKey<int>(2, 5);
        var state = new GrainState<ShardedOrder>(new ShardedOrder());
        await sharded.ReadStateAsync("orders", GrainId.Create(GrainType.Create("orders"), new IdSpan(key.ToUtf8().ToArray())), state);
        shards.Provider.Recorder.Commands.Should().ContainSingle().Which.ShardId.Should().Be(2, "the grain key names the shard");
        state.State!.Key.Should().Be(key);
    }

    [Fact]
    [Trait("Spec", "blk-ed685b5c044a4fefa64793618389b10c")]
    public void TheProviderBuildersResolveTheirKeyFromTheProviderSectionThenOrleansDataThenDefault()
    {
        IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var dbSection = Config(new() { ["Orleans:GrainStorage:catalog:DatabaseKey"] = "Catalog", ["OrleansData:DatabaseKey"] = "Common" });
        var (dbBuilder, dbServices) = SiloBuilder(dbSection);
        new ArgentSeaPgGrainStorageDbProviderBuilder().Configure(dbBuilder, "catalog", dbSection.GetSection("Orleans:GrainStorage:catalog"));
        dbServices.BuildServiceProvider().GetRequiredService<IOptionsMonitor<OrleansDbPersistenceOptions>>().Get("catalog").DatabaseKey.Should().Be("Catalog", "the provider's own section wins");

        var legacy = Config(new() { ["OrleansData:ShardSetKey"] = "Legacy" });
        var (shardBuilder, shardServices) = SiloBuilder(legacy);
        new ArgentSeaPgGrainStorageShardProviderBuilder().Configure(shardBuilder, "orders", legacy.GetSection("Orleans:GrainStorage:orders"));
        shardServices.BuildServiceProvider().GetRequiredService<IOptionsMonitor<OrleansShardPersistenceOptions>>().Get("orders").ShardSetKey.Should().Be("Legacy");

        var bare = Config(new() { ["OrleansData:ValidateGrainKeys"] = "false" });
        ArgentSeaPgGrainStorageProviderConfiguration.ResolveKey(bare, bare.GetSection("Orleans:GrainStorage:x"), "DatabaseKey").Should().Be("Default");

        var none = Config(new() { ["Something:Else"] = "x" });
        FluentActions.Invoking(() => ArgentSeaPgGrainStorageProviderConfiguration.ResolveKey(none, none.GetSection("Orleans:GrainStorage:x"), "DatabaseKey"))
            .Should().Throw<InvalidOperationException>("without the key in the provider section the OrleansData section is required");
    }

    #endregion

    #region Clustering and reminders

    [Fact]
    [Trait("Spec", "blk-ed685b5c044a4fefa64793618389b10c")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public void UseArgentSeaPgClusteringRegistersTheMembershipTableAndForClientsTheGatewayListProvider()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ClusterId"] = "c1", ["ServiceId"] = "s1" }).Build();
        var (silo, siloServices) = SiloBuilder(config);
        silo.UseArgentSeaPgClustering().Should().BeSameAs(silo);
        siloServices.Should().ContainSingle(d => d.ServiceType == typeof(IMembershipTable) && d.ImplementationType == typeof(ArgentSeaOrleansMembershipTable) && d.Lifetime == ServiceLifetime.Singleton);
        siloServices.Should().ContainSingle(d => d.ServiceType == typeof(IConfigurationValidator) && d.ImplementationType == typeof(ClusterOptionsValidator));
        siloServices.BuildServiceProvider().GetRequiredService<IOptions<ClusterOptions>>().Value.ClusterId.Should().Be("c1");

        var (client, clientServices) = ClientBuilder();
        client.UseArgentSeaPgClustering(_ => { }).Should().BeSameAs(client);
        clientServices.Should().ContainSingle(d => d.ServiceType == typeof(IGatewayListProvider) && d.ImplementationType == typeof(ArgentSeaGatewayListProvider) && d.Lifetime == ServiceLifetime.Singleton);
        clientServices.Should().ContainSingle(d => d.ServiceType == typeof(IMembershipTable) && d.ImplementationType == typeof(ArgentSeaOrleansMembershipTable));
    }

    [Fact]
    [Trait("Spec", "blk-ed685b5c044a4fefa64793618389b10c")]
    [Trait("Spec", "blk-948771504008464e998e76a2f11c4633")]
    public void UseArgentSeaPgReminderServiceRegistersTheTableTheValidatorAndAppliesTheOptions()
    {
        var services = new ServiceCollection();
        services.UseArgentSeaPgReminderService(o => o.MinimumReminderPeriod = TimeSpan.FromMinutes(2)).Should().BeSameAs(services);
        services.Should().ContainSingle(d => d.ServiceType == typeof(IReminderTable) && d.ImplementationType == typeof(ArgentSeaOrleansReminderTable) && d.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(d => d.ServiceType == typeof(IConfigurationValidator) && d.ImplementationType == typeof(ArgentSeaReminderOptionsValidator));
        services.BuildServiceProvider().GetRequiredService<IOptions<ReminderOptions>>().Value.MinimumReminderPeriod.Should().Be(TimeSpan.FromMinutes(2));

        var (silo, siloServices) = SiloBuilder();
        silo.UseArgentSeaPgReminderService(o => o.MinimumReminderPeriod = TimeSpan.FromMinutes(3)).Should().BeSameAs(silo);
        siloServices.Should().ContainSingle(d => d.ServiceType == typeof(IReminderTable) && d.ImplementationType == typeof(ArgentSeaOrleansReminderTable));
    }

    #endregion

    #region Provider types, as a real host resolves them

    private static HostApplicationBuilder Silo(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(settings) { ["Orleans:ClusterId"] = "c", ["Orleans:ServiceId"] = "s" });
        builder.UseOrleans(_ => { });
        return builder;
    }

    [Fact]
    [Trait("Spec", "blk-8b4e6953a3b042338493b7f32c5fc76c")]
    [Trait("Spec", "blk-ed685b5c044a4fefa64793618389b10c")]
    public void ArgentSeaPgGrainStorageIsShardSetStorageAndArgentSeaPgDbIsSingleDatabaseStorage()
    {
        using var shard = Silo(new() { ["Orleans:GrainStorage:orders:ProviderType"] = "ArgentSeaPg", ["Orleans:GrainStorage:orders:ShardSetKey"] = "Orders" }).Services.BuildServiceProvider();
        shard.GetRequiredService<IOptionsMonitor<OrleansShardPersistenceOptions>>().Get("orders").ShardSetKey.Should().Be("Orders");
        shard.GetRequiredService<IOptionsMonitor<OrleansDbPersistenceOptions>>().Get("orders").DatabaseKey.Should().Be(new OrleansDbPersistenceOptions().DatabaseKey, "the single-database builder did not run");

        using var db = Silo(new() { ["Orleans:GrainStorage:catalog:ProviderType"] = "ArgentSeaPgDb", ["Orleans:GrainStorage:catalog:DatabaseKey"] = "Catalog" }).Services.BuildServiceProvider();
        db.GetRequiredService<IOptionsMonitor<OrleansDbPersistenceOptions>>().Get("catalog").DatabaseKey.Should().Be("Catalog");
        db.GetRequiredService<IOptionsMonitor<OrleansShardPersistenceOptions>>().Get("catalog").ShardSetKey.Should().Be(new OrleansShardPersistenceOptions().ShardSetKey, "the shard-set builder did not run");
    }

    [Fact]
    [Trait("Spec", "blk-ed685b5c044a4fefa64793618389b10c")]
    [Trait("Spec", "blk-948771504008464e998e76a2f11c4633")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public void ArgentSeaPgClusteringAndRemindersInstallThisPackagesTables()
    {
        var builder = Silo(new() { ["Orleans:Clustering:ProviderType"] = "ArgentSeaPg", ["Orleans:Reminders:ProviderType"] = "ArgentSeaPg" });

        builder.Services.Should().Contain(d => d.ServiceType == typeof(IMembershipTable) && d.ImplementationType == typeof(ArgentSeaOrleansMembershipTable));
        builder.Services.Should().Contain(d => d.ServiceType == typeof(IReminderTable) && d.ImplementationType == typeof(ArgentSeaOrleansReminderTable));
    }

    [Fact]
    [Trait("Spec", "blk-5308f769443c4483a1d48fcd5a377128")]
    [Trait("Spec", "blk-ed685b5c044a4fefa64793618389b10c")]
    public void TheRegistrationsArePostgreSqlSpecificAndNoTwoShareAProviderKey()
    {
        var registrations = typeof(Queries).Assembly.GetCustomAttributesData()
            .Where(d => d.AttributeType.Name == "RegisterProviderAttribute")
            .Select(d => (Type: (string)d.ConstructorArguments[0].Value!, Kind: (string)d.ConstructorArguments[1].Value!, Side: (string)d.ConstructorArguments[2].Value!, Builder: (Type)d.ConstructorArguments[3].Value!))
            .ToList();

        registrations.Should().BeEquivalentTo(new[]
        {
            ("ArgentSeaPg", "Clustering", "Silo", typeof(ArgentSeaClusteringProviderBuilder)),
            ("ArgentSeaPg", "Clustering", "Client", typeof(ArgentSeaClusteringProviderBuilder)),
            ("ArgentSeaPgDb", "GrainStorage", "Silo", typeof(ArgentSeaPgGrainStorageDbProviderBuilder)),
            ("ArgentSeaPg", "GrainStorage", "Silo", typeof(ArgentSeaPgGrainStorageShardProviderBuilder)),
            ("ArgentSeaPg", "Reminders", "Silo", typeof(ArgentSeaRemindersProviderBuilder)),
        });
        registrations.GroupBy(r => (r.Type, r.Kind, r.Side)).Should().OnlyContain(g => g.Count() == 1, "Orleans keeps one builder per provider key");
    }

    #endregion
}
