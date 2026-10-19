using System;
using System.Data;
using System.Linq;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// The Mapper's ShardKey code handles four arities, but only the one- and two-part keys were covered by tests.
    /// A three-part key (ShardKey&lt;,,&gt;) failed to build its read delegate because the constructor-selection branch
    /// fell through and overwrote the argument array with a two-element one, and a four-part key (ShardKey&lt;,,,&gt;)
    /// was excluded from shard handling by a guard that tested the grandchild flag twice instead of the great-grandchild flag.
    /// </summary>
    public class ShardKeyDescendantMappingTests
    {
        public class GrandChildModel
        {
            [MapSqlShardKey("GcShard", "GcRecordId", "GcChildId", "GcGrandChildId")]
            [MapToSqlInt("GcRecordId")]
            [MapToSqlSmallInt("GcChildId")]
            [MapToSqlBigInt("GcGrandChildId")]
            public ShardKey<int, short, long> Key { get; set; } = ShardKey<int, short, long>.Empty;
        }

        public class GreatGrandChildModel
        {
            [MapSqlShardKey("GgcShard", "GgcRecordId", "GgcChildId", "GgcGrandChildId", "GgcGreatGrandChildId")]
            [MapToSqlBigInt("GgcRecordId")]
            [MapToSqlInt("GgcChildId")]
            [MapToSqlSmallInt("GgcGrandChildId")]
            [MapToSqlNVarChar("GgcGreatGrandChildId", 50)]
            public ShardKey<long, int, short, string>? Key { get; set; }
        }

        private static SqlParameter Out(string name, SqlDbType type, object value)
            => new SqlParameter(name, type) { Value = value, Direction = ParameterDirection.Output };

        [Fact]
        public void GrandChildKeyFromOutputParameters()
        {
            var prms = new SqlCommand().Parameters;
            prms.Add(Out("@GcShard", SqlDbType.SmallInt, (short)7));
            prms.Add(Out("@GcRecordId", SqlDbType.Int, 1001));
            prms.Add(Out("@GcChildId", SqlDbType.SmallInt, (short)-12));
            prms.Add(Out("@GcGrandChildId", SqlDbType.BigInt, 9_000_000_000L));

            var result = prms.ToModel<GrandChildModel>((short)99, new DebugLogger());

            result!.Key.Should().Be(new ShardKey<int, short, long>(7, 1001, -12, 9_000_000_000L));
        }

        [Fact]
        public void GreatGrandChildKeyFromOutputParameters()
        {
            var prms = new SqlCommand().Parameters;
            prms.Add(Out("@GgcShard", SqlDbType.SmallInt, (short)8));
            prms.Add(Out("@GgcRecordId", SqlDbType.BigInt, 5_000_000_000L));
            prms.Add(Out("@GgcChildId", SqlDbType.Int, 42));
            prms.Add(Out("@GgcGrandChildId", SqlDbType.SmallInt, (short)3));
            prms.Add(new SqlParameter("@GgcGreatGrandChildId", SqlDbType.NVarChar, 50) { Value = "leaf", Direction = ParameterDirection.Output });

            var result = prms.ToModel<GreatGrandChildModel>((short)99, new DebugLogger());

            result!.Key.Should().Be(new ShardKey<long, int, short, string>(8, 5_000_000_000L, 42, 3, "leaf"));
        }

        [Fact]
        public void GrandChildKeyFromDataReader()
        {
            var table = new DataTable();
            table.Columns.Add("GcShard", typeof(short));
            table.Columns.Add("GcRecordId", typeof(int));
            table.Columns.Add("GcChildId", typeof(short));
            table.Columns.Add("GcGrandChildId", typeof(long));
            table.Rows.Add((short)7, 1001, (short)-12, 9_000_000_000L);
            table.Rows.Add((short)8, 1002, (short)13, 1L);

            var list = Mapper.ToList<GrandChildModel>(table.CreateDataReader(), (short)99, new DebugLogger());
            // ToModel expects exactly one row.
            table.Rows.RemoveAt(1);
            var single = table.CreateDataReader().ToModel<GrandChildModel>((short)99, new DebugLogger());

            list.Select(m => m.Key).Should().Equal(
                new ShardKey<int, short, long>(7, 1001, -12, 9_000_000_000L),
                new ShardKey<int, short, long>(8, 1002, 13, 1L));
            single!.Key.Should().Be(new ShardKey<int, short, long>(7, 1001, -12, 9_000_000_000L));
        }

        [Fact]
        public void GreatGrandChildKeyFromDataReader()
        {
            var table = new DataTable();
            table.Columns.Add("GgcShard", typeof(short));
            table.Columns.Add("GgcRecordId", typeof(long));
            table.Columns.Add("GgcChildId", typeof(int));
            table.Columns.Add("GgcGrandChildId", typeof(short));
            table.Columns.Add("GgcGreatGrandChildId", typeof(string));
            table.Rows.Add((short)8, 5_000_000_000L, 42, (short)3, "leaf");
            table.Rows.Add((short)9, 1L, 2, (short)4, DBNull.Value);

            var list = Mapper.ToList<GreatGrandChildModel>(table.CreateDataReader(), (short)99, new DebugLogger());
            // ToModel expects exactly one row.
            table.Rows.RemoveAt(1);
            var single = table.CreateDataReader().ToModel<GreatGrandChildModel>((short)99, new DebugLogger());

            list.Should().HaveCount(2);
            list[0].Key.Should().Be(new ShardKey<long, int, short, string>(8, 5_000_000_000L, 42, 3, "leaf"));
            list[1].Key.Should().BeNull("a null key part leaves a nullable ShardKey null");
            single!.Key.Should().Be(new ShardKey<long, int, short, string>(8, 5_000_000_000L, 42, 3, "leaf"));
        }

        [Fact]
        public void GreatGrandChildKeyToInputParameters()
        {
            var prms = new SqlCommand().Parameters;
            var model = new GreatGrandChildModel { Key = new ShardKey<long, int, short, string>(8, 5_000_000_000L, 42, 3, "leaf") };

            prms.CreateInputParameters(model, new DebugLogger());

            prms["@GgcShard"].Value.Should().Be((short)8);
            prms["@GgcRecordId"].Value.Should().Be(5_000_000_000L);
            prms["@GgcChildId"].Value.Should().Be(42);
            prms["@GgcGrandChildId"].Value.Should().Be((short)3);
            prms["@GgcGreatGrandChildId"].Value.Should().Be("leaf");
        }

        [Fact]
        public void GreatGrandChildKeyCreatesOutputParameters()
        {
            var prms = new SqlCommand().Parameters;

            prms.CreateOutputParameters<GreatGrandChildModel>(new DebugLogger());

            prms.Contains("@GgcShard").Should().BeTrue();
            prms.Contains("@GgcRecordId").Should().BeTrue();
            prms.Contains("@GgcChildId").Should().BeTrue();
            prms.Contains("@GgcGrandChildId").Should().BeTrue();
            prms.Contains("@GgcGreatGrandChildId").Should().BeTrue();
            prms["@GgcGreatGrandChildId"].Direction.Should().Be(ParameterDirection.Output);
        }
    }
}
