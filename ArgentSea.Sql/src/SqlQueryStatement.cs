// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;

namespace ArgentSea.Sql
{
    /// <summary>
    /// Creates SQL Server <see cref="QueryStatement"/> instances using a SQL Server–specific default file extension.
    /// </summary>
    /// <remarks>
    /// <see cref="QueryStatement.Extension"/> is a single process-wide default, and AddPgServices sets it to “psql”.
    /// In a host that registers both SQL Server and PostgreSQL services, create SQL Server statements with this class so that
    /// their files resolve against <see cref="Extension"/> regardless of the shared default.
    /// The folder is still <see cref="QueryStatement.Folder"/>; both values are read when the statement is first loaded.
    /// </remarks>
    public static class SqlQueryStatement
    {
        /// <summary>
        /// The file extension (without the leading period) used to locate SQL Server statement files. The default is “sql”.
        /// </summary>
        public static string Extension { get; set; } = "sql";

        /// <summary>
        /// Creates a lazily-loaded statement from the file “&lt;Folder&gt;/&lt;name&gt;.&lt;Extension&gt;”.
        /// </summary>
        /// <param name="name">The statement name, which is also the file name without its extension.</param>
        public static Lazy<QueryStatement> Create(string name)
            => Create(name, null);

        /// <summary>
        /// Creates a lazily-loaded statement from the file “&lt;Folder&gt;/&lt;name&gt;.&lt;Extension&gt;”.
        /// </summary>
        /// <param name="name">The statement name, which is also the file name without its extension.</param>
        /// <param name="parameterNames">The names of the statement’s parameters, or null.</param>
        public static Lazy<QueryStatement> Create(string name, string[]? parameterNames)
            => new Lazy<QueryStatement>(() => QueryStatement.Create(name, parameterNames, QueryStatement.Folder, Extension).Value);
    }
}
