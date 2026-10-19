using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Pg.Test
{
    /// <summary>
    /// Spec section query-definitions, provider part: PgQueryStatement uses its own Extension (default "psql", not
    /// QueryStatement's shared static) and the shared Folder, both read when Value is evaluated.
    /// Shares the "QueryStatement statics" collection with <see cref="ConfigurationTests"/>, whose AddPgServices calls write
    /// QueryStatement.Extension, so the two never race on the statics.
    /// </summary>
    [Collection("QueryStatement statics")]
    public class PgQueryStatementTests : IDisposable
    {
        private readonly string _savedFolder = QueryStatement.Folder;
        private readonly string _savedSharedExtension = QueryStatement.Extension;
        private readonly string _savedPgExtension = PgQueryStatement.Extension;

        public void Dispose()
        {
            QueryStatement.Folder = _savedFolder;
            QueryStatement.Extension = _savedSharedExtension;
            PgQueryStatement.Extension = _savedPgExtension;
        }

        [Fact]
        [Trait("Spec", "blk-26a524efb5e04c02a83e792d850db5dc")]
        [Trait("Spec", "blk-acccddfca77141c7a9a28c2707af8c36")]
        [Trait("Spec", "blk-a9d5e4ee6765425eb5d84242df4accca")]
        [Trait("Spec", "blk-8dc82c304f2f4369920bc58b7d1c98a3")]
        public void PgQueryStatementUsesItsOwnExtensionAndIgnoresTheSharedOne()
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "PgStatements");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Foo.psql"), "SELECT 'psql'");
            File.WriteAllText(Path.Combine(folder, "Foo.shared"), "SELECT 'shared'");
            QueryStatement.Folder = "PgStatements";
            QueryStatement.Extension = "shared";
            PgQueryStatement.Extension = "psql";

            var query = PgQueryStatement.Create("Foo").Value;

            query.Sql.Should().Be("SELECT 'psql'", "the provider factory's Extension is used, not the shared static");
            query.ParameterNames.Should().BeNull();
            query.Type.Should().Be(System.Data.CommandType.Text);
        }
    }
}
