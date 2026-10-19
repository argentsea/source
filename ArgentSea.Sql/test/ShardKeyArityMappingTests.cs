using System.Data;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArgentSea.Sql.Test
{
    /// <summary>
    /// A four-part (great-grandchild) ShardKey must be mapped through its MapShardKey attribute by every compiler,
    /// and a ShardKey with no shard parameter takes its ShardId from the connection’s shard.
    /// </summary>
    public class ShardKeyArityMappingTests
    {
        public class GreatGrandChildModel
        {
            [MapSqlShardKey("GgShard", "GgRecordId", "GgChildId", "GgGrandChildId", "GgGreatGrandChildId")]
            [MapToSqlInt("GgRecordId")]
            [MapToSqlSmallInt("GgChildId")]
            [MapToSqlSmallInt("GgGrandChildId")]
            [MapToSqlSmallInt("GgGreatGrandChildId")]
            public ShardKey<int, short, short, short> Key { get; set; } = ShardKey<int, short, short, short>.Empty;
        }

        public class ConnectionShardModel
        {
            [MapSqlShardKey("CsRecordId")]
            [MapToSqlBigInt("CsRecordId")]
            public ShardKey<long> Key { get; set; } = ShardKey<long>.Empty;
        }

        [Fact]
        public void GreatGrandChildKeyCreatesOutputParameters()
        {
            var prms = new ParameterCollection();
            prms.CreateOutputParameters<GreatGrandChildModel>(NullLogger.Instance);

            prms.Count.Should().Be(5, "the shard parameter and all four key parts are output parameters");
            ((SqlParameter)prms["@GgShard"]).SqlDbType.Should().Be(SqlDbType.SmallInt);
            ((SqlParameter)prms["@GgRecordId"]).SqlDbType.Should().Be(SqlDbType.Int);
            ((SqlParameter)prms["@GgGreatGrandChildId"]).Direction.Should().Be(ParameterDirection.Output);
        }

        [Fact]
        public void GreatGrandChildKeyCreatesInputParameters()
        {
            var model = new GreatGrandChildModel { Key = new ShardKey<int, short, short, short>(7, 11, 12, 13, 14) };
            var prms = new ParameterCollection();
            prms.CreateInputParameters(model, NullLogger.Instance);

            ((SqlParameter)prms["@GgShard"]).Value.Should().Be((short)7);
            ((SqlParameter)prms["@GgRecordId"]).Value.Should().Be(11);
            ((SqlParameter)prms["@GgGreatGrandChildId"]).Value.Should().Be((short)14);
        }

        [Fact]
        public void GreatGrandChildKeyReadsFromOutputParameters()
        {
            var cmd = new SqlCommand();
            cmd.Parameters.Add(new SqlParameter("@GgShard", SqlDbType.SmallInt) { Value = (short)7, Direction = ParameterDirection.Output });
            cmd.Parameters.Add(new SqlParameter("@GgRecordId", SqlDbType.Int) { Value = 11, Direction = ParameterDirection.Output });
            cmd.Parameters.Add(new SqlParameter("@GgChildId", SqlDbType.SmallInt) { Value = (short)12, Direction = ParameterDirection.Output });
            cmd.Parameters.Add(new SqlParameter("@GgGrandChildId", SqlDbType.SmallInt) { Value = (short)13, Direction = ParameterDirection.Output });
            cmd.Parameters.Add(new SqlParameter("@GgGreatGrandChildId", SqlDbType.SmallInt) { Value = (short)14, Direction = ParameterDirection.Output });

            var result = cmd.Parameters.ToModel<GreatGrandChildModel>((short)99, NullLogger.Instance);

            result!.Key.Should().Be(new ShardKey<int, short, short, short>(7, 11, 12, 13, 14));
        }

        public class GrandChildModel
        {
            [MapSqlShardKey("GcShard", "GcRecordId", "GcChildId", "GcGrandChildId")]
            [MapToSqlInt("GcRecordId")]
            [MapToSqlSmallInt("GcChildId")]
            [MapToSqlNVarChar("GcGrandChildId", 50)]
            public ShardKey<int, short, string>? Key { get; set; }
        }

        [Fact]
        public void GrandChildKeyReadsFromOutputParameters()
        {
            var cmd = new SqlCommand();
            cmd.Parameters.Add(new SqlParameter("@GcShard", SqlDbType.SmallInt) { Value = (short)3, Direction = ParameterDirection.Output });
            cmd.Parameters.Add(new SqlParameter("@GcRecordId", SqlDbType.Int) { Value = 21, Direction = ParameterDirection.Output });
            cmd.Parameters.Add(new SqlParameter("@GcChildId", SqlDbType.SmallInt) { Value = (short)22, Direction = ParameterDirection.Output });
            cmd.Parameters.Add(new SqlParameter("@GcGrandChildId", SqlDbType.NVarChar, 50) { Value = "gc", Direction = ParameterDirection.Output });

            var result = cmd.Parameters.ToModel<GrandChildModel>((short)99, NullLogger.Instance);

            result!.Key.Should().Be(new ShardKey<int, short, string>(3, 21, 22, "gc"));
        }

        [Fact]
        public void KeyWithoutShardParameterTakesShardIdFromConnection()
        {
            var prms = new ParameterCollection();
            prms.CreateOutputParameters<ConnectionShardModel>(NullLogger.Instance);
            prms.Count.Should().Be(1, "no shard parameter is declared, so only the record id is an output parameter");

            var cmd = new SqlCommand();
            cmd.Parameters.Add(new SqlParameter("@CsRecordId", SqlDbType.BigInt) { Value = 42L, Direction = ParameterDirection.Output });

            var result = cmd.Parameters.ToModel<ConnectionShardModel>((short)9, NullLogger.Instance);

            result!.Key.Should().Be(new ShardKey<long>(9, 42L), "the ShardId comes from the connection’s shard");
        }
    }
}
