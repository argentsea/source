---
title: Design principles
description: The seven principles that shape every ArgentSea design decision, and what each one means for the code you write.
---

# Design principles

ArgentSea's features are not a grab bag. They follow from seven principles that the framework applies consistently, and knowing them explains most of its choices, including the ones that look unusual at first. When you wonder "why doesn't it do X for me?", the answer is usually on this page.

## 1. Design for scale-out and tenant isolation first

Data distribution is a day-one concern, never a feature bolted on later. Sharding a large data set and isolating each tenant in its own database are two forms of the same problem, and they share the same machinery: a shard set that queries like a single database, a global record identity where the engine no longer enforces one, and explicit read and write connections from the start.

Neither form can be retrofitted cheaply. Sharding forces a rethink of keys and relationships, and tenant isolation must be a database boundary rather than a `WHERE` clause. Being explicit about topology and about read versus write from the start means that read scale-out later is a configuration change, and that adopting shard keys when the time comes changes your keys and cross-shard relationships rather than the whole data layer.

## 2. Native-code performance, no abstraction tax

The framework must never be the bottleneck. ArgentSea delivers ORM-level convenience at hand-optimized ADO.NET speed by compiling property-to-data mapping into cached expression-tree delegates, favouring stored procedures to cut parse and compile overhead, and running shard queries concurrently.

The only cost is a sub-second compilation the first time each model type is mapped, repaid on every call thereafter and reset only when the process restarts. That trade is why ArgentSea suits performance-critical applications even when they never shard.

## 3. Stay a thin, composable layer over ADO.NET

ArgentSea is a layer, not a walled garden. Setting parameters, invoking a query and collecting results are independent steps you can use, skip or override individually, and there is always an escape hatch to raw ADO.NET.

No framework anticipates every requirement. Because each step is separable and works on standard ADO.NET types, a unique case never becomes a dead end: build parameters with the Mapper and issue the command yourself, remove a parameter after mapping, or hand-write a result handler for a shape the Mapper does not model.

## 4. Loosely couple the application from the database

Keep SQL static, consolidated and segregated from application binaries, and strictly parameterized, ideally behind stored procedures. The parameter set and result shape are the contract; the implementation behind it can be tuned, rewritten, partitioned, hinted or re-indexed without breaking the application.

SQL generated from C# couples the application to table internals and blocks independent optimization over the application's lifetime. Static SQL lets the people who tune databases adapt it as data grows, surfaces orphaned queries through reference counts, and, with EXECUTE-only permissions, shrinks the attack surface to exactly what procedures expose.

## 5. Make many connections manageable and secure

Sharding, read/write splitting and multi-environment deployment can explode the number of connections to manage. ArgentSea tames this with a hereditary configuration hierarchy: every connection value is inherited from parent to child and overridable at any level, down to a single read or write endpoint, and bound through the .NET configuration system so secrets stay out of source and promotion across environments needs no file transforms.

A fleet of connections with mostly shared settings is unmanageable if each is configured independently. Inheritance collapses the redundancy, configuration providers keep passwords in secure stores, and connection properties remain updatable at run time so that application-level failover is possible.

## 6. Be resilient by default, and never hide it

Transient failures are recovered from out of the box, with automatic retries (configurable count, interval and backoff curve) and circuit breaking, provided through Polly and applied with sensible defaults unless overridden anywhere in the configuration hierarchy. Every retry and every breaker trip is logged.

An unbounded backlog of stalled connections can cascade into total failure, so the breaker fails fast and reopens automatically once a server recovers. Resilience that happens silently robs operators of diagnostics, which is why ArgentSea turns off the driver's hidden retry logic in favour of its own logged behaviour. Failover itself is left to the infrastructure, where it can be coordinated.

## 7. One shared core, thin provider-specific packages

Nearly all of ArgentSea's logic lives in one provider-neutral core and is exposed through thin packages: `ArgentSea.Sql` for SQL Server and `ArgentSea.Pg` for PostgreSQL. Each provider is supported fully on its own terms, dependencies stay minimal, and a model class targets exactly one provider.

Most behaviour is provider-agnostic and belongs in one place; only the ADO.NET-specific bindings differ. Mixing providers within one model would force the Mapper to guess which provider will execute its parameters, complexity that a review found exceeds its value. Separate projects or services are the clean answer when an application must reach both platforms.

## How the principles show up in practice

| You notice that… | Because of principle |
| --- | --- |
| You must choose `Read` or `Write` on every call | 1, 6 |
| The first query against a new model takes a moment | 2 |
| You can call the Mapper without using ArgentSea's data methods | 3 |
| There is no LINQ provider and no SQL generation | 4 |
| `appsettings.json` has no connection strings | 5 |
| Retries appear in your logs | 6 |
| Your model can have `MapToSql*` or `MapToPg*` attributes, not both | 7 |

**Next:** [Architecture](/introduction/architecture)
