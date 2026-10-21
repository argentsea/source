using System.Net;
using ArgentSea.Integration.Test.Fixtures;
using ArgentSea.Orleans;
using ArgentSea.Orleans.Pg;
using ArgentSea.Pg;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Runtime;

namespace ArgentSea.Integration.Test.Orleans;

/// <summary>
/// ArgentSea.Orleans.Pg/src/common.psql is deployed as shipped into the PostgreSQL server and the PostgreSQL
/// ArgentSeaOrleansMembershipTable (plus its ArgentSeaGatewayListProvider) is driven against the real routines, connected as a
/// login holding only the orleans_writer role, so the script's grants are exercised too. These are the PostgreSQL ports of
/// the SQL Server MembershipTableIntegrationTests, plus the shared concurrency scenarios. Each test uses its own cluster id.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public class PgMembershipTableIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private PgDatabases? _databases;

    public PgMembershipTableIntegrationTests(PostgreSqlFixture fixture)
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
        _databases = _fixture.Databases("Common", PostgreSqlFixture.OrleansCommonDatabaseName, PostgreSqlFixture.WriterLogin, PostgreSqlFixture.LoginPassword);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateTime T0 = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private ArgentSeaOrleansMembershipTable Table(string clusterId)
        => new(_databases!, Options.Create(new ClusterOptions { ClusterId = clusterId, ServiceId = "it" }),
            Options.Create(new OrleansDbPersistenceOptions { DatabaseKey = "Common" }), NullLogger<ArgentSeaOrleansMembershipTable>.Instance);

    private static string NewClusterId() => "it-" + Guid.NewGuid().ToString("N").Substring(0, 12);

    private static SiloAddress Silo(string ip, int port, int generation) => SiloAddress.New(new IPEndPoint(IPAddress.Parse(ip), port), generation);

    private static MembershipEntry Entry(SiloAddress address, string name, SiloStatus status = SiloStatus.Active, int proxyPort = 30000) => new()
    {
        SiloAddress = address,
        SiloName = name,
        HostName = name + ".host",
        Status = status,
        ProxyPort = proxyPort,
        StartTime = T0,
        IAmAliveTime = T0.AddSeconds(5),
    };

    private static async Task<(ArgentSeaOrleansMembershipTable table, MembershipTableData data)> InitializedTable(ArgentSeaOrleansMembershipTable table)
    {
        await table.InitializeMembershipTable(true);
        return (table, await table.ReadAll());
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-9402c67a94344fe5a63790eb219c3ba6")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task InitializeCreatesTheVersionRecordOnceAndReadAllReturnsItWithNoMembers()
    {
        _fixture.SkipUnlessAvailable();
        var table = Table(NewClusterId());

        await table.InitializeMembershipTable(true);
        await table.InitializeMembershipTable(true);
        await table.InitializeMembershipTable(false);
        var data = await table.ReadAll();

        data.Version.Version.Should().Be(0);
        data.Version.VersionEtag.Should().Be("0");
        data.Members.Should().BeEmpty();
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-9402c67a94344fe5a63790eb219c3ba6")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task ReadAllWithoutAnInitializedVersionRecordThrows()
    {
        _fixture.SkipUnlessAvailable();
        var table = Table(NewClusterId());

        Func<Task> act = () => table.ReadAll();

        await act.Should().ThrowAsync<OrleansException>().WithMessage("*membership version table was not found*");
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-9402c67a94344fe5a63790eb219c3ba6")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task InsertRowStoresTheEntryAndReadRowAndReadAllReturnIt()
    {
        _fixture.SkipUnlessAvailable();
        var (table, initial) = await InitializedTable(Table(NewClusterId()));
        var address = Silo("10.0.0.1", 11111, 7);
        var entry = Entry(address, "silo-a");

        var inserted = await table.InsertRow(entry, initial.Version.Next());
        var row = await table.ReadRow(address);
        var all = await table.ReadAll();

        inserted.Should().BeTrue();
        row.Version.Version.Should().Be(1, "the insert advanced the table version");
        var (member, etag) = row.Members.Should().ContainSingle().Which;
        member.SiloAddress.Should().Be(address);
        member.SiloName.Should().Be("silo-a");
        member.HostName.Should().Be("silo-a.host");
        member.Status.Should().Be(SiloStatus.Active);
        member.ProxyPort.Should().Be(30000);
        member.StartTime.Should().Be(T0);
        member.StartTime.Kind.Should().Be(DateTimeKind.Utc);
        member.IAmAliveTime.Should().Be(T0.AddSeconds(5));
        member.SuspectTimes.Should().BeNullOrEmpty();
        etag.Should().Be(row.Version.VersionEtag, "the member row carries the table etag it was inserted under");
        all.Members.Should().ContainSingle().Which.Item1.SiloAddress.Should().Be(address);
        all.Version.Version.Should().Be(1);
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task InsertRowReturnsFalseForAnEtagMismatchOrAnExistingSilo()
    {
        _fixture.SkipUnlessAvailable();
        var (table, initial) = await InitializedTable(Table(NewClusterId()));
        var address = Silo("10.0.0.2", 11111, 1);

        var staleEtag = await table.InsertRow(Entry(address, "silo-b"), new TableVersion(1, "99"));
        var first = await table.InsertRow(Entry(address, "silo-b"), initial.Version.Next());
        var duplicate = await table.InsertRow(Entry(address, "silo-b-again"), (await table.ReadAll()).Version.Next());

        staleEtag.Should().BeFalse("a TableVersion etag that does not match the stored one is a concurrency conflict");
        first.Should().BeTrue();
        duplicate.Should().BeFalse("a row for the same address, port and generation already exists");
        (await table.ReadAll()).Members.Should().ContainSingle().Which.Item1.SiloName.Should().Be("silo-b");
    }

    [SkippableFact]
    [Trait("Spec", "blk-9402c67a94344fe5a63790eb219c3ba6")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task ReadRowForAnUnknownSiloThrows()
    {
        _fixture.SkipUnlessAvailable();
        var (table, _) = await InitializedTable(Table(NewClusterId()));

        Func<Task> act = () => table.ReadRow(Silo("10.9.9.9", 1, 1));

        await act.Should().ThrowAsync<OrleansException>().WithMessage("*membership record was not found*");
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task UpdateRowReplacesTheEntryAndReturnsFalseOnAnEtagConflict()
    {
        _fixture.SkipUnlessAvailable();
        var (table, initial) = await InitializedTable(Table(NewClusterId()));
        var address = Silo("10.0.0.3", 11111, 2);
        (await table.InsertRow(Entry(address, "silo-c"), initial.Version.Next())).Should().BeTrue();
        var current = await table.ReadRow(address);
        var (stored, rowEtag) = current.Members.Single();

        stored.Status = SiloStatus.ShuttingDown;
        stored.ProxyPort = 30001;
        stored.IAmAliveTime = T0.AddMinutes(1);
        var updated = await table.UpdateRow(stored, rowEtag, current.Version.Next());
        var conflict = await table.UpdateRow(stored, rowEtag, new TableVersion(current.Version.Version + 2, "99"));
        var after = await table.ReadRow(address);

        updated.Should().BeTrue();
        conflict.Should().BeFalse("a TableVersion etag that does not match is reported as a conflict, not thrown");
        after.Version.Version.Should().Be(current.Version.Version + 1);
        var member = after.Members.Single().Item1;
        member.Status.Should().Be(SiloStatus.ShuttingDown);
        member.ProxyPort.Should().Be(30001);
        member.IAmAliveTime.Should().Be(T0.AddMinutes(1));
        member.SiloName.Should().Be("silo-c", "the update does not rename the silo");
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task UpdateRowForAnUnknownSiloIsAConflict()
    {
        _fixture.SkipUnlessAvailable();
        var (table, initial) = await InitializedTable(Table(NewClusterId()));

        var result = await table.UpdateRow(Entry(Silo("10.0.0.4", 1, 1), "ghost"), "0", initial.Version.Next());

        result.Should().BeFalse("the procedure raises its concurrency signal when the member row is missing");
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-9402c67a94344fe5a63790eb219c3ba6")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task SuspectTimesRoundTripThroughUpdateRowReadRowAndReadAll()
    {
        _fixture.SkipUnlessAvailable();
        var (table, initial) = await InitializedTable(Table(NewClusterId()));
        var address = Silo("10.0.0.5", 11111, 3);
        var suspectorA = Silo("10.0.0.1", 1111, 1);
        var suspectorB = Silo("fe80::1234:5678:9abc:def0", 2222, 2);
        (await table.InsertRow(Entry(address, "silo-e"), initial.Version.Next())).Should().BeTrue();
        var current = await table.ReadRow(address);
        var (stored, rowEtag) = current.Members.Single();
        stored.SuspectTimes =
        [
            Tuple.Create(suspectorA, T0.AddMinutes(10)),
            Tuple.Create(suspectorA, T0.AddMinutes(11)), // the same suspector re-votes: the latest report wins
            Tuple.Create(suspectorB, T0.AddMinutes(10).AddSeconds(30)),
        ];

        (await table.UpdateRow(stored, rowEtag, current.Version.Next())).Should().BeTrue();
        var row = await table.ReadRow(address);
        var all = await table.ReadAll();

        var expected = new[] { (suspectorA, T0.AddMinutes(11)), (suspectorB, T0.AddMinutes(10).AddSeconds(30)) };
        row.Members.Single().Item1.SuspectTimes!.Select(s => (s.Item1, s.Item2)).Should().BeEquivalentTo(expected, "one row per distinct suspector with its latest vote");
        all.Members.Single().Item1.SuspectTimes!.Select(s => (s.Item1, s.Item2)).Should().BeEquivalentTo(expected);

        // A later update that drops suspector A keeps only B.
        var again = await table.ReadRow(address);
        var (stored2, rowEtag2) = again.Members.Single();
        stored2.SuspectTimes = [Tuple.Create(suspectorB, T0.AddMinutes(12))];
        (await table.UpdateRow(stored2, rowEtag2, again.Version.Next())).Should().BeTrue();
        (await table.ReadRow(address)).Members.Single().Item1.SuspectTimes!.Select(s => (s.Item1, s.Item2))
            .Should().BeEquivalentTo(new[] { (suspectorB, T0.AddMinutes(12)) }, "suspicions absent from the update are removed and present ones are refreshed");
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task UpdateIAmAliveChangesOnlyTheLivenessTime()
    {
        _fixture.SkipUnlessAvailable();
        var (table, initial) = await InitializedTable(Table(NewClusterId()));
        var address = Silo("10.0.0.6", 11111, 1);
        var entry = Entry(address, "silo-f");
        (await table.InsertRow(entry, initial.Version.Next())).Should().BeTrue();

        entry.IAmAliveTime = T0.AddHours(1);
        await table.UpdateIAmAlive(entry);
        var row = await table.ReadRow(address);

        row.Members.Single().Item1.IAmAliveTime.Should().Be(T0.AddHours(1));
        row.Members.Single().Item1.Status.Should().Be(SiloStatus.Active);
        row.Version.Version.Should().Be(1, "a liveness update does not advance the table version");
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task CleanupDefunctSiloEntriesRemovesDeadSilosOlderThanTheCutoffAndKeepsActiveOnes()
    {
        _fixture.SkipUnlessAvailable();
        var (table, initial) = await InitializedTable(Table(NewClusterId()));
        var dead = Silo("10.0.0.7", 11111, 1);
        var active = Silo("10.0.0.8", 11111, 1);
        var recentlyDead = Silo("10.0.0.9", 11111, 1);
        (await table.InsertRow(Entry(dead, "dead", SiloStatus.Dead), initial.Version.Next())).Should().BeTrue();
        var v = (await table.ReadAll()).Version;
        var activeEntry = Entry(active, "active");
        activeEntry.IAmAliveTime = T0.AddDays(-30); // old but still Active, so it must survive
        (await table.InsertRow(activeEntry, v.Next())).Should().BeTrue();
        v = (await table.ReadAll()).Version;
        var recent = Entry(recentlyDead, "recently-dead", SiloStatus.Dead);
        recent.IAmAliveTime = T0.AddDays(1);
        (await table.InsertRow(recent, v.Next())).Should().BeTrue();

        await table.CleanupDefunctSiloEntries(new DateTimeOffset(T0.AddHours(1)));
        var all = await table.ReadAll();

        all.Members.Select(m => m.Item1.SiloName).Should().BeEquivalentTo(new[] { "active", "recently-dead" }, "only non-active silos whose IAmAliveTime precedes the cutoff are removed");
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task DeleteMembershipTableEntriesRemovesEveryRecordOfTheCluster()
    {
        _fixture.SkipUnlessAvailable();
        var clusterId = NewClusterId();
        var (table, initial) = await InitializedTable(Table(clusterId));
        (await table.InsertRow(Entry(Silo("10.0.0.10", 11111, 1), "silo-j"), initial.Version.Next())).Should().BeTrue();
        var other = Table(NewClusterId());
        await other.InitializeMembershipTable(true);

        await table.DeleteMembershipTableEntries(clusterId);

        Func<Task> act = () => table.ReadAll();
        await act.Should().ThrowAsync<OrleansException>("the version record was deleted with the members");
        (await other.ReadAll()).Version.Version.Should().Be(0, "other clusters are untouched");
    }

    [SkippableFact]
    [Trait("Spec", "blk-9b537eb07a79432ea036fb19f4304bf9")]
    [Trait("Spec", "blk-9402c67a94344fe5a63790eb219c3ba6")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task TimestampsOfEveryKindAreStoredAsUtcAndReadBackAsUtc()
    {
        _fixture.SkipUnlessAvailable();
        var (table, initial) = await InitializedTable(Table(NewClusterId()));
        var address = Silo("10.0.0.11", 11111, 1);
        var entry = Entry(address, "kinds");
        var local = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Local);
        entry.StartTime = DateTime.SpecifyKind(T0, DateTimeKind.Unspecified);
        entry.IAmAliveTime = local;

        (await table.InsertRow(entry, initial.Version.Next())).Should().BeTrue();
        var member = (await table.ReadRow(address)).Members.Single().Item1;

        member.StartTime.Should().Be(T0, "an Unspecified time is taken to be UTC already");
        member.StartTime.Kind.Should().Be(DateTimeKind.Utc);
        member.IAmAliveTime.Should().Be(local.ToUniversalTime(), "a Local time is converted to the same instant in UTC");
        member.IAmAliveTime.Kind.Should().Be(DateTimeKind.Utc);
    }

    /// <summary>Everything a failed write must leave untouched: the table version, and each member row with its suspicions.</summary>
    private static async Task<string> StateOf(ArgentSeaOrleansMembershipTable table)
    {
        var all = await table.ReadAll();
        var members = all.Members
            .OrderBy(m => m.Item1.SiloName, StringComparer.Ordinal)
            .Select(m => $"{m.Item1.SiloAddress}|{m.Item1.Status}|{m.Item1.ProxyPort}|{m.Item1.IAmAliveTime:O}|etag={m.Item2}|suspects=" +
                string.Join(",", (m.Item1.SuspectTimes ?? []).OrderBy(s => s.Item1.ToString(), StringComparer.Ordinal).Select(s => $"{s.Item1}@{s.Item2:O}")));
        return $"v={all.Version.Version} etag={all.Version.VersionEtag}; " + string.Join("; ", members);
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-9402c67a94344fe5a63790eb219c3ba6")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task EveryWriteAdvancesTheTableEtagSoASecondWriteFromTheSameReadFails()
    {
        _fixture.SkipUnlessAvailable();
        var (table, initial) = await InitializedTable(Table(NewClusterId()));
        var silo = Silo("10.1.0.1", 11111, 1);

        (await table.InsertRow(Entry(silo, "first"), initial.Version.Next())).Should().BeTrue();
        var afterInsert = await table.ReadAll();
        (await table.InsertRow(Entry(Silo("10.1.0.2", 11111, 1), "second"), initial.Version.Next())).Should().BeFalse("the insert advanced the table etag, so the earlier read is stale");

        afterInsert.Version.VersionEtag.Should().NotBe(initial.Version.VersionEtag, "a successful insert advances the table etag");
        var (stored, rowEtag) = (await table.ReadRow(silo)).Members.Single();
        stored.Status = SiloStatus.ShuttingDown;
        (await table.UpdateRow(stored, rowEtag, afterInsert.Version.Next())).Should().BeTrue();
        (await table.UpdateRow(stored, rowEtag, afterInsert.Version.Next())).Should().BeFalse("the update advanced the table etag and the member's own etag");
        (await table.ReadAll()).Version.VersionEtag.Should().NotBe(afterInsert.Version.VersionEtag, "a successful update advances the table etag");
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task UpdateRowChecksTheMemberRowsOwnEtagWhichMayLagTheTableEtag()
    {
        _fixture.SkipUnlessAvailable();
        var (table, initial) = await InitializedTable(Table(NewClusterId()));
        var a = Silo("10.2.0.1", 11111, 1);
        (await table.InsertRow(Entry(a, "a"), initial.Version.Next())).Should().BeTrue();
        var (_, aEtag) = (await table.ReadRow(a)).Members.Single();
        (await table.InsertRow(Entry(Silo("10.2.0.2", 11111, 1), "b"), (await table.ReadAll()).Version.Next())).Should().BeTrue();
        var current = await table.ReadAll();
        current.Version.VersionEtag.Should().NotBe(aEtag, "inserting b advanced the table etag but did not touch a's row");
        var aEntry = current.Members.Single(m => m.Item1.SiloName == "a").Item1;

        var wrongMemberEtag = await table.UpdateRow(aEntry, "999", current.Version.Next());
        var lagging = await table.UpdateRow(aEntry, aEtag, current.Version.Next());

        wrongMemberEtag.Should().BeFalse("the member row's stored etag does not match the etag argument");
        lagging.Should().BeTrue("a member etag that lags the table etag is the normal case and is accepted");
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-9402c67a94344fe5a63790eb219c3ba6")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task OfSimultaneousWritersFromOneReadExactlyOneSucceeds()
    {
        _fixture.SkipUnlessAvailable();
        var (table, initial) = await InitializedTable(Table(NewClusterId()));
        var next = initial.Version.Next();

        var inserts = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => Task.Run(() => table.InsertRow(Entry(Silo($"10.3.0.{i}", 11111, 1), $"racer-{i}"), next))));

        inserts.Count(r => r).Should().Be(1, "every insert started from the same read, so only the first to commit may succeed");
        (await table.ReadAll()).Members.Should().ContainSingle();

        var current = await table.ReadAll();
        var (member, memberEtag) = current.Members.Single();
        var updates = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => Task.Run(() =>
        {
            var copy = new MembershipEntry
            {
                SiloAddress = member.SiloAddress, SiloName = member.SiloName, HostName = member.HostName, Status = SiloStatus.Active,
                ProxyPort = 30000 + i, StartTime = member.StartTime, IAmAliveTime = member.IAmAliveTime,
            };
            return table.UpdateRow(copy, memberEtag, current.Version.Next());
        })));

        updates.Count(r => r).Should().Be(1, "every update started from the same read");
    }

    [SkippableFact]
    [Trait("Spec", "blk-72228c3783af4a3495233be3754c956d")]
    [Trait("Spec", "blk-9402c67a94344fe5a63790eb219c3ba6")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task AFailedWriteLeavesTheVersionMemberAndSuspicionRowsUnchanged()
    {
        _fixture.SkipUnlessAvailable();
        var (table, initial) = await InitializedTable(Table(NewClusterId()));
        var silo = Silo("10.4.0.1", 11111, 1);
        (await table.InsertRow(Entry(silo, "subject"), initial.Version.Next())).Should().BeTrue();
        var read = await table.ReadRow(silo);
        var (stored, rowEtag) = read.Members.Single();
        stored.SuspectTimes = [Tuple.Create(Silo("10.4.0.9", 1, 1), T0.AddMinutes(1))];
        (await table.UpdateRow(stored, rowEtag, read.Version.Next())).Should().BeTrue();
        var current = await table.ReadAll();
        var (member, memberEtag) = current.Members.Single();
        var before = await StateOf(table);

        var changed = new MembershipEntry
        {
            SiloAddress = member.SiloAddress, SiloName = member.SiloName, HostName = member.HostName, Status = SiloStatus.Dead,
            ProxyPort = 1, StartTime = member.StartTime, IAmAliveTime = T0.AddDays(1),
            SuspectTimes = [Tuple.Create(Silo("10.4.0.8", 2, 2), T0.AddMinutes(2))],
        };
        var staleTable = await table.UpdateRow(changed, memberEtag, new TableVersion(current.Version.Version + 1, "999"));
        var staleMember = await table.UpdateRow(changed, "999", current.Version.Next());
        var missing = await table.UpdateRow(Entry(Silo("10.4.0.2", 1, 1), "ghost"), memberEtag, current.Version.Next());
        var duplicate = await table.InsertRow(Entry(silo, "subject-again"), current.Version.Next());

        new[] { staleTable, staleMember, missing, duplicate }.Should().AllBeEquivalentTo(false);
        (await StateOf(table)).Should().Be(before, "a write that fails any check commits nothing, not even the table-version change it made first");
    }

    [SkippableFact]
    [Trait("Spec", "blk-cbb4c09c00364a0cbf014fcf1c25a56e")]
    [Trait("Spec", "blk-ffa9b2fc0d09490da2c1470af1542cfb")]
    [Trait("Spec", "blk-d1a256b10cce4c5c9f9ec51f25e89fc6")]
    public async Task GatewayListProviderListsTheActiveSilosOfTheClusterAsTheClientLogin()
    {
        _fixture.SkipUnlessAvailable();
        var clusterId = NewClusterId();
        var (table, initial) = await InitializedTable(Table(clusterId));
        var activeSilo = Silo("127.0.0.1", 11111, 7);
        (await table.InsertRow(Entry(activeSilo, "gateway", SiloStatus.Active, 30000), initial.Version.Next())).Should().BeTrue();
        var v = (await table.ReadAll()).Version;
        (await table.InsertRow(Entry(Silo("127.0.0.2", 11112, 8), "joining", SiloStatus.Joining, 30000), v.Next())).Should().BeTrue();
        var provider = new ArgentSeaGatewayListProvider(
            Options.Create(new PgDbConnectionOptions()),
            Options.Create(new PgGlobalPropertiesOptions()),
            Options.Create(new ClusterClientDbOptions { DatabaseKey = "Common", ConnectionString = _fixture.ConnectionStringFor(PostgreSqlFixture.OrleansCommonDatabaseName, PostgreSqlFixture.ClientLogin) }), // GatewayListQuery unset: the package default applies
            Options.Create(new ClusterOptions { ClusterId = clusterId, ServiceId = "it" }),
            NullLogger<ArgentSeaGatewayListProvider>.Instance);

        await provider.InitializeGatewayListProvider();
        var gateways = await provider.GetGateways();

        gateways.Should().ContainSingle().Which.Should().Be(SiloAddress.New(new IPEndPoint(IPAddress.Loopback, 30000), 7).ToGatewayUri(), "the gateway URI uses the silo's proxy port and generation");
        provider.IsUpdatable.Should().BeTrue();
        provider.MaxStaleness.Should().Be(TimeSpan.FromSeconds(60));
    }
}
