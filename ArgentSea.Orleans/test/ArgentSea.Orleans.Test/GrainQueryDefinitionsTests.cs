using System.Reflection;
using FluentAssertions;

namespace ArgentSea.Orleans.Test
{
    /// <summary>
    /// Spec section grain-query-definitions: OrleansDbQueryDefinitions and OrleansShardQueryDefinitions (eager QueryProcedure
    /// versus deferred Lazy&lt;QueryStatement&gt; roles, the eight-overload constructor family, the role accessors) and
    /// OrleansQueryNotProvidedException's messages.
    /// </summary>
    public class GrainQueryDefinitionsTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "argentsea-orleans-query-tests", Guid.NewGuid().ToString("N"));

        public GrainQueryDefinitionsTests()
        {
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try { Directory.Delete(_folder, true); } catch { }
        }

        private Lazy<QueryStatement> LazyStatement(string name, string sql)
        {
            var path = Path.Combine(_folder, name + ".sql");
            File.WriteAllText(path, sql);
            return QueryStatement.Create(name, ["Id"], path);
        }

        private static readonly QueryProcedure Read = new("rdr.ReadOrderV1", ["Id"]);
        private static readonly QueryProcedure Write = new("wtr.WriteOrderV1", ["Id"]);
        private static readonly QueryProcedure Clear = new("wtr.ClearOrderV1", ["Id"]);

        [Fact]
        [Trait("Spec", "blk-99b2b5d7d06841dca53da5da2e70b2d6")]
        [Trait("Spec", "blk-f4ca7618f59f45eda1eb6588177965c0")]
        [Trait("Spec", "blk-81c09807f0d04d49aa194824ffe80fa7")]
        [Trait("Spec", "blk-40233d060df34e109af2dde22c05553f")]
        [Trait("Spec", "blk-e74fd76a011d498ebea99b436405ca07")]
        public void ShardDefinitionWithEagerProceduresReturnsTheSameInstancesGrainTypeAndFormat()
        {
            var definition = new OrleansShardQueryDefinitions("OrderGrain", Read, QueryResultFormat.OutputArgs, Write, Clear);

            definition.ReadQuery.Should().BeSameAs(Read);
            definition.WriteQuery.Should().BeSameAs(Write);
            definition.ClearQuery.Should().BeSameAs(Clear);
            definition.GrainType.Should().Be("OrderGrain");
            definition.ResultFormat.Should().Be(QueryResultFormat.OutputArgs);
        }

        [Fact]
        [Trait("Spec", "blk-0a1e09d3a0cd4dcdbdf60d9b1983f067")]
        [Trait("Spec", "blk-51c59cfd430541c79b30bf9b23f07234")]
        [Trait("Spec", "blk-1220e024c55b499b8d52535e9e6d993d")]
        [Trait("Spec", "blk-e74fd76a011d498ebea99b436405ca07")]
        public void DbDefinitionWithEagerProceduresReturnsTheSameInstancesGrainTypeAndFormat()
        {
            var definition = new OrleansDbQueryDefinitions("orders", Read, QueryResultFormat.ResultSet, Write, Clear);

            definition.ReadQuery.Should().BeSameAs(Read);
            definition.WriteQuery.Should().BeSameAs(Write);
            definition.ClearQuery.Should().BeSameAs(Clear);
            definition.GrainType.Should().Be("orders");
            definition.ResultFormat.Should().Be(QueryResultFormat.ResultSet);
        }

        [Fact]
        [Trait("Spec", "blk-0a1e09d3a0cd4dcdbdf60d9b1983f067")]
        [Trait("Spec", "blk-f4ca7618f59f45eda1eb6588177965c0")]
        [Trait("Spec", "blk-81c09807f0d04d49aa194824ffe80fa7")]
        [Trait("Spec", "blk-51c59cfd430541c79b30bf9b23f07234")]
        public void DeferredStatementRolesResolveTheLazyValueWhenAccessed()
        {
            var lazyRead = LazyStatement("ReadOrder", "SELECT 1");
            var lazyWrite = LazyStatement("WriteOrder", "SELECT 2");
            var lazyClear = LazyStatement("ClearOrder", "SELECT 3");
            var shard = new OrleansShardQueryDefinitions("OrderGrain", lazyRead, QueryResultFormat.ResultSet, lazyWrite, lazyClear);
            var db = new OrleansDbQueryDefinitions("orders", lazyRead, QueryResultFormat.ResultSet, Write, lazyClear);

            lazyRead.IsValueCreated.Should().BeFalse("the statement file is not read until the role is accessed");

            shard.ReadQuery.Should().BeSameAs(lazyRead.Value);
            shard.WriteQuery.Should().BeSameAs(lazyWrite.Value);
            shard.ClearQuery.Should().BeSameAs(lazyClear.Value);
            shard.ReadQuery.Sql.Should().Be("SELECT 1");
            db.ReadQuery.Should().BeSameAs(lazyRead.Value);
            db.WriteQuery.Should().BeSameAs(Write, "each role is independently eager or deferred");
            db.ClearQuery.Sql.Should().Be("SELECT 3");
        }

        [Theory]
        [InlineData(typeof(OrleansDbQueryDefinitions))]
        [InlineData(typeof(OrleansShardQueryDefinitions))]
        [Trait("Spec", "blk-1220e024c55b499b8d52535e9e6d993d")]
        [Trait("Spec", "blk-40233d060df34e109af2dde22c05553f")]
        public void TheConstructorFamilyCoversEveryEagerDeferredCombinationOfTheThreeRoles(Type definitionType)
        {
            var roleTypes = new[] { typeof(QueryProcedure), typeof(Lazy<QueryStatement>) };
            var constructors = definitionType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            constructors.Should().HaveCount(8);
            var shapes = constructors.Select(c => c.GetParameters().Select(p => p.ParameterType).ToArray()).ToList();
            shapes.Should().OnlyContain(s => s.Length == 5 && s[0] == typeof(string) && s[2] == typeof(QueryResultFormat));
            foreach (var read in roleTypes)
            {
                foreach (var write in roleTypes)
                {
                    foreach (var clear in roleTypes)
                    {
                        shapes.Should().ContainSingle(s => s[1] == read && s[3] == write && s[4] == clear, $"read {read.Name}, write {write.Name}, clear {clear.Name} must be constructible");
                    }
                }
            }
        }

        [Fact]
        [Trait("Spec", "blk-3f36d01b73e74bee9a8709de1cf5b555")]
        [Trait("Spec", "blk-d59cd454751f4f1d872725e2fde6fadc")]
        [Trait("Spec", "blk-81c09807f0d04d49aa194824ffe80fa7")]
        public void ShardDefinitionWithoutAClearRoleThrowsNamingClearQuery()
        {
            var definition = new OrleansShardQueryDefinitions("OrderGrain", Read, QueryResultFormat.ResultSet, Write, (QueryProcedure)null!);

            definition.Invoking(d => d.ClearQuery).Should().Throw<OrleansQueryNotProvidedException>()
                .WithMessage("The Orleans Grain “OrderGrain” does not have a query defined for its ClearQuery method.");
            definition.Invoking(d => d.ReadQuery).Should().NotThrow();
            definition.Invoking(d => d.WriteQuery).Should().NotThrow();
        }

        [Fact]
        [Trait("Spec", "blk-dacbff83c8724e9aac6e15aceb600ad6")]
        [Trait("Spec", "blk-ce21daa2abe448cdb9a3e9c4643b4d3c")]
        [Trait("Spec", "blk-51c59cfd430541c79b30bf9b23f07234")]
        public void DbDefinitionWithoutAWriteRoleThrowsNamingWriteQuery()
        {
            var definition = new OrleansDbQueryDefinitions("orders", Read, QueryResultFormat.ResultSet, (QueryProcedure)null!, Clear);

            definition.Invoking(d => d.WriteQuery).Should().Throw<OrleansQueryNotProvidedException>()
                .WithMessage("The Orleans Grain “orders” does not have a query defined for its WriteQuery method.");
            definition.Invoking(d => d.ReadQuery).Should().NotThrow();
            definition.Invoking(d => d.ClearQuery).Should().NotThrow();
        }

        [Fact]
        [Trait("Spec", "blk-ce21daa2abe448cdb9a3e9c4643b4d3c")]
        [Trait("Spec", "blk-d59cd454751f4f1d872725e2fde6fadc")]
        public void AMissingReadRoleNamesReadQuery()
        {
            var db = new OrleansDbQueryDefinitions("orders", (Lazy<QueryStatement>)null!, QueryResultFormat.ResultSet, Write, Clear);
            var shard = new OrleansShardQueryDefinitions("OrderGrain", (Lazy<QueryStatement>)null!, QueryResultFormat.ResultSet, Write, Clear);

            db.Invoking(d => d.ReadQuery).Should().Throw<OrleansQueryNotProvidedException>().WithMessage("*“orders”*ReadQuery method.");
            shard.Invoking(d => d.ReadQuery).Should().Throw<OrleansQueryNotProvidedException>().WithMessage("*“OrderGrain”*ReadQuery method.");
        }

        private static OrleansQueryNotProvidedException ThrowFromReadStateAsync() => new();

        [Fact]
        [Trait("Spec", "blk-ecbed0414fb34c2a8111968c67c8c4ea")]
        public void QueryNotProvidedExceptionMessagesNameTheGrainTypeAndStripTheStateAsyncSuffix()
        {
            new OrleansQueryNotProvidedException("orders", "WriteStateAsync").Message
                .Should().Be("The Orleans Grain “orders” does not have a query defined for its Write method.");
            new OrleansQueryNotProvidedException("orders", "ClearQuery").Message
                .Should().Be("The Orleans Grain “orders” does not have a query defined for its ClearQuery method.");
            new OrleansQueryNotProvidedException("ReadStateAsync").Message
                .Should().Be("The current Orleans Grain does not have a query defined for its Read method.");

            var inner = new InvalidOperationException("inner");
            var withInner = new OrleansQueryNotProvidedException("orders", inner, "ReadStateAsync");
            withInner.Message.Should().Be("The Orleans Grain “orders” does not have a query defined for its Read method.");
            withInner.InnerException.Should().BeSameAs(inner);

            // With callerMethod omitted the compiler supplies the calling member's name (with any StateAsync suffix stripped).
            ThrowFromReadStateAsync().Message.Should().Contain("its ThrowFromRead method");
        }
    }
}
