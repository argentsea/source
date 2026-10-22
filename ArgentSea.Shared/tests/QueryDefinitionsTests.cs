using System;
using System.Data;
using System.IO;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec section query-definitions: the Query descriptor, QueryProcedure, and QueryStatement's file resolution (rooted name,
    /// rooted folder, relative folder under the application base directory, null versus empty extension, and the shared static
    /// Folder and Extension read at Lazy.Value time). The tests that change the statics restore them before returning.
    /// </summary>
    public class QueryDefinitionsTests : IDisposable
    {
        private readonly string _root;
        private readonly string _savedFolder = QueryStatement.Folder;
        private readonly string _savedExtension = QueryStatement.Extension;

        public QueryDefinitionsTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "argentsea-query-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            QueryStatement.Folder = _savedFolder;
            QueryStatement.Extension = _savedExtension;
            try { Directory.Delete(_root, true); } catch { }
        }

        private string WriteFile(string relativePath, string sql)
        {
            var path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, sql);
            return path;
        }

        /// <summary>A folder under the application base directory, which is where a relative folder resolves.</summary>
        private static string AppRelativeFolder(string name)
        {
            var folder = Path.Combine(AppContext.BaseDirectory, name);
            Directory.CreateDirectory(folder);
            return folder;
        }

        #region Query and QueryProcedure

        [Fact]
        [Trait("Spec", "blk-f09e5e3da06d471dacb922688dbd3e04")]
        [Trait("Spec", "blk-fcd88d6e16e245d68078ad270ce3e20e")]
        [Trait("Spec", "blk-5bdaec2404b34a9e98d438dbbac67797")]
        [Trait("Spec", "blk-3283695957f94e069b461c6e262b3bed")]
        public void AQueryExposesTheValuesItWasConstructedWithAndKeepsTheSameParameterArray()
        {
            var names = new[] { "tenantId", "region" };

            Query query = new QueryProcedure("HealthCheck", names);

            query.Sql.Should().Be("HealthCheck");
            query.Name.Should().Be("HealthCheck");
            query.ParameterNames.Should().BeSameAs(names, "the array instance is stored, not copied");
            names[0] = "changed";
            query.ParameterNames![0].Should().Be("changed", "the array's contents stay externally mutable");
            typeof(Query).IsAbstract.Should().BeTrue();
            typeof(Query).GetProperty(nameof(Query.Type))!.GetGetMethod()!.IsAbstract.Should().BeTrue();
            foreach (var name in new[] { nameof(Query.Sql), nameof(Query.Name), nameof(Query.ParameterNames) })
            {
                typeof(Query).GetProperty(name)!.CanWrite.Should().BeFalse($"{name} is fixed by the constructor");
            }
        }

        [Fact]
        [Trait("Spec", "blk-acebd32584784e3cb9ce97647de7db6b")]
        [Trait("Spec", "blk-c5c4e93527f045c0a9f663f51ba6e269")]
        [Trait("Spec", "blk-0f7571664007455992ca53dc9b557aeb")]
        public void AQueryProcedureIsAStoredProcedureNamedByItsProcedure()
        {
            var withParameters = new QueryProcedure("ws.ReadSubscriberV1", new[] { "SubscriberKey" });
            var withoutParameters = new QueryProcedure("ws.ReadSubscriberV1");

            withParameters.Type.Should().Be(CommandType.StoredProcedure);
            withParameters.Sql.Should().Be("ws.ReadSubscriberV1");
            withParameters.Name.Should().Be("ws.ReadSubscriberV1");
            withParameters.ParameterNames.Should().Equal("SubscriberKey");
            withoutParameters.ParameterNames.Should().BeNull();
            withoutParameters.Type.Should().Be(CommandType.StoredProcedure);
        }

        #endregion

        #region QueryStatement

        [Fact]
        [Trait("Spec", "blk-34428aab586f4669a3d08c672d7c7868")]
        [Trait("Spec", "blk-4ce1cfe3f29d493b8b806fa49180ddc0")]
        [Trait("Spec", "blk-bb250f1932324ad995320d782fdbf79c")]
        [Trait("Spec", "blk-80ddec42e2a94234bb0130d159759d53")]
        [Trait("Spec", "blk-26a524efb5e04c02a83e792d850db5dc")]
        public void AFullFilePathMaterializesATextQueryFromTheFileContents()
        {
            var path = WriteFile("sample.sql", "SELECT 1");

            var lazy = QueryStatement.Create("sample", new[] { "p" }, path);
            var query = lazy.Value;

            query.Name.Should().Be("sample");
            query.Sql.Should().Be("SELECT 1");
            query.ParameterNames.Should().Equal("p");
            query.Type.Should().Be(CommandType.Text);
            lazy.Value.Should().BeSameAs(query, "the Lazy materializes once");
            typeof(QueryStatement).GetConstructors().Should().BeEmpty("a statement is only created through the Create factories");
        }

        [Fact]
        [Trait("Spec", "blk-26a524efb5e04c02a83e792d850db5dc")]
        public void TheFileIsNotReadUntilValueIsRequested()
        {
            var path = Path.Combine(_root, "never-written.sql");

            var lazy = QueryStatement.Create("deferred", null, path);

            lazy.IsValueCreated.Should().BeFalse();
            File.WriteAllText(path, "SELECT 2");
            lazy.Value.Sql.Should().Be("SELECT 2", "the file is read when Value is first evaluated");
        }

        [Fact]
        [Trait("Spec", "blk-41bffc4eec8b4329a513461fe7f6326c")]
        [Trait("Spec", "blk-9d7c0004be194159bd72d6e2a8ceb718")]
        public void ARootedNameIgnoresTheFolderAndTakesTheExtension()
        {
            WriteFile(Path.Combine("Custom", "Report.sql"), "SELECT 'rooted name'");
            var rootedName = Path.Combine(_root, "Custom", "Report");

            var query = QueryStatement.Create(rootedName, null, "SQL", "sql").Value;

            query.Sql.Should().Be("SELECT 'rooted name'");
            query.Name.Should().Be(rootedName);
        }

        [Fact]
        [Trait("Spec", "blk-1bb6571f0d4f4e02bbf27edf085d9ce2")]
        [Trait("Spec", "blk-9d7c0004be194159bd72d6e2a8ceb718")]
        public void ARootedFolderIsCombinedWithTheNameWithoutTheApplicationBaseDirectory()
        {
            var folder = Path.Combine(_root, "CustomSql");
            WriteFile(Path.Combine("CustomSql", "report.sql"), "SELECT 'rooted folder'");

            var query = QueryStatement.Create("report", new[] { "@p" }, folder, "sql").Value;

            query.Sql.Should().Be("SELECT 'rooted folder'");
            query.ParameterNames.Should().Equal("@p");
        }

        [Fact]
        [Trait("Spec", "blk-4943af64672e4c709c4f7fc985222701")]
        [Trait("Spec", "blk-9d7c0004be194159bd72d6e2a8ceb718")]
        [Trait("Spec", "blk-acccddfca77141c7a9a28c2707af8c36")]
        public void ARelativeFolderResolvesUnderTheApplicationBaseDirectoryUsingTheSharedDefaults()
        {
            var folder = AppRelativeFolder("SQL");
            File.WriteAllText(Path.Combine(folder, "Foo.sql"), "SELECT 'app relative'");
            QueryStatement.Folder = "SQL";
            QueryStatement.Extension = "sql";

            var query = QueryStatement.Create("Foo").Value;

            query.Sql.Should().Be("SELECT 'app relative'");
            typeof(QueryStatement).GetProperty(nameof(QueryStatement.Folder))!.GetGetMethod()!.IsStatic.Should().BeTrue("Folder and Extension are process-wide statics");
            typeof(QueryStatement).GetProperty(nameof(QueryStatement.Extension))!.GetGetMethod()!.IsStatic.Should().BeTrue();
        }

        [Fact]
        [Trait("Spec", "blk-df48bba748ff4d1c9eccfb2972410421")]
        [Trait("Spec", "blk-9d7c0004be194159bd72d6e2a8ceb718")]
        public void ANullExtensionAppendsNoSuffix()
        {
            var folder = Path.Combine(_root, "NoExt");
            WriteFile(Path.Combine("NoExt", "report"), "SELECT 'no extension'");

            var query = QueryStatement.Create("report", null, folder, null).Value;

            query.Sql.Should().Be("SELECT 'no extension'");
        }

        [Fact]
        [Trait("Spec", "blk-ec4e2c1558894440906f06612108277b")]
        [Trait("Spec", "blk-9d7c0004be194159bd72d6e2a8ceb718")]
        public void AnEmptyExtensionAlsoAppendsNoSuffix()
        {
            var folder = Path.Combine(_root, "EmptyExt");
            WriteFile(Path.Combine("EmptyExt", "myQuery"), "SELECT 'empty extension'");

            var query = QueryStatement.Create("myQuery", null, folder, string.Empty).Value;

            query.Sql.Should().Be("SELECT 'empty extension'", "an empty extension means no extension, not a trailing dot");
        }

        [Fact]
        [Trait("Spec", "blk-011fe139466b46c9827f96456d03994d")]
        [Trait("Spec", "blk-629607929c5841988c7876493d4971da")]
        [Trait("Spec", "blk-26a524efb5e04c02a83e792d850db5dc")]
        public void TheSharedExtensionAndFolderAreReadWhenValueIsEvaluatedNotWhenCreateIsCalled()
        {
            var folder = AppRelativeFolder("LateBound");
            File.WriteAllText(Path.Combine(folder, "Foo.sql"), "SELECT 'sql'");
            File.WriteAllText(Path.Combine(folder, "Foo.psql"), "SELECT 'psql'");
            QueryStatement.Folder = "LateBound";
            QueryStatement.Extension = "sql";

            var lazy = QueryStatement.Create("Foo");
            QueryStatement.Extension = "psql";
            var query = lazy.Value;

            query.Sql.Should().Be("SELECT 'psql'", "Extension was reassigned before Value was first read");
            var explicitLazy = QueryStatement.Create("Foo", null, folder, "sql");
            QueryStatement.Extension = "psql";
            explicitLazy.Value.Sql.Should().Be("SELECT 'sql'", "the four-argument overload bypasses the statics");
        }

        #endregion
    }
}
