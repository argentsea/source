// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArgentSea.Pg
{
    /// <summary>
    /// A collection of ShardSets.
    /// </summary>
    public class PgShardSets : ArgentSea.ShardSetsBase<PgShardConnectionOptions>
	{
		public PgShardSets(
			IOptions<PgShardConnectionOptions> configOptions,
            IOptions<PgGlobalPropertiesOptions>? globalOptions,
			ILogger<PgShardSets>? logger
			) : this(configOptions, new DataProviderServiceFactory(), globalOptions, logger)
		{

		}

        /// <summary>
        /// Initializes the collection over a caller-supplied provider factory instead of the PostgreSQL <see cref="DataProviderServiceFactory"/>.
        /// Intended for derived classes and tests that substitute an in-memory provider.
        /// </summary>
        protected PgShardSets(
            IOptions<PgShardConnectionOptions> configOptions,
            IDataProviderServiceFactory services,
            IOptions<PgGlobalPropertiesOptions>? globalOptions,
            ILogger<PgShardSets>? logger
            ) : base(configOptions, services, globalOptions?.Value, logger)
        {

        }
	}
}
