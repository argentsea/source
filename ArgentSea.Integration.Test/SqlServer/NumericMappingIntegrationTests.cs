// Copyright John Hicks. Licensed under the MIT license.
using System.Data;
using System.Reflection;
using System.Runtime.ExceptionServices;
using ArgentSea.Integration.Test.Fixtures;
using ArgentSea.Sql;
using ArgentSea.Sql.Test;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace ArgentSea.Integration.Test.SqlServer;

/// <summary>
/// Drives the numeric-mapping cases of ArgentSea.Sql.Test/NumericMappingTests.cs through a real SQL Server: input
/// parameters, readers, output parameters and table-valued parameters for every integer and binary width, plus the
/// binary-ordering contract. Runs against the SQL Server container (or ARGENTSEA_SQL_TEST_CONNECTION, which needs
/// CREATE TYPE permission) and is skipped when neither is available.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class NumericMappingIntegrationTests
{
    private readonly SqlServerFixture _fixture;
    private readonly ITestOutputHelper _output;

    public NumericMappingIntegrationTests(SqlServerFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [SkippableFact]
    [Trait("Spec", "blk-a66adaef2f1d429bb26b2e05e419f643")]
    public void ParametersReadersOutputsAndTvpsRoundTrip()
    {
        _fixture.SkipUnlessAvailable();
        using var connection = Open();
        foreach (var group in NumericMappingTests.Cases().GroupBy(c => (Type)c[0]!))
        {
            try
            {
                GetType().GetMethod(nameof(CheckModel), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(group.Key).Invoke(null, new object[] { connection, group.ToArray() });
            }
            catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); }
        }
        var maximum = new byte[] { 255, 255, 255, 255, 255, 255, 255, 255 };
        CheckModel<VarBinaryMaxModel<ulong>>(connection, new[] { new object[] { typeof(VarBinaryMaxModel<ulong>), ulong.MaxValue, maximum, SqlDbType.VarBinary } });
        CheckModel<VarBinaryWideModel<ulong>>(connection, new[] { new object[] { typeof(VarBinaryWideModel<ulong>), ulong.MaxValue, maximum, SqlDbType.VarBinary } });
        // Also retain the pre-existing byte[] null / empty / populated TVP conventions.
        CheckModel<VarBinary8Model<byte[]>>(connection, new[] {
            new object?[] { typeof(VarBinary8Model<byte[]>), null, DBNull.Value, SqlDbType.VarBinary },
            new object[] { typeof(VarBinary8Model<byte[]>), Array.Empty<byte>(), Array.Empty<byte>(), SqlDbType.VarBinary },
            new object[] { typeof(VarBinary8Model<byte[]>), new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }, SqlDbType.VarBinary }
        });
        CheckModel<Binary8Model<byte[]>>(connection, new[] {
            new object?[] { typeof(Binary8Model<byte[]>), null, DBNull.Value, SqlDbType.Binary },
            new object[] { typeof(Binary8Model<byte[]>), new byte[8], new byte[8], SqlDbType.Binary }
        });
    }

    private SqlConnection Open()
    {
        var connection = _fixture.Open();
        _output.WriteLine("SQL Server version: " + connection.ServerVersion);
        return connection;
    }

    private static void CheckModel<TModel>(SqlConnection connection, object?[][] cases) where TModel : new()
    {
        var logger = new DebugLogger();
        var prop = typeof(TModel).GetProperty("Value")!;
        var sqlType = (SqlDbType)cases[0][3]!;
        using var shape = new SqlCommand();
        shape.Parameters.CreateOutputParameters<TModel>(logger);
        var size = shape.Parameters["@Value"].Size;
        var sqlDeclaration = sqlType switch
        {
            SqlDbType.BigInt => "bigint",
            SqlDbType.Int => "int",
            SqlDbType.SmallInt => "smallint",
            SqlDbType.TinyInt => "tinyint",
            SqlDbType.Binary => $"binary({size})",
            _ => $"varbinary({(size == -1 ? "max" : size.ToString())})"
        };
        var typeName = "dbo.ArgentSeaNumeric_" + Guid.NewGuid().ToString("N");
        using (var create = new SqlCommand($"CREATE TYPE {typeName} AS TABLE (Value {sqlDeclaration} NULL)", connection))
            create.ExecuteNonQuery();
        try
        {
            foreach (var item in cases)
            {
                var model = new TModel();
                prop.SetValue(model, item[1]);
                using var input = new SqlCommand("SELECT @Value AS Value", connection);
                input.Parameters.CreateInputParameters(model, logger);
                using (var reader = input.ExecuteReader())
                    NumericMappingTests.AssertValue(item[1], prop.GetValue(reader.ToModel<TModel>(logger)));

                using var outCommand = new SqlCommand("SET @Value = @Source", connection);
                outCommand.Parameters.CreateOutputParameters<TModel>(logger);
                outCommand.Parameters.Add(new SqlParameter("@Source", sqlType, size) { Value = item[2] });
                outCommand.ExecuteNonQuery();
                NumericMappingTests.AssertValue(item[2], outCommand.Parameters["@Value"].Value);
                NumericMappingTests.AssertValue(item[1], prop.GetValue(outCommand.Parameters.ToModel<TModel>(logger)));

                using var tvp = new SqlCommand("SELECT Value FROM @Rows", connection);
                tvp.Parameters.Add(new SqlParameter("@Rows", SqlDbType.Structured)
                {
                    TypeName = typeName,
                    Value = new[] { TvpMapper.ToTvpRecord(model, logger) }
                });
                using (var reader = tvp.ExecuteReader())
                    NumericMappingTests.AssertValue(item[1], prop.GetValue(reader.ToModel<TModel>(logger)));
            }
            // Existing driver convention: a null TVP Value represents an empty table.
            using var empty = new SqlCommand("SELECT COUNT(*) FROM @Rows", connection);
            empty.Parameters.Add(new SqlParameter("@Rows", SqlDbType.Structured) { TypeName = typeName, Value = null });
            Assert.Equal(0, empty.ExecuteScalar());
        }
        finally
        {
            using var drop = new SqlCommand($"DROP TYPE {typeName}", connection);
            drop.ExecuteNonQuery();
        }
    }

    [SkippableFact]
    public void BinaryOrderingAndSameWidthCastsMatchTheContract()
    {
        _fixture.SkipUnlessAvailable();
        using var connection = Open();
        foreach (var group in NumericMappingTests.IntegerValues().GroupBy(c => c.Value.GetType()))
        {
            var values = group.ToArray();
            foreach (var value in values)
            {
                using var cast = new SqlCommand($"SELECT CAST(@Value AS binary({value.Bytes.Length}))", connection);
                cast.Parameters.Add(new SqlParameter("@Value", value.IntegerType) { Value = value.Provider });
                NumericMappingTests.AssertValue(value.Bytes, cast.ExecuteScalar());
            }
            foreach (var binaryType in new[] { SqlDbType.Binary, SqlDbType.VarBinary })
            {
                using var order = new SqlCommand { Connection = connection };
                var rows = new List<string>();
                for (int i = 0; i < values.Length; i++)
                {
                    rows.Add($"({i}, @I{i}, @B{i})");
                    order.Parameters.Add(new SqlParameter($"@I{i}", values[i].IntegerType) { Value = values[i].Provider });
                    order.Parameters.Add(new SqlParameter($"@B{i}", binaryType, values[i].Bytes.Length) { Value = values[i].Bytes });
                }
                var source = "(VALUES " + string.Join(",", rows) + ") AS v(Id, IntegerValue, BinaryValue)";
                var clrOrder = Enumerable.Range(0, values.Length).OrderBy(i => values[i].Value).ToArray();
                order.CommandText = "SELECT Id FROM " + source + " ORDER BY BinaryValue";
                var binaryOrder = ReadIds(order);
                if (group.Key == typeof(sbyte)) Assert.False(clrOrder.SequenceEqual(binaryOrder));
                else Assert.Equal(clrOrder, binaryOrder);
                order.CommandText = "SELECT Id FROM " + source + " ORDER BY IntegerValue";
                Assert.False(clrOrder.SequenceEqual(ReadIds(order)));
            }
        }
    }

    private static int[] ReadIds(SqlCommand command)
    {
        var ids = new List<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids.ToArray();
    }
}
