using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArgentSea.TestKit;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec section data-provider-services: the provider-neutral core reaches its database platform only through the
    /// injected IDataProviderServiceFactory. The resilience strategy asks it which failures are transient, and every
    /// connection, command and parameter set comes from it. The provider-specific factories are covered in
    /// ArgentSea.Sql.Test and ArgentSea.Pg.Test (SqlProviderServicesSpecTests, PgProviderServicesSpecTests).
    /// </summary>
    public class DataProviderServicesTests
    {
        private const string Key = "MainDb";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private static readonly CancellationToken None = CancellationToken.None;

        public class Customer
        {
            [FakeMap("CustomerId")]
            public int CustomerId { get; set; }

            [FakeMap("Name")]
            public string? Name { get; set; }
        }

        [Fact]
        [Trait("Spec", "blk-7cca6cc9400246fb9caf5f8f30758eb1")]
        [Trait("Spec", "blk-d460b8baf9dd4a8f91af5f429a768baa")]
        [Trait("Spec", "blk-3e1bd826f9a248159335f08bcdebd3bf")]
        public async Task TheRetryPolicyRetriesOnlyWhatTheProviderCallsTransient()
        {
            var services = new FakeProviderServices();
            var consulted = new List<Exception>();
            // The provider's verdict: a deadlock is transient, a constraint violation is not.
            services.IsTransient = ex =>
            {
                consulted.Add(ex);
                return ex.Message == "deadlock";
            };
            var databases = FakeDatabases.Builder(services).AddDatabase(Key).ConfigureConnections(c => c.RetryCount = 3).Build();
            var deadlocked = new QueryProcedure("rd.Deadlocked");
            var violating = new QueryProcedure("rd.Violating");
            var deadlock = new FakeDbException("deadlock");
            var violation = new FakeDbException("constraint violation");
            services.Script.ForQuery(deadlocked).ThrowsOnAttempt(1, deadlock).Returns(new[] { "Id" }, new object[] { 1 });
            services.Script.ForQuery(violating).Throws(violation);

            var ids = await databases[Key].Read.ListAsync<int>(deadlocked, new ParameterCollection(), "Id", None).WaitAsync(Timeout);
            Func<Task> act = () => databases[Key].Read.ListAsync<int>(violating, new ParameterCollection(), "Id", None).WaitAsync(Timeout);

            ids.Should().ContainSingle("the deadlocked command was retried and then succeeded").Which.Should().Be(1);
            services.Script.AttemptCount(deadlocked).Should().Be(2);
            (await act.Should().ThrowAsync<FakeDbException>()).Which.Should().BeSameAs(violation, "a non-transient failure surfaces on its first occurrence");
            services.Script.AttemptCount(violating).Should().Be(1, "the constraint violation was not retried");
            consulted.Should().Contain(deadlock).And.Contain(violation, "the retry policy consults the provider for every caught DbException");
            var settings = databases.Settings(Key, FakeConnectionAccess.Read);
            settings.RetryDelayRequests.Should().ContainSingle("one back-off wait precedes the single retry")
                .Which.Attempt.Should().Be(1);
        }

        [Fact]
        [Trait("Spec", "blk-a58adcb590fd42adb06ca2df142b4f29")]
        [Trait("Spec", "blk-eb85d2a3f3fe4458a255a7be7285225e")]
        [Trait("Spec", "blk-3e1bd826f9a248159335f08bcdebd3bf")]
        public async Task TheConnectionCommandAndParametersAllComeFromTheInjectedFactory()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase(Key).Build();
            var query = new QueryProcedure("rd.CustomerGet", new[] { "@CustomerId" });
            services.Script.ForQuery(query).Returns(new[] { "CustomerId", "Name" }, new object[] { 42, "Ada" });
            var prms = new ParameterCollection().AddFakeInputParameter("@CustomerId", 42).AddFakeInputParameter("@Unrequested", "ignored");

            var customers = await databases[Key].Read.MapListAsync<Customer>(query, prms, None).WaitAsync(Timeout);

            customers.Should().ContainSingle().Which.Name.Should().Be("Ada");
            var opened = services.Recorder.ConnectionsOpened.Should().ContainSingle("one provider connection is created and opened").Which;
            opened.ConnectionString.Should().Be(databases.Settings(Key, FakeConnectionAccess.Read).ConnectionString, "NewConnection receives the Read endpoint's resolved connection string");
            var command = services.Recorder.Commands.Should().ContainSingle().Which;
            command.Command.Should().BeOfType<FakeDbCommand>("the command is whatever NewCommand returned");
            command.Command.Connection.Should().BeOfType<FakeDbConnection>("the command is bound to the connection NewConnection returned");
            command.CommandText.Should().Be("rd.CustomerGet", "NewCommand receives the query text");
            command.ConnectionString.Should().Be(opened.ConnectionString);
            command.ParameterValues.Keys.Should().BeEquivalentTo(new[] { "@CustomerId" }, "SetParameters populates the command from the query's parameter names and the supplied collection");
            command.Parameter("@CustomerId").Should().Be(42);
            command.Parameters.Should().AllBeOfType<FakeDbParameter>("the core constructs no platform-specific parameter of its own");
        }

        [Fact]
        [Trait("Spec", "blk-a58adcb590fd42adb06ca2df142b4f29")]
        public async Task PerCallParameterValuesReachSetParameters()
        {
            var services = new FakeProviderServices();
            var shardSets = FakeShardSets.Builder(services).AddShardSet("Set", 1, 2).Build();
            var query = new QueryProcedure("rd.CustomerGet", new[] { "@CustomerId", "@ShardId" });
            services.Script.ForQuery(query).Returns(new[] { "CustomerId", "Name" }, new object[] { 1, "n" });
            var prms = new ParameterCollection().AddFakeInputParameter("@CustomerId", 42).AddFakeInputParameter("@ShardId", (short)-1);

            await shardSets["Set"].ReadAll.MapListAsync<Customer>(query, prms, "@ShardId", None).WaitAsync(Timeout);

            services.Recorder.Commands.Should().HaveCount(2);
            services.Recorder.CommandFor(query, 1)!.Parameter("@ShardId").Should().Be((short)1, "the per-call shard value replaces the collection's value through SetParameters");
            services.Recorder.CommandFor(query, 2)!.Parameter("@ShardId").Should().Be((short)2);
            prms["@ShardId"].Value.Should().Be(-1, "the caller's collection is not modified");
        }

        [Fact]
        [Trait("Spec", "blk-7df5c269006f4eeb87215f230f1e442b")]
        public void ParameterNotFoundExceptionNamesTheMissingParameterInItsMessage()
        {
            var named = new ParameterNotFoundException("@CustomerId");
            var bare = new ParameterNotFoundException();
            var inner = new InvalidOperationException("inner");
            var wrapped = new ParameterNotFoundException("custom message", inner);

            named.Message.Should().Be("Expected parameter “@CustomerId”, but it was not found in the parameter list.", "the single-string constructor takes the parameter name, not a message");
            bare.Message.Should().Be("A parameter name is defined in the statement or procedure’s parameter names set, but a corresponding parameter was not provided.");
            wrapped.Message.Should().Be("custom message", "a free-form message is only possible together with an inner exception");
            wrapped.InnerException.Should().BeSameAs(inner);
            named.Should().BeAssignableTo<ApplicationException>();
            typeof(ParameterNotFoundException).IsSealed.Should().BeTrue();
            typeof(ParameterNotFoundException).GetProperty("ParameterName").Should().BeNull("the name is interpolated into the message only");
        }
    }
}
