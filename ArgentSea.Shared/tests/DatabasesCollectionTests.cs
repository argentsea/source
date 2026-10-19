using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using static ArgentSea.Test.TestModels;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec section databases-collection: the DatabasesBase collection surface, the Database read/write pair, and the full
    /// DataConnection data-method matrix against a non-sharded connection, driven through the TestKit fake provider.
    /// </summary>
    public class DatabasesCollectionTests
    {
        private const string Key = "MainDb";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private static readonly CancellationToken None = CancellationToken.None;

        private static FakeDatabases Build(FakeProviderServices services, params string[] keys)
        {
            var builder = FakeDatabases.Builder(services);
            foreach (var key in keys.Length == 0 ? new[] { Key } : keys)
            {
                builder.AddDatabase(key);
            }
            return builder.Build();
        }

        #region Collection

        [Fact]
        [Trait("Spec", "blk-555c07ab1e724e48beeef741f9ab9996")]
        public void CollectionExposesConfiguredDatabasesByExactKey()
        {
            var databases = Build(new FakeProviderServices(), "MainDb", "Audit");

            databases.Count.Should().Be(2);
            databases["MainDb"].Should().NotBeNull();
            databases["MainDb"].Should().BeSameAs(databases["MainDb"], "the same entry is returned on every lookup");
            databases["Audit"].Should().NotBeSameAs(databases["MainDb"]);
            databases.Invoking(d => d["maindb"]).Should().Throw<KeyNotFoundException>("lookup is by exact key");
            databases.Invoking(d => d["Missing"]).Should().Throw<KeyNotFoundException>();
        }

        [Fact]
        [Trait("Spec", "blk-555c07ab1e724e48beeef741f9ab9996")]
        public void CollectionIsEmptyWhenNoConfigurationIsSupplied()
        {
            var databases = new FakeDatabases(new FakeDatabasesOptions { Databases = null! }, new FakeProviderServices(), null, NullLogger<DatabasesBase<FakeDatabasesOptions>>.Instance);

            databases.Count.Should().Be(0);
            databases.Cast<object>().Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-555c07ab1e724e48beeef741f9ab9996")]
        public void CopyToAndEnumerationExposeTheSameEntries()
        {
            var databases = Build(new FakeProviderServices(), "MainDb", "Audit");
            var copied = new DatabasesBase<FakeDatabasesOptions>.Database[3];

            databases.CopyTo(copied, 1);
            var enumerated = databases.Cast<KeyValuePair<string, DatabasesBase<FakeDatabasesOptions>.Database>>().ToList();

            copied[0].Should().BeNull();
            copied.Skip(1).Should().BeEquivalentTo(new[] { databases["MainDb"], databases["Audit"] });
            enumerated.Select(e => e.Key).Should().BeEquivalentTo(new[] { "MainDb", "Audit" });
            enumerated.Should().OnlyContain(e => ReferenceEquals(e.Value, databases[e.Key]));
            databases.IsSynchronized.Should().BeTrue();
            databases.SyncRoot.Should().NotBeNull().And.BeSameAs(databases.SyncRoot);
        }

        [Fact]
        [Trait("Spec", "blk-02ec2f3044644aea934d6dd236776ba8")]
        public void MembershipCannotChangeAfterConstruction()
        {
            var services = new FakeProviderServices();
            var builder = FakeDatabases.Builder(services).AddDatabase("MainDb");
            var databases = builder.Build();
            builder.AddDatabase("Late");

            databases.Count.Should().Be(1, "entries added to the configuration later are not picked up");
            databases.Invoking(d => d["Late"]).Should().Throw<KeyNotFoundException>();
            typeof(DatabasesBase<FakeDatabasesOptions>).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(m => m.Name).Should().NotContain(new[] { "Add", "Remove", "Clear", "Insert" }, "the public surface has no mutators");
            databases.Should().BeAssignableTo<ICollection>().And.NotBeAssignableTo<IDictionary>();
        }

        #endregion

        #region Database pair

        [Theory]
        [Trait("Spec", "blk-5d4f7991c3214198883eb97f8b715002")]
        [Trait("Spec", "blk-87933824a7d247b9b2a4c470fa4a9616")]
        [Trait("Spec", "blk-643a8cdaf4e34817b6037c584c960aa0")]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void DatabaseUsesTheOnlySuppliedEndpointForBothReadAndWrite(bool hasRead, bool hasWrite)
        {
            var services = new FakeProviderServices();
            var parent = Build(services);
            var endpoint = new FakeConnectionSettings { ConnectionString = "Server=W", ConnectionDescription = "W" };
            var configuration = new FakeDatabaseConfiguration { DatabaseKey = "Solo", ReadConnection = hasRead ? endpoint : null, WriteConnection = hasWrite ? endpoint : null };

            var database = new DatabasesBase<FakeDatabasesOptions>.Database(parent, configuration);

            database.Read.ConnectionString.Should().Be("Server=W");
            database.Write.ConnectionString.Should().Be("Server=W");
        }

        [Fact]
        [Trait("Spec", "blk-5d4f7991c3214198883eb97f8b715002")]
        public async Task ADatabaseConfiguredWithOnlyAWriteEndpointIsUsableThroughTheCollection()
        {
            var services = new FakeProviderServices();
            var options = new FakeDatabasesOptions
            {
                Databases = new[] { new FakeDatabaseConfiguration { DatabaseKey = Key, WriteConnection = new FakeConnectionSettings { ConnectionString = FakeConnectionInfo.ForDatabase(Key, FakeConnectionAccess.Write), ConnectionDescription = "w" } } }
            };
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Returns(new[] { "Id" }, new object[] { 1 });

            var databases = new FakeDatabases(options, services, null, NullLogger<DatabasesBase<FakeDatabasesOptions>>.Instance);
            var result = await databases[Key].Read.ListAsync<int>(query, new ParameterCollection(), "Id", None).WaitAsync(Timeout);

            result.Should().Equal(1);
            services.Recorder.Commands.Should().ContainSingle().Which.Connection.Access.Should().Be(FakeConnectionAccess.Write);
        }

        [Fact]
        [Trait("Spec", "blk-643a8cdaf4e34817b6037c584c960aa0")]
        [Trait("Spec", "blk-b17909a579d94fa5a6a00a8869eb1bd1")]
        public void DatabaseUsesEachEndpointWhenBothAreSuppliedAndThePairIsGetOnly()
        {
            var database = Build(new FakeProviderServices())[Key];

            database.Read.ConnectionString.Should().Be(FakeConnectionInfo.ForDatabase(Key, FakeConnectionAccess.Read));
            database.Write.ConnectionString.Should().Be(FakeConnectionInfo.ForDatabase(Key, FakeConnectionAccess.Write));
            var properties = typeof(DatabasesBase<FakeDatabasesOptions>.Database).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            properties.Select(p => p.Name).Should().BeEquivalentTo(new[] { "Read", "Write" });
            properties.Should().OnlyContain(p => p.GetSetMethod() == null && p.PropertyType == typeof(DatabasesBase<FakeDatabasesOptions>.DataConnection));
        }

        [Fact]
        [Trait("Spec", "blk-6a78adcd4f6c4715b773c2681c19ec04")]
        public void DataConnectionCannotBeConstructedFromOutsideTheLibrary()
        {
            var type = typeof(DatabasesBase<FakeDatabasesOptions>.DataConnection);

            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty();
            type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Should().ContainSingle().Which.IsAssembly.Should().BeTrue("the constructor is internal");
        }

        #endregion

        #region Mock results

        [Fact]
        [Trait("Spec", "blk-5bdb3c9214b14443a731e190aa989a0d")]
        public async Task MockResultsIsGetOnlyAlwaysTheSameInstanceAndReadByTheQueryMethods()
        {
            var services = new FakeProviderServices();
            var connection = Build(services)[Key].Read;
            typeof(DatabasesBase<FakeDatabasesOptions>.DataConnection).GetProperty(nameof(connection.MockResults))!.GetSetMethod().Should().BeNull();
            var gate = new ManualResetEventSlim(false);
            var readers = Enumerable.Range(0, 16).Select(_ => Task.Run(() => { gate.Wait(); return connection.MockResults; })).ToArray();
            gate.Set();
            var dictionaries = await Task.WhenAll(readers);

            dictionaries.Distinct().Should().ContainSingle("every concurrent first access receives the one published dictionary");
            dictionaries[0]["GetTotal"] = 42;
            (await connection.ReturnValueAsync<int>(new QueryProcedure("GetTotal"), new ParameterCollection(), "Total", None)).Should().Be(42, "a mock registered through any returned dictionary is honored");
        }

        [Fact]
        [Trait("Spec", "blk-e36445f2f54641b095bf3b5e511c120d")]
        [Trait("Spec", "blk-552f07f6cbe04b6fb9bd4ed842c9e8dd")]
        public async Task MockResultsForTheQueryNameShortCircuitReturnValueAndList()
        {
            var services = new FakeProviderServices();
            var connection = Build(services)[Key].Read;
            connection.MockResults["GetTotal"] = 42;
            connection.MockResults["GetNames"] = new List<string> { "mocked" };

            var total = await connection.ReturnValueAsync<int>(new QueryProcedure("GetTotal"), new ParameterCollection(), "Total", None).WaitAsync(Timeout);
            var names = await connection.ListAsync<string>(new QueryProcedure("GetNames"), new ParameterCollection(), "Name", None).WaitAsync(Timeout);

            total.Should().Be(42);
            names.Should().Equal("mocked");
            services.Recorder.Commands.Should().BeEmpty();
            services.Recorder.ConnectionsOpened.Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-e17384f08ede4e69aa69fca3b733e2a7")]
        public async Task AMockResultOfTheWrongRuntimeTypeFailsTheCast()
        {
            var connection = Build(new FakeProviderServices())[Key].Read;
            connection.MockResults["GetTotal"] = "forty-two";

            Func<Task> act = () => connection.ReturnValueAsync<int>(new QueryProcedure("GetTotal"), new ParameterCollection(), "Total", None);

            await act.Should().ThrowAsync<InvalidCastException>();
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task MockResultsShortCircuitTheMappingQueryAndRunFamilies()
        {
            var services = new FakeProviderServices();
            var connection = Build(services)[Key].Read;
            var customer = new Customer { Id = 1 };
            connection.MockResults["rd.Reader"] = customer;
            connection.MockResults["rd.Output"] = customer;
            connection.MockResults["rd.List"] = new List<Customer> { customer };
            connection.MockResults["rd.Query"] = customer;
            connection.MockResults["rd.Run"] = null!;
            connection.MockResults["rd.Int"] = 5;
            var parameters = new ParameterCollection();

            (await connection.MapReaderAsync<Customer>(new QueryProcedure("rd.Reader"), parameters, None)).Should().BeSameAs(customer);
            (await connection.MapOutputAsync<Customer>(new QueryProcedure("rd.Output"), parameters, None)).Should().BeSameAs(customer);
            (await connection.MapListAsync<Customer>(new QueryProcedure("rd.List"), parameters, None)).Should().ContainSingle();
            (await connection.QueryAsync<Customer>(new QueryProcedure("rd.Query"), parameters, (i, s, n, a, r, p, d, l) => throw new Exception("not run"), false, None)).Should().BeSameAs(customer);
            (await connection.ReturnValueAsync(new QueryProcedure("rd.Int"), parameters, None)).Should().Be(5);
            await connection.RunAsync(new QueryProcedure("rd.Run"), parameters, None);
            services.Recorder.Commands.Should().BeEmpty();
        }

        #endregion

        #region Data methods

        [Fact]
        [Trait("Spec", "blk-b9b01b19da18481dadf48870b9bdfead")]
        public async Task UntypedReturnValueAsyncReturnsTheProcedureReturnValue()
        {
            var services = new FakeProviderServices();
            var connection = Build(services)[Key].Write;
            var query = new QueryProcedure("wt.Save");
            services.Script.ForQuery(query).WithReturnValue(3);

            var result = await connection.ReturnValueAsync(query, new ParameterCollection(), None).WaitAsync(Timeout);

            result.Should().Be(3);
            var command = services.Recorder.CommandFor(query, 0) ?? services.Recorder.Commands.Single();
            command.ShardId.Should().BeNull("a database connection has no shard");
            command.Connection.DatabaseKey.Should().Be(Key);
            command.Connection.Access.Should().Be(FakeConnectionAccess.Write);
        }

        [Fact]
        [Trait("Spec", "blk-b9b01b19da18481dadf48870b9bdfead")]
        [Trait("Spec", "blk-8706b3516d9941a090b1ae8fe3cf16f0")]
        public async Task TypedReturnValueAsyncReturnsTheNamedOutputValue()
        {
            var services = new FakeProviderServices();
            var connection = Build(services)[Key].Read;
            var query = new QueryProcedure("GetOrderTotal");
            services.Script.ForQuery(query).WithOutput("@Total", 150);
            var parameters = new ParameterCollection { new FakeDbParameter("@Total", null, ParameterDirection.Output) };

            var total = await connection.ReturnValueAsync<int>(query, parameters, "@Total", None).WaitAsync(Timeout);

            total.Should().Be(150);
            services.Recorder.Commands.Should().ContainSingle().Which.Kind.Should().Be(FakeExecutionKind.NonQuery, "with a matching output parameter no reader is needed");
        }

        [Fact]
        [Trait("Spec", "blk-8c6a35420c6640b28bcdb57e50419e7c")]
        [Trait("Spec", "blk-a1e7eed7f7d5462ba2f19d58d2b42d65")]
        public async Task ListAsyncProjectsTheNamedColumnFromEveryRow()
        {
            var services = new FakeProviderServices();
            var connection = Build(services)[Key].Read;
            var query = new QueryProcedure("rd.Names");
            services.Script.ForQuery(query).Returns(new[] { "Id", "Name" }, new object[] { 1, "Alpha" }, new object[] { 2, "Beta" }, new object[] { 3, "Gamma" });

            var names = await connection.ListAsync<string>(query, new ParameterCollection(), "Name", None).WaitAsync(Timeout);
            var ids = await connection.ListAsync<int>(query, new ParameterCollection(), "Id", None).WaitAsync(Timeout);

            names.Should().Equal("Alpha", "Beta", "Gamma");
            ids.Should().Equal(1, 2, 3);
            services.Recorder.Commands.Should().OnlyContain(c => c.Connection.Access == FakeConnectionAccess.Read && c.ShardId == null);
        }

        [Fact]
        [Trait("Spec", "blk-8c6a35420c6640b28bcdb57e50419e7c")]
        public async Task MapListAsyncReturnsOneMappedModelPerRow()
        {
            var services = new FakeProviderServices();
            var connection = Build(services)[Key].Read;
            var query = new QueryProcedure("rd.Customers");
            services.Script.ForQuery(query).Returns(new[] { "Id", "Name" }, new object[] { 1, "one" }, new object[] { 2, "two" });

            var customers = await connection.MapListAsync<Customer>(query, new ParameterCollection(), None).WaitAsync(Timeout);

            customers.Select(c => (c.Id, c.Name)).Should().Equal((1, "one"), (2, "two"));
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task MapReaderAsyncOverloadsMapResultSetsAndHydrateAnInstance()
        {
            var services = new FakeProviderServices();
            var connection = Build(services)[Key].Read;
            var query = new QueryProcedure("rd.Customer");
            services.Script.ForQuery(query).Returns(new[] { "Id", "Name" }, new object[] { 42, "Ada" }).Returns(new[] { "LineId", "Qty" }, new object[] { 7, 2 }).Returns(new[] { "Text" }, new object[] { "n1" }, new object[] { "n2" });
            var instance = new Customer();

            var created = await connection.MapReaderAsync<Customer>(query, new ParameterCollection(), None).WaitAsync(Timeout);
            // The database connection offers no two-type overload: the multi-type forms take TModel plus two to eight reader positions.
            var withLists = await connection.MapReaderAsync<Customer, OrderLine, Note>(query, new ParameterCollection(), None).WaitAsync(Timeout);
            var hydrated = await connection.MapReaderAsync<Customer, OrderLine, Note>(instance, query, new ParameterCollection(), None).WaitAsync(Timeout);

            created!.Id.Should().Be(42);
            created.Lines.Should().BeNull();
            withLists!.Lines.Should().ContainSingle().Which.Qty.Should().Be(2);
            withLists.Notes!.Select(n => n.Text).Should().Equal("n1", "n2");
            hydrated.Should().BeSameAs(instance);
            instance.Lines.Should().HaveCount(1);
            services.Recorder.Commands.Should().OnlyContain(c => c.Behavior == System.Data.CommandBehavior.Default, "isTopOne is always false for the mapping overloads");
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task MapReaderWithCollectionsAsyncFillsCollectionPropertiesFromFollowingResultSets()
        {
            var services = new FakeProviderServices();
            var connection = Build(services)[Key].Read;
            var query = new QueryProcedure("rd.CustomerLines");
            services.Script.ForQuery(query).Returns(new[] { "Id" }, new object[] { 42 }).Returns(new[] { "LineId", "Qty" }, new object[] { 7, 2 }, new object[] { 8, 5 });
            var instance = new CustomerWithLines();

            var created = await connection.MapReaderWithCollectionsAsync<CustomerWithLines>(query, new ParameterCollection(), None).WaitAsync(Timeout);
            var hydrated = await connection.MapReaderWithCollectionsAsync<CustomerWithLines>(instance, query, new ParameterCollection(), None).WaitAsync(Timeout);

            created!.Lines!.Select(l => l.LineId).Should().Equal(7, 8);
            hydrated.Should().BeSameAs(instance);
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task MapOutputAsyncOverloadsMapOutputParametersAndResultSets()
        {
            var services = new FakeProviderServices();
            var connection = Build(services)[Key].Write;
            var query = new QueryProcedure("wt.Customer");
            services.Script.ForQuery(query).WithOutput("@Id", 42).WithOutput("@Name", "Ada").Returns(new[] { "LineId", "Qty" }, new object[] { 7, 2 });
            var parameters = new ParameterCollection().CreateOutputParameters<Customer>(NullLogger.Instance);
            var instance = new Customer();

            var created = await connection.MapOutputAsync<Customer>(query, parameters, None).WaitAsync(Timeout);
            var withLines = await connection.MapOutputAsync<Customer, OrderLine>(query, parameters, None).WaitAsync(Timeout);
            var hydrated = await connection.MapOutputAsync<Customer>(instance, query, parameters, None).WaitAsync(Timeout);

            created!.Id.Should().Be(42);
            created.Name.Should().Be("Ada");
            withLines!.Lines.Should().ContainSingle().Which.LineId.Should().Be(7);
            hydrated.Should().BeSameAs(instance);
            instance.Id.Should().Be(42);
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task QueryAsyncOverloadsForwardTheHandlerIsTopOneArgumentAndInstance()
        {
            var services = new FakeProviderServices();
            var connection = Build(services)[Key].Read;
            var query = new QueryProcedure("rd.Custom");
            var instance = new Customer();
            Customer? seen = null;
            QueryResultModelHandler<string, Customer> handler = (i, shardId, sprocName, arg, rdr, prms, description, logger) =>
            {
                seen = i;
                shardId.Should().Be(0, "a database connection reports shard id 0");
                return new Customer { Name = arg };
            };

            var withArg = await connection.QueryAsync<string, Customer>(query, new ParameterCollection(), handler, true, "tag", None).WaitAsync(Timeout);
            seen.Should().NotBeNull().And.NotBeSameAs(instance, "the instance-less overload constructs a new TModel");
            var withInstance = await connection.QueryAsync<string, Customer>(instance, query, new ParameterCollection(), handler, false, "tag2", None).WaitAsync(Timeout);
            var plain = await connection.QueryAsync<Customer>(instance, query, new ParameterCollection(), (i, s, n, a, r, p, d, l) => { a.Should().BeNull(); return i; }, false, None).WaitAsync(Timeout);
            await connection.QueryAsync<Customer>(query, new ParameterCollection(), (i, s, n, a, r, p, d, l) => i, false, None).WaitAsync(Timeout);

            withArg!.Name.Should().Be("tag");
            seen.Should().BeSameAs(instance);
            withInstance!.Name.Should().Be("tag2");
            plain.Should().BeSameAs(instance, "the handler decides what is returned, here the forwarded instance");
            var commands = services.Recorder.CommandsFor(query);
            commands.Select(c => c.Behavior).Should().Equal(System.Data.CommandBehavior.SingleRow, System.Data.CommandBehavior.Default, System.Data.CommandBehavior.Default, System.Data.CommandBehavior.Default);
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public async Task RunAsyncExecutesAQueryOrADatabaseBatch()
        {
            var services = new FakeProviderServices();
            var connection = Build(services)[Key].Write;
            services.Script.ForQuery("wt.Count").Returns(new[] { "N" }, new object[] { 9 });
            var batch = new DatabaseBatch<int>().Add(new QueryProcedure("wt.Prepare")).Add(new QueryProcedure("wt.Count"), "N");

            await connection.RunAsync(new QueryProcedure("wt.Touch"), new ParameterCollection(), None).WaitAsync(Timeout);
            var result = await connection.RunAsync(batch, None).WaitAsync(Timeout);

            result.Should().Be(9);
            var commands = services.Recorder.Commands;
            commands.Select(c => c.CommandText).Should().Equal("wt.Touch", "wt.Prepare", "wt.Count");
            commands[0].TransactionId.Should().BeNull("a plain run is not transactional");
            commands.Skip(1).Select(c => c.TransactionId).Distinct().Should().ContainSingle().Which.Should().NotBeNull();
        }

        [Fact]
        [Trait("Spec", "blk-6f28a0ef4ab3401a88e8304df6c914e2")]
        public void ConnectionStringResolvesTheEndpointsConnectionString()
        {
            var database = Build(new FakeProviderServices())[Key];

            database.Read.ConnectionString.Should().Be(FakeConnectionInfo.ForDatabase(Key, FakeConnectionAccess.Read));
            database.Write.ConnectionString.Should().Be(FakeConnectionInfo.ForDatabase(Key, FakeConnectionAccess.Write));
        }

        #endregion
    }
}
