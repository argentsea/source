using FluentAssertions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Xunit;

namespace ArgentSea.Test
{
    public class ParameterCollectionAndLoggingTests
    {
        private sealed class CapturingLogger : ILogger
        {
            public LogLevel MinLevel { get; set; } = LogLevel.Trace;
            public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= MinLevel;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    Entries.Add((logLevel, eventId, formatter(state, exception)));
                }
            }
        }

        [Fact]
        [Trait("Spec", "blk-2a25b55bf91d4cd2b3204e7c9cb53a0a")]
        [Trait("Spec", "blk-c652df4c1e3d4a9c9cfcb73a488bf6cc")]
        public void Insert_NonDbParameter_Throws()
        {
            var prms = new ParameterCollection();
            Action act = () => prms.Insert(0, "not a parameter");
            act.Should().Throw<ArgumentException>();
            prms.Count.Should().Be(0);
        }

        [Fact]
        public void BatchStepEnd_LogsCompletedMessage()
        {
            var logger = new CapturingLogger();
            logger.BatchStepEnd(2, "main");
            logger.Entries.Should().ContainSingle();
            logger.Entries[0].EventId.Name.Should().Be("BatchStepEnd");
            logger.Entries[0].Message.Should().StartWith("Completed batch step");
        }

        [Fact]
        public void ReadOutCacheEvents_AreDistinctFromSetOut()
        {
            var logger = new CapturingLogger();
            logger.SqlReadOutParametersCacheMiss(typeof(string));
            logger.SqlReadOutParametersCacheHit(typeof(string));
            logger.Entries[0].EventId.Name.Should().Be(nameof(LoggingExtensions.SqlReadOutParametersCacheMiss));
            logger.Entries[1].EventId.Name.Should().Be(nameof(LoggingExtensions.SqlReadOutParametersCacheHit));
            logger.Entries[0].Message.Should().Contain("reading output parameters");
        }

        [Fact]
        public void ExpressionTreeEvents_LogAtDebugLevel_AndOrdinalsUseOwnEvent()
        {
            var logger = new CapturingLogger { MinLevel = LogLevel.Debug };
            Expression code = Expression.Constant(1);
            logger.CreatedExpressionTreeForReaderOrdinals(typeof(string), code);
            logger.Entries.Should().ContainSingle();
            logger.Entries[0].EventId.Name.Should().Be(nameof(LoggingExtensions.CreatedExpressionTreeForReaderOrdinals));
            logger.Entries[0].Message.Should().Contain("ordinals");
        }

        [Fact]
        public void ExpressionTreeEvents_SkipRenderingWhenDebugDisabled()
        {
            var logger = new CapturingLogger { MinLevel = LogLevel.Information };
            logger.CreatedExpressionTreeForSetInParameters(typeof(string), Expression.Constant(1));
            logger.Entries.Should().BeEmpty();
        }

        [Fact]
        public void NullShardChildArguments_LogsOnlyWhenAnIdentityIsNull()
        {
            var logger = new CapturingLogger();
            logger.NullShardChildArguments("Child", new ShardKey<int, int>(1, 2, 3));
            logger.NullShardChildArguments("Child", new ShardKey<string, int>(1, "a", 3));
            logger.Entries.Should().BeEmpty();

            logger.NullShardChildArguments("Child", default(ShardKey<string, int>));
            logger.Entries.Should().ContainSingle();
            logger.Entries[0].EventId.Name.Should().Be(nameof(LoggingExtensions.NullShardChildArguments));
        }

        [Fact]
        public void TraceDbCmdExecuted_RespectsTraceLevel()
        {
            var logger = new CapturingLogger { MinLevel = LogLevel.Debug };
            logger.TraceDbCmdExecuted("cmd", "db", 5);
            logger.Entries.Should().BeEmpty();
        }
    }
}
