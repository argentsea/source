using FluentAssertions;
using System;
using Xunit;

namespace ArgentSea.Test
{
    public class ShardKeyStringLimitTests
    {
        [Fact]
        public void String_part_of_127_utf8_bytes_round_trips()
        {
            var key = new ShardKey<string>(3, new string('a', 127));
            new ShardKey<string>(key.ToArray()).Should().Be(key);
        }

        [Fact]
        [Trait("Spec", "blk-faee456447084061bb18f1b1bbcab911")]
        public void String_part_of_128_utf8_bytes_is_rejected_with_a_clear_exception()
        {
            var key = new ShardKey<string>(3, new string('a', 128));
            Action act = () => key.ToArray();
            act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*127 UTF-8 bytes*");
        }

        [Fact]
        public void Limit_is_measured_in_utf8_bytes_not_characters()
        {
            // 64 two-byte characters = 128 UTF-8 bytes.
            var key = new ShardKey<string>(3, new string('é', 64));
            Action act = () => key.ToArray();
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
