using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using ArgentSea.TestKit;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// A ShardSet fan-out passes the caller’s parameter collection to every shard; each shard must still send its own shard id.
    /// </summary>
    public class ShardParameterIsolationTests
    {
        private const string ShardParameterName = "@ShardId";
        private static readonly short[] ShardIds = { 1, 2, 3, 4 };

        private class TestParameter : DbParameter
        {
            public override DbType DbType { get; set; }
            public override ParameterDirection Direction { get; set; }
            public override bool IsNullable { get; set; }
            [AllowNull] public override string ParameterName { get; set; } = string.Empty;
            public override int Size { get; set; }
            [AllowNull] public override string SourceColumn { get; set; } = string.Empty;
            public override bool SourceColumnNullMapping { get; set; }
            public override object? Value { get; set; }
            public override void ResetDbType() { }
        }

        /// <summary>
        /// A connection whose open completes asynchronously, as a real network open does.
        /// </summary>
        private class SlowOpenConnection : DbConnection
        {
            public SlowOpenConnection(string connectionString) => this.ConnectionString = connectionString;
            [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
            public override string Database => "test";
            public override string DataSource => "test";
            public override string ServerVersion => "test";
            public override ConnectionState State => ConnectionState.Open;
            public override void ChangeDatabase(string databaseName) { }
            public override void Close() { }
            public override void Open() { }
            public override async Task OpenAsync(CancellationToken cancellationToken) => await Task.Delay(50, cancellationToken);
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
            protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        }

        /// <summary>
        /// A command that returns an empty result, so every shard of a fan-out runs to completion. (An earlier version of
        /// these tests stopped each shard by throwing once its parameters were recorded; under load the fan-out's
        /// cancel-on-fault then cancelled sibling shards before they had recorded theirs.)
        /// </summary>
        private class EmptyResultCommand : DbCommand
        {
            private readonly ParameterCollection _parameters = new ParameterCollection();
            [AllowNull] public override string CommandText { get; set; } = string.Empty;
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; }
            protected override DbParameterCollection DbParameterCollection => _parameters;
            protected override DbTransaction? DbTransaction { get; set; }
            public override void Cancel() { }
            public override void Prepare() { }
            protected override DbParameter CreateDbParameter() => new TestParameter();
            public override int ExecuteNonQuery() => 0;
            public override object? ExecuteScalar() => null;
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => new FakeDbDataReader(Array.Empty<FakeResultSet>());
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
                => Task.FromResult<DbDataReader>(new FakeDbDataReader(Array.Empty<FakeResultSet>()));
        }

        /// <summary>
        /// Records the value each parameter of each shard’s command is built with — the per-execution value if one is supplied,
        /// otherwise the collection’s value, as the providers apply them — and lets the query complete with no rows.
        /// </summary>
        private class RecordingServices : IDataProviderServiceFactory
        {
            private readonly ConcurrentDictionary<DbCommand, string> _commandConnections = new ConcurrentDictionary<DbCommand, string>();
            public ConcurrentDictionary<string, Dictionary<string, object?>> ValuesSent { get; } = new ConcurrentDictionary<string, Dictionary<string, object?>>();
            public object? ShardIdSent(short shardId) => ValuesSent[ConnectionName(shardId)][ShardParameterName];
            public bool GetIsErrorTransient(Exception exception) => false;
            public DbConnection NewConnection(string connectionString) => new SlowOpenConnection(connectionString);
            public DbCommand NewCommand(string storedProcedureName, DbConnection connection)
            {
                var cmd = new EmptyResultCommand { CommandText = storedProcedureName, Connection = connection };
                _commandConnections[cmd] = connection.ConnectionString;
                return cmd;
            }
            public void SetParameters(DbCommand cmd, string[]? parmeterNames, DbParameterCollection parameters, IDictionary<string, object>? parameterValues)
            {
                var sent = new Dictionary<string, object?>();
                foreach (DbParameter prm in parameters)
                {
                    sent[prm.ParameterName] = parameterValues is not null && parameterValues.TryGetValue(prm.ParameterName, out var value) ? value : prm.Value;
                }
                ValuesSent[_commandConnections[cmd]] = sent;
            }
        }

        private class TestShardConfiguration : IShardConnectionConfiguration
        {
            public short ShardId { get; set; }
            public IDataConnection? ReadConnectionInternal { get; set; }
            public IDataConnection? WriteConnectionInternal { get; set; }
        }

        private class TestShardSetConfiguration : IShardSetConnectionsConfiguration
        {
            public string? ShardSetName { get; set; }
            public short DefaultShardId { get; set; }
            public IShardConnectionConfiguration?[]? ShardsConfigInternal { get; set; }
            public IShardConnectionConfiguration? ReadConfigInternal => null;
            public IShardConnectionConfiguration? WriteConfigInternal => null;
        }

        private class TestShardSetsOptions : IShardSetsConfigurationOptions
        {
            public IShardSetConnectionsConfiguration?[]? ShardSetsConfigInternal { get; set; }
        }

        private class TestShardSets : ShardSetsBase<TestShardSetsOptions>
        {
            public TestShardSets(TestShardSetsOptions options, IDataProviderServiceFactory services)
                : base(Options.Create(options), services, null, null)
            {
            }
        }

        public class Model
        {
        }

        private static string ConnectionName(short shardId) => $"shard{shardId}";

        private static TestShardSets BuildShardSets(RecordingServices services)
        {
            var shards = new List<IShardConnectionConfiguration>();
            foreach (var shardId in ShardIds)
            {
                // CircuitBreakerFailureCount 0 disables the breaker, so the recorded failures do not open it.
                var connection = new SimpleDbConnection { ConnectionString = ConnectionName(shardId), RetryCount = 0, CircuitBreakerFailureCount = 0 };
                shards.Add(new TestShardConfiguration { ShardId = shardId, ReadConnectionInternal = connection, WriteConnectionInternal = connection });
            }
            var options = new TestShardSetsOptions
            {
                ShardSetsConfigInternal = new IShardSetConnectionsConfiguration[]
                {
                    new TestShardSetConfiguration { ShardSetName = "Set", DefaultShardId = ShardIds[0], ShardsConfigInternal = shards.ToArray() }
                }
            };
            return new TestShardSets(options, services);
        }

        private const string OtherParameterName = "@Other";

        private static ParameterCollection NewParameters()
        {
            var parameters = new ParameterCollection();
            parameters.Add(new TestParameter { ParameterName = ShardParameterName, DbType = DbType.Int16, Value = (short)-1 });
            parameters.Add(new TestParameter { ParameterName = OtherParameterName, DbType = DbType.String, Value = "shared" });
            return parameters;
        }

        [Fact]
        public async Task ReadFirstSendsEachShardItsOwnShardId()
        {
            var services = new RecordingServices();
            var shardSets = BuildShardSets(services);
            var parameters = NewParameters();

            var result = await shardSets["Set"].ReadFirst.MapReaderAsync<Model>(new QueryProcedure("ws.ReadFirstShardIdTest"), parameters, ShardParameterName, CancellationToken.None);
            result.Should().BeNull("no shard returned a row");

            services.ValuesSent.Should().HaveCount(ShardIds.Length);
            foreach (var shardId in ShardIds)
            {
                services.ShardIdSent(shardId).Should().Be(shardId, $"shard {shardId} must receive its own shard id");
                services.ValuesSent[ConnectionName(shardId)][OtherParameterName].Should().Be("shared");
            }
            parameters[ShardParameterName].Value.Should().Be((short)-1, "the caller’s parameter collection is not changed");
        }

        [Fact]
        public async Task ReadAllSendsShardIdsAndShardSpecificValues()
        {
            var services = new RecordingServices();
            var shardSets = BuildShardSets(services);
            var parameters = NewParameters();
            var shardsValues = new ShardsValues()
                .Add(ShardIds[0], OtherParameterName, "first")
                .Add(ShardIds[1])
                .Add(ShardIds[2], ShardParameterName, (short)42);

            var result = await shardSets["Set"].ReadAll.MapListAsync<Model>(new QueryProcedure("ws.ReadAllShardIdTest"), parameters, shardsValues, ShardParameterName, CancellationToken.None);
            result.Should().BeEmpty("no shard returned a row");

            services.ValuesSent.Should().HaveCount(3, "only the listed shards are queried");
            services.ShardIdSent(ShardIds[0]).Should().Be(ShardIds[0]);
            services.ValuesSent[ConnectionName(ShardIds[0])][OtherParameterName].Should().Be("first", "a shard-specific value applies to its shard");
            services.ShardIdSent(ShardIds[1]).Should().Be(ShardIds[1]);
            services.ValuesSent[ConnectionName(ShardIds[1])][OtherParameterName].Should().Be("shared", "other shards keep the shared value");
            services.ShardIdSent(ShardIds[2]).Should().Be((short)42, "an explicit shard-specific value for the shard parameter takes precedence");
            parameters[ShardParameterName].Value.Should().Be((short)-1, "the caller’s parameter collection is not changed");
            parameters[OtherParameterName].Value.Should().Be("shared");
        }

        [Fact]
        public async Task WriteRunSendsEachShardItsOwnShardId()
        {
            var services = new RecordingServices();
            var shardSets = BuildShardSets(services);
            var parameters = NewParameters();

            await shardSets["Set"].Write.RunAsync(new QueryProcedure("ws.WriteShardIdTest"), parameters, null!, ShardParameterName, CancellationToken.None);

            foreach (var shardId in ShardIds)
            {
                services.ShardIdSent(shardId).Should().Be(shardId, $"shard {shardId} must receive its own shard id");
            }
            parameters[ShardParameterName].Value.Should().Be((short)-1, "the caller’s parameter collection is not changed");
        }
    }
}
