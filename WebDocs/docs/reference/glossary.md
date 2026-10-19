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

The second record-key column of a multi-column shard key (`ChildId`). The third and fourth columns are the grandchild id and great-grandchild id. They express a table whose primary key has more than one column, typically a parent id plus an id within that parent (an order and its line number). Like the record id, they are unique only in combination with the shard id.

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

One query executed concurrently against every shard of a shard set, or a targeted subset given by shards values. Each shard runs on its own connection, with its own copy of the parameters, under its own resilience strategy. The per-shard results, produced by a result handler, are then combined. The three forms are ReadAll, ReadFirst and shard-set Write.

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

Populating a caller-supplied, already-existing model instance with query results, instead of always constructing a new model. It was introduced in version 2 so that objects whose lifetime ArgentSea does not own, such as Orleans grain state, can be loaded in place. Result handlers therefore receive the instance to fill as their first argument.

## K {#letter-k}

### Keyed model {#keyed-model}

<p class="glossary-synonyms">Also: <code>IKeyedModel</code></p>

A model that exposes its shard key through a `Key` property by implementing `IKeyedModel<TRecord>` (or the two-, three- or four-column variants). Implementing it unlocks shard-key utilities that operate on model lists: merging two lists by key, finding the foreign shards they reference, and building a shards values argument from them.

## M {#letter-m}

### Map method {#map-method}

<p class="glossary-synonyms">Also: <code>MapReaderAsync</code>, <code>MapOutputAsync</code>, <code>MapListAsync</code>, <code>Query method</code>, <code>QueryAsync</code></p>

A data method whose results are built by the Mapper, as opposed to a Query method, which takes a caller-written result handler. There are three families. `MapReader*` builds the root model from a single-row data reader result. `MapOutput*` builds it from output parameters. `MapList*` returns one model per row. MapReader and MapOutput can also fill up to eight result-set list properties from further result sets. On a shard set, the `First` and `All` suffixes select ReadFirst or ReadAll semantics.

### Mapper {#mapper}

<p class="glossary-synonyms">Also: <code>ArgentSea Mapper</code>, <code>ORM (parameters-and-results)</code></p>

ArgentSea's attribute-driven mapper between model properties and query parameters and results. The first time a model type is used, it reads the type's mapping attributes by reflection and compiles expression-tree delegates that create and set input parameters, create output parameters, read output parameters, read data-reader rows, or build table-valued-parameter rows. The delegates are cached per type for the life of the process, so later calls run at hand-coded ADO.NET speed. The Mapper is exposed as extension methods (`CreateInputParameters`, `CreateOutputParameters`, `ToModel`, `ToList`) and as ready-made result handlers. It never generates SQL, and each of its steps can be used, skipped or replaced independently.

### Mapping attribute {#mapping-attribute}

<p class="glossary-synonyms">Also: <code>MapTo attribute</code>, <code>property mapping attribute</code></p>

A provider-specific attribute on a model property, such as `MapToSqlNVarChar` or `MapToPgVarchar`. It declares the parameter or column name the property binds to, the database type, type options (length, precision, scale, locale) and the optional required flag. The property's .NET type must match the attribute's database type exactly; the Mapper never casts. Properties without a mapping attribute are ignored. The Mapper reads these attributes to build its compiled mapping delegates.

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

<p class="glossary-synonyms">Also: <code>ParameterCollection</code>, <code>QueryParameterCollection (older docs)</code></p>

`ParameterCollection` is a free-standing `DbParameterCollection` that can be created without a command object. Parameters can therefore be declared once, on the caller's thread, before any connection exists, and then copied onto each connection's command. Because every connection gets its own copy, a caller must not hold references to the original output parameters expecting them to receive results. ArgentSea's typed parameter extension methods and the Mapper work both on it and on any provider command's `Parameters`.

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

The optional list of parameter names declared on a Query. When present, only parameters whose names are in the list are applied to the command, so one model can feed queries that each expect a different subset of its mapped parameters (an INSERT without `LastModified`, an UPDATE with it). When absent, every parameter in the collection is sent. Extra or missing parameters are errors raised by the database, not by ArgentSea.

### QueryProcedure {#queryprocedure}

<p class="glossary-synonyms">Also: <code>stored procedure query</code></p>

A Query that names a stored procedure (SQL Server) or a function (PostgreSQL) and executes with command type StoredProcedure, so the SQL itself lives and evolves in the database. It is the preferred form for SQL Server. With PostgreSQL it can call only functions; stored procedures are invoked with `CALL` from a QueryStatement.

### QueryStatement {#querystatement}

<p class="glossary-synonyms">Also: <code>SQL statement file</code>, <code>statement query</code></p>

A Query whose parameterized SQL text lives in a file deployed beside the application rather than compiled into it. The default location is the `SQL` folder with extension `.sql`, or `.psql` once PostgreSQL services are registered (the default extension is one process-wide value). A host that registers both SQL Server and PostgreSQL services creates statements through `SqlQueryStatement` and `PgQueryStatement`, which each use their own extension (`.sql` and `.psql`) regardless of the shared default. The file is loaded lazily on first use and cached, and the query executes with command type Text. It is the preferred form for PostgreSQL and for databases the team cannot add procedures to. A missing file surfaces only at first use.

## R {#letter-r}

### Read scale-out {#read-scale-out}

<p class="glossary-synonyms">Also: <code>read replica scale-out</code>, <code>read/write splitting</code></p>

A deployment in which a write-capable primary handles writes while replica endpoints serve read-only traffic. Examples of such replicas are readable secondaries, hot standbys, read replicas and reader endpoints. ArgentSea supports it through the read/write connection pair. Replication latency, where a just-written record is not yet visible on the read endpoint, is the application's concern; ArgentSea does not retry on the write connection automatically.

### Read/write connection pair {#read-write-connection-pair}

<p class="glossary-synonyms">Also: <code>Read connection</code>, <code>Write connection</code>, <code>read endpoint</code>, <code>write endpoint</code></p>

The two data connections, `Read` and `Write`, that every database and every shard exposes. The caller chooses which one to use for each query; ArgentSea never infers whether a query reads or writes. Each endpoint is configured through the configuration hierarchy. When the two are not configured differently, both resolve to the same endpoint, so code that consistently picks Read or Write can move to read scale-out later without change.

### ReadAll {#readall}

<p class="glossary-synonyms">Also: <code>ShardSetReadAll</code>, <code>read-all fan-out</code></p>

The fan-out form that runs a query on the read connection of every targeted shard, waits for all of them, and returns every non-null per-shard result combined into one list. It is used when a result may span shards, such as all users with a given attribute.

### ReadFirst {#readfirst}

<p class="glossary-synonyms">Also: <code>ShardSetReadFirst</code>, <code>read-first fan-out</code>, <code>TopOne</code></p>

The fan-out form that runs a query on the read connection of every targeted shard and returns the first non-null result any shard produces. It then cancels the remaining shard queries and ignores their failures. When no shard produces a result it returns the default value, unless the caller cancelled (the call throws OperationCanceledException) or a shard failed (the failure is rethrown). It is intended for lookups by an attribute that should be unique across the set but is not the shard key (a user by login name). If the attribute is not actually unique, duplicates are silently discarded.

### Record id {#record-id}

<p class="glossary-synonyms">Also: <code>RecordId</code></p>

A record's own key value within its shard: the first record-key column of a shard key (`RecordId`). It is unique only within one shard; combined with the shard id it becomes unique across the shard set. Its type may be any of the key types the shard key serializer supports, such as integers, Guid, string, dates and times, decimal or byte arrays.

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

<p class="glossary-synonyms">Also: <code>retry sequence lengthening</code>, <code>RetryLengthening</code>, <code>SequenceLengthening</code></p>

How long the resilience strategy waits before each retry of a transient error. The wait is computed from `RetryInterval` (milliseconds) and the `RetryLengthening` sequence, which is one of Linear (1, 2, 3, 4… × interval), Lengthening (1, 3, 5, 7… × interval), HalfSquare or Squaring and defaults to Lengthening. `Fibonacci` is an obsolete alias of Lengthening kept so existing configuration still binds; the progression was never a Fibonacci sequence. `RetryCount` caps the number of retries. All three settings participate in the configuration hierarchy and fall back to built-in defaults when unset.

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

<p class="glossary-synonyms">Also: <code>type metadata</code>, <code>DataOrigin (obsolete)</code></p>

A compact code for the .NET types of a shard key's record-key columns, embedded in every serialized shard key: binary, UTF-8 and the external key. Deserializing checks it against the expected generic types, so bytes produced for one key shape cannot be read as another. It also lets a ghost shard key reconstruct a key whose types are unknown at compile time. It identifies key *types*, not tables: keys of the same types from different tables are indistinguishable. In older documentation it replaces the per-table DataOrigin character.

### Shard set {#shard-set}

<p class="glossary-synonyms">Also: <code>ShardSet</code>, <code>shardset</code></p>

A named collection of shards: databases with essentially identical schemas, each holding a distinct segment of one dataset. A shard set is addressed as a unit, both to route a record to the shard that holds it and to fan one query out across all (or a targeted subset of) its shards. It is configured by `ShardSetName` and retrieved by that exact key (casing, accents and kana must match). An application may define several shard sets, each an independent partitioning plan, but most define one.

### Shard sets collection {#shard-sets-collection}

<p class="glossary-synonyms">Also: <code>ShardSets</code>, <code>SqlShardSets</code>, <code>PgShardSets</code>, <code>ShardSetsBase</code></p>

The injectable singleton (`SqlShardSets`, `PgShardSets`; base `ShardSetsBase`) that holds every configured shard set, keyed by its exact `ShardSetName`. Its membership, and each shard set's set of shards, is fixed when it is built from configuration.

### Shard-set write {#shard-set-write}

<p class="glossary-synonyms">Also: <code>ShardSetWrite</code></p>

The fan-out form (`ShardSet.Write`) that runs a command or shard-set batch on the write connection of every targeted shard, for changes that must be applied across the set. Writes addressed to one record belong on that record's shard (or the default shard) instead.

### Sharding {#sharding}

<p class="glossary-synonyms">Also: <code>data sharding</code></p>

Partitioning one dataset across multiple databases (shards) so that each owns a distinct segment. It is used for incremental scale, for active-active disaster recovery in which each data center owns a segment, for regional data residency, and for tenant isolation. In exchange, the database engine can no longer enforce uniqueness or foreign keys across segments; that responsibility moves to the application's key design.

### ShardKey {#shardkey}

<p class="glossary-synonyms">Also: <code>shard key</code>, <code>ShardKey&lt;TRecord&gt;</code></p>

ArgentSea's immutable value type implementing the virtual compound key. It holds a shard id plus a record key of one to four columns: `ShardKey<TRecord>`, `ShardKey<TRecord, TChild>`, `ShardKey<TRecord, TChild, TGrandChild>` and `ShardKey<TRecord, TChild, TGrandChild, TGreatGrandChild>`. It also embeds shard key type metadata. Equality compares the shard id and the key values. Because it is a struct it cannot be null: an absent key is `ShardKey.Empty` (shard 0, default key values), or the property is declared `Nullable<ShardKey<…>>`. It serializes to bytes, UTF-8 and an external key.

### Shards values {#shards-values}

<p class="glossary-synonyms">Also: <code>ShardsValues</code>, <code>ShardParameterValue</code>, <code>shard parameter values</code></p>

The optional targeting argument (`ShardsValues`) of a shard-set query. It lists the shard ids to query, and each listed shard can carry parameter values that override the shared parameters on that shard only. When supplied, only the listed shards are queried, and an empty set queries none. When omitted, every shard in the set is queried with the same parameters.

## T {#letter-t}

### Table-valued parameter {#table-valued-parameter}

<p class="glossary-synonyms">Also: <code>TVP</code>, <code>TvpMapper</code></p>

A SQL Server parameter that carries a set of rows, used to save many records in one round trip. ArgentSea builds one from a list of models, with each model converted to a `SqlDataRecord` by the compiled TVP mapper, or from a collection property marked with a collection mapping attribute. An empty or null row set is sent as a null parameter value.

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

