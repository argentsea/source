using FluentAssertions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ArgentSea.Test
{
    public class ShardsValuesTests
    {
        [Fact]
        [Trait("Spec", "blk-fa8221bd205a47ee9cb8cf47f96cafd1")]
        public void ListConstructor_AddsEachDistinctShard()
        {
            var sv = new ShardsValues(new List<short> { 1, 2, 2, 3 });
            sv.Shards.Keys.Should().BeEquivalentTo(new short[] { 1, 2, 3 });
            sv.Shards.Values.Should().OnlyContain(prms => prms != null && prms.Count == 0);
        }

        [Fact]
        [Trait("Spec", "blk-3f8c71b2508d4f1e943b0ae6b752cbe7")]
        public void ListConstructor_NullList_Throws()
        {
            Action act = () => new ShardsValues((IList<short>)null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        [Trait("Spec", "blk-3c901f4c5902414f9aa7e0d058d6e7f5")]
        public void MergeShardsValues_AddsMissingShardsWithTheirParameters()
        {
            var target = new ShardsValues().Add(1);
            var source = new ShardsValues().Add(2, "@p", 5).Add(3);

            target.Merge(source);

            target.Shards.Keys.Should().BeEquivalentTo(new short[] { 1, 2, 3 });
            target.Shards[2]["@p"].Should().Be(5);
            target.Shards[3].Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-fed86efc1a7f46bd89399fa68aff6d01")]
        public void MergeShardsValues_MergesParametersIntoExistingShard()
        {
            var target = new ShardsValues().Add(1, "@a", 1);
            var source = new ShardsValues().Add(1, "@b", 2);

            target.Merge(source);

            target.Shards[1].Should().HaveCount(2);
            target.Shards[1]["@a"].Should().Be(1);
            target.Shards[1]["@b"].Should().Be(2);
        }

        [Fact]
        [Trait("Spec", "blk-c5d7812becb44134bbe4b5066eca86f0")]
        public void MergeShardsValues_DuplicateParameterOnSameShard_Throws()
        {
            var target = new ShardsValues().Add(1, "@a", 1);
            var source = new ShardsValues().Add(1, "@a", 2);

            Action act = () => target.Merge(source);
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        [Trait("Spec", "blk-c5d7812becb44134bbe4b5066eca86f0")]
        [Trait("Spec", "blk-268a3c1f2d354537b8f3f096f2aab97f")]
        public void MergeShardsValues_DuplicateParameter_LeavesTargetUnchanged()
        {
            var target = new ShardsValues().Add(1, "@a", 1);
            var source = new ShardsValues().Add(2, "@x", 9).Add(1, "@b", 2).Add(1, "@a", 3);

            Action act = () => target.Merge(source);

            act.Should().Throw<ArgumentException>();
            target.Shards.Keys.Should().BeEquivalentTo(new short[] { 1 });
            target.Shards[1].Should().HaveCount(1);
            target.Shards[1]["@a"].Should().Be(1);
        }

        [Fact]
        public void MergeList_NullList_Throws()
        {
            Action act = () => new ShardsValues().Merge((IList<short>)null!);
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void MergeShardsValues_MatchesMergeList_ForParameterlessShards()
        {
            var viaList = new ShardsValues().Add(1);
            viaList.Merge(new List<short> { 1, 2 });

            var viaValues = new ShardsValues().Add(1);
            viaValues.Merge(new ShardsValues(new List<short> { 1, 2 }));

            viaValues.Shards.Keys.Should().BeEquivalentTo(viaList.Shards.Keys);
        }

        [Fact]
        [Trait("Spec", "blk-933943de8ea143c2ba1c8d7ef2dca325")]
        public void CopyTo_WritesItemsAtIndex()
        {
            var sv = new ShardsValues().Add(1, "@a", 10).Add(2);
            var array = new ShardParameterValue[4];

            ((ICollection)sv).CopyTo(array, 1);

            array[0].Should().BeNull();
            array.Skip(1).Take(2).Select(v => v.ShardId).Should().BeEquivalentTo(new short[] { 1, 2 });
            array[3].Should().BeNull();
        }

        [Fact]
        [Trait("Spec", "blk-933943de8ea143c2ba1c8d7ef2dca325")]
        public void CopyTo_ArrayTooShort_Throws()
        {
            var sv = new ShardsValues().Add(1).Add(2);
            Action act = () => sv.CopyTo(new ShardParameterValue[2], 1);
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        [Trait("Spec", "blk-933943de8ea143c2ba1c8d7ef2dca325")]
        public void CopyTo_InvalidArguments_Throw()
        {
            var sv = new ShardsValues().Add(1);
            ((Action)(() => sv.CopyTo(null!, 0))).Should().Throw<ArgumentNullException>();
            ((Action)(() => sv.CopyTo(new ShardParameterValue[1], -1))).Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        [Trait("Spec", "blk-718eb1abbdb84ae2a6159149bddbec20")]
        [Trait("Spec", "blk-10b4ad7d121e40dbb0e1a5e9ddc4b09d")]
        public void IsSynchronized_IsFalse()
        {
            new ShardsValues().IsSynchronized.Should().BeFalse();
        }

        [Fact]
        [Trait("Spec", "blk-0bc088d3e242465c94c6ff7ddd5057a4")]
        public void ShardListForeign_ListOverloads_ExcludeCurrentShard()
        {
            var children = new List<ShardKey<int, int>> { new ShardKey<int, int>(1, 1, 1), new ShardKey<int, int>(2, 1, 1), new ShardKey<int, int>(3, 1, 1) };
            ShardsValues.ShardListForeign<int, int>(1, children).Shards.Keys.Should().BeEquivalentTo(new short[] { 2, 3 });

            var grandChildren = new List<ShardKey<int, int, int>> { new ShardKey<int, int, int>(1, 1, 1, 1), new ShardKey<int, int, int>(4, 1, 1, 1) };
            ShardsValues.ShardListForeign<int, int, int>(1, grandChildren).Shards.Keys.Should().BeEquivalentTo(new short[] { 4 });
        }
    }
}
