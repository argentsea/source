---
title: Sharding and tenanting
description: How ArgentSea spreads one data set across many databases, or gives every tenant its own, using shard sets, shard keys and concurrent fan-out queries.
---

# Sharding and tenanting

Sharding splits one logical data set across several databases, each owning a segment. ArgentSea was designed for this from the start: shard sets route a request to the right database, the `ShardKey` tells you which database holds a record, and fan-out queries ask many databases at once. The same machinery gives every cloud tenant a database of its own.

## Why shard, and what it costs

A single server has a ceiling, and buying for a growth forecast you do not trust is expensive. Shards let you **scale incrementally**: add a database when you need one. They also enable **active-active disaster recovery**, where two data centers each own a segment and process live traffic with reserve capacity for the other, instead of one site sitting idle and untested. **Regional residency and latency** follow the same pattern: European data lives on a European shard, close to its users and inside its jurisdiction. And **tenant isolation** is sharding by another name (more below).

The cost is that the database engine can no longer enforce what it does not know about. Unique keys are unique only within one shard, and a foreign key cannot point to a row in another database. Your application therefore needs a *global* identity for records, and must store the shard alongside every reference that can cross shards. ArgentSea's answer to both is the `ShardKey`.

## Tenants are shards

In a multi-tenant SaaS, the cleanest isolation is structural: each tenant gets its own database. ArgentSea models those databases as the shards of a shard set, typically named for what it holds:

```csharp
var tenants = _shardSets["Tenants"];
var tenant = tenants[tenantId];   // the shard id is the tenant id
```

Nothing a tenant does can touch another tenant's rows, because they are not in the same database. Each tenant can be backed up, restored, moved to a faster tier, given its own SLA, or decommissioned by dropping one database. Onboarding a tenant means adding one shard entry to configuration (see [Configuration](/guides/configuration)). Everything on this page, from shard keys to fan-out reports across every tenant, applies unchanged.

## Shard sets and shards

A **shard set** is a collection of databases with the same schema and different data. `services.AddSqlServices(configuration)` (or `AddPgServices`) registers a `SqlShardSets` (`PgShardSets`) singleton; inject it and capture the shard set your class needs:

```csharp
public class CustomerStore
{
    private readonly SqlShardSets.ShardSet _shardSet;
    private readonly ILogger<CustomerStore> _logger;

    public CustomerStore(SqlShardSets shardSets, ILogger<CustomerStore> logger)
    {
        _shardSet = shardSets["Customers"];
        _logger = logger;
    }

    public async Task<Customer> GetCustomer(ShardKey<int> key, CancellationToken cancellation)
    {
        var prms = new ParameterCollection()
            .AddSqlIntInputParameter("@CustomerId", key.RecordId);
        return await _shardSet[key].Read.MapReaderAsync<Customer>(Queries.GetCustomer, prms, cancellation);
    }
}
```

A shard set is indexed by shard id (`shardSet[shardId]`, a `short`) or, more conveniently, by any shard key (`shardSet[shardKey]`), which routes to the key's own shard. `shardSet.DefaultShard` is the shard named by `DefaultShardId` in configuration, the natural home for *new* records created by this client (a regional application defaults to its regional shard). Each shard has a `Read` and a `Write` connection; you always choose, and ArgentSea never infers intent or falls back from one to the other.

## The ShardKey

<ShardKeyAnatomy />

A `ShardKey` is a **virtual compound key**: the shard id plus the record's own database key. The combination is unique across the whole shard set, even though each database numbers its rows independently, and it carries its own routing information. No database stores it as one column; it exists in your application.

- The **shard id** is always a `short` (`SMALLINT`): two bytes, tens of thousands of possible shards, and no generic type parameter to repeat everywhere.
- The **record key** has one to four columns, given as generic arguments: `ShardKey<int>` for an `int` primary key, `ShardKey<long, short>` for an order line keyed by order id and line number, up to `ShardKey<TRecord, TChild, TGrandChild, TGreatGrandChild>`. Integers, `Guid`, `string`, `decimal`, dates, times and enums are all supported column types.
- It is an **immutable struct**, so it can never be null. An unset key is `ShardKey<int>.Empty`; test with `.IsEmpty`. For a column that may be `NULL`, declare the property as `ShardKey<int>?`.
- **Equality** (`==`, `Equals`, `GetHashCode`) compares the shard id and key values, so keys work as dictionary keys.

```csharp
var key = new ShardKey<int>(shardId: 3, recordId: 1042);
key.ShardId;   // 3
key.RecordId;  // 1042
key.ToString(); // { "shard": 3, "id": "1042"}
```

Every serialized form of a key (`ToArray()` binary, `ToUtf8()`, and the external string below) starts with compact **type metadata** describing the .NET type of each key column. Deserializing as the wrong generic type fails instead of producing a plausible wrong key, and a `GhostShardKey` can rebuild a key whose types are unknown at the point of handling, which logging and Orleans grain ids rely on. This metadata replaces version 1's `DataOrigin` character, which no longer exists.

## External keys

A sharded identity must cross the application boundary as one value: a REST route, a link, a JSON property. `ToExternalString()` produces a short, URL-safe string, and `ShardKey<int>.FromExternalString(s)` reverses it.

```csharp
var external = customer.Key.ToExternalString();   // e.g. in a route: /customers/{external}
var key = ShardKey<int>.FromExternalString(external);
```

The external string is **obfuscated and checksummed**: a mistyped, truncated or casually edited value fails to parse rather than addressing a different record, and record numbering is not exposed in plain text. It is **not encryption and not authorization**. Anyone can decode or forge one, so your application must still authorize every access.

## Mapping shard keys

The Mapper builds a `ShardKey` from ordinary columns or parameters. Put the shard key attribute on the property *and* the column's own type attribute, using the identical name:

::: code-group
```csharp [SQL Server]
[MapSqlShardKey("@CustomerId")]
[MapToSqlInt("@CustomerId", true)]
public ShardKey<int> Key { get; set; }
```
```csharp [PostgreSQL]
[MapPgShardKey("customer_id")]
[MapToPgInteger("customer_id", true)]
public ShardKey<int> Key { get; set; }
```
:::

Here the shard id comes implicitly from the connection that produced the row, which is right for a record's own primary key. The `true` on the column attribute marks it required: a `NULL` key means no record, so the Mapper returns `null` for the whole model (`MapSqlShardKey` also accepts a trailing `bool`; in the current source the Mapper reads the required flag from the column attribute, so set it there).

A reference to a record that may live on *another* shard must store that shard's id. Name the shard id column first:

::: code-group
```csharp [SQL Server]
[MapSqlShardKey("@ReferrerShardId", "@ReferrerId")]
[MapToSqlInt("@ReferrerId")]
public ShardKey<int>? Referrer { get; set; }

[MapSqlShardKey("@OrderShardId", "@OrderId", "@LineNumber")]
[MapToSqlBigInt("@OrderId")]
[MapToSqlSmallInt("@LineNumber")]
public ShardKey<long, short> OrderLine { get; set; }
```
```csharp [PostgreSQL]
[MapPgShardKey("referrer_shard_id", "referrer_id")]
[MapToPgInteger("referrer_id")]
public ShardKey<int>? Referrer { get; set; }

[MapPgShardKey("order_shard_id", "order_id", "line_number")]
[MapToPgBigint("order_id")]
[MapToPgSmallint("line_number")]
public ShardKey<long, short> OrderLine { get; set; }
```
:::

The shard id column is always `SMALLINT`, and ArgentSea maps it for you. Child key columns follow the record id in order, each with its own type attribute. The same attributes work in both directions: `prms.CreateInputParameters(model, logger)` splits a key into parameters when you write. See [Mapping](/guides/mapping) for nulls, nested models and result-set lists.

## Fan-out

<FanOutDemo />

When you hold a shard key, you query one shard. When you do not, you query many. A shard set exposes three fan-out forms, and each runs the query **concurrently** on every targeted shard, each shard on **its own connection, its own copy of the parameters and its own resilience policy**. Cross-shard latency is about one round trip of the slowest shard, not the sum.

- **`ReadAll`** uses every shard's read connection, waits for all, and returns the non-null results combined into one `List<TModel>`. Use it when the answer may span shards: every customer with an overdue balance.
- **`ReadFirst`** uses the read connections, returns the first non-null result, and cancels the rest. Use it for lookups by a *unique attribute that is not the key*, such as a login name or email address, where one hit is enough. If the attribute is not truly unique across shards, duplicates are silently dropped.
- **`Write`** uses every targeted shard's write connection, for changes that apply across the set: a maintenance script, a price update. Writes are never cancelled because a sibling failed, and every shard failure is surfaced, several as one `AggregateException`.

```csharp
public Task<Subscriber> GetByLogin(string login, CancellationToken cancellation)
{
    var prms = new ParameterCollection()
        .AddSqlNVarCharInputParameter("@Login", login, 255);
    return _shardSet.ReadFirst.MapReaderAsync<Subscriber>(Queries.GetSubscriberByLogin, prms, cancellation);
}
```

Because results arrive on several threads at once, a custom result handler passed to `QueryAsync` must be thread-safe. The Mapper's handlers already are. Single-record access by key is not a fan-out; `shardSet[key]` goes to one shard.

### Targeting shards with ShardsValues

Every fan-out method accepts an optional `ShardsValues` that names which shards to query, and optionally **overrides parameter values per shard**:

```csharp
var shards = new ShardsValues()
    .Add(1)
    .Add(3, "@Region", "EU");   // shard 3 sees @Region = "EU"; shard 1 keeps the shared value
var customers = await _shardSet.ReadAll.MapListAsync<Customer>(Queries.ListCustomers, prms, shards, cancellation);
```

An empty `ShardsValues` queries no shards. Omit it to query the whole set with the same parameters.

### Telling SQL which shard it is

Pass a `shardParameterName` and ArgentSea sets that parameter to each shard's id before the query runs, so your SQL can stamp outgoing references with its own shard id, or verify it:

```csharp
var prms = new ParameterCollection()
    .AddSqlSmallIntInputParameter("@ShardId", 0);   // declared here, set per shard
await _shardSet.Write.RunAsync(Queries.ArchiveClosedOrders, prms, "@ShardId", cancellation);
```

::: warning Shard ids live only in client configuration
ArgentSea never asks a database which shard it is. If two clients map shard ids to databases differently, or one entry is wrong, a write does not fail; it **silently updates the matching record id in the wrong database**. Mitigate this: store each database's own id in a table and check `@ShardId` against it inside write procedures, use rowversion-style optimistic concurrency so a stale or misrouted update is rejected, and manage shard configuration centrally.
:::

## Foreign shards

Relationships cross shards. A subscriber on shard 2 can list contacts whose records live on shards 1 and 5. The local shard stores each contact's full key (shard id included) and can return stub rows; the detail lives elsewhere. Implement `IKeyedModel<TRecord>` (a `Key` property) on the model and three helpers do the stitching:

```csharp
public class Contact : IKeyedModel<int>
{
    [MapSqlShardKey("@ContactShardId", "@ContactId")]
    [MapToSqlInt("@ContactId", true)]
    public ShardKey<int> Key { get; set; }
    // ...
}

// 1. Stubs from the subscriber's own shard.
var contacts = await _shardSet[subscriberKey].Read.MapListAsync<Contact>(Queries.GetContacts, prms, cancellation);
// 2. Which *other* shards hold them? (the local shard is excluded)
var foreign = subscriberKey.ForeignShards(contacts);
// 3. Ask only those shards, telling each one its id.
var details = await _shardSet.ReadAll.MapListAsync<Contact>(Queries.GetContactDetails, prms, foreign, "@ShardId", cancellation);
// 4. Replace stubs with full records, matched by key.
contacts = ShardKey<int>.Merge(contacts, details);
```

`ForeignShards(...)` returns a `ShardsValues` of the distinct shards in a list of keys or keyed models, minus the current one; `ShardKey<int>.ToShardsValues(records)` does the same without excluding any shard. `Merge(master, replacements)` walks the master list and swaps in the replacement with the same key; pass `appendUnmatchedReplacements: true` to keep extras. If step 3 produced many ids per shard, send them as a table-valued parameter (SQL Server) or stage them with COPY inside a [batch](/guides/batches) (PostgreSQL).

## The identity-range alternative

Some teams give each database a disjoint identity range instead of a compound key, so plain integers stay globally unique. ArgentSea works with that too: index the shard set with the shard id your own resolver derives from the integer, and skip `ShardKey`. The trade-offs are the ones the compound key was designed to avoid: both clients and servers must be configured with the ranges, and a mistake is very hard to repair.

**Next:** [Batches and transactions](/guides/batches)
