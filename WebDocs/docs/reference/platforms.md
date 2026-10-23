---
title: Platforms
description: The databases, cloud services, .NET version and packages ArgentSea 2 supports, with the dependency matrix and the key types a ShardKey can carry.
---

# Platforms

ArgentSea 2 runs on .NET 10 against SQL Server or PostgreSQL, on premises or in any of the major clouds. This page is the compatibility reference.

## Supported databases

**SQL Server**, through `ArgentSea.Sql` on Microsoft.Data.SqlClient. This covers SQL Server Express and the full editions, and Azure SQL Database. Read scale-out is supported out of the box: set `ApplicationIntent` to `ReadOnly` on a read connection and Always On readable secondaries or Azure SQL read scale-out serve the reads while the write connection goes to the primary.

**PostgreSQL**, through `ArgentSea.Pg` on Npgsql. This covers self-hosted PostgreSQL, Amazon RDS for PostgreSQL and Amazon Aurora PostgreSQL, Azure Database for PostgreSQL, and Google Cloud SQL for PostgreSQL. Point a read connection at a hot standby or a managed read replica endpoint and the write connection at the primary.

In both cases the choice of read or write is explicit in your code; ArgentSea never routes a call based on its SQL, and failover is left to the database platform. See [Configuration](/guides/configuration) and [Resilience](/guides/resilience).

**Microsoft Orleans** is supported on SQL Server through `ArgentSea.Orleans.Sql` and on PostgreSQL through `ArgentSea.Orleans.Pg`, both built on the abstract `ArgentSea.Orleans` layer. See [Orleans](/guides/orleans).

## Package matrix

All packages are version 2.1.0 and target `net10.0`.

| Package | Purpose | Depends on | NuGet |
| --- | --- | --- | --- |
| `ArgentSea` | Core: Mapper, Query objects, shard keys, configuration, resilience, logging, batches | Microsoft.Extensions.* 10.0.13, Polly 8.8.0 | [ArgentSea](https://www.nuget.org/packages/ArgentSea/) |
| `ArgentSea.Sql` | SQL Server provider | `ArgentSea`, Microsoft.Data.SqlClient 7.1.1 | [ArgentSea.Sql](https://www.nuget.org/packages/ArgentSea.Sql/) |
| `ArgentSea.Pg` | PostgreSQL provider | `ArgentSea`, Npgsql 10.0.3 | [ArgentSea.Pg](https://www.nuget.org/packages/ArgentSea.Pg/) |
| `ArgentSea.Orleans` | Abstract Orleans grain storage, clustering and reminder support | `ArgentSea`, Microsoft.Orleans 10.4.0 | [ArgentSea.Orleans](https://www.nuget.org/packages/ArgentSea.Orleans/) |
| `ArgentSea.Orleans.Sql` | Orleans providers for SQL Server | `ArgentSea.Orleans`, `ArgentSea.Sql`, Microsoft.Orleans 10.4.0 | [ArgentSea.Orleans.Sql](https://www.nuget.org/packages/ArgentSea.Orleans.Sql/) |
| `ArgentSea.Orleans.Pg` | Orleans providers for PostgreSQL | `ArgentSea.Orleans`, `ArgentSea.Pg`, Microsoft.Orleans 10.4.0 | [ArgentSea.Orleans.Pg](https://www.nuget.org/packages/ArgentSea.Orleans.Pg/) |

You install the provider package you need (`ArgentSea.Sql` or `ArgentSea.Pg`, and `ArgentSea.Orleans.Sql` or `ArgentSea.Orleans.Pg` for Orleans); the core and abstract packages come in transitively. See [Installation](/getting-started/installation).

## Runtime requirements

- .NET 10 (`net10.0`). Version 1 targeted .NET Standard; see [What's new](/reference/whats-new) for migration.
- Microsoft.Data.SqlClient 7.1.1 for SQL Server.
- Npgsql 10.0.3 for PostgreSQL.
- Microsoft.Orleans 10.4.0 (Runtime, Sdk and Reminders) for the Orleans packages.
- Polly 8.8.0 for retry and circuit breaking (installed with the core package).
- An `ILogger` implementation: logging is mandatory on every data method and Mapper call.

## One provider per project

A model and an application target exactly one database provider. You cannot mix `ArgentSea.Sql` and `ArgentSea.Pg` in the same project: the Mapper does not know which provider will execute the parameters it creates, so a model carrying both `MapToSql*` and `MapToPg*` attributes would be ambiguous. If you have both database platforms, keep them in separate projects. The [FAQ](/reference/faq) has the longer answer.

## Supported key types

A `ShardKey` record id (and child, grandchild and great-grandchild ids) can be any of the types below, including the nullable form and enums backed by the integer types. The generic constraint is `IComparable`, but serialization and the Mapper's expression trees work from this fixed list.

- `byte`, `short`, `int`, `long` (and enums with these underlying types)
- `char`, `string`
- `decimal`, `double`, `float`
- `Guid`
- `DateTime`, `DateOnly`, `TimeSpan`

`bool`, `DateTimeOffset`, `TimeOnly` and the unsigned integers (`sbyte`, `ushort`, `uint`, `ulong`) are not key types: the key's type metadata has no code for them, even though the SQL Server mapping attributes accept the unsigned types for ordinary columns. In practice keys use the signed integers, `string`, `Guid` and the date types. The shard id itself is always `short`.

## Licence and source

ArgentSea is released under the [MIT licence](https://opensource.org/licenses/MIT). The source, issues and contribution guidelines are at [github.com/argentsea/source](https://github.com/argentsea/source).
