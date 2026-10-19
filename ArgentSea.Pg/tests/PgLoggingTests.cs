using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ArgentSea.Pg.Test
{
    public class PgLoggingTests
    {
        private sealed class CapturingLogger : ILogger
        {
            public LogLevel MinLevel { get; set; } = LogLevel.Trace;
            public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = new();
            public List<LogLevel> LevelsQueried { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel)
            {
                LevelsQueried.Add(logLevel);
                return logLevel >= MinLevel;
            }
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= MinLevel)
                {
                    Entries.Add((logLevel, eventId, formatter(state, exception)));
                }
            }
        }

        [Fact]
        [Trait("Spec", "blk-b2f1ecbf8fdc46818abe840ed7750aa2")]
        [Trait("Spec", "blk-1aa134af0f084bb785fd7742011220c3")]
        public void CopyExpressionTreeEventDescribesCopyRowValues()
        {
            var logger = new CapturingLogger { MinLevel = LogLevel.Debug };
            logger.CreatedExpressionTreeForCopy(typeof(string), Expression.Constant(1));

            logger.Entries.Should().ContainSingle();
            logger.Entries[0].EventId.Name.Should().Be(nameof(PgLoggingExtensions.CreatedExpressionTreeForCopy));
            logger.Entries[0].Message.Should().Contain("COPY row values").And.NotContain("input parameters");
        }

        [Fact]
        [Trait("Spec", "blk-1aa134af0f084bb785fd7742011220c3")]
        public void CopyExpressionTreeEventGuardsAtDebug()
        {
            var logger = new CapturingLogger { MinLevel = LogLevel.Information };
            logger.CreatedExpressionTreeForCopy(typeof(string), Expression.Constant(1));

            logger.Entries.Should().BeEmpty();
            logger.LevelsQueried.Should().ContainSingle().Which.Should().Be(LogLevel.Debug);
        }
    }
}
