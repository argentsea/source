using System;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Pg.Test
{
    public class CopyTableNameTests
    {
        [Theory]
        [InlineData("tmp")]
        [InlineData("_tmp")]
        [InlineData("Tmp$1")]
        [InlineData("public.tmp")]
        [InlineData("_s1.t_2$")]
        [Trait("Spec", "blk-ed22ac024e0a4447846ebaa368d10180")]
        public void ValidNamesAreAccepted(string tableName)
        {
            Action act = () => CopyMapper.ValidatePgTableName(tableName);
            act.Should().NotThrow();
        }

        [Fact]
        public void EachPartMayBeSixtyThreeCharacters()
        {
            var part = new string('a', 63);
            Action act = () => CopyMapper.ValidatePgTableName($"{part}.{part}");
            act.Should().NotThrow("the 63-character NAMEDATALEN limit applies per identifier, not to the whole name");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("foo..bar")]
        [InlineData("foo.")]
        [InlineData(".foo")]
        [InlineData(".")]
        [InlineData("a.b.c")]
        [InlineData("a.b.c.d")]
        [InlineData("1abc")]
        [InlineData("public.1abc")]
        [InlineData("foo-bar")]
        [InlineData("foo\"bar")]
        [InlineData("public.foo bar")]
        [Trait("Spec", "blk-ed22ac024e0a4447846ebaa368d10180")]
        public void InvalidNamesAreRejected(string tableName)
        {
            Action act = () => CopyMapper.ValidatePgTableName(tableName);
            act.Should().Throw<Exception>();
        }

        [Fact]
        public void PartLongerThanSixtyThreeCharactersIsRejected()
        {
            var tooLong = new string('a', 64);
            Action act = () => CopyMapper.ValidatePgTableName($"public.{tooLong}");
            act.Should().Throw<Exception>().WithMessage("*63 characters*");
        }

        [Theory]
        [InlineData("tmp", "\"tmp\"")]
        [InlineData("public.tmp", "\"public\".\"tmp\"")]
        [Trait("Spec", "blk-ed22ac024e0a4447846ebaa368d10180")]
        public void QuotedNameQuotesEachPart(string tableName, string expected)
        {
            CopyMapper.QuotePgTableName(tableName).Should().Be(expected);
        }

        [Fact]
        public void QuotingValidatesTheName()
        {
            Action act = () => CopyMapper.QuotePgTableName("a.b.c");
            act.Should().Throw<Exception>().WithMessage("*at most one*");
        }
    }
}
