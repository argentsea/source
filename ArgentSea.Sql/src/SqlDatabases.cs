// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArgentSea.Sql
{
	public class SqlDatabases : DatabasesBase<SqlDbConnectionOptions>
	{
		public SqlDatabases(
			IOptions<SqlDbConnectionOptions> configOptions,
            IOptions<SqlGlobalPropertiesOptions>? globalOptions,
            ILogger<SqlDatabases>? logger
			) : this(configOptions, new DataProviderServiceFactory(), globalOptions, logger)
		{

		}

        /// <summary>
        /// Initializes the collection over a caller-supplied provider factory instead of the SQL Server <see cref="DataProviderServiceFactory"/>.
        /// Intended for derived classes and tests that substitute an in-memory provider.
        /// </summary>
        protected SqlDatabases(
            IOptions<SqlDbConnectionOptions> configOptions,
            IDataProviderServiceFactory services,
            IOptions<SqlGlobalPropertiesOptions>? globalOptions,
            ILogger<SqlDatabases>? logger
            ) : base(configOptions, services, globalOptions?.Value, logger)
        {

        }
	}
}
