using FluentAssertions;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// TvpMapper logged cache hits/misses (and its generated code logged per-property traces) through SqlLoggingExtensions
    /// methods that dereferenced the logger, so building a table-valued parameter row with no logger crashed.
    /// </summary>
    public class TvpNullLoggerTests
    {
        public class TvpNullLoggerModel
        {
            [MapToSqlInt("@TvpId")]
            public int Id { get; set; }
        }

        [Fact]
        public void ToTvpRecordWithoutLogger()
        {
            var record = TvpMapper.ToTvpRecord(new TvpNullLoggerModel { Id = 11 }, null);

            record.GetInt32(0).Should().Be(11);
        }
    }
}
