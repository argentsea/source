---
title: "Quickstart: a shard set"
description: Configure two shards, give a Customer model a shard key, and read, create and search across shards with ArgentSea's fan-out methods.
---

# Quickstart: a shard set

This walkthrough takes the shape of the [one-database quickstart](/getting-started/quickstart-database) and applies it to data spread across several databases. You will configure a shard set with two shards, give a `Customer` model a shard key, fetch a customer from the right shard, create one on the default shard, and search and list across every shard at once.

The same machinery serves two very different needs: splitting one large data set across servers, and giving each cloud tenant a database of its own. Either way a "shard" is a database with the same schema as its siblings, identified by a small integer.

## 1. Configure the shard set

Shard sets live in the `SqlShardSets` / `PgShardSets` configuration section. Because every shard shares most of its connection settings with the others, ArgentSea lets you state a property once at the shard-set level and have every shard inherit it. Only what differs per shard is written per shard.

::: code-group
```json [SQL Server]
{
  "SqlShardSets": [
    {
      "ShardSetName": "Customers",
      "DefaultShardId": 1,
      "DataSource": "localhost",
      "UserName": "webUser",
      "Read": { "ApplicationIntent": "ReadOnly" },
      "Write": { "ApplicationName": "CustomersWrite" },
      "Shards": [
        { "ShardId": 1, "InitialCatalog": "CustomersShard1" },
        { "ShardId": 2, "InitialCatalog": "CustomersShard2" }
      ]
    }
  ]
}
```
```json [PostgreSQL]
{
  "PgShardSets": [
    {
      "ShardSetName": "Customers",
      "DefaultShardId": 1,
      "Host": "localhost",
      "UserName": "webuser",
      "Read": { "ApplicationName": "CustomersRead" },
      "Write": { "ApplicationName": "CustomersWrite" },
      "Shards": [
        { "ShardId": 1, "Database": "customers_shard1" },
        { "ShardId": 2, "Database": "customers_shard2" }
      ]
    }
  ]
}
```
:::

Reading this top to bottom:

- `ShardSetName` is the key your code uses to find the shard set.
- `DataSource`/`Host` and `UserName` sit at the shard-set level, so both shards, and the read and write connection of each, inherit them.
- `Read` and `Write` are optional sections whose values apply only to the read connections, or only to the write connections, of every shard in the set. Pointing reads at a readable secondary (`ApplicationIntent: ReadOnly` on SQL Server) is the typical use.
- Each entry in `Shards` has a `ShardId` (a `short`, unique within the set) plus whatever differs for that shard; here only the database name. A shard can also carry its own `ReadConnection` and `WriteConnection` sections.
- `DefaultShardId` names the shard exposed as `shardSet.DefaultShard`, where this instance of the application creates new records.

In production the picture is often inverted (identical database names on different hosts), and the same inheritance handles it. The password belongs in User Secrets as before: `dotnet user-secrets set "SqlShardSets:0:Password" "…"` (or `PgShardSets:0:Password`). See the [Configuration guide](/guides/configuration).

::: warning Shard ids are configuration
The shard id comes only from client configuration. A mistyped `ShardId` silently writes to the wrong database. The [sharding guide](/guides/sharding) shows how to pass the shard id into your SQL (`shardParameterName`) so the database can verify it.
:::

## 2. The shard key

A record in a sharded set is identified by two values: the shard it lives on and its id within that shard. ArgentSea packages them into an immutable struct, `ShardKey<TRecord>`, where `TRecord` is the type of the record id (`int` here) and the shard id is always a `short`. Compound keys use `ShardKey<TRecord, TChild>` and deeper.

```csharp
var key = new ShardKey<int>(shardId: 1, recordId: 42);
key.ShardId;    // 1
key.RecordId;   // 42
key.IsEmpty;    // false; ShardKey<int>.Empty is the "no key" value
```

A shard key also has a string form that is safe to put in a URL or a JSON payload: `key.ToExternalString()` produces a short, obfuscated, checksummed token, and `ShardKey<int>.FromExternalString(token)` turns it back into a key. It is an encoding, not encryption or authorization: a client can hand it back, but it cannot forge one by guessing. Step 6 uses it in the API.

## 3. Create the model

The `Customer` model looks like the `Subscriber` from the previous quickstart, with one new property: its key. Two attributes go on it. `MapSqlShardKey` tells the Mapper that this property is a shard key whose record id comes from the parameter named `@CustomerId`; `MapToSqlInt` maps that record id column as usual. The names must match exactly.

::: code-group
```csharp [SQL Server]
using ArgentSea;
using ArgentSea.Sql;

public readonly record struct Customer : IKeyedModel<int>
{
    [MapSqlShardKey("@CustomerId")]
    [MapToSqlInt("@CustomerId")]
    public ShardKey<int> Key { get; init; }

    [MapToSqlNVarChar("@Name", 255)]
    public string Name { get; init; }

    [MapToSqlNVarChar("@Email", 255)]
    public string Email { get; init; }
}
```
```csharp [PostgreSQL]
using ArgentSea;
using ArgentSea.Pg;

public readonly record struct Customer : IKeyedModel<int>
{
    [MapPgShardKey("customer_id")]
    [MapToPgInteger("customer_id")]
    public ShardKey<int> Key { get; init; }

    [MapToPgVarchar("name", 255)]
    public string Name { get; init; }

    [MapToPgVarchar("email", 255)]
    public string Email { get; init; }
}
```
:::

There is no shard id column in this query: when a row is read on a shard's connection, ArgentSea fills `Key.ShardId` from that connection's configured shard id. A reference to a record on *another* shard does need a shard id column, and the attribute has a two-argument form for it: `[MapSqlShardKey("@ContactShardId", "@ContactId")]`.

Implementing `IKeyedModel<int>` (a single `Key` property) is optional but worthwhile: it unlocks helpers such as `ForeignShards` and `ToShardsValues` that work out which shards a set of models touches; see the [sharding guide](/guides/sharding).

## 4. SQL and the Queries class

Each shard has the same schema and the same procedures or statement files, so the SQL is written once and deployed to every shard.

::: code-group
```sql [SQL Server]
CREATE PROCEDURE ws.CustomerGet @CustomerId int AS
SELECT CustomerId, Name, Email FROM dbo.Customers WHERE CustomerId = @CustomerId;
GO
CREATE PROCEDURE ws.CustomerCreate @Name nvarchar(255), @Email nvarchar(255), @CustomerId int OUTPUT AS
INSERT dbo.Customers (Name, Email) VALUES (@Name, @Email);
SET @CustomerId = SCOPE_IDENTITY();
GO
CREATE PROCEDURE ws.CustomerFindByEmail @Email nvarchar(255) AS
SELECT CustomerId, Name, Email FROM dbo.Customers WHERE Email = @Email;
GO
CREATE PROCEDURE ws.CustomerList AS
SELECT CustomerId, Name, Email FROM dbo.Customers;
GO
```
```sql [PostgreSQL]
-- SQL/CustomerGet.psql
SELECT customer_id, name, email FROM ws.customers WHERE customer_id = @customer_id;

-- SQL/CustomerCreate.psql
INSERT INTO ws.customers (name, email) VALUES (@name, @email) RETURNING customer_id;

-- SQL/CustomerFindByEmail.psql
SELECT customer_id, name, email FROM ws.customers WHERE email = @email;

-- SQL/CustomerList.psql
SELECT customer_id, name, email FROM ws.customers;
```
:::

The `Queries` class follows the previous quickstart exactly: a static `QueryProcedure` property per procedure for SQL Server (`new QueryProcedure("ws.CustomerGet")`), or a `Lazy<QueryStatement>` from `QueryStatement.Create("CustomerGet")` per file for PostgreSQL, exposed as `Queries.CustomerGet`, `Queries.CustomerCreate`, `Queries.CustomerFindByEmail` and `Queries.CustomerList`.

## 5. The data-access class

The store now takes `SqlShardSets` / `PgShardSets` instead of the databases collection and looks up the shard set by name. From there, four methods show the four ways to address a shard set.

::: code-group
```csharp [SQL Server]
public class CustomerStore
{
    private readonly SqlShardSets.ShardSet _shards;
    private readonly ILogger<CustomerStore> _logger;

    public CustomerStore(SqlShardSets shardSets, ILogger<CustomerStore> logger)
    {
        _shards = shardSets["Customers"];
        _logger = logger;
    }

    // One shard, chosen by the key.
    public Task<Customer> GetAsync(ShardKey<int> key, CancellationToken cancellation)
    {
        var prms = new ParameterCollection().AddSqlIntInputParameter("@CustomerId", key.RecordId);
        return _shards[key].Read.MapReaderAsync<Customer>(Queries.CustomerGet, prms, cancellation);
    }

    // The default shard, for new records.
    public async Task<ShardKey<int>> CreateAsync(string name, string email, CancellationToken cancellation)
    {
        var prms = new ParameterCollection()
            .AddSqlNVarCharInputParameter("@Name", name, 255)
            .AddSqlNVarCharInputParameter("@Email", email, 255)
            .AddSqlIntOutputParameter("@CustomerId");
        var shard = _shards.DefaultShard;
        var id = await shard.Write.ReturnValueAsync<int>(Queries.CustomerCreate, "@CustomerId", prms, cancellation);
        return new ShardKey<int>(shard.ShardId, id);
    }

    // Every shard; first non-null result wins.
    public Task<Customer> FindByEmailAsync(string email, CancellationToken cancellation)
    {
        var prms = new ParameterCollection().AddSqlNVarCharInputParameter("@Email", email, 255);
        return _shards.ReadFirst.MapReaderAsync<Customer>(Queries.CustomerFindByEmail, prms, cancellation);
    }

    // Every shard; results concatenated.
    public Task<List<Customer>> ListAsync(CancellationToken cancellation)
        => _shards.ReadAll.MapListAsync<Customer>(Queries.CustomerList, new ParameterCollection(), cancellation);
}
```
```csharp [PostgreSQL]
public class CustomerStore
{
    private readonly PgShardSets.ShardSet _shards;
    private readonly ILogger<CustomerStore> _logger;

    public CustomerStore(PgShardSets shardSets, ILogger<CustomerStore> logger)
    {
        _shards = shardSets["Customers"];
        _logger = logger;
    }

    // One shard, chosen by the key.
    public Task<Customer> GetAsync(ShardKey<int> key, CancellationToken cancellation)
    {
        var prms = new ParameterCollection().AddPgIntegerInputParameter("customer_id", key.RecordId);
        return _shards[key].Read.MapReaderAsync<Customer>(Queries.CustomerGet, prms, cancellation);
    }

    // The default shard, for new records.
    public async Task<ShardKey<int>> CreateAsync(string name, string email, CancellationToken cancellation)
    {
        var prms = new ParameterCollection()
            .AddPgVarcharInputParameter("name", name, 255)
            .AddPgVarcharInputParameter("email", email, 255);
        var shard = _shards.DefaultShard;
        var id = await shard.Write.ReturnValueAsync<int>(Queries.CustomerCreate, "customer_id", prms, cancellation);
        return new ShardKey<int>(shard.ShardId, id);
    }

    // Every shard; first non-null result wins.
    public Task<Customer> FindByEmailAsync(string email, CancellationToken cancellation)
    {
        var prms = new ParameterCollection().AddPgVarcharInputParameter("email", email, 255);
        return _shards.ReadFirst.MapReaderAsync<Customer>(Queries.CustomerFindByEmail, prms, cancellation);
    }

    // Every shard; results concatenated.
    public Task<List<Customer>> ListAsync(CancellationToken cancellation)
        => _shards.ReadAll.MapListAsync<Customer>(Queries.CustomerList, new ParameterCollection(), cancellation);
}
```
:::

Taking the four in turn:

**`_shards[key].Read`** indexes the shard set by the key (or by a `short` shard id) and returns that shard, whose `Read` and `Write` connections have the same data methods you used on a single database. The query runs on one database; the key told ArgentSea which.

**`_shards.DefaultShard.Write`** is the shard configured by `DefaultShardId`. `ReturnValueAsync<int>` returns the output parameter (SQL Server) or first-row column (PostgreSQL) named in the second argument, and the store combines it with the shard's `ShardId` to build the new key. Writes always go to a shard you chose.

**`_shards.ReadFirst`** fans the query out to the read connection of every shard concurrently and returns the first non-null model, cancelling the rest: the tool for lookups by a unique attribute that is not the key, such as an email address.

**`_shards.ReadAll`** fans out the same way, waits for every shard, and concatenates the results into one list. Each shard runs on its own connection with its own parameter copy and its own retry policy, so a slow shard never blocks the others.

Both fan-out methods accept an optional `ShardsValues` to target a subset of shards and an optional `shardParameterName` that ArgentSea sets to each shard's id. Those, `Write.RunAsync` across all shards, and foreign-shard references are covered in the [sharding guide](/guides/sharding).

## 6. The API: keys in, keys out

A shard key is an awkward thing to put in a URL as two numbers, so the endpoint uses the external string form. The client receives one opaque token per customer and sends it back unchanged:

```csharp
app.MapPost("/customers", async (Customer input, CustomerStore store, CancellationToken cancellation) =>
{
    var key = await store.CreateAsync(input.Name, input.Email, cancellation);
    return Results.Created($"/customers/{key.ToExternalString()}", new { id = key.ToExternalString() });
});

app.MapGet("/customers/{id}", async (string id, CustomerStore store, CancellationToken cancellation) =>
{
    var key = ShardKey<int>.FromExternalString(id);
    var customer = await store.GetAsync(key, cancellation);
    return customer.Key.IsEmpty ? Results.NotFound() : Results.Ok(customer); // a missing customer comes back with an empty key
});

app.MapGet("/customers", (CustomerStore store, CancellationToken cancellation)
    => store.ListAsync(cancellation));
```

`FromExternalString` checks the checksum and the embedded key type, so a tampered token or one for a different key type is rejected rather than routed to the wrong shard. Serialize `Key` on your response models the same way and the shard structure never leaks into your API contract.

## What you have built

A shard set configured once with inheritance doing the repetition, a model whose only sharding-specific code is a two-attribute key property, and a store that addresses one shard, the default shard, or every shard with the same data methods used for a single database. The external key form keeps clients unaware of all of it.

**Next:** [Testing with mock results](/getting-started/testing)
