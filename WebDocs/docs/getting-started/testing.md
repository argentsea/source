---
title: Testing with mock results
description: Unit-test your data-access classes without a database by registering canned results against query names on an ArgentSea connection or shard set.
---

# Testing with mock results

Every connection, shard and shard set in ArgentSea carries a `MockResults` dictionary. Put an object in it under a query's name and the next data method that runs that query returns the object instead of opening a connection. This page shows how to build the ArgentSea services inside a test, how to register results, and what this technique does and does not prove.

## Why it works

An ArgentSea data call runs in stages: your code builds a `ParameterCollection` (by hand or through the Mapper), picks a `Query` and a connection, and calls a data method; ArgentSea then opens the connection, executes the command under its retry policy, and maps the result back. The mock check sits at the boundary between those two halves. Everything *before* the connection, including your parameter building, your `Queries` class, your choice of `Read` versus `Write` and the shard-key routing, runs for real. Only the trip to the database is replaced.

The dictionary is keyed by `Query.Name`, which is the procedure name for a `QueryProcedure` and the file name for a `QueryStatement`. The lookup happens inside the data method, so your production store class needs no interface, no fake, and no test-only constructor.

## Build the services in a test

`SqlDatabases` and `SqlShardSets` (or the Pg equivalents) bind their settings from `IConfiguration`, so a test builds a small in-memory configuration, registers the services exactly as `Program.cs` does, and constructs the collection. This is the pattern the ArgentSea test suite itself uses.

::: code-group
```csharp [SQL Server]
using ArgentSea.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

static SqlDatabases BuildDatabases()
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SqlDbConnections:0:DatabaseKey"] = "Subscribers",
            ["SqlDbConnections:0:DataSource"] = "not-a-real-server",
            ["SqlDbConnections:0:InitialCatalog"] = "SubscriberDb",
        })
        .Build();

    var services = new ServiceCollection();
    services.AddOptions();
    services.AddLogging();
    services.AddSqlServices(config);
    var provider = services.BuildServiceProvider();

    return new SqlDatabases(
        provider.GetRequiredService<IOptions<SqlDbConnectionOptions>>(),
        provider.GetRequiredService<IOptions<SqlGlobalPropertiesOptions>>(),
        NullLogger<SqlDatabases>.Instance);
}
```
```csharp [PostgreSQL]
using ArgentSea.Pg;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

static PgDatabases BuildDatabases()
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PgDbConnections:0:DatabaseKey"] = "Subscribers",
            ["PgDbConnections:0:Host"] = "not-a-real-server",
            ["PgDbConnections:0:Database"] = "subscriberdb",
        })
        .Build();

    var services = new ServiceCollection();
    services.AddOptions();
    services.AddLogging();
    services.AddPgServices(config);
    var provider = services.BuildServiceProvider();

    return new PgDatabases(
        provider.GetRequiredService<IOptions<PgDbConnectionOptions>>(),
        provider.GetRequiredService<IOptions<PgGlobalPropertiesOptions>>(),
        NullLogger<PgDatabases>.Instance);
}
```
:::

The server name does not need to exist; no connection is ever opened. Constructing the collection yourself, rather than resolving it from the provider, gives each test a fresh instance with an empty mock dictionary, so tests cannot leak results into each other. (Resolving `provider.GetRequiredService<SqlDatabases>()` also works, since `AddSqlServices` registers it as a singleton.) The repository's own tests pass an NSubstitute logger here; `NullLogger` is the dependency-free choice.

A shard set is built the same way from a `SqlShardSets:0:…` configuration, using `IOptions<SqlShardConnectionOptions>` and the `SqlShardSets` constructor (Pg: `PgShardConnectionOptions`, `PgShardSets`).

## Register a result and call the store

With the collection in hand, add the expected result under the query name, construct the store exactly as the DI container would, and assert on what comes back:

```csharp
[Fact]
public async Task GetAsync_returns_the_mapped_subscriber()
{
    var databases = BuildDatabases();
    var expected = new Subscriber { SubscriberId = 7, Name = "Ada" };
    databases["Subscribers"].Read.MockResults.Add(Queries.SubscriberGet.Name, expected);

    var store = new SubscriberStore(databases, NullLogger<SubscriberStore>.Instance);

    var actual = await store.GetAsync(7, CancellationToken.None);

    Assert.Same(expected, actual);
}
```

The stored object must be the type the data method returns: a `Subscriber` for `MapReaderAsync<Subscriber>`, a `List<Subscriber>` for `MapListAsync<Subscriber>`, an `int` for `ReturnValueAsync<int>`. A mismatch throws an invalid-cast exception at the call, which is the test telling you that the store's return type and the mock disagree.

Register on the connection your store actually uses. The `Read` and `Write` connections of a database each have their own dictionary, so a mock on `Read` does not satisfy a call on `Write`; that asymmetry is useful, because a store that reads on the write connection by mistake fails the test.

### Shards and shard sets

For sharded stores there are two places to register:

- **A single shard's connection**, for calls that go through `shardSet[key].Read` or `shardSet.DefaultShard.Write`:
  `shardSets["Customers"][1].Read.MockResults.Add(Queries.CustomerGet.Name, customer);`
- **The shard set**, for fan-out calls through `ReadAll`, `ReadFirst` or `Write`:
  `shardSets["Customers"].MockResults.Add(Queries.CustomerList.Name, new List<Customer> { … });`

A fan-out mock returns the stored object as the combined result; for `ReadAll.MapListAsync<Customer>` that is a `List<Customer>`.

::: warning Touching `MockResults` creates it
The shard-set dictionary is created on first access, and a shard-set *batch* is skipped whenever that dictionary exists, even empty. Read `shardSet.MockResults` only in test code, never in a production code path.
:::

## What this does not test

Mock results replace the database, so they say nothing about the database. In particular they cannot tell you that:

- the stored procedure or statement file exists, compiles, or returns the columns your model maps;
- a parameter name in a `MapTo…` attribute matches a column name (a mismatch is skipped and logged at Debug, not thrown);
- the SQL is correct or fast.

What they do test is everything you wrote in C#: that the store builds the right parameters, picks the right query and the right connection, routes a key to the right shard, and does the right thing with the result (including a `null` for "not found", which you mock by storing `null`). That is usually the part with the bugs you can fix without a database, and it runs in milliseconds.

For the SQL itself, keep a small integration suite against a real database (SQL Server Express, a container, or a throwaway PostgreSQL instance) that executes each query once with representative parameters. Because the query contract is explicit (a named procedure or file, a fixed parameter set, a known result shape), that suite is short and rarely changes.

**Next:** [Configuration](/guides/configuration)
