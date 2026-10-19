using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using NSubstitute;
using Xunit;

namespace ArgentSea.Sql.Test
{
	class SqlTimeOnlyModel
	{
		[MapToSqlTime("WakeTime")]
		public TimeOnly WakeTime { get; set; }

		[MapToSqlTime("NapTime")]
		public TimeOnly? NapTime { get; set; }

		[MapToSqlTime("SnoozeTime")]
		public TimeOnly? SnoozeTime { get; set; }
	}

	class SqlDateOnlyModel
	{
		[MapToSqlDate("StartDate")]
		public DateOnly StartDate { get; set; }

		[MapToSqlDate("EndDate")]
		public DateOnly? EndDate { get; set; }
	}

	class SqlReadOnlyMemoryBinaryModel
	{
		[MapToSqlVarBinary("Payload", 100)]
		public ReadOnlyMemory<byte> Payload { get; set; }
	}

	class SqlMemoryBinaryModel
	{
		[MapToSqlVarBinary("Payload", 100)]
		public Memory<byte> Payload { get; set; }
	}

	/// <summary>
	/// Covers the SQL Time/TimeOnly, Date/DateOnly and VarBinary type matrix: every type IsValidType accepts
	/// must work through input parameters, output parameters, the data reader and TVP records; the rest are rejected up front.
	/// </summary>
	public class TypeMappingCoverageTests
	{
		private static readonly TimeOnly Wake = new TimeOnly(6, 30, 15);
		private static readonly TimeOnly Nap = new TimeOnly(13, 45, 0);

		[Fact]
		[Trait("Spec", "blk-1e17157e9a7742fa8d14e0088a3f73ea")]
		public void TimeOnlyInputParametersAreSentAsTimeSpan()
		{
			var model = new SqlTimeOnlyModel() { WakeTime = Wake, NapTime = Nap, SnoozeTime = null };
			var prms = new SqlCommand().Parameters;

			prms.CreateInputParameters(model, new DebugLogger());

			((SqlParameter)prms["@WakeTime"]).SqlDbType.Should().Be(SqlDbType.Time);
			((SqlParameter)prms["@WakeTime"]).Value.Should().Be(Wake.ToTimeSpan());
			((SqlParameter)prms["@NapTime"]).SqlDbType.Should().Be(SqlDbType.Time);
			((SqlParameter)prms["@NapTime"]).Value.Should().Be(Nap.ToTimeSpan());
			((SqlParameter)prms["@SnoozeTime"]).Value.Should().Be(DBNull.Value, "a null TimeOnly? is sent as DbNull");
		}

		[Fact]
		[Trait("Spec", "blk-1e17157e9a7742fa8d14e0088a3f73ea")]
		public void TimeOnlyOutputParametersAreReadFromTimeSpanValues()
		{
			var prms = new SqlCommand().Parameters;
			prms.CreateOutputParameters<SqlTimeOnlyModel>(new DebugLogger());
			((SqlParameter)prms["@WakeTime"]).SqlDbType.Should().Be(SqlDbType.Time);
			((SqlParameter)prms["@WakeTime"]).Direction.Should().Be(ParameterDirection.Output);

			// SqlClient surfaces time output values as TimeSpan.
			prms["@WakeTime"].Value = Wake.ToTimeSpan();
			prms["@NapTime"].Value = Nap.ToTimeSpan();
			prms["@SnoozeTime"].Value = DBNull.Value;

			var result = prms.ToModel<SqlTimeOnlyModel>(new DebugLogger());

			result!.WakeTime.Should().Be(Wake);
			result.NapTime.Should().Be(Nap);
			result.SnoozeTime.Should().BeNull();
		}

		[Fact]
		[Trait("Spec", "blk-1e17157e9a7742fa8d14e0088a3f73ea")]
		public void TimeOnlyTvpRecordUsesTimeSpan()
		{
			var model = new SqlTimeOnlyModel() { WakeTime = Wake, NapTime = Nap, SnoozeTime = null };

			var record = TvpMapper.ToTvpRecord(model, new DebugLogger());

			record.GetTimeSpan(0).Should().Be(Wake.ToTimeSpan());
			record.GetTimeSpan(1).Should().Be(Nap.ToTimeSpan());
			record.IsDBNull(2).Should().BeTrue("a null TimeOnly? is written as DbNull");
		}

		[Fact]
		public void TimeOnlyIsReadFromTheDataReader()
		{
			var rdr = Substitute.For<DbDataReader>();
			rdr.NextResult().Returns(false);
			rdr.Read().Returns(true, false);
			rdr.IsClosed.Returns(false);
			rdr.HasRows.Returns(true);
			rdr.FieldCount.Returns(3);
			rdr.GetName(0).Returns("WakeTime");
			rdr.GetName(1).Returns("NapTime");
			rdr.GetName(2).Returns("SnoozeTime");
			rdr.GetFieldValue<TimeOnly>(0).Returns(Wake);
			rdr.GetFieldValue<TimeOnly>(1).Returns(Nap);
			rdr.IsDBNull(1).Returns(false);
			rdr.IsDBNull(2).Returns(true);

			var result = rdr.ToModel<SqlTimeOnlyModel>(new DebugLogger());

			result!.WakeTime.Should().Be(Wake);
			result.NapTime.Should().Be(Nap);
			result.SnoozeTime.Should().BeNull();
		}

		[Fact]
		public void DateOnlyOutputParametersAreReadFromDateTimeValues()
		{
			var prms = new SqlCommand().Parameters;
			prms.CreateOutputParameters<SqlDateOnlyModel>(new DebugLogger());

			// SqlClient surfaces date output values as DateTime.
			prms["@StartDate"].Value = new DateTime(2024, 2, 29);
			prms["@EndDate"].Value = new DateTime(2024, 3, 1);

			var result = prms.ToModel<SqlDateOnlyModel>(new DebugLogger());

			result!.StartDate.Should().Be(new DateOnly(2024, 2, 29));
			result.EndDate.Should().Be(new DateOnly(2024, 3, 1));
		}

		[Fact]
		public void DateOnlyInputAndTvpRoundTrip()
		{
			var model = new SqlDateOnlyModel() { StartDate = new DateOnly(2024, 2, 29), EndDate = null };
			var prms = new SqlCommand().Parameters;

			prms.CreateInputParameters(model, new DebugLogger());
			((SqlParameter)prms["@StartDate"]).SqlDbType.Should().Be(SqlDbType.Date);
			((SqlParameter)prms["@StartDate"]).Value.Should().Be(model.StartDate);
			((SqlParameter)prms["@EndDate"]).Value.Should().Be(DBNull.Value);

			var record = TvpMapper.ToTvpRecord(model, new DebugLogger());
			record.GetDateTime(0).Should().Be(new DateTime(2024, 2, 29));
			record.IsDBNull(1).Should().BeTrue();
		}

		[Fact]
		public void TimeOnlyGettersAcceptBothRepresentations()
		{
			var prm = new SqlParameter("@t", SqlDbType.Time) { Value = Wake };
			prm.GetTimeOnly().Should().Be(Wake);
			prm.Value = Wake.ToTimeSpan();
			prm.GetTimeOnly().Should().Be(Wake);
			prm.GetNullableTimeOnly().Should().Be(Wake);
			prm.Value = DBNull.Value;
			prm.GetNullableTimeOnly().Should().BeNull();
			((Action)(() => prm.GetTimeOnly())).Should().Throw<Exception>("a DbNull value cannot be read as a non-nullable TimeOnly");
		}

		[Theory]
		[InlineData(typeof(TimeSpan), true)]
		[InlineData(typeof(TimeSpan?), true)]
		[InlineData(typeof(TimeOnly), true)]
		[InlineData(typeof(TimeOnly?), true)]
		[InlineData(typeof(DateTime), false)]
		public void SqlTimeAcceptsTimeSpanAndTimeOnly(Type candidate, bool expected)
			=> new MapToSqlTimeAttribute("t").IsValidType(candidate).Should().Be(expected);

		[Theory]
		[InlineData(typeof(byte[]), true)]
		[InlineData(typeof(ReadOnlyMemory<byte>), false)]
		[InlineData(typeof(Memory<byte>), false)]
		[InlineData(typeof(ReadOnlySpan<byte>), false)]
		[InlineData(typeof(Span<byte>), false)]
		[Trait("Spec", "blk-659577321b9445b8b52fa4b61291f06a")]
		public void SqlVarBinaryAcceptsOnlyByteArrays(Type candidate, bool expected)
			=> new MapToSqlVarBinaryAttribute("b", 10).IsValidType(candidate).Should().Be(expected);

		[Fact]
		public void ReadOnlyMemoryBinaryPropertyIsRejectedUpFront()
		{
			var prms = new SqlCommand().Parameters;
			Action act = () => prms.CreateInputParameters(new SqlReadOnlyMemoryBinaryModel() { Payload = new byte[] { 1, 2 } }, new DebugLogger());
			act.Should().Throw<InvalidMapTypeException>();
		}

		[Fact]
		public void MemoryBinaryPropertyIsRejectedUpFront()
		{
			Action act = () => TvpMapper.ToTvpRecord(new SqlMemoryBinaryModel() { Payload = new byte[] { 1, 2 } }, new DebugLogger());
			act.Should().Throw<InvalidMapTypeException>();
		}
	}
}
