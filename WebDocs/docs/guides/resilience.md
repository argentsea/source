---
title: Resilience
description: Every ArgentSea endpoint retries transient errors with a configurable backoff and is protected by its own circuit breaker, on by default, visible in your logs, and tunable from the configuration hierarchy.
---

# Resilience

Networks blip, databases fail over, deadlocks happen. ArgentSea wraps every read and write endpoint in a [Polly](https://github.com/App-vNext/Polly) policy that retries transient errors with a growing delay and trips a circuit breaker when an endpoint stays down. You get this without configuring anything; this page explains what the defaults do, how to tune them, and what ArgentSea deliberately leaves to your infrastructure.

## Retrying transient errors

A retry only helps when the error might clear by itself. A dropped TCP connection or a deadlock is worth retrying; a permission error or a bad parameter is not, and retrying it would only delay the exception you need to see. ArgentSea therefore retries only errors that the **provider's data provider services** classify as transient:

- **SQL Server** (`ArgentSea.Sql`) keeps an explicit list of `SqlException` numbers: deadlock victim (1205), transport-level errors such as a connection forcibly closed or a timed-out connect (10053, 10054, 10060, 233, 64), Azure SQL Database throttling and "service busy" responses (40501, 40613, 40197, 10928, 10929, 49918–49920), a readable secondary that is not yet ready (4221), and the optimistic-concurrency conflicts raised by memory-optimized tables (41301, 41302, 41305, 41325, 41823, 41839, 41840).
- **PostgreSQL** (`ArgentSea.Pg`) defers to Npgsql's own classification, `NpgsqlException.IsTransient`.

Anything else is thrown to the caller on the first failure. When a transient error survives every retry, the last exception is thrown.

The policy wraps the whole operation (open, execute, read results), so a failure part-way through a command re-runs the command from the beginning. Design writes to be safe to repeat, such as an upsert keyed on the record id, so that a retry after a lost acknowledgement does no harm.

## Backoff sequences

Three properties shape the retries:

| Property | Meaning | Default |
| --- | --- | --- |
| `RetryCount` | Retries after the first attempt | 6 |
| `RetryInterval` | Base delay in milliseconds | 250 |
| `RetryLengthening` | How the delay grows with each attempt | `Lengthening` |

Each delay is a multiple of `RetryInterval`, and `RetryLengthening` chooses the multiplier series. With the default 250 ms interval the first five delays are:

| `RetryLengthening` | Multipliers | Delays at 250 ms |
| --- | --- | --- |
| `Linear` | 1, 2, 3, 4, 5 | 250, 500, 750, 1000, 1250 |
| `Lengthening` (default) | 1, 3, 5, 7, 9 | 250, 750, 1250, 1750, 2250 |
| `HalfSquare` | 0, 2, 4, 8, 12 | 0, 500, 1000, 2000, 3000 |
| `Squaring` | 1, 2, 4, 8, 16 | 250, 500, 1000, 2000, 4000 |

`Lengthening` retries quickly at first, then gives a struggling server progressively more room. `HalfSquare` retries once immediately before backing off. `Squaring` is classic exponential backoff. The value `Fibonacci` from earlier versions still binds and behaves exactly like `Lengthening` (the series was never a Fibonacci sequence) but is marked obsolete; prefer `Lengthening` in new configuration.

Retries and timeouts compound: six `Lengthening` retries wait about nine seconds in total before counting the connect attempts themselves. ArgentSea sets the SQL Server connect timeout to 5 seconds unless you override `ConnectTimeout`, well below the driver's 15-second default, so a dead server surfaces quickly; for PostgreSQL set `Timeout` in `PgGlobalSettings`. On a high-latency link raise the timeout and the interval rather than the retry count.

## The circuit breaker

Retries protect you from short interruptions but make a long outage *worse*: every request waits through its full retry sequence, requests pile up behind it, pools fill, and one dead database can stall the whole application. A **circuit breaker** is the fail-fast mechanism that prevents the pile-up. Each endpoint has its own, so a failing shard never trips the breaker for its healthy neighbours. Two properties control it:

| Property | Meaning | Default |
| --- | --- | --- |
| `CircuitBreakerFailureCount` | Consecutive failed operations (after retries) that open the breaker | 20 |
| `CircuitBreakerTestInterval` | Milliseconds the breaker stays open before allowing one trial call | 5000 |

While the breaker is open, calls to that endpoint fail **immediately** with Polly's `BrokenCircuitException` instead of waiting through retries. After `CircuitBreakerTestInterval` milliseconds a single trial call is let through; if it succeeds the breaker closes and traffic resumes, and if it fails the breaker stays open for another interval. Recovery is automatic. A `CircuitBreakerFailureCount` below 1 disables the breaker for that endpoint; retries still apply.

## Where the settings live

All five properties are ordinary members of the [configuration hierarchy](/guides/configuration). Most applications set them once in the global section, or not at all, and let every endpoint inherit them. Here a shard in another region, reached over a slower link, gets a longer base delay, fewer retries and a faster-tripping breaker, while the other shards keep the defaults:

::: code-group
```json [SQL Server]
{
  "SqlShardSets": [
    {
      "ShardSetName": "Tenants",
      "InitialCatalog": "Orders",
      "Shards": [
        { "ShardId": 0, "DataSource": "tenants-0.internal" },
        { "ShardId": 1, "DataSource": "tenants-1.internal" },
        {
          "ShardId": 2,
          "DataSource": "tenants-2.eu.internal",
          "ConnectTimeout": 10,
          "RetryCount": 4,
          "RetryInterval": 1000,
          "RetryLengthening": "Squaring",
          "CircuitBreakerFailureCount": 10
        }
      ]
    }
  ]
}
```
```json [PostgreSQL]
{
  "PgShardSets": [
    {
      "ShardSetName": "Tenants",
      "Database": "orders",
      "Shards": [
        { "ShardId": 0, "Host": "tenants-0.internal" },
        { "ShardId": 1, "Host": "tenants-1.internal" },
        {
          "ShardId": 2,
          "Host": "tenants-2.eu.internal",
          "Timeout": 10,
          "RetryCount": 4,
          "RetryInterval": 1000,
          "RetryLengthening": "Squaring",
          "CircuitBreakerFailureCount": 10
        }
      ]
    }
  ]
}
```
:::

Settings on a shard apply to both of its endpoints; use `ReadConnection` or `WriteConnection` to tune one, or the shard set's `Read` or `Write` section to tune every read or write endpoint in the set.

Changes take effect without a restart. Before each command, an endpoint compares its current `RetryCount` and breaker settings with those its policy was built from and rebuilds the policy only when they differ (which resets that breaker). `RetryInterval` and `RetryLengthening` are read on every retry, so changing them never resets a breaker.

## Every retry is logged

ADO.NET drivers have retry logic of their own. ArgentSea disables it (the SQL Server connection string is built with `ConnectRetryCount=0`) so that all retries happen in its own policy, where each is logged as a **Warning** naming the endpoint and attempt number, and a breaker opening is logged as **Critical**. A driver retry is invisible, and a database that is quietly retrying on every call is about to fail; you want to see it coming. [Logging](/guides/logging) lists the events to alert on.

## Failover is your call

ArgentSea does not fail an endpoint over to a standby server on its own. High-availability designs vary too widely for one client-side rule to be safe: a real failover is usually a coordinated infrastructure change (an availability-group listener, a DNS swap, a promoted replica), and a single client that decides for itself can split writes across two primaries. The circuit breaker keeps the application responsive while a database is down; bringing it back, or redirecting to another, belongs to your infrastructure.

What ArgentSea provides is the mechanism. Driver features such as SQL Server's `FailoverPartner` and `MultiSubnetFailover` are ordinary configuration properties. And because every connection property is updatable at run time and propagates to the endpoints that inherit it, your application can answer whatever failover signal you trust (a health check, an orchestrator event, an operator command) by setting a new `DataSource` or `Host` on the affected shard, shard set or global settings. The next command on each endpoint uses the new address; commands already in flight finish on the old one.

**Next:** [Logging](/guides/logging)
