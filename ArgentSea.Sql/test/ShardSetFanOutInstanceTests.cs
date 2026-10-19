using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// The ShardSet fan-out methods (ReadAll / Write) passed a null model instance to the Mapper result handlers,
    /// so every Map* call threw as soon as a shard returned a row. These tests run the fan-out against an in-memory reader.
    /// </summary>
    public class ShardSetFanOutInstanceTests
    {
        public class FanOutModel
        {
            [MapToSqlInt("@Id")]
            public int Id { get; set; }
        }

        [Fact]
        public async Task ReadAllMapReaderPopulatesAModelPerShard()
        {
            var shardSet = InMemoryShardSets.FromTestConfiguration()["Inherit"];

            var result = await shardSet.ReadAll.MapReaderAsync<FanOutModel>(new QueryProcedure("ws.FanOutTest"), new ParameterCollection(), CancellationToken.None);

            result.Should().HaveCount(shardSet.Count);
            result.Should().OnlyContain(m => m.Id == 7);
        }

        [Fact]
        public async Task WriteMapReaderPopulatesAModelPerShard()
        {
            var shardSet = InMemoryShardSets.FromTestConfiguration()["Inherit"];

            var result = await shardSet.Write.MapReaderAsync<FanOutModel>(new QueryProcedure("ws.FanOutTest"), new ParameterCollection(), CancellationToken.None);

            result.Should().HaveCount(shardSet.Count);
            result.Should().OnlyContain(m => m.Id == 7);
        }
    }
}
