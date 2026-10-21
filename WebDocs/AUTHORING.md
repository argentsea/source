# ArgentSea WebDocs — authoring brief

This file is the shared brief for everyone (human or agent) writing pages in `WebDocs/docs`. It records the
site's information architecture, the facts the pages must agree on, and the style rules. Read it fully before
writing a page.

## Audience and purpose

The reader is a working .NET developer who understands databases (SQL, ADO.NET, maybe EF Core or Dapper) but has
**never heard of ArgentSea**. Every page must answer, in order: *what is this, why would I want it, how do I use it*.
The site is an **introduction and overview**, not an API reference. Do not enumerate every attribute, overload or
option; show the one or two that teach the idea and link to the glossary or the source for the rest.

ArgentSea's elevator pitch (keep these claims consistent everywhere):

- A .NET data-access framework built on ADO.NET for **simplicity, high performance and global scale**.
- The one .NET framework **designed from the ground up for scale-out**: sharding a large data set across servers,
  or giving every cloud **tenant** a database of its own. Both use the same machinery (shard sets, shard keys, fan-out).
- **Great even when you don't shard**: compiled attribute mapping at hand-written ADO.NET speed, a replacement for
  connection strings that keeps secrets out of source, built-in retry and circuit breaking, structured logging.
- A **thin, composable layer** over ADO.NET: parameters, execution and result handling are separate steps you can
  use, skip or override; there is always an escape hatch to raw ADO.NET.
- **Static SQL, loosely coupled**: SQL lives in stored procedures (SQL Server) or deployed `.sql`/`.psql` files
  (PostgreSQL), never generated from C#; the parameter set and result shape are the contract.
- A first-class **Microsoft Orleans provider**: persists grain state in ordinary third-normal-form tables (so grain
  data can be queried across grains with SQL), and provides clustering and reminders. ShardKey values serialize as
  grain keys. Uniquely, it can run tenants/shards behind a single cluster.
- MIT licensed. Version 1 ran in production for years and handled billions of dollars of transactions. Version 2 is
  current (2.1.0), targets .NET 10.

Supported platforms: SQL Server (incl. Express, Azure SQL Database; readable secondaries / read scale-out) via
`ArgentSea.Sql` on Microsoft.Data.SqlClient; PostgreSQL (incl. Amazon RDS/Aurora, Azure Database for PostgreSQL,
Google Cloud SQL; hot standbys / read replicas) via `ArgentSea.Pg` on Npgsql. Orleans packages: `ArgentSea.Orleans`
(abstract), `ArgentSea.Orleans.Sql` (SQL Server implementation) and `ArgentSea.Orleans.Pg` (PostgreSQL implementation).

## Site map (URL = file under `docs/`, no extension)

| URL | Page | Owner |
| --- | --- | --- |
| `/` | Home (hero, pitch, feature cards, code teaser) | lead |
| `/introduction/what-is-argentsea` | What is ArgentSea? | lead |
| `/introduction/why-argentsea` | The problems it solves (sharding, tenanting, read scale-out) and how it compares | lead |
| `/introduction/core-concepts` | The mental model: Databases & ShardSets, Read/Write pairs, Query, Mapper, ShardKey, fan-out, resilience, batches | lead |
| `/introduction/principles` | The seven design principles | lead |
| `/introduction/architecture` | Packages, the provider seam, Orleans layering | lead |
| `/getting-started/installation` | Packages, service registration, dependencies | agent A |
| `/getting-started/quickstart-database` | First query against one database (config → model → query → call) | agent A |
| `/getting-started/quickstart-sharding` | First shard set: config, ShardKey, fan-out | agent A |
| `/getting-started/testing` | Unit testing with mock results | agent A |
| `/guides/configuration` | Hereditary configuration hierarchy, secrets, read/write endpoints, environments | agent B |
| `/guides/resilience` | Retry, backoff, circuit breaker, failover stance | agent B |
| `/guides/logging` | Structured, tiered logging and what to watch | agent B |
| `/guides/queries` | QueryProcedure vs QueryStatement, the Queries class, parameter names, the query paradigm, data methods | agent C |
| `/guides/mapping` | Attributes, nulls, required, nested models, result-set lists, TVP and COPY | agent C |
| `/guides/sharding` | Shard sets, shard keys, external keys, foreign shards, ReadAll/ReadFirst/Write, ShardsValues, shard id parameter | agent D |
| `/guides/batches` | Batches and transactions | agent D |
| `/guides/orleans` | Orleans grain persistence, clustering, reminders | agent E |
| `/reference/glossary` | Generated from `specs/glossary` (do not hand-edit) | script |
| `/reference/faq` | FAQ, updated | agent E |
| `/reference/whats-new` | What changed in version 2; migrating from version 1 | agent E |
| `/reference/platforms` | Supported databases, .NET versions, package matrix | agent E |

Only link to URLs in this table. Dead links fail the build.

## Facts the pages must agree on (current code, not the old DocFx docs)

The old DocFx tutorials are a good source for *explanations* but are outdated on several APIs. Where they conflict
with this list, this list wins. When in doubt, grep the source under `ArgentSea.Shared/src`, `ArgentSea.Sql/src`,
`ArgentSea.Pg/src`, `ArgentSea.Orleans/src`, `ArgentSea.Orleans.Sql/src` (ignore `obj/` and `.agentic/`), the test
projects, and the spec corpus digests.

**Packages / versions.** `ArgentSea` (core, installed transitively), `ArgentSea.Sql`, `ArgentSea.Pg`,
`ArgentSea.Orleans`, `ArgentSea.Orleans.Sql`, `ArgentSea.Orleans.Pg`; all 2.1.0; `net10.0`; Microsoft.Data.SqlClient 7.1.1; Npgsql 10.0.3;
Microsoft.Orleans 10.4.0; Polly 8.8.0; Microsoft.Extensions.* 10.0.13. Namespaces: `ArgentSea`, `ArgentSea.Sql`, `ArgentSea.Pg`, `ArgentSea.Orleans`.

**Service registration.** `services.AddSqlServices(configuration)` / `services.AddPgServices(configuration)`
(extension methods in `Microsoft.Extensions.DependencyInjection`). They bind the options and register the singletons
`SqlDatabases` + `SqlShardSets` (or `PgDatabases` + `PgShardSets`). Inject those into your data classes. An
`ILogger` must be available (every data method and Mapper call takes one; logging is mandatory).

**Configuration sections.** SQL Server: `SqlGlobalSettings` (object), `SqlDbConnections` (array of databases,
each with `DatabaseKey`), `SqlShardSets` (array, each with `ShardSetName`, optional `DefaultShardId`, optional
`Read`/`Write` sections, and `Shards` array each with `ShardId`). PostgreSQL: `PgGlobalSettings`, `PgDbConnections`,
`PgShardSets`. Any connection property (`DataSource`/`InitialCatalog`/`UserName`/`Password`/`WindowsAuth`/
`ApplicationIntent`… for SQL; `Host`/`Database`/`UserName`/`Password`… for Pg) can be set at any level and is
inherited downward: global → database → `ReadConnection`/`WriteConnection`; global → shard set → shard set `Read`/
`Write` → shard → shard `ReadConnection`/`WriteConnection`. Resilience properties live in the same hierarchy:
`RetryCount`, `RetryInterval` (ms), `RetryLengthening` (`Linear`, `Lengthening` (default), `HalfSquare`, `Squaring`;
`Fibonacci` is an obsolete alias of Lengthening), `CircuitBreakerFailureCount`, `CircuitBreakerTestInterval` (ms).
Secrets (passwords) go in User Secrets / Key Vault / environment variables, merged by .NET configuration.

**Collections and connections.** `SqlDatabases["MyDb"]` (exact-match key) returns a database with `.Read` and
`.Write` connections. `SqlShardSets["MyShardSet"]` returns a shard set with `.ReadAll`, `.ReadFirst`, `.Write`,
`.DefaultShard`, an indexer by `short shardId` or by `IShardKey`, and `.MockResults`. A shard (`ShardInstance`) has
`.Read` and `.Write`. The caller always chooses Read or Write; ArgentSea never infers it and never falls back.

**Data methods** (all take a `Query`, a `DbParameterCollection` (may be null), and a `CancellationToken`):
`RunAsync`, `ReturnValueAsync`, `ListAsync<TValue>(…, columnName, …)`, `QueryAsync` (custom result handler),
`MapListAsync<TModel>`, `MapReaderAsync<TModel[, TList1 … TList8]>`, `MapOutputAsync<TModel[, TList1 …]>`. Version 2
adds overloads whose first argument is an existing `TModel instance` to hydrate in place. On a shard set, `ReadAll`
returns `List<TModel>` combined from all shards, `ReadFirst` returns the first non-null result, `Write.RunAsync`
runs on every shard. Shard-set overloads accept an optional `ShardsValues` (which shards to hit, with optional
per-shard parameter overrides) and an optional `shardParameterName` (ArgentSea sets that parameter to each shard's
id). Batches: `database.Write.RunAsync(batch, cancellationToken)`.

**Query objects.** `new QueryProcedure("ws.GetCustomer")` or `new QueryProcedure("ws.GetCustomer", new[] { "CustomerId" })`
(second argument = parameter names to send; omitted = send all). `QueryStatement.Create("GetCustomer")` returns
`Lazy<QueryStatement>`; the SQL file is `SQL/GetCustomer.sql` (or `.psql` once Pg services are registered; a host
with both providers uses `SqlQueryStatement` / `PgQueryStatement`). Convention: one static `Queries` class exposing
every query as a static property, so usage is visible by reference count. Procedures are preferred for SQL Server;
statement files for PostgreSQL (Npgsql runs a Pg `QueryProcedure` as `CALL`, so it calls procedures only; read rows from a function through a statement file).

**Parameters.** `var prms = new ParameterCollection();` then provider extension methods such as
`prms.AddSqlIntInputParameter("@CustomerId", id)`, `prms.AddSqlNVarCharOutputParameter("@Name", 255)` (Pg:
`AddPgIntegerInputParameter`, `AddPgVarcharInputParameter`, …; check `PgParameterCollectionExtensions.cs` for exact
names). The `@` prefix is optional for SQL Server. Parameters are declared before any connection exists and are
copied to every connection, so never keep a reference to an output parameter object expecting it to be filled.

**Mapper.** Provider attributes on model properties: `[MapToSqlInt("@CustomerId")]`, `[MapToSqlNVarChar("@Name", 255)]`,
`[MapToSqlDateTime2("@Created")]`, `[MapToSqlUniqueIdentifier(...)]`, `[MapToSqlDecimal("@Price", 9, 2)]`;
PostgreSQL `[MapToPgInteger("customer_id")]`, `[MapToPgVarchar("name", 255)]`, `[MapToPgTimestamp(...)]`,
`[MapToPgUuid(...)]`, `[MapToPgNumeric("price", 9, 2)]`, `[MapToPgArray(...)]`, `[MapToPgHstore(...)]`. The .NET
type must match the attribute's database type exactly (no casting). Optional trailing `required: true` flag: a null
in that column means "no record" (single result → null, list → row omitted). Nullable value types and nullable enums
map null ↔ DBNull; `Guid.Empty`, `double.NaN`, `float.NaN` are null sentinels on non-nullable properties. Nested
models: `[MapToModel]` on a property whose type is itself mapped (flattened into the same parameters/columns).
Result-set list properties: `List<T>` properties filled by extra result sets, in the order of the generic arguments,
no attribute needed. Mapper extension methods: `prms.CreateInputParameters(model, logger)`,
`prms.CreateOutputParameters<TModel>(logger)`, `prms.ToModel<TModel>(logger)`, `rdr.ToModel<TModel>(logger)`,
`rdr.ToList<TModel>(logger)`. First use of a model type compiles expression trees (sub-second), cached per process.
Missing columns are skipped and logged at Debug; type mismatches throw. SQL Server: table-valued parameters from a
`List<TModel>` (`AddSqlTableValuedParameter`, empty list → null parameter). PostgreSQL: COPY bulk load as a batch
step (`batch.Add(models, "table_name")`).

**Shard keys.** `ShardKey<TRecord>`, `ShardKey<TRecord, TChild>`, `ShardKey<TRecord, TChild, TGrandChild>`,
`ShardKey<TRecord, TChild, TGrandChild, TGreatGrandChild>`. Immutable struct. **Shard id is always `short`** (not a
generic parameter). **There is no DataOrigin any more** (version 1 had one); version 2 embeds *type metadata* in
every serialized key instead. Construct with `new ShardKey<int>(shardId, recordId)`. `ShardKey<int>.Empty`,
`.IsEmpty`, `.ShardId`, `.RecordId` (`.ChildId`…), `.ToExternalString()` / `ShardKey<int>.FromExternalString(s)`
(URL-safe, obfuscated, checksummed; not encryption, not authorization), `.ToUtf8()`, `.ToArray()`, `.ToString()` (JSON-ish).
Use `Nullable<ShardKey<…>>` for nullable columns. Attribute: `[MapSqlShardKey("@CustomerId")]` plus the record-id
column's own `[MapToSqlInt("@CustomerId")]` with the exact same name; `[MapSqlShardKey("@CustomerShardId", "@CustomerId")]`
reads the shard id from a column (for foreign-shard references); child columns follow:
`[MapSqlShardKey("@OrderShardId", "@OrderId", "@OrderItemId")]`. To make the key required (a null record id means
"no record"), set the `required` flag on the record-id column's own mapping attribute, e.g.
`[MapToSqlInt("@CustomerId", true)]`; the shard-key attribute's trailing `bool` (`isRecordIdentifier`) is stored but
not read by the current Mapper.
Pg: `MapPgShardKey` with the same shapes. `IKeyedModel<TRecord>` (property `Key`) unlocks `ShardKey<T>.Merge(...)`,
`ForeignShards(...)`, `ToShardsValues(...)`. `GhostShardKey` carries a serialized key whose types are unknown; it cannot
reconstruct a key with an enum-typed part (the metadata records only the enum's underlying width, so `GetInstance` throws
`InvalidShardKeyMetadataException` for such keys; deserialize them with the original `ShardKey<...>` type).
Shard id comes from client configuration only; a misconfigured shard id silently writes to the wrong database, so
consider passing the shard id into SQL (`shardParameterName`) and verifying it.

**Fan-out.** ReadAll: read connections of every targeted shard, waits for all, concatenates non-null results.
ReadFirst: first non-null result wins, remaining shard queries cancelled (for lookups by a unique non-key attribute,
e.g. login name). Write: write connection of every targeted shard. Each shard runs on its own connection, its own
parameter copy, its own resilience policy, concurrently. Result handlers must be thread-safe.

**Resilience.** Per-endpoint Polly policy: retry on transient errors (provider classifies) with backoff, wrapped by
a circuit breaker. Defaults apply when nothing is configured. Driver-level retries are disabled so every retry is
logged. Failover is left to infrastructure; connection properties can be updated at run time and propagate.

**Batches.** `DatabaseBatch<TResult>`, `ShardBatch<TResult>`, `ShardSetBatch`. Steps run on one connection inside
one client transaction; the only place ArgentSea opens a client transaction. No distributed transactions; a shard-set
batch runs an independent transaction per shard. Custom `BatchStep` for bulk copy/COPY inside the transaction.

**Known code gaps found while documenting** (do not document as working): the circuit-breaker log events (event
id 11) were defined but never emitted up to 2.0.4 (fixed on branch claude/zealous-ramanujan-46ec33, commit 233758b0,
which also deletes the unused event 10 `CommandRetry`); `PgConnectionConfiguration.DefaultConnectTimeout` was declared but
never applied (now applied as the two-second connect timeout unless a configuration level sets Timeout).

**Mock results.** `MockResults` dictionary (on a database connection, shard, or shard set) keyed by Query name:
when a data method runs a Query whose name is present, the stored object is returned and no database is touched.

**Orleans.** `ArgentSea.Orleans.Sql`: `siloBuilder.AddArgentSeaDbOrleansGrainStorage("Common", o => o.Queries.Add("UserGrain", Queries.UserQueries))`
and `AddArgentSeaShardOrleansGrainStorage("Tenant", o => …)`; `OrleansDbQueryDefinitions("UserGrain", readQuery, QueryResultFormat.ResultSet|OutputArgs, writeQuery, clearQuery)`
and `OrleansShardQueryDefinitions(...)`; `siloBuilder.UseArgentSeaClustering()`; `clientBuilder.UseArgentSeaClustering(...)`;
`siloBuilder.UseArgentSeaReminderService(...)`; register `ShardKeyOrleansSerializer<T>` as `IFieldCodec<ShardKey<T>>`
and `IDeepCopier<ShardKey<T>>` so shard keys work as grain keys; options `OrleansDbPersistenceOptions`,
`OrleansShardPersistenceOptions` (`ShardSetKey`, `DatabaseKey`, `ValidateGrainKeys`); for clients the
`ClusterClientDbOptions` record (`DatabaseKey` default "Common", `GatewayListQuery` default
`GatewayListQuery` with no default in the shared record: each package's gateway list provider uses its own `Queries.OrleansClusterGatewayList` when it is unset, `MaxRefreshInterval`). Configuration-driven provider types are `ArgentSeaSql` (shard-set storage, clustering, reminders) and `ArgentSeaSqlDb` (single-database storage); the PostgreSQL package registers `ArgentSeaPg` and `ArgentSeaPgDb`, and its extensions are Pg-named (`AddArgentSeaPgDbOrleansGrainStorage`, `AddArgentSeaPgShardOrleansGrainStorage`, `UseArgentSeaPgClustering`, `UseArgentSeaPgReminderService`); its scripts `common.psql` / `tenant.psql` are packed in the NuGet `sql` folder. The readme's `ClusteringClientOptions` section name is not
bound by current source. Scripts `common.sql` / `tenant.sql` live in the ArgentSea.Orleans.Sql source (they are not
packed into NuGet) with `rdr`/`wtr` schemas. Grain state is read/written through
per-grain-type Query objects mapped by the Mapper; a missing row yields default-constructed state. See
`ArgentSea.Orleans.Sql/src/readme.md` and `ArgentSea.Orleans/test/ArgentSea.Orleans.Test/Queries.cs`.

**What changed in version 2** (for `/reference/whats-new`): .NET 10 and current drivers; Orleans support
(persistence in 3NF tables, clustering, reminders); instance hydration (populate an existing object); shard id fixed
as `short` (no longer generic); DataOrigin removed in favour of embedded key type metadata; faster shard key
serialization with UTF-8 and binary forms; external keys obfuscated + checksummed; `QueryStatement` file queries;
mock results; structured `LoggerMessage` logging with fixed event ids; Polly 8; zero-row TVP sent as null.

## Style rules

- Front matter on every page: `title`, `description` (one sentence, used for SEO and cards).
- Open with one or two sentences that say what the page is about and why the reader cares. No throat-clearing.
- Short paragraphs; one idea per paragraph. Headings every 150–300 words. Prefer `##` and `###` only.
- Teach with **one representative example**, then generalise in a sentence. Use realistic domain names
  (customers, orders, tenants, subscribers), never `Foo`.
- Show both providers where syntax differs, using a VitePress code group:

  ````md
  ::: code-group
  ```csharp [SQL Server]
  [MapToSqlInt("@CustomerId")]
  ```
  ```csharp [PostgreSQL]
  [MapToPgInteger("customer_id")]
  ```
  :::
  ````

  When the code is identical for both, show it once.
- Use VitePress containers sparingly: `::: tip`, `::: warning`, `::: danger`, `::: info`, `::: details Title`.
- Links: root-absolute without extension, e.g. `[the Mapper](/guides/mapping)`; anchors `(/guides/mapping#nulls)`.
  External links to the repo: `https://github.com/argentsea/source`. NuGet:
  `https://www.nuget.org/packages/ArgentSea.Sql/`, `https://www.nuget.org/packages/ArgentSea.Pg/`.
- Length: 700–1,600 words per page. If you need more, you are writing minutiae; cut and link.
- Terminology: "shard set", "shard key", "shard id", "read connection"/"write connection", "fan-out", "tenant",
  "the Mapper", "Query" (the object), "stored procedure" / "statement file". ArgentSea is always spelled that way.
- Code must compile against the current API listed above. C# examples use modern style (file-scoped namespaces not
  required; `var`, async/await, `CancellationToken cancellation`). Keep examples under ~40 lines.
- Do not invent features, numbers, or benchmarks. Do not promise roadmap items. Do not use "simply" or "just".
- End each page with a short "Next" line linking onward (`**Next:** [Mapping](/guides/mapping)`), except reference pages.
