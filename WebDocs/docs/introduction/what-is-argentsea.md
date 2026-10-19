---
title: What is ArgentSea?
description: ArgentSea is a thin, fast .NET data-access framework over ADO.NET, built so that sharding, tenant databases and read scale-out are ordinary rather than heroic.
---

# What is ArgentSea?

ArgentSea is a .NET data-access framework for SQL Server and PostgreSQL. It sits directly on ADO.NET and was designed, from its first line, for applications whose data will not fit comfortably in one database: data sets that must be **sharded** across servers, SaaS products that give each **tenant** a database of their own, and services that scale **reads** onto replicas. It also happens to be a very pleasant way to talk to a single database.

If you already know ADO.NET, you know most of what ArgentSea does. It adds the parts ADO.NET leaves to you: a configuration model that scales to hundreds of connections, a compiled object mapper, built-in retry and circuit breaking, a global record identity for sharded data, and a way to run one query across many databases at once and get one answer back.

## The thirty-second tour

Your application talks to ArgentSea through two injected collections: **Databases** (every non-sharded database, by key) and **ShardSets** (every group of shards, by name). Each database and each shard exposes a **Read** connection and a **Write** connection. You pick which one to use for each call; ArgentSea never guesses.

Every call takes a **Query**, which names a stored procedure or a parameterized SQL file deployed beside your application, together with a collection of parameters. ArgentSea opens the connection, applies the parameters, executes with retries, and hands the results to a **result handler**. In most cases that handler is the **Mapper**: attributes on your model's properties tell it which parameter or column each property binds to, and a compiled delegate does the work at the speed of hand-written code.

When data lives in a shard set, a **ShardKey** identifies a record by combining the shard id with the record's own key. Index a shard set with a key and you get the right shard. Ask the shard set itself and the query **fans out** to every shard in parallel, returning the first match or all of them combined.

```csharp
// One record, on the shard its key names:
var customer = await customers[key].Read
    .MapReaderAsync<Customer>(Queries.GetCustomer, prms, cancellation);

// Every shard at once, results merged into one list:
var overdue = await customers.ReadAll
    .MapListAsync<Invoice>(Queries.OverdueInvoices, prms, cancellation);
```

That is the whole model. The rest of this site explains why it is shaped this way and how to use it well.

## What ArgentSea is not

It is not an object-relational mapper in the Entity Framework sense. ArgentSea never generates SQL, never tracks changes, and never loads relationships lazily. Your SQL lives in the database or in files that ship with the application, and the parameters you pass and the result shape you receive are the contract between the two. That is a deliberate choice: it keeps the people who tune databases free to rewrite, hint, partition and re-index without a C# build, and it keeps the application's data surface auditable.

It is not a walled garden either. Setting parameters, executing a command and reading results are separate steps. You can use the Mapper to build parameters and run the command yourself, drop a parameter you do not want, or write a result handler against the raw `DbDataReader`. Anything ArgentSea does not cover, you do in plain ADO.NET with no penalty.

## Where it comes from

ArgentSea was built to run a high-volume, sharded commercial system. Version 1 ran in production for years and handled billions of dollars in transactions. Version 2 reworked the framework for .NET 10 and for Microsoft Orleans: it can now persist grain state in ordinary third-normal-form tables, provide cluster membership and reminders, and serve many tenant databases behind a single Orleans cluster. It is open source under the MIT license.

## Who it is for

ArgentSea fits teams that:

- are building something that must scale out, or want a data layer that will not have to be replaced when they do;
- run a multi-tenant product where each customer's data belongs in its own database;
- prefer stored procedures or static SQL, and want DBAs and developers to work independently;
- care about raw throughput and want the mapper out of the profiler;
- use Microsoft Orleans and want grain state they can query with SQL.

If your application has one database and a hundred users, ArgentSea will still serve you well, but its distinctive value appears the day your data outgrows a single server.

**Next:** [Why ArgentSea?](/introduction/why-argentsea)
