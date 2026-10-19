using FluentAssertions;
using NSubstitute;
using System;
using System.Data.Common;
using Xunit;

namespace ArgentSea.Test
{
    public class DbParameterNullValueTests
    {
        // A parameter whose Value was never set (for example, the output parameter of a command that did not run)
        // holds null rather than DBNull; unboxing it threw NullReferenceException.
        [Fact]
        [Trait("Spec", "blk-b5431f752ce0477e96509c975f1833d9")]
        public void DbParameter_GetInteger_NullValue_ThrowsUnexpectedNull()
        {
            var prm = Substitute.For<DbParameter>();
            prm.Value.Returns((object?)null);
            prm.ParameterName.Returns("@Id");

            var act = () => prm.GetInteger();
            act.Should().Throw<UnexpectedNullException>();
        }

        [Fact]
        [Trait("Spec", "blk-0eb07a4f476d4b2b8ed9b1c814c31d6b")]
        [Trait("Spec", "blk-1061beb1fd4448749b2813c5324db684")]
        public void DbParameter_GetDoubleAndGuid_NullValue_TreatedAsDbNull()
        {
            var prm = Substitute.For<DbParameter>();
            prm.Value.Returns((object?)null);

            double.IsNaN(prm.GetDouble()).Should().BeTrue();
            prm.GetGuid().Should().Be(Guid.Empty);
            prm.GetNullableGuid().Should().BeNull();
        }
    }
}
