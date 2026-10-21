using ArgentSea;

/// <summary>
/// Configuration for cluster clients to connect to the database to fetch cluster info.
/// </summary>
public sealed record ClusterClientDbOptions
{
    /// <summary>
    /// The DatabaseKey for the DbConnection in the ArgentSea Db Connections array. Defaults to “Common”.
    /// </summary>
    public required string DatabaseKey { get; set; } = "Common";

    /// <summary>
    /// The connection string IF THE ARGENTSEA DBCOLLECTION IS NOT USED. Otherwise, Null will cause the system to use the DatabaseKey connect via the ArgentSea DbCollection.
    /// </summary>
    public string? ConnectionString { get; set; } = null;


    /// <summary>
    /// The query to retrieve the cluster data. Null (the default) uses the gateway-list query of the provider package in use
    /// (ArgentSea.Orleans.Sql or ArgentSea.Orleans.Pg), since the query shape is specific to each database platform.
    /// </summary>
    public Query? GatewayListQuery { get; set; } = null;

    /// <summary>
    /// How often to query for changes to the cluster set.
    /// </summary>
    public TimeSpan MaxRefreshInterval { get; set; } = TimeSpan.FromSeconds(60);
}
