using System.Data;

namespace ArgentSea.Orleans.Pg;

/// <summary>
/// A query that calls PostgreSQL set-returning functions through a text command.
/// Npgsql runs a <see cref="QueryProcedure"/> as CALL, which can invoke procedures but not functions, and a procedure cannot
/// return a result set; so the reads of this package are functions, and this query holds only the call text — every query
/// rule lives in the function the schema scripts install.
/// </summary>
public sealed class PgFunctionQuery : Query
{
    /// <summary>
    /// Creates a query whose text calls one or more functions, e.g. “SELECT * FROM rdr.my_function_v1(@p_key);”.
    /// </summary>
    /// <param name="name">The query name, used for logging and mock results.</param>
    /// <param name="sql">The call text. Parameters are referenced as @name placeholders.</param>
    public PgFunctionQuery(string name, string sql)
        : base(sql, name, null)
    {
    }

    public override CommandType Type => CommandType.Text;
}
