using System;
using System.Collections.Generic;
using System.Data.Common;
using ArgentSea;
using ArgentSea.Pg;
using FluentAssertions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ArgentSea.Pg.Test
{
    public class ProviderServicesTests
    {
        // Multiplexing (with Npgsql-accepted whitespace) suppresses Prepare(), so these tests need no live database.
        private const string MultiplexingConnectionString = "Host=localhost;Multiplexing = true";

        private static NpgsqlCommand NewMultiplexingCommand()
            => new NpgsqlCommand("test_proc", new NpgsqlConnection(MultiplexingConnectionString));

        private static DbParameterCollection SourceParameters(params (string name, int value)[] items)
        {
            var prms = new NpgsqlCommand().Parameters;
            foreach (var item in items)
            {
                prms.Add(new NpgsqlParameter<int>(item.name, NpgsqlDbType.Integer) { TypedValue = item.value });
            }
            return prms;
        }

        [Fact]
        [Trait("Spec", "blk-a51a213c941c4a7cbd7fc8ca5a9e8aff")]
        [Trait("Spec", "blk-2a2a41ac73944646b18322571471bb18")]
        public void SetParameters_AllParameters_AppliesOverridesAndKeepsSourceValues()
        {
            var cmd = NewMultiplexingCommand();
            var source = SourceParameters(("a", 1), ("b", 2));
            var overrides = new Dictionary<string, object> { { "b", 20 } };

            new DataProviderServiceFactory().SetParameters(cmd, null, source, overrides);

            cmd.Parameters.Count.Should().Be(2);
            cmd.Parameters["a"].Value.Should().Be(1, "a parameter without an override keeps its source value");
            cmd.Parameters["b"].Value.Should().Be(20, "the parameterValues override must take effect");
            source["b"].Value.Should().Be(2, "the source collection is not mutated");
        }

        [Fact]
        [Trait("Spec", "blk-2a2a41ac73944646b18322571471bb18")]
        public void SetParameters_NamedParameters_AppliesOverridesInQueryOrder()
        {
            var cmd = NewMultiplexingCommand();
            var source = SourceParameters(("a", 1), ("b", 2), ("c", 3));
            var overrides = new Dictionary<string, object> { { "a", 10 } };

            new DataProviderServiceFactory().SetParameters(cmd, new[] { "c", "a" }, source, overrides);

            cmd.Parameters.Count.Should().Be(2);
            cmd.Parameters[0].ParameterName.Should().Be("c");
            cmd.Parameters[0].Value.Should().Be(3);
            cmd.Parameters[1].ParameterName.Should().Be("a");
            cmd.Parameters[1].Value.Should().Be(10);
        }

        [Fact]
        [Trait("Spec", "blk-2a2a41ac73944646b18322571471bb18")]
        [Trait("Spec", "blk-581e371f27fe439fa7b4f93812b288b3")]
        public void SetParameters_DuplicateSourceNames_FirstMatchWins()
        {
            var cmd = NewMultiplexingCommand();
            var source = SourceParameters(("a", 1), ("a", 99), ("b", 2));

            new DataProviderServiceFactory().SetParameters(cmd, new[] { "a", "b" }, source, null);

            cmd.Parameters.Count.Should().Be(2, "each query parameter name binds exactly one source parameter");
            cmd.Parameters[0].Value.Should().Be(1, "the first source parameter with a matching name wins");
            cmd.Parameters[1].Value.Should().Be(2);
        }

        [Fact]
        [Trait("Spec", "blk-b918756788854fbe8f42b1dc1051afb3")]
        public void SetParameters_MissingName_ThrowsParameterNotFound()
        {
            var cmd = NewMultiplexingCommand();
            var source = SourceParameters(("a", 1));

            Action act = () => new DataProviderServiceFactory().SetParameters(cmd, new[] { "missing" }, source, null);

            act.Should().Throw<ParameterNotFoundException>();
        }

        [Theory]
        [InlineData("Host=localhost;Multiplexing=true", true)]
        [InlineData("Host=localhost;Multiplexing = true", true)]
        [InlineData("Host=localhost; multiplexing = True ", true)]
        [InlineData("Host=localhost;Multiplexing=false", false)]
        [InlineData("Host=localhost", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [Trait("Spec", "blk-b196c063278a4942b872c563c3803816")]
        [Trait("Spec", "blk-332046760fae41769f34d787c1cde3e5")]
        public void IsMultiplexing_UsesNpgsqlParsing(string connectionString, bool expected)
        {
            DataProviderServiceFactory.IsMultiplexing(connectionString).Should().Be(expected);
        }

        [Fact]
        [Trait("Spec", "blk-b2890a643c89423c915c34524985b919")]
        [Trait("Spec", "blk-443cd4af7fba42939b63655493f30aa7")]
        public void NewCommand_NonNpgsqlConnection_ThrowsWithParamName()
        {
            var connection = NSubstitute.Substitute.For<DbConnection>();

            Action act = () => new DataProviderServiceFactory().NewCommand("test_proc", connection);

            act.Should().Throw<ArgumentException>()
                .Where(ex => ex.ParamName == "connection" && ex.Message.Contains("NpgsqlConnection"));
        }

        [Fact]
        public void ConnectionDescription_InheritedPropertyChange_IsNotStale()
        {
            var global = new PgGlobalPropertiesOptions { Database = "Db1", Host = "host1" };
            var config = new PgConnectionConfiguration();
            config.SetAmbientConfiguration(global, null, null, null);

            config.ConnectionDescription.Should().Be("database Db1 on server host1");

            global.Database = "Db2";
            config.ConnectionDescription.Should().Be("database Db2 on server host1", "a change to an inherited property invalidates the cached description");
        }

        [Fact]
        public void ConnectionDescription_AfterConnectionStringBuiltThenInheritedChange_IsNotStale()
        {
            var global = new PgGlobalPropertiesOptions { Database = "Db1", Host = "host1" };
            var config = new PgConnectionConfiguration();
            config.SetAmbientConfiguration(global, null, null, null);

            config.GetConnectionString(null).Should().Contain("Database=Db1");
            config.ConnectionDescription.Should().Be("database Db1 on server host1");

            global.Host = "host2";
            config.ConnectionDescription.Should().Be("database Db1 on server host2");
            config.GetConnectionString(null).Should().Contain("Host=host2");
        }

        [Fact]
        public void ConnectionDescription_ReadBeforeAmbientConfiguration_IsRefreshedBySetAmbientConfiguration()
        {
            var config = new PgConnectionConfiguration();
            config.ConnectionDescription.Should().Be("database  on server ");

            config.SetAmbientConfiguration(new PgGlobalPropertiesOptions { Database = "Db1", Host = "host1" }, null, null, null);

            config.ConnectionDescription.Should().Be("database Db1 on server host1");
        }

        [Fact]
        public void LocalPropertyChange_InvalidatesConnectionStringAndDescription()
        {
            var config = new PgConnectionConfiguration { Database = "Db1", Host = "host1" };
            config.SetAmbientConfiguration(null, null, null, null);

            config.GetConnectionString(null).Should().Contain("Database=Db1");
            config.ConnectionDescription.Should().Be("database Db1 on server host1");

            config.Database = "Db2";
            config.Host = "host2";

            config.ConnectionDescription.Should().Be("database Db2 on server host2", "a change to a local property invalidates the cached description");
            config.GetConnectionString(null).Should().Contain("Database=Db2").And.Contain("Host=host2", "a change to a local property invalidates the cached connection string");
        }

        [Fact]
        public void SetAmbientConfiguration_Repeated_DetachesFromPreviousSources()
        {
            var oldGlobal = new PgGlobalPropertiesOptions { Database = "Old", Host = "host1" };
            var newGlobal = new PgGlobalPropertiesOptions { Database = "New", Host = "host1" };
            var config = new PgConnectionConfiguration();
            config.SetAmbientConfiguration(oldGlobal, null, null, null);
            config.SetAmbientConfiguration(newGlobal, null, null, null);

            config.ConnectionDescription.Should().Be("database New on server host1");
            config.GetConnectionString(null).Should().Contain("Database=New");

            oldGlobal.Database = "Stale";
            newGlobal.Host = "host2";

            config.ConnectionDescription.Should().Be("database New on server host2", "only the current inheritance chain is observed");
        }

        [Fact]
        [Trait("Spec", "blk-a9d5e4ee6765425eb5d84242df4accca")]
        public void PgQueryStatement_UsesItsOwnExtension_NotTheSharedDefault()
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "argentsea-pgqs-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(folder);
            try
            {
                var name = System.IO.Path.Combine(folder, "Stmt");
                System.IO.File.WriteAllText(name + ".sql", "SELECT 'sql';");
                System.IO.File.WriteAllText(name + ".psql", "SELECT 'psql';");

                var statement = PgQueryStatement.Create(name, new[] { "p1" }).Value;

                statement.Sql.Should().Be("SELECT 'psql';", "PgQueryStatement resolves against PgQueryStatement.Extension, independent of QueryStatement.Extension");
                statement.ParameterNames.Should().Equal("p1");
                statement.Type.Should().Be(System.Data.CommandType.Text);
            }
            finally
            {
                System.IO.Directory.Delete(folder, true);
            }
        }
    }
}
