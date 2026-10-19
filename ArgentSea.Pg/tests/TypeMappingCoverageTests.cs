using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using NSubstitute;
using Xunit;

namespace ArgentSea.Pg.Test
{
	class PgTimeTzModel
	{
		[MapToPgTimeTz("open_time")]
		public DateTimeOffset OpenTime { get; set; }

		[MapToPgTimeTz("break_time")]
		public DateTimeOffset? BreakTime { get; set; }

		[MapToPgTimeTz("stamp")]
		public DateTimeOffset? Stamp { get; set; }
	}

	class PgTimeTzTimeSpanModel
	{
		[MapToPgTimeTz("open_time")]
		public TimeSpan OpenTime { get; set; }
	}

	class PgTimestampOffsetModel
	{
		[MapToPgTimestamp("created")]
		public DateTimeOffset Created { get; set; }
	}

	/// <summary>
	/// Spec section postgres-type-mapping, attribute part: the MapToPg*Attribute family (its constructors, the supported-type
	/// matrix behind IsValidType, SqlTypeName and the column definitions a COPY load renders) and MapPgShardKeyAttribute.
	/// The parameter-helper part of the section lives in <see cref="ParameterExtensionsTests"/>.
	/// </summary>
	public class TypeMappingCoverageTests
	{
		[Fact]
		[Trait("Spec", "blk-0d79efbbc48647019bb1089e43551823")]
		public void TimeTzOutputParametersReadDateTimeOffset()
		{
			var open = new DateTimeOffset(1, 1, 2, 8, 0, 0, TimeSpan.FromHours(-5));
			var stamp = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
			var cmd = new NpgsqlCommand();
			cmd.Parameters.Add(new NpgsqlParameter("open_time", NpgsqlDbType.TimeTz) { Value = open, Direction = ParameterDirection.Output });
			cmd.Parameters.Add(new NpgsqlParameter("break_time", NpgsqlDbType.TimeTz) { Value = DBNull.Value, Direction = ParameterDirection.Output });
			cmd.Parameters.Add(new NpgsqlParameter("stamp", NpgsqlDbType.TimeTz) { Value = stamp, Direction = ParameterDirection.Output });

			var result = cmd.Parameters.ToModel<PgTimeTzModel>(Substitute.For<ILogger>());

			result!.OpenTime.Should().Be(open);
			result.BreakTime.Should().BeNull();
			result.Stamp.Should().Be(stamp);
		}

		// Npgsql maps PostgreSQL timetz only to DateTimeOffset (DateTimeOffsetTimeTzConverter); a TimeSpan cannot be written or read as timetz.
		[Theory]
		[InlineData(typeof(DateTimeOffset), true)]
		[InlineData(typeof(DateTimeOffset?), true)]
		[InlineData(typeof(TimeSpan), false)]
		[InlineData(typeof(TimeSpan?), false)]
		[InlineData(typeof(DateTime), false)]
		[Trait("Spec", "blk-0d79efbbc48647019bb1089e43551823")]
		[Trait("Spec", "blk-3c87c5334ff74b5b84a38f73df21dcf1")]
		public void TimeTzAcceptsOnlyDateTimeOffset(Type candidate, bool expected)
			=> new MapToPgTimeTzAttribute("tz").IsValidType(candidate).Should().Be(expected);

		[Fact]
		[Trait("Spec", "blk-0d79efbbc48647019bb1089e43551823")]
		[Trait("Spec", "blk-3c87c5334ff74b5b84a38f73df21dcf1")]
		public void TimeTzTimeSpanPropertyIsRejectedUpFront()
		{
			var prms = new NpgsqlCommand().Parameters;
			Action act = () => prms.CreateInputParameters(new PgTimeTzTimeSpanModel() { OpenTime = new TimeSpan(8, 0, 0) }, Substitute.For<ILogger>());
			act.Should().Throw<InvalidMapTypeException>();
		}

		[Theory]
		[InlineData(typeof(DateTime), true)]
		[InlineData(typeof(DateTime?), true)]
		[InlineData(typeof(DateTimeOffset), false)]
		[InlineData(typeof(DateTimeOffset?), false)]
		[Trait("Spec", "blk-0d79efbbc48647019bb1089e43551823")]
		[Trait("Spec", "blk-3c87c5334ff74b5b84a38f73df21dcf1")]
		public void TimestampAcceptsOnlyDateTime(Type candidate, bool expected)
			=> new MapToPgTimestampAttribute("ts").IsValidType(candidate).Should().Be(expected);

		[Fact]
		[Trait("Spec", "blk-0d79efbbc48647019bb1089e43551823")]
		[Trait("Spec", "blk-3c87c5334ff74b5b84a38f73df21dcf1")]
		public void TimestampDateTimeOffsetPropertyIsRejectedUpFront()
		{
			var prms = new NpgsqlCommand().Parameters;
			Action act = () => prms.CreateInputParameters(new PgTimestampOffsetModel() { Created = DateTimeOffset.UtcNow }, Substitute.For<ILogger>());
			act.Should().Throw<InvalidMapTypeException>();
		}

		[Fact]
		[Trait("Spec", "blk-41a9bebdf55c44f9a0932b450373d38b")]
		[Trait("Spec", "blk-53c05775a3374014bce0be518982086b")]
		public void InternalCharColumnIsQuotedChar()
			=> new MapToPgInternalCharAttribute("flag").ColumnDefinition.Should().Be("\"flag\" \"char\"");

		[Theory]
		[InlineData(NpgsqlDbType.Double, "\"vals\" double precision[]")]
		[InlineData(NpgsqlDbType.InternalChar, "\"vals\" \"char\"[]")]
		[InlineData(NpgsqlDbType.TimestampTz, "\"vals\" timestamp with time zone[]")]
		[InlineData(NpgsqlDbType.TimeTz, "\"vals\" time with time zone[]")]
		[InlineData(NpgsqlDbType.Integer, "\"vals\" integer[]")]
		[InlineData(NpgsqlDbType.Text, "\"vals\" text[]")]
		[InlineData(NpgsqlDbType.Uuid, "\"vals\" uuid[]")]
		[InlineData(NpgsqlDbType.Varchar, "\"vals\" varchar[]")]
		[Trait("Spec", "blk-41a9bebdf55c44f9a0932b450373d38b")]
		[Trait("Spec", "blk-53c05775a3374014bce0be518982086b")]
		[Trait("Spec", "blk-0d79efbbc48647019bb1089e43551823")]
		public void ArrayColumnUsesPostgresElementTypeName(NpgsqlDbType elementType, string expected)
			=> new MapToPgArrayAttribute("vals", elementType).ColumnDefinition.Should().Be(expected);

		[Fact]
		[Trait("Spec", "blk-41a9bebdf55c44f9a0932b450373d38b")]
		[Trait("Spec", "blk-203472aadcc944968c6a3a3540cbedeb")]
		[Trait("Spec", "blk-0d79efbbc48647019bb1089e43551823")]
		public void ScalarColumnDefinitionsUsePostgresTypeNames()
		{
			new MapToPgVarcharAttribute("c", 20).ColumnDefinition.Should().Be("\"c\" varchar(20)");
			new MapToPgVarcharAttribute("c", -1).ColumnDefinition.Should().Be("\"c\" varchar", "a non-positive length means an unbounded varchar");
			new MapToPgCharAttribute("c", 2).ColumnDefinition.Should().Be("\"c\" char(2)");
			new MapToPgNumericAttribute("c", 9, 4).ColumnDefinition.Should().Be("\"c\" numeric(9, 4)");
			new MapToPgDoubleAttribute("c").ColumnDefinition.Should().Be("\"c\" double precision");
			new MapToPgTimestampTzAttribute("c").ColumnDefinition.Should().Be("\"c\" timestamp with time zone");
			new MapToPgTimeTzAttribute("c").ColumnDefinition.Should().Be("\"c\" time with time zone");
			new MapToPgByteaAttribute("c", 10).ColumnDefinition.Should().Be("\"c\" bytea");
		}

		/// <summary>The exact column definitions the section's scenarios spell out.</summary>
		[Fact]
		[Trait("Spec", "blk-203472aadcc944968c6a3a3540cbedeb")]
		[Trait("Spec", "blk-4d2822c9aa40497786c9f80253374c2d")]
		[Trait("Spec", "blk-53c05775a3374014bce0be518982086b")]
		[Trait("Spec", "blk-ecc0184f17d84298bb1f7d7359c0c6b0")]
		[Trait("Spec", "blk-41a9bebdf55c44f9a0932b450373d38b")]
		public void ColumnDefinitionsRenderTheSpecifiedExamples()
		{
			new MapToPgByteaAttribute("payload", 8000).ColumnDefinition.Should().Be("\"payload\" bytea",
				"the Bytea length configures parameter and buffer construction only and is never rendered");
			new MapToPgArrayAttribute("Tags", NpgsqlDbType.Varchar).ColumnDefinition.Should().Be("\"Tags\" varchar[]");
			new MapToPgArrayAttribute("vals", NpgsqlDbType.Double).ColumnDefinition.Should().Be("\"vals\" double precision[]", "not the raw enum name double[]");
			new MapToPgInternalCharAttribute("flag").ColumnDefinition.Should().Be("\"flag\" \"char\"", "PostgreSQL needs the quotes to distinguish it from char(n)");
			new MapToPgArrayAttribute("flags", NpgsqlDbType.InternalChar).ColumnDefinition.Should().Be("\"flags\" \"char\"[]");
			new MapToPgNumericAttribute("Amount", 18, 4).ColumnDefinition.Should().Be("\"Amount\" numeric(18, 4)");
		}

		// ---- The MapToPg*Attribute family as a whole -------------------------------------------------------------------

		/// <summary>
		/// Every public MapToPg*Attribute variant, the NpgsqlDbType it selects and the constructor arguments it requires
		/// after the parameter name (Varchar, Char and Bytea a length; Numeric a precision and scale; Array an element type).
		/// </summary>
		public static readonly IReadOnlyList<(Type Attribute, NpgsqlDbType PgType, Type[] Extra)> Variants = new (Type, NpgsqlDbType, Type[])[]
		{
			(typeof(MapToPgVarcharAttribute), NpgsqlDbType.Varchar, new[] { typeof(int) }),
			(typeof(MapToPgCharAttribute), NpgsqlDbType.Char, new[] { typeof(int) }),
			(typeof(MapToPgTextAttribute), NpgsqlDbType.Text, Type.EmptyTypes),
			(typeof(MapToPgBigintAttribute), NpgsqlDbType.Bigint, Type.EmptyTypes),
			(typeof(MapToPgIntegerAttribute), NpgsqlDbType.Integer, Type.EmptyTypes),
			(typeof(MapToPgSmallintAttribute), NpgsqlDbType.Smallint, Type.EmptyTypes),
			(typeof(MapToPgInternalCharAttribute), NpgsqlDbType.InternalChar, Type.EmptyTypes),
			(typeof(MapToPgBooleanAttribute), NpgsqlDbType.Boolean, Type.EmptyTypes),
			(typeof(MapToPgNumericAttribute), NpgsqlDbType.Numeric, new[] { typeof(byte), typeof(byte) }),
			(typeof(MapToPgMoneyAttribute), NpgsqlDbType.Money, Type.EmptyTypes),
			(typeof(MapToPgDoubleAttribute), NpgsqlDbType.Double, Type.EmptyTypes),
			(typeof(MapToPgRealAttribute), NpgsqlDbType.Real, Type.EmptyTypes),
			(typeof(MapToPgTimestampAttribute), NpgsqlDbType.Timestamp, Type.EmptyTypes),
			(typeof(MapToPgTimestampTzAttribute), NpgsqlDbType.TimestampTz, Type.EmptyTypes),
			(typeof(MapToPgDateAttribute), NpgsqlDbType.Date, Type.EmptyTypes),
			(typeof(MapToPgTimeAttribute), NpgsqlDbType.Time, Type.EmptyTypes),
			(typeof(MapToPgIntervalAttribute), NpgsqlDbType.Interval, Type.EmptyTypes),
			(typeof(MapToPgTimeTzAttribute), NpgsqlDbType.TimeTz, Type.EmptyTypes),
			(typeof(MapToPgArrayAttribute), NpgsqlDbType.Array | NpgsqlDbType.Integer, new[] { typeof(NpgsqlDbType) }),
			(typeof(MapToPgByteaAttribute), NpgsqlDbType.Bytea, new[] { typeof(int) }),
			(typeof(MapToPgHstoreAttribute), NpgsqlDbType.Hstore, Type.EmptyTypes),
			(typeof(MapToPgUuidAttribute), NpgsqlDbType.Uuid, Type.EmptyTypes),
		};

		private static object SampleArgument(Type parameterType)
			=> parameterType == typeof(int) ? 10
			: parameterType == typeof(byte) ? (byte)4
			: parameterType == typeof(NpgsqlDbType) ? NpgsqlDbType.Integer
			: throw new ArgumentException($"No sample value for {parameterType.Name}");

		/// <summary>Constructs a variant through its parameter-name constructor, or through the isRequired overload when a flag is given.</summary>
		private static PgParameterMapAttribute Instantiate(Type attribute, bool? isRequired = null)
		{
			var extra = Variants.Single(v => v.Attribute == attribute).Extra;
			var args = new List<object> { "c" };
			args.AddRange(extra.Select(SampleArgument));
			if (isRequired.HasValue)
			{
				args.Add(isRequired.Value);
			}
			return (PgParameterMapAttribute)Activator.CreateInstance(attribute, args.ToArray())!;
		}

		[Fact]
		[Trait("Spec", "blk-3a76cb28a8fd4dfbb5569d15d2733a19")]
		[Trait("Spec", "blk-9113fc8db9214b28aa4ecb02c7da0281")]
		[Trait("Spec", "blk-41a9bebdf55c44f9a0932b450373d38b")]
		public void EveryVariantHasANameConstructorAndAnIsRequiredOverloadAndSelectsItsNpgsqlDbType()
		{
			var family = typeof(PgParameterMapAttribute).Assembly.GetTypes()
				.Where(t => t.IsPublic && !t.IsAbstract && typeof(PgParameterMapAttribute).IsAssignableFrom(t));
			family.Should().BeEquivalentTo(Variants.Select(v => v.Attribute), "the table must name every public MapToPg*Attribute variant");

			foreach (var (attribute, pgType, extra) in Variants)
			{
				var constructors = attribute.GetConstructors().Select(c => c.GetParameters().Select(p => p.ParameterType).ToArray()).ToList();
				var nameConstructor = new[] { typeof(string) }.Concat(extra).ToArray();
				constructors.Should().HaveCount(2, "{0} has exactly a parameter-name constructor and an isRequired overload", attribute.Name);
				constructors.Should().Contain(c => c.SequenceEqual(nameConstructor), "{0} takes the parameter name then its own configuration", attribute.Name);
				constructors.Should().Contain(c => c.SequenceEqual(nameConstructor.Append(typeof(bool))), "{0} has an overload that also records isRequired", attribute.Name);

				var plain = Instantiate(attribute);
				var required = Instantiate(attribute, isRequired: true);
				plain.ParameterName.Should().Be("c");
				// The isRequired flag is stored by the base attribute as IsRecordIdentifier (a null value means the record itself is absent).
				plain.IsRecordIdentifier.Should().BeFalse("{0} is optional unless the flag says otherwise", attribute.Name);
				required.IsRecordIdentifier.Should().BeTrue();
				((NpgsqlDbType)plain.SqlType).Should().Be(pgType, "{0} selects that PostgreSQL type", attribute.Name);
				((NpgsqlDbType)required.SqlType).Should().Be(pgType);
				if (attribute != typeof(MapToPgArrayAttribute))
				{
					plain.SqlTypeName.Should().Be(pgType.ToString(), "SqlTypeName is the textual name of the selected NpgsqlDbType");
				}
				plain.ColumnDefinition.Should().StartWith("\"c\" ", "{0} renders the double-quoted column name first", attribute.Name);
				plain.ColumnDefinition.Should().NotContainEquivalentOf("internalchar").And.NotContainEquivalentOf("timestamptz").And.NotContainEquivalentOf("timetz")
					.And.NotEndWith(" double").And.NotEndWith(" double[]", "a raw NpgsqlDbType enum name is never rendered");
			}
		}

		/// <summary>Candidate CLR property types, including the shapes the matrix says each variant must reject.</summary>
		private static readonly Type[] Candidates =
		{
			typeof(string), typeof(char), typeof(object),
			typeof(int), typeof(int?), typeof(long), typeof(long?), typeof(short), typeof(short?), typeof(byte), typeof(byte?),
			typeof(bool), typeof(bool?), typeof(decimal), typeof(decimal?), typeof(double), typeof(double?), typeof(float), typeof(float?),
			typeof(DateTime), typeof(DateTime?), typeof(DateTimeOffset), typeof(DateTimeOffset?), typeof(TimeSpan), typeof(TimeSpan?),
			typeof(DateOnly), typeof(DateOnly?), typeof(TimeOnly), typeof(Guid), typeof(Guid?),
			typeof(byte[]), typeof(int[]), typeof(string[]), typeof(List<int>),
			typeof(IDictionary<string, string>), typeof(Dictionary<string, string>), typeof(IDictionary<string, object>),
			typeof(ConsoleColor), typeof(ConsoleColor?),
		};

		/// <summary>The supported-type matrix: exactly the CLR shapes each variant's IsValidType accepts.</summary>
		public static TheoryData<Type, Type[]> SupportedTypeMatrix => new()
		{
			{ typeof(MapToPgVarcharAttribute), new[] { typeof(string), typeof(ConsoleColor), typeof(ConsoleColor?) } },
			{ typeof(MapToPgCharAttribute), new[] { typeof(string), typeof(ConsoleColor), typeof(ConsoleColor?) } },
			{ typeof(MapToPgTextAttribute), new[] { typeof(string) } },
			{ typeof(MapToPgBigintAttribute), new[] { typeof(long), typeof(long?), typeof(ConsoleColor) } },
			{ typeof(MapToPgIntegerAttribute), new[] { typeof(int), typeof(int?), typeof(ConsoleColor), typeof(ConsoleColor?) } },
			{ typeof(MapToPgSmallintAttribute), new[] { typeof(short), typeof(short?), typeof(ConsoleColor), typeof(ConsoleColor?) } },
			{ typeof(MapToPgInternalCharAttribute), new[] { typeof(byte), typeof(byte?), typeof(ConsoleColor), typeof(ConsoleColor?) } },
			{ typeof(MapToPgBooleanAttribute), new[] { typeof(bool), typeof(bool?) } },
			{ typeof(MapToPgNumericAttribute), new[] { typeof(decimal), typeof(decimal?) } },
			{ typeof(MapToPgMoneyAttribute), new[] { typeof(decimal), typeof(decimal?) } },
			{ typeof(MapToPgDoubleAttribute), new[] { typeof(double), typeof(double?) } },
			{ typeof(MapToPgRealAttribute), new[] { typeof(float), typeof(float?) } },
			{ typeof(MapToPgTimestampAttribute), new[] { typeof(DateTime), typeof(DateTime?) } },
			{ typeof(MapToPgTimestampTzAttribute), new[] { typeof(DateTimeOffset), typeof(DateTimeOffset?) } },
			{ typeof(MapToPgDateAttribute), new[] { typeof(DateTime), typeof(DateTime?) } },
			{ typeof(MapToPgTimeAttribute), new[] { typeof(TimeSpan), typeof(TimeSpan?) } },
			{ typeof(MapToPgIntervalAttribute), new[] { typeof(TimeSpan), typeof(TimeSpan?) } },
			{ typeof(MapToPgTimeTzAttribute), new[] { typeof(DateTimeOffset), typeof(DateTimeOffset?) } },
			{ typeof(MapToPgArrayAttribute), new[] { typeof(byte[]), typeof(int[]), typeof(string[]) } },
			{ typeof(MapToPgByteaAttribute), new[] { typeof(byte[]) } },
			{ typeof(MapToPgHstoreAttribute), new[] { typeof(IDictionary<string, string>) } },
			{ typeof(MapToPgUuidAttribute), new[] { typeof(Guid), typeof(Guid?) } },
		};

		[Theory]
		[MemberData(nameof(SupportedTypeMatrix))]
		[Trait("Spec", "blk-0d79efbbc48647019bb1089e43551823")]
		[Trait("Spec", "blk-3c87c5334ff74b5b84a38f73df21dcf1")]
		[Trait("Spec", "blk-3a76cb28a8fd4dfbb5569d15d2733a19")]
		public void IsValidTypeAcceptsExactlyTheMatrixShapesForTheVariant(Type attribute, Type[] accepted)
		{
			var variant = Instantiate(attribute);
			accepted.Should().BeSubsetOf(Candidates, "every accepted shape must be among the candidates the test probes");

			foreach (var candidate in Candidates)
			{
				variant.IsValidType(candidate).Should().Be(accepted.Contains(candidate),
					"{0} {1} accept {2}", attribute.Name, accepted.Contains(candidate) ? "must" : "must not", candidate.Name);
			}
		}

		[Fact]
		[Trait("Spec", "blk-0d79efbbc48647019bb1089e43551823")]
		public void TheMatrixCoversEveryVariant()
			=> SupportedTypeMatrix.Select(row => (Type)row[0]).Should().BeEquivalentTo(Variants.Select(v => v.Attribute));

		// ---- MapPgShardKeyAttribute --------------------------------------------------------------------------------------

		[Fact]
		[Trait("Spec", "blk-06bf019d6ab94df49c203fe7351223ad")]
		[Trait("Spec", "blk-bca09c289fcd4925854e84e90ced036f")]
		[Trait("Spec", "blk-556af7f42b8748c39a471194218a7b49")]
		public void MapPgShardKeyWithOnlyARecordNameConfiguresNoShardMapping()
		{
			var implicitFlag = new MapPgShardKeyAttribute("RecordId");
			implicitFlag.ShardParameter.Should().BeNull("no PostgreSQL shard-id mapping is configured");
			implicitFlag.ShardIdName.Should().BeNull();
			implicitFlag.RecordIdName.Should().Be("RecordId");
			implicitFlag.ChildIdName.Should().BeNull();
			implicitFlag.GrandChildIdName.Should().BeNull();
			implicitFlag.GreatGrandChildIdName.Should().BeNull();
			implicitFlag.IsRecordIdentifier.Should().BeTrue("the overload without the flag defaults it to true");

			var explicitFlag = new MapPgShardKeyAttribute("RecordId", false);
			explicitFlag.ShardParameter.Should().BeNull();
			explicitFlag.RecordIdName.Should().Be("RecordId");
			explicitFlag.IsRecordIdentifier.Should().BeFalse("the overload with the flag takes the supplied value");
			new MapPgShardKeyAttribute("RecordId", true).IsRecordIdentifier.Should().BeTrue();
		}

		[Fact]
		[Trait("Spec", "blk-ce93a50ad48f489283fd61c7e607e4e2")]
		[Trait("Spec", "blk-556af7f42b8748c39a471194218a7b49")]
		public void MapPgShardKeyWithAShardNameConfiguresASmallintShardMappingAtEveryArity()
		{
			var cases = new (MapPgShardKeyAttribute Attribute, string? Child, string? GrandChild, string? GreatGrandChild, bool Flag)[]
			{
				(new MapPgShardKeyAttribute("ShardId", "RecordId"), null, null, null, true),
				(new MapPgShardKeyAttribute("ShardId", "RecordId", false), null, null, null, false),
				(new MapPgShardKeyAttribute("ShardId", "RecordId", "ChildId"), "ChildId", null, null, true),
				(new MapPgShardKeyAttribute("ShardId", "RecordId", "ChildId", false), "ChildId", null, null, false),
				(new MapPgShardKeyAttribute("ShardId", "RecordId", "ChildId", "GrandChildId"), "ChildId", "GrandChildId", null, true),
				(new MapPgShardKeyAttribute("ShardId", "RecordId", "ChildId", "GrandChildId", false), "ChildId", "GrandChildId", null, false),
				(new MapPgShardKeyAttribute("ShardId", "RecordId", "ChildId", "GrandChildId", "GreatGrandChildId"), "ChildId", "GrandChildId", "GreatGrandChildId", true),
				(new MapPgShardKeyAttribute("ShardId", "RecordId", "ChildId", "GrandChildId", "GreatGrandChildId", false), "ChildId", "GrandChildId", "GreatGrandChildId", false),
			};
			typeof(MapPgShardKeyAttribute).GetConstructors().Should().HaveCount(10, "two record-only overloads plus the eight shard overloads above");

			foreach (var (attribute, child, grandChild, greatGrandChild, flag) in cases)
			{
				var shard = attribute.ShardParameter.Should().BeOfType<MapToPgSmallintAttribute>("the shard identifier is a PostgreSQL smallint mapping").Subject;
				shard.ParameterName.Should().Be("ShardId");
				((NpgsqlDbType)shard.SqlType).Should().Be(NpgsqlDbType.Smallint);
				shard.IsRecordIdentifier.Should().BeFalse("the shard column is not itself the record identifier");
				attribute.ShardIdName.Should().Be("ShardId");
				attribute.RecordIdName.Should().Be("RecordId");
				attribute.ChildIdName.Should().Be(child);
				attribute.GrandChildIdName.Should().Be(grandChild);
				attribute.GreatGrandChildIdName.Should().Be(greatGrandChild);
				attribute.IsRecordIdentifier.Should().Be(flag, "overloads without the Boolean default to true and overloads with it take the supplied value");
			}
		}
	}
}
