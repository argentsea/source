using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Spec section shard-set-fan-out: the ShardSets and ShardSet collections, and the ReadAll / ReadFirst / Write fan-out
    /// surfaces driven end to end through the TestKit fake provider, so combining, cancellation, failure aggregation,
    /// shard selection, shard-id injection and mock short-circuits are proven on the real wiring.
    /// </summary>
    public class ShardSetFanOutTests
    {
        private const string ShardSetName = "Set";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private static readonly CancellationToken None = CancellationToken.None;

        public class Model
        {
            public short ShardId { get; set; }
            public string? Name { get; set; }
        }

        /// <summary>Maps the first row of the first result set, or returns null when the shard has no row.</summary>
        private static Model? FirstRowOrNull(Model? instance, short shardId, string sprocName, object? optionalArgument, DbDataReader rdr, DbParameterCollection parameters, string connectionDescription, ILogger? logger)
        {
            if (!rdr.Read())
            {
                return null;
            }
            return new Model { ShardId = shardId, Name = rdr.GetString(rdr.GetOrdinal("Name")) };
        }

        private static FakeShardSets BuildShardSets(FakeProviderServices services, params short[] shardIds)
            => FakeShardSets.Builder(services).AddShardSet(ShardSetName, shardIds).Build();

        private static ParameterCollection ShardParameter() => new ParameterCollection { new FakeDbParameter("@ShardId", (short)-1) };

        private static DbParameterCollection OutputParameters()
        {
            var parameters = new ParameterCollection().CreateOutputParameters<Customer>(NullLogger.Instance);
            parameters.Add(new FakeDbParameter("@ShardId", (short)-1));
            return parameters;
        }

        /// <summary>Scripts a one-row Customer result (and an output-parameter equivalent) per shard, with Id = 10 × shard id.</summary>
        private static void ScriptCustomers(FakeProviderServices services, Query query, params short[] shardIds)
        {
            foreach (var shardId in shardIds)
            {
                services.Script.ForQuery(query, shardId)
                    .Returns(new[] { "Id", "Name" }, new object[] { shardId * 10, "shard " + shardId })
                    .WithOutput("@Id", shardId * 10).WithOutput("@Name", "shard " + shardId);
            }
        }

        #region ShardSets and ShardSet collections

        [Fact]
        [Trait("Spec", "blk-47b6923aeed44978b4c456989cdd89fd")]
        [Trait("Spec", "blk-a970ab55d822492bacd9e51477cd6ea1")]
        [Trait("Spec", "blk-d98cf5e3fcc14737a4d0a71904efa009")]
        public void ShardSetsIndexerReturnsTheBoundShardSetAndTheCollectionExposesItsEntries()
        {
            var shardSets = FakeShardSets.Builder(new FakeProviderServices()).AddShardSet("north", 1).AddShardSet("south", 2).Build();

            var north = shardSets["north"];
            north.Should().NotBeNull().And.BeSameAs(shardSets["north"]);
            north[1].ShardId.Should().Be(1);
            shardSets["south"].Should().NotBeSameAs(north);
            shardSets.Invoking(s => s["east"]).Should().Throw<KeyNotFoundException>();
            shardSets.Count.Should().Be(2);
            shardSets.IsSynchronized.Should().BeTrue();
            shardSets.SyncRoot.Should().BeSameAs(shardSets.SyncRoot);
            var copied = new ShardSetsBase<FakeShardSetsOptions>.ShardSet[2];
            shardSets.CopyTo(copied, 0);
            copied.Should().BeEquivalentTo(new[] { north, shardSets["south"] });
            shardSets.Cast<KeyValuePair<string, ShardSetsBase<FakeShardSetsOptions>.ShardSet>>().Select(e => e.Key).Should().BeEquivalentTo(new[] { "north", "south" });
        }

        [Fact]
        [Trait("Spec", "blk-895a4143df974151a86c94cbf9838d78")]
        public void ShardSetsMembershipCannotChangeAfterConstruction()
        {
            var services = new FakeProviderServices();
            var builder = FakeShardSets.Builder(services).AddShardSet("north", 1);
            var shardSets = builder.Build();
            builder.AddShardSet("late", 9);

            shardSets.Count.Should().Be(1);
            shardSets.Invoking(s => s["late"]).Should().Throw<KeyNotFoundException>();
            typeof(ShardSetsBase<FakeShardSetsOptions>).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name)
                .Should().NotContain(new[] { "Add", "Remove", "Clear", "Insert" });
            shardSets.Should().BeAssignableTo<ICollection>().And.NotBeAssignableTo<IDictionary>();
        }

        [Fact]
        [Trait("Spec", "blk-d3101da2692440e880a8fb41eb89dea4")]
        [Trait("Spec", "blk-7b3ccb69dd16448b9fd3aa8ef328a31b")]
        public void ShardSetIndexerByShardKeyReturnsTheSameMemberAsByShardId()
        {
            var shardSet = BuildShardSets(new FakeProviderServices(), 3, 7)[ShardSetName];
            IShardKey key = new ShardKey<int>(7, 123);

            shardSet[key].Should().BeSameAs(shardSet[(short)7]);
            shardSet[new ShardKey<int, int>(3, 1, 2)].Should().BeSameAs(shardSet[(short)3]);
            shardSet.Invoking(s => s[(short)9]).Should().Throw<KeyNotFoundException>();
        }

        [Fact]
        [Trait("Spec", "blk-c7f15cb6bff142a492c0db286d8f224e")]
        public void ShardSetExposesItsMembersDefaultShardAndOperationSurfaces()
        {
            var shardSets = FakeShardSets.Builder(new FakeProviderServices()).AddShardSet(ShardSetName, 7, new short[] { 3, 7, 11 }).Build();
            var shardSet = shardSets[ShardSetName];

            shardSet.Count.Should().Be(3);
            shardSet.DefaultShard.Should().BeSameAs(shardSet[(short)7]);
            shardSet.ReadAll.Should().NotBeNull();
            shardSet.ReadFirst.Should().NotBeNull();
            shardSet.Write.Should().NotBeNull();
            shardSet.IsSynchronized.Should().BeTrue();
            shardSet.SyncRoot.Should().BeSameAs(shardSet.SyncRoot);
            shardSet.Key.Should().Be(ShardSetName, "the key is the configured shard set name");
            shardSet.MockResults.Should().NotBeNull().And.BeSameAs(shardSet.MockResults);
            var copied = new ShardInstance<FakeShardSetsOptions>[3];
            shardSet.CopyTo(copied, 0);
            copied.Select(s => s.ShardId).Should().BeEquivalentTo(new short[] { 3, 7, 11 });
            shardSet.Cast<KeyValuePair<short, ShardInstance<FakeShardSetsOptions>>>().Select(e => e.Key).Should().BeEquivalentTo(new short[] { 3, 7, 11 });
        }

        #endregion

        #region ReadAll

        [Fact]
        [Trait("Spec", "blk-af8ab827d9814361bc241e8960527dd9")]
        [Trait("Spec", "blk-097b051667ce4413b78ab451a420de53")]
        [Trait("Spec", "blk-62f61258b59d4719a8b5e22869ee73c5")]
        public async Task ReadAllQueryCombinesTheModelsOfEveryShard()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 7, 11);
            var query = new QueryProcedure("rd.GetModel");
            services.Script.ForQuery(query, 7).Returns(new[] { "Name" }, new object[] { "A" });
            services.Script.ForQuery(query, 11).Returns(new[] { "Name" }, new object[] { "B" });

            var result = await shardSets[ShardSetName].ReadAll.QueryAsync<Model>(query, new ParameterCollection(), FirstRowOrNull, null!, None).WaitAsync(Timeout);

            result.Select(m => (m.ShardId, m.Name)).Should().BeEquivalentTo(new[] { ((short)7, "A"), ((short)11, "B") }, "both shards contribute, in no particular order");
            services.Recorder.Commands.Should().OnlyContain(c => c.Connection.Access == FakeConnectionAccess.Read);
        }

        [Fact]
        [Trait("Spec", "blk-62f61258b59d4719a8b5e22869ee73c5")]
        [Trait("Spec", "blk-af8ab827d9814361bc241e8960527dd9")]
        public async Task ReadAllCombinesEachShardsListAndQueriesOnlyReadEndpoints()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2, 3);
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query, 1).Returns(new[] { "Id" }, new object[] { 10 }, new object[] { 11 });
            services.Script.ForQuery(query, 2).ReturnsNoRows("Id");
            services.Script.ForQuery(query, 3).Returns(new[] { "Id" }, new object[] { 30 });

            var result = await shardSets[ShardSetName].ReadAll.ListAsync<int>(query, "Id", new ParameterCollection(), None).WaitAsync(Timeout);

            result.Should().BeEquivalentTo(new[] { 10, 11, 30 }, "every shard's rows are combined");
            services.Recorder.ShardsExecuted.Should().BeEquivalentTo(new short[] { 1, 2, 3 });
            services.Recorder.Commands.Should().OnlyContain(c => c.Connection.Access == FakeConnectionAccess.Read && c.CommandText == "rd.ListIds");
        }

        [Fact]
        [Trait("Spec", "blk-62f61258b59d4719a8b5e22869ee73c5")]
        public async Task ReadAllListAsyncBuildsKeysFromEachShardOrFromTheShardColumn()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            var query = new QueryProcedure("rd.Keys");
            services.Script.ForQuery(query, 1).Returns(new[] { "ShardId", "RecordId", "ChildId" }, new object[] { (short)9, 100, 5 });
            services.Script.ForQuery(query, 2).Returns(new[] { "ShardId", "RecordId", "ChildId" }, new object[] { (short)8, 200, 6 });
            var readAll = shardSets[ShardSetName].ReadAll;

            var fromColumn = await readAll.ListAsync<int>(query, "ShardId", "RecordId", new ParameterCollection(), None).WaitAsync(Timeout);
            var childFromShard = await readAll.ListAsync<int, int>(query, "RecordId", "ChildId", new ParameterCollection(), None).WaitAsync(Timeout);
            var childFromColumn = await readAll.ListAsync<int, int>(query, "ShardId", "RecordId", "ChildId", new ParameterCollection(), None).WaitAsync(Timeout);

            fromColumn.Should().BeEquivalentTo(new[] { new ShardKey<int>(9, 100), new ShardKey<int>(8, 200) });
            childFromShard.Should().BeEquivalentTo(new[] { new ShardKey<int, int>(1, 100, 5), new ShardKey<int, int>(2, 200, 6) }, "without a shard column each key carries its own shard's id");
            childFromColumn.Should().BeEquivalentTo(new[] { new ShardKey<int, int>(9, 100, 5), new ShardKey<int, int>(8, 200, 6) });
        }

        [Fact]
        [Trait("Spec", "blk-62f61258b59d4719a8b5e22869ee73c5")]
        public async Task ReadAllMapListCombinesTheMappedRowsOfEveryShard()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            var query = new QueryProcedure("rd.Customers");
            services.Script.ForQuery(query, 1).Returns(new[] { "Id", "Name" }, new object[] { 10, "a" }, new object[] { 11, "b" });
            services.Script.ForQuery(query, 2).Returns(new[] { "Id", "Name" }, new object[] { 20, "c" });

            var result = await shardSets[ShardSetName].ReadAll.MapListAsync<Customer>(query, new ParameterCollection(), None).WaitAsync(Timeout);

            result.Select(c => c.Id).Should().BeEquivalentTo(new[] { 10, 11, 20 });
        }

        [Fact]
        [Trait("Spec", "blk-62f61258b59d4719a8b5e22869ee73c5")]
        public async Task ReadAllMapReaderCombinesTheMappedModelsAndDropsShardsWithoutARow()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2, 3);
            var query = new QueryProcedure("rd.Customer");
            ScriptCustomers(services, query, 1, 3);
            services.Script.ForQuery(query, 2).ReturnsNoRows("Id", "Name");
            foreach (var shardId in new short[] { 1, 3 })
            {
                services.Script.ForQuery(query, shardId).Returns(new[] { "LineId", "Qty" }, new object[] { shardId, 1 });
            }

            var single = await shardSets[ShardSetName].ReadAll.MapReaderAsync<Customer>(query, new ParameterCollection(), None).WaitAsync(Timeout);
            var withLines = await shardSets[ShardSetName].ReadAll.MapReaderAsync<Customer, OrderLine>(query, new ParameterCollection(), None).WaitAsync(Timeout);

            single.Select(c => c.Id).Should().BeEquivalentTo(new[] { 10, 30 }, "shard 2 returned no row and contributes nothing");
            withLines.Select(c => (c.Id, c.Lines!.Single().LineId)).Should().BeEquivalentTo(new[] { (10, 1), (30, 3) });
        }

        [Fact]
        [Trait("Spec", "blk-62f61258b59d4719a8b5e22869ee73c5")]
        public async Task ReadAllMapOutputCombinesTheOutputMappedModelsOfEveryShard()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            var query = new QueryProcedure("rd.Customer");
            ScriptCustomers(services, query, 1, 2);

            var result = await shardSets[ShardSetName].ReadAll.MapOutputAsync<Customer>(query, OutputParameters(), None).WaitAsync(Timeout);

            result.Select(c => (c.Id, c.Name)).Should().BeEquivalentTo(new[] { (10, "shard 1"), (20, "shard 2") });
        }

        [Fact]
        [Trait("Spec", "blk-62f61258b59d4719a8b5e22869ee73c5")]
        [Trait("Spec", "blk-af8ab827d9814361bc241e8960527dd9")]
        public async Task ReadAllDropsNullShardResultsAndSendsEachShardItsOwnId()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2, 3);
            var query = new QueryProcedure("rd.GetModel");
            services.Script.ForQuery(query, 1).Returns(new[] { "Name" }, new object[] { "one" });
            services.Script.ForQuery(query, 2).ReturnsNoRows("Name");
            services.Script.ForQuery(query, 3).Returns(new[] { "Name" }, new object[] { "three" });
            var parameters = ShardParameter();

            var result = await shardSets[ShardSetName].ReadAll.QueryAsync<Model>(query, parameters, "@ShardId", FirstRowOrNull, None).WaitAsync(Timeout);

            result.Select(m => (m.ShardId, m.Name)).Should().BeEquivalentTo(new[] { ((short)1, "one"), ((short)3, "three") }, "a shard that returns null contributes nothing");
            foreach (var shardId in new short[] { 1, 2, 3 })
            {
                services.Recorder.CommandFor(query, shardId)!.Parameter("@ShardId").Should().Be(shardId, "each shard's command carries that shard's id");
            }
            parameters[0].Value.Should().Be((short)-1, "the caller's collection is not modified");
        }

        [Fact]
        [Trait("Spec", "blk-62f61258b59d4719a8b5e22869ee73c5")]
        public async Task ReadAllQueryPassesTheCallerArgumentToEveryShardsHandler()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            var query = new QueryProcedure("rd.GetModel");
            services.Script.ForQuery(query).Returns(new[] { "Name" }, new object[] { "x" });
            var seen = new System.Collections.Concurrent.ConcurrentBag<(short, string)>();

            var result = await shardSets[ShardSetName].ReadAll.QueryAsync<string, Model>(query, new ParameterCollection(), (i, shardId, n, arg, r, p, d, l) => { seen.Add((shardId, arg)); return new Model { ShardId = shardId }; }, "ctx", None).WaitAsync(Timeout);

            result.Should().HaveCount(2);
            seen.Should().BeEquivalentTo(new[] { ((short)1, "ctx"), ((short)2, "ctx") });
        }

        [Fact]
        [Trait("Spec", "blk-a9f3cfde102645e297e0416dbc5be64c")]
        public async Task ReadAllFaultsWithTheFailingShardsExceptionAndCancelsSiblings()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2, 3);
            var query = new QueryProcedure("rd.ListIds");
            var slowShardStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            services.Script.ForQuery(query, 1).Returns(new[] { "Id" }, new object[] { 10 });
            // Shard 2 fails only once shard 3 is executing, so the cancellation of a sibling in flight is observable.
            services.Script.ForQuery(query, 2).OnExecuting((_, __) => slowShardStarted.Task).Throws(new FakeDbException("shard 2 down"));
            services.Script.ForQuery(query, 3).OnExecuting((_, __) => { slowShardStarted.TrySetResult(true); return Task.CompletedTask; }).WaitsUntilCancelled();

            Func<Task> act = () => shardSets[ShardSetName].ReadAll.ListAsync<int>(query, "Id", new ParameterCollection(), None).WaitAsync(Timeout);

            (await act.Should().ThrowAsync<FakeDbException>()).WithMessage("shard 2 down");
            var slowShard = services.Recorder.CommandFor(query, 3);
            slowShard.Should().NotBeNull();
            slowShard.CancellationToken.IsCancellationRequested.Should().BeTrue("the sibling shard's query is cancelled when another shard faults");
            slowShard.Exception.Should().BeAssignableTo<OperationCanceledException>();
        }

        [Fact]
        [Trait("Spec", "blk-a9f3cfde102645e297e0416dbc5be64c")]
        [Trait("Spec", "blk-904da535839d47688e045271777843b7")]
        public async Task ReadAllFaultsEvenWhenAnotherShardHadAlreadyCompleted()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 7, 11);
            var query = new QueryProcedure("rd.GetModel");
            var shard7Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            services.Script.ForQuery(query, 7).OnExecuting((_, __) => { shard7Done.TrySetResult(true); return Task.CompletedTask; }).Returns(new[] { "Name" }, new object[] { "A" });
            services.Script.ForQuery(query, 11).OnExecuting((_, __) => shard7Done.Task).Throws(new FakeDbException("shard 11 down"));

            Func<Task> act = () => shardSets[ShardSetName].ReadAll.QueryAsync<Model>(query, new ParameterCollection(), FirstRowOrNull, null!, None).WaitAsync(Timeout);

            (await act.Should().ThrowAsync<FakeDbException>()).WithMessage("shard 11 down");
            services.Recorder.CommandFor(query, 7)!.Exception.Should().BeNull("shard 7 completed normally, and its result is discarded");
        }

        [Fact]
        [Trait("Spec", "blk-a9f3cfde102645e297e0416dbc5be64c")]
        public async Task ReadAllCombinesSeveralShardFailuresIntoAnAggregateException()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            var query = new QueryProcedure("rd.ListIds");
            var bothStarted = new CountdownEvent(2);
            // Both shards fail only once both are executing, so neither failure is a cancellation of the other.
            Func<RecordedCommand, CancellationToken, Task> waitForBoth = (_, __) => Task.Run(() => { bothStarted.Signal(); bothStarted.Wait(); });
            services.Script.ForQuery(query, 1).OnExecuting(waitForBoth).Throws(new FakeDbException("shard 1 down"));
            services.Script.ForQuery(query, 2).OnExecuting(waitForBoth).Throws(new FakeDbException("shard 2 down"));

            Func<Task> act = () => shardSets[ShardSetName].ReadAll.ListAsync<int>(query, "Id", new ParameterCollection(), None).WaitAsync(Timeout);

            var aggregate = (await act.Should().ThrowAsync<AggregateException>()).Which;
            aggregate.InnerExceptions.Select(e => e.Message).Should().BeEquivalentTo(new[] { "shard 1 down", "shard 2 down" });
        }

        [Fact]
        [Trait("Spec", "blk-af8ab827d9814361bc241e8960527dd9")]
        public async Task ShardsValuesLimitTheShardsQueried()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2, 3);
            var query = new QueryProcedure("rd.ListIds");
            services.Script.ForQuery(query).Returns(new[] { "Id" }, new object[] { 1 });
            var shards = new ShardsValues().Add(1).Add(3);

            var result = await shardSets[ShardSetName].ReadAll.ListAsync<int>(query, "Id", new ParameterCollection(), shards, None).WaitAsync(Timeout);

            result.Should().HaveCount(2);
            services.Recorder.ShardsExecuted.Should().BeEquivalentTo(new short[] { 1, 3 });
        }

        [Fact]
        [Trait("Spec", "blk-af8ab827d9814361bc241e8960527dd9")]
        public async Task PerShardParameterValuesReachOnlyTheirOwnShard()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2, 3);
            var query = new QueryProcedure("rd.Filtered");
            var parameters = new ParameterCollection { new FakeDbParameter("@Filter", "default"), new FakeDbParameter("@ShardId", (short)-1) };
            var shards = new ShardsValues().Add(1, "@Filter", "one").Add(3, "@Filter", "three");

            await shardSets[ShardSetName].ReadAll.MapListAsync<Customer>(query, parameters, shards, "@ShardId", None).WaitAsync(Timeout);

            services.Recorder.ShardsExecuted.Should().BeEquivalentTo(new short[] { 1, 3 });
            services.Recorder.CommandFor(query, 1)!.Parameter("@Filter").Should().Be("one");
            services.Recorder.CommandFor(query, 3)!.Parameter("@Filter").Should().Be("three");
            services.Recorder.CommandFor(query, 1)!.Parameter("@ShardId").Should().Be((short)1);
            services.Recorder.CommandFor(query, 3)!.Parameter("@ShardId").Should().Be((short)3);
            parameters["@Filter"].Value.Should().Be("default");
        }

        [Fact]
        [Trait("Spec", "blk-57ef08d5babb4915ad0a23fc6cf7efea")]
        [Trait("Spec", "blk-a9701748f55d4eabab4e41d3415d55b9")]
        [Trait("Spec", "blk-29d63635afce4736b7b1c7f4be010d0f")]
        public async Task AnEmptyShardSelectionReachesNoShardAndReturnsTheEmptyOrDefaultResult()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            var shardSet = shardSets[ShardSetName];
            var query = new QueryProcedure("rd.Anything");
            var empty = new ShardsValues();
            var parameters = new ParameterCollection();

            (await shardSet.ReadAll.QueryAsync<Model>(query, parameters, empty, FirstRowOrNull, None)).Should().BeEmpty();
            (await shardSet.ReadAll.ListAsync<int>(query, "Id", parameters, empty, None)).Should().BeEmpty();
            (await shardSet.ReadAll.MapListAsync<Customer>(query, parameters, empty, None)).Should().BeEmpty();
            (await shardSet.ReadFirst.MapReaderAsync<Customer>(query, parameters, empty, None)).Should().BeNull();
            (await shardSet.ReadFirst.QueryAsync<Model>(query, parameters, empty, FirstRowOrNull, None)).Should().BeNull();
            await shardSet.Write.RunAsync(query, parameters, empty, None);
            (await shardSet.Write.QueryAsync<Model>(query, parameters, empty, FirstRowOrNull, None)).Should().BeEmpty();
            (await shardSet.Write.ListAsync<int>(query, "Id", parameters, empty, None)).Should().BeEmpty();
            await shardSet.Write.RunAsync(new ShardSetBatch().Add(query), empty, None);

            services.Recorder.Commands.Should().BeEmpty();
            services.Recorder.ConnectionsOpened.Should().BeEmpty();
        }

        #endregion

        #region ReadFirst

        [Fact]
        [Trait("Spec", "blk-ed70930c2ec94fb8b428748a4fa65630")]
        [Trait("Spec", "blk-31154526294748d19a16add7fd9c4e44")]
        [Trait("Spec", "blk-52e8b0e366e94a518ce0112694da3175")]
        public async Task ReadFirstReturnsTheFirstNonNullResultAndCancelsLosers()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2, 3);
            var query = new QueryProcedure("rd.FindModel");
            var slowShardStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            services.Script.ForQuery(query, 1).ReturnsNoRows("Name");
            services.Script.ForQuery(query, 2).OnExecuting((_, __) => slowShardStarted.Task).Returns(new[] { "Name" }, new object[] { "found on two" });
            services.Script.ForQuery(query, 3).OnExecuting((_, __) => { slowShardStarted.TrySetResult(true); return Task.CompletedTask; }).WaitsUntilCancelled();

            var result = await shardSets[ShardSetName].ReadFirst.QueryAsync<Model>(query, new ParameterCollection(), FirstRowOrNull, None).WaitAsync(Timeout);

            result.Should().NotBeNull();
            result.ShardId.Should().Be(2);
            result.Name.Should().Be("found on two");
            var loser = services.Recorder.CommandFor(query, 3);
            loser.Should().NotBeNull();
            loser.CancellationToken.IsCancellationRequested.Should().BeTrue("the losing shard's query is cancelled once a result is chosen");
            services.Recorder.ShardsExecuted.Should().BeEquivalentTo(new short[] { 1, 2, 3 }, "every shard is queried");
        }

        [Fact]
        [Trait("Spec", "blk-ed70930c2ec94fb8b428748a4fa65630")]
        [Trait("Spec", "blk-b8ee74af36e848928dcd9d85bddaaaa3")]
        public async Task ReadFirstSkipsANullShardResultInFavourOfALaterNonNullOne()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 2, 5);
            var query = new QueryProcedure("rd.Customer");
            var shard2Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            services.Script.ForQuery(query, 2).OnExecuting((_, __) => { shard2Done.TrySetResult(true); return Task.CompletedTask; }).ReturnsNoRows("Id", "Name");
            services.Script.ForQuery(query, 5).OnExecuting((_, __) => shard2Done.Task).Returns(new[] { "Id", "Name" }, new object[] { 7, "five" });

            var result = await shardSets[ShardSetName].ReadFirst.MapReaderAsync<Customer>(query, new ParameterCollection(), None).WaitAsync(Timeout);

            result.Should().NotBeNull();
            result.Id.Should().Be(7);
        }

        [Fact]
        [Trait("Spec", "blk-ed70930c2ec94fb8b428748a4fa65630")]
        public async Task ReadFirstReturnsNullWhenNoShardHasAResult()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            var query = new QueryProcedure("rd.FindModel");
            services.Script.ForQuery(query).ReturnsNoRows("Name");

            var result = await shardSets[ShardSetName].ReadFirst.QueryAsync<Model>(query, new ParameterCollection(), FirstRowOrNull, None).WaitAsync(Timeout);

            result.Should().BeNull();
            services.Recorder.ShardsExecuted.Should().BeEquivalentTo(new short[] { 1, 2 });
        }

        [Fact]
        [Trait("Spec", "blk-e122ba8da15748819f880d902c63aeba")]
        [Trait("Spec", "blk-a4c189813bcb423c9e740403b853ff08")]
        public async Task ReadFirstIgnoresAFaultedShardWhenAnotherShardProducesAResult()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 7, 11);
            var query = new QueryProcedure("rd.GetModel");
            var shard7Failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            services.Script.ForQuery(query, 7).OnExecuting((_, __) => { shard7Failed.TrySetResult(true); return Task.CompletedTask; }).Throws(new FakeDbException("shard 7 down"));
            services.Script.ForQuery(query, 11).OnExecuting((_, __) => shard7Failed.Task).Returns(new[] { "Name" }, new object[] { "B" });

            var result = await shardSets[ShardSetName].ReadFirst.QueryAsync<Model>(query, new ParameterCollection(), FirstRowOrNull, None).WaitAsync(Timeout);

            result.Should().NotBeNull();
            result.Name.Should().Be("B");
            result.ShardId.Should().Be(11);
            services.Recorder.CommandFor(query, 7)!.Exception.Should().BeOfType<FakeDbException>("the failure was recorded but never surfaced");
        }

        [Fact]
        [Trait("Spec", "blk-e122ba8da15748819f880d902c63aeba")]
        public async Task ReadFirstSurfacesTheShardFailuresOnlyWhenNoShardProducesAResult()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            var query = new QueryProcedure("rd.GetModel");
            services.Script.ForQuery(query, 1).Throws(new FakeDbException("shard 1 down"));
            services.Script.ForQuery(query, 2).ReturnsNoRows("Name");
            Func<Task> single = () => shardSets[ShardSetName].ReadFirst.QueryAsync<Model>(query, new ParameterCollection(), FirstRowOrNull, None).WaitAsync(Timeout);

            (await single.Should().ThrowAsync<FakeDbException>("a single failure is rethrown as-is")).WithMessage("shard 1 down");

            services.Script.ForQuery(query, 2).Throws(new FakeDbException("shard 2 down"));
            var aggregate = (await single.Should().ThrowAsync<AggregateException>("several failures are thrown together")).Which;
            aggregate.InnerExceptions.Select(e => e.Message).Should().BeEquivalentTo(new[] { "shard 1 down", "shard 2 down" });
        }

        [Fact]
        [Trait("Spec", "blk-ed70930c2ec94fb8b428748a4fa65630")]
        public async Task ReadFirstThrowsOperationCanceledWhenTheCallerCancelsBeforeAnyResult()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            var query = new QueryProcedure("rd.GetModel");
            using var cts = new CancellationTokenSource();
            services.Script.ForQuery(query, 1).OnExecuting((_, __) => { cts.Cancel(); return Task.CompletedTask; }).WaitsUntilCancelled();
            services.Script.ForQuery(query, 2).WaitsUntilCancelled();

            Func<Task> act = () => shardSets[ShardSetName].ReadFirst.QueryAsync<Model>(query, new ParameterCollection(), FirstRowOrNull, cts.Token).WaitAsync(Timeout);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        [Trait("Spec", "blk-52e8b0e366e94a518ce0112694da3175")]
        public async Task ReadFirstMappingFamiliesReturnTheFirstMappedModelFromTheSelectedShards()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2, 3);
            var query = new QueryProcedure("rd.Customer");
            ScriptCustomers(services, query, 3);
            services.Script.ForQuery(query, 3).Returns(new[] { "LineId", "Qty" }, new object[] { 33, 1 });
            services.Script.ForQuery(query, 1).ReturnsNoRows("Id", "Name");
            services.Script.ForQuery(query, 2).ReturnsNoRows("Id", "Name");
            var readFirst = shardSets[ShardSetName].ReadFirst;
            var only13 = new ShardsValues().Add(1).Add(3);

            var reader = await readFirst.MapReaderAsync<Customer>(query, ShardParameter(), only13, "@ShardId", None).WaitAsync(Timeout);
            var output = await readFirst.MapOutputAsync<Customer>(query, OutputParameters(), only13, "@ShardId", None).WaitAsync(Timeout);

            reader!.Id.Should().Be(30);
            output!.Name.Should().Be("shard 3");
            var selected = services.Recorder.CommandsFor(query);
            selected.Should().NotContain(c => c.ShardId == 2, "shard 2 is outside the selection");
            selected.Should().OnlyContain(c => (short)c.Parameter("@ShardId")! == c.ShardId!.Value, "each shard receives its own id when a shard parameter is named");
            services.Recorder.Clear();

            var readerWithLines = await readFirst.MapReaderAsync<Customer, OrderLine>(query, ShardParameter(), "@ShardId", None).WaitAsync(Timeout);
            var outputAll = await readFirst.MapOutputAsync<Customer>(query, OutputParameters(), None).WaitAsync(Timeout);

            readerWithLines!.Lines.Should().ContainSingle().Which.LineId.Should().Be(33);
            outputAll!.Id.Should().Be(30);
            services.Recorder.CommandsFor(query).Should().Contain(c => c.ShardId == 3, "the whole shard set is targeted");
        }

        #endregion

        #region Write

        [Fact]
        [Trait("Spec", "blk-394f8651b5054f4bb5d45b35b49a667c")]
        [Trait("Spec", "blk-408c382660464036ba418d98a58eeef1")]
        public async Task WriteRunWaitsForEveryShardAndAggregatesFailures()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2, 3);
            var query = new QueryProcedure("wt.Update");
            services.Script.ForQuery(query, 1).Throws(new FakeDbException("shard 1 down"));
            services.Script.ForQuery(query, 3).Throws(new FakeDbException("shard 3 down"));

            Func<Task> act = () => shardSets[ShardSetName].Write.RunAsync(query, new ParameterCollection(), None).WaitAsync(Timeout);

            var aggregate = (await act.Should().ThrowAsync<AggregateException>()).Which;
            aggregate.InnerExceptions.Select(e => e.Message).Should().BeEquivalentTo(new[] { "shard 1 down", "shard 3 down" });
            services.Recorder.ShardsExecuted.Should().BeEquivalentTo(new short[] { 1, 2, 3 }, "a write is not cancelled when a sibling fails");
            services.Recorder.Commands.Should().OnlyContain(c => c.Connection.Access == FakeConnectionAccess.Write);
            services.Recorder.CommandFor(query, 2)!.Exception.Should().BeNull("the healthy shard's write completes");
        }

        [Fact]
        [Trait("Spec", "blk-394f8651b5054f4bb5d45b35b49a667c")]
        [Trait("Spec", "blk-6d1a973b8c8e4edaa14880033059c7ea")]
        public async Task WriteRunRethrowsASingleShardFailureUnwrappedAfterTheOtherShardCompleted()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 7, 11);
            var query = new QueryProcedure("wt.Update");
            var shard11Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            services.Script.ForQuery(query, 7).OnExecuting((_, __) => shard11Started.Task);
            services.Script.ForQuery(query, 11).OnExecuting((_, __) => { shard11Started.TrySetResult(true); return Task.CompletedTask; }).Throws(new FakeDbException("shard 11 down"));

            Func<Task> act = () => shardSets[ShardSetName].Write.RunAsync(query, new ParameterCollection(), None).WaitAsync(Timeout);

            (await act.Should().ThrowAsync<FakeDbException>()).WithMessage("shard 11 down");
            var shard7 = services.Recorder.CommandFor(query, 7);
            shard7!.Exception.Should().BeNull("shard 7's write ran to completion and is not rolled back");
            shard7.CancellationToken.IsCancellationRequested.Should().BeFalse("writes are never cancelled because a sibling failed");
        }

        [Fact]
        [Trait("Spec", "blk-408c382660464036ba418d98a58eeef1")]
        [Trait("Spec", "blk-f1412c545d604e82bc9dc263e77811c2")]
        public async Task WriteRunTargetsEveryShardOrTheSelectionAndInjectsTheShardIdOnlyWhenNamed()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2, 3);
            var query = new QueryProcedure("wt.Update");
            var write = shardSets[ShardSetName].Write;

            await write.RunAsync(query, ShardParameter(), None).WaitAsync(Timeout);
            services.Recorder.Commands.Should().HaveCount(3).And.OnlyContain(c => (short)c.Parameter("@ShardId")! == -1, "no shard parameter name was supplied");
            services.Recorder.Clear();

            await write.RunAsync(query, ShardParameter(), new ShardsValues().Add(2), "@ShardId", None).WaitAsync(Timeout);
            services.Recorder.Commands.Should().ContainSingle().Which.Parameter("@ShardId").Should().Be((short)2);
        }

        [Fact]
        [Trait("Spec", "blk-394f8651b5054f4bb5d45b35b49a667c")]
        [Trait("Spec", "blk-408c382660464036ba418d98a58eeef1")]
        public async Task WriteBatchIsDispatchedToEveryTargetedShard()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2, 3);
            var batch = new ShardSetBatch().Add(new QueryProcedure("wt.Step1")).Add(new QueryProcedure("wt.Step2"));
            var write = shardSets[ShardSetName].Write;

            await write.RunAsync(batch, None).WaitAsync(Timeout);
            services.Recorder.ShardsExecuted.Should().BeEquivalentTo(new short[] { 1, 2, 3 });
            services.Recorder.TransactionEvents.Where(e => e.Kind == TransactionEventKind.Commit).Should().HaveCount(3, "each shard runs the batch in its own transaction");
            services.Recorder.Clear();

            await write.RunAsync(batch, new ShardsValues().Add(3), None).WaitAsync(Timeout);
            services.Recorder.ShardsExecuted.Should().Equal(3);
        }

        [Fact]
        [Trait("Spec", "blk-394f8651b5054f4bb5d45b35b49a667c")]
        [Trait("Spec", "blk-57ef08d5babb4915ad0a23fc6cf7efea")]
        public async Task WriteBatchIsSuppressedOnceTheMockDictionaryHasBeenObtainedButQueriesAreNot()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            var shardSet = shardSets[ShardSetName];
            var query = new QueryProcedure("wt.Update");
            _ = shardSet.MockResults;

            await shardSet.Write.RunAsync(new ShardSetBatch().Add(new QueryProcedure("wt.Step1")), None).WaitAsync(Timeout);
            services.Recorder.Commands.Should().BeEmpty("an obtained (even empty) mock dictionary skips the batch on every shard");

            await shardSet.Write.RunAsync(query, new ParameterCollection(), None).WaitAsync(Timeout);
            services.Recorder.ShardsExecuted.Should().BeEquivalentTo(new short[] { 1, 2 }, "a query is mocked only by its own name");
        }

        [Fact]
        [Trait("Spec", "blk-81c48d76541d427da7e4c747ea3428d4")]
        [Trait("Spec", "blk-14034258049641c0a255069c3aa1514a")]
        public async Task WriteQueryReturnsTheNonNullHandlerResultsOfTheSelectedShards()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 3, 5, 7);
            var query = new QueryProcedure("wt.Summarize");
            var shards = new ShardsValues().Add(3).Add(7);

            var result = await shardSets[ShardSetName].Write.QueryAsync<CustomerSummary>(query, new ParameterCollection(), shards,
                (i, shardId, n, a, r, p, d, l) => shardId == 5 ? null : new CustomerSummary { Id = shardId == 3 ? 42 : 84 }, None).WaitAsync(Timeout);

            result.Select(s => s.Id).Should().BeEquivalentTo(new[] { 42, 84 });
            services.Recorder.ShardsExecuted.Should().BeEquivalentTo(new short[] { 3, 7 });
            services.Recorder.Commands.Should().OnlyContain(c => c.Connection.Access == FakeConnectionAccess.Write);
        }

        [Fact]
        [Trait("Spec", "blk-788b420905de440faf9bb6a2c89a8b2b")]
        [Trait("Spec", "blk-8710b60f5360447a85332529cf5a90c5")]
        public async Task WriteMapOutputReturnsTheOutputMappedModelsOfTheSelectedShards()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 3, 5, 7);
            var query = new QueryProcedure("wt.Customer");
            services.Script.ForQuery(query, 3).WithOutput("@Id", 42);
            services.Script.ForQuery(query, 7).WithOutput("@Id", 84);

            var result = await shardSets[ShardSetName].Write.MapOutputAsync<CustomerSummary>(query, new ParameterCollection().CreateOutputParameters<CustomerSummary>(NullLogger.Instance), new ShardsValues().Add(3).Add(7), None).WaitAsync(Timeout);

            result.Select(s => s.Id).Should().BeEquivalentTo(new[] { 42, 84 });
            services.Recorder.ShardsExecuted.Should().BeEquivalentTo(new short[] { 3, 7 });
        }

        [Fact]
        [Trait("Spec", "blk-e12e41e5e5784d3ba0eced54f583aafd")]
        [Trait("Spec", "blk-f1dd0ae9f9484bcdb394951dce3d08d7")]
        public async Task WriteMapReaderReturnsTheReaderMappedModelsOfTheSelectedShards()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 3, 5, 7);
            var query = new QueryProcedure("wt.Customer");
            services.Script.ForQuery(query, 3).Returns(new[] { "Id" }, new object[] { 42 }).Returns(new[] { "LineId", "Qty" }, new object[] { 1, 1 });
            services.Script.ForQuery(query, 7).Returns(new[] { "Id" }, new object[] { 84 }).Returns(new[] { "LineId", "Qty" }, new object[] { 2, 2 });
            var shards = new ShardsValues().Add(3).Add(7);

            var summaries = await shardSets[ShardSetName].Write.MapReaderAsync<CustomerSummary>(query, new ParameterCollection(), shards, None).WaitAsync(Timeout);
            var withLines = await shardSets[ShardSetName].Write.MapReaderAsync<Customer, OrderLine>(query, new ParameterCollection(), shards, None).WaitAsync(Timeout);

            summaries.Select(s => s.Id).Should().BeEquivalentTo(new[] { 42, 84 });
            withLines.Select(c => (c.Id, c.Lines!.Single().LineId)).Should().BeEquivalentTo(new[] { (42, 1), (84, 2) });
            services.Recorder.Commands.Should().OnlyContain(c => c.Connection.Access == FakeConnectionAccess.Write && c.ShardId != 5);
        }

        [Fact]
        [Trait("Spec", "blk-b0e61088b6d846b9a7ffb7d4b22ead2f")]
        [Trait("Spec", "blk-a53af1a737c94d698b9c856d43a067dd")]
        public async Task WriteListCombinesColumnValuesAndKeysOfTheSelectedShards()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 3, 5, 7);
            var query = new QueryProcedure("wt.Insert");
            services.Script.ForQuery(query, 3).Returns(new[] { "RecordId", "ShardId", "ChildId" }, new object[] { 42, (short)30, 1 });
            services.Script.ForQuery(query, 7).Returns(new[] { "RecordId", "ShardId", "ChildId" }, new object[] { 84, (short)70, 2 });
            var shards = new ShardsValues().Add(3).Add(7);
            var write = shardSets[ShardSetName].Write;

            var values = await write.ListAsync<int>(query, "RecordId", new ParameterCollection(), shards, None).WaitAsync(Timeout);
            var keysFromColumn = await write.ListAsync<int>(query, "ShardId", "RecordId", new ParameterCollection(), shards, None).WaitAsync(Timeout);
            var childKeysFromShard = await write.ListAsync<int, int>(query, "RecordId", "ChildId", new ParameterCollection(), shards, None).WaitAsync(Timeout);

            values.Should().BeEquivalentTo(new[] { 42, 84 });
            keysFromColumn.Should().BeEquivalentTo(new[] { new ShardKey<int>(30, 42), new ShardKey<int>(70, 84) });
            childKeysFromShard.Should().BeEquivalentTo(new[] { new ShardKey<int, int>(3, 42, 1), new ShardKey<int, int>(7, 84, 2) }, "without a shard column the key carries the executing shard's id");
        }

        [Fact]
        [Trait("Spec", "blk-f1412c545d604e82bc9dc263e77811c2")]
        [Trait("Spec", "blk-62f61258b59d4719a8b5e22869ee73c5")]
        public async Task EveryFanOutFamilyInjectsEachShardsOwnIdWhenAShardParameterIsNamed()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            var query = new QueryProcedure("rd.Everything");
            ScriptCustomers(services, query, 1, 2);
            var shardSet = shardSets[ShardSetName];
            var calls = new List<Func<Task>>
            {
                () => shardSet.ReadAll.QueryAsync<Model>(query, ShardParameter(), "@ShardId", FirstRowOrNull, None),
                () => shardSet.ReadAll.MapListAsync<Customer>(query, ShardParameter(), "@ShardId", None),
                () => shardSet.ReadAll.MapReaderAsync<Customer>(query, ShardParameter(), "@ShardId", None),
                () => shardSet.ReadAll.MapOutputAsync<Customer>(query, OutputParameters(), "@ShardId", None),
                () => shardSet.ReadAll.ListAsync<int>(query, "Id", ShardParameter(), "@ShardId", None),
                () => shardSet.ReadFirst.QueryAsync<Model>(query, ShardParameter(), "@ShardId", FirstRowOrNull, None),
                () => shardSet.ReadFirst.MapReaderAsync<Customer>(query, ShardParameter(), "@ShardId", None),
                () => shardSet.ReadFirst.MapOutputAsync<Customer>(query, OutputParameters(), "@ShardId", None),
                () => shardSet.Write.RunAsync(query, ShardParameter(), "@ShardId", None),
                () => shardSet.Write.QueryAsync<Model>(query, ShardParameter(), "@ShardId", FirstRowOrNull, None),
                () => shardSet.Write.MapReaderAsync<Customer>(query, ShardParameter(), "@ShardId", None),
                () => shardSet.Write.MapOutputAsync<Customer>(query, OutputParameters(), "@ShardId", None),
                () => shardSet.Write.ListAsync<int>(query, "Id", ShardParameter(), "@ShardId", None),
            };

            foreach (var call in calls)
            {
                await call().WaitAsync(Timeout);
            }

            var commands = services.Recorder.CommandsFor(query);
            commands.Count.Should().BeGreaterThanOrEqualTo(calls.Count * 2 - 3, "every family reaches both shards, except that each of the three read-first calls may cancel its loser before it is recorded");
            commands.Should().OnlyContain(c => (short)c.Parameter("@ShardId")! == c.ShardId!.Value);
        }

        #endregion

        #region Mock results

        [Fact]
        [Trait("Spec", "blk-89f5570374004b60aa02aa449168f7bc")]
        [Trait("Spec", "blk-19124a42e93745959bbd76e4ef6d1eaf")]
        public async Task AMockResultForTheQueryNameShortCircuitsEveryFanOutFamily()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            var shardSet = shardSets[ShardSetName];
            var orders = new List<Customer> { new Customer { Id = 1 } };
            var customer = new Customer { Id = 2 };
            shardSet.MockResults["GetOrders"] = orders;
            shardSet.MockResults["GetOrder"] = customer;
            shardSet.MockResults["GetIds"] = new List<int> { 9 };
            shardSet.MockResults["Update"] = null!;
            var parameters = new ParameterCollection();

            (await shardSet.ReadAll.MapListAsync<Customer>(new QueryProcedure("GetOrders"), parameters, None)).Should().BeSameAs(orders);
            (await shardSet.ReadAll.MapReaderAsync<Customer>(new QueryProcedure("GetOrders"), parameters, None)).Should().BeSameAs(orders);
            (await shardSet.ReadAll.ListAsync<int>(new QueryProcedure("GetIds"), "Id", parameters, None)).Should().Equal(9);
            (await shardSet.ReadFirst.QueryAsync<Customer>(new QueryProcedure("GetOrder"), parameters, (i, s, n, a, r, p, d, l) => throw new Exception("not run"), None)).Should().BeSameAs(customer);
            (await shardSet.ReadFirst.MapReaderAsync<Customer>(new QueryProcedure("GetOrder"), parameters, None)).Should().BeSameAs(customer);
            (await shardSet.Write.QueryAsync<Customer>(new QueryProcedure("GetOrders"), parameters, (i, s, n, a, r, p, d, l) => throw new Exception("not run"), null!, None)).Should().BeSameAs(orders);
            (await shardSet.Write.ListAsync<int>(new QueryProcedure("GetIds"), "Id", parameters, None)).Should().Equal(9);
            await shardSet.Write.RunAsync(new QueryProcedure("Update"), parameters, None);

            services.Recorder.Commands.Should().BeEmpty("no query was dispatched to any shard member");
        }

        #endregion
    }
}
