using System.Net;
using ArgentSea.Integration.Test.Fixtures;
using ArgentSea.Orleans;
using ArgentSea.Orleans.Sql;
using ArgentSea.Sql;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Runtime;

namespace ArgentSea.Integration.Test.Orleans;

/// <summary>
/// Spec section orleans-cluster-providers, integration part: ArgentSea.Orleans.Sql/src/common.sql is deployed as shipped into
/// the SQL Server container and ArgentSeaOrleansMembershipTable (plus ArgentSeaGatewayListProvider) is driven against the real
/// procedures. This is the only place the C# parameter names, result-set ordinals and types are checked against the scripts.
/// Each test uses its own cluster id, so the tests share the deployed database without interfering.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MembershipTableIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;
    private SqlDatabases? _databases;

    public MembershipTableIntegrationTests(SqlServerFixture fixture)
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
        _databases = _fixture.Databases(("Common", SqlServerFixture.CommonDatabaseName));
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
    [Trait("Spec", "blk-3fc167cbdf3e49c6a8e47b4bb8fb2396")]
    [Trait("Spec", "blk-48618b09dcfc422591d98f6a12b2decb")]
    [Trait("Spec", "blk-0384635003634d9fb24160c19a2605d9")]
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
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
    [Trait("Spec", "blk-9f9dec6bf4664bfc8e80a06ec25f0500")]
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
    public async Task ReadAllWithoutAnInitializedVersionRecordThrows()
    {
        _fixture.SkipUnlessAvailable();
        var table = Table(NewClusterId());

        Func<Task> act = () => table.ReadAll();

        await act.Should().ThrowAsync<OrleansException>().WithMessage("*membership version table was not found*");
    }

    [SkippableFact]
    [Trait("Spec", "blk-b0466f322fc64ca7a7909149b65eba64")]
    [Trait("Spec", "blk-007f49e3cc044a36a51bac6992318b6e")]
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
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
        member.IAmAliveTime.Should().Be(T0.AddSeconds(5));
        member.SuspectTimes.Should().BeNullOrEmpty();
        etag.Should().Be(row.Version.VersionEtag, "the member row carries the table etag it was inserted under");
        all.Members.Should().ContainSingle().Which.Item1.SiloAddress.Should().Be(address);
        all.Version.Version.Should().Be(1);
    }

    [SkippableFact]
    [Trait("Spec", "blk-b0466f322fc64ca7a7909149b65eba64")]
    [Trait("Spec", "blk-b1ec371bd3ca4b9ea3b209bafdd75b9c")]
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
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
    public async Task ReadRowForAnUnknownSiloThrows()
    {
        _fixture.SkipUnlessAvailable();
        var (table, _) = await InitializedTable(Table(NewClusterId()));

        Func<Task> act = () => table.ReadRow(Silo("10.9.9.9", 1, 1));

        await act.Should().ThrowAsync<OrleansException>().WithMessage("*membership record was not found*");
    }

    [SkippableFact]
    [Trait("Spec", "blk-1a703d5db09d4821bebe4b7bc67dc013")]
    [Trait("Spec", "blk-bbd63fcee6df4785b7d7f7b2a96450cc")]
    [Trait("Spec", "blk-007f49e3cc044a36a51bac6992318b6e")]
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
    [Trait("Spec", "blk-1a703d5db09d4821bebe4b7bc67dc013")]
    public async Task UpdateRowForAnUnknownSiloIsAConflict()
    {
        _fixture.SkipUnlessAvailable();
        var (table, initial) = await InitializedTable(Table(NewClusterId()));

        var result = await table.UpdateRow(Entry(Silo("10.0.0.4", 1, 1), "ghost"), "0", initial.Version.Next());

        result.Should().BeFalse("the procedure raises its concurrency signal when the member row is missing");
    }

    [SkippableFact]
    [Trait("Spec", "blk-9f8fd4c6d54a48e388ad4ede378babc2")]
    [Trait("Spec", "blk-a9c6ac31b48e4deea41688cc834a355b")]
    [Trait("Spec", "blk-9f9dec6bf4664bfc8e80a06ec25f0500")]
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
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
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
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
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
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
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
    [Trait("Spec", "blk-36e0b96ba78b469ca4d807da68da10bb")]
    [Trait("Spec", "blk-eda1b01aed3a4036bcb5c861cb3c61b9")]
    [Trait("Spec", "blk-878290f0225f42e2a0918be1cc8c6693")]
    public async Task GatewayListProviderListsTheActiveSilosOfTheCluster()
    {
        _fixture.SkipUnlessAvailable();
        var clusterId = NewClusterId();
        var (table, initial) = await InitializedTable(Table(clusterId));
        var activeSilo = Silo("127.0.0.1", 11111, 7);
        (await table.InsertRow(Entry(activeSilo, "gateway", SiloStatus.Active, 30000), initial.Version.Next())).Should().BeTrue();
        var v = (await table.ReadAll()).Version;
        (await table.InsertRow(Entry(Silo("127.0.0.2", 11112, 8), "joining", SiloStatus.Joining, 30000), v.Next())).Should().BeTrue();
        var provider = new ArgentSeaGatewayListProvider(
            Options.Create(new SqlDbConnectionOptions()),
            Options.Create(new SqlGlobalPropertiesOptions()),
            Options.Create(new ClusterClientDbOptions { DatabaseKey = "Common", ConnectionString = _fixture.ConnectionStringFor(SqlServerFixture.CommonDatabaseName), GatewayListQuery = new QueryProcedure("rdr.OrleansClusterGatewayListV1", new[] { "@ClusterId" }) }),
            Options.Create(new ClusterOptions { ClusterId = clusterId, ServiceId = "it" }),
            NullLogger<ArgentSeaGatewayListProvider>.Instance);

        await provider.InitializeGatewayListProvider();
        var gateways = await provider.GetGateways();

        gateways.Should().ContainSingle().Which.Should().Be(SiloAddress.New(new IPEndPoint(IPAddress.Loopback, 30000), 7).ToGatewayUri(), "the gateway URI uses the silo's proxy port and generation");
        provider.IsUpdatable.Should().BeTrue();
        provider.MaxStaleness.Should().Be(TimeSpan.FromSeconds(60));
    }
}
