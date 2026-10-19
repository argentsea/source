using System.Data;
using System.Reflection;
using ArgentSea.Orleans.Sql;
using FluentAssertions;

namespace ArgentSea.Orleans.Sql.Test;

/// <summary>
/// Spec section orleans-runtime-query-catalog: every member of the Queries catalogue resolves to its declared version-1
/// procedure name and ordered parameter list, read procedures are rdr.-qualified and write/delete procedures wtr.-qualified,
/// and the composed reminder default definition.
/// </summary>
public class QueryCatalogTests
{
    public static TheoryData<string, string, string[]> Catalogue => new()
    {
        { nameof(Queries.OrleansClusterDelete), "wtr.OrleansClusterDeleteV1", new[] { "@ClusterId" } },
        { nameof(Queries.OrleansClusterGatewayQueryKey), "rdr.OrleansClusterGatewayQueryKeyV1", Array.Empty<string>() },
        { nameof(Queries.OrleansClusterInsertMemberKey), "wtr.OrleansClusterInsertMemberKeyV1", Array.Empty<string>() },
        { nameof(Queries.OrleansClusterInsertMemberVersionKey), "wtr.OrleansClusterInsertMemberVersionKeyV1", Array.Empty<string>() },
        { nameof(Queries.OrleansClusterMemberReadAllKey), "rdr.OrleansClusterMemberReadAllKeyV1", Array.Empty<string>() },
        { nameof(Queries.OrleansClusterMemberReadRowKey), "rdr.OrleansClusterMemberReadRowKeyV1", Array.Empty<string>() },
        { nameof(Queries.OrleansClusterUpdateIAmAliveTimeKey), "wtr.OrleansClusterUpdateIAmAliveTimeKeyV1", Array.Empty<string>() },
        { nameof(Queries.OrleansClusterUpdateMemberKey), "wtr.OrleansClusterUpdateMemberKeyV1", Array.Empty<string>() },
        { nameof(Queries.OrleansClusterDeleteDefunctMembers), "wtr.OrleansClusterDeleteDefunctMembersV1", Array.Empty<string>() },
        { nameof(Queries.OrleansReminderReadRowKey), "rdr.OrleansReminderReadRowKeyV1", new[] { "@GrainKey", "@GrainType", "@ReminderName" } },
        { nameof(Queries.OrleansReminderReadRowsKey), "rdr.OrleansReminderReadRowsKeyV1", new[] { "@GrainKey", "@GrainType" } },
        { nameof(Queries.OrleansReminderReadRangeRows1Key), "rdr.OrleansReminderReadRangeRows1KeyV1", new[] { "@BeginHash", "@EndHash" } },
        { nameof(Queries.OrleansReminderDeleteRowKey), "wtr.OrleansReminderDeleteRowKeyV1", new[] { "@GrainKey", "@GrainType", "@ReminderName", "@Version", "@IsFound" } },
        { nameof(Queries.OrleansReminderUpsertRowKey), "wtr.OrleansReminderUpsertRowKeyV1", new[] { "@GrainKey", "@GrainType", "@ReminderName", "@StartTime", "@Period", "@GrainHash", "@OldVersion", "@NewVersion" } },
    };

    private static QueryProcedure Member(string name)
        => (QueryProcedure)typeof(Queries).GetProperty(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    [Theory, MemberData(nameof(Catalogue))]
    [Trait("Spec", "blk-0d4ca4935e0a4dc2ac95690d5a9e64f8")]
    [Trait("Spec", "blk-1e85b946c580425bba6c01b3b781435c")]
    [Trait("Spec", "blk-262dc457a4af413e89b3b8b0e2ddbf19")]
    [Trait("Spec", "blk-66ec44ff62ab4862ac4b74735390a740")]
    [Trait("Spec", "blk-cd1926a7c06a4baabc6eb6cc3a74e4ad")]
    [Trait("Spec", "blk-faafe4db721d43e4a811667821eb51b7")]
    public void EveryCatalogueMemberResolvesToItsDeclaredProcedureAndOrderedParameters(string member, string procedure, string[] parameters)
    {
        var query = Member(member);

        query.Sql.Should().Be(procedure);
        query.Name.Should().Be(procedure);
        query.Type.Should().Be(CommandType.StoredProcedure);
        query.Sql.Should().EndWith("V1", "the catalogue pins version-1 procedures");
        var reads = member.Contains("Read", StringComparison.Ordinal) || member.Contains("Query", StringComparison.Ordinal);
        query.Sql.Should().StartWith(reads ? "rdr." : "wtr.", "reads are rdr.-qualified and writes/deletes wtr.-qualified");
        if (parameters.Length == 0)
        {
            (query.ParameterNames ?? Array.Empty<string>()).Should().BeEmpty();
        }
        else
        {
            query.ParameterNames.Should().Equal(parameters);
        }
        Member(member).Should().NotBeSameAs(query, "each access composes a fresh, equal contract").And.BeEquivalentTo(query);
    }

    [Fact]
    [Trait("Spec", "blk-cd1926a7c06a4baabc6eb6cc3a74e4ad")]
    [Trait("Spec", "blk-66ec44ff62ab4862ac4b74735390a740")]
    public void TheCatalogueHasExactlyTheFourteenProcedureMembersAndSixDeclareParameters()
    {
        var members = typeof(Queries).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(QueryProcedure))
            .Select(p => p.Name)
            .ToList();

        members.Should().HaveCount(14).And.BeEquivalentTo(Catalogue.Select(row => (string)row[0]));
        members.Count(m => Member(m).ParameterNames is { Length: > 0 }).Should().Be(6, "OrleansClusterDelete and the five reminder members declare parameters");
    }

    [Fact]
    [Trait("Spec", "blk-51f75a47449342d188f233ace3c7a8ee")]
    [Trait("Spec", "blk-ee640f568d434fe9be9d4317130f64f0")]
    [Trait("Spec", "blk-66ec44ff62ab4862ac4b74735390a740")]
    public void TheReminderDefaultDefinitionIsKeyedByTheEmptyGrainTypeAndComposedFromTheReminderQueries()
    {
        var definition = Queries.orleansDbQueryDefinitions;

        definition.GrainType.Should().Be("");
        definition.ResultFormat.Should().Be(QueryResultFormat.ResultSet);
        definition.ReadQuery.Sql.Should().Be("rdr.OrleansReminderReadRowKeyV1");
        definition.WriteQuery.Sql.Should().Be("wtr.OrleansReminderUpsertRowKeyV1");
        definition.ClearQuery.Sql.Should().Be("wtr.OrleansReminderDeleteRowKeyV1");
    }
}
