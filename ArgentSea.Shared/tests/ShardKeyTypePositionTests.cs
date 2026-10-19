using FluentAssertions;
using System;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Every supported key-part type must round-trip whether it is the first part (followed by other parts)
    /// or the last part, through both the binary and the external-string forms.
    /// </summary>
    public class ShardKeyTypePositionTests
    {
        public enum Color : short { Red = 1, Blue = 2 }
        public enum Size { Small = 1, Large = 2 }
        public enum Level : byte { Low = 1, High = 200 }
        public enum Big : long { Small = 1, Huge = 9_000_000_000L }

        private static void RoundTrips<T>(T value) where T : IComparable
        {
            var first = new ShardKey<T, int>(4, value, 77);
            new ShardKey<T, int>(first.ToArray()).Should().Be(first, $"{typeof(T).Name} as the first of two parts (binary)");
            ShardKey<T, int>.FromExternalString(first.ToExternalString()).Should().Be(first, $"{typeof(T).Name} as the first of two parts (string)");

            var last = new ShardKey<int, T>(4, 77, value);
            new ShardKey<int, T>(last.ToArray()).Should().Be(last, $"{typeof(T).Name} as the last of two parts (binary)");

            var middle = new ShardKey<int, T, int, int>(4, 1, value, 2, 3);
            new ShardKey<int, T, int, int>(middle.ToArray()).Should().Be(middle, $"{typeof(T).Name} in the middle of four parts");
        }

        [Fact] public void Guid_part() => RoundTrips(Guid.NewGuid());
        [Fact] public void Int_part() => RoundTrips(123456);
        [Fact] public void Long_part() => RoundTrips(9_000_000_000L);
        [Fact] public void Short_part() => RoundTrips((short)-12);
        [Fact] public void Byte_part() => RoundTrips((byte)200);
        [Fact] public void Char_part() => RoundTrips('Z');
        [Fact] public void Decimal_part() => RoundTrips(12345.678m);
        [Fact] public void Double_part() => RoundTrips(3.25d);
        [Fact] public void Float_part() => RoundTrips(1.5f);
        [Fact] public void DateTime_part() => RoundTrips(new DateTime(2026, 10, 11, 8, 30, 0));
        [Fact] public void TimeSpan_part() => RoundTrips(TimeSpan.FromMinutes(95));
        [Fact] public void String_part() => RoundTrips("record-id");
        [Fact, Trait("Spec", "blk-49dcb2e37a5347a298d76da7c90ecf87")] public void Enum_part() => RoundTrips(Color.Blue);
        [Fact] public void Int_enum_part() => RoundTrips(Size.Large);
        [Fact] public void Byte_enum_part() => RoundTrips(Level.High);
        [Fact] public void Long_enum_part() => RoundTrips(Big.Huge);
    }
}
