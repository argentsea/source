using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using ArgentSea;
using ArgentSea.Sql;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ArgentSea.Sql.Test
{
    public class ProviderServicesTests
    {
        private static DbParameterCollection SourceParameters(params (string name, int value)[] items)
        {
            var prms = new SqlCommand().Parameters;
            foreach (var item in items)
            {
                prms.Add(new SqlParameter(item.name, SqlDbType.Int) { Value = item.value });
            }
            return prms;
        }

        [Fact]
        [Trait("Spec", "blk-c9156902e6d74713a73697f03934c01c")]
        public void SetParameters_NamedParameters_AppliesOverridesInQueryOrder()
        {
            var cmd = new SqlCommand("test_proc");
            var source = SourceParameters(("@a", 1), ("@b", 2), ("@c", 3));
            var overrides = new Dictionary<string, object> { { "@a", 10 } };

            new DataProviderServiceFactory().SetParameters(cmd, new[] { "c", "@a" }, source, overrides);

            cmd.Parameters.Count.Should().Be(2);
            cmd.Parameters[0].ParameterName.Should().Be("@c");
            cmd.Parameters[0].Value.Should().Be(3);
            cmd.Parameters[1].ParameterName.Should().Be("@a");
            cmd.Parameters[1].Value.Should().Be(10);
        }

        [Fact]
        [Trait("Spec", "blk-c9156902e6d74713a73697f03934c01c")]
        [Trait("Spec", "blk-ead57f0f7da642f1850f13e02fc6f74d")]
        public void SetParameters_DuplicateSourceNames_FirstMatchWins()
        {
            var cmd = new SqlCommand("test_proc");
            var source = SourceParameters(("@a", 1), ("@a", 99), ("@b", 2));

            new DataProviderServiceFactory().SetParameters(cmd, new[] { "a", "b" }, source, null);

            cmd.Parameters.Count.Should().Be(2, "each query parameter name binds exactly one source parameter");
            cmd.Parameters[0].Value.Should().Be(1, "the first source parameter with a matching name wins");
            cmd.Parameters[1].Value.Should().Be(2);
        }

        [Fact]
        [Trait("Spec", "blk-41c99e355ae943869fd390dde0e251fe")]
        public void SetParameters_MissingName_ThrowsParameterNotFound()
        {
            var cmd = new SqlCommand("test_proc");
            var source = SourceParameters(("@a", 1));

            Action act = () => new DataProviderServiceFactory().SetParameters(cmd, new[] { "missing" }, source, null);

            act.Should().Throw<ParameterNotFoundException>();
        }

        [Fact]
        [Trait("Spec", "blk-7954318ba5354605bd93c70f6f21b1c4")]
        [Trait("Spec", "blk-a00f7f7d845b452098e65aece83ed5ba")]
        public void NewCommand_NonSqlConnection_ThrowsWithParamName()
        {
            var connection = NSubstitute.Substitute.For<DbConnection>();

            Action act = () => new DataProviderServiceFactory().NewCommand("test_proc", connection);

            act.Should().Throw<ArgumentException>()
                .Where(ex => ex.ParamName == "connection" && ex.Message.Contains("SqlConnection"));
        }

        [Fact]
        public void ConnectionDescription_InheritedPropertyChange_IsNotStale()
        {
            var global = new SqlGlobalPropertiesOptions { InitialCatalog = "Db1", DataSource = "host1" };
            var config = new SqlConnectionConfiguration();
            config.SetAmbientConfiguration(global, null, null, null);

            config.GetConnectionString(null).Should().Contain("Initial Catalog=Db1");
            config.ConnectionDescription.Should().Be("database Db1 on server host1");

            global.InitialCatalog = "Db2";

            config.ConnectionDescription.Should().Be("database Db2 on server host1", "a change to an inherited property invalidates the cached description");
            config.GetConnectionString(null).Should().Contain("Initial Catalog=Db2");
        }

        [Fact]
        public void LocalPropertyChange_InvalidatesConnectionStringAndDescription()
        {
            var config = new SqlConnectionConfiguration { InitialCatalog = "Db1", DataSource = "host1" };
            config.SetAmbientConfiguration(null, null, null, null);

            config.GetConnectionString(null).Should().Contain("Initial Catalog=Db1");
            config.ConnectionDescription.Should().Be("database Db1 on server host1");

            config.InitialCatalog = "Db2";
            config.DataSource = "host2";

            config.ConnectionDescription.Should().Be("database Db2 on server host2", "a change to a local property invalidates the cached description");
            config.GetConnectionString(null).Should().Contain("Initial Catalog=Db2").And.Contain("Data Source=host2", "a change to a local property invalidates the cached connection string");
        }

        [Fact]
        public void SetAmbientConfiguration_ClearsCachesAndDetachesFromPreviousSources()
        {
            var oldGlobal = new SqlGlobalPropertiesOptions { InitialCatalog = "Old", DataSource = "host1" };
            var newGlobal = new SqlGlobalPropertiesOptions { InitialCatalog = "New", DataSource = "host1" };
            var config = new SqlConnectionConfiguration();
            config.SetAmbientConfiguration(oldGlobal, null, null, null);
            config.GetConnectionString(null).Should().Contain("Initial Catalog=Old");
            config.ConnectionDescription.Should().Be("database Old on server host1");

            config.SetAmbientConfiguration(newGlobal, null, null, null);

            config.ConnectionDescription.Should().Be("database New on server host1", "a new inheritance chain invalidates the cached description");
            config.GetConnectionString(null).Should().Contain("Initial Catalog=New");

            oldGlobal.InitialCatalog = "Stale";
            newGlobal.DataSource = "host2";

            config.ConnectionDescription.Should().Be("database New on server host2", "only the current inheritance chain is observed");
        }

        [Fact]
        public void ApplicationIntent_SameValue_DoesNotRaisePropertyChanged()
        {
            var props = new SqlGlobalPropertiesOptions { ApplicationIntent = ApplicationIntent.ReadOnly };
            var raised = 0;
            props.PropertyChanged += (s, e) => raised++;

            props.ApplicationIntent = ApplicationIntent.ReadOnly;
            raised.Should().Be(0, "assigning the current value is not a change");

            props.ApplicationIntent = ApplicationIntent.ReadWrite;
            raised.Should().Be(1, "assigning a different value is a change");
        }

        [Fact]
        [Trait("Spec", "blk-b4f5c3bbce8e4e22b64b9e02c9838431")]
        public void SqlQueryStatement_UsesItsOwnExtension_NotTheSharedDefault()
        {
            var folder = Path.Combine(Path.GetTempPath(), "argentsea-sqlqs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var name = Path.Combine(folder, "Stmt");
                File.WriteAllText(name + ".sql", "SELECT 'sql';");
                File.WriteAllText(name + ".psql", "SELECT 'psql';");

                var statement = SqlQueryStatement.Create(name, new[] { "@p1" }).Value;

                statement.Sql.Should().Be("SELECT 'sql';", "SqlQueryStatement resolves against SqlQueryStatement.Extension, independent of QueryStatement.Extension");
                statement.ParameterNames.Should().Equal("@p1");
                statement.Type.Should().Be(CommandType.Text);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
