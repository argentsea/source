using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Xunit;
using ArgentSea;
using ArgentSea.Pg;
using FluentAssertions;
using Npgsql;
using NpgsqlTypes;

namespace ArgentSea.Pg.Test
{
    /// <summary>
    /// Spec section postgres-type-mapping, parameter part: the AddPg*Parameter helper family of PgParameterCollectionExtensions
    /// and the GetArray / GetHStore readers of PgParameterExtensions. The attribute part lives in <see cref="TypeMappingCoverageTests"/>.
    /// </summary>
    public class ParameterExtensionsTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void IntegerTests(int value)
        {
            var prms = new ParameterCollection();
            prms.AddPgIntegerInputParameter("1", value);
            prms.AddPgIntegerInputParameter("2", (int?)value);
            prms["1"].GetInteger().Should().Be(value, "that was the integer value");
            prms["2"].GetNullableInteger().Should().Be(value, "that was the integer value");
        }
        [Fact]
        public void NullIntegerTest()
        {
            var prm = new NpgsqlParameter("", NpgsqlDbType.Integer) { Value = System.DBNull.Value };
            prm.GetNullableInteger().Should().Be(null, "that was null database integer value");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(short.MaxValue)]
        [InlineData(short.MinValue)]
        public void ShortTests(short value)
        {
            var prms = new ParameterCollection();
            prms.AddPgSmallintInputParameter("1", value);
            prms.AddPgSmallintInputParameter("2", (short?)value);
            prms["1"].GetShort().Should().Be(value, "that was the short value");
            prms["2"].GetNullableShort().Should().Be(value, "that was the short value");
        }
        [Fact]
        public void NullShortTest()
        {
            var prm = new NpgsqlParameter("", NpgsqlDbType.Smallint) { Value = System.DBNull.Value };
            prm.GetNullableShort().Should().Be(null, "that was null database short value");
        }
        [Theory]
        [InlineData(0)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        public void LongTests(long value)
        {
            var prms = new ParameterCollection();
            prms.AddPgBigintInputParameter("1", value);
            prms.AddPgBigintInputParameter("2", (long?)value);
            prms["1"].GetLong().Should().Be(value, "that was the long value");
            prms["2"].GetNullableLong().Should().Be(value, "that was the long value");
        }
        [Fact]
        public void NullLongTest()
        {
            var prm = new NpgsqlParameter("", NpgsqlDbType.Bigint) { Value = System.DBNull.Value };
            prm.GetNullableLong().Should().Be(null, "that was null database long value");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(byte.MaxValue)]
        public void ByteTests(byte value)
        {
            var prms = new ParameterCollection();
            prms.AddPgInternalCharInputParameter("1", value);
            prms.AddPgInternalCharInputParameter("2", (byte?)value);
            prms["1"].GetByte().Should().Be(value, "that was the byte value");
            prms["2"].GetNullableByte().Should().Be(value, "that was the byte value");
        }
        [Fact]
        public void NullByteTest()
        {
            var prm = new NpgsqlParameter("", NpgsqlDbType.InternalChar) { Value = System.DBNull.Value };
            prm.GetNullableByte().Should().Be(null, "that was null database byte value");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BooleanTests(bool value)
        {
            var prms = new ParameterCollection();
            prms.AddPgBooleanInputParameter("1", value);
            prms.AddPgBooleanInputParameter("2", (bool?)value);
            prms["1"].GetBoolean().Should().Be(value, "that was the boolean value");
            prms["2"].GetNullableBoolean().Should().Be(value, "that was the boolean value");
        }
        [Fact]
        public void NullBooleanTest()
        {
            var prm = new NpgsqlParameter("", NpgsqlDbType.Boolean) { Value = System.DBNull.Value };
            prm.GetNullableBoolean().Should().Be(null, "that was null database boolean value");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(double.MaxValue)]
        [InlineData(double.MinValue)]
        public void DoubleTests(double value)
        {
            var prms = new ParameterCollection();
            prms.AddPgDoubleInputParameter("1", value);
            prms.AddPgDoubleInputParameter("2", (double?)value);
            prms["1"].GetDouble().Should().Be(value, "that was the double value");
            prms["2"].GetNullableDouble().Should().Be(value, "that was the double value");
        }
        [Fact]
        public void NullDoubleTest()
        {
            var prm = new NpgsqlParameter("", NpgsqlDbType.Double) { Value = System.DBNull.Value };
            prm.GetNullableDouble().Should().Be(null, "that was a null database double value");
            double.IsNaN(prm.GetDouble()).Should().Be(true, "that was a null database double value");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(float.MaxValue)]
        [InlineData(float.MinValue)]
        public void FloatTests(float value)
        {
            var prms = new ParameterCollection();
            prms.AddPgRealInputParameter("1", value);
            prms.AddPgRealInputParameter("2", (float?)value);
            prms["1"].GetFloat().Should().Be(value, "that was the single value");
            prms["2"].GetNullableFloat().Should().Be(value, "that was the single value");
        }
        [Fact]
        public void NullFloatTest()
        {
            var prm = new NpgsqlParameter("", NpgsqlDbType.Real) { Value = System.DBNull.Value };
            prm.GetNullableFloat().Should().Be(null, "that was null database single value");
            float.IsNaN(prm.GetFloat()).Should().Be(true, "that was a null database float value");
        }

        [Theory]
        [InlineData(null, -1)]
        [InlineData("", 4)]
        [InlineData("Test", 25)]
        [Trait("Spec", "blk-272dba609875491092b83303acf747f5")]
        [Trait("Spec", "blk-6b6d98d2520e465b9c2615e5e0961fbb")]
        [Trait("Spec", "blk-eb323bd17a0147c8ace901a523adf0b2")]
        public void VarCharTests(string value, int length)
        {
            var prms = new ParameterCollection();
            prms.AddPgVarcharInputParameter("1", value, length);
            if (value is null)
            {
                prms["1"].Value.Should().Be(System.DBNull.Value, "null should save as dbNull");
            }
            prms["1"].GetString().Should().Be(value, "that was the string value");
        }
        [Theory]
        [InlineData(null, -1)]
        [InlineData("", 4)]
        [InlineData("Test", 25)]
        public void CharTests(string value, int length)
        {
            var prms = new ParameterCollection();
            prms.AddPgCharInputParameter("1", value, length);
            if (value is null)
            {
                prms["1"].Value.Should().Be(System.DBNull.Value, "null should save as dbNull");
            }
            //prms["1"].GetString().Should().Be(value?.PadRight(length, ' '), "that was the string value");
            prms["1"].GetString().Should().Be(value, "that was the string value");
        }
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Test")]
        public void TextTests(string value)
        {
            var prms = new ParameterCollection();
            prms.AddPgTextInputParameter("1", value);
            if (value is null)
            {
                prms["1"].Value.Should().Be(System.DBNull.Value, "null should save as dbNull");
            }
            prms["1"].GetString().Should().Be(value, "that was the string value");
        }

        public static TheoryData<byte[]?, int> BinaryTestData => new TheoryData<byte[]?, int>
        {
            { null, 5 },
            { new byte[] { 1, 2, 3 }, 50 },
            { new byte[] { 0 }, 1 }
        };

        [Theory]
        [MemberData(nameof(BinaryTestData))]
        public void BinaryTests(byte[]? value, int length)
        {
            var prms = new ParameterCollection();
            prms.AddPgByteaInputParameter("1", value, length);
            prms["1"].Size.Should().Be(length, "that was the specified length");
            if (value is null)
            {
                prms["1"].Value.Should().Be(System.DBNull.Value, "null should save as dbNull");
                prms["1"].GetBytes().Should().BeNull("null was specified");
            }
            else
            {
                var data = prms["1"].GetBytes()!;
                for (var i = 0; i < data.Length; i++)
                {
                    data[i].Should().Be(value[i], "that was the array value");
                }
            }
        }

        public static TheoryData<DateTime> DateTimeTestData => new TheoryData<DateTime>
        {
            { DateTime.UtcNow },
            { DateTime.Now },
            { DateTime.MinValue },
            { DateTime.MaxValue }
        };

        [Theory]
        [MemberData(nameof(DateTimeTestData))]
        public void TimestampTests(DateTime value)
        {
            var prms = new ParameterCollection();
            prms.AddPgTimestampInputParameter("1", value);
            prms.AddPgTimestampInputParameter("2", (DateTime?)value);
            prms["1"].GetDateTime().Should().Be(value, "that was the datetime value");
            prms["2"].GetNullableDateTime().Should().Be(value, "that was the datetime value");
        }
        [Fact]
        public void NullDateTimeTest()
        {
            var prm = new NpgsqlParameter("", NpgsqlDbType.Timestamp) { Value = System.DBNull.Value };
            prm.GetNullableDateTime().Should().Be(null, "that was null database datetime value");
        }

        [Theory]
        [MemberData(nameof(DateTimeTestData))]
        public void DateTests(DateTime value)
        {
            var prms = new ParameterCollection();
            prms.AddPgDateInputParameter("1", value);
            prms.AddPgDateInputParameter("2", (DateTime?)value);
            prms["1"].GetDateTime().Date.Should().Be(value.Date, "that was the date value");
            prms["2"].GetNullableDateTime()!.Value.Date.Should().Be(value.Date, "that was the date value");
        }

        [Theory]
        [MemberData(nameof(DateTimeTestData))]
        public void TimeTests(DateTime value)
        {
            var prms = new ParameterCollection();
            prms.AddPgTimeInputParameter("1", value.TimeOfDay);
            prms.AddPgTimeInputParameter("2", (TimeSpan?)value.TimeOfDay);
            prms["1"].GetTimeSpan().Should().Be(value.TimeOfDay, "that was the time value");
            prms["2"].GetNullableTimeSpan().Should().Be(value.TimeOfDay, "that was the time value");
        }

        public static TheoryData<DateTimeOffset> DateTimeOffsetTestData => new TheoryData<DateTimeOffset>
        {
            { DateTimeOffset.Now },
            { DateTimeOffset.MinValue },
            { DateTimeOffset.MaxValue },
            { new DateTimeOffset(2018, 6, 10, 4, 22, 34, new TimeSpan(-4, 0, 0)) },
            { new DateTimeOffset(new DateTime(DateTime.Now.Ticks, DateTimeKind.Unspecified), new TimeSpan(-4, 0, 0)) }
        };

        [Theory]
        [MemberData(nameof(DateTimeOffsetTestData))]
        public void TimestampTzTests(DateTimeOffset value)
        {
            var prms = new ParameterCollection();
            prms.AddPgTimestampTzInputParameter("1", value);
            prms.AddPgTimestampTzInputParameter("2", (DateTimeOffset?)value);
            prms["1"].GetDateTimeOffset().Should().Be(value, "that was the datetime offset value");
            prms["2"].GetNullableDateTimeOffset().Should().Be(value, "that was the datetime offset value");
        }

        [Theory]
        [MemberData(nameof(DateTimeOffsetTestData))]
        public void IntervalTests(DateTimeOffset value)
        {
            var prms = new ParameterCollection();
            prms.AddPgIntervalInputParameter("1", value.TimeOfDay);
            prms.AddPgIntervalInputParameter("2", (TimeSpan?)value.TimeOfDay);
            prms["1"].GetTimeSpan().Should().Be(value.TimeOfDay, "that was the timespan value");
            prms["2"].GetNullableTimeSpan().Should().Be(value.TimeOfDay, "that was the timespan value");
        }

        [Fact]
        public void NullDateTimeOffsetTest()
        {
            var prm = new NpgsqlParameter("", NpgsqlDbType.TimestampTz) { Value = System.DBNull.Value };
            prm.GetNullableDateTimeOffset().Should().Be(null, "that was null database datetime offset value");
        }

        [Theory]
        [MemberData(nameof(DateTimeOffsetTestData))]
        public void TimeTzTests(DateTimeOffset value)
        {
            var prms = new ParameterCollection();
            prms.AddPgTimeTzInputParameter("1", value);
            prms.AddPgTimeTzInputParameter("2", (DateTimeOffset?)value);
            prms["1"].GetDateTimeOffset().Should().Be(value, "that was the time value");
            prms["2"].GetNullableDateTimeOffset().Should().Be(value, "that was the timezone value");

        }

        public static TheoryData<decimal, byte, byte> DecimalTestData => new TheoryData<decimal, byte, byte>
        {
            { 0M, 9, 2 },
            { decimal.MaxValue, 18, 0 },
            { decimal.MinValue, 22, 4 }
        };

        [Theory]
        [MemberData(nameof(DecimalTestData))]
        public void DecimalTests(decimal value, byte precision, byte scale)
        {
            var prms = new ParameterCollection();
            prms.AddPgNumericInputParameter("1", value, precision, scale);
            prms.AddPgNumericInputParameter("2", (decimal?)value, precision, scale);
            prms["1"].GetDecimal().Should().Be(value, "that was the decimal value");
            prms["2"].GetNullableDecimal().Should().Be(value, "that was the decimal value");
            prms["1"].Precision.Should().Be(precision, "that was the specified precision");
            prms["2"].Scale.Should().Be(scale, "that was the specified scale");

            if (value >= -92233720368547758.08M && value >= 92233720368547758.07M)
            {
                prms.AddPgMoneyInputParameter("3", value);
                prms.AddPgMoneyInputParameter("4", (decimal?)value);
                prms["3"].GetDecimal().Should().Be(value, "that was the decimal value");
                prms["4"].GetNullableDecimal().Should().Be(value, "that was the decimal value");
            }
        }
        [Fact]
        public void NullDecimalTest()
        {
            var prm = new NpgsqlParameter("", NpgsqlDbType.Numeric) { Value = System.DBNull.Value };
            prm.GetNullableDecimal().Should().Be(null, "that was null database decimal value");
        }
        public static TheoryData<Guid> GuidTestData => new TheoryData<Guid>
        {
            { Guid.NewGuid() }
        };
        [Theory]
        [MemberData(nameof(GuidTestData))]
        public void UuidTests(Guid value)
        {
            var prms = new ParameterCollection();
            prms.AddPgUuidInputParameter("1", value);
            prms.AddPgUuidInputParameter("2", (Guid?)value);
            prms["1"].GetGuid().Should().Be(value, "that was the guid value");
            prms["2"].GetNullableGuid().Should().Be(value, "that was the guid value");
        }
        [Fact]
        public void NullGuidTest()
        {
            var prm1 = new NpgsqlParameter("1", NpgsqlDbType.Uuid) { Value = System.DBNull.Value };
            prm1.GetNullableGuid().Should().BeNull("that was a null database guid value");
            prm1.GetGuid().Should().Be(Guid.Empty, "that was a null database guid value");
            var prm2 = new NpgsqlParameter("2", NpgsqlDbType.Uuid) { Value = Guid.Empty };
            prm2.GetGuid().Should().Be(Guid.Empty, "that was an empty database guid value");
            prm2.GetNullableGuid().Should().Be(Guid.Empty, "that was an empty database guid value");
        }

        // ---- PgParameterExtensions: output-value extraction -------------------------------------------------------------

        [Fact]
        [Trait("Spec", "blk-6bacc39bbc484629ab0b73e7bf01184c")]
        [Trait("Spec", "blk-eaa5edc642bb4294b06d96472eab40e1")]
        [Trait("Spec", "blk-712f0a1e06e447eb8538e156f3b3f6af")]
        public void GetArrayReturnsNullForADatabaseNullAndTheTypedArrayOtherwise()
        {
            var absent = new NpgsqlParameter("vals", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = DBNull.Value, Direction = ParameterDirection.Output };
            absent.GetArray<int>().Should().BeNull();

            var values = new[] { 1, 2, 3 };
            var present = new NpgsqlParameter("vals", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = values, Direction = ParameterDirection.Output };
            present.GetArray<int>().Should().BeSameAs(values, "the populated Value is returned as T[]");
        }

        [Fact]
        [Trait("Spec", "blk-092f20eb7d5a42d9b22f541afe81baf0")]
        [Trait("Spec", "blk-5350ccdc36094fbfacba5706d75b81ad")]
        [Trait("Spec", "blk-712f0a1e06e447eb8538e156f3b3f6af")]
        public void GetHStoreReturnsNullForADatabaseNullAndTheDictionaryOtherwise()
        {
            var absent = new NpgsqlParameter("pairs", NpgsqlDbType.Hstore) { Value = DBNull.Value, Direction = ParameterDirection.Output };
            absent.GetHStore().Should().BeNull();

            var pairs = new Dictionary<string, string> { ["region"] = "west" };
            var present = new NpgsqlParameter("pairs", NpgsqlDbType.Hstore) { Value = pairs, Direction = ParameterDirection.Output };
            var result = present.GetHStore();
            result.Should().BeSameAs(pairs);
            result!["region"].Should().Be("west");
        }

        // ---- PgParameterCollectionExtensions: the AddPg*Parameter helper family -----------------------------------------

        [Fact]
        [Trait("Spec", "blk-3df6ac1f9e9f4b1b965716ee0c781c4c")]
        [Trait("Spec", "blk-a6ce97df3325445fbb0c4f6109833d45")]
        [Trait("Spec", "blk-463f424fd8eb4f739dadf2c69512a56a")]
        [Trait("Spec", "blk-6b6d98d2520e465b9c2615e5e0961fbb")]
        [Trait("Spec", "blk-eb323bd17a0147c8ace901a523adf0b2")]
        public void AddPgIntegerInputParameterAppendsOneIntegerInputCarryingTheValueOrDatabaseNull()
        {
            var withValue = new NpgsqlCommand().Parameters;
            withValue.AddPgIntegerInputParameter("quantity", 7).Should().BeSameAs(withValue, "the same collection is returned");
            withValue.Count.Should().Be(1);
            withValue[0].ParameterName.Should().Be("quantity");
            withValue[0].NpgsqlDbType.Should().Be(NpgsqlDbType.Integer);
            withValue[0].Direction.Should().Be(ParameterDirection.Input);
            withValue[0].Value.Should().Be(7);

            var withNull = new NpgsqlCommand().Parameters;
            withNull.AddPgIntegerInputParameter("quantity", (int?)null).Should().BeSameAs(withNull);
            withNull.Count.Should().Be(1);
            withNull[0].ParameterName.Should().Be("quantity");
            withNull[0].NpgsqlDbType.Should().Be(NpgsqlDbType.Integer);
            withNull[0].Direction.Should().Be(ParameterDirection.Input);
            withNull[0].Value.Should().Be(DBNull.Value, "a missing nullable value is materialized as database null, never as a .NET null");
        }

        [Fact]
        [Trait("Spec", "blk-487b58aa42574a62ad7c0134ed7e2458")]
        [Trait("Spec", "blk-b0afde6cdceb45e7959caae50130646d")]
        public void NonNullableSentinelValuesAreSentAsDatabaseNullWhileTheNullableOverloadsSendThemAsIs()
        {
            var prms = new NpgsqlCommand().Parameters;
            prms.AddPgDoubleInputParameter("score", double.NaN).Should().BeSameAs(prms);
            prms.AddPgRealInputParameter("ratio", float.NaN);
            prms.AddPgUuidInputParameter("id", Guid.Empty);
            prms.AddPgDoubleInputParameter("score?", (double?)double.NaN);
            prms.AddPgRealInputParameter("ratio?", (float?)float.NaN);
            prms.AddPgUuidInputParameter("id?", (Guid?)Guid.Empty);

            prms["score"].NpgsqlDbType.Should().Be(NpgsqlDbType.Double);
            prms["score"].Direction.Should().Be(ParameterDirection.Input);
            prms["score"].Value.Should().Be(DBNull.Value, "NaN is the null marker, mirroring GetDouble returning NaN for a database null");
            prms["ratio"].NpgsqlDbType.Should().Be(NpgsqlDbType.Real);
            prms["ratio"].Value.Should().Be(DBNull.Value, "NaN is the null marker, mirroring GetFloat");
            prms["id"].NpgsqlDbType.Should().Be(NpgsqlDbType.Uuid);
            prms["id"].Value.Should().Be(DBNull.Value, "Guid.Empty is the null marker, mirroring GetGuid");

            prms["score?"].Value.Should().BeOfType<double>().Which.Should().Be(double.NaN, "the nullable overload maps only null to database null");
            prms["ratio?"].Value.Should().BeOfType<float>().Which.Should().Be(float.NaN);
            prms["id?"].Value.Should().Be(Guid.Empty);
        }

        [Fact]
        [Trait("Spec", "blk-bae30c3c5924432fada5870303437bbc")]
        [Trait("Spec", "blk-8ece4c2a49634ba0ba9145d50f001bfc")]
        public void OutputParametersReserveTheDeclaredTypeAndRetainSizePrecisionScaleAndElementType()
        {
            var prms = new NpgsqlCommand().Parameters;
            prms.AddPgVarcharOutputParameter("result", 12).Should().BeSameAs(prms, "the same collection is returned");
            prms.AddPgCharOutputParameter("code", 3);
            prms.AddPgByteaOutputParameter("blob", 64);
            prms.AddPgNumericOutputParameter("amount", 18, 4);
            prms.AddPgArrayOutputParameter("tags", NpgsqlDbType.Text);
            prms.AddPgIntegerOutputParameter("count");
            prms.Count.Should().Be(6);

            foreach (NpgsqlParameter prm in prms)
            {
                prm.Direction.Should().Be(ParameterDirection.Output, "{0} reserves a returned value", prm.ParameterName);
                prm.Value.Should().BeNull("{0} receives no input value", prm.ParameterName);
            }
            prms["result"].NpgsqlDbType.Should().Be(NpgsqlDbType.Varchar);
            prms["result"].Size.Should().Be(12);
            prms["code"].NpgsqlDbType.Should().Be(NpgsqlDbType.Char);
            prms["code"].Size.Should().Be(3);
            prms["blob"].NpgsqlDbType.Should().Be(NpgsqlDbType.Bytea);
            prms["blob"].Size.Should().Be(64);
            prms["amount"].NpgsqlDbType.Should().Be(NpgsqlDbType.Numeric);
            prms["amount"].Precision.Should().Be(18);
            prms["amount"].Scale.Should().Be(4);
            prms["tags"].NpgsqlDbType.Should().Be(NpgsqlDbType.Array | NpgsqlDbType.Text);
            prms["count"].NpgsqlDbType.Should().Be(NpgsqlDbType.Integer);
        }

        /// <summary>One helper overload: how to call it, and the type, direction and value of the parameter it must append.</summary>
        public sealed record HelperCase(Func<DbParameterCollection, DbParameterCollection> Add, NpgsqlDbType Type, ParameterDirection Direction, object? Value);

        private static readonly DateTime Stamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        private static readonly DateTimeOffset Offset = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
        private static readonly TimeSpan Span = new TimeSpan(1, 2, 3);
        private static readonly Guid Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        private static readonly byte[] Bytes = { 1, 2, 3 };
        private static readonly int[] Ints = { 1, 2 };
        private static readonly Dictionary<string, string> Pairs = new() { ["region"] = "west" };
        private static readonly object Null = DBNull.Value;

        private static HelperCase In(Func<DbParameterCollection, DbParameterCollection> add, NpgsqlDbType type, object? value)
            => new(add, type, ParameterDirection.Input, value);

        private static HelperCase Out(Func<DbParameterCollection, DbParameterCollection> add, NpgsqlDbType type)
            => new(add, type, ParameterDirection.Output, null);

        /// <summary>
        /// Every overload of the family, keyed by its signature (the parameter types after the collection and the name), so the
        /// census test can prove the table exercises each of the 61 overloads exactly once. Nullable overloads are called with
        /// null, so they prove the database-null materialization; value overloads carry a representative value.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, HelperCase> HelperFamily = new Dictionary<string, HelperCase>
        {
            ["AddPgVarcharInputParameter(String, Int32)"] = In(p => p.AddPgVarcharInputParameter("quantity", "abc", 10), NpgsqlDbType.Varchar, "abc"),
            ["AddPgVarcharOutputParameter(Int32)"] = Out(p => p.AddPgVarcharOutputParameter("quantity", 10), NpgsqlDbType.Varchar),
            ["AddPgCharInputParameter(String, Int32)"] = In(p => p.AddPgCharInputParameter("quantity", "abc", 3), NpgsqlDbType.Char, "abc"),
            ["AddPgCharOutputParameter(Int32)"] = Out(p => p.AddPgCharOutputParameter("quantity", 3), NpgsqlDbType.Char),
            ["AddPgTextInputParameter(String)"] = In(p => p.AddPgTextInputParameter("quantity", "abc"), NpgsqlDbType.Text, "abc"),
            ["AddPgTextOutputParameter()"] = Out(p => p.AddPgTextOutputParameter("quantity"), NpgsqlDbType.Text),
            ["AddPgBigintInputParameter(Int64)"] = In(p => p.AddPgBigintInputParameter("quantity", 7L), NpgsqlDbType.Bigint, 7L),
            ["AddPgBigintInputParameter(Int64?)"] = In(p => p.AddPgBigintInputParameter("quantity", (long?)null), NpgsqlDbType.Bigint, Null),
            ["AddPgBigintOutputParameter()"] = Out(p => p.AddPgBigintOutputParameter("quantity"), NpgsqlDbType.Bigint),
            ["AddPgIntegerInputParameter(Int32)"] = In(p => p.AddPgIntegerInputParameter("quantity", 7), NpgsqlDbType.Integer, 7),
            ["AddPgIntegerInputParameter(Int32?)"] = In(p => p.AddPgIntegerInputParameter("quantity", (int?)null), NpgsqlDbType.Integer, Null),
            ["AddPgIntegerOutputParameter()"] = Out(p => p.AddPgIntegerOutputParameter("quantity"), NpgsqlDbType.Integer),
            ["AddPgSmallintInputParameter(Int16)"] = In(p => p.AddPgSmallintInputParameter("quantity", (short)7), NpgsqlDbType.Smallint, (short)7),
            ["AddPgSmallintInputParameter(Int16?)"] = In(p => p.AddPgSmallintInputParameter("quantity", (short?)null), NpgsqlDbType.Smallint, Null),
            ["AddPgSmallintOutputParameter()"] = Out(p => p.AddPgSmallintOutputParameter("quantity"), NpgsqlDbType.Smallint),
            ["AddPgInternalCharInputParameter(Byte)"] = In(p => p.AddPgInternalCharInputParameter("quantity", (byte)7), NpgsqlDbType.InternalChar, (byte)7),
            ["AddPgInternalCharInputParameter(Byte?)"] = In(p => p.AddPgInternalCharInputParameter("quantity", (byte?)null), NpgsqlDbType.InternalChar, Null),
            ["AddPgInternalCharOutputParameter()"] = Out(p => p.AddPgInternalCharOutputParameter("quantity"), NpgsqlDbType.InternalChar),
            ["AddPgBooleanInputParameter(Boolean)"] = In(p => p.AddPgBooleanInputParameter("quantity", true), NpgsqlDbType.Boolean, true),
            ["AddPgBooleanInputParameter(Boolean?)"] = In(p => p.AddPgBooleanInputParameter("quantity", (bool?)null), NpgsqlDbType.Boolean, Null),
            ["AddPgBooleanOutputParameter()"] = Out(p => p.AddPgBooleanOutputParameter("quantity"), NpgsqlDbType.Boolean),
            ["AddPgNumericInputParameter(Decimal, Byte, Byte)"] = In(p => p.AddPgNumericInputParameter("quantity", 7.5m, 9, 2), NpgsqlDbType.Numeric, 7.5m),
            ["AddPgNumericInputParameter(Decimal?, Byte, Byte)"] = In(p => p.AddPgNumericInputParameter("quantity", (decimal?)null, 9, 2), NpgsqlDbType.Numeric, Null),
            ["AddPgNumericOutputParameter(Byte, Byte)"] = Out(p => p.AddPgNumericOutputParameter("quantity", 9, 2), NpgsqlDbType.Numeric),
            ["AddPgMoneyInputParameter(Decimal)"] = In(p => p.AddPgMoneyInputParameter("quantity", 7.5m), NpgsqlDbType.Money, 7.5m),
            ["AddPgMoneyInputParameter(Decimal?)"] = In(p => p.AddPgMoneyInputParameter("quantity", (decimal?)null), NpgsqlDbType.Money, Null),
            ["AddPgMoneyOutputParameter()"] = Out(p => p.AddPgMoneyOutputParameter("quantity"), NpgsqlDbType.Money),
            ["AddPgDoubleInputParameter(Double)"] = In(p => p.AddPgDoubleInputParameter("quantity", 7.5), NpgsqlDbType.Double, 7.5),
            ["AddPgDoubleInputParameter(Double?)"] = In(p => p.AddPgDoubleInputParameter("quantity", (double?)null), NpgsqlDbType.Double, Null),
            ["AddPgDoubleOutputParameter()"] = Out(p => p.AddPgDoubleOutputParameter("quantity"), NpgsqlDbType.Double),
            ["AddPgRealInputParameter(Single)"] = In(p => p.AddPgRealInputParameter("quantity", 7.5f), NpgsqlDbType.Real, 7.5f),
            ["AddPgRealInputParameter(Single?)"] = In(p => p.AddPgRealInputParameter("quantity", (float?)null), NpgsqlDbType.Real, Null),
            ["AddPgRealOutputParameter()"] = Out(p => p.AddPgRealOutputParameter("quantity"), NpgsqlDbType.Real),
            ["AddPgTimestampInputParameter(DateTime)"] = In(p => p.AddPgTimestampInputParameter("quantity", Stamp), NpgsqlDbType.Timestamp, Stamp),
            ["AddPgTimestampInputParameter(DateTime?)"] = In(p => p.AddPgTimestampInputParameter("quantity", (DateTime?)null), NpgsqlDbType.Timestamp, Null),
            ["AddPgTimestampOutputParameter()"] = Out(p => p.AddPgTimestampOutputParameter("quantity"), NpgsqlDbType.Timestamp),
            ["AddPgTimestampTzInputParameter(DateTimeOffset)"] = In(p => p.AddPgTimestampTzInputParameter("quantity", Offset), NpgsqlDbType.TimestampTz, Offset),
            ["AddPgTimestampTzInputParameter(DateTimeOffset?)"] = In(p => p.AddPgTimestampTzInputParameter("quantity", (DateTimeOffset?)null), NpgsqlDbType.TimestampTz, Null),
            ["AddPgTimestampTzOutputParameter()"] = Out(p => p.AddPgTimestampTzOutputParameter("quantity"), NpgsqlDbType.TimestampTz),
            ["AddPgDateInputParameter(DateTime)"] = In(p => p.AddPgDateInputParameter("quantity", Stamp), NpgsqlDbType.Date, Stamp),
            ["AddPgDateInputParameter(DateTime?)"] = In(p => p.AddPgDateInputParameter("quantity", (DateTime?)null), NpgsqlDbType.Date, Null),
            ["AddPgDateOutputParameter()"] = Out(p => p.AddPgDateOutputParameter("quantity"), NpgsqlDbType.Date),
            ["AddPgTimeInputParameter(TimeSpan)"] = In(p => p.AddPgTimeInputParameter("quantity", Span), NpgsqlDbType.Time, Span),
            ["AddPgTimeInputParameter(TimeSpan?)"] = In(p => p.AddPgTimeInputParameter("quantity", (TimeSpan?)null), NpgsqlDbType.Time, Null),
            ["AddPgTimeOutputParameter()"] = Out(p => p.AddPgTimeOutputParameter("quantity"), NpgsqlDbType.Time),
            ["AddPgIntervalInputParameter(TimeSpan)"] = In(p => p.AddPgIntervalInputParameter("quantity", Span), NpgsqlDbType.Interval, Span),
            ["AddPgIntervalInputParameter(TimeSpan?)"] = In(p => p.AddPgIntervalInputParameter("quantity", (TimeSpan?)null), NpgsqlDbType.Interval, Null),
            ["AddPgIntervalOutputParameter()"] = Out(p => p.AddPgIntervalOutputParameter("quantity"), NpgsqlDbType.Interval),
            ["AddPgTimeTzInputParameter(DateTimeOffset)"] = In(p => p.AddPgTimeTzInputParameter("quantity", Offset), NpgsqlDbType.TimeTz, Offset),
            ["AddPgTimeTzInputParameter(DateTimeOffset?)"] = In(p => p.AddPgTimeTzInputParameter("quantity", (DateTimeOffset?)null), NpgsqlDbType.TimeTz, Null),
            ["AddPgTimeTzOutputParameter()"] = Out(p => p.AddPgTimeTzOutputParameter("quantity"), NpgsqlDbType.TimeTz),
            ["AddPgArrayInputParameter(Array, NpgsqlDbType)"] = In(p => p.AddPgArrayInputParameter("quantity", Ints, NpgsqlDbType.Integer), NpgsqlDbType.Array | NpgsqlDbType.Integer, Ints),
            ["AddPgArrayOutputParameter(NpgsqlDbType)"] = Out(p => p.AddPgArrayOutputParameter("quantity", NpgsqlDbType.Integer), NpgsqlDbType.Array | NpgsqlDbType.Integer),
            ["AddPgByteaInputParameter(Byte[], Int32)"] = In(p => p.AddPgByteaInputParameter("quantity", Bytes, 10), NpgsqlDbType.Bytea, Bytes),
            ["AddPgByteaInputParameter(Byte[])"] = In(p => p.AddPgByteaInputParameter("quantity", Bytes), NpgsqlDbType.Bytea, Bytes),
            ["AddPgByteaOutputParameter(Int32)"] = Out(p => p.AddPgByteaOutputParameter("quantity", 10), NpgsqlDbType.Bytea),
            ["AddPgHstoreInputParameter(IDictionary`2)"] = In(p => p.AddPgHstoreInputParameter("quantity", Pairs), NpgsqlDbType.Hstore, Pairs),
            ["AddPgHstoreOutputParameter()"] = Out(p => p.AddPgHstoreOutputParameter("quantity"), NpgsqlDbType.Hstore),
            ["AddPgUuidInputParameter(Guid)"] = In(p => p.AddPgUuidInputParameter("quantity", Id), NpgsqlDbType.Uuid, Id),
            ["AddPgUuidInputParameter(Guid?)"] = In(p => p.AddPgUuidInputParameter("quantity", (Guid?)null), NpgsqlDbType.Uuid, Null),
            ["AddPgUuidOutputParameter()"] = Out(p => p.AddPgUuidOutputParameter("quantity"), NpgsqlDbType.Uuid),
        };

        private static readonly string[] SupportedTypes =
        {
            "Varchar", "Char", "Text", "Bigint", "Integer", "Smallint", "InternalChar", "Boolean", "Numeric", "Money", "Double", "Real",
            "Timestamp", "TimestampTz", "Date", "Time", "Interval", "TimeTz", "Array", "Bytea", "Hstore", "Uuid",
        };

        private static IReadOnlyList<MethodInfo> HelperMethods()
            => typeof(PgParameterCollectionExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name.StartsWith("AddPg", StringComparison.Ordinal) && m.Name.EndsWith("Parameter", StringComparison.Ordinal))
                .ToList();

        private static string PostgresTypeOf(MethodInfo method)
            => method.Name.Substring("AddPg".Length).Replace("InputParameter", string.Empty).Replace("OutputParameter", string.Empty);

        /// <summary>The method name followed by the parameter types after the collection and the parameter name, nullable types marked with '?'.</summary>
        private static string Signature(MethodInfo method)
            => method.Name + "(" + string.Join(", ", method.GetParameters().Skip(2)
                .Select(p => Nullable.GetUnderlyingType(p.ParameterType) is Type underlying ? underlying.Name + "?" : p.ParameterType.Name)) + ")";

        [Fact]
        [Trait("Spec", "blk-74ecb3a1d19b4c219c04ada1f2b352ff")]
        [Trait("Spec", "blk-1056cf5d563e450db9e5eb1e2d01d22c")]
        public void TheHelperFamilyHasTwentyTwoOutputAndThirtyNineInputOverloadsOverTwentyTwoTypes()
        {
            var methods = HelperMethods();
            methods.Should().OnlyContain(m => m.ReturnType == typeof(DbParameterCollection)
                && m.GetParameters()[0].ParameterType == typeof(DbParameterCollection)
                && m.GetParameters()[1].ParameterType == typeof(string), "every member extends a DbParameterCollection, takes the name second and returns the collection");

            var outputs = methods.Where(m => m.Name.EndsWith("OutputParameter", StringComparison.Ordinal)).ToList();
            var inputs = methods.Where(m => m.Name.EndsWith("InputParameter", StringComparison.Ordinal)).ToList();
            methods.Should().HaveCount(61);
            outputs.Should().HaveCount(22);
            inputs.Should().HaveCount(39);

            methods.Select(PostgresTypeOf).Distinct().Should().BeEquivalentTo(SupportedTypes);
            outputs.Select(PostgresTypeOf).Should().OnlyHaveUniqueItems("every type has exactly one Output overload")
                .And.BeEquivalentTo(SupportedTypes);

            var inputCensus = inputs.GroupBy(PostgresTypeOf).ToDictionary(g => g.Key, g => g.Count());
            var expectedCensus = SupportedTypes.ToDictionary(t => t, t => t is "Varchar" or "Char" or "Text" or "Array" or "Hstore" ? 1 : 2);
            inputCensus.Should().BeEquivalentTo(expectedCensus);

            var bytea = inputs.Where(m => PostgresTypeOf(m) == "Bytea").Select(m => m.GetParameters()[2].ParameterType).ToList();
            bytea.Should().Equal(new[] { typeof(byte[]), typeof(byte[]) }, "both Bytea input overloads take a byte[] rather than forming a nullable pair");
            foreach (var type in SupportedTypes.Where(t => expectedCensus[t] == 2 && t != "Bytea"))
            {
                var valueTypes = inputs.Where(m => PostgresTypeOf(m) == type).Select(m => m.GetParameters()[2].ParameterType).ToList();
                valueTypes.Should().ContainSingle(t => Nullable.GetUnderlyingType(t) != null, "{0} has a nullable-accepting input overload", type);
                valueTypes.Should().ContainSingle(t => Nullable.GetUnderlyingType(t) == null, "{0} has a non-nullable value input overload", type);
            }
            inputs.Where(m => PostgresTypeOf(m) == "TimeTz").Select(m => m.GetParameters()[2].ParameterType)
                .Should().BeEquivalentTo(new[] { typeof(DateTimeOffset), typeof(DateTimeOffset?) }, "Npgsql maps time with time zone only to DateTimeOffset");

            methods.Select(Signature).Should().BeEquivalentTo(HelperFamily.Keys, "the HelperFamily table exercises every overload exactly once");
            PgParameterCollectionExtensions.NormalizePgParameterName("Mixed:Case").Should().Be("Mixed:Case", "the name is used exactly as given");
        }

        public static TheoryData<string> HelperFamilySignatures
        {
            get
            {
                var data = new TheoryData<string>();
                foreach (var key in HelperFamily.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    data.Add(key);
                }
                return data;
            }
        }

        [Theory]
        [MemberData(nameof(HelperFamilySignatures))]
        [Trait("Spec", "blk-74ecb3a1d19b4c219c04ada1f2b352ff")]
        [Trait("Spec", "blk-1056cf5d563e450db9e5eb1e2d01d22c")]
        [Trait("Spec", "blk-463f424fd8eb4f739dadf2c69512a56a")]
        [Trait("Spec", "blk-8ece4c2a49634ba0ba9145d50f001bfc")]
        [Trait("Spec", "blk-6b6d98d2520e465b9c2615e5e0961fbb")]
        [Trait("Spec", "blk-eb323bd17a0147c8ace901a523adf0b2")]
        public void EveryHelperAppendsOneNpgsqlParameterOfItsTypeAndDirectionUnderTheExactNameAndReturnsTheCollection(string signature)
        {
            var helper = HelperFamily[signature];
            var prms = new NpgsqlCommand().Parameters;

            helper.Add(prms).Should().BeSameAs(prms, "the same collection is returned");

            prms.Count.Should().Be(1, "exactly one parameter is appended");
            var prm = prms[0];
            prm.ParameterName.Should().Be("quantity", "the name is used exactly as given, with no ':' or '@' prefix and unchanged casing");
            prm.NpgsqlDbType.Should().Be(helper.Type);
            prm.Direction.Should().Be(helper.Direction);
            if (helper.Value is null)
            {
                prm.Value.Should().BeNull("an output parameter receives no input value");
            }
            else
            {
                prm.Value.Should().Be(helper.Value, "an input carries the supplied value, or database null for an absent nullable value");
            }
        }
    }
}
