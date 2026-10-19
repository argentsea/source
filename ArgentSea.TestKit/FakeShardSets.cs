// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArgentSea.TestKit
{
    /// <summary>In-memory <see cref="IShardSetsConfigurationOptions"/>.</summary>
    public class FakeShardSetsOptions : IShardSetsConfigurationOptions
    {
        public FakeShardSetConfiguration[] ShardSets { get; set; } = Array.Empty<FakeShardSetConfiguration>();

        public IShardSetConnectionsConfiguration[] ShardSetsConfigInternal => ShardSets;
    }

    /// <summary>In-memory <see cref="IShardSetConnectionsConfiguration"/>. Shard-set-level read/write overrides are not modelled.</summary>
    public class FakeShardSetConfiguration : IShardSetConnectionsConfiguration
    {
        public string? ShardSetName { get; set; }
        public short DefaultShardId { get; set; }
        public FakeShardConfiguration[] Shards { get; set; } = Array.Empty<FakeShardConfiguration>();
        public IShardConnectionConfiguration[] ShardsConfigInternal => Shards;
        public IShardConnectionConfiguration? ReadConfigInternal => null;
        public IShardConnectionConfiguration? WriteConfigInternal => null;
    }

    /// <summary>In-memory <see cref="IShardConnectionConfiguration"/> over two <see cref="FakeConnectionSettings"/> endpoints.</summary>
    public class FakeShardConfiguration : IShardConnectionConfiguration
    {
        public short ShardId { get; set; }
        public FakeConnectionSettings? ReadConnection { get; set; }
        public FakeConnectionSettings? WriteConnection { get; set; }
        public IDataConnection? ReadConnectionInternal => ReadConnection;
        public IDataConnection? WriteConnectionInternal => WriteConnection;
    }

    /// <summary>
    /// A <see cref="ShardSetsBase{TConfiguration}"/> over in-memory configuration and (normally) a <see cref="FakeProviderServices"/>.
    /// Build one with <see cref="Builder(FakeProviderServices)"/>. The builder substitutes a <see cref="NullLogger"/> for a missing logger
    /// unless <see cref="FakeShardSetsBuilder.WithoutLogger"/> is called; this constructor passes the logger through as-is, so a test can
    /// exercise the library with a genuinely null logger. The resilience settings of every endpoint stay reachable through
    /// <see cref="Settings(string, short, FakeConnectionAccess)"/> and can be changed after construction (the connection manager
    /// rebuilds its policy when they change).
    /// </summary>
    public class FakeShardSets : ShardSetsBase<FakeShardSetsOptions>
    {
        public FakeShardSets(FakeShardSetsOptions options, IDataProviderServiceFactory services, DataConnectionConfigurationBase? globalConfiguration = null, ILogger<ShardSetsBase<FakeShardSetsOptions>>? logger = null)
            : base(Options.Create(options), services, globalConfiguration, logger)
        {
            Configuration = options;
            Services = services;
        }

        public FakeShardSetsOptions Configuration { get; }

        public IDataProviderServiceFactory Services { get; }

        public static FakeShardSetsBuilder Builder(FakeProviderServices services) => new FakeShardSetsBuilder(services);

        /// <summary>The <see cref="FakeConnectionSettings"/> an endpoint was configured with, so a test can change its retry or circuit-breaker settings.</summary>
        public FakeConnectionSettings Settings(string shardSetName, short shardId, FakeConnectionAccess access)
        {
            var set = Configuration.ShardSets.FirstOrDefault(s => s.ShardSetName == shardSetName) ?? throw new KeyNotFoundException($"No shard set named {shardSetName}.");
            var shard = set.Shards.FirstOrDefault(s => s.ShardId == shardId) ?? throw new KeyNotFoundException($"Shard set {shardSetName} has no shard {shardId}.");
            return (access == FakeConnectionAccess.Read ? shard.ReadConnection : shard.WriteConnection) ?? throw new InvalidOperationException($"The {access} endpoint is not configured.");
        }
    }

    /// <summary>
    /// Builds a <see cref="FakeShardSets"/>. Each shard gets a read and a write <see cref="FakeConnectionSettings"/> whose connection
    /// strings identify it (see <see cref="FakeConnectionInfo"/>). Defaults keep tests fast and deterministic: no retries, a 1 ms
    /// linear retry interval when retries are enabled, and the circuit breaker off (<c>CircuitBreakerFailureCount = 0</c>).
    /// </summary>
    public class FakeShardSetsBuilder
    {
        private readonly FakeProviderServices _services;
        private readonly List<FakeShardSetConfiguration> _shardSets = new List<FakeShardSetConfiguration>();
        private readonly List<Action<FakeConnectionSettings, FakeConnectionInfo>> _configure = new List<Action<FakeConnectionSettings, FakeConnectionInfo>>();
        private DataConnectionConfigurationBase? _global;
        private ILogger<ShardSetsBase<FakeShardSetsOptions>>? _logger;
        private bool _withoutLogger;

        public FakeShardSetsBuilder(FakeProviderServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        /// <summary>Adds a shard set whose default shard is the first id listed.</summary>
        public FakeShardSetsBuilder AddShardSet(string shardSetName, params short[] shardIds)
        {
            if (shardIds is null || shardIds.Length == 0)
            {
                throw new ArgumentException("A shard set needs at least one shard.", nameof(shardIds));
            }
            return AddShardSet(shardSetName, shardIds[0], shardIds);
        }

        public FakeShardSetsBuilder AddShardSet(string shardSetName, short defaultShardId, IEnumerable<short> shardIds)
        {
            if (string.IsNullOrEmpty(shardSetName))
            {
                throw new ArgumentNullException(nameof(shardSetName));
            }
            var shards = shardIds.Select(id => new FakeShardConfiguration
            {
                ShardId = id,
                ReadConnection = NewConnection(FakeConnectionInfo.ForShard(shardSetName, id, FakeConnectionAccess.Read)),
                WriteConnection = NewConnection(FakeConnectionInfo.ForShard(shardSetName, id, FakeConnectionAccess.Write))
            }).ToArray();
            _shardSets.Add(new FakeShardSetConfiguration { ShardSetName = shardSetName, DefaultShardId = defaultShardId, Shards = shards });
            return this;
        }

        /// <summary>Applies settings to every endpoint connection created so far and later (retry count, breaker, etc.).</summary>
        public FakeShardSetsBuilder ConfigureConnections(Action<FakeConnectionSettings> configure)
            => ConfigureConnections((connection, _) => configure(connection));

        public FakeShardSetsBuilder ConfigureConnections(Action<FakeConnectionSettings, FakeConnectionInfo> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            _configure.Add(configure);
            foreach (var shard in _shardSets.SelectMany(s => s.Shards))
            {
                if (shard.ReadConnection is not null)
                {
                    configure(shard.ReadConnection, FakeConnectionInfo.Parse(shard.ReadConnection.ConnectionString));
                }
                if (shard.WriteConnection is not null)
                {
                    configure(shard.WriteConnection, FakeConnectionInfo.Parse(shard.WriteConnection.ConnectionString));
                }
            }
            return this;
        }

        public FakeShardSetsBuilder WithGlobalConfiguration(DataConnectionConfigurationBase globalConfiguration)
        {
            _global = globalConfiguration;
            return this;
        }

        public FakeShardSetsBuilder WithLogger(ILogger<ShardSetsBase<FakeShardSetsOptions>> logger)
        {
            _logger = logger;
            _withoutLogger = false;
            return this;
        }

        /// <summary>Constructs the shard sets with a null logger instead of the default <see cref="NullLogger"/>, to exercise the library's null-logger paths.</summary>
        public FakeShardSetsBuilder WithoutLogger()
        {
            _logger = null;
            _withoutLogger = true;
            return this;
        }

        public FakeShardSets Build()
        {
            var logger = _withoutLogger ? null : _logger ?? NullLogger<ShardSetsBase<FakeShardSetsOptions>>.Instance;
            return new FakeShardSets(new FakeShardSetsOptions { ShardSets = _shardSets.ToArray() }, _services, _global, logger);
        }

        internal static FakeConnectionSettings NewDefaultConnection(string connectionString)
            => new FakeConnectionSettings
            {
                ConnectionString = connectionString,
                ConnectionDescription = connectionString,
                RetryCount = 0,
                RetryInterval = 1,
                RetryLengthening = SequenceLengthening.Linear,
                CircuitBreakerFailureCount = 0
            };

        private FakeConnectionSettings NewConnection(string connectionString)
        {
            var connection = NewDefaultConnection(connectionString);
            var info = FakeConnectionInfo.Parse(connectionString);
            foreach (var configure in _configure)
            {
                configure(connection, info);
            }
            return connection;
        }
    }
}
