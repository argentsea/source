---
title: Queries
description: How ArgentSea keeps SQL static and loosely coupled, the two kinds of Query object, the Queries class convention, the query paradigm, and the data-method families.
---

# Queries

ArgentSea runs SQL that you wrote, never SQL that it generated. This page explains why that is the design, how a `Query` object names your SQL, and how the data methods turn that SQL into results, on one database or across a shard set.

## Static, consolidated, segregated

When application code depends on how tables and columns are implemented, the application and the database are *tightly coupled*: nobody can change one without risking the other. ArgentSea aims for *loose coupling* instead. The parameter set a query accepts and the result shape it returns are the contract. As long as that contract holds, a DBA can rewrite the query, add hints, partition a table, materialize a view or re-index, and the application keeps working.

Three habits make that contract real:

- **Static.** Queries are parameterized SQL, written by hand. There is no query builder and no SQL injection surface from object graphs, and the SQL you see in a profiler is the SQL you wrote.
- **Consolidated.** Every query the application uses is declared in one place. Large applications accumulate orphaned procedures nobody dares to drop; a single declaration site lets a reference count answer "is this still used?".
- **Segregated.** SQL lives outside the compiled binaries, in stored procedures (SQL Server) or deployed statement files (PostgreSQL). A query that performed well at launch may need rewriting as data grows; keeping it out of the C# build lets the people who tune SQL do that without a release.

## Two kinds of Query

Every data method takes a `Query`. ArgentSea ships two implementations.

`QueryProcedure` names a stored procedure and sets the command type to `StoredProcedure`. It is the preferred choice on SQL Server. On PostgreSQL, Npgsql runs a stored-procedure command as `CALL <name>(...)` with named arguments, so `QueryProcedure` calls PostgreSQL *procedures* only. A procedure returns no rows, but values come back through its INOUT parameters, which ArgentSea reads like output parameters. To read rows from a PostgreSQL function, write `SELECT * FROM my_function(...)` in a statement file.

`QueryStatement` loads parameterized SQL from a file and sets the command type to `Text`. It is the preferred choice on PostgreSQL, and useful anywhere you cannot create procedures. Files live in a `SQL` folder under the application directory and are read lazily on first use, then cached. The extension defaults to `.sql`; `AddPgServices` changes the default to `.psql`.

::: code-group
```csharp [SQL Server]
// SQL lives in the ws.GetCustomer stored procedure.
public static QueryProcedure GetCustomer => new QueryProcedure("ws.GetCustomer");
```
```csharp [PostgreSQL]
// SQL lives in <AppFolder>/SQL/GetCustomer.psql, loaded on first use.
private static readonly Lazy<QueryStatement> _getCustomer = QueryStatement.Create("GetCustomer");
public static QueryStatement GetCustomer => _getCustomer.Value;
```
:::

::: warning Deploy the statement files
Set each `.sql`/`.psql` file's *Build Action* to None and *Copy to Output Directory* to "Copy if newer". A "file not found" error on first query almost always means this step was skipped.
:::

`QueryStatement.Extension` is one process-wide default, so a host that registers *both* providers should use the provider-specific factories instead: `SqlQueryStatement.Create(name)` resolves `.sql` and `PgQueryStatement.Create(name)` resolves `.psql`, whatever the shared default happens to be. `QueryStatement.Create` also has overloads that take a folder and extension, or an absolute file path.

## The Queries class

The convention is one static class, usually named `Queries`, that exposes every query as a static property:

```csharp
public static class Queries
{
    public static QueryProcedure GetCustomer => new QueryProcedure("ws.GetCustomer", new[] { "CustomerId" });
    public static QueryProcedure SaveCustomer => new QueryProcedure("ws.SaveCustomer");

    private static readonly Lazy<QueryStatement> _listOrders = QueryStatement.Create("ListOrders");
    public static QueryStatement ListOrders => _listOrders.Value;
}
```

Calls then read naturally: `db.Read.MapReaderAsync<Customer>(Queries.GetCustomer, prms, cancellation)`. The reason for the convention is maintenance, not style. Your IDE shows a reference count on each property; a count of zero means the procedure or file behind it is dead and can be dropped with confidence.

## Parameter names

Both `QueryProcedure` and `QueryStatement.Create` accept an optional string array of parameter names. When it is present, [the Mapper](/guides/mapping) creates only the parameters in that list; when it is absent, the Mapper creates a parameter for every mapped property.

That lets one model feed queries with different signatures. An INSERT rarely wants a `LastModified` parameter, while an UPDATE usually does; list the parameters on each query and the same `Customer` model works for both. The list also settles whether a shard id parameter should be sent, which otherwise depends on how the model is mapped. Sending a parameter the procedure does not declare is a database error, so the list is the simplest way to keep the contract exact.

On SQL Server the `@` prefix is optional; ArgentSea normalizes names to include it. On PostgreSQL names are used exactly as given, including case.

## The query paradigm

A typical ADO.NET method creates a connection, creates a command, adds parameters, executes, and reads. That order cannot work across a shard set, where the same parameters must run on many connections at once: each connection needs its own parameter copy (threads would otherwise overwrite each other's values), and the code that reads results may run on several threads concurrently.

ArgentSea therefore splits every query into two parts, and uses the same split whether or not the data is sharded:

1. The **caller** builds a `ParameterCollection` before any connection exists, then calls a data method. This runs on one thread.
2. A **result handler** receives each connection's `DbDataReader` and parameters and turns them into a model. It must be thread-safe, because it may run for several shards at the same time.

`ParameterCollection` is ArgentSea's `DbParameterCollection` that can be created with `new`, without a command. Provider extension methods add typed parameters to it:

::: code-group
```csharp [SQL Server]
using ArgentSea.Sql;

var prms = new ParameterCollection()
    .AddSqlIntInputParameter("@CustomerId", customerId)
    .AddSqlNVarCharOutputParameter("@Name", 255);

var customer = await _databases["MyDb"].Read.MapOutputAsync<Customer>(Queries.GetCustomer, prms, cancellation);
```
```csharp [PostgreSQL]
using ArgentSea.Pg;

var prms = new ParameterCollection()
    .AddPgIntegerInputParameter("customerid", customerId);

var customer = await _databases["MyDb"].Read.MapReaderAsync<Customer>(Queries.GetCustomer, prms, cancellation);
```
:::

ArgentSea *copies* the collection onto each connection's command. Do not keep a reference to an output parameter you added and expect it to hold a value afterwards; read output values from the collection the handler receives (the Mapper does this for you). On a shard set the only change is the connection: `shardSet.ReadAll` returns a `List<Customer>` combined from every shard, `ReadFirst` returns the first non-null result, and `Write` runs on every shard. See [Sharding](/guides/sharding) for shard selection and the shard id parameter.

## Data methods

Every data method takes a `Query`, a `DbParameterCollection` (null when there are none) and a `CancellationToken`. The families differ in what they return and whether the Mapper is involved.

| Method | Returns | Mapper |
| --- | --- | --- |
| `RunAsync` | Nothing; also runs a batch | No |
| `ReturnValueAsync` | The procedure's `int` return value, or `ReturnValueAsync<TValue>(…, dataName, …)` for one output parameter or first-row column | No |
| `ListAsync<TValue>(…, columnName, …)` | `List<TValue>` from one column | No |
| `QueryAsync` | Whatever your `QueryResultModelHandler<TArg, TModel>` delegate returns | No |
| `MapListAsync<TModel>` | `List<TModel>`, one per row | Yes |
| `MapReaderAsync<TModel[, TList…]>` | `TModel` from a one-row result set, plus up to eight list properties from further result sets | Yes |
| `MapOutputAsync<TModel[, TList…]>` | `TModel` from output parameters, plus list properties from result sets | Yes |

Version 2 adds overloads whose first argument is an existing instance to hydrate in place, for example `MapReaderAsync(customer, Queries.GetCustomer, prms, cancellation)`. That is how Orleans grain state is refreshed, and it is handy anywhere you already hold the object.

The `Map*` methods are `QueryAsync` with a handler the Mapper supplies. They cover most queries; [Mapping](/guides/mapping) describes how result sets become models.

## Custom result handlers

When a model lives in a library you cannot attribute, or a result needs shaping the Mapper does not do, write the handler yourself. It must match the `QueryResultModelHandler<TArg, TModel>` delegate:

```csharp
TModel Handler(TModel instance, short shardId, string sprocName, TArg arg,
    DbDataReader rdr, DbParameterCollection parameters,
    string connectionDescription, ILogger logger)
```

`instance` is the object to hydrate (null unless you used an instance overload), `shardId` is the current shard (ignore it for a plain database), `arg` is an optional value you pass through the call, and `connectionDescription` is for your log messages, which matter because the handler may run on several connections at once. The reader is disposed for you.

```csharp
public static PriceList PriceListHandler(PriceList instance, short shardId, string sprocName,
    string currency, DbDataReader rdr, DbParameterCollection parameters,
    string connectionDescription, ILogger logger)
{
    var result = instance ?? new PriceList { Currency = currency };
    while (rdr.Read())
    {
        result.Prices[rdr.GetInt32(0)] = rdr.GetDecimal(1);
    }
    return result;
}

// TArg = string, TModel = PriceList; "false" is the single-result hint (isTopOne).
var prices = await _db.Read.QueryAsync<string, PriceList>(
    Queries.GetPriceList, prms, PriceListHandler, false, "USD", cancellation);
```

If you have no argument to pass, declare `TArg` as `object` and use the `QueryAsync<TModel>` overload without it. A custom handler can also call the Mapper for part of the result, for instance `rdr.ToList<OrderItem>(logger)` for one result set and hand-written code for the next.

## The escape hatch

Nothing here is a closed pipeline. The parameter helpers are extension methods on `DbParameterCollection`, so they work on `SqlCommand.Parameters` and `NpgsqlCommand.Parameters` as well as on `ParameterCollection`. The Mapper's `CreateInputParameters`, `ToModel<T>` and `ToList<T>` work on any command's parameters or any `DbDataReader`. When a query needs something ArgentSea does not model, open the connection yourself with ADO.NET and keep using the pieces that help.

**Next:** [Mapping](/guides/mapping)
