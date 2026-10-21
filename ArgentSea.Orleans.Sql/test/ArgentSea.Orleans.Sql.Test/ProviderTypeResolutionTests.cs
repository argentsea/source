using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ArgentSea.Orleans.Sql.Test;

/// <summary>
/// The Orleans provider types this package registers, resolved the way a host resolves them: a real Orleans silo host built
/// from configuration (Orleans:{Kind}:{name}:ProviderType), not a direct call to a provider builder. Orleans keeps one builder
/// per provider-type string, so each storage kind needs its own type: ArgentSeaSql for shard sets, ArgentSeaSqlDb for a
/// single database.
/// </summary>
public class ProviderTypeResolutionTests
{
    private static ServiceProvider BuildSilo(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(settings)
        {
            ["Orleans:ClusterId"] = "c",
            ["Orleans:ServiceId"] = "s",
        });
        builder.UseOrleans(_ => { });
        return builder.Services.BuildServiceProvider();
    }

    [Fact]
    [Trait("Spec", "blk-6f418939dc074229857ac55313f1fffa")]
    public void ArgentSeaSqlGrainStorageIsShardSetStorageKeyedByShardSetKey()
    {
        using var services = BuildSilo(new()
        {
            ["Orleans:GrainStorage:orders:ProviderType"] = "ArgentSeaSql",
            ["Orleans:GrainStorage:orders:ShardSetKey"] = "Orders",
        });

        services.GetRequiredService<IOptionsMonitor<OrleansShardPersistenceOptions>>().Get("orders").ShardSetKey.Should().Be("Orders");
        services.GetRequiredService<IOptionsMonitor<OrleansDbPersistenceOptions>>().Get("orders").DatabaseKey.Should().Be(new OrleansDbPersistenceOptions().DatabaseKey, "the database builder did not run");
    }

    [Fact]
    [Trait("Spec", "blk-6f418939dc074229857ac55313f1fffa")]
    public void ArgentSeaSqlDbGrainStorageIsSingleDatabaseStorageKeyedByDatabaseKey()
    {
        using var services = BuildSilo(new()
        {
            ["Orleans:GrainStorage:catalog:ProviderType"] = "ArgentSeaSqlDb",
            ["Orleans:GrainStorage:catalog:DatabaseKey"] = "Catalog",
        });

        services.GetRequiredService<IOptionsMonitor<OrleansDbPersistenceOptions>>().Get("catalog").DatabaseKey.Should().Be("Catalog", "the single-database builder is reachable from configuration");
        services.GetRequiredService<IOptionsMonitor<OrleansShardPersistenceOptions>>().Get("catalog").ShardSetKey.Should().Be(new OrleansShardPersistenceOptions().ShardSetKey, "the shard-set builder did not run");
    }

    [Fact]
    [Trait("Spec", "blk-f7091204d4e14a5781c28d75c0239c1c")]
    [Trait("Spec", "blk-90920a87867a4469b540c803c23d0d32")]
    [Trait("Spec", "blk-04184cd42bbd4459b29b9b3eef55e985")]
    public void ArgentSeaSqlClusteringAndRemindersInstallThisPackagesTables()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Orleans:Clustering:ProviderType"] = "ArgentSeaSql",
            ["Orleans:Reminders:ProviderType"] = "ArgentSeaSql",
        };
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(settings) { ["Orleans:ClusterId"] = "c", ["Orleans:ServiceId"] = "s" });
        builder.UseOrleans(_ => { });

        builder.Services.Should().Contain(d => d.ServiceType == typeof(IMembershipTable) && d.ImplementationType == typeof(ArgentSeaOrleansMembershipTable));
        builder.Services.Should().Contain(d => d.ServiceType == typeof(IReminderTable) && d.ImplementationType == typeof(ArgentSeaOrleansReminderTable));
    }

    [Fact]
    [Trait("Spec", "blk-6f418939dc074229857ac55313f1fffa")]
    [Trait("Spec", "blk-f7091204d4e14a5781c28d75c0239c1c")]
    [Trait("Spec", "blk-90920a87867a4469b540c803c23d0d32")]
    public void NoTwoRegistrationsShareAProviderKeyAndTheRetiredArgentSeaTypeIsGone()
    {
        var registrations = typeof(Queries).Assembly.GetCustomAttributesData()
            .Where(d => d.AttributeType.Name == "RegisterProviderAttribute")
            .Select(d => (Type: (string)d.ConstructorArguments[0].Value!, Kind: (string)d.ConstructorArguments[1].Value!, Side: (string)d.ConstructorArguments[2].Value!, Builder: (Type)d.ConstructorArguments[3].Value!))
            .ToList();

        registrations.Should().BeEquivalentTo(new[]
        {
            ("ArgentSeaSql", "Clustering", "Silo", typeof(ArgentSeaClusteringProviderBuilder)),
            ("ArgentSeaSql", "Clustering", "Client", typeof(ArgentSeaClusteringProviderBuilder)),
            ("ArgentSeaSqlDb", "GrainStorage", "Silo", typeof(global::Orleans.Hosting.ArgentSeaGrainStorageDbProviderBuilder)),
            ("ArgentSeaSql", "GrainStorage", "Silo", typeof(global::Orleans.Hosting.ArgentSeaGrainStorageShardProviderBuilder)),
            ("ArgentSeaSql", "Reminders", "Silo", typeof(ArgentSeaRemindersProviderBuilder)),
        });
        registrations.GroupBy(r => (r.Type, r.Kind, r.Side)).Should().OnlyContain(g => g.Count() == 1, "Orleans keeps one builder per provider key");
        registrations.Should().NotContain(r => r.Type == "ArgentSea");
    }
}
