using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using static ArgentSea.Test.TestModels;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec section batch-execution: the BatchBase collection surface, the Add families of ShardBatch, DatabaseBatch and
    /// ShardSetBatch, every BatchExtensions step type, and execution order, transaction and result aggregation, all driven
    /// through the TestKit fake provider.
    /// </summary>
    public class BatchExecutionTests
    {
        private const string ShardSetName = "Set";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private static readonly CancellationToken None = CancellationToken.None;

        private static FakeShardSets BuildShardSets(FakeProviderServices services, params short[] shardIds)
            => FakeShardSets.Builder(services).AddShardSet(ShardSetName, shardIds).Build();

        /// <summary>A step that records what the batch hands it and returns a fixed result.</summary>
        private sealed class ProbeStep<TResult> : BatchStep<TResult>
        {
            private readonly TResult _result;
            private readonly Action<ProbeStep<TResult>>? _onExecute;

            public ProbeStep(TResult result, Action<ProbeStep<TResult>>? onExecute = null)
            {
                _result = result;
                _onExecute = onExecute;
            }

            public int Executions { get; private set; }
            public short ShardId { get; private set; }
            public DbConnection? Connection { get; private set; }
            public DbTransaction? Transaction { get; private set; }
            public string? ConnectionName { get; private set; }
            public IDataProviderServiceFactory? Services { get; private set; }
            public ILogger? Logger { get; private set; }
            public CancellationToken Token { get; private set; }

            protected internal override Task<TResult?> ExecuteAsync(short shardId, DbConnection connection, DbTransaction transaction, string connectionName, IDataProviderServiceFactory services, ILogger? logger, CancellationToken cancellationToken)
            {
                Executions++;
                ShardId = shardId;
                Connection = connection;
                Transaction = transaction;
                ConnectionName = connectionName;
                Services = services;
                Logger = logger;
                Token = cancellationToken;
                _onExecute?.Invoke(this);
                return Task.FromResult<TResult?>(_result);
            }
        }

        #region BatchBase collection surface

        [Fact]
        [Trait("Spec", "blk-596fbdf06ef847ea9347cc630f0a6dba")]
        [Trait("Spec", "blk-ffd930ed1489492585b0cc385b445284")]
        [Trait("Spec", "blk-a66f35b85fa24525af42af4fac1d74a7")]
        public void CountIndexerAndBothEnumerationsExposeTheSameOrderedSteps()
        {
            var a = new ProbeStep<int>(1);
            var b = new ProbeStep<int>(2);
            var batch = new ShardBatch<int>().Add(a).Add(b);

            batch.Count.Should().Be(2);
            batch[0].Should().BeSameAs(a);
            batch[1].Should().BeSameAs(b);
            ((IEnumerable<BatchStep<int>>)batch).Should().Equal(a, b);
            ((IEnumerable)batch).Cast<BatchStep<int>>().Should().Equal(a, b);
            var copied = new BatchStep<int>[3];
            batch.CopyTo(copied, 1);
            copied[0].Should().BeNull();
            copied.Skip(1).Should().Equal(a, b);
            batch.IsSynchronized.Should().BeTrue();
            batch.SyncRoot.Should().NotBeNull().And.BeSameAs(batch.SyncRoot);
        }

        [Fact]
        [Trait("Spec", "blk-cb2cc410018349d2973cd13ae363d775")]
        [Trait("Spec", "blk-08277261043440f98f45541c439e0ec6")]
        [Trait("Spec", "blk-6a5ec4418e8a4617aa83437130e823e3")]
        public void RemoveDropsAMatchingStepAndReportsWhetherOneWasRemoved()
        {
            var a = new ProbeStep<int>(1);
            var b = new ProbeStep<int>(2);
            var batch = new ShardBatch<int>().Add(a).Add(b);

            batch.Remove(a).Should().BeTrue();
            ((IEnumerable<BatchStep<int>>)batch).Should().Equal(b);
            batch.Remove(a).Should().BeFalse("A is no longer retained");
            ((IEnumerable<BatchStep<int>>)batch).Should().Equal(new[] { b }, "the sequence is unchanged");
        }

        [Fact]
        [Trait("Spec", "blk-8f7b929582d84b66b0a570778f63c0d7")]
        [Trait("Spec", "blk-9143e3e8a8b34067a848f4e5a6616e2a")]
        public void ClearRemovesEveryStep()
        {
            var batch = new DatabaseBatch<int>().Add(new ProbeStep<int>(1)).Add(new ProbeStep<int>(2));

            batch.Clear();

            batch.Count.Should().Be(0);
            ((IEnumerable<BatchStep<int>>)batch).Should().BeEmpty();
        }

        #endregion

        #region Add families

        [Fact]
        [Trait("Spec", "blk-8ea34f578b9e4d3d83a36c0e4c1f646b")]
        public async Task ShardBatchAddOverloadsAppendOneStepEachAndReturnTheSameBatch()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1);
            var probe = new ProbeStep<int>(5);
            var parameters = new ParameterCollection { new FakeDbParameter("@P", 1) };
            var batch = new ShardBatch<int>();

            var returned = batch.Add(probe).Add(new QueryProcedure("wt.NoParams")).Add(new QueryProcedure("wt.WithParams"), parameters);

            returned.Should().BeSameAs(batch);
            batch.Count.Should().Be(3);
            batch[0].Should().BeSameAs(probe, "the supplied step is preserved");
            var result = await shardSets[ShardSetName][1].Write.RunAsync(batch, None).WaitAsync(Timeout);
            result.Should().Be(5, "the query steps return default and do not overwrite the probe's result");
            var commands = services.Recorder.Commands;
            commands.Select(c => c.CommandText).Should().Equal("wt.NoParams", "wt.WithParams");
            commands[0].Parameters.Should().BeEmpty();
            commands[1].Parameter("@P").Should().Be(1);
            commands.Should().OnlyContain(c => c.Kind == FakeExecutionKind.NonQuery);
        }

        [Fact]
        [Trait("Spec", "blk-6be350c683a740a5a8d31b9beeebb723")]
        [Trait("Spec", "blk-362df8c527ec44039e7721b973e0c961")]
        [Trait("Spec", "blk-9eef5f8bfcd34f60bcc36a36b932e50d")]
        public async Task DatabaseBatchAddOverloadsAppendOneStepEachAndQueryStepsYieldDefault()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            var query = new QueryProcedure("wt.Save", new[] { "@P" });
            var parameters = new ParameterCollection { new FakeDbParameter("@P", 7), new FakeDbParameter("@Ignored", 8) };
            var probe = new ProbeStep<int>(0);
            var batch = new DatabaseBatch<int>();

            var returned = batch.Add(query, parameters).Add(new QueryProcedure("wt.Plain")).Add(probe);

            returned.Should().BeSameAs(batch);
            batch.Count.Should().Be(3);
            var result = await databases["MainDb"].Write.RunAsync(batch, None).WaitAsync(Timeout);
            result.Should().Be(default(int), "no step produced a value");
            var commands = services.Recorder.Commands;
            commands.Select(c => c.CommandText).Should().Equal("wt.Save", "wt.Plain");
            commands.Should().OnlyContain(c => c.Kind == FakeExecutionKind.NonQuery && c.CommandType == CommandType.StoredProcedure);
            commands[0].ParameterValues.Keys.Should().Equal("@P");
            commands[0].Parameter("@P").Should().Be(7, "only the parameters the query names are bound");
            commands[1].Parameters.Should().BeEmpty("the query-only overload uses an empty collection");
            probe.Executions.Should().Be(1);
        }

        [Fact]
        [Trait("Spec", "blk-246e1010eeab4ab1ad160fd6fbde5e58")]
        [Trait("Spec", "blk-9b9cbd31d7b04dfcb525398f376bd809")]
        [Trait("Spec", "blk-27c349eefc824103bdeecb0ec7ef9b0e")]
        public async Task ShardSetBatchAddOverloadsChainAndExecuteInAdditionOrderOnTheShard()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 7);
            var trace = new List<string>();
            var first = new ProbeStep<object>(null!, p => trace.Add("first"));
            var second = new ProbeStep<object>(null!, p => trace.Add("second"));
            var batch = new ShardSetBatch();

            var returned = batch.Add(first).Add(second).Add(new QueryProcedure("wt.Third")).Add(new QueryProcedure("wt.Fourth"), new ParameterCollection { new FakeDbParameter("@P", 4) });

            returned.Should().BeSameAs(batch);
            batch.Count.Should().Be(4);
            await shardSets[ShardSetName].Write.RunAsync(batch, None).WaitAsync(Timeout);
            trace.Should().Equal("first", "second");
            first.ShardId.Should().Be(7);
            second.ShardId.Should().Be(7);
            services.Recorder.Commands.Select(c => c.CommandText).Should().Equal("wt.Third", "wt.Fourth");
            services.Recorder.Commands[1].Parameter("@P").Should().Be(4);
        }

        [Fact]
        [Trait("Spec", "blk-af00d84f6a2948778508fb31eb83ca41")]
        [Trait("Spec", "blk-17f24ceb3de34f4a9db4e05afaa476f5")]
        [Trait("Spec", "blk-080ea93c36384501ad2677f13ffc95b0")]
        public void BatchExtensionsAddReturnsTheSameBatchWithOneStepPerCall()
        {
            var query = new QueryProcedure("wt.Insert");
            var parameters = new ParameterCollection();
            QueryResultModelHandler<object?, Customer> handler = (i, s, n, a, r, p, d, l) => null;

            var keyBatch = new ShardBatch<ShardKey<int>>();
            keyBatch.Add<int>(query, parameters, "ShardId", "RecordId").Should().BeSameAs(keyBatch);
            keyBatch.Add<int>(query, "ShardId", "RecordId").Add<int>(query, parameters, "RecordId").Add<int>(query, "RecordId").Should().BeSameAs(keyBatch);
            keyBatch.Count.Should().Be(4);

            var childBatch = new ShardBatch<ShardKey<int, int>>();
            childBatch.Add<int, int>(query, parameters, "ShardId", "RecordId", "ChildId").Add<int, int>(query, "ShardId", "RecordId", "ChildId")
                .Add<int, int>(query, parameters, "RecordId", "ChildId").Add<int, int>(query, "RecordId", "ChildId").Should().BeSameAs(childBatch);
            childBatch.Count.Should().Be(4);

            var modelBatch = new ShardBatch<Customer>();
            modelBatch.Add(query, parameters, handler).Add(query, handler).Add<string, Customer>(query, parameters, (i, s, n, a, r, p, d, l) => null, "arg").Add<string, Customer>(query, (i, s, n, a, r, p, d, l) => null, "arg").Should().BeSameAs(modelBatch);
            modelBatch.Count.Should().Be(4);

            var keyListBatch = new ShardBatch<IList<ShardKey<int>>>();
            keyListBatch.Add<int>(query, parameters, "ShardId", "RecordId").Add<int>(query, "ShardId", "RecordId").Add<int>(query, parameters, "RecordId").Add<int>(query, "RecordId").Should().BeSameAs(keyListBatch);
            keyListBatch.Count.Should().Be(4);

            var childListBatch = new ShardBatch<IList<ShardKey<int, int>>>();
            childListBatch.Add<int, int>(query, parameters, "ShardId", "RecordId", "ChildId").Add<int, int>(query, "ShardId", "RecordId", "ChildId")
                .Add<int, int>(query, parameters, "RecordId", "ChildId").Add<int, int>(query, "RecordId", "ChildId").Should().BeSameAs(childListBatch);
            childListBatch.Count.Should().Be(4);

            var recordBatch = new ShardBatch<int>();
            recordBatch.Add<int>(query, parameters, "RecordId").Add<int>(query, "RecordId").Should().BeSameAs(recordBatch);
            recordBatch.Count.Should().Be(2);

            var listBatch = new ShardBatch<IList<int>>();
            listBatch.Add<int>(query, parameters, "RecordId").Should().BeSameAs(listBatch);
            listBatch.Count.Should().Be(1);

            var dbModelBatch = new DatabaseBatch<Customer>();
            dbModelBatch.Add(query, parameters, handler).Add(query, handler).Add<string, Customer>(query, parameters, (i, s, n, a, r, p, d, l) => null, "arg").Add<string, Customer>(query, (i, s, n, a, r, p, d, l) => null, "arg").Should().BeSameAs(dbModelBatch);
            dbModelBatch.Count.Should().Be(4);

            var dbRecordBatch = new DatabaseBatch<int>();
            dbRecordBatch.Add<int>(query, parameters, "RecordId").Add<int>(query, "RecordId").Should().BeSameAs(dbRecordBatch);
            dbRecordBatch.Count.Should().Be(2);

            var dbListBatch = new DatabaseBatch<IList<int>>();
            dbListBatch.Add<int>(query, parameters, "RecordId").Should().BeSameAs(dbListBatch);
            dbListBatch.Count.Should().Be(1);
        }

        // The List<> receiver overloads used to cast the batch to the non-generic IList, which no batch implements, so every
        // one of them threw InvalidCastException instead of adding its step. They now add List<>-typed steps.
        [Fact]
        [Trait("Spec", "blk-080ea93c36384501ad2677f13ffc95b0")]
        [Trait("Spec", "blk-af00d84f6a2948778508fb31eb83ca41")]
        public void ListReceiverOverloadsAddAStepAndReturnTheBatch()
        {
            var query = new QueryProcedure("wt.Insert");
            var parameters = new ParameterCollection();

            var keyListBatch = new ShardBatch<List<ShardKey<int>>>();
            keyListBatch.Add<int>(query, parameters, "ShardId", "RecordId").Should().BeSameAs(keyListBatch);
            keyListBatch.Add<int>(query, "ShardId", "RecordId").Add<int>(query, parameters, "RecordId").Add<int>(query, "RecordId").Should().BeSameAs(keyListBatch);
            keyListBatch.Count.Should().Be(4);

            var childListBatch = new ShardBatch<List<ShardKey<int, int>>>();
            childListBatch.Add<int, int>(query, parameters, "ShardId", "RecordId", "ChildId").Add<int, int>(query, "ShardId", "RecordId", "ChildId")
                .Add<int, int>(query, parameters, "RecordId", "ChildId").Add<int, int>(query, "RecordId", "ChildId").Should().BeSameAs(childListBatch);
            childListBatch.Count.Should().Be(4);

            var listBatch = new ShardBatch<List<int>>();
            listBatch.Add<int>(query, parameters, "RecordId").Should().BeSameAs(listBatch);
            listBatch.Count.Should().Be(1);

            var dbListBatch = new DatabaseBatch<List<int>>();
            dbListBatch.Add<int>(query, parameters, "RecordId").Should().BeSameAs(dbListBatch);
            dbListBatch.Count.Should().Be(1);
        }

        #endregion

        #region Execution order, transaction and step wiring

        [Fact]
        [Trait("Spec", "blk-bbbe2422fa1544c7b49ba3db91fe44ef")]
        [Trait("Spec", "blk-e8aa2f5d048f4f1da98bdde13cca6bd2")]
        public async Task ShardBatchRunsStepsInOrderInsideOneCommittedTransaction()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1);
            var batch = new ShardBatch<object>()
                .Add(new QueryProcedure("wt.Step1"))
                .Add(new QueryProcedure("wt.Step2"))
                .Add(new QueryProcedure("wt.Step3"));

            await shardSets[ShardSetName][1].Write.RunAsync(batch, None).WaitAsync(Timeout);

            var commands = services.Recorder.Commands;
            commands.Select(c => c.CommandText).Should().Equal("wt.Step1", "wt.Step2", "wt.Step3");
            commands.Select(c => c.TransactionId).Distinct().Should().ContainSingle().Which.Should().NotBeNull("every step runs in the batch transaction");
            services.Recorder.ConnectionsOpened.Should().ContainSingle("the batch uses one connection");
            var events = services.Recorder.TransactionEvents;
            events.Select(e => e.Kind).Should().Equal(TransactionEventKind.Begin, TransactionEventKind.Commit);
            events[0].Sequence.Should().BeLessThan(commands[0].Sequence, "the transaction begins before the first step");
            events[1].Sequence.Should().BeGreaterThan(commands[2].Sequence, "the transaction commits after the last step");
        }

        [Fact]
        [Trait("Spec", "blk-bbbe2422fa1544c7b49ba3db91fe44ef")]
        public async Task ShardBatchRollsBackAndSkipsLaterStepsWhenAStepThrows()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1);
            services.Script.ForQuery("wt.Step2").Throws(new FakeDbException("step 2 failed"));
            var batch = new ShardBatch<object>()
                .Add(new QueryProcedure("wt.Step1"))
                .Add(new QueryProcedure("wt.Step2"))
                .Add(new QueryProcedure("wt.Step3"));

            Func<Task> act = () => shardSets[ShardSetName][1].Write.RunAsync(batch, None).WaitAsync(Timeout);

            (await act.Should().ThrowAsync<FakeDbException>()).WithMessage("step 2 failed");
            services.Recorder.Commands.Select(c => c.CommandText).Should().Equal("wt.Step1", "wt.Step2");
            var events = services.Recorder.TransactionEvents;
            events.Select(e => e.Kind).Should().Equal(TransactionEventKind.Begin, TransactionEventKind.Rollback);
            events.Should().NotContain(e => e.Kind == TransactionEventKind.Commit);
        }

        [Fact]
        [Trait("Spec", "blk-bbbe2422fa1544c7b49ba3db91fe44ef")]
        public async Task DatabaseBatchRollsBackAndSkipsLaterStepsWhenAStepThrows()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            services.Script.ForQuery("wt.Step1").Throws(new FakeDbException("step 1 failed"));
            var later = new ProbeStep<int>(9);
            var batch = new DatabaseBatch<int>().Add(new QueryProcedure("wt.Step1")).Add(later);

            Func<Task> act = () => databases["MainDb"].Write.RunAsync(batch, None).WaitAsync(Timeout);

            await act.Should().ThrowAsync<FakeDbException>();
            later.Executions.Should().Be(0, "nothing after the failing step runs");
            services.Recorder.TransactionEvents.Select(e => e.Kind).Should().Equal(TransactionEventKind.Begin, TransactionEventKind.Rollback);
        }

        [Fact]
        [Trait("Spec", "blk-bbbe2422fa1544c7b49ba3db91fe44ef")]
        [Trait("Spec", "blk-e8aa2f5d048f4f1da98bdde13cca6bd2")]
        public async Task EveryStepReceivesTheSameConnectionTransactionServicesLoggerAndTokenAndTheShardsId()
        {
            var services = new FakeProviderServices();
            var logger = new RecordingLogger<ShardSetsBase<FakeShardSetsOptions>>();
            var shardSets = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 12).WithLogger(logger).Build();
            using var cts = new CancellationTokenSource();
            var first = new ProbeStep<int>(0);
            var second = new ProbeStep<int>(0);
            var batch = new ShardBatch<int>().Add(first).Add(second);

            await shardSets[ShardSetName][12].Write.RunAsync(batch, cts.Token).WaitAsync(Timeout);

            first.ShardId.Should().Be(12);
            second.ShardId.Should().Be(12);
            second.Connection.Should().BeSameAs(first.Connection).And.BeOfType<FakeDbConnection>();
            second.Transaction.Should().BeSameAs(first.Transaction).And.BeOfType<FakeDbTransaction>();
            ((FakeDbTransaction)first.Transaction!).IsCommitted.Should().BeTrue();
            second.ConnectionName.Should().Be(first.ConnectionName).And.Contain("shard number 12");
            first.Services.Should().BeSameAs(services);
            first.Logger.Should().BeSameAs(logger);
            first.Token.Should().Be(cts.Token);
            second.Token.Should().Be(cts.Token);
        }

        [Fact]
        [Trait("Spec", "blk-bbbe2422fa1544c7b49ba3db91fe44ef")]
        [Trait("Spec", "blk-db9c113ffe1649229c350b59e6d29460")]
        public async Task DatabaseBatchStepsAlwaysReceiveShardIdZero()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            var probe = new ProbeStep<int>(0);

            await databases["MainDb"].Write.RunAsync(new DatabaseBatch<int>().Add(probe), None).WaitAsync(Timeout);

            probe.ShardId.Should().Be(0);
            probe.ConnectionName.Should().Be(FakeConnectionInfo.ForDatabase("MainDb", FakeConnectionAccess.Write), "the database's connection description");
        }

        [Fact]
        [Trait("Spec", "blk-5d12498b87f14818a352c49626be7ac8")]
        [Trait("Spec", "blk-004900fedf764e0f9635cbb01a38f540")]
        [Trait("Spec", "blk-296b51f44f474f97b39436e356cf9e82")]
        public async Task ShardBatchReturnsTheMostRecentNonDefaultStepResult()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 12);
            var trace = new List<int>();
            var batch = new ShardBatch<int>()
                .Add(new ProbeStep<int>(0, p => trace.Add(0)))
                .Add(new ProbeStep<int>(7, p => trace.Add(7)))
                .Add(new ProbeStep<int>(0, p => trace.Add(0)));

            var result = await shardSets[ShardSetName][12].Write.RunAsync(batch, None).WaitAsync(Timeout);

            result.Should().Be(7, "a later default result never overwrites the retained value");
            trace.Should().Equal(new[] { 0, 7, 0 }, "all three steps executed in order");
        }

        [Fact]
        [Trait("Spec", "blk-5d12498b87f14818a352c49626be7ac8")]
        public async Task ShardBatchReturnsDefaultWhenNoStepProducesAValueAndTheLatestValueOtherwise()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1);
            var connection = shardSets[ShardSetName][1].Write;

            var none = await connection.RunAsync(new ShardBatch<string>().Add(new ProbeStep<string>(null!)).Add(new QueryProcedure("wt.Step")), None).WaitAsync(Timeout);
            var latest = await connection.RunAsync(new ShardBatch<string>().Add(new ProbeStep<string>("first")).Add(new ProbeStep<string>("second")), None).WaitAsync(Timeout);

            none.Should().BeNull();
            latest.Should().Be("second");
        }

        [Fact]
        [Trait("Spec", "blk-c986c1ae849b478b8708afd317cb78f5")]
        public async Task ShardBatchReturnsTheLastNonDefaultStepResultWithTheExecutingShardsId()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1);
            services.Script.ForQuery("wt.Insert").Returns(new[] { "RecordId" }, new object[] { 42 });
            var batch = new ShardBatch<ShardKey<int>>()
                .Add(new QueryProcedure("wt.Prepare"))
                .Add(new QueryProcedure("wt.Insert"), "RecordId")
                .Add(new QueryProcedure("wt.Cleanup"));

            var key = await shardSets[ShardSetName][1].Write.RunAsync(batch, None).WaitAsync(Timeout);

            key.ShardId.Should().Be(1, "the key's shard id comes from the executing shard, not the row");
            key.RecordId.Should().Be(42);
            services.Recorder.Commands.Select(c => c.CommandText).Should().Equal("wt.Prepare", "wt.Insert", "wt.Cleanup");
        }

        [Fact]
        [Trait("Spec", "blk-af5aac7bf04f49e1a31dc2d169d73e80")]
        public async Task AQueryStepObservesAnAlreadyCancelledTokenBeforeCreatingItsCommand()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            var batch = new DatabaseBatch<int>().Add(new QueryProcedure("wt.Step1"));
            var cancelled = new CancellationToken(canceled: true);

            Func<Task> act = () => databases["MainDb"].Write.RunAsync(batch, cancelled);

            await act.Should().ThrowAsync<OperationCanceledException>();
            services.Recorder.Commands.Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-af5aac7bf04f49e1a31dc2d169d73e80")]
        public async Task AQueryStepPassesTheTokenToItsCommandSoMidCommandCancellationIsObserved()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            using var cts = new CancellationTokenSource();
            services.Script.ForQuery("wt.Slow").OnExecuting((_, __) => { cts.Cancel(); return Task.CompletedTask; }).WaitsUntilCancelled();
            var later = new ProbeStep<int>(1);
            var batch = new DatabaseBatch<int>().Add(new QueryProcedure("wt.Slow")).Add(later);

            Func<Task> act = () => databases["MainDb"].Write.RunAsync(batch, cts.Token).WaitAsync(Timeout);

            await act.Should().ThrowAsync<OperationCanceledException>();
            services.Recorder.Commands.Should().ContainSingle().Which.CancellationToken.IsCancellationRequested.Should().BeTrue();
            later.Executions.Should().Be(0);
            services.Recorder.TransactionEvents.Select(e => e.Kind).Should().Equal(TransactionEventKind.Begin, TransactionEventKind.Rollback);
        }

        #endregion

        #region BatchExtensions step types

        [Fact]
        [Trait("Spec", "blk-db9c113ffe1649229c350b59e6d29460")]
        [Trait("Spec", "blk-8dd9062f12b948279d80d61a3b5b6c13")]
        public async Task KeyStepWithoutAShardColumnUsesTheExecutingShardsId()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 7);
            var query = new QueryProcedure("wt.Insert");
            services.Script.ForQuery(query).Returns(new[] { "RecordId" }, new object[] { 42 });

            var key = await shardSets[ShardSetName][7].Write.RunAsync(new ShardBatch<ShardKey<int>>().Add<int>(query, "RecordId"), None).WaitAsync(Timeout);

            key.Should().Be(new ShardKey<int>(7, 42));
            services.Recorder.CommandFor(query, 7)!.Behavior.Should().Be(CommandBehavior.SingleRow);
        }

        [Fact]
        [Trait("Spec", "blk-080ea93c36384501ad2677f13ffc95b0")]
        public async Task KeyStepWithAShardColumnReadsTheShardIdFromTheRow()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 7);
            var query = new QueryProcedure("wt.Insert");
            services.Script.ForQuery(query).Returns(new[] { "ShardId", "RecordId" }, new object[] { (short)3, 42 });
            var parameters = new ParameterCollection { new FakeDbParameter("@P", 1) };

            var key = await shardSets[ShardSetName][7].Write.RunAsync(new ShardBatch<ShardKey<int>>().Add<int>(query, parameters, "ShardId", "RecordId"), None).WaitAsync(Timeout);

            key.Should().Be(new ShardKey<int>(3, 42));
            services.Recorder.CommandFor(query, 7)!.Parameter("@P").Should().Be(1);
        }

        [Fact]
        [Trait("Spec", "blk-db9c113ffe1649229c350b59e6d29460")]
        public async Task ChildKeyStepUsesTheExecutingShardOrTheShardColumn()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 7);
            var query = new QueryProcedure("wt.InsertChild");
            services.Script.ForQuery(query).Returns(new[] { "ShardId", "RecordId", "ChildId" }, new object[] { (short)3, 42, 5 });
            var connection = shardSets[ShardSetName][7].Write;

            var fromShard = await connection.RunAsync(new ShardBatch<ShardKey<int, int>>().Add<int, int>(query, "RecordId", "ChildId"), None).WaitAsync(Timeout);
            var fromColumn = await connection.RunAsync(new ShardBatch<ShardKey<int, int>>().Add<int, int>(query, "ShardId", "RecordId", "ChildId"), None).WaitAsync(Timeout);

            fromShard.Should().Be(new ShardKey<int, int>(7, 42, 5));
            fromColumn.Should().Be(new ShardKey<int, int>(3, 42, 5));
        }

        [Fact]
        [Trait("Spec", "blk-296b51f44f474f97b39436e356cf9e82")]
        [Trait("Spec", "blk-712db975c8854b41a0cb2679fab8b8bf")]
        public async Task SingleResultStepsReturnTheirEmptyOrDefaultValueWhenNoRowMatches()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 7);
            var query = new QueryProcedure("wt.Nothing");
            services.Script.ForQuery(query).ReturnsNoRows("ShardId", "RecordId", "ChildId");
            var connection = shardSets[ShardSetName][7].Write;

            var key = await connection.RunAsync(new ShardBatch<ShardKey<int>>().Add<int>(query, "RecordId"), None).WaitAsync(Timeout);
            var childKey = await connection.RunAsync(new ShardBatch<ShardKey<int, int>>().Add<int, int>(query, "RecordId", "ChildId"), None).WaitAsync(Timeout);
            var record = await connection.RunAsync(new ShardBatch<int>().Add<int>(query, "RecordId"), None).WaitAsync(Timeout);
            var text = await connection.RunAsync(new ShardBatch<string>().Add<string>(query, "RecordId"), None).WaitAsync(Timeout);

            key.Should().Be(ShardKey<int>.Empty);
            key.IsEmpty.Should().BeTrue();
            childKey.Should().Be(ShardKey<int, int>.Empty);
            record.Should().Be(default(int));
            text.Should().BeNull();
        }

        [Fact]
        [Trait("Spec", "blk-296b51f44f474f97b39436e356cf9e82")]
        public async Task AnEmptyStepResultNeverReplacesAnEarlierBatchResult()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 7);
            var insert = new QueryProcedure("wt.Insert");
            var nothing = new QueryProcedure("wt.Nothing");
            services.Script.ForQuery(insert).Returns(new[] { "RecordId" }, new object[] { 42 });
            services.Script.ForQuery(nothing).ReturnsNoRows("RecordId");
            var connection = shardSets[ShardSetName][7].Write;

            var key = await connection.RunAsync(new ShardBatch<ShardKey<int>>().Add<int>(insert, "RecordId").Add<int>(nothing, "RecordId"), None).WaitAsync(Timeout);
            var record = await connection.RunAsync(new ShardBatch<int>().Add<int>(insert, "RecordId").Add<int>(nothing, "RecordId"), None).WaitAsync(Timeout);

            key.Should().Be(new ShardKey<int>(7, 42), "ShardKey.Empty equals default(ShardKey) and so is not retained");
            record.Should().Be(42);
        }

        [Fact]
        [Trait("Spec", "blk-08408c4296454fa5b418f9519f5e9ad5")]
        [Trait("Spec", "blk-3bec8680c2984d1097abda3284c6dd98")]
        public async Task ListStepsReturnEveryRowInReaderOrder()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 7);
            var query = new QueryProcedure("wt.InsertMany");
            services.Script.ForQuery(query).Returns(new[] { "ShardId", "RecordId", "ChildId" }, new object[] { (short)1, 10, 100 }, new object[] { (short)2, 20, 200 }, new object[] { (short)3, 30, 300 });
            var connection = shardSets[ShardSetName][7].Write;

            var keys = await connection.RunAsync(new ShardBatch<IList<ShardKey<int>>>().Add<int>(query, "RecordId"), None).WaitAsync(Timeout);
            var keysFromColumn = await connection.RunAsync(new ShardBatch<IList<ShardKey<int>>>().Add<int>(query, "ShardId", "RecordId"), None).WaitAsync(Timeout);
            var childKeys = await connection.RunAsync(new ShardBatch<IList<ShardKey<int, int>>>().Add<int, int>(query, "RecordId", "ChildId"), None).WaitAsync(Timeout);
            var childKeysFromColumn = await connection.RunAsync(new ShardBatch<IList<ShardKey<int, int>>>().Add<int, int>(query, "ShardId", "RecordId", "ChildId"), None).WaitAsync(Timeout);
            var values = await connection.RunAsync(new ShardBatch<IList<int>>().Add<int>(query, new ParameterCollection(), "RecordId"), None).WaitAsync(Timeout);

            keys.Should().Equal(new ShardKey<int>(7, 10), new ShardKey<int>(7, 20), new ShardKey<int>(7, 30));
            keysFromColumn.Should().Equal(new ShardKey<int>(1, 10), new ShardKey<int>(2, 20), new ShardKey<int>(3, 30));
            childKeys.Should().Equal(new ShardKey<int, int>(7, 10, 100), new ShardKey<int, int>(7, 20, 200), new ShardKey<int, int>(7, 30, 300));
            childKeysFromColumn.Should().Equal(new ShardKey<int, int>(1, 10, 100), new ShardKey<int, int>(2, 20, 200), new ShardKey<int, int>(3, 30, 300));
            values.Should().Equal(10, 20, 30);
            services.Recorder.Commands.Should().OnlyContain(c => c.Behavior == CommandBehavior.Default);
        }

        [Fact]
        [Trait("Spec", "blk-080ea93c36384501ad2677f13ffc95b0")]
        [Trait("Spec", "blk-08408c4296454fa5b418f9519f5e9ad5")]
        public async Task ListReceiverStepsExecuteAndReturnTypedLists()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 7);
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            var query = new QueryProcedure("wt.InsertMany");
            services.Script.ForQuery(query).Returns(new[] { "ShardId", "RecordId", "ChildId" }, new object[] { (short)1, 10, 100 }, new object[] { (short)2, 20, 200 });
            var connection = shardSets[ShardSetName][7].Write;

            List<ShardKey<int>> keys = await connection.RunAsync(new ShardBatch<List<ShardKey<int>>>().Add<int>(query, "RecordId"), None).WaitAsync(Timeout);
            List<ShardKey<int>> keysFromColumn = await connection.RunAsync(new ShardBatch<List<ShardKey<int>>>().Add<int>(query, new ParameterCollection(), "ShardId", "RecordId"), None).WaitAsync(Timeout);
            List<ShardKey<int, int>> childKeys = await connection.RunAsync(new ShardBatch<List<ShardKey<int, int>>>().Add<int, int>(query, new ParameterCollection(), "RecordId", "ChildId"), None).WaitAsync(Timeout);
            List<ShardKey<int, int>> childKeysFromColumn = await connection.RunAsync(new ShardBatch<List<ShardKey<int, int>>>().Add<int, int>(query, "ShardId", "RecordId", "ChildId"), None).WaitAsync(Timeout);
            List<int> values = await connection.RunAsync(new ShardBatch<List<int>>().Add<int>(query, new ParameterCollection(), "RecordId"), None).WaitAsync(Timeout);
            List<int> dbValues = await databases["MainDb"].Write.RunAsync(new DatabaseBatch<List<int>>().Add<int>(query, new ParameterCollection(), "ChildId"), None).WaitAsync(Timeout);

            keys.Should().Equal(new ShardKey<int>(7, 10), new ShardKey<int>(7, 20));
            keysFromColumn.Should().Equal(new ShardKey<int>(1, 10), new ShardKey<int>(2, 20));
            childKeys.Should().Equal(new ShardKey<int, int>(7, 10, 100), new ShardKey<int, int>(7, 20, 200));
            childKeysFromColumn.Should().Equal(new ShardKey<int, int>(1, 10, 100), new ShardKey<int, int>(2, 20, 200));
            values.Should().Equal(10, 20);
            dbValues.Should().Equal(100, 200);
        }

        [Fact]
        [Trait("Spec", "blk-08408c4296454fa5b418f9519f5e9ad5")]
        public async Task ListStepsReturnAnEmptyListWhenNoRowMatches()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            var query = new QueryProcedure("wt.Nothing");
            services.Script.ForQuery(query).ReturnsNoRows("RecordId");

            var values = await databases["MainDb"].Write.RunAsync(new DatabaseBatch<IList<int>>().Add<int>(query, new ParameterCollection(), "RecordId"), None).WaitAsync(Timeout);

            values.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-ac586347095e478c852a0087c94aae1e")]
        [Trait("Spec", "blk-23d6d3114b7e4f7f9fb3bff2a54d024e")]
        public async Task ModelStepInvokesTheHandlerExactlyOnceWithTheExecutionContextAndReturnsItsValue()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 7);
            var query = new QueryProcedure("wt.Model");
            services.Script.ForQuery(query).Returns(new[] { "Id", "Name" }, new object[] { 1, "a" }, new object[] { 2, "b" }).WithOutput("@Out", 5);
            var parameters = new ParameterCollection { new FakeDbParameter("@Out", null, ParameterDirection.Output) };
            var calls = 0;
            (Customer? accumulator, short shardId, string sql, string arg, bool readerOpen, int outValue, string connection, bool hasLogger) seen = default;
            var produced = new Customer { Id = 99 };
            QueryResultModelHandler<string, Customer> handler = (instance, shardId, sprocName, arg, rdr, prms, description, logger) =>
            {
                calls++;
                seen = (instance, shardId, sprocName, arg, !rdr.IsClosed, (int)prms["@Out"].Value!, description, !(logger is null));
                return produced;
            };

            var result = await shardSets[ShardSetName][7].Write.RunAsync(new ShardBatch<Customer>().Add(query, parameters, handler, "the-arg"), None).WaitAsync(Timeout);

            result.Should().BeSameAs(produced, "the handler's value is the step's result");
            calls.Should().Be(1, "the handler runs once regardless of the row count");
            seen.accumulator.Should().NotBeNull().And.NotBeSameAs(produced, "the step hands the handler a fresh TModel instance to populate");
            seen.shardId.Should().Be(7);
            seen.sql.Should().Be("wt.Model", "the query's SQL text");
            seen.arg.Should().Be("the-arg");
            seen.readerOpen.Should().BeTrue();
            seen.outValue.Should().Be(5, "the command's own parameter collection is passed");
            seen.connection.Should().Contain("shard number 7");
            seen.hasLogger.Should().BeTrue();
            services.Recorder.CommandFor(query, 7)!.Behavior.Should().Be(CommandBehavior.Default);
        }

        [Fact]
        [Trait("Spec", "blk-ac586347095e478c852a0087c94aae1e")]
        [Trait("Spec", "blk-e8aa2f5d048f4f1da98bdde13cca6bd2")]
        public async Task ModelStepWithoutAnArgumentPassesNullAndReturnsWhateverTheHandlerDecides()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            var query = new QueryProcedure("wt.Model");
            services.Script.ForQuery(query).ReturnsNoRows("Id");
            object? seenArg = "unset";

            var result = await databases["MainDb"].Write.RunAsync(new DatabaseBatch<Customer>().Add(query, (instance, shardId, sprocName, arg, rdr, prms, description, logger) => { seenArg = arg; return rdr.Read() ? new Customer() : null; }), None).WaitAsync(Timeout);

            result.Should().BeNull("no sentinel is imposed on a handler-driven step");
            seenArg.Should().BeNull();
        }

        [Fact]
        [Trait("Spec", "blk-296b51f44f474f97b39436e356cf9e82")]
        public async Task RecordStepsReturnTheColumnValueOfTheFirstRow()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").Build();
            var query = new QueryProcedure("wt.Insert");
            services.Script.ForQuery(query).Returns(new[] { "Name" }, new object[] { "first" }, new object[] { "second" });
            var parameters = new ParameterCollection { new FakeDbParameter("@P", 1) };

            var name = await databases["MainDb"].Write.RunAsync(new DatabaseBatch<string>().Add<string>(query, parameters, "Name"), None).WaitAsync(Timeout);

            name.Should().Be("first");
            var command = services.Recorder.Commands.Single();
            command.Behavior.Should().Be(CommandBehavior.SingleRow);
            command.Parameter("@P").Should().Be(1);
        }

        #endregion

        #region ShardSetBatch

        [Fact]
        [Trait("Spec", "blk-9b9cbd31d7b04dfcb525398f376bd809")]
        public async Task ShardSetBatchRunsOnEveryShardEachInItsOwnTransaction()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            var batch = new ShardSetBatch().Add(new QueryProcedure("wt.Step1")).Add(new QueryProcedure("wt.Step2"));

            await shardSets[ShardSetName].Write.RunAsync(batch, None).WaitAsync(Timeout);

            foreach (var shardId in new short[] { 1, 2 })
            {
                var commands = services.Recorder.CommandsForShard(shardId);
                commands.Select(c => c.CommandText).Should().Equal("wt.Step1", "wt.Step2");
                commands.Select(c => c.TransactionId).Distinct().Should().ContainSingle();
            }
            services.Recorder.TransactionEvents.Where(e => e.Kind == TransactionEventKind.Commit).Should().HaveCount(2);
        }

        [Fact]
        [Trait("Spec", "blk-9b9cbd31d7b04dfcb525398f376bd809")]
        public async Task ShardSetBatchRollsBackOnlyTheShardWhoseStepFailed()
        {
            var services = new FakeProviderServices();
            var shardSets = BuildShardSets(services, 1, 2);
            services.Script.ForQuery("wt.Step2", 2).Throws(new FakeDbException("shard 2 step 2 failed"));
            var batch = new ShardSetBatch().Add(new QueryProcedure("wt.Step1")).Add(new QueryProcedure("wt.Step2"));

            Func<Task> act = () => shardSets[ShardSetName].Write.RunAsync(batch, None).WaitAsync(Timeout);

            (await act.Should().ThrowAsync<FakeDbException>()).WithMessage("shard 2 step 2 failed");
            services.Recorder.TransactionEvents.Where(e => e.ShardId == 1).Select(e => e.Kind).Should().Equal(TransactionEventKind.Begin, TransactionEventKind.Commit);
            services.Recorder.TransactionEvents.Where(e => e.ShardId == 2).Select(e => e.Kind).Should().Equal(TransactionEventKind.Begin, TransactionEventKind.Rollback);
        }

        [Fact]
        [Trait("Spec", "blk-bbbe2422fa1544c7b49ba3db91fe44ef")]
        public async Task ShardBatchCompletesWithANullLogger()
        {
            var services = new FakeProviderServices();
            var shardSets = new FakeShardSets(ShardSetOptions(1), services, null, null);
            var batch = new ShardBatch<object>().Add(new QueryProcedure("wt.Step1")).Add(new QueryProcedure("wt.Step2"));

            await shardSets[ShardSetName][1].Write.RunAsync(batch, None).WaitAsync(Timeout);

            services.Recorder.Commands.Select(c => c.CommandText).Should().Equal("wt.Step1", "wt.Step2");
        }

        [Fact]
        [Trait("Spec", "blk-bbbe2422fa1544c7b49ba3db91fe44ef")]
        public async Task DatabaseBatchCompletesWithANullLogger()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("MainDb").WithoutLogger().Build();
            var batch = new DatabaseBatch<object>().Add(new QueryProcedure("wt.Step1")).Add(new QueryProcedure("wt.Step2"));

            await databases["MainDb"].Write.RunAsync(batch, None).WaitAsync(Timeout);

            services.Recorder.Commands.Select(c => c.CommandText).Should().Equal("wt.Step1", "wt.Step2");
        }

        [Fact]
        [Trait("Spec", "blk-bbbe2422fa1544c7b49ba3db91fe44ef")]
        public async Task ShardSetBatchCompletesWithANullLogger()
        {
            var services = new FakeProviderServices();
            var shardSets = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 1, 2).WithoutLogger().Build();
            var batch = new ShardSetBatch().Add(new QueryProcedure("wt.Step1")).Add(new QueryProcedure("wt.Step2"));

            await shardSets[ShardSetName].Write.RunAsync(batch, None).WaitAsync(Timeout);

            services.Recorder.CommandsForShard(1).Select(c => c.CommandText).Should().Equal("wt.Step1", "wt.Step2");
            services.Recorder.CommandsForShard(2).Select(c => c.CommandText).Should().Equal("wt.Step1", "wt.Step2");
        }

        [Fact]
        [Trait("Spec", "blk-bbbe2422fa1544c7b49ba3db91fe44ef")]
        public async Task ShardSetBatchLogsOneStartAndOneEndPerStep()
        {
            var services = new FakeProviderServices();
            var logger = new RecordingLogger<ShardSetsBase<FakeShardSetsOptions>>();
            var shardSets = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 1).WithLogger(logger).Build();
            var batch = new ShardSetBatch()
                .Add(new QueryProcedure("wt.Step1"))
                .Add(new QueryProcedure("wt.Step2"))
                .Add(new QueryProcedure("wt.Step3"));

            await shardSets[ShardSetName].Write.RunAsync(batch, None).WaitAsync(Timeout);

            logger.EntriesFor(LoggingExtensions.EventIdentifier.BatchStep).Select(e => e.Name).Should().Equal(
                "BatchStepStart", "BatchStepEnd",
                "BatchStepStart", "BatchStepEnd",
                "BatchStepStart", "BatchStepEnd");
        }

        private static FakeShardSetsOptions ShardSetOptions(params short[] shardIds)
            => FakeShardSets.Builder(new FakeProviderServices()).AddShardSet(ShardSetName, shardIds).Build().Configuration;

        #endregion
    }
}
