// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ArgentSea.TestKit
{
    /// <summary>One log call: its level, event id (and name), rendered message and exception.</summary>
    public sealed class LogEntry
    {
        private const string OriginalFormatKey = "{OriginalFormat}";

        internal LogEntry(LogLevel level, EventId eventId, string? message, Exception? exception, IReadOnlyList<KeyValuePair<string, object?>>? properties)
        {
            Level = level;
            EventId = eventId;
            Message = message;
            Exception = exception;
            Properties = properties ?? Array.Empty<KeyValuePair<string, object?>>();
            foreach (var property in Properties)
            {
                if (string.Equals(property.Key, OriginalFormatKey, StringComparison.Ordinal))
                {
                    Template = property.Value as string;
                }
            }
        }

        public LogLevel Level { get; }
        public EventId EventId { get; }
        public int Id => EventId.Id;
        public string? Name => EventId.Name;
        public string? Message { get; }
        public Exception? Exception { get; }

        /// <summary>The structured state of the call (the named placeholder values of a <see cref="LoggerMessage"/> event, plus its original format).</summary>
        public IReadOnlyList<KeyValuePair<string, object?>> Properties { get; }

        /// <summary>The message template the event was defined with, when the state carries one (<see cref="LoggerMessage.Define"/> events always do).</summary>
        public string? Template { get; }

        public override string ToString() => $"[{Level}] {Id}:{Name} {Message}";
    }

    /// <summary>
    /// A logger that records every call at every level, so a test can assert which events (by id and name) the library emitted.
    /// Pass it to <see cref="FakeShardSetsBuilder.WithLogger"/> / <see cref="FakeDatabasesBuilder.WithLogger"/>.
    /// </summary>
    public class RecordingLogger : ILogger
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new ConcurrentQueue<LogEntry>();

        /// <summary>The lowest level that is recorded; everything below it reports <see cref="IsEnabled"/> false.</summary>
        public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

        public IReadOnlyList<LogEntry> Entries => _entries.ToList();

        public IReadOnlyList<LogEntry> EntriesFor(LoggingExtensions.EventIdentifier eventId)
            => _entries.Where(e => e.Id == (int)eventId).ToList();

        public IReadOnlyList<LogEntry> EntriesNamed(string eventName)
            => _entries.Where(e => string.Equals(e.Name, eventName, StringComparison.Ordinal)).ToList();

        public void Clear()
        {
            while (_entries.TryDequeue(out _)) { }
        }

        public virtual IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            _entries.Enqueue(new LogEntry(logLevel, eventId, formatter?.Invoke(state, exception), exception, state as IReadOnlyList<KeyValuePair<string, object?>>));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();
            public void Dispose() { }
        }
    }

    /// <summary>A <see cref="RecordingLogger"/> typed for the category the builders expect.</summary>
    public class RecordingLogger<TCategory> : RecordingLogger, ILogger<TCategory>
    {
    }
}
