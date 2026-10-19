using System.Threading;
using System.Threading.Tasks;
using ArgentSea.TestKit;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// Batch model steps passed a null model instance to their result handler, so the documented Mapper handlers
    /// (which populate the instance they are given) threw ArgumentNullException when used in a batch.
    /// </summary>
    public class BatchMapperHandlerTests
    {
        public class BatchModel
        {
            [MapToSqlInt("@Id")]
            public int Id { get; set; }
        }

        [Fact]
        public async Task ShardBatchModelStepPopulatesTheModelThroughAMapperHandler()
        {
            var services = new FakeProviderServices();
            var shardSets = FakeShardSets.Builder(services).AddShardSet("Set", 1).Build();
            services.Script.ForQuery("ws.BatchModel").Returns(new[] { "Id" }, new object?[] { 7 });
            var batch = new ShardBatch<BatchModel>().Add(new QueryProcedure("ws.BatchModel"), Mapper.ModelFromReaderResultsHandler<BatchModel>);

            var result = await shardSets["Set"][1].Write.RunAsync(batch, CancellationToken.None);

            result.Should().NotBeNull();
            result!.Id.Should().Be(7);
        }

        [Fact]
        public async Task DatabaseBatchModelStepPopulatesTheModelThroughAMapperHandler()
        {
            var services = new FakeProviderServices();
            var databases = FakeDatabases.Builder(services).AddDatabase("Db").Build();
            services.Script.ForQuery("ws.BatchModel").Returns(new[] { "Id" }, new object?[] { 8 });
            var batch = new DatabaseBatch<BatchModel>().Add(new QueryProcedure("ws.BatchModel"), Mapper.ModelFromReaderResultsHandler<BatchModel>);

            var result = await databases["Db"].Write.RunAsync(batch, CancellationToken.None);

            result.Should().NotBeNull();
            result!.Id.Should().Be(8);
        }
    }
}
