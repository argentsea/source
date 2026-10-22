using System.Buffers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Session;
using Orleans.Serialization.WireProtocol;

namespace ArgentSea.Orleans.Test
{
    /// <summary>
    /// Spec section shard-key-orleans-codecs: the ShardKeyOrleansSerializer family's non-reference payload framing, the
    /// reference-field short-circuit, DeepCopy for every arity and the registration attributes.
    /// </summary>
    public class ShardKeyOrleansCodecTests
    {
        private static readonly SerializerSessionPool Pool =
            new ServiceCollection().AddSerializer().BuildServiceProvider().GetRequiredService<SerializerSessionPool>();

        private const int Sentinel = 777;

        [Fact]
        [Trait("Spec", "blk-c09c3160f9124722811f0ce04343c268")]
        [Trait("Spec", "blk-67158cfafab944f28c38b6bafc5ae730")]
        [Trait("Spec", "blk-7c6d2e6cd68a493eb67e0ac3f9adba35")]
        [Trait("Spec", "blk-29988f43ddb2412291fb928738c7af65")]
        public void ANonReferenceFieldIsALengthPrefixedVarUInt32LengthFollowedByTheKeyBytes()
        {
            var key = new ShardKey<int>(42, 123456);
            var codec = new ShardKeyOrleansSerializer<int>();
            var buffer = new ArrayBufferWriter<byte>();
            using (var session = Pool.GetSession())
            {
                var writer = Writer.Create(buffer, session);
                codec.WriteField(ref writer, 0, typeof(ShardKey<int>), key);
                Int32Codec.WriteField(ref writer, 1, Sentinel);
                writer.Commit();
            }
            var expectedBytes = key.ToArray().ToArray();

            using (var session = Pool.GetSession())
            {
                var reader = Reader.Create(buffer.WrittenMemory, session);
                var field = reader.ReadFieldHeader();
                field.WireType.Should().Be(WireType.LengthPrefixed, "a struct value is never written as a reference");
                var length = reader.ReadVarUInt32();
                length.Should().Be((uint)expectedBytes.Length);
                reader.ReadBytes(length).ToArray().Should().Equal(expectedBytes, "the payload is the key's ToArray representation");
                Int32Codec.ReadValue(ref reader, reader.ReadFieldHeader()).Should().Be(Sentinel);
            }

            using (var session = Pool.GetSession())
            {
                var reader = Reader.Create(buffer.WrittenMemory, session);
                var decoded = codec.ReadValue(ref reader, reader.ReadFieldHeader());
                decoded.Should().Be(key);
                decoded.ShardId.Should().Be(42);
                decoded.RecordId.Should().Be(123456);
                Int32Codec.ReadValue(ref reader, reader.ReadFieldHeader()).Should().Be(Sentinel, "exactly the length-prefixed bytes are consumed");
            }
        }

        [Fact]
        [Trait("Spec", "blk-67158cfafab944f28c38b6bafc5ae730")]
        [Trait("Spec", "blk-aa4b5e2417884c8596bb5d00f88d4a5e")]
        public void AReferenceFieldIsResolvedThroughOrleansReferenceHandlingWithoutReadingAnyKeyBytes()
        {
            // A ShardKey is a struct, so Orleans never writes one as a reference itself; the reader still honours a reference
            // field that points at the key it recorded when it read the first (length-prefixed) occurrence.
            var key = new ShardKey<int>(42, 123456);
            var codec = new ShardKeyOrleansSerializer<int>();
            var buffer = new ArrayBufferWriter<byte>();
            using (var session = Pool.GetSession())
            {
                var writer = Writer.Create(buffer, session);
                codec.WriteField(ref writer, 0, typeof(ShardKey<int>), key);
                writer.WriteFieldHeader(1, typeof(ShardKey<int>), typeof(ShardKey<int>), WireType.Reference);
                writer.WriteVarUInt32(1); // the reference id the first field was recorded under
                Int32Codec.WriteField(ref writer, 1, Sentinel);
                writer.Commit();
            }

            using var readSession = Pool.GetSession();
            var reader = Reader.Create(buffer.WrittenMemory, readSession);
            codec.ReadValue(ref reader, reader.ReadFieldHeader()).Should().Be(key);
            var field = reader.ReadFieldHeader();
            field.WireType.Should().Be(WireType.Reference);

            var decoded = codec.ReadValue(ref reader, field);

            decoded.Should().Be(key, "the value comes from Orleans' reference handling, which recorded the first occurrence");
            Int32Codec.ReadValue(ref reader, reader.ReadFieldHeader()).Should().Be(Sentinel, "no length or key bytes are read for a reference field");
        }

        [Fact]
        [Trait("Spec", "blk-67158cfafab944f28c38b6bafc5ae730")]
        public void WritingTheSameKeyTwiceInOneSessionNeverProducesAReferenceField()
        {
            var key = new ShardKey<int>(1, 2);
            var codec = new ShardKeyOrleansSerializer<int>();
            var buffer = new ArrayBufferWriter<byte>();
            using (var session = Pool.GetSession())
            {
                var writer = Writer.Create(buffer, session);
                codec.WriteField(ref writer, 0, typeof(ShardKey<int>), key);
                codec.WriteField(ref writer, 1, typeof(ShardKey<int>), key);
                writer.Commit();
            }

            using var readSession = Pool.GetSession();
            var reader = Reader.Create(buffer.WrittenMemory, readSession);
            var first = reader.ReadFieldHeader();
            first.WireType.Should().Be(WireType.LengthPrefixed);
            codec.ReadValue(ref reader, first).Should().Be(key);
            var second = reader.ReadFieldHeader();
            second.WireType.Should().Be(WireType.LengthPrefixed, "ShardKey is a value type, so Orleans reference tracking never matches an earlier occurrence");
            codec.ReadValue(ref reader, second).Should().Be(key);
        }

        [Fact]
        [Trait("Spec", "blk-29988f43ddb2412291fb928738c7af65")]
        [Trait("Spec", "blk-f8cd232c3d50449aad3f306b66210337")]
        public void DeepCopyRebuildsAnEqualKeyOfTheSameArityFromItsBytes()
        {
            var one = new ShardKey<int>(42, 123456);
            var two = new ShardKey<int, string>(3, 7, "child");
            var three = new ShardKey<Guid, long, short>(-1, Guid.NewGuid(), 9_000_000_000L, 5);
            var four = new ShardKey<string, int, long, byte>(12, "a", 2, 3L, 4);

            new ShardKeyOrleansSerializer<int>().DeepCopy(one, null!).Should().Be(one);
            new ShardKeyOrleansSerializer<int, string>().DeepCopy(two, null!).Should().Be(two);
            new ShardKeyOrleansSerializer<Guid, long, short>().DeepCopy(three, null!).Should().Be(three);
            new ShardKeyOrleansSerializer<string, int, long, byte>().DeepCopy(four, null!).Should().Be(four);
            new ShardKeyOrleansSerializer<int>().DeepCopy(one, null!).ToArray().ToArray().Should().Equal(one.ToArray().ToArray());
        }

        [Fact]
        [Trait("Spec", "blk-bc99ebb02bd948aaa8bdeb12658e3486")]
        [Trait("Spec", "blk-29988f43ddb2412291fb928738c7af65")]
        public void EveryCodecArityDeclaresBothRegisterSerializerAndRegisterCopier()
        {
            var codecs = new[]
            {
                typeof(ShardKeyOrleansSerializer<>),
                typeof(ShardKeyOrleansSerializer<,>),
                typeof(ShardKeyOrleansSerializer<,,>),
                typeof(ShardKeyOrleansSerializer<,,,>),
            };

            foreach (var codec in codecs)
            {
                codec.GetCustomAttributes(typeof(RegisterSerializerAttribute), false).Should().ContainSingle(codec.Name + " must register as a serializer");
                codec.GetCustomAttributes(typeof(RegisterCopierAttribute), false).Should().ContainSingle(codec.Name + " must register as a copier");
                codec.GetInterfaces().Select(i => i.IsGenericType ? i.GetGenericTypeDefinition() : i)
                    .Should().Contain(typeof(IFieldCodec<>)).And.Contain(typeof(global::Orleans.Serialization.Cloning.IDeepCopier<>));
            }
        }
    }
}
