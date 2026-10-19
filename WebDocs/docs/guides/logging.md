---
title: Logging
description: ArgentSea logs through precompiled, structured events with fixed ids and levels, so you can alert on retries and breaker trips in production and read the Mapper's generated code in development.
---

# Logging

Every ArgentSea data method and every Mapper call takes an `ILogger`, and the parameter is not optional. This page explains why, lists what ArgentSea logs and at which level, and shows which events to watch once you are in production.

## Why logging is mandatory

Data-tier failures in a sharded, concurrent application are hard to diagnose after the fact. A query fans out to ten shards on ten threads, and an exception from one of them has lost its context by the time it reaches a controller. The Mapper runs compiled code you cannot step through. A driver that retries silently hides a degrading database until it fails outright.

So ArgentSea makes logging part of the design. Because a logger is always present, every retry, breaker trip, missing column and compiled mapping is recorded, tagged with the query name and the connection it ran on. Inject `ILogger<MyDataStore>` into your data class and pass it through:

```csharp
public class CustomerStore
{
    private readonly SqlDatabases.Database _db;
    private readonly ILogger<CustomerStore> _logger;

    public CustomerStore(SqlDatabases databases, ILogger<CustomerStore> logger)
    {
        _db = databases["Catalog"];
        _logger = logger;
    }

    public Task<Customer> GetCustomerAsync(int customerId, CancellationToken cancellation)
    {
        var prms = new ParameterCollection()
            .AddSqlIntInputParameter("@CustomerId", customerId);
        return _db.Read.MapOutputAsync<Customer>(Queries.GetCustomer, prms, cancellation);
    }
}
```

The data method and the Mapper share the logger registered through `services.AddLogging()`; nothing else is required.

## Structured events with fixed ids

Every event is a precompiled `LoggerMessage` definition with a fixed numeric **event id**, a fixed **level** and named placeholders. A disabled level therefore costs almost nothing (no string is formatted, nothing is allocated), and the ids are stable, so an alert rule written against event 11 keeps working across releases.

The ids are the members of `LoggingExtensions.EventIdentifier` in the `ArgentSea` namespace:

| Id | Event | Level | What it tells you |
| --- | --- | --- | --- |
| 11 | `CircuitBreaker` | **Critical** on open; Information on trial call and on restore | An endpoint has failed `CircuitBreakerFailureCount` times in a row and is now failing fast; later entries show it being retested and recovering |
| 9 | `ConnectRetry` | **Warning** | A transient error occurred and ArgentSea is retrying; includes the endpoint and the attempt number |
| 7 | `MapperResultsReaderInvalid` | Error | A data reader handed to the Mapper was null or closed; names the query and connection |
| 6 | `UnexpectedDbNull` | Debug (Information for shard keys) | A required column was null, so the model (or a shard key) was discarded; names the column that caused it |
| 2, 3 | `MapperSqlParameterNotFound`, `MapperSqlColumnNotFound` | Debug | A mapped property had no matching parameter or result column; it was skipped |
| 1 | `ExpressionTreeCreation` | Debug | The Mapper compiled a delegate for a model type; the entry contains the generated code |
| 4 | `MapperCacheStatus` | Debug on miss, Trace on hit | Whether a compiled delegate already existed; a miss is normal once per type per process |
| 0 | `ConnectionStringBuilt` | Debug | An endpoint resolved its hierarchy into a connection string (password masked) |
| 12 | `BatchStep` | Debug | A batch step started or completed on a connection |
| 8 | `CmdExecuted` | Trace | A command finished, with the query name, connection (or shard set and shard id) and elapsed milliseconds |
| 5 | `MapperProcessTrace` | Trace | The Mapper is processing a named property; useful context when something else fails |

Id 10, `CommandRetry`, is reserved. The SQL Server package adds `SqlLoggingExtensions.SqlEventIdentifier` for table-valued-parameter mapping (cache status at Information on a miss, property trace at Debug), and the PostgreSQL package logs COPY mapping under the shared ids above.

Every data-access entry carries the query name and a connection description of the form `database Orders on server tenants-1.internal`, so a log line from a fan-out across twenty shards still tells you which one it came from.

## What to watch in production

Run production at **Information** or **Warning**. At those levels ArgentSea is nearly silent while things are healthy, which makes the signals easy to spot:

- **Warnings with event id 9** are retries. One now and then is normal. A steady stream from the same endpoint means that endpoint is degrading: a saturated server, a flapping network path, an Azure SQL database being throttled. This is the early warning that a driver-level retry would have hidden from you.
- **A Critical entry with event id 11** means the breaker opened. Calls to that endpoint are now failing fast, so downstream errors will follow until the Information entries for the same id report that a trial call succeeded and the circuit is restored.

::: info Releases up to 2.0.4
Builds up to and including 2.0.4 define the event id 11 messages but never emit them; the breaker callbacks were wired in the source after that release. On those builds, detect an open breaker by the `BrokenCircuitException` your calls receive and by the retry warnings (event id 9) that precede it.
:::
- **Errors with event id 7** indicate a coding or server-side problem rather than an outage: a procedure returned no result set where the Mapper expected one.

Alert on 11, chart 9 per endpoint, and page someone on 7. Because every retry runs through ArgentSea's own policy (driver retries are disabled, as [Resilience](/guides/resilience) explains), these counts are complete.

## Seeing the generated mapping code

The Mapper compiles an expression tree for each model type the first time it is used and caches the delegate for the life of the process. At **Debug**, event 1 writes the pseudo-code of that tree to the log before it is compiled: which column maps to which property, how nulls are handled, where a nested model is flattened. It is the fastest way to answer "why is this property empty?" in development. Debug also reports cache misses (event 4; expect one per model type, and investigate if they recur), mapped columns and parameters absent from the result (events 2 and 3), and the exact column whose null caused a required model to come back as `null` (event 6).

At **Trace**, event 5 records every property the Mapper touches and event 8 records the elapsed time of every command. Trace is verbose; use it on one process while reproducing a problem.

::: warning The log level is a performance setting
Debug and Trace write large entries on hot paths. Set `Logging:LogLevel:ArgentSea` to `Information` (or `Warning`) in production and raise it only where you are actively investigating.
:::

## Any sink works

ArgentSea logs through `Microsoft.Extensions.Logging` and knows nothing about where the entries go. Whatever provider you register receives them: the console during development; Application Insights, Amazon CloudWatch, Serilog or OpenTelemetry in production. Placeholders such as `{connectionName}` and `{milliseconds}` arrive as named properties, so you can filter and aggregate on them instead of parsing message text.

**Next:** [Queries](/guides/queries)
