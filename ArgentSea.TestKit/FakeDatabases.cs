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
    /// <summary>In-memory <see cref="IDatabaseConfigurationOptions"/>.</summary>
    public class FakeDatabasesOptions : IDatabaseConfigurationOptions
    {
        public FakeDatabaseConfiguration[] Databases { get; set; } = Array.Empty<FakeDatabaseConfiguration>();

        public IDatabaseConnectionConfiguration[] DbConnectionsInternal => Databases;
    }

    /// <summary>In-memory <see cref="IDatabaseConnectionConfiguration"/> over two <see cref="FakeConnectionSettings"/> endpoints.</summary>
    public class FakeDatabaseConfiguration : IDatabaseConnectionConfiguration
    {
        public string? DatabaseKey { get; set; }
        public FakeConnectionSettings? ReadConnection { get; set; }
        public FakeConnectionSettings? WriteConnection { get; set; }
        public IDataConnection? ReadConnectionInternal => ReadConnection;
        public IDataConnection? WriteConnectionInternal => WriteConnection;
    }

    /// <summary>
    /// A <see cref="DatabasesBase{TConfiguration}"/> over in-memory configuration and (normally) a <see cref="FakeProviderServices"/>.
    /// Build one with <see cref="Builder(FakeProviderServices)"/>. The builder substitutes a <see cref="NullLogger"/> for a missing logger
    /// unless <see cref="FakeDatabasesBuilder.WithoutLogger"/> is called; this constructor passes the logger through as-is, so a test can
    /// exercise the library with a genuinely null logger.
    /// </summary>
    public class FakeDatabases : DatabasesBase<FakeDatabasesOptions>
    {
        public FakeDatabases(FakeDatabasesOptions options, IDataProviderServiceFactory services, DataConnectionConfigurationBase? globalConfiguration = null, ILogger<DatabasesBase<FakeDatabasesOptions>>? logger = null)
            : base(Options.Create(options), services, globalConfiguration, logger)
        {
            Configuration = options;
            Services = services;
        }

        public FakeDatabasesOptions Configuration { get; }

        public IDataProviderServiceFactory Services { get; }

        public static FakeDatabasesBuilder Builder(FakeProviderServices services) => new FakeDatabasesBuilder(services);

        /// <summary>The <see cref="FakeConnectionSettings"/> an endpoint was configured with, so a test can change its retry or circuit-breaker settings.</summary>
        public FakeConnectionSettings Settings(string databaseKey, FakeConnectionAccess access)
        {
            var db = Configuration.Databases.FirstOrDefault(d => d.DatabaseKey == databaseKey) ?? throw new KeyNotFoundException($"No database named {databaseKey}.");
            return (access == FakeConnectionAccess.Read ? db.ReadConnection : db.WriteConnection) ?? throw new InvalidOperationException($"The {access} endpoint is not configured.");
        }
    }

    /// <summary>
    /// Builds a <see cref="FakeDatabases"/>. Each database gets a read and a write <see cref="FakeConnectionSettings"/> whose connection
    /// strings identify it (see <see cref="FakeConnectionInfo"/>), with the same fast, deterministic resilience defaults as
    /// <see cref="FakeShardSetsBuilder"/>.
    /// </summary>
    public class FakeDatabasesBuilder
    {
        private readonly FakeProviderServices _services;
        private readonly List<FakeDatabaseConfiguration> _databases = new List<FakeDatabaseConfiguration>();
        private readonly List<Action<FakeConnectionSettings, FakeConnectionInfo>> _configure = new List<Action<FakeConnectionSettings, FakeConnectionInfo>>();
        private DataConnectionConfigurationBase? _global;
        private ILogger<DatabasesBase<FakeDatabasesOptions>>? _logger;
        private bool _withoutLogger;

        public FakeDatabasesBuilder(FakeProviderServices services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public FakeDatabasesBuilder AddDatabase(string databaseKey)
        {
            if (string.IsNullOrEmpty(databaseKey))
            {
                throw new ArgumentNullException(nameof(databaseKey));
            }
            _databases.Add(new FakeDatabaseConfiguration
            {
                DatabaseKey = databaseKey,
                ReadConnection = NewConnection(FakeConnectionInfo.ForDatabase(databaseKey, FakeConnectionAccess.Read)),
                WriteConnection = NewConnection(FakeConnectionInfo.ForDatabase(databaseKey, FakeConnectionAccess.Write))
            });
            return this;
        }

        public FakeDatabasesBuilder ConfigureConnections(Action<FakeConnectionSettings> configure)
            => ConfigureConnections((connection, _) => configure(connection));

        public FakeDatabasesBuilder ConfigureConnections(Action<FakeConnectionSettings, FakeConnectionInfo> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            _configure.Add(configure);
            foreach (var db in _databases)
            {
                if (db.ReadConnection is not null)
                {
                    configure(db.ReadConnection, FakeConnectionInfo.Parse(db.ReadConnection.ConnectionString));
                }
                if (db.WriteConnection is not null)
                {
                    configure(db.WriteConnection, FakeConnectionInfo.Parse(db.WriteConnection.ConnectionString));
                }
            }
            return this;
        }

        public FakeDatabasesBuilder WithGlobalConfiguration(DataConnectionConfigurationBase globalConfiguration)
        {
            _global = globalConfiguration;
            return this;
        }

        public FakeDatabasesBuilder WithLogger(ILogger<DatabasesBase<FakeDatabasesOptions>> logger)
        {
            _logger = logger;
            _withoutLogger = false;
            return this;
        }

        /// <summary>Constructs the databases with a null logger instead of the default <see cref="NullLogger"/>, to exercise the library's null-logger paths.</summary>
        public FakeDatabasesBuilder WithoutLogger()
        {
            _logger = null;
            _withoutLogger = true;
            return this;
        }

        public FakeDatabases Build()
        {
            var logger = _withoutLogger ? null : _logger ?? NullLogger<DatabasesBase<FakeDatabasesOptions>>.Instance;
            return new FakeDatabases(new FakeDatabasesOptions { Databases = _databases.ToArray() }, _services, _global, logger);
        }

        private FakeConnectionSettings NewConnection(string connectionString)
        {
            var connection = FakeShardSetsBuilder.NewDefaultConnection(connectionString);
            var info = FakeConnectionInfo.Parse(connectionString);
            foreach (var configure in _configure)
            {
                configure(connection, info);
            }
            return connection;
        }
    }
}
