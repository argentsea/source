---
title: "Quickstart: one database"
description: A linear walkthrough from appsettings.json to a working minimal-API endpoint that reads and writes a Subscriber through ArgentSea.
---

# Quickstart: one database

This walkthrough builds a small web API that reads and updates a `Subscriber` record in a single, non-sharded database. By the end you will have seen every moving part of ArgentSea once: configuration, a mapped model, a `Queries` class, a data-access class, and an endpoint that uses it. The [sharding quickstart](/getting-started/quickstart-sharding) reuses all of it.

It assumes you have completed [Installation](/getting-started/installation): the provider package is referenced and `AddSqlServices` / `AddPgServices` is in `Program.cs`.

## 1. Describe the connection

ArgentSea does not use connection strings. Instead, connection properties are ordinary configuration values, which means they can be split across `appsettings.json`, environment variables, User Secrets and Key Vault, and .NET merges them for you. Put the non-secret parts in `appsettings.json`:

::: code-group
```json [SQL Server]
{
  "SqlDbConnections": [
    {
      "DatabaseKey": "Subscribers",
      "DataSource": "localhost",
      "InitialCatalog": "SubscriberDb",
      "UserName": "webUser"
    }
  ]
}
```
```json [PostgreSQL]
{
  "PgDbConnections": [
    {
      "DatabaseKey": "Subscribers",
      "Host": "localhost",
      "Database": "subscriberdb",
      "UserName": "webuser"
    }
  ]
}
```
:::

`DatabaseKey` is the name your code will use to find this database. The remaining properties are the same ones you would put in a connection string, spelled as the driver spells them (`DataSource`/`InitialCatalog` for SqlClient, `Host`/`Database` for Npgsql). Using Windows authentication? Set `"WindowsAuth": true` and skip the user name and password entirely.

The password goes in User Secrets so it never lands in source control. From the project directory:

::: code-group
```bash [SQL Server]
dotnet user-secrets init
dotnet user-secrets set "SqlDbConnections:0:Password" "your-password"
```
```bash [PostgreSQL]
dotnet user-secrets init
dotnet user-secrets set "PgDbConnections:0:Password" "your-password"
```
:::

The `:0:` is the array index: the secret merges into the first entry of `SqlDbConnections`, alongside the properties from `appsettings.json`. In production the same key can come from an environment variable or a secrets vault. The [Configuration guide](/guides/configuration) covers the full hierarchy, including separate read and write endpoints and global defaults.

## 2. Register the services

If you followed the installation page this is already done, but here it is in context, together with the data-access class you will write in step 5:

::: code-group
```csharp [SQL Server]
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSqlServices(builder.Configuration);
builder.Services.AddSingleton<SubscriberStore>();
var app = builder.Build();
```
```csharp [PostgreSQL]
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPgServices(builder.Configuration);
builder.Services.AddSingleton<SubscriberStore>();
var app = builder.Build();
```
:::

## 3. Create the model

A model is a plain class whose properties carry mapping attributes. Each attribute names the parameter or column the property corresponds to and fixes its database type. The .NET property type must match the database type exactly (an `int` for `int`/`integer`, a `string` for `nvarchar`/`varchar`); ArgentSea does not cast.

::: code-group
```csharp [SQL Server]
using ArgentSea.Sql;

public class Subscriber
{
    [MapToSqlInt("@SubscriberId")]
    public int SubscriberId { get; set; }

    [MapToSqlNVarChar("@Name", 255)]
    public string Name { get; set; }

    [MapToSqlDateTime2("@Expiration")]
    public DateTime? Expiration { get; set; }
}
```
```csharp [PostgreSQL]
using ArgentSea.Pg;

public class Subscriber
{
    [MapToPgInteger("subscriber_id")]
    public int SubscriberId { get; set; }

    [MapToPgVarchar("name", 255)]
    public string Name { get; set; }

    [MapToPgTimestamp("expiration")]
    public DateTime? Expiration { get; set; }
}
```
:::

Property names do not need to match column names; the attribute carries the database name, so your C# can follow .NET conventions while the database follows its own. `Expiration` is nullable because the column allows nulls: a nullable value type maps null to `DBNull` and back without any extra work.

The model needs a public parameterless constructor, because ArgentSea creates instances for you. The first time a type is mapped, ArgentSea compiles expression trees for it (a sub-second one-off cost) and caches them for the life of the process, so the mapping runs at the speed of hand-written ADO.NET code. [Mapping](/guides/mapping) has the full attribute list, nulls, required columns and nested models.

## 4. Write the SQL and the Queries class

ArgentSea does not generate SQL. For SQL Server the SQL lives in stored procedures; for PostgreSQL it lives in `.psql` files deployed with the application. Either way the parameters and result columns are the contract, and the SQL behind that contract can be tuned without touching C#.

::: code-group
```sql [SQL Server]
CREATE PROCEDURE ws.SubscriberGet
  @SubscriberId int
AS
SELECT SubscriberId, Name, Expiration
FROM dbo.Subscribers
WHERE SubscriberId = @SubscriberId;
GO

CREATE PROCEDURE ws.SubscriberSave
  @SubscriberId int,
  @Name nvarchar(255),
  @Expiration datetime2
AS
UPDATE dbo.Subscribers
SET Name = @Name, Expiration = @Expiration
WHERE SubscriberId = @SubscriberId;
GO
```
```sql [PostgreSQL]
-- SQL/SubscriberGet.psql
SELECT subscriber_id, name, expiration
FROM ws.subscribers
WHERE subscriber_id = @subscriber_id;

-- SQL/SubscriberSave.psql
UPDATE ws.subscribers
SET name = @name, expiration = @expiration
WHERE subscriber_id = @subscriber_id;
```
:::

For PostgreSQL, create a `SQL` folder in the project and mark the files to copy to the output directory, for example with `<None Update="SQL\**\*.psql" CopyToOutputDirectory="PreserveNewest" />` in the project file. `AddPgServices` switches the default statement-file extension to `.psql`, so `QueryStatement.Create("SubscriberGet")` loads `SQL/SubscriberGet.psql` the first time it is used and caches it from then on.

Now give each query a name in code. The convention is one static `Queries` class with a static property per query, so a reference count tells you which queries the application still uses:

::: code-group
```csharp [SQL Server]
using ArgentSea;

public static class Queries
{
    public static QueryProcedure SubscriberGet { get; } = new QueryProcedure("ws.SubscriberGet");
    public static QueryProcedure SubscriberSave { get; } = new QueryProcedure("ws.SubscriberSave");
}
```
```csharp [PostgreSQL]
using ArgentSea;

public static class Queries
{
    private static readonly Lazy<QueryStatement> _subscriberGet = QueryStatement.Create("SubscriberGet");
    private static readonly Lazy<QueryStatement> _subscriberSave = QueryStatement.Create("SubscriberSave");

    public static QueryStatement SubscriberGet => _subscriberGet.Value;
    public static QueryStatement SubscriberSave => _subscriberSave.Value;
}
```
:::

`QueryStatement.Create` returns a `Lazy<QueryStatement>` so the file is read once, on first use, even when many threads start at the same moment. A `QueryProcedure` takes an optional second argument listing the parameter names to send; without it, every parameter in the collection is sent. [Queries](/guides/queries) discusses when that matters.

## 5. Write the data-access class

The data class takes the `SqlDatabases` / `PgDatabases` collection and a logger, looks up the database by key, and calls a data method on its `Read` or `Write` connection. You always choose Read or Write explicitly; ArgentSea never infers it.

::: code-group
```csharp [SQL Server]
using ArgentSea;
using ArgentSea.Sql;

public class SubscriberStore
{
    private readonly SqlDatabases.Database _db;
    private readonly ILogger<SubscriberStore> _logger;

    public SubscriberStore(SqlDatabases databases, ILogger<SubscriberStore> logger)
    {
        _db = databases["Subscribers"];
        _logger = logger;
    }

    public Task<Subscriber> GetAsync(int subscriberId, CancellationToken cancellation)
    {
        var prms = new ParameterCollection()
            .AddSqlIntInputParameter("@SubscriberId", subscriberId);
        return _db.Read.MapReaderAsync<Subscriber>(Queries.SubscriberGet, prms, cancellation);
    }

    public Task SaveAsync(Subscriber subscriber, CancellationToken cancellation)
    {
        var prms = new ParameterCollection()
            .CreateInputParameters(subscriber, _logger);
        return _db.Write.RunAsync(Queries.SubscriberSave, prms, cancellation);
    }
}
```
```csharp [PostgreSQL]
using ArgentSea;
using ArgentSea.Pg;

public class SubscriberStore
{
    private readonly PgDatabases.Database _db;
    private readonly ILogger<SubscriberStore> _logger;

    public SubscriberStore(PgDatabases databases, ILogger<SubscriberStore> logger)
    {
        _db = databases["Subscribers"];
        _logger = logger;
    }

    public Task<Subscriber> GetAsync(int subscriberId, CancellationToken cancellation)
    {
        var prms = new ParameterCollection()
            .AddPgIntegerInputParameter("subscriber_id", subscriberId);
        return _db.Read.MapReaderAsync<Subscriber>(Queries.SubscriberGet, prms, cancellation);
    }

    public Task SaveAsync(Subscriber subscriber, CancellationToken cancellation)
    {
        var prms = new ParameterCollection()
            .CreateInputParameters(subscriber, _logger);
        return _db.Write.RunAsync(Queries.SubscriberSave, prms, cancellation);
    }
}
```
:::

Two things are worth noticing. In `GetAsync`, the single input parameter is added by hand with a typed extension method, and `MapReaderAsync<Subscriber>` turns the first row of the result into a `Subscriber`, or returns `null` when the query returns no rows. In `SaveAsync`, `CreateInputParameters` asks the Mapper to build one input parameter per mapped property on the model, so the call stays the same length however many columns the table grows to.

`ParameterCollection` is ArgentSea's provider-neutral `DbParameterCollection`. Parameters are built before any connection exists and copied onto the real command, which is what makes the retry and fan-out machinery possible, and it is why you should never hold a reference to an output parameter expecting it to be filled in.

## 6. Expose an endpoint

With the store registered, a minimal-API endpoint is a few lines:

```csharp
app.MapGet("/subscribers/{id:int}", async (int id, SubscriberStore store, CancellationToken cancellation) =>
{
    var subscriber = await store.GetAsync(id, cancellation);
    return subscriber is null ? Results.NotFound() : Results.Ok(subscriber);
});

app.MapPut("/subscribers/{id:int}", async (int id, Subscriber subscriber, SubscriberStore store, CancellationToken cancellation) =>
{
    subscriber.SubscriberId = id;
    await store.SaveAsync(subscriber, cancellation);
    return Results.NoContent();
});

app.Run();
```

Run the project and request `/subscribers/1`. Watch the console too: ArgentSea logs each command, and if the password or server is wrong you will see the retry attempts and the circuit breaker in the log rather than a bare exception.

## What you have built

Configuration with the secret kept out of source, a model whose attributes are the only mapping code you wrote, SQL that lives in the database or beside the binary, and a store class whose methods are two or three lines each. Everything else (parameter copying, connection opening, retries, logging) ArgentSea did. The same shape scales to a shard set with surprisingly few changes, which is what the next quickstart shows.

**Next:** [Quickstart: a shard set](/getting-started/quickstart-sharding)
