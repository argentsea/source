using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static ArgentSea.Test.TestModels;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec section shard-addressing: ShardInstance endpoint fallback and immutability, and every ShardDataConnection method family
    /// (ReturnValueAsync, ListAsync, MapListAsync, MapReaderAsync, MapReaderWithCollectionsAsync, MapOutputAsync, QueryAsync, RunAsync)
    /// driven through the TestKit fake provider.
    /// </summary>
    public class ShardAddressingTests
    {
        private const string ShardSetName = "Set";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private static readonly CancellationToken None = CancellationToken.None;

        private static FakeShardSets BuildShardSets(FakeProviderServices services, params short[] shardIds)
            => FakeShardSets.Builder(services).AddShardSet(ShardSetName, shardIds).Build();

        private static ParameterCollection ShardParameter() => new ParameterCollection { new FakeDbParameter("@ShardId", (short)-1) };

        #region ShardInstance

        [Theory]
        [Trait("Spec", "blk-f215beac7d244d3db6a5e16b6105b9cf")]
        [Trait("Spec", "blk-6f1ed49912f540ac8abb55a91df19b67")]
        [Trait("Spec", "blk-bcc654c721494cd18aae3fd08254df6c")]
        [InlineData(7, false, true, "Server=write.example;Database=orders")]
        [InlineData(8, true, false, "Server=read.example;Database=orders")]
        public void ShardInstanceUsesTheOnlyConfiguredEndpointForBothReadAndWrite(short shardId, bool hasRead, bool hasWrite, string connectionString)
        {
            var services = new FakeProviderServices();
            var parent = BuildShardSets(services, 1);
            var endpoint = new FakeConnectionSettings { ConnectionString = connectionString, ConnectionDescription = "only" };
            var configuration = new FakeShardConfiguration
            {
                ShardId = shardId,
                ReadConnection = hasRead ? endpoint : null,
                WriteConnection = hasWrite ? endpoint : null
            };

            var instance = new ShardInstance<FakeShardSetsOptions>(parent, shardId, configuration);

            instance.ShardId.Should().Be(shardId);
            instance.Read.ConnectionString.Should().Be(connectionString);
            instance.Write.ConnectionString.Should().Be(connectionString);
        }

        [Fact]
        [Trait("Spec", "blk-f215beac7d244d3db6a5e16b6105b9cf")]
        public void ShardInstanceUsesEachEndpointWhenBothAreConfigured()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 3);

            var instance = shardSets[ShardSetName][3];

            instance.Read.ConnectionString.Should().Be(FakeConnectionInfo.ForShard(ShardSetName, 3, FakeConnectionAccess.Read));
            instance.Write.ConnectionString.Should().Be(FakeConnectionInfo.ForShard(ShardSetName, 3, FakeConnectionAccess.Write));
        }

        [Fact]
        [Trait("Spec", "blk-f215beac7d244d3db6a5e16b6105b9cf")]
        public async Task AShardConfiguredWithOnlyAWriteEndpointIsUsableThroughTheShardSet()
        {
            var services = new FakeProviderServices();
            var options = new FakeShardSetsOptions
            {
                ShardSets = new[]
                {
                    new FakeShardSetConfiguration
                    {
                        ShardSetName = ShardSetName,
                        DefaultShardId = 7,
                        Shards = new[] { new FakeShardConfiguration { ShardId = 7, WriteConnection = new FakeConnectionSettings { ConnectionString = FakeConnectionInfo.ForShard(ShardSetName, 7, FakeConnectionAccess.Write), ConnectionDescription = "w" } } }
                    }
                }
            };
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Returns(new[] { "Id" }, new object[] { 1 });

            var shardSets = new FakeShardSets(options, services, null, NullLogger<ShardSetsBase<FakeShardSetsOptions>>.Instance);
            var result = await shardSets[ShardSetName][7].Read.ListAsync<int>(query, new ParameterCollection(), "Id", None).WaitAsync(Timeout);

            result.Should().Equal(1);
            services.Recorder.Commands.Should().ContainSingle().Which.Connection.Access.Should().Be(FakeConnectionAccess.Write, "the read endpoint falls back to the write endpoint");
        }

        [Fact]
        [Trait("Spec", "blk-2b6f841e451845c1b1cbeac82c608259")]
        [Trait("Spec", "blk-31f6e73d962f45b48abccc064405a84f")]
        [Trait("Spec", "blk-1038cbe9e45a46e78ec27c4583ed6e59")]
        public void ShardInstanceExposesExactlyShardIdReadAndWriteAndNoneCanBeReassigned()
        {
            var type = typeof(ShardInstance<FakeShardSetsOptions>);
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            properties.Select(p => p.Name).Should().BeEquivalentTo(new[] { "ShardId", "Read", "Write" });
            properties.Should().OnlyContain(p => p.GetSetMethod() == null, "every member is get-only");
            type.GetProperty("ShardId")!.PropertyType.Should().Be(typeof(short));
            type.GetProperty("Read")!.PropertyType.Should().Be(typeof(ShardDataConnection<FakeShardSetsOptions>));
            type.GetProperty("Write")!.PropertyType.Should().Be(typeof(ShardDataConnection<FakeShardSetsOptions>));
            type.GetFields(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty();

            var instance = BuildShardSets(new FakeProviderServices(), 5)[ShardSetName][5];
            instance.ShardId.Should().Be(5);
            instance.Read.Should().BeSameAs(instance.Read, "the same endpoint is returned on every read");
            instance.Write.Should().NotBeSameAs(instance.Read);
        }

        #endregion

        #region Connection state

        [Fact]
        [Trait("Spec", "blk-40ff4b4cb78948cca0124bca55911db4")]
        [Trait("Spec", "blk-dfbe80d34b56419890a3e24db9e5f7d8")]
        public void MockResultsIsNeverNullAndIsTheSameDictionaryOnEveryRead()
        {
            var connection = BuildShardSets(new FakeProviderServices(), 1)[ShardSetName][1].Read;

            var first = connection.MockResults;
            first.Should().NotBeNull();
            first.Should().BeEmpty();
            connection.MockResults.Should().BeSameAs(first);
            typeof(ShardDataConnection<FakeShardSetsOptions>).GetProperty(nameof(connection.MockResults))!.GetSetMethod().Should().BeNull("mocks are registered by mutating the dictionary, never by assignment");
            connection.ConnectionString.Should().Be(FakeConnectionInfo.ForShard(ShardSetName, 1, FakeConnectionAccess.Read));
        }

        [Fact]
        [Trait("Spec", "blk-dfbe80d34b56419890a3e24db9e5f7d8")]
        public async Task ConcurrentFirstAccessToMockResultsPublishesOneDictionary()
        {
            var connection = BuildShardSets(new FakeProviderServices(), 1)[ShardSetName][1].Read;
            var gate = new ManualResetEventSlim(false);
            var readers = Enumerable.Range(0, 16).Select(_ => Task.Run(() => { gate.Wait(); return connection.MockResults; })).ToArray();
            gate.Set();

            var dictionaries = await Task.WhenAll(readers);

            dictionaries.Distinct().Should().ContainSingle();
        }

        [Fact]
        [Trait("Spec", "blk-259c49a3bbb94a6196ae8522a440c288")]
        [Trait("Spec", "blk-9192dc969a8c4a109176b89f9b449a99")]
        public async Task MockResultForTheQueryNameShortCircuitsReturnValueAsync()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Read;
            connection.MockResults["GetCount"] = 7;

            var result = await connection.ReturnValueAsync<int>(new QueryProcedure("GetCount"), "@Count", new ParameterCollection(), None).WaitAsync(Timeout);

            result.Should().Be(7);
            services.Recorder.Commands.Should().BeEmpty("the database-return path is not invoked");
            services.Recorder.ConnectionsOpened.Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-259c49a3bbb94a6196ae8522a440c288")]
        [Trait("Spec", "blk-918258df78454a1e91d1386762e09948")]
        public async Task MockResultsShortCircuitEveryResultReturningFamily()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Read;
            var customer = new Customer { Id = 1 };
            var key = new ShardKey<int>(9, 9);
            connection.MockResults["rd.Key"] = key;
            connection.MockResults["rd.Keys"] = new List<ShardKey<int>> { key };
            connection.MockResults["rd.Values"] = new List<int> { 4 };
            connection.MockResults["rd.Reader"] = customer;
            connection.MockResults["rd.Output"] = customer;
            connection.MockResults["rd.List"] = new List<Customer> { customer };
            var parameters = new ParameterCollection();

            (await connection.ReturnValueAsync<int>(new QueryProcedure("rd.Key"), "@S", "@R", parameters, None)).Should().Be(key);
            (await connection.ListAsync<int>(new QueryProcedure("rd.Keys"), "R", parameters, None)).Should().Equal(key);
            (await connection.ListAsync<int>(new QueryProcedure("rd.Values"), parameters, "V", None)).Should().Equal(4);
            (await connection.MapReaderAsync<Customer>(new QueryProcedure("rd.Reader"), parameters, None)).Should().BeSameAs(customer);
            (await connection.MapOutputAsync<Customer>(new QueryProcedure("rd.Output"), parameters, None)).Should().BeSameAs(customer);
            (await connection.MapListAsync<Customer>(new QueryProcedure("rd.List"), parameters, None)).Should().ContainSingle().Which.Should().BeSameAs(customer);
            services.Recorder.Commands.Should().BeEmpty();
        }

        #endregion

        #region ReturnValueAsync family

        [Fact]
        [Trait("Spec", "blk-e3303a5d0a6a45a7853f7dee97543f8e")]
        public async Task ReturnValueAsyncWithoutParametersReturnsTheProcedureReturnValue()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Write;
            var query = new QueryProcedure("wt.Save");
            services.Script.ForQuery(query).WithReturnValue(3);

            var result = await connection.ReturnValueAsync(query, None).WaitAsync(Timeout);

            result.Should().Be(3);
            var command = services.Recorder.CommandFor(query, 1);
            command!.Connection.Access.Should().Be(FakeConnectionAccess.Write);
            command.Parameters.Should().ContainSingle(p => p.Direction == ParameterDirection.ReturnValue);
        }

        [Fact]
        [Trait("Spec", "blk-e3303a5d0a6a45a7853f7dee97543f8e")]
        public async Task ReturnValueAsyncWithAShardParameterNameSendsTheShardId()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 6)[ShardSetName][6].Write;
            var query = new QueryProcedure("wt.Save");
            var parameters = ShardParameter();

            await connection.ReturnValueAsync(query, parameters, "@ShardId", None).WaitAsync(Timeout);
            await connection.ReturnValueAsync(query, parameters, None).WaitAsync(Timeout);

            var commands = services.Recorder.CommandsFor(query);
            commands[0].Parameter("@ShardId").Should().Be((short)6);
            commands[1].Parameter("@ShardId").Should().Be((short)-1, "without a shard parameter name the value is sent as supplied");
            parameters["@ShardId"].Value.Should().Be((short)-1);
        }

        [Fact]
        [Trait("Spec", "blk-e3303a5d0a6a45a7853f7dee97543f8e")]
        public async Task ReturnValueAsyncReadsANamedOutputParameter()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 2)[ShardSetName][2].Read;
            var query = new QueryProcedure("rd.GetCount");
            services.Script.ForQuery(query).WithOutput("@Count", 99);
            var parameters = new ParameterCollection { new FakeDbParameter("@Count", null, ParameterDirection.Output), new FakeDbParameter("@ShardId", (short)-1) };

            var plain = await connection.ReturnValueAsync<int>(query, "@Count", parameters, None).WaitAsync(Timeout);
            var withShard = await connection.ReturnValueAsync<int>(query, "@Count", parameters, "@ShardId", None).WaitAsync(Timeout);

            plain.Should().Be(99);
            withShard.Should().Be(99);
            services.Recorder.CommandsFor(query)[0].Parameter("@ShardId").Should().Be((short)-1);
            services.Recorder.CommandsFor(query)[1].Parameter("@ShardId").Should().Be((short)2);
        }

        [Fact]
        [Trait("Spec", "blk-e3303a5d0a6a45a7853f7dee97543f8e")]
        public async Task ReturnValueAsyncBuildsAKeyWhoseShardIdComesFromTheNamedOutput()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Write;
            var query = new QueryProcedure("wt.Insert");
            services.Script.ForQuery(query).WithOutput("@ShardId", (short)5).WithOutput("@RecordId", 19);
            var parameters = new ParameterCollection
            {
                new FakeDbParameter("@ShardId", null, ParameterDirection.Output),
                new FakeDbParameter("@RecordId", null, ParameterDirection.Output)
            };

            var key = await connection.ReturnValueAsync<int>(query, "@ShardId", "@RecordId", parameters, None).WaitAsync(Timeout);

            key.Should().Be(new ShardKey<int>(5, 19), "the shard id is the one the query output, not the endpoint's");
        }

        [Fact]
        [Trait("Spec", "blk-e3303a5d0a6a45a7853f7dee97543f8e")]
        public async Task ReturnValueAsyncBuildsAKeyFromTheFirstRowWhenNoOutputParameterMatches()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Write;
            var query = new QueryProcedure("wt.Insert");
            services.Script.ForQuery(query).Returns(new[] { "ShardId", "RecordId" }, new object[] { (short)5, 19 });

            var key = await connection.ReturnValueAsync<int>(query, "ShardId", "RecordId", new ParameterCollection(), None).WaitAsync(Timeout);

            key.Should().Be(new ShardKey<int>(5, 19));
            services.Recorder.CommandFor(query, 1)!.Behavior.Should().Be(CommandBehavior.SingleRow);
        }

        [Fact]
        [Trait("Spec", "blk-e3303a5d0a6a45a7853f7dee97543f8e")]
        public async Task ReturnValueAsyncChildKeyUsesTheEndpointShardIdWhenNoShardOutputIsNamed()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 4)[ShardSetName][4].Write;
            var query = new QueryProcedure("wt.InsertChild");
            services.Script.ForQuery(query).WithOutput("@RecordId", 19).WithOutput("@ChildId", 3).WithOutput("@ShardId", (short)9);
            var parameters = new ParameterCollection
            {
                new FakeDbParameter("@RecordId", null, ParameterDirection.Output),
                new FakeDbParameter("@ChildId", null, ParameterDirection.Output),
                new FakeDbParameter("@ShardId", null, ParameterDirection.Output)
            };

            var fromEndpoint = await connection.ReturnValueAsync<int, int>(query, "@RecordId", "@ChildId", parameters, None).WaitAsync(Timeout);
            var fromOutput = await connection.ReturnValueAsync<int, int>(query, "@ShardId", "@RecordId", "@ChildId", parameters, None).WaitAsync(Timeout);

            fromEndpoint.Should().Be(new ShardKey<int, int>(4, 19, 3));
            fromOutput.Should().Be(new ShardKey<int, int>(9, 19, 3));
        }

        [Fact]
        [Trait("Spec", "blk-e3303a5d0a6a45a7853f7dee97543f8e")]
        public async Task ReturnValueAsyncKeyOverloadsWithAShardParameterNameSendTheShardId()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 4)[ShardSetName][4].Write;
            var query = new QueryProcedure("wt.Insert");
            services.Script.ForQuery(query).WithOutput("@RecordId", 1).WithOutput("@ChildId", 2).WithOutput("@ShardOut", (short)4);
            var parameters = new ParameterCollection
            {
                new FakeDbParameter("@ShardId", (short)-1),
                new FakeDbParameter("@ShardOut", null, ParameterDirection.Output),
                new FakeDbParameter("@RecordId", null, ParameterDirection.Output),
                new FakeDbParameter("@ChildId", null, ParameterDirection.Output)
            };

            await connection.ReturnValueAsync<int>(query, "@ShardOut", "@RecordId", parameters, "@ShardId", None).WaitAsync(Timeout);
            await connection.ReturnValueAsync<int, int>(query, "@RecordId", "@ChildId", parameters, "@ShardId", None).WaitAsync(Timeout);
            await connection.ReturnValueAsync<int, int>(query, "@ShardOut", "@RecordId", "@ChildId", parameters, "@ShardId", None).WaitAsync(Timeout);

            services.Recorder.CommandsFor(query).Should().HaveCount(3).And.OnlyContain(c => (short)c.Parameter("@ShardId")! == 4);
        }

        #endregion

        #region ListAsync family

        [Fact]
        [Trait("Spec", "blk-3e1d5489bad6432893426d0e3407fdb8")]
        public async Task ListAsyncReturnsTheNamedColumnOfEveryRow()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Read;
            var query = new QueryProcedure("rd.Names");
            services.Script.ForQuery(query).Returns(new[] { "Id", "Name" }, new object[] { 1, "Alpha" }, new object[] { 2, "Beta" }, new object[] { 3, "Gamma" });

            var names = await connection.ListAsync<string>(query, new ParameterCollection(), "Name", None).WaitAsync(Timeout);

            names.Should().Equal("Alpha", "Beta", "Gamma");
        }

        [Fact]
        [Trait("Spec", "blk-4a906e29c44f409e9d466f8daf1e6225")]
        [Trait("Spec", "blk-542dbb9bdedb42c59e81b63c00568aac")]
        public async Task ListAsyncBuildsKeysFromTheEndpointShardIdWhenNoShardColumnIsNamed()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 4)[ShardSetName][4].Read;
            var query = new QueryProcedure("rd.Keys");
            services.Script.ForQuery(query).Returns(new[] { "RecordId" }, new object[] { 19 });

            var keys = await connection.ListAsync<int>(query, "RecordId", new ParameterCollection(), None).WaitAsync(Timeout);

            keys.Should().ContainSingle().Which.Should().Be(new ShardKey<int>(4, 19));
        }

        [Fact]
        [Trait("Spec", "blk-3e1d5489bad6432893426d0e3407fdb8")]
        public async Task ListAsyncBuildsKeysFromTheShardColumnWhenOneIsNamed()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 4)[ShardSetName][4].Read;
            var query = new QueryProcedure("rd.Keys");
            services.Script.ForQuery(query).Returns(new[] { "ShardId", "RecordId" }, new object[] { (short)2, 19 }, new object[] { (short)9, 20 });

            var keys = await connection.ListAsync<int>(query, "ShardId", "RecordId", new ParameterCollection(), None).WaitAsync(Timeout);

            keys.Should().Equal(new ShardKey<int>(2, 19), new ShardKey<int>(9, 20));
        }

        [Fact]
        [Trait("Spec", "blk-3e1d5489bad6432893426d0e3407fdb8")]
        public async Task ListAsyncBuildsChildKeysFromTheEndpointOrTheShardColumn()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 4)[ShardSetName][4].Read;
            var query = new QueryProcedure("rd.ChildKeys");
            services.Script.ForQuery(query).Returns(new[] { "ShardId", "RecordId", "ChildId" }, new object[] { (short)2, 19, 7 });

            var fromEndpoint = await connection.ListAsync<int, int>(query, "RecordId", "ChildId", new ParameterCollection(), None).WaitAsync(Timeout);
            var fromColumn = await connection.ListAsync<int, int>(query, "ShardId", "RecordId", "ChildId", new ParameterCollection(), None).WaitAsync(Timeout);

            fromEndpoint.Should().Equal(new ShardKey<int, int>(4, 19, 7));
            fromColumn.Should().Equal(new ShardKey<int, int>(2, 19, 7));
        }

        [Fact]
        [Trait("Spec", "blk-3e1d5489bad6432893426d0e3407fdb8")]
        public async Task ListAsyncOverloadsWithAShardParameterNameSendTheShardId()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 4)[ShardSetName][4].Read;
            var query = new QueryProcedure("rd.Keys");
            services.Script.ForQuery(query).Returns(new[] { "ShardId", "RecordId", "ChildId" }, new object[] { (short)4, 1, 2 });
            var parameters = ShardParameter();

            await connection.ListAsync<int>(query, parameters, "RecordId", "@ShardId", None).WaitAsync(Timeout);
            await connection.ListAsync<int>(query, "RecordId", parameters, "@ShardId", None).WaitAsync(Timeout);
            await connection.ListAsync<int>(query, "ShardId", "RecordId", parameters, "@ShardId", None).WaitAsync(Timeout);
            await connection.ListAsync<int, int>(query, "RecordId", "ChildId", parameters, "@ShardId", None).WaitAsync(Timeout);
            await connection.ListAsync<int, int>(query, "ShardId", "RecordId", "ChildId", parameters, "@ShardId", None).WaitAsync(Timeout);

            services.Recorder.CommandsFor(query).Should().HaveCount(5).And.OnlyContain(c => (short)c.Parameter("@ShardId")! == 4);
            parameters["@ShardId"].Value.Should().Be((short)-1);
        }

        [Fact]
        [Trait("Spec", "blk-3e1d5489bad6432893426d0e3407fdb8")]
        public async Task ListAsyncReturnsAnEmptyListWhenTheQueryYieldsNoRows()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Read;
            var query = new QueryProcedure("rd.Keys");
            services.Script.ForQuery(query).ReturnsNoRows("RecordId");

            (await connection.ListAsync<int>(query, "RecordId", new ParameterCollection(), None)).Should().BeEmpty();
            (await connection.ListAsync<int>(query, new ParameterCollection(), "RecordId", None)).Should().BeEmpty();
        }

        #endregion

        #region MapListAsync family

        [Fact]
        [Trait("Spec", "blk-227a5ba93576403ab607bb6dec80bf08")]
        public async Task MapListAsyncReturnsOneNewModelPerRow()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Read;
            var query = new QueryProcedure("rd.Customers");
            services.Script.ForQuery(query).Returns(new[] { "Id", "Name" }, new object[] { 1, "one" }, new object[] { 2, "two" });

            var customers = await connection.MapListAsync<Customer>(query, new ParameterCollection(), None).WaitAsync(Timeout);

            customers.Select(c => (c.Id, c.Name)).Should().Equal((1, "one"), (2, "two"));
            customers.Select(c => c).Distinct().Should().HaveCount(2, "each row yields its own instance");
        }

        [Fact]
        [Trait("Spec", "blk-227a5ba93576403ab607bb6dec80bf08")]
        public async Task MapListAsyncVariantsSendTheParametersAsIsOrWithTheShardId()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 3)[ShardSetName][3].Read;
            var query = new QueryProcedure("rd.Customers");
            services.Script.ForQuery(query).Returns(new[] { "Id", "Name" }, new object[] { 1, "one" });
            var parameters = ShardParameter();

            await connection.MapListAsync<Customer>(query, parameters, None).WaitAsync(Timeout);
            await connection.MapListAsync<Customer>(query, parameters, "@ShardId", None).WaitAsync(Timeout);

            var commands = services.Recorder.CommandsFor(query);
            commands[0].Parameter("@ShardId").Should().Be((short)-1);
            commands[1].Parameter("@ShardId").Should().Be((short)3);
            parameters["@ShardId"].Value.Should().Be((short)-1, "the caller's parameter collection is left unchanged");
        }

        #endregion

        #region MapReaderAsync and MapReaderWithCollectionsAsync families

        [Fact]
        [Trait("Spec", "blk-442183723e8e4c988bcd907ca510bf7e")]
        public async Task MapReaderAsyncConstructsTheModelFromTheFirstResultSet()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Read;
            var query = new QueryProcedure("rd.Customer");
            services.Script.ForQuery(query).Returns(new[] { "Id", "Name" }, new object[] { 42, "Ada" });

            var customer = await connection.MapReaderAsync<Customer>(query, new ParameterCollection(), None).WaitAsync(Timeout);

            customer!.Id.Should().Be(42);
            customer.Name.Should().Be("Ada");
            customer.Lines.Should().BeNull("only one result set was requested");
            services.Recorder.CommandFor(query, 1)!.Behavior.Should().Be(CommandBehavior.Default, "the mapping overloads do not request a single row");
        }

        [Fact]
        [Trait("Spec", "blk-442183723e8e4c988bcd907ca510bf7e")]
        public async Task MapReaderAsyncWithAnInstancePopulatesAndReturnsThatInstance()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Read;
            var query = new QueryProcedure("rd.Customer");
            services.Script.ForQuery(query).Returns(new[] { "Id", "Name" }, new object[] { 42, "Ada" }).Returns(new[] { "LineId", "Qty" }, new object[] { 7, 2 });
            var instance = new Customer();

            var single = await connection.MapReaderAsync<Customer>(instance, query, new ParameterCollection(), None).WaitAsync(Timeout);
            var withLines = await connection.MapReaderAsync<Customer, OrderLine>(instance, query, ShardParameter(), "@ShardId", None).WaitAsync(Timeout);

            single.Should().BeSameAs(instance);
            withLines.Should().BeSameAs(instance);
            instance.Id.Should().Be(42);
            instance.Lines.Should().ContainSingle().Which.LineId.Should().Be(7);
        }

        [Fact]
        [Trait("Spec", "blk-442183723e8e4c988bcd907ca510bf7e")]
        public async Task MapReaderAsyncMapsAdditionalResultSetsToListPropertiesOfTheirType()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Read;
            var query = new QueryProcedure("rd.CustomerWithLines");
            services.Script.ForQuery(query)
                .Returns(new[] { "Id", "Name" }, new object[] { 42, "Ada" })
                .Returns(new[] { "LineId", "Qty" }, new object[] { 7, 2 }, new object[] { 8, 5 });

            var customer = await connection.MapReaderAsync<Customer, OrderLine>(query, new ParameterCollection(), None).WaitAsync(Timeout);

            customer!.Id.Should().Be(42);
            customer.Lines!.Select(l => (l.LineId, l.Qty)).Should().Equal((7, 2), (8, 5));
        }

        [Fact]
        [Trait("Spec", "blk-442183723e8e4c988bcd907ca510bf7e")]
        public async Task MapReaderAsyncThrowsWhenTheModelResultSetHasMoreThanOneRow()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Read;
            var query = new QueryProcedure("rd.Customer");
            services.Script.ForQuery(query).Returns(new[] { "Id", "Name" }, new object[] { 1, "a" }, new object[] { 2, "b" });

            Func<Task> act = () => connection.MapReaderAsync<Customer>(query, new ParameterCollection(), None).WaitAsync(Timeout);

            await act.Should().ThrowAsync<UnexpectedMultiRowResultException>();
        }

        [Fact]
        [Trait("Spec", "blk-442183723e8e4c988bcd907ca510bf7e")]
        public async Task MapReaderAsyncReturnsNullWhenTheRequiredColumnIsNullOrNoRowIsReturned()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Read;
            var nullId = new QueryProcedure("rd.NullId");
            var noRows = new QueryProcedure("rd.NoRows");
            services.Script.ForQuery(nullId).Returns(new[] { "Id", "Name" }, new object?[] { null, "ghost" });
            services.Script.ForQuery(noRows).ReturnsNoRows("Id", "Name");

            (await connection.MapReaderAsync<Customer>(nullId, new ParameterCollection(), None)).Should().BeNull("Id is the record identifier");
            (await connection.MapReaderAsync<Customer>(noRows, new ParameterCollection(), None)).Should().BeNull();
            (await connection.MapReaderAsync<Customer, OrderLine>(nullId, new ParameterCollection(), None)).Should().BeNull("the multi-result overloads report a missing model the same way");
            (await connection.MapReaderAsync<Customer, OrderLine>(noRows, new ParameterCollection(), None)).Should().BeNull();
        }

        [Fact]
        [Trait("Spec", "blk-442183723e8e4c988bcd907ca510bf7e")]
        public async Task MapReaderAsyncWithAShardParameterNameSendsTheShardId()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 5)[ShardSetName][5].Read;
            var query = new QueryProcedure("rd.Customer");
            services.Script.ForQuery(query).Returns(new[] { "Id", "Name" }, new object[] { 1, "a" }).Returns(new[] { "LineId", "Qty" }, new object[] { 1, 1 });
            var parameters = ShardParameter();

            await connection.MapReaderAsync<Customer>(query, parameters, "@ShardId", None).WaitAsync(Timeout);
            await connection.MapReaderAsync<Customer, OrderLine>(query, parameters, "@ShardId", None).WaitAsync(Timeout);
            await connection.MapReaderAsync<Customer>(query, parameters, None).WaitAsync(Timeout);

            var commands = services.Recorder.CommandsFor(query);
            commands.Take(2).Should().OnlyContain(c => (short)c.Parameter("@ShardId")! == 5);
            commands[2].Parameter("@ShardId").Should().Be((short)-1);
        }

        [Fact]
        [Trait("Spec", "blk-4316bdad84244bedbb95c6162ce7be98")]
        public async Task MapReaderWithCollectionsAsyncFillsCollectionPropertiesFromTheFollowingResultSets()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Read;
            var query = new QueryProcedure("rd.CustomerLines");
            services.Script.ForQuery(query)
                .Returns(new[] { "Id" }, new object[] { 42 })
                .Returns(new[] { "LineId", "Qty" }, new object[] { 7, 2 }, new object[] { 8, 5 });
            var instance = new CustomerWithLines();

            var created = await connection.MapReaderWithCollectionsAsync<CustomerWithLines>(query, new ParameterCollection(), None).WaitAsync(Timeout);
            var hydrated = await connection.MapReaderWithCollectionsAsync<CustomerWithLines>(instance, query, ShardParameter(), "@ShardId", None).WaitAsync(Timeout);
            services.Recorder.CommandsFor(query)[1].Parameter("@ShardId").Should().Be((short)1);

            created!.Id.Should().Be(42);
            created.Lines!.Select(l => l.LineId).Should().Equal(7, 8);
            hydrated.Should().BeSameAs(instance);
            instance.Lines.Should().HaveCount(2);
        }

        [Fact]
        [Trait("Spec", "blk-4316bdad84244bedbb95c6162ce7be98")]
        public async Task MapReaderWithCollectionsAsyncThrowsWhenACollectionResultSetIsMissing()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Read;
            var query = new QueryProcedure("rd.CustomerLines");
            services.Script.ForQuery(query).Returns(new[] { "Id" }, new object[] { 42 });

            Func<Task> act = () => connection.MapReaderWithCollectionsAsync<CustomerWithLines>(query, new ParameterCollection(), None).WaitAsync(Timeout);

            (await act.Should().ThrowAsync<UnexpectedSqlResultException>()).WithMessage("*Lines*result set #2*");
        }

        #endregion

        #region MapOutputAsync family

        [Fact]
        [Trait("Spec", "blk-e2ecccf4af964c5abb42137a3429257a")]
        public async Task MapOutputAsyncConstructsTheModelFromOutputParameters()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Write;
            var query = new QueryProcedure("wt.Customer");
            services.Script.ForQuery(query).WithOutput("@Id", 42).WithOutput("@Name", "Ada");
            var parameters = new ParameterCollection().CreateOutputParameters<Customer>(NullLogger.Instance);

            var customer = await connection.MapOutputAsync<Customer>(query, parameters, None).WaitAsync(Timeout);

            customer!.Id.Should().Be(42);
            customer.Name.Should().Be("Ada");
            customer.Lines.Should().BeNull();
            services.Recorder.CommandFor(query, 1)!.Kind.Should().Be(FakeExecutionKind.Reader);
        }

        [Fact]
        [Trait("Spec", "blk-e2ecccf4af964c5abb42137a3429257a")]
        public async Task MapOutputAsyncMapsReaderResultSetsToListPropertiesAndHydratesAnInstance()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Write;
            var query = new QueryProcedure("wt.Customer");
            services.Script.ForQuery(query).WithOutput("@Id", 42).WithOutput("@Name", "Ada").Returns(new[] { "LineId", "Qty" }, new object[] { 7, 2 });
            var parameters = new ParameterCollection().CreateOutputParameters<Customer>(NullLogger.Instance);
            var instance = new Customer();

            var created = await connection.MapOutputAsync<Customer, OrderLine>(query, parameters, None).WaitAsync(Timeout);
            var hydrated = await connection.MapOutputAsync<Customer, OrderLine>(instance, query, parameters, None).WaitAsync(Timeout);
            var plain = await connection.MapOutputAsync<Customer>(instance, query, parameters, None).WaitAsync(Timeout);

            created!.Lines.Should().ContainSingle().Which.LineId.Should().Be(7);
            hydrated.Should().BeSameAs(instance);
            plain.Should().BeSameAs(instance);
            instance.Id.Should().Be(42);
        }

        [Fact]
        [Trait("Spec", "blk-e2ecccf4af964c5abb42137a3429257a")]
        public async Task MapOutputAsyncReturnsNullWhenTheRequiredOutputIsNull()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Write;
            var query = new QueryProcedure("wt.Customer");
            services.Script.ForQuery(query).WithOutput("@Id", DBNull.Value).WithOutput("@Name", "Ada");
            var parameters = new ParameterCollection().CreateOutputParameters<Customer>(NullLogger.Instance);

            var customer = await connection.MapOutputAsync<Customer>(query, parameters, None).WaitAsync(Timeout);

            customer.Should().BeNull();
        }

        [Fact]
        [Trait("Spec", "blk-e2ecccf4af964c5abb42137a3429257a")]
        public async Task MapOutputAsyncWithAShardParameterNameSendsTheShardId()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 8)[ShardSetName][8].Write;
            var query = new QueryProcedure("wt.Customer");
            services.Script.ForQuery(query).WithOutput("@Id", 1).Returns(new[] { "LineId", "Qty" }, new object[] { 1, 1 });
            var parameters = new ParameterCollection().CreateOutputParameters<Customer>(NullLogger.Instance);
            parameters.Add(new FakeDbParameter("@ShardId", (short)-1));

            await connection.MapOutputAsync<Customer>(query, parameters, "@ShardId", None).WaitAsync(Timeout);
            await connection.MapOutputAsync<Customer, OrderLine>(query, parameters, "@ShardId", None).WaitAsync(Timeout);
            await connection.MapOutputAsync<Customer>(new Customer(), query, parameters, "@ShardId", None).WaitAsync(Timeout);

            services.Recorder.CommandsFor(query).Should().HaveCount(3).And.OnlyContain(c => (short)c.Parameter("@ShardId")! == 8);
        }

        #endregion

        #region QueryAsync family

        [Fact]
        [Trait("Spec", "blk-9133bd2aa6a647cbbd882229fba04e54")]
        public async Task QueryAsyncReturnsWhatTheHandlerProducesAndForwardsIsTopOne()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Read;
            var query = new QueryProcedure("rd.Custom");
            services.Script.ForQuery(query).Returns(new[] { "Name" }, new object[] { "x" });
            var handlerCalls = 0;
            QueryResultModelHandler<object?, Customer> handler = (instance, shardId, sprocName, arg, rdr, prms, description, logger) =>
            {
                handlerCalls++;
                arg.Should().BeNull("the argument-less overload passes null");
                return new Customer { Id = 100 + shardId };
            };

            var topOne = await connection.QueryAsync<Customer>(query, new ParameterCollection(), handler, true, None).WaitAsync(Timeout);
            var all = await connection.QueryAsync<Customer>(query, new ParameterCollection(), handler, false, None).WaitAsync(Timeout);

            topOne!.Id.Should().Be(101);
            all!.Id.Should().Be(101);
            handlerCalls.Should().Be(2);
            var commands = services.Recorder.CommandsFor(query);
            commands[0].Behavior.Should().Be(CommandBehavior.SingleRow);
            commands[1].Behavior.Should().Be(CommandBehavior.Default);
        }

        [Fact]
        [Trait("Spec", "blk-9133bd2aa6a647cbbd882229fba04e54")]
        public async Task QueryAsyncThreadsTheOptionalArgumentAndShardIdThroughToTheHandler()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 6)[ShardSetName][6].Read;
            var query = new QueryProcedure("rd.Custom");
            var parameters = ShardParameter();
            QueryResultModelHandler<string, Customer> handler = (instance, shardId, sprocName, arg, rdr, prms, description, logger) => new Customer { Id = shardId, Name = arg };

            var withShard = await connection.QueryAsync<string, Customer>(query, parameters, "@ShardId", handler, false, "tag", None).WaitAsync(Timeout);
            var plain = await connection.QueryAsync<string, Customer>(query, parameters, handler, false, "tag", None).WaitAsync(Timeout);
            await connection.QueryAsync<Customer>(query, parameters, "@ShardId", (i, s, n, a, r, p, d, l) => null, false, None).WaitAsync(Timeout);

            withShard!.Name.Should().Be("tag");
            withShard.Id.Should().Be(6);
            plain!.Name.Should().Be("tag");
            var commands = services.Recorder.CommandsFor(query);
            commands[0].Parameter("@ShardId").Should().Be((short)6);
            commands[1].Parameter("@ShardId").Should().Be((short)-1);
            commands[2].Parameter("@ShardId").Should().Be((short)6);
        }

        #endregion

        #region RunAsync families

        [Fact]
        [Trait("Spec", "blk-66bbc3e1abb0488f9f872e5aabab5f92")]
        public async Task RunAsyncExecutesTheQueryAsANonQueryAndCompletes()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 2)[ShardSetName][2].Write;
            var query = new QueryProcedure("wt.Touch");
            var parameters = ShardParameter();

            await connection.RunAsync(query, parameters, None).WaitAsync(Timeout);
            await connection.RunAsync(query, parameters, "@ShardId", None).WaitAsync(Timeout);

            var commands = services.Recorder.CommandsFor(query);
            commands.Should().HaveCount(2).And.OnlyContain(c => c.Kind == FakeExecutionKind.NonQuery && c.Connection.Access == FakeConnectionAccess.Write);
            commands[0].Parameter("@ShardId").Should().Be((short)-1);
            commands[1].Parameter("@ShardId").Should().Be((short)2);
        }

        [Fact]
        [Trait("Spec", "blk-66bbc3e1abb0488f9f872e5aabab5f92")]
        public async Task RunAsyncThrowsTheProvidersFailure()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Write;
            var query = new QueryProcedure("wt.Touch");
            services.Script.ForQuery(query).Throws("boom");

            Func<Task> act = () => connection.RunAsync(query, new ParameterCollection(), None).WaitAsync(Timeout);

            (await act.Should().ThrowAsync<FakeDbException>()).WithMessage("boom");
        }

        [Fact]
        [Trait("Spec", "blk-c986c1ae849b478b8708afd317cb78f5")]
        public async Task RunAsyncWithAShardBatchExecutesItInOneTransactionAndReturnsItsResult()
        {
            var services = new FakeProviderServices();
            var connection = BuildShardSets(services, 1)[ShardSetName][1].Write;
            services.Script.ForQuery("wt.Insert").Returns(new[] { "RecordId" }, new object[] { 42 });
            var batch = new ShardBatch<ShardKey<int>>().Add(new QueryProcedure("wt.Prepare")).Add(new QueryProcedure("wt.Insert"), "RecordId");

            var key = await connection.RunAsync(batch, None).WaitAsync(Timeout);

            key.Should().Be(new ShardKey<int>(1, 42));
            services.Recorder.Commands.Select(c => c.TransactionId).Distinct().Should().ContainSingle().Which.Should().NotBeNull();
            services.Recorder.TransactionEvents.Select(e => e.Kind).Should().Equal(TransactionEventKind.Begin, TransactionEventKind.Commit);
        }

        #endregion
    }
}
