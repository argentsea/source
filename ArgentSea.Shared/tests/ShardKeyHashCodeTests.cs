using FluentAssertions;
using Xunit;

namespace ArgentSea.Test
{
    public class ShardKeyHashCodeTests
    {
        // GetHashCode compared the child part's length while indexing the grandchild (and great-grandchild) part,
        // so a later part that serializes shorter than the child part threw IndexOutOfRangeException.
        [Fact]
        public void GrandChildKey_GetHashCode_LaterPartShorterThanChild_DoesNotThrow()
        {
            var key = new ShardKey<int, long, byte>(1, 2, 3L, (byte)4);
            var act = () => key.GetHashCode();
            act.Should().NotThrow();
            key.GetHashCode().Should().Be(new ShardKey<int, long, byte>(1, 2, 3L, (byte)4).GetHashCode());
        }

        [Fact]
        public void GreatGrandChildKey_GetHashCode_LaterPartsShorterThanChild_DoesNotThrow()
        {
            var key = new ShardKey<int, long, byte, byte>(1, 2, 3L, (byte)4, (byte)5);
            var act = () => key.GetHashCode();
            act.Should().NotThrow();
        }

        // The hash folded in only the shard id and record id: the child bytes were computed into a buffer that was
        // never used, so every key sharing a shard and record id collided regardless of its child parts.
        [Fact]
        public void ChildParts_ContributeToTheHash()
        {
            new ShardKey<int, int>(1, 2, 3).GetHashCode().Should().NotBe(new ShardKey<int, int>(1, 2, 4).GetHashCode());
            new ShardKey<int, int, int>(1, 2, 3, 4).GetHashCode().Should().NotBe(new ShardKey<int, int, int>(1, 2, 3, 5).GetHashCode());
            new ShardKey<int, int, int, int>(1, 2, 3, 4, 5).GetHashCode().Should().NotBe(new ShardKey<int, int, int, int>(1, 2, 3, 4, 6).GetHashCode());
        }

        [Fact]
        public void EqualKeys_HaveEqualHashes()
        {
            new ShardKey<int, string>(1, 2, "child").GetHashCode().Should().Be(new ShardKey<int, string>(1, 2, "child").GetHashCode());
            new ShardKey<string>(3, "record").GetHashCode().Should().Be(new ShardKey<string>(3, "record").GetHashCode());
        }

        // A default (uninitialized) key has null reference-type parts; hashing it threw NullReferenceException.
        [Fact]
        public void DefaultKeys_WithNullParts_CanBeHashed()
        {
            var act = () =>
            {
                default(ShardKey<string>).GetHashCode();
                default(ShardKey<string, string>).GetHashCode();
                default(ShardKey<string, string, string>).GetHashCode();
                default(ShardKey<string, string, string, string>).GetHashCode();
            };
            act.Should().NotThrow();
        }
    }
}
