using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Additional, non-happy-path coverage for ShardKey variants.
    /// Some tests intentionally document current defects (marked with Skip) so they can be enabled after fixes.
    /// </summary>
    public class ShardKeyRobustTests
    {
        // ---------- Helpers ----------
        private static string CorruptExternalString(string good)
        {
            // Flip a payload character (keep first two checksum chars so checksum mismatch occurs)
            if (good.Length <= 3) return good + "A";
            var chars = good.ToCharArray();
            for (int i = 2; i < chars.Length; i++)
            {
                if (chars[i] != chars[0])
                {
                    chars[i] = chars[i] == 'A' ? 'B' : 'A';
                    break;
                }
            }
            return new string(chars);
        }

        private static byte[] CorruptGreatGrandChildMetadata(byte[] raw)
        {
            // raw[0] = header; metadata bytes follow.
            // For 4-type key metaLen==3. Metadata bytes at [1],[2],[3].
            // Great-grandchild 6 bits live in low six bits of metadata[3] (after packing order).
            var copy = (byte[])raw.Clone();
            if (copy.Length < 5) return copy;
            copy[3] ^= 0b0001_1111; // flip several low bits -> changes great-grandchild type code only.
            return copy;
        }

        // ---------- Empty / Basics ----------
        [Fact]
        public void Empty_SharedAcrossVariants_Defaults()
        {
            var k1 = ShardKey<int>.Empty;
            k1.IsEmpty.Should().BeTrue();
            k1.ShardId.Should().Be(0);
            k1.RecordId.Should().Be(0);

            var k2 = ShardKey<int, short>.Empty;
            k2.IsEmpty.Should().BeTrue();
            k2.ChildId.Should().Be(0);

            var k3 = ShardKey<int, short, byte>.Empty;
            k3.IsEmpty.Should().BeTrue();
            k3.GrandChildId.Should().Be(0);

            var k4 = ShardKey<int, short, byte, long>.Empty;
            k4.IsEmpty.Should().BeTrue();
            k4.GreatGrandChildId.Should().Be(0L);
        }

        [Fact]
        [Trait("Spec", "blk-e69384c75de041938f2a12571f415bda")]
        [Trait("Spec", "blk-dc5f9f9bd788438d93c4147818aff3c1")]
        public void Empty_String_Defaults()
        {
            var k1 = ShardKey<string>.Empty;
            k1.IsEmpty.Should().BeTrue();
            k1.ShardId.Should().Be(0);
            k1.RecordId.Should().Be(string.Empty);
        }

        [Fact]
        [Trait("Spec", "blk-dc5f9f9bd788438d93c4147818aff3c1")]
        public void Empty_String_Defaults_AllArities()
        {
            var k2 = ShardKey<string, int>.Empty;
            k2.IsEmpty.Should().BeTrue();
            k2.RecordId.Should().Be(string.Empty);

            var k3 = ShardKey<int, string, int>.Empty;
            k3.IsEmpty.Should().BeTrue();
            k3.ChildId.Should().Be(string.Empty);

            var k4 = ShardKey<int, int, int, string>.Empty;
            k4.IsEmpty.Should().BeTrue();
            k4.GreatGrandChildId.Should().Be(string.Empty);
        }

        [Fact]
        [Trait("Spec", "blk-7176e67845364a74927338a5196acaa5")]
        [Trait("Spec", "blk-dc5f9f9bd788438d93c4147818aff3c1")]
        public void IsEmpty_AgreesAcrossArities_ForEmptyStringKeys()
        {
            new ShardKey<string>(0, "").IsEmpty.Should().BeTrue();
            new ShardKey<string, int>(0, "", 0).IsEmpty.Should().BeTrue();
            new ShardKey<string, int, int>(0, "", 0, 0).IsEmpty.Should().BeTrue();
            new ShardKey<string, int, int, int>(0, "", 0, 0, 0).IsEmpty.Should().BeTrue();
            new ShardKey<string, int>(0, "a", 0).IsEmpty.Should().BeFalse();
        }

        [Fact]
        [Trait("Spec", "blk-dc5f9f9bd788438d93c4147818aff3c1")]
        public void IsEmpty_UninitializedStringKey_IsEmptyAndDoesNotThrow()
        {
            default(ShardKey<string>).IsEmpty.Should().BeTrue();
            default(ShardKey<string, string>).IsEmpty.Should().BeTrue();
            default(ShardKey<string, string, string>).IsEmpty.Should().BeTrue();
            default(ShardKey<string, string, string, string>).IsEmpty.Should().BeTrue();
        }

        [Fact]
        [Trait("Spec", "blk-fb0058f97f11478aac677e63099f32df")]
        public void Equality_And_Inequality_Work()
        {
            var a = new ShardKey<int>(2, 100);
            var b = new ShardKey<int>(2, 100);
            var c = new ShardKey<int>(3, 100);
            (a == b).Should().BeTrue();
            (a != c).Should().BeTrue();

            var ch1 = new ShardKey<int, short>(2, 100, 5);
            var ch2 = new ShardKey<int, short>(2, 100, 5);
            var ch3 = new ShardKey<int, short>(2, 100, 6);
            ch1.Should().Be(ch2);
            ch1.Should().NotBe(ch3);
        }

        [Fact]
        [Trait("Spec", "blk-fb0058f97f11478aac677e63099f32df")]
        public void HashCode_Stable_For_SameValues()
        {
            var a1 = new ShardKey<long>(7, 9999999999);
            var a2 = new ShardKey<long>(7, 9999999999);
            a1.GetHashCode().Should().Be(a2.GetHashCode());
        }

        // ---------- Serialization Round Trips ----------
        [Theory]
        [InlineData(0, 1)]
        [InlineData(short.MaxValue, int.MaxValue)]
        [InlineData(42, 123456)]
        [Trait("Spec", "blk-1382dcb681fa4794b25642d74579d558")]
        [Trait("Spec", "blk-48ec5c393ad342319928a62dff09ab47")]
        public void ShardKey_Serialize_RoundTrip(short shard, int record)
        {
            var sk = new ShardKey<int>(shard, record);
            var arr = sk.ToArray();
            var parsed = new ShardKey<int>(arr);
            parsed.Should().Be(sk);

            var ext = sk.ToExternalString();
            var parsed2 = ShardKey<int>.FromExternalString(ext);
            parsed2.Should().Be(sk);

            var utf8 = sk.ToUtf8();
            var parsed3 = new ShardKey<int>(utf8.Span);
            parsed3.Should().Be(sk);
        }

        [Fact]
        public void ShardChild_Serialize_RoundTrip()
        {
            var sc = new ShardKey<int, Guid>(5, 67890, Guid.NewGuid());
            var arr = sc.ToArray();
            new ShardKey<int, Guid>(arr).Should().Be(sc);
            ShardKey<int, Guid>.FromExternalString(sc.ToExternalString()).Should().Be(sc);
        }

        [Fact]
        public void ShardGrandChild_Serialize_RoundTrip()
        {
            var gc = new ShardKey<int, short, byte>(1, 2, 77, 9);
            var arr = gc.ToArray();
            new ShardKey<int, short, byte>(arr).Should().Be(gc);
            ShardKey<int, short, byte>.FromExternalString(gc.ToExternalString()).Should().Be(gc);
        }

        [Fact]
        public void ShardGreatGrandChild_Serialize_RoundTrip()
        {
            var ggc = new ShardKey<int, short, byte, string>(3, 2, 77, 9, "X");
            var arr = ggc.ToArray();
            new ShardKey<int, short, byte, string>(arr).Should().Be(ggc);
            ShardKey<int, short, byte, string>.FromExternalString(ggc.ToExternalString()).Should().Be(ggc);
        }

        // ---------- Tamper / Corruption Detection ----------
        [Fact]
        [Trait("Spec", "blk-47c1a251f6944b8399d1a25a4ea9695f")]
        public void FromExternalString_CorruptedChecksum_Throws()
        {
            var sk = new ShardKey<int>(1, 42);
            var ext = sk.ToExternalString();
            var bad = CorruptExternalString(ext);
            Action act = () => ShardKey<int>.FromExternalString(bad);
            act.Should().Throw<Exception>().WithMessage("*not valid*");
        }

        // ---------- TryParse Tests (Documenting Current Defects) ----------
        [Fact]
        [Trait("Spec", "blk-73983907932349c19a5928e3110a1a12")]
        [Trait("Spec", "blk-832af49464594a2891b06b587cdebdb7")]
        public void TryParse_ChildKey_Succeeds_AfterFix()
        {
            var key = new ShardKey<int, short>(4, 400, 5);
            var data = key.ToArray();
            var ok = ShardKey<int, short>.TryParse(data.Span, out var parsed);
            ok.Should().BeTrue();
            parsed.Should().Be(key);
        }

        [Fact]
        public void TryParse_GrandChildKey_Succeeds_AfterFix()
        {
            var key = new ShardKey<int, short, byte>(45, 1, 2, 3);
            var data = key.ToArray();
            var ok = ShardKey<int, short, byte>.TryParse(data.Span, out var parsed);
            ok.Should().BeTrue();
            parsed.Should().Be(key);
        }

        [Fact]
        public void TryParse_GreatGrandChildKey_Succeeds_AfterFix()
        {
            var key = new ShardKey<int, short, byte, string>(17, 1, 2, 3, "z");
            var data = key.ToArray();
            var ok = ShardKey<int, short, byte, string>.TryParse(data.Span, out var parsed);
            ok.Should().BeTrue();
            parsed.Should().Be(key);
        }

        [Fact]
        public void TryParse_SingleType_Succeeds()
        {
            var key = new ShardKey<long>(9, 1234567890);
            var data = key.ToArray();
            var ok = ShardKey<long>.TryParse(data.Span, out var parsed);
            ok.Should().BeTrue();
            parsed.Should().Be(key);
        }

        [Fact]
        [Trait("Spec", "blk-8731259a6be747e4af8d57b5ccf8681f")]
        [Trait("Spec", "blk-832af49464594a2891b06b587cdebdb7")]
        public void TryParse_ShortData_Fails()
        {
            var tiny = new byte[] { 0x81 }; // header only (metaLen=1 + no payload)
            var ok = ShardKey<int>.TryParse(tiny, out _);
            ok.Should().BeFalse();
        }

        // ---------- ForeignShards ----------
        [Fact]
        [Trait("Spec", "blk-94135859029f48cfaffef2a34471bcf1")]
        [Trait("Spec", "blk-a3a6682f824f456485e0a7053a75170f")]
        public void ForeignShards_ExcludesCurrentShard()
        {
            var baseKey = new ShardKey<int>(5, 10);
            var list = new List<ShardKey<int>>
            {
                baseKey,
                new ShardKey<int>(6, 11),
                new ShardKey<int>(6, 12),
                new ShardKey<int>(7, 13),
            };
            var foreign = baseKey.ForeignShards(list);
            foreign.Shards.Keys.Should().BeEquivalentTo(new short[] { 6, 7 });
            foreign.Shards.Keys.Should().NotContain(baseKey.ShardId);
        }

        // ---------- Merge Behavior ----------
        private class Model1 : IKeyedModel<int>
        {
            public ShardKey<int> Key { get; set; }
            public string? Value { get; set; }
        }

        [Fact]
        [Trait("Spec", "blk-42e2af8988e8467085b0ff9c6223fb4a")]
        public void Merge_Replaces_Matching_Records()
        {
            var original = new List<Model1>
            {
                new Model1 { Key = new ShardKey<int>(1, 10), Value = "old10" },
                new Model1 { Key = new ShardKey<int>(1, 11), Value = "old11" }
            };
            var replacements = new List<Model1>
            {
                new Model1 { Key = new ShardKey<int>(1, 11), Value = "new11" }
            };
            var merged = ShardKey<int>.Merge(original, replacements, appendUnmatchedReplacements: false);
            merged.Single(m => m.Key.RecordId == 11).Value.Should().Be("new11");
            merged.Count.Should().Be(2);
        }

        [Fact]
        [Trait("Spec", "blk-42e2af8988e8467085b0ff9c6223fb4a")]
        [Trait("Spec", "blk-f0dd107e67494f76a84692ef686cbeb9")]
        public void Merge_Appends_Unmatched_Replacements_When_Requested()
        {
            var original = new List<Model1>
            {
                new Model1 { Key = new ShardKey<int>(1, 10), Value = "old10" }
            };
            var replacements = new List<Model1>
            {
                new Model1 { Key = new ShardKey<int>(1, 10), Value = "new10" },
                new Model1 { Key = new ShardKey<int>(1, 11), Value = "new11" } // unmatched, should be appended
            };
            var merged = ShardKey<int>.Merge(original, replacements, appendUnmatchedReplacements: true);
            merged.Should().HaveCount(2);
            merged.Any(m => m.Key.RecordId == 11).Should().BeTrue();
        }
    }
}