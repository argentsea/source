// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace ArgentSea.Sql
{
    /// <summary>
    /// This configuration class defines an array of database <see cref="SqlDbConnectionConfiguration">connection configurations</see>.
    /// <example>
    /// For example, you might configure your appsettings.json like this:
    /// <code>
    ///   "SqlDbConnections": [
    ///   {
    ///     "DatabaseKey": "MyDatabase",
    ///     "UserName": "webUser",
    ///     "Password": "pwd1234",
    ///     "DataSource": "localhost",
    ///     "InitialCatalog": "MyDb",
    ///     "WriteConnection": {
    ///       "DataSource": "primaryserver"
    ///     }
    ///   }
    ///   ]
    ///</code>
    /// Connection settings placed directly on the entry apply to both the read and the write connection; settings within the optional
    /// "ReadConnection" or "WriteConnection" objects override them for that connection only.
    ///</example>
    /// </summary>
    public class SqlDbConnectionOptions : IDatabaseConfigurationOptions
	{
		public SqlDbConnectionConfiguration[]? SqlDbConnections { get; set; }

        public IDatabaseConnectionConfiguration?[]? DbConnectionsInternal { get => SqlDbConnections; }

	}
	public class SqlDbConnectionConfiguration : SqlConnectionPropertiesBase, IDatabaseConnectionConfiguration
    {
		public string? DatabaseKey { get; set; }

        public IDataConnection? ReadConnectionInternal { get => ReadConnection; }
        public IDataConnection? WriteConnectionInternal { get => WriteConnection; }
        public SqlConnectionConfiguration? ReadConnection { get; set; } = new SqlConnectionConfiguration();
        public SqlConnectionConfiguration? WriteConnection { get; set; } = new SqlConnectionConfiguration();
    }
}
