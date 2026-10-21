using System.Data.Common;
using System.Net;
using ArgentSea.Pg;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Orleans.Configuration;
using Orleans.Messaging;
using Orleans.Runtime.Configuration;

namespace ArgentSea.Orleans.Pg;

public class ArgentSeaGatewayListProvider : IGatewayListProvider
{
    private readonly TimeSpan _staleness;
    private readonly ILogger<ArgentSeaGatewayListProvider> _logger;
    private readonly string _clusterId;
    private readonly string _readConnectionString;
    private readonly Query _query;

    public ArgentSeaGatewayListProvider(IOptions<PgDbConnectionOptions> dbOptions, IOptions<PgGlobalPropertiesOptions> globalOptions, IOptions<ClusterClientDbOptions> clientDbOptions, IOptions<ClusterOptions> clusterOptions, ILogger<ArgentSeaGatewayListProvider> logger)
    {
        PgDbConnectionConfiguration? pgConnection = null;
        if (clientDbOptions.Value.ConnectionString is null)
        {
            foreach (var cnn in dbOptions.Value.PgDbConnections ?? [])
            {
                if (cnn.DatabaseKey == clientDbOptions.Value.DatabaseKey)
                {
                    pgConnection = cnn;
                    break;
                }
            }
            if (pgConnection is null)
            {
                throw new ArgumentException($"ArgentSea Orleans gateway database connection was not found for key “{clientDbOptions.Value.DatabaseKey}”.");
            }
            // As in DatabasesBase, fall back to the write connection when no read connection is configured.
            var readConnection = pgConnection.ReadConnection ?? pgConnection.WriteConnection ?? throw new ArgumentException($"ArgentSea DB database read connection is null for key “{clientDbOptions.Value.DatabaseKey}”.");
            readConnection.SetAmbientConfiguration(globalOptions.Value, null, null, pgConnection);
            _readConnectionString = readConnection.GetConnectionString(logger);
        }
        else
        {
            _readConnectionString = clientDbOptions.Value.ConnectionString;
        }

        _query = clientDbOptions.Value.GatewayListQuery ?? Queries.OrleansClusterGatewayList;
        _staleness = clientDbOptions.Value.MaxRefreshInterval;
        _clusterId = clusterOptions.Value.ClusterId ?? throw new ArgumentException($"The clusterId the persistance cluster using Db “{clientDbOptions.Value.DatabaseKey}” was not provided.");
        _logger = logger;
    }

    public TimeSpan MaxStaleness { get => _staleness; }

    public bool IsUpdatable => true;

    public async Task<IList<Uri>> GetGateways()
    {
        await using var cnn = new NpgsqlConnection(_readConnectionString);
        await using var cmd = new NpgsqlCommand(_query.Sql, cnn) { CommandType = _query.Type };
        cmd.Parameters.Add(new NpgsqlParameter("p_cluster_id", NpgsqlDbType.Varchar, 150) { Value = _clusterId });
        await cnn.OpenAsync();
        await using var rdr = await cmd.ExecuteReaderAsync();
        return ReadGatewayUris(rdr);
    }

    /// <summary>
    /// Maps every [address bytes, port, generation] row of the gateway-list query to a gateway URI, in result order.
    /// </summary>
    internal static List<Uri> ReadGatewayUris(DbDataReader rdr)
    {
        List<Uri> result = [];
        while (rdr.Read())
        {
            var ip = new IPAddress((byte[])rdr.GetValue(0));
            var silo = SiloAddress.New(new IPEndPoint(ip, rdr.GetInt32(1)), rdr.GetInt32(2));
            result.Add(silo.ToGatewayUri());
        }
        return result;
    }

    public Task InitializeGatewayListProvider()
    {
        _logger.LogDebug("Gateway list provider initialized.");
        return Task.CompletedTask;
    }
}
