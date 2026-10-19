// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;

namespace ArgentSea.TestKit
{
    public enum FakeExecutionKind
    {
        Reader,
        NonQuery,
        Scalar
    }

    public enum TransactionEventKind
    {
        Begin,
        Commit,
        Rollback
    }

    /// <summary>
    /// One execution of a command by the fake provider. Parameter values are captured as bound (after any
    /// per-execution <c>parameterValues</c> overrides), so a shard's own id is visible for the shard parameter.
    /// </summary>
    public sealed class RecordedCommand
    {
        internal RecordedCommand(int sequence, FakeDbCommand command, FakeExecutionKind kind, CommandBehavior behavior, CancellationToken cancellationToken, int attempt)
        {
            Sequence = sequence;
            Command = command;
            Kind = kind;
            Behavior = behavior;
            CancellationToken = cancellationToken;
            Attempt = attempt;
            var connection = command.FakeConnection;
            ConnectionString = connection?.ConnectionString;
            Connection = connection?.Info ?? FakeConnectionInfo.Parse(null);
            CommandText = command.CommandText;
            CommandType = command.CommandType;
            TransactionId = (command.Transaction as FakeDbTransaction)?.Id;
            var bound = new Dictionary<string, object?>(StringComparer.Ordinal);
            var list = new List<FakeDbParameter>();
            foreach (FakeDbParameter prm in command.Parameters)
            {
                list.Add(prm);
                if (!string.IsNullOrEmpty(prm.ParameterName))
                {
                    bound[prm.ParameterName] = prm.Value;
                }
            }
            ParameterValues = bound;
            Parameters = list;
        }

        /// <summary>The order in which the provider executed this command, across every connection (1-based).</summary>
        public int Sequence { get; }

        public FakeDbCommand Command { get; }

        public string? ConnectionString { get; }

        /// <summary>The shard set, shard (or database) and endpoint the command ran against.</summary>
        public FakeConnectionInfo Connection { get; }

        /// <summary>The shard id from the connection string, or null for a database connection.</summary>
        public short? ShardId => Connection.ShardId;

        public string CommandText { get; }

        public CommandType CommandType { get; }

        public FakeExecutionKind Kind { get; }

        public CommandBehavior Behavior { get; }

        /// <summary>Parameter values as bound, keyed by the parameter name as declared (including any leading '@').</summary>
        public IReadOnlyDictionary<string, object?> ParameterValues { get; }

        /// <summary>The command's parameter objects, so output values set by the script are visible after execution.</summary>
        public IReadOnlyList<FakeDbParameter> Parameters { get; }

        public CancellationToken CancellationToken { get; }

        /// <summary>The <see cref="FakeDbTransaction.Id"/> the command was enlisted in, or null.</summary>
        public int? TransactionId { get; }

        /// <summary>The 1-based execution attempt of this (query, shard), across retries.</summary>
        public int Attempt { get; }

        /// <summary>The exception the execution threw, if any (set after the command runs).</summary>
        public Exception? Exception { get; internal set; }

        /// <summary>True when no script matched the command and the provider returned its defaults.</summary>
        public bool Unscripted { get; internal set; }

        /// <summary>Returns a bound parameter value by name, with or without a leading '@'.</summary>
        public object? Parameter(string name)
        {
            if (ParameterValues.TryGetValue(name, out var value))
            {
                return value;
            }
            var alternate = name.StartsWith("@", StringComparison.Ordinal) ? name.Substring(1) : "@" + name;
            if (ParameterValues.TryGetValue(alternate, out value))
            {
                return value;
            }
            throw new KeyNotFoundException($"No parameter named {name} was bound to {CommandText}.");
        }

        public override string ToString() => $"#{Sequence} {CommandText} on {ConnectionString} ({Kind}, attempt {Attempt})";
    }

    public sealed class TransactionEvent
    {
        internal TransactionEvent(int sequence, FakeDbTransaction transaction, TransactionEventKind kind, bool implicitRollback)
        {
            Sequence = sequence;
            TransactionId = transaction.Id;
            Kind = kind;
            ImplicitRollback = implicitRollback;
            Connection = ((FakeDbConnection)transaction.Connection!).Info; // a fake transaction keeps its connection
            IsolationLevel = transaction.IsolationLevel;
        }

        public int Sequence { get; }
        public int TransactionId { get; }
        public TransactionEventKind Kind { get; }
        /// <summary>True when the rollback was caused by disposing an uncommitted transaction rather than an explicit Rollback().</summary>
        public bool ImplicitRollback { get; }
        public FakeConnectionInfo Connection { get; }
        public short? ShardId => Connection.ShardId;
        public IsolationLevel IsolationLevel { get; }

        public override string ToString() => $"#{Sequence} transaction {TransactionId} {Kind}{(ImplicitRollback ? " (on dispose)" : string.Empty)} on {Connection}";
    }

    public sealed class ConnectionEvent
    {
        internal ConnectionEvent(int sequence, FakeDbConnection connection)
        {
            Sequence = sequence;
            Connection = connection.Info;
            ConnectionString = connection.ConnectionString;
        }

        public int Sequence { get; }
        public FakeConnectionInfo Connection { get; }
        public string? ConnectionString { get; }
        public short? ShardId => Connection.ShardId;
    }

    /// <summary>
    /// Thread-safe record of everything the fake provider did: connections opened, commands executed (in execution order)
    /// and transaction begin/commit/rollback events. One sequence runs across all three, so their relative order is visible.
    /// </summary>
    public sealed class CommandRecorder
    {
        private readonly object _sync = new object();
        private readonly List<RecordedCommand> _commands = new List<RecordedCommand>();
        private readonly List<TransactionEvent> _transactions = new List<TransactionEvent>();
        private readonly List<ConnectionEvent> _connections = new List<ConnectionEvent>();
        private int _sequence;

        /// <summary>Every executed command, in execution order (a snapshot).</summary>
        public IReadOnlyList<RecordedCommand> Commands
        {
            get { lock (_sync) { return _commands.ToArray(); } }
        }

        public IReadOnlyList<TransactionEvent> TransactionEvents
        {
            get { lock (_sync) { return _transactions.ToArray(); } }
        }

        public IReadOnlyList<ConnectionEvent> ConnectionsOpened
        {
            get { lock (_sync) { return _connections.ToArray(); } }
        }

        public IReadOnlyList<RecordedCommand> CommandsFor(string commandText)
            => Commands.Where(c => string.Equals(c.CommandText, commandText, StringComparison.Ordinal)).ToList();

        public IReadOnlyList<RecordedCommand> CommandsFor(Query query) => CommandsFor(query.Sql);

        public IReadOnlyList<RecordedCommand> CommandsForShard(short shardId)
            => Commands.Where(c => c.ShardId == shardId).ToList();

        public RecordedCommand? CommandFor(string commandText, short shardId)
            => Commands.FirstOrDefault(c => c.ShardId == shardId && string.Equals(c.CommandText, commandText, StringComparison.Ordinal));

        public RecordedCommand? CommandFor(Query query, short shardId) => CommandFor(query.Sql, shardId);

        /// <summary>The distinct shard ids that executed at least one command, in first-execution order.</summary>
        public IReadOnlyList<short> ShardsExecuted
            => Commands.Where(c => c.ShardId.HasValue).Select(c => c.ShardId.GetValueOrDefault()).Distinct().ToList();

        public void Clear()
        {
            lock (_sync)
            {
                _commands.Clear();
                _transactions.Clear();
                _connections.Clear();
            }
        }

        internal RecordedCommand RecordCommand(FakeDbCommand command, FakeExecutionKind kind, CommandBehavior behavior, CancellationToken cancellationToken, int attempt)
        {
            lock (_sync)
            {
                var record = new RecordedCommand(++_sequence, command, kind, behavior, cancellationToken, attempt);
                _commands.Add(record);
                return record;
            }
        }

        internal void RecordTransaction(FakeDbTransaction transaction, TransactionEventKind kind, bool implicitRollback = false)
        {
            lock (_sync)
            {
                _transactions.Add(new TransactionEvent(++_sequence, transaction, kind, implicitRollback));
            }
        }

        internal void RecordConnectionOpened(FakeDbConnection connection)
        {
            lock (_sync)
            {
                _connections.Add(new ConnectionEvent(++_sequence, connection));
            }
        }
    }
}
