using System;
using System.Data.Common;
using System.Linq;
using ArgentSea.TestKit;
using FluentAssertions;
using Xunit;

namespace ArgentSea.Test
{
    /// <summary>
    /// Models may be <c>readonly record struct</c> types: the Mapper only needs a parameterless constructor and
    /// settable (including init-only) properties, and value-type models round-trip through readers and parameters.
    /// </summary>
    public class RecordStructModelTests
    {
        public readonly record struct Point
        {
            [FakeMap("Id", true)]
            public int Id { get; init; }

            [FakeMap("Name")]
            public string? Name { get; init; }

            [FakeMap("Score")]
            public decimal? Score { get; init; }
        }

        public readonly record struct Positional(
            [property: FakeMap("Id", true)] int Id,
            [property: FakeMap("Name")] string? Name);

        private static FakeDbDataReader Reader(string[] columns, params object?[][] rows)
            => new FakeDbDataReader(new[] { new FakeResultSet(columns, rows) });

        [Fact]
        public void ReadonlyRecordStructWithInitPropertiesRoundTripsThroughReaderAndParameters()
        {
            var list = Reader(new[] { "Id", "Name", "Score" }, new object?[] { 1, "a", 2.5m }, new object?[] { 2, null, null })
                .ToList<Point>(0, null);
            list.Should().Equal(new Point { Id = 1, Name = "a", Score = 2.5m }, new Point { Id = 2 });

            var single = Reader(new[] { "Id", "Name", "Score" }, new object?[] { 7, "z", 1m }).ToModel<Point>(0, null);
            single.Should().Be(new Point { Id = 7, Name = "z", Score = 1m });

            var prms = new ParameterCollection().CreateInputParameters(new Point { Id = 3, Name = "n", Score = 9m }, null);
            prms.Cast<DbParameter>().Select(p => p.Value).Should().BeEquivalentTo(new object[] { 3, "n", 9m });
        }

        [Fact]
        public void AMissingRecordComesBackAsDefaultForAStructModelRatherThanNull()
        {
            // Id is the required column: a null there means "no record". A class model would be null here and the row
            // would be dropped from a list; a struct model has no null, so the Mapper yields default(TModel) in both places.
            var single = Reader(new[] { "Id", "Name", "Score" }, new object?[] { null, "ghost", null }).ToModel<Point>(0, null);
            single.Should().Be(default(Point));

            var list = Reader(new[] { "Id", "Name", "Score" }, new object?[] { null, "ghost", null }, new object?[] { 5, "e", null })
                .ToList<Point>(0, null);
            list.Should().Equal(default(Point), new Point { Id = 5, Name = "e" });
        }

        [Fact]
        public void PositionalReadonlyRecordStructRoundTripsThroughReader()
        {
            var list = Reader(new[] { "Id", "Name" }, new object?[] { 4, "d" }).ToList<Positional>(0, null);
            list.Should().Equal(new Positional(4, "d"));
        }
    }
}
