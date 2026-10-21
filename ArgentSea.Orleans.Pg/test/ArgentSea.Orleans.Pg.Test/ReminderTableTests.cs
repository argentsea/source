using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Data;

namespace ArgentSea.Orleans.Pg.Test;

/// <summary>
/// The PostgreSQL ArgentSeaOrleansReminderTable driven through a PgShardSets over the TestKit's scripted provider: shard
/// selection from the grain key, parameter binding, INOUT result values and row decoding.
/// </summary>
public class ReminderTableTests
{
    private const string ShardSetKey = "Reminders";
    private static readonly DateTime StartAt = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] ReminderColumns = ["grain_key", "grain_type", "reminder_name", "start_time", "period", "version"];

    private static (ArgentSeaOrleansReminderTable table, FakePgShardSets shards) Build()
    {
        var shards = FakePgShardSets.Create(ShardSetKey, 1, 2, 3);
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
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    public void AMissingShardSetFailsConstruction()
    {
        var act = () => new ArgentSeaOrleansReminderTable(FakePgShardSets.Create("Other", 1),
            Options.Create(new OrleansShardPersistenceOptions { ShardSetKey = ShardSetKey }), NullLogger<ArgentSeaOrleansReminderTable>.Instance);

        act.Should().Throw<KeyNotFoundException>().WithMessage("*Reminders*");
    }

    [Fact]
    [Trait("Spec", "blk-d2e041c4703b4a5ab9ef5178f10688aa")]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    public async Task ReadRowSelectsTheGrainShardAndDecodesTheMatchingRow()
    {
        var (table, shards) = Build();
        var grainId = Grain(3, 9);
        shards.Provider.Script.ForQuery(Queries.OrleansReminderReadRow, 3).Returns(ReminderColumns, Row(grainId, "daily", 11));

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
        cmd.CommandText.Should().Be(Queries.OrleansReminderReadRow.Sql);
        ((byte[])cmd.Parameter("p_grain_key")!).Should().Equal(grainId.Key.Value.ToArray());
        cmd.Parameter("p_grain_type").Should().Be("mygrain");
        cmd.Parameter("p_reminder_name").Should().Be("daily");
    }

    [Fact]
    [Trait("Spec", "blk-d2e041c4703b4a5ab9ef5178f10688aa")]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    public async Task ReadRowWithNoMatchingRowReturnsAnEntryBearingTheGrainId()
    {
        var (table, shards) = Build();
        var grainId = Grain(3, 9);
        shards.Provider.Script.ForQuery(Queries.OrleansReminderReadRow).ReturnsNoRows(ReminderColumns);

        var entry = await table.ReadRow(grainId, "daily");

        entry.Should().NotBeNull();
        entry!.GrainId.Should().Be(grainId);
        entry.ReminderName.Should().BeNull();
        entry.ETag.Should().BeNull();
    }

    [Fact]
    [Trait("Spec", "blk-d2e041c4703b4a5ab9ef5178f10688aa")]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    public async Task ReadRowsForAGrainReturnsEveryRowOfItsShardAndSkipsRowsWithoutAGrainType()
    {
        var (table, shards) = Build();
        var grainId = Grain(2, 5);
        shards.Provider.Script.ForQuery(Queries.OrleansReminderReadRows, 2).Returns(ReminderColumns,
            Row(grainId, "r1", 1),
            [grainId.Key.Value.ToArray(), null, "ghost", StartAt, 1L, 1],
            Row(grainId, "r2", 2));

        var data = await table.ReadRows(grainId);

        data.Reminders.Select(r => r.ReminderName).Should().BeEquivalentTo(["r1", "r2"]);
        var cmd = shards.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.ShardId.Should().Be(2);
        cmd.CommandText.Should().Be(Queries.OrleansReminderReadRows.Sql);
    }

    [Fact]
    [Trait("Spec", "blk-d2e041c4703b4a5ab9ef5178f10688aa")]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    public async Task ReadRowsForAHashRangeReturnsEveryRowOfEveryShard()
    {
        var (table, shards) = Build();
        var a = Grain(1, 1);
        var b = Grain(2, 2);
        shards.Provider.Script.ForQuery(Queries.OrleansReminderReadRangeRows, 1).Returns(ReminderColumns, Row(a, "a1", 1), Row(a, "a2", 2));
        shards.Provider.Script.ForQuery(Queries.OrleansReminderReadRangeRows, 2).Returns(ReminderColumns, Row(b, "b1", 3));
        shards.Provider.Script.ForQuery(Queries.OrleansReminderReadRangeRows, 3).ReturnsNoRows(ReminderColumns);

        var data = await table.ReadRows(10, uint.MaxValue);

        data.Reminders.Select(r => r.ReminderName).Should().BeEquivalentTo(["a1", "a2", "b1"], "all rows of each shard are drained");
        var commands = shards.Provider.Recorder.Commands;
        commands.Select(c => c.ShardId).Should().BeEquivalentTo(new short?[] { 1, 2, 3 }, "the range read fans out to every read shard");
        commands.Should().OnlyContain(c => Equals(c.Parameter("p_begin_hash"), 10L) && Equals(c.Parameter("p_end_hash"), (long)uint.MaxValue), "the uint hash bounds are widened to bigint");
    }

    [Fact]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    public async Task RemoveRowSubmitsTheVersionToTheGrainShardAndReturnsTheInOutFoundFlag()
    {
        var (table, shards) = Build();
        var grainId = Grain(3, 9);
        shards.Provider.Script.ForQuery(Queries.OrleansReminderDeleteRow, 3).WithOutput("p_is_found", false);

        var missing = await table.RemoveRow(grainId, "daily", "5");

        missing.Should().BeFalse();
        var cmd = shards.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.ShardId.Should().Be(3);
        cmd.Connection.Access.Should().Be(FakeConnectionAccess.Write);
        cmd.CommandText.Should().Be(Queries.OrleansReminderDeleteRow.Sql);
        cmd.Parameter("p_version").Should().Be(5);
        cmd.Parameters.Single(p => p.ParameterName == "p_is_found").Direction.Should().Be(ParameterDirection.InputOutput, "a procedure's INOUT parameter must be passed as a CALL argument");

        shards.Provider.Script.ForQuery(Queries.OrleansReminderDeleteRow, 3).WithOutput("p_is_found", true);
        (await table.RemoveRow(grainId, "daily", "5")).Should().BeTrue();
    }

    [Fact]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    [Trait("Spec", "blk-9b537eb07a79432ea036fb19f4304bf9")]
    public async Task UpsertRowWritesTheEntryInUtcToTheGrainShardAndReturnsTheNewVersion()
    {
        var (table, shards) = Build();
        var grainId = Grain(3, 9);
        shards.Provider.Script.ForQuery(Queries.OrleansReminderUpsertRow, 3).WithOutput("p_new_version", 7);
        var entry = new ReminderEntry { GrainId = grainId, ReminderName = "daily", StartAt = StartAt.ToLocalTime(), Period = TimeSpan.FromTicks(600_000_000L), ETag = null };

        var version = await table.UpsertRow(entry);

        version.Should().Be("7");
        var cmd = shards.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.ShardId.Should().Be(3);
        cmd.CommandText.Should().Be(Queries.OrleansReminderUpsertRow.Sql);
        cmd.Parameter("p_start_time").Should().Be(new DateTimeOffset(StartAt, TimeSpan.Zero), "a Local start time is converted to UTC");
        cmd.Parameter("p_period").Should().Be(600_000_000L);
        cmd.Parameter("p_grain_hash").Should().Be((long)grainId.GetUniformHashCode());
        var oldVersion = cmd.Parameter("p_old_version");
        (oldVersion is null || oldVersion is DBNull).Should().BeTrue("a new reminder has no old version");
        cmd.Parameters.Single(p => p.ParameterName == "p_new_version").Direction.Should().Be(ParameterDirection.InputOutput);

        entry.ETag = "7";
        shards.Provider.Script.ForQuery(Queries.OrleansReminderUpsertRow, 3).WithOutput("p_new_version", 8);
        (await table.UpsertRow(entry)).Should().Be("8");
        shards.Provider.Recorder.Commands[1].Parameter("p_old_version").Should().Be(7);
    }

    [Fact]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    public async Task TestOnlyClearTableCompletesWithoutInvokingAnyQuery()
    {
        var (table, shards) = Build();

        await table.TestOnlyClearTable();

        shards.Provider.Recorder.Commands.Should().BeEmpty();
    }
}
