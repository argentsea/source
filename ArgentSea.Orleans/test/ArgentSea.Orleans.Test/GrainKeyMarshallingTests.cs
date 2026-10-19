using System.Text;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArgentSea.Orleans.Test
{
    /// <summary>
    /// Spec section grain-key-marshalling: SpanParser's delimiter-separated UTF-8 key encoding (Append / Extract for every
    /// supported type, the '+' delimiter invariant), GetDbGrainId's per-type delegate cache, the grain-identity delegates
    /// OrleansExpressionHelper compiles (and the exceptions they raise), and QueryExtensions.ShardId.
    /// </summary>
    public class GrainKeyMarshallingTests
    {
        #region SpanParser

        [Fact]
        [Trait("Spec", "blk-f1fc490b84124413b27a81dae7774e68")]
        [Trait("Spec", "blk-9370ebac48704ec4876e2f184f028139")]
        [Trait("Spec", "blk-e45646b846444943904345e272f161aa")]
        [Trait("Spec", "blk-8ef360077bef4801952eaacd6a438b9e")]
        public void EverySupportedTypeRoundTripsThroughAppendAndExtractInSequence()
        {
            var guid = Guid.NewGuid();
            Span<byte> buffer = stackalloc byte[256];
            var position = 0;
            position = buffer.Append(position, guid);
            position = buffer.Append(position, long.MinValue);
            position = buffer.Append(position, ulong.MaxValue);
            position = buffer.Append(position, -12345);
            position = buffer.Append(position, uint.MaxValue);
            position = buffer.Append(position, short.MinValue);
            position = buffer.Append(position, ushort.MaxValue);
            position = buffer.Append(position, (byte)0xAB);
            position = buffer.Append(position, (sbyte)-5);
            position = buffer.Append(position, "Wanna know");
            position = buffer.Append(position, -98765.4321m);
            position = buffer.Append(position, 0L);

            ReadOnlySpan<byte> span = buffer[..position];
            span = span.Extract(out Guid g).Extract(out long l).Extract(out ulong ul).Extract(out int i).Extract(out uint ui)
                .Extract(out short s).Extract(out ushort us).Extract(out byte b).Extract(out sbyte sb).Extract(out string str).Extract(out decimal d).Extract(out long last);

            g.Should().Be(guid);
            l.Should().Be(long.MinValue);
            ul.Should().Be(ulong.MaxValue);
            i.Should().Be(-12345);
            ui.Should().Be(uint.MaxValue);
            s.Should().Be(short.MinValue);
            us.Should().Be(ushort.MaxValue);
            b.Should().Be(0xAB);
            sb.Should().Be(-5);
            str.Should().Be("Wanna know");
            d.Should().Be(-98765.4321m);
            last.Should().Be(0L);
            span.IsEmpty.Should().BeTrue("the final extraction returns an empty suffix");
        }

        [Fact]
        [Trait("Spec", "blk-8ef360077bef4801952eaacd6a438b9e")]
        [Trait("Spec", "blk-f1fc490b84124413b27a81dae7774e68")]
        public void NumericSegmentsAreUpperCaseHexadecimalAndGuidsAreThirtyTwoDigits()
        {
            Span<byte> buffer = stackalloc byte[128];
            var position = 0;
            position = buffer.Append(position, new Guid("00112233-4455-6677-8899-aabbccddeeff"));
            position = buffer.Append(position, 255L);
            position = buffer.Append(position, 255);
            position = buffer.Append(position, (short)255);
            position = buffer.Append(position, (byte)255);
            position = buffer.Append(position, 4294967295u);

            Encoding.UTF8.GetString(buffer[..position]).Should().Be("00112233445566778899aabbccddeeff+FF+FF+FF+FF+FFFFFFFF");
        }

        [Fact]
        [Trait("Spec", "blk-23cff734d95b4b12bfd076fb06999603")]
        [Trait("Spec", "blk-9370ebac48704ec4876e2f184f028139")]
        public void TheDelimiterNeverPrecedesTheFirstSegmentAndAppearsExactlyOnceBeforeEachLaterOne()
        {
            Span<byte> buffer = stackalloc byte[64];
            var position = 0;
            position = buffer.Append(position, 1);
            position.Should().Be(1, "no delimiter is written before the first segment");
            position = buffer.Append(position, 2);
            position = buffer.Append(position, 3);

            var text = Encoding.UTF8.GetString(buffer[..position]);
            text.Should().Be("1+2+3");
            text.Count(c => c == '+').Should().Be(2);
            text[0].Should().NotBe('+');
        }

        [Fact]
        [Trait("Spec", "blk-e0796afc493a4e1fb6a3ad2e3c6570fd")]
        [Trait("Spec", "blk-9370ebac48704ec4876e2f184f028139")]
        [Trait("Spec", "blk-e45646b846444943904345e272f161aa")]
        public void TwoGuidsAppendedToASixtyFiveByteBufferReturnThirtyTwoThenSixtyFive()
        {
            var first = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            var second = new Guid("ffeeddcc-bbaa-9988-7766-554433221100");
            Span<byte> buffer = stackalloc byte[65];

            var afterFirst = buffer.Append(0, first);
            var afterSecond = buffer.Append(afterFirst, second);

            afterFirst.Should().Be(32);
            afterSecond.Should().Be(65);
            Encoding.UTF8.GetString(buffer).Should().Be("00112233445566778899aabbccddeeff+ffeeddccbbaa99887766554433221100");

            ReadOnlySpan<byte> span = buffer;
            var rest = span.Extract(out Guid g1).Extract(out Guid g2);
            g1.Should().Be(first);
            g2.Should().Be(second);
            rest.IsEmpty.Should().BeTrue();
        }

        [Fact]
        [Trait("Spec", "blk-f1fc490b84124413b27a81dae7774e68")]
        [Trait("Spec", "blk-8ef360077bef4801952eaacd6a438b9e")]
        public void StringsAndRawBytesSubstituteTheGroupSeparatorForEmbeddedPlusSignsAndRoundTrip()
        {
            Span<byte> buffer = stackalloc byte[64];
            var position = buffer.Append(0, "a+b");
            position = buffer.Append(position, "c+d"u8);
            position = buffer.Append(position, 7);

            var raw = buffer[..position].ToArray();
            raw.Count(x => x == (byte)'+').Should().Be(2, "only the two segment separators are '+'");
            raw.Count(x => x == 0x1D).Should().Be(2, "embedded '+' is stored as the group separator");

            ReadOnlySpan<byte> span = raw;
            var rest = span.Extract(out string first).Extract(out string second).Extract(out int n);
            first.Should().Be("a+b", "an embedded '+' round-trips through Extract");
            second.Should().Be("c+d");
            n.Should().Be(7);
            rest.IsEmpty.Should().BeTrue();
        }

        [Fact]
        [Trait("Spec", "blk-e45646b846444943904345e272f161aa")]
        public void ExtractingAFinalMultiByteStringSegmentReturnsAnEmptySuffix()
        {
            Span<byte> buffer = stackalloc byte[64];
            var position = buffer.Append(0, 1);
            position = buffer.Append(position, "héllo wörld");
            ReadOnlySpan<byte> span = buffer[..position];

            var rest = span.Extract(out int n).Extract(out string text);

            n.Should().Be(1);
            text.Should().Be("héllo wörld");
            rest.IsEmpty.Should().BeTrue("the consumed byte count, not the character count, positions the suffix");
        }

        [Fact]
        [Trait("Spec", "blk-e45646b846444943904345e272f161aa")]
        public void ExtractingAnUnparsableSegmentThrowsInvalidCastForEveryNumericType()
        {
            var bad = Encoding.UTF8.GetBytes("zz+1");
            FluentActions.Invoking(() => { ReadOnlySpan<byte> s = bad; s.Extract(out long _); }).Should().Throw<InvalidCastException>().WithMessage("*Int64*");
            FluentActions.Invoking(() => { ReadOnlySpan<byte> s = bad; s.Extract(out ulong _); }).Should().Throw<InvalidCastException>().WithMessage("*UInt64*");
            FluentActions.Invoking(() => { ReadOnlySpan<byte> s = bad; s.Extract(out int _); }).Should().Throw<InvalidCastException>().WithMessage("*Int32*");
            FluentActions.Invoking(() => { ReadOnlySpan<byte> s = bad; s.Extract(out uint _); }).Should().Throw<InvalidCastException>().WithMessage("*UInt32*");
            FluentActions.Invoking(() => { ReadOnlySpan<byte> s = bad; s.Extract(out short _); }).Should().Throw<InvalidCastException>().WithMessage("*Int16*");
            FluentActions.Invoking(() => { ReadOnlySpan<byte> s = bad; s.Extract(out ushort _); }).Should().Throw<InvalidCastException>().WithMessage("*UInt16*");
            FluentActions.Invoking(() => { ReadOnlySpan<byte> s = bad; s.Extract(out byte _); }).Should().Throw<InvalidCastException>().WithMessage("*Byte*");
            FluentActions.Invoking(() => { ReadOnlySpan<byte> s = bad; s.Extract(out sbyte _); }).Should().Throw<InvalidCastException>().WithMessage("*signed byte*");
            FluentActions.Invoking(() => { ReadOnlySpan<byte> s = bad; s.Extract(out Guid _); }).Should().Throw<InvalidCastException>().WithMessage("*Guid*");
            FluentActions.Invoking(() => { ReadOnlySpan<byte> s = bad; s.Extract(out decimal _); }).Should().Throw<InvalidCastException>().WithMessage("*decimal*");
        }

        [Theory]
        [InlineData("12.5")]
        [InlineData("-0.0001")]
        [InlineData("0")]
        [InlineData("79228162514264337593543950335")]
        [InlineData("-79228162514264337593543950335")]
        [InlineData("0.0000000000000000000000000001")]
        [InlineData("1.10")]
        [Trait("Spec", "blk-f1fc490b84124413b27a81dae7774e68")]
        [Trait("Spec", "blk-8ef360077bef4801952eaacd6a438b9e")]
        [Trait("Spec", "blk-9370ebac48704ec4876e2f184f028139")]
        [Trait("Spec", "blk-e45646b846444943904345e272f161aa")]
        public void DecimalKeySegmentsRoundTripBetweenOtherSegmentsWithoutAnEmbeddedDelimiter(string text)
        {
            var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            Span<byte> buffer = stackalloc byte[128];
            var position = buffer.Append(0, 1);
            position = buffer.Append(position, value);
            position = buffer.Append(position, 2);

            var encoded = Encoding.UTF8.GetString(buffer[..position]);
            encoded.Count(c => c == '+').Should().Be(2, "a decimal segment never contains the delimiter");
            encoded.Should().Be($"1+{text}+2", "decimal segments are invariant general-format text");

            ReadOnlySpan<byte> span = buffer[..position];
            var rest = span.Extract(out int first).Extract(out decimal decoded).Extract(out int last);
            first.Should().Be(1);
            decoded.Should().Be(value);
            last.Should().Be(2);
            rest.IsEmpty.Should().BeTrue();

            ReadOnlySpan<byte> alone = Encoding.UTF8.GetBytes(text);
            alone.Extract(out decimal single).IsEmpty.Should().BeTrue("a final decimal segment leaves an empty suffix");
            single.Should().Be(value);
        }

        public class DecimalKeyModel
        {
            [FakeMap("Amount", true)]
            public decimal Amount { get; set; }

            [FakeMapInteger("Line", true)]
            public int Line { get; set; }

            [FakeMapString("Note", 50)]
            public string? Note { get; set; }
        }

        [Fact]
        [Trait("Spec", "blk-f1fc490b84124413b27a81dae7774e68")]
        [Trait("Spec", "blk-eb6de7f5b11048178738883cd96debaf")]
        [Trait("Spec", "blk-c2ef4184d570486badfa64db4d7fb152")]
        public void ADecimalRecordIdentifierRoundTripsThroughTheGrainIdDelegates()
        {
            var model = new DecimalKeyModel { Amount = 1234.5678m, Line = 3, Note = "ignored" };
            var toId = OrleansExpressionHelper.BuildGrainIdFromDbModel<DecimalKeyModel>(null);
            var toParameters = OrleansExpressionHelper.BuildDbReadLambda<DecimalKeyModel>("ledger", null);
            var toModel = OrleansExpressionHelper.BuildSetIdFromGrainId<DecimalKeyModel>("ledger", null);

            var id = toId(model, null);
            Encoding.UTF8.GetString(id.Value.Span).Should().Be("1234.5678+3");

            var prms = new ParameterCollection();
            toParameters(id.Value, prms, null);
            prms["@Amount"].Value.Should().Be(1234.5678m);
            prms["@Line"].Value.Should().Be(3);

            var target = new DecimalKeyModel();
            toModel(id.Value, target, null);
            target.Amount.Should().Be(1234.5678m);
            target.Line.Should().Be(3);
            target.Note.Should().BeNull();

            SpanParser.GetDbGrainId(model, NullLogger.Instance).Should().Be(id);
        }

        #endregion

        #region GetDbGrainId

        public class Order
        {
            [FakeMapGuid("OrderId", true)]
            public Guid OrderId { get; set; }

            [FakeMapInteger("Line", true)]
            public int Line { get; set; }
        }

        public class CachedOrder
        {
            [FakeMapGuid("OrderId", true)]
            public Guid OrderId { get; set; }
        }

        [Fact]
        [Trait("Spec", "blk-1451d15d1d1143b48a35815ab357580c")]
        [Trait("Spec", "blk-2da9b77fca54432e93660733c0b7be4e")]
        [Trait("Spec", "blk-f1fc490b84124413b27a81dae7774e68")]
        public void GetDbGrainIdCompilesOncePerModelTypeAndReusesTheCachedDelegate()
        {
            var logger = new RecordingLogger();
            var first = new CachedOrder { OrderId = Guid.NewGuid() };
            var second = new CachedOrder { OrderId = Guid.NewGuid() };

            var firstId = SpanParser.GetDbGrainId(first, logger);
            var firstEvents = logger.Entries.Where(e => e.Id == (int)LoggingExtensions.EventIdentifier.MapperCacheStatus).ToList();
            logger.Clear();
            var secondId = SpanParser.GetDbGrainId(second, logger);
            var secondEvents = logger.Entries.Where(e => e.Id == (int)LoggingExtensions.EventIdentifier.MapperCacheStatus).ToList();

            firstEvents.Should().ContainSingle().Which.Name.Should().Be(nameof(LoggingExtensions.OrleansDbCacheMiss));
            secondEvents.Should().ContainSingle().Which.Name.Should().Be(nameof(LoggingExtensions.OrleansDbCacheHit));
            firstEvents[0].Message.Should().Contain(nameof(CachedOrder));
            Encoding.UTF8.GetString(firstId.Value.Span).Should().Be(first.OrderId.ToString("N"), "each call builds the IdSpan from its own model");
            Encoding.UTF8.GetString(secondId.Value.Span).Should().Be(second.OrderId.ToString("N"));
        }

        [Fact]
        [Trait("Spec", "blk-1e5574b319c54e5aabfd568a7f801002")]
        [Trait("Spec", "blk-67273136680441c5b8dd797c02dc1367")]
        public void GetDbGrainIdWithANullModelThrowsArgumentNullNamingTheModel()
        {
            Order? order = null;

            FluentActions.Invoking(() => SpanParser.GetDbGrainId(order!, NullLogger.Instance))
                .Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("model");
        }

        #endregion

        #region OrleansExpressionHelper delegates

        public class ThreeKeyModel
        {
            [FakeMapGuid("A", true)]
            public Guid A { get; set; }

            [FakeMapString("B", 50, true)]
            public string B { get; set; } = string.Empty;

            [FakeMapInteger("C", true)]
            public int C { get; set; }

            [FakeMapString("NotAKey", 50)]
            public string? NotAKey { get; set; }
        }

        [Fact]
        [Trait("Spec", "blk-eb6de7f5b11048178738883cd96debaf")]
        [Trait("Spec", "blk-c2ef4184d570486badfa64db4d7fb152")]
        public void TheGrainIdDelegatesAppendAndExtractRecordIdentifierPropertiesInDeclarationOrder()
        {
            var model = new ThreeKeyModel { A = Guid.NewGuid(), B = "héllo", C = 0x7F, NotAKey = "ignored" };
            var toId = OrleansExpressionHelper.BuildGrainIdFromDbModel<ThreeKeyModel>(null);
            var toParameters = OrleansExpressionHelper.BuildDbReadLambda<ThreeKeyModel>("orders", null);
            var toModel = OrleansExpressionHelper.BuildSetIdFromGrainId<ThreeKeyModel>("orders", null);

            var id = toId(model, null);

            var expected = $"{model.A:N}+héllo+7F";
            Encoding.UTF8.GetString(id.Value.Span).Should().Be(expected);
            id.Value.Length.Should().Be(32 + Encoding.UTF8.GetByteCount("héllo") + 2 + 2, "the encoded lengths plus one delimiter per property after the first");

            var prms = new ParameterCollection();
            toParameters(id.Value, prms, null);
            prms.Cast<System.Data.Common.DbParameter>().Select(p => p.ParameterName).Should().Equal("@A", "@B", "@C");
            prms["@A"].Value.Should().Be(model.A);
            prms["@B"].Value.Should().Be("héllo");
            prms["@C"].Value.Should().Be(0x7F);

            var target = new ThreeKeyModel { NotAKey = "kept" };
            toModel(id.Value, target, null);
            target.A.Should().Be(model.A);
            target.B.Should().Be("héllo");
            target.C.Should().Be(0x7F);
            target.NotAKey.Should().Be("kept", "only record-identifier properties are set from the grain id");
        }

        public class DateKeyModel
        {
            [FakeMap("When", true)]
            public DateTime When { get; set; }
        }

        [Fact]
        [Trait("Spec", "blk-c244337ded434a45bf72c9d25764118c")]
        [Trait("Spec", "blk-f3d3ab8826134c4e992db970c3bf8c33")]
        public void ARecordIdentifierOfATypeSpanParserCannotHandleThrowsInvalidKeyType()
        {
            FluentActions.Invoking(() => OrleansExpressionHelper.BuildDbReadLambda<DateKeyModel>("orders", null))
                .Should().Throw<InvalidKeyTypeException>().WithMessage("*“orders”*When*");
            FluentActions.Invoking(() => OrleansExpressionHelper.BuildSetIdFromGrainId<DateKeyModel>("orders", null))
                .Should().Throw<InvalidKeyTypeException>().WithMessage("*“orders”*When*");
            FluentActions.Invoking(() => OrleansExpressionHelper.BuildGrainIdFromDbModel<DateKeyModel>(null))
                .Should().Throw<InvalidKeyTypeException>().WithMessage("*When*");

            new InvalidKeyTypeException().Message.Should().Contain("not a supported key data type");
            new InvalidKeyTypeException("orders").Message.Should().Contain("“orders”");
            new InvalidKeyTypeException("orders", "When").Message.Should().Contain("“orders”").And.Contain("for When");
        }

        public class ShardedOrder
        {
            [FakeMapShardKey("Shard", "OrderId")]
            [FakeMapInteger("OrderId")]
            public ShardKey<int> Key { get; set; } = ShardKey<int>.Empty;
        }

        [Fact]
        [Trait("Spec", "blk-db2db64747a04350a72d145f5014b3be")]
        [Trait("Spec", "blk-97e0178b21ba4709af87ecc0b628c39b")]
        public void AValidatingWriteOrClearDelegateThrowsIdKeyMismatchWhenTheGrainIdentityDiffersFromTheModelKey()
        {
            var model = new ShardedOrder { Key = new ShardKey<int>(3, 100) };
            var grainIdentity = new ShardKey<int>(3, 200).ToUtf8();
            var write = OrleansExpressionHelper.BuildShardWriteLambda<ShardedOrder>("orders", true);
            var clear = OrleansExpressionHelper.BuildShardClearLambda<ShardedOrder>("orders", true, null);
            var lenientWrite = OrleansExpressionHelper.BuildShardWriteLambda<ShardedOrder>("orders", false);

            FluentActions.Invoking(() => write(grainIdentity, model)).Should().Throw<OrleansIdKeyMismatchException>().WithMessage("*“orders”*");
            FluentActions.Invoking(() => clear(grainIdentity, model, new ParameterCollection(), null)).Should().Throw<OrleansIdKeyMismatchException>().WithMessage("*“orders”*");
            write(model.Key.ToUtf8(), model).Should().Be(3, "a matching identity passes validation");
            lenientWrite(grainIdentity, model).Should().Be(3, "without validation the model's own key is used");

            new OrleansIdKeyMismatchException().Message.Should().Contain("does not match");
            new OrleansIdKeyMismatchException("orders", "id", "key").Message.Should().Contain("(id)").And.Contain("(key)").And.Contain("“orders”");
            new OrleansIdKeyMismatchException("orders", "id", "key", new Exception("x")).InnerException!.Message.Should().Be("x");
        }

        public class UnshardedModel
        {
            [FakeMapInteger("Id", true)]
            public int Id { get; set; }
        }

        [Fact]
        [Trait("Spec", "blk-d7422e4dd22547fb9fc328522ea9d607")]
        [Trait("Spec", "blk-9d089dce951649d095047ec1768f87b6")]
        public void AModelWithoutAShardKeyPropertyMakesEveryShardDelegateBuilderThrowShardKeyNotFound()
        {
            FluentActions.Invoking(() => OrleansExpressionHelper.BuildShardReadLambda<UnshardedModel>("orders", null)).Should().Throw<ShardKeyNotFoundException>().WithMessage("*“orders”*");
            FluentActions.Invoking(() => OrleansExpressionHelper.BuildSetShardLambda<UnshardedModel>("orders", null)).Should().Throw<ShardKeyNotFoundException>().WithMessage("*“orders”*");
            FluentActions.Invoking(() => OrleansExpressionHelper.BuildShardWriteLambda<UnshardedModel>("orders", true)).Should().Throw<ShardKeyNotFoundException>().WithMessage("*“orders”*");
            FluentActions.Invoking(() => OrleansExpressionHelper.BuildShardClearLambda<UnshardedModel>("orders", true, null)).Should().Throw<ShardKeyNotFoundException>().WithMessage("*“orders”*");

            new ShardKeyNotFoundException().Should().BeAssignableTo<Exception>().And.NotBeAssignableTo<ApplicationException>();
            new ShardKeyNotFoundException().Message.Should().Contain("ShardKey attribute could not be found");
            new ShardKeyNotFoundException("orders", new Exception("x")).InnerException!.Message.Should().Be("x");
        }

        public class SeveralShardKeysModel
        {
            [FakeMapShardKey("NullableShard", "NullableId")]
            [FakeMapInteger("NullableId")]
            public ShardKey<int>? Nullable { get; set; }

            [FakeMapShardKey("PlainShard", "PlainId", null, null, null, false)]
            [FakeMapInteger("PlainId")]
            public ShardKey<int> Plain { get; set; } = new ShardKey<int>(1, 1);

            [FakeMapShardKey("MarkedShard", "MarkedId")]
            [FakeMapInteger("MarkedId")]
            public ShardKey<int> Marked { get; set; } = new ShardKey<int>(2, 2);
        }

        public class UnmarkedShardKeysModel
        {
            [FakeMapShardKey("NullableShard", "NullableId")]
            [FakeMapInteger("NullableId")]
            public ShardKey<int>? Nullable { get; set; }

            [FakeMapShardKey("FirstShard", "FirstId", null, null, null, false)]
            [FakeMapInteger("FirstId")]
            public ShardKey<int> First { get; set; } = new ShardKey<int>(1, 1);

            [FakeMapShardKey("SecondShard", "SecondId", null, null, null, false)]
            [FakeMapInteger("SecondId")]
            public ShardKey<int> Second { get; set; } = new ShardKey<int>(2, 2);
        }

        [Fact]
        [Trait("Spec", "blk-2ea3cefb1d6848ceaca35a534a97ee20")]
        public void TheShardDelegatesPreferTheRecordIdentifierShardKeyAndOtherwiseTheFirstNonNullableOne()
        {
            var identity = new ShardKey<int>(7, 77).ToUtf8();

            var marked = new SeveralShardKeysModel();
            OrleansExpressionHelper.BuildSetShardLambda<SeveralShardKeysModel>("orders", null)(identity, marked, null);
            marked.Marked.Should().Be(new ShardKey<int>(7, 77), "the IsRecordIdentifier property wins over earlier ones");
            marked.Plain.Should().Be(new ShardKey<int>(1, 1));
            var prms = new ParameterCollection();
            OrleansExpressionHelper.BuildShardReadLambda<SeveralShardKeysModel>("orders", null)(identity, prms, null).Should().Be(7);
            prms.Cast<System.Data.Common.DbParameter>().Select(p => p.ParameterName).Should().Equal("@MarkedShard", "@MarkedId");
            OrleansExpressionHelper.BuildShardWriteLambda<SeveralShardKeysModel>("orders", false)(identity, marked).Should().Be(7);

            var unmarked = new UnmarkedShardKeysModel();
            OrleansExpressionHelper.BuildSetShardLambda<UnmarkedShardKeysModel>("orders", null)(identity, unmarked, null);
            unmarked.First.Should().Be(new ShardKey<int>(7, 77), "without a marked key the first non-nullable ShardKey in declaration order is used");
            unmarked.Second.Should().Be(new ShardKey<int>(2, 2));
            unmarked.Nullable.Should().BeNull();
            var prms2 = new ParameterCollection();
            OrleansExpressionHelper.BuildShardReadLambda<UnmarkedShardKeysModel>("orders", null)(identity, prms2, null).Should().Be(7);
            prms2.Cast<System.Data.Common.DbParameter>().Select(p => p.ParameterName).Should().Equal("@FirstShard", "@FirstId");
        }

        #endregion

        #region QueryExtensions and package surface

        [Fact]
        [Trait("Spec", "blk-0e1732fa0eb94384a6755f8eebfeee39")]
        [Trait("Spec", "blk-56515be7ec1041328afb34be6834992d")]
        [Trait("Spec", "blk-bcaead24e52742fbac05f9e00437d433")]
        public void ShardIdReturnsTheShardIdentifierEncodedInTheGrainKey()
        {
            var grainId = GrainId.Create(GrainType.Create("orders"), new IdSpan(new ShardKey<int>(17, 5).ToUtf8().ToArray()));
            grainId.ShardId().Should().Be(17);

            var compound = GrainId.Create(GrainType.Create("orders"), new IdSpan(new ShardKey<Guid, long>(-3, Guid.NewGuid(), 9L).ToUtf8().ToArray()));
            compound.ShardId().Should().Be(-3);
        }

        [Fact]
        [Trait("Spec", "blk-df8fd4798bae4fd9b106145682054616")]
        public void ThePackageExposesNoIdParserType()
        {
            var types = typeof(SpanParser).Assembly.GetTypes().Select(t => t.Name).ToList();

            types.Should().NotContain("IdParser").And.NotContain("IdParser2");
        }

        #endregion
    }
}
