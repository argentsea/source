---
title: Installation
description: Add the ArgentSea package for your database, register its services in Program.cs, and understand what gets injected into your code.
---

# Installation

ArgentSea ships as one NuGet package per database platform. This page gets the package into your project and the services into your dependency-injection container; the two quickstarts that follow put them to work.

## Choose a package

Pick the package that matches your database. Each one pulls in the core `ArgentSea` package and the ADO.NET driver for its platform, so one reference is all you need.

| Database | Package | Driver |
| --- | --- | --- |
| SQL Server, Azure SQL Database | [`ArgentSea.Sql`](https://www.nuget.org/packages/ArgentSea.Sql/) | Microsoft.Data.SqlClient 7.1.1 |
| PostgreSQL (incl. RDS, Aurora, Azure, Cloud SQL) | [`ArgentSea.Pg`](https://www.nuget.org/packages/ArgentSea.Pg/) | Npgsql 10.0.3 |

::: code-group
```bash [SQL Server]
dotnet add package ArgentSea.Sql
```
```bash [PostgreSQL]
dotnet add package ArgentSea.Pg
```
:::

The current version is 2.1.0 and it targets **.NET 10**. Every package in the family (`ArgentSea`, `ArgentSea.Sql`, `ArgentSea.Pg`, `ArgentSea.Orleans`, `ArgentSea.Orleans.Sql`, `ArgentSea.Orleans.Pg`) is released at the same version number, so keep them aligned.

If you are hosting Microsoft Orleans grains, there are three further packages: `ArgentSea.Orleans` (the abstract provider), and two implementations that give you grain persistence, clustering and reminders: `ArgentSea.Orleans.Sql` on top of `ArgentSea.Sql` and `ArgentSea.Orleans.Pg` on top of `ArgentSea.Pg`. They are covered in the [Orleans guide](/guides/orleans).

### One provider per project

Pick one provider per project. Both packages can be referenced side by side, but the two providers share one process-wide default for where `QueryStatement` files live, so a host that registers both must create its statements through the provider-specific `SqlQueryStatement` and `PgQueryStatement` factories instead of `QueryStatement.Create`. If you only register one provider, which is the common case, none of that applies and you can ignore it.

## Dependencies

ArgentSea is a thin layer over ADO.NET and leans on the standard Microsoft building blocks rather than reinventing them:

- **Microsoft.Extensions.Configuration**, **Options** and **Logging** (10.0.x): connection settings are bound from `IConfiguration` into options objects, and every data call writes structured log events.
- **Polly** 8.8.0: retry with backoff and a circuit breaker, applied per connection endpoint. You never call Polly directly; ArgentSea configures the policies from your settings. See [Resilience](/guides/resilience).
- **Microsoft.Data.SqlClient** or **Npgsql**: the real ADO.NET driver. ArgentSea never hides it; there is always an escape hatch to the raw provider types.

All of these are restored automatically when you add the package.

## Register the services

ArgentSea plugs into the standard .NET hosting model. In `Program.cs`, pass the application's configuration to the provider's registration method. The method is an extension on `IServiceCollection` in the `Microsoft.Extensions.DependencyInjection` namespace, so no extra `using` is required.

::: code-group
```csharp [SQL Server]
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSqlServices(builder.Configuration);

var app = builder.Build();
app.Run();
```
```csharp [PostgreSQL]
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPgServices(builder.Configuration);

var app = builder.Build();
app.Run();
```
:::

That single call does three things:

1. Binds the configuration sections (`SqlGlobalSettings`, `SqlDbConnections`, `SqlShardSets`, or the `Pg…` equivalents) into options objects. The next page shows what those sections look like.
2. Registers **`SqlDatabases`** and **`SqlShardSets`** (or **`PgDatabases`** and **`PgShardSets`**) as singletons. `SqlDatabases` is the collection of your non-sharded databases; `SqlShardSets` is the collection of your shard sets. If you have no shard sets configured, the collection is empty, and vice versa.
3. Reads `SqlGlobalSettings` / `PgGlobalSettings` as the root of the configuration hierarchy that every connection inherits from.

Anything that needs data access then asks for the collection in its constructor:

::: code-group
```csharp [SQL Server]
public class SubscriberStore
{
    private readonly SqlDatabases _databases;
    private readonly ILogger<SubscriberStore> _logger;

    public SubscriberStore(SqlDatabases databases, ILogger<SubscriberStore> logger)
    {
        _databases = databases;
        _logger = logger;
    }
}
```
```csharp [PostgreSQL]
public class SubscriberStore
{
    private readonly PgDatabases _databases;
    private readonly ILogger<SubscriberStore> _logger;

    public SubscriberStore(PgDatabases databases, ILogger<SubscriberStore> logger)
    {
        _databases = databases;
        _logger = logger;
    }
}
```
:::

Register your own data classes as you normally would (`builder.Services.AddSingleton<SubscriberStore>()`); the ArgentSea collections are safe to share across threads.

## Logging is required

Every ArgentSea data method and every Mapper call takes an `ILogger`. This is deliberate: retries, circuit-breaker trips, skipped columns and slow queries are all reported through structured log events with fixed event ids, and there is no silent mode. `WebApplication.CreateBuilder` registers logging for you; if you are building a plain `ServiceCollection` (for example in a test or a console host), call `services.AddLogging()` before `AddSqlServices`. The [Logging guide](/guides/logging) explains what each event means and which ones to watch.

::: tip Keep the `@` optional
With SQL Server, parameter names in attributes and extension methods can be written with or without the leading `@`; ArgentSea adds it for parameters and strips it when matching reader columns. PostgreSQL names are used exactly as written.
:::

## What you have now

After this page your project references one provider package, `Program.cs` registers its services, and any class can take `SqlDatabases` / `SqlShardSets` (or the Pg equivalents) plus an `ILogger` in its constructor. What is missing is configuration that tells ArgentSea where the databases are, a model to map results into, and a query to run.

**Next:** [Quickstart: one database](/getting-started/quickstart-database)
