// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace ArgentSea.TestKit
{
    /// <summary>
    /// A command that executes against the owning <see cref="FakeProviderServices"/>' <see cref="ResultScript"/>:
    /// it records itself, throws any scripted fault for this attempt, applies scripted output and return values to its
    /// parameters and returns a <see cref="FakeDbDataReader"/> over the scripted result sets.
    /// </summary>
    public class FakeDbCommand : DbCommand
    {
        private readonly FakeProviderServices _services;
        private readonly ParameterCollection _parameters = new ParameterCollection();
        private DbConnection? _connection;

        internal FakeDbCommand(FakeProviderServices services, string commandText, DbConnection connection)
        {
            _services = services;
            CommandText = commandText;
            _connection = connection;
        }

        public FakeDbConnection? FakeConnection => _connection as FakeDbConnection;

        [AllowNull] public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; } = 30;
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get => _connection; set => _connection = value; }
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => new FakeDbParameter();

        public override int ExecuteNonQuery()
            => ExecuteCoreAsync(FakeExecutionKind.NonQuery, CommandBehavior.Default, CancellationToken.None).GetAwaiter().GetResult().RecordsAffected;

        public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            var reader = await ExecuteCoreAsync(FakeExecutionKind.NonQuery, CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
            return reader.RecordsAffected;
        }

        public override object? ExecuteScalar()
            => Scalar(ExecuteCoreAsync(FakeExecutionKind.Scalar, CommandBehavior.Default, CancellationToken.None).GetAwaiter().GetResult());

        public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
            => Scalar(await ExecuteCoreAsync(FakeExecutionKind.Scalar, CommandBehavior.Default, cancellationToken).ConfigureAwait(false));

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => ExecuteCoreAsync(FakeExecutionKind.Reader, behavior, CancellationToken.None).GetAwaiter().GetResult();

        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
            => await ExecuteCoreAsync(FakeExecutionKind.Reader, behavior, cancellationToken).ConfigureAwait(false);

        private static object? Scalar(FakeDbDataReader reader)
        {
            using (reader)
            {
                return reader.Read() && reader.FieldCount > 0 ? reader.GetValue(0) : null;
            }
        }

        private async Task<FakeDbDataReader> ExecuteCoreAsync(FakeExecutionKind kind, CommandBehavior behavior, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connection = FakeConnection;
            if (!(connection is null) && connection.State != ConnectionState.Open)
            {
                throw new InvalidOperationException($"The connection for {CommandText} is not open.");
            }
            var shardId = connection?.Info?.ShardId;
            var attempt = _services.Script.NextAttempt(CommandText, shardId);
            var record = _services.Recorder.RecordCommand(this, kind, behavior, cancellationToken, attempt);
            var script = _services.Script.Resolve(CommandText, shardId);
            try
            {
                if (script is null)
                {
                    record.Unscripted = true;
                    if (_services.Script.ThrowOnUnscriptedQuery)
                    {
                        throw new InvalidOperationException($"No result is scripted for query '{CommandText}' on {connection?.ConnectionString ?? "an unknown connection"}.");
                    }
                    ApplyReturnValue(null);
                    return new FakeDbDataReader(Array.Empty<FakeResultSet>());
                }
                if (!(script.OnExecute is null))
                {
                    await script.OnExecute(record, cancellationToken).ConfigureAwait(false);
                }
                if (script.Delay > TimeSpan.Zero)
                {
                    await Task.Delay(script.Delay, cancellationToken).ConfigureAwait(false);
                }
                if (script.WaitsForCancellation)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                // A scripted fault is the server's answer to a command that has already been sent (the hook and any delay
                // stand for the round trip), so it wins over a cancellation that arrived meanwhile. Otherwise a sibling's
                // cancel-on-fault racing this command's own fault would turn the fault into an OperationCanceledException.
                var fault = script.FaultFor(attempt);
                if (!(fault is null))
                {
                    if (fault.IsTransient.HasValue)
                    {
                        _services.MarkTransient(fault.Exception, fault.IsTransient.Value);
                    }
                    throw fault.Exception;
                }
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var output in script.OutputValues)
                {
                    var prm = FindParameter(output.Key);
                    if (!(prm is null))
                    {
                        prm.Value = output.Value;
                    }
                }
                ApplyReturnValue(script.ReturnValue);
                return new FakeDbDataReader(script.ResultSets, script.RecordsAffected);
            }
            catch (Exception ex)
            {
                record.Exception = ex;
                throw;
            }
        }

        private void ApplyReturnValue(int? returnValue)
        {
            foreach (DbParameter prm in _parameters)
            {
                if (prm.Direction == ParameterDirection.ReturnValue)
                {
                    prm.Value = returnValue ?? 0;
                }
            }
        }

        private DbParameter? FindParameter(string name)
        {
            var alternate = name.StartsWith("@", StringComparison.Ordinal) ? name.Substring(1) : "@" + name;
            foreach (DbParameter prm in _parameters)
            {
                if (string.Equals(prm.ParameterName, name, StringComparison.OrdinalIgnoreCase) || string.Equals(prm.ParameterName, alternate, StringComparison.OrdinalIgnoreCase))
                {
                    return prm;
                }
            }
            return null;
        }
    }
}
