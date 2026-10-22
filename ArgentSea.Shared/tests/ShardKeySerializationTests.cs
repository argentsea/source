using System;
using System.Linq;
using System.Text;
using ArgentSea.ShardKeys;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec section shard-key-serialization: the type-code codec (GetKeyDataType, TryEncodeTypeMetadata, ThrowIfMetadataMismatch,
    /// GetArgType), value encoding and decoding (GetValueBytes, TryConvertFromBytes), GhostShardKey, the checksum-embedded external
    /// key string and the StringExtensions text cleaning and URL-safe encoding.
    /// </summary>
    public class ShardKeySerializationTests
    {
        public enum ByteLevel : byte { Low = 1, High = 2 }

        #region Type metadata

        [Fact]
        [Trait("Spec", "blk-7c0129dd747d4252a68b59a5127cb96e")]
        [Trait("Spec", "blk-2632e46de8eb4fc2b7ea40af22c27a3d")]
        public void TypeMetadataPacksSixBitCodesIntoOneTwoOrThreeBytes()
        {
            ShardKeySerialization.TryEncodeTypeMetadata(typeof(Guid), out var one).Should().BeTrue();
            ShardKeySerialization.TryEncodeTypeMetadata(typeof(Guid), typeof(int), out var two).Should().BeTrue();
            ShardKeySerialization.TryEncodeTypeMetadata(typeof(Guid), typeof(int), typeof(string), out var three).Should().BeTrue();
            ShardKeySerialization.TryEncodeTypeMetadata(typeof(Guid), typeof(int), typeof(string), typeof(long), out var four).Should().BeTrue();
            ShardKeySerialization.TryEncodeTypeMetadata(typeof(object), typeof(int), out var undefined).Should().BeFalse();

            one.ToArray().Should().Equal(new byte[] { 0x04 }, "code 1 packed into the top six bits of one byte");
            two.Length.Should().Be(2);
            three.Length.Should().Be(3);
            four.Length.Should().Be(3, "four six-bit codes fit in three bytes");
            undefined.Length.Should().Be(2, "the bytes are written even on failure");
            undefined.ToArray()[0].Should().Be(0, "an Undefined part is encoded as zero");
            four.ToArray()[2].Should().NotBe(three.ToArray()[2], "the third byte also carries the great-grandchild code");
        }

        [Fact]
        [Trait("Spec", "blk-3ae75246ea2e459fb01a9eb3fe57a9bf")]
        [Trait("Spec", "blk-2632e46de8eb4fc2b7ea40af22c27a3d")]
        [Trait("Spec", "blk-49dcb2e37a5347a298d76da7c90ecf87")]
        [Trait("Spec", "blk-baea8bad2d8240e48d5ab5ab75237209")]
        public void KeyDataTypeCodesMapTypesAndRecoverEnumsAsTheirUnderlyingPrimitive()
        {
            Enum.GetValues<KeyDataType>().Cast<byte>().Should().Equal(Enumerable.Range(0, 20).Select(i => (byte)i));
            ShardKeySerialization.GetKeyDataType(typeof(Guid)).Should().Be(KeyDataType.Guid);
            ShardKeySerialization.GetKeyDataType(typeof(string)).Should().Be(KeyDataType.String);
            ShardKeySerialization.GetKeyDataType(typeof(DayOfWeek)).Should().Be(KeyDataType.Int32Enum);
            ShardKeySerialization.GetKeyDataType(typeof(ByteLevel)).Should().Be(KeyDataType.ByteEnum);
            ShardKeySerialization.GetKeyDataType(typeof(TimeOnly)).Should().Be(KeyDataType.Undefined);
            ShardKeySerialization.GetKeyDataType(typeof(object)).Should().Be(KeyDataType.Undefined);
            ShardKeySerialization.GetArgType(KeyDataType.Int32Enum).Should().Be(typeof(int), "the original enum type is not recoverable from the code");
            ShardKeySerialization.GetArgType(KeyDataType.ByteEnum).Should().Be(typeof(byte));
            ShardKeySerialization.GetArgType(KeyDataType.Blob).Should().Be(typeof(Array));
            ShardKeySerialization.GetArgType(KeyDataType.TimeOnly).Should().BeNull();
            ShardKeySerialization.GetArgType(KeyDataType.Undefined).Should().BeNull();
        }

        [Fact]
        [Trait("Spec", "blk-05cd1742ecda4c8a9ff1a414d2542829")]
        [Trait("Spec", "blk-07f0a2a6b26440119539afd3ea70f6f9")]
        [Trait("Spec", "blk-3b3ef157df48459ca3c5b0d3be15261f")]
        public void ThrowIfMetadataMismatchNamesTheTypeAtTheMismatchedPosition()
        {
            Action stringAgainstGuid = () => ShardKeySerialization.ThrowIfMetadataMismatch(new byte[] { 0x04 }, typeof(string));
            Action wrongLength = () => ShardKeySerialization.ThrowIfMetadataMismatch(new byte[] { 0x04, 0x00 }, typeof(Guid));
            Action unencodable = () => ShardKeySerialization.ThrowIfMetadataMismatch(new byte[] { 0x04, 0x00 }, typeof(Guid), typeof(object));
            ShardKeySerialization.TryEncodeTypeMetadata(typeof(Guid), typeof(int), typeof(string), out var three);
            var childMismatch = three.ToArray();
            childMismatch[1] ^= 0xFF;
            var grandChildMismatch = three.ToArray();
            grandChildMismatch[2] ^= 0x01;
            Action child = () => ShardKeySerialization.ThrowIfMetadataMismatch(childMismatch, typeof(Guid), typeof(int), typeof(string));
            Action grandChild = () => ShardKeySerialization.ThrowIfMetadataMismatch(grandChildMismatch, typeof(Guid), typeof(int), typeof(string));
            Action greatGrandChildOnPositionTwo = () => ShardKeySerialization.ThrowIfMetadataMismatch(grandChildMismatch, typeof(Guid), typeof(int), typeof(string), typeof(long));

            stringAgainstGuid.Should().Throw<InvalidShardKeyMetadataException>().WithMessage("*System.String*");
            wrongLength.Should().Throw<InvalidShardKeyMetadataException>().WithMessage("The metadata embedded in the serialized shardkey data does not match the presecribed data types required by the current shardkey definition. The data is corrupt.");
            unencodable.Should().Throw<InvalidShardKeyMetadataException>().WithMessage("*System.Guid*", "only the first-position type is named, not the type that failed to encode");
            child.Should().Throw<InvalidShardKeyMetadataException>().WithMessage("*System.Int32*");
            grandChild.Should().Throw<InvalidShardKeyMetadataException>().WithMessage("*System.String*");
            greatGrandChildOnPositionTwo.Should().Throw<InvalidShardKeyMetadataException>().WithMessage("*System.String*", "a position-2 mismatch is attributed to the grandchild type even in the four-type overload");
            new Action(() => ShardKeySerialization.ThrowIfMetadataMismatch(three.Span, typeof(Guid), typeof(int), typeof(string))).Should().NotThrow();
            new InvalidShardKeyMetadataException(typeof(int)).Message.Should().Be("The metadata embedded in the serialized shardkey does not match the prescribed data type of System.Int32 required by the current shardkey definition. The data is corrupt.");
            typeof(InvalidShardKeyMetadataException).Should().BeSealed().And.BeDerivedFrom<ApplicationException>();
        }

        [Fact]
        [Trait("Spec", "blk-3b3ef157df48459ca3c5b0d3be15261f")]
        public void DeserializingBytesOfADifferentKeyShapeThrowsInvalidShardKeyMetadataException()
        {
            var guidKey = new ShardKey<Guid>(1, Guid.NewGuid()).ToArray();

            Action act = () => new ShardKey<int>(guidKey);

            act.Should().Throw<InvalidShardKeyMetadataException>().WithMessage("*System.Int32*");
            ShardKey<int>.TryParse(guidKey.Span, out _).Should().BeFalse("TryParse reports the same mismatch without throwing");
        }

        #endregion

        #region Values

        [Fact]
        [Trait("Spec", "blk-e22203afb5f94d98a193cdfc697fb876")]
        [Trait("Spec", "blk-faee456447084061bb18f1b1bbcab911")]
        public void TryConvertFromBytesDecodesAnInt32AndAdvancesTheCursor()
        {
            ReadOnlySpan<byte> data = new byte[] { 0x01, 0x00, 0x00, 0x00 };
            var position = 0;

            var ok = ShardKeySerialization.TryConvertFromBytes(ref data, ref position, typeof(int), out dynamic result);

            ok.Should().BeTrue();
            ((int)result).Should().Be(1);
            position.Should().Be(4);
        }

        [Fact]
        [Trait("Spec", "blk-eb6e1a65b28b4c3f8fdc0cadffa70db7")]
        [Trait("Spec", "blk-faee456447084061bb18f1b1bbcab911")]
        public void TryConvertFromBytesReturnsFalseForAnUnrecognizedTypeWithoutThrowing()
        {
            ReadOnlySpan<byte> data = new byte[] { 0x07 };
            var position = 0;

            var ok = ShardKeySerialization.TryConvertFromBytes(ref data, ref position, typeof(object), out dynamic result);

            ok.Should().BeFalse();
            ((object?)result).Should().BeNull();
            position.Should().Be(0);
        }

        [Fact]
        [Trait("Spec", "blk-385c95f5b55a4937981cb0686346a44b")]
        [Trait("Spec", "blk-b2e0180d5dde43ff8c755e24e25c06f9")]
        [Trait("Spec", "blk-23ce8afeb9564280b5d1e19de4a89620")]
        [Trait("Spec", "blk-faee456447084061bb18f1b1bbcab911")]
        public void TryConvertFromBytesMeasuresFixedWidthValuesFromTheCursorNotTheSpanStart()
        {
            ReadOnlySpan<byte> five = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x2A };
            var position = 3;
            ShardKeySerialization.TryConvertFromBytes(ref five, ref position, typeof(int), out dynamic intResult).Should().BeFalse("only two bytes remain ahead of the cursor");
            ((int)intResult).Should().Be(0);
            position.Should().Be(3);

            ReadOnlySpan<byte> sixteen = new byte[16];
            position = 1;
            ShardKeySerialization.TryConvertFromBytes(ref sixteen, ref position, typeof(Guid), out dynamic guidResult).Should().BeFalse("only fifteen bytes remain ahead of the cursor");
            ((Guid)guidResult).Should().Be(Guid.Empty);
            position.Should().Be(1);
            ShardKeySerialization.TryConvertFromBytes(ref sixteen, ref position, typeof(decimal), out dynamic decimalResult).Should().BeFalse();
            ((decimal)decimalResult).Should().Be(decimal.Zero);
            position.Should().Be(1);

            ReadOnlySpan<byte> guidThenInt = new Guid("11111111-2222-3333-4444-555555555555").ToByteArray().Concat(BitConverter.GetBytes(7)).ToArray();
            position = 0;
            ShardKeySerialization.TryConvertFromBytes(ref guidThenInt, ref position, typeof(Guid), out dynamic guid).Should().BeTrue();
            ((Guid)guid).Should().Be(new Guid("11111111-2222-3333-4444-555555555555"));
            position.Should().Be(16, "a Guid consumes exactly sixteen bytes so a later part can follow it");
            ShardKeySerialization.TryConvertFromBytes(ref guidThenInt, ref position, typeof(int), out dynamic following).Should().BeTrue();
            ((int)following).Should().Be(7);
        }

        [Fact]
        [Trait("Spec", "blk-61f517296c924f84855ba218fba84336")]
        [Trait("Spec", "blk-faee456447084061bb18f1b1bbcab911")]
        public void TryConvertFromBytesReturnsFalseWhenAVariableLengthPartDeclaresMoreBytesThanRemain()
        {
            ReadOnlySpan<byte> data = new byte[] { 0x07, 0x41 };
            var position = 0;

            var ok = ShardKeySerialization.TryConvertFromBytes(ref data, ref position, typeof(string), out dynamic result);

            ok.Should().BeFalse("the length-and-flag byte declares three payload bytes but only one follows");
            position.Should().Be(0);
            ReadOnlySpan<byte> empty = Array.Empty<byte>();
            position = 0;
            ShardKeySerialization.TryConvertFromBytes(ref empty, ref position, typeof(string), out _).Should().BeFalse("a cursor at the end of the span has no length byte to read");
            ShardKeySerialization.TryConvertFromBytes(ref empty, ref position, typeof(byte), out _).Should().BeFalse();
            ShardKeySerialization.TryConvertFromBytes(ref empty, ref position, typeof(byte[]), out _).Should().BeFalse();
        }

        [Fact]
        [Trait("Spec", "blk-faee456447084061bb18f1b1bbcab911")]
        public void VariableLengthPartsWithAZeroLengthByteShortCircuitToTheirEmptyValue()
        {
            ReadOnlySpan<byte> data = new byte[] { 0x00, 0x00, 0x00 };
            var position = 0;

            ShardKeySerialization.TryConvertFromBytes(ref data, ref position, typeof(string), out dynamic text).Should().BeTrue();
            ((string)text).Should().Be(string.Empty);
            position.Should().Be(1);
            ShardKeySerialization.TryConvertFromBytes(ref data, ref position, typeof(byte[]), out dynamic blob).Should().BeTrue();
            ((byte[]?)blob).Should().BeNull();
            position.Should().Be(2);
            ShardKeySerialization.TryConvertFromBytes(ref data, ref position, typeof(ReadOnlyMemory<byte>), out dynamic memory).Should().BeTrue();
            ((ReadOnlyMemory<byte>)memory).Length.Should().Be(0);
            position.Should().Be(3);
        }

        [Fact]
        [Trait("Spec", "blk-e6f478a56795435ea70080ee9cdd564a")]
        [Trait("Spec", "blk-faee456447084061bb18f1b1bbcab911")]
        public void GetValueBytesPrefixesAStringWithItsLengthAndFlagByte()
        {
            ShardKeySerialization.GetValueBytes("AB").Should().Equal(0x05, 0x41, 0x42);
            ShardKeySerialization.GetValueBytes(1).Should().Equal(BitConverter.GetBytes(1));
            ShardKeySerialization.GetValueBytes(DayOfWeek.Tuesday).Should().Equal(BitConverter.GetBytes((int)DayOfWeek.Tuesday), "an enum is re-dispatched through its underlying type");
            ShardKeySerialization.GetValueBytes(ByteLevel.High).Should().Equal(0x02);
            new Action(() => ShardKeySerialization.GetValueBytes(new string('x', 128))).Should().Throw<ArgumentOutOfRangeException>();
            ReadOnlySpan<byte> round = ShardKeySerialization.GetValueBytes(new DateOnly(2024, 2, 29));
            var position = 0;
            ShardKeySerialization.TryConvertFromBytes(ref round, ref position, typeof(DateOnly), out dynamic date).Should().BeTrue();
            ((DateOnly)date).Should().Be(new DateOnly(2024, 2, 29), "a DateOnly part can be written as well as read");
        }

        [Fact]
        [Trait("Spec", "blk-faee456447084061bb18f1b1bbcab911")]
        [Trait("Spec", "blk-49dcb2e37a5347a298d76da7c90ecf87")]
        [Trait("Spec", "blk-7d8433e1617e48b29bfdb7be9d115e18")]
        public void EnumKeyPartsRoundTripAsTheirUnderlyingTypeAndReadBackAsTheEnum()
        {
            var key = new ShardKey<DayOfWeek, ByteLevel>(2, DayOfWeek.Friday, ByteLevel.High);

            var copy = new ShardKey<DayOfWeek, ByteLevel>(key.ToArray());
            ReadOnlySpan<byte> raw = ShardKeySerialization.GetValueBytes(DayOfWeek.Friday);
            var position = 0;
            ShardKeySerialization.TryConvertFromBytes(ref raw, ref position, typeof(DayOfWeek), out dynamic decoded);

            copy.Should().Be(key);
            copy.RecordId.Should().Be(DayOfWeek.Friday);
            copy.ChildId.Should().Be(ByteLevel.High);
            ((DayOfWeek)decoded).Should().Be(DayOfWeek.Friday);
            Action ghost = () => new GhostShardKey(key.ToArray()).GetInstance();
            ghost.Should().Throw<Exception>("a ghost selects the underlying primitive type for an enum part, whose metadata code then differs from the stored enum code")
                .Which.Should().Match<Exception>(e => e is InvalidShardKeyMetadataException || e.InnerException is InvalidShardKeyMetadataException);
        }

        [Fact]
        [Trait("Spec", "blk-a8db2ae363da4918a5556882aebc0512")]
        public void IsEqualWithNullsTreatsTwoNullsAsEqualAndANullAgainstAValueAsUnequal()
        {
            ShardKeySerialization.IsEqualWithNulls<string>(null!, null!).Should().BeTrue();
            ShardKeySerialization.IsEqualWithNulls<string>(null!, "a").Should().BeFalse();
            ShardKeySerialization.IsEqualWithNulls<string>("a", null!).Should().BeFalse();
            ShardKeySerialization.IsEqualWithNulls("a", "a").Should().BeTrue();
            ShardKeySerialization.IsEqualWithNulls(1, 2).Should().BeFalse();
        }

        #endregion

        #region GhostShardKey

        [Fact]
        [Trait("Spec", "blk-1b9b1b387fab4da2b441435c71c49d54")]
        [Trait("Spec", "blk-2b260aa4b2a544ffb697e711413eddf8")]
        [Trait("Spec", "blk-271dd0dd4a414ab6bc1964c3bee81729")]
        [Trait("Spec", "blk-30c536c49b71490ea360f17396a34637")]
        [Trait("Spec", "blk-3124bd2c587447408738d53228c61b94")]
        public void GhostShardKeyReconstructsTheKeyShapeFromBinaryOrUtf8Bytes()
        {
            var source = new ShardKey<int, short, byte, string>(1, 6, 7, 8, "9");
            var fromBinary = new GhostShardKey(source.ToArray());
            var fromUtf8 = new GhostShardKey(source.ToUtf8());

            ((object)fromBinary.GetInstance()!).Should().BeOfType<ShardKey<int, short, byte, string>>();
            ((ShardKey<int, short, byte, string>)fromBinary.GetInstance()!).Should().Be(source);
            fromBinary.ToString().Should().Be(source.ToString());
            fromUtf8.ToString().Should().Be(source.ToString());
            ((object)new GhostShardKey(new ShardKey<Guid>(2, Guid.NewGuid()).ToArray()).GetInstance()!).Should().BeOfType<ShardKey<Guid>>();
            ((object)new GhostShardKey(new ShardKey<int, string>(2, 1, "c").ToArray()).GetInstance()!).Should().BeOfType<ShardKey<int, string>>();
            ((object)new GhostShardKey(new ShardKey<int, string, long>(2, 1, "c", 3L).ToArray()).GetInstance()!).Should().BeOfType<ShardKey<int, string, long>>();
        }

        [Fact]
        [Trait("Spec", "blk-950183d8c64f4676b4e66b0940ac9b9c")]
        [Trait("Spec", "blk-d99d3a2a5c9c492590ade62b854dff2a")]
        public void GhostShardKeyReadsTheShardIdAfterTheMetadata()
        {
            new GhostShardKey(new byte[] { 0x81, 0x00, 0x34, 0x12, 0x00 }).GetShardId().Should().Be(4660);
            new GhostShardKey(new ShardKey<int, short>(-7, 1, 2).ToUtf8()).GetShardId().Should().Be(-7);
        }

        [Fact]
        [Trait("Spec", "blk-0aae46c6ef3b49e18573fec1d5ce50b5")]
        [Trait("Spec", "blk-9e97941e304a41888b9952964915df6a")]
        [Trait("Spec", "blk-0eca80bd58e6429cb53c233e0712ddef")]
        public void AGhostOverFewerThanFiveBytesIsNotAShardKey()
        {
            var ghost = new GhostShardKey(new byte[] { 0x80, 0x00, 0x00, 0x00 });

            ghost.ToString().Should().Be("{ Not a ShardKey }");
            ((object?)ghost.GetInstance()).Should().BeNull();
            new Action(() => ghost.GetShardId()).Should().Throw<InvalidShardKeyMetadataException>();
        }

        #endregion

        #region External strings and encoding

        [Fact]
        [Trait("Spec", "blk-5770536dd9e9496c88277e1173e41a82")]
        [Trait("Spec", "blk-f0d1fcc0704a4870bd2f9d7ea7ae7c70")]
        [Trait("Spec", "blk-c8968aa909454df6b1591aca05705038")]
        [Trait("Spec", "blk-fade934144ca47e586f5aed81b1bed01")]
        public void TheExternalStringChecksumRejectsAnAlteredPrefix()
        {
            StringExtensions.SerializeToExternalString(new byte[] { 0x00 }).Should().Be("AAt1");
            StringExtensions.SerializeFromExternalString("AAt1").ToArray().Should().Equal(0x00);
            new Action(() => StringExtensions.SerializeFromExternalString("BAt1")).Should().Throw<Exception>().WithMessage("External key string is not valid.");
            new Action(() => StringExtensions.SerializeFromExternalString("!At1")).Should().Throw<Exception>().WithMessage("External key string has been corrupted.");
            var payload = Enumerable.Range(0, 20).Select(i => (byte)(i * 13)).ToArray();
            StringExtensions.SerializeFromExternalString(StringExtensions.SerializeToExternalString(payload)).ToArray().Should().Equal(payload, "unaltered bytes always reverse");
        }

        [Fact]
        [Trait("Spec", "blk-c8968aa909454df6b1591aca05705038")]
        public void AKeyExternalStringWithACorruptedChecksumIsRejectedWithTheDocumentedMessage()
        {
            var external = new ShardKey<int>(1, 42).ToExternalString();
            var corrupted = (external[0] == 'A' ? 'B' : 'A') + external.Substring(1);

            Action act = () => ShardKey<int>.FromExternalString(corrupted);

            act.Should().Throw<Exception>().WithMessage("External key string is not valid.");
        }

        [Fact]
        [Trait("Spec", "blk-e7038e46d4814c59a9818e5d5e87b1c0")]
        [Trait("Spec", "blk-eda3888af389419ea964ad32608b7b4e")]
        [Trait("Spec", "blk-fade934144ca47e586f5aed81b1bed01")]
        public void EncodingUsesOnlyTheUrlSafeAlphabetAndDecodesFromEitherRepresentation()
        {
            var bytes = new byte[] { 0x74, 0x65, 0x73, 0x74 };

            var text = StringExtensions.EncodeToString(bytes);
            var utf8 = StringExtensions.EncodeToUtf8(bytes);

            text.Should().Be("qbpLq1");
            Encoding.UTF8.GetString(utf8.Span).Should().Be(text, "both encodings produce the same sequence");
            StringExtensions.Decode(text).ToArray().Should().Equal(bytes);
            StringExtensions.Decode(utf8.Span).ToArray().Should().Equal(bytes);
            var everyByte = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            StringExtensions.EncodeToString(everyByte).Should().MatchRegex("^[0-9A-Za-z_~]+$");
            StringExtensions.Decode(StringExtensions.EncodeToString(everyByte)).ToArray().Should().Equal(everyByte);
        }

        #endregion

        #region Text cleaning

        [Fact]
        [Trait("Spec", "blk-0a8e1d90116c4c82b28e563f8717e7e2")]
        [Trait("Spec", "blk-5de0a2c37b224b0eadea31da517f7a45")]
        public void DefaultCleaningTrimsSeparatorsAndDropsControlCharactersAndSurrogatePairs()
        {
            var emoji = char.ConvertFromUtf32(0x1F603);

            ("  This is a test" + emoji).CleanInput().Should().Be("This is a test");
            ((string?)null).CleanInput().Should().BeNull();
            "a\tb c".CleanInput().Should().Be("ab c", "control characters are removed while interior separators keep their order");
            "   ".CleanInput().Should().Be(string.Empty);
        }

        [Fact]
        [Trait("Spec", "blk-7b9c62c5475e49499cee996ea98f177e")]
        [Trait("Spec", "blk-c148c82173f14cfb9439a074444b4de6")]
        [Trait("Spec", "blk-af9b3d057c3343e3970d20c2dfa62ca8")]
        public void AllowEmojisAndAllowMultilineRetainSurrogatesAndNormalizeLineFeeds()
        {
            var emoji = char.ConvertFromUtf32(0x1F603);
            var text = " " + emoji + "One\nTwo ";

            text.CleanInput(StringExtensions.InputCleaningOptions.AllowEmojis | StringExtensions.InputCleaningOptions.AllowMultiline).Should().Be(emoji + "One\r\nTwo");
            text.CleanInput(StringExtensions.InputCleaningOptions.AllowMultiline).Should().Be("One\r\nTwo");
            text.CleanInput(StringExtensions.InputCleaningOptions.AllowEmojis).Should().Be(emoji + "OneTwo");
            "a\rb".CleanInput(StringExtensions.InputCleaningOptions.AllowMultiline).Should().Be("ab", "a standalone carriage return is still a control character");
            ((int)StringExtensions.InputCleaningOptions.CleanAll).Should().Be(0);
            ((int)StringExtensions.InputCleaningOptions.AllowEmojis).Should().Be(1);
            ((int)StringExtensions.InputCleaningOptions.AllowMultiline).Should().Be(2);
            typeof(StringExtensions.InputCleaningOptions).GetCustomAttributes(typeof(FlagsAttribute), false).Should().NotBeEmpty();
        }

        #endregion
    }
}
