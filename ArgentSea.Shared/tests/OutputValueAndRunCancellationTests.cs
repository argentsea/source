using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Output-parameter value reads accept a null when the requested type can hold one, and the non-batch Run path
    /// passes the caller’s cancellation token to the command.
    /// </summary>
    public class OutputValueAndRunCancellationTests
    {
        private class TestParameter : DbParameter
        {
            public override DbType DbType { get; set; }
            public override ParameterDirection Direction { get; set; } = ParameterDirection.Output;
            public override bool IsNullable { get; set; }
            [AllowNull] public override string ParameterName { get; set; } = string.Empty;
            public override int Size { get; set; }
            [AllowNull] public override string SourceColumn { get; set; } = string.Empty;
            public override bool SourceColumnNullMapping { get; set; }
            public override object? Value { get; set; }
            public override void ResetDbType() { }
        }

        private static DbParameter Output(object? value) => new TestParameter { ParameterName = "@Out", Value = value };

        [Fact]
        public void NullOutputForNullableValueTypeReturnsNull()
        {
            DataConnectionManager.ReadOutputValue<int?>(Output(DBNull.Value), "q", "@Out").Should().BeNull();
            DataConnectionManager.ReadOutputValue<int?>(Output(null), "q", "@Out").Should().BeNull();
        }

        [Fact]
        public void NullOutputForReferenceTypeReturnsNull()
        {
            DataConnectionManager.ReadOutputValue<string>(Output(DBNull.Value), "q", "@Out").Should().BeNull();
        }

        [Fact]
        public void NullOutputForNonNullableValueTypeThrows()
        {
            Action act = () => DataConnectionManager.ReadOutputValue<int>(Output(DBNull.Value), "q", "@Out");
            act.Should().Throw<UnexpectedSqlResultException>().WithMessage("*q*@Out*null*");
        }

        [Fact]
        public void NonNullOutputIsReturned()
        {
            DataConnectionManager.ReadOutputValue<int?>(Output(7), "q", "@Out").Should().Be(7);
            DataConnectionManager.ReadOutputValue<int>(Output(7), "q", "@Out").Should().Be(7);
        }

        private class OpenConnection : DbConnection
        {
            public OpenConnection(string connectionString) => this.ConnectionString = connectionString;
            [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
            public override string Database => "test";
            public override string DataSource => "test";
            public override string ServerVersion => "test";
            public override ConnectionState State => ConnectionState.Open;
            public override void ChangeDatabase(string databaseName) { }
            public override void Close() { }
            public override void Open() { }
            public override Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
            protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        }

        private class CommandServices : IDataProviderServiceFactory
        {
            public DbCommand Command { get; } = Substitute.For<DbCommand>();
            public bool GetIsErrorTransient(Exception exception) => false;
            public DbConnection NewConnection(string connectionString) => new OpenConnection(connectionString);
            public DbCommand NewCommand(string storedProcedureName, DbConnection connection) => Command;
            public void SetParameters(DbCommand cmd, string[]? parmeterNames, DbParameterCollection parameters, IDictionary<string, object>? parameterValues) { }
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

        [Fact]
        public async Task RunPassesTheCancellationTokenToTheCommand()
        {
            var services = new CommandServices();
            var connection = new SimpleDbConnection { ConnectionString = "shard1", RetryCount = 0, CircuitBreakerFailureCount = 0 };
            var options = new TestShardSetsOptions
            {
                ShardSetsConfigInternal = new IShardSetConnectionsConfiguration[]
                {
                    new TestShardSetConfiguration
                    {
                        ShardSetName = "Set",
                        DefaultShardId = 1,
                        ShardsConfigInternal = new IShardConnectionConfiguration[] { new TestShardConfiguration { ShardId = 1, ReadConnectionInternal = connection, WriteConnectionInternal = connection } }
                    }
                }
            };
            var shardSets = new TestShardSets(options, services);
            using var cts = new CancellationTokenSource();

            await shardSets["Set"][1].Write.RunAsync(new QueryProcedure("ws.RunCancellationTest"), new ParameterCollection(), cts.Token);

            await services.Command.Received(1).ExecuteNonQueryAsync(Arg.Is<CancellationToken>(t => t.CanBeCanceled));
        }
    }
}
