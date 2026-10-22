using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// The default retry lengthening is the odd-number progression (1, 3, 5, 7… × RetryInterval),
    /// and the obsolete "Fibonacci" name still binds to it from configuration.
    /// </summary>
    public class RetryLengtheningTests
    {
        [Fact]
        [Trait("Spec", "blk-72ccc2fd25164b36b613de540315a781")]
        public void LengtheningIsTheOddNumberProgression()
        {
            var config = new SqlGlobalPropertiesOptions { RetryInterval = 10, RetryLengthening = SequenceLengthening.Lengthening };
            config.GetRetryTimespan(1).Should().Be(TimeSpan.FromMilliseconds(10));
            config.GetRetryTimespan(2).Should().Be(TimeSpan.FromMilliseconds(30));
            config.GetRetryTimespan(3).Should().Be(TimeSpan.FromMilliseconds(50));
            config.GetRetryTimespan(4).Should().Be(TimeSpan.FromMilliseconds(70));
        }

        [Fact]
        public void LengtheningIsTheDefault()
        {
            var unset = new SqlGlobalPropertiesOptions { RetryInterval = 10 };
            var explicitLengthening = new SqlGlobalPropertiesOptions { RetryInterval = 10, RetryLengthening = SequenceLengthening.Lengthening };
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                unset.GetRetryTimespan(attempt).Should().Be(explicitLengthening.GetRetryTimespan(attempt));
            }
        }

        [Theory]
        [InlineData("Lengthening")]
        [InlineData("Fibonacci")]
        [InlineData("fibonacci")]
        [Trait("Spec", "blk-94549f430b8c4cb99db26efb95e3a4e2")]
        public void ConfigurationNameBindsToLengthening(string configuredName)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["RetryLengthening"] = configuredName })
                .Build();

            var bound = configuration.Get<SqlGlobalPropertiesOptions>();

            bound!.RetryLengthening.Should().Be(SequenceLengthening.Lengthening);
        }

        [Fact]
        [Trait("Spec", "blk-94549f430b8c4cb99db26efb95e3a4e2")]
        public void ObsoleteFibonacciAliasHasTheLengtheningValue()
        {
#pragma warning disable CS0618 // the obsolete alias is the subject of this test
            ((int)SequenceLengthening.Fibonacci).Should().Be((int)SequenceLengthening.Lengthening);
#pragma warning restore CS0618
        }
    }
}
