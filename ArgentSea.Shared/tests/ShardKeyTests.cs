using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec section shard-key: the ShardKey family's equality and hash invariants across all four arities, IsEmpty and Empty,
    /// ToString per arity, serialization round trips, shard-value collection, foreign-shard exclusion and keyed-record merging.
    /// </summary>
    public class ShardKeyTests
    {
        private class Keyed : IKeyedModel<int>
        {
            public ShardKey<int> Key { get; set; }
            public string? Value { get; set; }
        }

        private class KeyedChild : IKeyedModel<int, short>
        {
            public ShardKey<int, short> Key { get; set; }
        }

        #region Equality and hash codes

        public static IEnumerable<object[]> EqualityCases()
        {
            yield return new object[] { new ShardKey<int>(2, 100), new ShardKey<int>(2, 100), new ShardKey<int>(3, 100), new ShardKey<int>(2, 101) };
            yield return new object[] { new ShardKey<int, short>(2, 100, 5), new ShardKey<int, short>(2, 100, 5), new ShardKey<int, short>(3, 100, 5), new ShardKey<int, short>(2, 100, 6) };
            yield return new object[] { new ShardKey<int, short, byte>(2, 100, 5, 9), new ShardKey<int, short, byte>(2, 100, 5, 9), new ShardKey<int, short, byte>(3, 100, 5, 9), new ShardKey<int, short, byte>(2, 100, 5, 8) };
            yield return new object[] { new ShardKey<int, short, byte, string>(2, 100, 5, 9, "X"), new ShardKey<int, short, byte, string>(2, 100, 5, 9, "X"), new ShardKey<int, short, byte, string>(3, 100, 5, 9, "X"), new ShardKey<int, short, byte, string>(2, 100, 5, 9, "Y") };
        }

        [Theory]
        [Trait("Spec", "blk-fb0058f97f11478aac677e63099f32df")]
        [Trait("Spec", "blk-8024c324446040f38b972c9a9fb99aa3")]
        [MemberData(nameof(EqualityCases))]
        public void KeysAreEqualOnlyWhenEveryConstituentMatchesAndEqualKeysHashAlike(object same1, object same2, object differentShard, object differentLastPart)
        {
            same1.Equals(same2).Should().BeTrue();
            same1.GetHashCode().Should().Be(same2.GetHashCode(), "equal values yield equal hash codes");
            same1.Equals(differentShard).Should().BeFalse("the shard identifier differs");
            same1.Equals(differentLastPart).Should().BeFalse("the last identity position differs");
            same1.Equals(null).Should().BeFalse();
            same1.Equals("not a key").Should().BeFalse();
        }

        [Fact]
        [Trait("Spec", "blk-fb0058f97f11478aac677e63099f32df")]
        public void EqualityOperatorsAgreeWithEqualsAtEveryArity()
        {
            (new ShardKey<int>(2, 100) == new ShardKey<int>(2, 100)).Should().BeTrue();
            (new ShardKey<int>(2, 100) != new ShardKey<int>(3, 100)).Should().BeTrue();
            (new ShardKey<int, short>(2, 100, 5) == new ShardKey<int, short>(2, 100, 5)).Should().BeTrue();
            (new ShardKey<int, short>(2, 100, 5) != new ShardKey<int, short>(2, 101, 5)).Should().BeTrue();
            (new ShardKey<int, short, byte>(2, 100, 5, 9) == new ShardKey<int, short, byte>(2, 100, 5, 9)).Should().BeTrue();
            (new ShardKey<int, short, byte>(2, 100, 5, 9) != new ShardKey<int, short, byte>(2, 100, 6, 9)).Should().BeTrue();
            (new ShardKey<int, short, byte, string>(2, 100, 5, 9, "X") == new ShardKey<int, short, byte, string>(2, 100, 5, 9, "X")).Should().BeTrue();
            (new ShardKey<int, short, byte, string>(2, 100, 5, 9, "X") != new ShardKey<int, short, byte, string>(2, 100, 5, 8, "X")).Should().BeTrue();
        }

        #endregion

        #region Empty

        [Fact]
        [Trait("Spec", "blk-e69384c75de041938f2a12571f415bda")]
        [Trait("Spec", "blk-7176e67845364a74927338a5196acaa5")]
        [Trait("Spec", "blk-dc5f9f9bd788438d93c4147818aff3c1")]
        public void StringKeyEmptyValuesUseTheEmptyStringAndReportEmptyAtEveryArity()
        {
            ShardKey<string>.Empty.IsEmpty.Should().BeTrue();
            ShardKey<string>.Empty.RecordId.Should().Be(string.Empty, "Empty's record id is the empty string, not null");
            ShardKey<string, int>.Empty.IsEmpty.Should().BeTrue();
            ShardKey<string, int>.Empty.RecordId.Should().Be(string.Empty);
            new ShardKey<string, int>(0, "", 0).IsEmpty.Should().BeTrue();
            new ShardKey<string>(0, "").IsEmpty.Should().Be(new ShardKey<string, int>(0, "", 0).IsEmpty, "an empty-string key reports the same result regardless of arity");
            new ShardKey<string, int>(0, "a", 0).IsEmpty.Should().BeFalse();
            new ShardKey<string, int>(1, "", 0).IsEmpty.Should().BeFalse("a non-zero shard id is never empty");
            ShardKey<int, string, int, string>.Empty.IsEmpty.Should().BeTrue();
            ShardKey<int, string, int, string>.Empty.GreatGrandChildId.Should().Be(string.Empty);
            default(ShardKey<string, string, string, string>).IsEmpty.Should().BeTrue("a null identity position is treated as empty");
        }

        [Fact]
        [Trait("Spec", "blk-dc5f9f9bd788438d93c4147818aff3c1")]
        public void ValueTypeKeysAreEmptyOnlyAtTheirDefaultValues()
        {
            ShardKey<int>.Empty.Should().Be(new ShardKey<int>(0, 0));
            new ShardKey<int>(0, 1).IsEmpty.Should().BeFalse();
            new ShardKey<Guid>(0, Guid.Empty).IsEmpty.Should().BeTrue();
            new ShardKey<Guid>(0, Guid.NewGuid()).IsEmpty.Should().BeFalse();
            ShardKey<int, short, byte, long>.Empty.IsEmpty.Should().BeTrue();
            new ShardKey<int, short, byte, long>(0, 0, 0, 0, 1).IsEmpty.Should().BeFalse("the last position is not at its empty value");
        }

        #endregion

        #region ToString

        [Fact]
        [Trait("Spec", "blk-6c375ebbb0d14e2ab3310985f83b000f")]
        [Trait("Spec", "blk-a6ac290717d948e1b37f0596ad60af4e")]
        public void ToStringRendersTheShardIdAndEveryIdentityPositionInOrder()
        {
            new ShardKey<int>(3, 2).ToString().Should().Be("{ \"shard\": 3, \"id\": \"2\"}");
            new ShardKey<int, short>(3, 2, 77).ToString().Should().Be("\"shard\": 3, \"ids\": [\"2\", \"77\"]");
            new ShardKey<int, short, byte>(3, 2, 77, 9).ToString().Should().Be("\"shard\": 3, \"ids\": [\"2\", \"77\", \"9\"]");
            new ShardKey<int, short, byte, string>(3, 2, 77, 9, "X").ToString().Should().Be("\"shard\": 3, \"ids\": [\"2\", \"77\", \"9\", \"X\"]");
        }

        #endregion

        #region Serialization round trips

        [Fact]
        [Trait("Spec", "blk-1382dcb681fa4794b25642d74579d558")]
        [Trait("Spec", "blk-48ec5c393ad342319928a62dff09ab47")]
        [Trait("Spec", "blk-ed0b0d30cd434cd080e7b74afcad9025")]
        public void AFourPartKeyRoundTripsThroughBinaryUtf8AndExternalForms()
        {
            var key = new ShardKey<int, short, byte, string>(3, 2, 77, 9, "X");

            var fromBinary = new ShardKey<int, short, byte, string>(key.ToArray());
            var fromUtf8 = new ShardKey<int, short, byte, string>(key.ToUtf8().Span);
            var fromExternal = ShardKey<int, short, byte, string>.FromExternalString(key.ToExternalString());

            foreach (var copy in new[] { fromBinary, fromUtf8, fromExternal })
            {
                copy.Should().Be(key);
                (copy.ShardId, copy.RecordId, copy.ChildId, copy.GrandChildId, copy.GreatGrandChildId).Should().Be(((short)3, 2, (short)77, (byte)9, "X"));
            }
        }

        [Fact]
        [Trait("Spec", "blk-48ec5c393ad342319928a62dff09ab47")]
        public void EveryArityRoundTripsThroughEachSupportedForm()
        {
            var one = new ShardKey<long>(1, 9_000_000_000L);
            var two = new ShardKey<int, short>(4, 400, 5);
            var three = new ShardKey<Guid, int, string>(45, Guid.NewGuid(), 2, "three");

            new ShardKey<long>(one.ToArray()).Should().Be(one);
            new ShardKey<long>(one.ToUtf8().Span).Should().Be(one);
            ShardKey<long>.FromExternalString(one.ToExternalString()).Should().Be(one);
            new ShardKey<int, short>(two.ToArray()).Should().Be(two);
            new ShardKey<int, short>(two.ToUtf8().Span).Should().Be(two);
            ShardKey<int, short>.FromExternalString(two.ToExternalString()).Should().Be(two);
            new ShardKey<Guid, int, string>(three.ToArray()).Should().Be(three);
            new ShardKey<Guid, int, string>(three.ToUtf8().Span).Should().Be(three);
            ShardKey<Guid, int, string>.FromExternalString(three.ToExternalString()).Should().Be(three);
        }

        [Fact]
        [Trait("Spec", "blk-73983907932349c19a5928e3110a1a12")]
        [Trait("Spec", "blk-832af49464594a2891b06b587cdebdb7")]
        public void TryParseReconstructsAKeyFromItsBinaryOrUtf8Bytes()
        {
            var key = new ShardKey<int, short>(4, 400, 5);

            ShardKey<int, short>.TryParse(key.ToArray().Span, out var fromBinary).Should().BeTrue();
            ShardKey<int, short>.TryParse(key.ToUtf8().Span, out var fromUtf8).Should().BeTrue();

            fromBinary.Should().Be(key);
            fromUtf8.Should().Be(key);
        }

        [Fact]
        [Trait("Spec", "blk-8731259a6be747e4af8d57b5ccf8681f")]
        [Trait("Spec", "blk-832af49464594a2891b06b587cdebdb7")]
        public void TryParseReturnsFalseAndTheEmptyKeyForUndersizedData()
        {
            ShardKey<int>.TryParse(new byte[] { 0x81 }, out var result).Should().BeFalse();
            result.Should().Be(ShardKey<int>.Empty);
            ShardKey<int>.TryParse(new byte[] { 0x81, 0x04, 0x01, 0x00 }, out result).Should().BeFalse("a shard id without a record id is undersized");
            result.Should().Be(ShardKey<int>.Empty);
        }

        [Fact]
        [Trait("Spec", "blk-9c6558d4aab04ee29f1589e5317b7900")]
        [Trait("Spec", "blk-cce005d48ad44ea0aff90fe2a57857cd")]
        [Trait("Spec", "blk-4491ecdb11c8413dbf6a235fd0af58d2")]
        public void TheKeyFamilyImplementsIShardKeyAndIKeyedModelExposesAMatchingKey()
        {
            IShardKey key = new ShardKey<int, short>(4, 400, 5);

            key.ShardId.Should().Be(4);
            key.IsEmpty.Should().BeFalse();
            key.ToArray().Length.Should().BeGreaterThan(0);
            key.ToUtf8().Length.Should().BeGreaterThan(0);
            key.ToExternalString().Should().NotBeNullOrEmpty();
            typeof(IShardKey).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(new[] { "ShardId", "IsEmpty" });
            typeof(IShardKey).GetMethods().Select(m => m.Name).Should().BeEquivalentTo(new[] { "ToExternalString", "ToArray", "ToUtf8", "get_ShardId", "get_IsEmpty" });
            foreach (var arity in new[] { typeof(ShardKey<>), typeof(ShardKey<,>), typeof(ShardKey<,,>), typeof(ShardKey<,,,>) })
            {
                arity.IsValueType.Should().BeTrue();
                arity.GetInterfaces().Should().Contain(typeof(IShardKey));
                arity.GetGenericArguments().Should().OnlyContain(t => t.GetGenericParameterConstraints().Contains(typeof(IComparable)));
            }
            typeof(IKeyedModel<int, short>).GetProperty("Key")!.PropertyType.Should().Be(typeof(ShardKey<int, short>));
            typeof(IKeyedModel<int, short>).GetProperty("Key")!.CanWrite.Should().BeFalse();
            typeof(ShardKey<,,,>).GetProperties().Select(p => p.Name).Should().Contain(new[] { "ShardId", "RecordId", "ChildId", "GrandChildId", "GreatGrandChildId" });
            typeof(ShardKey<,>).GetProperties().Select(p => p.Name).Should().NotContain("GrandChildId");
        }

        [Fact]
        [Trait("Spec", "blk-2dabbb25d492416587b62c62170576e7")]
        public void InvalidShardArgumentsExceptionCarriesOnlyItsMessages()
        {
            new InvalidShardArgumentsException().Message.Should().Be("An empty shard key type must have zeros for shard and record values.");
            new InvalidShardArgumentsException("custom").Message.Should().Be("custom");
            new InvalidShardArgumentsException("custom", new Exception("inner")).InnerException!.Message.Should().Be("inner");
            typeof(InvalidShardArgumentsException).Should().BeSealed().And.BeDerivedFrom<ApplicationException>();
            typeof(InvalidShardArgumentsException).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Should().BeEmpty();
        }

        #endregion

        #region Shard values, foreign shards and merge

        [Fact]
        [Trait("Spec", "blk-c954b8af21624d6b88228fce414af22e")]
        [Trait("Spec", "blk-76d462a3a00444ff8bf8a7011627d6ab")]
        public void ToShardsValuesContainsEachDistinctShardIdentifierOnce()
        {
            var records = new List<Keyed>
            {
                new Keyed { Key = new ShardKey<int>(5, 10) }, new Keyed { Key = new ShardKey<int>(6, 11) }, new Keyed { Key = new ShardKey<int>(6, 12) }, new Keyed { Key = new ShardKey<int>(7, 13) }
            };

            var values = ShardKey<int>.ToShardsValues(records);

            values.Shards.Keys.Should().BeEquivalentTo(new short[] { 5, 6, 7 });
            values.Count.Should().Be(3, "duplicate keys on the same shard contribute one identifier");
        }

        [Fact]
        [Trait("Spec", "blk-94135859029f48cfaffef2a34471bcf1")]
        [Trait("Spec", "blk-a3a6682f824f456485e0a7053a75170f")]
        public void ForeignShardsExcludesTheReceivingKeysShardAndListsEachOtherShardOnce()
        {
            var receiving = new ShardKey<int>(5, 10);
            var keys = new List<ShardKey<int>> { receiving, new ShardKey<int>(6, 11), new ShardKey<int>(6, 12), new ShardKey<int>(7, 13) };
            var models = keys.Select(k => new Keyed { Key = k }).ToList();

            receiving.ForeignShards(keys).Shards.Keys.Should().BeEquivalentTo(new short[] { 6, 7 });
            receiving.ForeignShards((IList<ShardKey<int>>)keys).Shards.Keys.Should().BeEquivalentTo(new short[] { 6, 7 });
            receiving.ForeignShards(models).Shards.Keys.Should().BeEquivalentTo(new short[] { 6, 7 });
            var child = new ShardKey<int, short>(1, 1, 1);
            child.ForeignShards(new List<KeyedChild> { new KeyedChild { Key = child }, new KeyedChild { Key = new ShardKey<int, short>(2, 1, 1) } }).Shards.Keys.Should().Equal(2);
        }

        [Fact]
        [Trait("Spec", "blk-f0dd107e67494f76a84692ef686cbeb9")]
        [Trait("Spec", "blk-42e2af8988e8467085b0ff9c6223fb4a")]
        public void MergeReplacesMatchingRecordsAndAppendsUnmatchedOnesOnlyWhenRequested()
        {
            var master = new List<Keyed> { new Keyed { Key = new ShardKey<int>(1, 10), Value = "old10" }, new Keyed { Key = new ShardKey<int>(1, 11), Value = "old11" } };
            var replacements = new List<Keyed> { new Keyed { Key = new ShardKey<int>(1, 11), Value = "new11" }, new Keyed { Key = new ShardKey<int>(1, 12), Value = "new12" } };

            var appended = ShardKey<int>.Merge(master, replacements, appendUnmatchedReplacements: true);
            var notAppended = ShardKey<int>.Merge(master, replacements, appendUnmatchedReplacements: false);

            appended.Select(m => (m.Key.RecordId, m.Value)).Should().BeEquivalentTo(new[] { (10, "old10"), (11, "new11"), (12, "new12") });
            notAppended.Select(m => (m.Key.RecordId, m.Value)).Should().BeEquivalentTo(new[] { (10, "old10"), (11, "new11") });
            ShardKey<int>.Merge((IList<Keyed>)master, (IList<Keyed>)replacements, true).Should().HaveCount(3, "the IList overloads behave the same");
            ShardKey<int>.Merge(master, new List<Keyed> { new Keyed { Key = new ShardKey<int>(2, 11), Value = "other shard" } }).Select(m => m.Value).Should().Equal(new[] { "old10", "old11" }, "a key on another shard does not match by record id alone");
        }

        #endregion
    }
}
