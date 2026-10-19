using System.Data;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// The logger is optional throughout ArgentSea, but the Mapper logged cache hits/misses (and its generated code logged
    /// per-property traces) through LoggingExtensions methods that dereferenced the logger, so mapping with no logger crashed.
    /// </summary>
    public class NullLoggerMapperTests
    {
        public class NullLoggerModel
        {
            [MapToSqlInt("@NullLoggerId")]
            public int Id { get; set; }

            [MapToSqlNVarChar("@NullLoggerName", 50)]
            public string? Name { get; set; }
        }

        [Fact]
        public void CreateInputParametersWithoutLogger()
        {
            var prms = new SqlCommand().Parameters;

            prms.CreateInputParameters(new NullLoggerModel { Id = 3, Name = "three" }, null);

            prms["@NullLoggerId"].Value.Should().Be(3);
            prms["@NullLoggerName"].Value.Should().Be("three");
        }

        [Fact]
        public void ToListWithoutLogger()
        {
            var table = new DataTable();
            table.Columns.Add("NullLoggerId", typeof(int));
            // The Name column is deliberately absent: a missing column is logged (SqlFieldNotFound) from generated code.
            table.Rows.Add(5);

            var result = Mapper.ToList<NullLoggerModel>(table.CreateDataReader(), 0, null);

            result.Should().ContainSingle().Which.Id.Should().Be(5);
        }
    }
}
