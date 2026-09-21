// © John Hicks. All rights reserved. Licensed under the MIT license.
// See the LICENSE file in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// Driver-backed verification that an empty table-valued parameter (sent as a null <see cref="SqlParameter.Value"/>,
    /// per the fix in SqlParameterCollectionExtensions) round-trips through Microsoft.Data.SqlClient and SQL Server as
    /// a zero-row table, and that a populated collection still round-trips its rows. This exercises the actual driver
    /// and server, which the unit tests in CollectionMapWriteTests.cs cannot: those confirm the .NET-side value the
    /// library produces, not that the driver accepts it. Requires a live SQL Server / LocalDB instance and is skipped
    /// unless the ARGENTSEA_SQL_TEST_CONNECTION environment variable is set.
    /// </summary>
    public class TvpEmptyCollectionIntegrationTests
    {
        private const string SkipReason = "ARGENTSEA_SQL_TEST_CONNECTION environment variable is not set; skipping driver-backed TVP integration test.";
        private static readonly string ConnectionString = Environment.GetEnvironmentVariable("ARGENTSEA_SQL_TEST_CONNECTION");

        private readonly ITestOutputHelper _output;

        public TvpEmptyCollectionIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void EmptyAndNonEmptyTvp_RoundTripThroughSqlServer_AsZeroRowsAndPopulatedRows()
        {
            if (string.IsNullOrEmpty(ConnectionString))
            {
                _output.WriteLine(SkipReason);
                return;
            }

            using var connection = new SqlConnection(ConnectionString);
            connection.Open();

            EnsureTestTypeAndProcedureExist(connection);

            var emptyCount = ExecuteChildCountProc(connection, new List<CollectionWriteChild>());
            emptyCount.Should().Be(0, "an empty collection must be sent as a zero-row table-valued parameter and accepted by the driver, not rejected client-side");

            var twoElementCount = ExecuteChildCountProc(connection, new List<CollectionWriteChild>
            {
                new CollectionWriteChild { ChildId = 1, ChildName = "One" },
                new CollectionWriteChild { ChildId = 2, ChildName = "Two" },
            });
            twoElementCount.Should().Be(2, "a populated collection must still round-trip its rows through the driver");
        }

        private static void EnsureTestTypeAndProcedureExist(SqlConnection connection)
        {
            using (var checkTypeCmd = new SqlCommand(
                "SELECT COUNT(*) FROM sys.types WHERE is_table_type = 1 AND name = 'ArgentSeaTvpTestType'", connection))
            {
                var typeExists = (int)checkTypeCmd.ExecuteScalar() > 0;
                if (!typeExists)
                {
                    using var createTypeCmd = new SqlCommand(
                        "CREATE TYPE dbo.ArgentSeaTvpTestType AS TABLE (ChildId int, ChildName nvarchar(100))", connection);
                    createTypeCmd.ExecuteNonQuery();
                }
            }

            using var createProcCmd = new SqlCommand(
                "CREATE OR ALTER PROCEDURE dbo.ArgentSeaTvpTestProc @Children dbo.ArgentSeaTvpTestType READONLY AS SELECT COUNT(*) FROM @Children",
                connection);
            createProcCmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Builds the @Children table-valued parameter the same way production code would (via CreateInputParameters),
        /// then copies its name, SqlDbType and Value onto a SqlCommand's own SqlParameterCollection: the library's
        /// ParameterCollection is a standalone DbParameterCollection implementation, not the SqlCommand's own, so its
        /// parameters cannot be handed to SqlCommand directly. TypeName is set to the schema-qualified test table type
        /// created by EnsureTestTypeAndProcedureExist rather than copied from the source parameter, because
        /// CollectionWriteParent's [MapToSqlTableValuedParameter] attribute declares the unqualified "ChildTableType"
        /// name used by the unit tests in CollectionMapWriteTests.cs, which do not touch a real database and so never
        /// need that name to resolve to an actual SQL Server type.
        /// </summary>
        private static int ExecuteChildCountProc(SqlConnection connection, List<CollectionWriteChild> children)
        {
            var dbLogger = new DebugLogger();
            var prms = new ParameterCollection();
            var model = new CollectionWriteParent
            {
                Id = 1,
                Name = "Parent",
                Children = children
            };
            prms.CreateInputParameters<CollectionWriteParent>(model, dbLogger);

            var sourcePrm = (SqlParameter)prms["@Children"];

            using var cmd = new SqlCommand("dbo.ArgentSeaTvpTestProc", connection)
            {
                CommandType = CommandType.StoredProcedure
            };
            var destPrm = new SqlParameter(sourcePrm.ParameterName, sourcePrm.SqlDbType)
            {
                TypeName = "dbo.ArgentSeaTvpTestType",
                Value = sourcePrm.Value
            };
            cmd.Parameters.Add(destPrm);

            return (int)cmd.ExecuteScalar();
        }
    }
}
