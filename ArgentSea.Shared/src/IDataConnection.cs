using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ArgentSea
{
    public interface IDataConnection
    {
        /// <summary>
        /// When overridden in a derived class, returns a description that can be used for logging connection errors.
        /// </summary>
        string ConnectionDescription { get; }

        /// <summary>
        /// When overridden in a derived class, returns the ADO.NET connection string from the various connection propeties. Typically, a ConnectionStringBuilder is used for this purpose.
        /// </summary>
        /// <returns></returns>
        string GetConnectionString(ILogger? logger);

        /// <summary>
        /// The effective retry count: the connection’s own value or, if not set, the value inherited from its ambient configuration.
        /// </summary>
        int? RetryCount { get;  }

        /// <summary>
        /// The delay before the given retry attempt, using the effective (inherited) RetryInterval and RetryLengthening.
        /// </summary>
        TimeSpan GetRetryTimespan(int attemptCount);

        /// <summary>
        /// The effective circuit breaker failure count: the connection’s own value or, if not set, the value inherited from its ambient configuration.
        /// </summary>
        int? CircuitBreakerFailureCount { get; }

        /// <summary>
        /// The effective circuit breaker test interval: the connection’s own value or, if not set, the value inherited from its ambient configuration.
        /// </summary>
        int? CircuitBreakerTestInterval { get; }

        /// <summary>
        /// Sets the configuration levels this connection inherits from. Connection-string and resilience settings are resolved in this precedence (most specific last):
        /// global, shard set, shard-set read/write, shard (or database), then the connection itself. Any level may be null.
        /// </summary>
        void SetAmbientConfiguration(DataConnectionConfigurationBase? globalProperties, DataConnectionConfigurationBase? shardSetProperties, DataConnectionConfigurationBase? readWriteProperties, DataConnectionConfigurationBase? shardProperties);
    }
}
