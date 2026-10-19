using System.Net;
using ArgentSea.Sql;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Messaging;

namespace ArgentSea.Orleans.Sql.Test;

/// <summary>
/// Spec section orleans-cluster-providers: ArgentSeaOrleansMembershipTable (driven through a SqlDatabases over the TestKit's
/// scripted provider, plus the reader mapping of ReadAll / ReadRow), ArgentSeaGatewayListProvider, and the clustering
/// registration extensions and provider builder.
/// </summary>
public class ClusterProvidersTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Alive = new(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc);

    private static (ArgentSeaOrleansMembershipTable table, FakeSqlDatabases dbs, RecordingLogger<ArgentSeaOrleansMembershipTable> logger) BuildTable(string clusterId = "cluster-a")
    {
        var dbs = FakeSqlDatabases.Create("Common");
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

    private static (ISiloBuilder builder, ServiceCollection services) SiloBuilder(IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<ISiloBuilder>();
        builder.Services.Returns(services);
        builder.Configuration.Returns(configuration ?? new ConfigurationBuilder().Build());
        return (builder, services);
    }

    private static (IClientBuilder builder, ServiceCollection services) ClientBuilder()
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<IClientBuilder>();
        builder.Services.Returns(services);
        builder.Configuration.Returns(new ConfigurationBuilder().Build());
        return (builder, services);
    }

    #region Membership writes

    [Fact]
    [Trait("Spec", "blk-48618b09dcfc422591d98f6a12b2decb")]
    [Trait("Spec", "blk-3fc167cbdf3e49c6a8e47b4bb8fb2396")]
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
    public async Task InitializeMembershipTableCreatesTheVersionRecordOnlyWhenAsked()
    {
        var (table, dbs, _) = BuildTable("cluster-a");

        await table.InitializeMembershipTable(tryInitTableVersion: false);
        dbs.Provider.Recorder.Commands.Should().BeEmpty("tryInitTableVersion=false completes without a membership query");

        await table.InitializeMembershipTable(tryInitTableVersion: true);

        var cmd = dbs.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.CommandText.Should().Be(Queries.OrleansClusterInsertMemberVersionKey.Sql);
        cmd.Connection.Access.Should().Be(FakeConnectionAccess.Write);
        cmd.Parameter("@ClusterId").Should().Be("cluster-a");
        cmd.Parameter("@ETagNo").Should().Be(0);
        cmd.Parameter("@Version").Should().Be(0);
    }

    [Fact]
    [Trait("Spec", "blk-b0466f322fc64ca7a7909149b65eba64")]
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
    [Trait("Spec", "blk-007f49e3cc044a36a51bac6992318b6e")]
    public async Task InsertRowBindsTheMemberAndTableVersionAndReturnsTrueWhenTheProcedureCompletes()
    {
        var (table, dbs, _) = BuildTable();
        var entry = Entry();

        var result = await table.InsertRow(entry, new TableVersion(3, "7"));

        result.Should().BeTrue();
        var cmd = dbs.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.CommandText.Should().Be(Queries.OrleansClusterInsertMemberKey.Sql);
        cmd.Connection.Access.Should().Be(FakeConnectionAccess.Write);
        cmd.Parameter("@ClusterId").Should().Be("cluster-a");
        ((byte[])cmd.Parameter("@Address")!).Should().Equal(IPAddress.Parse("10.0.0.1").GetAddressBytes());
        cmd.Parameter("@Port").Should().Be(11111);
        cmd.Parameter("@Generation").Should().Be(7);
        cmd.Parameter("@SiloName").Should().Be("silo-1");
        cmd.Parameter("@HostName").Should().Be("host-1");
        cmd.Parameter("@Status").Should().Be((short)SiloStatus.Active, "Status is bound as smallint");
        cmd.Parameter("@ProxyPort").Should().Be(30000);
        cmd.Parameter("@StartTime").Should().Be(Start);
        cmd.Parameter("@IAmAliveTime").Should().Be(Alive);
        cmd.Parameter("@ETagNo").Should().Be(7, "the table version's etag string is parsed as the numeric concurrency token");
        cmd.Parameter("@Version").Should().Be(3);
    }

    [Fact]
    [Trait("Spec", "blk-b0466f322fc64ca7a7909149b65eba64")]
    [Trait("Spec", "blk-b1ec371bd3ca4b9ea3b209bafdd75b9c")]
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
    public async Task InsertRowReturnsFalseOnTheConcurrencyConflictSignalAndRethrowsEverythingElse()
    {
        var (table, dbs, logger) = BuildTable();
        dbs.Provider.Script.ForQuery(Queries.OrleansClusterInsertMemberKey).Throws(SqlExceptions.WithNumber(ArgentSeaOrleansMembershipTable.ConcurrencyConflictErrorNumber, "TableVersion etag mismatch"));

        var conflict = await table.InsertRow(Entry(), new TableVersion(3, "7"));

        conflict.Should().BeFalse("error 51000 is the procedure's etag/duplicate-row signal");
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message!.Contains("conflict"));

        var timeout = SqlExceptions.WithNumber(-2, "Timeout expired");
        dbs.Provider.Script.ForQuery(Queries.OrleansClusterInsertMemberKey).Throws(timeout);
        var act = () => table.InsertRow(Entry(), new TableVersion(3, "7"));

        (await act.Should().ThrowAsync<SqlException>()).Which.Should().BeSameAs(timeout, "a timeout is never reported as a concurrency conflict");
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Exception == timeout);
    }

    [Fact]
    [Trait("Spec", "blk-1a703d5db09d4821bebe4b7bc67dc013")]
    [Trait("Spec", "blk-9f8fd4c6d54a48e388ad4ede378babc2")]
    [Trait("Spec", "blk-a9c6ac31b48e4deea41688cc834a355b")]
    [Trait("Spec", "blk-007f49e3cc044a36a51bac6992318b6e")]
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
    public async Task UpdateRowBindsTheMemberItsEtagTheTableVersionAndOneSuspectRowPerSuspector()
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

        var result = await table.UpdateRow(entry, "7", new TableVersion(4, "7"));

        result.Should().BeTrue();
        var cmd = dbs.Provider.Recorder.Commands.Should().ContainSingle().Which;
        cmd.CommandText.Should().Be(Queries.OrleansClusterUpdateMemberKey.Sql);
        cmd.Parameter("@ClusterId").Should().Be("cluster-a");
        cmd.Parameter("@Status").Should().Be((short)SiloStatus.ShuttingDown);
        cmd.Parameter("@ProxyPort").Should().Be(30000);
        cmd.Parameter("@IAmAliveTime").Should().Be(Alive);
        ((byte[])cmd.Parameter("@Address")!).Should().Equal(IPAddress.Parse("10.0.0.1").GetAddressBytes());
        cmd.Parameter("@Port").Should().Be(11111);
        cmd.Parameter("@Generation").Should().Be(7);
        cmd.Parameter("@ETagNo").Should().Be(7);
        cmd.Parameter("@Version").Should().Be(4);

        var suspects = cmd.Parameter("@SuspectTimes").Should().BeAssignableTo<IEnumerable<SqlDataRecord>>().Which.ToList();
        suspects.Should().HaveCount(2, "repeated reports from one suspector collapse to one row");
        var first = suspects.Single(r => r.GetInt32(1) == 1111);
        ((byte[])first.GetValue(0)).Should().Equal(IPAddress.Parse("10.0.0.1").GetAddressBytes());
        first.GetInt32(2).Should().Be(1);
        first.GetDateTime(3).Should().Be(a2, "the latest report is the suspector's current vote");
        var second = suspects.Single(r => r.GetInt32(1) == 2222);
        second.GetInt32(2).Should().Be(2);
        second.GetDateTime(3).Should().Be(b);
    }

    [Fact]
    [Trait("Spec", "blk-007f49e3cc044a36a51bac6992318b6e")]
    public async Task UpdateRowWithNoSuspectReportsOmitsTheSuspectTableValuedParameter()
    {
        var (table, dbs, _) = BuildTable();

        await table.UpdateRow(Entry(), "7", new TableVersion(4, "7"));

        var value = dbs.Provider.Recorder.Commands.Should().ContainSingle().Which.Parameter("@SuspectTimes");
        (value is null || value is DBNull).Should().BeTrue("the TVP is null when there are no reports");
    }

    [Fact]
    [Trait("Spec", "blk-1a703d5db09d4821bebe4b7bc67dc013")]
    [Trait("Spec", "blk-bbd63fcee6df4785b7d7f7b2a96450cc")]
    public async Task UpdateRowReturnsFalseOnTheConcurrencyConflictSignalAndRethrowsEverythingElse()
    {
        var (table, dbs, logger) = BuildTable();
        dbs.Provider.Script.ForQuery(Queries.OrleansClusterUpdateMemberKey).Throws(SqlExceptions.WithNumber(51000, "Cluster member not found or invalid eTag"));

        var conflict = await table.UpdateRow(Entry(), "7", new TableVersion(4, "7"));

        conflict.Should().BeFalse();
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message!.Contains("conflict"));

        var timeout = SqlExceptions.WithNumber(-2, "Timeout expired");
        dbs.Provider.Script.ForQuery(Queries.OrleansClusterUpdateMemberKey).Throws(timeout);
        var act = () => table.UpdateRow(Entry(), "7", new TableVersion(4, "7"));

        (await act.Should().ThrowAsync<SqlException>()).Which.Should().BeSameAs(timeout);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Exception == timeout);
    }

    [Fact]
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
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
        commands[0].CommandText.Should().Be(Queries.OrleansClusterUpdateIAmAliveTimeKey.Sql);
        commands[0].Parameter("@IAmAliveTime").Should().Be(Alive);
        commands[0].Parameter("@Generation").Should().Be(7);
        commands[1].CommandText.Should().Be(Queries.OrleansClusterDeleteDefunctMembers.Sql);
        commands[1].Parameter("@BeforeDate").Should().Be(before.UtcDateTime, "the cutoff is submitted as UTC");
        commands[2].CommandText.Should().Be(Queries.OrleansClusterDelete.Sql);
        commands[2].Parameter("@ClusterId").Should().Be("other-cluster", "the explicitly supplied cluster is deleted, not the configured one");
    }

    #endregion

    #region Membership reads

    private static readonly string[] VersionColumns = ["ClusterId", "ETagNo", "Version"];
    private static readonly string[] MemberColumns = ["MemberId", "Address", "Port", "Generation", "SiloName", "HostName", "Status", "ProxyPort", "StartTime", "IAmAliveTime", "ETagNo", "Version"];
    private static readonly string[] SuspectColumns = ["MemberId", "Address", "Port", "Generation", "Timestamp"];

    private static object?[] MemberRow(int id, string ip, int port, int generation, string name, int etag = 7, int version = 3)
        => [id, IPAddress.Parse(ip).GetAddressBytes(), port, generation, name, "host-" + name, (short)SiloStatus.Active, 30000, Start, Alive, etag, version];

    [Fact]
    [Trait("Spec", "blk-9f9dec6bf4664bfc8e80a06ec25f0500")]
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
    [Trait("Spec", "blk-007f49e3cc044a36a51bac6992318b6e")]
    public void ReadAllMapsTheVersionRowEveryMemberRowAndAttachesSuspectsOnlyToTheirMember()
    {
        var logger = new RecordingLogger();
        using var rdr = new FakeDbDataReader(
        [
            new FakeResultSet(VersionColumns, [["cluster-a", 7, 3]]),
            new FakeResultSet(MemberColumns, [MemberRow(1, "10.0.0.1", 11111, 7, "silo-1"), MemberRow(2, "10.0.0.2", 22222, 8, "silo-2")]),
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
        var (silo2, _) = data.Members[1];
        silo2.SuspectTimes.Should().ContainSingle().Which.Should().Be(Tuple.Create(Silo("10.0.0.1", 11111, 7), Alive), "the suspect row is attached to the member it references");
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Message!.Contains("id: 99"), "a suspect row for an unknown member is logged and skipped");
    }

    [Fact]
    [Trait("Spec", "blk-0384635003634d9fb24160c19a2605d9")]
    [Trait("Spec", "blk-9f9dec6bf4664bfc8e80a06ec25f0500")]
    public void ReadAllWithoutAMemberResultSetReturnsVersionOnlyData()
    {
        var logger = new RecordingLogger();
        using var rdr = new FakeDbDataReader([new FakeResultSet(VersionColumns, [["cluster-a", 7, 3]])]);

        var data = ArgentSeaOrleansMembershipTable.ReadAllFromReader(rdr, logger);

        data.Version.Version.Should().Be(3);
        data.Version.VersionEtag.Should().Be("7");
        data.Members.Should().BeEmpty();
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error);
    }

    [Fact]
    [Trait("Spec", "blk-9f9dec6bf4664bfc8e80a06ec25f0500")]
    public void ReadAllWithoutASuspectResultSetReturnsTheMembersWithTheVersionAndWithoutAVersionRowThrows()
    {
        var logger = new RecordingLogger();
        using var rdr = new FakeDbDataReader(
        [
            new FakeResultSet(VersionColumns, [["cluster-a", 7, 3]]),
            new FakeResultSet(MemberColumns, [MemberRow(1, "10.0.0.1", 11111, 7, "silo-1", etag: 6, version: 9)]),
        ]);

        var data = ArgentSeaOrleansMembershipTable.ReadAllFromReader(rdr, logger);

        data.Members.Should().ContainSingle().Which.Item2.Should().Be("6", "each member carries its own etag");
        data.Version.Version.Should().Be(3);
        logger.Entries.Where(e => e.Level == LogLevel.Error).Should().HaveCount(3, "etag mismatch, version ahead of the table, and the missing suspects set are each logged");

        using var empty = new FakeDbDataReader([new FakeResultSet(VersionColumns, [])]);
        FluentActions.Invoking(() => ArgentSeaOrleansMembershipTable.ReadAllFromReader(empty, logger)).Should().Throw<OrleansException>().WithMessage("*version table was not found*");
    }

    private static readonly string[] RowColumns = ["SiloName", "HostName", "Status", "ProxyPort", "StartTime", "IAmAliveTime", "VersionTimestamp", "MemberETagNo", "MemberVersion", "ETagNo", "Version"];
    private static readonly string[] RowSuspectColumns = ["Address", "Port", "Generation", "Timestamp"];

    [Fact]
    [Trait("Spec", "blk-38b686606e9c4870904ca712c505ff14")]
    [Trait("Spec", "blk-007f49e3cc044a36a51bac6992318b6e")]
    public void ReadRowMapsTheAddressedMemberItsEtagTheTableVersionAndItsSuspects()
    {
        var key = Silo("10.0.0.1", 11111, 7);
        using var rdr = new FakeDbDataReader(
        [
            new FakeResultSet(RowColumns, [["silo-1", "host-1", (short)SiloStatus.Joining, 30000, Start, Alive, Alive, 7, 3, 7, 3]]),
            new FakeResultSet(RowSuspectColumns, [[IPAddress.Parse("10.0.0.2").GetAddressBytes(), 22222, 8, Alive]]),
        ]);

        var data = ArgentSeaOrleansMembershipTable.ReadRowFromReader(key, rdr, NullLogger.Instance);

        data.Version.Version.Should().Be(3);
        data.Version.VersionEtag.Should().Be("7");
        var (entry, etag) = data.Members.Should().ContainSingle().Which;
        entry.SiloAddress.Should().Be(key, "the addressed silo is the record's address");
        entry.SiloName.Should().Be("silo-1");
        entry.HostName.Should().Be("host-1");
        entry.Status.Should().Be(SiloStatus.Joining, "Status is read as the smallint column");
        entry.ProxyPort.Should().Be(30000);
        entry.StartTime.Should().Be(Start);
        entry.IAmAliveTime.Should().Be(Alive);
        etag.Should().Be("7");
        entry.SuspectTimes.Should().ContainSingle().Which.Should().Be(Tuple.Create(Silo("10.0.0.2", 22222, 8), Alive));

        using var missing = new FakeDbDataReader([new FakeResultSet(RowColumns, [])]);
        FluentActions.Invoking(() => ArgentSeaOrleansMembershipTable.ReadRowFromReader(key, missing, NullLogger.Instance)).Should().Throw<OrleansException>().WithMessage("*record was not found*");
    }

    #endregion

    #region Gateway list provider

    [Fact]
    [Trait("Spec", "blk-36e0b96ba78b469ca4d807da68da10bb")]
    [Trait("Spec", "blk-eda1b01aed3a4036bcb5c861cb3c61b9")]
    [Trait("Spec", "blk-878290f0225f42e2a0918be1cc8c6693")]
    public void GatewayUrisAreBuiltFromEveryAddressPortGenerationRowInOrder()
    {
        using var one = new FakeDbDataReader([new FakeResultSet(["Address", "Port", "Generation"], [[new byte[] { 127, 0, 0, 1 }, 30000, 7]])]);
        ArgentSeaGatewayListProvider.ReadGatewayUris(one).Should().ContainSingle().Which.Should().Be(Silo("127.0.0.1", 30000, 7).ToGatewayUri());

        using var two = new FakeDbDataReader([new FakeResultSet(["Address", "Port", "Generation"],
        [
            [IPAddress.Parse("10.0.0.2").GetAddressBytes(), 30001, 8],
            [IPAddress.Parse("fe80::1").GetAddressBytes(), 30002, 9],
        ])]);
        ArgentSeaGatewayListProvider.ReadGatewayUris(two).Should().Equal(Silo("10.0.0.2", 30001, 8).ToGatewayUri(), Silo("fe80::1", 30002, 9).ToGatewayUri());

        using var none = new FakeDbDataReader([new FakeResultSet(["Address", "Port", "Generation"], [])]);
        ArgentSeaGatewayListProvider.ReadGatewayUris(none).Should().BeEmpty();
    }

    [Fact]
    [Trait("Spec", "blk-878290f0225f42e2a0918be1cc8c6693")]
    [Trait("Spec", "blk-46e89b65b0d54ec9b781190ca4e5af28")]
    [Trait("Spec", "blk-566c476d46b8403da27b52d87ef4263e")]
    [Trait("Spec", "blk-00b33c29672b456f993c6fd5dad3cbbb")]
    public async Task GatewayProviderMetadataComesFromTheClientOptionsWhoseDefaultsAreDeclared()
    {
        var defaults = new ClusterClientDbOptions { DatabaseKey = "Common", GatewayListQuery = new QueryProcedure("rdr.OrleansClusterGatewayListV1", ["@ClusterId"]) };
        defaults.ConnectionString.Should().BeNull();
        defaults.MaxRefreshInterval.Should().Be(TimeSpan.FromSeconds(60));
        var declaredDefaults = (ClusterClientDbOptions)Activator.CreateInstance(typeof(ClusterClientDbOptions))!;
        declaredDefaults.DatabaseKey.Should().Be("Common");
        declaredDefaults.GatewayListQuery.Sql.Should().Be("rdr.OrleansClusterGatewayListV1");
        declaredDefaults.GatewayListQuery.ParameterNames.Should().Equal("@ClusterId");

        var provider = new ArgentSeaGatewayListProvider(
            Options.Create(new SqlDbConnectionOptions { SqlDbConnections = [] }),
            Options.Create(new SqlGlobalPropertiesOptions()),
            Options.Create(new ClusterClientDbOptions { DatabaseKey = "Common", ConnectionString = "Data Source=explicit", GatewayListQuery = declaredDefaults.GatewayListQuery, MaxRefreshInterval = TimeSpan.FromSeconds(42) }),
            Options.Create(new ClusterOptions { ClusterId = "alpha" }),
            NullLogger<ArgentSeaGatewayListProvider>.Instance);

        provider.MaxStaleness.Should().Be(TimeSpan.FromSeconds(42));
        provider.IsUpdatable.Should().BeTrue();
        await provider.Invoking(p => p.InitializeGatewayListProvider()).Should().NotThrowAsync("initialization retrieves nothing");
    }

    #endregion

    #region Registration

    [Fact]
    [Trait("Spec", "blk-74dc7cff791c4fd38e12d3dd20b7a2fd")]
    public void UseArgentSeaClusteringOnASiloRegistersTheMembershipTableAndTheClusterOptionsValidator()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ClusterId"] = "c1", ["ServiceId"] = "s1" }).Build();
        var (builder, services) = SiloBuilder(config);

        builder.UseArgentSeaClustering().Should().BeSameAs(builder);

        services.Should().ContainSingle(d => d.ServiceType == typeof(IMembershipTable))
            .Which.Should().Match<ServiceDescriptor>(d => d.ImplementationType == typeof(ArgentSeaOrleansMembershipTable) && d.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(d => d.ServiceType == typeof(IConfigurationValidator) && d.ImplementationType == typeof(ClusterOptionsValidator) && d.Lifetime == ServiceLifetime.Singleton);
        services.BuildServiceProvider().GetRequiredService<IOptions<ClusterOptions>>().Value.ClusterId.Should().Be("c1", "the parameterless overload binds the builder configuration to ClusterOptions");

        var (withAction, actionServices) = SiloBuilder();
        var applied = false;
        withAction.UseArgentSeaClustering(_ => applied = true).Should().BeSameAs(withAction);
        actionServices.Should().ContainSingle(d => d.ServiceType == typeof(IMembershipTable) && d.ImplementationType == typeof(ArgentSeaOrleansMembershipTable));
        actionServices.Should().ContainSingle(d => d.ServiceType == typeof(IConfigurationValidator) && d.ImplementationType == typeof(ClusterOptionsValidator));
        actionServices.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<IConfiguration>), "a non-null action is applied to service configuration");
        applied.Should().BeFalse("the action is registered, not invoked, at configuration time");

        var (withNull, nullServices) = SiloBuilder();
        withNull.Invoking(b => b.UseArgentSeaClustering(null!)).Should().NotThrow("a null action is skipped");
        nullServices.Should().NotContain(d => d.ServiceType == typeof(IConfigureOptions<IConfiguration>));
    }

    [Fact]
    [Trait("Spec", "blk-621e7c8dd8444163bb0a808a46ad59a8")]
    public void UseArgentSeaClusteringOnAClientRegistersTheGatewayListProviderTheMembershipTableAndTheValidator()
    {
        var (builder, services) = ClientBuilder();

        builder.UseArgentSeaClustering(_ => { }).Should().BeSameAs(builder);

        services.Should().ContainSingle(d => d.ServiceType == typeof(IGatewayListProvider))
            .Which.Should().Match<ServiceDescriptor>(d => d.ImplementationType == typeof(ArgentSeaGatewayListProvider) && d.Lifetime == ServiceLifetime.Singleton,
                "an Orleans client finds its gateways through IGatewayListProvider");
        services.Should().ContainSingle(d => d.ServiceType == typeof(IMembershipTable) && d.ImplementationType == typeof(ArgentSeaOrleansMembershipTable) && d.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(d => d.ServiceType == typeof(IConfigurationValidator) && d.ImplementationType == typeof(ClusterOptionsValidator) && d.Lifetime == ServiceLifetime.Singleton);

        var (withNull, nullServices) = ClientBuilder();
        withNull.Invoking(b => b.UseArgentSeaClustering(null!)).Should().NotThrow("a null action is skipped");
        nullServices.Should().Contain(d => d.ServiceType == typeof(IGatewayListProvider));
    }

    [Fact]
    [Trait("Spec", "blk-f7091204d4e14a5781c28d75c0239c1c")]
    public void TheClusteringProviderBuilderBindsClusterOptionsFromTheSectionAndEnablesClusteringForBothHostRoles()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Orleans:Clustering:ProviderType"] = "ArgentSea",
            ["Orleans:Clustering:ClusterId"] = "from-section",
            ["Orleans:Clustering:ServiceId"] = "svc",
        }).Build();
        var section = config.GetSection("Orleans:Clustering");
        var providerBuilder = new ArgentSeaClusteringProviderBuilder();

        var (silo, siloServices) = SiloBuilder(config);
        providerBuilder.Configure(silo, "ArgentSea", section);
        siloServices.Should().ContainSingle(d => d.ServiceType == typeof(IMembershipTable) && d.ImplementationType == typeof(ArgentSeaOrleansMembershipTable));
        siloServices.BuildServiceProvider().GetRequiredService<IOptions<ClusterOptions>>().Value.ClusterId.Should().Be("from-section");

        var (client, clientServices) = ClientBuilder();
        providerBuilder.Configure(client, "ArgentSea", section);
        clientServices.Should().ContainSingle(d => d.ServiceType == typeof(IMembershipTable) && d.ImplementationType == typeof(ArgentSeaOrleansMembershipTable));
        clientServices.Should().ContainSingle(d => d.ServiceType == typeof(IGatewayListProvider) && d.ImplementationType == typeof(ArgentSeaGatewayListProvider));
        clientServices.BuildServiceProvider().GetRequiredService<IOptions<ClusterOptions>>().Value.ClusterId.Should().Be("from-section");
    }

    #endregion
}
