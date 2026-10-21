using System.Net;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Orleans.Configuration;

namespace ArgentSea.Orleans.Pg.Test;

/// <summary>
/// The PostgreSQL ArgentSeaOrleansMembershipTable: writes driven through a PgDatabases over the TestKit's scripted provider
/// (parameter binding, endpoint, concurrency classification), the reader mappers of ReadAll / ReadRow, and the suspect arrays.
/// </summary>
public class MembershipTableTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Alive = new(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

    private static (ArgentSeaOrleansMembershipTable table, FakePgDatabases dbs, RecordingLogger<ArgentSeaOrleansMembershipTable> logger) BuildTable(string clusterId = "cluster-a")
    {
        var dbs = FakePgDatabases.Create("Common");
        var logger = new RecordingLogger<ArgentSeaOrleansMembershipTable>();
        var table = new ArgentSeaOrleansMembershipTable(
            dbs,
            Options.Create(new ClusterOptions { ClusterId = clusterId, ServiceId = "svc" }),
            Options.Create(new OrleansDbPersistenceOptions { DatabaseKey = "Common" }),
            logger);
        return (table, dbs, logger);
    }

    private static SiloAddress Silo(string ip, int port, int generation)
        => SiloAddress.New(new IPEndPoint(IPAddress.Parse(ip), port), generation);

    private static MembershipEntry Entry(string ip = "10.0.0.1", int port = 11111, int generation = 7) => new()
    {
        SiloAddress = Silo(ip, port, generation),
        SiloName = "silo-1",
        HostName = "host-1",
        Status = SiloStatus.Active,
        ProxyPort = 30000,
        StartTime = Start,
        IAmAliveTime = Alive,
    };

    #region Writes

    [Fact]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public void AMissingDatabaseKeyFailsConstruction()
    {
        var act = () => new ArgentSeaOrleansMembershipTable(
            FakePgDatabases.Create("Other"),
            Options.Create(new ClusterOptions { ClusterId = "c" }),
            Options.Create(new OrleansDbPersistenceOptions { DatabaseKey = "Common" }),
            NullLogger<ArgentSeaOrleansMembershipTable>.Instance);

        act.Should().Throw<KeyNotFoundException>().WithMessage("*Common*");
    }

    [Fact]
    [Trait("Spec", "blk-1a9bf1257b894f9191d36ef2cc2e8f7e")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public async Task InitializeMembershipTableCreatesTheVersionRecordOnlyWhenAsked()
    {
        var (table, dbs, _) = BuildTable("cluster-a");

        await table.InitializeMembershipTable(tryInitTableVersion: false);
        dbs.Provider.Recorder.Commands.Should().BeEmpty("tryInitTableVersion=false completes without a membership query");

        await table.InitializeMembershipTable(tryInitTableVersion: true);

        var cmd = dbs.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.CommandText.Should().Be(Queries.OrleansClusterInsertMemberVersion.Sql);
        cmd.Connection.Access.Should().Be(FakeConnectionAccess.Write);
        cmd.Parameter("p_cluster_id").Should().Be("cluster-a");
        cmd.Parameter("p_etag_no").Should().Be(0);
        cmd.Parameter("p_version").Should().Be(0);
    }

    [Fact]
    [Trait("Spec", "blk-1a9bf1257b894f9191d36ef2cc2e8f7e")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    [Trait("Spec", "blk-21ed445a0be54d25921da2c9eb2d944c")]
    public async Task InsertRowBindsTheMemberAndTableVersionInUtcAndReturnsTrueWhenTheProcedureCompletes()
    {
        var (table, dbs, _) = BuildTable();
        var entry = Entry();
        entry.StartTime = DateTime.SpecifyKind(Start, DateTimeKind.Unspecified);

        var result = await table.InsertRow(entry, new TableVersion(3, "7"));

        result.Should().BeTrue();
        var cmd = dbs.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.CommandText.Should().Be(Queries.OrleansClusterInsertMember.Sql);
        cmd.Connection.Access.Should().Be(FakeConnectionAccess.Write);
        cmd.Parameter("p_cluster_id").Should().Be("cluster-a");
        ((byte[])cmd.Parameter("p_address")!).Should().Equal(IPAddress.Parse("10.0.0.1").GetAddressBytes());
        cmd.Parameter("p_port").Should().Be(11111);
        cmd.Parameter("p_generation").Should().Be(7);
        cmd.Parameter("p_silo_name").Should().Be("silo-1");
        cmd.Parameter("p_host_name").Should().Be("host-1");
        cmd.Parameter("p_status").Should().Be((short)SiloStatus.Active, "Status is bound as smallint");
        cmd.Parameter("p_proxy_port").Should().Be(30000);
        cmd.Parameter("p_start_time").Should().Be(new DateTimeOffset(Start, TimeSpan.Zero), "an Unspecified time is sent as UTC");
        cmd.Parameter("p_i_am_alive_time").Should().Be(new DateTimeOffset(Alive, TimeSpan.Zero));
        cmd.Parameter("p_etag_no").Should().Be(7, "the table version's etag string is parsed as the numeric concurrency token");
        cmd.Parameter("p_version").Should().Be(3);
    }

    [Fact]
    [Trait("Spec", "blk-1a9bf1257b894f9191d36ef2cc2e8f7e")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public async Task InsertRowReturnsFalseOnTheConcurrencyConflictSqlStateAndRethrowsEverythingElse()
    {
        var (table, dbs, logger) = BuildTable();
        dbs.Provider.Script.ForQuery(Queries.OrleansClusterInsertMember).Throws(PostgresExceptions.WithSqlState(ArgentSeaOrleansMembershipTable.ConcurrencyConflictSqlState, "Concurrency error"));

        var conflict = await table.InsertRow(Entry(), new TableVersion(3, "7"));

        conflict.Should().BeFalse("SQLSTATE AS001 is the procedure's etag/duplicate-row signal");
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message!.Contains("conflict"));

        var other = PostgresExceptions.WithSqlState("57014", "canceling statement due to statement timeout");
        dbs.Provider.Script.ForQuery(Queries.OrleansClusterInsertMember).Throws(other);
        var act = () => table.InsertRow(Entry(), new TableVersion(3, "7"));

        (await act.Should().ThrowAsync<PostgresException>()).Which.Should().BeSameAs(other, "a timeout is never reported as a concurrency conflict");
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Exception == other);
    }

    [Fact]
    [Trait("Spec", "blk-1a9bf1257b894f9191d36ef2cc2e8f7e")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public async Task UpdateRowBindsTheMemberItsOwnEtagTheTableVersionAndOneSuspectPerSuspector()
    {
        var (table, dbs, _) = BuildTable();
        var entry = Entry();
        entry.Status = SiloStatus.ShuttingDown;
        var a1 = new DateTime(2026, 8, 16, 10, 0, 0, DateTimeKind.Utc);
        var a2 = a1.AddHours(1);
        var b = a1.AddMinutes(30);
        entry.AddSuspector(Silo("10.0.0.1", 1111, 1), a1);
        entry.AddSuspector(Silo("10.0.0.1", 1111, 1), a2);
        entry.AddSuspector(Silo("10.0.0.2", 2222, 2), b);

        var result = await table.UpdateRow(entry, "5", new TableVersion(4, "7"));

        result.Should().BeTrue();
        var cmd = dbs.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.CommandText.Should().Be(Queries.OrleansClusterUpdateMember.Sql);
        cmd.Parameter("p_cluster_id").Should().Be("cluster-a");
        cmd.Parameter("p_status").Should().Be((short)SiloStatus.ShuttingDown);
        cmd.Parameter("p_proxy_port").Should().Be(30000);
        cmd.Parameter("p_i_am_alive_time").Should().Be(new DateTimeOffset(Alive, TimeSpan.Zero));
        ((byte[])cmd.Parameter("p_address")!).Should().Equal(IPAddress.Parse("10.0.0.1").GetAddressBytes());
        cmd.Parameter("p_port").Should().Be(11111);
        cmd.Parameter("p_generation").Should().Be(7);
        cmd.Parameter("p_member_etag_no").Should().Be(5, "the member row's own etag argument is checked by the procedure");
        cmd.Parameter("p_etag_no").Should().Be(7, "the table version's etag is checked separately");
        cmd.Parameter("p_version").Should().Be(4);

        var addresses = (byte[][])cmd.Parameter("p_suspect_addresses")!;
        var ports = (int[])cmd.Parameter("p_suspect_ports")!;
        var generations = (int[])cmd.Parameter("p_suspect_generations")!;
        var times = (DateTime[])cmd.Parameter("p_suspect_times")!;
        ports.Should().HaveCount(2, "repeated reports from one suspector collapse to one entry");
        addresses.Should().HaveCount(2);
        generations.Should().HaveCount(2);
        var first = Array.IndexOf(ports, 1111);
        addresses[first].Should().Equal(IPAddress.Parse("10.0.0.1").GetAddressBytes());
        generations[first].Should().Be(1);
        times[first].Should().Be(a2, "the latest report is the suspector's current vote");
        times[Array.IndexOf(ports, 2222)].Should().Be(b);
    }

    [Fact]
    [Trait("Spec", "blk-1a9bf1257b894f9191d36ef2cc2e8f7e")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public async Task UpdateRowWithNoSuspectReportsSendsEmptyArrays()
    {
        var (table, dbs, _) = BuildTable();

        await table.UpdateRow(Entry(), "7", new TableVersion(4, "7"));

        var cmd = dbs.Provider.Recorder.Commands.Should().ContainSingle().Which;
        ((int[])cmd.Parameter("p_suspect_ports")!).Should().BeEmpty("an empty list replaces the member's suspicions with none");
    }

    [Fact]
    [Trait("Spec", "blk-1a9bf1257b894f9191d36ef2cc2e8f7e")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public async Task UpdateRowReturnsFalseOnTheConcurrencyConflictSqlStateAndRethrowsEverythingElse()
    {
        var (table, dbs, logger) = BuildTable();
        dbs.Provider.Script.ForQuery(Queries.OrleansClusterUpdateMember).Throws(PostgresExceptions.WithSqlState("AS001", "Cluster member not found or invalid eTag"));

        var conflict = await table.UpdateRow(Entry(), "7", new TableVersion(4, "7"));

        conflict.Should().BeFalse();
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message!.Contains("conflict"));

        var broken = new NpgsqlException("connection broken");
        dbs.Provider.Script.ForQuery(Queries.OrleansClusterUpdateMember).Throws(broken);
        var act = () => table.UpdateRow(Entry(), "7", new TableVersion(4, "7"));

        (await act.Should().ThrowAsync<NpgsqlException>()).Which.Should().BeSameAs(broken);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Exception == broken);
    }

    [Fact]
    [Trait("Spec", "blk-1a9bf1257b894f9191d36ef2cc2e8f7e")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public void OnlyTheConcurrencySqlStateIsAConflict()
    {
        ArgentSeaOrleansMembershipTable.IsConcurrencyConflict(PostgresExceptions.WithSqlState("AS001")).Should().BeTrue();
        ArgentSeaOrleansMembershipTable.IsConcurrencyConflict(PostgresExceptions.WithSqlState("23505")).Should().BeFalse("a unique violation outside the procedures' checks is a fault");
        ArgentSeaOrleansMembershipTable.IsConcurrencyConflict(PostgresExceptions.WithSqlState("40001")).Should().BeFalse();
        ArgentSeaOrleansMembershipTable.IsConcurrencyConflict(new NpgsqlException("connection broken")).Should().BeFalse();
        ArgentSeaOrleansMembershipTable.IsConcurrencyConflict(new TimeoutException()).Should().BeFalse();
    }

    [Fact]
    [Trait("Spec", "blk-1a9bf1257b894f9191d36ef2cc2e8f7e")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public async Task LivenessDefunctCleanupAndClusterDeleteBindTheirParametersToTheWriteEndpoint()
    {
        var (table, dbs, _) = BuildTable();
        var entry = Entry();
        var before = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.FromHours(2));

        await table.UpdateIAmAlive(entry);
        await table.CleanupDefunctSiloEntries(before);
        await table.DeleteMembershipTableEntries("other-cluster");

        var commands = dbs.Provider.Recorder.Commands;
        commands.Should().HaveCount(3).And.OnlyContain(c => c.Connection.Access == FakeConnectionAccess.Write);
        commands[0].CommandText.Should().Be(Queries.OrleansClusterUpdateIAmAliveTime.Sql);
        commands[0].Parameter("p_i_am_alive_time").Should().Be(new DateTimeOffset(Alive, TimeSpan.Zero));
        commands[0].Parameter("p_generation").Should().Be(7);
        commands[1].CommandText.Should().Be(Queries.OrleansClusterDeleteDefunctMembers.Sql);
        ((DateTimeOffset)commands[1].Parameter("p_before_date")!).Offset.Should().Be(TimeSpan.Zero, "the cutoff is submitted as UTC");
        ((DateTimeOffset)commands[1].Parameter("p_before_date")!).Should().Be(before);
        commands[2].CommandText.Should().Be(Queries.OrleansClusterDelete.Sql);
        commands[2].Parameter("p_cluster_id").Should().Be("other-cluster", "the explicitly supplied cluster is deleted, not the configured one");
    }

    #endregion

    #region Reads

    private static readonly string[] VersionColumns = ["version_timestamp", "etag_no", "version"];
    private static readonly string[] MemberColumns = ["cluster_member_id", "address", "port", "generation", "silo_name", "host_name", "status", "proxy_port", "start_time", "i_am_alive_time", "membership_etag_no", "membership_version"];
    private static readonly string[] SuspectColumns = ["cluster_member_id", "address", "port", "generation", "suspicion_timestamp"];

    private static object?[] MemberRow(int id, string ip, int port, int generation, string name, int etag = 7, int version = 3)
        => [id, IPAddress.Parse(ip).GetAddressBytes(), port, generation, name, "host-" + name, (short)SiloStatus.Active, 30000, Start, Alive, etag, version];

    [Fact]
    [Trait("Spec", "blk-1a9bf1257b894f9191d36ef2cc2e8f7e")]
    [Trait("Spec", "blk-e7566bc44d504db7875fff7333a3d53c")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public void ReadAllMapsTheVersionRowEveryMemberRowAndAttachesSuspectsOnlyToTheirMember()
    {
        var logger = new RecordingLogger();
        using var rdr = new FakeDbDataReader(
        [
            new FakeResultSet(VersionColumns, [[Alive, 7, 3]]),
            new FakeResultSet(MemberColumns, [MemberRow(1, "10.0.0.1", 11111, 7, "silo-1"), MemberRow(2, "10.0.0.2", 22222, 8, "silo-2", etag: 5)]),
            new FakeResultSet(SuspectColumns,
            [
                [2, IPAddress.Parse("10.0.0.1").GetAddressBytes(), 11111, 7, Alive],
                [99, IPAddress.Parse("10.0.0.9").GetAddressBytes(), 9, 9, Alive],
            ]),
        ]);

        var data = ArgentSeaOrleansMembershipTable.ReadAllFromReader(rdr, logger);

        data.Version.Version.Should().Be(3);
        data.Version.VersionEtag.Should().Be("7");
        data.Members.Should().HaveCount(2);
        var (silo1, etag1) = data.Members[0];
        silo1.SiloAddress.Should().Be(Silo("10.0.0.1", 11111, 7));
        silo1.SiloName.Should().Be("silo-1");
        silo1.HostName.Should().Be("host-silo-1");
        silo1.Status.Should().Be(SiloStatus.Active);
        silo1.ProxyPort.Should().Be(30000);
        silo1.StartTime.Should().Be(Start);
        silo1.IAmAliveTime.Should().Be(Alive);
        etag1.Should().Be("7");
        silo1.SuspectTimes.Should().BeNullOrEmpty();
        var (silo2, etag2) = data.Members[1];
        etag2.Should().Be("5", "each member carries its own etag, which may lag the table etag");
        silo2.SuspectTimes.Should().ContainSingle().Which.Should().Be(Tuple.Create(Silo("10.0.0.1", 11111, 7), Alive), "the suspect row is attached to the member it references");
        logger.Entries.Where(e => e.Level == LogLevel.Error).Should().ContainSingle(e => e.Message!.Contains("id: 99"), "a suspect row for an unknown member is logged and skipped; a lagging member etag is not an error");
    }

    [Fact]
    [Trait("Spec", "blk-e7566bc44d504db7875fff7333a3d53c")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public void ReadAllWithoutAVersionRowThrowsAndWithoutLaterResultSetsReturnsWhatItHas()
    {
        var logger = new RecordingLogger();
        using var versionOnly = new FakeDbDataReader([new FakeResultSet(VersionColumns, [[Alive, 7, 3]])]);
        var data = ArgentSeaOrleansMembershipTable.ReadAllFromReader(versionOnly, logger);
        data.Version.Version.Should().Be(3);
        data.Members.Should().BeEmpty();

        using var noSuspects = new FakeDbDataReader(
        [
            new FakeResultSet(VersionColumns, [[Alive, 7, 3]]),
            new FakeResultSet(MemberColumns, [MemberRow(1, "10.0.0.1", 11111, 7, "silo-1", etag: 6, version: 9)]),
        ]);
        ArgentSeaOrleansMembershipTable.ReadAllFromReader(noSuspects, logger).Members.Should().ContainSingle();
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message!.Contains("greater than VersionTable"), "a member version ahead of the table is logged");

        using var empty = new FakeDbDataReader([new FakeResultSet(VersionColumns, [])]);
        FluentActions.Invoking(() => ArgentSeaOrleansMembershipTable.ReadAllFromReader(empty, logger)).Should().Throw<OrleansException>().WithMessage("*version table was not found*");
    }

    private static readonly string[] RowColumns = ["silo_name", "host_name", "status", "proxy_port", "start_time", "i_am_alive_time", "version_timestamp", "membership_etag_no", "membership_version", "etag_no", "version"];
    private static readonly string[] RowSuspectColumns = ["address", "port", "generation", "suspicion_timestamp"];

    [Fact]
    [Trait("Spec", "blk-1a9bf1257b894f9191d36ef2cc2e8f7e")]
    [Trait("Spec", "blk-e7566bc44d504db7875fff7333a3d53c")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public void ReadRowMapsTheAddressedMemberItsEtagTheTableVersionAndItsSuspectsAndThrowsForAnUnknownSilo()
    {
        var key = Silo("10.0.0.1", 11111, 7);
        using var rdr = new FakeDbDataReader(
        [
            new FakeResultSet(RowColumns, [["silo-1", "host-1", (short)SiloStatus.Joining, 30000, Start, Alive, Alive, 6, 3, 7, 3]]),
            new FakeResultSet(RowSuspectColumns, [[IPAddress.Parse("10.0.0.2").GetAddressBytes(), 22222, 8, Alive]]),
        ]);

        var data = ArgentSeaOrleansMembershipTable.ReadRowFromReader(key, rdr, NullLogger.Instance);

        data.Version.Version.Should().Be(3);
        data.Version.VersionEtag.Should().Be("7");
        var (entry, etag) = data.Members.Should().ContainSingle().Which;
        entry.SiloAddress.Should().Be(key, "the addressed silo is the record's address");
        entry.SiloName.Should().Be("silo-1");
        entry.Status.Should().Be(SiloStatus.Joining, "Status is read as the smallint column");
        entry.StartTime.Should().Be(Start);
        entry.IAmAliveTime.Should().Be(Alive);
        etag.Should().Be("6", "the member's own etag");
        entry.SuspectTimes.Should().ContainSingle().Which.Should().Be(Tuple.Create(Silo("10.0.0.2", 22222, 8), Alive));

        using var missing = new FakeDbDataReader([new FakeResultSet(RowColumns, [])]);
        FluentActions.Invoking(() => ArgentSeaOrleansMembershipTable.ReadRowFromReader(key, missing, NullLogger.Instance)).Should().Throw<OrleansException>().WithMessage("*record was not found*");
    }

    [Fact]
    [Trait("Spec", "blk-e7566bc44d504db7875fff7333a3d53c")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    [Trait("Spec", "blk-21ed445a0be54d25921da2c9eb2d944c")]
    public void ReadMappersReturnUtcTimesEvenWhenTheReaderReturnsAnotherKind()
    {
        var unspecified = DateTime.SpecifyKind(Start, DateTimeKind.Unspecified);
        using var rdr = new FakeDbDataReader(
        [
            new FakeResultSet(VersionColumns, [[Alive, 7, 3]]),
            new FakeResultSet(MemberColumns, [[1, IPAddress.Parse("10.0.0.1").GetAddressBytes(), 11111, 7, "s", "h", (short)SiloStatus.Active, 30000, unspecified, unspecified, 7, 3]]),
            new FakeResultSet(SuspectColumns, []),
        ]);

        var member = ArgentSeaOrleansMembershipTable.ReadAllFromReader(rdr, NullLogger.Instance).Members.Single().Item1;

        member.StartTime.Kind.Should().Be(DateTimeKind.Utc);
        member.StartTime.Should().Be(Start);
    }

    #endregion

    #region Suspect arrays

    [Fact]
    [Trait("Spec", "blk-1a9bf1257b894f9191d36ef2cc2e8f7e")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    public void BuildSuspectArraysForNoReportsIsEmpty()
    {
        var none = ArgentSeaOrleansMembershipTable.BuildSuspectArrays(null);
        none.Addresses.Should().BeEmpty();
        none.Ports.Should().BeEmpty();
        none.Generations.Should().BeEmpty();
        none.Times.Should().BeEmpty();
        ArgentSeaOrleansMembershipTable.BuildSuspectArrays([]).Ports.Should().BeEmpty();
    }

    [Fact]
    [Trait("Spec", "blk-1a9bf1257b894f9191d36ef2cc2e8f7e")]
    [Trait("Spec", "blk-3734c298c721480daf472f5b8508d797")]
    [Trait("Spec", "blk-21ed445a0be54d25921da2c9eb2d944c")]
    public void BuildSuspectArraysKeepsOneEntryPerSuspectorWithItsLatestReportInUtc()
    {
        var t = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var suspects = new List<Tuple<SiloAddress, DateTime>>
        {
            Tuple.Create(Silo("10.0.0.1", 11111, 7), t),
            Tuple.Create(Silo("10.0.0.1", 11111, 7), t.AddMinutes(5)),
            Tuple.Create(Silo("10.0.0.1", 11111, 7), t.AddMinutes(1)),
            Tuple.Create(Silo("10.0.0.1", 11111, 8), t), // different generation
            Tuple.Create(Silo("10.0.0.1", 11112, 7), t), // different port
            Tuple.Create(Silo("fe80::1234:5678:9abc:def0", 65535, int.MaxValue), DateTime.SpecifyKind(t, DateTimeKind.Unspecified)),
        };

        var arrays = ArgentSeaOrleansMembershipTable.BuildSuspectArrays(suspects);

        arrays.Ports.Should().HaveCount(4);
        arrays.Addresses.Should().HaveCount(4).And.Contain(a => a.Length == 16, "IPv6 suspectors are supported");
        arrays.Times[0].Should().Be(t.AddMinutes(5), "the latest report from a repeated suspector wins");
        arrays.Times.Should().OnlyContain(time => time.Kind == DateTimeKind.Utc);
    }

    #endregion
}
