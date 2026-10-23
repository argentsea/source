---
title: What's new
description: What changed in ArgentSea 2, why it changed, and the exact code edits needed to migrate a version 1 application.
---

# What's new in ArgentSea 2

Version 2 is a modernization of a framework that ran in production for years. It moves to .NET 10, adds a Microsoft Orleans provider, and tightens the shard key model. Most of the public surface is unchanged; the breaking changes are listed below with the edit each one needs.

## The changes

### .NET 10 and current drivers

Version 2 targets `net10.0` only and drops .NET Standard. Dependencies move to Microsoft.Data.SqlClient 7.1.1, Npgsql 10.0.3, Microsoft.Orleans 10.4.0 and Polly 8.8.0 (as of 2.1.0). See [Platforms](/reference/platforms).

### Orleans support

New packages `ArgentSea.Orleans` (abstract) and `ArgentSea.Orleans.Sql` make ArgentSea the only Orleans storage provider that persists grain state in third-normal-form tables rather than serialized blobs, so grain state can be queried across grains with SQL, plus a membership table, gateway list provider and reminder table. Shard keys serialize as grain keys, so one cluster can serve many tenant databases. See [Orleans](/guides/orleans).

Version 2.1 adds `ArgentSea.Orleans.Pg`, the same providers over PostgreSQL. It also renames the configuration-driven Orleans provider types (`ArgentSeaSql` and `ArgentSeaSqlDb` replace `ArgentSea`) and makes the SQL Server membership table enforce its etags through new V2 procedures, which needs a whole-cluster upgrade. See [Upgrading from 2.0](/guides/orleans#upgrading-from-2-0).

### Instance hydration

Every mapping data method now has an overload whose first argument is an existing model instance to populate in place, instead of always constructing a new object. This was required for Orleans, which owns the state object, and it is useful whenever you want to refresh an object you already hold.

### Shard id is always `short`

The shard id type is no longer a generic parameter. Within one application the type never varied, yet every declaration had to restate it; `short` gives tens of thousands of shards in two bytes per persisted key.

### DataOrigin removed; key type metadata added

The DataOrigin character that tagged a key with its source table is gone. Serialized keys now carry compact type metadata, so a key deserialized as the wrong generic types fails, logging can print a key without knowing its types, and Orleans can parse a grain id into a key.

### Faster key serialization

Shard keys serialize to a binary form (`ToArray()`) and a UTF-8 form (`ToUtf8()`), both faster than the version 1 string path. The external string is XOR-obfuscated, prefixed with a 12-bit checksum and encoded in a URL-safe alphabet; a corrupted or edited value is rejected on parse.

### Statement-file queries

`QueryStatement.Create("GetCustomer")` loads SQL from a `.sql` or `.psql` file at first use, as an alternative to a stored procedure. This is the preferred form for PostgreSQL. See [Queries](/guides/queries).

### Mock results

Database connections, shards and shard sets carry a `MockResults` dictionary keyed by Query name; a matching name short-circuits the call and returns the stored object. See [Testing](/getting-started/testing).

### Structured logging with fixed event ids

Logging goes through precompiled `LoggerMessage` definitions with fixed, numbered event ids (connection string built, expression tree creation, mapper cache status, command executed, retries, circuit breaker and so on), so logs can be filtered and alerted on by id. An `ILogger` is required on every data method and Mapper call. See [Logging](/guides/logging).

### Polly 8

Retry and circuit breaking are built on Polly 8: a wait-and-retry policy for transient errors wrapped by a circuit breaker, one per read or write endpoint. Configuration keys are unchanged: `RetryCount`, `RetryInterval`, `RetryLengthening`, `CircuitBreakerFailureCount`, `CircuitBreakerTestInterval`. `Fibonacci` remains as an obsolete alias of `Lengthening`.

### Zero-row table-valued parameters

An empty or null list passed to `AddSqlTableValuedParameter` is sent as a null parameter, which SQL Server treats as an empty table. Callers that skipped the parameter or pre-checked `Count` can remove that code.

## Migrating from version 1

Work through these in order; the compiler will point you at most of them.

### 1. Retarget and update packages

Set `<TargetFramework>net10.0</TargetFramework>` and update `ArgentSea.Sql` or `ArgentSea.Pg` to 2.1.0. The core `ArgentSea` package comes along transitively.

### 2. Drop the shard id type parameter

Remove the first generic argument from every `ShardKey`, and the DataOrigin argument from every constructor.

```csharp
// Version 1
public ShardKey<byte, int> CustomerKey { get; set; }
var key = new ShardKey<byte, int>('c', 1, 1042);

// Version 2
public ShardKey<int> CustomerKey { get; set; }
var key = new ShardKey<int>(1, 1042);
```

Compound keys shift the same way: `ShardKey<byte, long, short>` becomes `ShardKey<long, short>`. Delete your `DataOrigins` class.

### 3. Replace the shard key attribute

The generic `MapShardKey` attribute with a DataOrigin character is replaced by provider-specific attributes that take only column names. The record id column keeps its own mapping attribute with the same name.

::: code-group
```csharp [SQL Server]
// Version 1
[MapShardKey('c', "@CustomerId")]
[MapToSqlInt("@CustomerId")]
public ShardKey<byte, int> CustomerKey { get; set; }

// Version 2
[MapSqlShardKey("@CustomerId")]
[MapToSqlInt("@CustomerId")]
public ShardKey<int> CustomerKey { get; set; }
```
```csharp [PostgreSQL]
// Version 1
[MapShardKey('c', "customerid")]
[MapToPgInteger("customerid")]
public ShardKey<byte, int> CustomerKey { get; set; }

// Version 2
[MapPgShardKey("customer_id")]
[MapToPgInteger("customer_id")]
public ShardKey<int> CustomerKey { get; set; }
```
:::

Keys that read the shard id from a column use the two-argument form, `[MapSqlShardKey("@CustomerShardId", "@CustomerId")]`; child columns follow in order.

### 4. Rename the parameter collection

`QueryParameterCollection` is now `ParameterCollection`. The extension methods on it are unchanged.

```csharp
var prms = new ParameterCollection()
    .AddSqlIntInputParameter("@CustomerId", customerId);
```

### 5. Update custom result handlers

Handlers passed to the `Query*` methods receive the model instance as a new first argument, to support hydration. Return the instance (or a new object) as before.

```csharp
// Version 1
static Customer Handle(short shardId, string sprocName, object arg,
    DbDataReader rdr, DbParameterCollection prms, string connectionDescription, ILogger logger)

// Version 2
static Customer Handle(Customer instance, short shardId, string sprocName, object arg,
    DbDataReader rdr, DbParameterCollection prms, string connectionDescription, ILogger logger)
```

### 6. Update batch steps that return a key

Batch steps that built a new `ShardKey` from a result column no longer take a DataOrigin: `batch.Add(Queries.CustomerSave, prms, "CustomerId")` for a key whose shard id is the batch's shard, or `batch.Add(query, prms, "ShardId", "CustomerId")` when the shard id is in a column.

### 7. Re-issue external keys if you stored them

The external string format changed with the removal of DataOrigin and the addition of the checksum. Keys you persisted or bookmarked in version 1 will not parse in version 2; regenerate them from the shard id and record id with `ToExternalString()`.

### 8. Check your logging

Every data method and Mapper call takes an `ILogger`, and logging is not optional. If any version 1 code passed `null` for it, inject a logger instead; the new fixed event ids are worth wiring into your log filters while you are there.

Nothing else should need attention: configuration sections, the `Queries` class convention, `SqlDatabases` / `SqlShardSets` and the fan-out methods keep their version 1 names.
