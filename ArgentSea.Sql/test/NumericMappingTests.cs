// Copyright John Hicks. Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ArgentSea.Sql.Test
{
    public class NumericMappingTests
    {
        public static IEnumerable<object?[]> Cases()
        {
            foreach (var item in IntegerValues())
            {
                var type = item.Value.GetType();
                foreach (var template in new[] { item.IntegerModel, item.BinaryModel, item.VarBinaryModel })
                {
                    var binary = template != item.IntegerModel;
                    yield return new object?[] { template.MakeGenericType(type), item.Value, binary ? item.Bytes : item.Provider, item.SqlType(binary, template == item.VarBinaryModel) };
                    yield return new object?[] { template.MakeGenericType(typeof(Nullable<>).MakeGenericType(type)), item.Value, binary ? item.Bytes : item.Provider, item.SqlType(binary, template == item.VarBinaryModel) };
                }
            }
            var nullTypes = new HashSet<Type>();
            foreach (var item in IntegerValues())
            {
                if (!nullTypes.Add(item.Value.GetType())) continue;
                foreach (var template in new[] { item.IntegerModel, item.BinaryModel, item.VarBinaryModel })
                    yield return new object?[] { template.MakeGenericType(typeof(Nullable<>).MakeGenericType(item.Value.GetType())), null, DBNull.Value,
                        item.SqlType(template != item.IntegerModel, template == item.VarBinaryModel) };
            }
        }

        public sealed record MappingCase(object Value, object Provider, byte[] Bytes, Type IntegerModel, Type BinaryModel, Type VarBinaryModel, SqlDbType IntegerType)
        {
            public SqlDbType SqlType(bool binary, bool variable) => binary ? (variable ? SqlDbType.VarBinary : SqlDbType.Binary) : IntegerType;
        }

        public static IEnumerable<MappingCase> IntegerValues()
        {
            yield return new MappingCase((ulong)0UL, (long)0, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, typeof(BigIntModel<>), typeof(Binary8Model<>), typeof(VarBinary8Model<>), SqlDbType.BigInt);
            yield return new MappingCase((ulong)1UL, (long)1, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 }, typeof(BigIntModel<>), typeof(Binary8Model<>), typeof(VarBinary8Model<>), SqlDbType.BigInt);
            yield return new MappingCase((ulong)9223372036854775807UL, (long)9223372036854775807, new byte[] { 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, typeof(BigIntModel<>), typeof(Binary8Model<>), typeof(VarBinary8Model<>), SqlDbType.BigInt);
            yield return new MappingCase((ulong)9223372036854775808UL, (long)-9223372036854775808, new byte[] { 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, typeof(BigIntModel<>), typeof(Binary8Model<>), typeof(VarBinary8Model<>), SqlDbType.BigInt);
            yield return new MappingCase((ulong)9223372036854775809UL, (long)-9223372036854775807, new byte[] { 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 }, typeof(BigIntModel<>), typeof(Binary8Model<>), typeof(VarBinary8Model<>), SqlDbType.BigInt);
            yield return new MappingCase((ulong)18446744073709551615UL, (long)-1, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, typeof(BigIntModel<>), typeof(Binary8Model<>), typeof(VarBinary8Model<>), SqlDbType.BigInt);
            yield return new MappingCase((ulong)72623859790382856UL, (long)72623859790382856, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, typeof(BigIntModel<>), typeof(Binary8Model<>), typeof(VarBinary8Model<>), SqlDbType.BigInt);
            yield return new MappingCase((uint)0, (int)0, new byte[] { 0x00, 0x00, 0x00, 0x00 }, typeof(IntModel<>), typeof(Binary4Model<>), typeof(VarBinary4Model<>), SqlDbType.Int);
            yield return new MappingCase((uint)1, (int)1, new byte[] { 0x00, 0x00, 0x00, 0x01 }, typeof(IntModel<>), typeof(Binary4Model<>), typeof(VarBinary4Model<>), SqlDbType.Int);
            yield return new MappingCase((uint)2147483647, (int)2147483647, new byte[] { 0x7F, 0xFF, 0xFF, 0xFF }, typeof(IntModel<>), typeof(Binary4Model<>), typeof(VarBinary4Model<>), SqlDbType.Int);
            yield return new MappingCase((uint)2147483648, (int)-2147483648, new byte[] { 0x80, 0x00, 0x00, 0x00 }, typeof(IntModel<>), typeof(Binary4Model<>), typeof(VarBinary4Model<>), SqlDbType.Int);
            yield return new MappingCase((uint)2147483649, (int)-2147483647, new byte[] { 0x80, 0x00, 0x00, 0x01 }, typeof(IntModel<>), typeof(Binary4Model<>), typeof(VarBinary4Model<>), SqlDbType.Int);
            yield return new MappingCase((uint)4294967295, (int)-1, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, typeof(IntModel<>), typeof(Binary4Model<>), typeof(VarBinary4Model<>), SqlDbType.Int);
            yield return new MappingCase((uint)16909060, (int)16909060, new byte[] { 0x01, 0x02, 0x03, 0x04 }, typeof(IntModel<>), typeof(Binary4Model<>), typeof(VarBinary4Model<>), SqlDbType.Int);
            yield return new MappingCase((ushort)0, (short)0, new byte[] { 0x00, 0x00 }, typeof(SmallIntModel<>), typeof(Binary2Model<>), typeof(VarBinary2Model<>), SqlDbType.SmallInt);
            yield return new MappingCase((ushort)1, (short)1, new byte[] { 0x00, 0x01 }, typeof(SmallIntModel<>), typeof(Binary2Model<>), typeof(VarBinary2Model<>), SqlDbType.SmallInt);
            yield return new MappingCase((ushort)32767, (short)32767, new byte[] { 0x7F, 0xFF }, typeof(SmallIntModel<>), typeof(Binary2Model<>), typeof(VarBinary2Model<>), SqlDbType.SmallInt);
            yield return new MappingCase((ushort)32768, (short)-32768, new byte[] { 0x80, 0x00 }, typeof(SmallIntModel<>), typeof(Binary2Model<>), typeof(VarBinary2Model<>), SqlDbType.SmallInt);
            yield return new MappingCase((ushort)32769, (short)-32767, new byte[] { 0x80, 0x01 }, typeof(SmallIntModel<>), typeof(Binary2Model<>), typeof(VarBinary2Model<>), SqlDbType.SmallInt);
            yield return new MappingCase((ushort)65535, (short)-1, new byte[] { 0xFF, 0xFF }, typeof(SmallIntModel<>), typeof(Binary2Model<>), typeof(VarBinary2Model<>), SqlDbType.SmallInt);
            yield return new MappingCase((ushort)258, (short)258, new byte[] { 0x01, 0x02 }, typeof(SmallIntModel<>), typeof(Binary2Model<>), typeof(VarBinary2Model<>), SqlDbType.SmallInt);
            yield return new MappingCase((sbyte)-128, (byte)128, new byte[] { 0x80 }, typeof(TinyIntModel<>), typeof(Binary1Model<>), typeof(VarBinary1Model<>), SqlDbType.TinyInt);
            yield return new MappingCase((sbyte)-1, (byte)255, new byte[] { 0xFF }, typeof(TinyIntModel<>), typeof(Binary1Model<>), typeof(VarBinary1Model<>), SqlDbType.TinyInt);
            yield return new MappingCase((sbyte)0, (byte)0, new byte[] { 0x00 }, typeof(TinyIntModel<>), typeof(Binary1Model<>), typeof(VarBinary1Model<>), SqlDbType.TinyInt);
            yield return new MappingCase((sbyte)1, (byte)1, new byte[] { 0x01 }, typeof(TinyIntModel<>), typeof(Binary1Model<>), typeof(VarBinary1Model<>), SqlDbType.TinyInt);
            yield return new MappingCase((sbyte)127, (byte)127, new byte[] { 0x7F }, typeof(TinyIntModel<>), typeof(Binary1Model<>), typeof(VarBinary1Model<>), SqlDbType.TinyInt);

        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void AllPathsRoundTrip(Type modelType, object value, object provider, SqlDbType sqlType)
            => Invoke(nameof(CheckAllPaths), modelType, value, provider, sqlType);

        internal static void Invoke(string method, Type modelType, params object?[] args)
        {
            try { typeof(NumericMappingTests).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(modelType).Invoke(null, args); }
            catch (TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); }
        }

        private static void CheckAllPaths<TModel>(object? value, object? provider, SqlDbType sqlType) where TModel : new()
        {
            var prop = typeof(TModel).GetProperty("Value")!;
            var model = new TModel();
            prop.SetValue(model, value);
            var logger = new DebugLogger();
            using var cmd = new SqlCommand();
            checked { cmd.Parameters.CreateInputParameters(model, logger); }
            var prm = cmd.Parameters["@Value"];
            Assert.Equal(sqlType, prm.SqlDbType);
            AssertValue(provider, prm.Value);
            if (provider != DBNull.Value) Assert.Equal(provider!.GetType(), prm.Value!.GetType());

            using var output = new SqlCommand();
            output.Parameters.CreateOutputParameters<TModel>(logger);
            Assert.Equal(sqlType, output.Parameters["@Value"].SqlDbType);
            Assert.Equal(ParameterDirection.Output, output.Parameters["@Value"].Direction);
            if (sqlType == SqlDbType.Binary || sqlType == SqlDbType.VarBinary)
                Assert.Equal(prm.Size, output.Parameters["@Value"].Size);
            output.Parameters["@Value"].Value = provider;
            AssertValue(value, prop.GetValue(output.Parameters.ToModel<TModel>(logger)));

            var table = new DataTable();
            table.Columns.Add("Value", ProviderType(sqlType));
            table.Rows.Add(new[] { provider });
            using var reader = table.CreateDataReader();
            AssertValue(value, prop.GetValue(reader.ToModel<TModel>(logger)));

            var record = TvpMapper.ToTvpRecord(model, logger);
            Assert.Equal(sqlType, record.GetSqlMetaData(0).SqlDbType);
            AssertValue(provider, record.GetValue(0));
            if (sqlType == SqlDbType.Binary || sqlType == SqlDbType.VarBinary)
                Assert.Equal(prm.Size, record.GetSqlMetaData(0).MaxLength);

            using var absent = new SqlCommand();
            AssertValue(prop.GetValue(new TModel()), prop.GetValue(absent.Parameters.ToModel<TModel>(logger)));
            var missing = new DataTable();
            missing.Columns.Add("Other", typeof(int));
            missing.Rows.Add(1);
            using var missingReader = missing.CreateDataReader();
            AssertValue(prop.GetValue(new TModel()), prop.GetValue(missingReader.ToModel<TModel>(logger)));

            using var ignored = new SqlCommand();
            ignored.Parameters.CreateInputParameters(model, new HashSet<string> { "@Value" }, logger);
            Assert.Empty(ignored.Parameters);
        }

        internal static Type ProviderType(SqlDbType type) => type switch
        {
            SqlDbType.BigInt => typeof(long), SqlDbType.Int => typeof(int),
            SqlDbType.SmallInt => typeof(short), SqlDbType.TinyInt => typeof(byte),
            _ => typeof(byte[])
        };

        internal static void AssertValue(object? expected, object? actual)
        {
            if (expected is byte[] bytes) Assert.Equal(bytes, Assert.IsType<byte[]>(actual));
            else Assert.Equal(expected, actual);
        }

        [Fact]
        public void EnumAcceptanceRemainsUnchanged()
        {
            Assert.True(new MapToSqlBigIntAttribute("v").IsValidType(typeof(TestEnum)));
            Assert.False(new MapToSqlBigIntAttribute("v").IsValidType(typeof(TestEnum?)));
            foreach (var attr in new SqlParameterMapAttribute[] { new MapToSqlIntAttribute("v"), new MapToSqlSmallIntAttribute("v"), new MapToSqlTinyIntAttribute("v") })
            {
                Assert.True(attr.IsValidType(typeof(TestEnum)));
                Assert.True(attr.IsValidType(typeof(TestEnum?)));
            }
            Invoke(nameof(CheckAllPaths), typeof(BigIntModel<TestEnum>), TestEnum.One, 1L, SqlDbType.BigInt);
            Invoke(nameof(CheckAllPaths), typeof(IntModel<TestEnum?>), TestEnum.One, 1, SqlDbType.Int);
            Invoke(nameof(CheckAllPaths), typeof(SmallIntModel<TestEnum?>), TestEnum.One, (short)1, SqlDbType.SmallInt);
            Invoke(nameof(CheckAllPaths), typeof(TinyIntModel<TestEnum?>), TestEnum.One, (byte)1, SqlDbType.TinyInt);
        }
        public enum TestEnum : ushort { One = 1 }

        [Fact]
        public void WidthConfigurationDiagnosticsCoverEveryBuildPath()
        {
            Invoke(nameof(CheckInvalidWidth), typeof(Binary4Model<ulong>));
            Invoke(nameof(CheckInvalidWidth), typeof(VarBinary2Model<uint?>));
        }

        private static void CheckInvalidWidth<TModel>() where TModel : new()
        {
            var logger = new DebugLogger();
            using var cmd = new SqlCommand();
            Action[] builds = {
                () => cmd.Parameters.CreateInputParameters(new TModel(), logger),
                () => cmd.Parameters.CreateOutputParameters<TModel>(logger),
                () => cmd.Parameters.ToModel<TModel>(logger),
                () => TvpMapper.ToTvpRecord(new TModel(), logger),
                () => { var table = new DataTable(); table.Columns.Add("Value", typeof(byte[])); table.Rows.Add(new byte[] { 1 }); using var reader = table.CreateDataReader(); reader.ToModel<TModel>(logger); }
            };
            foreach (var build in builds)
            {
                var ex = Assert.Throws<InvalidBinaryMappingLengthException>(build);
                Assert.Equal("Value", ex.Name);
                var target = typeof(TModel).GetProperty("Value")!.PropertyType;
                Assert.Equal(Nullable.GetUnderlyingType(target) ?? target, Nullable.GetUnderlyingType(ex.TargetType) ?? ex.TargetType);
                Assert.True(ex.ConfiguredLength < ex.ExpectedWidth);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(7)]
        [InlineData(9)]
        public void MalformedBinaryReadsReportWidth(int length)
        {
            foreach (var type in new[] { typeof(Binary8Model<ulong>), typeof(Binary8Model<ulong?>), typeof(VarBinary8Model<ulong>), typeof(VarBinary8Model<ulong?>) })
                Invoke(nameof(CheckMalformed), type, length);
        }
        private static void CheckMalformed<TModel>(int length) where TModel : new()
        {
            var logger = new DebugLogger();
            using var output = new SqlCommand();
            output.Parameters.CreateOutputParameters<TModel>(logger);
            output.Parameters["@Value"].Value = new byte[length];
            var ex = Assert.Throws<InvalidBinaryValueLengthException>(() => output.Parameters.ToModel<TModel>(logger));
            Assert.Equal("@Value", ex.Name);
            Assert.Equal(8, ex.ExpectedWidth);
            Assert.Equal(length, ex.ActualLength);
            var table = new DataTable();
            table.Columns.Add("Value", typeof(byte[]));
            table.Rows.Add(new object[] { new byte[length] });
            using var reader = table.CreateDataReader();
            ex = Assert.Throws<InvalidBinaryValueLengthException>(() => reader.ToModel<TModel>(logger));
            Assert.Equal("Value", ex.Name);
            Assert.Equal(length, ex.ActualLength);
        }

        [Fact]
        public void LengthRulesAndUnsupportedTypesRemainDistinct()
        {
            foreach (var type in new[] { typeof(ulong), typeof(uint), typeof(ushort), typeof(sbyte), typeof(ulong?), typeof(uint?), typeof(ushort?), typeof(sbyte?) })
            {
                var width = NumericBinaryConverter.GetWidth(type);
                Assert.True(new MapToSqlBinaryAttribute("v", width).IsValidType(type));
                foreach (int size in new[] { -1, width, 8000 })
                    Assert.True(new MapToSqlVarBinaryAttribute("v", size).IsValidType(type));
                foreach (int size in new[] { -2, 0, 8001 })
                    Assert.Throws<InvalidBinaryMappingLengthException>(() => new MapToSqlVarBinaryAttribute("v", size).IsValidType(type));
                Assert.Throws<InvalidBinaryMappingLengthException>(() => new MapToSqlBinaryAttribute("v", width + 1).IsValidType(type));
            }
            Assert.False(new MapToSqlBinaryAttribute("v", 8).IsValidType(typeof(long)));
            Assert.False(new MapToSqlBigIntAttribute("v").IsValidType(typeof(uint)));
            using var cmd = new SqlCommand();
            Assert.Throws<InvalidMapTypeException>(() => cmd.Parameters.CreateOutputParameters<Binary8Model<long>>(new DebugLogger()));
        }

        [Fact]
        public void RequiredNullAndTvpReorderingRetainTheirSemantics()
        {
            var logger = new DebugLogger();
            using var output = new SqlCommand();
            output.Parameters.CreateOutputParameters<RequiredBinaryModel>(logger);
            output.Parameters["@Key"].Value = DBNull.Value;
            Assert.Null(output.Parameters.ToModel<RequiredBinaryModel>(logger));
            var table = new DataTable();
            table.Columns.Add("Key", typeof(byte[]));
            table.Rows.Add(DBNull.Value);
            using var reader = table.CreateDataReader();
            Assert.Null(reader.ToModel<RequiredBinaryModel>(logger));
            var model = new MixedNumericModel { First = ulong.MaxValue, Second = 0x01020304, Third = -1 };
            var record = TvpMapper.ToTvpRecord(model, new[] { "Third", "Second" }, logger);
            Assert.Equal(2, record.FieldCount);
            Assert.Equal("Third", record.GetName(0));
            Assert.Equal((byte)255, record.GetByte(0));
            Assert.Equal("Second", record.GetName(1));
            AssertValue(new byte[] { 1, 2, 3, 4 }, record.GetValue(1));
            record = TvpMapper.ToTvpRecord(new MixedNumericModel(), new[] { "Second" }, logger);
            Assert.True(record.IsDBNull(0));
        }

        [Fact]
        public void VarBinaryCapacityAndMaxKeepAnExactNumericPayload()
        {
            foreach (var type in new[] { typeof(VarBinaryMaxModel<ulong>), typeof(VarBinaryWideModel<ulong>) })
                Invoke(nameof(CheckAllPaths), type, ulong.MaxValue, new byte[] { 255,255,255,255,255,255,255,255 }, SqlDbType.VarBinary);
        }

        [Fact]
        public void NullIsNotANumericBinaryLengthError()
        {
            using var output = new SqlCommand();
            output.Parameters.CreateOutputParameters<Binary8Model<ulong>>(new DebugLogger());
            output.Parameters["@Value"].Value = DBNull.Value;
            Assert.Throws<UnexpectedNullException>(() => output.Parameters.ToModel<Binary8Model<ulong>>(new DebugLogger()));
            var table = new DataTable();
            table.Columns.Add("Value", typeof(byte[]));
            table.Rows.Add(DBNull.Value);
            using var reader = table.CreateDataReader();
            Assert.Throws<UnexpectedNullException>(() => reader.ToModel<Binary8Model<ulong>>(new DebugLogger()));
        }

        [Fact]
        public void ExistingBytesRoundTripIncludingEmptyAndNull()
        {
            Invoke(nameof(CheckAllPaths), typeof(VarBinary8Model<byte[]>), new byte[] { 1, 2 }, new byte[] { 1, 2 }, SqlDbType.VarBinary);
            Invoke(nameof(CheckAllPaths), typeof(VarBinary8Model<byte[]>), Array.Empty<byte>(), Array.Empty<byte>(), SqlDbType.VarBinary);
            Invoke(nameof(CheckAllPaths), typeof(Binary8Model<byte[]>), null, DBNull.Value, SqlDbType.Binary);
        }
    }
    public class BigIntModel<T> { [MapToSqlBigInt("Value")] public T? Value { get; set; } }
    public class IntModel<T> { [MapToSqlInt("Value")] public T? Value { get; set; } }
    public class SmallIntModel<T> { [MapToSqlSmallInt("Value")] public T? Value { get; set; } }
    public class TinyIntModel<T> { [MapToSqlTinyInt("Value")] public T? Value { get; set; } }
    public class Binary1Model<T> { [MapToSqlBinary("Value", 1)] public T? Value { get; set; } }
    public class VarBinary1Model<T> { [MapToSqlVarBinary("Value", 1)] public T? Value { get; set; } }
    public class Binary2Model<T> { [MapToSqlBinary("Value", 2)] public T? Value { get; set; } }
    public class VarBinary2Model<T> { [MapToSqlVarBinary("Value", 2)] public T? Value { get; set; } }
    public class Binary4Model<T> { [MapToSqlBinary("Value", 4)] public T? Value { get; set; } }
    public class VarBinary4Model<T> { [MapToSqlVarBinary("Value", 4)] public T? Value { get; set; } }
    public class Binary8Model<T> { [MapToSqlBinary("Value", 8)] public T? Value { get; set; } }
    public class VarBinary8Model<T> { [MapToSqlVarBinary("Value", 8)] public T? Value { get; set; } }
    public class RequiredBinaryModel
    {
        [MapToSqlBinary("Key", 8, true)] public ulong? Key { get; set; }
    }
    public class MixedNumericModel
    {
        [MapToSqlBigInt("First")] public ulong First { get; set; }
        [MapToSqlBinary("Second", 4)] public uint? Second { get; set; }
        [MapToSqlTinyInt("Third")] public sbyte Third { get; set; }
    }
    public class VarBinaryMaxModel<T> { [MapToSqlVarBinary("Value", -1)] public T? Value { get; set; } }
    public class VarBinaryWideModel<T> { [MapToSqlVarBinary("Value", 16)] public T? Value { get; set; } }
}
