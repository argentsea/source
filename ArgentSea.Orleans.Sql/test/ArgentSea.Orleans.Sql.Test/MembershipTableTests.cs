using System.Net;
using ArgentSea.Orleans.Sql;
using FluentAssertions;
using Orleans.Runtime;

namespace ArgentSea.Orleans.Sql.Test;

public class MembershipTableTests
{
    private static SiloAddress Silo(string ip, int port, int generation)
        => SiloAddress.New(new IPEndPoint(IPAddress.Parse(ip), port), generation);

    [Fact]
    [Trait("Spec", "blk-9f8fd4c6d54a48e388ad4ede378babc2")]
    [Trait("Spec", "blk-007f49e3cc044a36a51bac6992318b6e")]
    public void BuildSuspectRecords_NullOrEmpty_ReturnsNoRows()
    {
        ArgentSeaOrleansMembershipTable.BuildSuspectRecords(null).Should().BeEmpty();
        ArgentSeaOrleansMembershipTable.BuildSuspectRecords([]).Should().BeEmpty();
    }

    [Fact]
    [Trait("Spec", "blk-9f8fd4c6d54a48e388ad4ede378babc2")]
    [Trait("Spec", "blk-a9c6ac31b48e4deea41688cc834a355b")]
    public void BuildSuspectRecords_RepeatedSuspector_MergesToOneRowWithLatestTimestamp()
    {
        var early = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var late = early.AddMinutes(5);
        var suspects = new List<Tuple<SiloAddress, DateTime>>
        {
            Tuple.Create(Silo("10.0.0.1", 11111, 7), early),
            Tuple.Create(Silo("10.0.0.1", 11111, 7), late),
            Tuple.Create(Silo("10.0.0.1", 11111, 7), early.AddMinutes(1)),
        };

        var rows = ArgentSeaOrleansMembershipTable.BuildSuspectRecords(suspects);

        rows.Should().ContainSingle();
        ((byte[])rows[0].GetValue(0)).Should().Equal(IPAddress.Parse("10.0.0.1").GetAddressBytes());
        rows[0].GetInt32(1).Should().Be(11111);
        rows[0].GetInt32(2).Should().Be(7);
        rows[0].GetDateTime(3).Should().Be(late);
    }

    [Fact]
    [Trait("Spec", "blk-9f8fd4c6d54a48e388ad4ede378babc2")]
    public void BuildSuspectRecords_DistinctSuspectors_KeepOneRowEach()
    {
        var t = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var suspects = new List<Tuple<SiloAddress, DateTime>>
        {
            Tuple.Create(Silo("10.0.0.1", 11111, 7), t),
            Tuple.Create(Silo("10.0.0.1", 11111, 8), t), // different generation
            Tuple.Create(Silo("10.0.0.1", 11112, 7), t), // different port
            Tuple.Create(Silo("10.0.0.2", 11111, 7), t), // different address
        };

        ArgentSeaOrleansMembershipTable.BuildSuspectRecords(suspects).Should().HaveCount(4);
    }

    [Fact]
    [Trait("Spec", "blk-9f8fd4c6d54a48e388ad4ede378babc2")]
    public void BuildSuspectRecords_Ipv6Suspectors_AreSupportedAndMerged()
    {
        var t = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var suspects = new List<Tuple<SiloAddress, DateTime>>
        {
            Tuple.Create(Silo("fe80::1234:5678:9abc:def0", 65535, int.MaxValue), t),
            Tuple.Create(Silo("fe80::1234:5678:9abc:def0", 65535, int.MaxValue), t.AddSeconds(30)),
        };

        var rows = ArgentSeaOrleansMembershipTable.BuildSuspectRecords(suspects);

        rows.Should().ContainSingle();
        ((byte[])rows[0].GetValue(0)).Should().HaveCount(16);
        rows[0].GetDateTime(3).Should().Be(t.AddSeconds(30));
    }

    [Fact]
    [Trait("Spec", "blk-1a703d5db09d4821bebe4b7bc67dc013")]
    [Trait("Spec", "blk-b0466f322fc64ca7a7909149b65eba64")]
    public void IsConcurrencyConflict_NonSqlFailures_AreNotConflicts()
    {
        ArgentSeaOrleansMembershipTable.IsConcurrencyConflict(new TimeoutException()).Should().BeFalse();
        ArgentSeaOrleansMembershipTable.IsConcurrencyConflict(new InvalidOperationException("connection broken")).Should().BeFalse();
    }
}
