---
title: Why ArgentSea?
description: The problems ArgentSea exists to solve, from sharding and tenant isolation to read scale-out, and how it compares with Entity Framework Core and Dapper.
---

# Why ArgentSea?

Every data framework helps you read and write rows. ArgentSea exists because growth changes the problem: at some point the question is no longer *how do I query this table* but *how do I query this table when it lives in forty databases, in three regions, each with a replica*. ArgentSea is the .NET framework built around that question.

## The problem: data that outgrows one server

Internet-scale systems and multi-tenant SaaS products eventually meet one or more of these needs.

**Sharding.** The data set is too large, or too hot, for one server. Splitting it into segments that live on separate databases is the most cost-effective way to grow, and it enables active-active disaster recovery and regional data residency. The price is that the database engine can no longer enforce uniqueness or foreign keys across segments, and every query must know where to go.

**Tenant isolation.** A SaaS product has many customers, and each one's data should be isolated structurally, not by a `WHERE TenantId = @t` clause that one forgotten filter can defeat. A database per tenant gives you per-customer backups, service levels, replication and clean decommissioning. It also means a single application now manages a database per customer.

**Read scale-out.** Reads outnumber writes by orders of magnitude. Readable secondaries, hot standbys and read replicas can absorb them, but only if the application reliably sends reads and writes to different endpoints.

Each of these multiplies connections, complicates configuration, and demands concurrency that standard data access was never shaped for. Most frameworks assume one connection string and one database. Bolting these concerns on later means rewriting the data layer, which is why so many teams never do.

## The ArgentSea answer

ArgentSea treats data distribution as a day-one concern and gives every piece of the problem a first-class home.

| Need | What ArgentSea provides |
| --- | --- |
| Many connections, shared settings, secrets out of source | A [configuration hierarchy](/guides/configuration) where each property is set once and inherited, bound through .NET configuration providers |
| Reads on replicas, writes on the primary | A `Read` and a `Write` connection on every database and shard; you choose, explicitly, every time |
| A record identity that survives sharding | [`ShardKey`](/guides/sharding#shardkey), a compact compound key of shard id plus record key, with a URL-safe external form |
| Querying many databases as one | [Fan-out](/guides/sharding#fan-out) across a shard set: `ReadAll`, `ReadFirst` and `Write`, concurrent, with results merged |
| Surviving transient failures | Per-endpoint [retry and circuit breaking](/guides/resilience), on by default, always logged |
| Mapping without a performance tax | A [Mapper](/guides/mapping) that compiles attribute metadata into cached delegates |
| SQL that DBAs can own | [Queries](/guides/queries) as stored procedures or deployed statement files; no generated SQL |
| Orleans with a queryable store | The only [provider](/guides/orleans) that persists grain state in normalized tables instead of blobs, so SQL can query across grains; it also runs tenants behind one cluster |

The unifying idea is that **sharded and single-database calls have the same shape**: build parameters, name a query, pick `Read` or `Write`, hand the results to the Mapper. The queries, models, mapping attributes, configuration and habits you build against one database all carry over to a shard set.

That is not the same as saying the move is free, and it is worth being precise about what changes when a data set moves into a shard set:

- Every sharded record needs a shard-qualified identity. In practice that means adopting `ShardKey` on your models, in your stored procedures' parameters, and in the ids you expose through APIs and URLs. Existing data has to be re-keyed or mapped.
- Relationships that cross shards need design. A reference to a record on another shard must store that shard's id, and the database can no longer enforce it. Joins that used to span those tables become fan-out queries stitched together in code.
- New records need a placement rule, whether that is the default shard, a tenant, a region or a hash.

ArgentSea makes each of those a supported, well-trodden path rather than a research project, and the parts of your data layer that are not about keys stay as they are. Teams that start on one database and use `Read` and `Write` consistently also get read scale-out later as a pure configuration change. Sharding itself is a real design step; it is a much smaller one here than it would be on a framework that never anticipated it.

## How it compares

ArgentSea occupies a different spot than the frameworks most .NET developers reach for. It is fair to all three to say so plainly.

**Entity Framework Core** models your domain as entities and generates SQL from LINQ. It is excellent for rapid development against one database and for teams who want the ORM to own the schema. It does not shard, does not split reads from writes, and its generated SQL is hard for a DBA to tune in place. ArgentSea gives up LINQ and change tracking to keep SQL static and ownable, and gains sharding, tenanting and read scale-out as native features.

**Dapper** maps result rows to objects with very little ceremony and leaves everything else to you. ArgentSea's Mapper plays a similar role but also builds input and output parameters, handles nested models and multiple result sets, and is compiled rather than reflective. Around that mapper ArgentSea adds the configuration model, resilience, batches and the whole sharding layer that Dapper intentionally leaves out.

**Raw ADO.NET** is what ArgentSea is built on, and what you can always drop back to. ArgentSea removes the boilerplate and adds the distributed machinery, while keeping each step (parameters, execution, result handling) separate and compatible with ordinary ADO.NET types.

## What you give up

ArgentSea asks a few things of you, and it is better to know them now.

- You write SQL. Stored procedures on SQL Server, statement files or functions on PostgreSQL. If you want a framework to generate queries from an object model, this is not it.
- You choose read or write for every call. That explicitness is what makes read scale-out a configuration change later, but it is a habit to form.
- One project targets one provider. A model's attributes are SQL Server or PostgreSQL attributes, not both. Applications that need both platforms split along a service boundary.
- When you shard, key design becomes your responsibility. ArgentSea gives you `ShardKey` and the tools around it, but the engine no longer enforces uniqueness across shards.

In exchange you get a data layer that is fast, auditable, resilient, and ready for the day one database is no longer enough.

**Next:** [Core concepts](/introduction/core-concepts)
