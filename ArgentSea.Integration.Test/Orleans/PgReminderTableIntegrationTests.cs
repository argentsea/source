using ArgentSea.Integration.Test.Fixtures;
using ArgentSea.Orleans;
using ArgentSea.Orleans.Pg;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;

namespace ArgentSea.Integration.Test.Orleans;

/// <summary>
/// ArgentSea.Orleans.Pg/src/tenant.psql is deployed as shipped into two tenant databases (shards 0 and 1 of a PostgreSQL shard
/// set) and the PostgreSQL ArgentSeaOrleansReminderTable is driven against the real routines, connected as a login holding
/// only the orleans_writer role. These are the PostgreSQL ports of the SQL Server ReminderTableIntegrationTests.
/// Grain keys are serialized ShardKeys, which is how the table picks the shard.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public class PgReminderTableIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private ArgentSeaOrleansReminderTable? _table;

    public PgReminderTableIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }
        await _fixture.EnsureOrleansScriptsAsync();
        var shardSets = _fixture.ShardSets("Tenants",
            [(0, PostgreSqlFixture.OrleansTenant0DatabaseName), (1, PostgreSqlFixture.OrleansTenant1DatabaseName)],
            PostgreSqlFixture.WriterLogin, PostgreSqlFixture.LoginPassword);
        _table = new ArgentSeaOrleansReminderTable(shardSets, Options.Create(new OrleansShardPersistenceOptions { ShardSetKey = "Tenants" }), NullLogger<ArgentSeaOrleansReminderTable>.Instance);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateTime T0 = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A grain whose key is a UTF8-encoded ShardKey (the form ArgentSea.Orleans persistence produces), so GrainId.ShardId() resolves the shard. Each call yields a fresh grain.</summary>
    private static GrainId Grain(short shardId, string type = "it.reminder")
        => GrainId.Create(GrainType.Create(type), new IdSpan(new ShardKey<Guid>(shardId, Guid.NewGuid()).ToUtf8().ToArray()));

    private static ReminderEntry Reminder(GrainId grain, string name, TimeSpan? period = null, string? etag = null) => new()
    {
        GrainId = grain,
        ReminderName = name,
        StartAt = T0,
        Period = period ?? TimeSpan.FromMinutes(5),
        ETag = etag,
    };

    [SkippableFact]
    [Trait("Spec", "blk-9b537eb07a79432ea036fb19f4304bf9")]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    [Trait("Spec", "blk-d2e041c4703b4a5ab9ef5178f10688aa")]
    public async Task UpsertRowInsertsThenUpdatesAndReadRowReturnsTheStoredReminder()
    {
        _fixture.SkipUnlessAvailable();
        var grain = Grain(1);

        var insertedEtag = await _table!.UpsertRow(Reminder(grain, "daily"));
        var stored = await _table!.ReadRow(grain, "daily");
        var updatedEtag = await _table!.UpsertRow(Reminder(grain, "daily", TimeSpan.FromMinutes(10), insertedEtag));
        var updated = await _table!.ReadRow(grain, "daily");

        insertedEtag.Should().Be("0", "a new reminder starts at version 0 and the upsert returns the resulting version");
        stored.Should().NotBeNull();
        stored!.GrainId.Should().Be(grain);
        stored.ReminderName.Should().Be("daily");
        stored.StartAt.Should().Be(T0);
        stored.StartAt.Kind.Should().Be(DateTimeKind.Utc);
        stored.Period.Should().Be(TimeSpan.FromMinutes(5));
        stored.ETag.Should().Be("0");
        updatedEtag.Should().Be("1", "an update with the current version advances it by one");
        updated!.Period.Should().Be(TimeSpan.FromMinutes(10));
        updated.ETag.Should().Be("1");
    }

    [SkippableFact]
    [Trait("Spec", "blk-9b537eb07a79432ea036fb19f4304bf9")]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    [Trait("Spec", "blk-d2e041c4703b4a5ab9ef5178f10688aa")]
    public async Task UpsertRowWithAStaleEtagFails()
    {
        _fixture.SkipUnlessAvailable();
        var grain = Grain(0);
        var etag = await _table!.UpsertRow(Reminder(grain, "stale"));
        await _table!.UpsertRow(Reminder(grain, "stale", etag: etag));

        Func<Task> act = () => _table!.UpsertRow(Reminder(grain, "stale", etag: etag));

        await act.Should().ThrowAsync<Exception>("the stored version moved on and the procedure raises its concurrency error");
        (await _table!.ReadRow(grain, "stale"))!.ETag.Should().Be("1", "the failed upsert changed nothing");
    }

    [SkippableFact]
    [Trait("Spec", "blk-9b537eb07a79432ea036fb19f4304bf9")]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    [Trait("Spec", "blk-d2e041c4703b4a5ab9ef5178f10688aa")]
    public async Task UpsertRowAcceptsAGrainHashAboveInt32MaxValue()
    {
        _fixture.SkipUnlessAvailable();
        // GrainId.GetUniformHashCode() is a uint; the table stores it as bigint, so a hash above int.MaxValue must be accepted.
        GrainId grain;
        do
        {
            grain = Grain(0, "it.highhash");
        } while (grain.GetUniformHashCode() <= int.MaxValue);

        var etag = await _table!.UpsertRow(Reminder(grain, "high"));
        var rows = await _table!.ReadRows(grain);

        etag.Should().Be("0");
        rows.Reminders.Should().ContainSingle().Which.ReminderName.Should().Be("high");
    }

    [SkippableFact]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    [Trait("Spec", "blk-d2e041c4703b4a5ab9ef5178f10688aa")]
    public async Task ReadRowForAMissingReminderReturnsAnEntryBearingTheGrainId()
    {
        _fixture.SkipUnlessAvailable();
        var grain = Grain(1);

        var entry = await _table!.ReadRow(grain, "missing");

        entry.Should().NotBeNull();
        entry!.GrainId.Should().Be(grain);
        entry.ReminderName.Should().BeNull();
        entry.ETag.Should().BeNull();
    }

    [SkippableFact]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    [Trait("Spec", "blk-d2e041c4703b4a5ab9ef5178f10688aa")]
    public async Task ReadRowsByGrainReturnsEveryReminderOfThatGrainOnly()
    {
        _fixture.SkipUnlessAvailable();
        var grain = Grain(0);
        var neighbour = Grain(0);
        await _table!.UpsertRow(Reminder(grain, "a1"));
        await _table!.UpsertRow(Reminder(grain, "a2"));
        await _table!.UpsertRow(Reminder(neighbour, "b1"));

        var rows = await _table!.ReadRows(grain);

        rows.Reminders.Select(r => r.ReminderName).Should().BeEquivalentTo(new[] { "a1", "a2" });
        rows.Reminders.Should().OnlyContain(r => r.GrainId == grain);
    }

    [SkippableFact]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    [Trait("Spec", "blk-d2e041c4703b4a5ab9ef5178f10688aa")]
    public async Task ReadRowsByHashRangeCollectsEveryMatchingReminderFromBothShards()
    {
        _fixture.SkipUnlessAvailable();
        var type = "it.range." + Guid.NewGuid().ToString("N").Substring(0, 8);
        var onShard0 = Grain(0, type);
        var onShard1 = Grain(1, type);
        await _table!.UpsertRow(Reminder(onShard0, "s0-a"));
        await _table!.UpsertRow(Reminder(onShard0, "s0-b"));
        await _table!.UpsertRow(Reminder(onShard1, "s1-a"));
        await _table!.UpsertRow(Reminder(onShard1, "s1-b"));
        var hash0 = onShard0.GetUniformHashCode();
        var hash1 = onShard1.GetUniformHashCode();

        // begin == end wraps around and therefore covers the whole ring.
        var everything = await _table!.ReadRows(0u, 0u);
        // (begin, end] is exclusive of begin and inclusive of end.
        var onlyShard0 = await _table!.ReadRows(hash0 - 1, hash0);
        var wrapped = await _table!.ReadRows(hash0, hash0);

        var mine = everything.Reminders.Where(r => r.GrainId.Type.ToString() == type).Select(r => r.ReminderName);
        mine.Should().BeEquivalentTo(new[] { "s0-a", "s0-b", "s1-a", "s1-b" }, "every shard contributes all of its matching rows");
        onlyShard0.Reminders.Where(r => r.GrainId.Type.ToString() == type).Select(r => r.ReminderName).Should().BeEquivalentTo(new[] { "s0-a", "s0-b" });
        if (hash1 != hash0)
        {
            onlyShard0.Reminders.Should().NotContain(r => r.GrainId == onShard1);
        }
        wrapped.Reminders.Where(r => r.GrainId.Type.ToString() == type).Should().NotBeEmpty("a single-point range wraps and covers the ring, matching Orleans' ring semantics");
    }

    [SkippableFact]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    [Trait("Spec", "blk-d2e041c4703b4a5ab9ef5178f10688aa")]
    public async Task RemoveRowIsConditionalOnTheVersionAndReportsWhetherARowWasFound()
    {
        _fixture.SkipUnlessAvailable();
        var grain = Grain(1);
        var etag = await _table!.UpsertRow(Reminder(grain, "gone"));

        var wrongVersion = await _table!.RemoveRow(grain, "gone", "42");
        var removed = await _table!.RemoveRow(grain, "gone", etag!);
        var again = await _table!.RemoveRow(grain, "gone", etag!);
        var after = await _table!.ReadRow(grain, "gone");

        wrongVersion.Should().BeFalse("a version that does not match leaves the row in place");
        removed.Should().BeTrue();
        again.Should().BeFalse("the row is gone");
        after!.ReminderName.Should().BeNull();
    }

    [SkippableFact]
    [Trait("Spec", "blk-be4943a5cfd048578a53e790169e33bd")]
    [Trait("Spec", "blk-a6a660767f734b97b189a4b178764a32")]
    [Trait("Spec", "blk-d2e041c4703b4a5ab9ef5178f10688aa")]
    public async Task TestOnlyClearTableIsANoOp()
    {
        _fixture.SkipUnlessAvailable();
        var grain = Grain(0);
        await _table!.UpsertRow(Reminder(grain, "kept"));

        await _table!.TestOnlyClearTable();

        (await _table!.ReadRow(grain, "kept"))!.ETag.Should().Be("0", "nothing is cleared");
    }
}
