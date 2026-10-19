---
title: Configuration
description: ArgentSea replaces connection strings with an inheritable property hierarchy, so hundreds of read and write endpoints are configured from a handful of values and secrets never touch source control.
---

# Configuration

ArgentSea does not use connection strings. Every connection attribute (server, database, login, timeouts, pooling, resilience settings) is a discrete property that you set once at whatever level it applies to, and every connection below that level inherits it. This page explains why that matters, how the levels nest, and how to keep passwords and environment-specific values out of your JSON files.

## Why not connection strings?

A sharded application with read scale-out needs a lot of connections. Twenty shards, each with a read and a write endpoint, is forty connections; add a few non-sharded databases and several environments (development, test, staging, production in two regions) and you are managing hundreds of strings, nearly all of which repeat the same login, database name, packet size and timeout. Rotating a password or renaming a server means touching every one of them.

Connection strings also cannot be assembled from more than one place. A password cannot come from a secrets store while the server name comes from an environment variable, because the string is opaque to the .NET configuration system.

The **Hereditary Configuration Hierarchy** solves both problems. Because each attribute is a separate configuration key, configuration volume scales with what *differs* between connections rather than with how many there are, and the .NET configuration system can merge each key from whichever provider manages it best: JSON files, User Secrets, Azure Key Vault, AWS Secrets Manager, environment variables or the command line. ArgentSea builds the real connection string for each endpoint from the resolved properties at run time.

## The inheritance levels

<ConfigHierarchy />

Every property can be set at any level. A value set on a child overrides the same value inherited from its parent; a value the child does not set is inherited.

For a **non-sharded database** there are three levels:

1. **Global** (`SqlGlobalSettings` / `PgGlobalSettings`): applies to every connection ArgentSea makes.
2. **Database** (an entry in `SqlDbConnections` / `PgDbConnections`, identified by `DatabaseKey`): applies to both of that database's endpoints.
3. **Endpoint** (`ReadConnection` / `WriteConnection`): applies to one endpoint only.

A **shard set** has five levels, because one extra level makes read scale-out manageable:

1. **Global**, as above.
2. **Shard set** (an entry in `SqlShardSets` / `PgShardSets`, identified by `ShardSetName`): applies to every shard in the set.
3. **Shard set `Read` / `Write`**: applies to every *read* endpoint (or every *write* endpoint) across all shards in the set. This is where a read-only login or `ApplicationIntent` goes, so you do not repeat it on every shard.
4. **Shard** (an entry in `Shards`, identified by `ShardId`): applies to both endpoints of that shard; typically the server address.
5. **Endpoint** (`ReadConnection` / `WriteConnection` on the shard): one endpoint of one shard.

Resilience settings (`RetryCount`, `RetryInterval`, `RetryLengthening`, `CircuitBreakerFailureCount`, `CircuitBreakerTestInterval`) are properties in the same hierarchy and inherit the same way; see [Resilience](/guides/resilience).

## A worked example

This configuration defines one catalog database and a shard set of tenant databases. Notice that `InitialCatalog` (or `Database`) and `UserName` are set **once**, on the shard set, and inherited by every endpoint; and that shard 1's read endpoint **overrides** the server once, pointing reads at a replica. There is no `Password` anywhere in this file on purpose; the next section shows where it goes.

::: code-group
```json [SQL Server]
{
  "SqlGlobalSettings": {
    "ApplicationName": "OrdersApi",
    "ConnectTimeout": 3
  },
  "SqlDbConnections": [
    {
      "DatabaseKey": "Catalog",
      "DataSource": "catalog-sql.internal",
      "InitialCatalog": "Catalog",
      "WindowsAuth": true
    }
  ],
  "SqlShardSets": [
    {
      "ShardSetName": "Tenants",
      "DefaultShardId": 0,
      "InitialCatalog": "Orders",
      "UserName": "orders_app",
      "Read": { "ApplicationIntent": "ReadOnly" },
      "Shards": [
        { "ShardId": 0, "DataSource": "tenants-0.internal" },
        {
          "ShardId": 1,
          "DataSource": "tenants-1.internal",
          "ReadConnection": { "DataSource": "tenants-1-replica.internal" }
        }
      ]
    }
  ]
}
```
```json [PostgreSQL]
{
  "PgGlobalSettings": {
    "ApplicationName": "OrdersApi",
    "Timeout": 3
  },
  "PgDbConnections": [
    {
      "DatabaseKey": "Catalog",
      "Host": "catalog-pg.internal",
      "Database": "catalog",
      "UserName": "catalog_app"
    }
  ],
  "PgShardSets": [
    {
      "ShardSetName": "Tenants",
      "DefaultShardId": 0,
      "Database": "orders",
      "UserName": "orders_app",
      "Read": { "UserName": "orders_reader" },
      "Shards": [
        { "ShardId": 0, "Host": "tenants-0.internal" },
        {
          "ShardId": 1,
          "Host": "tenants-1.internal",
          "ReadConnection": { "Host": "tenants-1-replica.internal" }
        }
      ]
    }
  ]
}
```
:::

Shard 0's read and write endpoints both resolve to `tenants-0.internal` with database `Orders`, login `orders_app`, application name `OrdersApi` and a 3-second connect timeout, none of which were written on the shard itself. Shard 1's write endpoint is the same shape; its read endpoint differs in exactly one property.

Property names mirror the driver's own connection-string keywords: `DataSource`, `InitialCatalog`, `ApplicationIntent`, `Encrypt`, `MultiSubnetFailover`, `PacketSize`, `MaxPoolSize` and so on for SQL Server; `Host`, `Port`, `Database`, `SslMode`, `SearchPath`, `Multiplexing` and so on for PostgreSQL. `UserName`, `Password` and `WindowsAuth` are common to both. The [glossary](/reference/glossary) and the source (`SqlConnectionPropertiesBase`, `PgConnectionPropertiesBase`) list the rest; you will rarely need more than a server, a database and a login.

`services.AddSqlServices(configuration)` (or `AddPgServices`) binds these sections and registers the `SqlDatabases` and `SqlShardSets` singletons (or `PgDatabases` and `PgShardSets`) that your data classes inject; see [Installation](/getting-started/installation).

## Secrets and environments

Because the hierarchy is bound through the ordinary .NET configuration system, any key can be supplied by any configuration provider, and later providers override earlier ones. That is how passwords stay out of source control and how the same build runs in every environment.

**Key naming.** Configuration providers that store flat key–value pairs address a JSON path with `:` separators, and array elements by index. The shard set above is element `0` of `SqlShardSets`, so a password shared by every shard in the set is:

```text
SqlShardSets:0:Password
```

Environment variables cannot contain `:`, so use a double underscore; the configuration system translates it. A password for one specific shard's endpoint goes deeper into the path:

::: code-group
```text [SQL Server]
SqlShardSets__0__Password=...            # every endpoint in the shard set
SqlShardSets__0__Shards__1__Password=... # both endpoints of shard 1 only
SqlDbConnections__0__Password=...        # the Catalog database
```
```text [PostgreSQL]
PgShardSets__0__Password=...
PgShardSets__0__Shards__1__Password=...
PgDbConnections__0__Password=...
```
:::

Azure Key Vault secret names cannot contain `:` either; its configuration provider accepts `--` instead (`SqlShardSets--0--Password`).

**During development**, put the same keys in User Secrets. Because `secrets.json` is JSON, mirror the array shape and leave the other elements empty so the indexes line up:

```json
{
  "SqlShardSets": [
    { "Password": "..." }
  ],
  "SqlDbConnections": [
    { "Password": "..." }
  ]
}
```

**Environment-specific values** (server names, database names) work the same way: keep the structure and the application-level settings in `appsettings.json`, and let each environment supply `SqlShardSets__0__Shards__1__DataSource` and friends from environment variables, a deployment pipeline or a secrets store. No file transforms are needed.

::: warning Arrays are positional
Options arrays bind by index, not by key. The index of a shard set or database is its position in the configuration, and that position must agree across every provider that contributes to it. Verify `DatabaseKey` and `ShardSetName` in each element rather than assuming the order.
:::

::: tip A mis-shaped section fails silently
If a section name or property is misspelled, the options object binds to null or stays empty, and the only symptom is a warning in the log at startup and a missing database or shard set at run time. When something is unexpectedly absent, check the spelling of the section names first.
:::

## Read and write endpoints

Every database and every shard has a **read connection** and a **write connection**, and your code always says which one it wants (`databases["Catalog"].Read`, `shard.Write`). When both point at the same server, the usual starting point, you set nothing extra: both inherit the same values. Being explicit from day one means that adding a readable secondary or a replica later is a configuration change, not a code change.

- **SQL Server.** Set `ApplicationIntent` to `ReadOnly` in the shard set's `Read` section (or on a database's `ReadConnection`). An Always On availability group listener, or Azure SQL Database with read scale-out enabled, then routes those connections to a readable secondary without a different server name. If your secondary has its own address, override `DataSource` on the read endpoint instead, as shard 1 does above.
- **PostgreSQL.** Point the read endpoint's `Host` at a hot standby or read replica (Amazon RDS and Aurora, Azure Database for PostgreSQL and Google Cloud SQL all expose one). A different `UserName` for readers goes in the shard set's `Read` section.

Reads from a secondary may lag the primary slightly; that is a property of the replication topology, and ArgentSea never silently falls back from one endpoint to the other.

## The default shard

A shard set may declare a `DefaultShardId`. It identifies the shard this *client* should use when it creates new records, and it is exposed as `shardSet.DefaultShard`. Deploy the same application in several regions with a different `DefaultShardId` per region (an environment variable such as `SqlShardSets__0__DefaultShardId`) and each region's new customers land in a nearby database, while reads by shard key still reach every shard.

## Keys must match exactly

`SqlDatabases["Catalog"]` and `SqlShardSets["Tenants"]` are dictionary lookups on the `DatabaseKey` and `ShardSetName` values in your configuration. The match is exact: casing, spacing, accents and spelling. A key that is not found throws, so a typo surfaces the first time the collection is used rather than as a wrong-database bug. The same is true of `shardSet[shardId]` and the `ShardId` values; see [Sharding](/guides/sharding) for why consistent shard ids matter so much.

## Changing values at run time

The collections are built once at startup and their *layout* is fixed: adding or removing a database or shard needs a restart. The *values* are not fixed. Every connection property is a settable .NET property that raises a change notification; set a new `DataSource` on a shard, a shard set's `Read` section or the global settings, and every endpoint that inherits it rebuilds its connection string on its next use. Queries already running are unaffected. This is what lets you redirect traffic during a failover; ArgentSea provides the mechanism and leaves the decision to you, as [Resilience](/guides/resilience) explains.

**Next:** [Resilience](/guides/resilience)
