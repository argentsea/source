---
title: Orleans
description: Persist Microsoft Orleans grain state in ordinary relational tables, run clustering and reminders on SQL Server or PostgreSQL, and serve many tenant databases from one cluster.
---

# Orleans

ArgentSea is a storage, clustering and reminder provider for [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/). It stores grain state in third-normal-form tables instead of serialized blobs, so grain data can be queried, counted and aggregated across grains with plain SQL. It is also the only provider that lets a single Orleans cluster persist each tenant's grains in that tenant's own database.

## Why a different kind of provider

Orleans' standard storage providers serialize a grain's state into an opaque blob and write it to one row keyed by the grain id. That is fast to implement, but it has two costs. The data is invisible to SQL: you cannot ask "which subscribers have lapsed?" or "how many orders are open per region?" without activating every grain. And the blob format is coupled to the .NET type that produced it, so a renamed property or a changed serializer becomes a data migration.

ArgentSea takes the other route. For each grain state type you register three Query objects: a read, a write and a clear. When Orleans asks the provider to read state, ArgentSea derives the key parameters from the grain id, runs the read Query, and hydrates the state object with [the Mapper](/guides/mapping). Writes map the state's properties to parameters and run the write Query; clears run the clear Query. Your stored procedures own the table design, so grain state lives in whatever normalized shape you choose and is reportable, indexable and governable like any other data.

Because the whole stack is ArgentSea, you also get its [configuration hierarchy](/guides/configuration), [retry and circuit breaker](/guides/resilience) and [structured logging](/guides/logging) on every grain read and write.

## Two storage variants

Two packages implement the providers: `ArgentSea.Orleans.Sql` for SQL Server and `ArgentSea.Orleans.Pg` for PostgreSQL. They work the same way; the examples on this page use SQL Server, and [PostgreSQL](#postgresql) below lists what differs. Each package registers two storage providers on the silo builder. Each is named; the name is the storage name you reference from `[PersistentState]`.

- `AddArgentSeaDbOrleansGrainStorage("Common", …)` persists state in **one database**, found in `SqlDbConnections` by the `DatabaseKey` in its options. The provider name must equal that database key (the options validator enforces it).
- `AddArgentSeaShardOrleansGrainStorage("Tenant", …)` persists state in a **shard set**, named by `ShardSetKey` in its options. The grain key is a serialized `ShardKey`, so the shard id travels inside the grain id and ArgentSea routes every read and write to the right tenant database without any lookup.

The sharded variant is what lets one cluster serve many tenants: tenant 7's `SubscriberGrain` activations read and write `Tenant7`, tenant 8's read and write `Tenant8`, and the silos are shared.

```csharp
builder.UseOrleans(silo =>
{
    silo.AddArgentSeaDbOrleansGrainStorage("Common", o =>
    {
        o.DatabaseKey = "Common";
        o.Queries.Add("UserGrain", Queries.UserQueries);
    });
    silo.AddArgentSeaShardOrleansGrainStorage("Tenant", o =>
    {
        o.ShardSetKey = "Default";
        o.ValidateGrainKeys = true;
        o.Queries.Add("SubscriberGrain", Queries.SubscriberQueries);
    });
});
```

The options types are `OrleansDbPersistenceOptions` and `OrleansShardPersistenceOptions`. You can also register them the plain .NET Options way, `services.Configure<OrleansShardPersistenceOptions>("Tenant", o => …)` followed by `services.AddGrainStorage("Tenant", ArgentSeaGrainStorageFactory.CreateShards)`; the builder extensions above do exactly that for you.

Orleans' configuration-driven provider registration works too: set `Orleans:GrainStorage:{name}:ProviderType` and supply `ShardSetKey` (or `DatabaseKey`) in that section or in a top-level `OrleansData` section. Orleans resolves one builder per provider type, so each storage kind has its own type. Shard sets, the common case, take the plain name:

| Provider type | Package | Registers |
|---|---|---|
| `ArgentSeaSql` | `ArgentSea.Orleans.Sql` | shard-set grain storage (`ShardSetKey`), clustering, reminders |
| `ArgentSeaSqlDb` | `ArgentSea.Orleans.Sql` | single-database grain storage (`DatabaseKey`) |
| `ArgentSeaPg` | `ArgentSea.Orleans.Pg` | shard-set grain storage (`ShardSetKey`), clustering, reminders |
| `ArgentSeaPgDb` | `ArgentSea.Orleans.Pg` | single-database grain storage (`DatabaseKey`) |

```json
"Orleans": {
  "GrainStorage": {
    "Tenant": { "ProviderType": "ArgentSeaSql", "ShardSetKey": "Default" },
    "Common": { "ProviderType": "ArgentSeaSqlDb", "DatabaseKey": "Common" }
  }
}
```

## Query definitions per grain type

Each entry in `Queries` is an `OrleansDbQueryDefinitions` or `OrleansShardQueryDefinitions` struct: the state name, the read Query, how the read returns its data (`QueryResultFormat.ResultSet` for a data reader, `QueryResultFormat.OutputArgs` for output parameters), the write Query and the clear Query. Any of the three can be a `QueryProcedure` or a `Lazy<QueryStatement>` [statement file](/guides/queries).

```csharp
public static class Queries
{
    public static QueryProcedure GetSubscriber => new("ws.ReadSubscriberV1", new[] { "SubscriberId" });
    public static QueryProcedure WriteSubscriber => new("ws.WriteSubscriberV1");
    public static QueryProcedure ClearSubscriber => new("ws.ClearSubscriberV1", new[] { "SubscriberId" });

    public static OrleansShardQueryDefinitions SubscriberQueries =
        new("SubscriberGrain", GetSubscriber, QueryResultFormat.ResultSet, WriteSubscriber, ClearSubscriber);
}
```

The dictionary key is the **state name** Orleans passes to the provider: the first argument of `[PersistentState("SubscriberGrain", "Tenant")]`, or `"state"` for a grain that derives from `Grain<TState>`. A grain whose state name has no registration fails at first use with `OrleansQueryNotProvidedException`, so register every state type up front.

When the read Query returns no row, the provider does not fail. It creates default state: if the state type implements `IActivator<TModel>` its static factory is called; otherwise the type is default-constructed and its key property is set from the grain id. `RecordExists` is `false` until the first write.

## The grain state model

Grain state is an ordinary ArgentSea model. For the sharded provider it needs one non-nullable `ShardKey` property carrying a `MapSqlShardKey` attribute; marking it as the record identifier (the trailing `true`) makes the choice explicit when a model has several keys. Implementing `IKeyedModel<T>` is optional but unlocks the key helpers described in [Sharding](/guides/sharding).

```csharp
public class SubscriberState : IKeyedModel<int>
{
    [MapSqlShardKey("@SubscriberId", true)]
    [MapToSqlInt("@SubscriberId")]
    public ShardKey<int> Key { get; set; }

    [MapToSqlNVarChar("@Name", 255)]
    public string Name { get; set; } = string.Empty;

    [MapToSqlDateTime2("@Created")]
    public DateTime Created { get; set; }
}

public class SubscriberGrain(
    [PersistentState("SubscriberGrain", "Tenant")] IPersistentState<SubscriberState> state)
    : Grain, ISubscriberGrain
{
    public Task<string> GetName() => Task.FromResult(state.State.Name);

    public async Task Rename(string name)
    {
        state.State.Name = name;
        await state.WriteStateAsync();
    }
}
```

The grain id is the shard key's UTF-8 form. To address a subscriber from a client, build the key and use it as a string grain key: `grainFactory.GetGrain<ISubscriberGrain>(Encoding.UTF8.GetString(key.ToUtf8().Span))`. With `ValidateGrainKeys` on (the default), a write or clear whose model key disagrees with the grain id throws rather than writing to the wrong shard. For the single-database provider the grain id is built from the properties flagged as record identifiers instead; `SpanParser.GetDbGrainId(model, logger)` produces it.

::: tip Shard keys in grain messages
Orleans must know how to serialize `ShardKey` values that appear in grain interfaces and state. Register `ShardKeyOrleansSerializer<T>` for every key shape you use, on silos and clients alike:

```csharp
services.AddSingleton<IFieldCodec<ShardKey<int>>, ShardKeyOrleansSerializer<int>>();
services.AddSingleton<IDeepCopier<ShardKey<int>>, ShardKeyOrleansSerializer<int>>();
```

Two-, three- and four-part keys have matching generic serializers.
:::

## Clustering and reminders

ArgentSea also provides the cluster membership table and the gateway list the client uses to find silos, both backed by stored procedures in the common database. On the silo, `silo.UseArgentSeaClustering()` binds `ClusterOptions` and registers the membership table; it reads the database named by `OrleansDbPersistenceOptions.DatabaseKey` (default `Common`).

On the client, register the gateway list provider and point it at the same database through `ClusterClientDbOptions`: `DatabaseKey` (default `Common`), `GatewayListQuery` and `MaxRefreshInterval` (default 60 seconds). Leave `GatewayListQuery` unset to use the package's own query (`rdr.OrleansClusterGatewayListV1` on SQL Server, `rdr.orleans_cluster_gateway_list_v1` on PostgreSQL); set it only if you replace that routine. A `clientBuilder.UseArgentSeaClustering(…)` extension exists as well.

```csharp
builder.UseOrleansClient(client =>
{
    client.Configure<ClusterOptions>(builder.Configuration.GetSection("OrleansConfig"));
    client.Services.AddSqlServices(builder.Configuration);
    client.Services.Configure<ClusterClientDbOptions>(o => o.MaxRefreshInterval = TimeSpan.FromMinutes(2));
    client.Services.AddSingleton<IGatewayListProvider, ArgentSeaGatewayListProvider>();
});
```

Reminders come from `silo.UseArgentSeaReminderService(o => { })`, which takes Orleans' own `ReminderOptions`. The reminder table lives in the shard set named by `OrleansShardPersistenceOptions.ShardSetKey`, and each reminder is stored in the shard its grain key resolves to, so a tenant's reminders stay with the tenant's data.

Two scripts in the `ArgentSea.Orleans.Sql` source create everything these providers need, each with a `rdr` schema for read procedures and a `wtr` schema for write procedures: run `common.sql` against the common database (cluster membership tables and procedures) and `tenant.sql` against every shard (the reminder table and procedures). Grant the read login EXECUTE on `rdr` and the write login EXECUTE on `wtr`.

The membership table uses optimistic concurrency as Orleans requires: every insert or update of a silo's row checks the table's etag and advances it in the same statement, also checks the row's own etag on update, and commits all of its changes or none. A write that loses to another silo's write is reported to Orleans as a conflict, and Orleans re-reads and retries it.

## PostgreSQL

`ArgentSea.Orleans.Pg` provides the same storage, clustering and reminder providers over PostgreSQL. Register them with the `Pg`-named extensions, which never collide with the SQL Server ones:

```csharp
builder.Services.AddPgServices(builder.Configuration);

builder.UseOrleans(silo =>
{
    silo.UseArgentSeaPgClustering();
    silo.AddArgentSeaPgDbOrleansGrainStorage("Common", o => o.Queries.Add("UserGrain", Queries.UserQueries));
    silo.AddArgentSeaPgShardOrleansGrainStorage("Tenant", o => o.Queries.Add("SubscriberGrain", Queries.SubscriberQueries));
    silo.UseArgentSeaPgReminderService(o => { });
});
```

Clients use `clientBuilder.UseArgentSeaPgClustering(…)`, which registers the PostgreSQL `ArgentSeaGatewayListProvider` (namespace `ArgentSea.Orleans.Pg`). Configuration-driven hosts use the provider types `ArgentSeaPg` and `ArgentSeaPgDb` from the table above.

**Grain queries.** Npgsql runs a `QueryProcedure` as `CALL`, which invokes a PostgreSQL procedure, and a procedure cannot return rows. So on PostgreSQL:

- the **read** Query is either a [statement file](/guides/queries) (`QueryStatement`) that selects the row, with `QueryResultFormat.ResultSet`, or a procedure whose INOUT parameters return the values, with `QueryResultFormat.OutputArgs`;
- the **write** and **clear** Queries are usually procedures (`QueryProcedure`), named in lower case, whose parameter names match the Mapper attribute names.

```csharp
public class SubscriberState
{
    [MapPgShardKey("subscriber_id")]
    [MapToPgInteger("subscriber_id")]
    public ShardKey<int> Key { get; set; }

    [MapToPgVarchar("name", 255)]
    public string Name { get; set; } = string.Empty;
}

public static class Queries
{
    private static readonly Lazy<QueryStatement> ReadSubscriber = PgQueryStatement.Create("ReadSubscriber"); // SQL/ReadSubscriber.psql
    public static QueryProcedure WriteSubscriber => new("ws.write_subscriber");
    public static QueryProcedure ClearSubscriber => new("ws.clear_subscriber");

    public static OrleansShardQueryDefinitions SubscriberQueries =
        new("SubscriberGrain", ReadSubscriber, QueryResultFormat.ResultSet, WriteSubscriber, ClearSubscriber);
}
```

**Schema scripts.** The package ships `common.psql` (cluster membership) and `tenant.psql` (reminders) in its `sql` folder. Run `common.psql` in the common database and `tenant.psql` in every shard database, as the role that will own the objects. Each script is idempotent and runs as one command (for example `psql -f common.psql`). They create the tables in an `orleans` schema, read functions in `rdr` and write procedures in `wtr`, and the group roles `orleans_reader`, `orleans_writer` and `orleans_client` (without login rights). Grant those to your login roles:

```sql
CREATE ROLE silo_login LOGIN PASSWORD '…' IN ROLE orleans_writer;
CREATE ROLE client_login LOGIN PASSWORD '…' IN ROLE orleans_client;
```

Every routine runs with its owner's rights (`SECURITY DEFINER`) and a fixed `search_path`, and PUBLIC cannot execute any of them. `orleans_reader` can call the `rdr` functions, `orleans_writer` the `rdr` functions and the `wtr` procedures, and `orleans_client` only the gateway list. No role can touch the tables directly.

**Timestamps** are stored as `timestamptz` in UTC. A `DateTime` of kind `Unspecified` is taken to be UTC already, a `Local` one is converted, and every value read back is UTC.

## Upgrading from 2.0

- **Provider types.** The configuration-driven provider type `ArgentSea` is gone. Use `ArgentSeaSql` for shard-set grain storage, clustering and reminders, and `ArgentSeaSqlDb` for single-database grain storage. The old name resolved only to shard-set storage, so a `DatabaseKey` section silently became shard-set storage; it now reaches single-database storage.
- **Membership procedures.** `common.sql` replaces `wtr.OrleansClusterInsertMemberKeyV1` and `wtr.OrleansClusterUpdateMemberKeyV1` with V2 procedures that enforce the etag checks, and drops the V1 versions. A V1 silo writes the etag back without checking it, so V1 and V2 silos must not share a cluster: upgrade the whole cluster at once (stop it, run the new `common.sql`, start the upgraded silos). Later procedure versions keep V2's etag behaviour, so future upgrades can roll.
- **Gateway query.** `ClusterClientDbOptions.GatewayListQuery` no longer has a default in the shared options; leaving it unset now selects the package's own query.

## A complete silo

```csharp
using ArgentSea;
using ArgentSea.Orleans;
using ArgentSea.Orleans.Sql;
using Orleans.Configuration;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSqlServices(builder.Configuration);
builder.Services.AddSingleton<IFieldCodec<ShardKey<int>>, ShardKeyOrleansSerializer<int>>();
builder.Services.AddSingleton<IDeepCopier<ShardKey<int>>, ShardKeyOrleansSerializer<int>>();

builder.UseOrleans(silo =>
{
    silo.Configure<ClusterOptions>(builder.Configuration.GetSection("OrleansConfig"));
    silo.UseArgentSeaClustering();
    silo.AddArgentSeaDbOrleansGrainStorage("Common", o =>
    {
        o.DatabaseKey = "Common";
        o.Queries.Add("UserGrain", Queries.UserQueries);
    });
    silo.AddArgentSeaShardOrleansGrainStorage("Tenant", o =>
    {
        o.ShardSetKey = "Default";
        o.Queries.Add("SubscriberGrain", Queries.SubscriberQueries);
    });
    silo.UseArgentSeaReminderService(o => { });
});

await builder.Build().RunAsync();
```

The matching `appsettings.json` uses the same `SqlDbConnections` and `SqlShardSets` sections as any other ArgentSea host (see [Configuration](/guides/configuration)); passwords belong in User Secrets or a vault.

```json
{
  "OrleansConfig": { "ClusterId": "default", "ServiceId": "default" },
  "SqlDbConnections": [
    { "DatabaseKey": "Common", "DataSource": "sql.internal", "InitialCatalog": "Common" }
  ],
  "SqlShardSets": [
    {
      "ShardSetName": "Default",
      "DefaultShardId": 0,
      "DataSource": "sql.internal",
      "UserName": "orleansWriter",
      "Read": { "ApplicationIntent": "ReadOnly", "UserName": "orleansReader" },
      "Shards": [
        { "ShardId": 0, "InitialCatalog": "Tenant1" },
        { "ShardId": 1, "InitialCatalog": "Tenant2" }
      ]
    }
  ],
  "OrleansData": { "ShardSetKey": "Default", "DatabaseKey": "Common", "ValidateGrainKeys": true }
}
```

## Current limits

- **One provider package per project.** `ArgentSea.Orleans` is the abstract package (you do not reference it directly); reference `ArgentSea.Orleans.Sql` or `ArgentSea.Orleans.Pg`. The two can be loaded side by side without registration conflicts, but ArgentSea does not support mapping one model type for both database providers, and that combination is untested.
- **Grain keys are shard keys** for the sharded provider, and every key shape in use needs its serializer registered.
- **Membership and reminders use ADO.NET directly** rather than the Mapper, because Orleans' `MembershipEntry` and `ReminderEntry` are sealed and cannot carry mapping attributes. They depend on the versioned procedures from the two scripts.
- State wrapped in `Orleans.Concurrency.Immutable<T>` is not supported.

**Next:** [Platforms](/reference/platforms)
