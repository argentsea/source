using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// Spec section query-definitions, provider part: SqlQueryStatement uses its own Extension (not QueryStatement's shared
    /// static) and the shared Folder, both read when Value is evaluated.
    /// </summary>
    public class SqlQueryStatementTests : IDisposable
    {
        private readonly string _savedFolder = QueryStatement.Folder;
        private readonly string _savedSharedExtension = QueryStatement.Extension;
        private readonly string _savedSqlExtension = SqlQueryStatement.Extension;

        public void Dispose()
        {
            QueryStatement.Folder = _savedFolder;
            QueryStatement.Extension = _savedSharedExtension;
            SqlQueryStatement.Extension = _savedSqlExtension;
        }

        [Fact]
        [Trait("Spec", "blk-26a524efb5e04c02a83e792d850db5dc")]
        [Trait("Spec", "blk-acccddfca77141c7a9a28c2707af8c36")]
        [Trait("Spec", "blk-b4f5c3bbce8e4e22b64b9e02c9838431")]
        public void SqlQueryStatementUsesItsOwnExtensionAndIgnoresTheSharedOne()
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "SqlStatements");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Foo.tsql"), "SELECT 'tsql'");
            File.WriteAllText(Path.Combine(folder, "Foo.shared"), "SELECT 'shared'");
            QueryStatement.Folder = "SqlStatements";
            QueryStatement.Extension = "shared";
            SqlQueryStatement.Extension = "tsql";

            var query = SqlQueryStatement.Create("Foo", new[] { "@p" }).Value;

            query.Sql.Should().Be("SELECT 'tsql'", "the provider factory's Extension is used, not the shared static");
            query.Name.Should().Be("Foo");
            query.ParameterNames.Should().Equal("@p");
            query.Type.Should().Be(System.Data.CommandType.Text);
            SqlQueryStatement.Extension.Should().Be("tsql");
        }
    }
}
