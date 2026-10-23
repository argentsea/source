---
title: Glossary
description: Definitions of every term ArgentSea uses, from shard set and shard key to fan-out, resilience strategy and grain persistence.
outline: false
---

# Glossary

ArgentSea has a small, precise vocabulary. This page is generated from the project's specification corpus, so it is the authoritative meaning of each term as the current code uses it.

<GlossaryIndex :letters='["B","C","D","E","F","G","I","K","M","N","O","P","Q","R","S","T","V"]' />

## B {#letter-b}

### Batch {#batch}

<p class="glossary-synonyms">Also: <code>query batch</code>, <code>DatabaseBatch</code>, <code>ShardBatch</code>, <code>ShardSetBatch</code></p>

An ordered list of batch steps that ArgentSea executes on one connection inside one client-managed ADO.NET transaction, committed only when every step succeeds. There are three kinds. A database batch runs on a non-sharded connection; a shard batch runs on one shard. Both may return a result: a value, a list of values, a model, or one or more shard keys. A shard-set batch runs the same steps on every targeted shard, each shard in its own transaction, and returns no result. The batch is the only place ArgentSea enlists a client-side transaction. Its main purpose is to combine non-query work, such as a bulk load, with queries in one transaction.

### Batch step {#batch-step}

<p class="glossary-synonyms">Also: <code>BatchStep</code></p>

One unit of work in a batch, implementing the abstract `BatchStep`. Most steps execute a Query; custom steps allow non-query operations, such as a bulk copy or COPY load, inside the batch transaction. When several steps produce a result, the batch returns the last non-null, non-default one.

## C {#letter-c}

### Child id {#child-id}

<p class="glossary-synonyms">Also: <code>ChildId</code>, <code>GrandChildId</code>, <code>GreatGrandChildId</code>, <code>grandchild id</code>, <code>great-grandchild id</code></p>

A subordinate key component (`ChildId`, `GrandChildId`, `GreatGrandChildId`) of a multi-column `ShardKey`, representing composite primary-key columns beneath `RecordId` within the same shard.

### Circuit breaker {#circuit-breaker}

<p class="glossary-synonyms">Also: <code>circuit breaking</code></p>

The fail-fast stage of the resilience strategy. After `CircuitBreakerFailureCount` consecutive failures on a data connection, further calls on that connection fail immediately instead of queuing behind a database that is unlikely to answer. After `CircuitBreakerTestInterval` milliseconds a single trial call is allowed through; if it succeeds, normal traffic resumes. Each read or write endpoint has its own breaker. Configuring a failure count below 1 disables it.

### Configuration hierarchy {#configuration-hierarchy}

<p class="glossary-synonyms">Also: <code>Hereditary Configuration Hierarchy</code>, <code>global settings</code></p>

ArgentSea's replacement for connection strings. Every connection attribute is a discrete property that can be set at any level of the hierarchy and is inherited by the levels below unless overridden there. A non-sharded database inherits global → database → read/write endpoint. A shard set inherits global → shard set → the shard set's `Read` or `Write` section → shard → read/write endpoint. This lets one value, such as a password, a login or a packet size, serve many connections, and lets different configuration providers supply different properties. The layout of the hierarchy is fixed once it is built, but property values remain updatable at run time and changes propagate to inheriting children.

### Connection description {#connection-description}

<p class="glossary-synonyms">Also: <code>connectionDescription</code>, <code>ConnectionDescription</code></p>

A human-readable identifier of a data connection that ArgentSea passes to result handlers and includes in its log events and errors. Because one query may run concurrently on many connections, it is the information that ties a failure or log line to the shard and endpoint that produced it.

### COPY load {#copy-load}

<p class="glossary-synonyms">Also: <code>CopyMapper</code>, <code>binary import</code>, <code>NpgsqlBinaryImporter</code></p>

PostgreSQL bulk loading in which ArgentSea streams a list of models into a table or temporary table with `COPY` through Npgsql's binary importer. The column definitions are derived from the models' mapping attributes. It runs as a batch step, so a following step can process the loaded rows in the same transaction.

## D {#letter-d}

### Data connection {#data-connection}

<p class="glossary-synonyms">Also: <code>DataConnection</code>, <code>ShardDataConnection</code>, <code>endpoint</code></p>

One read or write endpoint of a database or shard: the object on which data methods are invoked. It owns its resolved connection settings, which are derived from the configuration hierarchy and recomputed when an inherited property changes, together with its own resilience strategy and its mock results. Every command it runs opens a fresh pooled provider connection.

### Data provider services {#data-provider-services}

<p class="glossary-synonyms">Also: <code>IDataProviderServiceFactory</code>, <code>DataProviderServiceFactory</code></p>

The provider seam, `IDataProviderServiceFactory`, through which the provider-neutral core does all platform-specific work: creating connections and commands, copying a parameter collection onto a command (applying query parameter names and per-shard overrides), and classifying transient errors. Each database provider implements it once.

### Database provider {#database-provider}

<p class="glossary-synonyms">Also: <code>provider package</code>, <code>ArgentSea.Sql</code>, <code>ArgentSea.Pg</code></p>

One of ArgentSea's database-platform packages: ArgentSea.Sql (SQL Server, over SqlClient) or ArgentSea.Pg (PostgreSQL, over Npgsql). Each supplies the platform's configuration options and global, database and shard-set sections, its mapping attributes, typed parameter extension methods, data provider services and service registration, all on top of the shared provider-neutral core (ArgentSea). A model's attributes belong to exactly one provider.

### Databases collection {#databases-collection}

<p class="glossary-synonyms">Also: <code>Databases</code>, <code>SqlDatabases</code>, <code>PgDatabases</code>, <code>DatabasesBase</code>, <code>database key</code></p>

The injectable singleton (`SqlDatabases`, `PgDatabases`; base `DatabasesBase`) that holds every configured non-sharded database, keyed by its `DatabaseKey`. Lookup requires an exact match on casing, accents and kana. Each entry exposes a read/write connection pair. Its membership is fixed when it is built from configuration.

### Default shard {#default-shard}

<p class="glossary-synonyms">Also: <code>DefaultShard</code>, <code>DefaultShardId</code></p>

The shard, named by a shard set's `DefaultShardId` setting, that a client uses as the destination for new records. It is reached through the shard set's `DefaultShard` property. Because the setting is per client configuration, different clients (for example, different regions) can default to different shards.

## E {#letter-e}

### External key {#external-key}

<p class="glossary-synonyms">Also: <code>external string</code>, <code>external identifier</code></p>

The URL-safe string form of a shard key, produced by `ToExternalString()` and reversed by `FromExternalString()`. It is used to carry a sharded record identity as a single value across REST routes, JSON payloads and similar boundaries. The string is obfuscated and carries a short checksum, so accidental alteration or casual tampering is detected on parsing. It is not encryption or a signature and must not be relied on for authorization.

## F {#letter-f}

### Fan-out query {#fan-out-query}

<p class="glossary-synonyms">Also: <code>shard-set query</code>, <code>cross-shard query</code></p>

A query or batch executed concurrently across every shard of a `ShardSet` (or the subset specified by `ShardsValues`), where each shard runs on its own connection with its own cloned parameter set under its own resilience policy. `ShardSet` provides three fan-out modes: `ReadAll` (combines all non-null shard results), `ReadFirst` (returns the first non-null result and cancels remaining shards), and `Write` (executes on shard write connections and lets all shards finish).

### Foreign shard {#foreign-shard}

<p class="glossary-synonyms">Also: <code>ForeignShards</code></p>

Relative to a record, any shard other than the one that holds it. A record that refers to a record on a foreign shard must store that record's shard id alongside its key, and no database-enforced foreign key can cover the relationship. ArgentSea helps locate such references: it can collect the foreign shards named by a list of keys or keyed models into a shards values argument for a targeted fan-out query, and the Mapper can read a foreign shard id from a column named in a shard key mapping.

## G {#letter-g}

### Ghost shard key {#ghost-shard-key}

<p class="glossary-synonyms">Also: <code>GhostShardKey</code></p>

A holder for serialized shard-key data whose generic key types are not known where it is handled. On request, it reads the embedded shard key type metadata and reconstructs the matching ShardKey instance. Construction is deliberately cheap, so a key can be carried, for example through logging, without paying for deserialization unless it is actually used.

### Grain persistence {#grain-persistence}

<p class="glossary-synonyms">Also: <code>ArgentSeaDbGrainPersistence</code>, <code>ArgentSeaShardGrainPersistence</code>, <code>grain storage provider</code></p>

ArgentSea's Orleans grain storage provider. It reads, writes and clears grain state through per-grain-type queries mapped by the Mapper to ordinary relational tables (third normal form), rather than to serialized state blobs, so grain data can be queried across grains with SQL. It has two variants: on a single database (`ArgentSeaDbGrainPersistence`), or on a shard set (`ArgentSeaShardGrainPersistence`), where the shard is resolved from the grain's key, a serialized ShardKey. When no row exists, the state is created by default construction or by the model's `IActivator` factory.

### Grain query definitions {#grain-query-definitions}

<p class="glossary-synonyms">Also: <code>OrleansDbQueryDefinitions</code>, <code>OrleansShardQueryDefinitions</code>, <code>QueryResultFormat</code></p>

The per-grain-type registration (`OrleansDbQueryDefinitions`, `OrleansShardQueryDefinitions`) of the read, write and clear Query objects that grain persistence uses for that grain type. It also specifies whether the read query returns state as output parameters or as a result set. It is registered in the persistence options keyed by grain type; a grain type with no registration fails on first use.

## I {#letter-i}

### Instance hydration {#instance-hydration}

<p class="glossary-synonyms">Also: <code>populate existing instance</code></p>

Populating a caller-supplied, already-existing model instance with query results in place instead of constructing a new model instance. This allows objects whose lifecycle ArgentSea does not own (such as Orleans grain state) to be hydrated directly; result handlers therefore receive the target model instance as their first argument.

## K {#letter-k}

### Keyed model {#keyed-model}

<p class="glossary-synonyms">Also: <code>IKeyedModel</code></p>

A model that exposes its shard key through a `Key` property by implementing `IKeyedModel<TRecord>` (or the two-, three- or four-column variants). Implementing it unlocks shard-key utilities that operate on model lists: merging two lists by key, finding the foreign shards they reference, and building a shards values argument from them.

## M {#letter-m}

### Map method {#map-method}

<p class="glossary-synonyms">Also: <code>MapReaderAsync</code>, <code>MapOutputAsync</code>, <code>MapListAsync</code></p>

A data method whose results are hydrated into models automatically by the `Mapper`, as opposed to a `QueryAsync` method, which takes a caller-written result handler delegate. There are three families: `MapReader*` builds the root model from a single-row data reader result; `MapOutput*` builds it from output parameters; and `MapList*` returns one model per row. `MapReader` and `MapOutput` can also populate up to eight child list properties (`TReaderResult0`–`TReaderResult7`) from subsequent result sets. On a shard set, the `First` and `All` suffixes select `ReadFirst` or `ReadAll` fan-out semantics.

### Mapper {#mapper}

<p class="glossary-synonyms">Also: <code>ArgentSea Mapper</code>, <code>ORM (parameters-and-results)</code></p>

ArgentSea's attribute-driven mapper between model properties and query parameters and results. The first time a model type is used, it reads the type's mapping attributes by reflection and compiles expression-tree delegates that create and set input parameters, create output parameters, read output parameters, read data-reader rows, or build table-valued-parameter rows. The delegates are cached per type for the life of the process, so later calls run at hand-coded ADO.NET speed. The Mapper is exposed as extension methods (`CreateInputParameters`, `CreateOutputParameters`, `ToModel`, `ToList`) and as ready-made result handlers. It never generates SQL, and each of its steps can be used, skipped or replaced independently.

### Mapping attribute {#mapping-attribute}

<p class="glossary-synonyms">Also: <code>MapTo attribute</code>, <code>property mapping attribute</code></p>

A provider-specific attribute on a model property, such as `MapToSqlNVarChar` or `MapToPgVarchar`. It declares the parameter or column name the property binds to, the database type, type options (length, precision, scale, locale), and the optional `Required` flag. Each attribute accepts a closed set of .NET property types: either the provider's native CLR type or an explicitly supported reversible conversion (such as `ulong`, `uint`, `ushort`, and `sbyte` integer and big-endian binary representations on SQL Server, and enum underlying integer types), rejecting undeclared coercions at mapping construction time. Properties without a mapping attribute are ignored. The `Mapper` reads these attributes to build its cached compiled mapping delegates.

### Mock results {#mock-results}

<p class="glossary-synonyms">Also: <code>MockResults</code>, <code>canned results</code></p>

A dictionary on a data connection, shard or shard set, keyed by Query name. When a data method is invoked with a Query whose name is present, the stored object is returned as the result and no database call is made. It lets unit tests exercise application code through ArgentSea's real call path without a live database.

### Model {#model}

<p class="glossary-synonyms">Also: <code>Model class</code>, <code>TModel</code></p>

An application class whose properties carry mapping attributes. It serves as the source of a query's parameters or the target of its results. A model inherits the mapping attributes of its base classes. It may nest other models (nested model), hold result-set list properties, and expose its identity as a keyed model.

## N {#letter-n}

### Nested model {#nested-model}

<p class="glossary-synonyms">Also: <code>MapToModel</code>, <code>MapToModel property</code></p>

A model property marked `[MapToModel]` whose type is itself a mapped model, such as a shared Address model inside Customer, Store and Vendor. Its mapping attributes extend the parent's parameter and column set: they are flattened into the same parameters or row, not read from a separate result. Nested models may chain to any depth. The Mapper instantiates the nested object when the property is null, settable and default-constructible.

### Null sentinel {#null-sentinel}

<p class="glossary-synonyms">Also: <code>NaN as null</code>, <code>Guid.Empty as null</code></p>

A non-nullable .NET value that ArgentSea treats as database null when writing or reading a property that is not `Nullable<>`. `Guid.Empty`, `double.NaN` and `float.NaN` round-trip to database null. A property declared nullable stores these values literally instead. Null strings and arrays are database null, while empty strings are stored as zero-length strings. Nullable value types and nullable enums map null to database null directly.

## O {#letter-o}

### Orleans cluster providers {#orleans-cluster-providers}

<p class="glossary-synonyms">Also: <code>ArgentSeaOrleansMembershipTable</code>, <code>ArgentSeaGatewayListProvider</code>, <code>ArgentSeaOrleansReminderTable</code>, <code>Orleans clustering</code>, <code>Orleans reminders</code></p>

ArgentSea.Orleans.Sql's implementations of the Orleans membership table, gateway list provider and reminder table. They store cluster membership, client gateway discovery and grain reminders in SQL Server through versioned stored procedures, with reader (`rdr`) and writer (`wtr`) schemas. Because Orleans' membership and reminder types are sealed and cannot carry mapping attributes, these providers use ADO.NET directly rather than the Mapper.

## P {#letter-p}

### Parameter collection {#parameter-collection}

<p class="glossary-synonyms">Also: <code>ParameterCollection</code></p>

`ParameterCollection` is a free-standing `DbParameterCollection` implementation that can be instantiated on the caller's thread before a database connection or command exists, and then cloned per shard or retry onto each connection's `DbCommand`. Because each execution receives its own parameter copies, callers must read output values via result handlers rather than holding references to the original input `DbParameter` instances. ArgentSea's typed parameter extension methods and the `Mapper` operate uniformly on `ParameterCollection` and on provider `DbParameterCollection` instances.

## Q {#letter-q}

### Query {#query}

<p class="glossary-synonyms">Also: <code>Query object</code>, <code>Queries class</code></p>

The object passed to every ArgentSea data method to name the SQL to execute. It carries the SQL text or procedure name, the command type, a name used in logging and in the mock-results lookup, and optional query parameter names. It has two concrete forms, QueryProcedure and QueryStatement. Applications conventionally expose every Query as a static property of one `Queries` class, so usage is visible by reference count. A Query is the handle; the query contract is the interface it stands for.

### Query contract {#query-contract}

<p class="glossary-synonyms">Also: <code>query interface</code>, <code>data contract</code></p>

The interface a query presents to the application: the parameters it accepts and the results it returns. Behind that interface the SQL implementation can be rewritten, re-indexed, hinted or re-partitioned without changing application code. It is a property of a query, not an object. The object that names a query in code is a Query (a QueryProcedure or QueryStatement).

### Query paradigm {#query-paradigm}

<p class="glossary-synonyms">Also: <code>ArgentSea Query Paradigm</code>, <code>caller/handler pattern</code>, <code>two-phase query execution</code></p>

ArgentSea's pattern for coding data access, which splits it into two parts. The caller builds the parameters on a single thread and invokes a query method. The result handler converts each connection's results into a model and may run concurrently on many threads, one per shard. The split exists because one parameter set must be copied to, and its results collected from, many connections at once. It is applied to non-sharded databases as well, so that sharded and non-sharded code look the same and the Mapper serves both.

### Query parameter names {#query-parameter-names}

<p class="glossary-synonyms">Also: <code>ParameterNames</code>, <code>parameter list</code></p>

The optional `ParameterNames` filter declared on a `Query`. When present, only parameters from the `ParameterCollection` whose names appear in `ParameterNames` are copied onto the command, allowing one model or parameter collection to supply multiple queries with different parameter subsets.

### QueryProcedure {#queryprocedure}

<p class="glossary-synonyms">Also: <code>stored procedure query</code></p>

A `Query` that names a server-side stored procedure (`CommandType.StoredProcedure`), so the SQL implementation lives in the database rather than in application binaries. On SQL Server it invokes a stored procedure via `EXEC`; on PostgreSQL (`Npgsql` 7+) it emits `CALL <name>(...)` and invokes a PostgreSQL procedure (`CREATE PROCEDURE`) only—PostgreSQL functions (`CREATE FUNCTION`, including set-returning functions) must be invoked via `QueryStatement` (`SELECT * FROM fn(...)`).

### QueryStatement {#querystatement}

<p class="glossary-synonyms">Also: <code>SQL statement file</code>, <code>statement query</code></p>

A `Query` whose parameterized SQL text lives in a file deployed beside the application rather than compiled into application binaries, executing with `CommandType.Text`. The default folder is `SQL` and the default extension is `.sql` (or `.psql` once PostgreSQL services are registered via `QueryStatement.Extension`). A host that registers both SQL Server and PostgreSQL services can use `SqlQueryStatement` (`.sql`) and `PgQueryStatement` (`.psql`) to avoid sharing the process-wide default extension. The SQL file is loaded lazily on first use and cached. On PostgreSQL (`Npgsql` 7+), `QueryStatement` is required when invoking PostgreSQL functions (`SELECT * FROM fn(...)`), because `QueryProcedure` (`CommandType.StoredProcedure`) emits `CALL` and invokes `CREATE PROCEDURE` routines only.

## R {#letter-r}

### Read scale-out {#read-scale-out}

<p class="glossary-synonyms">Also: <code>read replica scale-out</code>, <code>read/write splitting</code></p>

A deployment in which a write-capable primary handles writes while replica endpoints serve read-only traffic. Examples of such replicas are readable secondaries, hot standbys, read replicas and reader endpoints. ArgentSea supports it through the read/write connection pair. Replication latency, where a just-written record is not yet visible on the read endpoint, is the application's concern; ArgentSea does not retry on the write connection automatically.

### Read/write connection pair {#read-write-connection-pair}

<p class="glossary-synonyms">Also: <code>Read connection</code>, <code>Write connection</code>, <code>read endpoint</code>, <code>write endpoint</code></p>

The two data connections, `Read` and `Write`, that every database and every shard exposes. The caller chooses which one to use for each query; ArgentSea never infers whether a query reads or writes. Each endpoint is configured through the configuration hierarchy. When the two are not configured differently, both resolve to the same endpoint, so code that consistently picks Read or Write can move to read scale-out later without change.

### ReadAll {#readall}

<p class="glossary-synonyms">Also: <code>ShardSetReadAll</code>, <code>read-all fan-out</code></p>

The `ShardSet.ReadAll` (`ReadQueryAllAsync`) fan-out mode that executes a query concurrently on the read connection of every targeted shard, waits for all shards to complete (cancelling remaining shard reads if one faults), and combines all non-null per-shard results into a single list.

### ReadFirst {#readfirst}

<p class="glossary-synonyms">Also: <code>ShardSetReadFirst</code>, <code>read-first fan-out</code>, <code>TopOne</code></p>

The `ShardSet.ReadFirst` (`ReadQueryFirstAsync`) fan-out mode that executes a query concurrently on the read connection of every targeted shard, returns the first non-null result produced by any shard, and cancels remaining in-flight shard queries via a linked token.

### Record id {#record-id}

<p class="glossary-synonyms">Also: <code>RecordId</code></p>

The primary record-key component (`RecordId`) of a `ShardKey` within its shard. It is unique within one shard and, combined with `ShardId` (and any child key components), forms a globally unique identity across the shard set.

### Required mapping {#required-mapping}

<p class="glossary-synonyms">Also: <code>required</code>, <code>required attribute</code></p>

The `required` flag on a mapping attribute or shard key mapping. When the mapped database value is null, the Mapper treats the record as absent rather than as a record with a null property: a single-model read returns null, and a list read omits that row. Because nested model properties are flattened into their parent, a required column inside a nested model nulls the whole parent model, not just the nested property. When the Mapper drops a model this way, it logs the culprit column at Debug level.

### Resilience strategy {#resilience-strategy}

<p class="glossary-synonyms">Also: <code>resilience policy</code></p>

The policy ArgentSea applies to every command it executes on a data connection: retry with retry backoff on transient errors, wrapped by a circuit breaker. Each read or write endpoint has its own policy instance. It is built from the `RetryCount`, `RetryInterval`, `RetryLengthening`, `CircuitBreakerFailureCount` and `CircuitBreakerTestInterval` settings, with built-in defaults for any that are unset, and is rebuilt when the endpoint's retry count or circuit-breaker settings change. The provider driver's own connection retries are turned off so that every retry goes through, and is logged by, this policy.

### Result handler {#result-handler}

<p class="glossary-synonyms">Also: <code>QueryResultModelHandler</code>, <code>handler delegate</code>, <code>data handler</code></p>

A delegate matching `QueryResultModelHandler<TArg, TModel>` that turns one connection's command results, its data reader and parameter collection, into a model. It receives a model instance to populate, the shard id (default for non-sharded databases), the query name, an optional caller-supplied argument, the connection description and the logger. It is invoked once per connection and, in a fan-out query, concurrently on many threads, so it must be thread-safe. The Mapper supplies ready-made handlers used by the Map methods; Query methods take a caller-written one.

### Result-set list property {#result-set-list-property}

<p class="glossary-synonyms">Also: <code>list property</code>, <code>child list</code></p>

A settable `List<Model>` or `IList<Model>` property of a model that the Mapper fills from an additional data-reader result set, expressing a one-to-many relationship (an order and its items). No attribute is needed: the generic arguments of a MapReader or MapOutput call give the expected order of result sets, and each set is assigned to the property whose element type matches. A model therefore cannot have two list properties of the same element type. Lists inside listed models are not populated.

### Retry backoff {#retry-backoff}

<p class="glossary-synonyms">Also: <code>RetryLengthening</code>, <code>SequenceLengthening</code></p>

The delay the resilience policy waits before each retry of a transient error, computed from `RetryInterval` (default `250` ms) and the `RetryLengthening` (`SequenceLengthening`) strategy: `Lengthening` (`(2n - 1) * interval`, i.e., `1, 3, 5, 7... * interval`, the default; `Fibonacci` is retained as an `[Obsolete]` alias), `Linear` (`n * interval`), `HalfSquare` (`floor(n^2 / 2) * interval`, i.e., `0, 2, 4, 8, 12... * interval`, so the first retry is immediate), or `Squaring` (`2^(n - 1) * interval`, i.e., `1, 2, 4, 8, 16... * interval`). `RetryCount` (default `6`) caps the maximum number of retry attempts. All three settings participate in the connection configuration hierarchy.

## S {#letter-s}

### Shard {#shard}

<p class="glossary-synonyms">Also: <code>shard instance</code>, <code>ShardInstance</code></p>

One database within a shard set that holds one segment of the set's data. It is identified by its shard id and exposed as a shard instance with its own read and write connections. A query addressed to a shard runs on that one database only.

### Shard id {#shard-id}

<p class="glossary-synonyms">Also: <code>ShardId</code>, <code>shard identifier</code></p>

The 16-bit integer (`short`) that identifies one shard within its shard set and forms the shard part of every shard key. The shard id is assigned by client configuration. The shard database does not itself know its id unless the application tells it, so every client must agree on the shard id-to-database mapping; a mismatch silently directs reads and writes to the wrong database.

### Shard id parameter {#shard-id-parameter}

<p class="glossary-synonyms">Also: <code>shardParameterName</code>, <code>shardParameterOrdinal</code></p>

An optional query parameter, named by the caller, that ArgentSea sets to the current shard's id on each connection before executing. It lets SQL know which shard it is running on, for example to return shard-qualified keys, or to verify that the connection reached the shard the client expects and so guard against shard misconfiguration.

### Shard key mapping {#shard-key-mapping}

<p class="glossary-synonyms">Also: <code>MapShardKey</code>, <code>MapSqlShardKey</code>, <code>MapPgShardKey</code>, <code>MapShardKeyAttribute</code></p>

The provider attribute (`MapSqlShardKey`, `MapPgShardKey`; base `MapShardKeyAttribute`) that assembles a ShardKey property from mapped columns or parameters. It names the record-key columns (and child columns for compound keys), each of which must also carry its own mapping attribute with exactly the same name, and optionally a shard id column. Without a shard id column, the shard id comes from the connection that produced the row. With one, it comes from the data, which is how references to a foreign shard are read.

### Shard key type metadata {#shard-key-type-metadata}

<p class="glossary-synonyms">Also: <code>type metadata</code>, <code>ShardKeyTypeMetadata</code></p>

A compact byte header encoding the arity and `.NET` `ShardKeyType` of a `ShardKey`'s record-id and child-id components, embedded at the front of every serialized representation (binary, UTF-8, and external string). Deserialization validates this header against the target generic type arguments so bytes produced for one key shape cannot be decoded as an incompatible key type, and `GhostShardKey` uses it to reconstruct and display keys whose generic types are not known at compile time. It identifies key component types rather than specific database tables.

### Shard set {#shard-set}

<p class="glossary-synonyms">Also: <code>ShardSet</code>, <code>shardset</code></p>

A named collection of shards: databases with essentially identical schemas, each holding a distinct segment of one dataset. A shard set is addressed as a unit, both to route a record to the shard that holds it and to fan one query out across all (or a targeted subset of) its shards. It is configured by `ShardSetName` and retrieved by that exact key (casing, accents and kana must match). An application may define several shard sets, each an independent partitioning plan, but most define one.

### Shard sets collection {#shard-sets-collection}

<p class="glossary-synonyms">Also: <code>ShardSets</code>, <code>SqlShardSets</code>, <code>PgShardSets</code>, <code>ShardSetsBase</code></p>

The injectable singleton (`SqlShardSets`, `PgShardSets`; base `ShardSetsBase`) that holds every configured shard set, keyed by its exact `ShardSetName`. Its membership, and each shard set's set of shards, is fixed when it is built from configuration.

### Shard-set write {#shard-set-write}

<p class="glossary-synonyms">Also: <code>ShardSetWrite</code></p>

The `ShardSet.Write` (`RunAllAsync`, `BatchAllAsync`, `WriteQueryAllAsync`) fan-out mode that executes a command or batch concurrently on the write connection of every targeted shard without cancelling sibling shards on failure.

### Sharding {#sharding}

<p class="glossary-synonyms">Also: <code>data sharding</code></p>

Partitioning one dataset across multiple databases (shards) so that each owns a distinct segment. It is used for incremental scale, for active-active disaster recovery in which each data center owns a segment, for regional data residency, and for tenant isolation. In exchange, the database engine can no longer enforce uniqueness or foreign keys across segments; that responsibility moves to the application's key design.

### ShardKey {#shardkey}

<p class="glossary-synonyms">Also: <code>shard key</code>, <code>ShardKey&lt;TRecord&gt;</code></p>

ArgentSea's immutable value type implementing the virtual compound key. It combines a `short` `ShardId` (`0` = empty) with one to four key components (`RecordId`, `ChildId`, `GrandChildId`, `GreatGrandChildId`): `ShardKey<TRecord>`, `ShardKey<TRecord, TChild>`, `ShardKey<TRecord, TChild, TGrandChild>`, and `ShardKey<TRecord, TChild, TGrandChild, TGreatGrandChild>`. It embeds `ShardKeyTypeMetadata` in binary, UTF-8, and external string representations, while value equality compares only `ShardId` and the key component values.

### Shards values {#shards-values}

<p class="glossary-synonyms">Also: <code>ShardsValues</code>, <code>ShardParameterValue</code>, <code>shard parameter values</code></p>

The optional targeting argument (`ShardsValues`) of a shard-set query. It lists the shard ids to query, and each listed shard can carry parameter values that override the shared parameters on that shard only. When supplied, only the listed shards are queried, and an empty set queries none. When omitted, every shard in the set is queried with the same parameters.

## T {#letter-t}

### Table-valued parameter {#table-valued-parameter}

<p class="glossary-synonyms">Also: <code>TVP</code>, <code>TvpMapper</code></p>

A SQL Server structured parameter (`SqlDbType.Structured`) that carries a set of rows to save many records in one round trip. ArgentSea builds one from a sequence of models (converted to `SqlDataRecord` rows by the compiled `TvpMapper`) via `AddSqlTableValuedParameter`, or from a collection property marked with `[MapToSqlTableValuedParameter]`. An empty or null row set is sent with `Value = null`.

### Tenanting {#tenanting}

<p class="glossary-synonyms">Also: <code>tenant isolation</code>, <code>tenant database</code></p>

A deployment pattern in which each tenant's (customer's) data lives in its own database. ArgentSea models the tenant databases as the shards of a shard set, so tenant routing uses the same shard id, shard key and fan-out machinery as data sharding.

### Transient error {#transient-error}

<p class="glossary-synonyms">Also: <code>transient fault</code>, <code>retryable error</code></p>

A database error that the provider classifies as likely to clear on its own, such as a network interruption, deadlock, failover in progress, or optimistic-concurrency conflict in SQL Server in-memory tables. The provider decides through its data provider services' `GetIsErrorTransient`. Only transient errors are retried by the resilience strategy; all others surface to the caller at once.

## V {#letter-v}

### Virtual compound key {#virtual-compound-key}

<p class="glossary-synonyms">Also: <code>compound key</code>, <code>shard-qualified key</code></p>

A record identity formed by combining the shard id of the shard that holds a record with the record's own key of one to four columns. The combination is unique across a shard set even though each database assigns its keys independently, and it carries the information needed to route a request to the right shard. It is "virtual" because no database stores or enforces it as one key. ShardKey is ArgentSea's implementation.

