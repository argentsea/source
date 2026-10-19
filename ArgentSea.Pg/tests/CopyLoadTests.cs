using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ArgentSea.Pg.Test
{
    /// <summary>
    /// Spec section copy-load, unit part: the destination-table DDL and COPY command text CopyMapper builds for model and
    /// shard-key loads, the per-type compile-once cache, the Add registration family and the statements it logs. The steps run
    /// through the TestKit fake provider up to the point where a real NpgsqlConnection is needed for the binary import, which is
    /// where each step stops with an InvalidCastException; executing the COPY itself is integration work.
    /// </summary>
    public class CopyLoadTests
    {
        private const string Db = "MainDb";
        private const string ShardSetName = "Set";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private static readonly CancellationToken None = CancellationToken.None;

        public class Person
        {
            [MapToPgInteger("Id")]
            public int Id { get; set; }

            [MapToPgVarchar("Name", 50)]
            public string? Name { get; set; }
        }

        public class Widget
        {
            [MapToPgInteger("Id")]
            public int Id { get; set; }
        }

        public class KeyedPerson
        {
            [MapPgShardKey("ShardId", "RecordId")]
            [MapToPgInteger("RecordId")]
            public ShardKey<int> Key { get; set; }

            [MapToPgVarchar("Name", 50)]
            public string? Name { get; set; }
        }

        private static (FakeProviderServices Services, FakeDatabases Databases, RecordingLogger Logger) Database()
        {
            var services = new FakeProviderServices();
            var logger = new RecordingLogger<DatabasesBase<FakeDatabasesOptions>>();
            var databases = FakeDatabases.Builder(services).AddDatabase(Db).WithLogger(logger).Build();
            return (services, databases, logger);
        }

        private static (FakeProviderServices Services, FakeShardSets ShardSets, RecordingLogger Logger) ShardSet()
        {
            var services = new FakeProviderServices();
            var logger = new RecordingLogger<ShardSetsBase<FakeShardSetsOptions>>();
            var shardSets = FakeShardSets.Builder(services).AddShardSet(ShardSetName, 3).WithLogger(logger).Build();
            return (services, shardSets, logger);
        }

        private static async Task RunUntilTheBinaryImport(Func<Task> run)
        {
            // The fake connection is not an NpgsqlConnection, so each step stops where it would begin the binary import.
            await run.Should().ThrowAsync<InvalidCastException>();
        }

        private static (string TableDef, string ImportDef) LoggedStatements(RecordingLogger logger)
        {
            var entry = logger.EntriesNamed(nameof(PgLoggingExtensions.CopySqlStatements)).Should().ContainSingle().Which;
            entry.Level.Should().Be(LogLevel.Debug);
            entry.Id.Should().Be((int)LoggingExtensions.EventIdentifier.CmdExecuted);
            var message = entry.Message!;
            var tableStart = message.IndexOf("CREATE", StringComparison.Ordinal);
            var copyStart = message.IndexOf("COPY ", StringComparison.Ordinal);
            var tableDef = message.Substring(tableStart, message.IndexOf(" \r\n and used the copy import", StringComparison.Ordinal) - tableStart);
            var importDef = message.Substring(copyStart).TrimEnd('.');
            return (tableDef, importDef);
        }

        #region Model loads

        [Fact]
        [Trait("Spec", "blk-0c043264e46f44f28dbea6dd06451a09")]
        [Trait("Spec", "blk-969d29c8d4bc4914a15e26847f8a326f")]
        [Trait("Spec", "blk-42921ec4b5cf43ab8eefc1878a564092")]
        [Trait("Spec", "blk-0065a202e5464441ba6e78fee28cbee0")]
        [Trait("Spec", "blk-7cee1125d4324f8cb4978303e9a3e1f9")]
        public async Task AnUnqualifiedTableNameCreatesATransactionLocalTableAndCopiesTheMappedColumns()
        {
            var (services, databases, logger) = Database();
            var batch = new DatabaseBatch<string>();
            var people = new List<Person> { new Person { Id = 7, Name = "Ada" }, new Person { Id = 8, Name = "Lin" } };

            var returned = batch.Add(people, "stage_people");
            await RunUntilTheBinaryImport(() => databases[Db].Write.RunAsync(batch, None).WaitAsync(Timeout));

            returned.Should().BeSameAs(batch);
            var (tableDef, importDef) = LoggedStatements(logger);
            tableDef.Should().StartWith("CREATE TEMP TABLE  \"stage_people\" (");
            tableDef.Should().Contain("\"Id\" integer NULL").And.Contain("\"Name\" varchar(50) NULL");
            tableDef.Should().EndWith(") ON COMMIT DROP;");
            importDef.Should().Be("COPY \"stage_people\" (\"Id\",\"Name\") FROM STDIN BINARY;");
            var command = services.Recorder.Commands.Should().ContainSingle().Which;
            command.CommandText.Should().Be(tableDef, "the table is created before the import begins");
            command.Kind.Should().Be(FakeExecutionKind.NonQuery);
            command.TransactionId.Should().NotBeNull("the table is created inside the batch transaction");
        }

        [Fact]
        [Trait("Spec", "blk-0c043264e46f44f28dbea6dd06451a09")]
        [Trait("Spec", "blk-ed22ac024e0a4447846ebaa368d10180")]
        public async Task ASchemaQualifiedTableNameCreatesAPersistentTableWithEachPartQuoted()
        {
            var (services, databases, logger) = Database();
            var batch = new DatabaseBatch<string>().Add(new List<Person> { new Person { Id = 1 } }, "staging.people");

            await RunUntilTheBinaryImport(() => databases[Db].Write.RunAsync(batch, None).WaitAsync(Timeout));

            var (tableDef, importDef) = LoggedStatements(logger);
            tableDef.Should().StartWith("CREATE TABLE IF NOT EXISTS \"staging\".\"people\" (");
            tableDef.Should().NotContain("ON COMMIT DROP").And.EndWith(");");
            importDef.Should().Be("COPY \"staging\".\"people\" (\"Id\",\"Name\") FROM STDIN BINARY;");
            services.Recorder.Commands.Single().CommandText.Should().Be(tableDef);
        }

        [Fact]
        [Trait("Spec", "blk-0c043264e46f44f28dbea6dd06451a09")]
        [Trait("Spec", "blk-33784e6261c14c6dbcbf461f47ecd8e6")]
        public async Task AModelShardKeyPropertyContributesItsShardAndRecordColumns()
        {
            var (_, databases, logger) = Database();
            var batch = new DatabaseBatch<string>().Add(new List<KeyedPerson> { new KeyedPerson { Key = new ShardKey<int>(1, 2), Name = "n" } }, "keyed");

            await RunUntilTheBinaryImport(() => databases[Db].Write.RunAsync(batch, None).WaitAsync(Timeout));

            var (tableDef, importDef) = LoggedStatements(logger);
            tableDef.Should().Contain("\"ShardId\" smallint NULL").And.Contain("\"RecordId\" integer NULL").And.Contain("\"Name\" varchar(50) NULL");
            importDef.Should().Be("COPY \"keyed\" (\"ShardId\",\"RecordId\",\"Name\") FROM STDIN BINARY;");
        }

        public class Measurement
        {
            [MapToPgInteger("Id")]
            public int Id { get; set; }

            [MapToPgInteger("Reading")]
            public int? Reading { get; set; }

            [MapToPgTimestamp("TakenAt")]
            public DateTime? TakenAt { get; set; }
        }

        [Fact]
        [Trait("Spec", "blk-0c043264e46f44f28dbea6dd06451a09")]
        public async Task NullableValueTypePropertiesCompileAndContributeTheirColumns()
        {
            // The writer for a Nullable<T> property must compile (it reads HasValue on the nullable type, not on T) and write NULL
            // when there is no value; the integration tests load such rows for real.
            var (_, databases, logger) = Database();
            var batch = new DatabaseBatch<string>().Add(new List<Measurement> { new Measurement { Id = 1, Reading = null, TakenAt = null } }, "readings");

            await RunUntilTheBinaryImport(() => databases[Db].Write.RunAsync(batch, None).WaitAsync(Timeout));

            var (tableDef, importDef) = LoggedStatements(logger);
            tableDef.Should().Contain("\"Reading\" integer NULL").And.Contain("\"TakenAt\" timestamp NULL");
            importDef.Should().Be("COPY \"readings\" (\"Id\",\"Reading\",\"TakenAt\") FROM STDIN BINARY;");
        }

        [Fact]
        [Trait("Spec", "blk-ed22ac024e0a4447846ebaa368d10180")]
        public async Task AnInvalidTableNameIsRejectedBeforeAnyCommandText()
        {
            var (services, databases, logger) = Database();
            var batch = new DatabaseBatch<string>().Add(new List<Person> { new Person() }, "a.b.c");

            Func<Task> act = () => databases[Db].Write.RunAsync(batch, None).WaitAsync(Timeout);

            (await act.Should().ThrowAsync<Exception>()).WithMessage("*at most one*");
            services.Recorder.Commands.Should().BeEmpty();
            logger.EntriesNamed(nameof(PgLoggingExtensions.CopySqlStatements)).Should().BeEmpty();
        }

        #endregion

        #region Caching

        [Fact]
        [Trait("Spec", "blk-2bbcd753b33c4d8da21d676bc67ba9c8")]
        [Trait("Spec", "blk-194ee549664e4022a86d7d8b72681fc2")]
        [Trait("Spec", "blk-c1e6fd83729e47eeabd86fdfbd116c80")]
        [Trait("Spec", "blk-1aa134af0f084bb785fd7742011220c3")]
        [Trait("Spec", "blk-b2f1ecbf8fdc46818abe840ed7750aa2")]
        public async Task TheWriterForAModelTypeIsCompiledOnceAndReusedOnTheNextBatch()
        {
            var (_, databases, logger) = Database();
            var first = new DatabaseBatch<string>().Add(new List<Widget> { new Widget { Id = 1 } }, "widgets");
            var second = new DatabaseBatch<string>().Add(new List<Widget> { new Widget { Id = 2 } }, "other_widgets");

            await RunUntilTheBinaryImport(() => databases[Db].Write.RunAsync(first, None).WaitAsync(Timeout));
            var miss = logger.EntriesNamed(nameof(PgLoggingExtensions.PgCopyCacheMiss)).Should().ContainSingle().Which;
            var compiled = logger.EntriesNamed(nameof(PgLoggingExtensions.CreatedExpressionTreeForCopy)).Should().ContainSingle().Which;
            logger.Clear();
            await RunUntilTheBinaryImport(() => databases[Db].Write.RunAsync(second, None).WaitAsync(Timeout));

            miss.Level.Should().Be(LogLevel.Information);
            miss.Id.Should().Be((int)LoggingExtensions.EventIdentifier.MapperCacheStatus);
            miss.Message.Should().Contain(typeof(Widget).ToString());
            compiled.Level.Should().Be(LogLevel.Debug);
            compiled.Id.Should().Be((int)LoggingExtensions.EventIdentifier.ExpressionTreeCreation);
            compiled.Message.Should().Contain("Pg COPY row values").And.Contain(nameof(PgLoggingExtensions.TraceCopyMapperProperty), "the writer traces each property it writes");
            var hit = logger.EntriesNamed(nameof(PgLoggingExtensions.PgCopyCacheHit)).Should().ContainSingle().Which;
            hit.Level.Should().Be(LogLevel.Debug);
            hit.Id.Should().Be((int)LoggingExtensions.EventIdentifier.MapperCacheStatus);
            logger.EntriesNamed(nameof(PgLoggingExtensions.PgCopyCacheMiss)).Should().BeEmpty();
            logger.EntriesNamed(nameof(PgLoggingExtensions.CreatedExpressionTreeForCopy)).Should().BeEmpty("the cached writer is reused without recompiling");
            LoggedStatements(logger).ImportDef.Should().Be("COPY \"other_widgets\" (\"Id\") FROM STDIN BINARY;", "the cached text is re-targeted at the new table name");
        }

        #endregion

        #region Shard-key loads

        [Fact]
        [Trait("Spec", "blk-33784e6261c14c6dbcbf461f47ecd8e6")]
        [Trait("Spec", "blk-bf71333b9ab14cc3bceccfb12dfebb10")]
        [Trait("Spec", "blk-0065a202e5464441ba6e78fee28cbee0")]
        [Trait("Spec", "blk-7cee1125d4324f8cb4978303e9a3e1f9")]
        public async Task AChildKeyListLoadsShardRecordAndChildColumnsIntoAPersistentTable()
        {
            var (services, shardSets, logger) = ShardSet();
            var batch = new ShardBatch<string>();
            var keys = new List<ShardKey<int, int>> { new ShardKey<int, int>(3, 42, 9) };

            var returned = batch.Add(keys, "work.copy_keys", new MapToPgSmallintAttribute("ShardId"), new MapToPgIntegerAttribute("RecordId"), new MapToPgIntegerAttribute("ChildId"));
            await RunUntilTheBinaryImport(() => shardSets[ShardSetName][3].Write.RunAsync(batch, None).WaitAsync(Timeout));

            returned.Should().BeSameAs(batch);
            var (tableDef, importDef) = LoggedStatements(logger);
            tableDef.Should().Be("CREATE TABLE IF NOT EXISTS \"work\".\"copy_keys\" (\"ShardId\" smallint, \"RecordId\" integer, \"ChildId\" integer);");
            importDef.Should().Be("COPY \"work\".\"copy_keys\" (\"ShardId\", \"RecordId\", \"ChildId\") FROM STDIN (FORMAT BINARY)");
            var command = services.Recorder.Commands.Should().ContainSingle().Which;
            command.CommandText.Should().Be(tableDef);
            command.ShardId.Should().Be(3);
        }

        [Fact]
        [Trait("Spec", "blk-33784e6261c14c6dbcbf461f47ecd8e6")]
        public async Task ARecordKeyListLoadsIntoATransactionLocalTableOnEveryShardOfAShardSetBatch()
        {
            var (services, shardSets, logger) = ShardSet();
            var batch = new ShardSetBatch();

            var returned = batch.Add(new List<ShardKey<int>> { new ShardKey<int>(3, 1) }, "tmp_keys", new MapToPgSmallintAttribute("ShardId"), new MapToPgIntegerAttribute("RecordId"));
            await RunUntilTheBinaryImport(() => shardSets[ShardSetName].Write.RunAsync(batch, None).WaitAsync(Timeout));

            returned.Should().BeSameAs(batch);
            var (tableDef, importDef) = LoggedStatements(logger);
            tableDef.Should().Be("CREATE TEMP TABLE IF NOT EXISTS \"tmp_keys\" (\"ShardId\" smallint, \"RecordId\" integer) ON COMMIT DROP;");
            importDef.Should().Be("COPY \"tmp_keys\" (\"ShardId\", \"RecordId\") FROM STDIN (FORMAT BINARY)");
            services.Recorder.Commands.Should().ContainSingle().Which.ShardId.Should().Be(3);
        }

        [Fact]
        [Trait("Spec", "blk-33784e6261c14c6dbcbf461f47ecd8e6")]
        public async Task GrandChildAndGreatGrandChildKeyListsEmitEveryIdentityColumnInKeyOrder()
        {
            var (_, shardSets, logger) = ShardSet();
            var grand = new ShardBatch<string>().Add(new List<ShardKey<int, int, int>> { new ShardKey<int, int, int>(3, 1, 2, 3) }, "g", new MapToPgSmallintAttribute("S"), new MapToPgIntegerAttribute("R"), new MapToPgIntegerAttribute("C"), new MapToPgIntegerAttribute("G"));
            await RunUntilTheBinaryImport(() => shardSets[ShardSetName][3].Write.RunAsync(grand, None).WaitAsync(Timeout));
            LoggedStatements(logger).ImportDef.Should().Be("COPY \"g\" (\"S\", \"R\", \"C\", \"G\") FROM STDIN (FORMAT BINARY)");
            logger.Clear();

            var great = new ShardBatch<string>().Add(new List<ShardKey<int, int, int, int>> { new ShardKey<int, int, int, int>(3, 1, 2, 3, 4) }, "gg", new MapToPgSmallintAttribute("S"), new MapToPgIntegerAttribute("R"), new MapToPgIntegerAttribute("C"), new MapToPgIntegerAttribute("G"), new MapToPgIntegerAttribute("GG"));
            await RunUntilTheBinaryImport(() => shardSets[ShardSetName][3].Write.RunAsync(great, None).WaitAsync(Timeout));

            var (tableDef, importDef) = LoggedStatements(logger);
            tableDef.Should().Be("CREATE TEMP TABLE IF NOT EXISTS \"gg\" (\"S\" smallint, \"R\" integer, \"C\" integer, \"G\" integer, \"GG\" integer) ON COMMIT DROP;");
            importDef.Should().Be("COPY \"gg\" (\"S\", \"R\", \"C\", \"G\", \"GG\") FROM STDIN (FORMAT BINARY)");
        }

        [Fact]
        [Trait("Spec", "blk-7cee1125d4324f8cb4978303e9a3e1f9")]
        public void TheAddFamilyCoversEveryBatchTypeAndKeyArityWithEquivalentListAndIListForms()
        {
            var adds = typeof(CopyMapper).GetMethods().Where(m => m.Name == "Add").ToList();

            adds.Select(m => m.GetParameters()[0].ParameterType).Select(t => t.IsGenericType ? t.GetGenericTypeDefinition() : t).Distinct()
                .Should().BeEquivalentTo(new[] { typeof(DatabaseBatch<>), typeof(ShardBatch<>), typeof(ShardSetBatch) });
            var listForms = adds.Where(m => m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(List<>)).ToList();
            var ilistForms = adds.Where(m => m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(IList<>)).ToList();
            listForms.Should().HaveCount(ilistForms.Count, "every IList<> operation has a List<> convenience form");
            var listBatch = new DatabaseBatch<string>().Add(new List<Person>(), "t");
            var ilistBatch = new DatabaseBatch<string>().Add((IList<Person>)new List<Person>(), "t");
            listBatch.Count.Should().Be(1);
            ilistBatch.Count.Should().Be(1);
            listBatch[0].GetType().Should().Be(ilistBatch[0].GetType(), "both forms register the same step type");
        }

        #endregion
    }
}
