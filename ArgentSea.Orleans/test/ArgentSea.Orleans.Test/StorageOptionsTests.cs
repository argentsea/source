using FluentAssertions;
using Orleans.Storage;

namespace ArgentSea.Orleans.Test
{
    public class StorageOptionsTests
    {
        [Fact]
        [Trait("Spec", "blk-b92d0339c55941f5a445fd73d5fb618c")]
        [Trait("Spec", "blk-1b90d7630d384cc0a907f1be231869a1")]
        public void DbPersistenceOptionsUseNonSerializingSerializer()
        {
            IStorageProviderSerializerOptions options = new OrleansDbPersistenceOptions();
            options.GrainStorageSerializer.Should().BeOfType<FakeGrainStorageSerializer>("ArgentSea maps grain state relationally and never serializes it");
        }

        [Fact]
        [Trait("Spec", "blk-b92d0339c55941f5a445fd73d5fb618c")]
        [Trait("Spec", "blk-423b79d3955d40e883835d9cc1f8b854")]
        public void ShardPersistenceOptionsUseNonSerializingSerializer()
        {
            IStorageProviderSerializerOptions options = new OrleansShardPersistenceOptions();
            options.GrainStorageSerializer.Should().BeOfType<FakeGrainStorageSerializer>("ArgentSea maps grain state relationally and never serializes it");
        }

        [Fact]
        [Trait("Spec", "blk-b92d0339c55941f5a445fd73d5fb618c")]
        public void FakeSerializerIsANoOp()
        {
            var serializer = new FakeGrainStorageSerializer();
            serializer.Serialize(new { Value = 1 }).ToArray().Should().BeEmpty();
            serializer.Deserialize<string>(BinaryData.FromString("ignored")).Should().BeNull();
        }
    }
}
