using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec section shard-targeting-values: ShardsValues construction, Add, Count and enumeration placeholders, CopyTo, Merge rules,
    /// Remove, ShardListForeign across every key arity and both collection forms, and the ShardParameterValue carrier.
    /// </summary>
    public class ShardTargetingValuesTests
    {
        private class Keyed1 : IKeyedModel<int> { public ShardKey<int> Key { get; set; } }
        private class Keyed2 : IKeyedModel<int, int> { public ShardKey<int, int> Key { get; set; } }
        private class Keyed3 : IKeyedModel<int, int, int> { public ShardKey<int, int, int> Key { get; set; } }
        private class Keyed4 : IKeyedModel<int, int, int, int> { public ShardKey<int, int, int, int> Key { get; set; } }

        #region Construction and Add

        [Fact]
        [Trait("Spec", "blk-e8ce4a25a8324472b54913ad5ab5d56a")]
        [Trait("Spec", "blk-fa8221bd205a47ee9cb8cf47f96cafd1")]
        [Trait("Spec", "blk-3c01074987804ada8e046447cf9f617f")]
        public void TheListConstructorAddsOneEmptyShardPerDistinctIdentifier()
        {
            var values = new ShardsValues(new List<short> { 3, 7, 3 });
            var empty = new ShardsValues(new List<short>());

            values.Shards.Keys.Should().BeEquivalentTo(new short[] { 3, 7 });
            values.Shards.Values.Should().OnlyContain(p => p != null && p.Count == 0);
            values.Count.Should().Be(2);
            empty.Count.Should().Be(0);
            empty.Add(1).Cast<ShardParameterValue>().Should().ContainSingle().Which.ShardId.Should().Be(1);
        }

        [Fact]
        [Trait("Spec", "blk-3f8c71b2508d4f1e943b0ae6b752cbe7")]
        [Trait("Spec", "blk-7375353a653040bc995ac02fa4d4798e")]
        [Trait("Spec", "blk-0c881ae8341e4adda228a41ee4619c75")]
        public void NullConstructorArgumentsThrowArgumentNullExceptionNamingTheParameter()
        {
            new Action(() => new ShardsValues((IList<short>)null!)).Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("list");
            new Action(() => new ShardsValues((Dictionary<short, IDictionary<string, object>>)null!)).Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("shards");
            new Action(() => new ShardsValues().Merge((ShardsValues)null!)).Should().Throw<ArgumentNullException>();
            new Action(() => new ShardsValues().Merge((IList<short>)null!)).Should().Throw<ArgumentNullException>();
        }

        [Fact]
        [Trait("Spec", "blk-0bf207dc0f1848e2b89a180338d82543")]
        [Trait("Spec", "blk-915bec8a5ce24f9ca3f1832c1fa38282")]
        [Trait("Spec", "blk-e04b6bf7290f4cc888d1230b6c775f01")]
        public void AddCreatesOrPreservesTheShardAndReturnsTheSameCollection()
        {
            var values = new ShardsValues();

            var afterPlain = values.Add(7);
            var afterNamed = values.Add(7, "region", "west");
            values.Add(2);

            afterPlain.Should().BeSameAs(values);
            afterNamed.Should().BeSameAs(values);
            values.Cast<ShardParameterValue>().Select(v => (v.ShardId, v.ParameterName, v.ParameterValue)).Should().BeEquivalentTo(new[] { ((short)7, "region", (object)"west"), ((short)2, (string?)null, (object?)null) });
            values.Add(7).Shards[7].Should().HaveCount(1, "re-adding an existing shard preserves its values");
            new Action(() => values.Add(7, "region", "east")).Should().Throw<ArgumentException>("the name already exists for the shard");
        }

        #endregion

        #region Count, enumeration and CopyTo

        [Fact]
        [Trait("Spec", "blk-eff1cfce177c4fa9a3f8a4e955eef8b1")]
        [Trait("Spec", "blk-fc9cd525b2644a1daf53f6a2f8a8b495")]
        public void CountAndEnumerationYieldOnePlaceholderPerEmptyShardAndOneEntryPerNamedValue()
        {
            var values = new ShardsValues(new Dictionary<short, IDictionary<string, object>>
            {
                { 3, new Dictionary<string, object>() },
                { 8, new Dictionary<string, object> { { "limit", 25 } } },
                { 9, new Dictionary<string, object> { { "a", 1 }, { "b", 2 } } }
            });

            values.Count.Should().Be(4);
            values.Cast<ShardParameterValue>().Select(v => (v.ShardId, v.ParameterName, v.ParameterValue))
                .Should().Equal(((short)3, null, null), ((short)8, "limit", 25), ((short)9, "a", 1), ((short)9, "b", 2));
            values.Count.Should().Be(values.Cast<ShardParameterValue>().Count());
        }

        [Fact]
        [Trait("Spec", "blk-718eb1abbdb84ae2a6159149bddbec20")]
        [Trait("Spec", "blk-10b4ad7d121e40dbb0e1a5e9ddc4b09d")]
        [Trait("Spec", "blk-0c881ae8341e4adda228a41ee4619c75")]
        public void TheCollectionIsNotSynchronizedAndExposesAStableSyncRoot()
        {
            var values = new ShardsValues();

            ((ICollection)values).IsSynchronized.Should().BeFalse();
            ((ICollection)values).SyncRoot.Should().NotBeNull().And.BeSameAs(((ICollection)values).SyncRoot);
            ((ICollection)values).SyncRoot.Should().NotBeSameAs(values);
        }

        [Fact]
        [Trait("Spec", "blk-933943de8ea143c2ba1c8d7ef2dca325")]
        public void CopyToWritesExactlyCountEntriesAtTheIndexAndValidatesItsArguments()
        {
            var values = new ShardsValues().Add(1, "a", 10).Add(2);
            var array = new ShardParameterValue[4];

            values.CopyTo(array, 1);

            array[0].Should().BeNull();
            (array[1].ShardId, array[1].ParameterName, array[1].ParameterValue).Should().Be(((short)1, "a", (object)10));
            (array[2].ShardId, array[2].ParameterName).Should().Be(((short)2, (string?)null));
            array[3].Should().BeNull("positions outside the written range are unchanged");
            new Action(() => values.CopyTo(null!, 0)).Should().Throw<ArgumentNullException>();
            new Action(() => values.CopyTo(new ShardParameterValue[2], -1)).Should().Throw<ArgumentOutOfRangeException>();
            new Action(() => values.CopyTo(new ShardParameterValue[2], 1)).Should().Throw<ArgumentException>();
        }

        [Fact]
        [Trait("Spec", "blk-0c881ae8341e4adda228a41ee4619c75")]
        public void RemoveDropsAnExistingShardAndIgnoresAnAbsentOne()
        {
            var values = new ShardsValues().Add(1).Add(2, "a", 1);

            values.Remove(2);
            values.Remove(9);

            values.Shards.Keys.Should().Equal(1);
        }

        #endregion

        #region Merge

        [Fact]
        [Trait("Spec", "blk-264681d2e45d40488e4359d677847028")]
        [Trait("Spec", "blk-ca71f7854d3e493aa615ec2c7e7058cc")]
        [Trait("Spec", "blk-3c901f4c5902414f9aa7e0d058d6e7f5")]
        public void MergeAddsAbsentShardsWithACopyOfTheirParametersOrAnEmptySet()
        {
            var target = new ShardsValues();
            var source = new ShardsValues().Add(9, "limit", 10).Add(6);

            target.Merge(source);

            target.Shards[9].Should().BeEquivalentTo(new Dictionary<string, object> { { "limit", 10 } });
            target.Shards[9].Should().NotBeSameAs(source.Shards[9], "the parameters are copied into a new set");
            target.Shards[6].Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-77cf5e7cfc004d2dbb633bd61ba0cb4e")]
        [Trait("Spec", "blk-fed86efc1a7f46bd89399fa68aff6d01")]
        public void MergeAddsIncomingParametersToAnExistingShard()
        {
            var target = new ShardsValues().Add(4, "region", "west");
            var nullSet = new ShardsValues(new Dictionary<short, IDictionary<string, object>> { { 5, null! } });

            target.Merge(new ShardsValues().Add(4, "limit", 10));
            nullSet.Merge(new ShardsValues().Add(5, "x", 1));

            target.Shards[4].Should().BeEquivalentTo(new Dictionary<string, object> { { "region", "west" }, { "limit", 10 } });
            nullSet.Shards[5].Should().BeEquivalentTo(new Dictionary<string, object> { { "x", 1 } }, "a null parameter set is replaced by an empty one first");
        }

        [Fact]
        [Trait("Spec", "blk-268a3c1f2d354537b8f3f096f2aab97f")]
        [Trait("Spec", "blk-c5d7812becb44134bbe4b5066eca86f0")]
        public void MergeWithADuplicateParameterNameThrowsBeforeChangingAnything()
        {
            var target = new ShardsValues().Add(4, "limit", 5);
            var source = new ShardsValues().Add(7, "region", "west").Add(4, "limit", 10);

            Action act = () => target.Merge(source);

            act.Should().Throw<ArgumentException>().WithMessage("*4*").WithMessage("*limit*");
            target.Shards.Keys.Should().Equal(new short[] { 4 }, "no shard was added");
            target.Shards[4].Should().BeEquivalentTo(new Dictionary<string, object> { { "limit", 5 } });
        }

        [Fact]
        [Trait("Spec", "blk-0c881ae8341e4adda228a41ee4619c75")]
        public void MergeOfAListAddsOnlyAbsentShardsAndLeavesExistingValuesUntouched()
        {
            var values = new ShardsValues().Add(1, "a", 1);

            values.Merge(new List<short> { 1, 2 });

            values.Shards.Keys.Should().BeEquivalentTo(new short[] { 1, 2 });
            values.Shards[1].Should().HaveCount(1);
            values.Shards[2].Should().BeEmpty();
        }

        #endregion

        #region ShardListForeign

        [Fact]
        [Trait("Spec", "blk-c25c4bc7ab8942519e86b815c7dff473")]
        [Trait("Spec", "blk-0bc088d3e242465c94c6ff7ddd5057a4")]
        [Trait("Spec", "blk-e04b6bf7290f4cc888d1230b6c775f01")]
        public void ShardListForeignReturnsOneEmptyEntryPerOtherDistinctShard()
        {
            var keys = new List<ShardKey<int>> { new ShardKey<int>(2, 1), new ShardKey<int>(5, 1), new ShardKey<int>(5, 2), new ShardKey<int>(9, 1) };

            var foreign = ShardsValues.ShardListForeign(2, keys);

            foreign.Shards.Keys.Should().BeEquivalentTo(new short[] { 5, 9 });
            foreign.Shards.Values.Should().OnlyContain(p => p.Count == 0);
            foreign.Cast<ShardParameterValue>().Should().OnlyContain(v => v.ParameterName == null && v.ParameterValue == null);
        }

        [Fact]
        [Trait("Spec", "blk-0bc088d3e242465c94c6ff7ddd5057a4")]
        [Trait("Spec", "blk-0c881ae8341e4adda228a41ee4619c75")]
        public void ShardListForeignCoversEveryArityAndBothCollectionFormsForKeysAndKeyedModels()
        {
            var expected = new short[] { 2, 3 };
            var k1 = new List<ShardKey<int>> { new ShardKey<int>(1, 1), new ShardKey<int>(2, 1), new ShardKey<int>(3, 1), new ShardKey<int>(3, 2) };
            var k2 = k1.Select(k => new ShardKey<int, int>(k.ShardId, k.RecordId, 0)).ToList();
            var k3 = k1.Select(k => new ShardKey<int, int, int>(k.ShardId, k.RecordId, 0, 0)).ToList();
            var k4 = k1.Select(k => new ShardKey<int, int, int, int>(k.ShardId, k.RecordId, 0, 0, 0)).ToList();
            var m1 = k1.Select(k => new Keyed1 { Key = k }).ToList();
            var m2 = k2.Select(k => new Keyed2 { Key = k }).ToList();
            var m3 = k3.Select(k => new Keyed3 { Key = k }).ToList();
            var m4 = k4.Select(k => new Keyed4 { Key = k }).ToList();

            ShardsValues.ShardListForeign(1, k1).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign(1, (IList<ShardKey<int>>)k1).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign(1, k2).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign(1, (IList<ShardKey<int, int>>)k2).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign(1, k3).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign(1, (IList<ShardKey<int, int, int>>)k3).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign(1, k4).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign(1, (IList<ShardKey<int, int, int, int>>)k4).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign<int, Keyed1>(1, m1).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign<int, Keyed1>(1, (IList<Keyed1>)m1).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign<int, int, Keyed2>(1, m2).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign<int, int, Keyed2>(1, (IList<Keyed2>)m2).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign<int, int, int, Keyed3>(1, m3).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign<int, int, int, Keyed3>(1, (IList<Keyed3>)m3).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign<int, int, int, int, Keyed4>(1, m4).Shards.Keys.Should().BeEquivalentTo(expected);
            ShardsValues.ShardListForeign<int, int, int, int, Keyed4>(1, (IList<Keyed4>)m4).Shards.Keys.Should().BeEquivalentTo(expected);
            typeof(ShardsValues).GetMethods().Count(m => m.Name == nameof(ShardsValues.ShardListForeign)).Should().Be(16);
        }

        #endregion

        #region ShardParameterValue

        [Fact]
        [Trait("Spec", "blk-42d4e0e40d06403db95c5d3ece3ee1aa")]
        [Trait("Spec", "blk-a2b9274a172a4475b81b9135e0936ca9")]
        [Trait("Spec", "blk-45a2f5bbcae6444caa8d6bfe00bed9b9")]
        [Trait("Spec", "blk-1c40b7b31fc0430a85e1d5dc6386dfe2")]
        public void ShardParameterValueExposesItsConstructionValuesThroughMutableProperties()
        {
            var value = new ShardParameterValue(7, "@status", "active");
            var blank = new ShardParameterValue();

            (value.ShardId, value.ParameterName, value.ParameterValue).Should().Be(((short)7, "@status", (object)"active"));
            (blank.ShardId, blank.ParameterName, blank.ParameterValue).Should().Be(((short)0, (string?)null, (object?)null));
            value.ShardId = 8;
            value.ParameterName = "@other";
            value.ParameterValue = 1;
            (value.ShardId, value.ParameterName, value.ParameterValue).Should().Be(((short)8, "@other", (object)1));
        }

        #endregion
    }
}
