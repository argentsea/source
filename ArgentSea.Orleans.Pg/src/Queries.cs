namespace ArgentSea.Orleans.Pg;

/// <summary>
/// The routines installed by common.psql (cluster membership) and tenant.psql (reminders).
/// Writes are procedures, run as CALL through <see cref="QueryProcedure"/>; reads are functions, called through
/// <see cref="PgFunctionQuery"/> text. Parameter names match the routines' parameter names.
/// </summary>
public static class Queries
{
    #region Orleans Cluster Membership
    public static PgFunctionQuery OrleansClusterGatewayList => new(nameof(OrleansClusterGatewayList),
        "SELECT * FROM rdr.orleans_cluster_gateway_list_v1(@p_cluster_id);");

    /// <summary>Three result sets, as for SQL Server: the version row, the member rows, the suspect rows.</summary>
    public static PgFunctionQuery OrleansClusterMemberReadAll => new(nameof(OrleansClusterMemberReadAll),
        "SELECT * FROM rdr.orleans_cluster_version_read_v1(@p_cluster_id); " +
        "SELECT * FROM rdr.orleans_cluster_member_read_all_v1(@p_cluster_id); " +
        "SELECT * FROM rdr.orleans_cluster_suspicion_read_all_v1(@p_cluster_id);");

    /// <summary>Two result sets, as for SQL Server: the addressed member row joined to the version row, then its suspect rows.</summary>
    public static PgFunctionQuery OrleansClusterMemberReadRow => new(nameof(OrleansClusterMemberReadRow),
        "SELECT * FROM rdr.orleans_cluster_member_read_row_v1(@p_cluster_id, @p_address, @p_port, @p_generation); " +
        "SELECT * FROM rdr.orleans_cluster_suspicion_read_row_v1(@p_cluster_id, @p_address, @p_port, @p_generation);");

    public static QueryProcedure OrleansClusterInsertMemberVersion => new("wtr.orleans_cluster_insert_member_version_v1");

    public static QueryProcedure OrleansClusterInsertMember => new("wtr.orleans_cluster_insert_member_v1");

    public static QueryProcedure OrleansClusterUpdateMember => new("wtr.orleans_cluster_update_member_v1");

    public static QueryProcedure OrleansClusterUpdateIAmAliveTime => new("wtr.orleans_cluster_update_i_am_alive_time_v1");

    public static QueryProcedure OrleansClusterDeleteDefunctMembers => new("wtr.orleans_cluster_delete_defunct_members_v1");

    public static QueryProcedure OrleansClusterDelete => new("wtr.orleans_cluster_delete_v1");
    #endregion

    #region Orleans Reminders
    public static PgFunctionQuery OrleansReminderReadRow => new(nameof(OrleansReminderReadRow),
        "SELECT * FROM rdr.orleans_reminder_read_row_v1(@p_grain_key, @p_grain_type, @p_reminder_name);");

    public static PgFunctionQuery OrleansReminderReadRows => new(nameof(OrleansReminderReadRows),
        "SELECT * FROM rdr.orleans_reminder_read_rows_v1(@p_grain_key, @p_grain_type);");

    public static PgFunctionQuery OrleansReminderReadRangeRows => new(nameof(OrleansReminderReadRangeRows),
        "SELECT * FROM rdr.orleans_reminder_read_range_rows_v1(@p_begin_hash, @p_end_hash);");

    public static QueryProcedure OrleansReminderDeleteRow => new("wtr.orleans_reminder_delete_row_v1");

    public static QueryProcedure OrleansReminderUpsertRow => new("wtr.orleans_reminder_upsert_row_v1");
    #endregion
}
