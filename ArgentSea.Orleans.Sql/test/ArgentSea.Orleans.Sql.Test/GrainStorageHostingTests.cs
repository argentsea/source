using ArgentSea.Sql;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Storage;

namespace ArgentSea.Orleans.Sql.Test;

/// <summary>
/// Spec section orleans-grain-storage-hosting: the ArgentSeaSiloBuilderExtensions registration families, the
/// ArgentSeaGrainStorageFactory delegates (database versus shard set, options resolved by provider name) and the declarative
/// provider builders.
/// </summary>
public class GrainStorageHostingTests
{
    public class StoredOrder
    {
        [MapToSqlInt("OrderId", true)]
        public int OrderId { get; set; }

        [MapToSqlNVarChar("Name", 50)]
        public string? Name { get; set; }
    }

    public class ShardedOrder
    {
        [MapSqlShardKey("Shard", "OrderId")]
        [MapToSqlInt("OrderId")]
        public ShardKey<int> Key { get; set; } = ShardKey<int>.Empty;
    }

    private static ServiceCollection CoreServices(FakeSqlDatabases? dbs = null, FakeSqlShardSets? shards = null)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.Configure<ClusterOptions>(o => { o.ClusterId = "c"; o.ServiceId = "s"; });
        if (dbs is not null)
        {
            services.AddSingleton<SqlDatabases>(dbs);
        }
        if (shards is not null)
        {
            services.AddSingleton<SqlShardSets>(shards);
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

    private static ServiceDescriptor StorageRegistration(IServiceCollection services, string name)
        => services.Should().ContainSingle(d => d.ServiceType == typeof(IGrainStorage) && d.IsKeyedService && Equals(d.ServiceKey, name),
            $"grain storage must be registered under the provider name “{name}”").Which;

    private static IGrainStorage ResolveStorage(IServiceCollection services, string name)
        => services.BuildServiceProvider().GetRequiredKeyedService<IGrainStorage>(name);

    private static OrleansDbQueryDefinitions DbQueries(string grainType)
        => new(grainType, new QueryProcedure($"rdr.Read{grainType}"), QueryResultFormat.ResultSet, new QueryProcedure($"wtr.Write{grainType}"), new QueryProcedure($"wtr.Clear{grainType}"));

    private static OrleansShardQueryDefinitions ShardQueries(string grainType)
        => new(grainType, new QueryProcedure($"rdr.Read{grainType}"), QueryResultFormat.ResultSet, new QueryProcedure($"wtr.Write{grainType}"), new QueryProcedure($"wtr.Clear{grainType}"));

    #region ArgentSeaSiloBuilderExtensions

    [Fact]
    [Trait("Spec", "blk-50f2ebc17335430aa5195fb36985261b")]
    [Trait("Spec", "blk-424b9c87d4814d67874b812d2bb81e61")]
    [Trait("Spec", "blk-7e04fd4beed94b3bb0b0f47847434a4c")]
    public void AddDbGrainStorageOnAServiceCollectionRegistersTheDatabaseFactoryUnderTheNameAfterApplyingTheOptions()
    {
        var services = CoreServices(FakeSqlDatabases.Create("Common"));

        services.AddArgentSeaDbOrleansGrainStorage("orders-db", ob => ob.Configure(o => o.DatabaseKey = "Common")).Should().BeSameAs(services);

        StorageRegistration(services, "orders-db").Lifetime.Should().Be(ServiceLifetime.Singleton);
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptionsMonitor<OrleansDbPersistenceOptions>>().Get("orders-db").DatabaseKey.Should().Be("Common");
        provider.GetRequiredKeyedService<IGrainStorage>("orders-db").Should().BeOfType<ArgentSeaDbGrainPersistence<SqlDbConnectionOptions>>();
    }

    [Fact]
    [Trait("Spec", "blk-25461c5ab652456ebd6cedc749dfe24f")]
    [Trait("Spec", "blk-424b9c87d4814d67874b812d2bb81e61")]
    [Trait("Spec", "blk-7e04fd4beed94b3bb0b0f47847434a4c")]
    public void AddShardGrainStorageOnAServiceCollectionRegistersTheShardFactoryUnderTheNameAfterApplyingTheOptions()
    {
        var services = CoreServices(shards: FakeSqlShardSets.Create("Catalog", 1));

        services.AddArgentSeaShardOrleansGrainStorage("catalog", ob => ob.Configure(o => o.ShardSetKey = "Catalog")).Should().BeSameAs(services);

        StorageRegistration(services, "catalog").Lifetime.Should().Be(ServiceLifetime.Singleton);
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptionsMonitor<OrleansShardPersistenceOptions>>().Get("catalog").ShardSetKey.Should().Be("Catalog");
        provider.GetRequiredKeyedService<IGrainStorage>("catalog").Should().BeOfType<ArgentSeaShardGrainPersistence<SqlShardConnectionOptions>>();
    }

    [Fact]
    [Trait("Spec", "blk-83b96d8a89ac45899e606ccc897222a6")]
    [Trait("Spec", "blk-ac98c04bdb17447d9d01fd404f2540fa")]
    [Trait("Spec", "blk-5d9be4829a8c493ab541cedbf04e0c9a")]
    [Trait("Spec", "blk-a15aa975eb0d4dbaa3346b8e898dd737")]
    [Trait("Spec", "blk-7e04fd4beed94b3bb0b0f47847434a4c")]
    public void TheSiloBuilderDbOverloadsReturnTheBuilderAndRegisterTheDatabaseFactoryUnderTheName()
    {
        var (plain, plainServices) = SiloBuilder();
        plainServices.AddSingleton<SqlDatabases>(FakeSqlDatabases.Create("Common"));
        plain.AddArgentSeaDbOrleansGrainStorage("orders-db").Should().BeSameAs(plain);
        StorageRegistration(plainServices, "orders-db");
        ResolveStorage(plainServices, "orders-db").Should().BeOfType<ArgentSeaDbGrainPersistence<SqlDbConnectionOptions>>("the database factory, not the shard factory, is registered");

        var (withOptions, optionServices) = SiloBuilder();
        optionServices.AddSingleton<SqlDatabases>(FakeSqlDatabases.Create("Archive"));
        withOptions.AddArgentSeaDbOrleansGrainStorage("orders-db", o => o.DatabaseKey = "Archive").Should().BeSameAs(withOptions);
        StorageRegistration(optionServices, "orders-db");
        var provider = optionServices.BuildServiceProvider();
        provider.GetRequiredService<IOptionsMonitor<OrleansDbPersistenceOptions>>().Get("orders-db").DatabaseKey.Should().Be("Archive", "the options action is applied to the options registered under the provider name");
        provider.GetRequiredKeyedService<IGrainStorage>("orders-db").Should().BeOfType<ArgentSeaDbGrainPersistence<SqlDbConnectionOptions>>();
    }

    [Fact]
    [Trait("Spec", "blk-01ee3ac6eaa44fa2840e65a7855f1166")]
    [Trait("Spec", "blk-89bc0faa30a44ab5bc9c72ca6476f406")]
    [Trait("Spec", "blk-a15aa975eb0d4dbaa3346b8e898dd737")]
    [Trait("Spec", "blk-7e04fd4beed94b3bb0b0f47847434a4c")]
    public void TheSiloBuilderShardOverloadsReturnTheBuilderAndRegisterTheShardFactoryUnderTheName()
    {
        var (plain, plainServices) = SiloBuilder();
        plainServices.AddSingleton<SqlShardSets>(FakeSqlShardSets.Create("Default", 1));
        plain.AddArgentSeaShardOrleansGrainStorage("orders-shard").Should().BeSameAs(plain);
        StorageRegistration(plainServices, "orders-shard");
        ResolveStorage(plainServices, "orders-shard").Should().BeOfType<ArgentSeaShardGrainPersistence<SqlShardConnectionOptions>>("the shard factory, not the database factory, is registered");

        var (withOptions, optionServices) = SiloBuilder();
        optionServices.AddSingleton<SqlShardSets>(FakeSqlShardSets.Create("Orders", 1));
        withOptions.AddArgentSeaShardOrleansGrainStorage("orders-shard", o => { o.ShardSetKey = "Orders"; o.ValidateGrainKeys = false; }).Should().BeSameAs(withOptions);
        StorageRegistration(optionServices, "orders-shard");
        var provider = optionServices.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OrleansShardPersistenceOptions>>().Get("orders-shard");
        options.ShardSetKey.Should().Be("Orders");
        options.ValidateGrainKeys.Should().BeFalse();
        provider.GetRequiredKeyedService<IGrainStorage>("orders-shard").Should().BeOfType<ArgentSeaShardGrainPersistence<SqlShardConnectionOptions>>();
    }

    #endregion

    #region ArgentSeaGrainStorageFactory

    [Fact]
    [Trait("Spec", "blk-de7cfd68350545b49da7b8622ba009f0")]
    [Trait("Spec", "blk-f69201d58edd41aca5c0efa05e72c24e")]
    [Trait("Spec", "blk-72aef794489e40859ce8f8592555f11f")]
    public async Task CreateDbBuildsDatabaseStorageFromTheSingletonCollectionAndTheOptionsNamedForTheProvider()
    {
        var dbs = FakeSqlDatabases.Create("Common", "Archive");
        var services = CoreServices(dbs);
        services.Configure<OrleansDbPersistenceOptions>("orders-db", o => { o.DatabaseKey = "Common"; o.Queries["orders"] = DbQueries("Orders"); });
        services.Configure<OrleansDbPersistenceOptions>("archive-db", o => { o.DatabaseKey = "Archive"; o.Queries["archive"] = DbQueries("Archive"); });
        var provider = services.BuildServiceProvider();
        dbs.Provider.Script.ForQuery("rdr.ReadOrders").ReturnsNoRows("OrderId", "Name");
        dbs.Provider.Script.ForQuery("rdr.ReadArchive").ReturnsNoRows("OrderId", "Name");

        var orders = ArgentSeaGrainStorageFactory.CreateDb(provider, "orders-db");
        var archive = ArgentSeaGrainStorageFactory.CreateDb(provider, "archive-db");

        orders.Should().BeOfType<ArgentSeaDbGrainPersistence<SqlDbConnectionOptions>>();
        var model = new StoredOrder { OrderId = 5 };
        var grainId = GrainId.Create(GrainType.Create("orders"), SpanParser.GetDbGrainId(model, NullLogger.Instance));
        await orders.ReadStateAsync("orders", grainId, new GrainState<StoredOrder>(new StoredOrder()));
        dbs.Provider.Recorder.Commands.Should().ContainSingle().Which.Connection.DatabaseKey.Should().Be("Common", "the options named for the provider select the database");

        await FluentActions.Awaiting(() => archive.ReadStateAsync("orders", grainId, new GrainState<StoredOrder>(new StoredOrder())))
            .Should().ThrowAsync<OrleansQueryNotProvidedException>("the archive provider's options know only the archive grain");
        await archive.ReadStateAsync("archive", GrainId.Create(GrainType.Create("archive"), SpanParser.GetDbGrainId(model, NullLogger.Instance)), new GrainState<StoredOrder>(new StoredOrder()));
        dbs.Provider.Recorder.Commands.Should().HaveCount(2).And.Subject.Last().Connection.DatabaseKey.Should().Be("Archive");
    }

    [Fact]
    [Trait("Spec", "blk-397b61a161cf44608bf25d95bebe6980")]
    [Trait("Spec", "blk-b7039682f75745e3871e492698af0dd1")]
    [Trait("Spec", "blk-72aef794489e40859ce8f8592555f11f")]
    public async Task CreateShardsBuildsShardedStorageFromTheOptionsNamedForTheProvider()
    {
        var shards = FakeSqlShardSets.Create("Catalog", 1, 2);
        var services = CoreServices(shards: shards);
        services.Configure<OrleansShardPersistenceOptions>("catalog-shards", o => { o.ShardSetKey = "Catalog"; o.Queries["orders"] = ShardQueries("Orders"); });
        services.Configure<OrleansShardPersistenceOptions>("other-shards", o => { o.ShardSetKey = "Catalog"; o.Queries["other"] = ShardQueries("Other"); });
        var provider = services.BuildServiceProvider();
        shards.Provider.Script.ForQuery("rdr.ReadOrders").ReturnsNoRows("Shard", "OrderId");

        var storage = ArgentSeaGrainStorageFactory.CreateShards(provider, "catalog-shards");

        storage.Should().BeOfType<ArgentSeaShardGrainPersistence<SqlShardConnectionOptions>>();
        var key = new ShardKey<int>(2, 5);
        var grainId = GrainId.Create(GrainType.Create("orders"), new IdSpan(key.ToUtf8().ToArray()));
        var state = new GrainState<ShardedOrder>(new ShardedOrder());
        await storage.ReadStateAsync("orders", grainId, state);
        var cmd = shards.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.Connection.ShardSetName.Should().Be("Catalog");
        cmd.ShardId.Should().Be(2);
        state.State!.Key.Should().Be(key);

        var other = ArgentSeaGrainStorageFactory.CreateShards(provider, "other-shards");
        await FluentActions.Awaiting(() => other.ReadStateAsync("orders", grainId, state)).Should().ThrowAsync<OrleansQueryNotProvidedException>("each provider name resolves its own options");
    }

    #endregion

    #region Declarative provider builders

    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    [Trait("Spec", "blk-324fc1bb2edd4bd69c7875fc9ec60a98")]
    [Trait("Spec", "blk-242e632bcc3e4bfca54b139ed4dee1bb")]
    [Trait("Spec", "blk-6f418939dc074229857ac55313f1fffa")]
    public void TheDbProviderBuilderRegistersCreateDbUnderTheNameAndResolvesTheDatabaseKeyFromTheProviderSectionThenOrleansData()
    {
        var providerSection = Config(new() { ["Orleans:GrainStorage:catalog:ProviderType"] = "ArgentSeaSqlDb", ["Orleans:GrainStorage:catalog:DatabaseKey"] = "Catalog", ["OrleansData:DatabaseKey"] = "Common" });
        var (fromSection, sectionServices) = SiloBuilder(providerSection);
        sectionServices.AddSingleton<SqlDatabases>(FakeSqlDatabases.Create("Catalog"));
        new ArgentSeaGrainStorageDbProviderBuilder().Configure(fromSection, "catalog", providerSection.GetSection("Orleans:GrainStorage:catalog"));
        StorageRegistration(sectionServices, "catalog");
        var provider = sectionServices.BuildServiceProvider();
        provider.GetRequiredService<IOptionsMonitor<OrleansDbPersistenceOptions>>().Get("catalog").DatabaseKey.Should().Be("Catalog", "the provider's own section wins");
        provider.GetRequiredKeyedService<IGrainStorage>("catalog").Should().BeOfType<ArgentSeaDbGrainPersistence<SqlDbConnectionOptions>>();

        var legacy = Config(new() { ["Orleans:GrainStorage:catalog:ProviderType"] = "ArgentSeaSqlDb", ["OrleansData:DatabaseKey"] = "Common" });
        var (fromLegacy, legacyServices) = SiloBuilder(legacy);
        new ArgentSeaGrainStorageDbProviderBuilder().Configure(fromLegacy, "catalog", legacy.GetSection("Orleans:GrainStorage:catalog"));
        legacyServices.BuildServiceProvider().GetRequiredService<IOptionsMonitor<OrleansDbPersistenceOptions>>().Get("catalog").DatabaseKey.Should().Be("Common");

        var bare = Config(new() { ["OrleansData:ValidateGrainKeys"] = "false" });
        var (fromDefault, defaultServices) = SiloBuilder(bare);
        new ArgentSeaGrainStorageDbProviderBuilder().Configure(fromDefault, "catalog", bare.GetSection("Orleans:GrainStorage:catalog"));
        defaultServices.BuildServiceProvider().GetRequiredService<IOptionsMonitor<OrleansDbPersistenceOptions>>().Get("catalog").DatabaseKey.Should().Be("Default");

        var none = Config(new() { ["Something:Else"] = "x" });
        var (noData, _) = SiloBuilder(none);
        FluentActions.Invoking(() => new ArgentSeaGrainStorageDbProviderBuilder().Configure(noData, "catalog", none.GetSection("Orleans:GrainStorage:catalog")))
            .Should().Throw<InvalidOperationException>("without the key in the provider section the OrleansData section is required");
    }

    [Fact]
    [Trait("Spec", "blk-324fc1bb2edd4bd69c7875fc9ec60a98")]
    [Trait("Spec", "blk-9205f247b71a41b8bc98746a5d68727e")]
    [Trait("Spec", "blk-6f418939dc074229857ac55313f1fffa")]
    public void TheShardProviderBuilderRegistersCreateShardsUnderTheNameAndResolvesTheShardSetKeyFromTheProviderSectionThenOrleansData()
    {
        var providerSection = Config(new() { ["Orleans:GrainStorage:orders:ProviderType"] = "ArgentSeaSql", ["Orleans:GrainStorage:orders:ShardSetKey"] = "Orders", ["OrleansData:ShardSetKey"] = "Legacy" });
        var (fromSection, sectionServices) = SiloBuilder(providerSection);
        sectionServices.AddSingleton<SqlShardSets>(FakeSqlShardSets.Create("Orders", 1));
        new ArgentSeaGrainStorageShardProviderBuilder().Configure(fromSection, "orders", providerSection.GetSection("Orleans:GrainStorage:orders"));
        StorageRegistration(sectionServices, "orders");
        var provider = sectionServices.BuildServiceProvider();
        provider.GetRequiredService<IOptionsMonitor<OrleansShardPersistenceOptions>>().Get("orders").ShardSetKey.Should().Be("Orders");
        provider.GetRequiredKeyedService<IGrainStorage>("orders").Should().BeOfType<ArgentSeaShardGrainPersistence<SqlShardConnectionOptions>>();

        var legacy = Config(new() { ["Orleans:GrainStorage:orders:ProviderType"] = "ArgentSeaSql", ["OrleansData:ShardSetKey"] = "Legacy" });
        var (fromLegacy, legacyServices) = SiloBuilder(legacy);
        new ArgentSeaGrainStorageShardProviderBuilder().Configure(fromLegacy, "orders", legacy.GetSection("Orleans:GrainStorage:orders"));
        legacyServices.BuildServiceProvider().GetRequiredService<IOptionsMonitor<OrleansShardPersistenceOptions>>().Get("orders").ShardSetKey.Should().Be("Legacy");

        var bare = Config(new() { ["OrleansData:DatabaseKey"] = "Common" });
        var (fromDefault, defaultServices) = SiloBuilder(bare);
        new ArgentSeaGrainStorageShardProviderBuilder().Configure(fromDefault, "orders", bare.GetSection("Orleans:GrainStorage:orders"));
        defaultServices.BuildServiceProvider().GetRequiredService<IOptionsMonitor<OrleansShardPersistenceOptions>>().Get("orders").ShardSetKey.Should().Be("Default");

        var none = Config(new() { ["Something:Else"] = "x" });
        var (noData, _) = SiloBuilder(none);
        FluentActions.Invoking(() => new ArgentSeaGrainStorageShardProviderBuilder().Configure(noData, "orders", none.GetSection("Orleans:GrainStorage:orders")))
            .Should().Throw<InvalidOperationException>();
    }

    #endregion
}
