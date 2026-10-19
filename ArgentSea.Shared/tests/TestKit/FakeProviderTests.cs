using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using ArgentSea.TestKit;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// The TestKit fake provider itself: output parameters, return values, parameter binding and the Databases collection.
    /// </summary>
    public class FakeProviderTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task DatabaseOutputParameterValueIsReturned()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            var query = new QueryProcedure("rd.GetCount");
            services.Script.ForQuery(query).WithOutput("@Count", 99);
            var parameters = new ParameterCollection { new FakeDbParameter("@Count", null, ParameterDirection.Output) };

            var result = await databases["MainDb"].Read.ReturnValueAsync<int>(query, parameters, "@Count", CancellationToken.None).WaitAsync(Timeout);

            result.Should().Be(99);
            var command = services.Recorder.Commands.Should().ContainSingle().Which;
            command.Connection.DatabaseKey.Should().Be("MainDb");
            command.Connection.Access.Should().Be(FakeConnectionAccess.Read);
            command.ShardId.Should().BeNull();
            parameters[0].Value.Should().BeNull("the caller's parameter object is not the one the provider bound");
        }

        [Fact]
        public async Task ProcedureReturnValueIsReturned()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            var query = new QueryProcedure("wt.Save");
            services.Script.ForQuery(query).WithReturnValue(3);

            var result = await databases["MainDb"].Write.ReturnValueAsync(query, new ParameterCollection(), CancellationToken.None).WaitAsync(Timeout);

            result.Should().Be(3);
            services.Recorder.Commands.Should().ContainSingle().Which.Connection.Access.Should().Be(FakeConnectionAccess.Write);
        }

        [Fact]
        public async Task UnscriptedQueryReturnsNoRowsAndAReturnValueOfZero()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();

            var list = await databases["MainDb"].Read.ListAsync<int>(new QueryProcedure("rd.Nothing"), new ParameterCollection(), "Id", CancellationToken.None).WaitAsync(Timeout);
            var returnValue = await databases["MainDb"].Write.ReturnValueAsync(new QueryProcedure("wt.Nothing"), new ParameterCollection(), CancellationToken.None).WaitAsync(Timeout);

            list.Should().BeEmpty();
            returnValue.Should().Be(0);
            services.Recorder.Commands.Should().OnlyContain(c => c.Unscripted);
        }

        [Fact]
        public async Task UnscriptedQueryThrowsWhenTheScriptIsStrict()
        {
            var services = new FakeProviderServices();
            services.Script.ThrowOnUnscriptedQuery = true;
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();

            Func<Task> act = () => databases["MainDb"].Write.RunAsync(new QueryProcedure("wt.Nothing"), new ParameterCollection(), CancellationToken.None).WaitAsync(Timeout);

            (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*wt.Nothing*");
        }

        [Fact]
        public void SetParametersCopiesOnlyTheNamedParametersAndAppliesOverrides()
        {
            var services = new FakeProviderServices();
            var connection = services.NewConnection(FakeConnectionInfo.ForDatabase("MainDb", FakeConnectionAccess.Read));
            var command = services.NewCommand("rd.Query", connection);
            var source = new ParameterCollection
            {
                new FakeDbParameter("@Keep", 1),
                new FakeDbParameter("@Drop", 2),
                new FakeDbParameter("@Override", 3)
            };

            services.SetParameters(command, new[] { "Keep", "@Override" }, source, new System.Collections.Generic.Dictionary<string, object> { ["@Override"] = 30 });

            command.Parameters.Count.Should().Be(2);
            command.Parameters["@Keep"].Value.Should().Be(1);
            command.Parameters["@Override"].Value.Should().Be(30, "a per-execution value overrides the collection's value");
            source["@Override"].Value.Should().Be(3, "the source collection is not changed");
            command.Parameters["@Keep"].Should().NotBeSameAs(source["@Keep"], "the command gets independent copies");
        }

        [Fact]
        public void SetParametersThrowsWhenANamedParameterIsMissing()
        {
            var services = new FakeProviderServices();
            var connection = services.NewConnection(FakeConnectionInfo.ForDatabase("MainDb", FakeConnectionAccess.Read));
            var command = services.NewCommand("rd.Query", connection);

            Action act = () => services.SetParameters(command, new[] { "@Missing" }, new ParameterCollection(), null);

            act.Should().Throw<ParameterNotFoundException>();
        }

        [Fact]
        public void ReaderExposesMultipleResultSetsAndConvertsCompatibleTypes()
        {
            var reader = new FakeDbDataReader(new[]
            {
                new FakeResultSet(new[] { "Id", "Name" }, new[] { new object?[] { 1, "a" }, new object?[] { 2, null } }),
                new FakeResultSet(new[] { "Total" }, new[] { new object[] { 5 } })
            });

            reader.Read().Should().BeTrue();
            reader.GetInt64(0).Should().Be(1L, "an int literal reads as a long");
            reader.GetFieldValue<short>(reader.GetOrdinal("id")).Should().Be((short)1, "column lookup is case-insensitive when no exact match exists");
            reader.GetString(1).Should().Be("a");
            reader.Read().Should().BeTrue();
            reader.IsDBNull(1).Should().BeTrue();
            reader.GetFieldValue<string>(1).Should().BeNull();
            reader.GetFieldValue<int?>(1).Should().BeNull();
            reader.Read().Should().BeFalse();
            reader.NextResult().Should().BeTrue();
            reader.Read().Should().BeTrue();
            reader.GetInt32(0).Should().Be(5);
            reader.NextResult().Should().BeFalse();
        }

        [Fact]
        public void ConnectionStringsRoundTripThroughTheInfoParser()
        {
            var shard = FakeConnectionInfo.Parse(FakeConnectionInfo.ForShard("Set", 7, FakeConnectionAccess.Write));
            shard.ShardSetName.Should().Be("Set");
            shard.ShardId.Should().Be(7);
            shard.Access.Should().Be(FakeConnectionAccess.Write);
            shard.DatabaseKey.Should().BeNull();

            var database = FakeConnectionInfo.Parse(FakeConnectionInfo.ForDatabase("MainDb", FakeConnectionAccess.Read));
            database.DatabaseKey.Should().Be("MainDb");
            database.ShardId.Should().BeNull();

            FakeConnectionInfo.Parse("anything else").ShardId.Should().BeNull();
        }
    }
}
