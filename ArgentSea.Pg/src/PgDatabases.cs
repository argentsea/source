// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArgentSea.Pg
{
    /// <summary>
    /// This class manages the non-sharded PosgreSQL database connections.
    /// </summary>
    public class PgDatabases : DatabasesBase<PgDbConnectionOptions>
	{
		public PgDatabases(
			IOptions<PgDbConnectionOptions> configOptions,
			IOptions<PgGlobalPropertiesOptions>? globalOptions,
			ILogger<PgDatabases>? logger
			) : this(configOptions, new DataProviderServiceFactory(), globalOptions, logger)
		{

		}

        /// <summary>
        /// Initializes the collection over a caller-supplied provider factory instead of the PostgreSQL <see cref="DataProviderServiceFactory"/>.
        /// Intended for derived classes and tests that substitute an in-memory provider.
        /// </summary>
        protected PgDatabases(
            IOptions<PgDbConnectionOptions> configOptions,
            IDataProviderServiceFactory services,
            IOptions<PgGlobalPropertiesOptions>? globalOptions,
            ILogger<PgDatabases>? logger
            ) : base(configOptions, services, globalOptions?.Value, logger)
        {

        }
	}
}
