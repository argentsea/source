# ArgentSea Orleans PostgreSQL Implementation

As a persistence provider for Microsoft Orleans, ArgentSea can save grain data into 3rd-normal-form tables. Grain state can then be queried *across* grains by the database: which users have specific attributes, how many orders are open per region, and so on. With an ArgentSea shard set, one Orleans cluster can keep each tenant's grains in that tenant's own database.

This package provides, over PostgreSQL:

* grain storage, in a single database or in a shard set;
* the cluster membership table, and the gateway list provider clients use to find silos;
* the reminder table, sharded with the grains it belongs to.

The SQL Server counterpart is ArgentSea.Orleans.Sql.

## Database setup

The package ships two idempotent scripts in its `sql` folder. Run each as the role that will own the objects:

* `common.psql` in the common database (cluster membership);
* `tenant.psql` in every shard database (reminders).

They create the tables in an `orleans` schema, read functions in `rdr` and write procedures in `wtr`, and three group roles without login rights. Every routine runs with its owner's rights and a fixed `search_path`, and PUBLIC cannot execute any of them:

* `orleans_reader` can call the `rdr` functions;
* `orleans_writer` can call the `rdr` functions and the `wtr` procedures;
* `orleans_client` can call only `rdr.orleans_cluster_gateway_list_v1`.

Give your login roles the group role they need:

```sql
CREATE ROLE silo_login LOGIN PASSWORD '...' IN ROLE orleans_writer;
CREATE ROLE client_login LOGIN PASSWORD '...' IN ROLE orleans_client;
```

## Silo setup

```C#
builder.Services.AddPgServices(builder.Configuration); // ArgentSea
builder.Services.AddSingleton<IFieldCodec<ShardKey<Guid>>, ShardKeyOrleansSerializer<Guid>>();
builder.Services.AddSingleton<IDeepCopier<ShardKey<Guid>>, ShardKeyOrleansSerializer<Guid>>();

builder.UseOrleans(silo =>
{
    silo.UseArgentSeaPgClustering();
    silo.AddArgentSeaPgDbOrleansGrainStorage("Common", o => o.Queries.Add("UserGrain", Queries.UserQueries));
    silo.AddArgentSeaPgShardOrleansGrainStorage("Tenant", o => o.Queries.Add("SubscriberGrain", Queries.SubscriberQueries));
    silo.UseArgentSeaPgReminderService(o => { });
});
```

The connections come from the `PgDbConnections` and `PgShardSets` configuration sections of ArgentSea.Pg. Clustering reads the database named by `OrleansDbPersistenceOptions.DatabaseKey` (default `Common`); reminders use the shard set named by `OrleansShardPersistenceOptions.ShardSetKey`.

Configuration-driven registration uses the provider types `ArgentSeaPg` (shard-set grain storage, clustering and reminders) and `ArgentSeaPgDb` (single-database grain storage):

```json
"Orleans": {
    "GrainStorage": {
        "Tenant": { "ProviderType": "ArgentSeaPg", "ShardSetKey": "Default" },
        "Common": { "ProviderType": "ArgentSeaPgDb", "DatabaseKey": "Common" }
    }
}
```

## Grain queries

Npgsql runs an ArgentSea `QueryProcedure` as `CALL`, which invokes a PostgreSQL procedure, and a procedure cannot return rows. So, for each grain state type:

* the **read** query is a statement file (`PgQueryStatement.Create(...)`) that selects the row, with `QueryResultFormat.ResultSet`, or a procedure whose INOUT parameters return the values, with `QueryResultFormat.OutputArgs`;
* the **write** and **clear** queries are usually procedures (`QueryProcedure`), whose parameter names match the Mapper attribute names.

## Client setup

```C#
builder.UseOrleansClient(client =>
{
    client.Services.AddPgServices(builder.Configuration);
    client.UseArgentSeaPgClustering(_ => { });
});
```

The gateway list provider reads `ClusterClientDbOptions`: `DatabaseKey` (default `Common`), an optional explicit `ConnectionString`, `MaxRefreshInterval`, and `GatewayListQuery`, which you leave unset to use the package's own query.

## Concurrency

Every insert or update of a silo's membership row checks the table's etag and advances it in the same statement, checks the row's own etag on update, and commits all of its changes or none. A write that loses to another silo's write is reported to Orleans as a conflict (SQLSTATE `AS001`), and Orleans re-reads and retries it.

## Timestamps

Times are stored as `timestamptz` in UTC. A `DateTime` of kind `Unspecified` is taken to be UTC already, a `Local` one is converted, and every value read back is UTC.

## Contributions

Contributions are very welcome.

## License

[MIT.](https://opensource.org/licenses/MIT)
