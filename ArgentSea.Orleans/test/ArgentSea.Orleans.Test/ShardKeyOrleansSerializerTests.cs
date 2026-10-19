using System.Buffers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;

namespace ArgentSea.Orleans.Test
{
    /// <summary>
    /// The ShardKey codecs must frame their field the way Orleans frames any LengthPrefixed field, so that
    /// (a) a key of any size round-trips and (b) Orleans' generic SkipField — used for version tolerance —
    /// lands exactly on the next field.
    /// </summary>
    public class ShardKeyOrleansSerializerTests
    {
        private static readonly SerializerSessionPool Pool =
            new ServiceCollection().AddSerializer().BuildServiceProvider().GetRequiredService<SerializerSessionPool>();

        private const int Sentinel = 12345;
        private static string S(int length, char c) => new(c, length);

        public static TheoryData<int> PartLengths => new() { 10, 60, 127 };

        private static byte[] WriteThenSentinel<T>(IFieldCodec<T> codec, T value)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using var session = Pool.GetSession();
            var writer = Writer.Create(buffer, session);
            codec.WriteField(ref writer, 0, typeof(T), value);
            Int32Codec.WriteField(ref writer, 1, Sentinel);
            writer.Commit();
            return buffer.WrittenMemory.ToArray();
        }

        private static void AssertRoundTripsAndSkips<T>(IFieldCodec<T> codec, T value)
        {
            var bytes = WriteThenSentinel(codec, value);

            using (var session = Pool.GetSession())
            {
                var reader = Reader.Create(bytes, session);
                var back = codec.ReadValue(ref reader, reader.ReadFieldHeader());
                back.Should().Be(value);
                Int32Codec.ReadValue(ref reader, reader.ReadFieldHeader()).Should().Be(Sentinel);
            }
            using (var session = Pool.GetSession())
            {
                var reader = Reader.Create(bytes, session);
                reader.SkipField(reader.ReadFieldHeader());
                Int32Codec.ReadValue(ref reader, reader.ReadFieldHeader()).Should().Be(Sentinel,
                    "Orleans' generic SkipField must step over the whole ShardKey field");
            }
        }

        [Theory, MemberData(nameof(PartLengths))]
        [Trait("Spec", "blk-67158cfafab944f28c38b6bafc5ae730")]
        [Trait("Spec", "blk-29988f43ddb2412291fb928738c7af65")]
        [Trait("Spec", "blk-7c6d2e6cd68a493eb67e0ac3f9adba35")]
        public void OnePartKey_RoundTripsAndSkips(int length)
            => AssertRoundTripsAndSkips(new ShardKeyOrleansSerializer<string>(),
                new ShardKey<string>(7, S(length, 'a')));

        [Theory, MemberData(nameof(PartLengths))]
        [Trait("Spec", "blk-67158cfafab944f28c38b6bafc5ae730")]
        [Trait("Spec", "blk-29988f43ddb2412291fb928738c7af65")]
        public void TwoPartKey_RoundTripsAndSkips(int length)
            => AssertRoundTripsAndSkips(new ShardKeyOrleansSerializer<string, string>(),
                new ShardKey<string, string>(7, S(length, 'a'), S(length, 'b')));

        [Theory, MemberData(nameof(PartLengths))]
        [Trait("Spec", "blk-67158cfafab944f28c38b6bafc5ae730")]
        [Trait("Spec", "blk-29988f43ddb2412291fb928738c7af65")]
        public void ThreePartKey_RoundTripsAndSkips(int length)
            => AssertRoundTripsAndSkips(new ShardKeyOrleansSerializer<string, string, string>(),
                new ShardKey<string, string, string>(7, S(length, 'a'), S(length, 'b'), S(length, 'c')));

        [Theory, MemberData(nameof(PartLengths))]
        [Trait("Spec", "blk-67158cfafab944f28c38b6bafc5ae730")]
        [Trait("Spec", "blk-29988f43ddb2412291fb928738c7af65")]
        public void FourPartKey_RoundTripsAndSkips(int length)
            => AssertRoundTripsAndSkips(new ShardKeyOrleansSerializer<string, string, string, string>(),
                new ShardKey<string, string, string, string>(7, S(length, 'a'), S(length, 'b'), S(length, 'c'), S(length, 'd')));

        [Fact]
        [Trait("Spec", "blk-67158cfafab944f28c38b6bafc5ae730")]
        [Trait("Spec", "blk-7c6d2e6cd68a493eb67e0ac3f9adba35")]
        public void LargestKey_ExceedsTheOldOneByteLimit_AndStillRoundTrips()
        {
            var key = new ShardKey<string, string, string, string>(7, S(127, 'a'), S(127, 'b'), S(127, 'c'), S(127, 'd'));
            key.ToArray().Length.Should().BeGreaterThan(255);
            AssertRoundTripsAndSkips(new ShardKeyOrleansSerializer<string, string, string, string>(), key);
        }

        [Fact]
        [Trait("Spec", "blk-67158cfafab944f28c38b6bafc5ae730")]
        public void FixedWidthKey_RoundTripsAndSkips()
            => AssertRoundTripsAndSkips(new ShardKeyOrleansSerializer<Guid, int, long, short>(),
                new ShardKey<Guid, int, long, short>(3, Guid.NewGuid(), 42, 9_000_000_000L, 5));
    }
}
