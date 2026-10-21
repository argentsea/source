using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using ArgentSea.Pg;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Orleans.Configuration;

namespace ArgentSea.Orleans.Pg;

/// <summary>
/// The Orleans cluster membership table over the routines of common.psql.
/// This cannot use the ArgentSea MapTo attributes because the MembershipEntry class is sealed.
/// </summary>
public class ArgentSeaOrleansMembershipTable : IMembershipTable
{
    private readonly PgDatabases.Database _db;
    private readonly string _clusterId;
    private readonly ILogger<ArgentSeaOrleansMembershipTable> _logger;
    private readonly string _dbKey;

    public ArgentSeaOrleansMembershipTable(PgDatabases dbs, IOptions<ClusterOptions> clusterOptions, IOptions<OrleansDbPersistenceOptions> dbOptions, ILogger<ArgentSeaOrleansMembershipTable> logger)
    {
        ArgumentNullException.ThrowIfNull(dbs, nameof(dbs));
        _dbKey = dbOptions.Value.DatabaseKey;
        var db = dbs[_dbKey];
        if (db is null)
        {
            throw new KeyNotFoundException($"Database connection “{_dbKey}” was not found in the databases collection.");
        }
        _db = db;
        _clusterId = clusterOptions.Value.ClusterId;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the membership table, will be called before all other methods
    /// </summary>
    /// <param name="tryInitTableVersion">whether an attempt will be made to init the underlying table</param>
    public Task InitializeMembershipTable(bool tryInitTableVersion)
    {
        // The membership tables already exist, via the common.psql script; nothing to do here, other than maybe create a version record.
        if (tryInitTableVersion)
        {
            var prms = new ParameterCollection()
                .AddPgVarcharInputParameter("p_cluster_id", _clusterId, 150)
                .AddPgIntegerInputParameter("p_etag_no", 0)
                .AddPgIntegerInputParameter("p_version", 0);
            return _db.Write.RunAsync(Queries.OrleansClusterInsertMemberVersion, prms, CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Delete all dead silo entries older than <paramref name="beforeDate"/>
    /// </summary>
    public Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate)
    {
        var prms = new ParameterCollection()
            .AddPgVarcharInputParameter("p_cluster_id", _clusterId, 150)
            .AddPgTimestampTzInputParameter("p_before_date", beforeDate.ToUniversalTime());
        try
        {
            return _db.Write.RunAsync(Queries.OrleansClusterDeleteDefunctMembers, prms, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up defunct silo entries.");
            throw;
        }
    }

    /// <summary>
    /// Deletes all entries of the given clusterId
    /// </summary>
    public Task DeleteMembershipTableEntries(string clusterId)
    {
        ArgumentNullException.ThrowIfNull(clusterId, nameof(clusterId));

        var prms = new ParameterCollection()
            .AddPgVarcharInputParameter("p_cluster_id", clusterId, 150);
        try
        {
            return _db.Write.RunAsync(Queries.OrleansClusterDelete, prms, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting membership table entries for clusterId:{ClusterId}", clusterId);
            throw;
        }
    }

    /// <summary>
    /// Atomically tries to insert (add) a new MembershipEntry for one silo and also update the TableVersion.
    /// The procedure succeeds only when the stored table etag equals the TableVersion's etag and no row exists for the silo;
    /// it then advances the table etag and stamps it on the new row. On any failure nothing changes.
    /// </summary>
    /// <param name="entry">MembershipEntry to be inserted.</param>
    /// <param name="tableVersion">The new TableVersion for this table, along with its etag.</param>
    /// <returns>True if the insert operation succeeded and false otherwise.</returns>
    public async Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion)
    {
        ArgumentNullException.ThrowIfNull(entry, nameof(entry));
        ArgumentNullException.ThrowIfNull(tableVersion, nameof(tableVersion));

        var prms = new ParameterCollection()
            .AddPgVarcharInputParameter("p_cluster_id", _clusterId, 150)
            .AddPgByteaInputParameter("p_address", entry.SiloAddress.Endpoint.Address.GetAddressBytes())
            .AddPgIntegerInputParameter("p_port", entry.SiloAddress.Endpoint.Port)
            .AddPgIntegerInputParameter("p_generation", entry.SiloAddress.Generation)
            .AddPgVarcharInputParameter("p_silo_name", entry.SiloName, 150)
            .AddPgVarcharInputParameter("p_host_name", entry.HostName, 150)
            .AddPgSmallintInputParameter("p_status", (short)entry.Status)
            .AddPgIntegerInputParameter("p_proxy_port", entry.ProxyPort)
            .AddPgTimestampTzInputParameter("p_start_time", UtcTime.ToUtcOffset(entry.StartTime))
            .AddPgTimestampTzInputParameter("p_i_am_alive_time", UtcTime.ToUtcOffset(entry.IAmAliveTime))
            .AddPgIntegerInputParameter("p_etag_no", int.Parse(tableVersion.VersionEtag, CultureInfo.InvariantCulture))
            .AddPgIntegerInputParameter("p_version", tableVersion.Version);
        try
        {
            await _db.Write.RunAsync(Queries.OrleansClusterInsertMember, prms, CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            _logger.LogWarning(ex, "Membership insert conflict for host:{Host} on Port:{Port}", entry.HostName, entry.SiloAddress.Endpoint.Port);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting row for host:{Host} on Port:{Port}", entry.HostName, entry.SiloAddress.Endpoint.Port);
            throw;
        }
    }

    /// <summary>
    /// The SQLSTATE the membership write procedures raise to signal a table/row etag conflict (or, for inserts, an
    /// already-existing silo row). Orleans expects such conflicts to be reported as a false result.
    /// </summary>
    internal const string ConcurrencyConflictSqlState = "AS001";

    /// <summary>
    /// True only for the procedure-raised concurrency signal; connection, timeout and other PostgreSQL errors are not conflicts.
    /// </summary>
    internal static bool IsConcurrencyConflict(Exception ex)
        => ex is PostgresException pgEx && pgEx.SqlState == ConcurrencyConflictSqlState;

    /// <summary>
    /// Atomically reads the full content of the Membership Table: the version, member and suspect rows come from one
    /// REPEATABLE READ snapshot.
    /// </summary>
    public async Task<MembershipTableData> ReadAll()
    {
        try
        {
            var prms = new ParameterCollection().AddPgVarcharInputParameter("p_cluster_id", _clusterId, 150);
            return await ReadSnapshotAsync(Queries.OrleansClusterMemberReadAll, prms, rdr => ReadAllFromReader(rdr, _logger));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading all membership entries.");
            throw;
        }
    }

    /// <summary>
    /// Maps the three result sets of the ReadAll query (version row; member rows; suspect rows) to membership-table data.
    /// </summary>
    internal static MembershipTableData ReadAllFromReader(DbDataReader rdr, ILogger logger)
    {
        var list = new List<Tuple<MembershipEntry, string>>();
        if (!rdr.Read())
        {
            throw new OrleansException("The membership version table was not found.");
        }
        var versionETagNo = rdr.GetInt32(1);
        var tv = new TableVersion(rdr.GetInt32(2), versionETagNo.ToString(CultureInfo.InvariantCulture));
        if (!rdr.NextResult())
        {
            logger.LogError("Member table query did not result expected Members result.");
            return new MembershipTableData(tv);
        }

        var members = new Dictionary<int, MembershipEntry>();
        while (rdr.Read())
        {
            var me = new MembershipEntry()
            {
                SiloAddress = SiloAddress.New(new IPEndPoint(new IPAddress((byte[])rdr.GetValue(1)), rdr.GetInt32(2)), rdr.GetInt32(3)),
                SiloName = rdr.GetString(4),
                HostName = rdr.GetString(5),
                Status = (SiloStatus)rdr.GetInt16(6),
                ProxyPort = rdr.GetInt32(7),
                StartTime = UtcTime.ToUtc(rdr.GetDateTime(8)),
                IAmAliveTime = UtcTime.ToUtc(rdr.GetDateTime(9))
            };
            // A member's etag is the table etag of the write that last changed that row, so it may lag the table etag.
            var memberETagNo = rdr.GetInt32(10);
            var memberVersion = rdr.GetInt32(11);
            if (memberVersion > tv.Version)
            {
                logger.LogError($"Member Version {memberVersion} is greater than VersionTable version number {tv.Version}. This may indicate data corruption.");
            }
            members.Add(rdr.GetInt32(0), me);
            list.Add(new Tuple<MembershipEntry, string>(me, memberETagNo.ToString(CultureInfo.InvariantCulture)));
        }
        if (!rdr.NextResult())
        {
            logger.LogError("Member table query did not result expected Suspects result.");
            return new MembershipTableData(list, tv);
        }
        while (rdr.Read())
        {
            if (!members.TryGetValue(rdr.GetInt32(0), out var member))
            {
                logger.LogError($"Member Suspect record (id: {rdr.GetInt32(0)}) refers to a member record which doesn't exist (members collection count:{members.Count}).");
            }
            else
            {
                member.AddSuspector(SiloAddress.New(new IPEndPoint(new IPAddress((byte[])rdr.GetValue(1)), rdr.GetInt32(2)), rdr.GetInt32(3)), UtcTime.ToUtc(rdr.GetDateTime(4)));
            }
        }

        return new MembershipTableData(list, tv);
    }

    /// <summary>
    /// Atomically reads the Membership Table information about a given silo: its row, the table version and its suspect
    /// rows come from one REPEATABLE READ snapshot.
    /// </summary>
    public async Task<MembershipTableData> ReadRow(SiloAddress key)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));

        var prms = new ParameterCollection()
            .AddPgVarcharInputParameter("p_cluster_id", _clusterId, 150)
            .AddPgByteaInputParameter("p_address", key.Endpoint.Address.GetAddressBytes())
            .AddPgIntegerInputParameter("p_port", key.Endpoint.Port)
            .AddPgIntegerInputParameter("p_generation", key.Generation);
        return await ReadSnapshotAsync(Queries.OrleansClusterMemberReadRow, prms, rdr => ReadRowFromReader(key, rdr, _logger));
    }

    /// <summary>
    /// Maps the two result sets of the ReadRow query (the addressed member row joined to the version row; its suspect rows) to membership-table data.
    /// </summary>
    internal static MembershipTableData ReadRowFromReader(SiloAddress key, DbDataReader rdr, ILogger logger)
    {
        var list = new List<Tuple<MembershipEntry, string>>();
        if (!rdr.Read())
        {
            throw new OrleansException("The requested membership record was not found.");
        }
        var versionETagNo = rdr.GetInt32(9);
        var tv = new TableVersion(rdr.GetInt32(10), versionETagNo.ToString(CultureInfo.InvariantCulture));

        var me = new MembershipEntry()
        {
            SiloAddress = key,
            SiloName = rdr.GetString(0),
            HostName = rdr.GetString(1),
            Status = (SiloStatus)rdr.GetInt16(2),
            ProxyPort = rdr.GetInt32(3),
            StartTime = UtcTime.ToUtc(rdr.GetDateTime(4)),
            IAmAliveTime = UtcTime.ToUtc(rdr.GetDateTime(5))
        };
        // Ordinal 6 is the version row's timestamp; the member etag and version follow it.
        // A member's etag is the table etag of the write that last changed that row, so it may lag the table etag.
        var memberETagNo = rdr.GetInt32(7);
        var memberVersion = rdr.GetInt32(8);
        if (memberVersion > tv.Version)
        {
            logger.LogError($"Member Version {memberVersion} is greater than VersionTable version number {tv.Version}. This may indicate data corruption.");
        }
        list.Add(new Tuple<MembershipEntry, string>(me, memberETagNo.ToString(CultureInfo.InvariantCulture)));

        if (!rdr.NextResult())
        {
            logger.LogError("Member table query did not result expected Suspects result.");
            return new MembershipTableData(list, tv);
        }
        while (rdr.Read())
        {
            me.AddSuspector(SiloAddress.New(new IPEndPoint(new IPAddress((byte[])rdr.GetValue(0)), rdr.GetInt32(1)), rdr.GetInt32(2)), UtcTime.ToUtc(rdr.GetDateTime(3)));
        }
        return new MembershipTableData(list, tv);
    }

    /// <summary>
    /// Runs a multi-statement read inside one REPEATABLE READ transaction, so every result set reflects the same snapshot.
    /// </summary>
    private async Task<MembershipTableData> ReadSnapshotAsync(Query query, DbParameterCollection prms, Func<DbDataReader, MembershipTableData> map)
    {
        await using var cnn = new NpgsqlConnection(_db.Read.ConnectionString);
        await cnn.OpenAsync();
        await using var tx = await cnn.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using var cmd = new NpgsqlCommand(query.Sql, cnn, tx) { CommandType = query.Type };
        foreach (NpgsqlParameter prm in prms)
        {
            cmd.Parameters.Add(prm.Clone());
        }
        MembershipTableData result;
        await using (var rdr = await cmd.ExecuteReaderAsync())
        {
            result = map(rdr);
        }
        await tx.CommitAsync();
        return result;
    }

    public Task UpdateIAmAlive(MembershipEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry, nameof(entry));

        var prms = new ParameterCollection()
            .AddPgTimestampTzInputParameter("p_i_am_alive_time", UtcTime.ToUtcOffset(entry.IAmAliveTime))
            .AddPgVarcharInputParameter("p_cluster_id", _clusterId, 150)
            .AddPgByteaInputParameter("p_address", entry.SiloAddress.Endpoint.Address.GetAddressBytes())
            .AddPgIntegerInputParameter("p_port", entry.SiloAddress.Endpoint.Port)
            .AddPgIntegerInputParameter("p_generation", entry.SiloAddress.Generation);
        try
        {
            return _db.Write.RunAsync(Queries.OrleansClusterUpdateIAmAliveTime, prms, CancellationToken.None);
        }
        catch
        {
            _logger.LogError("Error updating IAmAliveTime for entry: {Entry}", entry);
            throw;
        }
    }

    /// <summary>
    /// Atomically tries to update the MembershipEntry for one silo and also update the TableVersion.
    /// The procedure succeeds only when the stored table etag equals the TableVersion's etag and the member row's stored etag
    /// equals <paramref name="etag"/>; it then advances the table etag, stamps it on the member row and replaces the row's
    /// suspicions. On any failure nothing changes.
    /// </summary>
    /// <param name="entry">MembershipEntry to be updated.</param>
    /// <param name="etag">The etag for the given MembershipEntry.</param>
    /// <param name="tableVersion">The new TableVersion for this table, along with its etag.</param>
    /// <returns>True if the update operation succeeded and false otherwise.</returns>
    public async Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion)
    {
        ArgumentNullException.ThrowIfNull(entry, nameof(entry));
        ArgumentNullException.ThrowIfNull(etag, nameof(etag));
        ArgumentNullException.ThrowIfNull(tableVersion, nameof(tableVersion));

        var suspects = BuildSuspectArrays(entry.SuspectTimes);

        var prms = new ParameterCollection()
            .AddPgVarcharInputParameter("p_cluster_id", _clusterId, 150)
            .AddPgSmallintInputParameter("p_status", (short)entry.Status)
            .AddPgIntegerInputParameter("p_proxy_port", entry.ProxyPort)
            .AddPgTimestampTzInputParameter("p_i_am_alive_time", UtcTime.ToUtcOffset(entry.IAmAliveTime))
            .AddPgByteaInputParameter("p_address", entry.SiloAddress.Endpoint.Address.GetAddressBytes())
            .AddPgIntegerInputParameter("p_port", entry.SiloAddress.Endpoint.Port)
            .AddPgIntegerInputParameter("p_generation", entry.SiloAddress.Generation)
            .AddPgArrayInputParameter("p_suspect_addresses", suspects.Addresses, NpgsqlDbType.Bytea)
            .AddPgArrayInputParameter("p_suspect_ports", suspects.Ports, NpgsqlDbType.Integer)
            .AddPgArrayInputParameter("p_suspect_generations", suspects.Generations, NpgsqlDbType.Integer)
            .AddPgArrayInputParameter("p_suspect_times", suspects.Times, NpgsqlDbType.TimestampTz)
            .AddPgIntegerInputParameter("p_member_etag_no", int.Parse(etag, CultureInfo.InvariantCulture))
            .AddPgIntegerInputParameter("p_etag_no", int.Parse(tableVersion.VersionEtag, CultureInfo.InvariantCulture))
            .AddPgIntegerInputParameter("p_version", tableVersion.Version);

        try
        {
            await _db.Write.RunAsync(Queries.OrleansClusterUpdateMember, prms, CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            // The procedure raises AS001 when the member row is missing or its etag/the TableVersion etag does not match.
            _logger.LogWarning(ex, "Membership update conflict for entry: {Entry}", entry);
            return false;
        }
        catch (Exception ex)
        {
            // Connection, timeout and other failures are not concurrency conflicts; Orleans must see them as faults.
            _logger.LogError(ex, "Error updating row for entry: {Entry}", entry);
            throw;
        }
    }

    /// <summary>
    /// The suspect list as four parallel arrays (one element per suspecting silo) for the update procedure to unnest.
    /// </summary>
    internal readonly record struct SuspectArrays(byte[][] Addresses, int[] Ports, int[] Generations, DateTime[] Times);

    /// <summary>
    /// Collapses the suspect list to one entry per suspecting silo (Address, Port, Generation), which is the primary key of
    /// the suspicion table. When the same silo reported more than once, the LATEST report wins: Orleans counts a suspicion
    /// vote only while it is fresh, and a silo that re-votes replaces its earlier vote. Times are normalized to UTC.
    /// </summary>
    internal static SuspectArrays BuildSuspectArrays(IEnumerable<Tuple<SiloAddress, DateTime>>? suspectTimes)
    {
        var latest = new Dictionary<string, (byte[] Address, int Port, int Generation, DateTime Time)>(StringComparer.Ordinal);
        foreach (var susp in suspectTimes ?? [])
        {
            var addr = susp.Item1.Endpoint.Address.GetAddressBytes();
            var port = susp.Item1.Endpoint.Port;
            var gen = susp.Item1.Generation;
            var time = UtcTime.ToUtc(susp.Item2);
            var aKey = string.Concat(Convert.ToHexString(addr), ":", port.ToString(CultureInfo.InvariantCulture), ":", gen.ToString(CultureInfo.InvariantCulture));
            if (latest.TryGetValue(aKey, out var dup))
            {
                if (dup.Time < time) // keep the latest report: it is the suspector's current, freshest vote
                {
                    latest[aKey] = dup with { Time = time };
                }
                continue;
            }
            latest.Add(aKey, (addr, port, gen, time));
        }
        var rows = latest.Values.ToList();
        return new SuspectArrays(
            [.. rows.Select(r => r.Address)],
            [.. rows.Select(r => r.Port)],
            [.. rows.Select(r => r.Generation)],
            [.. rows.Select(r => r.Time)]);
    }
}
