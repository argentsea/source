using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using ArgentSea.Pg;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace ArgentSea.Orleans.Pg;

/// <summary>
/// The Orleans reminder table over the routines of tenant.psql, sharded by the grain key's ShardKey.
/// This cannot use the ArgentSea MapTo attributes because the ReminderEntry class is sealed.
/// </summary>
public class ArgentSeaOrleansReminderTable : IReminderTable
{
    private readonly PgShardSets.ShardSet shardSet;
    private readonly ILogger<ArgentSeaOrleansReminderTable> logger;
    private readonly string shardSetKey;

    public ArgentSeaOrleansReminderTable(PgShardSets shards, IOptions<OrleansShardPersistenceOptions> dbOptions, ILogger<ArgentSeaOrleansReminderTable> logger)
    {
        ArgumentNullException.ThrowIfNull(shards, nameof(shards));
        this.shardSetKey = dbOptions.Value.ShardSetKey;
        var shardSet = shards[shardSetKey];
        if (shardSet is null)
        {
            throw new KeyNotFoundException($"ShardSet “{shardSetKey}” cannot be found in the ShardSets collection.");
        }
        this.shardSet = shardSet;
        this.logger = logger;
    }

    public async Task<ReminderEntry?> ReadRow(GrainId grainId, string reminderName)
    {
        ArgumentNullException.ThrowIfNull(reminderName, nameof(reminderName));

        var prms = new ParameterCollection()
            .AddPgByteaInputParameter("p_grain_key", grainId.Key.Value.ToArray())
            .AddPgVarcharInputParameter("p_grain_type", grainId.Type.ToString(), 1023)
            .AddPgVarcharInputParameter("p_reminder_name", reminderName, 150);

        var shardId = grainId.ShardId();
        try
        {
            var result = await shardSet[shardId].Read.QueryAsync<GrainId, ReminderEntry?>(Queries.OrleansReminderReadRow, prms, ReadRowHandler, true, grainId, CancellationToken.None);
            return result ?? new ReminderEntry() { GrainId = grainId };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading reminder row.");
            throw;
        }
    }

    private static ReminderEntry? ReadRowHandler(ReminderEntry? instance, short shardId, string sprocName, GrainId grainId, DbDataReader rdr, DbParameterCollection prms, string connectionDescription, ILogger? logger)
    {
        if (!rdr.HasRows || rdr.IsClosed || !rdr.Read() || rdr.IsDBNull(0))
        {
            return new ReminderEntry() { GrainId = grainId };
        }
        return ReadEntry(rdr);
    }

    /// <summary>
    /// Result handler for the multi-row reminder queries. The data connection invokes it once per query execution
    /// (i.e. once per shard), so it must drain every row of the reader into the shared bag.
    /// </summary>
    internal static object ReadRowsHandler(object? instance, short shardId, string sprocName, ConcurrentBag<ReminderEntry> bagEntries, DbDataReader rdr, DbParameterCollection prms, string connectionDescription, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(bagEntries);

        if (!rdr.HasRows || rdr.IsClosed)
        {
            return null!;
        }

        while (rdr.Read())
        {
            if (rdr.IsDBNull(1))
            {
                continue;
            }
            bagEntries.Add(ReadEntry(rdr));
        }
        return null!;
    }

    /// <summary>Maps one [grain key, grain type, name, start time, period ticks, version] row.</summary>
    internal static ReminderEntry ReadEntry(DbDataReader rdr)
    {
        var gType = new GrainType(UTF8Encoding.UTF8.GetBytes(rdr.GetString(1)));
        var grainKey = new IdSpan((byte[])rdr.GetValue(0));
        return new ReminderEntry()
        {
            GrainId = new GrainId(gType, grainKey),
            ReminderName = rdr.GetString(2),
            StartAt = UtcTime.ToUtc(rdr.GetDateTime(3)),
            Period = new TimeSpan(rdr.GetInt64(4)),
            ETag = rdr.GetInt32(5).ToString(CultureInfo.InvariantCulture),
        };
    }

    public async Task<ReminderTableData> ReadRows(GrainId grainId)
    {
        var prms = new ParameterCollection()
            .AddPgByteaInputParameter("p_grain_key", grainId.Key.Value.ToArray())
            .AddPgVarcharInputParameter("p_grain_type", grainId.Type.ToString(), 1023);

        var shardId = grainId.ShardId();
        try
        {
            var result = new ConcurrentBag<ReminderEntry>();
            await shardSet[shardId].Read.QueryAsync<ConcurrentBag<ReminderEntry>, object>(Queries.OrleansReminderReadRows, prms, ReadRowsHandler, false, result, CancellationToken.None); // Using TArg to pass a shared list to which handler can append. Ignoring return value.
            return new ReminderTableData(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading reminder rows.");
            throw;
        }
    }

    public async Task<ReminderTableData> ReadRows(uint begin, uint end)
    {
        var prms = new ParameterCollection()
            .AddPgBigintInputParameter("p_begin_hash", (long)begin)
            .AddPgBigintInputParameter("p_end_hash", (long)end);
        try
        {
            var result = new ConcurrentBag<ReminderEntry>();
            await shardSet.ReadAll.QueryAsync<ConcurrentBag<ReminderEntry>, object>(Queries.OrleansReminderReadRangeRows, prms, ReadRowsHandler, result, CancellationToken.None); // Using TArg to pass a shared list to which handler can append. Ignoring return value.
            return new ReminderTableData(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading reminder rows.");
            throw;
        }
    }

    public Task<bool> RemoveRow(GrainId grainId, string reminderName, string eTag)
    {
        ArgumentNullException.ThrowIfNull(reminderName, nameof(reminderName));

        var prms = new ParameterCollection()
            .AddPgByteaInputParameter("p_grain_key", grainId.Key.Value.ToArray())
            .AddPgVarcharInputParameter("p_grain_type", grainId.Type.ToString(), 1023)
            .AddPgVarcharInputParameter("p_reminder_name", reminderName, 150)
            .AddPgIntegerInputParameter("p_version", int.Parse(eTag, CultureInfo.InvariantCulture))
            .AddInOutParameter("p_is_found", NpgsqlDbType.Boolean);

        var shardId = grainId.ShardId();
        try
        {
            return shardSet[shardId].Write.ReturnValueAsync<bool>(Queries.OrleansReminderDeleteRow, "p_is_found", prms, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting reminder row.");
            throw;
        }
    }

    public Task TestOnlyClearTable()
    {
        // do nothing
        return Task.CompletedTask;
    }

    public async Task<string?> UpsertRow(ReminderEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry, nameof(entry));

        int? etag = entry.ETag is not null ? int.Parse(entry.ETag, CultureInfo.InvariantCulture) : null;

        var shardId = entry.GrainId.ShardId();
        var prms = new ParameterCollection()
            .AddPgByteaInputParameter("p_grain_key", entry.GrainId.Key.Value.ToArray())
            .AddPgVarcharInputParameter("p_grain_type", entry.GrainId.Type.ToString(), 1023)
            .AddPgVarcharInputParameter("p_reminder_name", entry.ReminderName, 150)
            .AddPgTimestampTzInputParameter("p_start_time", UtcTime.ToUtcOffset(entry.StartAt))
            .AddPgBigintInputParameter("p_period", entry.Period.Ticks)
            .AddPgBigintInputParameter("p_grain_hash", (long)entry.GrainId.GetUniformHashCode())
            .AddPgIntegerInputParameter("p_old_version", etag)
            .AddInOutParameter("p_new_version", NpgsqlDbType.Integer);

        try
        {
            var intTag = await shardSet[shardId].Write.ReturnValueAsync<int>(Queries.OrleansReminderUpsertRow, "p_new_version", prms, CancellationToken.None);
            return intTag.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error upserting reminder row.");
            throw;
        }
    }
}

internal static class ReminderParameterExtensions
{
    /// <summary>
    /// An INOUT procedure argument, sent as NULL and read back after the CALL. Npgsql passes an InputOutput parameter as a
    /// CALL argument, which a procedure's INOUT parameter requires.
    /// </summary>
    internal static DbParameterCollection AddInOutParameter(this DbParameterCollection prms, string parameterName, NpgsqlDbType type)
    {
        prms.Add(new NpgsqlParameter(parameterName, type) { Direction = ParameterDirection.InputOutput, Value = DBNull.Value });
        return prms;
    }
}
