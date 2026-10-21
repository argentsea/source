using System.Text;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Orleans.Configuration;
using Orleans.Hosting;

namespace ArgentSea.Orleans.Sql.Test;

/// <summary>
/// Spec section orleans-reminder-storage: ArgentSeaOrleansReminderTable driven through a SqlShardSets over the TestKit's
/// scripted provider, and the reminder-service registration extensions and provider builder.
/// (ArgentSeaReminderOptionsValidator is tested in ArgentSea.Orleans.Test.)
/// </summary>
public class ReminderStorageTests
{
    private const string ShardSetKey = "Reminders";
    private static readonly DateTime StartAt = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] ReminderColumns = ["GrainKey", "GrainType", "ReminderName", "StartTime", "Period", "Version"];

    private static (ArgentSeaOrleansReminderTable table, FakeSqlShardSets shards) Build()
    {
        var shards = FakeSqlShardSets.Create(ShardSetKey, 1, 2, 3);
        var table = new ArgentSeaOrleansReminderTable(
            shards,
            Options.Create(new OrleansShardPersistenceOptions { ShardSetKey = ShardSetKey }),
            new RecordingLogger<ArgentSeaOrleansReminderTable>());
        return (table, shards);
    }

    private static GrainId Grain(short shardId, int recordId, string type = "mygrain")
        => GrainId.Create(GrainType.Create(type), new IdSpan(new ShardKey<int>(shardId, recordId).ToUtf8().ToArray()));

    private static object?[] Row(GrainId grainId, string name, int version, DateTime? start = null, long periodTicks = 600_000_000L)
        => [grainId.Key.Value.ToArray(), grainId.Type.ToString(), name, start ?? StartAt, periodTicks, version];

    [Fact]
    [Trait("Spec", "blk-578cd7199de843e6a7e8cba2a78851b7")]
    [Trait("Spec", "blk-165ab70846224c2ba61f6c50a355fa45")]
    [Trait("Spec", "blk-7750a2740c1048cea01fdb839855bcf6")]
    public async Task ReadRowSelectsTheGrainShardAndDecodesTheMatchingRow()
    {
        var (table, shards) = Build();
        var grainId = Grain(3, 9);
        shards.Provider.Script.ForQuery(Queries.OrleansReminderReadRowKey, 3).Returns(ReminderColumns, Row(grainId, "daily", 11));

        var entry = await table.ReadRow(grainId, "daily");

        entry.Should().NotBeNull();
        entry!.GrainId.Should().Be(grainId, "the owner is rebuilt from the grain type and key bytes");
        entry.ReminderName.Should().Be("daily");
        entry.StartAt.Should().Be(StartAt);
        entry.Period.Should().Be(TimeSpan.FromTicks(600_000_000L));
        entry.ETag.Should().Be("11", "the integer row version is exposed as an invariant-culture string");
        var cmd = shards.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.ShardId.Should().Be(3);
        cmd.Connection.Access.Should().Be(FakeConnectionAccess.Read);
        cmd.CommandText.Should().Be(Queries.OrleansReminderReadRowKey.Sql);
        ((byte[])cmd.Parameter("@GrainKey")!).Should().Equal(grainId.Key.Value.ToArray());
        cmd.Parameter("@GrainType").Should().Be("mygrain");
        cmd.Parameter("@ReminderName").Should().Be("daily");
    }

    [Fact]
    [Trait("Spec", "blk-10774f1f917147f8aa5ea030c3f5baec")]
    [Trait("Spec", "blk-578cd7199de843e6a7e8cba2a78851b7")]
    [Trait("Spec", "blk-165ab70846224c2ba61f6c50a355fa45")]
    public async Task ReadRowWithNoMatchingRowReturnsAnEntryBearingTheGrainId()
    {
        var (table, shards) = Build();
        var grainId = Grain(3, 9);
        shards.Provider.Script.ForQuery(Queries.OrleansReminderReadRowKey).ReturnsNoRows(ReminderColumns);

        var entry = await table.ReadRow(grainId, "daily");

        entry.Should().NotBeNull();
        entry!.GrainId.Should().Be(grainId);
        entry.ReminderName.Should().BeNull();
        entry.ETag.Should().BeNull();
        shards.Provider.Recorder.Commands.Should().ContainSingle().Which.ShardId.Should().Be(3);
    }

    [Fact]
    [Trait("Spec", "blk-b9fca0761f3641f18aa98f1916ecef9b")]
    [Trait("Spec", "blk-165ab70846224c2ba61f6c50a355fa45")]
    public async Task ReadRowsForAGrainReturnsEveryRowOfItsShardAndSkipsRowsWithoutAGrainType()
    {
        var (table, shards) = Build();
        var grainId = Grain(2, 5);
        shards.Provider.Script.ForQuery(Queries.OrleansReminderReadRowsKey, 2).Returns(ReminderColumns,
            Row(grainId, "r1", 1),
            [grainId.Key.Value.ToArray(), null, "ghost", StartAt, 1L, 1],
            Row(grainId, "r2", 2),
            Row(grainId, "r3", 3));

        var data = await table.ReadRows(grainId);

        data.Reminders.Select(r => r.ReminderName).Should().BeEquivalentTo(["r1", "r2", "r3"]);
        data.Reminders.Should().OnlyContain(r => r.GrainId == grainId);
        var cmd = shards.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.ShardId.Should().Be(2);
        cmd.CommandText.Should().Be(Queries.OrleansReminderReadRowsKey.Sql);
        cmd.Parameter("@GrainType").Should().Be("mygrain");
    }

    [Fact]
    [Trait("Spec", "blk-7131c458eb9c4d35b5c2f516c54d01a6")]
    [Trait("Spec", "blk-b9fca0761f3641f18aa98f1916ecef9b")]
    [Trait("Spec", "blk-165ab70846224c2ba61f6c50a355fa45")]
    public async Task ReadRowsForAHashRangeReturnsEveryRowOfEveryShard()
    {
        var (table, shards) = Build();
        var a = Grain(1, 1);
        var b = Grain(2, 2);
        shards.Provider.Script.ForQuery(Queries.OrleansReminderReadRangeRows1Key, 1).Returns(ReminderColumns, Row(a, "a1", 1), Row(a, "a2", 2));
        shards.Provider.Script.ForQuery(Queries.OrleansReminderReadRangeRows1Key, 2).Returns(ReminderColumns, Row(b, "b1", 3), Row(b, "b2", 4));
        shards.Provider.Script.ForQuery(Queries.OrleansReminderReadRangeRows1Key, 3).ReturnsNoRows(ReminderColumns);

        var data = await table.ReadRows(10, 20);

        data.Reminders.Select(r => r.ReminderName).Should().BeEquivalentTo(["a1", "a2", "b1", "b2"], "all rows of each shard are drained, not just the first");
        var commands = shards.Provider.Recorder.Commands;
        commands.Select(c => c.ShardId).Should().BeEquivalentTo(new short?[] { 1, 2, 3 }, "the range read fans out to every read shard");
        commands.Should().OnlyContain(c => c.Connection.Access == FakeConnectionAccess.Read && c.CommandText == Queries.OrleansReminderReadRangeRows1Key.Sql);
        commands.Should().OnlyContain(c => Equals(c.Parameter("@BeginHash"), 10L) && Equals(c.Parameter("@EndHash"), 20L));
    }

    [Fact]
    [Trait("Spec", "blk-9417c0653239458ca295acaccb2ca6cb")]
    [Trait("Spec", "blk-0cdfc06ce27c464094fc3351be37e58f")]
    [Trait("Spec", "blk-165ab70846224c2ba61f6c50a355fa45")]
    public async Task RemoveRowSubmitsTheVersionToTheGrainShardAndReturnsWhetherTheRowWasFound()
    {
        var (table, shards) = Build();
        var grainId = Grain(3, 9);
        shards.Provider.Script.ForQuery(Queries.OrleansReminderDeleteRowKey, 3).WithOutput("@IsFound", false);

        var missing = await table.RemoveRow(grainId, "daily", "5");

        missing.Should().BeFalse();
        var cmd = shards.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.ShardId.Should().Be(3);
        cmd.Connection.Access.Should().Be(FakeConnectionAccess.Write);
        cmd.CommandText.Should().Be(Queries.OrleansReminderDeleteRowKey.Sql);
        cmd.Parameter("@ReminderName").Should().Be("daily");
        cmd.Parameter("@Version").Should().Be(5, "the version text is the conditional row version");

        shards.Provider.Script.ForQuery(Queries.OrleansReminderDeleteRowKey, 3).WithOutput("@IsFound", true);
        (await table.RemoveRow(grainId, "daily", "5")).Should().BeTrue();
    }

    [Fact]
    [Trait("Spec", "blk-b04bedc77996402d9ce87a764727ec3a")]
    [Trait("Spec", "blk-315f0b666c68427ba94e3e85f87ecc72")]
    [Trait("Spec", "blk-165ab70846224c2ba61f6c50a355fa45")]
    [Trait("Spec", "blk-7750a2740c1048cea01fdb839855bcf6")]
    public async Task UpsertRowWritesTheEntryToTheGrainShardAndReturnsTheNewVersionAsAString()
    {
        var (table, shards) = Build();
        var grainId = Grain(3, 9);
        shards.Provider.Script.ForQuery(Queries.OrleansReminderUpsertRowKey, 3).WithOutput("@NewVersion", 7);
        var entry = new ReminderEntry { GrainId = grainId, ReminderName = "daily", StartAt = StartAt, Period = TimeSpan.FromTicks(600_000_000L), ETag = null };

        var version = await table.UpsertRow(entry);

        version.Should().Be("7");
        var cmd = shards.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.ShardId.Should().Be(3);
        cmd.Connection.Access.Should().Be(FakeConnectionAccess.Write);
        cmd.CommandText.Should().Be(Queries.OrleansReminderUpsertRowKey.Sql);
        ((byte[])cmd.Parameter("@GrainKey")!).Should().Equal(grainId.Key.Value.ToArray());
        cmd.Parameter("@GrainType").Should().Be("mygrain");
        cmd.Parameter("@ReminderName").Should().Be("daily");
        cmd.Parameter("@StartTime").Should().Be(StartAt);
        cmd.Parameter("@Period").Should().Be(600_000_000L);
        cmd.Parameter("@GrainHash").Should().Be((long)grainId.GetUniformHashCode());
        var oldVersion = cmd.Parameter("@OldVersion");
        (oldVersion is null || oldVersion is DBNull).Should().BeTrue("a new reminder has no old version");

        entry.ETag = "7";
        shards.Provider.Script.ForQuery(Queries.OrleansReminderUpsertRowKey, 3).WithOutput("@NewVersion", 8);
        (await table.UpsertRow(entry)).Should().Be("8");
        shards.Provider.Recorder.Commands[1].Parameter("@OldVersion").Should().Be(7);
    }

    [Fact]
    [Trait("Spec", "blk-d2375a38770f4255956ffc8ed666fba9")]
    [Trait("Spec", "blk-2d63393ce9914d1191f66ccc8e7d71d6")]
    public async Task TestOnlyClearTableCompletesWithoutInvokingAnyQuery()
    {
        var (table, shards) = Build();

        var task = table.TestOnlyClearTable();

        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
        shards.Provider.Recorder.Commands.Should().BeEmpty();
    }

    #region Registration

    [Fact]
    [Trait("Spec", "blk-b7271cc351ac475c827504fe69e69094")]
    public void UseArgentSeaReminderServiceOnAServiceCollectionRegistersTheTableTheValidatorAndAppliesTheOptions()
    {
        var services = new ServiceCollection();

        services.UseArgentSeaReminderService(o => o.MinimumReminderPeriod = TimeSpan.FromMinutes(2)).Should().BeSameAs(services);

        services.Should().ContainSingle(d => d.ServiceType == typeof(IReminderTable) && d.ImplementationType == typeof(ArgentSeaOrleansReminderTable) && d.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(d => d.ServiceType == typeof(IConfigurationValidator) && d.ImplementationType == typeof(ArgentSeaReminderOptionsValidator) && d.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(d => d.ServiceType == typeof(IReminderService) || d.ServiceType.FullName!.StartsWith("Orleans.Runtime.ReminderService", StringComparison.Ordinal), "Orleans reminders are registered");
        services.BuildServiceProvider().GetRequiredService<IOptions<ReminderOptions>>().Value.MinimumReminderPeriod.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    [Trait("Spec", "blk-b7271cc351ac475c827504fe69e69094")]
    public void UseArgentSeaReminderServiceOnASiloBuilderRegistersIntoTheBuilderServicesAndReturnsTheBuilder()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<ISiloBuilder>();
        builder.Services.Returns(services);

        builder.UseArgentSeaReminderService(o => o.MinimumReminderPeriod = TimeSpan.FromMinutes(3)).Should().BeSameAs(builder);

        services.Should().ContainSingle(d => d.ServiceType == typeof(IReminderTable) && d.ImplementationType == typeof(ArgentSeaOrleansReminderTable));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IConfigurationValidator) && d.ImplementationType == typeof(ArgentSeaReminderOptionsValidator));
        services.BuildServiceProvider().GetRequiredService<IOptions<ReminderOptions>>().Value.MinimumReminderPeriod.Should().Be(TimeSpan.FromMinutes(3));
    }

    [Fact]
    [Trait("Spec", "blk-04184cd42bbd4459b29b9b3eef55e985")]
    [Trait("Spec", "blk-90920a87867a4469b540c803c23d0d32")]
    public void TheRemindersProviderBuilderInstallsTheReminderServiceWithDefaultOptionsAndRegistersClusterOptions()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<ISiloBuilder>();
        builder.Services.Returns(services);
        builder.Configuration.Returns(new ConfigurationBuilder().Build());
        var section = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Orleans:Reminders:ProviderType"] = "ArgentSeaSql" }).Build().GetSection("Orleans:Reminders");

        new ArgentSeaRemindersProviderBuilder().Configure(builder, "ArgentSeaSql", section);

        services.Should().ContainSingle(d => d.ServiceType == typeof(IReminderTable) && d.ImplementationType == typeof(ArgentSeaOrleansReminderTable));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IConfigurationValidator) && d.ImplementationType == typeof(ArgentSeaReminderOptionsValidator));
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<ClusterOptions>>().Should().NotBeNull("a ClusterOptions options registration is added");
        provider.GetRequiredService<IOptions<ReminderOptions>>().Value.MinimumReminderPeriod.Should().Be(new ReminderOptions().MinimumReminderPeriod, "ReminderOptions are left at their defaults");

        typeof(ArgentSeaRemindersProviderBuilder).Assembly.GetCustomAttributesData()
            .Where(d => d.AttributeType.Name == "RegisterProviderAttribute")
            .Select(d => d.ConstructorArguments.Select(a => a.Value).ToArray())
            .Should().ContainEquivalentOf(new object[] { "ArgentSeaSql", "Reminders", "Silo", typeof(ArgentSeaRemindersProviderBuilder) },
                "Orleans discovers the builder through the assembly-level (\"ArgentSeaSql\", \"Reminders\", \"Silo\") registration");
    }

    #endregion
}
