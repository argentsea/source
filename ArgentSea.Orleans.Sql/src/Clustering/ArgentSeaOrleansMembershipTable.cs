using System.Data.Common;
using System.Net;
using ArgentSea;
using ArgentSea.Sql;
using Microsoft.Extensions.Logging;
using System.Data;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Microsoft.Data.SqlClient.Server;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace ArgentSea.Orleans.Sql;

/// <summary>
/// This cannot use the ArgentSea MapTo attributes because the ReminderEntry class is sealed.
/// </summary>
public class ArgentSeaOrleansMembershipTable : IMembershipTable
{
    private readonly SqlDatabases.Database _db;
    private readonly string _clusterId;
    private readonly ILogger<ArgentSeaOrleansMembershipTable> _logger;
    private readonly string _dbKey;

    public ArgentSeaOrleansMembershipTable(SqlDatabases dbs, IOptions<ClusterOptions> clusterOptions, IOptions<OrleansDbPersistenceOptions> dbOptions, ILogger<ArgentSeaOrleansMembershipTable> logger)
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
        //The membership table already exists, via our setup SQL scripts; nothing to do here, other than maybe create a version record.

        // Initialize version table with clusterId (if needed), even before cluster is ready
        if (tryInitTableVersion)
        {
            var prms = new ParameterCollection()
                .AddSqlNVarCharInputParameter("@ClusterId", _clusterId, 150)
                .AddSqlIntInputParameter("@ETagNo", 0)
                .AddSqlIntInputParameter("@Version", 0);
            return _db.Write.RunAsync(Queries.OrleansClusterInsertMemberVersionKey, prms, CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Delete all dead silo entries older than <paramref name="beforeDate"/>
    /// </summary>
    public Task CleanupDefunctSiloEntries(DateTimeOffset beforeDate)
    {
        var prms = new ParameterCollection()
            .AddSqlNVarCharInputParameter("@ClusterId", _clusterId, 150)
            .AddSqlDateTime2InputParameter("BeforeDate", beforeDate.UtcDateTime);
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
            .AddSqlNVarCharInputParameter("@ClusterId", clusterId, 150);
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
    /// If operation succeeds, the following changes would be made to the table:
    /// 1) New MembershipEntry will be added to the table.
    /// 2) The newly added MembershipEntry will also be added with the new unique automatically generated eTag.
    /// 3) TableVersion.Version in the table will be updated to the new TableVersion.Version.
    /// 4) TableVersion etag in the table will be updated to the new unique automatically generated eTag.
    /// All those changes to the table, insert of a new row and update of the table version and the associated etags, should happen atomically, or fail atomically with no side effects.
    /// The operation should fail in each of the following conditions:
    /// 1) A MembershipEntry for a given silo already exist in the table
    /// 2) Update of the TableVersion failed since the given TableVersion etag (as specified by the TableVersion.VersionEtag property) did not match the TableVersion etag in the table.
    /// </summary>
    /// <param name="entry">MembershipEntry to be inserted.</param>
    /// <param name="tableVersion">The new TableVersion for this table, along with its etag.</param>
    /// <returns>True if the insert operation succeeded and false otherwise.</returns>
    public async Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion)
    {
        ArgumentNullException.ThrowIfNull(entry, nameof(entry));
        ArgumentNullException.ThrowIfNull(tableVersion, nameof(tableVersion));


        var prms = new ParameterCollection()
            .AddSqlNVarCharInputParameter("@ClusterId", _clusterId, 150)
            .AddSqlVarBinaryInputParameter("Address", entry.SiloAddress.Endpoint.Address.GetAddressBytes(), 16)
            .AddSqlIntInputParameter("@Port", entry.SiloAddress.Endpoint.Port)
            .AddSqlIntInputParameter("@Generation", entry.SiloAddress.Generation)
            .AddSqlNVarCharInputParameter("@SiloName", entry.SiloName, 150)
            .AddSqlNVarCharInputParameter("@HostName", entry.HostName, 150)
            .AddSqlSmallIntInputParameter("@Status", (short)entry.Status)
            .AddSqlIntInputParameter("@ProxyPort", entry.ProxyPort)
            .AddSqlDateTime2InputParameter("@StartTime", entry.StartTime)
            .AddSqlDateTime2InputParameter("@IAmAliveTime", entry.IAmAliveTime)
            .AddSqlIntInputParameter("@ETagNo", int.Parse(tableVersion.VersionEtag, CultureInfo.InvariantCulture))
            .AddSqlIntInputParameter("@Version", tableVersion.Version);
        try
        {
            await _db.Write.RunAsync(Queries.OrleansClusterInsertMemberKey, prms, CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            // The procedure raises error 51000 when the TableVersion etag does not match or the silo row already exists.
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
    /// The SQL error number the membership write procedures THROW to signal a TableVersion/row etag conflict
    /// (or, for inserts, an already-existing silo row). Orleans expects such conflicts to be reported as a false result.
    /// </summary>
    internal const int ConcurrencyConflictErrorNumber = 51000;

    /// <summary>
    /// In-memory OLTP errors raised when a simultaneous writer changed the same memory-optimized row first
    /// (41302 update conflict, 41305 repeatable-read validation, 41325 serializable validation, 41301 commit dependency).
    /// The write lost the race exactly as an etag mismatch would, so these are conflicts too.
    /// </summary>
    internal static readonly int[] WriteConflictErrorNumbers = [41302, 41305, 41325, 41301];

    /// <summary>
    /// True only for the procedure-raised concurrency signal or a memory-optimized write conflict; transient, connection
    /// and timeout failures are not conflicts.
    /// </summary>
    internal static bool IsConcurrencyConflict(Exception ex)
        => ex is SqlException sqlEx && sqlEx.Errors.Cast<SqlError>().Any(e => e.Number == ConcurrencyConflictErrorNumber || WriteConflictErrorNumbers.Contains(e.Number));


    /// <summary>
    /// Atomically reads the full content of the Membership Table.
    /// The returned MembershipTableData includes all MembershipEntry entry for all silos in the table and the 
    /// TableVersion for this table. The MembershipEntries and the TableVersion have to be read atomically.
    /// </summary>
    /// <returns>The membership information for a given table: MembershipTableData consisting multiple MembershipEntry entries and
    /// TableVersion, all read atomically.</returns>
    public async Task<MembershipTableData> ReadAll()
    {
        // This would be SO much easier with ArgentSea...
        try
        {
            using var cnn = new SqlConnection(_db.Read.ConnectionString);
            using var cmd = new SqlCommand(Queries.OrleansClusterMemberReadAllKey.Sql, cnn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddSqlNVarCharInputParameter("@ClusterId", _clusterId, 150);
            await cnn.OpenAsync();
            using var rdr = await cmd.ExecuteReaderAsync();
            return ReadAllFromReader(rdr, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading all membership entries.");
            throw;
        }
    }

    /// <summary>
    /// Maps the three result sets of rdr.OrleansClusterMemberReadAllKeyV1 (version row; member rows; suspect rows) to membership-table data.
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
            int memberETagNo = -1;
            int memberVersion = int.MaxValue;
            var me = new MembershipEntry()
            {
                SiloAddress = SiloAddress.New(new IPEndPoint(new IPAddress((byte[])rdr.GetValue(1)), rdr.GetInt32(2)), rdr.GetInt32(3)),
                SiloName = rdr.GetString(4),
                HostName = rdr.GetString(5),
                Status = (SiloStatus)rdr.GetInt16(6),
                ProxyPort = rdr.GetInt32(7),
                StartTime = rdr.GetDateTime(8),
                IAmAliveTime = rdr.GetDateTime(9)
            };
            // A member's etag is the table etag of the write that last changed that row, so it may lag the table etag.
            memberETagNo = rdr.GetInt32(10);
            memberVersion = rdr.GetInt32(11);
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
                member.AddSuspector(SiloAddress.New(new IPEndPoint(new IPAddress((byte[])rdr.GetValue(1)), rdr.GetInt32(2)), rdr.GetInt32(3)), rdr.GetDateTime(4));
            }
        }

        return new MembershipTableData(list, tv);
    }

    /// <summary>
    /// Atomically reads the Membership Table information about a given silo.
    /// The returned MembershipTableData includes one MembershipEntry entry for a given silo and the 
    /// TableVersion for this table. The MembershipEntry and the TableVersion have to be read atomically.
    /// </summary>
    /// <param name="key">The address of the silo whose membership information needs to be read.</param>
    /// <returns>The membership information for a given silo: MembershipTableData consisting one MembershipEntry entry and
    /// TableVersion, read atomically.</returns>
    public async Task<MembershipTableData> ReadRow(SiloAddress key)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));

        using var cnn = new SqlConnection(_db.Read.ConnectionString);
        using var cmd = new SqlCommand(Queries.OrleansClusterMemberReadRowKey.Sql, cnn);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddSqlNVarCharInputParameter("@ClusterId", _clusterId, 150)
            .AddSqlVarBinaryInputParameter("@Address", key.Endpoint.Address.GetAddressBytes(), 16)
            .AddSqlIntInputParameter("@Port", key.Endpoint.Port)
            .AddSqlIntInputParameter("@Generation", key.Generation);

        await cnn.OpenAsync();
        using var rdr = await cmd.ExecuteReaderAsync();
        return ReadRowFromReader(key, rdr, _logger);
    }

    /// <summary>
    /// Maps the two result sets of rdr.OrleansClusterMemberReadRowKeyV1 (the addressed member row joined to the version row; its suspect rows) to membership-table data.
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
            Status = (SiloStatus)rdr.GetInt16(2), // Status is smallint, as in ReadAll.
            ProxyPort = rdr.GetInt32(3),
            StartTime = rdr.GetDateTime(4),
            IAmAliveTime = rdr.GetDateTime(5)
        };
        // Ordinal 6 is OrleansClusterMemberVersion.Timestamp; the member etag and version follow it.
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
            me.AddSuspector(SiloAddress.New(new IPEndPoint(new IPAddress((byte[])rdr.GetValue(0)), rdr.GetInt32(1)), rdr.GetInt32(2)), rdr.GetDateTime(3));
        }
        return new MembershipTableData(list, tv);
    }

    public Task UpdateIAmAlive(MembershipEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry, nameof(entry));

        var prms = new ParameterCollection()
            .AddSqlDateTime2InputParameter("@IAmAliveTime", entry.IAmAliveTime)
            .AddSqlNVarCharInputParameter("@ClusterId", _clusterId, 150)
            .AddSqlVarBinaryInputParameter("@Address", entry.SiloAddress.Endpoint.Address.GetAddressBytes(), 16)
            .AddSqlIntInputParameter("@Port", entry.SiloAddress.Endpoint.Port)
            .AddSqlIntInputParameter("@Generation", entry.SiloAddress.Generation);
        try
        {
            return _db.Write.RunAsync(Queries.OrleansClusterUpdateIAmAliveTimeKey, prms, CancellationToken.None);
        }
        catch
        {
            _logger.LogError("Error updating IAmAliveTime for entry: {Entry}", entry);
            throw;
        }
    }
    private static SqlMetaData[] SuspectTableColumns => [
            new SqlMetaData("Address", SqlDbType.VarBinary, 150L),
            new SqlMetaData("Port", SqlDbType.Int),
            new SqlMetaData("Generation", SqlDbType.Int),
            new SqlMetaData("Timestamp", SqlDbType.DateTime2) ];

    /// <summary>
    /// Atomically tries to update the MembershipEntry for one silo and also update the TableVersion.
    /// If operation succeeds, the following changes would be made to the table:
    /// 1) The MembershipEntry for this silo will be updated to the new MembershipEntry (the old entry will be fully substituted by the new entry) 
    /// 2) The eTag for the updated MembershipEntry will also be eTag with the new unique automatically generated eTag.
    /// 3) TableVersion.Version in the table will be updated to the new TableVersion.Version.
    /// 4) TableVersion etag in the table will be updated to the new unique automatically generated eTag.
    /// All those changes to the table, update of a new row and update of the table version and the associated etags, should happen atomically, or fail atomically with no side effects.
    /// The operation should fail in each of the following conditions:
    /// 1) A MembershipEntry for a given silo does not exist in the table
    /// 2) A MembershipEntry for a given silo exist in the table but its etag in the table does not match the provided etag.
    /// 3) Update of the TableVersion failed since the given TableVersion etag (as specified by the TableVersion.VersionEtag property) did not match the TableVersion etag in the table.
    /// </summary>
    /// <param name="entry">MembershipEntry to be updated.</param>
    /// <param name="etag">The etag  for the given MembershipEntry.</param>
    /// <param name="tableVersion">The new TableVersion for this table, along with its etag.</param>
    /// <returns>True if the update operation succeeded and false otherwise.</returns>
    public async Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion)
    {
        ArgumentNullException.ThrowIfNull(entry, nameof(entry));
        ArgumentNullException.ThrowIfNull(etag, nameof(etag));
        ArgumentNullException.ThrowIfNull(tableVersion, nameof(tableVersion));

        var usualSuspects = BuildSuspectRecords(entry.SuspectTimes);

        var prms = new ParameterCollection()
            .AddSqlNVarCharInputParameter("@ClusterId", _clusterId, 150)
            .AddSqlSmallIntInputParameter("@Status", (short)entry.Status)
            .AddSqlIntInputParameter("@ProxyPort", entry.ProxyPort)
            .AddSqlDateTime2InputParameter("@IAmAliveTime", entry.IAmAliveTime)
            .AddSqlVarBinaryInputParameter("Address", entry.SiloAddress.Endpoint.Address.GetAddressBytes(), 16)
            .AddSqlIntInputParameter("@Port", entry.SiloAddress.Endpoint.Port)
            .AddSqlIntInputParameter("@Generation", entry.SiloAddress.Generation)
            .AddSqlTableValuedParameter("@SuspectTimes", usualSuspects.Count > 0 ? usualSuspects : null)
            .AddSqlIntInputParameter("@MemberETagNo", int.Parse(etag, CultureInfo.InvariantCulture))
            .AddSqlIntInputParameter("@ETagNo", int.Parse(tableVersion.VersionEtag, CultureInfo.InvariantCulture))
            .AddSqlIntInputParameter("@Version", tableVersion.Version);

        try
        {
            await _db.Write.RunAsync(Queries.OrleansClusterUpdateMemberKey, prms, CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            // The procedure raises error 51000 when the member row is missing or its etag/the TableVersion etag does not match.
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
    /// Collapses the suspect list to one table-valued-parameter row per suspecting silo (Address, Port, Generation),
    /// which is the primary key of the SuspicionType TVP and of the suspicion table.
    /// When the same silo reported more than once, the LATEST report wins: Orleans counts a suspicion vote only while it is
    /// fresh (younger than the death-vote expiration), and a silo that re-votes replaces its earlier vote, so the most recent
    /// timestamp is the one that represents the suspector's current vote.
    /// </summary>
    internal static List<SqlDataRecord> BuildSuspectRecords(IEnumerable<Tuple<SiloAddress, DateTime>>? suspectTimes)
    {
        var usualSuspects = new Dictionary<string, SqlDataRecord>(StringComparer.Ordinal);
        foreach (var susp in suspectTimes ?? [])
        {
            var addr = susp.Item1.Endpoint.Address.GetAddressBytes();
            var port = susp.Item1.Endpoint.Port;
            var gen = susp.Item1.Generation;
            // Content-equality key over the same (Address, Port, Generation) triple the TVP is keyed on.
            var aKey = string.Concat(Convert.ToHexString(addr), ":", port.ToString(CultureInfo.InvariantCulture), ":", gen.ToString(CultureInfo.InvariantCulture));
            if (usualSuspects.TryGetValue(aKey, out var dup))
            {
                if (dup.GetDateTime(3) < susp.Item2) // keep the latest report: it is the suspector's current, freshest vote
                {
                    dup.SetDateTime(3, susp.Item2);
                }
                continue;
            }
            var row = new SqlDataRecord(SuspectTableColumns);
            row.SetValue(0, addr);
            row.SetInt32(1, port);
            row.SetInt32(2, gen);
            row.SetDateTime(3, susp.Item2);
            usualSuspects.Add(aKey, row);
        }
        return [.. usualSuspects.Values];
    }

}