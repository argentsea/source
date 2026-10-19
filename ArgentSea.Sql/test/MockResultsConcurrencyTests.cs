using System;
using System.Collections.Generic;
using System.Linq;
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
    public class MockResultsConcurrencyTests
    {
        private const int Contenders = 64;
        private const int Rounds = 20;

        private static ServiceProvider BuildServices()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("configurationsettings.json")
                .Build();
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddLogging();
            services.AddSqlServices(config);
            return services.BuildServiceProvider();
        }

        private static SqlDatabases BuildDatabases()
        {
            var sp = BuildServices();
            return new SqlDatabases(
                sp.GetRequiredService<IOptions<SqlDbConnectionOptions>>(),
                sp.GetRequiredService<IOptions<SqlGlobalPropertiesOptions>>(),
                NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<SqlDatabases>>());
        }

        private static SqlShardSets BuildShardSets()
        {
            var sp = BuildServices();
            return new SqlShardSets(
                sp.GetRequiredService<IOptions<SqlShardConnectionOptions>>(),
                sp.GetRequiredService<IOptions<SqlGlobalPropertiesOptions>>(),
                NSubstitute.Substitute.For<Microsoft.Extensions.Logging.ILogger<SqlShardSets>>());
        }

        // Releases every contender at once against a fresh owner and returns the instances each one observed.
        private static Dictionary<string, object>[] TouchConcurrently(Func<Dictionary<string, object>> touch)
        {
            var seen = new Dictionary<string, object>[Contenders];
            using var start = new Barrier(Contenders);
            var tasks = Enumerable.Range(0, Contenders).Select(i => Task.Factory.StartNew(() =>
            {
                start.SignalAndWait();
                seen[i] = touch();
            }, TaskCreationOptions.LongRunning)).ToArray();
            Task.WaitAll(tasks);
            return seen;
        }

        [Fact]
        public void DatabaseMockResultsIsOneInstanceUnderConcurrentFirstAccess()
        {
            for (var round = 0; round < Rounds; round++)
            {
                var connection = BuildDatabases()["MainDb"].Read;
                var seen = TouchConcurrently(() => connection.MockResults);
                seen.Should().OnlyContain(d => ReferenceEquals(d, connection.MockResults),
                    "every concurrent first caller must receive the dictionary the query methods read");
            }
        }

        [Fact]
        public void ShardSetMockResultsIsOneInstanceUnderConcurrentFirstAccess()
        {
            for (var round = 0; round < Rounds; round++)
            {
                var shardSet = BuildShardSets()["Inherit"];
                var seen = TouchConcurrently(() => shardSet.MockResults);
                seen.Should().OnlyContain(d => ReferenceEquals(d, shardSet.MockResults),
                    "every concurrent first caller must receive the dictionary the query methods read");
            }
        }

        [Fact]
        public void SyncRootIsAPlainLockObject()
        {
            BuildDatabases().SyncRoot.GetType().Should().Be(typeof(object));
            BuildShardSets().SyncRoot.GetType().Should().Be(typeof(object));
        }
    }
}
