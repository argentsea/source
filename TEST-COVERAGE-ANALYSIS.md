# ArgentSea test-coverage analysis

Date: 2026-10-15. Baseline taken on `main` at 095af9bf.

## 1. Where things stand

### Test suite

| Project | Tests | Result | Notes |
| --- | ---: | --- | --- |
| ArgentSea.Test (core) | 118 | pass | |
| ArgentSea.Sql.Test | 365 | pass, 2 skipped | skipped tests need `ARGENTSEA_SQL_TEST_CONNECTION` |
| ArgentSea.Pg.Test | 150 | **1 failing** | `ConfigurationTests.TestServiceBuilder` |
| ArgentSea.Orleans.Test | 46 | pass | |
| ArgentSea.Orleans.Sql.Test | 12 | pass | |
| **Total** | **691** | | no CI workflow exists; nothing runs these automatically |

The Pg failure is drift, not a product defect: the test compares a whole 50-key Npgsql connection string for equality. The test config declares `"SearchPath": null`; the .NET configuration binder once bound JSON null to `""` (which Npgsql renders as `Search Path=`) and now binds it to `null` (which ArgentSea correctly skips). The connection is semantically identical, but the literal no longer matches. The test should assert on the parsed builder's keys, not on the rendered string.

### Line coverage (`dotnet test --collect:"Code Coverage;Format=Cobertura"`)

| Assembly | Lines | Covered | % |
| --- | ---: | ---: | ---: |
| ArgentSea (core) | 5,408 | 2,778 | **51%** |
| ArgentSea.Sql | 2,355 | 2,034 | 86% |
| ArgentSea.Pg | 1,767 | 1,265 | 72% |
| ArgentSea.Orleans | 1,068 | 574 | 54% |
| ArgentSea.Orleans.Sql | 470 | 45 | **10%** |

### Spec corpus

| Spec | Blocks | Non-rejected | Cite a test |
| --- | ---: | ---: | ---: |
| behavior (Behavioral + Scenario + Example) | 586 | ~430 | 53 |
| interfaces | 163 | ~150 | 5 |
| invariants | 59 | ~57 | 3 |
| schemas | 66 | ~60 | 4 |
| **Testable total** | | **821** | **65 (8%)** |

Every block's `evidenceLinks` array is empty. The only spec-to-test linkage is 59 source entries with `relation: verifies` (plus 12 `validates`), so the corpus cannot currently tell you which behaviors are enforced. 892 of 1,114 blocks are `Draft`, 581 are `GeneratedProposal`, and only one is `Verified`.

### Status after phase 5

Date: 2026-10-18, phases 1 to 5 complete, then the spec-evidence gap closed the same day (test counts below are after that closure; the line-coverage table is the phase-5 collection). Rerun with `pwsh scripts/coverage-summary.ps1 -Collect -Markdown` (line coverage) and `pwsh scripts/spec-evidence.ps1 -ShowMissing -Markdown` (spec evidence).

| Project | Tests | Result |
| --- | ---: | --- |
| ArgentSea.Test (core) | 619 | pass |
| ArgentSea.Sql.Test | 597 | pass |
| ArgentSea.Pg.Test | 356 | pass |
| ArgentSea.Orleans.Test | 198 | pass |
| ArgentSea.Orleans.Sql.Test | 64 | pass |
| ArgentSea.Integration.Test | 39 | skip without a Docker engine; run by the ubuntu CI job |
| **Total** | **1,873** | `dotnet test ArgentSea.slnx` green (the phase-5 build passed ten times in a row with zero failures); no test is skipped for a known defect |

Line coverage (`dotnet test ArgentSea.slnx --collect:"Code Coverage;Format=Cobertura"`, one report for the whole solution, so a line counts as covered when any test project hits it):

| Assembly | Lines | Covered | % | Baseline |
| --- | ---: | ---: | ---: | ---: |
| ArgentSea (core) | 5,303 | 4,396 | **83%** | 51% |
| ArgentSea.Sql | 1,731 | 1,612 | 93% | 86% |
| ArgentSea.Pg | 1,772 | 1,531 | 86% | 72% |
| ArgentSea.Orleans | 1,093 | 934 | 85% | 54% |
| ArgentSea.Orleans.Sql | 470 | 402 | **86%** | 10% |

Spec evidence (non-rejected blocks with at least one `[Trait("Spec", "<block id>")]` test; a block adopted from a brief also counts a test that cites its brief-era id):

| Spec | Blocks | Non-rejected | Cite a test | % |
| --- | ---: | ---: | ---: | ---: |
| behavior | 587 | 555 | 554 | 100% |
| interfaces | 163 | 155 | 155 | 100% |
| invariants | 59 | 53 | 52 | 98% |
| schemas | 66 | 59 | 59 | 100% |
| **Testable total** | **875** | **822** | **820** | **100%** (after phase 5: 777, 95%; baseline 65 of 821, 8%) |

The two testable blocks that have no test are both `Superseded` and need none: blk-7735af9d (orleans-sql-server-package, superseded by the current package invariants) and blk-123b620a (shard-targeting-values, superseded by blk-0bc088d3, whose ShardListForeign tests in `ShardTargetingValuesTests` cover the same behavior). The script counts them because it excludes only `Rejected` blocks.

The 43 blocks the phase-5 list left untested were closed on 2026-10-18 without a product change:

- **postgres-type-mapping** (30): `ArgentSea.Pg/tests/TypeMappingCoverageTests.cs` now holds the attribute half of the section: a table of all 22 `MapToPg*Attribute` variants (two constructors each, `SqlTypeName`, the selected `NpgsqlDbType`), the supported-type matrix as a theory over 39 candidate CLR shapes per variant (the matrix in blk-0d79efbb matches every `IsValidType` override, including the Bigint quirk of rejecting a nullable enum), the scenario column definitions, and all ten `MapPgShardKeyAttribute` overloads. `ArgentSea.Pg/tests/ParameterExtensionsTests.cs` holds the helper half: `GetArray`/`GetHStore`, the NaN and `Guid.Empty` sentinels, output size/precision/scale/element-type retention, a reflection census proving the family is exactly 22 Output plus 39 Input overloads over 22 types, and a theory that calls every one of the 61 overloads once (nullable overloads with null) and checks the appended parameter's name, type, direction and value.
- **postgres-service-registration** (7) and **sql-server-service-registration** (6): the `ConfigurationTests` class of each provider gained tests for what `AddPgServices`/`AddSqlServices` register (option bindings, singleton `*Databases`/`*ShardSets`, same collection returned), for `AddPgServices` always resetting the shared `QueryStatement.Extension` to `psql`, and for the facades hard-wiring their provider's `DataProviderServiceFactory` (read back from the base class's private field). The existing `PgQueryStatementTests`/`SqlQueryStatementTests` and `*QueryStatement_UsesItsOwnExtension*` tests were tagged for the provider-scoped extension blocks. Because `AddPgServices` writes a process-wide static, the Pg `ConfigurationTests` and `PgQueryStatementTests` now share an xunit collection so they never run concurrently.

Two of those blocks describe the SQL Server facades more strictly than the code: blk-2c0c040b and blk-1b2c97ea say `SqlDatabases`/`SqlShardSets` never accept an `IDataProviderServiceFactory`, but each has a protected constructor that does (the hook the TestKit-based tests use). The public constructor and every container-resolved instance are bound to SqlClient exactly as the blocks say, so the tests pin that and note the protected hook; the blocks should be amended to mention it.

Spec blocks whose recorded facts phase 5 changed and which need amending: blk-90f700c0 (ArgentSea.Sql.csproj optimized Debug and not Release; now the reverse), blk-54654080 (ArgentSea.Orleans.csproj declared `Description` twice; now once), blk-cac267d8 (ArgentSea.Orleans.Sql.csproj packed no readme; it now packs its readme.md), blk-d6d3d68d (ArgentSea.Sql.csproj has two `InternalsVisibleTo` grants since the integration test project), and blk-642b4e90 (the solution also holds ArgentSea.TestKit and ArgentSea.Integration.Test).

## 2. The central finding: coverage is inverted

The leaf layers are well tested. The orchestration core, which is the product, is almost untested.

| Layer | Representative file | Coverage |
| --- | --- | ---: |
| Typed parameter helpers | `PgParameterCollectionExtensions.cs`, `SqlParameterCollectionExtensions.cs` | 98%, 90% |
| Connection property bags | `SqlConnectionPropertiesBase.cs`, `PgConnectionPropertiesBase.cs` | 98%, 99% |
| Expression fragment builders | `ExpressionHelpers.cs`, `TvpExpressionHelpers.cs` | 95%, 95% |
| Fan-out helper | `ShardFanOut.cs` | 98% |
| **Shard-set read-all / read-first / write** | `ShardSetReadAll.cs`, `ShardSetReadFirst.cs`, `ShardSetWrite.cs` | **4%, 5%, 10%** |
| **Single-shard connection** | `ShardDataConnection.cs` (2,255 lines) | **5%** |
| **Batch step library** | `BatchExtensions.cs` | **0%** |
| **Shard set collection** | `ShardSet.cs` | 22% |
| **Resilient execution** | `DataConnectionManager.cs` | 40% |
| **Database connection** | `Databases.cs` | 35% |
| **Mapper result handlers** | `Mapper.cs` (4,190 lines) | 56% |
| **Pg COPY bulk load** | `CopyMapper.cs` | **4%** |
| **Orleans membership / reminders** | `ArgentSeaOrleansMembershipTable.cs`, `ArgentSeaOrleansReminderTable.cs` | 11%, 16% |
| **Orleans hosting / registration** | `ClusteringExtensions.cs`, `ArgentSeaSiloBuilderExtensions.cs`, `ArgentSeaGatewayListProvider.cs` | 0% |

Put differently: the three things ArgentSea exists to do (fan a query across shards, retry and break on failure, run a batch in a transaction) and its headline integration (Orleans) have essentially no tests. The existing `ShardFanOut` tests prove the helper's task-combinator semantics, not that `ReadAll`/`ReadFirst`/`Write` use it correctly.

The test history explains this. Almost every test file was added as part of a bug-fix sweep (commit messages like "batch-3 sweep defects"), so tests pin individual fixes rather than cover a surface.

## 3. Why these areas are untested: the harness gap

The seam exists. `IDataProviderServiceFactory` is injected into `DatabasesBase`, `ShardSetsBase`, and `DataConnectionManager`, and `InternalsVisibleTo` is set for every test project. But there is no shared fake provider. Four test files each hand-roll one:

- `BatchCancellationTests.FakeServices` + `TokenCapturingCommand`
- `ShardParameterIsolationTests.RecordingServices` + `SlowOpenConnection` + `TestShardSets`
- `OutputValueAndRunCancellationTests.OpenConnection`
- `ResiliencePolicyTests` (NSubstitute on the factory only)

Each stops the pipeline as early as it can (one throws from `SetParameters` to abort before execution) because there is no fake `DbDataReader` scripting. That is why nothing past "the command was built" is covered.

**Recommendation: build one scripted in-memory ADO.NET provider and share it across all five test projects.** Concretely, a `ArgentSea.TestKit` project (or a shared-project under `ArgentSea.Shared/tests/Fakes`) containing:

- `FakeProviderServices : IDataProviderServiceFactory` that returns `FakeDbConnection` / `FakeDbCommand` / `FakeDbDataReader` / `FakeDbTransaction`.
- A script keyed by `(query name, shard id)` that supplies ordered result sets (rows as `object[]` with column names), output-parameter values, return values, and optionally "throw this exception on attempt N" with a transient/non-transient flag.
- A recorder exposing every command executed: connection string, shard id, command text, parameter values as bound, transaction begin/commit/rollback, and the cancellation token observed.
- Builders for `ShardSetsBase`/`DatabasesBase` subclasses over in-memory `IShardSetsConfigurationOptions` (the `TestShardSets` pattern already in `ShardParameterIsolationTests`).

This one harness unlocks roughly 250 of the 821 spec blocks (every fan-out, batch, resilience, shard-addressing, databases-collection, grain-persistence, and Orleans.Sql table scenario).

For the paths that genuinely need a database (Pg binary COPY, the Orleans SQL scripts, TVP round trips), add opt-in **integration tests on Testcontainers** for SQL Server and PostgreSQL, gated by an environment variable the way `NumericMappingIntegrationTests` already is.

## 4. Test inventory by area, prioritized

Spec counts are non-rejected blocks in the named `sectionId`. "Tested" is how many cite a test today.

### P0: the execution pipeline

**resilient-command-execution** (14 spec, 0 tested; `DataConnectionManager.cs` 40%)

- Transient `DbException` is retried; non-transient is thrown on first attempt (spec: deadlock retried, constraint violation not).
- Retry exhaustion rethrows the last exception; with `RetryCount = 2` the delays are 250 ms then 750 ms (spec gives exact numbers; inject the clock or assert on `GetRetryTimespan` calls).
- Circuit breaker opens after `CircuitBreakerFailureCount` failures and fails fast for `CircuitBreakerTestInterval`; `CircuitBreakerFailureCount` of 0 or null means retry policy only (the invariant says breaker and retry are always composed, never independent).
- `UnexpectedSqlResultException` message when an expected output name is absent.
- Mock short-circuit: `MockResults[""]` returns the batch result without executing; a query-name key returns without execution for every data method.
- `WithShardId` precedence: an explicit `parameterValues` entry for the shard parameter wins over the shard id; an ordinal out of range leaves values untouched.
- Retry and circuit-breaker log events are emitted with the right event ids (the breaker events never fired through 2.0.4; no test caught it).

**shard-addressing** (22 spec, 0 tested; `ShardDataConnection.cs` 5%, `ShardInstance.cs`)

- `ShardInstance` with only a read or only a write endpoint uses it for both; `ShardId`/`Read`/`Write` are immutable.
- `ReturnValueAsync<TRecord>` and `<TRecord,TChild>` overloads build the `ShardKey` from the connection's shard id when no shard column is named, and from the column when one is.
- `ListAsync<TRecord>` key overloads likewise.
- Every `MapReaderAsync`/`MapOutputAsync`/`QueryAsync`/`MapListAsync` overload reaches the manager with the right handler and `isTopOne`; the hydrate-in-place overloads return the same instance.
- `MockResults` is never null and short-circuits by query name.

**databases-collection** (16 spec, 0 tested; `Databases.cs` 35%)

- Membership immutable after construction; exact-key lookup; `CopyTo`, enumeration.
- Same data-method matrix as above against a non-sharded `DataConnection`, including the `ReturnValueAsync` typed/untyped split and `ListAsync<TValue>` by column name.

### P0: shard-set fan-out

**shard-set-fan-out** (44 spec, 2 tested; `ShardSetReadAll.cs` 4%, `ShardSetReadFirst.cs` 5%, `ShardSetWrite.cs` 10%, `ShardSet.cs` 22%)

- `ReadAll` combines per-shard lists in any order; null per-shard results are dropped; empty `ShardsValues` returns an empty list without touching any shard.
- `ReadAll` faults with the first shard's exception and cancels siblings; `ReadFirst` ignores a faulted shard while another still can answer, returns `default` when all are null, and cancels losers once a winner is chosen; `Write` waits for every shard and aggregates failures (the `ShardFanOut` tests prove these semantics in isolation; these tests prove the wiring).
- `ShardsValues` subsetting: only listed shards are queried; per-shard parameter overrides reach only that shard; `shardParameterName` injects each shard's own id (one test exists; extend to every method family).
- Mock results by query name short-circuit every family; the "ShardSet batch is suppressed by an existing mock dictionary" rule.
- Indexer by `short` and by `IShardKey`; `DefaultShard`; membership immutable.
- `Write.MapReaderAsync`/`MapOutputAsync`/`QueryAsync` return combined non-null results (spec lists `[42, 84]`-style scenarios).

### P0: batches and transactions

**batch-execution** (31 spec, 2 tested; `BatchExtensions.cs` 0%, `BatchBase.cs` 17%)

- `Add` overload family returns the same batch for chaining; `Count`/indexer/enumeration/`Remove`/`Clear`.
- Steps execute in registration order on one open connection inside one transaction; a failing step rolls back and nothing later runs; success commits.
- Each `BatchExtensions` step type: single `ShardKey` result (shard id taken from `ExecuteAsync`'s argument, not the row), child key, `List<ShardKey>` in reader order, model via handler called exactly once, record and list-of-record steps; no-row returns `ShardKey.Empty` rather than null.
- `ShardSetBatch` runs on every targeted shard; `ShardBatch<TResult>` returns the last non-default step result (spec: steps 0, 7, 0 return 7).
- Cancellation token reaches every step (3 tests exist).

### P1: mapping

**model-mapping** (48 spec, 9 tested) and **mapping-expression-builders** (46 spec, 0 tested; `Mapper.cs` 56%, exception classes 0 to 19%)

- Result handlers: `ModelFromReaderResultsHandler` with 1 to 8 list result sets in generic-argument order; `ModelFromOutResultsHandler`; `ModelFromReaderWithCollectionsHandler` including the "fewer result sets than collection properties" `UnexpectedSqlResultException`; `UnexpectedMultiRowResultException` when a top-one read yields two rows.
- Null semantics per ADR: `required` null on single read returns null, on list omits the row; `Guid.Empty`/`double.NaN`/`float.NaN` sentinels; nullable enums.
- Nested `MapToModel` flattens into one parameter set and instantiates a null nested instance; `CreateInputParameters` adds pre-existing names to the ignore set.
- Diagnostics: `MultipleMapAttributesException` names class and property; `MapAttributeMissingException.ShardElement`; `InvalidMapTypeException` before any parameter is appended; `NoMappingAttributesFoundException`.
- Missing column is skipped and logged at Debug; type mismatch throws (ADR "strict about types, tolerant about presence").
- Cache behavior: second use of a model type is a cache hit (log event) and does not recompile.

`ExpressionHelpers.cs` is 95% line-covered, so most of the 46 expression-builder scenarios are *executed* by existing tests but not *asserted*. Converting a dozen of them into explicit assertions (nullable reader path uses `GetFieldValue<int?>`, enum-as-string writes `"Shipped"`, byte[] passed without `ToArray`) is cheap.

**table-valued-parameters** (17 spec, 0 tested; `TvpMapper.cs` 71%) and **sql-server-type-mapping** (25 spec, 2 tested)

- TVP column subset ordinals (one test exists), `MapToModel` flattening into a TVP row, `ShardKey` component columns, empty collection is a null parameter (ADR), JSON column throws `NotImplementedException` with the documented message, `ImmutableArray` default handled.
- `IsValidType` matrix per attribute as a `[Theory]` over the schema block's type table.

**copy-load** (13 spec, 1 tested; `CopyMapper.cs` 4%, 384 lines, the largest uncovered file)

- Unit: destination-table DDL text, COPY command text, and per-type caching (compile once, reuse).
- Integration (Testcontainers Postgres): model-collection and shard-key `Add` overloads actually load rows, temp vs persistent table, within a transaction.

### P1: shard keys and serialization

**shard-key** (23 spec, 12 tested; `ShardChild.cs` 48%, `ShardGrandChild.cs` 44%, `ShardGreatGrandChild.cs` 48%) and **shard-key-serialization** (41 spec, 9 tested; `ShardKeySerialization.cs` 56%, `GhostShardKey.cs` 41%)

- Equality invariant across all four arities (differs in shard id or any position); `IsEmpty` for string keys (`Empty.RecordId` is `""` not null); `ToString` for each arity; hash-code stability.
- `FromExternalString` with corrupted checksum throws with the documented message; `GhostShardKey.ToString`/`GetShardId`/`GetInstance` on short byte arrays; `TryConvertFromBytes` returns false on unknown type codes; `ThrowIfMetadataMismatch`; enum parts round-trip as their underlying type.
- Several spec scenarios in this section describe *current* behavior that looks like a defect (bounds checks that compare against the full span rather than the remaining bytes). Review those before writing tests so the tests pin intended behavior, not the bug.

**shard-targeting-values** (28 spec, 0 tested; `ShardsValues.cs` 65%): the `Merge` rules (add missing shard, add missing parameter, duplicate parameter throws and leaves target unchanged), `ShardListForeign` excludes the current shard, placeholder entries for parameterless shards, `CopyTo`. Many are already covered by `ShardsValuesTests` but not cited by the spec; link them.

### P1: configuration and queries

**connection-configuration-hierarchy** (27 spec, 0 tested in core) and **sql-/postgres-connection-settings** (24 + 36 spec, 4 + 4 tested)

- A precedence `[Theory]`: each of global, shard-set, read/write, shard, and endpoint levels setting the same property, asserting which value wins for Read and for Write.
- `GetRetryTimespan` curves with the spec's exact values (Linear 750 ms, HalfSquare 1000 ms, Squaring 1000 ms, Lengthening 400 ms at attempt 3 with 100 ms interval; `Fibonacci` alias).
- `PropertyChanged` fires only on a real change and invalidates the cached connection string and description.
- Resilience settings inherit through the hierarchy (one test exists; extend to each level).
- `PgConnectionConfiguration.DefaultConnectTimeout` is declared but never applied (noted in `WebDocs/AUTHORING.md`); write the failing test.
- Replace the whole-string connection-string assertions with per-key assertions so Npgsql/SqlClient upgrades stop breaking the suite.

**query-definitions** (22 spec, 1 tested; `QueryStatement.cs` 41%): all pure and easy. Rooted vs relative folder, null vs empty extension (the spec notes `""` yields a trailing dot), `Extension` read at `Lazy.Value` time so `AddPgServices` after creation changes the file read, `SqlQueryStatement`/`PgQueryStatement` ignore the shared static, `CommandType.Text` always, `QueryProcedure` is `StoredProcedure`.

**query-parameters** (17 spec, 1 tested; `ParameterCollection.cs` 31%): ordered semantics, name lookup (`Contains`/`IndexOf`/indexer), `Insert` of a non-`DbParameter` throws, output getters' null rules (`GetGuid` → `Guid.Empty`, `GetDouble` → NaN, `GetInteger` → `UnexpectedNullException` naming the column).

### P2: provider services

**sql-server-provider-services** (10 spec, 1 tested; `DataProviderServiceFactory.cs` 52%) and **postgres-provider-services** (20 spec, 5 tested)

- `GetIsErrorTransient` as a `[Theory]` over every listed SQL error number plus a non-listed one and a non-`SqlException`.
- `NewCommand` with a foreign connection type throws `ArgumentException` with `ParamName == "connection"`.
- `SetParameters`: missing name throws `ParameterNotFoundException` with the normalized name; `parameterValues` override; independent parameter copies; Pg `Prepare()` only when multiplexing is off.

### P2: Orleans

**grain-persistence** (66 spec, 0 tested; persistence classes 66 to 75%, validators 0%), **grain-query-definitions** (13 spec; `OrleansQueryDefinitions.cs` 16%), **grain-key-marshalling** (23 spec, 6 tested; `SpanParser.cs` 42%), **shard-key-orleans-codecs** (9 spec)

- The three options validators are pure and have about twelve `OrleansConfigurationException` scenarios each in the spec (missing key, key mismatch with provider name, empty queries, shard set absent, shard without read/write endpoint, negative reminder period).
- `OrleansQueryNotProvidedException` messages for read/write/clear; `Lazy<QueryStatement>` vs `QueryProcedure` definitions.
- Read/write/clear for both DB-backed and shard-backed persistence through `MockResults` (the `GrainReadStateTests` harness already does this): `RecordExists` transitions, null-state `NoNullAllowedException` ordering before the query-registration check, `IActivator<T>` vs default construction, output-parameter vs result-set formats, per-`(shardSetKey, validateGrainKeys)` delegate caching.
- `SpanParser` round-trips for every supported type and the `+` delimiter invariant; `OrleansIdKeyMismatchException` when the grain identity disagrees with the model.
- Codec: `WriteField`/`ReadValue` round trip for all four arities, reference-field short-circuit, `DeepCopy`.

**orleans-cluster-providers** (21 spec, 0 tested; `ArgentSeaOrleansMembershipTable.cs` 11%), **orleans-reminder-storage** (21 spec, 0 tested; 16%), **orleans-grain-storage-hosting** (19 spec, 0 tested; 0 to 17%), **orleans-runtime-query-catalog** (8 spec)

Everything here is driven through `SqlDatabases`, so `MockResults` covers it without a database:

- `InsertRow`/`UpdateRow` return false on an etag conflict and rethrow on timeout; `ReadAll` maps version row and members; `ReadRow` by address; suspect-list merging (4 tests exist for the static helper); `InitializeMembershipTable` passes `tryInitTableVersion`.
- Reminders: `ReadRows` across shards returns every row; `UpsertRow` returns the version string; `RemoveRow` false when nothing matched; `TestOnlyClearTable` is a no-op.
- `ArgentSeaGatewayListProvider.GetGateways` builds URIs from rows.
- Registration: `UseArgentSeaClustering` on silo and client registers `IMembershipTable`/`IGatewayListProvider`; `AddArgentSeaGrainStorage` registers a named `IGrainStorage` with the DB vs shard factory; provider-section key resolution (4 tests exist).
- `Queries` catalogue: each `QueryProcedure` resolves to the declared `rdr.`/`wtr.` V1 name and parameter list (a `[Theory]` over the invariant).
- Integration (Testcontainers SQL Server): deploy `common.sql` and `tenant.sql`, then run the Orleans membership and reminder table contracts end to end. This is the only way to prove the scripts match the C# contracts.

### P3: logging, debug view, package invariants

**core-log-events** (25 spec, 0 tested; `LoggerExtensions.cs` 87% lines but almost no assertions): one table-driven test over every `LoggerMessage` delegate asserting event id, name, level, and that each wrapper actually invokes it. This directly guards the "event defined but never emitted" class of bug. Same for `SqlLoggingExtensions`, `PgLoggingExtensions`, and the Orleans `LoggingExtensions`.

**expression-debug-view** (18 spec; `DebugViewWriter.cs` is `[ExcludeFromCodeCoverage]`, 1,394 lines of vendored code): a handful of golden-string tests for precedence parenthesization and the lambda/block forms; low priority.

**Package invariants** (`argent-sea`, `argent-sea-core`, `*-provider-package`, `orleans-*-package`: ~20 invariants about csproj contents): not worth unit tests. Either a single test that parses the csproj XML, or treat them as documentation and exclude them from the coverage KPI.

## 5. Process recommendations

1. **Add CI.** There is no `.github/workflows`. A `dotnet test ArgentSea.slnx` on push plus the Cobertura collector (already works with `--collect:"Code Coverage;Format=Cobertura"`) is the minimum. Until it exists, the one failing Pg test will keep being invisible.
2. **Fix the failing test and the brittle pattern** behind it (whole connection-string equality in both Sql and Pg `ConfigurationTests`).
3. **Measure spec coverage, not line coverage.** Line coverage is misleading here (95% on `ExpressionHelpers` with no assertions). Adopt a convention where each test cites the block it verifies (for example `[Trait("Spec", "blk-…")]`), attach the link with `attach_spec_evidence` so `evidenceLinks` stops being empty, and report "non-rejected blocks with evidence" as the KPI. Today that number is 65 of 821.
4. **Review generated scenarios before pinning them.** 581 blocks are `GeneratedProposal` and some describe observed behavior that is plausibly a bug (serialization bounds checks, `QueryStatement` trailing-dot extension, nullable sentinels passed through as `Guid.Empty`). A test that pins such a scenario freezes the defect. Decide intent first, then write the test, then promote the block.
5. **Reorganize tests by spec section** rather than by the sweep that produced them, so a reader can find "the fan-out tests" without grepping.

## 6. Suggested sequencing

| Phase | Work | Approx. new tests | Unlocks |
| --- | --- | ---: | --- |
| 1 | Shared fake ADO.NET provider + CI + fix failing test | 0 | everything below |
| 2 | P0: resilient execution, shard addressing, databases, fan-out, batches | ~120 | the three core value propositions |
| 3 | P1: mapper handlers, shard-key family, config hierarchy, query definitions, parameters | ~150 | the daily-use surface |
| 4 | P2: provider services, Orleans persistence/validators/tables/hosting, Testcontainers for COPY and the Orleans SQL scripts | ~110 | Orleans.Sql from 10% to a real number |
| 5 | P3: logging table, debug view goldens, csproj invariants (or exclude) | ~40 | guards the "defined but never fired" bug class |

Rough outcome: core assembly from 51% to the high 80s, Orleans.Sql from 10% to 70%+, and spec evidence from 8% to a majority of non-rejected blocks.
