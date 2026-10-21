using ArgentSea.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Messaging;
using Orleans.Runtime.Configuration;
using System.Data;
using System.Data.Common;
using System.Net;

namespace ArgentSea.Orleans.Sql;

public class ArgentSeaGatewayListProvider : IGatewayListProvider
{
    private readonly TimeSpan _staleness;
    private readonly ILogger<ArgentSeaGatewayListProvider> _logger;
    private readonly string _clusterId;
    private readonly string _readConnectionString;
    private readonly Query _query;

    public ArgentSeaGatewayListProvider(IOptions<SqlDbConnectionOptions> dbOptions, IOptions<SqlGlobalPropertiesOptions> globalOptions, IOptions<ClusterClientDbOptions> clientDbOptions, IOptions<ClusterOptions> clusterOptions, ILogger<ArgentSeaGatewayListProvider> logger)
    {
        SqlDbConnectionConfiguration? sqlConnection = null;
        if (clientDbOptions.Value.ConnectionString is null)
        {
            foreach (var cnn in dbOptions.Value.SqlDbConnections ?? [])
            {

                if (cnn.DatabaseKey == clientDbOptions.Value.DatabaseKey)
                {
                    sqlConnection = cnn;
                    break;
                }
            }
            if (sqlConnection is null)
            {
                throw new ArgumentException($"ArgentSea Orleans gateway database connection was not found for key “{clientDbOptions.Value.DatabaseKey}”.");
            }
            // As in DatabasesBase, fall back to the write connection when no read connection is configured.
            var readConnection = sqlConnection.ReadConnection ?? sqlConnection.WriteConnection ?? throw new ArgumentException($"ArgentSea DB database read connection is null for key “{clientDbOptions.Value.DatabaseKey}”.");
            readConnection.SetAmbientConfiguration(globalOptions.Value, null, null, sqlConnection);
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
        using var cnn = new SqlConnection(_readConnectionString);
        using var cmd = new SqlCommand(_query.Sql, cnn);
        cmd.CommandType = _query.Type;  //CommandType.StoredProcedure;
        var prm = new SqlParameter("@ClusterId", SqlDbType.NVarChar, 150)
        {
            Value = _clusterId
        };
        cmd.Parameters.Add(prm);
        await cnn.OpenAsync();
        using var rdr = await cmd.ExecuteReaderAsync();
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
