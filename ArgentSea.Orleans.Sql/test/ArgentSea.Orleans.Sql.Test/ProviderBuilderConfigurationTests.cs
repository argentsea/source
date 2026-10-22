using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Orleans.Hosting;

namespace ArgentSea.Orleans.Sql.Test;

public class ProviderBuilderConfigurationTests
{
    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    [Trait("Spec", "blk-566af5dfe48e44a9a243401a545ffa14")]
    [Trait("Spec", "blk-d08a58cad02549279309b6cc41df6fd4")]
    public void ResolveKey_ProviderSectionWins()
    {
        var config = Config(new()
        {
            ["Orleans:GrainStorage:Tenant:ProviderType"] = "ArgentSeaSql",
            ["Orleans:GrainStorage:Tenant:ShardSetKey"] = "FromProvider",
            ["OrleansData:ShardSetKey"] = "FromLegacy",
        });

        ArgentSeaGrainStorageProviderConfiguration
            .ResolveKey(config, config.GetSection("Orleans:GrainStorage:Tenant"), "ShardSetKey")
            .Should().Be("FromProvider");
    }

    [Fact]
    [Trait("Spec", "blk-566af5dfe48e44a9a243401a545ffa14")]
    [Trait("Spec", "blk-b7f391c29d5049c5b5adf2aa224cd3dc")]
    public void ResolveKey_ProviderSectionWithoutKey_FallsBackToOrleansData()
    {
        var config = Config(new()
        {
            ["Orleans:GrainStorage:Common:ProviderType"] = "ArgentSeaSqlDb",
            ["OrleansData:DatabaseKey"] = "Common",
        });

        ArgentSeaGrainStorageProviderConfiguration
            .ResolveKey(config, config.GetSection("Orleans:GrainStorage:Common"), "DatabaseKey")
            .Should().Be("Common");
    }

    [Fact]
    [Trait("Spec", "blk-566af5dfe48e44a9a243401a545ffa14")]
    [Trait("Spec", "blk-b7f391c29d5049c5b5adf2aa224cd3dc")]
    public void ResolveKey_OrleansDataWithoutKey_UsesDefault()
    {
        var config = Config(new() { ["OrleansData:ValidateGrainKeys"] = "false" });

        ArgentSeaGrainStorageProviderConfiguration
            .ResolveKey(config, config.GetSection("Orleans:GrainStorage:Common"), "DatabaseKey")
            .Should().Be("Default");
    }

    [Fact]
    [Trait("Spec", "blk-566af5dfe48e44a9a243401a545ffa14")]
    public void ResolveKey_NoProviderKeyAndNoOrleansData_Throws()
    {
        var config = Config(new() { ["Something:Else"] = "x" });

        var act = () => ArgentSeaGrainStorageProviderConfiguration
            .ResolveKey(config, config.GetSection("Orleans:GrainStorage:Common"), "DatabaseKey");

        act.Should().Throw<InvalidOperationException>();
    }
}
