// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArgentSea.TestKit
{
    /// <summary>
    /// One scripted result set: column names and rows of values.
    /// </summary>
    public sealed class FakeResultSet
    {
        public static readonly FakeResultSet Empty = new FakeResultSet(Array.Empty<string>(), Array.Empty<Type>(), Array.Empty<object[]>());

        public FakeResultSet(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
            : this(columns, new Type[columns.Count], rows)
        {
        }

        public FakeResultSet(IReadOnlyList<string> columns, IReadOnlyList<Type> columnTypes, IReadOnlyList<object?[]> rows)
        {
            Columns = columns ?? throw new ArgumentNullException(nameof(columns));
            ColumnTypes = columnTypes ?? new Type[columns.Count];
            Rows = rows ?? Array.Empty<object[]>();
            if (ColumnTypes.Count != Columns.Count)
            {
                throw new ArgumentException("One column type (or null) is required per column.", nameof(columnTypes));
            }
            for (var i = 0; i < Rows.Count; i++)
            {
                if (Rows[i] is null || Rows[i].Length != Columns.Count)
                {
                    throw new ArgumentException($"Row {i} has {Rows[i]?.Length.ToString() ?? "no"} values, but the result set has {Columns.Count} columns.", nameof(rows));
                }
            }
        }

        public IReadOnlyList<string> Columns { get; }

        /// <summary>Declared column types; a null entry means the type is inferred from the first non-null value in the column.</summary>
        public IReadOnlyList<Type> ColumnTypes { get; }

        public IReadOnlyList<object?[]> Rows { get; }
    }

    /// <summary>
    /// A scripted failure: the exception the fake command throws on a given attempt, and whether the provider reports it as transient.
    /// </summary>
    public sealed class FakeFault
    {
        public FakeFault(Exception exception, bool? isTransient)
        {
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
            IsTransient = isTransient;
        }

        public Exception Exception { get; }

        /// <summary>The scripted transient flag; null defers to the exception itself (a <see cref="FakeDbException"/>'s own <see cref="FakeDbException.IsTransient"/>).</summary>
        public bool? IsTransient { get; }
    }

    /// <summary>
    /// What a query returns (and whether it fails) when the fake provider executes it. Build one with
    /// <see cref="ResultScript.ForQuery(string, short?)"/>; every method returns the same instance for chaining.
    /// </summary>
    public sealed class ScriptedQuery
    {
        private readonly List<FakeResultSet> _resultSets = new List<FakeResultSet>();
        private readonly Dictionary<string, object?> _outputValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<int, FakeFault> _faultsByAttempt = new ConcurrentDictionary<int, FakeFault>();
        private FakeFault? _faultOnEveryAttempt;

        internal ScriptedQuery(string commandText, short? shardId)
        {
            CommandText = commandText;
            ShardId = shardId;
        }

        /// <summary>The procedure name or statement text this script answers.</summary>
        public string CommandText { get; }

        /// <summary>The shard this script is specific to, or null when it answers every shard (and database connections).</summary>
        public short? ShardId { get; }

        public IReadOnlyList<FakeResultSet> ResultSets => _resultSets;

        public IReadOnlyDictionary<string, object?> OutputValues => _outputValues;

        /// <summary>The procedure return value; null leaves a return-value parameter at 0.</summary>
        public int? ReturnValue { get; private set; }

        public int RecordsAffected { get; private set; } = -1;

        /// <summary>A delay applied before the command executes (after it is recorded).</summary>
        public TimeSpan Delay { get; private set; }

        /// <summary>When true the command never completes on its own: it waits until its cancellation token is cancelled and then throws <see cref="OperationCanceledException"/>.</summary>
        public bool WaitsForCancellation { get; private set; }

        /// <summary>An optional hook that runs before the command executes; it receives the command's recorded invocation and token.</summary>
        public Func<RecordedCommand, CancellationToken, Task>? OnExecute { get; private set; }

        /// <summary>Adds a result set. Call again to script a second (third, ...) result set.</summary>
        public ScriptedQuery Returns(string[] columns, params object?[][] rows)
        {
            _resultSets.Add(new FakeResultSet(columns, rows));
            return this;
        }

        /// <summary>Adds a result set with declared column types (null entries are inferred from the data).</summary>
        public ScriptedQuery Returns(string[] columns, Type[] columnTypes, params object?[][] rows)
        {
            _resultSets.Add(new FakeResultSet(columns, columnTypes, rows));
            return this;
        }

        public ScriptedQuery Returns(FakeResultSet resultSet)
        {
            _resultSets.Add(resultSet ?? throw new ArgumentNullException(nameof(resultSet)));
            return this;
        }

        /// <summary>Adds an empty result set (columns but no rows).</summary>
        public ScriptedQuery ReturnsNoRows(params string[] columns)
        {
            _resultSets.Add(new FakeResultSet(columns, Array.Empty<object[]>()));
            return this;
        }

        /// <summary>Sets the value of an output (or input/output) parameter after the command executes. The name matches with or without a leading '@'.</summary>
        public ScriptedQuery WithOutput(string parameterName, object? value)
        {
            _outputValues[parameterName] = value;
            return this;
        }

        public ScriptedQuery WithReturnValue(int returnValue)
        {
            ReturnValue = returnValue;
            return this;
        }

        public ScriptedQuery WithRecordsAffected(int recordsAffected)
        {
            RecordsAffected = recordsAffected;
            return this;
        }

        /// <summary>
        /// Throws <paramref name="exception"/> on the given 1-based attempt (per shard); other attempts run normally.
        /// <paramref name="isTransient"/> tells the provider whether to report the error as transient; null leaves that to the exception
        /// (a <see cref="FakeDbException"/> reports its own <see cref="FakeDbException.IsTransient"/>; anything else is not transient).
        /// </summary>
        public ScriptedQuery ThrowsOnAttempt(int attempt, Exception exception, bool? isTransient = null)
        {
            if (attempt < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(attempt), "Attempts are numbered from 1.");
            }
            _faultsByAttempt[attempt] = new FakeFault(exception, isTransient);
            return this;
        }

        /// <summary>Throws a transient <see cref="FakeDbException"/> on the given 1-based attempt, so the retry policy retries it.</summary>
        public ScriptedQuery ThrowsTransientOnAttempt(int attempt, string message = "transient fake failure")
            => ThrowsOnAttempt(attempt, new FakeDbException(message, isTransient: true), isTransient: true);

        /// <summary>Throws <paramref name="exception"/> on every attempt. See <see cref="ThrowsOnAttempt(int, Exception, bool?)"/> for <paramref name="isTransient"/>.</summary>
        public ScriptedQuery Throws(Exception exception, bool? isTransient = null)
        {
            _faultOnEveryAttempt = new FakeFault(exception, isTransient);
            return this;
        }

        /// <summary>Throws a non-transient <see cref="FakeDbException"/> on every attempt.</summary>
        public ScriptedQuery Throws(string message = "fake failure")
            => Throws(new FakeDbException(message, isTransient: false));

        public ScriptedQuery Delays(TimeSpan delay)
        {
            Delay = delay;
            return this;
        }

        /// <summary>The command blocks until its cancellation token is cancelled, then throws <see cref="OperationCanceledException"/>.</summary>
        public ScriptedQuery WaitsUntilCancelled()
        {
            WaitsForCancellation = true;
            return this;
        }

        public ScriptedQuery OnExecuting(Func<RecordedCommand, CancellationToken, Task> hook)
        {
            OnExecute = hook;
            return this;
        }

        internal FakeFault? FaultFor(int attempt)
        {
            if (_faultsByAttempt.TryGetValue(attempt, out var fault))
            {
                return fault;
            }
            return _faultOnEveryAttempt;
        }
    }

    /// <summary>
    /// The results the fake provider returns, keyed by (query name, shard id). A script registered for a specific shard takes
    /// precedence over one registered for every shard; an unscripted query returns no rows (and a return value of 0).
    /// Query names match a <see cref="QueryProcedure"/>'s procedure name or a <see cref="QueryStatement"/>'s SQL text,
    /// which is what the provider receives; use <see cref="ForQuery(Query, short?)"/> to key by the Query object itself.
    /// </summary>
    public sealed class ResultScript
    {
        private readonly ConcurrentDictionary<(string, short?), ScriptedQuery> _scripts = new ConcurrentDictionary<(string, short?), ScriptedQuery>();
        private readonly ConcurrentDictionary<(string, short?), int> _attempts = new ConcurrentDictionary<(string, short?), int>();

        /// <summary>When true, executing a query with no script throws <see cref="InvalidOperationException"/> instead of returning no rows.</summary>
        public bool ThrowOnUnscriptedQuery { get; set; }

        /// <summary>A delay applied to every <see cref="FakeDbConnection.OpenAsync(CancellationToken)"/>; zero yields once instead.</summary>
        public TimeSpan OpenDelay { get; set; }

        /// <summary>Returns the script for a query (creating it if needed), for every shard or for one shard.</summary>
        public ScriptedQuery ForQuery(string queryName, short? shardId = null)
        {
            if (string.IsNullOrEmpty(queryName))
            {
                throw new ArgumentNullException(nameof(queryName));
            }
            return _scripts.GetOrAdd((queryName, shardId), key => new ScriptedQuery(key.Item1, key.Item2));
        }

        public ScriptedQuery ForQuery(Query query, short? shardId = null)
        {
            if (query is null)
            {
                throw new ArgumentNullException(nameof(query));
            }
            return ForQuery(query.Sql, shardId);
        }

        /// <summary>How many times the provider has executed the query on the shard (null for database connections), across retries.</summary>
        public int AttemptCount(string queryName, short? shardId = null)
            => _attempts.TryGetValue((queryName, shardId), out var count) ? count : 0;

        public int AttemptCount(Query query, short? shardId = null) => AttemptCount(query.Sql, shardId);

        public void Clear()
        {
            _scripts.Clear();
            _attempts.Clear();
        }

        internal ScriptedQuery? Resolve(string? commandText, short? shardId)
        {
            if (commandText is null)
            {
                return null;
            }
            if (shardId.HasValue && _scripts.TryGetValue((commandText, shardId), out var specific))
            {
                return specific;
            }
            return _scripts.TryGetValue((commandText, null), out var general) ? general : null;
        }

        internal int NextAttempt(string commandText, short? shardId)
            => _attempts.AddOrUpdate((commandText ?? string.Empty, shardId), 1, (_, count) => count + 1);
    }
}
