using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using ArgentSea.TestKit;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Spec section query-parameters: ParameterCollection's ordered semantics and name lookups, and the DbParameterExtensions
    /// null rules (nullable and reference getters return null, GetDouble/GetFloat/GetGuid return a sentinel, every other
    /// required getter throws UnexpectedNullException naming the parameter).
    /// </summary>
    public class QueryParametersTests
    {
        private static FakeDbParameter Prm(string name, object? value = null) => new FakeDbParameter(name, value ?? DBNull.Value, ParameterDirection.Output);

        #region ParameterCollection

        [Fact]
        [Trait("Spec", "blk-7b1831a2b4df42eea3b85f3cd05f8c6d")]
        [Trait("Spec", "blk-5142a57a45914028ac50b23800c9e262")]
        [Trait("Spec", "blk-9653b178c7b848449e47d323657276c1")]
        public void AddAndInsertKeepTheRequestedSequence()
        {
            var a = Prm("@a");
            var b = Prm("@b");
            var prms = new ParameterCollection();

            var countAfterAdd = prms.Add(a);
            prms.Insert(0, b);

            countAfterAdd.Should().Be(1, "Add returns the count after the append");
            prms.Count.Should().Be(2);
            prms[0].Should().BeSameAs(b);
            prms[1].Should().BeSameAs(a);
            prms.Cast<DbParameter>().Should().Equal(b, a);
        }

        [Fact]
        [Trait("Spec", "blk-9653b178c7b848449e47d323657276c1")]
        public void TheCollectionSupportsRangeAppendReplacementRemovalAndCopy()
        {
            var a = Prm("@a");
            var b = Prm("@b");
            var c = Prm("@c");
            var prms = new ParameterCollection();

            prms.AddRange(new DbParameter[] { a, b });
            prms.Add(c);
            prms[1] = Prm("@b2");
            prms["@c"] = Prm("@c2");
            var copied = new DbParameter[4];
            prms.CopyTo(copied, 1);

            prms.Cast<DbParameter>().Select(p => p.ParameterName).Should().Equal("@a", "@b2", "@c2");
            copied[0].Should().BeNull();
            copied.Skip(1).Select(p => p.ParameterName).Should().Equal("@a", "@b2", "@c2");
            prms.Contains(a).Should().BeTrue();
            prms.IndexOf(a).Should().Be(0);
            prms.IndexOf(b).Should().Be(-1, "the parameter at index 1 was replaced");
            prms.RemoveAt("@b2");
            prms.Remove(a);
            prms.RemoveAt(0);
            prms.Count.Should().Be(0);
            prms.AddRange(new DbParameter[] { a, b });
            prms.Clear();
            prms.Count.Should().Be(0);
            prms.SyncRoot.Should().NotBeNull();
        }

        [Fact]
        [Trait("Spec", "blk-fc0e539f9461471fb59918781f34f139")]
        [Trait("Spec", "blk-2bcd39286101474db8464c5ef39f88c9")]
        public void NameLookupsResolveToTheFirstExactMatchOrReportAbsence()
        {
            var a = Prm("@a");
            var b = Prm("@b");
            var b2 = Prm("@b");
            var prms = new ParameterCollection { a, b, b2 };

            prms.Contains("@b").Should().BeTrue();
            prms.IndexOf("@b").Should().Be(1);
            prms["@b"].Should().BeSameAs(b, "the first occurrence wins");
            prms.Contains("@missing").Should().BeFalse();
            prms.IndexOf("@missing").Should().Be(-1);
            prms["@missing"].Should().BeNull();
            prms.Contains("@B").Should().BeFalse("the match is exact");
        }

        [Fact]
        [Trait("Spec", "blk-2a25b55bf91d4cd2b3204e7c9cb53a0a")]
        [Trait("Spec", "blk-c652df4c1e3d4a9c9cfcb73a488bf6cc")]
        public void InsertingANonParameterThrowsAndLeavesTheCollectionUnchanged()
        {
            var prms = new ParameterCollection { Prm("@a") };

            Action insert = () => prms.Insert(0, "not-a-parameter");
            Action add = () => prms.Add("not-a-parameter");

            insert.Should().Throw<ArgumentException>();
            add.Should().Throw<ArgumentException>();
            prms.Count.Should().Be(1);
            prms[0].ParameterName.Should().Be("@a");
            new Action(() => new ParameterCollection().Insert(0, null!)).Should().Throw<ArgumentNullException>();
        }

        #endregion

        #region DbParameterExtensions

        [Fact]
        [Trait("Spec", "blk-0eb07a4f476d4b2b8ed9b1c814c31d6b")]
        [Trait("Spec", "blk-4ca639c0abd54427979c9552f5d35f39")]
        [Trait("Spec", "blk-1061beb1fd4448749b2813c5324db684")]
        [Trait("Spec", "blk-321c294b20ab4f4a92b8f0c497f516c7")]
        [Trait("Spec", "blk-9ce8b34127d84677bb26fd63d9cee0e0")]
        public void FloatingPointAndGuidGettersReturnSentinelsForDbNullWhileTheirNullableFormsReturnNull()
        {
            var prm = Prm("@v");

            prm.GetDouble().Should().Be(double.NaN);
            prm.GetFloat().Should().Be(float.NaN);
            prm.GetGuid().Should().Be(Guid.Empty);
            prm.GetNullableDouble().Should().BeNull();
            prm.GetNullableFloat().Should().BeNull();
            prm.GetNullableGuid().Should().BeNull();
            Prm("@g", Guid.Empty).GetGuid().Should().Be(Guid.Empty, "a stored empty Guid is returned unchanged");
            Prm("@g", Guid.Empty).GetNullableGuid().Should().Be(Guid.Empty);
            Prm("@d", 1.5).GetDouble().Should().Be(1.5);
        }

        [Fact]
        [Trait("Spec", "blk-4f01d2b1b0c642d88b04536d8a8d1888")]
        [Trait("Spec", "blk-12ff180743b9478283828196cc6dd09b")]
        [Trait("Spec", "blk-9ce8b34127d84677bb26fd63d9cee0e0")]
        public void NullableAndReferenceGettersReturnNullForDbNull()
        {
            var prm = Prm("@v");

            prm.GetNullableInteger().Should().BeNull();
            prm.GetString().Should().BeNull();
            prm.GetBytes().Should().BeNull();
            prm.GetJson().Should().BeNull();
            prm.GetNullableLong().Should().BeNull();
            prm.GetNullableShort().Should().BeNull();
            prm.GetNullableByte().Should().BeNull();
            prm.GetNullableBoolean().Should().BeNull();
            prm.GetNullableDecimal().Should().BeNull();
            prm.GetNullableDateTime().Should().BeNull();
            prm.GetNullableDateTimeOffset().Should().BeNull();
            prm.GetNullableTimeSpan().Should().BeNull();
            prm.GetNullableDateOnly().Should().BeNull();
            prm.GetNullableTimeOnly().Should().BeNull();
            Prm("@s", "text").GetString().Should().Be("text");
            Prm("@n", 5).GetNullableInteger().Should().Be(5);
            using var json = JsonDocument.Parse("{}");
            Prm("@j", json).GetJson().Should().BeSameAs(json);
        }

        [Fact]
        [Trait("Spec", "blk-b5431f752ce0477e96509c975f1833d9")]
        [Trait("Spec", "blk-471d4aa1976f4072886f2b589b9173fd")]
        [Trait("Spec", "blk-81e68b222bee4ba891b9d220e9edc818")]
        public void RequiredScalarGettersThrowUnexpectedNullExceptionNamingTheTypeAndParameter()
        {
            var count = Prm("@RowCount");

            var ex = new Action(() => count.GetInteger()).Should().Throw<UnexpectedNullException>().Which;

            ex.ExpectedType.Should().Be(typeof(int));
            ex.ColumnName.Should().Be("@RowCount");
            ex.Message.Should().Be("The database column @RowCount unexpectedly returned a “null” value and cannot be assigned to a System.Int32.");
            new Action(() => count.GetLong()).Should().Throw<UnexpectedNullException>().Which.ExpectedType.Should().Be(typeof(long));
            new Action(() => count.GetShort()).Should().Throw<UnexpectedNullException>().Which.ExpectedType.Should().Be(typeof(short));
            new Action(() => count.GetByte()).Should().Throw<UnexpectedNullException>().Which.ExpectedType.Should().Be(typeof(byte));
            new Action(() => count.GetBoolean()).Should().Throw<UnexpectedNullException>().Which.ExpectedType.Should().Be(typeof(bool));
            new Action(() => count.GetDecimal()).Should().Throw<UnexpectedNullException>().Which.ExpectedType.Should().Be(typeof(decimal));
            new Action(() => count.GetDateTime()).Should().Throw<UnexpectedNullException>().Which.ExpectedType.Should().Be(typeof(DateTime));
            new Action(() => count.GetDateTimeOffset()).Should().Throw<UnexpectedNullException>().Which.ExpectedType.Should().Be(typeof(DateTimeOffset));
            new Action(() => count.GetTimeSpan()).Should().Throw<UnexpectedNullException>().Which.ExpectedType.Should().Be(typeof(TimeSpan));
            new Action(() => count.GetDateOnly()).Should().Throw<UnexpectedNullException>().Which.ExpectedType.Should().Be(typeof(DateOnly));
            new Action(() => count.GetTimeOnly()).Should().Throw<UnexpectedNullException>().Which.ExpectedType.Should().Be(typeof(TimeOnly));
            new UnexpectedNullException().ExpectedType.Should().BeNull();
            new UnexpectedNullException("m").ColumnName.Should().BeNull();
            typeof(UnexpectedNullException).Should().NotBeSealed().And.BeDerivedFrom<ApplicationException>();
        }

        [Fact]
        [Trait("Spec", "blk-9ce8b34127d84677bb26fd63d9cee0e0")]
        public void DateAndTimeGettersAcceptTheProviderRepresentations()
        {
            Prm("@d", new DateTime(2024, 2, 29, 13, 0, 0)).GetDateOnly().Should().Be(new DateOnly(2024, 2, 29));
            Prm("@d", new DateOnly(2024, 2, 29)).GetNullableDateOnly().Should().Be(new DateOnly(2024, 2, 29));
            Prm("@t", new TimeSpan(6, 30, 15)).GetTimeOnly().Should().Be(new TimeOnly(6, 30, 15));
            Prm("@t", new TimeOnly(6, 30, 15)).GetNullableTimeOnly().Should().Be(new TimeOnly(6, 30, 15));
        }

        #endregion
    }
}
