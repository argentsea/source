using System.Data;

namespace ArgentSea.Integration.Test.Fixtures;

[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}

[CollectionDefinition(Name)]
public class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSql";
}

/// <summary>A Query whose text is supplied inline rather than read from a SQL file; the provider sends it as CommandType.Text.</summary>
public sealed class TextQuery : Query
{
    public TextQuery(string sql, string? name = null, string[]? parameterNames = null)
        : base(sql, name ?? sql, parameterNames)
    {
    }

    public override CommandType Type => CommandType.Text;
}
