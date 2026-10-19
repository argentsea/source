// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ArgentSea;

namespace ArgentSea.Sql
{
    /// <summary>
    /// This class represents a single database connection — a database connection or a shard instance read or write connection.
    /// </summary>
    public class SqlConnectionConfiguration : SqlConnectionPropertiesBase, IDataConnection
    {
        private string? _connectionString;
        private SqlConnectionPropertiesBase? _globalProperties;
        private SqlConnectionPropertiesBase? _shardSetProperties;
        private SqlConnectionPropertiesBase? _readWriteProperties;
        private SqlConnectionPropertiesBase? _shardProperties;
        private readonly object _connectionLock = new object();
        private string? _connectionDescription;

        private const int DefaultConnectTimeout = 5; //minimum recommended value per https://docs.microsoft.com/en-us/sql/database-engine/database-mirroring/connect-clients-to-a-database-mirroring-session-sql-server?view=sql-server-2017#RetryAlgorithm

        public SqlConnectionConfiguration()
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

        private void SetProperties(SqlConnectionStringBuilder csb, DataConnectionConfigurationBase properties)
        {
            if (properties.Password is not null)
            {
                csb.Password = properties.Password;
            }
            if (properties.UserName is not null)
            {
                csb.UserID = properties.UserName;
            }
            if (properties.WindowsAuth is not null)
            {
                csb.IntegratedSecurity = properties.WindowsAuth.Value;
            }
            var props = (SqlConnectionPropertiesBase)properties;
            if (props.ApplicationIntent is not null)
            {
                csb.ApplicationIntent = props.ApplicationIntent.Value;
            }
            if (props.ApplicationName is not null)
            {
                csb.ApplicationName = props.ApplicationName;
            }
            //if (props.ConnectRetryCount is not null)
            //{
            //    csb.ConnectRetryCount = props.ConnectRetryCount.Value;
            //}
            //if (props.ConnectRetryInterval is not null)
            //{
            //    csb.ConnectRetryInterval = props.ConnectRetryInterval.Value;
            //}
            if (props.ConnectTimeout is not null)
            {
                csb.ConnectTimeout = props.ConnectTimeout.Value;
            }
            if (props.CurrentLanguage is not null)
            {
                csb.CurrentLanguage = props.CurrentLanguage;
            }
            if (props.DataSource is not null)
            {
                csb.DataSource = props.DataSource;
            }
            if (props.Encrypt is not null)
            {
                csb.Encrypt = props.Encrypt.Value;
            }
            if (props.FailoverPartner is not null)
            {
                csb.FailoverPartner = props.FailoverPartner;
            }
            if (props.InitialCatalog is not null)
            {
                csb.InitialCatalog = props.InitialCatalog;
            }
            if (props.LoadBalanceTimeout is not null)
            {
                csb.LoadBalanceTimeout = props.LoadBalanceTimeout.Value;
            }
            if (props.MaxPoolSize is not null)
            {
                csb.MaxPoolSize = props.MaxPoolSize.Value;
            }
            if (props.MinPoolSize is not null)
            {
                csb.MinPoolSize = props.MinPoolSize.Value;
            }
            if (props.MultipleActiveResultSets is not null)
            {
                csb.MultipleActiveResultSets = props.MultipleActiveResultSets.Value;
            }
            if (props.MultiSubnetFailover is not null)
            {
                csb.MultiSubnetFailover = props.MultiSubnetFailover.Value;
            }
            if (props.PacketSize is not null)
            {
                csb.PacketSize = props.PacketSize.Value;
            }
            if (props.PersistSecurityInfo is not null)
            {
                csb.PersistSecurityInfo = props.PersistSecurityInfo.Value;
            }
            if (props.Pooling is not null)
            {
                csb.Pooling = props.Pooling.Value;
            }
            if (props.Replication is not null)
            {
                csb.Replication = props.Replication.Value;
            }
            if (props.TrustServerCertificate is not null)
            {
                csb.TrustServerCertificate = props.TrustServerCertificate.Value;
            }
            if (props.TypeSystemVersion is not null)
            {
                csb.TypeSystemVersion = props.TypeSystemVersion;
            }
            if (props.UserInstance is not null)
            {
                csb.UserInstance = props.UserInstance.Value;
            }
            if (props.WorkstationID is not null)
            {
                csb.WorkstationID = props.WorkstationID;
            }
        }

        public string GetConnectionString(ILogger? logger)
        {
            if (string.IsNullOrEmpty(_connectionString))
            {
                var csb = new SqlConnectionStringBuilder();
                csb.ConnectTimeout = DefaultConnectTimeout;
                csb.ConnectRetryCount = 0;
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
                _connectionDescription = $"database {csb.InitialCatalog} on server {csb.DataSource}";
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
            _globalProperties = globalProperties as SqlConnectionPropertiesBase;
            _shardSetProperties = shardSetProperties as SqlConnectionPropertiesBase;
            _readWriteProperties = readWriteProperties as SqlConnectionPropertiesBase;
            _shardProperties = shardProperties as SqlConnectionPropertiesBase;
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
                    string? initialCatalog = _globalProperties?.InitialCatalog;
                    string? dataSource = _globalProperties?.DataSource;
                    if (!string.IsNullOrEmpty(_shardSetProperties?.InitialCatalog))
                    {
                        initialCatalog = _shardSetProperties.InitialCatalog;
                    }
                    if (!string.IsNullOrEmpty(_shardSetProperties?.DataSource))
                    {
                        dataSource = _shardSetProperties.DataSource;
                    }

                    if (!string.IsNullOrEmpty(_readWriteProperties?.InitialCatalog))
                    {
                        initialCatalog = _readWriteProperties.InitialCatalog;
                    }
                    if (!string.IsNullOrEmpty(_readWriteProperties?.DataSource))
                    {
                        dataSource = _readWriteProperties.DataSource;
                    }

                    if (!string.IsNullOrEmpty(_shardProperties?.InitialCatalog))
                    {
                        initialCatalog = _shardProperties.InitialCatalog;
                    }
                    if (!string.IsNullOrEmpty(_shardProperties?.DataSource))
                    {
                        dataSource = _shardProperties.DataSource;
                    }

                    if (!string.IsNullOrEmpty(this.InitialCatalog))
                    {
                        initialCatalog = this.InitialCatalog;
                    }
                    if (!string.IsNullOrEmpty(this.DataSource))
                    {
                        dataSource = this.DataSource;
                    }
                    _connectionDescription = $"database {initialCatalog} on server {dataSource}";
                }
                return this._connectionDescription;
            }
        }
    }
}
