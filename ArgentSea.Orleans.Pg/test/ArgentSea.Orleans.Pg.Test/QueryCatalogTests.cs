using System.Data;
using System.Reflection;
using FluentAssertions;

namespace ArgentSea.Orleans.Pg.Test;

/// <summary>
/// The Queries catalogue: writes are wtr-schema procedures (run as CALL), reads are text calls of rdr-schema functions with
/// @p_ placeholders, and the membership reads keep the SQL Server result-set shapes (three for ReadAll, two for ReadRow).
/// </summary>
public class QueryCatalogTests
{
    public static TheoryData<string, string> Procedures => new()
    {
        { nameof(Queries.OrleansClusterInsertMemberVersion), "wtr.orleans_cluster_insert_member_version_v1" },
        { nameof(Queries.OrleansClusterInsertMember), "wtr.orleans_cluster_insert_member_v1" },
        { nameof(Queries.OrleansClusterUpdateMember), "wtr.orleans_cluster_update_member_v1" },
        { nameof(Queries.OrleansClusterUpdateIAmAliveTime), "wtr.orleans_cluster_update_i_am_alive_time_v1" },
        { nameof(Queries.OrleansClusterDeleteDefunctMembers), "wtr.orleans_cluster_delete_defunct_members_v1" },
        { nameof(Queries.OrleansClusterDelete), "wtr.orleans_cluster_delete_v1" },
        { nameof(Queries.OrleansReminderDeleteRow), "wtr.orleans_reminder_delete_row_v1" },
        { nameof(Queries.OrleansReminderUpsertRow), "wtr.orleans_reminder_upsert_row_v1" },
    };

    public static TheoryData<string, string[]> Functions => new()
    {
        { nameof(Queries.OrleansClusterGatewayList), ["rdr.orleans_cluster_gateway_list_v1"] },
        { nameof(Queries.OrleansClusterMemberReadAll), ["rdr.orleans_cluster_version_read_v1", "rdr.orleans_cluster_member_read_all_v1", "rdr.orleans_cluster_suspicion_read_all_v1"] },
        { nameof(Queries.OrleansClusterMemberReadRow), ["rdr.orleans_cluster_member_read_row_v1", "rdr.orleans_cluster_suspicion_read_row_v1"] },
        { nameof(Queries.OrleansReminderReadRow), ["rdr.orleans_reminder_read_row_v1"] },
        { nameof(Queries.OrleansReminderReadRows), ["rdr.orleans_reminder_read_rows_v1"] },
        { nameof(Queries.OrleansReminderReadRangeRows), ["rdr.orleans_reminder_read_range_rows_v1"] },
    };

    private static Query Member(string name)
        => (Query)typeof(Queries).GetProperty(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    [Theory, MemberData(nameof(Procedures))]
    [Trait("Spec", "blk-10658e9ad5854821a3781da0d2ef9fe4")]
    public void EveryWriteIsAVersionedWtrProcedure(string member, string procedure)
    {
        var query = Member(member);

        query.Should().BeOfType<QueryProcedure>();
        query.Type.Should().Be(CommandType.StoredProcedure, "Npgsql runs a stored-procedure command as CALL");
        query.Sql.Should().Be(procedure);
        Member(member).Should().NotBeSameAs(query, "each access composes a fresh, equal contract");
    }

    [Theory, MemberData(nameof(Functions))]
    [Trait("Spec", "blk-10658e9ad5854821a3781da0d2ef9fe4")]
    public void EveryReadIsATextCallOfVersionedRdrFunctionsOnePerResultSet(string member, string[] functions)
    {
        var query = Member(member);

        query.Should().BeOfType<PgFunctionQuery>();
        query.Type.Should().Be(CommandType.Text);
        query.Name.Should().Be(member);
        var statements = query.Sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        statements.Should().HaveCount(functions.Length, "one statement, and so one result set, per function");
        for (var i = 0; i < functions.Length; i++)
        {
            statements[i].Should().StartWith($"SELECT * FROM {functions[i]}(@p_");
        }
    }

    [Fact]
    [Trait("Spec", "blk-10658e9ad5854821a3781da0d2ef9fe4")]
    public void TheCatalogueHasExactlyTheFourteenMembers()
    {
        typeof(Queries).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(p => p.Name)
            .Should().BeEquivalentTo(Procedures.Select(r => (string)r[0]).Concat(Functions.Select(r => (string)r[0])));
    }
}
