using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Non-query batch steps must pass the batch's cancellation token to ExecuteNonQueryAsync,
    /// so a cancellation is observed while the step's command is running.
    /// </summary>
    public class BatchCancellationTests
    {
        private sealed class TokenCapturingCommand : DbCommand
        {
            public List<CancellationToken> NonQueryTokens { get; } = new();

            public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            {
                NonQueryTokens.Add(cancellationToken);
                return Task.FromResult(0);
            }

            [AllowNull] public override string CommandText { get; set; } = string.Empty;
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; }
            protected override DbParameterCollection DbParameterCollection { get; } = new ParameterCollection();
            protected override DbTransaction? DbTransaction { get; set; }
            public override void Cancel() { }
            public override int ExecuteNonQuery() => throw new NotSupportedException();
            public override object ExecuteScalar() => throw new NotSupportedException();
            public override void Prepare() { }
            protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        }

        private sealed class FakeServices : IDataProviderServiceFactory
        {
            public TokenCapturingCommand Command { get; } = new();
            public bool GetIsErrorTransient(Exception exception) => false;
            public DbConnection NewConnection(string connectionString) => throw new NotSupportedException();
            public DbCommand NewCommand(string storedProcedureName, DbConnection connection) => Command;
            public void SetParameters(DbCommand cmd, string[]? parmeterNames, DbParameterCollection parameters, IDictionary<string, object>? parameterValues) { }
        }

        [Fact]
        public async Task DatabaseBatch_QueryStep_PassesCancellationToken()
        {
            using var cts = new CancellationTokenSource();
            var services = new FakeServices();
            var batch = new DatabaseBatch<object>().Add(new QueryProcedure("ws.Step"));

            await batch.ExecuteAsync(0, null!, null!, "db", services, NullLogger.Instance, cts.Token);

            services.Command.NonQueryTokens.Should().ContainSingle().Which.Should().Be(cts.Token);
        }

        [Fact]
        public async Task ShardBatch_QueryStep_PassesCancellationToken()
        {
            using var cts = new CancellationTokenSource();
            var services = new FakeServices();
            var batch = new ShardBatch<object>().Add(new QueryProcedure("ws.Step"));

            await batch.ExecuteAsync(1, null!, null!, "shard", services, NullLogger.Instance, cts.Token);

            services.Command.NonQueryTokens.Should().ContainSingle().Which.Should().Be(cts.Token);
        }

        [Fact]
        public async Task ShardSetBatch_QueryStep_PassesCancellationToken()
        {
            using var cts = new CancellationTokenSource();
            var services = new FakeServices();
            var batch = new ShardSetBatch().Add(new QueryProcedure("ws.Step"));

            await batch.ExecuteAsync(1, null!, null!, "shard", services, NullLogger.Instance, cts.Token);

            services.Command.NonQueryTokens.Should().ContainSingle().Which.Should().Be(cts.Token);
        }
    }
}
