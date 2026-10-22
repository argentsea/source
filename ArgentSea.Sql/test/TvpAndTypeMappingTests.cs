using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Text.Json;
using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// Spec sections table-valued-parameters and sql-server-type-mapping: TvpMapper's row projection (column subsets, nested
    /// MapToModel flattening, shard-key columns, the Json limitation, caching), the collection attribute's ImmutableArray handling,
    /// the IsValidType matrix of every MapToSql* attribute, the scalar helper sentinel rules and the MapSqlShardKeyAttribute overloads.
    /// </summary>
    public class TvpAndTypeMappingTests
    {
        #region Models

        public enum Status { Pending = 0, Shipped = 2 }

        public class Address
        {
            [MapToSqlNVarChar("City", 50)]
            public string? City { get; set; }
        }

        public class Order
        {
            [MapToSqlNVarChar("Name", 50)]
            public string? Name { get; set; }

            [MapToModel]
            public Address? ShipAddress { get; set; }
        }

        public class KeyedRow
        {
            [MapSqlShardKey("ShardId", "RecordId")]
            [MapToSqlInt("RecordId")]
            public ShardKey<int> Key { get; set; }

            [MapToSqlNVarChar("Label", 20)]
            public string? Label { get; set; }
        }

        public class ChildKeyedRow
        {
            [MapSqlShardKey("ShardId", "RecordId", "ChildId")]
            [MapToSqlInt("RecordId")]
            [MapToSqlSmallInt("ChildId")]
            public ShardKey<int, short>? Key { get; set; }
        }

        public class GrandChildKeyedRow
        {
            [MapSqlShardKey("ShardId", "RecordId", "ChildId", "GrandChildId")]
            [MapToSqlInt("RecordId")]
            [MapToSqlSmallInt("ChildId")]
            [MapToSqlBigInt("GrandChildId")]
            public ShardKey<int, short, long> Key { get; set; }
        }

        public class GreatGrandChildKeyedRow
        {
            [MapSqlShardKey("ShardId", "RecordId", "ChildId", "GrandChildId", "GreatGrandChildId")]
            [MapToSqlInt("RecordId")]
            [MapToSqlSmallInt("ChildId")]
            [MapToSqlBigInt("GrandChildId")]
            [MapToSqlNVarChar("GreatGrandChildId", 10)]
            public ShardKey<int, short, long, string> Key { get; set; }
        }

        public class JsonRow
        {
            [MapToSqlInt("Id")]
            public int Id { get; set; }

            [MapToSqlJson("Doc")]
            public JsonDocument? Doc { get; set; }
        }

        public class SentinelRow
        {
            [MapToSqlUniqueIdentifier("Key")]
            public Guid Key { get; set; }

            [MapToSqlFloat("Score")]
            public double Score { get; set; }

            [MapToSqlReal("Temp")]
            public float Temp { get; set; }

            [MapToSqlVarBinary("Blob", 10)]
            public byte[]? Blob { get; set; }

            [MapToSqlNVarChar("Status", 10)]
            public Status Status { get; set; }

            [MapToSqlInt("Count")]
            public int? Count { get; set; }

            [MapToSqlDate("Day")]
            public DateOnly? Day { get; set; }
        }

        public class DuplicateColumnRow
        {
            [MapToSqlInt("Id")]
            public int Id { get; set; }

            [MapToSqlInt("@Id")]
            public int SameId { get; set; }

            [MapToSqlNVarChar("Name", 20)]
            public string? Name { get; set; }
        }

        public class CachedTvpRow
        {
            [MapToSqlInt("Id")]
            public int Id { get; set; }
        }

        public class TracedTvpRow
        {
            [MapToSqlInt("First")]
            public int First { get; set; }

            [MapToSqlInt("Second")]
            public int Second { get; set; }
        }

        public class ImmutableParent
        {
            [MapToSqlInt("Id")]
            public int Id { get; set; }

            [MapToSqlTableValuedParameter("@Rows", "RowTableType")]
            public ImmutableArray<CachedTvpRow> Rows { get; set; }
        }

        #endregion

        #region Table-valued parameters

        [Fact]
        [Trait("Spec", "blk-03877f34088e41c3bc4613ec34d8a7ce")]
        [Trait("Spec", "blk-d7f5a73aa96c4fb99195bf458d897ae7")]
        [Trait("Spec", "blk-228687eae22e43e29513484279c12650")]
        public void ANestedMapToModelPropertyFlattensIntoTheSameTvpRow()
        {
            var order = new Order { Name = "PO-48213", ShipAddress = new Address { City = "Springfield" } };

            var record = TvpMapper.ToTvpRecord(order, null);

            record.FieldCount.Should().Be(2, "ShipAddress contributes no column of its own");
            record.GetName(0).Should().Be("Name");
            record.GetString(0).Should().Be("PO-48213");
            record.GetName(1).Should().Be("City");
            record.GetString(1).Should().Be("Springfield");
        }

        [Fact]
        [Trait("Spec", "blk-74941f3ac3184613b6ef9b24cf5e7257")]
        [Trait("Spec", "blk-fbdf64e03c074d3897c44271174d5f91")]
        public void AShardKeyPropertyEmitsItsComponentColumnsAndNullsForAnEmptyKey()
        {
            var populated = TvpMapper.ToTvpRecord(new KeyedRow { Key = new ShardKey<int>(3, 41), Label = "x" }, null);
            var empty = TvpMapper.ToTvpRecord(new KeyedRow { Key = ShardKey<int>.Empty, Label = "x" }, null);

            Enumerable.Range(0, populated.FieldCount).Select(populated.GetName).Should().Equal("ShardId", "RecordId", "Label");
            populated.GetInt16(0).Should().Be(3);
            populated.GetInt32(1).Should().Be(41);
            empty.IsDBNull(0).Should().BeTrue("the shard id of an Empty key is written as null");
            empty.IsDBNull(1).Should().BeTrue("the record id of an Empty key is written as null");
            empty.GetString(2).Should().Be("x");
        }

        [Fact]
        [Trait("Spec", "blk-fbdf64e03c074d3897c44271174d5f91")]
        public void CompoundShardKeysOfEveryArityEmitEveryComponentAndANullKeyEmitsNulls()
        {
            var child = TvpMapper.ToTvpRecord(new ChildKeyedRow { Key = new ShardKey<int, short>(1, 2, 3) }, null);
            var nullChild = TvpMapper.ToTvpRecord(new ChildKeyedRow { Key = null }, null);
            var grandChild = TvpMapper.ToTvpRecord(new GrandChildKeyedRow { Key = new ShardKey<int, short, long>(1, 2, 3, 4L) }, null);

            Enumerable.Range(0, child.FieldCount).Select(child.GetName).Should().Equal("ShardId", "RecordId", "ChildId");
            (child.GetInt16(0), child.GetInt32(1), child.GetInt16(2)).Should().Be(((short)1, 2, (short)3));
            Enumerable.Range(0, nullChild.FieldCount).Select(nullChild.IsDBNull).Should().OnlyContain(isNull => isNull, "a null nullable key writes null for every component");
            Enumerable.Range(0, grandChild.FieldCount).Select(grandChild.GetName).Should().Equal("ShardId", "RecordId", "ChildId", "GrandChildId");
            grandChild.GetInt64(3).Should().Be(4L);
        }

        [Fact]
        [Trait("Spec", "blk-fbdf64e03c074d3897c44271174d5f91")]
        public void AFourPartShardKeyEmitsAllFiveComponentColumns()
        {
            var record = TvpMapper.ToTvpRecord(new GreatGrandChildKeyedRow { Key = new ShardKey<int, short, long, string>(1, 2, 3, 4L, "five") }, null);

            Enumerable.Range(0, record.FieldCount).Select(record.GetName).Should().Equal("ShardId", "RecordId", "ChildId", "GrandChildId", "GreatGrandChildId");
            (record.GetInt16(0), record.GetInt32(1), record.GetInt16(2), record.GetInt64(3), record.GetString(4)).Should().Be(((short)1, 2, (short)3, 4L, "five"));
        }

        [Fact]
        [Trait("Spec", "blk-a7ad7834a9e7457aa98a63b326ce70f3")]
        [Trait("Spec", "blk-7e52b0a5787c4dfc807dd5137f61d5c3")]
        [Trait("Spec", "blk-819adff89883499c866bcaa1a3e45aa4")]
        public void AJsonMappedPropertyCannotBeProjectedIntoATvpRow()
        {
            Action act = () => TvpMapper.ToTvpRecord(new JsonRow { Id = 1 }, null);

            act.Should().Throw<NotImplementedException>().WithMessage("Json data type is not implemented in ADO.NET for table-valued functions.");
            new ParameterCollection().CreateInputParameters(new JsonRow { Id = 1 }, null).Cast<SqlParameter>().Select(p => p.ParameterName).Should().Equal(new[] { "@Id", "@Doc" }, "the same property maps to a scalar input parameter");
        }

        [Fact]
        [Trait("Spec", "blk-414cab67e68e4e63ab8639c77a8988ac")]
        [Trait("Spec", "blk-f8bd3bf6109449d9863f4016a9ff86cb")]
        [Trait("Spec", "blk-704f4b571f09478084ce83cf7fe88225")]
        public void TheSecondProjectionOfAModelTypeIsACacheHitThatDoesNotRecompile()
        {
            var logger = new RecordingLogger();

            TvpMapper.ToTvpRecord(new CachedTvpRow { Id = 1 }, logger);
            logger.EntriesNamed(nameof(SqlLoggingExtensions.SqlTvpCacheMiss)).Should().ContainSingle();
            var compiled = logger.EntriesNamed(nameof(LoggingExtensions.CreatedExpressionTreeForModel)).Count;
            logger.Clear();
            var second = TvpMapper.ToTvpRecord(new CachedTvpRow { Id = 2 }, logger);

            compiled.Should().BeGreaterThan(0);
            logger.EntriesNamed(nameof(SqlLoggingExtensions.SqlTvpCacheHit)).Should().ContainSingle().Which.Id.Should().Be((int)SqlLoggingExtensions.SqlEventIdentifier.MapperTvpCacheStatus);
            logger.EntriesNamed(nameof(LoggingExtensions.CreatedExpressionTreeForModel)).Should().BeEmpty("the cached delegate is reused");
            second.GetInt32(0).Should().Be(2);
            Enum.GetNames(typeof(SqlLoggingExtensions.SqlEventIdentifier)).Should().Equal("MapperTvpCacheStatus", "MapperTvpTrace");
        }

        [Fact]
        [Trait("Spec", "blk-6ede686128d540138594c38740e2775d")]
        public void TheProjectionReportsEachMappedPropertyAsItIsProcessed()
        {
            var logger = new RecordingLogger();

            TvpMapper.ToTvpRecord(new TracedTvpRow { First = 1, Second = 2 }, logger);

            var traces = logger.EntriesNamed(nameof(SqlLoggingExtensions.TraceTvpMapperProperty));
            traces.Select(t => t.Message).Should().ContainInOrder("Tvp mapper is now processing property First.", "Tvp mapper is now processing property Second.");
            traces.Should().OnlyContain(t => t.Level == LogLevel.Debug && t.Id == (int)SqlLoggingExtensions.SqlEventIdentifier.MapperTvpTrace);
        }

        [Fact]
        [Trait("Spec", "blk-a66adaef2f1d429bb26b2e05e419f643")]
        [Trait("Spec", "blk-c097ca91f3854f53b45b348090cbf3f0")]
        [Trait("Spec", "blk-0539e7af93cc455ba620a90d60dd1467")]
        public void AColumnListSelectsAndReordersTheRecordByNormalizedName()
        {
            var row = new SentinelRow { Key = Guid.NewGuid(), Score = 1.5, Count = 7, Status = Status.Shipped };

            var record = TvpMapper.ToTvpRecord(row, new List<string> { "Count", "Key", "Status" }, null);

            record.FieldCount.Should().Be(3);
            Enumerable.Range(0, 3).Select(record.GetName).Should().Equal("Count", "Key", "Status");
            record.GetInt32(0).Should().Be(7);
            record.GetGuid(1).Should().Be(row.Key);
            record.GetString(2).Should().Be("Shipped", "an enum is written as its name");
        }

        [Fact]
        [Trait("Spec", "blk-fa97502c32fc46a086c7424e0618b2e6")]
        public void AColumnListNamingAnUnmappedColumnIsRejected()
        {
            Action act = () => TvpMapper.ToTvpRecord(new SentinelRow(), new List<string> { "Count", "Missing" }, null);

            act.Should().Throw<Exception>().WithMessage("*Missing*").WithMessage("*not found in the model attributes*");
        }

        [Fact]
        [Trait("Spec", "blk-1ab91b07ae9345fdacb59b2cdfc12aab")]
        public void TvpBuildersWriteDatabaseNullForSentinelsNullsAndEmptyBinaryValues()
        {
            var nulls = TvpMapper.ToTvpRecord(new SentinelRow { Key = Guid.Empty, Score = double.NaN, Temp = float.NaN, Blob = null, Count = null, Day = null }, null);
            var day = new DateOnly(2024, 2, 29);
            var values = TvpMapper.ToTvpRecord(new SentinelRow { Key = Guid.NewGuid(), Score = 2.5, Temp = 3.5f, Blob = new byte[] { 1 }, Count = 4, Day = day, Status = Status.Pending }, null);

            foreach (var column in new[] { "Key", "Score", "Temp", "Blob", "Count", "Day" })
            {
                nulls.IsDBNull(nulls.GetOrdinal(column)).Should().BeTrue($"{column} holds a null sentinel");
            }
            values.GetDouble(values.GetOrdinal("Score")).Should().Be(2.5);
            values.GetFloat(values.GetOrdinal("Temp")).Should().Be(3.5f);
            values.GetInt32(values.GetOrdinal("Count")).Should().Be(4);
            values.GetDateTime(values.GetOrdinal("Day")).Should().Be(day.ToDateTime(TimeOnly.MinValue), "a DateOnly becomes a midnight DateTime");
            values.GetString(values.GetOrdinal("Status")).Should().Be("Pending");
        }

        [Fact]
        [Trait("Spec", "blk-78741b89b7d74c2993cadeeb1b24f3ea")]
        [Trait("Spec", "blk-1ab91b07ae9345fdacb59b2cdfc12aab")]
        public void ADuplicateNormalizedColumnNameContributesOneColumn()
        {
            var record = TvpMapper.ToTvpRecord(new DuplicateColumnRow { Id = 1, SameId = 2, Name = "n" }, null);

            Enumerable.Range(0, record.FieldCount).Select(record.GetName).Should().Equal(new[] { "Id", "Name" }, "the second mapping of Id (declared as @Id) adds no second metadata entry");
            record.GetInt32(0).Should().Be(1, "only the first occurrence contributes an assignment");
        }

        [Fact]
        [Trait("Spec", "blk-d7c0d2f1f8e8497a98767b974e1d6bc0")]
        [Trait("Spec", "blk-b4e0ec89a80e4a8098bbb175a16a3743")]
        [Trait("Spec", "blk-228687eae22e43e29513484279c12650")]
        public void ADefaultImmutableArrayCollectionIsTreatedAsEmptyAndTheTvpParameterCarriesItsTypeName()
        {
            var populated = new ParameterCollection().CreateInputParameters(new ImmutableParent { Id = 1, Rows = ImmutableArray.Create(new CachedTvpRow { Id = 5 }) }, null);
            var defaulted = new ParameterCollection().CreateInputParameters(new ImmutableParent { Id = 1, Rows = default }, null);

            var rows = (SqlParameter)populated["@Rows"];
            rows.SqlDbType.Should().Be(SqlDbType.Structured);
            rows.TypeName.Should().Be("RowTableType");
            ((IEnumerable<SqlDataRecord>)rows.Value).Single().GetInt32(0).Should().Be(5);
            var empty = (SqlParameter)defaulted["@Rows"];
            empty.TypeName.Should().Be("RowTableType");
            empty.Value.Should().BeNull("a default ImmutableArray is normalized to Empty and an empty collection is sent as a null reference");
            new MapToSqlTableValuedParameterAttribute("@Rows", "RowTableType").TypeName.Should().Be("RowTableType");
        }

        #endregion

        #region IsValidType matrix

        public static IEnumerable<object[]> TypeMatrix()
        {
            static object[] Case(SqlParameterMapAttribute attribute, Type candidate, bool expected) => new object[] { attribute, candidate, expected };
            var stringFamily = new SqlParameterMapAttribute[] { new MapToSqlNVarCharAttribute("c", 10), new MapToSqlNCharAttribute("c", 10), new MapToSqlVarCharAttribute("c", 10, 1033), new MapToSqlCharAttribute("c", 10, 1033) };
            foreach (var attribute in stringFamily)
            {
                yield return Case(attribute, typeof(string), true);
                yield return Case(attribute, typeof(Status), true);
                yield return Case(attribute, typeof(Status?), true);
                yield return Case(attribute, typeof(int), false);
                yield return Case(attribute, typeof(char[]), false);
            }
            yield return Case(new MapToSqlBigIntAttribute("c"), typeof(long), true);
            yield return Case(new MapToSqlBigIntAttribute("c"), typeof(long?), true);
            yield return Case(new MapToSqlBigIntAttribute("c"), typeof(Status), true);
            yield return Case(new MapToSqlBigIntAttribute("c"), typeof(DayOfWeek?), false);
            yield return Case(new MapToSqlBigIntAttribute("c"), typeof(int), false);
            yield return Case(new MapToSqlIntAttribute("c"), typeof(int), true);
            yield return Case(new MapToSqlIntAttribute("c"), typeof(int?), true);
            yield return Case(new MapToSqlIntAttribute("c"), typeof(Status), true);
            yield return Case(new MapToSqlIntAttribute("c"), typeof(DayOfWeek?), true);
            yield return Case(new MapToSqlIntAttribute("c"), typeof(long), false);
            yield return Case(new MapToSqlIntAttribute("c"), typeof(string), false);
            yield return Case(new MapToSqlSmallIntAttribute("c"), typeof(short), true);
            yield return Case(new MapToSqlSmallIntAttribute("c"), typeof(short?), true);
            yield return Case(new MapToSqlSmallIntAttribute("c"), typeof(Status), true);
            yield return Case(new MapToSqlSmallIntAttribute("c"), typeof(Status?), true);
            yield return Case(new MapToSqlSmallIntAttribute("c"), typeof(int), false);
            yield return Case(new MapToSqlTinyIntAttribute("c"), typeof(byte), true);
            yield return Case(new MapToSqlTinyIntAttribute("c"), typeof(byte?), true);
            yield return Case(new MapToSqlTinyIntAttribute("c"), typeof(Status), true);
            yield return Case(new MapToSqlTinyIntAttribute("c"), typeof(Status?), true);
            yield return Case(new MapToSqlTinyIntAttribute("c"), typeof(short), false);
            yield return Case(new MapToSqlBitAttribute("c"), typeof(bool), true);
            yield return Case(new MapToSqlBitAttribute("c"), typeof(bool?), true);
            yield return Case(new MapToSqlBitAttribute("c"), typeof(Status), false);
            yield return Case(new MapToSqlBitAttribute("c"), typeof(int), false);
            foreach (var attribute in new SqlParameterMapAttribute[] { new MapToSqlDecimalAttribute("c", 9, 2), new MapToSqlMoneyAttribute("c"), new MapToSqlSmallMoneyAttribute("c") })
            {
                yield return Case(attribute, typeof(decimal), true);
                yield return Case(attribute, typeof(decimal?), true);
                yield return Case(attribute, typeof(double), false);
                yield return Case(attribute, typeof(Status), false);
            }
            yield return Case(new MapToSqlFloatAttribute("c"), typeof(double), true);
            yield return Case(new MapToSqlFloatAttribute("c"), typeof(double?), true);
            yield return Case(new MapToSqlFloatAttribute("c"), typeof(float), false);
            yield return Case(new MapToSqlRealAttribute("c"), typeof(float), true);
            yield return Case(new MapToSqlRealAttribute("c"), typeof(float?), true);
            yield return Case(new MapToSqlRealAttribute("c"), typeof(double), false);
            foreach (var attribute in new SqlParameterMapAttribute[] { new MapToSqlDateTimeAttribute("c"), new MapToSqlDateTime2Attribute("c") })
            {
                yield return Case(attribute, typeof(DateTime), true);
                yield return Case(attribute, typeof(DateTime?), true);
                yield return Case(attribute, typeof(DateOnly), false);
                yield return Case(attribute, typeof(DateTimeOffset), false);
            }
            yield return Case(new MapToSqlDateAttribute("c"), typeof(DateOnly), true);
            yield return Case(new MapToSqlDateAttribute("c"), typeof(DateOnly?), true);
            yield return Case(new MapToSqlDateAttribute("c"), typeof(DateTime), false);
            yield return Case(new MapToSqlTimeAttribute("c"), typeof(TimeSpan), true);
            yield return Case(new MapToSqlTimeAttribute("c"), typeof(TimeSpan?), true);
            yield return Case(new MapToSqlTimeAttribute("c"), typeof(TimeOnly), true);
            yield return Case(new MapToSqlTimeAttribute("c"), typeof(TimeOnly?), true);
            yield return Case(new MapToSqlTimeAttribute("c"), typeof(DateTime), false);
            yield return Case(new MapToSqlTimeAttribute("c"), typeof(Status), false);
            yield return Case(new MapToSqlDateTimeOffsetAttribute("c"), typeof(DateTimeOffset), true);
            yield return Case(new MapToSqlDateTimeOffsetAttribute("c"), typeof(DateTimeOffset?), true);
            yield return Case(new MapToSqlDateTimeOffsetAttribute("c"), typeof(DateTime), false);
            yield return Case(new MapToSqlUniqueIdentifierAttribute("c"), typeof(Guid), true);
            yield return Case(new MapToSqlUniqueIdentifierAttribute("c"), typeof(Guid?), true);
            yield return Case(new MapToSqlUniqueIdentifierAttribute("c"), typeof(string), false);
            foreach (var attribute in new SqlParameterMapAttribute[] { new MapToSqlVarBinaryAttribute("c", 10), new MapToSqlBinaryAttribute("c", 10) })
            {
                yield return Case(attribute, typeof(byte[]), true);
                yield return Case(attribute, typeof(ReadOnlyMemory<byte>), false);
                yield return Case(attribute, typeof(Memory<byte>), false);
                yield return Case(attribute, typeof(ReadOnlySpan<byte>), false);
                yield return Case(attribute, typeof(Span<byte>), false);
                yield return Case(attribute, typeof(Status), false);
                yield return Case(attribute, typeof(string), false);
            }
            yield return Case(new MapToSqlJsonAttribute("c"), typeof(JsonDocument), true);
            yield return Case(new MapToSqlJsonAttribute("c"), typeof(string), false);
            yield return Case(new MapToSqlJsonAttribute("c"), typeof(JsonElement), false);
        }

        [Theory]
        [Trait("Spec", "blk-4dfb8fa847e245408aa4654aaaa087d0")]
        [Trait("Spec", "blk-d3cb8531e7f849c5baaecff4c9e150c9")]
        [Trait("Spec", "blk-2cf54380bb1d42169fdeee43a54820e4")]
        [Trait("Spec", "blk-5cad24a381cb4586aa4ca009cb6da523")]
        [Trait("Spec", "blk-659577321b9445b8b52fa4b61291f06a")]
        [MemberData(nameof(TypeMatrix))]
        public void IsValidTypeAcceptsExactlyTheDeclaredClrShapes(SqlParameterMapAttribute attribute, Type candidate, bool expected)
        {
            attribute.IsValidType(candidate).Should().Be(expected, $"{attribute.GetType().Name} {(expected ? "accepts" : "rejects")} {candidate.Name}");
        }

        [Fact]
        [Trait("Spec", "blk-d3cb8531e7f849c5baaecff4c9e150c9")]
        [Trait("Spec", "blk-819adff89883499c866bcaa1a3e45aa4")]
        [Trait("Spec", "blk-1b2097798c3243a89b307bf23468644d")]
        public void EveryAttributeTargetsItsDeclaredSqlDbTypeAndRetainsItsDimensions()
        {
            var expectations = new (SqlParameterMapAttribute Attribute, SqlDbType Type)[]
            {
                (new MapToSqlNVarCharAttribute("c", 10), SqlDbType.NVarChar), (new MapToSqlNCharAttribute("c", 10), SqlDbType.NChar), (new MapToSqlVarCharAttribute("c", 10, 1033), SqlDbType.VarChar), (new MapToSqlCharAttribute("c", 10, 1033), SqlDbType.Char),
                (new MapToSqlBigIntAttribute("c"), SqlDbType.BigInt), (new MapToSqlIntAttribute("c"), SqlDbType.Int), (new MapToSqlSmallIntAttribute("c"), SqlDbType.SmallInt), (new MapToSqlTinyIntAttribute("c"), SqlDbType.TinyInt),
                (new MapToSqlBitAttribute("c"), SqlDbType.Bit), (new MapToSqlDecimalAttribute("c", 9, 2), SqlDbType.Decimal), (new MapToSqlMoneyAttribute("c"), SqlDbType.Money), (new MapToSqlSmallMoneyAttribute("c"), SqlDbType.SmallMoney),
                (new MapToSqlFloatAttribute("c"), SqlDbType.Float), (new MapToSqlRealAttribute("c"), SqlDbType.Real), (new MapToSqlDateTimeAttribute("c"), SqlDbType.DateTime), (new MapToSqlDateTime2Attribute("c"), SqlDbType.DateTime2),
                (new MapToSqlDateAttribute("c"), SqlDbType.Date), (new MapToSqlTimeAttribute("c"), SqlDbType.Time), (new MapToSqlDateTimeOffsetAttribute("c"), SqlDbType.DateTimeOffset), (new MapToSqlUniqueIdentifierAttribute("c"), SqlDbType.UniqueIdentifier),
                (new MapToSqlVarBinaryAttribute("c", 10), SqlDbType.VarBinary), (new MapToSqlBinaryAttribute("c", 10), SqlDbType.Binary), (new MapToSqlJsonAttribute("c"), SqlDbType.Json)
            };

            expectations.Should().HaveCount(23, "the matrix lists 23 variants");
            foreach (var (attribute, type) in expectations)
            {
                ((SqlDbType)attribute.SqlType).Should().Be(type, attribute.GetType().Name);
                attribute.ParameterName.Should().Be("@c");
                attribute.ColumnName.Should().Be("c");
                attribute.IsRecordIdentifier.Should().BeFalse();
            }
            new MapToSqlNVarCharAttribute("c", 10).Length.Should().Be(10);
            new MapToSqlVarCharAttribute("c", 10, 1033).LocaleId.Should().Be(1033);
            new MapToSqlDecimalAttribute("c", 9, 2).Precision.Should().Be(9);
            new MapToSqlDecimalAttribute("c", 9, 2).Scale.Should().Be(2);
            new MapToSqlIntAttribute("c", true).IsRecordIdentifier.Should().BeTrue();
            typeof(SqlParameterMapAttribute).IsAbstract.Should().BeTrue();
            typeof(SqlParameterMapAttribute).GetMethod(nameof(SqlParameterMapAttribute.AppendTvpExpressions))!.IsAbstract.Should().BeTrue("each variant supplies its own table-valued-parameter mapping");
        }

        #endregion

        #region Scalar helpers

        [Fact]
        [Trait("Spec", "blk-006bd9877d684e559068d422e2324594")]
        [Trait("Spec", "blk-ddff8ceba2ae42999cbc87880bd1a92c")]
        [Trait("Spec", "blk-7adcb8628dcf4eb7ba257e6e1d5a6131")]
        [Trait("Spec", "blk-5998974b207a44639094d50cd6bd39b7")]
        public void ScalarInputHelpersAppendOneNormalizedParameterAndRepresentNullAsDbNull()
        {
            var prms = new ParameterCollection();

            var returned = prms.AddSqlNVarCharInputParameter("note", null, 20);

            returned.Should().BeSameAs(prms);
            var prm = prms.Cast<SqlParameter>().Should().ContainSingle().Which;
            prm.ParameterName.Should().Be("@note");
            prm.SqlDbType.Should().Be(SqlDbType.NVarChar);
            prm.Size.Should().Be(20);
            prm.Direction.Should().Be(ParameterDirection.Input);
            prm.Value.Should().Be(DBNull.Value);
            var output = (SqlParameter)new ParameterCollection().AddSqlDecimalOutputParameter("amount", 9, 2)["@amount"];
            output.Direction.Should().Be(ParameterDirection.Output);
            (output.Precision, output.Scale).Should().Be(((byte)9, (byte)2));
            output.Value.Should().BeNull("output helpers omit an input value");
            ((SqlParameter)new ParameterCollection().AddSqlVarCharOutputParameter("code", 5, 1033)["@code"]).LocaleId.Should().Be(1033);
        }

        [Fact]
        [Trait("Spec", "blk-b34f1178b89f4afaabdeea96fdc04901")]
        [Trait("Spec", "blk-4d1ea1cf29494f81b32a597dcdda7f3c")]
        [Trait("Spec", "blk-95a96389e0ba42d28884046a24fe5b89")]
        [Trait("Spec", "blk-ddff8ceba2ae42999cbc87880bd1a92c")]
        public void OnlyTheNonNullableOverloadsTreatNaNAndGuidEmptyAsDatabaseNull()
        {
            var prms = new ParameterCollection();

            prms.AddSqlFloatInputParameter("score", double.NaN);
            prms.AddSqlFloatInputParameter("nullableScore", (double?)double.NaN);
            prms.AddSqlRealInputParameter("temp", float.NaN);
            prms.AddSqlRealInputParameter("nullableTemp", (float?)float.NaN);
            prms.AddSqlUniqueIdentifierInputParameter("key", Guid.Empty);
            prms.AddSqlUniqueIdentifierInputParameter("nullableKey", (Guid?)Guid.Empty);
            prms.AddSqlFloatInputParameter("absent", (double?)null);

            prms["@score"].Value.Should().Be(DBNull.Value);
            prms["@nullableScore"].Value.Should().Be(double.NaN, "a nullable caller has a real null to express database null");
            prms["@temp"].Value.Should().Be(DBNull.Value);
            prms["@nullableTemp"].Value.Should().Be(float.NaN);
            prms["@key"].Value.Should().Be(DBNull.Value);
            prms["@nullableKey"].Value.Should().Be(Guid.Empty);
            prms["@absent"].Value.Should().Be(DBNull.Value, "a HasValue-false nullable is database null");
        }

        [Fact]
        [Trait("Spec", "blk-93ec2444b0f145ab8e8e548ffc126ae1")]
        [Trait("Spec", "blk-c28591a43fea48bd932d4ae526b3c5ef")]
        [Trait("Spec", "blk-48f70a8f347b4bb8aa4490079567c4c9")]
        [Trait("Spec", "blk-0539e7af93cc455ba620a90d60dd1467")]
        public void ShardKeyTvpHelpersPlaceTheShardIdBeforeTheRecordIdInSuppliedOrder()
        {
            var prms = new ParameterCollection();

            prms.AddSqlTableValuedParameter("keys", new[] { new ShardKey<int>(7, 101), new ShardKey<int>(8, 102) }, "ShardId", SqlDbType.SmallInt, "RecordId", SqlDbType.Int);

            var prm = (SqlParameter)prms["@keys"];
            prm.SqlDbType.Should().Be(SqlDbType.Structured);
            prm.Direction.Should().Be(ParameterDirection.Input);
            var rows = ((IEnumerable<SqlDataRecord>)prm.Value).ToList();
            rows.Select(r => (r.GetInt16(0), r.GetInt32(1))).Should().Equal(((short)7, 101), ((short)8, 102));
            rows[0].GetName(0).Should().Be("ShardId");
            rows[0].GetName(1).Should().Be("RecordId");
        }

        [Fact]
        [Trait("Spec", "blk-48f70a8f347b4bb8aa4490079567c4c9")]
        public void CompoundShardKeyTvpHelpersPlaceEachChildIdAfterTheRecordId()
        {
            var prms = new ParameterCollection();

            prms.AddSqlTableValuedParameter("keys", new[] { new ShardKey<int, short, long>(1, 2, 3, 4L) }, "ShardId", SqlDbType.SmallInt, "RecordId", SqlDbType.Int, "ChildId", SqlDbType.SmallInt, "GrandChildId", SqlDbType.BigInt);

            var row = ((IEnumerable<SqlDataRecord>)((SqlParameter)prms["@keys"]).Value).Single();
            Enumerable.Range(0, row.FieldCount).Select(row.GetName).Should().Equal("ShardId", "RecordId", "ChildId", "GrandChildId");
            (row.GetInt16(0), row.GetInt32(1), row.GetInt16(2), row.GetInt64(3)).Should().Be(((short)1, 2, (short)3, 4L));
        }

        #endregion

        #region MapSqlShardKeyAttribute overloads

        [Fact]
        [Trait("Spec", "blk-27a909e7831d4b22b4cec527fedb2534")]
        [Trait("Spec", "blk-544c89a461b5426a99e59aed1f943c4e")]
        [Trait("Spec", "blk-ff10d09381b749af81072215d4e6275c")]
        [Trait("Spec", "blk-87b4d12511b44bee81eac88cd493c36d")]
        public void TheShardIdNameBecomesANonRequiredSmallIntMappingAndIsRecordIdentifierIsPreserved()
        {
            var explicitFlag = new MapSqlShardKeyAttribute("ShardId", "OrderId", false);
            var defaultFlag = new MapSqlShardKeyAttribute("ShardId", "OrderId");
            var noShard = new MapSqlShardKeyAttribute("OrderId");

            explicitFlag.ShardIdName.Should().Be("ShardId");
            explicitFlag.RecordIdName.Should().Be("OrderId");
            explicitFlag.IsRecordIdentifier.Should().BeFalse();
            var shard = explicitFlag.ShardParameter.Should().BeOfType<MapToSqlSmallIntAttribute>().Which;
            shard.IsRecordIdentifier.Should().BeFalse();
            ((SqlDbType)shard.SqlType).Should().Be(SqlDbType.SmallInt);
            defaultFlag.IsRecordIdentifier.Should().BeTrue("an overload without isRecordIdentifier configures it as true");
            noShard.ShardParameter.Should().BeNull();
            noShard.ShardIdName.Should().BeNull();
            new MapSqlShardKeyAttribute("OrderId", false).IsRecordIdentifier.Should().BeFalse();
        }

        [Fact]
        [Trait("Spec", "blk-f5d4f50c3bfb42db95eca1d3063c1cb7")]
        [Trait("Spec", "blk-7ca48d1f21e8404a97d157ece788421b")]
        [Trait("Spec", "blk-ff10d09381b749af81072215d4e6275c")]
        public void TheThreeArgumentOverloadForwardsTheChildNameToEverySubordinateSlotWhileDeeperOverloadsFillOnlyTheirOwn()
        {
            var three = new MapSqlShardKeyAttribute("ShardId", "RecordId", "ChildId");
            var four = new MapSqlShardKeyAttribute("ShardId", "RecordId", "ChildId", "GrandChildId");
            var fourWithFlag = new MapSqlShardKeyAttribute("ShardId", "RecordId", "ChildId", false);
            var five = new MapSqlShardKeyAttribute("ShardId", "RecordId", "ChildId", "GrandChildId", "GreatGrandChildId");
            var six = new MapSqlShardKeyAttribute("ShardId", "RecordId", "ChildId", "GrandChildId", "GreatGrandChildId", false);

            (three.ChildIdName, three.GrandChildIdName, three.GreatGrandChildIdName, three.IsRecordIdentifier).Should().Be(("ChildId", "ChildId", "ChildId", true));
            (four.ChildIdName, four.GrandChildIdName, four.GreatGrandChildIdName, four.IsRecordIdentifier).Should().Be(("ChildId", "GrandChildId", null, true));
            (fourWithFlag.ChildIdName, fourWithFlag.GrandChildIdName, fourWithFlag.GreatGrandChildIdName, fourWithFlag.IsRecordIdentifier).Should().Be(("ChildId", null, null, false));
            (five.ChildIdName, five.GrandChildIdName, five.GreatGrandChildIdName, five.IsRecordIdentifier).Should().Be(("ChildId", "GrandChildId", "GreatGrandChildId", true));
            (six.ChildIdName, six.GrandChildIdName, six.GreatGrandChildIdName, six.IsRecordIdentifier).Should().Be(("ChildId", "GrandChildId", "GreatGrandChildId", false));
            three.ShardIdName.Should().Be("ShardId");
        }

        #endregion
    }
}
