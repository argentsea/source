using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// A shard set whose connections never reach a server: every command returns a single-row reader with an Id column of 7,
    /// and output/return parameters are left unset.
    /// </summary>
    internal sealed class InMemoryShardSets : ShardSetsBase<SqlShardConnectionOptions>
    {
        private InMemoryShardSets(IOptions<SqlShardConnectionOptions> configOptions, SqlGlobalPropertiesOptions globalOptions)
            : base(configOptions, new InMemoryProviderFactory(), globalOptions, null)
        {
        }

        public static InMemoryShardSets FromTestConfiguration()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("configurationsettings.json")
                .Build();
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging();
            services.AddSqlServices(config);
            var serviceProvider = services.BuildServiceProvider();
            var globalOptions = serviceProvider.GetRequiredService<IOptions<SqlGlobalPropertiesOptions>>();
            var shardOptions = serviceProvider.GetRequiredService<IOptions<SqlShardConnectionOptions>>();
            return new InMemoryShardSets(shardOptions, globalOptions.Value);
        }
    }

    internal sealed class InMemoryProviderFactory : IDataProviderServiceFactory
    {
        public bool GetIsErrorTransient(Exception exception) => false;
        public DbConnection NewConnection(string connectionString) => new InMemoryConnection();
        public DbCommand NewCommand(string storedProcedureName, DbConnection connection) => new InMemoryCommand { Connection = connection };
        public void SetParameters(DbCommand cmd, string[]? parmeterNames, DbParameterCollection parameters, IDictionary<string, object>? parameterValues)
        {
        }
    }

    internal sealed class InMemoryConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;
        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "InMemory";
        public override string DataSource => "InMemory";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new InMemoryCommand { Connection = this };
    }

    internal sealed class InMemoryCommand : DbCommand
    {
        private readonly SqlParameterCollection _parameters = new SqlCommand().Parameters;
        [AllowNull] public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new SqlParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            table.Rows.Add(7);
            return table.CreateDataReader();
        }
    }
}
