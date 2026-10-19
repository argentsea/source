// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using System.IO;
using System.Data;
using System.Reflection;

namespace ArgentSea
{
    /// <summary>
    /// This implementation of Query loads the SQL from a relative file.
    /// </summary>
    public class QueryStatement : Query
    {
        private static readonly Lazy<string?> appPath = new Lazy<string?>(() => 
            {
                var assembly = typeof(QueryStatement).GetTypeInfo().Assembly;
                return Path.GetDirectoryName(AppContext.BaseDirectory);
            });

        private QueryStatement()
            : base(string.Empty, string.Empty, null)
        {
            //hide default constructor
        }

        private QueryStatement(string name, string sql, string[]? parameterNames)
            : base(sql, name, parameterNames)
        {
            //
        }
        public override CommandType Type { get => CommandType.Text; }

        public static string Folder { get; set; } = "SQL";

        /// <summary>
        /// The process-wide default file extension (without the leading period) used by the Create overloads that do not specify one. The default is “sql”.
        /// </summary>
        /// <remarks>
        /// AddPgServices sets this value to “psql”. A host that registers both SQL Server and PostgreSQL services should create statements
        /// with the provider-specific factories (ArgentSea.Sql.SqlQueryStatement or ArgentSea.Pg.PgQueryStatement), which do not use this shared default.
        /// </remarks>
        public static string Extension { get; set; } = "sql";

        private static QueryStatement QueryStatementFactory(string name, string filepath, string[]? parameterNames)
        {
            var sql = File.ReadAllText(filepath);
            return new QueryStatement(name, sql, parameterNames);
        }

        public static Lazy<QueryStatement> Create(string name)
        {
            return new Lazy<QueryStatement>(() =>
            {
                return QueryStatementFactory(name, GetSqlFullPath(name, QueryStatement.Folder, QueryStatement.Extension), null);
            });
        }
        public static Lazy<QueryStatement> Create(string name, string[]? parameterNames)
        {
            return new Lazy<QueryStatement>(() =>
            {
                return QueryStatementFactory(name, GetSqlFullPath(name, QueryStatement.Folder, QueryStatement.Extension), parameterNames);
            });
        }
        public static Lazy<QueryStatement> Create(string name, string[]? parameterNames, string folderName, string? extension)
        {
            return new Lazy<QueryStatement>(() =>
            {
                return QueryStatementFactory(name, GetSqlFullPath(name, folderName, extension), parameterNames);
            });
        }
        public static Lazy<QueryStatement> Create(string name, string[]? parameterNames, string fullFilePath)
        {
            return new Lazy<QueryStatement>(() =>
            {
                return QueryStatementFactory(name, fullFilePath, parameterNames);
            });
        }
        private static string GetSqlFullPath(string name, string folderName, string? extension)
        {
            if (Path.IsPathRooted(name))
            {
                if (!string.IsNullOrEmpty(extension))
                {
                    return Path.GetFullPath($"{name}.{extension}");
                }
                else
                {
                    return Path.GetFullPath($"{name}");
                }
            }
            else if (Path.IsPathRooted(folderName))
            {
                if (!string.IsNullOrEmpty(extension))
                {
                    return Path.GetFullPath($"{folderName}{Path.DirectorySeparatorChar}{name}.{extension}");

                }
                else
                {
                    return Path.GetFullPath($"{folderName}{Path.DirectorySeparatorChar}{name}");
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(extension))
                {
                    return Path.GetFullPath($"{appPath.Value}{Path.DirectorySeparatorChar}{folderName}{Path.DirectorySeparatorChar}{name}.{extension}");
                }
                else
                {
                    return Path.GetFullPath($"{appPath.Value}{Path.DirectorySeparatorChar}{folderName}{Path.DirectorySeparatorChar}{name}");
                }
            }
        }
    }
}