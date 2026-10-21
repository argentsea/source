---
title: FAQ
description: Straight answers to the questions developers ask when they first meet ArgentSea, from "is it an ORM?" to replication lag, transactions, Orleans and testing.
---

# Frequently asked questions

Short answers to the questions that come up most often, with links to the pages that go deeper.

## Is ArgentSea an ORM?

No. ArgentSea maps **parameters and results**, not objects to tables. It never generates SQL, never tracks entity changes and never decides how your schema looks. Your SQL lives in stored procedures or statement files; the parameter set and result shape are the contract, and [the Mapper](/guides/mapping) moves values between that contract and your model properties.

Compared with **EF Core**: EF Core owns the schema, generates queries from LINQ and tracks changes, which is productive for CRUD-heavy applications with one database. ArgentSea gives up query generation in exchange for static, DBA-reviewable SQL, hand-written-ADO.NET speed and native fan-out across shards. EF Core has no concept of a shard set; ArgentSea was designed around one.

Compared with **Dapper**: both are thin layers over ADO.NET that map results to objects, and both leave SQL to you. Dapper maps by convention at run time; ArgentSea maps by attribute through compiled expression trees, adds output-parameter and table-valued-parameter mapping, and layers on configuration, resilience, logging, sharding and batches that Dapper leaves to the application.

## Do I need to shard to benefit?

No. Most of ArgentSea is useful against a single database: compiled attribute mapping, a [configuration hierarchy](/guides/configuration) that keeps passwords out of connection strings, separate read and write connections for read scale-out, [retry and circuit breaking](/guides/resilience), [structured logging](/guides/logging) and [mock results](/getting-started/testing). Sharding is there when you need it. Moving a database into a shard set later is a real change, because every sharded record needs a `ShardKey` and cross-shard relationships need deliberate design, but the queries, models, mapping and calling pattern you already have carry over, so it is a change to your keys rather than a new data layer. [Why ArgentSea?](/introduction/why-argentsea) spells out what the move involves.

## How does ArgentSea pick read versus write?

It does not; you do. Every database and shard exposes a `Read` connection and a `Write` connection, and every data call names one. ArgentSea never infers intent from the SQL and never falls back from one to the other. If you have no read replica, point both at the same server and the choice costs nothing.

## What about replication lag?

A read connection that targets a readable secondary or hot standby may be slightly behind the primary. ArgentSea does not hide this. The usual pattern is to read through `Write` immediately after a write when the user must see their own change, and through `Read` everywhere else. Where consistency matters more than scale, configure the read connection to the primary.

## Does it do distributed transactions?

No. The only place ArgentSea opens a client transaction is a [batch](/guides/batches), and a batch runs on one connection. A shard-set batch runs an independent transaction per shard; if one shard fails, the others are not rolled back. Design cross-shard operations to be idempotent or compensating.

## What happens if ArgentSea is not flexible enough?

ArgentSea is a thin layer over ADO.NET, and the steps are separable: declare parameters, run the Query, handle the results. Skip or replace any step that has unusual needs and keep the rest. If the Mapper adds a parameter you do not want, remove it from the collection after mapping. If you need a result shape the Mapper cannot build, pass your own handler to `QueryAsync`. If you need raw ADO.NET, the provider connection is yours to use.

## Can I use PostgreSQL and SQL Server in the same project?

No. A model and an application target exactly one provider. The Mapper does not know which provider will execute the parameters it creates, so attributes from two providers on one model would be ambiguous, and the provider-specific delegates would have to be kept apart. Both obstacles could be overcome, at a cost in complexity that has not been justified so far.

## Why stored procedures and statement files instead of generated SQL?

Static SQL is faster to execute, easier to manage and safer. A procedure is not re-parsed on every call, and on SQL Server compiled plans can be a dramatic win. When the application has only EXECUTE permission, a DBA can see exactly which tables and columns the application touches, refactor the schema with confidence, and hint, rewrite or re-index a bad plan without a code release. With dynamic SQL, finding and fixing that plan means traces and a deployment. On PostgreSQL the same idea is delivered by deployed `.psql` statement files; see [Queries](/guides/queries).

## How do I keep my data secure?

Start with a knowledgeable DBA. ArgentSea helps in three ways: the configuration design keeps passwords out of connection strings and source control; parameterized procedures and statements leave no room for SQL injection; and running with EXECUTE-only permissions means no user can reach an operation that a procedure does not expose.

## Is the external key secure?

No, and it is not meant to be. `ToExternalString()` produces a URL-safe form of a shard key that is obfuscated and checksummed, so a mistyped or hand-edited value is rejected rather than silently addressing another record, and record numbering is not exposed in routes. It is not encrypted or signed; anyone can decode or forge one. Never use an external key as proof that the caller may access the record. Authorize every request in the application.

## Can I use it with Orleans?

Yes. `ArgentSea.Orleans.Sql` (SQL Server) and `ArgentSea.Orleans.Pg` (PostgreSQL) store grain state in third-normal-form tables through per-grain-type read, write and clear Queries, so grain data can be queried across grains with SQL. It also provides cluster membership, the gateway list and reminders, and it can persist each tenant's grains in that tenant's own database behind one cluster. See [Orleans](/guides/orleans).

## What .NET versions are supported?

Version 2 targets .NET 10. Version 1 targeted .NET Standard; if you are on it, see [What's new](/reference/whats-new) for the migration notes. Full platform details are on the [Platforms](/reference/platforms) page.

## How do I test without a database?

Every database connection, shard and shard set has a `MockResults` dictionary keyed by Query name. When a data method runs a Query whose name is present, the stored object is returned and no connection is opened. Everything before the call, including parameter creation and mapping, still runs, so tests exercise your real data-access code. See [Testing](/getting-started/testing).

## Where did DataOrigin go?

It was removed in version 2. The DataOrigin character tagged a key with its source table, but it did not help in real applications and added overhead everywhere keys were declared. Version 2 embeds compact type metadata in every serialized key instead, so a key deserialized as the wrong generic type fails and a `GhostShardKey` can rebuild a key whose types are not known in advance. Migration steps are on the [What's new](/reference/whats-new) page.

## How fast is the Mapper?

Fast enough that you should not need to hand-write ADO.NET for performance. The first time a model type is mapped for a given purpose, ArgentSea reads its attributes by reflection, builds an expression tree and compiles it to a delegate; that first call costs reflection plus compilation, typically under a second, and is logged at Debug level. The compiled delegate is cached for the life of the process, so every later call runs at compiled-code speed with no per-call reflection. We do not publish benchmark numbers; measure on your own models and compare with the hand-written equivalent.

## Where do I report a problem or contribute?

The source lives at [github.com/argentsea/source](https://github.com/argentsea/source). Contributions are welcome under the MIT licence.
