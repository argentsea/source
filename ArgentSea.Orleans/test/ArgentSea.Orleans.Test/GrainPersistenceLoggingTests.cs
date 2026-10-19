using ArgentSea.Sql;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Orleans.Configuration;
using Orleans.Storage;
using System.Linq.Expressions;

namespace ArgentSea.Orleans.Test
{
    /// <summary>
    /// The shard grain-storage provider logs shard events, and the expression-tree events guard at the level they log at.
    /// </summary>
    public class GrainPersistenceLoggingTests
    {
        private const string ShardSetKey = "LoggingShards";
        private const short ShardId = 1;

        private sealed class CapturingLogger<T> : ILogger<T>
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

        public class ShardModel
        {
            [MapSqlShardKey("LoggingShard", "LoggingRecordId")]
            [MapToSqlInt("LoggingRecordId")]
            public ShardKey<int> RecordKey { get; set; } = ShardKey<int>.Empty;

            [MapToSqlNVarChar("LoggingDescription", -1)]
            public string? Description { get; set; }
        }

        private static QueryProcedure ReadQuery(string grainType) => new QueryProcedure($"ws.Read{grainType}");
        private static QueryProcedure ClearQuery(string grainType) => new QueryProcedure($"ws.Clear{grainType}");

        private static (ArgentSeaShardGrainPersistence<SqlShardConnectionOptions> storage, SqlShardSets shards, CapturingLogger<ArgentSeaShardGrainPersistence<SqlShardConnectionOptions>> logger) BuildShardStorage(string grainType)
        {
            var shardOptions = new SqlShardConnectionOptions
            {
                SqlShardSets = new[]
                {
                    new SqlShardConnectionOptions.SqlShardConnectionsConfiguration
                    {
                        ShardSetName = ShardSetKey,
                        DefaultShardId = ShardId,
                        DataSource = "localhost",
                        Shards = new[] { new SqlShardConnectionOptions.SqlShardConnectionConfiguration { ShardId = ShardId, InitialCatalog = "LoggingShard1" } }
                    }
                }
            };
            var shards = new SqlShardSets(Options.Create(shardOptions), Options.Create(new SqlGlobalPropertiesOptions()), Substitute.For<ILogger<SqlShardSets>>());
            var definitions = new List<OrleansShardQueryDefinitions> { new(grainType, ReadQuery(grainType), QueryResultFormat.ResultSet, new QueryProcedure($"ws.Write{grainType}"), ClearQuery(grainType)) };
            var logger = new CapturingLogger<ArgentSeaShardGrainPersistence<SqlShardConnectionOptions>>();
            var storage = new ArgentSeaShardGrainPersistence<SqlShardConnectionOptions>(
                shards,
                new OrleansShardPersistenceOptions(ShardSetKey, definitions),
                new ClusterOptions { ServiceId = "test" },
                logger);
            return (storage, shards, logger);
        }

        private static GrainId ShardGrainId(string grainType, ShardKey<int> key)
            => GrainId.Create(GrainType.Create(grainType), new IdSpan(key.ToUtf8().ToArray()));

        [Fact]
        [Trait("Spec", "blk-e62546e07b5e474cafcee04228744da9")]
        public async Task ShardClearLogsTheShardClearEventWithTheShardId()
        {
            const string grainType = "LoggingShardClear";
            var (storage, shards, logger) = BuildShardStorage(grainType);
            shards[ShardSetKey][ShardId].Write.MockResults.Add(ClearQuery(grainType).Name, null!);
            var key = new ShardKey<int>(ShardId, 77);
            var grainState = new GrainState<ShardModel>(new ShardModel { RecordKey = key });

            await storage.ClearStateAsync(grainType, ShardGrainId(grainType, key), grainState);

            var names = logger.Entries.Select(e => e.EventId.Name).ToList();
            names.Should().Contain(nameof(LoggingExtensions.TraceShardClearCmdExecuted));
            names.Should().NotContain(nameof(LoggingExtensions.TraceDbClearCmdExecuted));
            logger.Entries.Single(e => e.EventId.Name == nameof(LoggingExtensions.TraceShardClearCmdExecuted)).Message.Should().Contain($"shard {ShardId}");
        }

        [Fact]
        [Trait("Spec", "blk-48b5d1a5f3ea4a94a1531ea0a58547f0")]
        public async Task ShardActivationCacheLookupLogsShardCacheEvents()
        {
            const string grainType = "LoggingShardActivation";
            var (storage, shards, logger) = BuildShardStorage(grainType);
            shards[ShardSetKey][ShardId].Read.MockResults.Add(ReadQuery(grainType).Name, null!);
            var grainState = new GrainState<ShardModel>(new ShardModel());

            await storage.ReadStateAsync(grainType, ShardGrainId(grainType, new ShardKey<int>(ShardId, 5)), grainState);

            var names = logger.Entries.Select(e => e.EventId.Name).ToList();
            names.Should().Contain(nameof(LoggingExtensions.OrleansShardCacheMiss));
            names.Should().NotContain(nameof(LoggingExtensions.OrleansDbCacheMiss));
            names.Should().NotContain(nameof(LoggingExtensions.OrleansDbCacheHit));
        }

        [Fact]
        [Trait("Spec", "blk-8b1b32df08e148fa832435354c7a3e5b")]
        [Trait("Spec", "blk-d0cc6db246764e9eb27c2cf3f1941f24")]
        public void ActivationExpressionTreeHasItsOwnEvent()
        {
            var logger = new CapturingLogger<GrainPersistenceLoggingTests>();
            logger.CreatedExpressionTreeForActivation("Grain", Expression.Constant(1));

            logger.Entries.Should().ContainSingle();
            logger.Entries[0].Level.Should().Be(LogLevel.Debug);
            logger.Entries[0].EventId.Id.Should().Be((int)LoggingExtensions.EventIdentifier.ExpressionTreeCreation);
            logger.Entries[0].EventId.Name.Should().Be(nameof(LoggingExtensions.CreatedExpressionTreeForActivation));
            logger.Entries[0].Message.Should().Contain("activation").And.NotContain("for database");
        }

        [Fact]
        [Trait("Spec", "blk-8b1b32df08e148fa832435354c7a3e5b")]
        public void ExpressionTreeEventsGuardAtDebug()
        {
            var logger = new CapturingLogger<GrainPersistenceLoggingTests> { MinLevel = LogLevel.Information };
            Expression code = Expression.Constant(1);
            logger.CreatedExpressionTreeForDatabaseKey("Grain", code);
            logger.CreatedExpressionTreeForShardKey("Grain", code);
            logger.CreatedExpressionTreeForActivation("Grain", code);

            logger.Entries.Should().BeEmpty();
            logger.LevelsQueried.Should().HaveCount(3).And.OnlyContain(l => l == LogLevel.Debug);
        }
    }
}
