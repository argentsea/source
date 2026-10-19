using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArgentSea.Sql;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ArgentSea.Sql.Test
{
    public class ShardSetMockTests
    {
        private static SqlShardSets BuildShardSets()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("configurationsettings.json")
                .Build();
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging();
            services.AddSqlServices(config);
            var serviceProvider = services.BuildServiceProvider();
            var globalOptions = serviceProvider.GetRequiredService<IOptions<SqlGlobalPropertiesOptions>>();
            var sqlShardOptions = serviceProvider.GetRequiredService<IOptions<SqlShardConnectionOptions>>();
            var shardLogger = NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<SqlShardSets>>();
            return new SqlShardSets(sqlShardOptions, globalOptions, shardLogger);
        }

        [Fact]
        public async Task ShardSetBatchExecutesWhenNoMockDictionaryWasRequested()
        {
            var shardSet = BuildShardSets()["Inherit"];
            var batch = new ShardSetBatch();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // A pre-cancelled token is only observed after the mock short-circuit, so reaching it proves the batch was dispatched.
            Func<Task> act = () => shardSet.Write.RunAsync(batch, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>("a ShardSet batch without mocks must be executed against the shards");
        }

        [Fact]
        public async Task ShardSetBatchIsSkippedWhenMockDictionaryExists()
        {
            var shardSet = BuildShardSets()["Inherit"];
            shardSet.MockResults.Should().NotBeNull("the mock dictionary is created on first access");
            var batch = new ShardSetBatch();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Func<Task> act = () => shardSet.Write.RunAsync(batch, cts.Token);
            await act.Should().NotThrowAsync("an existing (even empty) mock dictionary suppresses ShardSet batch execution");
        }

        public class WriteModel
        {
            public int Id { get; set; }
        }

        public class WriteDetail
        {
            public int DetailId { get; set; }
        }

        [Fact]
        public async Task WriteMapReaderHasSingleReaderResultOverloads()
        {
            var shardSet = BuildShardSets()["Inherit"];
            var query = new QueryProcedure("ws.WriteMapReaderTest");
            var expected = new List<WriteModel> { new WriteModel { Id = 7 } };
            shardSet.MockResults.Add(query.Name, expected);
            var prms = new ParameterCollection();

            (await shardSet.Write.MapReaderAsync<WriteModel, WriteDetail>(query, prms, CancellationToken.None)).Should().BeSameAs(expected);
            (await shardSet.Write.MapReaderAsync<WriteModel, WriteDetail>(query, prms, "ShardId", CancellationToken.None)).Should().BeSameAs(expected);
            (await shardSet.Write.MapReaderAsync<WriteModel, WriteDetail>(query, prms, new ShardsValues(), CancellationToken.None)).Should().BeSameAs(expected);
            (await shardSet.Write.MapReaderAsync<WriteModel, WriteDetail>(query, prms, new ShardsValues(), "ShardId", CancellationToken.None)).Should().BeSameAs(expected);
        }

        [Fact]
        public async Task ShardSetQueryDoesNotCreateMockDictionaryAndSoDoesNotSuppressBatch()
        {
            var shardSet = BuildShardSets()["Inherit"];
            var query = new QueryProcedure("ws.TestProcedure");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // A query without mocks must not materialize the mock dictionary as a side effect...
            Func<Task> readAct = () => shardSet.Write.RunAsync(query, new ParameterCollection(), cts.Token);
            await readAct.Should().ThrowAsync<OperationCanceledException>();

            // ...so a subsequent batch is still executed.
            Func<Task> batchAct = () => shardSet.Write.RunAsync(new ShardSetBatch(), cts.Token);
            await batchAct.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
