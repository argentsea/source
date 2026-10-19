using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlTypes;
using System.Globalization;
using ArgentSea;
using ArgentSea.Sql;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// Spec section sql-server-provider-services: the SQL Server realization of IDataProviderServiceFactory.
    /// Transient classification over the fixed error-number table, command creation guards, connection creation and the
    /// parameter copy rules of SetParameters. The ordering and duplicate-name rules are also covered by ProviderServicesTests.
    /// </summary>
    public class SqlProviderServicesSpecTests
    {
        private static readonly DataProviderServiceFactory Factory = new DataProviderServiceFactory();

        /// <summary>Every error number DataProviderServiceFactory.GetIsErrorTransient lists, grouped as the spec groups them.</summary>
        private static readonly int[] TransientNumbers =
        {
            41301, 41302, 41305, 41325, // memory-optimized-table transaction or row-version conflicts
            41823, 41839, 41840,        // memory-optimized-table resource or commit-dependency limits
            4221,                       // read-secondary login blocked waiting for row-version transition
            49918, 49919, 49920,        // Azure SQL subscription busy or throttled
            4060, 40613,                // database or its login unavailable while the database comes online
            40197, 40501, 10928, 10929, // service busy or resource-governance limit reached
            10053, 10054, 10060, 20, 64, 233, // network or transport-level failure before or during login
            1205,                       // deadlock victim
        };

        public static TheoryData<int> TransientErrorNumbers => new TheoryData<int>(TransientNumbers);

        public static TheoryData<int> NonTransientErrorNumbers => new TheoryData<int>
        {
            547,   // constraint violation
            2627,  // unique key violation
            2601,  // unique index violation
            208,   // invalid object name
            51000, // application-raised THROW (the Orleans scripts' concurrency signal)
            50000, // RAISERROR default
            0,
            -1,
            1204,  // one below the deadlock victim
            1206,  // one above the deadlock victim
        };

        #region GetIsErrorTransient

        [Theory]
        [MemberData(nameof(TransientErrorNumbers))]
        [Trait("Spec", "blk-494b49456138442883d6833b47f978a0")]
        [Trait("Spec", "blk-82930baeafae4696a535d9c4d093f624")]
        [Trait("Spec", "blk-ead57f0f7da642f1850f13e02fc6f74d")]
        public void EveryListedSqlErrorNumberIsTransient(int number)
        {
            var exception = SqlExceptionFactory.WithNumber(number);

            exception.Number.Should().Be(number, "the factory must produce the requested error number for the test to mean anything");
            Factory.GetIsErrorTransient(exception).Should().BeTrue($"SQL error {number} is in the fixed transient set");
        }

        [Fact]
        [Trait("Spec", "blk-494b49456138442883d6833b47f978a0")]
        public void TheTransientSetHasExactlyTwentyFourEntries()
        {
            new HashSet<int>(TransientNumbers).Count.Should().Be(24, "the spec enumerates a fixed set of 24 error numbers, and this theory must cover all of them");
        }

        [Theory]
        [MemberData(nameof(NonTransientErrorNumbers))]
        [Trait("Spec", "blk-494b49456138442883d6833b47f978a0")]
        [Trait("Spec", "blk-a13d7200e66345a7827d4e51b84cff90")]
        public void AnUnlistedSqlErrorNumberIsNotTransient(int number)
        {
            Factory.GetIsErrorTransient(SqlExceptionFactory.WithNumber(number)).Should().BeFalse($"SQL error {number} is not in the fixed transient set");
        }

        [Fact]
        [Trait("Spec", "blk-df742008a88740f99388dd903c62b7d9")]
        [Trait("Spec", "blk-494b49456138442883d6833b47f978a0")]
        public void ANonSqlExceptionIsNeverTransient()
        {
            Factory.GetIsErrorTransient(new InvalidOperationException("not a SqlException")).Should().BeFalse();
            Factory.GetIsErrorTransient(new TimeoutException()).Should().BeFalse();
            Factory.GetIsErrorTransient(new OtherProviderException(1205)).Should().BeFalse("even a DbException carrying a transient-looking code is not a SqlException");
        }

        private sealed class OtherProviderException : DbException
        {
            public OtherProviderException(int code) : base("other provider", code) { }
            public override bool IsTransient => true;
        }

        #endregion

        #region NewConnection / NewCommand

        [Fact]
        [Trait("Spec", "blk-ead57f0f7da642f1850f13e02fc6f74d")]
        public void NewConnectionReturnsASqlConnectionInitializedFromTheConnectionString()
        {
            const string connectionString = "Data Source=db;Initial Catalog=app;Integrated Security=True";

            var connection = Factory.NewConnection(connectionString);

            connection.Should().BeOfType<SqlConnection>();
            connection.ConnectionString.Should().Be(connectionString);
            connection.State.Should().Be(ConnectionState.Closed, "the factory does not open the connection");
        }

        [Fact]
        [Trait("Spec", "blk-ead57f0f7da642f1850f13e02fc6f74d")]
        public void NewCommandBindsTheProcedureNameToTheSuppliedSqlConnection()
        {
            using var connection = new SqlConnection("Data Source=db;Initial Catalog=app");

            var command = Factory.NewCommand("dbo.GetAccount", connection);

            command.Should().BeOfType<SqlCommand>();
            command.CommandText.Should().Be("dbo.GetAccount");
            command.Connection.Should().BeSameAs(connection);
        }

        [Fact]
        [Trait("Spec", "blk-7954318ba5354605bd93c70f6f21b1c4")]
        [Trait("Spec", "blk-a00f7f7d845b452098e65aece83ed5ba")]
        [Trait("Spec", "blk-ead57f0f7da642f1850f13e02fc6f74d")]
        public void NewCommandRejectsAForeignConnectionTypeNamingTheConnectionParameter()
        {
            var foreign = NSubstitute.Substitute.For<DbConnection>();

            Action act = () => Factory.NewCommand("dbo.GetAccount", foreign);

            var thrown = act.Should().ThrowExactly<ArgumentException>().Which;
            thrown.ParamName.Should().Be("connection");
            thrown.Message.Should().StartWith("The connection must be a SqlConnection.");
        }

        #endregion

        #region SetParameters

        private static SqlParameterCollection Source(params SqlParameter[] parameters)
        {
            var prms = new SqlCommand().Parameters;
            foreach (var prm in parameters)
            {
                prms.Add(prm);
            }
            return prms;
        }

        [Fact]
        [Trait("Spec", "blk-0383c591b46a405389d8e13bfe15de08")]
        [Trait("Spec", "blk-c9156902e6d74713a73697f03934c01c")]
        public void ARequestedNameSelectsOnlyThatParameterAndTheReplacementValueWins()
        {
            var cmd = new SqlCommand("dbo.Proc");
            var source = Source(new SqlParameter("@id", SqlDbType.Int) { Value = 1 }, new SqlParameter("@label", SqlDbType.NVarChar, 50) { Value = "old" });
            var replacements = new Dictionary<string, object> { { "@id", 42 } };

            Factory.SetParameters(cmd, new[] { "@id" }, source, replacements);

            cmd.Parameters.Count.Should().Be(1);
            var prm = cmd.Parameters[0];
            prm.ParameterName.Should().Be("@id");
            prm.Value.Should().Be(42);
            cmd.Parameters.Contains("@label").Should().BeFalse("only requested names are copied");
            source["@id"].Value.Should().Be(1, "the replacement is applied to the copy, not the source");
        }

        [Fact]
        [Trait("Spec", "blk-41c99e355ae943869fd390dde0e251fe")]
        [Trait("Spec", "blk-c9156902e6d74713a73697f03934c01c")]
        [Trait("Spec", "blk-7df5c269006f4eeb87215f230f1e442b")]
        public void AMissingRequestedNameThrowsParameterNotFoundWithTheNormalizedName()
        {
            var cmd = new SqlCommand("dbo.Proc");
            var source = Source(new SqlParameter("@id", SqlDbType.Int) { Value = 1 });

            Action act = () => Factory.SetParameters(cmd, new[] { "missing" }, source, null);

            act.Should().ThrowExactly<ParameterNotFoundException>()
                .WithMessage("Expected parameter “@missing”, but it was not found in the parameter list.",
                    "the name is normalized with the @ prefix before lookup and that normalized name is what the exception reports");
            cmd.Parameters.Count.Should().Be(0);
        }

        [Fact]
        [Trait("Spec", "blk-c9156902e6d74713a73697f03934c01c")]
        public void AMissingNameStopsProcessingBeforeLaterNamesAreCopied()
        {
            var cmd = new SqlCommand("dbo.Proc");
            var source = Source(new SqlParameter("@a", SqlDbType.Int) { Value = 1 }, new SqlParameter("@c", SqlDbType.Int) { Value = 3 });

            Action act = () => Factory.SetParameters(cmd, new[] { "@a", "@b", "@c" }, source, null);

            act.Should().ThrowExactly<ParameterNotFoundException>().WithMessage("*“@b”*");
            cmd.Parameters.Count.Should().Be(1, "names before the missing one were copied, names after it were not");
            cmd.Parameters[0].ParameterName.Should().Be("@a");
        }

        [Fact]
        [Trait("Spec", "blk-c9156902e6d74713a73697f03934c01c")]
        [Trait("Spec", "blk-ead57f0f7da642f1850f13e02fc6f74d")]
        public void WithoutRequestedNamesEveryParameterIsCopiedInSourceOrderWithAllProperties()
        {
            var cmd = new SqlCommand("dbo.Proc");
            var rich = new SqlParameter("@name", SqlDbType.NVarChar, 80)
            {
                CompareInfo = SqlCompareOptions.IgnoreCase,
                Direction = ParameterDirection.InputOutput,
                IsNullable = true,
                LocaleId = 1033,
                Offset = 2,
                SourceColumn = "Name",
                SourceColumnNullMapping = true,
                Value = "ada",
            };
            var money = new SqlParameter("@amount", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = 12.5m };
            var tvp = new SqlParameter("@rows", SqlDbType.Structured) { TypeName = "dbo.RowType", Value = null };
            var source = Source(rich, money, tvp);

            Factory.SetParameters(cmd, Array.Empty<string>(), source, null);

            cmd.Parameters.Count.Should().Be(3);
            var copiedName = cmd.Parameters[0];
            copiedName.Should().NotBeSameAs(rich, "the destination gets an independent copy");
            copiedName.ParameterName.Should().Be("@name");
            copiedName.SqlDbType.Should().Be(SqlDbType.NVarChar);
            copiedName.DbType.Should().Be(DbType.String);
            copiedName.Size.Should().Be(80);
            copiedName.CompareInfo.Should().Be(SqlCompareOptions.IgnoreCase);
            copiedName.Direction.Should().Be(ParameterDirection.InputOutput);
            copiedName.IsNullable.Should().BeTrue();
            copiedName.LocaleId.Should().Be(1033);
            copiedName.Offset.Should().Be(2);
            copiedName.SourceColumn.Should().Be("Name");
            copiedName.SourceColumnNullMapping.Should().BeTrue();
            copiedName.Value.Should().Be("ada");
            copiedName.SqlValue.Should().Be(rich.SqlValue);

            var copiedMoney = cmd.Parameters[1];
            copiedMoney.ParameterName.Should().Be("@amount");
            copiedMoney.Precision.Should().Be(18);
            copiedMoney.Scale.Should().Be(4);
            copiedMoney.Value.Should().Be(12.5m);

            var copiedTvp = cmd.Parameters[2];
            copiedTvp.ParameterName.Should().Be("@rows");
            copiedTvp.SqlDbType.Should().Be(SqlDbType.Structured);
            copiedTvp.TypeName.Should().Be("dbo.RowType");
            copiedTvp.Value.Should().BeNull("an empty table-valued parameter travels as a null value");
        }

        [Fact]
        [Trait("Spec", "blk-c9156902e6d74713a73697f03934c01c")]
        public void CopiesAreIndependentOfTheSourceParameters()
        {
            var cmd = new SqlCommand("dbo.Proc");
            var source = Source(new SqlParameter("@a", SqlDbType.Int) { Value = 1 });
            var replacements = new Dictionary<string, object> { { "@a", 2 } };

            Factory.SetParameters(cmd, null, source, replacements);

            cmd.Parameters[0].Value.Should().Be(2, "the replacement value is bound on the command");
            source[0].Value.Should().Be(1, "the source parameter is not modified by the replacement");
            cmd.Parameters[0].Value = 99;
            source[0].Value.Should().Be(1, "mutating the copy does not touch the source");
            source[0].Value = 7;
            cmd.Parameters[0].Value.Should().Be(99, "mutating the source does not touch the copy");
        }

        [Fact]
        [Trait("Spec", "blk-c9156902e6d74713a73697f03934c01c")]
        public void ReplacementValuesAreKeyedByTheNormalizedParameterName()
        {
            var cmd = new SqlCommand("dbo.Proc");
            var source = Source(new SqlParameter("@a", SqlDbType.Int) { Value = 1 }, new SqlParameter("@b", SqlDbType.Int) { Value = 2 });
            // A replacement keyed without the @ prefix does not match a parameter named with it.
            var replacements = new Dictionary<string, object> { { "a", 10 }, { "@b", 20 } };

            Factory.SetParameters(cmd, null, source, replacements);

            cmd.Parameters["@a"].Value.Should().Be(1, "the replacement key must equal the parameter's own name");
            cmd.Parameters["@b"].Value.Should().Be(20);
        }

        [Fact]
        [Trait("Spec", "blk-c9156902e6d74713a73697f03934c01c")]
        public void RequestedNamesAreCopiedInRequestOrderNotSourceOrder()
        {
            var cmd = new SqlCommand("dbo.Proc");
            var source = Source(new SqlParameter("@a", SqlDbType.Int) { Value = 1 }, new SqlParameter("@b", SqlDbType.Int) { Value = 2 }, new SqlParameter("@c", SqlDbType.Int) { Value = 3 });

            Factory.SetParameters(cmd, new[] { "c", "@a" }, source, null);

            cmd.Parameters.Count.Should().Be(2);
            cmd.Parameters[0].ParameterName.Should().Be("@c");
            cmd.Parameters[1].ParameterName.Should().Be("@a");
        }

        #endregion
    }
}
