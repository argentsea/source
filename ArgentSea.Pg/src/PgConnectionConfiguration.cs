// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System.Collections.Generic;
using Npgsql;
using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace ArgentSea.Pg
{
    /// <summary>
    /// This class represents a single database connection — a database connection or a shard instance read or write connection.
    /// Its connection properties are combined with those inherited from the global, shard-set, shard, and database-level configuration.
    /// </summary>
    public class PgConnectionConfiguration : PgConnectionPropertiesBase, IDataConnection
    {

        private string? _connectionString;
        private PgConnectionPropertiesBase? _globalProperties;
        private PgConnectionPropertiesBase? _shardSetProperties;
        private PgConnectionPropertiesBase? _readWriteProperties;
        private PgConnectionPropertiesBase? _shardProperties;
        private string? _connectionDescription;

        private const int DefaultConnectTimeout = 2;

        public PgConnectionConfiguration()
        {
            // A change to this configuration's own (local) connection properties also invalidates the cached values.
            // The handler only clears fields and never raises PropertyChanged, so self-subscription cannot recurse.
            this.PropertyChanged += HandlePropertyChanged;
        }

        private void HandlePropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            // A local or inherited property changed: both the built connection string and the description derived from it are stale.
            _connectionString = null;
            _connectionDescription = null;
        }

        private void SetProperties(NpgsqlConnectionStringBuilder csb, DataConnectionConfigurationBase properties)
        {
            if (properties.Password is not null)
            {
                csb.Password = properties.Password;
            }
            if (properties.UserName is not null)
            {
                csb.Username = properties.UserName;
            }
            //if (properties.WindowsAuth is not null)
            //{
            //    csb.IntegratedSecurity = properties.WindowsAuth.Value;
            //}
            var props = (PgConnectionPropertiesBase)properties;
            if (props.ApplicationName is not null)
            {
                csb.ApplicationName = props.ApplicationName;
            }
            if (props.AutoPrepareMinUsages is not null)
            {
                csb.AutoPrepareMinUsages = props.AutoPrepareMinUsages.Value;
            }
            if (props.CancellationTimeout is not null)
            {
                csb.CancellationTimeout = props.CancellationTimeout.Value;
            }
            if (props.CheckCertificateRevocation is not null)
            {
                csb.CheckCertificateRevocation = props.CheckCertificateRevocation.Value;
            }
            //if (props.ClientCertificate is not null)
            //{
            //    csb.ClientCertificate = props.ClientCertificate;
            //}
            //if (props.ClientCertificateKey is not null)
            //{
            //    csb.ClientCertificateKey = props.ClientCertificateKey;
            //}
            if (props.ClientEncoding is not null)
            {
                csb.ClientEncoding = props.ClientEncoding;
            }
            if (props.CommandTimeout is not null)
            {
                csb.CommandTimeout = props.CommandTimeout.Value;
            }
            if (props.ConnectionIdleLifetime is not null)
            {
                csb.ConnectionIdleLifetime = props.ConnectionIdleLifetime.Value;
            }
            if (props.ConnectionLifetime is not null)
            {
                csb.ConnectionLifetime = props.ConnectionLifetime.Value;
            }
            if (props.ConnectionPruningInterval is not null)
            {
                csb.ConnectionPruningInterval = props.ConnectionPruningInterval.Value;
            }
            //if (props.ConvertInfinityDateTime is not null)
            //{
            //    csb.ConvertInfinityDateTime = props.ConvertInfinityDateTime.Value;
            //}
            if (props.Database is not null)
            {
                csb.Database = props.Database;
            }
            if (props.Encoding is not null)
            {
                csb.Encoding = props.Encoding;
            }
            if (props.Enlist is not null)
            {
                csb.Enlist = props.Enlist.Value;
            }
            if (props.Host is not null)
            {
                csb.Host = props.Host;
            }
            if (props.IncludeErrorDetails is not null)
            {
                csb.IncludeErrorDetail = props.IncludeErrorDetails.Value;
            }
            if (props.IncludeRealm is not null)
            {
                csb.IncludeRealm = props.IncludeRealm.Value;
            }
            //if (props.InternalCommandTimeout is not null)
            //{
            //    csb.InternalCommandTimeout = props.InternalCommandTimeout.Value;
            //}
            if (props.KeepAlive is not null)
            {
                csb.KeepAlive = props.KeepAlive.Value;
            }
            if (props.KerberosServiceName is not null)
            {
                csb.KerberosServiceName = props.KerberosServiceName;
            }
            if (props.LoadTableComposites is not null)
            {
                csb.LoadTableComposites = props.LoadTableComposites.Value;
            }
            if (props.LogParameters is not null)
            {
                csb.LogParameters = props.LogParameters.Value;
            }
            if (props.MaxAutoPrepare is not null)
            {
                csb.MaxAutoPrepare = props.MaxAutoPrepare.Value;
            }
            if (props.MaxPoolSize is not null)
            {
                csb.MaxPoolSize = props.MaxPoolSize.Value;
            }
            if (props.MinPoolSize is not null)
            {
                csb.MinPoolSize = props.MinPoolSize.Value;
            }
            if (props.Multiplexing is not null)
            {
                csb.Multiplexing = props.Multiplexing.Value;
            }
            if (props.NoResetOnClose is not null)
            {
                csb.NoResetOnClose = props.NoResetOnClose.Value;
            }
            if (props.Options is not null)
            {
                csb.Options = props.Options;
            }
            if (props.Passfile is not null)
            {
                csb.Passfile = props.Passfile;
            }
            if (props.PersistSecurityInfo is not null)
            {
                csb.PersistSecurityInfo = props.PersistSecurityInfo.Value;
            }
            if (props.Pooling is not null)
            {
                csb.Pooling = props.Pooling.Value;
            }
            if (props.Port is not null)
            {
                csb.Port = props.Port.Value;
            }
            if (props.ReadBufferSize is not null)
            {
                csb.ReadBufferSize = props.ReadBufferSize.Value;
            }
            if (props.RootCertificate is not null)
            {
                csb.RootCertificate = props.RootCertificate;
            }
            if (props.SearchPath is not null)
            {
                csb.SearchPath = props.SearchPath;
            }
            if (props.ServerCompatibilityMode is not null)
            {
                csb.ServerCompatibilityMode = props.ServerCompatibilityMode.Value;
            }
            if (props.SocketReceiveBufferSize is not null)
            {
                csb.SocketReceiveBufferSize = props.SocketReceiveBufferSize.Value;
            }
            if (props.SocketSendBufferSize is not null)
            {
                csb.SocketSendBufferSize = props.SocketSendBufferSize.Value;
            }
            if (props.SslMode is not null)
            {
                csb.SslMode = props.SslMode.Value;
            }
            if (props.TcpKeepAlive is not null)
            {
                csb.TcpKeepAlive = props.TcpKeepAlive.Value;
            }
            if (props.TcpKeepAliveInterval is not null)
            {
                csb.TcpKeepAliveInterval = props.TcpKeepAliveInterval.Value;
            }
            if (props.TcpKeepAliveTime is not null)
            {
                csb.TcpKeepAliveTime = props.TcpKeepAliveTime.Value;
            }
            if (props.Timeout is not null)
            {
                csb.Timeout = props.Timeout.Value;
            }
            if (props.Timezone is not null)
            {
                csb.Timezone = props.Timezone;
            }
            //if (props.TrustServerCertificate is not null)
            //{
            //    csb.TrustServerCertificate = props.TrustServerCertificate.Value;
            //}
            //if (props.UsePerfCounters is not null)
            //{
            //    csb.UsePerfCounters = props.UsePerfCounters.Value;
            //}
            //if (props.UseSslStream is not null)
            //{
            //    csb.UseSslStream = props.UseSslStream.Value;
            //}
            if (props.WriteBufferSize is not null)
            {
                csb.WriteBufferSize = props.WriteBufferSize.Value;
            }
            if (props.WriteCoalescingBufferThresholdBytes is not null)
            {
                csb.WriteCoalescingBufferThresholdBytes = props.WriteCoalescingBufferThresholdBytes.Value;
            }
            //if (props.WriteCoalescingDelayUs is not null)
            //{
            //    csb.WriteCoalescingDelayUs = props.WriteCoalescingDelayUs.Value;
            //}
        }

        public string GetConnectionString(ILogger? logger)
        {
            if (string.IsNullOrEmpty(_connectionString))
            {
                var csb = new NpgsqlConnectionStringBuilder();
                csb.Timeout = DefaultConnectTimeout; // the connect timeout; any level that sets Timeout overrides it below
                if (_globalProperties is not null)
                {
                    SetProperties(csb, _globalProperties);
                }
                if (_shardSetProperties is not null)
                {
                    SetProperties(csb, _shardSetProperties);
                }
                if (_readWriteProperties is not null)
                {
                    SetProperties(csb, _readWriteProperties);
                }
                if (_shardProperties is not null)
                {
                    SetProperties(csb, _shardProperties);
                }
                SetProperties(csb, this);
                _connectionString = csb.ToString();
                // Refresh the description from the effective (built) values so it matches the connection actually used.
                _connectionDescription = $"database {csb.Database} on server {csb.Host}";
                var logCS = _connectionString;
                var pwd = csb.Password;
                if (!string.IsNullOrEmpty(pwd))
                {
                    logCS = logCS.Replace(pwd, "********");
                }
                logger?.SqlConnectionStringBuilt(logCS);
            }
            return _connectionString;
        }

        public void SetAmbientConfiguration(DataConnectionConfigurationBase? globalProperties, DataConnectionConfigurationBase? shardSetProperties, DataConnectionConfigurationBase? readWriteProperties, DataConnectionConfigurationBase? shardProperties)
        {
            // Detach from any previous inheritance chain so repeated calls neither accumulate handlers nor keep listening to stale sources.
            UnsubscribeAmbient();
            _globalProperties = globalProperties as PgConnectionPropertiesBase;
            _shardSetProperties = shardSetProperties as PgConnectionPropertiesBase;
            _readWriteProperties = readWriteProperties as PgConnectionPropertiesBase;
            _shardProperties = shardProperties as PgConnectionPropertiesBase;
            SetAmbientResilience(_globalProperties, _shardSetProperties, _readWriteProperties, _shardProperties);
            // The inheritance chain changed, so any cached connection string or description is stale.
            _connectionString = null;
            _connectionDescription = null;

            if (_globalProperties is not null)
            {
                _globalProperties.PropertyChanged += HandlePropertyChanged;
            }
            if (_shardSetProperties is not null)
            {
                _shardSetProperties.PropertyChanged += HandlePropertyChanged;
            }
            if (_readWriteProperties is not null)
            {
                _readWriteProperties.PropertyChanged += HandlePropertyChanged;
            }
            if (_shardProperties is not null)
            {
                _shardProperties.PropertyChanged += HandlePropertyChanged;
            }
        }

        private void UnsubscribeAmbient()
        {
            if (!(_globalProperties is null))
            {
                _globalProperties.PropertyChanged -= HandlePropertyChanged;
            }
            if (!(_shardSetProperties is null))
            {
                _shardSetProperties.PropertyChanged -= HandlePropertyChanged;
            }
            if (!(_readWriteProperties is null))
            {
                _readWriteProperties.PropertyChanged -= HandlePropertyChanged;
            }
            if (!(_shardProperties is null))
            {
                _shardProperties.PropertyChanged -= HandlePropertyChanged;
            }
        }

        int? IDataConnection.RetryCount => this.InheritedRetryCount;

        int? IDataConnection.CircuitBreakerFailureCount => this.InheritedCircuitBreakerFailureCount;

        int? IDataConnection.CircuitBreakerTestInterval => this.InheritedCircuitBreakerTestInterval;

        public string ConnectionDescription
        {
            get
            {
                if (string.IsNullOrEmpty(this._connectionDescription))
                {
                    string? database = _globalProperties?.Database;
                    string? host = _globalProperties?.Host;
                    if (!string.IsNullOrEmpty(_shardSetProperties?.Database))
                    {
                        database = _shardSetProperties.Database;
                    }
                    if (!string.IsNullOrEmpty(_shardSetProperties?.Host))
                    {
                        host = _shardSetProperties.Host;
                    }

                    if (!string.IsNullOrEmpty(_readWriteProperties?.Database))
                    {
                        database = _readWriteProperties.Database;
                    }
                    if (!string.IsNullOrEmpty(_readWriteProperties?.Host))
                    {
                        host = _readWriteProperties.Host;
                    }

                    if (!string.IsNullOrEmpty(_shardProperties?.Database))
                    {
                        database = _shardProperties.Database;
                    }
                    if (!string.IsNullOrEmpty(_shardProperties?.Host))
                    {
                        host = _shardProperties.Host;
                    }

                    if (!string.IsNullOrEmpty(this.Database))
                    {
                        database = this.Database;
                    }
                    if (!string.IsNullOrEmpty(this.Host))
                    {
                        host = this.Host;
                    }
                    _connectionDescription = $"database {database} on server {host}";
                }
                return this._connectionDescription;
            }
        }
    }
}
