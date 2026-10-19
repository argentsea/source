using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ArgentSea.Test
{
    public class ResiliencePolicyTests
    {
        private class TransientDbException : DbException
        {
            public TransientDbException() : base("transient") { }
        }

        /// <summary>
        /// A connection that inherits its resilience settings the way the provider connections do.
        /// </summary>
        private class InheritingConnection : DataConnectionConfigurationBase, IDataConnection
        {
            public string ConnectionDescription => "test";
            public string GetConnectionString(Microsoft.Extensions.Logging.ILogger? logger) => string.Empty;
            public void SetAmbientConfiguration(DataConnectionConfigurationBase? globalProperties, DataConnectionConfigurationBase? shardSetProperties, DataConnectionConfigurationBase? readWriteProperties, DataConnectionConfigurationBase? shardProperties)
                => SetAmbientResilience(globalProperties, shardSetProperties, readWriteProperties, shardProperties);
            int? IDataConnection.RetryCount => this.InheritedRetryCount;
            int? IDataConnection.CircuitBreakerFailureCount => this.InheritedCircuitBreakerFailureCount;
            int? IDataConnection.CircuitBreakerTestInterval => this.InheritedCircuitBreakerTestInterval;
        }

        private class ConfigurationLevel : DataConnectionConfigurationBase { }

        [Fact]
        public async Task InheritedSettingsReachThePolicy()
        {
            var global = new ConfigurationLevel { RetryCount = 1, RetryInterval = 1, RetryLengthening = SequenceLengthening.Linear, CircuitBreakerFailureCount = 0 };
            var shardSet = new ConfigurationLevel();
            var connection = new InheritingConnection();
            connection.SetAmbientConfiguration(global, shardSet, null, null);
            var manager = new DataConnectionManager(0, TransientServices(), connection, "test", null);

            (await CountAttemptsAsync(manager)).Should().Be(2, "the global retry count of 1 is inherited");

            shardSet.RetryCount = 3;
            (await CountAttemptsAsync(manager)).Should().Be(4, "a shard-set value overrides global and the manager rebuilds its policy");

            var beforeBreakerChange = manager.ResiliencePolicy;
            global.CircuitBreakerFailureCount = 5;
            manager.ResiliencePolicy.Should().NotBeSameAs(beforeBreakerChange, "an inherited circuit-breaker change rebuilds the policy");
        }

        private static IDataProviderServiceFactory TransientServices()
        {
            var services = Substitute.For<IDataProviderServiceFactory>();
            services.GetIsErrorTransient(Arg.Any<Exception>()).Returns(true);
            return services;
        }

        private static DataConnectionManager BuildManager(SimpleDbConnection config)
        {
            var services = Substitute.For<IDataProviderServiceFactory>();
            services.GetIsErrorTransient(Arg.Any<Exception>()).Returns(true);
            return new DataConnectionManager(0, services, config, "test", null);
        }

        private static async Task<int> CountAttemptsAsync(DataConnectionManager manager)
        {
            var attempts = 0;
            Func<Task> act = () => manager.ResiliencePolicy.ExecuteAsync(_ =>
            {
                attempts++;
                throw new TransientDbException();
            }, CancellationToken.None);
            await act.Should().ThrowAsync<TransientDbException>();
            return attempts;
        }

        [Fact]
        public void PolicyIsReusedWhileSettingsAreUnchanged()
        {
            var config = new SimpleDbConnection { RetryCount = 2, RetryInterval = 1, CircuitBreakerFailureCount = 0 };
            var manager = BuildManager(config);

            var first = manager.ResiliencePolicy;
            config.RetryInterval = 5; // read on every retry, so no rebuild is needed
            config.RetryLengthening = SequenceLengthening.Linear;

            manager.ResiliencePolicy.Should().BeSameAs(first, "an unchanged policy keeps its circuit-breaker state");
        }

        [Fact]
        public void PolicyIsRebuiltWhenCircuitBreakerSettingsChange()
        {
            var config = new SimpleDbConnection { RetryCount = 2, RetryInterval = 1, CircuitBreakerFailureCount = 5, CircuitBreakerTestInterval = 100 };
            var manager = BuildManager(config);
            var first = manager.ResiliencePolicy;

            config.CircuitBreakerTestInterval = 200;
            var second = manager.ResiliencePolicy;
            second.Should().NotBeSameAs(first);

            config.CircuitBreakerFailureCount = 0;
            manager.ResiliencePolicy.Should().NotBeSameAs(second);
        }

        [Fact]
        public async Task ChangedRetryCountTakesEffect()
        {
            var config = new SimpleDbConnection { RetryCount = 1, RetryInterval = 1, RetryLengthening = SequenceLengthening.Linear, CircuitBreakerFailureCount = 0 };
            var manager = BuildManager(config);

            (await CountAttemptsAsync(manager)).Should().Be(2, "one try plus one retry");

            config.RetryCount = 3;
            (await CountAttemptsAsync(manager)).Should().Be(4, "the changed retry count applies to later executions");
        }
    }
}
