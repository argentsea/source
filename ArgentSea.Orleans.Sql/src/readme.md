# ArgentSea Orleans SQL Implementation

As a persistance provider for Microsoft Orleans, ArgentSea can save grain data into 3rd-normal-form tables. 

This means that grain state can be queried *across* grains by the SQL engine. For example, if you have a grains that correspond to users, you now have the ability to query user data: which users have specific attributes, how many users meet a criteria, etc.

## Setup

Example appsettings.json sections in the Silos project:

```json
    "OrleansConfig": {
        "ClusterId": "default",
        "ServiceId": "default"
    },
    "SqlDbConnections": [
        {
            "DatabaseKey": "Common",
            "InitialCatalog": "Common"
        }
    ],
    "SqlShardSets": [
        {
            "ShardSetName": "Default",
            "DefaultShardId": 0,
            "UserName": "orleansWriter",
            "Read": {
                "ApplicationIntent": "ReadOnly",
                "UserName": "orleansReader"
            },
            "Shards": [
                {
                    "ShardId": 0,
                    "InitialCatalog": "Tenant1"
                },
                {
                    "ShardId": 1,
                    "InitialCatalog": "Tenant2"
                }
            ]
        }
    ],
    "OrleansData": {
        "ShardSetKey": "Default",
        "DatabaseKey": "Common",
        "ValidateGrainKeys": false
    },
```


An example startup file section:

```C#
builder.UseOrleans((context, siloBuilder) =>
{
    siloBuilder.Services.AddSqlServices(context.Configuration); //ArgentSea
    siloBuilder.Services.AddSingleton<IFieldCodec<ShardKey<Guid>>, ShardKeyOrleansSerializer>();
    siloBuilder.Services.AddSingleton<IDeepCopier<ShardKey<Guid>>, ShardKeyOrleansSerializer>();
    siloBuilder.Services.AddOptions();
    siloBuilder.Services.Configure<OrleansShardPersistenceOptions>(DataProviderConstants.Tenant, options =>
    {
        options.Queries.Add("Scale", ScaleGrainQueries.ScaleQueryDefinition);
    });
    siloBuilder.Services.Configure<OrleansDbPersistenceOptions>(DataProviderConstants.Common, options =>
    {
        //options.Queries.Add("Users", UserGrainQueries.UserQueryDefinition);
    });
});

```

## Configuring the Orleans Client (API)

Your Program.cs startup class should include:

```C#
builder.UseOrleansClient(client => {
    client.Configure<ClientMessagingOptions>(opts =>
    {
        opts.ResponseTimeoutWithDebugger = TimeSpan.FromSeconds(10);
    });
    client.Services.AddSingleton<IFieldCodec<ShardKey<Guid>>, ShardKeyOrleansSerializer>();
    client.Services.AddSingleton<IDeepCopier<ShardKey<Guid>>, ShardKeyOrleansSerializer>();
    client.Services.AddSingleton<IGatewayListProvider, ArgentSeaGatewayListProvider>();
});
```

You will also need to add to appsettings.json — or other configuration — information about how to connect to the Common database:
```json
    "ClusteringClientOptions": {
        "MaxRefreshInterval": "0.00:02:00",
        "ConnectionDatabase": "Common",
        "ProcedureName": "rdr.OrleansClusterGatewayListV1"
    }
```

## Contributions

Contributions are very welcome.

## License

[MIT.](https://opensource.org/licenses/MIT)

## Configuration-driven provider types

When Orleans providers are registered from configuration (`Orleans:{Kind}:{name}:ProviderType`), this package registers `ArgentSeaSql` for shard-set grain storage, clustering and reminders, and `ArgentSeaSqlDb` for single-database grain storage. (ArgentSea.Orleans.Pg registers `ArgentSeaPg` and `ArgentSeaPgDb`.)

## Upgrading from 2.0

* The provider type `ArgentSea` is gone. Use `ArgentSeaSql` or `ArgentSeaSqlDb` as above. The old name resolved only to shard-set storage, so single-database storage was unreachable from configuration.
* `common.sql` replaces `wtr.OrleansClusterInsertMemberKeyV1` and `wtr.OrleansClusterUpdateMemberKeyV1` with V2 procedures that enforce the membership etag checks (each write compares and advances the table etag in one statement, checks the member row's own etag on update, and commits all of its changes or none), and drops the V1 versions. V1 and V2 silos must not share a cluster, so upgrade the whole cluster at once: stop it, run the new `common.sql`, and start the upgraded silos.
* `ClusterClientDbOptions.GatewayListQuery` has no default in the shared options any more. Leave it unset to use this package's `rdr.OrleansClusterGatewayListV1`.