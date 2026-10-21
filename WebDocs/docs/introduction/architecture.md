---
title: Architecture
description: How ArgentSea is layered, from the provider-neutral core and the thin SQL Server and PostgreSQL packages to the Microsoft Orleans providers, and what happens when a query runs.
---

# Architecture

ArgentSea is one core library plus a thin package for each database platform, with the Microsoft Orleans providers stacked on top. This page shows how the pieces fit, what each package contributes, and the path a single query takes through them.

## Packages

<PackageMap />

**ArgentSea** (the core) is written against `System.Data.Common` and never names SqlClient or Npgsql. It holds everything that can be expressed without a platform: the configuration hierarchy and the `Databases` and `ShardSets` collections, the fan-out engine, the per-endpoint resilience policies, batches, `ShardKey` and its serialization, the Mapper's expression-tree compiler, and the structured log events. You do not reference it directly; the provider packages bring it in.

**ArgentSea.Sql** and **ArgentSea.Pg** each implement one narrow seam, the *data provider services*: create a connection, create a command, copy a parameter collection onto a command, and decide whether an error is transient. Around that seam each package adds what only it can know: the configuration option classes and their JSON section names, the `MapToSql*` or `MapToPg*` attributes with the platform's types, typed parameter extension methods, bulk loading (table-valued parameters, or `COPY`), and the `AddSqlServices` / `AddPgServices` registration. A project references exactly one of them.

**ArgentSea.Orleans** is the abstract Orleans provider: grain persistence built on any ArgentSea provider's collections, the `ShardKey` serializer for grain keys, and the option types. **ArgentSea.Orleans.Sql** binds it to SQL Server and **ArgentSea.Orleans.Pg** to PostgreSQL; each adds cluster membership, gateway discovery and reminders through versioned database routines.

## The two halves of a data method

Every data method in ArgentSea, sharded or not, is built from the same two halves. The split exists because of sharding, but it is applied everywhere so that sharded and non-sharded code look identical.

The **caller** runs on one thread. It builds a `ParameterCollection` before any connection exists, picks a `Query`, and invokes a method on a connection or a shard set. ArgentSea then opens a connection and command per target database, copies the parameters onto each command, and executes.

The **result handler** runs once per connection, possibly on several threads at once, and turns that connection's results (the data reader and the parameter collection, with any output values) into a model. The `Map*` methods supply the Mapper's compiled, thread-safe handlers; the `Query*` methods accept your own.

```text
caller thread                     per-connection work (one per shard, concurrent)
──────────────                    ─────────────────────────────────────────────
build ParameterCollection   ──►   open pooled connection
choose Query                      copy parameters, set shard id parameter
call shardSet.ReadAll.Map… ──►    execute under retry + circuit breaker
                                  result handler maps reader → model
collect / merge results     ◄──   log command, retries, mapper events
```

## What happens when a query runs

Take `customers.ReadAll.MapListAsync<Customer>(Queries.ByRegion, prms, cancellation)` on a shard set with four shards.

1. **Targeting.** The shard set decides which shards take part: all four, or the subset named by a `ShardsValues` argument.
2. **Mock check.** If the query's name is present in the shard set's `MockResults`, the stored object is returned and nothing else happens. This is how unit tests run data classes without a database.
3. **Per-shard setup.** For each shard, ArgentSea takes the shard's `Read` endpoint, whose connection settings were resolved from the configuration hierarchy at startup, creates a provider connection and command, and copies the parameters onto it, applying the query's parameter-name filter and any per-shard overrides.
4. **Execution.** Each command runs under its endpoint's own resilience policy. Transient failures, as classified by the provider package, are retried with backoff; a run of failures trips that endpoint's circuit breaker without affecting the others.
5. **Handling.** As each reader opens, the Mapper's compiled delegate for `Customer` reads the rows by ordinal and builds the list for that shard, on that shard's thread.
6. **Merging.** `ReadAll` waits for every shard and concatenates the non-null lists. `ReadFirst` would instead return the first non-null result and cancel the remaining shards.
7. **Logging.** Throughout, fixed-id log events record the command, any retries, breaker state changes, and mapper cache activity.

For a single database the sequence is the same with one target. For a batch, step 4 runs every step on one connection inside one client transaction.

## Where the Mapper fits

The Mapper is not a layer the data methods pass through; it is a library of compiled delegates they call. On first use of a model type for a given purpose (input parameters, output parameters, reader rows, bulk rows), it reads the mapping attributes by reflection, builds an expression tree, compiles it and caches the delegate per type for the life of the process. The same delegates are available to you as extension methods on parameter collections and data readers, which is how you combine the Mapper with your own ADO.NET code.

## Where Orleans fits

The Orleans storage provider sits above the collections like any other data class. For each grain type you register a read, a write and a clear `Query`; the provider uses the Mapper to hydrate the grain's existing state object from the read query's results and to build parameters from it for the write. With the sharded variant, the grain key is a serialized `ShardKey`, so the provider indexes the shard set with it and reaches the tenant's database directly. Clustering and reminders use the same configured database connections through stored procedures.

## Dependencies

The core depends only on `Microsoft.Extensions.Configuration`, `Logging` and `Options`, `System.Collections.Immutable` and Polly. The provider packages add Microsoft.Data.SqlClient or Npgsql. The Orleans packages add the Microsoft.Orleans runtime. There is nothing else.

**Next:** [Installation](/getting-started/installation)
