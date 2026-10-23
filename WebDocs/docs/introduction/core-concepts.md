---
title: Core concepts
description: The eight ideas that make up ArgentSea's mental model, from the Databases and ShardSets collections to fan-out, resilience and batches.
---

# Core concepts

ArgentSea has a small vocabulary, and once you know it every page on this site reads easily. This page introduces each idea in the order you meet it in code. Each section links to the guide that goes deeper.

<ConceptGrid />

## Databases and shard sets

When you register ArgentSea at startup, it builds two singleton collections from configuration and makes them injectable.

**Databases** holds every non-sharded database, keyed by its `DatabaseKey`. **ShardSets** holds every shard set, keyed by its `ShardSetName`. A shard set is a group of databases with the same schema, each holding a distinct slice of one data set; a shard is one of those databases, identified by a 16-bit **shard id**. A tenant-per-database product is a shard set whose shards are tenants.

```csharp
public class OrderStore(SqlDatabases databases, SqlShardSets shardSets)
{
    private readonly SqlDatabases.Database _catalog = databases["Catalog"];
    private readonly SqlShardSets.ShardSet _orders = shardSets["Orders"];
}
```

Both collections are fixed once built. Keys must match exactly, including case. See [Configuration](/guides/configuration).

## Read and write connections

Every database and every shard exposes two connections, `Read` and `Write`. They are configured through the same hierarchy; when you have not configured them differently, both point at the same server.

The caller chooses which to use for every call. ArgentSea does not inspect your SQL to decide, and it does not fall back to the write connection when a read finds nothing. This discipline is what lets you point `Read` at a replica later without touching application code.

```csharp
var order = await _orders[key].Read.MapReaderAsync<Order>(Queries.GetOrder, prms, cancellation);
await _orders[key].Write.RunAsync(Queries.CloseOrder, prms, cancellation);
```

## Queries

A `Query` is the object you pass to every data method to say which SQL to run. It comes in two forms. A `QueryProcedure` names a stored procedure (or, on PostgreSQL, a function). A `QueryStatement` names a parameterized SQL file deployed in the application's `SQL` folder and loaded on first use. Either way, ArgentSea never generates SQL; the parameters you send and the result shape you expect are the contract.

By convention an application declares every query as a static property of one `Queries` class, so that your IDE's reference count tells you which SQL is still in use.

```csharp
public static class Queries
{
    public static QueryProcedure GetOrder => new("rdr.GetOrder", ["OrderId"]);
    public static QueryProcedure CloseOrder => new("wtr.CloseOrder", ["OrderId", "ClosedBy"]);
}
```

Parameters are declared in a free-standing `ParameterCollection` before any connection exists, then copied to each connection that runs the query. See [Queries](/guides/queries).

## The Mapper

The Mapper moves values between model properties and the database: into input parameters, out of output parameters, out of data-reader rows, and (on SQL Server) into table-valued parameter rows. You describe the mapping with attributes on your model.

```csharp
public readonly record struct Order
{
    [MapToSqlBigInt("@OrderId")]            public long OrderId { get; init; }
    [MapToSqlDateTime2("@Placed")]          public DateTime Placed { get; init; }
    [MapToSqlDecimal("@Total", 12, 2)]      public decimal Total { get; init; }
    [MapToSqlNVarChar("@Note", 500)]        public string? Note { get; init; }
    public List<OrderItem> Items { get; init; }   // filled from a second result set
}
```

The first time a model type is used, the Mapper reads its attributes by reflection and compiles expression trees into delegates; every later call runs that compiled code. The data methods whose names start with `Map` use it automatically: `MapReaderAsync`, `MapOutputAsync`, `MapListAsync`. See [Mapping](/guides/mapping).

## ShardKey

Once a data set spans databases, a record's own id is no longer enough to find it. `ShardKey<TRecord>` combines the shard id with the record key into one immutable value that carries its own routing information. Variants with two, three and four key columns cover compound keys.

```csharp
var key = new ShardKey<int>(shardId: 3, recordId: 1042);
var customer = await customers[key].Read.MapReaderAsync<Customer>(Queries.GetCustomer, prms, cancellation);

string external = key.ToExternalString();          // URL-safe, for routes and JSON
var same = ShardKey<int>.FromExternalString(external);
```

The Mapper builds shard keys directly from mapped columns, taking the shard id from the connection that produced the row or, for references to records on other shards, from a column. See [Sharding & tenanting](/guides/sharding).

## Fan-out

A shard set has three fan-out connections that run one query on every shard, or on a targeted subset, concurrently. `ReadAll` waits for every shard and returns all results as one list. `ReadFirst` returns the first non-null result and cancels the rest, which is how you find a user by login name when you do not yet know their shard. `Write` runs a command on every shard's write connection.

```csharp
var user = await users.ReadFirst.MapReaderAsync<User>(Queries.UserByLogin, prms, cancellation);
var admins = await users.ReadAll.MapListAsync<User>(Queries.UsersByRole, prms, cancellation);
```

Each shard runs on its own connection with its own copy of the parameters, under its own resilience policy. Result handlers therefore run on several threads at once; the Mapper's handlers are thread-safe, and yours must be too.

## Resilience

Every read and write endpoint owns a policy, built with Polly, that retries transient errors with a configurable backoff and wraps the retries in a circuit breaker. After a run of consecutive failures the breaker opens and calls fail fast instead of queuing behind a database that is not answering; after a test interval one call is let through to probe recovery. Defaults apply when nothing is configured, every retry and trip is logged, and the driver's own silent retries are turned off. See [Resilience](/guides/resilience).

## Batches

ArgentSea prefers transactions inside the database, where a stored procedure can hold a lock for microseconds rather than across network round trips. The one place it opens a client transaction is a **batch**: an ordered list of steps run on one connection, committed when all succeed and rolled back if any throws. Batches exist for sequences you cannot express in one procedure and for combining bulk loads with queries atomically. A shard-set batch runs an independent transaction per shard; there are no distributed transactions. See [Batches & transactions](/guides/batches).

## Putting it together

A request to load a customer's recent orders, in a sharded system, reads like this from end to end: parse the external key from the route into a `ShardKey`; index the shard set with it to reach the right shard; call `Read.MapReaderAsync<Customer, Order>` with the query and parameters; receive a `Customer` whose `Orders` list was filled from the second result set, mapped by compiled code, with retries if the network blinked, and a log line recording the command. None of that code mentions a connection string, a `SqlCommand`, or a thread.

**Next:** [Design principles](/introduction/principles)
