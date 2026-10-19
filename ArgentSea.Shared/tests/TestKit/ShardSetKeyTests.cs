using ArgentSea.TestKit;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// ShardSet.Key was never assigned, so it was always null.
    /// </summary>
    public class ShardSetKeyTests
    {
        [Fact]
        public void ShardSetKeyIsTheConfiguredShardSetName()
        {
            var shardSets = FakeShardSets.Builder(new FakeProviderServices()).AddShardSet("Customers", 1, 2).Build();

            shardSets["Customers"].Key.Should().Be("Customers");
        }
    }
}
