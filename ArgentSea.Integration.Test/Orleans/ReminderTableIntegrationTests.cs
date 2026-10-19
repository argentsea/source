using ArgentSea.Integration.Test.Fixtures;
using ArgentSea.Orleans;
using ArgentSea.Orleans.Sql;
using ArgentSea.Sql;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Runtime;

namespace ArgentSea.Integration.Test.Orleans;

/// <summary>
/// Spec section orleans-reminder-storage, integration part: ArgentSea.Orleans.Sql/src/tenant.sql is deployed as shipped into
/// two tenant databases (shards 0 and 1 of a SQL shard set) and ArgentSeaOrleansReminderTable is driven against the real
/// procedures: upsert with and without an etag, read by grain and name, read every reminder of a grain, read by hash range
/// across both shards, and conditional removal. Grain keys are serialized ShardKeys, which is how the table picks the shard.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ReminderTableIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private ArgentSeaOrleansReminderTable? _table;

    public ReminderTableIntegrationTests(SqlServerFixture fixture)
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
        var shardSets = _fixture.ShardSets("Tenants", (0, SqlServerFixture.Tenant0DatabaseName), (1, SqlServerFixture.Tenant1DatabaseName));
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
    [Trait("Spec", "blk-b04bedc77996402d9ce87a764727ec3a")]
    [Trait("Spec", "blk-315f0b666c68427ba94e3e85f87ecc72")]
    [Trait("Spec", "blk-578cd7199de843e6a7e8cba2a78851b7")]
    [Trait("Spec", "blk-7750a2740c1048cea01fdb839855bcf6")]
    [Trait("Spec", "blk-165ab70846224c2ba61f6c50a355fa45")]
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
        stored.Period.Should().Be(TimeSpan.FromMinutes(5));
        stored.ETag.Should().Be("0");
        updatedEtag.Should().Be("1", "an update with the current version advances it by one");
        updated!.Period.Should().Be(TimeSpan.FromMinutes(10));
        updated.ETag.Should().Be("1");
    }

    [SkippableFact]
    [Trait("Spec", "blk-b04bedc77996402d9ce87a764727ec3a")]
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
    [Trait("Spec", "blk-b04bedc77996402d9ce87a764727ec3a")]
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
    [Trait("Spec", "blk-10774f1f917147f8aa5ea030c3f5baec")]
    [Trait("Spec", "blk-578cd7199de843e6a7e8cba2a78851b7")]
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
    [Trait("Spec", "blk-b9fca0761f3641f18aa98f1916ecef9b")]
    [Trait("Spec", "blk-165ab70846224c2ba61f6c50a355fa45")]
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
    [Trait("Spec", "blk-7131c458eb9c4d35b5c2f516c54d01a6")]
    [Trait("Spec", "blk-b9fca0761f3641f18aa98f1916ecef9b")]
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
        var nothing = await _table!.ReadRows(hash0, hash0 == uint.MaxValue ? hash0 : hash0);

        var mine = everything.Reminders.Where(r => r.GrainId.Type.ToString() == type).Select(r => r.ReminderName);
        mine.Should().BeEquivalentTo(new[] { "s0-a", "s0-b", "s1-a", "s1-b" }, "every shard contributes all of its matching rows");
        onlyShard0.Reminders.Where(r => r.GrainId.Type.ToString() == type).Select(r => r.ReminderName).Should().BeEquivalentTo(new[] { "s0-a", "s0-b" });
        if (hash1 != hash0)
        {
            onlyShard0.Reminders.Should().NotContain(r => r.GrainId == onShard1);
        }
        nothing.Reminders.Where(r => r.GrainId.Type.ToString() == type).Should().NotBeEmpty("a single-point range wraps and covers the ring, matching Orleans' ring semantics");
    }

    [SkippableFact]
    [Trait("Spec", "blk-9417c0653239458ca295acaccb2ca6cb")]
    [Trait("Spec", "blk-0cdfc06ce27c464094fc3351be37e58f")]
    [Trait("Spec", "blk-165ab70846224c2ba61f6c50a355fa45")]
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
    [Trait("Spec", "blk-2d63393ce9914d1191f66ccc8e7d71d6")]
    [Trait("Spec", "blk-d2375a38770f4255956ffc8ed666fba9")]
    public async Task TestOnlyClearTableIsANoOp()
    {
        _fixture.SkipUnlessAvailable();
        var grain = Grain(0);
        await _table!.UpsertRow(Reminder(grain, "kept"));

        await _table!.TestOnlyClearTable();

        (await _table!.ReadRow(grain, "kept"))!.ETag.Should().Be("0", "nothing is cleared");
    }
}
