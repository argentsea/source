using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Orleans.Hosting;

namespace ArgentSea.Orleans.Sql.Test;

public class ProviderBuilderConfigurationTests
{
    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    [Trait("Spec", "blk-6f418939dc074229857ac55313f1fffa")]
    [Trait("Spec", "blk-9205f247b71a41b8bc98746a5d68727e")]
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
    [Trait("Spec", "blk-6f418939dc074229857ac55313f1fffa")]
    [Trait("Spec", "blk-242e632bcc3e4bfca54b139ed4dee1bb")]
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
    [Trait("Spec", "blk-6f418939dc074229857ac55313f1fffa")]
    [Trait("Spec", "blk-242e632bcc3e4bfca54b139ed4dee1bb")]
    public void ResolveKey_OrleansDataWithoutKey_UsesDefault()
    {
        var config = Config(new() { ["OrleansData:ValidateGrainKeys"] = "false" });

        ArgentSeaGrainStorageProviderConfiguration
            .ResolveKey(config, config.GetSection("Orleans:GrainStorage:Common"), "DatabaseKey")
            .Should().Be("Default");
    }

    [Fact]
    [Trait("Spec", "blk-6f418939dc074229857ac55313f1fffa")]
    public void ResolveKey_NoProviderKeyAndNoOrleansData_Throws()
    {
        var config = Config(new() { ["Something:Else"] = "x" });

        var act = () => ArgentSeaGrainStorageProviderConfiguration
            .ResolveKey(config, config.GetSection("Orleans:GrainStorage:Common"), "DatabaseKey");

        act.Should().Throw<InvalidOperationException>();
    }
}
