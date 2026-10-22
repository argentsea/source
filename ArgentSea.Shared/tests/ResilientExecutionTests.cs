using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec section resilient-command-execution (DataConnectionManager), driven through the TestKit fake provider: the retry
    /// policy, its delays and log events, the circuit breaker, output-value reads, mock short-circuits and shard-id binding.
    /// </summary>
    public class ResilientExecutionTests
    {
        private const string ShardSetName = "Set";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private static FakeShardSets BuildShardSets(FakeProviderServices services, int retryCount, RecordingLogger<ShardSetsBase<FakeShardSetsOptions>>? logger = null)
        {
            var builder = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 1).ConfigureConnections(c => c.RetryCount = retryCount);
            if (!(logger is null))
            {
                builder.WithLogger(logger);
            }
            return builder.Build();
        }

        #region Retry policy

        [Fact]
        [Trait("Spec", "blk-e6aa1648791e402f895375329e4173b0")]
        public async Task TransientFailureIsRetriedAndTheRetrySucceeds()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 2);
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).ThrowsTransientOnAttempt(1).Returns(new[] { "Id" }, new object[] { 7 });

            var result = await shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            result.Should().Equal(7);
            services.Script.AttemptCount(query, 1).Should().Be(2, "the first attempt failed transiently and the second succeeded");
            services.Recorder.CommandsFor(query).Should().HaveCount(2);
            services.Recorder.ConnectionsOpened.Should().HaveCount(2, "each attempt opens its own connection");
        }

        [Fact]
        [Trait("Spec", "blk-e6aa1648791e402f895375329e4173b0")]
        public async Task NonTransientFailureIsNotRetried()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 2);
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Throws(new FakeDbException("constraint violation", isTransient: false));

            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            (await act.Should().ThrowAsync<FakeDbException>()).WithMessage("constraint violation");
            services.Script.AttemptCount(query, 1).Should().Be(1, "a non-transient error is thrown on the first attempt");
        }

        [Fact]
        [Trait("Spec", "blk-e6aa1648791e402f895375329e4173b0")]
        public async Task TransientNonDbExceptionIsNotRetried()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 2);
            var query = new QueryProcedure("rd.ListIds");
            // The provider says it is transient, but the retry policy handles DbException only.
            services.Script.ForQuery(query).Throws(new TimeoutException("socket timeout"), isTransient: true);

            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            await act.Should().ThrowAsync<TimeoutException>();
            services.Script.AttemptCount(query, 1).Should().Be(1);
        }

        [Fact]
        [Trait("Spec", "blk-86dbeaf076634c90bf77166c6296107b")]
        [Trait("Spec", "blk-21df1e68973e42949dbc9b414d1f4527")]
        public async Task ExhaustedRetriesRethrowTheLastFailureUnwrapped()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 6);
            var query = new QueryProcedure("rd.ListIds");
            var failure = new FakeDbException("still down", isTransient: true);
            services.Script.ForQuery(query).Throws(failure);

            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            var thrown = (await act.Should().ThrowAsync<FakeDbException>()).Which;
            thrown.Should().BeSameAs(failure, "the provider's own exception surfaces, not a wrapper or a RetryLimitExceededException");
            services.Script.AttemptCount(query, 1).Should().Be(7, "the initial attempt plus six retries");
        }

        [Fact]
        [Trait("Spec", "blk-e6aa1648791e402f895375329e4173b0")]
        public async Task ScriptedTransientFlagIsHonoredByGetIsErrorTransient()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 1);
            var query = new QueryProcedure("rd.ListIds");
            // The exception itself says it is not transient; the script entry flags it as transient, and that flag wins.
            var exception = new FakeDbException("deadlock", isTransient: false);
            services.Script.ForQuery(query).ThrowsOnAttempt(1, exception, isTransient: true).Returns(new[] { "Id" }, new object[] { 1 });

            var result = await shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            result.Should().Equal(1);
            services.GetIsErrorTransient(exception).Should().BeTrue();
            services.Script.AttemptCount(query, 1).Should().Be(2);
        }

        [Fact]
        [Trait("Spec", "blk-e6aa1648791e402f895375329e4173b0")]
        public async Task RetryCountZeroMeansASingleAttempt()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 0);
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).ThrowsTransientOnAttempt(1).Returns(new[] { "Id" }, new object[] { 1 });

            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            await act.Should().ThrowAsync<FakeDbException>();
            services.Script.AttemptCount(query, 1).Should().Be(1);
        }

        [Fact]
        [Trait("Spec", "blk-e6aa1648791e402f895375329e4173b0")]
        [Trait("Spec", "blk-bd54bc17c2364adab664d3cfcccf7373")]
        public async Task RetryDelaysFollowTheConnectionsRetryTimespanForEachAttempt()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 6);
            // The spec's default progression: no RetryInterval or RetryLengthening override, so 250 ms × (1, 3, 5, …).
            var settings = shardSets.Settings(ShardSetName, 1, FakeConnectionAccess.Read);
            settings.RetryInterval = null;
            settings.RetryLengthening = null;
            settings.WithoutWaiting();
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).ThrowsTransientOnAttempt(1).ThrowsTransientOnAttempt(2).Returns(new[] { "Id" }, new object[] { 3 });

            var result = await shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            result.Should().Equal(new[] { 3 }, "the third attempt's result is returned and the DbException never surfaces");
            services.Script.AttemptCount(query, 1).Should().Be(3);
            settings.RetryDelayRequests.Select(r => r.Attempt).Should().Equal(new[] { 1, 2 }, "the manager asks for the delay before each retry, by retry number");
            settings.RetryDelayRequests.Select(r => r.ComputedDelay).Should().Equal(new[] { TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(750) });
            settings.RetryDelayRequests.Should().OnlyContain(r => r.AppliedDelay == TimeSpan.Zero, "the test substitutes the wait, so no time is spent sleeping");
        }

        [Fact]
        [Trait("Spec", "blk-e6aa1648791e402f895375329e4173b0")]
        public async Task RetryDelaysUseTheConfiguredIntervalAndLengthening()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 3);
            var settings = shardSets.Settings(ShardSetName, 1, FakeConnectionAccess.Read);
            settings.RetryInterval = 100;
            settings.RetryLengthening = SequenceLengthening.Squaring;
            settings.WithoutWaiting();
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Throws(new FakeDbException("down", isTransient: true));

            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            await act.Should().ThrowAsync<FakeDbException>();
            settings.RetryDelayRequests.Select(r => r.ComputedDelay.TotalMilliseconds).Should().Equal(100, 200, 400);
            settings.RetryDelayRequests.Should().OnlyContain(r => r.AppliedDelay == TimeSpan.Zero, "the test substitutes the wait, so no time is spent sleeping");
        }

        [Fact]
        [Trait("Spec", "blk-e6aa1648791e402f895375329e4173b0")]
        public async Task EachRetryLogsTheConnectRetryEventWithTheAttemptNumber()
        {
            var services = new FakeProviderServices();
            var logger = new RecordingLogger<ShardSetsBase<FakeShardSetsOptions>>();
            var shardSets = BuildShardSets(services, retryCount: 2, logger);
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Throws(new FakeDbException("down", isTransient: true));

            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            await act.Should().ThrowAsync<FakeDbException>();
            var retries = logger.EntriesFor(LoggingExtensions.EventIdentifier.ConnectRetry);
            retries.Should().HaveCount(2, "one event per retry, none for the initial attempt");
            retries.Should().OnlyContain(e => e.Name == nameof(LoggingExtensions.RetryingDbConnection) && e.Level == LogLevel.Warning);
            retries[0].Message.Should().Contain("attempt number 1");
            retries[1].Message.Should().Contain("attempt number 2");
            retries.Should().OnlyContain(e => e.Message!.Contains("shard number 1"), "the event names the connection");
            logger.EntriesFor(LoggingExtensions.EventIdentifier.CircuitBreaker).Should().BeEmpty("the breaker is disabled on this connection");
        }

        #endregion

        #region Circuit breaker

        [Fact]
        [Trait("Spec", "blk-cb28eb3e0dae430dad77a57294d6b7c0")]
        public async Task CircuitBreakerOpensAfterTheConfiguredFailureCountAndFailsFast()
        {
            var services = new FakeProviderServices();
            var shardSets = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 1)
                .ConfigureConnections(c => { c.RetryCount = 0; c.CircuitBreakerFailureCount = 2; c.CircuitBreakerTestInterval = 60_000; })
                .Build();
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Throws(new FakeDbException("down", isTransient: false));
            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            await act.Should().ThrowAsync<FakeDbException>();
            await act.Should().ThrowAsync<FakeDbException>();
            services.Script.AttemptCount(query, 1).Should().Be(2);

            await act.Should().ThrowAsync<BrokenCircuitException>("the second consecutive failure opened the circuit");
            await act.Should().ThrowAsync<BrokenCircuitException>();
            services.Script.AttemptCount(query, 1).Should().Be(2, "no command is attempted while the circuit is open");
        }

        [Fact]
        [Trait("Spec", "blk-cb28eb3e0dae430dad77a57294d6b7c0")]
        [Trait("Spec", "blk-7ec3c28d2a3d479d95b283d8a4a61e0e")]
        public async Task CircuitBreakerWrapsTheRetryPolicySoExhaustedRetriesCountAsOneFailure()
        {
            var services = new FakeProviderServices();
            var shardSets = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 1)
                .ConfigureConnections(c => { c.RetryCount = 1; c.CircuitBreakerFailureCount = 2; c.CircuitBreakerTestInterval = 60_000; })
                .Build();
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Throws(new FakeDbException("down", isTransient: true));
            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            await act.Should().ThrowAsync<FakeDbException>();
            services.Script.AttemptCount(query, 1).Should().Be(2, "the inner retry policy retried once before the breaker saw a failure");
            await act.Should().ThrowAsync<FakeDbException>();
            services.Script.AttemptCount(query, 1).Should().Be(4);

            await act.Should().ThrowAsync<BrokenCircuitException>("two exhausted executions opened the breaker");
            services.Script.AttemptCount(query, 1).Should().Be(4, "an open circuit short-circuits the retry policy as well");
        }

        [Fact]
        [Trait("Spec", "blk-cb28eb3e0dae430dad77a57294d6b7c0")]
        public async Task CircuitBreakerCountsANonDbExceptionTheRetryPolicyDoesNotHandle()
        {
            var services = new FakeProviderServices();
            var shardSets = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 1)
                .ConfigureConnections(c => { c.RetryCount = 3; c.CircuitBreakerFailureCount = 1; c.CircuitBreakerTestInterval = 60_000; })
                .Build();
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Throws(new InvalidOperationException("not a DbException"));
            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            await act.Should().ThrowAsync<InvalidOperationException>();
            await act.Should().ThrowAsync<BrokenCircuitException>();
            services.Script.AttemptCount(query, 1).Should().Be(1);
        }

        [Fact]
        [Trait("Spec", "blk-2fb59db576074650b58c7fcd2a6b1ae4")]
        public async Task TwentyConsecutiveFailuresOpenTheCircuitForTheTestInterval()
        {
            var services = new FakeProviderServices();
            var shardSets = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 1)
                .ConfigureConnections(c => { c.RetryCount = 0; c.CircuitBreakerFailureCount = 20; c.CircuitBreakerTestInterval = 5000; })
                .Build();
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Throws(new FakeDbException("down", isTransient: false));
            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            for (var i = 0; i < 20; i++)
            {
                await act.Should().ThrowAsync<FakeDbException>();
            }
            services.Script.AttemptCount(query, 1).Should().Be(20);

            await act.Should().ThrowAsync<BrokenCircuitException>("the 21st execution within the test interval fails immediately");
            services.Script.AttemptCount(query, 1).Should().Be(20, "no command reaches the database for the rest of the interval");
        }

        [Fact]
        [Trait("Spec", "blk-7ec3c28d2a3d479d95b283d8a4a61e0e")]
        [Trait("Spec", "blk-cb28eb3e0dae430dad77a57294d6b7c0")]
        public async Task CircuitBreakerFailureCountBelowOneLeavesOnlyTheRetryPolicy()
        {
            var services = new FakeProviderServices();
            var shardSets = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 1)
                .ConfigureConnections(c => { c.RetryCount = 1; c.CircuitBreakerFailureCount = 0; c.CircuitBreakerTestInterval = 60_000; })
                .Build();
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Throws(new FakeDbException("down", isTransient: true));
            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            for (var i = 0; i < 5; i++)
            {
                await act.Should().ThrowAsync<FakeDbException>("without a breaker every execution reaches the provider");
            }
            services.Script.AttemptCount(query, 1).Should().Be(10, "five executions, each with one retry");
        }

        [Fact]
        [Trait("Spec", "blk-cb28eb3e0dae430dad77a57294d6b7c0")]
        public async Task UnsetCircuitBreakerFailureCountUsesTheDefaultThresholdOfTwenty()
        {
            // The configuration leaves the breaker unset: the manager's default (20 failures, 5 s) applies rather than no breaker.
            var services = new FakeProviderServices();
            var shardSets = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 1)
                .ConfigureConnections(c => { c.RetryCount = 0; c.CircuitBreakerFailureCount = null; c.CircuitBreakerTestInterval = null; })
                .Build();
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Throws(new FakeDbException("down", isTransient: false));
            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            for (var i = 0; i < 20; i++)
            {
                await act.Should().ThrowAsync<FakeDbException>();
            }
            await act.Should().ThrowAsync<BrokenCircuitException>();
            services.Script.AttemptCount(query, 1).Should().Be(20);
        }

        [Fact]
        [Trait("Spec", "blk-cb28eb3e0dae430dad77a57294d6b7c0")]
        public async Task CircuitBreakerTransitionsAreLoggedWithTheCircuitBreakerEventId()
        {
            var services = new FakeProviderServices();
            var logger = new RecordingLogger<ShardSetsBase<FakeShardSetsOptions>>();
            var shardSets = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 1)
                .ConfigureConnections(c => { c.RetryCount = 0; c.CircuitBreakerFailureCount = 1; c.CircuitBreakerTestInterval = 200; })
                .WithLogger(logger)
                .Build();
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).ThrowsOnAttempt(1, new FakeDbException("down", isTransient: false)).Returns(new[] { "Id" }, new object[] { 1 });
            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            await act.Should().ThrowAsync<FakeDbException>();
            var breaking = logger.EntriesFor(LoggingExtensions.EventIdentifier.CircuitBreaker);
            breaking.Should().ContainSingle("opening the circuit is logged once").Which.Name.Should().Be(nameof(LoggingExtensions.CiruitBreakingDbConnection));
            breaking[0].Level.Should().Be(LogLevel.Critical);
            breaking[0].Message.Should().Contain("shard number 1");

            // Fail-fast while open is proven by the 60 s-interval tests; here the interval is short so the half-open probe can run.
            await Task.Delay(400);
            await act.Should().NotThrowAsync("after the test interval the half-open probe runs and succeeds");
            services.Script.AttemptCount(query, 1).Should().Be(2);
            logger.EntriesFor(LoggingExtensions.EventIdentifier.CircuitBreaker).Select(e => e.Name).Should().Equal(
                nameof(LoggingExtensions.CiruitBreakingDbConnection),
                nameof(LoggingExtensions.CiruitBrokenDbConnectionTest),
                nameof(LoggingExtensions.CiruitBrokenDbConnectionRestored));
        }

        #endregion

        #region Output values

        [Fact]
        [Trait("Spec", "blk-315b7adee51448599ba794ae94ec1c1a")]
        [Trait("Spec", "blk-0eb3bc0bd61b4c24a1c0ee6756ed97cc")]
        [Trait("Spec", "blk-ddd6e8fcd68a412c9a68697034acd118")]
        public async Task NullOutputParameterThrowsUnexpectedSqlResultForANonNullableValueType()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            var query = new QueryProcedure("GetCustomerBalance");
            // The procedure runs but never sets the output parameter, so it stays null.
            var parameters = new ParameterCollection { new FakeDbParameter("@Balance", null, ParameterDirection.Output) };

            Func<Task> act = () => databases["MainDb"].Read.ReturnValueAsync<decimal>(query, parameters, "@Balance", CancellationToken.None).WaitAsync(Timeout);

            var thrown = (await act.Should().ThrowAsync<UnexpectedSqlResultException>()).Which;
            thrown.Message.Should().Be("Database query GetCustomerBalance expected to output the value of @Balance, but the database value was null.");
            thrown.Should().BeAssignableTo<ApplicationException>();
            services.Recorder.CommandsFor(query).Should().ContainSingle("the exception describes a completed query, not a failed one");
        }

        [Fact]
        [Trait("Spec", "blk-315b7adee51448599ba794ae94ec1c1a")]
        [Trait("Spec", "blk-0eb3bc0bd61b4c24a1c0ee6756ed97cc")]
        public async Task NullOutputParameterReturnsNullForANullableOrReferenceType()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            var query = new QueryProcedure("GetCustomerBalance");
            var parameters = new ParameterCollection
            {
                new FakeDbParameter("@Balance", DBNull.Value, ParameterDirection.Output),
                new FakeDbParameter("@Note", null, ParameterDirection.Output)
            };

            var balance = await databases["MainDb"].Read.ReturnValueAsync<decimal?>(query, parameters, "@Balance", CancellationToken.None).WaitAsync(Timeout);
            var note = await databases["MainDb"].Read.ReturnValueAsync<string>(query, parameters, "@Note", CancellationToken.None).WaitAsync(Timeout);

            balance.Should().BeNull();
            note.Should().BeNull();
        }

        [Fact]
        [Trait("Spec", "blk-ddd6e8fcd68a412c9a68697034acd118")]
        public void UnexpectedSqlResultExceptionHasTheDocumentedDefaultMessage()
        {
            new UnexpectedSqlResultException().Message.Should().Be("The query results are missing an expected value.");
            new UnexpectedSqlResultException("m", new Exception("inner")).InnerException!.Message.Should().Be("inner");
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task ReturnValueFallsBackToTheFirstRowColumnWhenNoOutputParameterMatches()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            var query = new QueryProcedure("rd.GetTotal");
            services.Script.ForQuery(query).Returns(new[] { "Total" }, new object[] { 150 }, new object[] { 999 });

            var total = await databases["MainDb"].Read.ReturnValueAsync<int>(query, new ParameterCollection(), "Total", CancellationToken.None).WaitAsync(Timeout);

            total.Should().Be(150, "the value comes from the first row");
            services.Recorder.Commands.Should().ContainSingle().Which.Behavior.Should().Be(CommandBehavior.SingleRow);
        }

        #endregion

        #region Mock results

        [Fact]
        [Trait("Spec", "blk-c84aa7f29fd34ef8a641dfce93ae97b8")]
        [Trait("Spec", "blk-6e408070d01942e6b118c015e24795a8")]
        public async Task EmptyStringMockKeyReturnsTheBatchResultWithoutExecuting()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            var connection = databases["MainDb"].Write;
            connection.MockResults[string.Empty] = "mock-summary";
            var batch = new DatabaseBatch<string>().Add(new QueryProcedure("wt.Step1"));

            var result = await connection.RunAsync(batch, CancellationToken.None).WaitAsync(Timeout);

            result.Should().Be("mock-summary");
            services.Recorder.Commands.Should().BeEmpty();
            services.Recorder.ConnectionsOpened.Should().BeEmpty();
            services.Recorder.TransactionEvents.Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-c84aa7f29fd34ef8a641dfce93ae97b8")]
        public async Task ShardConnectionBatchIsAlsoMockedByTheEmptyStringKey()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 0);
            var connection = shardSets[ShardSetName][1].Write;
            connection.MockResults[string.Empty] = 42;

            var result = await connection.RunAsync(new ShardBatch<int>().Add(new QueryProcedure("wt.Step1")), CancellationToken.None).WaitAsync(Timeout);

            result.Should().Be(42);
            services.Recorder.Commands.Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task QueryNameMockShortCircuitsEveryQueryDrivenMethod()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 0);
            var connection = shardSets[ShardSetName][1].Read;
            var model = new Model { Name = "mocked" };
            connection.MockResults["rd.Return"] = 5;
            connection.MockResults["rd.List"] = new List<Model> { model };
            connection.MockResults["rd.Query"] = model;
            connection.MockResults["rd.Run"] = new object();
            var parameters = new ParameterCollection();

            (await connection.ReturnValueAsync(new QueryProcedure("rd.Return"), parameters, CancellationToken.None)).Should().Be(5);
            (await connection.MapListAsync<Model>(new QueryProcedure("rd.List"), parameters, CancellationToken.None)).Should().ContainSingle().Which.Should().BeSameAs(model);
            (await connection.QueryAsync<Model>(new QueryProcedure("rd.Query"), parameters, (_, __, ___, ____, _____, ______, _______, ________) => throw new Exception("handler must not run"), false, CancellationToken.None)).Should().BeSameAs(model);
            await connection.RunAsync(new QueryProcedure("rd.Run"), parameters, CancellationToken.None);

            services.Recorder.Commands.Should().BeEmpty("a matching mock entry returns without opening a connection");
            services.Recorder.ConnectionsOpened.Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task MockedExecutionsDoNotCountAgainstTheCircuitBreaker()
        {
            var services = new FakeProviderServices();
            var shardSets = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 1)
                .ConfigureConnections(c => { c.RetryCount = 0; c.CircuitBreakerFailureCount = 1; c.CircuitBreakerTestInterval = 60_000; })
                .Build();
            var connection = shardSets[ShardSetName][1].Read;
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Throws(new FakeDbException("down"));
            Func<Task> act = () => connection.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);
            await act.Should().ThrowAsync<FakeDbException>();
            await act.Should().ThrowAsync<BrokenCircuitException>();

            connection.MockResults["rd.ListIds"] = new List<int> { 9 };

            await act.Should().NotThrowAsync();
            (await connection.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None)).Should().Equal(9);
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task MocksAreKeyedByQueryNameSoOtherQueriesStillExecute()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 0);
            var connection = shardSets[ShardSetName][1].Read;
            connection.MockResults["rd.Other"] = new List<int> { 1 };
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Returns(new[] { "Id" }, new object[] { 2 });

            var result = await connection.ListAsync<int>(query, new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);

            result.Should().Equal(2);
            services.Recorder.CommandsFor(query).Should().ContainSingle();
        }

        #endregion

        #region Shard id binding and execution plumbing

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task ShardIdIsBoundToTheNamedParameterWithoutTouchingTheCallersCollection()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 0);
            var query = new QueryProcedure("rd.ListIds");
            var parameters = new ParameterCollection { new FakeDbParameter("@ShardId", (short)-1), new FakeDbParameter("@Other", "x") };

            await shardSets[ShardSetName][1].Read.ListAsync<int>(query, parameters, "Id", "@ShardId", CancellationToken.None).WaitAsync(Timeout);

            var command = services.Recorder.CommandFor(query, 1);
            command!.Parameter("@ShardId").Should().Be((short)1);
            command.Parameter("@Other").Should().Be("x");
            parameters["@ShardId"].Value.Should().Be((short)-1, "the shard id is applied to the command copy only");
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task ACallerSuppliedValueForTheShardParameterTakesPrecedenceOverTheShardId()
        {
            var services = new FakeProviderServices();
            var shardSets = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 1, 2).Build();
            var query = new QueryProcedure("rd.ListIds");
            var parameters = new ParameterCollection { new FakeDbParameter("@ShardId", (short)-1) };
            var shards = new ShardsValues().Add(1, "@ShardId", (short)99).Add(2);

            await shardSets[ShardSetName].ReadAll.ListAsync<int>(query, "Id", parameters, shards, "@ShardId", CancellationToken.None).WaitAsync(Timeout);

            services.Recorder.CommandFor(query, 1)!.Parameter("@ShardId").Should().Be((short)99, "an explicit per-execution value wins");
            services.Recorder.CommandFor(query, 2)!.Parameter("@ShardId").Should().Be((short)2, "a shard without an explicit value gets its own id");
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task WithoutAShardParameterNameTheParameterValuesAreLeftUntouched()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 0);
            var query = new QueryProcedure("rd.ListIds");
            var parameters = new ParameterCollection { new FakeDbParameter("@ShardId", (short)-1) };

            await shardSets[ShardSetName][1].Read.ListAsync<int>(query, parameters, "Id", CancellationToken.None).WaitAsync(Timeout);

            services.Recorder.CommandFor(query, 1)!.Parameter("@ShardId").Should().Be((short)-1, "no ordinal was identified, so nothing is bound");
        }

        [Fact]
        [Trait("Spec", "blk-b4ac513b6a1b46ca9bf2b0970974124f")]
        public async Task QueryHandlerReceivesTheCommandTextShardIdArgumentReaderAndParameters()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 0);
            var query = new InlineQuery("rd.Find", "SELECT Name FROM T");
            services.Script.ForQuery(query).Returns(new[] { "Name" }, new object[] { "found" }).WithOutput("@Count", 3);
            var parameters = new ParameterCollection { new FakeDbParameter("@Count", null, ParameterDirection.Output) };
            (string sprocName, short shardId, string arg, string connection, bool hadLogger, int count, string name) captured = default;

            var result = await shardSets[ShardSetName][1].Read.QueryAsync<string, Model>(query, parameters, (instance, shardId, sprocName, arg, rdr, prms, connectionDescription, logger) =>
            {
                rdr.Read();
                captured = (sprocName, shardId, arg, connectionDescription, !(logger is null), (int)prms["@Count"].Value!, rdr.GetString(0));
                return new Model { Name = rdr.GetString(0) };
            }, false, "the-argument", CancellationToken.None).WaitAsync(Timeout);

            result!.Name.Should().Be("found");
            captured.sprocName.Should().Be("SELECT Name FROM T", "the handler receives the executed command text (query.Sql), not the query name");
            captured.shardId.Should().Be(1);
            captured.arg.Should().Be("the-argument");
            captured.connection.Should().Contain("shard number 1");
            captured.hadLogger.Should().BeTrue();
            captured.count.Should().Be(3, "the handler sees the command's own parameters, with their output values");
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task CancellationDuringExecutionIsObservedByTheProvider()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 2);
            var query = new QueryProcedure("rd.Slow");
            using var cts = new CancellationTokenSource();
            services.Script.ForQuery(query).OnExecuting((_, __) => { cts.Cancel(); return Task.CompletedTask; }).WaitsUntilCancelled();

            Func<Task> act = () => shardSets[ShardSetName][1].Read.RunAsync(query, new ParameterCollection(), cts.Token).WaitAsync(Timeout);

            await act.Should().ThrowAsync<OperationCanceledException>();
            var command = services.Recorder.Commands.Should().ContainSingle("cancellation is not retried").Which;
            command.CancellationToken.IsCancellationRequested.Should().BeTrue();
            command.Kind.Should().Be(FakeExecutionKind.NonQuery);
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task AnAlreadyCancelledTokenIsRejectedBeforeAnyConnectionOpens()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 0);
            var query = new QueryProcedure("rd.ListIds");
            var cancelled = new CancellationToken(canceled: true);

            Func<Task> act = () => shardSets[ShardSetName][1].Read.ListAsync<int>(query, new ParameterCollection(), "Id", cancelled);

            await act.Should().ThrowAsync<OperationCanceledException>();
            services.Recorder.ConnectionsOpened.Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task ReturnValueAddsAReturnValueParameterWhenTheCallerSuppliesNone()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, retryCount: 0);
            var query = new QueryProcedure("wt.Save");
            services.Script.ForQuery(query).WithReturnValue(3);
            var parameters = new ParameterCollection { new FakeDbParameter("@Id", 1) };

            var result = await shardSets[ShardSetName][1].Write.ReturnValueAsync(query, parameters, CancellationToken.None).WaitAsync(Timeout);

            result.Should().Be(3);
            var command = services.Recorder.CommandFor(query, 1);
            command!.Parameters.Should().ContainSingle(p => p.Direction == ParameterDirection.ReturnValue, "the manager added the return-value parameter to the command");
            parameters.Count.Should().Be(1, "the caller's collection is not changed");
        }

        #endregion

        private sealed class InlineQuery : Query
        {
            public InlineQuery(string name, string sql) : base(sql, name, null) { }
            public override CommandType Type => CommandType.Text;
        }

        public class Model
        {
            public string? Name { get; set; }
        }
    }
}
