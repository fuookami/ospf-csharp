# Kotlin Sync Status

Baseline:

- C#: `2d2aedd1b3b54541b1c43569444e86d8edb0a7e9`
- Kotlin: `12fdb587eb4d02b1b944b2802b44648d224038d9`
- Plan: `logs/kotlin-sync-plan.md` (local only, ignored by git)
- Package version: `1.1.2`（跟基线根的 `pom.xml` 走，不是本仓自定的；判据 `PackagingMetadataTests` 钉住两边一致）

## Current Phase (as of Batch 1328)

**The sync is not complete.** Phases P4 through P8 are closed; **P9 is in progress** (Batches 242–1328).
The resumable inventory (what is left, the known gaps, and the batch procedure) lives in the handoff
section at the top of `logs/kotlin-sync-plan.md` (`## 零、截至轮次上限的交接状态`).

Closed in P9 so far: the examples and adapters (Batches 242–248), nine benchmarks plus the run entry
point (249–256), axis reductions in `Math.MultiArray` (257), the Bpp3d packing-geometry chain that turned
a guard which always answered "no overlap" into a real one — overlap, missing geometry, outside-bin,
support coverage, loading plans, weights (258–263), the benchmark trend script (264) and the
broad-phase sweep (265).

Summary of what is left in P9:

- ~~Bilingual `README.md` / `README_ch.md`~~ — expanded in Batch 267 (98/93 → 171/158 lines, same topic
  coverage; the baseline's extra bulk is Maven `<dependency>` blocks with no C# counterpart). Demo5's bilingual
  READMEs landed in Batch 268 and are pinned by `Demo5DocumentationTests`.
  **The rest of this bullet used to say "Still missing: the other modules' READMEs, a migration guide, and the
  capability matrix" — B1292 checked all three and <b>all three exist**: the other modules carry
  `README.md`/`README_ch.md` (`Bpp2d`, `Bpp3d`, `Csp1d`, …), the guide is `docs/kotlin-migration-guide.md`, and the
  matrix is `docs/kotlin-capability-matrix.md`. Criterion: `StatusDocLeftoverTests`. The stale "still missing" is
  the same disease the third bullet below warns about: a stale cannot stops the work.**
- ~~`Yaml` / `Toml` channels~~ — **retracted in Batch 267**: YAML/TOML encode and decode have been
  implemented in `ConfigurationFormats` (via `Tomlyn` and `YamlDotNet`) and are covered by
  `ConfigurationFormatRoundTripTests` and `ConfigurationFormatsTests`.
- ~~`PatternColumnBenchmark.kt` cannot be ported: it needs BPP1D's `PatternAssignmentContext`, and there
  is **no `Framework.Bpp1d` project here at all**; `GenerationCacheBuildBenchmark.kt` needs
  `DFSGenerator`, which is `internal` to `Framework.Csp1d`.~~
  **Retracted in Batch 953, both halves were false**:
  `src/Framework.Bpp1d` **exists**, `DFSGenerator` is **`public`**, and both benchmarks were ported and run
  (six benchmarks each) in Batch 952. The claim had been sitting here since the project was smaller and
  nobody re-read it — **a stale "cannot" is more expensive than a stale "done", because it stops the work**.
- ~~`Infra.Item.Weight` is carried but takes part in no constraint: `BinType.Capacity` is not used for a
  weight limit.~~ **Retracted in Batch 953**: `BinType.MaxWeight`
  **is** consumed by `PackingProblem.ValidatePattern`, line for line with the baseline (item weight is
  converted into the bin's unit, scaled by the count, summed, and compared against `maxWeight`). The field
  the old note named, `BinType.Capacity`, **does not exist in the baseline either** — the baseline's
  `BinType` is `(id, length, maxWeight, cost, inventory)`.
- ~~The baseline's PWL diagnostics and shape-DTO cache remain unported~~ — **retracted in Batch 955, and these two
  were never gaps at all**: the pinned baseline has **no shape cache anywhere** (`git grep -i "shape.*cache"`
  returns nothing outside the changelog, and `cache` appears only in benchmark names and README prose), and the
  BPP modules have **no PWL and no diagnostics either** (`git grep -ic pwl` over
  `framework-bpp1d/src/main` and `framework-bpp3d/src/main` returns zero). **Both notes named capabilities the
  baseline does not have**, so there was never anything to port. The one real PWL gap is in the solver plugins
  and the missing function expansion layer, measured in Batch 954.

Known-unreliable areas are listed in the handoff section as well; the most important one is that
**behavioural assertions cannot catch "wired to another legal algorithm"** (four surviving mutants in
Batches 215, 216 and 217). No claim of Kotlin interoperability or performance improvement is made
anywhere: cross-language validation was measured only for `SerializedSolution`,
`RemoteTaskSubmitRequest`, and the `configuration`/`sha256` digest.

## Status Legend

- `Implemented`: the C# capability and its behavioral tests exist.
- `Contract verified`: no external service or native SDK is required, and the contract tests pass.
- `Integration verified`: the real SDK, license, database, or message service was exercised successfully.
- `Gap`: capability is missing or still contains a placeholder.

File count parity is not evidence of capability parity.
Batch evidence records the state at the time of that batch; later batches can supersede earlier
limitations. The capability map and current gate describe the latest verified state.

## Capability Map

| Area | Kotlin source | C# entry point | Status | Next batch |
|---|---|---|---|---|
| Log and heartbeat | `framework/log`, `RunningHeartBeat.kt` | `Framework.Log`, `RunningHeartBeat` | Contract verified | P0 |
| Utils update delta | `utils/parallel`, `Time.kt`, `UUIDv7.kt`, `DurationSerializer.kt` | `Utils.Parallel`, `TimeExtensions`, `UuidV7`, duration serializers | Implemented; public-surface equivalence audited | P1 complete |
| MultiArray update delta | `multiarray/AccessOrder.kt`, `MultiArray.kt`, shape tests | `AccessOrder`, `MultiArray`, `MultiArrayView` | Contract verified | P1 |
| Math update delta | distributions, `ChannelProjection.kt`, fractal helpers, random extensions | `Math.Distribution`, `Math.Sequence`, `Math.Fractal`, `RandomExtensions` | Contract verified | P1 |
| Quantities update delta | composed-unit domain operations | `PhysicalUnit` | Contract verified | P1 |
| Core model and diagnostics | `core.model`, `core.solver.report`, `core.analysis` | `Core` | Contract tests cover unified report and its conservative numeric conversion, status-aware legacy no-solution mapping, identity registration, immutable provenance, direct matrix identity propagation and coordinated fallback-identity/token commit, linear-state and derived token snapshots, partial source-structure and dump-time variable invalidation, read-only matrix column/objective/metadata collections, sparse-backed row projection, shape validation, independent continuous-relaxation batches and managed matrix snapshot validation, atomic in-process identifier allocation, registered function dependencies for frozen dynamic rows, closure lifecycle, partial nested linear-symbol lowering, generic factory-time symbol preparation and direct generic matrix conversion, explicit fixed-variable elimination and solution restoration, resolved and combined objective evaluation, criticality profile, explicit dual and Farkas dual model construction with row-aligned certificates, CP exact-domain primitives, constraints, CP model snapshot, interval constraints, deterministic fake session/solver, exact finite MIP lowering, portable checkpoint schema 3.0, solver-readable linear and quadratic LP export, and selected function behaviors including generic cardinality indicators and positive-part graphs; dynamic write transactions capture tokens, constraints, constraint groups, objectives, derived row state and stable identities and recover automatically when a commit fails; removed constraints, objective terms and dynamic rows are structured operations, and removed identities become reusable. Concurrent or reentrant model mutation is not supported; the earlier 37-of-49 function inventory predates these new types and needs recounting. Partial `If`, `IfIn`, `IfThen`, `Imply`, `Sigmoid`, univariate and bivariate piecewise lowering is available. Native termination/proof propagation and generic higher-level solver entry points, broader generated-matrix identity propagation, automatic fixed-column solving, complete structural invalidation, conditional, matrix, native-function backend dispatch, nested and quadratic function semantics remain gaps | P2 |
| Solver runtime configuration and provenance | `core/solver/config/SolverConfig.kt`, `core/solver/report/SolverReportMetadata.kt`, `core/solver/Gap.kt` | `Core.Solver.Config.SolverConfig`, `BackendConfigurations`, `SolverReportMetadata`, `SolvingStatusSupport`, `NoImprovementMonitor` | Contract verified: one shared runtime record with validated ranges, sorted and redacted backend parameters, snapshot and provenance keys, pinned gap formula and no-improvement gates; duration text still differs from the Kotlin `30s` form, so the cross-language configuration fingerprint stays a P4 item | P3 |
| Gurobi adapter | `core-plugin-gurobi` | `Core.Plugin.Gurobi` | Contract verified; selected linear and quadratic solves, explicit fixed-column restoration, the shared time limit, MIP gap, thread count and status-callback path, and Gurobi readback of an exported quadratic LP verified with Gurobi 10.0.1; the reported backend version is the managed assembly file version, not a native-library probe; not full backend acceptance | P3 |
| Gurobi 11 adapter | `core-plugin-gurobi11/GurobiLinearSolver.kt`, `GurobiQuadraticSolver.kt`, `GurobiColumnGenerationSolver.kt`, `GurobiBendersDecompositionSolver.kt` | `Core.Plugin.Gurobi11.*` over the shared `Core.Plugin.Gurobi` implementation | Compatibility facade verified on the installed Gurobi 10.0.1 ABI: linear and quadratic reports, column-generation LP row duals, feasible Benders row duals, infeasible Benders Farkas multipliers, cancellation, capabilities, provenance and fingerprints. Reports identify `gurobi11` / `Gurobi 11` with the loaded managed assembly version. An independent Gurobi 11 SDK is not installed, so native Gurobi 11 acceptance, Gurobi11-specific function lowering/selection, SOS2/PWL writers, remote configuration, native IIS and quadratic Benders remain unverified | P3 |
| SCIP adapter | `core-plugin-scip/ScipLinearSolver.kt`, `ScipQuadraticSolver.kt`, `ScipConstraintProgrammingSolver.kt` | `Core.Plugin.Scip.ScipLinearSolver`, `ScipQuadraticSolver`, `ScipConstraintProgrammingSolver` | Selected LP/MIP/QP/QCP solves, master solves, event-limited cancellation, initial-solution hints, solution pools, status callbacks, native parameter application, per-row LP duals, Farkas certificates, shared legacy IIS diagnosis, and the finite-domain CP facade are verified with Windows SCIP 9.2.4. The CP facade is strictly an exact lowering to integer MIP, not native CP: Boolean logic, reification, sparse domains, AllDifferent, Element, tables, intervals, NoOverlap, assumptions, hints, and incremental solves are declared as `ExactLowering`; Cumulative requires explicit policy opt-in, and Circuit, Automaton, Reservoir, conflict cores, native logging, and true backend CP remain unsupported. Quadratic objectives use the baseline's explicit epigraph-variable/equality lowering; concurrent solving is not ported and is recorded as an ignored parameter; dual and Farkas elements are anonymous model-local identities mapped back by row index, certificates carry row multipliers only, and native IIS, quadratic IIS, and native writers remain missing | P3 |
| COPT adapter | `core-plugin-copt/CoptLinearSolver.kt`, `CoptQuadraticSolver.kt`, `CoptBendersDecompositionSolver.kt`, `CoptSolverCallBack.kt`, `CoptNativePiecewise.kt` | `Core.Plugin.Copt.CoptLinearSolver`, `CoptQuadraticSolver`, `CoptBendersDecompositionSolver`, `CoptColumnGenerationSolver` | Integration verified with Windows COPT 8.0.3 under the non-commercial size limit: LP/MIP and QP/QCP solves, row duals and Farkas certificates for LP, native IIS for LP and finite MIP, solution pools, initial-solution hints, cancellation/status callbacks, and accurate unbounded/zero-column/time-limit outcomes. Declared linear capabilities include Dual/Farkas/NativeIis/WarmStart/SolutionPool/Interrupt; quadratic capabilities include WarmStart/SolutionPool/Interrupt only. The native callback manager and creating-environment callbacks, quadratic Benders, COPT SOS2/PWL dispatch, and commercial-scale license acceptance remain gaps | P3 |
| CPLEX adapter | `core-plugin-cplex/CplexLinearSolver.kt`, `CplexQuadraticSolver.kt`, `CplexBendersDecompositionSolver.kt`, `CplexColumnGenerationSolver.kt`, `CplexSolverCallBack.kt` | `Core.Plugin.Cplex.CplexLinearSolver`, `CplexQuadraticSolver`, `CplexBendersDecompositionSolver`, `CplexColumnGenerationSolver` | Integration verified with Windows CPLEX 12.10.0.0: LP/MIP and QP/QCP solves, objective offsets, LP row duals, Farkas certificates, pricing and feasible Benders row duals, infeasible Benders Farkas multipliers, solution pools, initial MIP starts, status callbacks, cancellation and time limits. Linear capabilities declare Lp/Mip/Dual/Farkas/WarmStart/SolutionPool/Interrupt; quadratic capabilities declare Qp/Qcp/Mip/WarmStart/SolutionPool/Interrupt. Native IIS, a complete point-based callback manager, SOS2/PWL dispatch, quadratic Benders, remote backend configuration, commercial-scale and non-Windows acceptance remain gaps | P3 |
| Other solver adapters | `core-plugin-*` | `Core.Plugin.*` | Partial: all adapters share the unified runtime configuration. MOSEK now has a compile-optional linear LP/MIP implementation, unified reports, initial-solution submission, limits and an SDK-free environment-lost facade, but its three native cases are blocked by a missing license. MindOPT has compile-optional LP/MIP and QP/QCP paths but is blocked by an invalid license. Hexaly now has compile-optional LP/MIP and QP/QCP native paths with reports, callbacks, cancellation, warm starts and column-generation LP duals, but real solves are blocked by a missing license. LINGO and OPTVerse match the pinned Kotlin empty placeholder modules: C# exposes only the empty classes, not an `ILinearSolver` facade, and claims no backend capability | P3 |
| Shared framework | `framework/solver` | `Framework.Solver` | Linear/quadratic combinator contracts verified. The shared linear Benders engine covers model partitioning, optimality/feasibility cuts, verified incumbent merging, revision-aware stopping and unified reports; Gurobi, Gurobi11, SCIP, COPT, CPLEX and MindOPT adapters expose the budget and report boundary. Logic-based Benders covers binary and bounded-integer bindings, CP value sources, verified conflict/no-good cuts, exact integer no-good encoding, cut validation/deduplication, cancellation, progress callbacks and proof-gated reports. Portable Benders checkpoint/restore now covers standalone schema 1.0, legacy schema 1.0 migration, the Core CP envelope schema 3.0, SPI cut re-decoding, exact-proof gating, trace validation, strict master-resume evidence and next-iteration continuation. `ColumnProjection<V>` now covers captured sparse indexes, source and column-pool revisions, exact-one masters, phase-one and integer forms, refined resources, identity-checked duals, same-snapshot pricing batches, stale-batch rejection and solution extraction. `ColumnGenerationBatch<C>` now validates exact scoped exhaustion and no-improving-column evidence with recursive child coverage; multi-constraint cut installation is atomic, and linear/quadratic constraint rollback restores constraint-group indexes. Dynamic write transactions capture tokens, constraints, constraint groups, objectives, derived row state and stable identities, with automatic recovery when a commit fails; dynamic rows can be added, removed and re-registered under the same identity, removed constraints release their identity, non-contiguous constraint-group reuse is rejected, and column replacements are materialized before publication. Concurrent/reentrant model mutation, strictly atomic two-step master publication and full projection thread safety are not claimed. Remote protocol parity and the complete cross-language wire matrix remain pending | P4 |
| Remote protocol | `framework/solver/remote` | `Framework.Solver.Remote` | Partial. `src/Framework/Solver/Remote/` was an isolated stub with no callers (12 files, 876 lines) against 22 pinned Kotlin main files (~9.6k lines). Two batches landed so far. The wire result contract adds `SerializedSolution` with `ObjectiveValueInt64` (so `9007199254740993` survives without a double round-trip), `VariableValuesById`, `IntervalValues`, `ElapsedMs` and the baseline provenance/fingerprint/statistics maps; `RemoteWireEnums` plus `RemoteWireEnumConverter<TEnum>`/`RemoteNullableWireEnumConverter<TEnum>` map unrecognized server values to `Unknown` or null without throwing and never read them as success; `ClaimsExactOptimum` follows the baseline rule (`optimal` authoritative, feasible, non-`NONE` proof, `OPTIMAL` presence), so a zero gap alone never proves optimality; `RemoteReportMapping` prefers the exact integer objective. The `ExportCheckpointAsync`/`FetchFinalResultAsync` `Ok(null)` stubs are replaced with real decoding, and an empty body still means "not produced yet". Scheduling models (`SchedulingEstimate`, `QualityTarget`, `SchedulingRequest`, `SchedulingDecision`), the baseline `TaskComplexity`/`TimeSensitivity`/`TaskStatus`/`SliceStatus`/`TaskMeta`/`ModelData`/`SolverConfig`/`SolvePayload` shapes, and `GET /api/v1/capabilities` with envelope decoding are in place; `StartAsync` probes capabilities before a CP submission and rejects without uploading when schema major, protocol `2.0` or the `CP` model type is missing. Four pinned fixtures are byte-identical to `12fdb587` and round-trip through the decoder. Submission still omits `requestId`/`priority`/`budgetScope`/`deadline`/`scheduling`, `SchedulingDecision` is not yet wired into slice or final results, and canonical-digest algorithm, configuration duration semantics, asynchronous replay, exactly-once merging and the nine missing Kotlin client/codec files remain gaps. The transport plugin registry (`RemoteSolverHttpTransportPlugins`) registers the baseline `jdk` plugin and rejects unknown names with `SolverNotFound` instead of falling back; `ResumeAsync` rejects `StrictCheckpoint` mode without issuing any request, and `StopAsync` only reports success for `STOPPED`/`COMPLETED`, polling up to 32 times and returning `false` otherwise. `PortableCheckpointCodec` forwards `Encode`/`DecodeOrNull`/`DecodeCompatibleOrNull`/`Sha256` to Core with no second field list or digest, keeps migration behind the explicit compatible entry, and throws on an unencodable envelope. `RemoteReportMapping.ValidateLinearQuadraticResult` enforces the baseline v1/v2 rules (future majors rejected, feasibility/optimality/presence consistency, no objective without an incumbent, strict results need a floating objective, no dual objectives, no Int64 objective on linear/quadratic results, complete fingerprint schemas and run/attempt identifiers) and gates `FetchFinalResultAsync`. Both timeout knobs map onto the single `HttpClient.Timeout`, report mapping does not yet cover diagnostics/statistics/issues, and `AwaitSliceEndAsync` still sends fractional milliseconds. Cross-language behavior is now measured rather than assumed: a temporary probe on the pinned Kotlin `12fdb587` side decoded C#-produced JSON, encoded JSON that C# decoded, and re-encoded C#'s output so C# could decode it again — all three directions agreed, and that run caught a real defect C#-side computed properties (`claimsExactOptimum`, `ModelData.IsReference`/`IsInline`/`ModelType`, `SolvePayload.ModelRef`) leaking onto the wire, which `[JsonIgnore]` now prevents. The Kotlin-produced output is kept as `tests/Framework.Tests/RemoteFixtures/solution-kotlin-encoded-v2.json`. Coverage is still only `SerializedSolution` and `RemoteTaskSubmitRequest`: the scheduling/capability shapes and the CP checkpoint envelope have not been cross-language round-tripped, and Kotlin's unknown-enum filter versus the C# converter has not been compared byte for byte, so broader interoperability is not claimed. Identifier value types now use `RemoteWireIdentifierConverterFactory` so they serialize as bare strings exactly like the Kotlin value classes, and `StartAsync` submits the baseline `RemoteTaskSubmitRequest` to `POST /api/v1/tasks` after uploading the payload through `objectStoragePort`, refusing to produce a handle for a rejected or unknown status. `ResumeAsync` verifies a tenant-scoped v2 checkpoint before the call (path shape, schema `3.0`, `sourceFormat = "v2"`, complete identity, `runId` equal to the task id, `attemptId` prefixing the checkpoint id, and matching model/configuration/solver fingerprints) and then requires the resume action to preserve that identity, treating a missing field as a mismatch. The action view still omits statistics, diagnostics, cancellation chain and ancestor provenance. `RemoteReportMapping.ToRemoteIssues`/`ToRemoteDiagnostics` parse the indexed `<prefix>.<index>.code|category|message|details.<key>` fields into Core `SolveIssue`/`SolveDiagnostics<Flt64>` with numeric index ordering, rejecting missing code/category/message, unrecognized categories, malformed keys and unknown diagnostic prefixes — but `infeasibility.` evidence is not parsed yet and the result mapping does not consume these helpers. The canonical `configuration`/`sha256` fingerprint is now **measured** to match the pinned Kotlin baseline byte for byte (input containing `=`, newline, backslash and non-ASCII produced `9f29745e…` on both sides), so an earlier documented "length-prefixed versus `key=value`" divergence is retracted: it was a documentation error, not a code defect. The `solver` fingerprint is a C#-side extension rather than a sync gap: the pinned Kotlin `SolveFingerprinting` has only `sha256` and `configuration` (plus a private `escape`), so an earlier documented "solver/model entries not yet compared" is retracted as a documentation error; the extension is now pinned by a regression test on its exact audit value so a refactor cannot silently change persisted solver identities | P4 |. `AwaitSliceEndAsync` now decodes the envelope and parses the SCREAMING_SNAKE status through `RemoteWireEnums.TryParseTaskStatus` (it previously compared against a lowercase `"completed"` literal, so a completed task could never be recognized) and carries `Status`/`Outcome`/`RunId`/`AttemptId`/`Scheduling` on `SliceResult`; the objective and gap fields are still null because the task-view and artifact sources are not parsed yet. `AwaitSliceEndAsync` now polls to a terminal state like the baseline, returning a non-terminal result on `SUSPENDED`, refusing to infer from an unknown status, rejecting a non-positive quantum before any request, and setting no client deadline so a normal server suspension is never converted into a task stop. The baseline's unknown-semantics rule is now enforced: when the status, slice outcome, termination reason, solution presence, proof status or scheduling outcome is `UNKNOWN`, `RemoteTaskActionView.InferredFeasible` stays false and `InferredOutcome` stays `UNKNOWN`, so a "completed but semantically unknown" observation can never be reported as a feasible solution (it previously keyed feasibility straight off `status == Completed`). `RemoteTaskActionView.ToSliceResult` now performs the baseline merge (result artifact beats the task view beats status inference) into a widened `SliceResult` carrying problem status, termination reason, solution presence, proof status, checkpoint/incumbent/result references, artifact digest, best bound, statistics, diagnostics and schema version; `FetchResultArtifactAsync` supplies the artifact at a terminal state and treats an absent artifact as "not produced yet". The full termination fallback chain is in place, including "`FAILED` must never report `COMPLETED`", and proof status is never back-inferred from feasibility. `SchedulingDecision.Complete` fills only the `taskId`/`sliceId`/`nodeId`/`checkpointRef`/`incumbentRef`/`modelFingerprint`/`outcome` fields the server left empty, never overwriting a supplied value, and forces the outcome to `UNKNOWN` under unknown semantics. `IdentityFingerprints()`/`IdentityFingerprintSchemas()` merge the explicit maps with the three flat fingerprint fields and let the scheduling model fingerprint win, with blank schemas skipped rather than written as empty entries, and the artifact's entries overwrite the view side. `Provenance` merges in baseline `putAll` order (view first, artifact overwrites by key, omitted keys survive). The baseline's `forceTimeout` parameter is deliberately not ported: no call site in the pinned file ever passes `true`, so it is the baseline's own dead branch rather than a missing capability. All requests now use only the baseline's five paths — `POST /api/v1/tasks`, `GET /api/v1/capabilities`, `GET /api/v1/tasks/{taskId}`, `POST .../stop` and `POST .../resume`; earlier invented `/tasks/resume`, `/tasks/{id}/slices/{sid}/await`, `/tasks/{id}/checkpoint` and `/tasks/{id}/result` endpoints are gone, `GetTaskViewAsync` is the single observation path, a missing task is a failure rather than "still running", and `ExportCheckpointAsync`/`FetchResultArtifactAsync` derive from `latestCheckpointRef`/`latestResultRef` plus the object store. `FetchFinalResultAsync` follows the baseline flow (view, `HasObservation` guard, artifact via `latestResultRef`, merge through the already-tested `ToSliceResult`, map to `SolveResult`) instead of decoding the response body directly, and a missing task yields null rather than a failure. `ToRemoteEvidence` parses the `infeasibility.*` fields into Core's `InfeasibilityEvidence` and feeds `ToRemoteDiagnostics`: no such field yields null (not an error), all five axis fields are mandatory and must match verbatim, the five list fields are JSON-array-encoded strings that fail when malformed, and variable-bound references split on the last colon so an identifier may contain colons. Their wire names are the **raw PascalCase member names** (`Lower`, `Verified`, `Exact`, `NotChecked`), not SCREAMING_SNAKE — the baseline compares with `valueOf(name)` and never changes case — and `InfeasibilityEvidenceSource.NativeIis` is mapped to the baseline's `NativeIIS` spelling without renaming the Core enum member. The evidence tail fields are parsed too (`variableBoundIds` derived from the refs, `elapsedMs` rejecting negatives, `unavailable.*` requiring a matching category and a non-blank message, `reference`, `verificationChecks` rejecting negatives, `terminationReason`). `SliceResult.SolveDiagnostics` is filled by the client-side `AttachDiagnostics` — parsing lives in Client because `RemoteReportMapping` belongs there and Domain must not depend on it — and a diagnostics parse failure never fails the whole result: the raw `Diagnostics` map survives and the reason is folded into the message. Cancellation entries round-trip through `RemoteTaskActionCancellation`, and an unknown `origin` keeps its raw text as `WireOrigin` — the baseline's catch-all variant cannot express both `external` and `backend`, so without it `backend` would be written back as `external`. `provenance` is deliberately **two types**: the view's is a `Map<String,String>` (matching `RemoteTaskView` and the `GET /api/v1/tasks/{taskId}` path) while `RemoteTaskActionProvenance` is a separate typed record reserved for action responses, because giving both the same field made `threadCount` unparsable. Payload upload now carries object metadata: `IObjectStoragePort` gained a metadata overload as a **default interface method** (so existing implementations keep compiling while a capable one can persist it), and `PutPayloadAsync` passes `taskId`/`sliceId`/`tenantId` as the baseline does instead of only a content type. There is no "async replay" or "exactly-once merge" layer: a scan of the pinned baseline finds neither, so that long-standing item is retracted as a documentation error. Idempotence rests on two documented facts, both already implemented and now tested — `requestId` is a deterministic `<tenant>:<task>:<slice>` id so a retry is the same request, and cancellation interrupts the backend only once while a listener registered *after* cancellation still runs immediately (inverting that would leak an un-interrupted backend). `CheckpointMetadata` now uses the baseline's `ref`/`createdAtEpochMs` field names (it previously wrote `objectRef`/`createdAt`, so neither side could read the other's required fields), and the poll interval matches the baseline's 200 ms instead of 50 ms. `RemoteConstraintProgrammingClient.SolveAsync` ports the baseline's `solve`: the CP snapshot is encoded through Core's `ConstraintProgrammingSnapshotCodec` into `ospf-cp-snapshot-json` raw model data with `targetType=cp` and size estimates, an encoding failure returns a structured error without any network request, and the scheduling quantum deliberately never reaches `config` because the baseline keeps it out of the checkpoint fingerprint. `OspfRemoteModelSerializer.IsValidRemoteIdentity` is the identity gate every element must pass before remote serialization: a self-declared origin must appear in its provenance (contradiction is rejected rather than half-trusted), `ModelLocal` may carry neither origin nor provenance and may not use an `artifact:` id, and `Stable` requires a non-`model-local-` id with a namespace, a schema version and a recorded origin. The wire models now carry the baseline's identity fields (`identityId`, `identityScope` defaulting to `MODEL_LOCAL`, origin kind/key, namespace, schema version, `identityProvenance`), which were previously absent so the remote side lost element-identity provenance, and `Serialize(ILinearTriadModelView)` validates the whole model through Core's existing `ValidateIdentity()` before gating each element — no model-level accessor had to be invented, since whole-model validation already guarantees one shared namespace and schema. The quadratic wire models had three structural defects that are now fixed: `SerializedQuadraticConstraintCell` carried a single `colIndex` and could not express a quadratic term at all, `SerializedQuadraticObjectiveCell` carried a row index the baseline does not have while missing `colIndex2`, and `SerializedQuadraticModel` merged the linear and quadratic constraint lists into one, losing the linear constraints' shape. (Kotlin's nullable `colIndex2` and C#'s `Col2 < 0` sentinel differ internally but not on the wire, since both emit separate `linearCells`/`quadraticCells` lists.) `Serialize(IQuadraticTetradModelView)` and the three `ModelData` factories are now in place — the same row deliberately appears in both the linear and quadratic constraint lists (the baseline's way of letting a linear consumer read a quadratic model), and a serialization failure propagates unchanged instead of producing an empty payload. `ValidateRawCpResult` ports the baseline's self-consistency gate — schema majors 1/2 only, feasibility cross-checked against problem status, optimality, proof status and solution presence, paired result-ref/digest, parseable statistics, and matching v2 fingerprint key sets — so a contradictory result is rejected rather than handed downstream; `NormalizeLegacyCpResult` marks a legacy 1.0 "infeasible + completed + no proof status" result as `CLAIMED` instead of `NONE`, keeping "explicitly unproven" distinct from "field absent". One baseline check cannot be copied literally: its expected presence derives from independent optimal/feasible booleans, while C# encodes optimality in the presence field itself, so only the non-circular half is enforced. `ValidateExecutionBinding` adds the cross-talk guard: under v2+ the task/slice must match exactly (under v1 only when present), and whenever the result carries any observation it must present a model fingerprint matching this snapshot and a configuration fingerprint matching this request — so a result produced for another task, slice or model is rejected rather than attributed to this request. The model fingerprint is an **instance-level** binding, not a content hash: the snapshot encoding embeds process-global variable identifiers, so a freshly built, identically-shaped snapshot hashes differently. The binding therefore holds only for the very snapshot that was uploaded, and the value is neither a content address nor comparable across processes. That property was traced to its source: both sides generate CP snapshot variable identifiers from a **process-global** incrementing counter in the identical `identifier:index` shape (C# additionally makes it thread-safe, which the baseline documents as *not* thread-safe), so a CP snapshot encoding is process-dependent by design and cannot be compared byte-for-byte across processes or languages. `ValidateSerializedCpResult` guards the two sources of truth — the raw result and the stored stable-solution artifact — rejecting any disagreement on feasibility, optimality, proof status, schema version, termination reason, solution presence, problem status, objective (exact and floating), gap, provenance/fingerprints, statistics/diagnostics, run/attempt or artifact digest, since otherwise it is impossible to say which one the caller is handed. As in the baseline, v2+ requires field-by-field equality while a v1 artifact may omit fields but must match any it supplies — schema version itself is never relaxed. `RemoteCpResultArtifact` loads the artifact (a missing `resultRef` yields `null` rather than an error, since a legacy backend may omit it; a reference without storage, a missing object, a failed read or malformed JSON each produce a structured error) and enforces the format-independent structural rules — v2 markers conflicting with a legacy schema, all 22 audit fields present, 8 key fields non-blank, with the error naming exactly which fields failed. **Digest recomputation is deliberately not implemented**: the baseline hashes Kotlin's `Json { encodeDefaults = true }` output, which is sensitive to `SerializedSolution`'s field declaration order, default-value set, `Flt64` serialization and enum naming, and the baseline cannot be executed here to verify byte equality — guessing the order and shipping it would reject *valid* baseline artifacts, which is worse than not checking. Only agreement between the artifact's declared digest and the result's declared digest is enforced, and digest recomputation is not claimed as a capability until measured across languages. `RemoteCpResultMaterializer.Classify` returns a closed three-state classification — only "infeasible with `INFEASIBLE` and `COMPLETED`" is a concluded infeasibility while every other infeasible result is unknown (so a time limit is never reported as "no solution exists"), and a feasible result without a stable solution is an error whenever the snapshot has variables or intervals rather than an empty solution being fabricated — then validates the stable solution in full (exact variable set, per-value domain containment, interval set and per-interval definition). It deliberately does not build the Core output record at that stage, because that needs a `SolveReport<Int64>`; a later batch verified that C#'s `SolverProvenance` is in fact structurally identical to the baseline's (`Descriptor`, `NativeVersion`, `EffectiveParameters`, `IgnoredParameters`, `ThreadCount`, `RandomSeed`, `Deterministic`, `EnvironmentSummary`), so no design decision is pending there after all. `RemoteSolverReportMapping` also covers the linear/quadratic path: `ToIncumbentReport` rejects a solution whose length differs from the model variable count (a mismatch means it answers another problem and is never truncated or zero-padded), treats a missing objective as an error rather than 0 (zero is a legitimate objective), and reports infeasibility as `ORModelInfeasible`; `ToEmptySolutionOutput` allows an empty solution only for a zero-variable model, since "empty but successful" would hide an incomplete result; `ToSolveReport` materializes the incumbent only when the presence is not `NONE`, so "says there is none" is never confused with "happens to have no solution field". These take an already-loaded artifact and stay synchronous, unlike the baseline where they are `suspend` and read storage inline. `RemoteSolverClient.SolveSliceAsync` returns the **unreduced** terminal slice: the existing `SolveAsync` collapses it into the seven-field `SolveResult`, which has no `ResultRef`/`ArtifactDigest`/`ProblemStatus`/`TerminationReason`/`SolutionPresence`/`ProofStatus`/`RunId` and therefore cannot support materializing a stable solution or assembling an audit report. The new method reuses the same loop and the same single observation path, and neither `SolveAsync` nor `ISolverExecutionPort` changed. `RemoteLinearSolver` decorates a local solver but routes the unified report entry over remote execution — identity validation, model serialization, submission, unreduced terminal slice, artifact read, report assembly. Its legacy blocking entries deliberately return a structured error instead of forwarding to the local solver, because forwarding would silently turn a "remote solver" into a local one on some entries (dumping still forwards, since it only converts models and can never switch the solving backend). `RemoteQuadraticSolver` is symmetric, with `targetType=quadratic` so the backend does not interpret a quadratic payload as linear and silently drop its quadratic terms; C#'s `IQuadraticSolver` has no `Descriptor` member, which is reflected rather than papered over by adding one. `RemoteSolverRuntimeOptions` replaces the previously generated task/slice/node/tenant values with injectable ones. `RemoteCpResultMaterializer.ToOutput` now builds the three Core CP output records — infeasible, unknown and feasible are deliberately three distinct records so downstream can tell "no solution" from "unknown" — and an optimality claim becomes `SolverStatus.Optimal` only when it is a proven optimum, otherwise it is downgraded to `Feasible`; `ToCpReport` keeps the objective as an `Int64` and propagates a diagnostics parse failure instead of emitting a report without the infeasibility evidence. The cancellation token now reaches the execution port on every call (it was previously swallowed in the middle layer), and the out-of-domain rejection branch finally has a fixture — a variable restricted to `[0, 2]` receiving 5 — with an in-domain case asserted first so the rejection cannot pass for an unrelated reason. `ToRemoteSolveReport` assembles a `SolveReport<Flt64>` on the slice result, preferring the materialized best bound over the backend's self-reported statistic, mapping `UNKNOWN`/missing termination to `BackendFailure` (never disguised as `Completed`) and `UNKNOWN` presence to `None`, and inventing no provenance when the backend supplied none. The materializer also **re-validates the solution against the snapshot** rather than trusting the backend: every constraint is re-evaluated and the objective independently recomputed and compared, catching a self-consistent solution that does not satisfy this model — something the cross-talk fingerprints cannot, since they only prove the same input was measured | P4 |
| Geometry and packing | `framework-geometry2d`, BPP/CSP modules | `Framework.Bpp1d`, `Framework.Bpp2d`, `Framework.Bpp3d`, `Framework.Csp1d`, `Framework.Csp2d` | P6 closed: all five projects exist and the gap inventory reports **0 real gaps** for `framework-bpp` (10), `framework-bpp2d` (56), `framework-bpp3d` (105) and `framework-csp2d` (18). `Box2Geometry` lives in `Framework.Bpp2d.Domain` because the geometry module was merged rather than given its own project — a different assembly, not a missing capability | P6 complete |
| Network and scheduling | network and Gantt modules | `Framework.GanttScheduling`, `Framework.NetworkScheduling` | P7 closed: `Framework.NetworkScheduling` exists (75 sources, covering allocation, design, flow, generalized flow, path, path compilation, resilience, route compilation, route generation and structure) and the gap inventory reports **0 real gaps** for its 114 baseline main sources. Earlier "NetworkScheduling pending" rows were stale | P7 complete |
| Heuristic | `core-plugin-heuristic` | `Core.Plugin.Heuristic` | P8 closed: the plugin carries the algorithm families (`GA`, `GCO`, `GWO`, `HCA`, `HS`, `MVO`, `NS`, `PSO`, `SAA`, `SCA`, `SOA`, `WCA`, `WarSO`, `EVO`), and `runner/`, the solver trio, `SearchRegistry`, `PolicyTrainer`, `BuiltInAlgorithms` and `CompositeSearchSession` all landed (last P8 change in Batch 241). Earlier "composition and hyper-heuristic rebuild pending" rows were stale | P8 complete |
| Examples and benchmarks | `example`, `benchmark` | `Example`, `Benchmark` | Demo4 request isolation and registry operations contract verified; broader Demo2/Demo4 and performance fixtures remain gaps | P9 |

## External Dependencies

Managed solver and framework projects discover SDKs through standard environment variables. Optional
managed SDK sources are excluded when unavailable; SCIP instead loads its C library at runtime.
The default solution remains buildable without those SDKs, which is not integration verification.

| Dependency | Environment | Required for | P0 status |
|---|---|---|---|
| Gurobi | `GUROBI_HOME`, license | Gurobi integration | Contract verified; selected real linear and quadratic solves, runtime configuration and status callbacks verified on 10.0.1; the reported version is the managed assembly file version, not a native probe, and this is not complete backend acceptance |
| Gurobi 11 | `GUROBI_HOME`, Gurobi 11 license | Gurobi 11 integration | Compatibility verified through the `gurobi11` facade on the installed Gurobi 10.0.1 ABI. The build prefers `gurobi110.netstandard20.dll` and falls back to `gurobi100.netstandard20.dll`; independent Gurobi 11 SDK/native acceptance remains unverified |
| SCIP | `SCIP_HOME` or the native library search path | SCIP integration and CP checks | Selected Windows 9.2.4 linear, quadratic, quadratically constrained, and exact-lowering CP solves verified, including fixed values, false assumptions, scalar and interval hints, AllDifferent, and tables; native IIS, native CP diagnostics, quadratic IIS, and the native writers remain gaps |
| COPT | `COPT_HOME`, license | COPT integration | Integration verified on Windows 8.0.3 under the non-commercial size limit: LP/MIP, QP/QCP, LP dual/Farkas, native LP/MIP IIS, solution pool, initial hints, cancellation and status callbacks. No commercial license was exercised; native callback points, quadratic Benders, and SOS2/PWL writers remain unverified |
| CPLEX | `CPLEX_STUDIO_DIR1210`, native DLL directory in `PATH`, license | CPLEX integration | Integration verified on Windows CPLEX 12.10.0.0: LP/MIP, QP/QCP, LP dual/Farkas, solution pools, initial hints, cancellation and status callbacks. **B1220 correction: point-based callbacks are ported** (`CplexSolveCallback` 201 lines plus `CplexContinuousCallback`, both wired via `model.Use(...)` in `CplexNativeSolver.cs:150,160`; the baseline `CplexSolverCallBack.kt` is 157 lines). Native IIS, SOS2/PWL writers, quadratic Benders, remote backend configuration, commercial-scale and non-Windows acceptance remain gaps |
| MOSEK | `MOSEK_10_2_INSTALLDIR`, `MOSEK_10_2_BINDIR`, license | MOSEK integration | Implementation and contract verified for linear LP/MIP, unified reports, objective offsets, initial solutions, time limits, thread settings and the SDK-free environment-lost facade. MOSEK 10.2.11 is installed but no license is available; all three opt-in solves return `AuthenticationError` / `err_missing_license_file(1008)`, so real-backend acceptance remains unverified. Only the linear entry exists; dual, Farkas, native IIS, solution pools, native progress callbacks and in-solve interruption are not declared |
| Hexaly | `HEXALY_HOME`, license | Hexaly integration | Implementation complete and contract verified for LP/MIP and QP/QCP snapshots, native model construction, unified reports, objective offsets, time/thread limits, warm starts, lifecycle/status callbacks, cancellation, no-improvement stopping and column-generation LP row duals. The managed SDK 13.0.20241120-Win64 is installed, but every native solve is blocked by missing `license.dat`; no LP/MIP/QP result is accepted. Multi-solution requests are explicitly unsupported, and Dual/Farkas/NativeIIS/SolutionPool are not declared |
| MindOpt | `MINDOPT_HOME`, license | MindOpt integration | Implementation complete for LP/MIP, QP/QCP, objective offsets, row duals, Farkas evidence, solution pools, warm starts, limits and MIP cancellation; SDK 2.3.0 is present but its license is invalid, so integration is unverified. The no-SDK build uses an explicit environment-lost facade |
| LINGO | Not applicable | None | Pinned Kotlin module is an empty placeholder. C# mirrors the empty class surface, implements no solver contract and has no integration target |
| OPTVerse | Not applicable | None | Pinned Kotlin module is an empty placeholder. C# mirrors the empty class surface, implements no solver contract and has no integration target |
| PostgreSQL | `OSPF_POSTGRES_CONNECTION` | PostgreSQL integration | Not verified: environment unset |
| MongoDB | `OSPF_MONGO_CONNECTION` | MongoDB integration | Not verified: environment unset |
| Redis | `OSPF_REDIS_CONNECTION` | Redis integration | Not verified: environment unset |
| Kafka | `OSPF_KAFKA_BOOTSTRAP_SERVERS` | Kafka integration | Not verified: environment unset |

A missing SDK, license, or service is recorded as not verified. It is never counted as a passing
integration result.

## P0 Evidence

- The missing `Framework.Log` and heartbeat contracts were added with bilingual XML documentation.
- SDK paths are resolved from environment variables. Gurobi, MOSEK, and Hexaly native sources are
  excluded when their managed assemblies are absent.
- The default Release solution builds without `GUROBI_HOME`; the Gurobi contract test returns
  `OREngineEnvironmentLost` instead of failing compilation.
- Release clean build: 0 errors. Existing analyzer/style warnings remain recorded in the local build
  log; the Gurobi optional boundary adds no build errors.
- Full test run: 2109 passed, 1 skipped, 0 failed. The skipped test is the pre-existing Delaunay
  circumcircle precision test.
- Gurobi no-SDK test run: 2 passed. SQLite and source-generator coverage run as part of
  `Framework.Plugin.Tests` (58 passed).
- Raw build and test logs are kept under ignored `logs/` and are not repository inputs.

## P1 Evidence

- Quantities now derives product, quotient, and integer-power domains through the shared domain
  operators. Affine units still reject multiplication, division, powers, and reciprocals; bit and
  length powers retain `Discrete` and `Continuous` domains respectively.
- Math ports the baseline distribution family, entropy injection, channel projections, fractal
  helpers, and signed/unsigned random ranges. Unsigned generation now covers the full `UInt64`
  domain without routing through `Int64`.
- MultiArray returns independent iterator snapshots, supports requested access-order iteration and
  flattening, preserves coordinate-indexing fallbacks for custom or externally modified shapes, and
  avoids coordinate decoding for constant/default factories.
- Utils adds ISO duration serialization, RFC-compatible UUIDv7 GUID byte ordering, Result-aware
  parallel all/any/none predicates, and fatal error preservation and collection in worker pools.
- Base contract tests: Math 375 passed / 1 pre-existing skip; Quantities 162 passed; MultiArray
  43 passed; Utils 62 passed. Total: 642 passed, 1 skipped, 0 failed.
- The new tests add no analyzer warnings. Six pre-existing Math style warnings remain in the
  baseline; they are not counted as clean-build evidence.

## P2 Evidence

### P2 Batch 1: Unified report and stable identities

- Added `SolveReport<V>`, solve fingerprinting, cancellation facts, model-element identity
  registration, derived identity scopes, and solver-output report adapters.
- The old solver-output adapter now preserves iteration, node, best-bound, gap, and solve-time
  statistics when constructing unified reports.
- Contract tests cover conclusion versus termination, incumbent and proof strength, cancellation,
  fingerprints, stable IDs, provenance, fallback scopes, and duplicate-ID rejection.

### P2 Batch 2: Model lifecycle and immutable linear state

- `LinearMetaModel<V>` now exposes an optional stable identity registry, a structure observer, and
  a session-owned structural revision. `QuadraticMetaModel<V>` accepts the same registry.
- Dynamic linear rows require expanded variables and non-strict comparisons, reject nonfinite
  values, normalize coefficients, make identical ID/content registration idempotent, and reject
  conflicting or reserved IDs.
- Snapshots freeze caller input, include variable bounds and normalized rows, detect direct row,
  objective, and bound changes at refresh boundaries, and reject stale or foreign revisions.
- Closing a model is idempotent; `Dispose()` delegates to `Close()`, and mutation entry points reject
  post-close writes with `ApplicationStopped` (void removal/solution entry points do not mutate a
  closed model).
- `LinearModelStateTest` covers dynamic-row detachment and idempotency, nonfinite/strict/unknown
  inputs, revision invalidation, duplicate display names, foreign revisions, freeze-on-snapshot,
  and closed-model mutation.

### P2 Batch 3: Criticality evidence and CP exact primitives

- Added proof-gated `AnalysisStatus` mapping, finite immutable `ObjectiveTarget` values, and a
  criticality profile that classifies only constraints from proven-unreachable targets. Unknown,
  unsupported, and reachable observations remain auditable but never become bottleneck evidence.
- Criticality validation rejects duplicate target identities, blank constraint identities,
  nonfinite targets, invalid relations, and classifications that disagree with the evidence.
  Profile observations and caller-supplied classification sets are frozen at construction.
- Dynamic-row identity registration now rolls back the detached row if the model identity registry
  rejects it. Linear and quadratic registration also preserve the requested name and display name.
- Added exact signed 64-bit `IntegerDomain` intervals and sparse domains, including safe
  cardinality overflow, bounded enumeration, canonical sparse values, and Boolean/singleton
  factories.
- Added immutable `BooleanLiteral` variable/constant forms with stable IDs, negation, and structured
  missing-assignment evaluation.
- New tests cover local/persistent/structural classification, proof gates, invalid evidence,
  immutable profile snapshots, registration rollback, domain boundaries, and Boolean literals.

### P2 Batch 4: CP expressions, constraints, and model snapshot

- Added immutable integer CP expression nodes for constants, variables, linear terms, and structured
  invalid nodes. Linear merging uses exact `BigInteger` accumulation, rejects `Int64` overflow,
  merges by stable source ID, and removes zero terms.
- Added scalar CP constraints for integer comparisons, Boolean conjunction/disjunction/exclusive-or,
  literals, implications, reification, all-different, element, allowed/forbidden tables, circuit,
  automaton, and reservoir. Shapes and integer-only domains are validated at construction time.
- Added stable/model-local identity scopes, reserved-prefix rejection, legacy-origin normalization,
  provenance validation, and AST rebinding from source variables to registered CP IDs.
- Added an independent CP model registry with ordered variable/expression/constraint/objective
  registration, single-objective direction validation, reference validation, repeatable snapshot
  creation, and idempotent close/dispose. Closed models reject reads and writes that require the
  released registries with `ApplicationStopped`.
- CP snapshots freeze all collections and validate duplicate identities across variable, constraint,
  and objective kinds, unregistered references, objective direction, and identity metadata.
- Seventeen CP contract tests cover expression exactness, constraint boundaries, registration order,
  duplicate IDs, one-variable/one-ID binding, unregistered references, snapshot immutability,
  objective rules, identity scopes, and close behavior.

### P2 Batch 5: CP intervals, scheduling constraints, and session SPI

- Added `IntervalVariable`, `IntervalValue`, and `IntervalId` with exact `end = start + size`
  validation, `BigInteger` overflow protection, fixed-duration factories, optional presence, and
  immutable snapshot registration.
- Added half-open `NoOverlap` and event-point `Cumulative` constraints. Absent optional intervals do
  not participate, negative demand/capacity and resource-sum overflow are rejected or handled
  exactly, and the model validates interval references.
- Added the CP solver/session/options SPI and capability declarations without binding Core to a
  vendor SDK.
- Added exact CP interval and scheduling coverage plus the CP solver output contract.
- Release forced build: 0 errors, 749 existing analyzer/style warnings. Full default test run:
  2230 passed across 23 assemblies, 1 pre-existing skipped test, 0 failed. Core: 390 passed.

### P2 Batch 6: Deterministic CP fake solver and reusable session

- Added `FakeConstraintProgrammingSolver`, a deterministic exhaustive solver restricted to small
  models and contract tests. It declares native CP capabilities but is not a production search
  engine.
- The session supports one-shot and reusable `SolveAsync` calls, hard fixed values, assumption
  validation, exact objective direction, interval values, cancellation/time/node/solution limits,
  proven infeasibility, and idempotent close/dispose. Closed sessions return
  `ApplicationStopped`.
- Exact hints and interval hints are boundary-checked against model domains and presence literals;
  malformed hints return structured errors instead of being ignored.
- Core tests: 398 passed, 0 failed. New fake-solver tests: 5 passed. Core.Tests Release build: 0
  errors, no new warnings from the batch.

### P2 Batch 7: Exact finite MIP lowering

- Added `ConstraintProgrammingToLinearModelLowerer` and
  `ConstraintProgrammingLoweringPolicy`, lowering finite CP integer domains, sparse domains, fixed
  values, assumptions, one objective, interval links, comparisons, Boolean logic, implication,
  reification, AllDifferent, constant Element, allowed/forbidden tables, NoOverlap, and explicitly
  opted-in Cumulative constraints.
- CP variable bounds are applied to the lowered variables and checked against the exact IEEE-754
  integer range before model construction. Coefficients, constants, and right-hand sides use the
  same exactness gate; unsupported Circuit, Automaton, Reservoir, complex wrapped logic, and
  over-budget decompositions return structured failures.
- Lowering artifacts retain source-variable, source-interval, auxiliary-variable, and
  compiled-constraint provenance. Stable registrations remain order-independent, and generated
  linear rows carry their originating CP constraint identity.
- Small-domain exhaustive tests compare every source assignment with whether the lowered rows admit
  any auxiliary assignment. Coverage includes sparse domains, logic/reification, AllDifferent,
  Element, allowed/forbidden tables, optional and variable-duration intervals, and NoOverlap.
- Cumulative tests cover zero demand/duration budget bypass, overload, adjacent half-open intervals,
  optional-interval semantics, explicit opt-in, time/work budgets, and rejection when wrapped by
  implication or reification.
- Core tests: 409 passed, 0 failed. Core.Tests Release build: 0 errors. Forced Core Release rebuild:
  0 errors, 21 existing warnings; none points to a Batch 7 source or test file.
- Full Release solution build: 0 errors, 728 existing analyzer/style warnings; none points to a
  Batch 7 source or test file. Full test run: 2249 passed across 23 assemblies, 1 pre-existing
  skipped Delaunay test, 0 failed.

### P2 Batch 8: Portable checkpoint schema 3.0

- Added a canonical CP snapshot codec and a portable schema 3.0 checkpoint envelope containing
  stable identity, model/configuration/solver fingerprints, run and attempt identifiers, incumbent,
  historical bounds, assumptions, conflicts, Benders state, cancellation chain, provenance,
  metadata, and integrity digest.
- Strict decode accepts only `sourceFormat = v2` with `schemaVersion = 3.0`. Compatible decode
  migrates schema 2.0 and legacy v1, refreshes the digest, and rejects malformed documents rather
  than falling back to a legacy parser.
- An absent incumbent is encoded as `null`; it is never replaced by an empty object. Restore
  revalidates incumbent values and intervals against the snapshot, exact objective value, historical
  bounds and gap, assumptions, conflicts, Benders fingerprints, configuration, solver, run, attempt,
  cancellation chain, and provenance.
- String maps are sorted by key before digest calculation and emission. A document whose maps are
  unsorted and whose digest was signed over that unsorted text is rejected; the ascending-key control
  document passes.
- The shared Kotlin fixture round-trips byte-for-byte with all 25 envelope fields, explicit nulls,
  canonical map order, and digest
  `3f2d2f499f1eb6e7e4378d7a04c85c88896973000a2567c0b908fd24daf65637`.
- Batch contract tests: 10 passed, 0 failed. Core tests: 419 passed, 0 failed. Forced
  non-incremental Core Release build: 0 errors, 21 existing analyzer/style warnings; none points to
  a Batch 8 source or test file.
- Full Release solution build: 0 errors, 728 existing analyzer/style warnings. Full test rerun:
  2259 passed across 23 assemblies, 1 pre-existing skipped Delaunay test, 0 failed. The first full
  run exposed an existing concurrent mutation race in the Demo4 static domain registry. The race is
  not caused by this batch and remains classified under P9 rather than being counted as a passing
  result.

### P2 Batch 9: Typed function registration lifecycle

- Replaced the placeholder `object model` registration boundary with explicit linear and quadratic
  mechanism-model interfaces. Linear functions now enter `MetaModel<V>` through
  `LinearFunctionSymbolAdapter<V>`, while quadratic mechanism models dispatch quadratic registrations
  before the linear fallback.
- `MetaModel<V>` now registers auxiliary tokens and rolls back both the symbol and any newly added
  auxiliary variables when registration fails. Duplicate symbol names are rejected before auxiliary
  registration, and batch insertion stops at the first failure.
- Concurrent token tables now retain intermediate symbols, reject duplicate names with
  `SymbolRepetitive`, and no longer report a successful no-op when symbol insertion is requested.
- `LinearFunctionSymbolAdapter<V>` delegates auxiliary registration, constraint registration, and
  evaluation; exposes a stable adapter identifier; reads `IHasResultPolynomial<V>` when available;
  and exposes polynomial dependencies. `LinearExpressionSymbol` and `QuadraticExpressionSymbol` also
  received stable identifiers, fixing dictionary lookup through their previously changing hash code.
- Contract tests cover auxiliary registration, function registration dispatch, quadratic precedence,
  failure propagation, rollback after partially added helpers, duplicate-name rejection, adapter
  polynomial/evaluation behavior, stable identifiers, and the 47 function types in the original fixture through the
  typed lifecycle.
- Fatal auxiliary and constraint registration errors now preserve `Fatal` instead of being treated as
  success or degraded to `Failed`; failed auxiliary registration still removes both the symbol and
  every helper added during the attempt.
- Existing function bodies still return placeholder constraints/evaluation. This batch validates the
  registration contract and lifecycle only; real function semantics remain in P2.
- Batch contract tests: 105 passed, 0 failed. Core tests: 425 passed, 0 failed. Forced
  non-incremental Core Release build: 0 errors, 21 existing analyzer/style warnings; none points to a
  Batch 9 source or test file.
- Full Release solution build: 0 errors, 728 existing analyzer/style warnings. Full test run:
  2265 passed across 23 assemblies, 1 pre-existing skipped Delaunay test, 0 failed.

### P2 Batch 10: ABS and fatal token-registration propagation

- Replaced the placeholder `AbsFunction` with the Kotlin-boundary implementation: finite input
  bounds tighten the result and positive/negative-part domains; per-part Big-M values are inferred
  from those bounds or use the shared `1_000_000` fallback; evaluation returns the mathematical
  absolute value; and registration writes the four exact linear rows (`result = pos + neg`,
  `x - pos + neg = 0`, and the two selector rows).
- Negative, non-finite, or unrepresentable Big-M/input values now return `IllegalArgument` instead
  of producing invalid solver rows. `VariableType` bounds were corrected to the Kotlin baseline:
  `Percentage` and `UContinuous` start at zero, and `Continuous` uses the decimal-precision range.
- The remaining function/token batch paths now use `IsFailed`, so `Fatal` from auxiliary token
  registration and batched adds is preserved instead of being treated as success. As in Kotlin,
  token-table registration returns the fatal result after any already-added helper remains; the
  higher-level `MetaModel` path still performs its own symbol/helper rollback.
- `LinearPolynomialBounds<V>` and the `AbsFunction` constructor are breaking public API changes.
  Repository callers were updated; no compatibility constructor was retained.
- Batch 10 targeted tests: 123 passed, 0 failed. Core tests: 434 passed, 0 failed. Forced
  non-incremental Core Release build: 0 errors, 21 existing analyzer/style warnings, none in a
  Batch 10 source or test file. Full Release solution build: 0 errors, 728 existing analyzer/style
  warnings. Full test run: 2274 passed across 23 assemblies, 1 pre-existing skipped Delaunay test,
  0 failed.
- Real function semantics are still incomplete: ABS is implemented, while the other 46 existing
  function types remain placeholders.

### P2 Batch 11: MAX/MIN extremum functions

- Replaced `MaxFunction`, `MinFunction`, `MinMaxFunction`, and `MaxMinFunction` with the Kotlin
  behavior. `MinMaxFunction` delegates to `MAX`; `MaxMinFunction` delegates to `MIN`.
- Extremum inputs now have non-empty validation, mathematical evaluation, finite result bounds when
  all input bounds are known, and per-candidate Big-M values derived from the shared extremum anchor.
  Missing bounds use one uniform default Big-M, matching Kotlin's fallback.
- Registration writes the exact `2n + 1` linear rows: candidate lower/upper rows, one selector per
  candidate, and `sum(selector) = 1`. Negative, non-finite, or unrepresentable Big-M/input values
  return `IllegalArgument`.
- Constructors are breaking named API changes and now require the input polynomial list; the
  in-repository lifecycle fixtures were updated. No placeholder constructor was retained.
- Batch 11 targeted tests: 111 passed, 0 failed. Core tests: 439 passed, 0 failed. Forced
  non-incremental Core Release build: 0 errors, 21 existing analyzer/style warnings, none in a
  Batch 11 source or test file. Full Release solution build: 0 errors, 728 existing analyzer/style
  warnings. Full test run: 2279 passed across 23 assemblies, 1 pre-existing skipped Delaunay test,
  0 failed.
- Real function semantics are still incomplete: ABS and the four extremum functions are implemented;
  42 of the 47 existing function types remain placeholders.

### P2 Batch 12: Floor, ceiling, and rounding

- Replaced `FloorFunction`, `CeilingFunction`, and `RoundingFunction` placeholders with evaluation
  and exact row generation. FLOOR/CEILING each register three integer rows; ROUND registers seven
  rows using integer quotient, fractional, and half-up indicator variables.
- Inputs and explicit Big-M values are checked for finite, representable values before registration.
  The shared tolerance is `1e-10`.
- ROUND evaluation matches the baseline `Flt64.round()` behavior, including ties-to-even at positive
  and negative midpoints. The Kotlin linearization treats an exact `0.5` fraction as round-up; this
  inherited evaluator/model midpoint difference remains documented rather than silently hidden.
- Batch tests cover evaluation, row names/counts, feasible and infeasible assignments, missing
  values, nonfinite inputs, and nonfinite explicit Big-M.

### P2 Batch 13: Logical and indicator functions

- Replaced `BinaryzationFunction`, `AndFunction`, `OrFunction`, `NotFunction`, and `XorFunction`
  placeholders with evaluation and indicator/linearization constraints.
- Direct binary AND/OR inputs use the one-helper fast path. The AND upper row was corrected to
  `sum(inputs) - result <= n - 1`, so a zero input cannot admit `result = 1`. XOR implements the
  zero band, nonzero complement, single-selector lower rows, and pairwise upper rows.
- Automatic Big-M values are revalidated at registration, so expanded input domains fail with
  `IllegalArgument` instead of registering incomplete rows.

### P2 Batch 14: Modulo, semi-continuous, and step-range functions

- Replaced `ModFunction`, `SemiFunction`, and `InStepRangeFunction` placeholders with their Kotlin
  behavior. MOD evaluates floor-modulo and registers decomposition, remainder-bound, and result
  rows. SEMI exposes `0` or `[lb, ub]`, supports variable-bound inference, and registers both domain
  rows. IN_STEP_RANGE delegates the scaled quotient to FLOOR, exposes the composed result
  polynomial, and registers the lower/upper bound row.
- Invalid divisors, non-positive/nonfinite steps, reversed bounds, and nonfinite bounds are rejected.
  Missing evaluation inputs return `null`; nonfinite registration inputs return structured failures.
- Batch tests cover floor semantics for negative dividends, semi-continuous defaults and inference,
  step-range upper/inside/outside values, exact row names/counts, feasible/infeasible assignments,
  helper exposure, and invalid inputs.
- Constructors are breaking API changes: MOD now requires its input polynomial and divisor, SEMI
  takes optional `lb`/`ub` before the name, and IN_STEP_RANGE requires lower/upper polynomials plus a
  step. Repository fixtures were updated; no placeholder constructors were retained.

### P2 Batch 15: balanced ternaryzation, one-of, and same-as

- Replaced `BalanceTernaryzationFunction`, `OneOfFunction`, and `SameAsFunction` placeholders with the
  Kotlin behavior. BTER maps its input to `{-1, 0, 1}` with an epsilon zero band and a strict-boundary
  gap, registers six rows (two condition-range indicator pairs, the result link, and branch
  exclusivity), and returns `null` for values inside the open transition gaps.
- Added `ConditionSupport`, the C# counterpart of Kotlin `relationIndicatorConstraints`: a finite
  condition range plus the true/false branch gap drives two rows, with no Big-M fallback and no
  Big-M used when the range already fixes the indicator. LT/LE normalize by negating the difference
  polynomial; GT/GE use it directly.
- ONEOF counts nonzero inputs during evaluation and registers four nonzero-indicator rows per input,
  `sum(indicators) = 1`, and `result = 1`. Its defaults now match the Kotlin baseline constants
  (tolerance `1e-10`, strict boundary `16 * (1e-10 + 2^-52)`) rather than the stale `0.5` documented
  in the Kotlin KDoc.
- SAMEAS initially evaluated satisfaction of LE/LT/GE/GT/EQ/NE with epsilon semantics and reported
  whether all flags agreed. At this historical gate, both result encoding and LE/GE/EQ endpoint rows
  were defective; see the later SameAs continuations for the corrections and changed gap contract.
  LT/GT/NE indicator lowering remains unsupported and returns `ApplicationError` without writing
  partial rows, matching Kotlin `simpleIndicatorConstraints`.
- Constructors are breaking API changes: BTER now requires the input polynomial, ONEOF the input
  polynomial list, and SAMEAS the inequality list plus a positive epsilon. Lifecycle fixtures were
  updated; no placeholder constructors were retained.
- Disclosed gap: Kotlin expresses BTER through the deferred `IndicatorStructure`/native-indicator path
  with an attached `DifferenceIndicator`. C# registers the mathematically equivalent six rows directly;
  the deferred structure and native writer remain in the later native-function batches.
- Batch 15 targeted tests: 19 passed, 0 failed, covering three-state evaluation, open-gap rejection,
  strict-boundary row names, feasible/infeasible indicator assignments, exact one-of selection,
  constraint versus measurement linkage, every comparison's tolerance semantics, unsupported
  relations, and invalid configuration.

### P2 Batch 16: satisfied-amount functions

- Adopted the Kotlin constraint-input shape: `LinearConstraintInput<V>` now carries the normalized
  flattened expression (`lhs - rhs`), the comparison sign, the lhs value range, and the pre-normalization
  rhs constant, built through `LinearConstraintInput<V>.From`. The previous C# record was a constraint
  registration bundle with no callers, so the replacement is a breaking change with no repository fallout.
- `SatisfiedAmountFunction` counts satisfied inequalities through one binary flag per inequality, exposes
  `sum(u)` as the result polynomial, writes the Kotlin two-row LE/GE indicator lowering with names
  `{name}_sat_le_ub_{i}` / `{name}_sat_le_lb_{i}` / `{name}_sat_ge_ub_{i}` / `{name}_sat_ge_lb_{i}`, uses the
  four zero-band rows with a per-equality side column for EQ, appends `{name}_amount` as `sum(u) >= amount`,
  and returns `OREngineModelingException` for in-range LT/GT/NE without writing partial rows. Evaluation
  applies the Kotlin epsilon semantics per comparison and reports 0/1 when `amount` is set, otherwise the count.
- `SatisfiedAmountInequalityFunction` plus `AnyFunction` (`[1, n]`), `AllFunction` (`[n, n]`),
  `AtLeastInequalityFunction` (`[k, n]`), `NotAllFunction` (`[1, n-1]`, no range for a single input), and
  `NumerableFunction` (explicit range) mirror the Kotlin aliases, default `epsilon = 1e-6`, `u_{i}`/`y` helper
  names, `{name}_i{i}_upper` / `{name}_i{i}_lower` rows, trivially fixed `{name}_i{i}_flag` rows when zero lies
  outside the lhs range, no rows for unbounded ranges, and the `{name}_amount_lb` / `{name}_amount_ub` pair.
- Historical correction: Batch 16 replaced the Kotlin in-range zero-band rows with sign-specific
  lowering and rejected LT/GT/NE. That was a baseline divergence, not an accepted synchronization
  decision. Batch 17 restores the fixed baseline; the earlier claim that registration and evaluation
  agree is withdrawn. The separate `SatisfiedAmountFunction` lowering described above is unchanged.
- Constructors are breaking API changes: satisfied-amount symbols now require the inequality or constraint-input
  list plus epsilon. Lifecycle fixtures were updated; no placeholder constructors were retained.
- Batch 16 targeted tests: 18 passed, 0 failed, covering count versus 0/1 threshold evaluation, per-sign row names
  and row counts, equality zero-band rows with side columns, feasible/infeasible flag assignments, trivially fixed
  flags, unbounded ranges leaving flags free, the count-range indicator pair, every alias, strict-relation rejection
  without model pollution, and invalid configuration.

### P2 Batch 17: restore the fixed satisfied-amount baseline

- Source checked with `git show 12fdb587:ospf-kotlin-core/src/main/fuookami/ospf/kotlin/core/symbol/function/SatisfiedAmountInequality.kt`,
  rather than following the Kotlin working-tree HEAD.
- `SatisfiedAmountInequalityFunction` now emits `d + M*u <= M + epsilon` and
  `d - M*u >= -M - epsilon` for all six comparisons when the normalized range contains zero,
  where `M = ensurePositiveBigM(max(abs(lb), abs(ub)))`. EQ has no additional side column.
  Helper order is all input flags followed by the optional count-range output.
- Baseline limitation, not an equivalence claim: `u=1` implies `abs(d) <= epsilon` but `u=0`
  does not require a violated comparison. Evaluation still counts by comparison sign. Likewise,
  `y=1` implies count-range membership, but membership does not force `y=1`. An unbounded input
  range leaves its flag free. These behaviors must be considered before using the output as a
  reified predicate; this batch does not silently repair the Kotlin contract.
- Empty counting and empty All inputs, zero-epsilon EQ, all six comparison rows, shifted constants,
  independent residual/flag feasibility vectors, alias implications, and helper ordering are tested.
  Non-finite/unbounded count-range configuration returns `IllegalArgument` before any model row is
  written instead of unwrapping an infinite bound.
- The 47-type count is the original lifecycle fixture (including quadratic types); 25 have Flt64
  behavior and 22 remain placeholders. The added shared inequality-count base is not an extra
  migrated fixture type. Natural-name generic numeric function APIs remain an explicit P2 gap.

### P2 Batch 18: natural-name generic ABS and native constraint cells

- Ported the fixed `Abs.kt` generic entry point as `AbsFunction<V>` with an explicit `IIntoValue<V>`.
  Polynomial coefficients, result polynomial, per-part Big-M values, and evaluation use `V` arithmetic;
  no non-generic compatibility class or duplicate Flt64 algorithm remains. Existing Flt64 callers and
  lifecycle fixtures now instantiate `AbsFunction<Flt64>`.
- `IIntoValue<V>` now inherits the existing `IFlt64ValueConverter<V>`, so the same converter can be
  passed to the function and `LinearMetaModel<V>` without a wrapper or duplicate converter implementation.
- The non-Flt64 tests exposed shared constraint bugs: `default(V)` is not necessarily numeric zero
  (`default(RtnX)` has an invalid denominator), and cells used to round-trip native coefficients through
  Flt64 on construction/read. Linear and quadratic constraints now accumulate from the registered zero,
  negate constants directly, and retain native coefficients in `LinearCellImpl<V>` / `QuadraticCellImpl<V>`.
  GE/GT normalization negates native terms instead of subtracting from `default(V)`.
- Model token tables now pass the model converter through both manual and auto-indexed token creation.
  Previously, cached evaluation of any non-Flt64 model could attempt an invalid cast from Flt64 to V.
  Tests cover both index modes with concurrent configuration enabled and disabled.
- Tests exercise Flt32, Int64, FltX, and RtnX ABS model registration; values above 2^53 and decimal
  fractions survive evaluation without converter calls. Linear/quadratic cells retain large integer,
  decimal, and rational coefficients without conversion; solution-vector, keyed, and cached evaluation
  and relation normalization are checked. Non-finite coefficients and encoding overflow return
  `IllegalArgument` without adding model rows.
- Numeric boundary limitation retained from fixed `BigM.kt`: bound inference and helper variable ranges
  use Flt64; generic evaluation and native cells do not make those boundaries exact. Token cached results
  also retain their existing Flt64 solver-boundary storage. RtnX is the repository's double-backed rational
  implementation, not an arbitrary-precision rational guarantee. Deferred/native ABS structures remain
  part of the pending native-writer work; these tests do not establish real backend integration.

#### Batch 18 API migration

Replace `new AbsFunction(polynomial, ...)` with:

```csharp
var abs = new AbsFunction<Flt64>(
    polynomial: polynomial,
    converter: IIntoValue<Flt64>.Identity,
    name: "absolute_cost"
);
```

For other numeric types, pass an `IIntoValue<V>` to both `AbsFunction<V>` and the model's `converter`
parameter. Existing converter classes with public `IntoValue`, `FromValue`, `Zero`, and `One` members
continue to work; old explicit `IIntoValue<V>` member implementations should become public members or
implement the inherited declaring interfaces. Cell constructors now accept `V coefficient` and no
converter argument; callers convert SDK values at their adapter boundary before constructing a cell.
At the end of Batch 18, the 47-type lifecycle fixture had one natural-name generic API, 24 other
Flt64-specific implementations and 22 placeholders. This was not P2 completion.

### P2 Batch 19: generic extrema and checked Big-M construction

- Compared `Max.kt` and `MinMax.kt` at fixed Kotlin commit `12fdb587`. `MaxFunction<V>`,
  `MinFunction<V>`, `MinMaxFunction<V>`, and `MaxMinFunction<V>` now use the natural generic
  names, accept one `IIntoValue<V>`, and retain V coefficients, evaluation and generated rows.
  The wrappers still delegate to the corresponding extremum implementation, as in Kotlin.
- The input domain and candidate-specific Big-M formulas remain the fixed baseline's formulas;
  Flt64 bounds and the fallback remain unchanged. Non-finite or unencodable values return
  `IllegalArgument`. Unrepresentable numeric negation or a wrapped negative Big-M distance is
  rejected before adding rows rather than silently creating an incorrect model. This is an
  intentional safety boundary beyond the Kotlin baseline.
- Flt32, Int64, FltX and RtnX tests register all four variants, verify extrema and tie selectors,
  and reject multiple selections. Large Int64 and fractional FltX values are evaluated and placed
  in native constraint coefficients without losing precision through a double round trip.
  Regression tests cover non-finite inputs, failed conversion, negation overflow and wrapped
  Big-M distances. Existing Flt64 callers and lifecycle fixtures were migrated.
- `BigMValues.IsUsable(V, converter)` centralizes the existing ABS encoding guard and is reused
  by the extrema; failed/fatal constraint registration is propagated without conversion to success.
- Limitations: result/helper variable ranges and automatic bound inference remain Flt64, as in the
  fixed Kotlin baseline. When bounds are unavailable, the baseline fallback M=1,000,000 is not
  proof of a valid bound; callers must supply an appropriate explicit value. Evaluation uses the
  underlying number type's arithmetic and is not a general checked-overflow guarantee. The result
  variable is still a bounded `RealVar`: large evaluation fixtures do not prove that the same values
  can be represented by a solver model. Existing empty-list and null-element constructor guards
  still throw `ArgumentException`; replacing normal failures with a Result-based construction path
  remains required before P2 is complete. Deferred native extremum structures and real solver
  integration remain unverified.

#### Batch 19 API migration

Replace non-generic constructors with `MaxFunction<V>`, `MinFunction<V>`,
`MinMaxFunction<V>`, or `MaxMinFunction<V>` and pass the same `IIntoValue<V>` used by the model:

```csharp
var maximum = new MaxFunction<Flt64>(
    polynomials: candidates,
    converter: IIntoValue<Flt64>.Identity,
    name: "maximum"
);
```

No non-generic duplicate is retained. The 47-type lifecycle fixture now has five natural-name
numeric generic APIs (ABS and four extrema), 20 additional Flt64 implementations and 22
placeholders. These counts do not establish complete P2 or native solver support.

### P2 Batch 20: generic rounding and representable strict gaps

- Compared `Floor.kt`, `Ceiling.kt`, and `Rounding.kt` at fixed Kotlin commit `12fdb587`.
  `FloorFunction<V>`, `CeilingFunction<V>`, and `RoundingFunction<V>` now retain V in their
  input, evaluation, result polynomial, and generated rows. The shared `InStepRangeFunction`
  dependency and all repository callers have migrated. No non-generic compatibility class remains.
- Evaluation uses V arithmetic rather than converting the input through Flt64; midpoint evaluation
  retains ties-to-even. Tests cover Flt32, Flt64, Int64 above 2^53, large fractional FltX and RtnX,
  adjacent Flt64 midpoint values and signed zero. `RtnX` remains the existing double-backed type.
- FltX and RtnX tests register and inspect the three/three/seven rows, validate positive and
  negative assignments, and preserve fractional FltX coefficients. Existing Flt64 tests exercise
  the same row lifecycle. Conversion failures and non-finite inputs are rejected before row writes.
- The fixed baseline's `1e-10` strict gap cannot be retained by Int64 or by Flt32 subtraction at
  one; registering these generic models returns `IllegalArgument` before writing rows instead of
  silently dropping the gap. Flt64/FltX constants can also absorb the gap when rows are flattened;
  those inputs are rejected if the two flattened bounds collapse. This is a deliberate safety
  difference from Kotlin, not a claim of full numeric-type parity. Evaluation remains available.
- The encoding still excludes a small interval below each strict boundary, and its effective gap
  may change through numeric rounding or a solver's feasibility tolerance. The finite-representation
  check is not proof that a particular solver can enforce `1e-10`. The Kotlin baseline's ROUND
  mismatch is retained and tested: evaluation ties to even, while the model rounds a half up.
  Variable domains and token caching still cross an Flt64 boundary; RtnX is not arbitrary precision.
  Underlying arithmetic overflow and post-conversion solver precision are not generally guaranteed.

#### Batch 20 API migration

Replace the non-generic constructors and pass the model's converter, for example:

```csharp
var floor = new FloorFunction<Flt64>(
    polynomial: polynomial,
    converter: IIntoValue<Flt64>.Identity,
    name: "floor"
);
```

Use the same pattern for `CeilingFunction<V>` and `RoundingFunction<V>`. FLOOR and CEILING retain
the reserved, unused `bigM` argument. A type unable to represent the strict gap can evaluate but
cannot currently register these constraints; use a supported coefficient type rather than relying
on a silently weakened model.

### P2 Batch 21: generic comparison indicators

- Compared `Inequality.kt`, `Conditional.kt`, `IndicatorStructure.kt`, and
  `InequalityFunctionDedicatedTest.kt` at fixed Kotlin commit `12fdb587`. The previous placeholder
  is replaced by `InequalityFunction<V>`: LT/LE/GT/GE register two rows, EQ/NE register four rows
  and a side variable, and evaluation returns one, zero, or null in the transition gap. Input
  coefficients, arithmetic, result polynomial, and rows retain V.
- Truth-table and row feasibility tests cover all six relations, both Big-M endpoints, zero-band
  boundaries, and undefined gaps. Flt64, Flt32, Int64, FltX and RtnX are exercised with
  representable explicit gaps; evaluation also checks values beyond double integer precision.
  Integer addition and multiplication overflow is rejected during comparison evaluation rather
  than returning a wrapped result. Other numeric implementations retain their own arithmetic limits.
- Before adding helper tokens or rows, invalid signs, non-finite values, coefficient-sum overflow,
  invalid bands, and gaps collapsed by the selected numeric type, Big-M or flattened constant are
  rejected. These extra representability checks are conservative safety differences from Kotlin,
  not proof that downstream floating conversion or solver feasibility tolerances preserve the gap.
  Missing input or helper tokens are checked before comparison rows are added.
- At Batch 21, shared linear lowering accepted variable monomials only. A comparison containing an
  intermediate symbol returns `IllegalArgument` during registration, although evaluation can use
  a supplied value for that symbol. General nested-function flattening remains a P2 gap. The shared
  constraint cell factory also silently omits missing tokens in other callers; the comparison's
  precheck did not repair that wider issue. The missing-token issue is addressed in Batch 22;
  comparison-function precheck still rejected nested inputs through Batch 30; Batch 31 adds the
  bounded nested-input support described below.
- Further lifecycle audit found that `ConcurrentTokenTable` constructs a token list over the mutable
  table's dictionary rather than copying it, and its `Dispose` does not dispose that list's refresh
  callbacks. This affects the derived-model isolation and ownership claims; fix and regression-test
  the shared lifecycle before considering those guarantees complete. Batch 22 supersedes this
  finding for the built-in mutable tables and mechanism-model builders.
- These checks do not prove an explicit or fallback Big-M covers the input domain; unbounded inputs
  use the baseline fallback of 1,000,000. At this batch direct row registration was sequential;
  the later constraint-write transaction described below supersedes that behavior for shared writers.
  Arbitrary token batches and external side effects remain outside its guarantee. Deferred/native
  indicators and real solver enforcement were not verified at this gate.

#### Batch 21 API migration

The former `new InequalityFunction()` placeholder must be replaced with the actual comparison and
the model's numeric converter:

```csharp
var comparison = new InequalityFunction<Flt64>(
    lhs: input,
    rhs: Flt64.Zero,
    sign: Comparison.LE,
    converter: IIntoValue<Flt64>.Identity,
    name: "comparison"
);
```

Register the input variables and the `LinearFunctionSymbolAdapter<Flt64>` with the same meta-model.
Use explicit representable `tolerance` and `strictBoundary` values when the default gaps collapse
in the chosen numeric type, and choose a valid Big-M for the actual input domain.

### P2 Batch 22: token ownership and model-term resolution

- Compared `TokenList.kt`, `Relation.kt`, and `ConstraintSign.kt` at fixed Kotlin commit
  `12fdb587`. Built-in mutable token tables now refresh solver-order and solution caches on add,
  remove, and token-result changes. A derived token table copies the variable-to-token mapping but
  shares the token objects and their result values, matching the baseline's shallow snapshot.
  Multiple derived models can be disposed independently; disposal, including failed built-in model
  construction, unregisters their own refresh callbacks. Parent removal or closure does not remove
  tokens from an existing derived snapshot.
- Callback registration, result notification, and removal use coordinated locks; a regression test
  concurrently updates results and creates/disposes snapshots. This is not a blanket thread-safety
  guarantee for all model mutation or the exposed symbol collection. Token objects, variables,
  bounds, and solution values remain shared, not deep-frozen. Direct token changes invalidate the
  token-list solution flag, not expression caches; `SetSolution` flushes only the receiving table.
  Flush any other affected tables explicitly. No performance claim is made for the callback-copying
  implementation.
- Shared linear/quadratic cell creation now returns `IllegalArgument` for nonzero unregistered
  variables or unexpanded symbols, including the second quadratic factor. Nonzero terms are not
  silently omitted or demoted to linear; zero terms are ignored. Both direct factory calls and
  built-in model constraint construction propagate failures; dynamic row failure does not add the
  offending row. The existing comparison-function all-rows token preflight is retained.
- The two built-in linear objective paths and the quadratic objective path, including promoted
  linear objectives, use the same reference validation and copy their nonzero terms before exposing
  the mechanism model. This protects those build paths only: custom mechanism models or later
  externally replaced sub-object collections can bypass them, and existing native writers are not
  independently hardened. Objective evaluation in `SubObject.cs` was still a placeholder in
  this batch and is addressed in Batch 23; these tests are not evidence of coefficient finiteness
  validation or a real solver round trip.
- `ILinearRelation<V>.ConstraintRelation()` and `IQuadraticRelation<V>.ConstraintRelation()` now
  return a `Result` instead of throwing for `NE` or an unknown sign, matching Kotlin's failure
  contract; `Fatal` from a custom relation is preserved by the constraint factories. As in the
  fixed Kotlin baseline, plain `LT` and `GT` relations map to non-strict `<=` and `>=` rows and
  therefore admit equality. Do not use those plain rows as evidence of strict enforcement; use an
  appropriate explicit gap or a supported comparison-indicator construction.
- Remaining P2 work includes recursive intermediate-symbol expansion, genuinely immutable full
  model state, checking whether a closed parent can be built into a new mechanism model (addressed
  for already-closed built-in parents in Batch 23), arbitrary multi-row registration rollback,
  complete function/conditional and quadratic semantics, and native function structures. Real
  solvers and Linux have not been verified in this batch.

#### Batch 22 API migration

Register every nonzero variable before building a mechanism model or adding a dynamic row. Expand
intermediate symbols first; the current shared path returns an error instead of dropping them.
For the relation API, inspect the `Result` from `relation.ConstraintRelation()` before using its
value, rather than treating the method as a direct `ConstraintRelation` return. A
`ConcurrentTokenTable` created with an explicit token-list argument owns and disposes that list;
do not pass a list that must remain independently usable after the table is disposed.

### P2 Batch 23: objective evaluation and closed-parent boundary

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `SubObject.kt`, `MechanismModelObjectiveSupport.kt` | `LinearSubObject<V>`, `QuadraticSubObject<V>`, built-in mechanism-model factories | `ObjectiveEvaluationTest` | Batch 23 verified resolved variable terms, constants, token results, short and reordered solution vectors, promoted linear terms, and representative Flt64/FltX/RtnX arithmetic; Batch 30 adds partial linear nested lowering, while unresolved helpers and quadratic nesting remain unsupported |
| `MetaModel.kt` | `MetaSubObject<V>.Evaluate` | `ObjectiveEvaluationTest` | Variables and constants evaluated; unknown or short results return null, or zero for the whole objective when requested; intentional correctness differences from the fixed Kotlin implementation are described below |
| token and cell evaluation | `Token<V>`, `LinearCellImpl<V>`, `QuadraticCellImpl<V>` | `ObjectiveEvaluationTest` | Missing results, bounds and solution entries remain unknown; checked through constraints and a function adapter |
| `model/mechanism`, `callback/CallBackModel.kt` | `LinearModelState<V>`, `CallBackModel<V>.AddObjective` | `ObjectiveEvaluationTest` | Dynamic-row RHS and opposite-direction objective negation use the numeric type's negation; a representative rational case is verified, not complete callback-model parity |
| model lifecycle | `LinearMechanismModel<V>.InvokeAsync`, `QuadraticMechanismModel<V>.InvokeAsync` | `ObjectiveEvaluationTest`, `TokenSnapshotLifecycleTest` | An already-closed built-in parent rejects a new build; pre-existing snapshots remain usable |

- The public sub-object factories now resolve and validate nonzero terms once, preserve the
  `Result` failure, retain read-only term/cell lists, and evaluate those cells plus the constant.
  A cell keeps its token-table reference and shares token results with derived models; the snapshot
  is not a deep freeze. Custom model and writer implementations can still bypass validation.
- `MetaSubObject<V>` now includes its constant even when it contains variables. At fixed Kotlin
  commit `12fdb587`, its implementation instead omits the constant in this case despite documenting
  it. A too-short vector also returns unknown instead of throwing. These are deliberate correctness
  and safety differences, not claims of exact behavioral parity. Its `zeroIfNone` returns zero for
  the *entire* unknown objective, not just the missing term. Like the Kotlin baseline, it still
  requires resolution of zero-coefficient monomials, whereas mechanism sub-objects omit them.
- Token results are still stored as Flt64: cached generic results can round at `SetResult`; the
  FltX precision test covers a supplied solution vector, not lossless token storage or a solver
  round trip. RtnX remains the repository's double-backed rational type. No overflow/finiteness
  validation or performance improvement is claimed by the evaluation tests.
- Dynamic-row RHS, state objective sign, and callback objective direction no longer subtract from
  `default(V)`, which is not a valid generic numeric zero for RtnX. The regression exercises finite
  rational values; it does not prove generic arithmetic is overflow-checked or complete the callback
  model's independent placeholder paths.
- Existing derived models remain independent of parent closure for their token mapping. The
  closed-parent check is an entry check only; concurrent `Close` and construction, mutation of
  symbol collections, and use of a disposed model are not guaranteed thread-safe.
- At the end of this batch, flattened linear `AddObject` discarded its supplied category and name;
  Batch 24 fixes that metadata. Some native writers still aggregate terms without honoring
  per-sub-object categories. Do not treat tests of sub-object values as verification of solver
  results. At the time, nested-symbol expansion and Kotlin's pending-helper objective terms were
  unsupported; Batch 30 subsequently adds the limited linear expansion described below.

#### Batch 23 API migration

`LinearSubObject<V>.Create` and `QuadraticSubObject<V>.Create` now require the token table and
numeric converter and return a `Result`; inspect it before using the objective. The supplied table
must remain valid for vector evaluation. `SubObject<V>.Cells` now exposes `ICell<V>` instances,
not monomials; use `LinearTerms()` or `QuadraticTerms()` when emitting solver coefficients.
Applications that relied on missing token results being implicitly zero must now handle null.

### P2 Batch 24: objective metadata and combined evaluation

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `MetaModel.kt`, `MechanismModelObjectiveSupport.kt`, `SubObject.kt` | `LinearMetaModel<V>.AddObject`, built-in mechanism factories, `SubObject<V>` | `ObjectiveEvaluationTest` | Linear flattened and polynomial registrations retain category, name, display name, and registration order; quadratic and promoted linear objectives retain display names |
| `LinearTriadDumpBuilders.kt`, `QuadraticTetradDumpBuilders.kt` | `MechanismModelObjectiveExtensions.GetObjectiveValue` | `ObjectiveEvaluationTest` | Cached linear/quadratic values use the sole part's direction or the parent direction for multiple parts; unknown values remain null; matrix writers are not fixed by this batch |
| `LinearModelState.kt` | `LinearModelState<V>` | `ObjectiveEvaluationTest`, `LinearModelStateTest` | Flattened objective direction participates in the minimizing snapshot; objective changes invalidate old revisions |
| `Object.kt` | `SingleObject` | `ObjectiveEvaluationTest` | Its part-list is copied and read-only; part references and token results remain shared |

- For a single sub-objective `GetObjectiveValue` returns its value in that sub-objective's own
  direction, even if the enclosing `SingleObject.Category` differs. For multiple parts, it adds
  matching directions and subtracts opposite ones relative to the enclosing category. An empty
  objective returns the registered numeric zero, or null if no constants are registered. A missing
  token result or an incompatible custom part also returns null. This uses cached token values,
  which remain Flt64-backed; it is not a lossless generic solution-vector evaluator.
- Both linear registration forms are retained and processed in registration order. At the fixed
  Kotlin baseline, `buildLinearObjectiveSubObjects` selects flattened objectives when any exist
  and consequently omits separately registered polynomial objectives. Keeping both avoids silently
  dropping registered terms and is an intentional behavioral difference. `MetaSubObjects` now
  includes flattened linear registrations as well as polynomial ones. Display names are also kept
  by the built-in C# mechanism sub-objects.
- This batch did **not** fix the solver-bound triad/tetrad conversion: existing Gurobi, MOSEK and Hexaly
  writers can still ignore sub-object direction, and their current `DumpAsync` signature cannot
  represent a validation failure. Hexaly's quadratic mechanism dump remains unimplemented. Until
  the shared export and its Result-based caller migration are complete, do not infer a correct
  solver objective from `GetObjectiveValue` or the passing Core tests. At the Kotlin baseline,
  `QuadraticTetradDumpBuilders.kt` also adds rather than subtracts an unfixed quadratic coefficient
  in its opposite-direction branch; that apparent defect must not be silently copied when the
  matrix export is migrated. Batch 25 fixed the linear triad path, and Batch 26 fixes this sign in
  the basic quadratic conversion without copying the baseline defect.
- The tests cover empty, single and multiple objectives, both enclosing directions, repeated
  variable contributions, constants, missing results, mixed registration, quadratic terms,
  rational zero, retained metadata, read-only part lists, and stale linear-state revisions. They
  do not establish nonfinite/overflow checks, deep model immutability, concurrent mutation safety,
  native writer correctness, or real-solver behavior.

#### Batch 24 API migration

Consumers enumerating `LinearMetaModel<V>.MetaSubObjects` now also see flattened linear objectives,
in registration order; remove any separate counting or accumulation of those objectives.
`SubObject<V>.DisplayName` retains the supplied display name and `SingleObject.SubObjects` is a
read-only copy of the input list. `GetObjectiveValue` no longer always returns null; handle its
documented effective-direction and unknown-result behavior. No solver dump signature changes in
this batch.

### P2 Batch 25: linear mechanism-to-matrix conversion

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `LinearTriadDumpBuilders.kt`, `DumpHelpers.kt` | `LinearMechanismModelDumpExtensions.ToLinearTriadModel` | `LinearMechanismModelDumpTest` | Linear variable/row order, direction, constants, repeated coefficients, initial values and defensive failures verified without an SDK; this is not complete Kotlin export parity |
| `model/mechanism/MechanismModel.kt` | `LinearMechanismModel<V>.InvokeAsync`, `QuadraticMechanismModel<V>.InvokeAsync` | `LinearMechanismModelDumpTest` | At Batch 25, nonempty fixed-variable requests failed; Batch 37 replaces that restriction with symbol preparation |
| `LinearTriadDumpBuilders.kt` and solver adapters | `IAbstractLinearSolver.DumpAsync`, linear plugin and combinatorial entries | `GurobiSolverContractTest`, `LinearDumpForwardingTest` | Result-based conversion wired; Gurobi SDK and no-SDK entry contracts verified; native Hexaly source changed but not compiled in this environment |
| Linear column-generation and Benders call sites | Existing linear solver call sites | `GurobiSolverContractTest` | Gurobi MILP, solution-pool, LP, master and subproblem paths propagate an invalid-bound failure before the native solver is invoked; other backends' end-to-end paths are not verified |

- A single sub-objective retains its own direction. Multiple parts add matching directions and
  subtract opposite ones relative to the model direction, including constants. Duplicate objective
  and row coefficients are accumulated in source order, with one matrix entry per column. Existing
  rows are retained in order, rather than extracting bound rows; this preserves the existing
  row-index mappings used by column generation and Benders. Token results become optional initial
  values in the matrix view, not proof that a native warm start works.
- The converter returns `IllegalArgument` for unresolved tokens, lazy constraints, unsupported objective parts or
  relations, nonfinite coefficients/constants/RHS/initial values, invalid or missing variable
  bounds, and nonfinite intermediate sums. Cancellation returns `ApplicationStopped`. Finite open
  bounds, including integer bounds, are rejected rather than silently closed; an explicitly
  unbounded variable must supply infinity bounds because a missing bound can also mean an empty
  range. Concurrent model mutation and disposal during conversion are not guaranteed safe.
- The fixed Kotlin `clampCoefficient` clamps even finite large coefficients, and substitutes a
  finite value for infinity. This C# converter deliberately preserves finite coefficients and
  rejects nonfinite ones, avoiding silent alteration of the mathematical model. Aggregating
  duplicate floating-point terms can nevertheless change rounding, and a sum that overflows is
  rejected even if later terms might cancel. Backend-specific coefficient limits and numerical
  tolerances still require adapter validation.
- The triad view still lacks complete constraint origin, priority, stable identity/provenance and
  lazy-constraint metadata; lazy rows now fail explicitly instead of being treated as ordinary rows.
  At the end of this batch fixed-variable substitution was not implemented: both mechanism factories
  rejected nonempty `fixedVariables` rather than ignoring them, while an empty map remained accepted.
  Batch 27 adds matrix substitution but does not change those factories. Native-function writers, bound-row
  extraction and full immutable snapshots remain open. At the end of Batch 25, the quadratic
  tetrad path still had its old signature and writer behavior; Batch 26 updates the conversion and
  signature while leaving the remaining writer gaps open.

#### Batch 25 API migration

`IAbstractLinearSolver.DumpAsync(LinearMechanismModel<Flt64>, CancellationToken)` now returns
`Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>>` instead of a model directly. Match
`Ok`, `Failed` and `Fatal` before accessing the model, dispose a successfully returned matrix
model, and propagate failures and cancellation. A custom solver must update its implementation;
pure conversion is also available via `ToLinearTriadModel`. Successful conversion alone does not
mean that a solver SDK, license, native writer, warm start or backend is available.
At Batch 25, both mechanism-model factories rejected nonempty `fixedVariables`; Batch 37 now accepts
them for preparation, not matrix substitution. Linear dumps still reject lazy rows until their
metadata can be preserved and honored.

### P2 Batch 26: quadratic mechanism-to-matrix conversion

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `QuadraticTetradDumpBuilders.kt`, `DumpHelpers.kt` | `QuadraticMechanismModelDumpExtensions.ToQuadraticTetradModel` | `QuadraticMechanismModelDumpTest` | SDK-independent basic conversion verified, including mixed directions, promoted linear terms, commutative cross terms, row order, and failures; not complete export parity |
| Solver dump entries | `IAbstractQuadraticSolver.DumpAsync`, quadratic plugin and combinatorial entries | `GurobiSolverContractTest`, `QuadraticDumpForwardingTest`, COPT/CPLEX/Gurobi11/MindOpt/SCIP quadratic tests | Result-based pure conversion and forwarding verified; native Hexaly source changed but not compiled here |
| Shared bounds handling | `MechanismModelDumpVariables.Build` | Linear and quadratic mechanism dump tests | Both converters share the existing bounds and initial-value validation; linear behavior remains covered |

- One sub-objective retains its own direction; multiple sub-objectives are added or subtracted
  relative to the model direction, including quadratic coefficients and constants. Cross terms
  `x*y` and `y*x` are merged under the same ordered column pair; linear terms use `Col2 = -1`.
  Duplicate terms are accumulated in source order, zero sums removed, and rows and variables keep
  their original order. The fixed Kotlin source's opposite-direction quadratic sign defect is not
  copied. Floating-point aggregation can change rounding; intermediate overflow is rejected.
- Unresolved variables, nonfinite coefficients/constants/RHS/initial values, overflow, invalid
  relations, finite open or missing bounds, and lazy rows return a failure. Cancellation returns
  `ApplicationStopped`. Finite large coefficients are preserved, not clamped as in the Kotlin
  baseline. As in Batch 25, explicit infinity is needed for an unbounded side and concurrent model
  modification or disposal during conversion is not guaranteed safe.
- This is pure matrix conversion, not solver availability. Real solver limits and numerical
  tolerances, warm starts, model-solving behavior, and Hexaly's SDK branch are unverified. Without
  the Hexaly SDK, its existing placeholder exposes only name and configuration and does not
  implement `IQuadraticSolver`. At the end of this batch fixed-variable substitution was still a gap;
  Batch 27 adds it to the matrix converters only. Bound-row extraction, row origin/priority/identity/provenance, lazy rows,
  deep freezing and native function writers remain gaps. `QuadraticTetradModel.ExportLP` is still
  an existing stub and must not be treated as a working export.

#### Batch 26 API migration

`IAbstractQuadraticSolver.DumpAsync(QuadraticMechanismModel<Flt64>, CancellationToken)` now returns
`Task<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>>` rather than the model directly.
Custom solver implementations must update the signature; callers must handle `Ok`, `Failed` and
`Fatal` and dispose a successfully returned model. `ToQuadraticTetradModel` provides the same
SDK-independent conversion. Existing stub backends now expose this conversion, but their solve
operations remain unimplemented or unverified.

### P2 Batch 27: fixed-variable matrix elimination

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `LinearTriadDumpBuilders.kt`, `QuadraticTetradDumpBuilders.kt` | `ToLinearTriadModel`, `ToQuadraticTetradModel` | `FixedVariableDumpTest` | Direct matrix elimination verified without an SDK; factory preparation arrived separately in Batch 37 |
| `LinearTriadDumpBuilders.kt`, `QuadraticTetradDumpBuilders.kt`, `ModelView.kt` | `MechanismModelDumpVariables.Build`, `ModelViewVariable.Origin` | `FixedVariableDumpTest` | Remaining columns retain their source variables; input validation and manual reconstruction were covered at Batch 27; the later fixed-column continuation adds checked restoration |

- Fixed linear terms move to the constraint RHS or objective constant. Quadratic terms reduce to a
  linear term if one factor is fixed, or to a constant if both are fixed; objective directions are
  handled as in Batches 25 and 26. Original row ordering, including constant and infeasible rows,
  is preserved. No source token result is changed by conversion.
- Invalid, unregistered, duplicate-key, nonfinite, out-of-domain or fractional integer values and
  overflow in an intermediate result return `IllegalArgument`; cancellation returns
  `ApplicationStopped`. Built-in Int64/UInt64 scalar and combination variables use their original
  typed bounds for fixed-value validation instead of rounded Flt64 bounds. Binary and ternary type
  domains remain enforced even if the mutable range is widened. For custom integer variables whose
  bounds expose only an Flt64 view, magnitudes at or above `2^53` are conservatively rejected.
  Fixed values and all matrix arithmetic are still Flt64: a value rounded before it reaches this
  API cannot be recovered, and intermediate overflow or floating-point rounding can occur.
- Each exported variable's nullable `Origin` points to the source variable and `Copy()` preserves
  that reference. The reference is not a deep snapshot or a stable serialized identity. At Batch 27
  callers had to retain the fixed-value map and combine it with each free column's solved value; the
  later fixed-column continuation supplies checked restoration for the original dumped matrix.
  Passing the shorter matrix solution directly to `SetSolution(IReadOnlyList<...>)` can still
  misalign tokens. Do not reconstruct a solution without an incumbent; manually built views may have
  no `Origin` and cannot use the new mapping.
- At Batch 27, both mechanism-model factories still rejected nonempty `fixedVariables`; Batch 37
  adds factory-time symbol preparation. Solver `DumpAsync` and solve interfaces still do not
  accept this mapping; use the direct matrix conversion explicitly. Nested function preprocessing,
  automatic solver-side restoration, lazy rows, provenance and full immutability remain gaps. The
  later fixed-column continuation verifies selected real-backend cases, not complete backend behavior.
  Both matrix `ExportLP` implementations remain existing stubs.

#### Batch 27 API migration

The matrix conversion extensions now take `fixedVariables` before `cancellationToken`. Existing
positional cancellation calls such as `ToLinearTriadModel(token)` must become
`ToLinearTriadModel(cancellationToken: token)` (and likewise for the quadratic converter). To use
fixed variables, pass a map of registered `IVariableItem` to finite `Flt64` values, check the Result,
and dispose the successful matrix model. Since Batch 37, `InvokeAsync` accepts the same map for
symbol preparation, which is independent of matrix elimination. The direct converter does not perform automatic solution
restoration; use the explicit mapping described in the later fixed-column continuation.
`ModelViewVariable` has an optional final `origin` constructor argument; existing source
calls may omit it, but precompiled consumers must be rebuilt for the changed constructor signature.

### P2 Batch 28: Flt64 symbol registration and preparation

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `TokenTableRegistrationSupport.kt`, `DependencyLayers.kt` | `TokenTableRegistrationExtensions.Register` | `TokenTableRegistrationTest`, `FunctionSymbolRegistrationTest` | Flt64 registration, stable dependency layers, preparation and bounded rollback verified independently of mechanism factories |
| `IntermediateSymbolExpressionSupport.kt` | `QuadraticExpressionSymbol` | `TokenTableRegistrationTest` | Separate linear terms are now included in dependencies, preparation, evaluation and polynomial snapshots; nested model flattening remains unsupported |

- Registration now fast-paths only a genuinely zero, non-function linear polynomial; functions still
  register their helpers. Other symbols invoke `Prepare` in dependency order and cache only a value
  actually returned. Results become visible to later layers only after the entire current layer is
  prepared. Every intermediate dependency must be in the submitted collection, and submitted symbols
  must not already be registered in the table; extending an existing graph this way is unsupported.
  An unresolved external dependency, a cycle, a repeated input symbol or a
  non-Flt64 symbol fails explicitly. Unlike the fixed Kotlin baseline, an unresolved graph does not
  return success. Progress is emitted initially and after each completed layer; callback failures,
  including Fatal, are propagated. A 512-symbol reversed chain and partial fixed values are covered.
- Prepared dependencies are supplied through a read-only overlay without mutating the caller's map;
  the source map's lookup comparer is preserved. An explicitly fixed intermediate value that
  conflicts exactly with a computable value is rejected. An unknown prepared value remains unknown,
  even if an explicit intermediate value is available to a dependent. This is not a complete
  validation of fixed-value finiteness, variable domains or model feasibility. The overlay is not the
  same dictionary instance as the caller's map, so custom `Prepare` implementations must not rely
  on dictionary reference identity. Callers must not mutate the map during registration.
- With the built-in token tables and no concurrent or callback-driven structural modification,
  Result-based failure restores the
  registered symbols' prior null-solution value, polynomial and range caches and removes newly added
  symbols and helper tokens; existing tokens and other cache keys are retained. This is not a general
  transaction: callback notifications, custom symbol side effects, changes to existing token results
  or unrelated caches, exceptions, and auto-index numbers already consumed are not reliably rolled back.
  Custom token tables must implement removal correctly. Concurrent or callback-driven mutation is not
  supported. The copied polynomial cache is a shallow list snapshot, not recursive flattening or a
  deep immutable model. The later token-registration transaction below supersedes this batch's
  key-only rollback and custom-table removal contract; its isolation and shallow-copy limits apply.
- As in the baseline, prepared values are placed under the null-solution cache key, including when
  fixed values were supplied. Changing fixed values, token results or a symbol's polynomial requires
  an explicit `Flush` before relying on the cache again. At the end of Batch 28, partial fixed values
  could fall back to old token results, and adapter dependencies did not include semantic inputs;
  these preparation issues are addressed in Batch 29 below. Automatic cache invalidation remains incomplete.
- At Batch 28 the factories did not call this routine or accept nonempty `fixedVariables`; Batch 37
  adds generic preparation and registration progress. Solver-side solution reconstruction and
  automatic integration with matrix elimination remain unimplemented.

#### Batch 28 API migration

`Register` now accepts `RegistrationStatusCallBack` instead of `Action<RegistrationStatus>`; callback
lambdas must return a `Try`, and failure aborts registration. External implementations of
`IAbstractTokenTable<V>` must implement `ClearCachedValue(cacheKey, solution)` to remove only that
solution-keyed value and report whether it existed; fixed-value and other solution entries must stay
untouched. Rebuild external consumers. Untyped or non-Flt64 symbols and unresolved dependency graphs
that previously appeared to succeed now return `IllegalArgument`.

### P2 Batch 29: function input dependencies and preparation

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `FunctionSymbol.kt`, `DependencyLayers.kt`, `TokenTableRegistrationSupport.kt` | `IMathFunctionSymbol<V>.InputSymbols`, `LinearFunctionSymbolAdapter<V>`, `TokenTableRegistrationExtensions.Register` | `FunctionInputContractTest`, `TokenTableRegistrationTest` | Implemented functions declare their semantic inputs; Flt64 layered preparation and bounded rollback verified independently of mechanism factories |
| `Mod.kt` | `ModFunction.ResultPolynomial` | `TokenTableRegistrationTest.ModAdapter_ShouldExposeItsResultVariable` | Adapter now exposes and reads the MOD result variable, rather than a zero polynomial |

- An adapter's dependencies are the union of semantic input and structural result-polynomial
  intermediate symbols. All 27 concrete function types with implemented result polynomials expose their actual
  possible evaluation inputs, including both inequality sides and normalized constraint inputs.
  A reversed expression -> ABS -> FLOOR -> MAX chain, missing dependencies, cycles and generic
  metadata/evaluation are covered. These tests exercise preparation, not nested model flattening or
  an end-to-end solver. The earlier 47-type fixture omitted the concrete
  `SatisfiedAmountInequalityFunction` base class; it is now included, bringing the fixture to 48.
- For Flt64 `Register`, a null or empty fixed map uses a snapshot of the token results available before
  auxiliary registration, together with earlier prepared layers. An unrelated unknown token does not
  block preparation. A nonempty fixed map is authoritative: missing values do not silently fall back
  to old token results, and the fixed map's lookup comparer is preserved. Prepared values are still
  stored under the null-solution key and require explicit cache flushing after changes.
- Direct adapter `Prepare` uses available token values when its map is null or empty; a nonempty map
  is authoritative. Direct linear and quadratic expression `Prepare` likewise does not fall back to
  the token table for a missing entry in a nonempty map. Other `Evaluate` overloads are unchanged.
  Unlike the fixed Kotlin baseline, C# does not require every token, including unrelated auxiliary
  tokens, to have a result before semantic preparation; Kotlin's adapter also declares no dependencies.
  This is an intentional correctness difference, not a claim of identical evaluation timing.
- `Register` rejects an adapter with unknown input metadata before mutating the table. During adapter
  preparation, its input view only exposes declared inputs; an observed undeclared lookup fails and
  triggers the same bounded rollback as Batch 28. An unavailable declared input read by indexer leaves
  the value unknown. This cannot prove that unexecuted branches or custom functions reading external
  state declared all their inputs; callers must provide complete, stable metadata and avoid side effects.
  Non-adapter custom intermediate symbols still own their dependency and preparation contracts.
- At Batch 29 the factories did not invoke this registration routine and rejected nonempty
  `fixedVariables`; Batch 37 adds preparation. Full nested flattening, structural solver reads of nested symbols, automatic
  solution recovery, generic registration and cache invalidation remain open; this batch does not
  change the status of placeholder functions or claim real-backend verification.

#### Batch 29 API migration

Custom `IMathFunctionSymbol<V>` implementations used through `LinearFunctionSymbolAdapter<V>` in
`Register` must expose every symbol their `Evaluate` can read through `InputSymbols`. The default
`null` means unknown, not an empty dependency set, and is rejected during layered registration;
use an empty collection only for functions with no inputs. `InputSymbols` and input polynomials must
not change during registration. The preparation dictionary is a filtered view, not the original map;
missing values must remain unknown and functions must not depend on its object identity. Custom code
that relied on partial fixed maps falling back to an old token solution must now supply all intended
fixed values explicitly. Consumers of the MOD adapter now see its result variable in the structural
polynomial instead of a constant zero and should rebuild and revalidate their model extraction.

### P2 Batch 30: partial nested linear lowering

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `MathInequalityFlatten.kt`, `MetaModel.kt` | `LinearInequalityConstraint<V>.FlattenData`, linear and quadratic mechanism `AddConstraint`, `LinearSubObject<V>.Create` | `LinearSymbolFlattenTest`, `ConstraintResolutionTest`, `ObjectiveEvaluationTest` | Linear expressions and registered function-result polynomials lower into variables for linear mechanisms; nested quadratic expressions and some other linear entry points remain gaps |
| `MathInequalityFlatten.kt`, `LinearConstraintInput.kt` | `LinearConstraintInput<V>.From` | `LinearSymbolFlattenTest` | Nested plain expressions lower; function symbols are deliberately rejected because the flattened input cannot retain their registration lifecycle |

- Linear mechanism construction expands both inequality sides and each objective recursively using
  an explicit stack, preserving depth-first term order and each symbol's constant accumulation order.
  Constraints then merge variables by `VariableItemKey`; objectives retain term order and duplicates
  until matrix conversion. Coefficients and constants stay in V. A 2,048-level chain, repeated
  branches, both sides, constants, objective evaluation, matrix extraction and revision changes are
  tested. Direct linear constraints added to an already-built quadratic mechanism use the same path.
- Function adapters expand their **structural result polynomial**, not their prepared semantic value.
  The built-in mechanism factory requires referenced function symbols to be registered in that
  model and then registers their rows. A function adapter without `IHasResultPolynomial<V>`, an
  unknown nonzero symbol, a nonzero cycle, or an unresolved nonzero variable fails; zero terms are
  ignored as before. An ABS over a nested expression is checked against its in-memory rows and
  matrix, not against a real solver. Custom intermediate symbols must honor their own polynomial and
  registration contracts. Directly constructing a `LinearConstraintInput<V>` can bypass `From`'s
  lifecycle check, and standalone `FlattenData()` has no token table with which to check registration.
- Unlike Kotlin's recursive baseline, the expansion detects cycles and does not use the call stack.
  Integer multiplication, addition, subtraction and negation wrap, nonfinite intermediate results,
  arithmetic exceptions and nonzero products underflowing to zero are rejected in the new arithmetic
  path. Tests cover Int64, Flt64, FltX and the existing double-backed RtnX; arbitrary custom numeric
  implementations and every representability limit are not proven. Plain objective terms are still
  checked for nonfinite values by matrix conversion rather than by the sub-object factory, and
  duplicate objective sums and general evaluation are not comprehensively overflow-checked.
  Floating operations retain their source traversal order but bitwise cross-language equivalence,
  including signed-zero behavior, is not claimed.
- The expanded objective and constraint cells are snapshots at mechanism construction, unlike the
  Kotlin flattened-objective registration timing. Later symbol edits do not mutate an existing
  mechanism; a later state refresh sees the new structure. Concurrent mutation is unsupported, and
  shared subgraphs are expanded per occurrence without memoization or an output-size budget, so a
  large branching expression can consume substantial time and memory. Token results are still shared.
- At Batch 30, `InequalityFunction<V>` still rejects intermediate inputs in its own numeric/row
  precheck; Batch 31 addresses that path. `LinearModelState<V>.RegisterRow` accepted variable-only
  input at this point; Batch 33 adds plain nested expressions. Quadratic meta-model linear inputs,
  objectives and genuine quadratic terms were not recursively lowered until Batch 32. Fixed-variable
  preprocessing, atomicity for arbitrary external writers, and real solver integration remain P2 work.

#### Batch 30 API migration

Register each referenced function adapter on the target meta-model before building a mechanism;
registering only its helper variable no longer permits a nested reference in the built-in linear
model paths. Check the returned `Result` for unknown functions, cycles or unrepresentable arithmetic.
`LinearConstraintInput<V>.From` accepts nested plain expressions but rejects function references
until their lifecycle can be carried and checked through the input. Callers using detached flatten
data or custom token tables remain responsible for registering function constraints and rebuilding
after model changes.

### P2 Batch 31: nested comparison-indicator inputs

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `Inequality.kt`, `IndicatorStructure.kt`, `MathInequalityFlatten.kt` | `InequalityFunction<V>.RegisterAuxiliaryTokens`, `RegisterConstraints` | `InequalityNestedInputTest`, `InequalityFunctionTest` | Nested linear expressions and registered structural function results work in built-in linear and quadratic mechanisms; native indicators and real backends remain unverified |

- Comparison-row preflight now expands the structural input with the shared linear lowering before
  deriving Big-M, checking flattened constants, gaps and merged coefficients, or writing anything.
  It runs again at row registration, so edits to nested expressions are rechecked. Nonzero cycles,
  unknown function results, multiplication underflow and integer overflow are rejected. Already
  constructed mechanism rows stay snapshots; a new build sees valid changes and rejects invalid
  ones. Built-in factory failures dispose their derived token table.
- Auxiliary-token registration has no model snapshot and permits a nested function to be registered
  later. Row registration requires each nested function adapter to be present in the target token
  table and prechecks all expanded variables and comparison helpers. The built-in factory registers
  every function's own rows before returning a model. Externally mutating the symbol collection or
  custom implementations can defeat this lifecycle assumption. The later constraint-write
  transaction covers participating row writers, not arbitrary token batches. Concurrent model
  mutation and custom function side effects are unsupported.
- ABS through a nested expression is tested for all six comparisons and transition bands against
  in-memory rows and a linear matrix, including registration of the outer function before ABS.
  A quadratic mechanism, Int64/Flt32/FltX/RtnX structural results, missing input/result tokens,
  cycle and overflow failures, changed constants and snapshot retention are also covered. These
  checks do not establish real solver enforcement, all numerical implementations, or bitwise
  agreement after floating-point expansion.
- Unlike the fixed Kotlin baseline, which derives default Big-M from the original input at
  construction, C# recomputes it during preflight from the expanded variable polynomial. It may
  infer a different finite bound instead of the old unbounded-symbol fallback. A zero term is
  discarded by expansion. Bound inference uses Flt64 conversion without a proof of outward rounding;
  an inferred or explicit Big-M and the 1,000,000 fallback are not guaranteed sufficient. Variable
  bounds and solver feasibility tolerances remain caller responsibilities. Evaluation still
  consumes supplied semantic values for the original input symbols; it does not evaluate nested
  functions from their helper variables.

#### Batch 31 API migration

Existing comparison constructors are unchanged. Register each nonzero referenced function adapter
on the target model, including its input variables; adding only its result variable is insufficient.
Check both auxiliary and row-registration results, and rebuild after modifying a nested expression.
Code that depended on rejecting nested comparison inputs or on the old fallback Big-M should review
its model and chosen bound. The other Batch 30 nested/quadratic limitations are unchanged.

### P2 Batch 32: quadratic nested expression lowering

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `MathInequalityFlatten.kt`, `MechanismModel.kt`, `MetaModel.kt`, `MechanismModelObjectiveSupport.kt` | `QuadraticMechanismModel<V>`, `QuadraticConstraintImpl<V>.Create`, `QuadraticSubObject<V>.Create` | `QuadraticSymbolFlattenTest`, `QuadraticMechanismModelDumpTest` | Nested structural lowering, row and objective snapshots, and matrix conversion verified in memory; real quadratic solver enforcement remains unverified |

- Nonzero linear and quadratic intermediate factors are expanded using explicit frames before a
  quadratic constraint is merged or a sub-objective is resolved. Each side of an inequality is
  expanded separately, so cancelling an unregistered function does not bypass its lifecycle check.
  Variables are resolved against the target token table, and symmetric variable pairs use the same
  merge key. Promoted linear meta-model constraints retain the original name and display name;
  linear and quadratic dynamic rows also retain the relation name unless explicitly overridden.
- Nested constants are accumulated within their source expression before its parent contribution.
  Source terms are retained for objectives until the matrix conversion; constraints merge by
  variable key. Function adapters use their structural result polynomial, must be registered on the
  target token table, and still need their own constraint-registration lifecycle. Factory failures
  dispose the derived token table, while dynamic row failure leaves the row list unchanged. Existing
  mechanism coefficients and terms are snapshots, not live views of subsequent expression edits.
- Cycles, unsupported symbols, missing nonzero variables, terms above degree two, checked integer
  arithmetic, non-finite intermediate calculations and nonzero multiplication underflow fail as
  Results. Tests cover 2048 nesting levels, repeated/shared factors, zero factors, Flt64, Int64,
  FltX and the existing double-backed RtnX representation. Direct variable-only objective terms
  retain the existing matrix-boundary checks for non-finite values; a directly supplied invalid
  coefficient to the public constraint factory is likewise not validated until matrix conversion.
- The fixed Kotlin generic quadratic inequality path performs an identity merge and does not itself
  recursively expand intermediate factors. This C# behavior is a deliberate correctness extension,
  not a claim of identical cross-language semantics. Expansion can reassociate floating-point
  products; bitwise equality and identical signed-zero behavior are not guaranteed. Higher-degree
  nonzero products are rejected before possible cancellation of separate terms. Shared DAGs are not
  cached or size-limited and can require substantial time and memory; concurrent expression edits
  during a build are unsupported. Custom token tables or direct factory callers must ensure that
  registered functions actually have their defining rows installed.

#### Batch 32 API migration

Register every nonzero function adapter and its variables on the target model, inspect the returned
Result when adding or building quadratic rows and objectives, and rebuild after changing an expression.
Callers that previously expected nested quadratic terms to fail should review the resulting rows;
unrepresentable arithmetic or degree now fails earlier. Name fallback for promoted linear meta-model
constraints and dynamic rows now follows the original relation unless an explicit override is supplied.

### P2 Batch 33: safe dynamic rows and structural snapshots (historical)

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `LinearModelState.kt`, `MathInequalityFlatten.kt` | `LinearModelState<V>.RegisterRow`, `RefreshAsync`, `RequireCurrentAsync` | `LinearModelStateSafetyTest`, `LinearModelStateTest`, `ObjectiveEvaluationTest`, `LinearSymbolFlattenTest` | Plain nested rows, checked representative arithmetic, snapshot retention and failure propagation verified in memory; the Batch 33 function restriction is superseded by Batch 34 |

- A dynamic row now expands plain nested linear expressions on each side with the shared iterative
  lowering path, checks registered variables even when nonzero terms subsequently cancel, and stores
  an independent normalized variable-level row. Equivalent normalized definitions with the same ID
  are idempotent; a different definition, strict comparison, invalid ID, unknown variable, cycle or
  unrepresentable arithmetic fails before adding a row. Zero-coefficient input terms are ignored,
  including otherwise missing symbols. An identity-registry failure still rolls back the appended row.
- At this batch, nonzero function symbols were deliberately rejected even when registered. Freezing only their
  result variables would discard the requirement to keep their defining rows registered. Detached
  lowering reports that it cannot retain function registration; mechanism-model lowering with a
  token table continues to report an actually unregistered function. Supporting frozen dynamic rows
  containing functions requires lifecycle dependencies that survive direct mechanism construction,
  removal, restoration, and identity retries; see Batch 34 for the subsequent implementation and limits.
- Refresh checks row coefficients and right-hand sides, objective signs, products and sums before
  publishing a new snapshot. Integer wraparound, nonfinite intermediates, arithmetic exceptions and
  nonzero multiplication underflow fail instead of publishing a corrupted revision. A failed refresh
  leaves the last published snapshot and revision unchanged, although local row IDs may already
  have been reserved. `Fatal` errors retain their full error list through refresh and current-version
  validation. A closed model consistently returns `ApplicationStopped`, including idempotent retries.
- Tests exercise a 2048-level expression, normalized retry, mutation after registration, registered
  and missing ABS symbols, cancellation, Int64 overflow, FltX decimal overflow and precision beyond
  2^53, Flt64 overflow, the existing double-backed RtnX case, failed-refresh recovery, identity
  rollback and model closure. The fixed Kotlin baseline accepts only variable-level dynamic rows;
  plain-expression expansion and conservative checked arithmetic are C# correctness extensions,
  not a claim of identical semantics. A mathematically representable final expression can still be
  rejected if an intermediate operation overflows. Custom numeric types, exact floating-point
  cross-language behavior and concurrent mutation remain unverified.

#### Batch 33 API migration

Check the returned Result for row registration and snapshot refresh, including `Fatal`. RegisterRow
now accepts plain nested linear expressions and freezes their current expansion; later edits require a
new row identity rather than updating that row. Batch 34 subsequently accepts registered function
inputs subject to the lifecycle restrictions below. Rows or objectives previously accepted
after integer wraparound or other invalid intermediate arithmetic can now fail. Closed refreshes and
retries report `ApplicationStopped` rather than a generic validation failure.

### P2 Batch 34: function-dependent dynamic rows

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `LinearModelState.kt`, `MathInequalityFlatten.kt`, `MechanismModel.kt` | `LinearModelState<V>.RegisterRow`, `LinearMechanismModel<V>.InvokeAsync` | `LinearModelStateFunctionTest`, `LinearModelStateSafetyTest` | Registered function dependencies, frozen rows, in-memory lifecycle checks and representative structural revision checks verified; real solver and arbitrary custom function side effects not verified |

- A dynamic row can now reference registered linear functions through nested expressions. Nonzero
  function references are retained even if their expanded terms cancel; missing functions, missing
  variables, cycles and invalid arithmetic still fail. The row freezes the normalized expansion,
  while the model retains each referenced function's expanded result and nested function identities.
  Reusing an ID requires the same frozen definition and function references. Capture rechecks the
  source expansion before publishing a row, and failed capture does not reserve its identity.
- Direct mechanism-model builds and state refreshes validate those dependencies before and after
  function-constraint registration. Removing a required function or changing its result structure
  fails the build; restoring it permits recovery. Failures release the derived token table and
  preserve `Failed` or `Fatal` results. Changes to a function's input can produce different defining
  rows and a new snapshot revision when the result projection remains stable; this depends on the
  function actually registering those rows and is not a guarantee for arbitrary custom functions.
  Since Batch 39, ABS, extrema and binaryzation copy their direct monomial lists, so changing the
  original list no longer changes those functions or creates a new revision.
- Function projections are compared conservatively in expanded term order and by dependency
  reference, so equivalent reordered or re-expressed results can be rejected. For the existing
  double-backed `RtnX`, state comparisons use its stored representation rather than overflowing
  cross-products; equivalent fractions and signed zero with different representations may also
  invalidate a revision or retry. This does not repair the public `RtnX` comparison or its arithmetic,
  and its representation-based check must be revisited if the type's layout changes. Other numeric
  types use their default equality; custom implementations with incorrect equality remain unverified.
- The dependency metadata belongs to the original model and registered row. Copying the frozen bare
  inequality into another model does not carry it. Arbitrary side effects in custom functions and
  concurrent edits during registration or build are unsupported. Each function is expanded again
  for capture and validation; deep or branching dependency graphs can incur quadratic or greater
  work, with no measured performance claim. The fixed Kotlin baseline accepts variable-level
  dynamic rows, so this expansion and lifecycle tracking is a C# correctness extension.
- Tests cover ABS defining rows, nested and cancelled functions, dependency identity, removal and
  restoration, mutations during capture or registration, identity rollback, `Fatal` propagation,
  a 128-function chain, FltX/RtnX/Int64 and cross-product overflow in projections, retries and
  all stored snapshot value positions. This is in-memory contract evidence, not a real solver run.

#### Batch 34 API migration

Register the referenced functions and their variables on the same model before `RegisterRow`, retain
them while the row is active, and check Results from row registration and every subsequent build or
refresh. Do not copy a bare frozen inequality to transfer lifecycle dependencies. If the function's
result structure changes, create a new row identity instead of retrying the old one; even equivalent
RtnX representations can conservatively be treated as a change.

### P2 Batch 35: generic standalone symbol preparation

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `TokenTableRegistrationSupport.kt`, `DependencyLayers.kt`, `MechanismModel.kt` | `TokenTableRegistrationExtensions.Register<V>`, `IMathFunctionSymbol<V>.TryEvaluate` | `TokenTableGenericRegistrationTest`, `TokenTableRegistrationTest` | Representative generic standalone preparation, checked function arithmetic and bounded rollback verified in memory; factory preparation follows in Batch 37 |

- The standalone layered registration path accepts a typed converter and fixed values in `V` without
  passing those fixed values through Flt64. It retains the Flt64 overload, orders nested adapters by
  declared inputs, preserves the source mapping, and treats a nonempty fixed-value map as the only
  input source. Without fixed values, available token results still cross the existing Flt64 token
  boundary. Prepared values, preexisting cache entries, auxiliary tokens, progress failure and
  `Fatal` propagation are exercised for FltX, Int64, Flt64 and representative RtnX values.
- `TryEvaluate` distinguishes a missing input (`Ok(null)`) from a checked arithmetic failure.
  ABS, extrema and floor/ceiling/rounding use shared checked polynomial evaluation; integer product
  and sum wraparound, invalid results and arithmetic exceptions fail instead of caching a wrapped
  value. Flt64 evaluation retains ordinary operation order and multiplication-underflow behavior.
  Existing `Evaluate` on these functions now returns null on a detected failure, rather than a
  wrapped result or an arithmetic exception. Failure during preparation restores the affected caches
  and removes newly registered symbols and helper tokens under the existing bounded rollback rules.
- RtnX still uses two double fields, not arbitrary-precision rational arithmetic. The checked
  function path rejects negative denominators, nonfinite/underflowed components, unrepresentable
  intermediate products, rounded-to-zero cancellation, and ambiguous extremum/rounding
  comparisons. Exact binary-product equality distinguishes equivalent fractions from rounded ties;
  some mathematically valid inputs are conservatively rejected. Fixed function result conflict
  checks use stored representation, so equivalent fractions can also conflict. The shared flatten
  validity check now rejects malformed RtnX storage, but this does not repair public RtnX arithmetic
  or comparisons. Its storage-layout assumptions must be revisited if that type changes.
- These checks do not establish exact rational arithmetic, complete checks for other numeric types,
  or safe intermediate operations in arbitrary custom functions. The default `TryEvaluate` checks
  the returned value and arithmetic exceptions only; a custom function must implement its own
  checked method to report internal failures. Other existing functions, including comparison
  indicators, may still return `Ok(null)` for internal unsupported or invalid cases. Direct custom
  symbol implementations, callbacks, and token-table mutations are subject to the Batch 28/29 side
  effect and concurrency limitations.
- At Batch 35 neither mechanism-model factory was wired to this standalone preparer; Batch 37 adds
  that integration. Preparing semantic values is not matrix elimination, does not fix token
  values in the parent model, and does not restore a solution. Matrix elimination remains the
  separate Batch 27 operation. The later fixed-column continuation adds explicit checked recovery;
  automatic solver integration remains open.

#### Batch 35 API migration

Use the converter-taking `Register<V>` overload for non-Flt64 tables and check its `Failed` and
`Fatal` results. For ABS, extrema and rounding, use `TryEvaluate` when an unknown input must be
distinguished from numeric failure; `Evaluate` alone returns null for both. Custom function authors
should override `TryEvaluate`, declare all input symbols, and avoid side effects. Continue to use the
matrix dump's explicit fixed-variable elimination; Batch 37 describes factory preparation separately.

### P2 Batch 36: automatic-index rollback prerequisite

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `MechanismModel.kt` token checkpoints | `AbstractMetaModel<V>.Add`, `TokenTableRegistrationExtensions.Register<V>` | `FunctionSymbolRegistrationTest`, `TokenTableGenericRegistrationTest` | Failed auxiliary registration restores the automatic next index under the limited rollback contract; Batch 37 adds a separate factory transaction |

- Automatic token tables now capture the exact next solver index alongside the existing token keys
  before symbol registration. On `Failed` or `Fatal`, newly introduced tokens are removed and the
  captured index is restored when no surviving token occupies that index or a later one. This
  preserves gaps left by earlier removals rather than reconstructing the index from the maximum.
  Successful registration retains its assigned indices. Manual and other mutable token tables keep
  the existing key-based removal behavior.
- Tests cover `Failed` and `Fatal` from meta-model helper registration and standalone progress
  callbacks, an FltX preparation failure, preexisting gaps, callback cleanup, successful registration,
  and replacement of an original token with a later index. The latter keeps the advanced index to
  avoid reusing an index still occupied by that replacement.
- This is not Kotlin's complete mechanism-build transaction. Key-based rollback does not restore an
  original token removed or replaced by a callback; unrelated additions, arbitrary callback side
  effects, exceptions, and concurrent model or token-table mutation remain outside the rollback
  guarantee. Capture and rollback each use the automatic table lock, but the full registration does
  not hold it. No public API migration is needed. At Batch 36 the mechanism factories still rejected
  nonempty `fixedVariables`; Batch 37 covers their distinct preparation transaction.
  The later token-registration transaction below supersedes the key-only rollback described here.

### P2 Batch 37: mechanism-factory symbol preparation

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `MechanismModel.kt`, `TokenTableRegistrationSupport.kt`, `DependencyLayers.kt` | `LinearMechanismModel<V>.InvokeAsync`, `QuadraticMechanismModel<V>.InvokeAsync` | `MechanismModelPreparationTest`, `FunctionSymbolRegistrationTest` | Generic in-memory factory preparation, representative token ownership and failure paths verified; complete fixed-variable solving and arbitrary callback isolation remain gaps |

- Both built-in factories now accept `fixedVariables` for dependency-ordered semantic preparation.
  A nonempty map is the only input source; null or an empty map can use existing token results.
  Its comparer is preserved. FltX and Int64 fixed values stay in `V`, while existing token results
  retain the current Flt64 boundary. Nonfinite inputs fail. This preparation is not matrix
  substitution, bound/domain validation, or automatic solution restoration.
- Existing token results are copied to an isolated working table for preparation; a custom symbol
  mutating that table cannot change the parent's existing token results. Delivered mechanism tokens
  still share existing identities and results with the parent; newly registered helper identities
  are committed only after successful construction. Temporary tables and model snapshots are
  released on `Failed`, `Fatal`, normal exceptions, parent closure and detected token/symbol
  changes. The original automatic next index is not advanced on failed construction.
- Unregistered pure linear/quadratic expression dependencies can be prepared as hidden dependencies,
  including nested expressions, without entering the public symbol set or registration progress
  totals. Unregistered function dependencies remain invalid. Preparation caches belong to the
  mechanism table, not the parent; they are not automatically kept coherent after direct token or
  symbol changes.
- Function adapters must declare `InputSymbols`; unknown inputs now fail factory construction instead
  of silently succeeding. This includes existing placeholder functions that have no declared input
  contract. Auxiliary-token registration runs on `MetaModel.Add` and again while constructing a
  mechanism model; custom registrations must be repeatable. Preparation progress is wired through;
  the `concurrent`, `blocking`, and dumping-progress arguments remain unused. Arbitrary callbacks or function constraint writers can mutate the
  parent or the delivered model's shared tokens directly; those side effects are not rolled back.
  Concurrent structural or solution changes, direct standalone registration, deeply mutable
  variables/symbols and cache invalidation are not covered by the factory isolation guarantee.
- Tests exercise linear/quadratic models, auto/manual indices, nested and partial fixed inputs,
  custom comparison, FltX/Int64, existing and newly added token identities, isolated result writes,
  failed or fatal progress and function constraints, exceptions, parent mutation/closure, and
  replaced working tokens. These are in-memory contracts, not backend solve evidence.

#### Batch 37 API migration

Check the factory Result and dispose successful models. Custom function adapters must supply complete,
stable `InputSymbols` and support repeated auxiliary registration; functions without that metadata
now fail even when no fixed map is supplied. To eliminate fixed columns, pass the mapping separately
to `ToLinearTriadModel` or `ToQuadraticTetradModel`, and explicitly restore a full solution as described
in the later fixed-column continuation. The factory currently ignores `concurrent`, `blocking`, and
`dumpingStatusCallBack`.
Do not infer fixed bounds, solver warm starts, or an incumbent from a prepared cache.

中文迁移提示：检查工厂返回的 Result 并释放成功模型；自定义函数适配器须完整声明
`InputSymbols`，且辅助 token 注册须可重复调用。`InvokeAsync` 的 `fixedVariables` 只用于符号准备，
需要固定列消元时仍须将固定值显式传给矩阵转换，并按后续固定列恢复章节还原完整解。准备缓存不代表已固定变量、
已设置求解器热启动或已存在可行解；工厂当前不使用 `concurrent`、`blocking` 和 `dumpingStatusCallBack` 参数。

### P2 Batch 38: generic range-distance function

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `SlackRange.kt`, `Max.kt` | `SlackRangeFunction<V>.Create`, `LinearFunctionSymbolAdapter<V>` | `SlackRangeFunctionTest`, `FunctionSymbolRegistrationTest` | Representative generic evaluation and in-memory linear/quadratic rows verified; Big-M validity, shared extrema bounds and real-solver enforcement remain open |

- Replaces the Flt64 placeholder with `max(lower-input, input-upper, 0)`, using the shared maximum
  function's result variable, three binary selectors and seven rows. With a sufficient Big-M, those
  rows do not permit an inflated result even without a minimizing objective. The input monomial list
  is copied and read-only, but referenced variables and intermediate symbols are not frozen. The
  wrapper delegates its current name and display name to the inner function.
- Creation returns `Failed` for reversed or nonfinite bounds, invalid input, nonpositive or
  nonfinite explicit Big-M, and detected negation or derived-constant overflow. Checked evaluation
  distinguishes missing input from detected arithmetic failure. Tests cover Flt64, Flt32, Int64,
  FltX and representative RtnX values, exact large integer/decimal evaluation, nested ABS input in
  linear and quadratic factories, fixed-value preparation, nonfinite values, overflow and factory
  snapshot cleanup. RtnX checks remain conservative and do not repair its public arithmetic.
- Unlike the shared maximum's default inferred result range, this wrapper resets its result domain
  to `[0,+infinity)`: converting large FltX constants through Flt64 can otherwise create an empty
  domain. The underlying `MaxFunction<V>` and other extrema still use that potentially unsafe
  inference and need a shared outward-rounding or conservative-bound fix; this batch does not fix
  them. Default Big-M is still inferred via Flt64 or falls back to `1000000`; neither default nor an
  explicit positive Big-M is proven sufficient for the model domain. A too-small value can remove
  valid solutions. Nested symbol bounds, mutable symbol structure, floating-point rounding, custom
  converters and solver tolerances require particular care; no real solver has been exercised.
- At this batch direct `RegisterConstraints` delegated sequential writes to the maximum function.
  Its shared writer now uses the later constraint-write transaction, subject to that contract's
  limits. Arbitrary callbacks and parent mutations remain outside the guarantee. Deferred/native
  maximum structures and quadratic slack functions remain gaps; `SlackFunction` itself is still a
  placeholder.

#### Batch 38 API migration

The old parameterless `SlackRangeFunction` was a nonfunctional placeholder and is removed. Call
`SlackRangeFunction<V>.Create(input, lower, upper, converter, ...)`, check the `Result`, and add a
`LinearFunctionSymbolAdapter<V>` with its input dependencies to the same meta-model. Use
`TryEvaluate` to distinguish an unknown value from arithmetic failure. Supply a verified positive
Big-M when the default cannot be justified, and dispose successful mechanism models.

中文迁移提示：原无参数 `SlackRangeFunction` 未实现建模，现已移除；通过泛型 `Create` 构造并检查
Result，将适配器及输入依赖注册到同一元模型。`TryEvaluate` 可区分缺值与数值失败；自行确认 Big-M
覆盖完整定义域，共享写行失败按下述事务契约回滚，外部副作用不受该契约保护。

### P2 Batch 39: captured-bound lifecycle (historical)

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `Abs.kt`, `Max.kt`, `Binaryzation.kt`, `BigM.kt` | `AbsFunction<V>`, extrema and wrappers, `BinaryzationFunction` | `CapturedBoundsLifecycleTest`, `LinearModelStateFunctionTest` | Direct input snapshots and representative captured-range invalidation verified; inferred bounds and Big-M are not proven safe |

- ABS, extrema and binaryzation now copy their direct monomial lists into read-only snapshots. When
  construction inferred bounds used for ABS helpers or an extremum result, registration checks the
  direct input ranges before adding rows, even with an explicit Big-M. Automatic binaryzation also
  checks its captured range; explicit-M binaryzation does not depend on a captured range. The check
  follows through the extremum wrappers and `SlackRangeFunction<V>`.
- Built-in variable ranges are tracked by the original native range object, not its Flt64 view.
  Replacing that object invalidates registration, including with an equivalent or narrower range. This catches
  an Int64 upper-bound expansion from `2^53` to `2^53+1` that disappears in the Flt64 view. A
  custom `IVariableItem` without the built-in tracker fails conservatively when captured bounds
  depend on it. Direct intermediate range values and interval kinds are compared structurally;
  RtnX uses its stored representation, so mathematically equivalent representations can invalidate
  the snapshot. Captured-range arithmetic and invalid-operation read errors return a failure before row writes.
- Batch 39 did not fix outward rounding. Its seven precision regressions and the narrow default
  helper domains are addressed in Batch 40 below; this does not prove all inferred Big-M values or
  custom converters safe. The original reproducer remains in ignored `logs/` as historical evidence.
- At Batch 39, checks were performed only at registration and did not detect changes after model
  construction. The later tracked-bound gate below extends this for selected functions and standard
  conversions; it still does not freeze nested expressions or guarantee concurrent mutation safety.
  A late failure from sequential direct row writes can still leave earlier rows; the factory has only
  its previously documented bounded rollback. This historical batch did not verify real-solver enforcement.

#### Batch 39 API migration

Set input ranges before constructing these functions. After replacing an input range, reconstruct
the function and its adapter rather than retrying the old one; this conservative rule also applies
to narrowing. Changes to the caller's original monomial list no longer update the function: build a
new one for a new expression. Custom variable implementations without the built-in range tracker
may now fail registration when finite bounds were captured. Check every registration/build Result,
independently validate bounds and Big-M, and rebuild after any later model mutation.

中文迁移提示：先设定输入范围再构造函数；范围替换（包括收窄）后重新创建函数和适配器。原单项式
列表的后续修改不再改变函数，自定义变量在捕获有限界时可能被保守拒绝。仍须自行核对推导范围、
Big-M 和嵌套符号；本批不保证求解模型正确。高精度舍入问题的后续处理见 Batch 40。

### P2 Batch 40: finite-bound rounding and helper domains

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `BigM.kt`, `Abs.kt`, `Max.kt`, `Binaryzation.kt` | `BigMValues.FiniteBounds`, `AbsFunction<V>`, extrema and `BinaryzationFunction` | `FiniteBoundsSafetyTest`, `CapturedBoundsLifecycleTest`, `MechanismModelPreparationTest` | Representative directed bounds and failure paths verified; full Big-M and solver correctness remain unproven |

- The shared linear-bound calculation uses exact integer fractions for supported built-in numeric
  values, takes the hull of native and encoded coefficients/constants, reads built-in variables'
  native ranges, and rounds products and successive sums outward. It also includes endpoint values
  after conversion into `V`; unrecognized numeric representations, custom variables without a
  native range, non-finite values, detected inconsistent encodings and unrepresentable bounds
  produce no inferred bounds. `FltX`, `RtnX`, `IntX` and `UIntX` expose their stored values without
  relying on private field layouts. The original seven precision reproducers now run in the normal
  test assembly, alongside large native integer bounds, subnormal underflow, floating accumulation,
  decimal encoding, Flt32/RtnX, overflow and converter checks.
- ABS helper domains start at `[0,+infinity)` and extremum results at the full real range before
  inferred limits are projected outward to Flt64. Automatically inferred extrema distances and ABS
  magnitudes are rounded upward, including the solver encoding; an unrepresentable integer
  magnitude such as `abs(Int64.MinValue)` fails before rows are written. ABS, extrema and automatic
  binaryzation no longer register with their `1000000` fallback when finite inference fails. For
  automatic paths they also retain direct range tracking. Kotlin's pinned baseline uses ordinary
  Flt64 arithmetic and fallback here; these C# checks are deliberate conservative differences.
- This does **not** certify arbitrary `IIntoValue<V>` implementations: matching `FromValue` at
  inspected values and checking converted endpoints cannot prove monotonicity, stability, interior
  behavior or absence of side effects. RtnX stores two doubles; exact rational interval arithmetic
  does not bound every rounding error of its numerator/denominator evaluation. Solver conversion
  of native integer variable limits, floating row normalization, solver tolerances and negative-zero
  semantics are not proven here. BigInteger interval work has not been benchmarked and may cost
  more for large expressions. The two-attempt solver-bound conversion can conservatively reject
  representable values.
- An explicit nonnegative Big-M is still trusted rather than proved sufficient, including where
  bounds are unavailable; other Big-M functions retained the `1000000` fallback at this batch.
  Batch 41 changes several of those callers, but not all Big-M-based functions. Direct nested
  symbol range declarations, mutations after construction or
  model build, concurrent changes and custom converter behavior remain the caller's responsibility.
  Sequential direct row writes can still leave earlier rows on later failure. No real solver has
  been used to validate the resulting model.

#### Batch 40 API migration

Set finite input ranges before constructing ABS, extrema or automatically parameterized
binaryzation. If inference is unavailable, supply an independently validated explicit Big-M or
handle the registration failure; this is not a proof that the supplied value is sufficient. A
function adapter whose intermediate range is unknown may require an explicit value. Reconstruct
the function after changing a tracked range, and check every `Result`. The new numeric
`ToDecimal`, `ToParts` and `ToBigInteger` methods expose the stored representations.

中文迁移提示：先设置有限输入范围，再创建 ABS、极值或自动二值化函数；无法推导时注册会失败，
调用方可提供自行验证的显式 Big-M，但系统仍不证明其充分性。未知范围的嵌套符号也可能需要显式值。
范围变化后重新创建函数并检查 Result；自定义转换器、RtnX 求值及求解器舍入仍需独立核验。
其他函数的回退与限制更新见 Batch 41。

### P2 Batch 41: automatic indicator bound failures

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `BigM.kt`, `Inequality.kt`, `Binaryzation.kt`, `And.kt`, `OneOf.kt`, `SameAs.kt`, `SatisfiedAmount.kt` | `InequalityFunction<V>`, `BinaryzationFunction`, `AndFunction`, `OrFunction`, `NotFunction`, `XorFunction`, `OneOfFunction`, `SameAsFunction`, `SatisfiedAmountFunction` | `AutomaticIndicatorBoundsTest`, `FiniteBoundsSafetyTest`, `InequalityNestedInputTest` | Missing and unrepresentable automatic bounds rejected; indicator row arithmetic remains unsafe in known cases |

- The shared inference now returns no value when finite bounds, magnitude, positive promotion or
  solver representation cannot be proven. The listed automatic indicator paths reject that state
  instead of silently using `1000000`; this is a deliberate difference from the pinned Kotlin
  baseline. Comparison preflight checks before helper tokens and again before rows, using the
  expanded input. The other listed multi-input functions construct their rows in memory and reject
  a later unbounded or overflowing input before adding any of those rows. The later constraint-write
  transaction additionally covers failures during shared writes, within its documented limits.
- `NotFunction` snapshots its input monomial list, preserving consistency with its cached binary
  fast path, and derives an automatic M at row registration rather than construction. AND, OR
  and NOT retain their direct binary fast paths. Automatic binaryzation still captures its input
  bounds at construction and rejects a changed tracked range; when inference fails its exposed
  `BigM` is zero and registration fails. An ABS constructed with unavailable automatic bounds
  likewise exposes zero for its invalid part bounds instead of an unused fallback. Other logical
  functions retain their existing input-list and model-mutation semantics.
- **Known correctness gaps at Batch 41, not passed acceptance at that time:** a binaryzation with explicit M=`1e16` and
  tolerance `0.125` accepts input zero with its flag set to one because floating arithmetic erases
  the gap. `SatisfiedAmountFunction` with `x` in `[-2,2]`, `x <= 0`, epsilon `0.25` and automatic
  M=`2` rejects the valid `x=-2, flag=1` endpoint: its row requires `-x - 2*flag <= -0.25`.
  Its evaluator also uses an epsilon-shifted comparison that is not identical to the LE/GE row
  split. These two cases were reproduced as failing diagnostic tests; their source and output are
  retained under ignored `logs/`, not counted as successful tests. Batch 42 below rejects the
  binaryzation case and restores the tested satisfied endpoint; it does not complete the shared
  indicator-arithmetic, zero-band, normalization or endpoint audit.
- An inferred magnitude alone is not a proof of the final row's soundness; explicitly supplied M
  is not checked for sufficiency either. `BalanceTernaryzationFunction`, `SemiFunction` and other
  independent fallback-based functions are not changed here. Unknown nested ranges can be
  conservatively rejected; custom converters, changing bounds after model construction, external
  writer side effects, concurrent mutations and real-solver tolerances remain unverified.

#### Batch 41 API migration

Provide finite input ranges before registration or supply an independently verified explicit M;
check the returned `Result`. Rebuild after changing model input domains. For comparisons, both
helper registration and row registration may now fail. For the remaining listed indicators, a
failed bound inference is detected before row writes, but helper tokens can already exist. Do not
assume a successful registration certifies indicator rows: independently verify the interval and
encoding. The two specific Batch 41 examples were subsequently handled as described in Batch 42.

中文迁移提示：比较、逻辑及计数指示函数的自动 M 不再对无法证明的界使用 `1000000`；应先设置
有限范围或自行验证并传入显式 M，检查每个 Result，模型范围改变后重建。推导成功仍不代表指示行
正确：上述两个具体复现的后续处理见 Batch 42，完整行编码仍未验收。

### P2 Batch 42: selected indicator gaps and endpoint relaxation

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `Binaryzation.kt`, `SatisfiedAmount.kt`, `IndicatorStructure.kt` | `BinaryzationFunction`, `SatisfiedAmountFunction` | `IndicatorConstraintSafetyTest` | Selected gap collapses and endpoint failures handled; full row and evaluation correctness not verified |

- Before writing binaryzation rows, C# expands the input and both rows, rejects an input reference
  to its own result variable, missing nonzero variable tokens, failed expansion, non-finite or
  mismatched flag coefficients, and a detected collapse of the normalized true/false boundary.
  Thus the Batch 41 `M=1e16`, `tolerance=0.125` example fails registration conservatively; it is
  not a successfully encoded indicator. Missing input/flag tokens, a cyclic expression, a nested
  constant and a nested self-reference whose coefficient rounds away are covered with an existing
  sentinel row preserved.
- For LE/GE satisfied-amount rows, the relaxed coefficient is now the exact sum of M and epsilon
  rounded upward to a representable solver value. The original `[-2,2]` endpoint example succeeds
  for both signs, automatic and explicit M; shifted representable endpoints and subnormal epsilon
  are also tested. After expansion, the function preflights all generated rows and registered
  variables, rejects references to its own helper variables and checks LE/GE normalized gap
  collapse. A later bad input therefore does not write the earlier prepared rows for these detected
  failures. These C# rejections and the larger relaxation deliberately differ from the pinned
  Kotlin rows. A positive explicit M is still not proved sufficient.
- **Outstanding correctness gaps at Batch 42, not passed acceptance at that time:** for `x <= 0`, epsilon `0.25`,
  `x=0.125`, evaluation reports satisfied but neither flag is feasible in the generated LE split;
  GE has the symmetric mismatch. EQ uses identical tolerance and strict boundary, so at
  `abs(x)=epsilon` both flags can be feasible although evaluation reports satisfied. With zero
  epsilon LE/GE also overlap at the boundary. These evaluation/row behaviors follow the pinned
  Kotlin baseline. Batch 43 below explicitly chooses a gap and three-state evaluation: a strict
  complement over continuous reals cannot be represented exactly by a finite closed MILP without
  a gap or overlap.
- Normalization is not an exact encoding proof. For `x + 0.3 <= 0`, M=`2`, epsilon=`0.25`,
  `x=-0.3`, flag `1`, the stored upper row's exact binary-rational left side exceeds its stored
  right side by `2^-54`, even though the in-process floating accumulation can round it to equality.
  No real solver tolerance was tested. For `amount` set, evaluation returns a Boolean, but the
  result polynomial remains `sum(u)` and an added row enforces the threshold: two true flags can
  yield polynomial 2 versus evaluation 1, and a false evaluation can make the model infeasible.
  This is also the pinned Kotlin shape. Batch 43 keeps the hard threshold and changes the structural
  result to the Boolean value on feasible models; it does not turn the hard constraint into an
  unconstrained reified predicate.
- Six diagnostic tests covering the LE/GE gap, EQ overlap, exact normalized endpoint and both
  `amount` mismatches failed and are **not** part of the passing gate. Their source and output are
  retained in ignored `logs/p2-batch42-outstanding-tests.cs.txt` and
  `logs/p2-batch42-outstanding-red.log`. The normal tests use `Constraint.IsTrue` floating
  accumulation and do not prove real-solver behavior. Other zero/nonzero indicators, all rounding
  paths, concurrent mutation between preflight and writes, and arbitrary writer failures remain
  unverified; sequential writer failures can still leave rows. See Batch 43 for the subsequent
  diagnostic status. P2 remains incomplete.

#### Batch 42 API migration

Check every registration `Result`. Binaryzation and satisfied-amount registrations may now fail
for a detected collapsed gap, missing token or self-referential helper rather than produce rows.
Do not treat a successful registration as proof of exact thresholds or sufficient explicit M; avoid
using satisfied-amount output as an unconstrained reified Boolean; the chosen threshold and boundary
semantics are described in Batch 43. Preserve and rebuild input symbols rather than mutating them during registration.

中文迁移提示：二值化与满足数量函数遇到已检测的间隔塌陷、缺少 token 或自引用辅助变量时会
保守失败；逐项检查 Result。注册成功不代表 Big-M 充分或端点精确。满足数量的后续区间与阈值
语义见 Batch 43；它不是不附加硬约束的双向布尔指示器。

### P2 Batch 43: satisfied-count semantics and hard threshold

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `SatisfiedAmount.kt`, `IndicatorStructure.kt`, `Inequality.kt` | `SatisfiedAmountFunction` | `SatisfiedAmountSemanticTest`, `SatisfiedAmountFunctionsTest` | Three-state examples and feasible threshold result verified; exact row encoding and real solvers remain unverified |

- The C# evaluator now follows the existing comparison-indicator split for supported LE/GE inputs:
  with `d=lhs-rhs`, LE is true for `d<=0`, false for `d>=epsilon`, and unknown in between; GE is
  symmetric. EQ is true for `abs(d)<=epsilon`, false for `abs(d)>=strictBoundary`, and unknown
  in between. Any unknown input makes the aggregate evaluation null. A positive finite epsilon is
  required, avoiding the previous zero-gap overlap. This deliberately differs from Kotlin's
  always-Boolean epsilon-shifted evaluation and from its overlapping EQ rows.
- `strictBoundary` is an optional appended constructor argument and defaults to twice epsilon.
  For EQ it must be finite and strictly greater than epsilon. Row preflight rejects detected
  overflow, indistinguishable relaxation coefficients, and overlap of the normalized bands,
  comparing the latter as exact binary fractions. This is still not a proof that normalized
  thresholds or relaxation endpoints equal the intended values. LE/GE ignore `strictBoundary`.
  The gap is an intentional modeling exclusion, not a representation of every continuous input.
- `amount` retains Kotlin's hard `sum(flags)>=amount` row and standalone 0/1 evaluation. The
  result polynomial is now constant one when the hard threshold is present, matching evaluation
  on feasible models rather than exposing the count as the Boolean result. If the threshold is
  false, evaluation returns zero but the model is infeasible; this is not a free reified predicate.
  Without `amount`, the result polynomial and evaluation remain the count. Tests cover both through
  a registered adapter, fixed-value preparation and a nested objective, as well as zero and
  unreachable thresholds. The caller can compare the unthresholded count separately when a
  reified threshold without a hard constraint is required.
- Because a threshold result now flattens to a constant, self-reference preflight also tracks
  function adapters themselves, including through a nested expression; checking only flag columns
  would miss that cycle. Tests retain existing rows on a later invalid equality and cover explicit
  and default boundaries, both flags, both equality sides, invalid boundaries and a collapsed gap.
  LT/GT/NE registration remains unsupported. Custom writers, concurrent changes and later
  sequential writer failures retain the prior limitations.
- The Batch 42 LE/GE and EQ diagnostic examples are superseded by the new documented gap semantics,
  and the `amount` polynomial mismatch is fixed on feasible models. Its false-result diagnostic
  expected feasibility despite the deliberately retained hard threshold, so that expectation is
  not an acceptance criterion. The exact `x+0.3<=0` endpoint diagnostic still fails; rerun output
  is in ignored `logs/p2-batch43-endpoint-red.log` and is excluded from the passing gate. Normal
  tests use floating `Constraint.IsTrue`; exact normalization, EPS/Big-M sufficiency, solver
  feasibility tolerances, native indicator writers and real-solver behavior remain unverified.

#### Batch 43 API migration

Handle null evaluation in each LE/GE/EQ gap, set positive epsilon and choose a finite EQ
`strictBoundary` larger than epsilon when the default of `2*epsilon` is not appropriate. The
setting may still be rejected if rows cannot retain a detected gap. With `amount`, the function
enforces the threshold and exposes constant one in a feasible model; use the unthresholded count
and a separate comparison for a reified Boolean that may be false without infeasibility. Existing
callers that treated the thresholded result polynomial as a count must migrate.

中文迁移提示：LE/GE 和 EQ 的真、假分支之间现在明确留出不可判定区间，求值在区间内返回 null；
epsilon 必须为正，EQ 的 `strictBoundary` 默认是两倍 epsilon，也可显式指定更大的有限值。
`amount` 仍是硬性最低数量，其可行模型结果多项式改为常量 1；若需要可为 0 的布尔结果，应对
不带 `amount` 的计数另建比较。浮点归一化端点与真实求解器容差仍未验收。

### P2 Batch 44: shifted LE/GE residual rows

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `SatisfiedAmount.kt` | `SatisfiedAmountFunction`, `ResidualVars` | `SatisfiedAmountResidualTest`, `IndicatorConstraintSafetyTest` | Selected LE/GE branch endpoints checked as exact binary fractions in mechanism rows and matrix dump; not a general evaluation-equivalence proof |
| `core-plugin-gurobi` | `GurobiLinearSolver` | `SatisfiedAmountGurobiIntegrationTest` | 14 selected real Gurobi 10.0.1 solves at `FeasibilityTol=1e-9`; other capabilities remain unaccepted |

- A shifted or nested LE/GE input now receives a continuous residual `r` and a linking equality
  `r=d`, where `d` is the expanded input difference. The indicator rows refer to `r` with zero
  constant, avoiding the loss of a small offset when the Big-M term is normalized into the RHS.
  Direct unshifted inputs keep the existing two-row form. This intentionally differs from the
  pinned Kotlin formulation. The residual is allocated at construction if the two original
  constants differ or an input term is an intermediate symbol; it starts with a full real range.
- Preflight compares the active true and false branch constants as exact binary fractions, checks
  the merged input coefficients against their pre-merge values, and checks helper registration.
  A nonrepresentable difference between the original two constants, an inexact coefficient merge,
  or a detected changed input without an encodable residual fails before any rows are written.
  Later-input failures leave earlier prepared rows unwritten. Explicit Big-M sufficiency is still
  the caller's responsibility; the extra residual row does not validate it.
- The original `x+0.3<=0`, `M=2`, `epsilon=0.25`, `x=-0.3`, `flag=1` endpoint is now feasible
  under exact binary-fraction inspection of both the mechanism and its ordinary matrix dump.
  The same endpoint and a false endpoint were solved through the real Gurobi plugin; additional
  LE/GE true, false, wrong-flag and gap cases at epsilon `0.125` also passed, with Gurobi
  `FeasibilityTol=1e-9` explicitly set. These are selected instances, not an exact-arithmetic
  guarantee or acceptance of the whole Gurobi adapter. Gurobi's own tolerance can admit points
  near a mathematical boundary; its default integer tolerance remains in effect.
- **Remaining numerical mismatch:** residual construction starts from the existing Flt64-expanded
  input; nested expansion itself may have rounded, and evaluation keeps its existing ordered
  floating accumulation. For `1+x+y<=0`, `x=1e16`, `y=-1e16`, evaluation returns true while the
  exact rows require residual `1` and the false flag. The explicit M=`2` in that fixture is
  sufficient only for this assignment, not for the full unbounded domain. This discrepancy is
  reproduced by `FloatingEvaluation_CanStillDifferFromExactRowArithmetic` and is not counted as
  evaluation-equivalence acceptance. EQ endpoint encoding was unverified in this batch; see the
  later EQ batch for selected cases. Other indicators, fixed-variable substitution, custom
  converters/writers, concurrent mutation and sequential writer rollback remain unverified. Do not
  equate exact row-coefficient inspection with solver exactness.
- Opt in to the real-backend test with `dotnet test tests/Core.Plugin.Gurobi.Tests/Core.Plugin.Gurobi.Tests.csproj --configuration Release -p:RunGurobiIntegration=true --filter FullyQualifiedName~SatisfiedAmountGurobiIntegrationTest`.
  It requires the Gurobi 10 SDK, native runtime and license; requesting it without the SDK fails
  the build, and a missing license is a failure rather than a silent pass. The default build
  excludes these opt-in cases.

#### Batch 44 API migration

Inspect `ResidualVars` by input index and include each non-null residual in manually assembled
solution vectors or warm starts; it equals the expanded `lhs-rhs`. It also appears in
`HelperVariables` and the matrix, and a `{name}_sat_residual_{i}` equality is added. Existing code
that assumes one flag per LE/GE input or a fixed number of rows must adapt. Rebuild the function
after structural input changes and handle a failed Result for unrepresentable constants or merged
coefficients. Other input arithmetic and solver tolerances are not yet proven equivalent.

中文迁移提示：带偏移或嵌套的 LE/GE 输入会增加连续差值变量和定义等式；手工解向量或 warm start
需要为 `ResidualVars` 的非空项提供展开后差值。原始常量差或合并系数无法精确表示时注册失败，
不能只依赖返回标志数量或原行数。仅验证了部分 Gurobi 10.0.1 实例；求值浮点累加、固定变量代换
及求解器容差仍可能产生差异。EQ 端点的后续进展见下一批记录。

### P2 continuation: checked equality branches

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `SatisfiedAmount.kt`, `BigM.kt` | `SatisfiedAmountFunction`, `EqPositiveVars`, `EqNegativeVars`, `ResidualVars` | `SatisfiedAmountResidualTest`, `SatisfiedAmountSemanticTest`, `SatisfiedAmountFunctionsTest` | Selected exact EQ rows, matrix dumps and failure preflights covered; expansion and solver-wide equivalence not proven |
| `core-plugin-gurobi` | `GurobiLinearSolver` | `SatisfiedAmountGurobiIntegrationTest` | 14 selected EQ and 14 prior LE/GE cases passed at `FeasibilityTol=1e-9`; not full backend acceptance |

- With equality difference `d`, true flag `u`, positive and negative outside flags `p` and `n`,
  the checked C# formulation uses `u+p+n=1`, `d-M(p+n)<=epsilon`,
  `-d-M(p+n)<=epsilon`, `d+O(u+n)>=strictBoundary` and
  `d-O(u+p)<=-strictBoundary`. Here `M` must bound `abs(d)` and `O` is `M+strictBoundary`
  rounded upward to a representable solver value. Thus active branch thresholds are not recovered
  by subtracting a large coefficient from a rounded RHS. This intentionally differs from the
  pinned Kotlin four-row, single-side formulation; shared zero-indicator helpers and other callers
  are unchanged.
- Shifted or nested EQ inputs now use the same full-range residual and linking row as LE/GE;
  direct unshifted EQ inputs do not. Preflight checks the original constant difference, each
  expanded row's input coefficients, helper coefficients, active constants, one-hot row and token
  presence. A later input or unrepresentable relaxation fails before any prepared rows are added.
  Input coefficients and residual links are compared after intermediate expansion: expansion itself
  can round. Sequential failures inside a custom writer are not rolled back by this preflight.
- Eight shifted/nested exact-row regression cases all failed before the change, including rejected
  valid band/outside endpoints and an incorrectly admitted gap point. The final suite checks those
  cases in both mechanism and ordinary matrix rows, checks every binary assignment for selected
  direct input values including both Big-M endpoints, and exercises tiny and adjacent representable
  boundaries, directed relaxation, missing helpers and residuals, self-reference, inexact constant
  or coefficient merges and later-input failure. A `strictBoundary` immediately above epsilon can
  now be represented in these rows; practical solvers may still treat such a narrow gap as within
  tolerance. At this historical gate, automatic Big-M failed for some bounded nested inputs; the
  subsequent nested-bound batch below addresses the selected cases.
- Real Gurobi 10.0.1 tests explicitly fix the input by variable bounds and the result flag, then
  solve selected true, false, wrong-flag and gap instances. The 14 new EQ cases and 14 LE/GE cases
  passed with `FeasibilityTol=1e-9` and default integer tolerance. This does not test fixed-variable
  substitution or prove behavior within solver tolerances. The existing Flt64 evaluation-order
  discrepancy, arbitrary nested arithmetic, other indicator functions, other writers and other
  solver backends remain open.

#### Equality API migration

`SatisfiedAmountFunction.EqSideVars` is replaced by input-indexed `EqPositiveVars` and
`EqNegativeVars`. For each EQ, supply `(u,p,n)=(1,0,0)` for the band, `(0,1,0)` for the positive
outside branch and `(0,0,1)` for the negative outside branch. Non-EQ entries are null. Manually
assembled solution vectors or warm starts must also provide each non-null `ResidualVars` value.
Helper ordering is all count flags, all positive EQ flags, all negative EQ flags, then residuals;
EQ adds five branch rows and, for shifted/nested inputs, one residual row. Rebuild callers that
assumed the old single-side variable or four-row layout. At this historical gate
`SameAsFunction.EqSideVars` was unchanged; its later migration is described below.

中文迁移提示：`SatisfiedAmountFunction.EqSideVars` 改为按输入索引的 `EqPositiveVars` 和
`EqNegativeVars`；三个标志 `(u,p,n)` 分别用 `(1,0,0)` 表示带内、`(0,1,0)` 表示正向带外、
`(0,0,1)` 表示负向带外。等式有五条分支行，带偏移或嵌套时另有一条残差定义行；手工解向量和
warm start 须填入全部辅助变量。Big-M 仍需覆盖差值绝对值，显式值由调用方证明。连续间隔、求值
累加次序、展开舍入和 solver 容差仍有差异风险；其他共享零值指示函数没有随本批修复。

### P2 continuation: nested satisfied-count bounds (historical gate)

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `SatisfiedAmount.kt`, `BigM.kt` | `SatisfiedAmountFunction` | `SatisfiedAmountResidualTest` | Selected LE/GE/EQ nested expressions and registered ABS results infer bounds after expansion; lifecycle and numerical equivalence remain limited |
| `core-plugin-gurobi` | `GurobiLinearSolver` | `SatisfiedAmountGurobiIntegrationTest` | Ten selected nested automatic-bound cases pass on Gurobi 10.0.1; not complete backend acceptance |

- Automatic Big-M now uses the current bounds of variables in the expanded difference at row
  registration. This handles selected multi-level expressions and registered function-result
  variables with finite ranges. An unregistered function, cycle, unbounded expanded variable or
  unrepresentable bound fails before any prepared indicator rows are written. Explicit Big-M
  still requires caller proof and does not bypass expansion checks.
- This intentionally differs from the pinned Kotlin path, which infers from the original symbols'
  declared ranges and falls back to a default Big-M. The C# path does not trust an intermediate
  declaration instead of its expanded variables: a wider expansion yields a wider bound, and an
  unbounded expansion fails even if a custom intermediate declares a finite range. This can reject
  a model formerly accepted using a genuine tighter intermediate range; provide a proven explicit
  Big-M when appropriate. The change is local to `SatisfiedAmountFunction`; other Big-M callers
  have not been broadened or certified by this batch.
- Bounds are inferred when the mechanism model is built, not when the function is constructed.
  At this historical gate, the regression suite demonstrated that an LE, GE or EQ matrix dump could
  combine new variable bounds with old Big-M values. The tracked-bound gate below now rejects this
  particular change in the standard dump path; it does not detect arbitrary expression mutations.
- The checked endpoints are exact with respect to the already-expanded rows, not a proof of
  arbitrary nested arithmetic or evaluation order. Fixed-variable substitution, concurrent edits,
  subsequent custom-writer failure, other indicator functions and solver tolerances remain separate
  limitations. The ten new native Gurobi cases use explicit input constraints, not fixed-variable
  substitution; they are not full solver acceptance.

中文迁移提示：自动 Big-M 在注册行时先展开输入，再使用展开后变量的当前范围；不能证明有限范围
时返回失败，不使用 Kotlin 的默认回退值。自定义中间符号即使声明了较窄范围，只要底层变量无界也
会失败；可在调用方确认充分性后显式指定 Big-M。机制模型建好后修改输入表达式或相关变量范围仍须
重新构建；后续批次对部分函数的已登记范围增加了失效检查，不能检测任意表达式修改。

### P2 continuation: tracked modeling-bound invalidation

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `SatisfiedAmount.kt`, `Inequality.kt`, `Abs.kt`, `Max.kt`, `Binaryzation.kt`, `BigM.kt` | `BasicMechanismModel<V>.ValidateBoundDependencies`, linear/quadratic matrix dumps, `LinearModelState<V>.RefreshAsync` | `CapturedBoundsLifecycleTest`, `SatisfiedAmountResidualTest`, `LinearModelStateFunctionTest` | Selected tracked bounds invalidate before commit and standard snapshots; coverage is not universal |
| `core-plugin-gurobi` | `GurobiLinearSolver.DumpAsync`, `GurobiQuadraticSolver.DumpAsync` | `SatisfiedAmountGurobiIntegrationTest` | Selected stale linear/quadratic dumps return failure; no full backend acceptance |

- Automatic `SatisfiedAmountFunction` and `InequalityFunction<V>` record the ranges used after
  expansion. ABS, extrema and automatic binaryzation register their construction-time dependency
  snapshots as well, including manual registration onto a concrete mechanism model. ABS and extrema
  can depend on captured helper/result domains even with an explicit M. Explicit M for binaryzation,
  inequality and satisfied-count does not add a bound dependency; its sufficiency remains the caller's
  responsibility.
- The linear and quadratic factories check registered dependencies before committing prepared tokens;
  both standard matrix conversions and the linear-state snapshot check before and after reading.
  A detected change returns `IllegalArgument` instead of publishing a mixed-range result. A failed
  linear-state snapshot does not publish a new revision or reserve pending model-local row IDs; this
  does not promise rollback of the factory's earlier successful token preparation. Previously returned
  matrix and state snapshots retain their original values.
- Built-in variables use native range-object identity, so replacing a range fails conservatively even
  when equal or narrower, including integer changes invisible in Flt64. Restoring the original range
  object permits reuse, but rebuilding is the recommended path. For functions that captured bounds at
  construction, reconstruct the function and adapter as well. Unrelated variable changes do not
  invalidate these dependencies. Untrackable custom variables fail when their bounds are required.
- Only explicitly registered dependencies are protected. At this gate AND/OR/NOT/XOR, OneOf and
  SameAs were not yet covered; see the following continuation. Other bound-dependent functions and
  custom functions have not all been migrated. Direct reads of
  `IMechanismModel.Constraints` and custom writers can bypass the standard checks; custom writers
  must explicitly call `ValidateBoundDependencies()` and still own their failure handling. Two
  checks do not make concurrent edits safe, and unchanged declared intermediate ranges do not prove
  nested expression structure unchanged. Floating evaluation/expansion, fixed-variable substitution,
  solver tolerances and partial writes by custom writers remain separate limitations.

#### Tracked-bound migration

Check every construction, refresh and dump `Result`. Rebuild after changing a relevant bound or
expression; for construction-captured ABS, extrema and automatic binaryzation, create new function
instances. Writers that consume mechanism rows directly must validate registered dependencies and
must not modify the model while reading it. Explicit Big-M remains a caller-supplied proof obligation.

中文迁移提示：检查构建、状态刷新和矩阵转储的 Result；相关范围或表达式改变后重新构建模型。
ABS、极值和自动二值化在构造时捕获范围，须重新创建函数及适配器；等值或收窄的范围替换也会
保守失败。直接读取机制行的自定义 writer 须显式调用 `ValidateBoundDependencies()`，调用期间不得
并发修改。当前仅覆盖已登记函数，显式 Big-M 的充分性仍由调用方保证。

### P2 continuation: logical-function inputs and automatic bounds

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `And.kt`, `OneOf.kt`, `SameAs.kt`, `BigM.kt` | `AndFunction`, `OrFunction`, `NotFunction`, `XorFunction`, `OneOfFunction`, `SameAsFunction` | `LogicalBoundsLifecycleTest`, `AutomaticIndicatorBoundsTest`, `LogicalBoundsGurobiIntegrationTest` | Selected input snapshots, expanded bounds and lifecycle checks verified; indicator arithmetic and other semantics remain incomplete |

- These six functions now copy the direct monomial lists at construction, including both sides of
  `SameAs` inequalities while retaining their comparison and name metadata. The symbols themselves
  remain references: mutation of nested expressions is not detected. AND/OR expose their cached
  binary inputs through read-only collections; the direct binary fast paths of AND/OR/NOT stay
  independent of Big-M bounds. Other exposed helper-variable collections are not covered by this
  input-snapshot guarantee and must not be mutated by callers.
- Non-binary paths expand the registered input before inferring M, track the expanded variables'
  ranges for automatic M, and validate snapshots before and after writing. Missing variables,
  unregistered function symbols, cyclic inputs and references to the function's own helpers are
  rejected before writing the prepared rows. Explicit M still does not prove or track its own
  sufficiency. Equal or narrower replacement range objects also invalidate automatic dependencies.
- The later constraint-write transaction registers dependencies after the post-write check and
  rolls them back with the rows on failure. A failed rollback requires discarding or rebuilding the
  model; range changes themselves are not undone. Custom model implementations do not automatically
  gain durable dependency tracking.
- Continuous AND previously copied Kotlin's `sum(u) >= n*y` plus `y <= u[i]`, which admits `y=0`
  when all inputs are true. Its sum row is now `sum(u)-y <= n-1`, retaining `y <= u[i]`; the
  selected two-input truth table and native solver endpoints exercise this intentional correction
  relative to the fixed Kotlin baseline. The row name and count remain unchanged, but old cached
  matrices and warm starts must be rebuilt or revalidated.
- At this gate, `SameAs` had separate result defects: constraint mode linked its result to the first
  satisfaction flag, and measurement mode rejected more than one differing flag. The two diagnostic
  failures were fixed in the following continuation; comparison indicator endpoints, general floating
  arithmetic, solver tolerances and the shared nonzero/zero row encoding were not fixed here.

#### Logical-function migration

Build functions from the intended direct input terms; later list edits no longer update them. Rebuild
the mechanism model after a tracked range change or any failed manual registration, and rebuild cached
AND matrices after the sum-row correction. Explicit M, nested expression mutation, direct/custom writers
and concurrent model changes remain the caller's responsibility; check every `Result`.

中文迁移提示：构造函数时会复制直接单项式列表；后续修改原列表不再改变函数。自动 M 使用注册时
展开后的变量范围，相关范围被替换（包括等值或收窄）后应重建机制模型；显式 M 的充分性仍由调用方
保证。连续 AND 的合计行已修正，缓存矩阵和 warm start 需要重建或重新校验。手动注册失败可能留下
部分行及依赖，须重建后再试；自定义模型、嵌套表达式修改、并发操作和 SameAs 既有语义缺口不在本批保证范围内。

### P2 continuation: SameAs result encoding

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `SameAs.kt`, `BigM.kt` | `SameAsFunction` | `SameAsResultEncodingTest`, `SameAsResultGurobiIntegrationTest` | Historical gate: selected Boolean result encoding verified; see the following endpoint continuation |

- The pinned Kotlin baseline and the old C# rows linked constraint-mode `y` to the first satisfaction
  flag even though all-false is an all-equal state. They also used `y + sum(diff) = 1`, which rejects
  multiple differences. C# now deliberately differs: constraint mode enforces all flags equal and
  `y = 1`; measurement mode retains `diff[i] = u[0] XOR u[i]` and adds `y + diff[i] <= 1` for every
  difference together with `y + sum(diff) >= 1`. One input fixes `y = 1` in both modes.
- Evaluation now reads every inequality before returning an all-equal result, like the pinned Kotlin
  code; a missing later input returns null even after earlier flags differ. The prior result diagnostics
  now pass. The new tests enumerate all input, satisfaction, difference and result bit assignments for
  one through four LE inequalities with values -1 or 1; they do not prove general comparison arithmetic.
- A separate diagnostic at this gate demonstrated four failures: at exactly `+epsilon` for LE, `-epsilon`
  for GE, and `+/-epsilon` for EQ, evaluation treats the inequality as satisfied while the registered
  rows also permit its false flag. This may admit an incorrect SameAs result; these failures are kept
  in ignored `logs/p2-sameas-boundary-diagnostic/` and were not counted as passing tests at this gate.
  The following continuation implements an explicit continuous gap and passes those diagnostics.
  LT/GT/NE registration remains unsupported. Floating rounding, nested expansion, explicit M sufficiency,
  solver feasibility/integer tolerances and writer rollback remain outside this result-encoding proof.

#### SameAs migration

Rebuild cached matrices and revalidate warm starts. The existing `*_result_link` row now fixes the result
to 1 instead of copying the first flag; `*_result_sum` changes from equality to a lower bound, and every
measurement difference adds a `*_result_diff_i` upper-bound row. Row count and the LP relaxation change;
helper variables and their order did not change in this result-only batch; the following endpoint batch
does change them. Callers using partial evaluation must now supply all inputs to obtain 0 after differing
flags. These changes fix defects in the pinned Kotlin baseline rather than claiming byte-for-byte row parity.

中文迁移提示：需重建缓存矩阵并重新校验 warm start。约束模式的 `*_result_link` 现在将结果固定为 1；
度量模式的 `*_result_sum` 改为下界，并为每个差异变量新增 `*_result_diff_i` 上界行。行数与 LP 松弛
有所变化，本结果批次辅助变量及其顺序不变，但后续端点批次再次调整了它们。部分求值即使已发现不同状态，
缺失后续输入时也返回 null。上述结果编码有意修正固定 Kotlin 基线；端点修正及剩余限制见下一节。

### P2 continuation: SameAs comparison boundaries

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `SameAs.kt`, `BigM.kt` | `SameAsFunction` | `SameAsBoundarySemanticsTest`, `SameAsBoundaryRowsTest`, `SameAsBoundaryGurobiIntegrationTest` | Selected LE/GE/EQ endpoints, open gaps, residual rows and native solves verified; strict relation registration, complete floating semantics and generic API pending |

- Let `d = lhs - rhs`, `e = epsilon`, and `s = StrictBoundary`. LE is satisfied at `d <= e` and
  violated at `d >= s`; GE uses the mirrored boundaries `-e` and `-s`; EQ is satisfied at
  `abs(d) <= e` and violated at `abs(d) >= s`. Values in the intervening open gaps evaluate to null
  and have no feasible binary indicator assignment. `e` must be positive, finite and usable; `s` must also be
  usable and strictly greater than `e`. The default `s` is `2 * e`; an overflow or invalid explicit
  value makes evaluation return null and registration return failure.
- This is an intentional departure from the pinned Kotlin baseline, which evaluates a Boolean for each
  available finite difference and registers with `strictBoundary = epsilon`; coincident continuous endpoints can admit both
  indicator states. C# uses one-hot satisfied/violated flags for LE/GE, and one-hot satisfied/positive/
  negative flags for EQ, keeping active thresholds on the right-hand side. Shifted or nested inputs use
  an auxiliary residual and definition row. Automatic Big-M uses the expanded variable bounds; explicit
  Big-M must cover the absolute input difference and remains the caller's responsibility. The relaxed
  `M + s` is rounded upward, and unsafe constant/row coefficient conversions or missing tokens fail
  before prepared rows are written. Mid-write rollback now requires the later transaction contract.
- The historical four endpoint diagnostics now pass; the open-gap evaluation regression originally
  failed four of 14 cases. The selected exact arithmetic tests enumerate assignments at satisfied,
  violated and gap points for direct, shifted and nested inputs, with explicit and inferred M, in both
  mechanism rows and the ordinary matrix. They also cover adjacent/subnormal boundaries with large M,
  directed rounding at domain endpoints, missing later flags or residuals, inexact coefficient and
  constant merges, and overflow. Floating evaluation order and nested expansion can still differ from
  exact row arithmetic; fixed-variable substitution, arbitrary writers, concurrent modification and
  solver tolerances have not been fully proven.
- Explicit opt-in Gurobi 10.0.1 tests add 84 selected endpoint/gap solves to the earlier 82 cases.
  Inputs and flags are fixed by equality rows, not fixed-column substitution; successful solves check
  auxiliary branch values. `FeasibilityTol=1e-9` and integer tolerance remains at its default. Adjacent
  and subnormal cases were checked only by exact row/matrix arithmetic, not real-solver acceptance.
  These 166 cases do not constitute full Gurobi or other-backend acceptance.

#### SameAs boundary migration

`SameAsFunction.EqSideVars` is replaced by input-indexed `EqPositiveVars` and `EqNegativeVars`.
For EQ, `(satisfied, positive, negative)` is `(1,0,0)` inside the band, `(0,1,0)` on the positive
outside branch and `(0,0,1)` on the negative outside branch. LE/GE use the new input-indexed
`ViolationFlags`, complementary to `SatisfactionFlags`. Nonmatching entries are null. Non-null
`ResidualVars` must equal the expanded input difference. Helper order is result, satisfaction flags,
measurement differences, violation flags, EQ positive flags, EQ negative flags, then residuals.
LE/GE now use three branch rows; EQ uses five, plus a residual row for shifted/nested inputs. Rebuild
cached matrices and revalidate manual solution vectors and warm starts. Consumers must explicitly
handle null evaluation or infeasibility in the open gaps and select a boundary wide enough for the
solver's numerical tolerances.

中文迁移提示：`EqSideVars` 改为按输入索引的 `EqPositiveVars`、`EqNegativeVars`；等式带内、正向带外、
负向带外的三个标志分别为 `(1,0,0)`、`(0,1,0)`、`(0,0,1)`。LE/GE 新增与满足标志互补的
`ViolationFlags`；偏移或嵌套输入使用 `ResidualVars`。辅助变量顺序为结果、满足标志、差异、违反标志、
等式正向标志、等式负向标志、残差；缓存矩阵须重建，手工解向量和 warm start 须重新校验。默认违反边界
为 `2 * epsilon`，可显式指定严格大于 epsilon 的边界；中间开区间求值为 null，模型无可行分支。调用方
须选择适合求解器容差的间隔并保证显式 Big-M 足够；浮点求值、嵌套展开及固定变量代换仍未全面验证，
写行回滚受下述事务契约限制，LT/GT/NE 注册不支持。

### P2 continuation: generic quadratic product

- Kotlin `Product.kt` describes the product of two linear polynomials as a quadratic intermediate
  expression. `ProductFunction<V>.Create(left, right, converter, ...)` replaces the former parameterless
  Flt64 linear-function placeholder. It returns a `Result`, snapshots the direct monomial lists and
  expands their coefficients in `V`; it adds neither a result variable nor a product constraint.
  A symbol input must be a variable or an `ILinearIntermediateSymbol<V>`. Invalid inputs, non-finite
  values, selected integer/rational overflow and multiplication underflow are rejected rather than
  silently emitted. `Polynomial` and direct input lists are read-only snapshots; `AsMutable()` returns
  an independent copy of the expanded expression, rather than Kotlin's empty mutable polynomial.
- **Migration:** check `Create` before using its value, add the product itself to the
  `QuadraticMetaModel<V>`, and refer to it in a quadratic monomial for objectives or constraints.
  Register any dependent function adapters separately. Using `product.Polynomial` directly bypasses
  the product's registration check; do not rely on it to preserve function lifecycles. This is a
  breaking replacement of the old no-argument `ProductFunction`, which had no working behavior.
- `IQuadraticMathFunctionSymbol<V>` declares its direct input symbols and supplies checked evaluation
  for registration. Preparation validates declared inputs, orders dependencies, rejects undeclared
  reads and propagates `Failed`/`Fatal`, with the existing token/cache rollback. `TryEvaluate(values)`
  expects values for the direct input symbols; `Evaluate(values, tokenTable, converter, ...)` can
  instead resolve nested intermediates and fall back to token values. Public `Prepare` and
  `PrepareAndCache` follow the Kotlin empty-map/token fallback and fixed-map cache distinction;
  registration deliberately uses the checked prepared-input path so an unrelated fixed map does not
  reuse stale token values. A partial fixed-map cache can still depend on the current token results;
  callers must invalidate or recompute it when those results change. `Flush` forwards to dependencies.
- A nested symbol object is not deep-copied. Rebuild the mechanism model after changing a nested
  expression; already-built rows and objectives remain snapshots. Cycles and missing registrations
  are rejected during model construction, but direct evaluation or flush of a manually created cyclic
  graph is not guarded. Arithmetic inside an arbitrary nested intermediate is governed by that
  intermediate's own implementation, not the product's checked evaluation.
- Product evaluation computes each input first, while model lowering consumes the expanded quadratic
  coefficients. Floating-point rounding, lost additions and nested-expression expansion can therefore
  differ even when all values are finite. The product rejects a nonzero direct input multiplication
  that underflows to zero, but this is not an exact floating-point equivalence proof. `RtnX` remains
  backed by doubles; the selected representation checks do not make it arbitrary precision. Solver
  conversion, fixed-variable substitution and concurrent mutation still need separate acceptance.
- Checked evaluation processes the left input before the right and each input in monomial order. An
  overflow reached before a missing value fails; a missing value stops evaluation without checking
  later terms. A shared direct symbol is resolved once per evaluation, unlike Kotlin's per-term
  resolution, so stateful or concurrently changing intermediate values are not equivalent. The
  shared checked-polynomial evaluator also rejects a null value map rather than treating it as a
  missing symbol or returning a constant.

中文迁移提示：旧无参线性占位类型已移除，改用 `ProductFunction<V>.Create(left, right, converter)`，
检查 `Result` 后将乘积符号及其依赖的函数适配器分别加入二次元模型，再用该符号构造目标或约束。
乘积不生成辅助变量或额外约束；输入单项式列表是浅快照，嵌套符号修改后需重建模型。`TryEvaluate`
读取直接输入值，普通 `Evaluate` 可递归求值并回退 token；手工部分固定值缓存可能依赖 token 状态，
其变化后应失效或重新计算。浮点展开与逐步求值并不保证逐位一致，嵌套数值和并发修改仍需另行验证。
受检求值按先左后右、各自单项式顺序处理：先出现的溢出返回失败，先缺值则停止且不检查后续项。
共享直接符号一次求值只解析一次，与 Kotlin 逐项解析不同；不能依赖有状态或并发变化的中间值。
共享的受检多项式求值也会将 null 值映射视为失败，而非缺失符号或常量求值。

### P2 continuation: bounded integer product

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `IntegerProduct.kt`, `LinearizedFunctionScenariosTest.kt` | `IntegerProductFunction<V>.Create`, `LinearFunctionSymbolAdapter<V>` | `IntegerProductFunctionTest`, `IntegerProductGurobiIntegrationTest` | Representative generic evaluation, rows, lifecycle and selected Gurobi solves verified; full numeric equivalence and other backends remain unverified |

- A bounded integer `x` in `[L,U]` is represented by `L + sum(2^i * bit[i])` with a width row;
  four mask rows per bit encode the product of that bit and the bounded input. `Create` returns a
  `Result`, reads native integer bounds including open endpoints, checks exact integer conversion
  and rejects domains or widths beyond `2^53`, unrepresentable weights, unusable input bounds and
  selected overflow/underflow. The generic row tests cover Flt64, Flt32, Int64, FltX and RtnX.
  Built-in variable ranges and recognized numeric ranges of derived variable types are supported;
  arbitrary `IVariableItem` implementations without a trackable native range are rejected.
- Both integer endpoints must be representable by `V`. For `Flt32`, any non-singleton domain
  extending outside `[-2^24,2^24]` is rejected because it contains integers that cannot be represented
  individually, even if its endpoints are representable. An exactly representable singleton outside
  that interval is allowed. This is a conservative C# restriction rather than Kotlin parity.
- Unlike Kotlin, C# adds an `InputVar` and a definition row so the expanded input is not repeatedly
  merged into the masks. The helper order is result, input, all bits, all masks; for `n` bits it
  normally generates `4*n + 6` rows. The exact maximum width `2^53` adds a high-bit exclusion row:
  a floating accumulation can round `2^53 + 1` back to `2^53`. This changes auxiliary layouts,
  row names/counts and LP relaxations relative to Kotlin; do not reuse Kotlin warm starts by index.
- An explicit input range is enforced by rows, even if narrower than the underlying variable range;
  it is not a claim that every original input remains feasible. Otherwise bounds are inferred from
  the expanded variables. The integer range and inferred input ranges are captured before converter
  calls and rechecked before creation succeeds. Replacing any tracked range, even with an equal or
  narrower range, requires a new function and model; with explicit input bounds, the input variables'
  ranges are not dependencies. Standard matrix dumps reject tracked stale ranges, but `TryEvaluate`
  uses the captured domain and does not itself check live ranges.
- Direct input monomial lists are shallow snapshots. Registration checks the expanded input structure,
  rejects missing tokens or unregistered nested functions, and compares row-merge coefficients and
  constants using exact arithmetic before writing. A previously built model does not detect arbitrary
  later changes to nested expression structure. Caller-owned converter behavior, concurrent mutation,
  helper-variable edits and arbitrary callback side effects remain outside the transaction guarantee.
  The shared writer now rolls back rows and bound dependencies on a detected failure when the model
  supports the transaction contract described below; a failed rollback requires rebuilding the model.
- Evaluation returns `Ok(null)` for missing values. Invalid, fractional, out-of-domain or overflowed
  inputs return `Failed` from `TryEvaluate` and null from `Evaluate`; Kotlin returns null for these cases.
  Numeric and solver conversion can round; selected merge and nonzero-underflow checks are not proof
  of equivalence between nested expansion, sequential floating evaluation, fixed substitution and a
  solver's tolerances. The large-width safeguard does not guarantee a solver can handle large scales.

#### Integer-product migration

Create the function with `IntegerProductFunction<V>.Create(integer, input, converter, inputBounds: ...)`,
check the result, register the integer and all input variables and dependent function adapters, then
register a `LinearFunctionSymbolAdapter<V>` for the new function on the same `LinearMetaModel<V>`.
Use the adapter in objectives or constraints and use the same numeric converter as the model. Recreate
the function, adapter and model after tracked bounds or nested structure change. Do not modify the
exposed helper variables during construction or solving.

中文迁移提示：使用 `IntegerProductFunction<V>.Create` 并检查 `Result`，将整数变量、输入变量、
依赖函数适配器和整数乘积适配器注册到同一 `LinearMetaModel<V>`。C# 比 Kotlin 多一个输入辅助变量
及其定义行，辅助变量、行布局和 LP 松弛不同；不得按旧索引复用 warm start。显式输入界会真正收紧
模型；自动界及整数界被替换后须重新创建函数和模型。整数域两端须可由 V 精确表示；Flt32 非单点域
不得越过 ±2^24，域外可精确表示的单点仍可使用。构造时会检测代表性的转换器范围副作用，注册
预检合并系数与常量；共享写行失败按下述事务契约回滚，但不保证并发安全、外部回调副作用或浮点逐位等价。`TryEvaluate` 对越界或非法值
返回失败，缺值返回 `Ok(null)`；`Evaluate` 两者均返回 null。仅选定 Gurobi 实例和一个固定列代换
通过，不能据此宣称其他求解器或极端数值已验收。

### P2 continuation: scalar conditional classification and unsupported placeholders

At fixed Kotlin baseline `12fdb587`, `Conditional.kt` and `DiscreteCondition.kt` provide a
three-valued classifier and validate the relationship between the discrete step and strict gap.
The C# `ConditionClassification.Classify` and `ClassifyDiscrete` now implement the four GT/GE/LT/LE
truth tables, including the open undefined gap, and `ValidateDiscreteParameters` checks
`0 < strictBoundary <= delta`. Invalid or non-encodable values and unsupported relations return
`Failed`, not `Undefined`. The discrete step is validated but does not change the scalar truth table.
For a difference `d` and positive gap `g`, the true/false boundaries are GT: `d >= g` / `d <= 0`,
GE: `d >= 0` / `d <= -g`, LT: `d <= -g` / `d >= 0`, and LE: `d <= 0` / `d >= g`; values
between the respective boundaries are `Undefined`.

The classifier compares the exact native representations of Flt32, Flt64, FltX, RtnX and the built-in
signed/unsigned integers, including the numerator and denominator stored by `RtnX`, instead of rounding
both operands to `double` first. It rejects `URtn8`, unsupported custom numeric types, malformed or
underflowing `RtnX` values and inputs whose
natural Flt64 conversion is non-finite, zero after underflow, or a full-range sentinel. This comparison
can distinguish values that collapse to the same solver value; it **does not** establish that a solver
can encode them distinctly. Custom converter behavior is not checked. There is no polynomial lattice
proof, bounded-condition validation, strict-positive conversion, constraint generation, or native
indicator dispatch in this batch; the existing range-driven `ConditionSupport` is a separate internal
Flt64 implementation and is not certified by these scalar tests.

At this historical batch, `IfFunction`, `IfInFunction`, `IfThenFunction` and `ImplyFunction` were placeholders, but their
`TryEvaluate`, `RegisterAuxiliaryTokens` and `RegisterConstraints` now return a bilingual failure with
`ErrorCode.Other`. `LinearMetaModel.Add` propagates that failure and rolls back the attempted symbol.
Their nullable `Evaluate` still returns null. An unregistered `LinearFunctionSymbolAdapter` may still
expose its fallback zero polynomial or return zero through its token-based evaluation overloads; do
not use these placeholders or bypass registration. Other placeholder functions have not been hardened.

#### Conditional migration

Use `ConditionClassification.Classify(difference, relation, strictBoundary)` for scalar continuous
classification, or `ConditionClassification.ClassifyDiscrete(difference, relation, delta,
strictBoundary)` when a discrete step must also be validated. Check `Result.IsOk` before reading
`Value`; `TruthValue.Undefined` is a successful gap classification, not a false result. Supply a
business gap appropriate for the domain and solver tolerances. In that batch, previously accepted
registration or checked evaluation of the four empty conditional placeholders became an explicit
failure. The following `If`, `IfIn`, `IfThen` and `Imply` batches supersede their placeholders for limited supported
cases only.

中文迁移提示：标量条件可使用 `ConditionClassification.Classify`，离散步长也需校验时使用
`ClassifyDiscrete`；先检查 `Result`，再区分 `True`、`False` 与 `Undefined`。按原数值比较不代表
转换为求解器数值后仍可区分；该接口本身不提供格点证明或模型约束。当时四个条件函数仍是占位，
之后的 `If`、`IfIn`、`IfThen` 和 `Imply` 批次已实现下述受限子集。不可通过未注册适配器
的零多项式绕过失败；其他占位函数尚未统一修复。

### P2 continuation: bounded generic `If`

Against `If.kt`, `Conditional.kt` and `DiscreteCondition.kt` at the fixed Kotlin baseline,
`IfFunction<V>.Create(condition, converter, strictBoundary, ...)` supports GT/GE/LT/LE. The initial
batch handled direct variables and single-result adapters; the following nested-expression batch
extends this to exactly expandable linear intermediate expressions. EQ/NE, unsupported numeric
types, non-finite or non-exact solver coefficients and unproven ranges fail with `IllegalArgument`;
they are not claimed as supported. The pinned Kotlin `Conditional.kt` classifier and
`DiscreteCondition.kt` lowering also reject EQ/NE; their absence is not a C#-only capability gap.
The factory validates `0 < strictBoundary <= delta`, defaulting
`delta` to the gap. A non-fixed continuous condition requires `delta == strictBoundary`; a discrete
condition requires integer combined coefficients and proves the relevant nearest lattice point using
their exact GCD and offset. A range entirely on one valid branch can avoid the lattice proof.
This is a conservative subset: a finite discrete domain might be safe even when its infinite lattice
cannot be proven safe by this check.

The linearization uses a result bit, a false-branch bit and a continuous condition-value variable,
with six rows: definition, lower and upper condition bounds, one-hot, true branch and false branch.
Active thresholds are not expressed by cancellation against a large relaxation; relaxation values
are rounded upward and must remain exactly solver-encodable. Both inferred and explicit condition
bounds are **enforced by rows**, unlike Kotlin's caller-supplied range assumption. An explicit range
that is too narrow therefore changes the feasible domain; `Evaluate` and `TryEvaluate` classify the
supplied value without checking that range. Missing inputs or a gap return `Ok(null)` from
`TryEvaluate`; invalid arithmetic returns a failure. The nullable `Evaluate` returns null for both.
Input and result monomial lists are read-only shallow snapshots, not deep copies of referenced symbols.

Auto-inferred variable ranges are captured at factory time and checked at registration and standard
linear/quadratic matrix dumps. Any replacement, including an equal or narrower range, requires a new
function and adapter or restoring the original range object. Explicit ranges are enforced instead of
tracking underlying variable ranges. Missing helper tokens, unregistered nested functions, stale
bounds and inexact merged coefficients fail before rows are written. Meta-model registration rolls
back a failed symbol addition; shared row insertion requires a supported transaction and rolls back
detected failures as described below.
Direct model access or custom writers can bypass standard stale-bound checks, nested symbol mutation
after model construction is not tracked, and concurrent mutation is not supported.

#### `If` migration

The parameterless non-generic placeholder `new IfFunction()` has been removed. Create the typed
function through `IfFunction<V>.Create`, inspect its `Result`, register a
`LinearFunctionSymbolAdapter<V>` in the meta-model, and use that adapter as the result expression.
Specify the gap explicitly and provide a `LinearPolynomialBounds<V>` when finite input bounds cannot
be inferred. `IfFunction<V>.From` now also accepts a normalized `LinearConstraintInput<V>` and returns
a checked adapter, as described below. There is no legacy `bigM`/`tolerance` compatibility parameter.
The helper layout and six rows differ from Kotlin, so rebuild cached matrices
and revalidate indexed warm starts. Flt64/Flt32/Int64/FltX/RtnX representative cases are tested, but
all stored coefficients, constants, thresholds and bounds must be exact in the converter's Flt64
encoding; for example, RtnX `1/10` is rejected as a coefficient. The subsequent direct-input
classification check rejects selected runtime discrepancies, but does not prove nested or solver
arithmetic equivalent to exact matrix arithmetic. Select a gap appropriate for the solver's
feasibility and integrality tolerances.

中文迁移提示：原无参 `IfFunction` 占位已移除。通过 `IfFunction<V>.Create` 传入条件多项式、
转换器和严格间隔，先检查 Result，再将 `LinearFunctionSymbolAdapter<V>` 注册到元模型；约束输入
也可用下述 `IfFunction<V>.From`。当前支持可精确展开的线性中间表达式及函数结果；不支持 EQ/NE
及旧 `bigM`/`tolerance` 参数。
自动推导范围在创建时捕获，范围对象替换后须重建函数与适配器；显式范围会
实际收紧模型，而求值不检查该范围。非固定连续条件要求 `delta == strictBoundary`；离散条件使用
合并整数系数的最大公约数和偏移证明，不能证明时保守失败。新编码含两个二值变量、一个连续变量
及六行，缓存矩阵与按索引排列的 warm start 需重建或重验。只读快照不深拷贝引用对象；共享写行
按下述事务契约处理失败，并发修改、回调副作用和建成后的嵌套结构修改仍无原子或失效保证。

### P2 continuation: nested `If` expressions

An acyclic `ILinearIntermediateSymbol<V>` can now contain multiple terms, constants and other
intermediate symbols, including registered function adapters. `If` compares the source expansion
with the actual flattened coefficients and constant using exact rational arithmetic, and rejects
unrepresentable source values or a changed result caused by flattening/merging roundoff. The proof
is iterative and handles shared subexpressions; it does not prove bitwise equivalence to sequential
floating-point evaluation. Direct `TryEvaluate` still needs values for its direct input symbols;
fixed-value model preparation resolves registered and hidden dependencies through the normal token
lifecycle. Underlying function adapters must be registered separately.

At registration `If` repeats the exact-expansion check, including when a nested mutation is hidden
by floating-point rounding. Auto-inferred bounds track the flattened variables; range replacement
invalidates standard linear and quadratic dumps. Recoverable converter exceptions during creation
or revalidation and writer exceptions during row registration return a failed `Result`; partial rows
are rolled back only under the transaction contract described below.
Nested structure changes after a model is built
are still not tracked by the dump, and direct/custom writers or concurrent mutation remain outside
the guarantee. Explicit bounds remain enforced rather than inferred. Function-result arithmetic,
solver tolerances and runtime evaluation order still require care; no blanket numerical-equivalence
or performance claim is made.

中文说明：`If` 的条件现在可包含无环的多层线性中间表达式和已注册的函数适配器，包括多项
结果及偏移。创建和注册时以精确有理数核对展开后的合并系数及常量；浮点展开丢失有效信息时
保守失败。转换器在创建或重新校验时及 writer 写行时抛出可恢复异常会返回失败；写行事务的范围见下文。
直接求值仍需传入直接依赖符号的值，固定值构建则使用已有依赖准备流程。模型创建后
修改嵌套结构仍不会使标准转储自动失效；顺序浮点求值与矩阵运算也不保证逐位等价。

### P2 continuation: `If` constraint inputs and exact normalization

The fixed Kotlin `If.kt` constraint-input factory is available as `IfFunction<V>.From`. It returns a
`Result<LinearFunctionSymbolAdapter<V>, ...>` and uses the input's normalized `FlattenData` and `Sign`;
it does not infer a condition bound from `LhsRange` or reinterpret `RhsConstant`, nor does it adopt the
input's name. Pass a function name, gap, optional discrete step and optional condition bounds explicitly.
As with `Create`, explicit bounds are enforced on the model and the supported relations are GT/GE/LT/LE.
The resulting adapter must be registered in the meta-model. Direct term lists are snapshotted; manually
supplied nested function symbols retain their dependencies and must be registered separately.

`LinearConstraintInput<V>.From` now compares the final normalized coefficients and constant against
an iterative exact-rational expansion of both source sides, before publishing the input. This checks
native numeric values without first converting them to Flt64, preserving representable decimal cases;
rounded or unprovable expansion returns `IllegalArgument`. Flt64, Flt32, Int64, FltX and RtnX are
exercised. URtn8 and unknown numeric types that `ExactBoundValue` cannot read are conservatively
rejected, which is a breaking change for callers previously relying on unchecked normalization.
The factory still rejects nested function symbols because its flattened output cannot retain their
registration lifecycle. Manually constructed or externally obtained `LinearConstraintInput` values
can already have lost information; the `If` factory cannot reconstruct or verify their original sides.
The source lists and nested symbols must not be mutated concurrently. Neither check proves sequential
floating-point evaluation is bitwise equivalent to exact algebra or solver arithmetic.

中文迁移提示：`IfFunction<V>.From` 接收已归一化的约束输入并返回 Result 包装的适配器，
检查成功后将适配器注册到元模型。它使用 `FlattenData` 和 `Sign`，不把 `LhsRange`、
`RhsConstant` 或输入名称自动套用到条件；函数名及显式范围由调用方提供，显式范围会约束模型。
`LinearConstraintInput<V>.From` 现在按原生数值精确核对左右两侧展开后的系数与常量，不可证明时
返回失败，URtn8 和未支持的自定义数值类型会保守拒绝；先前依赖不精确合并的调用需调整输入。
该工厂不能保留函数符号生命周期，仍拒绝函数输入；手工构造的约束输入无法追溯此前丢失的精度，
调用方须确认来源。浮点逐项求值与矩阵运算、并发修改和模型建成后的嵌套结构修改仍不保证等价。

### P2 continuation: bounded generic `IfIn`

Against fixed `IfIn.kt`, `Conditional.kt` and `DiscreteCondition.kt`,
`IfInFunction<V>.Create(x, lower, upper, converter, strictBoundary, ...)` replaces the old
non-generic placeholder. Both endpoints belong to the true branch. `Classify` returns `True`,
`False` or `Undefined`: the open regions within `strictBoundary` outside either endpoint are
undefined; missing or invalid inputs return failure. `TryEvaluate` returns `Ok(null)` for missing
inputs or undefined results and failure for invalid arithmetic; `Evaluate` returns null in either case.

The implementation composes the existing checked `If` lower/upper relations with three AND rows.
It always uses seven helper variables and 15 rows, including when a side is fixed, unlike Kotlin's
three helpers and folding/native-indicator alternatives. `LowerCondition` and `UpperCondition` expose
the two result flags, false flags and difference variables; the upper difference is `x - upper`.
All side rows and link rows are preflighted before the first row is written. Meta-model registration
rolls back a failed symbol addition; shared row writes require the transaction contract below. Custom
token collections must implement the separate batch contract; Core cannot verify their behavior.
Direct calls and custom writers do not inherit all standard model protections; concurrent mutation
remains unsupported.

The two endpoint shifts and, if supplied, the four shifted input bounds must be exactly representable
both in V and in the solver encoding. Each side separately applies `If`'s finite-bound and discrete
lattice checks, so this is a conservative subset of the Kotlin API. A supplied `bounds` constrains x
in the model rather than merely serving as an assumption; evaluation does not check that range.
Inferred variable bounds are captured at creation and checked on registration and standard matrix
dumps. Replacing a range invalidates them, while nested structure changes after building the model
are not automatically detected. Input terms and result terms are shallow read-only snapshots;
nested function adapters must be independently registered. Legacy `bigM` and `tolerance` arguments
are not provided, and native indicators are not implemented.

**Numerical limitation:** direct-input checks reject the previously observed `x = 1e16 + v`,
`lower = upper = 1e16`, `v = 1` discrepancy. Cache preparation additionally checks the expanded
model expressions when their variable values are available, and otherwise leaves the result uncached.
Direct evaluation cannot verify the provenance of an intermediate value; solver tolerances,
residual representability and other function types remain open. Do not use evaluation as a general
proof of model feasibility.

#### `IfIn` migration

Replace `new IfInFunction()` with a checked `IfInFunction<V>.Create`, then register a
`LinearFunctionSymbolAdapter<V>` and use the adapter as the result expression. Pass finite endpoints,
an explicit positive gap and, if inference is impossible, enforced `LinearPolynomialBounds<V>` for x.
Check the `Result` before accessing the function, and handle `Undefined` separately from false.
Rebuild cached matrices and manual warm starts for the seven-helper layout.

中文迁移提示：原无参 `IfInFunction` 占位已移除。通过 `IfInFunction<V>.Create` 提供输入、闭区间、
转换器及严格间隔；检查 Result 后注册 `LinearFunctionSymbolAdapter<V>`。端点为真、区间外的间隔
为不可判定；`Classify` 缺值返回失败，而 `TryEvaluate` 缺值返回 `Ok(null)`。显式范围会实际收紧
模型，两侧差值和范围必须精确可表示，格点无法证明时保守失败。新编码使用七个辅助变量及十五行，
须重建矩阵并复核 warm start；准备缓存会核对可取得的展开变量值，证据不足时不缓存；直接求值
无法验证中间值的来源，不能把求值当作可行性证明。自定义写入器须实现下述事务契约；模型创建后的
嵌套结构修改仍不保证自动失效。

### P2 continuation: bounded generic `IfThen`

Against fixed `IfThen.kt` and `ConditionalValue.kt`, `IfThenFunction<V>.Create` returns the true-branch
polynomial when a GT/GE/LT/LE condition holds and zero when false. This is not the implication
constraint implemented by Kotlin's separate `ImplyFunction`. `Classify` fails for missing condition
inputs and returns `Undefined` in the business gap. `TryEvaluate` returns `Ok(null)` for missing
inputs or the gap, and short-circuits the then expression when the condition is false. Invalid
arithmetic and unsupported or inexact encoding return `IllegalArgument`; `Evaluate` cannot distinguish
failure from a missing or undefined result.

The implementation composes the existing bounded `If` and binary `IntegerProduct` encodings: seven
helper variables and 16 linear rows, including fixed-condition cases. Their order is the condition's
true flag, false flag and value, followed by the product's result, input, bit and masked value.
Kotlin's native indicator/folding layout is different. Both sets of rows are preflighted before
writing; automatic bounds are tracked at creation and on standard matrix dumps. Direct input terms
are shallow read-only snapshots, but nested symbols are not deep-copied. `conditionBounds` and
`thenBounds` are enforced even when the condition is false; an explicit then bound can therefore
shrink the feasible region compared with Kotlin's fixed-false folding. Evaluation does not check
either bound. Custom token collections must enforce their batch contract; shared row writes use the
transaction contract below. External callback effects remain outside the built-in transaction;
model-built nested structure changes and concurrent mutation are not automatically invalidated.

Creation inherits `If`'s conservative finite-bound, exact-encoding and lattice requirements; the
then expression and its bounds must also be exactly representable at the solver boundary. When true,
the sequential then value and its encoding must respectively equal the exact native and encoded
expression results, so even a
rounding difference that a solver tolerance could accept is rejected. Model-aware preparation also
checks the saved expanded then expression, and leaves the value uncached if expanded evidence is
missing. A proven mismatch returns failure and rolls back the registration cache. Only the existing
restricted ABS proof can derive a nested auxiliary value; direct evaluation still cannot establish
intermediate provenance or model feasibility, and false-branch evaluation does not inspect then input.

#### `IfThen` migration

Replace `new IfThenFunction()` with `IfThenFunction<V>.Create(condition, then, converter,
strictBoundary, ...)`, inspect the `Result`, and register a `LinearFunctionSymbolAdapter<V>`.
`IfThenFunction<V>.From` accepts a normalized constraint input and defaults the then polynomial to
one; it uses the input's flattened data and sign, not its range or name metadata. Rebuild saved matrices
and warm starts for the new helper layout. Legacy `bigM`/`tolerance`, native indicators, automatic
constant folding and unrestricted callback rollback are not implemented.

中文迁移提示：无参 `IfThenFunction` 占位已移除。通过 `IfThenFunction<V>.Create` 提供条件、
真分支表达式、转换器及严格间隔，检查 Result 后注册线性函数适配器；`From` 接收归一化约束，
默认真分支为一。真假间隔返回 `Undefined`，假分支可在 then 缺值时返回零。真分支的逐项值须与
原生精确值及求解器编码值一致；舍入不同也可能被保守拒绝。固定值准备额外核对可取得的展开值，
无证据时不缓存，不能将直接求值当作模型可行性证明。显式条件范围和 then 范围均实际约束模型，
即使条件为假也是如此；因此可能比 Kotlin 固定假分支折叠更严格。当前使用七个辅助变量、十六行，
须重建矩阵并重验 warm start；共享写行按下述契约处理失败，自定义 token 集合、回调副作用、并发修改
及模型建成后的嵌套结构更改仍不保证完整回滚或失效。后续批次另行实现受限的逻辑蕴含 `Imply`。

### P2 continuation: bounded generic `Imply`

Against fixed `Imply.kt`, `ImpliedCondition.kt` and `FunctionSymbol.kt`,
`ImplyFunction<V>.Create` enforces that the consequent is true whenever the antecedent is true.
The antecedent is classified even when the consequent is irrelevant; its false branch short-circuits
the consequent, and its gap remains undefined. When the antecedent is true, a false or gap consequent
makes the model infeasible. A false antecedent does not require the consequent to be classified,
so a consequent inside the gap remains feasible. `Classify` fails when a required input is missing;
`TryEvaluate` returns `Ok(null)` for missing inputs or gaps and returns zero for a false implication.
Both operands and their finite bounds are validated at creation, including with a fixed-false antecedent.

This is a hard constraint, not a free Boolean-valued implication: every feasible model has result one.
The C# adapter therefore exposes constant one for objectives and nested expressions, while standalone
evaluation may return zero for an infeasible assignment. Fixed-value preparation never caches that
zero as the model's constant result. At the pinned Kotlin baseline, `ImplyFunction` does not expose
a result polynomial or `resultVar`, and its legacy adapter falls back to zero; consumers must not
assume the two adapters' result polynomials match. Explicitly register the C# adapter before using
it in an objective or another expression; direct use of an unregistered adapter is not validation.

The current lowering reuses the checked antecedent `If` and gates only the consequent's true row,
with four helper variables and ten rows, including fixed branches. It requires the `If` finite-bound,
exact solver encoding and lattice proofs for both operands, even if the antecedent is false. Bounds
on both condition values are enforced in every branch; in particular, explicit consequent bounds
may shrink Kotlin's fixed-false region. Input lists are shallow read-only snapshots, inferred bounds
are tracked, and model-aware preparation verifies available expanded inputs only on evaluated branches.
Missing proof leaves the result uncached; direct evaluation does not prove model feasibility or nested
provenance. Native indicator/deferred structures, unrestricted callback rollback, arbitrary token-batch
atomicity, concurrent mutation and post-build nested-structure invalidation remain incomplete.

#### `Imply` migration

Replace the no-argument placeholder with `ImplyFunction<V>.Create(antecedent, consequent, converter,
strictBoundary, ...)`, check its `Result`, and register a `LinearFunctionSymbolAdapter<V>` as a hard
constraint or a separately registered symbol. Rebuild matrices and revalidate warm starts for the new
helper layout. The old `bigM`/`tolerance` compatibility parameters and Kotlin's indicator-variable
layout are not implemented.

中文迁移提示：原无参占位已移除。使用 `ImplyFunction<V>.Create` 提供前后件、转换器、正间隔及
可选范围，检查 Result 后注册适配器。前件为假或未定义时不读取后件；前件为真时后件为假或处于
间隔会使模型不可行。该函数是强制成立的约束，可行模型中的结果为一；单独求值可以返回零，
因此不要把单独求值当作可行性证明。固定 Kotlin 的旧适配器因缺少结果契约而回退为零，C# 则明确
使用常量一。两侧范围在所有分支强制执行，并继承 If 的数值和格点限制；即使前件为假也可能因
显式后件范围收紧模型。当前编码为四个辅助变量和十行，须重建矩阵并重验 warm start；未注册的
适配器、任意令牌集合、外部回调副作用、并发修改和建成后的嵌套结构修改不保证完整回滚或失效。

### P2 continuation: constraint-write transactions

The fixed Kotlin `BigM.kt` uses constraint-count rollback and snapshots mutable token lists. C# now
uses `IAbstractLinearMechanismModel<V>.BeginConstraintWrite()` and `IConstraintWriteTransaction`
instead: `Commit` ends the transaction, while a failed commit must still permit `Rollback`. The
default interface method returns a failure before any rows are written. Custom writers that opt in
must restore every piece of their own write-mutated state, including mutable tokens and caches, and
must keep a failed commit rollbackable. A writer's implementation is trusted, not audited by Core.

Built-in linear and quadratic models checkpoint their actual row collection and tracked-bound list.
Linear rows added to a quadratic model are promoted to quadratic rows, so the quadratic collection
is the rollback target. Built-in mechanism models use structurally immutable token-list snapshots;
they do not auto-materialize tokens during writes. Under the no-concurrency/no-reentrancy contract,
the shared indicator and rounding writers also cover ABS, extremum, comparison, bounded conditional
and integer-product functions. They restore the original rows and dependencies after a failed row,
enumeration exception, post-write bound check or failed commit. If rollback fails or throws, a
`Fatal` contains the original errors followed by the rollback errors; the model may still be dirty
and must be discarded or rebuilt. Existing rows retain their identity. Detected bound changes are
not themselves undone; a fresh function may be needed before retrying.

This is not a general transaction over user-owned variables, arbitrary converter or callback side
effects, token result values or caches changed by such callbacks, concurrent/reentrant edits, or
post-build nested-structure mutation. Built-in writers require callbacks not to mutate the model or
tokens. It also does not make auxiliary-token batch registration atomic. Custom model callers that
previously relied on partial writes must implement the contract or handle the new pre-write failure.
This statement concerns row-write transactions only; the later token-registration transaction has
its own separate contract and does not create a single global transaction across both operations.

中文迁移提示：共享函数写行现在调用 `BeginConstraintWrite`；自定义机制模型默认不支持事务，
因此会在写入首行前返回失败。要接入该路径，须实现 `IConstraintWriteTransaction` 并恢复写入
影响的全部自身状态（包括可变 token 和缓存），且提交失败后仍能回滚。内置模型回滚新增行及
边界依赖；二次模型使用提升后的二次行集合。回滚同时失败时返回包含原始错误和回滚错误的
`Fatal`，此时模型可能已损坏，应丢弃或重建。变量范围等外部副作用不会恢复；任意回调、并发或
重入修改及辅助 token 批量原子性仍不在本批保证范围内。

### P2 continuation: token batches and registration transactions

Against the fixed `TokenList.kt`, `TokenListAtomicityTest.kt` and token registration paths,
`AutoTokenList<V>`, `ManualTokenList<V>`, `ConcurrentAutoTokenTable<V>` and
`ConcurrentManualTokenTable<V>` validate a complete variable batch before publishing it. Iterator,
key, null-item and checked index failures leave the original token mapping and next index unchanged.
A write failure rolls back added keys; a failed rollback returns `Fatal`
with the original and rollback errors, so the collection must be discarded or rebuilt. The mutable
table's token list locks its batch during validation and publication; standalone lists are not
thread-safe. The iterator must not mutate or reenter the target collection. `IAddableTokenCollection`
declares this batch contract, but Core cannot verify third-party implementations.

Built-in mutable tables snapshot their token mapping and table-owner refresh callbacks, the
next index, symbol collection and all table cache mappings for `BeginTokenRegistration()`. The
single and batch symbol additions, `AbstractMetaModel<V>.Add` and both normal and hidden-dependency
registration use this transaction. Failed/throwing operations or commits trigger rollback; a failed
or throwing rollback returns `Fatal` in original-error/rollback-error order. A custom symbol collection
can itself fail during restoration; after `Fatal`, do not reuse the table. A closed built-in table
rejects symbol additions. Token identity and other snapshots' callbacks are preserved, but the
snapshot is shallow: shared token result values, variable/symbol objects, externally owned state and
in-place edits to cached objects are not rolled back. In particular, if a callback changes token
values, flush affected table caches before using them again. Closing, concurrent or reentrant
mutation during registration is unsupported; a failed rollback after closure can leave a closed or
partially changed table. Table-level registration is not an isolated transaction for observers of
the exposed `Symbols` collection, and it is not combined atomically with mechanism-row writes.

Custom `IAbstractMutableTokenTable<V>` implementations must implement `BeginTokenRegistration` and
`ITokenRegistrationTransaction` before using these registration paths. The default fails before
mutation. An implementation must restore its own registration-mutated state and permit rollback
after failed commit; Core trusts the implementation rather than auditing it. Built-in registration
snapshots all existing tokens, symbols and cache mappings, including for a direct single-symbol add;
repeated individual additions can therefore have quadratic total snapshot cost. The dependent-symbol
loop reuses its enclosing transaction. Variable batches materialize their input before writing.
No performance or concurrent-registration claim is made.

At this gate C# retained idempotent duplicate handling and manual solver indices still came from
`item.Index`. The following manual-index batch supersedes the latter; the duplicate policy still
differs from Kotlin and token-list parity is not yet claimed.

中文迁移提示：内置变量批量添加先完整验证再发布，重复键保留原 token；枚举器不得重入或修改
目标集合。自定义 `IAddableTokenCollection` 必须自行满足批量契约。内置表的符号注册和元模型
添加现在保存 token 身份、表所属回调、下一索引、符号和全部表缓存映射；自定义表默认不支持，
须实现 `BeginTokenRegistration` 与 `ITokenRegistrationTransaction`，且提交失败后仍可回滚。
回滚失败返回包含原错误和回滚错误的 `Fatal`，须丢弃或重建表。事务是浅快照，不恢复共享 token
值、外部副作用或缓存对象的原地修改；若回调改变 token 值，使用前须刷新受影响表的缓存。不支持
事务期间关闭、并发或重入修改，也不保证公开符号集合的观察隔离或与写行的联合原子性。单符号
添加会复制已有状态，重复添加可能产生二次复杂度，尚无性能测量。

该批保留了 C# 的重复键幂等语义，与 Kotlin 可配置的拒绝或替换不同；当时手动索引也来自变量自身，
该索引差异已由下一个批次修复。重复策略仍待迁移，不能将当前 token 能力标为完全对齐。

### P2 continuation: sequential manual token indices

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `TokenList.kt`, `MechanismModel.kt` | `ManualTokenList<V>`, `ConcurrentManualTokenTable<V>`, `BeginTokenRegistration`, mechanism factories | `TokenBatchAtomicityTest`, `TokenRegistrationTransactionTest`, `MechanismModelPreparationTest`, linear/quadratic dump tests, `TokenOrderingGurobiIntegrationTest` | Manual and automatic registration indices, selected rollback and matrix/solver ordering tested; see the later duplicate-policy gate |

Built-in manual lists and tables now assign `SolverIndex` in registration order instead of reading
the variable's `Index`, matching the fixed Kotlin manual-list behavior for newly registered keys.
Both manual and automatic collections retain gaps after removal. `TokensInSolver` sorts by the
assigned index, while `IndexOf` and solution vectors use compact positions in that order; `Find(int)`
also retains its existing out-of-range fallback to an exact `SolverIndex`. Checked index exhaustion
returns a failure without partially publishing the batch. Standalone lists are not thread-safe;
concurrent table additions are serialized, but registration transactions and callbacks retain their
previously documented no-concurrency/no-reentrancy limits.

For the built-in tables, registration rollback restores the next index even after a deletion gap.
Mechanism preparation copies that counter, checks the parent counter before commit and adopts the
working counter on success. A parent add-then-remove during preparation is therefore detected even
if its token mapping looks unchanged. Existing snapshot sharing and callback side-effect limits still
apply. At this earlier gate duplicate keys remained idempotent; the later duplicate-policy gate
wires `MetaModelConfiguration.CheckTokenExists` and handles auxiliary-token re-registration.

**Migration:** Manual `SolverIndex` is no longer `IVariableItem.Index`; register variables in the
intended order, and use `IndexOf` or the current matrix variable `Index` for vector positions rather
than a potentially sparse `SolverIndex`. Rebuild old manual-table snapshots and cached matrices, and
revalidate or reconstruct indexed solutions and warm starts; persisted indices are not portable
across this change. Mapping values by variable identity is safer than assuming former column order.
This does not change the separately documented fixed-column substitution or shared-result semantics.

中文迁移提示：手动列表和表现在按注册顺序递增分配 `SolverIndex`，删除后不复用空洞；向量和矩阵
列位置使用排序后的紧凑位置，可通过 `IndexOf` 或矩阵变量的 `Index` 获取，不应直接使用可能不连续
的 `SolverIndex`。`Find(int)` 保留越界时按 `SolverIndex` 查找的既有回退。请重建旧手动表快照和缓存
矩阵，并重建或复核按索引排列的解与 warm start；按变量身份映射更安全。内置事务回滚和机制模型
构建已保存、检查并提交手动表的下一索引；回调副作用、共享结果及并发注册仍有原先限制。当时重复键
仍幂等，后续重复策略批次已接入 `CheckTokenExists` 并处理辅助变量重注册。

### P2 continuation: duplicate token policy and auxiliary registration

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `TokenList.kt`, `TokenTableRegistrationSupport.kt`, `MechanismModel.kt`, `MetaModel.kt` | `AutoTokenList<V>`, `ManualTokenList<V>`, `ConcurrentAutoTokenTable<V>`, `ConcurrentManualTokenTable<V>`, `MetaModelConfiguration.CheckTokenExists` | `TokenBatchAtomicityTest`, `TokenRegistrationTransactionTest`, `MechanismModelPreparationTest`, linear/quadratic dump tests, `TokenOrderingGurobiIntegrationTest` | Duplicate reject/replace, selected atomic rollback, mechanism re-registration and selected real Gurobi solves verified; transaction isolation and full token parity remain incomplete |

All four built-in collections default to `checkTokenExists: false`. Each occurrence of a duplicate
key creates a fresh token at the next index and replaces the old token, including repeated keys in
one batch. `checkTokenExists: true` rejects an existing or repeated key with `TokenExisted` before
publishing the batch. `MetaModelConfiguration.CheckTokenExists` now reaches the token table and
the mechanism-build working copy. A failed iterator, checked index overflow or failed write does
not consume indices; built-in rollback restores the prior token identity and table-owned callback.
A failed rollback returns `Fatal`, and the affected collection must be discarded or rebuilt.

Old immutable snapshots retain their original token and its value, while the replacing table
detaches its own callback and owns the new, initially unassigned token. Successful replacement
clears the mutable table's value caches, including explicit-solution and fixed-value entries;
flatten and range caches and independent snapshots are not cleared. This is a deliberate safety
difference: the fixed Kotlin token-table `add` delegates to the list without flushing table-level
value caches. Directly changing a token's result, external callback effects and in-place changes to
cached objects are still outside the registration transaction and may require an explicit `Flush`.

Mechanism preparation skips both pre-existing and newly registered auxiliary keys during the
individual auxiliary-registration calls, then restores the ordinary policy before constraint
registration. Unlike Kotlin's add-only wrapper, C# retains the working table identity for existing
custom functions that inspect or capture it; the scope does not make arbitrary reentrant mutation
safe. Parent or working-token replacement during construction is still checked before commit.
Standalone symbol registration does not use the mechanism-only skip policy. The transaction remains
a shallow snapshot; concurrent/reentrant mutations and arbitrary external side effects are not
isolated, and full performance and custom collection behavior are not verified.

**Migration:** Previous C# releases treated duplicate variable addition as an idempotent no-op.
Do not re-add a variable just to ensure it exists: look it up first, or set
`new MetaModelConfiguration(CheckTokenExists: true)` and handle `TokenExisted`. With the default
policy a repeat discards that table's old result and changes solver column order. Existing snapshots
still reference the old token. Rebuild affected mechanism models, matrices and warm starts, and map
stored solutions by variable identity rather than reusing old column indices. Standalone lists and
tables accept `checkTokenExists` in their constructors; custom collections must implement their own
atomic semantics. The `env` system property used by Kotlin is not read by the .NET default.

中文迁移提示：旧 C# 的重复变量添加是幂等操作；现在四种内置列表和表默认每次重复都替换 token、
丢弃该表的旧结果并消耗新索引，同一批内重复也一样。设置 `CheckTokenExists: true` 可让已有键或批内
重复键以 `TokenExisted` 失败，整批不发布。请先查询再添加，或显式处理该错误；受影响的机制模型、
矩阵和 warm start 应重建，旧快照仍持有原 token。替换成功会清除可变表的值缓存，但不会刷新其他快照、
展平或范围缓存；这比固定 Kotlin 的表级缓存处理更保守。直接修改 token 结果及外部回调副作用仍需由
调用方管理。机制构建只在辅助变量注册期间跳过已知键，随后恢复普通策略；自定义集合、重入和并发
事务仍有既有限制，不能据此宣称 token 或整个 P2 已完全对齐。

### P2 continuation: revision identity and guarded solution writeback

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `LinearModelState.kt`, `TokenList.kt`, solver column-generation and Benders paths | `LinearModelRevision`, `LinearModelState<V>`, `MechanismModelSolutionExtensions.ApplySolverSolution` | `TokenRevisionAndSnapshotTest`, `MechanismModelSolutionTest`, `SolutionSnapshotGurobiIntegrationTest` | Cross-numeric-type owner collision fixed; selected snapshot and solution-writeback contracts tested; complete structural invalidation and backend acceptance remain open |

`LinearModelRevision` owners now use a non-generic, process-local counter. Previously separate
`LinearModelState<V>` static counters could give two different numeric types the same owner and
sequence, allowing a foreign revision to be accepted. Like the Kotlin baseline, structural
comparison uses ordered variables and bounds, rows, objective and direction, but not token object
identity or solution values. Replacing a middle token can reorder columns and advance the revision;
replacing the last or only token may leave it unchanged. Changes made and reversed between refreshes
are not observed. A revision is not a token-identity, solution, warm-start or feasibility proof and
is not a persistent or cross-process identity.

Old mechanism snapshots retain replaced tokens and share unchanged token objects with the parent;
they do not provide independent values for all variables. Already-created linear and quadratic
matrices retain their original column order and initial values. Rebuild a matrix and revalidate its
warm start after relevant source changes; a structural revision alone does not make an old result
safe to write into a current token table.

`ApplySolverSolution` is a new C# safety boundary for complete Flt64 vectors produced in a live
linear or quadratic mechanism model's full column order. It checks the exact length, finite values,
source and parent lifetimes, ordered token **reference** identity and registered modeling-bound
dependencies before writing. It flushes the source and parent table caches on success. A changed,
removed or replaced token is rejected even if its key or the structural revision is unchanged;
short or long vectors are rejected rather than partially written. This two-argument overload does not reconstruct eliminated
fixed columns; the later three-argument overload performs explicit checked restoration. Neither validates values against variable domains, establishes feasibility or optimality, or
detect every bound, constraint, objective or symbol change. Keep the entire model and its owned
tables unchanged and alive from matrix dump through writeback. Concurrent or reentrant modification
is unsupported: checking and writing are not an atomic operation. If a callback throws during the
write, `Fatal` reports that values may be partially changed; discard or rebuild the affected model.
Other snapshots sharing tokens do not have their table caches flushed automatically.

The existing Gurobi, Gurobi11, SCIP, COPT, CPLEX and MindOPT column-generation and Benders feasible
writeback paths, and the Hexaly column-generation source path, now use the guard. Benders fixed-column
indices come from the mechanism snapshot. This does not make those backends complete or validated;
in particular, this solution-writeback check does not protect no-incumbent or Farkas/dual evidence.
The later Gurobi Benders continuation adds a separate, limited token-snapshot check for its Farkas
path. Other callers, including custom solvers and the existing public `SetSolution` /
`SetSolverSolution` list overloads, remain unchecked and can still partially write or misapply old
vectors. Use `ApplySolverSolution` with the originating, live mechanism and handle `Failed`/`Fatal`;
do not dispose its owned token table separately.

中文迁移提示：跨数值类型的线性模型现在共用进程内会话编号，避免错误接受其他模型的版本。版本只
反映刷新时观察到的结构和列顺序，不包含 token 身份或解；替换末列也可能保持相同版本。新方法
`ApplySolverSolution` 对完整求解器向量检查长度、有限值、表中 token 引用与顺序，以及已登记的建模
边界；随后刷新来源和父表缓存并回填。已有求解器包装的可行解路径已接入，但普通 `SetSolution` /
`SetSolverSolution`、自定义求解器及无 incumbent/Farkas 证据仍不受此回填检查保护；后续批次仅为
Gurobi Benders 的 Farkas 路径增加独立的有限 token 快照检查。方法不验证所有模型修改、
变量域或可行性；完整向量重载不支持固定列消元后的短向量，后续三参数重载可显式恢复。方法不支持并发或重入修改；从转储到回填应保持模型及自有表存活
且不变。写入回调异常返回 `Fatal`，模型可能部分改变，须丢弃或重建；其他共享快照缓存需自行刷新。
真实后端能力与完整结构失效检测仍待后续阶段验收。

### P2 continuation: fixed-column restoration and concurrent identifiers

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `LinearTriadDumpBuilders.kt`, `QuadraticTetradDumpBuilders.kt`, `MechanismModel.kt` | `ToLinearTriadModel`, `ToQuadraticTetradModel`, `RestoreSolverSolution`, `ApplySolverSolution(model, matrix, solution)` | `FixedVariableSolutionTest`, `FixedVariableSolutionGurobiIntegrationTest`, `BendersFixedColumnsGurobiIntegrationTest` | Original matrix mapping and selected real solves verified; only Gurobi Benders integrates it automatically so far; complete structural validation remains a gap |
| `AbstractVariableItem.kt`, variable and symbol constructors | `IdentifierGenerator.Gen` and its existing callers | `IdentifierGeneratorConcurrencyTest` | Atomic in-process allocation tested; differs deliberately from the fixed Kotlin baseline's non-thread-safe counter |

Direct linear and quadratic matrix dumps now retain a private snapshot of fixed values, free-column
positions, source identity and ordered token identities. `RestoreSolverSolution` reconstructs a full
Flt64 vector without writing it; the three-argument `ApplySolverSolution` restores and then invokes
the existing guarded writeback. The mapping accepts only the original matrix produced directly by the
same mechanism model: manually constructed or copied matrices have no mapping. The original column
references, token identities, source and parent lifetimes, vector length and finite values, and
registered modeling-bound dependencies are checked. Mutating the caller's fixed-value dictionary
after dumping does not alter the snapshot. The mapping holds identity markers rather than tokens;
the garbage-collection regression covers built-in scalar variables, not arbitrary custom objects.

This is a C# safety facility, not a general automatic fixed-variable solving pipeline. `DumpAsync` does not
take the map, and neither general `FeasibleSolverOutput` nor solution-pool entries are rewritten automatically.
The later Gurobi Benders subproblem path is a limited exception described below.
After confirming that a solver actually returned an incumbent, explicitly dump with the fixed map,
solve that original matrix, and pass each reduced solution vector to `RestoreSolverSolution` or
`ApplySolverSolution`. Check `Failed`/`Fatal` and dispose successful models according to their existing
ownership rules. Factory-time fixed values prepare symbols separately and do not eliminate columns.
Do not treat restored values as proof of feasibility or optimality.

The check does not validate every constraint, objective, ordinary variable range or symbol change,
matrix row mutation or matrix disposal. Keep the source, parent and matrix unchanged and alive from
dump through solve and writeback. Direct setters and custom solver paths are not protected; except for
the later Gurobi Benders token check, no-incumbent/Farkas/dual evidence is not protected by this API.
Concurrent or reentrant mutation is unsupported. A callback exception
can leave partial writes, so a `Fatal` requires discarding or rebuilding the model. Other snapshots
sharing tokens still require their own cache management.

`IdentifierGenerator` now uses an atomic unsigned counter shared by its existing variable,
combination and symbol callers. This avoids duplicate keys during ordinary concurrent allocation,
unlike Kotlin's fixed baseline; it does not make the model or token table generally thread-safe.
The identifiers remain process-local, not persistent or cross-process identities. The unused internal
reset operation and eventual unsigned wraparound can reuse identifiers; do not reset while objects
using earlier identifiers remain alive.

中文迁移提示：使用旧的分步入口时，固定列必须分别传入机制模型工厂（符号准备）与直接矩阵转换（消元）；后续新增的联合构建入口会共用一份快照。确认存在
incumbent 后，对原矩阵的每个缩短解调用 `RestoreSolverSolution`，或调用三参数
`ApplySolverSolution` 还原并回填。不要把短向量直接交给普通 setter；一般自动求解入口和解池不会自动
还原，后续 Gurobi Benders 子问题是局部例外。调用方须保持来源模型、父模型和矩阵存活且不变，并处理 Result；这些检查不证明可行性或最优性。
标识分配改为原子操作，但不保证整个模型线程安全，也不能作为持久化身份。

### P2 continuation: Gurobi Benders fixed columns and evidence boundary

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `GurobiBendersDecompositionSolver.kt`, `LinearTriadDumpBuilders.kt` | `GurobiBendersDecompositionSolver.SolveSubAsync`, `RestoreSolverSolution` | `BendersFixedColumnsGurobiIntegrationTest`, `FixedVariableSolutionTest` | Selected Gurobi 10.0.1 cases tested; fixed-column solve integration is limited to this path, cuts and complete backend acceptance remain open |
| `GurobiBendersDecompositionSolver.kt`, solver failure callbacks | `ValidateSolverTokenSnapshot`, Gurobi linear/quadratic `AfterFailure` | `BendersFixedColumnsGurobiIntegrationTest`, `CapturedBoundsLifecycleTest` | Selected callback failures and a limited no-incumbent snapshot check tested; evidence validity and complete structural invalidation remain open |

The Gurobi linear Benders subproblem snapshots the supplied fixed-value map before registration,
uses it both for symbol preparation and fixed-column elimination, relaxes the remaining columns to
LP, and restores the complete solution in mechanism-token order before guarded writeback. Unknown,
non-finite or out-of-domain fixed values fail before the native solve. The restore mapping checks
source and original column identities both before and after reading the input vector; this does not
make concurrent or reentrant model mutation supported. `AfterFailure` failures and fatals now
propagate in both the Gurobi linear and quadratic solvers rather than being silently ignored.

On infeasibility, the subproblem reports a result only after Gurobi provides nonempty, finite,
nonzero Farkas row multipliers and the original source, columns, ordered tokens, lifetimes and
registered bounds pass `ValidateSolverTokenSnapshot`. Multiplier dictionaries use constraint
reference identity. This check does not verify arbitrary row, objective, matrix or symbol changes,
and the returned row multipliers are not an independently checked certificate including variable
bounds. Ordinary dual evidence is not independently validated, and cuts are still `null`. Keep the
model unchanged throughout the operation; other backends and direct/custom paths do not acquire
these guarantees. A successful selected solve is not full Gurobi backend acceptance.

At this historical gate, construction and dumping were separate transactions: an unknown or
out-of-domain fixed variable could be rejected only after committing a delayed helper token. The
following build-and-dump continuation fixes this specific gap for Gurobi Benders and the new joint
Core APIs, but not for the old two-step path or failures after a successful dump. A blanket outer
rollback would also risk undoing legitimate parent-table changes made by callbacks.

中文迁移提示：Gurobi 线性 Benders 子问题现在会捕获固定值、消元、松弛自由列，并在回填前恢复完整
列向量。失败回调的 `Failed`/`Fatal` 会传播。无 incumbent 时仅在取得非空有限 Farkas 行乘子并通过
来源、列、token、生命周期及已登记边界检查后返回不可行结果；这不是包含变量界的独立证明，也不
校验完整结构，割仍未生成。当时机制构建和矩阵转换不是同一事务；后续联合构建批次已修复 Gurobi
Benders 转储失败时辅助 token 提前提交的问题。旧的分步入口及成功转储后的求解失败仍不保证完整
回滚；其他后端、解池和自定义路径尚未自动获得这些保障。

### P2 continuation: atomic mechanism construction and matrix dump

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `MechanismModel.kt`, `LinearTriadDumpBuilders.kt`, `QuadraticTetradDumpBuilders.kt` | `BuildLinearTriadModelAsync`, `BuildQuadraticTetradModelAsync` | `MechanismModelPreparationTest`, `BendersFixedColumnsGurobiIntegrationTest` | Joint pre-commit dump and selected Gurobi 10.0.1 paths tested; this is a C# safety facility, not general solver-transaction or backend acceptance |

The new Flt64 joint build methods take one fixed-value snapshot before registration, prepare the
mechanism, and complete and validate the matrix dump before committing delayed helper tokens to the
parent. A failed or fatal preparation, invalid fixed values, failed dump, observed cancellation or
parent token conflict returns a failure without publishing helpers from the working snapshot. This
does not undo helpers or other changes that a callback itself made to the parent. A failed commit disposes
the prepared matrix; on success the caller owns and must separately dispose both returned objects.
The matrix retains the original fixed-column mapping for explicit restoration. Fixed delayed helpers
remain valid inputs. Cancellation is cooperative: a request arriving after the last check may not
prevent commit. Concurrent or reentrant model mutation is unsupported; external callback effects
are not reverted, and parent changes made by callbacks are not blindly rolled back.

The existing `InvokeAsync` plus separate dump remains a two-step operation. Gurobi's linear Benders
subproblem now uses the joint method, but commits before the native solve; a subsequent solver or
callback failure can still leave helpers in the parent. Other solvers and general `DumpAsync` have
not been migrated. Do not treat any failed solve as proof that the parent is unchanged; inspect or
rebuild as appropriate. Snapshot validation is still not a full structural or evidence proof.

中文迁移提示：线性和二次 Flt64 元模型可分别使用 `BuildLinearTriadModelAsync`、
`BuildQuadraticTetradModelAsync`，它们在辅助 token 提交前完成矩阵转换，共用固定值快照；成功时
模型和矩阵须分别释放。检查点观察到取消、准备或转储失败时不会从工作快照提交辅助 token，父表发生
冲突时也不会覆盖回调所作更改；回调自行向父表添加的 token 不会撤销。取消在最后一次检查后发生时
仍可能提交。外部回调副作用不恢复，也不支持并发
或重入修改。旧分步入口仍是两个事务；Gurobi Benders 在成功转储后再求解，后续原生求解或回调失败
仍可能保留已提交 token，其他后端尚未接入，不能据此声称全局失败原子性。

### P2 continuation: direct source-structure invalidation (earlier batch)

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `MechanismModel.kt`, `LinearTriadDumpBuilders.kt`, `QuadraticTetradDumpBuilders.kt` | `MechanismModelSourceSnapshot`, direct dumps, `ValidateSolverTokenSnapshot`, `RestoreSolverSolution`, `ApplySolverSolution` | `MechanismModelStructureSafetyTest`, `CapturedBoundsLifecycleTest`, `MechanismModelPreparationTest`, `SolutionSnapshotGurobiIntegrationTest`, `BendersFixedColumnsGurobiIntegrationTest` | Direct source structures and dump-time variable definitions are guarded; this conservative C# safety check is not complete structural or evidence validation |

Linear and quadratic mechanism construction records parent constraint, objective and symbol
references and the direct monomial references in parent rows and objectives. It checks this snapshot
around registration and before helper-token commit. A callback's own parent changes are retained,
but delayed helpers from the working copy are not committed after a detected conflict. After a
successful build, a direct dump rejects detected parent-structure changes. A variable definition
snapshot is captured before each dump and checked again before publication, including eliminated
fixed variables. Matrix restore and writeback check that dump's variable definitions and the source
structure before and after reading a solution. The complete-vector overload without a matrix instead
checks the prepared mechanism's variable definitions.

For built-in variables the original typed range object is retained and compared by reference, so
changes hidden by Flt64 rounding are rejected. An equivalent replacement also invalidates the old
snapshot. Custom variables expose only Flt64 bounds, which are compared by value. A range change
that is unrelated to registered function-bound dependencies can be used by a newly dumped matrix;
the old matrix remains invalid until the original range object is restored. A change to a registered
bound dependency requires rebuilding the mechanism. These checks are snapshot comparisons, not
monotonic revision numbers: restoring the same references can make a snapshot appear valid again.

At that stage the guard did not observe arbitrary nested or custom symbol internals, independent edits to
mechanism rows or matrix rows/objectives, every token mutation during a dump, matrix disposal, or
mutation followed by restoration. It does not establish solution feasibility, dual validity, or a
complete independent Farkas certificate. Concurrent and reentrant mutation remain unsupported;
checking and writeback are not atomic, and callback side effects are not undone. Existing direct
setters and custom solver paths remain unchecked. The selected Gurobi Benders Farkas path now also
rejects the tested direct source changes; other native evidence paths are not thereby accepted.

中文迁移提示：构建时若注册或提交前回调修改来源约束、目标、符号或变量定义，受检构建会失败且不
提交工作副本中的延迟辅助 token，但保留回调自己对父模型的修改。已有机制模型检测到来源直接结构
改变后须重建；与函数登记边界无关的变量范围改变后可以重新转储，但旧矩阵不能回填或用于受检证据。
完整向量重载仍按机制模型准备时的范围检查，使用新矩阵求解时应使用三参数重载。内置变量的等价新
范围对象也会保守失效；自定义变量只有 Flt64 边界可比较。检查不涵盖所有嵌套符号、机制行和矩阵行
修改，也不能识别先改变再恢复原引用的历史；不支持并发或重入修改，不证明可行性、对偶或 Farkas。

### P2 continuation: mechanism-row and matrix snapshot safety

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `MechanismModel.kt`, `LinearTriadModel.kt`, `QuadraticTetradModel.kt` | Direct matrix dumps, `ValidateSolverTokenSnapshot`, `RestoreSolverSolution`, `ApplySolverSolution`, selected Gurobi and Hexaly wrappers | `MatrixSnapshotSafetyTest`, `SolutionSnapshotGurobiIntegrationTest` | Direct mechanism-row and original-matrix snapshots tested; not a complete structure version or independent proof |

Linear and quadratic dumps capture the mechanism row and cell identities, cell coefficients and
token identities, and check them again before publishing the matrix. The row snapshot keeps weak
row and cell references; with the tested built-in variables, retaining a matrix does not keep its
source mechanism, parent, token or rows alive. A successful post-build row registration invalidates an earlier matrix, while a fresh
dump uses the new rows. Transaction rollback restores the previous matching snapshot; a matrix
dumped from the rolled-back rows becomes invalid.

Restore, matrix-aware writeback and the no-incumbent check also compare the original matrix's row
view and its separately stored sparse representation, signs, right-hand sides, names, sources,
objective and disposal state. Numeric comparisons retain the exact double bits, including signed
zero. Both matrix and row checks run before and after reading the solver vector; detected changes
return a failure before writeback. Disposing a shared constraint batch also invalidates the matrix.
At that stage these were point-in-time comparisons, not monotonic revisions: changing content and
restoring the same references and values could make a snapshot match again. The later sparse-matrix
revision described below closes that particular gap, not the remaining non-sparse gaps.

Gurobi column-generation MILP, solution-pool and LP paths and the Benders master now retain the
original matrix through writeback; Benders subproblem feasible writeback also checks that matrix,
and its no-incumbent Farkas path uses the same guard. The Hexaly wrapper source is connected to the
matrix-aware overload and was exercised with a fake solver only, not the actual SDK. Other backend
wrappers and the two-argument complete-vector overload do not gain matrix validation from this
change. Callers using a directly dumped matrix should pass that original matrix to the three-argument
overload and dispose it only after result extraction.

These checks do not cover arbitrary nested symbols, native solver-model modifications, non-sparse
mutation followed by restoration, or all custom getters and callback side effects. Concurrent and reentrant
mutation remain unsupported; validation and writeback are not atomic. The checks do not establish
solution feasibility, dual validity or a complete Farkas certificate. Snapshots add storage and
scans proportional to the represented rows and terms; no performance improvement is claimed.

中文迁移提示：机制模型在转储后增加或替换约束，或原矩阵的行视图、稀疏行、方向、右端、名称、
来源、目标内容或释放状态发生变化时，旧矩阵不能再用于受检回填或无解快照检查；正常回滚后原有行
快照可再次匹配，回滚掉的行所生成的矩阵失效。新结构可重新转储。Gurobi 已接入所列路径，Hexaly
仅经假后端验证；其他后端及两参数完整向量入口不自动检查矩阵。当时检查是快照比较而非单调版本号；
下文的稀疏矩阵版本仅解决稀疏集合的修改后还原，仍不支持并发/重入或证明可行性、对偶及完整 Farkas 证据。

### P2 continuation: sparse matrix ownership and revisions

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `SparseMatrix.kt`, `LinearTriadModel.kt`, `QuadraticTetradModel.kt`, `LinearModelState.kt` | `SparseVector`, `SparseMatrix`, `SparseQuadraticVector`, `SparseQuadraticMatrix`, matrix-aware solution checks | `SparseMatrixOwnershipTest`, `MatrixSnapshotSafetyTest`, `SparseSnapshotGurobiIntegrationTest` | Sparse list ownership and restored-change invalidation tested; not a complete model revision |

Sparse vector and matrix constructors now accept `IEnumerable<T>` and copy the supplied sequence. A matrix deliberately retains
the vector objects in its copied row list: mutating a shared vector affects every matrix that owns
that vector. Each exposed row and entry collection is a tracked `IList<T>`; successful insert,
replacement, removal and clear operations invalidate its revision even when the original contents
are subsequently restored. An attempted removal that does not change the collection does not
invalidate it. As in the Kotlin baseline, `Add` retains zero coefficients, including signed zero,
and therefore invalidates the revision. Assigning the same item or clearing an empty
collection may conservatively invalidate it. A matrix-aware snapshot records both the row-list and
each vector's revision, so shared-row changes invalidate every affected snapshot. As before, normal
mechanism-row transaction rollback can make the pre-transaction row snapshot valid again.

The fixed Kotlin `SparseMatrix.kt` retains its mutable constructor lists, while Kotlin's
`LinearModelState.refresh()` advances its version only when the refreshed structure differs. Copying
the input sequence and detecting intervening sparse writes are deliberate C# safety differences,
not claims that the Kotlin baseline already has the same historical change tracking. General numeric
types, sparse transpose and the nullable quadratic second index remain separate parity work.

This is a breaking API and ownership change: `Rows` and `Entries` are now `IList<T>`, not
`List<T>`, and modifying a constructor's original list no longer modifies the constructed object.
Build a list before construction or update the returned collection explicitly; replace `List<T>`-
specific operations such as `AddRange` and `Sort` with explicit edits or construct a new object.
The new constructors also accept the exposed `IList<T>` directly. To obtain independent rows,
create separate vectors rather than reusing a vector reference.

At that gate, `LinearConstraintBatch` and `QuadraticConstraintBatch` still kept separate `Lhs` and `SparseLhs`
representations; this limitation is superseded by the sparse-backed projection described later.
Revision checks protect the original directly dumped matrix only on matrix-aware paths. At this gate
they did not cover change-and-restore of mechanism rows, row views,
metadata, objective, variables or arbitrary custom objects; the next batch prevents direct writes to
several of those collections but does not detect arbitrary mutations. They are not a concurrent/reentrant mutation
protocol, a native-model change detector, or a feasibility, dual or Farkas proof. Each collection
write creates a revision identity, and each snapshot retains per-row identities; no performance
improvement is claimed.

中文迁移提示：四种稀疏向量/矩阵构造器现在接受 `IEnumerable<T>` 并复制传入序列；矩阵浅复制行列表，仍共享传入的向量对象。
`Rows`、`Entries` 从 `List<T>` 改为 `IList<T>`；`AddRange`、`Sort` 等须改为逐项操作或重新构造。
通过集合完成写入即使随后还原，也会使受检旧矩阵失效；同一向量共享给多个矩阵时都会失效。
未发生写入的删除不失效；按 Kotlin 基线，零系数（包括负零）`Add` 现在保留条目并使旧快照失效；相同值
赋值及空集合清除可能保守失效。Kotlin 固定基线会保留构造列表别名，且线性结构版本仅在刷新时发现差异
才递增，C# 的输入复制和稀疏写入检测是明确的安全差异，不声称 Kotlin 已具备同样的历史追踪。通用数值类型、
稀疏转置及二次项第二索引可空仍待同步。失败回滚机制行的原有语义不变。在当时的门禁中，约束批次的 `Lhs` 与 `SparseLhs`
仍不互相同步；这一限制已由后续稀疏行投影批次取代。该批不覆盖非稀疏字段的修改后还原；下一批禁止对其中部分
集合直接写入，但仍不检测任意修改，也不覆盖自定义对象内部状态、并发/重入或完整证明，其他后端仍需分别验收。

### P2 continuation: matrix collection ownership

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `ModelView.kt`, `LinearTriadModel.kt`, `QuadraticTetradModel.kt` | `ModelConstraint`, `Objective<C>`, `BasicLinearTriadModel`, `BasicQuadraticTetradModel` | `MatrixCollectionOwnershipTest`, `MatrixSnapshotSafetyTest`, `FixedVariableSolutionTest`, `MatrixCollectionOwnershipGurobiIntegrationTest` | Collection isolation contract tested; shared batch ownership remains a gap; row coherence superseded by the next batch |

Linear and quadratic matrix models now take a shallow copy of their input variable sequence and
expose a read-only collection. `Objective<C>` likewise copies the cell sequence and exposes it as
read-only; `Copy()` still shares the individual cells. Constraint metadata was already copied at
construction, but its signs, RHS, names and sources now expose read-only views instead of the
underlying lists. Those views still become empty when their batch is disposed. A copied batch has
independent metadata, but two models given the same batch still share its disposal and sparse rows.
The fixed Kotlin baseline exposes the original objective and variable lists and exposes its mutable
metadata lists through read-only Kotlin types. The extra collection isolation is a deliberate C#
safety difference, not a claim of matching historical mutation tracking.

This is a behavioral compatibility change: changes to caller-owned variable or objective lists no
longer update a constructed model, and writes through an `IList` downcast of these collections or
constraint metadata are rejected by the standard .NET read-only collection contract. Build a new
model or batch to apply such changes. List elements are not deep-copied: built-in objective cells
are immutable records, but custom objective cells, variable `Type`, `Origin` and other referenced
objects may still be mutable. At that gate these changes did not synchronize `Lhs` and `SparseLhs` or validate row
counts; the following batch addresses those two limitations. They do not detect every possible model mutation, isolate shared batch disposal, provide thread safety,
or prove a solution or infeasibility certificate. Copying adds allocations proportional to input
size; no performance result is claimed.

中文迁移提示：线性/二次矩阵模型的变量列表和 `Objective<C>` 的单元格列表改为浅复制并通过只读集合
暴露；约束方向、右端、名称和来源仍在构造时复制，现在也不能通过向下转换修改。批次释放时元数据视图
仍会清空；`Copy()` 后的批次元数据独立，但直接共用批次的两个模型仍会互相影响释放和稀疏行。原列表的
后续修改不再更新模型，需要构造新模型或批次；对只读集合进行 `IList` 写入按 .NET 集合协议抛出不支持
异常。目标单元格及变量关联对象并未深复制，自定义可变对象仍需调用方管理。Kotlin 固定基线保留上述
列表别名或可变列表，C# 的隔离是明确安全差异。当时本批未解决 `Lhs` 与 `SparseLhs` 分叉和行数验证，
后续批次已处理；任意修改检测、共享批次释放、并发/重入及完整证明仍未解决，也未测量新增复制和只读
视图的性能成本。

### P2 continuation: sparse-backed constraint rows and shape checks

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `LinearTriadModel.kt`, `QuadraticTetradModel.kt`, `ModelView.kt` | `LinearConstraintBatch.Lhs`, `QuadraticConstraintBatch.Lhs`, `ModelConstraint.ValidateShape()` | `ConstraintRowsProjectionTest`, `ConstraintShapeTest`, `MatrixSnapshotSafetyTest`, `ConstraintRowsGurobiIntegrationTest`, `MosekContractTest`, `MosekIntegrationTest` | Sparse projection and selected solver-boundary shape checks tested; independent derived-batch ownership remains open |

`SparseLhs` is the sole stored row representation. The outer `Lhs` view reflects the current sparse
row order and entries when indexed or enumerated; each indexed row is a separate read-only snapshot,
so a held row does not change after later edits. Re-index the outer view to read the latest row.
The outer view no longer implements mutable `IList<T>`; attempting to write an individual row's
read-only collection is rejected. Row indices are projected from the current position, including
when multiple positions share one sparse vector. A batch copy clones the current sparse rows and
retains independent row contents. Signed zero and the quadratic linear-term sentinel `Col2 = -1`
are preserved. Each row access projects that row's entries, not the entire matrix; allocations and
repeated accesses have not been benchmarked.

`ValidateShape()` returns `ApplicationStopped` for a disposed batch and `IllegalArgument` when the
current row count differs from the construction-time `ConstraintCount`, or signs, RHS, names,
sources or present priorities have a different count. Row addition or removal therefore requires
rebuilding a batch with matching metadata before solving. Restoring a matching shape does not restore
a directly dumped matrix's earlier revision after a sparse write. The currently implemented Gurobi
linear/quadratic, MOSEK linear and Hexaly linear/quadratic native paths check shape before creating
their solver implementation and again before consuming rows, including Gurobi's separate pool
path. Other and custom solvers must call the check themselves; it is not automatic for all
`IModelView` consumers. This check does not validate every coefficient, column index or relation.

Kotlin's fixed baseline builds `lhs` lazily and caches it, so edits made after its first access can
leave its projected rows stale. C#'s live outer projection and shape rejection are intentional
safety differences, not a claim that the Kotlin baseline already provides these guarantees. Neither
the projection nor the checks make concurrent or reentrant mutations safe. Shared batch disposal,
derived-model batch ownership and validation of independently copied derived batches remain open;
do not infer those guarantees from this change. Snapshot checks still do not prove feasibility,
duals or Farkas certificates.

中文迁移提示：`SparseLhs` 是唯一存储的行数据；`Lhs` 外层随稀疏行增删、替换和条目修改动态投影，
每次索引得到独立只读行快照，已持有的单行不会随之后的修改变化；需要重新索引才能读取当前行。
外层不再提供可写 `IList<T>`，对单行只读集合写入会被拒绝。共享向量出现在多行时，行索引按当前
位置生成；`Copy()` 复制当前稀疏行，保留负零和二次线性项 `Col2 = -1`。每次访问会投影该行并分配
快照，未做性能测量。

`ValidateShape()` 对已释放批次返回 `ApplicationStopped`，对当前行数与构造时 `ConstraintCount`、方向、
右端、名称、来源或非空优先级数量不符返回 `IllegalArgument`。增删行须重新构造元数据匹配的批次；
将行数恢复并不会撤销直接转储矩阵的稀疏版本失效。现有 Gurobi 线性/二次、MOSEK 线性和 Hexaly
线性/二次原生路径在创建求解实现前及写入行前检查，Gurobi 解池单独覆盖；其他或自定义求解器须自行
调用，不代表所有模型消费者均已校验，也不检查全部系数、列索引和关系符号。固定 Kotlin 基线惰性缓存
`lhs`，首次访问后的稀疏修改可能导致旧投影；C# 动态外层和形状拒绝是明确安全差异。并发/重入、共享
批次释放、派生模型批次所有权及独立副本被修改时的证据检查仍未解决，不能将本批当作可行性、对偶或
Farkas 完整证明。

### P2 continuation: direct conditional evaluation safety

`IfFunction<V>.TryEvaluate` and `IfInFunction<V>.Classify`/`TryEvaluate` preserve the existing
sequential native arithmetic, but independently compute exact rational results from the supplied
direct inputs and their Flt64 encodings. They compare all three branch classifications, including
each side of the interval; a mismatch or an invalid converter returns `IllegalArgument`. Harmless
rounding is still accepted when the classifications agree. Missing inputs retain their previous
`Ok(null)` behavior for `TryEvaluate`; `IfIn.Classify` still fails for missing inputs. The nullable
`Evaluate` cannot distinguish an error from a missing or undefined value: use `TryEvaluate` when
the reason matters. Fixed-value token preparation propagates these failures and rolls back rather
than caching the detected wrong result. Evaluations now invoke the supplied converter, so mutable
or throwing converters can cause a structured failure.

This compares exact mathematical classifications, not the solver's finite-precision row evaluation
or feasibility tolerance. Direct evaluation of an already rounded intermediate still lacks its source
variables. The subsequent model-aware preparation check below handles the reproduced nested
`1e16 + v` counterexample when its expanded inputs can be verified.

中文说明：`If` 和 `IfIn` 保留原逐项运算顺序，同时按传入的直接符号值计算精确结果及求解器
编码结果；三者的分支分类不一致，或转换器无效时返回 `IllegalArgument`，分类不变的舍入仍可接受。
区间条件分别检查上下两侧；`TryEvaluate` 的缺值语义不变。可空 `Evaluate` 无法区分失败、缺值和
间隔，需要原因时应使用 `TryEvaluate`。固定值准备会传播已检测到的失败并回滚缓存；求值现在会调用
转换器。仅有中间值的直接求值仍无法核对其来源；后续准备检查可在展开变量值可验证时处理该嵌套
反例，但不能推断求解器容差下的可行性。其他函数及更广的嵌套来源安全仍待后续处理。

### P2 continuation: model-aware nested conditional preparation

Token registration and `LinearFunctionSymbolAdapter<V>.Prepare` separately compare a conditional's
sequential direct-input classification with the exact native and Flt64-encoded values of its saved
expanded expression. `IfIn` checks each side, even if the combined result remains false. A proven
branch disagreement returns `IllegalArgument` during registration and restores prior token caches;
an unavailable expanded value leaves the condition uncached. Harmless rounding remains acceptable
when all classifications agree. Exact expansion evaluation does not repeat potentially overflowing
native arithmetic. Manual `Prepare` returns null for a missing proof or failed comparison;
`PrepareAndCache` also removes the previous no-solution cache for these conditional functions.

The proof view is separate from the declared input view: it uses explicit variable assignments and,
after a dependency layer finishes, independently verified auxiliary results. At this earlier gate only ABS of
an exactly expanded, zero-offset, single variable with coefficient +1 or -1 can provide derived
auxiliary evidence; its prepared result must match both exact native and encoded input magnitudes.
Explicit variable assignments take priority. Other nested function results are not silently promoted
to solver-variable evidence. Unrelated ABS functions are not evaluated for proof. Manual preparation
does not derive auxiliary evidence from intermediate values; callers must provide the corresponding
variable assignments. These assignments are not themselves a feasibility proof. The later conditional
evidence gate extends the automatically checked subset to `If` and `Sigmoid`.

The public `TryEvaluate` and `IfIn.Classify` retain direct-input semantics and cannot verify an
intermediate value's provenance. Exact algebra is not a guarantee of solver tolerance or intermediate
row feasibility. The broader nested-function proof set, custom writers, concurrent mutation and
post-build structural invalidation remain open.

中文迁移提示：注册或手动准备 `If`/`IfIn` 时，除原直接输入的顺序分类外，还分别核对已保存
展开式的原生精确值及 Flt64 编码分类；区间两侧独立检查。注册时发现分支不一致返回
`IllegalArgument` 并恢复已有缓存；缺少展开变量证据则不缓存，分类不变的舍入仍可接受。
手动 `Prepare` 在证据不足或不一致时返回 null，`PrepareAndCache` 会清除这两个条件函数的旧缓存。
准备证据独立于函数声明的输入视图，显式变量值优先；当时仅对可精确展开为零常量、
单变量且系数为 +1 或 -1 的 ABS 验证结果并提供辅助变量证据，其余函数结果不自动推定。手动准备
需要调用方提供相应变量值。直接 `TryEvaluate`/`Classify` 仍不验证中间值来源；求解器容差、
变量赋值是否可行、其他嵌套函数及模型构建后的结构失效仍未全面保证。后续批次已将受检范围
扩展到 `If` 和 `Sigmoid`，见最新证据批次。

### P2 implemented function behavior (staged generic migration)

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `Abs.kt` | `AbsFunction<V>` | `AbsFunctionTest`, `GenericAbsFunctionTest` | Generic API and representative Flt64/Flt32/Int64/FltX/RtnX behavior verified; deferred/native structure pending |
| `Max.kt`, `MinMax.kt` | `MaxFunction<V>`, `MinFunction<V>`, `MinMaxFunction<V>`, `MaxMinFunction<V>` | `ExtremumFunctionTest`, `GenericExtremumFunctionTest`, `FiniteBoundsSafetyTest` | Generic representative behavior and directed bound examples verified; deferred/native structures and Big-M limitations remain |
| `SlackRange.kt`, `Max.kt` | `SlackRangeFunction<V>.Create` | `SlackRangeFunctionTest` | Generic representative behavior and nested factory paths verified; shared Big-M limitations remain (Batch 40) |
| `Floor.kt`, `Ceiling.kt`, `Rounding.kt` | `FloorFunction<V>`, `CeilingFunction<V>`, `RoundingFunction<V>` | `RoundingFunctionTest`, `GenericRoundingFunctionTest` | Generic evaluation and Flt64/FltX/RtnX rows verified; Int64/Flt32 registration explicitly unsupported for collapsed gaps; ROUND midpoint mismatch disclosed |
| `Inequality.kt`, `IndicatorStructure.kt`, `Conditional.kt` | `InequalityFunction<V>` | `InequalityFunctionTest`, `InequalityNestedInputTest`, `AutomaticIndicatorBoundsTest` | Representative comparisons and missing-bound failures covered; complete row arithmetic, native indicators and real solver enforcement remain gaps |
| `Conditional.kt`, `DiscreteCondition.kt` | `ConditionClassification`, `TruthValue` | `ConditionClassificationTest` | Scalar four-relation truth table and parameter validation tested; general lowering remains incomplete |
| `If.kt`, `Conditional.kt`, `DiscreteCondition.kt`, `LinearConstraintInput.kt` | `IfFunction<V>.Create`, `IfFunction<V>.From`, `LinearConstraintInput<V>.From` | `IfFunctionTest`, `IfNestedFunctionTest`, `IfConstraintInputTest`, `ConditionalEvaluationSafetyTest`, `NestedConditionalPreparationSafetyTest`, `LinearConstraintInputPrecisionTest`, `IfGurobiIntegrationTest` | Bounded normalization and direct/model-aware branch checks verified; broader nested evidence, native indicators and other limitations remain |
| `IfIn.kt`, `Conditional.kt`, `DiscreteCondition.kt` | `IfInFunction<V>.Create`, `Classify` | `IfInFunctionTest`, `ConditionalEvaluationSafetyTest`, `NestedConditionalPreparationSafetyTest`, `IfGurobiIntegrationTest` | Selected interval rows, direct/model-aware branch checks and Gurobi boundaries verified; broader nested evidence and other limitations remain |
| `IfThen.kt`, `ConditionalValue.kt`, `LinearConstraintInput.kt` | `IfThenFunction<V>.Create`, `From`, `Classify` | `IfThenFunctionTest`, `IfThenGurobiIntegrationTest` | Selected bounded conditional-value rows, nested preparation, fixed-column substitution and Gurobi cases verified; stricter then bounds and arithmetic, callback isolation/native indicators and other limitations remain |
| `Imply.kt`, `ImpliedCondition.kt`, `FunctionSymbol.kt` | `ImplyFunction<V>.Create`, `Classify` | `ImplyFunctionTest`, `ImplyGurobiIntegrationTest` | Selected gated rows, short-circuiting, nested preparation and Gurobi cases verified; result-polynomial difference, enforced bounds, callback isolation/native indicators and other limits remain |
| `Product.kt` | `ProductFunction<V>.Create`, `IQuadraticMathFunctionSymbol<V>` | `ProductFunctionTest`, `ProductGurobiIntegrationTest` | Typed expression, registration, matrix and selected native solves tested; floating grouping and nested-mutation limits remain |
| `IntegerProduct.kt` | `IntegerProductFunction<V>.Create` | `IntegerProductFunctionTest`, `IntegerProductGurobiIntegrationTest` | Selected typed MILP rows, lifecycle, fixed substitution and native solves tested; floating and backend limits remain |
| `BigM.kt`, `MechanismModel.kt` | `BeginConstraintWrite`, `IConstraintWriteTransaction`, shared function row writers | `ConstraintWriteTransactionTest`, `LogicalBoundsLifecycleTest`, `IfNestedFunctionTest` | Linear/quadratic rollback, write/commit failures, stale-bound checks and rollback error propagation tested; custom transactions are trusted and callback/token-batch isolation remains incomplete |
| `TokenList.kt`, `TokenListAtomicityTest.kt`, `TokenTable.kt` | `IAddableTokenCollection<V>`, `BeginTokenRegistration`, `ITokenRegistrationTransaction` | `TokenBatchAtomicityTest`, `TokenRegistrationTransactionTest`, `FunctionSymbolRegistrationTest` | Built-in duplicate policy, batch, index allocation and symbol rollback covered; custom implementations are trusted, shared values/callback effects and isolation remain limited |
| `Binaryzation.kt`, `And.kt` | `BinaryzationFunction`, `AndFunction`, `OrFunction`, `NotFunction`, `XorFunction` | `LogicalFunctionTest`, `AutomaticIndicatorBoundsTest`, `IndicatorConstraintSafetyTest` | Binaryzation rejects selected gap collapses; complete indicator arithmetic and generic numeric API pending |
| `Mod.kt`, `Semi.kt`, `InStepRange.kt` | `ModFunction`, `SemiFunction`, `InStepRangeFunction` | `DiscreteArithmeticFunctionTest` | Flt64 baseline behavior verified; generic numeric API pending |
| `BalanceTernaryzation.kt`, `OneOf.kt` | `BalanceTernaryzationFunction`, `OneOfFunction` | `TernarySelectionFunctionsTest` | Selected Flt64 behavior tested; generic numeric API and deferred native-indicator structure pending |
| `SameAs.kt` | `SameAsFunction` | `SameAsResultEncodingTest`, `SameAsBoundaryRowsTest`, `SameAsBoundaryGurobiIntegrationTest` | Selected Boolean results and LE/GE/EQ endpoints tested; strict relation registration, full floating semantics and generic API pending |
| `SatisfiedAmount.kt`, `SatisfiedAmountInequality.kt`, `LinearConstraintInput.kt` | `SatisfiedAmountFunction`, `SatisfiedAmountInequalityFunction`, `AnyFunction`, `AllFunction`, `AtLeastInequalityFunction`, `NotAllFunction`, `NumerableFunction` | `SatisfiedAmountFunctionsTest`, `SatisfiedAmountSemanticTest`, `SatisfiedAmountResidualTest`, `SatisfiedAmountGurobiIntegrationTest` | Selected LE/GE/EQ exact-row endpoints and Gurobi cases verified; evaluation rounding, expansion and other inequality-count differences remain; generic numeric API pending |

### Gate repair: Demo4 request isolation

| Kotlin source | C# entry point | Behavioral test | Status |
|---|---|---|---|
| `demo4/domain/task/model/AircraftType.kt`, other task and crew pools | `AircraftType`, `AircraftMinorType`, `Aircraft`, `Airport`, `CrewMan`, `CrewManRank`, `Pilot`, `PilotRank` | `Demo4RegistryConcurrencyTest` | Individual registry operations and snapshot enumeration exercised concurrently; no cross-registry transaction or mutable-object thread-safety guarantee |
| `demo4/Application.kt`, `demo4/infrastructure/dto/Input.kt` (empty in the fixed baseline) | `FlightRecoveryApplication.RunAsync`, `FlightRecoveryApplication.DomainData`, `Input.Airports` | `Demo4RegistryConcurrencyTest`, `Demo4RequestDomainTest`, `Demo4E2ETests` | C# request-local construction and prepared domain input covered; model/solution pipeline remains incomplete |

- The Batch 44 no-SDK failure was caused by concurrent mutations of the process-wide
  `AircraftMinorType` dictionary. All eight Demo4 registries now support concurrent individual
  reads, writes, and enumeration; `AircraftType.GetOrAdd` returns the published instance for a
  key. `Values` returns a point-in-time, shallow snapshot instead of the former live collection.
  Registering the same key still replaces the prior value. These pools are still process-wide and
  unbounded; compound `Find`/`Register` sequences and mutable objects are not made atomic.
- `FlightRecoveryApplication` now constructs aircraft, minor types, types and airports locally for
  each input, without reading or publishing the global pools. Flight legs may reference only
  aircraft in that input. Within a request, repeated aircraft IDs and minor-type codes retain the
  first definition, as do repeated airport ICAOs; a repeated aircraft still appears repeatedly in
  the aircraft list. Missing aircraft references are still skipped rather than reported as errors.
- `Input.Airports` now supplies request-local airport types; omitted types default to domestic.
  Invalid airport entries, ICAO lengths or enum values fail domain construction and cause the
  public `RunAsync` to return a failure. This does not validate every other DTO field.
- **Migration:** pre-registering an aircraft, minor type or airport no longer injects it into
  `RunAsync`. Supply airport classifications through `Input.Airports`, and aircraft cost,
  location and enabled time through `Input.Aircrafts`. To pass enriched domain objects instead,
  construct a `FlightRecoveryApplication.DomainData` with a time window, aircraft, usability map,
  tasks and origin bunches, then call `RunAsync(input, domainData)`; the `input` supplies the
  problem identifier and settings, not domain objects in this overload. Pass null for the original
  DTO-building behavior. The DTO itself still cannot express route/connection-time maps or passenger
  capacities. Direct domain users may still use the static registries, but must not assume that an
  earlier `Values` result changes with subsequent registrations.
- **迁移提示：**`RunAsync` 不再读取或写入全局池；机场类型应放入 `Input.Airports`，飞机成本、
  位置和可用时间放入 `Input.Aircrafts`。也可手工组装 `FlightRecoveryApplication.DomainData`
  并调用 `RunAsync(input, domainData)` 传入航线/连接时间、容量和初始航班束等丰富对象；
  此重载从 `input` 读取问题标识和设置，不读取其中的领域对象，传入 null 则沿用 DTO 构建。
  DTO 尚不能直接表达航线/连接时间及容量。静态池的 `Values` 改为浅快照，跨池操作和对象自身
  的并发安全不受本次修改保证。
- Prepared data remains caller-owned and is not copied. The caller must keep its collections and
  objects unchanged during a run and isolate simultaneous requests; the application can itself set
  indices on supplied aircraft. It checks basic references, duplicate aircraft identity, usable
  task times, consistency with each aircraft's own usability and origin-bunch aircraft ownership,
  but does not validate arbitrary user-defined task behavior or all DTO fields. The existing
  `BunchGenerationContext.Init` failure path can still throw. Fake-solver tests verify that the
  initial-bunch path accesses a transfer's route-duration map and independent requests' connection
  maps, not that those values alter a correct optimization model or that the full application is
  generally thread-safe.
- **Not a solve acceptance:** the current Gantt bunch-compilation registration, task/executor
  constraints and cancellation objective include no-op implementations; Demo4 omits the bunch-cost
  pipeline. The branch-and-price call to `AnalyzeBunchSolution` also omits the solver solution, and
  the Demo4 context returns an empty bunch/cancellation result when that argument is null. An `Ok`
  from the zero-output mock is therefore only an orchestration smoke test, not evidence of a feasible
  solution, correct costs, a complete model or correct use of every supplied domain field. These
  remain P7/P9 work and must not be represented as validated solver behavior.
- **数据所有权与限制：**传入集合和对象不会复制；调用方应在运行期间保持其不变并隔离并发请求，
  但应用自身可能设置飞机索引。当前仅检查基本引用、重复飞机身份、任务时间、飞机可用性引用
  与初始航班束归属；自定义任务和 DTO 并未全面校验，既有 `BunchGenerationContext.Init` 仍有
  异常路径。模拟请求测试只证明初始束生成访问了中转航线时长表和各自的连接时间表，不能证明
  领域值进入正确优化模型。
  任务束编译、约束、目标和结果提取仍有占位或缺口；模拟求解返回成功不等于可行解或真实闭环。
- The original isolation/registry regression tests all failed before the fix; the final focused
  suite has 12 passing cases. Twenty repeated runs of the initial five isolation/registry cases and five
  repeated runs of the final no-SDK Example.Tests assembly passed. This does not prove that all
  solver or application operations are safe for simultaneous use.

### Previous gate: registry isolation

- After the Demo4 gate repair, the Windows Release solution run with configured SDKs had 2911
  passed, 0 failed and 1 skipped across 23 assemblies; Core: 1048 passed. With solver SDK and
  external-service environment variables unset, the isolated run had 2889 passed, 0 failed and
  1 skipped across 22 assemblies. The prior Batch 44 no-SDK run had 2876 passed, 1 failed and
  1 skipped, with a captured concurrent dictionary mutation in Demo4; its failure is not erased
  by a successful retry. An earlier Batch 37 Demo4 failure had no captured stack, so its cause
  cannot be established from that run alone.
- Benchmark.Tests, Hexaly.Tests and MOSEK.Tests have no runnable tests in the no-SDK configuration;
  SDK-backed Benchmark.Tests and Hexaly.Tests also have none here. The skip is the existing
  Delaunay circumcircle precision test.
- Batch 44 final targeted function regression: 128 passed; Core has 1048 passing tests. The
  original shifted endpoint is covered in the normal exact-row
  suite. Gurobi SDK and isolated no-SDK branches each passed 4 contract tests. An explicit opt-in
  run separately passed 14 selected real Gurobi cases with no skips; missing-SDK opt-in fails.
- Forced non-incremental Windows Release solution build with the configured SDKs: 0 errors, 744
  existing analyzer/style warnings. The isolated no-SDK rebuild had 0 errors and 719 existing
  warnings. The new Demo4 test file has no warnings; no warning points to the added source lines.
- Linux x64 on Debian 13 in an isolated, network-disabled container: the official .NET SDK 10.0.402
  archive matched Microsoft's release SHA-512, offline restore used a copy of the local NuGet cache,
  and a forced Release rebuild had 0 errors and 719 existing warnings. The full run had 2889
  passed, 0 failed and 1 skipped across 22 assemblies. Benchmark.Tests, Hexaly.Tests and
  MOSEK.Tests had no runnable tests. Source and package caches were mounted read-only and copied
  into the disposable container; this is not a fresh-network restore or external-backend check.
- Full `git diff --check` still reports the pre-existing extra EOF blank line in
  `LinearConstraintInput.cs`; the touched `SatisfiedAmount.cs` EOF was normalized. Raw logs and
  isolated no-SDK artifacts are kept under ignored `logs/`, not in source control.

### Previous gate: prepared Demo4 domain data

- The focused Demo4 request-domain, registry and E2E suite has 29 passing tests. A prior red
  regression reproduced a null-reference exception for a task with neither a time nor time window;
  this case now returns `IllegalArgument`. The new tests also cover missing/inconsistent usability,
  an uninitialized aircraft, distinct aircraft with the same registration, invalid origin-bunch
  ownership and null tasks, a transfer with a time window instead of a scheduled time, and the fact
  that a run can index a caller-owned aircraft. The test using 12 independent fake-solver requests
  also passed 10 separate repetitions, which is not a general thread-safety proof.
- Forced Windows Release builds: configured SDKs 0 errors / 743 existing warnings; isolated
  no-SDK 0 errors / 718 existing warnings. Full solution tests: 2920 passed / 1 skipped across
  23 assemblies with SDKs; 2898 passed / 1 skipped across 22 assemblies without SDKs. The added
  test file and new validation lines introduced no reported warnings.
- Linux x64 Debian 13, network-disabled container, .NET SDK 10.0.402 and a copy of the local NuGet
  cache: forced Release build 0 errors / 718 existing warnings; full solution 2898 passed /
  1 skipped across 22 assemblies. This is neither a fresh-network restore nor real-backend
  validation. In no-SDK runs Benchmark, Hexaly and MOSEK test assemblies have no runnable tests;
  the existing Delaunay precision test remains skipped.
- Full `git diff --check` still reports the pre-existing EOF blank line in
  `LinearConstraintInput.cs`. That batch did not rerun the selected Gurobi integration cases or
  validate other external solvers/services. Raw logs and isolated artifacts remain under ignored
  `logs/`; the known P2 floating-evaluation mismatch and fixed-variable gaps remain unresolved.

### Previous gate: checked equality branches

- The final selected Core function/safety suite passed 133 tests. The earlier eight shifted EQ
  exact-row cases failed before the implementation, while a subsequent exploratory run showed two
  automatic nested-bound cases return failure rather than infer a bound. The final suite explicitly
  checks that conservative failure; it does not count the failed exploratory run as acceptance.
- Windows forced Release builds: configured SDKs 0 errors / 743 existing warnings; isolated
  no-SDK 0 errors / 718 existing warnings. Full solution tests: 2945 passed / 1 skipped across
  23 assemblies with SDKs; 2923 passed / 1 skipped across 22 assemblies without SDKs. Core has
  1073 passing cases. None of the files changed in this batch introduced a reported warning.
- Linux x64 Debian 13 in a network-disabled container, using the official .NET SDK 10.0.402 and
  a copied local NuGet cache: forced Release build 0 errors / 718 existing warnings; full solution
  2923 passed / 1 skipped across 22 assemblies. This is not a fresh-network restore or external
  solver validation. Benchmark, Hexaly and MOSEK test assemblies have no runnable tests in the
  no-SDK runs; the existing Delaunay precision test remains skipped.
- The opt-in Gurobi 10.0.1 run passed 28 selected cases, including 14 newly added EQ cases at
  `FeasibilityTol=1e-9`; this is not complete Gurobi acceptance. Other solvers and external
  services were not verified in this batch. Full `git diff --check` still reports the pre-existing
  EOF blank line in `LinearConstraintInput.cs`; logs and isolated artifacts remain under `logs/`.

### Previous gate: nested satisfied-count bounds

- Before this batch, ten of 55 selected residual cases failed because bounded nested inputs could
  not infer Big-M. The final targeted function and indicator suite passes 189 cases, including
  expanded bounds, unbounded or later-invalid inputs, declared-versus-expanded bounds and the
  documented stale-model limitation. The expanded residual test class passes 68 cases.
- The opt-in Gurobi 10.0.1 run passes 38 selected solves, including ten new nested automatic-bound
  cases at `FeasibilityTol=1e-9`; the configured integer tolerance is unchanged. Other solver
  backends and services are not accepted by this run.
- Windows forced Release builds: configured SDKs 0 errors / 743 existing warnings; isolated
  no-SDK 0 errors / 718 existing warnings. Full solution tests: 2968 passed / 1 skipped across
  23 assemblies with SDKs; 2946 passed / 1 skipped across 22 assemblies without SDKs. Core has
  1096 passing cases. The files touched in this batch introduce no reported warnings.
- Linux x64 Debian 13 in a network-disabled container, using the official .NET SDK 10.0.402 and a
  copied local NuGet cache: forced Release build 0 errors / 718 existing warnings; full solution
  2946 passed / 1 skipped across 22 assemblies. This is neither a fresh-network restore nor
  external solver validation. Benchmark, Hexaly and MOSEK have no runnable tests in the no-SDK
  runs; the existing Delaunay precision test remains skipped. With SDKs, Benchmark and Hexaly also
  have no runnable tests. Full `git diff --check` still reports the existing EOF blank line in
  `LinearConstraintInput.cs`; raw logs and isolated artifacts remain under `logs/`.

### Previous gate: tracked modeling-bound invalidation

- The pre-fix LE/GE/EQ stale-dump regressions all failed; a separate pre-fix state-refresh regression
  returned success after a tracked range changed during capture. The final selected Core suite passes
  220 cases, including manual registration for eight function variants, stale linear/quadratic and
  fixed-variable dumps, equivalent/narrower replacement, state revision and row-ID rollback, nested
  bounds, unrelated changes and Gurobi-independent failures.
- The opt-in Gurobi 10.0.1 suite passes 39 selected cases: 38 solver cases and one stale linear/
  quadratic dump check. The selected solves use `FeasibilityTol=1e-9` and the default integer
  tolerance. This is not comprehensive Gurobi or other-backend acceptance.
- Windows forced Release builds: configured SDKs 0 errors / 743 existing warnings and full solution
  2999 passed / 1 skipped across 23 assemblies; isolated no-SDK 0 errors / 718 existing warnings and
  full solution 2977 passed / 1 skipped across 22 assemblies. Core has 1127 passing cases. The files
  touched by this gate introduced no reported build warnings.
- Linux x64 Debian 13 in a network-disabled container, with the official .NET SDK 10.0.402 and a copy
  of the local NuGet cache: forced Release build 0 errors / 718 existing warnings; full solution
  2977 passed / 1 skipped across 22 assemblies. This is not a fresh-network restore or native-solver
  validation. An initial Oracle Linux 9 container could not start the SDK because ICU was missing;
  it is not an Oracle Linux acceptance result.
- Benchmark and Hexaly have no runnable tests in the configured-SDK run; Benchmark, Hexaly and MOSEK
  have none in the no-SDK runs. The existing Delaunay precision case remains skipped. Full
  `git diff --check` still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`;
  raw logs and isolated artifacts remain under `logs/`.

### Previous gate: logical-function bounds and AND

- The initial 30 lifecycle cases produced 23 failures before this batch. The final selected suite
  passes 389 cases, including 112 logical lifecycle cases; an independent continuous-AND truth-table
  regression failed before the AND row correction. The tests exercise six functions, manual and
  factory registration, stale linear/quadratic dumps, replacement bounds, nested inputs, missing
  variables, cycles, binary fast paths and range changes during registration.
- The opt-in Gurobi 10.0.1 suite passes 63 selected cases, including 24 new logical endpoint cases
  and the previous 38 selected satisfied-count solves plus one stale-dump check. Two of the new
  AND cases failed before the row correction. Solves use `FeasibilityTol=1e-9` and the default
  integer tolerance; they do not constitute full Gurobi acceptance.
- At this gate the two independent `SameAs` result-consistency diagnostics failed and were not counted
  as passing tests. They passed after the subsequent correction; the surrounding indicator behavior
  remains unaccepted. Their original red output remains in `logs/p2-sameas-diagnostic-red.log`.
- Windows forced Release builds: configured SDKs 0 errors / 743 existing warnings and full solution
  3111 passed / 1 skipped across 23 assemblies; isolated no-SDK 0 errors / 718 existing warnings
  and full solution 3089 passed / 1 skipped across 22 assemblies. Core has 1239 passing cases.
  No new warnings were reported for this batch's source and test files.
- Linux x64 Debian 13 in a network-disabled container, using the official .NET SDK 10.0.402 and a
  copied local NuGet cache: forced Release build 0 errors / 718 existing warnings; full solution
  3089 passed / 1 skipped across 22 assemblies. This is neither a fresh-network restore nor native
  solver validation. Benchmark, Hexaly and MOSEK have no runnable tests in the no-SDK runs;
  Benchmark and Hexaly have none in the configured-SDK run. The existing Delaunay test remains
  skipped. Other external solvers and services were not verified by this batch.
- Full `git diff --check` still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`;
  raw logs and isolated artifacts remain under ignored `logs/`. No performance claim is made.

### Previous gate: SameAs result encoding

- The new regression initially failed seven of nine cases: four constraint-mode truth-table cases,
  two measurement-mode multi-difference cases and the missing-later-input case. After correction, all
  nine pass; together with the existing ternary/selection tests the targeted suite passes 28 cases.
  The two earlier independent result diagnostics also pass. The four separate exact-boundary
  diagnostics failed at this gate and were excluded; they pass in the following continuation.
- The opt-in Gurobi 10.0.1 suite passes 82 selected cases: 19 new SameAs solves, 24 logical cases,
  38 satisfied-count solves and one stale-dump check. SameAs inputs/results were fixed with equality
  rows rather than fixed-column substitution; feasible solutions were checked for satisfaction and
  difference values. Solves use `FeasibilityTol=1e-9` and the default integer tolerance. This is
  not comprehensive Gurobi acceptance and did not cover the then-failing comparison endpoints.
- Windows forced Release builds: configured SDKs 0 errors / 743 existing warnings and full solution
  3120 passed / 1 skipped across 23 assemblies; isolated no-SDK 0 errors / 718 existing warnings
  and full solution 3098 passed / 1 skipped across 22 assemblies. Core has 1248 passing cases.
  The source and test files touched in this batch report no new build warnings.
- Linux x64 Debian 13 in a network-disabled container, using official .NET SDK 10.0.402 and a copied
  local NuGet cache: forced Release build 0 errors / 718 existing warnings; full solution 3098 passed /
  1 skipped across 22 assemblies. This is neither a fresh-network restore nor native-solver validation.
  Benchmark, Hexaly and MOSEK have no runnable tests in the no-SDK runs; Benchmark and Hexaly have none
  in the configured-SDK run. The existing Delaunay test remains skipped. Other external solvers and
  services were not verified by this batch.
- Full `git diff --check` still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`.
  Raw logs and the original boundary failures remain under ignored `logs/`; those failures did not
  appear in this gate's passing solution test totals. No performance claim is made.

### Previous gate: SameAs comparison boundaries

- The final selected function and lifecycle suite passes 346 cases. The four historical endpoint
  diagnostics also pass; the original red runs are retained under ignored `logs/`. The exact row and
  matrix checks include 13 additional cases for upward relaxation at domain endpoints, inexact later
  coefficients and missing later branch flags.
- The opt-in Gurobi 10.0.1 suite passes 166 selected cases, including 84 new boundary/gap cases,
  81 earlier selected solves and one stale-dump check. The solves use `FeasibilityTol=1e-9` and default
  integer tolerance. They do not cover subnormal gaps, fixed-column substitution or full backend behavior.
- Windows forced Release builds: configured SDKs 0 errors / 743 existing warnings, full solution 3186
  passed / 1 skipped across 23 assemblies; isolated no-SDK 0 errors / 718 existing warnings, full
  solution 3164 passed / 1 skipped across 22 assemblies. Core has 1314 passing cases. The files touched
  in this batch report no new build warnings; the Gurobi SDK and no-SDK branches each include four
  contract cases.
- Linux x64 Debian 13 in a network-disabled container used the official .NET SDK 10.0.402 archive
  (SHA-512 checked against the saved release manifest) and a copied local NuGet cache. The forced
  Release build had 0 errors / 718 existing warnings; the full solution had 3164 passed / 1 skipped
  across 22 assemblies. This is neither a fresh-network restore nor native-solver validation.
- Benchmark and Hexaly have no runnable tests with configured SDKs; Benchmark, Hexaly and MOSEK have
  none in the no-SDK runs. The existing Delaunay precision test remains skipped. Full `git diff --check`
  still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`; raw logs and isolated
  artifacts remain under ignored `logs/`. Other external solvers and services were not accepted.

### Previous gate: generic quadratic product

- The focused product, registration, flattening and preparation suite passes 304 cases, including
  19 product/SPI cases and one shared null-input case. Direct input-underflow and partial-input
  overflow regressions first failed before their fixes and now pass, including failure and rollback
  during partial fixed-value model preparation. An earlier missing term still short-circuits later
  overflow checks. The separate existing linear-function underflow behavior is unchanged.
- Explicit opt-in Gurobi 10.0.1 tests pass 168 selected cases: two new quadratic objective/constraint
  solves, 165 previous selected solves and one stale-dump check. The product cases set `NonConvex=2`
  and include an active product constraint; they do not establish complete solver acceptance. The
  opt-in build without the Gurobi SDK fails explicitly as expected and is not counted as a pass.
- Windows forced Release builds: configured SDKs 0 errors / 743 existing warnings, full solution
  3203 passed / 1 skipped across 23 runnable assemblies; isolated no-SDK 0 errors / 718 existing
  warnings, full solution 3181 passed / 1 skipped across 22 runnable assemblies. Core has 1331 passing
  cases. The files touched in this batch have no new compiler or style warnings.
- Linux x64 Debian 13 in a network-disabled container used the official .NET SDK 10.0.402 archive
  (SHA-512 checked against the saved release manifest) and a copied local NuGet cache. The forced
  Release build had 0 errors / 718 existing warnings, and the full solution had 3181 passed / 1 skipped
  across 22 runnable assemblies. This is neither a fresh-network restore nor native-solver validation.
- Benchmark and Hexaly have no runnable tests with configured SDKs; Benchmark, Hexaly and MOSEK have
  none in the no-SDK runs. The existing Delaunay precision test remains skipped. Full `git diff --check`
  still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. Raw logs and isolated
  artifacts remain under ignored `logs/`; other external solvers and services were not accepted.

### Previous gate: bounded integer product

- The new Core suite passes 19 cases, including exact mechanism-row and matrix enumeration for
  `x` in `[-2,2]` and five input values, incorrect result/mask assignments and unused codes. It also
  covers five value types, zero width, tightened explicit bounds, open integer bounds, six other
  native integer range types, unrepresentable numeric domains, missing dependencies/tokens, fixed-value preparation, tracked stale
  ranges, an unregistered nested function, invalid conversion and a `2^53` high-bit guard. Two
  coefficient/high-bit diagnostics and two converter-induced range-change cases failed before their
  respective corrections. An Flt32 non-representable domain regression also failed before correction;
  original red outputs remain in ignored `logs/`.
- The explicit opt-in Gurobi 10.0.1 selection passes 188 cases, including 20 new integer-product
  cases: nine correct and nine incorrect fixed assignments using equality rows, one free optimization
  and one fixed-column substitution. The 18 fixed-assignment cases use `FeasibilityTol=1e-9` and the
  default integer tolerance; the two optimization/substitution cases use solver defaults. The rest
  include two quadratic solves, prior linear solves and a stale-dump check. This is selected-case
  evidence, not complete backend acceptance. Without the SDK, explicit opt-in fails with the expected
  missing-SDK error and is not counted as an integration pass.
- Windows forced Release builds: configured SDKs 0 errors / 743 existing warnings, full solution
  3222 passed / 1 skipped across 23 runnable assemblies; isolated no-SDK 0 errors / 718 existing
  warnings, full solution 3200 passed / 1 skipped across 22 runnable assemblies. Core has 1350
  passing cases. The new source and test files have no reported compiler or style warnings.
- Linux x64 Debian 13 in a network-disabled container, with the official .NET SDK 10.0.402 archive
  SHA-512 checked against the saved release manifest and a copied local NuGet cache, has 0 errors /
  718 existing warnings and 3200 passed / 1 skipped across 22 runnable assemblies. This is neither a
  fresh-network restore nor native-solver validation. An earlier Linux run copied source and tests
  while the converter-range fix was being edited and failed both new range regressions; it is not
  counted in the final gate and remains in `logs/p2-integer-product-linux.log`.
- Benchmark and Hexaly have no runnable tests with configured SDKs; Benchmark, Hexaly and MOSEK have
  none in the no-SDK runs. The existing Delaunay precision test remains skipped. Full `git diff --check`
  still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. Raw logs and isolated
  artifacts remain under ignored `logs/`; other external solvers and services were not accepted.

### Historical gate: scalar condition classification

- The focused classifier, placeholder and related function-registration suite passes 196 cases,
  including 29 classifier and four placeholder cases. The tests cover the four relation tables,
  business-gap endpoints, adjacent/subnormal values, negative zero, invalid values and relations,
  representative float/integer/decimal/rational types and native values that collapse to the same
  Flt64 encoding. Before the placeholder fix, all four checked-evaluation regressions failed because
  they returned `Ok(null)`; the red output remains in ignored `logs/`. These tests do not prove
  polynomial lattice spacing, conditional row correctness or solver encoding equivalence.
- Windows forced Release builds: configured SDKs 0 errors / 743 existing warnings and 3255 passed /
  1 skipped across 23 runnable assemblies; isolated no-SDK 0 errors / 718 existing warnings and 3233
  passed / 1 skipped across 22 runnable assemblies. Core has 1383 passing cases. The touched source
  and test files have no reported compiler or style warnings; both Gurobi branches have four contract
  tests. The existing Delaunay precision test remains skipped, not passed.
- Linux x64 Debian 13 in a network-disabled container used the official .NET SDK 10.0.402 archive
  (SHA-512 checked against the saved release manifest) and a copied local NuGet cache. The forced
  Release build had 0 errors / 718 existing warnings, and the solution had 3233 passed / 1 skipped
  across 22 runnable assemblies. This is neither a fresh-network restore nor native-solver validation.
- Explicit opt-in Gurobi 10.0.1 regression passes 188 previously selected cases, including 187 solver
  cases and one stale-dump check. No new solver rows or classifier-to-solver integration were added;
  this is not complete backend acceptance. Benchmark and Hexaly have no runnable tests with configured
  SDKs; Benchmark, Hexaly and MOSEK have none in the no-SDK runs. Other solvers, external services,
  cross-language execution and performance were not verified in this batch.
- Full `git diff --check` still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`.
  Raw logs and isolated artifacts remain under ignored `logs/`. At this historical gate all four
  conditional functions were unimplemented; the subsequent bounded `If` batch supersedes that for
  the supported subset. Other placeholders and direct use of an unregistered adapter can still bypass
  these explicit registration failures.

### Previous gate: bounded generic `If`

- The selected condition, function and lifecycle suite passes 292 cases. These include exact row and
  matrix checks for all four supported relations, lattice offsets and combined-coefficient GCD,
  representative numeric types, explicit and tracked bounds, nested adapters and registration
  failures. The read-only snapshot regression first failed and then passed; its original output is
  retained in ignored `logs/`.
- Explicit opt-in Gurobi 10.0.1 tests pass 230 selected cases: 229 solve cases and one stale-dump
  check. The 42 new `If` cases cover branch endpoints, gaps, free optimization and fixed-column
  substitution. This is selected-case evidence, not complete backend acceptance. The explicit
  opt-in build without the SDK fails as expected and is not counted as a pass.
- Windows forced Release builds: configured SDKs 0 errors / 743 existing warnings, full solution
  3283 passed / 1 skipped across 23 runnable assemblies; isolated no-SDK 0 errors / 718 existing
  warnings, full solution 3261 passed / 1 skipped across 22 runnable assemblies. Core has 1411
  passing cases. This batch's source and test files report no new build warnings.
- Linux x64 Debian 13 in a network-disabled container used the official .NET SDK 10.0.402 and a
  copied local NuGet cache: forced Release build 0 errors / 718 existing warnings, full solution
  3261 passed / 1 skipped across 22 runnable assemblies. Concurrent output interleaved two test
  assembly summary lines; their recorded passed counts are 162 and 43. This is neither a fresh-network
  restore nor native-solver validation.
- Benchmark and Hexaly have no runnable tests with configured SDKs; Benchmark, Hexaly and MOSEK have
  none in the no-SDK runs. The existing Delaunay precision test remains skipped. Full `git diff --check`
  still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. Raw logs and isolated
  artifacts remain under ignored `logs/`; other external solvers and services were not accepted.

### Previous gate: nested `If` expressions

- The focused function, flattening and lifecycle suite passes 345 cases, including 25 new nested
  cases. These exercise four relation truth tables, integer lattice offsets, a multi-term structural
  function fixture across five numeric types, a nested Flt64 ABS, fixed-value preparation, linear/quadratic stale bounds,
  deep/shared expressions, rounded coefficients/constants, missing function registration,
  converter failures and a custom writer's partial failure. The original nested tests had seven failures before implementation; four
  converter revalidation cases and three argument-exception cases subsequently failed before their
  respective fixes. Their red logs remain under ignored `logs/`.
- The explicit opt-in Gurobi 10.0.1 selection passes 271 cases, including 270 solves and one
  stale-dump check. This batch adds 40 nested boundary/gap solves using `FeasibilityTol=1e-9` and
  default integer tolerance, plus one nested fixed-column substitution using solver defaults. These
  are selected cases, not complete backend acceptance. Opting in without the SDK fails as expected
  and is not counted as a pass.
- Windows forced Release builds: configured SDKs 0 errors / 743 existing warnings, full solution
  3308 passed / 1 skipped across 23 runnable assemblies; isolated no-SDK 0 errors / 718 existing
  warnings, full solution 3286 passed / 1 skipped across 22 runnable assemblies. Core has 1436
  passing cases. This batch's source and test files report no new build warnings.
- Linux x64 Debian 13 in a network-disabled container, with the official .NET SDK 10.0.402 and a
  copied local NuGet cache, has 0 errors / 718 existing warnings and 3286 passed / 1 skipped across
  22 runnable assemblies, including 1436 Core cases. Concurrent output interleaved some assembly
  summaries. This is neither a fresh-network restore nor native-solver validation.
- Benchmark and Hexaly have no runnable tests with configured SDKs; Benchmark, Hexaly and MOSEK have
  none in the no-SDK runs. The existing Delaunay precision test remains skipped. Full `git diff --check`
  still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. Raw logs and isolated
  artifacts remain under ignored `logs/`; other external solvers and services were not accepted.

### Previous gate: `If` constraint inputs

- The selected Core suite passes 189 cases, including nine new factory cases and six normalization
  cases. They exercise all four supported relations, exact mechanism rows and matrices, explicit
  bounds, discrete steps, snapshots, nested-function registration, five numeric types, a deep
  expression, unsupported types and inexact constant/coefficient expansion. Two precision cases
  failed before the shared normalization check; the red output is retained in ignored `logs/`.
- A separate pre-fix diagnostic showed `(2^53*x + x) - 2^53*x` normalizing to zero instead of `x`.
  The factory now rejects that source. `IfFunction<V>.From` still cannot validate the provenance of
  a manually supplied normalized input; exact algebra does not prove sequential floating evaluation.
- Explicit opt-in Gurobi 10.0.1 passes 91 selected `If` solves, including eight new normalized-input
  cases at default solver tolerances. This is selected-case evidence, not complete backend acceptance.
- Windows forced Release builds: configured SDKs 0 errors / 743 existing warnings, full solution
  3323 passed / 1 skipped across 23 runnable assemblies; isolated no-SDK 0 errors / 718 existing
  warnings, full solution 3301 passed / 1 skipped across 22 runnable assemblies. Core has 1451
  passing cases. This batch's source and test files report no new build warnings.
- Linux x64 Debian 13 in a network-disabled container, with the official .NET SDK 10.0.402 and a
  copied local NuGet cache, has 0 errors / 718 existing warnings and 3301 passed / 1 skipped across
  22 runnable assemblies, including 1451 Core cases. This is neither a fresh-network restore nor
  native-solver validation. Benchmark and Hexaly have no runnable tests with configured SDKs;
  Benchmark, Hexaly and MOSEK have none in the no-SDK runs. The existing Delaunay precision test
  remains skipped. Full `git diff --check` still reports the pre-existing EOF blank line in
  `LinearConstraintInput.cs`. Raw logs and isolated artifacts remain under ignored `logs/`.

### Previous gate: bounded generic `IfIn`

- The related Core suite passed 191 cases, including 23 `IfInFunctionTest` cases. They cover
  three-valued endpoint/gap behavior, all binary assignments against mechanism rows and matrix
  dumps, singleton intervals, fixed-side ranges, enforced explicit bounds, representative numeric
  types, discrete lattice rejection, nested function registration, fixed-value preparation, stale
  bounds, input snapshots and failure before row insertion. The floating-grouping discrepancy
  described above is reproduced by a passing diagnostic test; it is not an equivalence pass.
- Explicit opt-in Gurobi 10.0.1 passed 109 selected `If`/`IfIn` cases: the 18 new `IfIn` cases include
  14 fixed input/result cases at `FeasibilityTol=1e-9` and four fixed-column substitutions at default
  tolerances. Opting in without the SDK returned the expected missing-SDK build error, not a passing
  integration result. These selected cases do not constitute full Gurobi backend acceptance.
- Windows Release forced solution builds had 0 errors / 743 existing warnings with configured SDKs,
  and 0 errors / 718 existing warnings in the isolated no-SDK configuration. Full tests had 3345
  passed / 1 skipped across 23 runnable assemblies with SDKs and 3323 passed / 1 skipped across 22
  runnable assemblies without SDKs; Core had 1473 passed in both. No warnings were reported in the
  source and test files touched by this batch.
- Linux x64 Debian 13 in a network-disabled container, using the previously verified .NET SDK 10.0.402
  archive and a copied local NuGet cache, had 0 errors / 718 existing warnings and 3323 passed /
  1 skipped across 22 runnable assemblies, including 1473 Core cases. An earlier Linux run preceded
  the additional snapshot test and is not the final gate. This is neither fresh-network restore nor
  native-solver validation.
- Benchmark and Hexaly have no runnable tests with configured SDKs; Benchmark, Hexaly and MOSEK have
  none without SDKs. The existing Delaunay precision test remains skipped. Full `git diff --check`
  still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. Raw logs and isolated
  artifacts remain under ignored `logs/`.

### Previous gate: direct conditional evaluation safety

- The related function, classification and token-registration suite passed 213 cases; the then-new
  `ConditionalEvaluationSafetyTest` contributed 21, including a diagnostic for the rounded nested
  input that the subsequent model-aware preparation batch supersedes. The initial regression run had five failures
  and one pass before the check. Coverage includes all four comparisons, both interval sides,
  subtraction and grouping roundoff, Int64/Flt32/FltX/RtnX cases, harmless rounding, converter
  exceptions and fixed-value cache rollback.
- Windows forced Release builds have 0 errors / 743 existing warnings with configured SDKs and
  0 errors / 718 existing warnings in the isolated no-SDK configuration. Full tests have 3366
  passed / 1 skipped across 23 runnable assemblies with SDKs, and 3344 passed / 1 skipped across
  22 without SDKs; both have 1494 Core cases. The touched files add no reported build warnings.
- Linux x64 Debian 13 in a network-disabled container, using the SHA-512-verified .NET SDK 10.0.402
  archive and a copied local NuGet cache, has 0 errors / 718 existing warnings and 3344 passed /
  1 skipped across 22 runnable assemblies, including all 1494 Core cases. This is neither a
  fresh-network restore nor native-solver validation.
- Explicit opt-in Gurobi 10.0.1 runs 109 previously selected `If`/`IfIn` cases successfully. No new
  native solver scenario was added in this batch. An explicit opt-in without the SDK fails with the
  expected missing-SDK error and is not counted as an integration pass. The selected cases do not
  constitute complete Gurobi acceptance.
- The existing Delaunay precision test remains skipped; Benchmark and Hexaly have no runnable tests
  with configured SDKs, and Benchmark, Hexaly and MOSEK have none without SDKs. Full `git diff --check`
  still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. Other external
  backends, cross-language fixtures and performance were not verified. Raw logs and isolated
  artifacts remain under ignored `logs/`.

### Previous gate: model-aware nested conditional preparation

- The related Core suite passes 304 cases, including 27 nested-preparation cases. The initial
  regression had nine failures and one pass before implementation. Coverage includes all four
  relations, both interval sides, a false combined interval with conflicting side indicators,
  harmless rounding, Int64-to-Flt64 conversion, exact cancellation of overflowing native products,
  incomplete values, stale tokens, explicit helper precedence, same-layer ABS evidence, unchecked ABS
  results, manual cache invalidation and registration rollback.
- Windows forced Release builds have 0 errors / 743 existing warnings with configured SDKs and
  0 errors / 718 existing warnings in the isolated no-SDK configuration. Full tests have 3393 passed /
  1 skipped across 23 runnable assemblies with SDKs, and 3371 passed / 1 skipped across 22 without;
  both include 1521 Core cases. No touched source or test file reports a build warning.
- Linux x64 Debian 13 in a network-disabled container, using the previously SHA-512-verified .NET SDK
  10.0.402 archive and copied local NuGet cache, has 0 errors / 718 existing warnings and 3371 passed /
  1 skipped across 22 runnable assemblies, including 1521 Core cases. One assembly summary interleaves
  with another assembly's output. This is neither a fresh-network restore nor native-solver validation.
- Explicit opt-in Gurobi 10.0.1 passes 109 previously selected `If`/`IfIn` cases. No new native-solver
  case was added. An explicit opt-in without the SDK fails with the expected missing-SDK error and is
  not an integration pass. The selected cases are not complete Gurobi acceptance.
- At that gate only unit-coefficient single-variable ABS provided derived auxiliary proof. Other nested functions
  require explicit variable assignments or leave the condition uncached; direct `TryEvaluate` still
  cannot establish nested provenance or solver feasibility. Other solvers, services, cross-language
  fixtures and performance were not verified.
- The existing Delaunay precision test remains skipped; Benchmark and Hexaly have no runnable tests
  with configured SDKs, and Benchmark, Hexaly and MOSEK have none without SDKs. Full `git diff --check`
  still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. Raw logs and isolated
  artifacts remain under ignored `logs/`.

### Previous gate: bounded generic `IfThen`

- The related Core suite passes 286 cases, including 26 `IfThenFunctionTest` cases covering all four relations and gaps, direct and matrix row
  assignments, negative/positive then values, short-circuit and missing values, exact arithmetic,
  inferred and explicit bounds, nested-result proof, fixed-value preparation, stale caches, input
  snapshots, factory-time range mutation, missing helpers, the normalized input factory and five
  representative numeric types. The initial regression failed to compile against the old non-generic
  placeholder; this is evidence of the missing API, not a behavioral red test.
- Explicit opt-in Gurobi 10.0.1 passes 124 selected `If`/`IfIn`/`IfThen` cases, including 11 new
  fixed-row conditional-value cases at `FeasibilityTol=1e-9` and four fixed-column substitutions at
  default tolerances. Explicit opt-in without the SDK reports the expected missing-SDK failure, not
  an integration pass. These cases do not constitute complete Gurobi acceptance.
- Windows forced Release builds have 0 errors / 743 existing warnings with configured SDKs and
  0 errors / 718 existing warnings without. Full tests pass 3418 / 1 skipped across 23 runnable
  assemblies with SDKs, and 3396 / 1 skipped across 22 without; both include 1546 Core cases.
  The changed source and test files report no new build warnings.
- Linux x64 Debian 13 in a network-disabled container, using the previously SHA-512-verified .NET SDK
  10.0.402 archive and copied local NuGet cache, has 0 errors / 718 existing warnings and 3396 passed /
  1 skipped across 22 runnable assemblies, including 1546 Core cases. An earlier run's Math summary
  interleaved with another test's output; the final run uses the strengthened assertions. This is neither
  fresh-network restore nor native-solver validation.
- The existing Delaunay precision test remains skipped; Benchmark and Hexaly have no runnable tests
  with configured SDKs, and Benchmark, Hexaly and MOSEK have none without. Full `git diff --check`
  still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. Other solvers, external
  services, cross-language execution, broader numeric equivalence and performance were not verified.
  Raw logs and isolated artifacts remain under ignored `logs/`.

### Previous gate: bounded generic `Imply`

- The related Core suite passes 310 cases, including 19 `ImplyFunctionTest` cases. They cover all
  four relations, exact mechanism/matrix assignments, inactive consequents in the business gap,
  short-circuiting and missing values, infeasible fixed branches, five numeric types, bounded inputs,
  nested preparation, required function registration, constant result semantics and preflight failure.
  The initial test did not compile against the former non-generic placeholder; it was an API red light,
  not a behavioral failure.
- Explicit opt-in Gurobi 10.0.1 passes 146 selected `If`/`IfIn`/`IfThen`/`Imply` cases, including
  18 new fixed-row and four fixed-column implication cases. An opt-in without the SDK gives the expected
  missing-SDK error and does not count as a pass. This is not complete Gurobi acceptance.
- Windows forced Release builds have 0 errors / 743 existing warnings with configured SDKs and
  0 errors / 718 existing warnings without. Full tests pass 3436 / 1 skipped across 23 runnable
  assemblies with SDKs and 3414 / 1 skipped across 22 without; both include 1564 Core cases. No
  touched source or test file reports a new warning.
- Linux x64 Debian 13 in a network-disabled container, using the SHA-512-verified .NET SDK 10.0.402
  archive and copied local NuGet cache, has 0 errors / 718 existing warnings and 3414 passed /
  1 skipped across 22 runnable assemblies, including 1564 Core cases. Some summary lines interleave
  with individual test output. This is neither a fresh-network restore nor native-solver validation.
- The existing Delaunay precision test remains skipped; Benchmark and Hexaly have no runnable tests
  with configured SDKs, and Benchmark, Hexaly and MOSEK have none without. Full `git diff --check`
  still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. At that gate direct
  partial-write rollback, native indicators, other solvers and services, cross-language execution and
  performance were not verified. Raw logs and isolated artifacts remain under ignored `logs/`.

### Previous gate: constraint-write transactions

- The Core suite passes 1596 tests. The transaction regressions cover linear and promoted quadratic
  rows, preserved existing-row identity, failure and exception after the second append, fatal error
  propagation, iterator and commit failures, rollback failures, post-write range changes in seven
  function types and integer product, bound-dependency rollback, and a writer without transactions.
- Explicit opt-in Gurobi 10.0.1 passes 313 selected existing cases for `If`, `IfThen`, `Imply`,
  integer product, logical bounds, `SameAs` boundaries and satisfied counts. No new solver case was
  added in this batch; these cases do not constitute complete Gurobi acceptance.
- Forced Windows Release solution builds have 0 errors / 743 existing warnings with configured SDKs
  and 0 errors / 718 existing warnings in the isolated no-SDK configuration. Full tests pass 3468 /
  1 skipped across 23 runnable assemblies with SDKs and 3446 / 1 skipped across 22 without; both
  include 1596 Core cases. No batch-touched source or test file reports a new warning.
- Linux x64 Debian 13 in a network-disabled container, using the SHA-512-verified official .NET SDK
  10.0.402 archive and a copied local NuGet cache, has 0 errors / 718 existing warnings and 3446
  passed / 1 skipped across 22 runnable assemblies, including 1596 Core cases. Some parallel test
  summary lines interleave; all 22 success totals were independently summed. This is neither a
  fresh-network restore nor native-solver validation.
- Benchmark and Hexaly have no runnable tests with configured SDKs; Benchmark, Hexaly and MOSEK
  have none without SDKs. The existing Delaunay precision test remains skipped. Full
  `git diff --check` still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`.
  The row transaction contract does not imply arbitrary callback isolation or token-batch atomicity;
  the subsequent token gate addresses built-in token batches under its separate contract.
  Other solvers and services, cross-language execution and performance were not verified in this
  batch. Raw logs and isolated artifacts remain under ignored `logs/`.

### Previous gate: token batches and registration transactions

- The related token, function-registration and mechanism-preparation suite passes 282 cases; the
  two token-transaction test classes contribute 61. Tests exercise iterator, null and index failures,
  duplicate token identity, automatic-index overflow, direct symbol batches, closed tables, write
  and commit exceptions, rollback failures, shallow shared snapshots and callback ownership, custom
  table opt-in, transaction lifecycle, and restoration of symbols, tokens, indices and caches. The earlier token-batch and
  registration regressions had 8 and 8 failures respectively; after resuming, closed/null single
  symbol checks exposed four more failures before the fix.
- Windows forced Release solution builds have 0 errors / 743 existing warnings with configured SDKs
  and 0 errors / 718 existing warnings with SDKs isolated. Full tests pass 3529 / 1 skipped across
  23 runnable assemblies with SDKs and 3507 / 1 skipped across 22 without; both include 1657 Core
  cases. No touched source or test file reports a build warning.
- Linux x64 Debian 13 in a network-disabled container, using the previously SHA-512-verified official
  .NET SDK 10.0.402 archive and a copied local NuGet cache, has 0 errors / 718 existing warnings and
  3507 passed / 1 skipped across 22 runnable assemblies, including 1657 Core cases. The 22 summary
  counts were independently summed. This is not a
  fresh-network restore or native solver verification.
- Explicit opt-in Gurobi 10.0.1 passes 313 selected existing cases for `If`, `IfThen`, `Imply`,
  integer product, logical bounds, `SameAs` boundaries and satisfied counts. No new solver scenario
  was added, and this is not complete backend acceptance. Opt-in without the SDK fails with the
  expected missing-SDK error and is not counted as an integration pass.
- The existing Delaunay precision test remains skipped. Benchmark and Hexaly have no runnable tests
  with configured SDKs; Benchmark, Hexaly and MOSEK have none without. Full `git diff --check` still
  reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. External services, other
  solvers, cross-language execution and performance were not verified. Raw logs and isolated build
  artifacts remain under ignored `logs/`.
- Token result values and external effects are not rolled back; custom transaction implementations
  are trusted and concurrent/reentrant registration is unsupported. Per-symbol full snapshots may
  be expensive. At this gate both duplicate handling and manual `SolverIndex` allocation differed;
  the subsequent manual-index gate addresses only the latter.

### Previous gate: sequential manual token indices

- The related token, registration, mechanism-preparation and matrix-order tests pass 126 cases.
  They cover four built-in collection types, scalar index collisions, removal gaps, index overflow,
  rollback across a prior gap, parallel table additions, parent add/remove conflicts, delayed
  helpers, and compact linear/quadratic objective, constraint and initial-value ordering. The first
  run of these regressions had 22 failures before the index fix.
- Forced Windows Release builds have 0 errors / 743 existing warnings with configured SDKs and
  0 errors / 718 existing warnings with SDKs isolated. Full tests pass 3544 / 1 skipped across
  23 runnable assemblies with SDKs and 3522 / 1 skipped across 22 without; both include 1672 Core
  cases. No batch-touched source or test file reports a build warning.
- Linux x64 Debian 13 in a network-disabled container, using the previously SHA-512-verified .NET
  SDK 10.0.402 archive and a copied local NuGet cache, has 0 errors / 718 existing warnings and
  3522 passed / 1 skipped across 22 runnable assemblies, including 1672 Core cases. Interleaved
  summary lines were independently counted. This is neither fresh-network restore nor native-solver
  verification.
- Explicit opt-in Gurobi 10.0.1 passes two new binary MIP cases for manual and automatic tables.
  Each checks native column names and MIP start, a constraint row, the optimum and solution
  assignment after a removed token. Missing-SDK opt-in fails with the expected SDK error and is not
  counted as a pass. These cases do not constitute complete Gurobi acceptance.
- The existing Delaunay precision test remains skipped. Benchmark and Hexaly have no runnable tests
  with configured SDKs; Benchmark, Hexaly and MOSEK have none without. Full `git diff --check` still
  reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. Other solver backends,
  external services, cross-language execution and performance remain unverified. Raw logs and
  isolated build artifacts remain under ignored `logs/`.

### Previous gate: duplicate token policy

- Against the fixed Kotlin token list and registration paths, the four built-in collections now
  reject or replace duplicates as configured. Mechanism-only auxiliary re-registration skips known
  keys without replacing captured working-table references. The related token, transaction,
  mechanism and matrix suite passed 157 cases; the initial duplicate-policy regression had eight
  failures before the fix. Tests cover existing and batch-repeated keys, original identity and
  callbacks, cache invalidation, iterator/write/overflow failures, rollback and `Fatal`, checked
  model configuration, linear/quadratic helpers, parent/working replacement and matrix ordering.
- Forced Windows Release solution builds have 0 errors / 743 existing warnings with configured SDKs
  and 0 errors / 718 existing warnings with SDKs isolated. Full tests pass 3575 / 1 skipped across
  23 runnable assemblies with SDKs and 3553 / 1 skipped across 22 without; Core has 1703 passed in
  each. No batch-touched source or test file reports a new build warning.
- Linux x64 Debian 13 in a network-disabled container, with the previously SHA-512-verified official
  .NET SDK 10.0.402 archive and a copied local NuGet cache, has 0 errors / 718 existing warnings and
  3553 passed / 1 skipped across 22 runnable assemblies, including 1703 Core cases. Interleaved
  summaries were independently counted. This is not a fresh-network restore or native-solver check.
- Explicit opt-in Gurobi 10.0.1 passes four selected binary MIP cases, two of them new replacement
  cases; these verify native column order, MIP starts, constraint columns, optimum and solution
  mapping. The no-SDK opt-in fails with the expected missing-SDK error and is not counted as a pass.
  Neither result constitutes complete Gurobi backend acceptance.
- The existing Delaunay precision test remains skipped. Benchmark and Hexaly have no runnable tests
  with configured SDKs; Benchmark, Hexaly and MOSEK have none without. Full `git diff --check` still
  reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. Other solvers, services,
  cross-language execution and performance remain unverified; raw logs remain under ignored `logs/`.

### Previous gate: revision identity and guarded solution writeback

- The related Core suite passes 193 cases. Coverage includes cross-numeric-type revision ownership,
  token replacement and reordering, retained linear/quadratic snapshots and matrices, complete and
  invalid solution vectors, model closure, cache flushing, unreadable inputs and callback failure.
  The owner-collision regression failed before the fix. Before guarded writeback, all ten real
  Gurobi cases replacing the first or last token across five wrapper paths incorrectly succeeded.
- Explicit opt-in Gurobi 10.0.1 passes 31 selected tests, including 15 cases across five wrapper
  paths with no replacement, first-token replacement or last-token replacement. An opt-in build
  without the SDK fails with the expected missing-SDK error; it is not an integration pass. A
  temporary fake Hexaly backend compiles the column-generation source and passes nine wrapper cases;
  this is not real Hexaly SDK or solver validation. Neither check constitutes backend acceptance.
- Forced Windows Release solution builds have 0 errors / 743 existing warnings with configured SDKs
  and 0 errors / 718 existing warnings with SDKs isolated. Full tests pass 3604 / 1 skipped across
  23 runnable assemblies with SDKs and 3582 / 1 skipped across 22 without; Core has 1732 passed in
  each. No source or test file touched by this batch reports a build warning.
- Linux x64 Debian 13 in a network-disabled container, using the previously SHA-512-verified official
  .NET SDK 10.0.402 and a copied local NuGet cache, has 0 errors / 718 existing warnings and 3582
  passed / 1 skipped across 22 runnable assemblies, including 1732 Core cases. Parallel output
  interleaves some summary lines. This is neither a fresh-network restore nor native-solver validation.
- The existing Delaunay precision test remains skipped. Benchmark and Hexaly have no runnable tests
  with configured SDKs; Benchmark, Hexaly and MOSEK have none without. Full `git diff --check` still
  reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. Other backends, external
  services, cross-language execution and performance remain unverified; raw logs are under ignored
  `logs/`.
- At this gate the guard checked neither complete structural invalidation nor feasibility or
  optimality, and did not restore eliminated fixed columns. The later fixed-column continuation adds
  explicit restoration, but direct setters, no-incumbent/Farkas/dual evidence and concurrent or
  reentrant mutation remain unprotected. Shared snapshots other than the source and parent need their
  own cache management; callback failure can leave partially changed values.

### Previous gate: fixed-column restoration and concurrent identifiers

- The related Core suite after the identifier fix passes 87 cases, including the new concurrent
  identifier regression and fixed-column and mechanism-preparation cases. The earlier restoration
  selection passed 104 cases. Restoration coverage includes interleaved, all-fixed and no-fixed
  columns, empty models, negative zero, invalid vectors, wrong source, changed columns and tokens,
  closed models, callbacks and the built-in scalar-variable garbage-collection case. The first
  restoration red run failed to compile because the API was absent; it was not a behavioral red test.
- The first Linux full run built successfully but failed one Core preparation case: an unknown value
  was unexpectedly cached. A separate pre-fix concurrency regression produced only 11,331 distinct
  identifiers from 40,000 variable/symbol allocations. Duplicate variable keys are consistent with
  that failure, but the original Linux failure was not instrumented to prove the exact interleaving.
  The regression and the originally failed preparation case pass after atomic allocation.
- Explicit opt-in Gurobi 10.0.1 passes 27 selected cases, including eight new linear/quadratic
  fixed-column cases, all-fixed feasible/infeasible models, stale source replacement and linear
  solution-pool restoration. This is not full backend acceptance. Explicit opt-in without the SDK
  fails with the expected missing-SDK error and is not counted as an integration pass. Eight new
  opt-in-only style warnings were fixed before the final selected run; that test file now reports none.
- Forced Windows Release solution builds have 0 errors / 743 existing warnings with configured SDKs
  and 0 errors / 718 existing warnings with SDKs isolated. Full tests pass 3625 / 1 skipped across
  23 runnable assemblies with SDKs and 3603 / 1 skipped across 22 without; Core has 1753 passed in
  each. The new restoration and identifier files have no reported build warnings.
- Linux x64 Debian 13 in a network-disabled container, using the previously SHA-512-verified official
  .NET SDK 10.0.402 and a copied local NuGet cache, has 0 errors / 718 existing warnings and 3603
  passed / 1 skipped across 22 runnable assemblies, including 1753 Core cases. The test projects ran
  serially; their internal test concurrency remains enabled. This is neither a fresh-network restore
  nor native-solver validation.
- The full-gate snapshots preceded the explicit-type-only changes to the Gurobi integration test;
  those lines are excluded from the default configurations on both operating systems. After the
  edit, the selected opt-in tests and both Windows default Gurobi test-project configurations were
  rerun (27, 23 and 4 passed respectively).
- The existing Delaunay precision test remains skipped. Benchmark and Hexaly have no runnable tests
  with configured SDKs; Benchmark, Hexaly and MOSEK have none without. Full `git diff --check` still
  reports the pre-existing EOF blank line in `LinearConstraintInput.cs`. Other backends, services,
  cross-language execution and performance remain unverified; raw logs are under ignored `logs/`.

### Historical gate: Gurobi Benders and snapshot-read safety

- The related Core selection passes 169 cases, including checks on both sides of input reads,
  delayed auxiliary registration and invalid fixed maps. The source-before-input regression failed
  in both linear and quadratic cases before the fix. The transaction-boundary test distinguishes
  early rejection of non-finite values from unknown/out-of-domain values that fail after a helper
  was committed; it documents a gap, not successful rollback.
- Explicit opt-in Gurobi 10.0.1 passes 48 selected cases, including the new fixed-column,
  callback-failure, Farkas, stale-snapshot and delayed-helper scenarios. Thirteen initial Benders
  regressions and a constraint-reference identity regression failed before their fixes. These cases
  are not full backend acceptance; opt-in without the SDK fails explicitly as expected and is not
  counted as an integration pass. The default SDK-backed Gurobi test project was rebuilt after the
  opt-in run and passes its 23 default cases.
- Forced Windows Release builds have 0 errors / 743 warnings with configured SDKs and 0 errors /
  718 warnings with SDKs isolated. Full tests pass 3639 / 1 skipped across 23 runnable assemblies
  with SDKs, and 3617 / 1 skipped across 22 without; Core has 1767 passes in each. The Delaunay
  precision case remains skipped. No batch-touched source or test file reports a build warning.
- Linux x64 Debian 13 in a network-disabled container, using the previously SHA-512-verified
  .NET SDK 10.0.402 archive and a copied local NuGet cache, builds with 0 errors / 718 warnings and
  passes 3617 / 1 skipped across 22 runnable assemblies, including 1767 Core cases. This is neither
  a fresh-network restore nor native-solver validation. The final source changes after the full
  Windows build are comments only; the Gurobi opt-in selection was rerun afterward.
- Benchmark and Hexaly have no runnable tests with configured SDKs; Benchmark, Hexaly and MOSEK
  have none without. Full `git diff --check` still reports the pre-existing EOF blank line in
  `LinearConstraintInput.cs`; the files touched by this batch have no check errors. Raw logs are
  under ignored `logs/`.
- At this gate the matrix dump and mechanism construction were separate transactions; the later
  joint build-and-dump gate fixes the tested Gurobi dump-failure case. Native solve or callback
  failure after a successful dump can still retain helper tokens. Farkas row multipliers are not a
  complete independently verified certificate, cuts remain unavailable, and the snapshot checks do
  not cover arbitrary structure changes. Other solvers, services, cross-language execution and
  performance are not accepted by this historical gate.

### Previous gate: joint build-and-dump safety

- The related Core selection passes 250 cases. It covers linear and quadratic models, manual and
  automatic token tables, invalid fixed values and unsupported bounds, delayed helpers, captured
  mappings, observed cancellation, unreadable inputs, parent callback changes, and pre-commit
  `Failed`/`Fatal`/exception/null results. Before the fix, both Gurobi unknown-fixed-variable and
  out-of-domain delayed-helper regressions failed; the parent-change preservation tests already
  passed. Raw red and green logs are under ignored `logs/`.
- Explicit opt-in Gurobi 10.0.1 passes 52 selected cases, including fixed delayed helpers and the
  documented native-callback failure after a successful dump. These are not full backend acceptance.
  Opt-in without the SDK reports the expected missing-SDK error, not a pass; afterward the isolated
  default Gurobi project passes four tests. The SDK-backed default project is rebuilt after opt-in
  and passes its 23 default tests as part of the full solution run.
- Forced Windows Release builds have 0 errors / 743 existing warnings with configured SDKs and
  0 errors / 718 existing warnings with SDKs isolated. Full tests pass 3682 / 1 skipped across
  23 runnable assemblies with SDKs, and 3660 / 1 skipped across 22 without; Core has 1810 passes
  in each. The SDK-backed full gate preceded a documentation-only XML remarks edit clarifying the
  cancellation checkpoint. No batch-touched source or test file reports a build warning.
- Linux x64 Debian 13 in a network-disabled container, using the previously SHA-512-verified .NET
  SDK 10.0.402 archive and a copied local NuGet cache, builds with 0 errors / 718 existing warnings
  and passes 3660 / 1 skipped across 22 runnable assemblies, including 1810 Core cases. This is not
  a fresh-network restore or native solver validation.
- The Delaunay precision test remains skipped. Benchmark and Hexaly have no runnable tests with
  configured SDKs; Benchmark, Hexaly and MOSEK have none without. Full `git diff --check` still
  reports the pre-existing EOF blank line in `LinearConstraintInput.cs`; the batch files checked
  separately have no whitespace errors.
- Joint build/dump is not a transaction around the native solve or arbitrary callback effects;
  cancellation is cooperative, and old two-step calls still commit before dumping. Other backends,
  services, cross-language execution, complete structural invalidation and performance remain
  unverified. P2 and the overall synchronization remain incomplete.

### Historical gate: direct source and variable snapshot safety

- Core Release passes 1845 tests, including linear and quadratic source-row/objective changes,
  direct monomial replacement, symbol addition/removal, fixed-column and large-integer range
  changes, read-time mutation, fresh dumps after unrelated range changes, snapshot-time unreadable custom bounds,
  registration/pre-commit callbacks and delayed-helper writeback. The original Core structure tests
  had 12 behavioral failures before the guard; a later dump-specific red run had 13 failures. An
  initial full run exposed seven old fixtures that construct mechanisms directly without initializing
  the new snapshot; the fixtures were updated and the full suite rerun, rather than skipping them.
- Explicit Gurobi 10.0.1 opt-in passes 71 selected cases, including five framework solve paths,
  Farkas rejection after source changes, fixed delayed helpers and a two-solution pool control.
  The original red run showed 12 stale-result successes; its three continuous-model pool cases
  instead failed for an unrelated solver error, so they were corrected to use a binary pool and are
  not counted as pre-fix stale-result demonstrations. An intermediate green attempt also exposed and
  fixed a false rejection of committed delayed helpers. These tests are not full backend acceptance.
- Forced Windows Release builds have 0 errors / 743 existing warnings with configured SDKs and
  0 errors / 718 existing warnings with SDKs isolated. Full runs pass 3717 / 1 skipped across 23
  runnable assemblies with SDKs, and 3695 / 1 skipped across 22 without; Core has 1845 passes in
  each. Explicit opt-in with an isolated missing Gurobi SDK fails with the expected diagnostic and
  is not an integration pass.
- Linux x64 Debian 13 in a network-disabled container, using the previously SHA-512-verified
  official .NET SDK 10.0.402 archive and a copied NuGet cache, builds with 0 errors / 718 existing
  warnings and passes 3695 / 1 skipped across 22 runnable assemblies, including 1845 Core cases.
  This is neither a fresh-network restore nor native-solver acceptance.
- After the full gates, positional calls in the snapshot guard and its test were changed to multiline
  named arguments without changing their operations. The final Core selection passes 266 cases and
  the Gurobi opt-in selection passes 71; the configured-SDK default Gurobi project was then rebuilt
  and passes 23 cases. The full Linux and Windows gates were not repeated for those formatting edits.
- The Delaunay precision test remains skipped. No batch-touched source or test file reports a build
  warning; full `git diff --check` still reports the pre-existing EOF blank line in
  `LinearConstraintInput.cs`. Raw logs are under ignored `logs/`. Complete structure and matrix-row
  invalidation, concurrent/reentrant safety, independent Farkas proof, other real backends, external
  services, cross-language execution and performance remain unverified. P2 and overall synchronization
  remain incomplete.

### Previous gate: mechanism-row and matrix snapshot safety

- The new Core regression suite has 46 cases. Its first 30 cases all failed before the shared
  guards were added. The expanded suite covers added and replaced rows, successful rollback, custom
  cells, unreadable or changing coefficients at dump time, both matrix row representations,
  metadata, objective cells, fixed columns, signed zero, disposal, read-time mutation and weak
  ownership. A final related Core selection passes 98 cases.
- Gurobi 10.0.1 explicit opt-in passes 79 selected cases, including 12 new real-solver controls
  and stale mechanism-row cases over five feasible wrapper paths and the Benders Farkas path. Four
  feasible wrapper cases returned stale successes in the initial red run; the existing Benders
  subproblem already rejected the tested row change. The default Gurobi project was rebuilt after
  opt-in and passes 23 cases. Missing-SDK opt-in fails with the expected diagnostic, not an
  integration pass. These cases are not complete backend acceptance.
- A temporary fake Hexaly solver passes 15 wrapper cases, including matrix-batch disposal and
  mechanism-row mutation. This checks the wrapper source, not the real Hexaly SDK or license.
- Windows Release builds report 0 errors and 743 existing warnings with configured SDKs, and 0
  errors and 718 existing warnings with SDKs isolated. Full tests pass 3763 / 1 skipped across 23
  runnable assemblies with SDKs and 3741 / 1 skipped across 22 without, including 1891 Core cases.
  The configured-SDK forced build preceded the last two identity tests, which were compiled by the
  full test run. Full Windows gates preceded a test-argument formatting change and two explanatory
  source comments; the 98-case Core and 79-case Gurobi selections were rerun afterwards.
- Linux x64 uses a network-disabled local `postgres:16` image based on Debian 13 solely as an OS
  image, the previously SHA-512-verified official .NET SDK 10.0.402 and a copied NuGet cache. It
  builds with 0 errors / 718 existing warnings and passes 3741 / 1 skipped across 22 runnable
  assemblies, including 1891 Core cases. An initial run on plain `debian:13` stopped before build
  because ICU was absent; no database service was used. This is not fresh-network restore or
  native-solver acceptance.
- The existing Delaunay precision case remains skipped. No batch-touched source or test file
  reports a build warning; full `git diff --check` still reports the pre-existing EOF blank line
  in `LinearConstraintInput.cs`. Raw logs are in ignored `logs/`. Other real solvers, Hexaly,
  external services, cross-language execution and performance remain unverified. P2 and overall
  synchronization remain incomplete.

### Previous gate: sparse matrix ownership and revisions

- Forty-two additional Core cases cover changed-and-restored entries and row lists through generic
  and non-generic collection paths, scaling, shared rows, constructor ownership, failed removal,
  zero and signed zero, and mutation while reading a solution. The 148-case related Core selection
  passes. The initial restored-change/ownership selection had 24 failures and two passing controls;
  the four zero-retention cases failed before the `Add` change.
- Gurobi 10.0.1 explicit opt-in passes 83 selected cases. Six new real-solver cases include controls
  and restored sparse changes for linear and quadratic feasible solves and a linear infeasible
  solve; the checks use the original directly dumped matrix. The selected tests do not establish
  full Gurobi backend acceptance or a complete infeasibility certificate. Missing-SDK opt-in
  reports the expected error; the default isolated no-SDK Gurobi project passes four cases.
- Windows Release forced builds complete with 0 errors and 743 existing warnings with the configured
  SDKs, and 0 errors and 718 existing warnings with SDKs isolated. Full tests pass 3805 / 1 skipped
  across 23 runnable assemblies with SDKs, and 3783 / 1 skipped across 22 without; Core has 1933
  passing cases in each run.
- Linux x64 Debian 13 runs network-disabled in the local `postgres:16` OS image, using the
  previously SHA-512-verified .NET SDK 10.0.402 and a copy of the local NuGet cache. Its forced build
  completes with 0 errors / 718 existing warnings, and 3783 / 1 skipped tests pass across 22 runnable
  assemblies, including 1933 Core cases. No database service or fresh-network restore is involved.
- The existing Delaunay precision case remains skipped. No batch-touched source or test file reports
  a build warning; full `git diff --check` still reports the pre-existing EOF blank line in
  `LinearConstraintInput.cs`. Raw logs are in ignored `logs/`. Other real solvers, real Hexaly,
  external services, cross-language execution and performance remain unverified. P2 and overall
  synchronization remain incomplete.

### Previous gate: matrix collection ownership

- Twenty-one additional Core cases cover caller-owned list and array inputs, generic and
  non-generic read-only interfaces, both batch-copy disposal orders, shallow custom cells and
  rejected writes during solution reads. Existing tests now assert that these writes are blocked;
  they continue to test source mismatch, independently supplied column mismatches, signed-zero
  snapshot comparison, actual sparse/row changes and disposal. The first six independent ownership
  cases failed before the change. The final related Core selection passes 137 cases.
- Gurobi 10.0.1 explicit opt-in passes 93 selected cases, including four new real-solver controls
  for directly constructed linear and quadratic models with and without later input-list changes.
  The tests also verify that attempts to mutate exposed collections in a modeling callback are
  rejected. Missing-SDK opt-in fails with the expected diagnostic and is not an integration pass;
  the isolated default no-SDK project passes four cases. Afterward, the configured-SDK default
  project was rebuilt and passes 23 cases. These selections do not establish complete Gurobi or
  other backend acceptance.
- Forced Windows Release builds complete with 0 errors and 743 existing warnings with configured
  SDKs, and 0 errors and 718 existing warnings with SDKs isolated. Full tests pass 3826 / 1 skipped
  across 23 runnable assemblies with SDKs, and 3804 / 1 skipped across 22 without; Core has 1954
  passing cases in each run.
- Linux x64 Debian 13 runs network-disabled in the local `postgres:16` OS image, using the
  previously SHA-512-verified official .NET SDK 10.0.402 and a copy of the local NuGet cache. Its
  forced build completes with 0 errors / 718 existing warnings and 3804 / 1 skipped tests pass
  across 22 runnable assemblies, including 1954 Core cases. No database service or fresh-network
  restore is involved.
- The existing Delaunay precision case remains skipped. No batch-touched source or test file reports
  a build warning; full `git diff --check` still reports the pre-existing EOF blank line in
  `LinearConstraintInput.cs`. Raw logs are in ignored `logs/`. At this gate, separate `Lhs` and `SparseLhs`,
  malformed row counts, shared batch disposal, custom mutable elements, concurrent/reentrant use,
  complete proofs, other real solvers, external services, cross-language execution and the cost of
  copying remain unverified or unresolved. P2 and overall synchronization remain incomplete.

### Current gate: sparse-backed rows and shape checks

- The related Core selection passes 174 cases. Twenty-nine new Core cases cover live outer views,
  immutable held rows, shared vectors, row insertion/removal/replacement, batch copying, signed zero,
  the quadratic linear-term sentinel, each metadata count, priorities, empty batches and disposal.
  Existing matrix-snapshot tests retain restored-write invalidation, read-time mutations and
  transaction rollback. The first two projection tests failed against the stale one-time view.
- Gurobi 10.0.1 explicit opt-in passes 107 selected cases, including 14 new controls for linear and
  quadratic coefficients, valid binary solution pools, invalid row counts before solver creation,
  and row insertion in the environment callback on single and pool paths. In the initial red run,
  the four single-solve cases incorrectly succeeded; the four pool cases used continuous models and
  failed with an unrelated solver error, so they were changed to binary and are not evidence of
  pre-fix pool success. Missing-SDK opt-in reports the expected diagnostic and is not an
  integration pass. The selected real-solver cases do not constitute full backend acceptance.
- Four MOSEK shape cases fail in the initial run because disposal tries to load the unavailable
  managed runtime before returning the shape error; all four pass when validation precedes solver
  construction. These cases use no native environment and are not MOSEK solver acceptance. A
  temporary fake Hexaly API compiles the linear and quadratic source and passes six shape controls;
  it is not a real SDK, license or solver test.
- Forced Windows Release builds complete with 0 errors / 743 existing warnings with configured
  SDKs and 0 errors / 718 existing warnings with SDKs isolated. Full tests pass 3859 / 1 skipped
  across 23 runnable assemblies with SDKs and 3833 / 1 skipped across 22 without; Core has 1983
  passing cases in each. The configured-SDK default Gurobi project passes 23 cases after the opt-in
  run; the isolated no-SDK project passes four. Existing MOSEK smoke tests can return early when its
  runtime is absent, so the full test count is not real MOSEK acceptance.
- Linux x64 Debian 13 runs network-disabled in the local `postgres:16` OS image, using the
  previously SHA-512-verified official .NET SDK 10.0.402 and a copy of the local NuGet cache. Its
  forced build completes with 0 errors / 718 existing warnings, and 3833 / 1 skipped tests pass
  across 22 runnable assemblies, including 1983 Core cases. No database service or fresh-network
  restore is involved.
- The Delaunay precision case remains skipped. No batch-touched source or test file reports a
  build warning; full `git diff --check` still reports the pre-existing EOF blank line in
  `LinearConstraintInput.cs`. Raw logs are in ignored `logs/`. Shared derived-batch disposal and
  independent derived-batch mutation, arbitrary custom solvers, invalid coefficient/column values,
  concurrent/reentrant mutation, complete proofs, other real backends, external services,
  cross-language execution and projection performance remain unresolved or unverified. P2 and
  overall synchronization remain incomplete.

### Current gate: continuous-relaxation ownership and snapshots

- Against pinned Kotlin `core/model/intermediate/LinearTriadModel.kt`, `LinearConstraintBatch.copy()`
  and `BasicLinearTriadModel.copy()` independently copy rows. Kotlin also carries identity metadata,
  origins and priorities that the C# batch does not yet fully represent; this batch does not claim
  that parity. C# `LinearTriadModel.RelaxToContinuous()` replaces column kinds only, preserves bounds
  and column origins, copies the currently represented row data, and owns its batch independently.
  It records the actual managed relaxation snapshot and rejects invalid shape, disposal, or a detected
  source mutation during creation. This does not establish a convex hull for arbitrary variable kinds.
- Gurobi column-generation LP and fixed-column Benders subproblems now validate both the original
  dumped matrix and the relaxation they actually solve before restoring/applying a solution or
  publishing dual/Farkas results. Read-time mutation and sparse writes followed by restoration are
  rejected without writing token results. Five other adapters copy batches in their LP and Benders
  relaxation helpers, but their linear solver paths still return fixed environment failures; their
  result-validation and real-backend work remain open.
- The Core targeted selection passes 143 cases, including ten new cases for both disposal orders,
  unrelated relaxations, source and solved-row mutations, restored sparse writes, read-time changes,
  and invalid/closed matrices. The initial red run failed to compile because the new relaxation and
  snapshot overloads did not yet exist; it is not a behavioral pre-fix failure count. Twenty shared
  helper tests across the five unavailable adapters exercise both paths and disposal orders through
  their private helpers; they are lifecycle tests, not solver acceptance.
- Explicit opt-in with Gurobi 10.0.1 passes 450 selected integration cases, including nine new
  controls for LP primal/dual values, Benders/LP rejection after source changes during dual extraction,
  and original/relaxed row mutations after native feasible or infeasible solves. The mutation tests
  exercise the snapshot API around a direct Gurobi solve; the wrapper callbacks do not expose their
  private relaxed matrices. Benders Farkas row multipliers are not a complete independently checked
  certificate including variable bounds. Opt-in without the SDK reports the expected missing-SDK
  error; it is not a pass. The default configured-SDK Gurobi project was rebuilt and passes 23 cases.
- Forced Windows Release builds produce 0 errors / 743 existing warnings with configured SDKs and
  0 errors / 718 existing warnings with SDKs isolated. The full default tests pass 3889 / 1 skipped
  across 23 runnable assemblies with SDKs, and 3863 / 1 skipped across 22 without; Core has 1993
  passing cases in each. Linux x64 Debian 13 runs network-disabled in the local `postgres:16` OS
  image using the previously SHA-512-verified .NET 10.0.402 SDK and local NuGet cache: 0 errors /
  718 existing warnings and 3863 / 1 skipped across 22 runnable assemblies. This is not native Linux
  backend or external-service acceptance. After the full gates, ownership documentation and an
  unused import changed, two opt-in-only tests were added, and the Benders solution vector's existing
  defensive copy was restored; the relevant Core and Gurobi selections were rebuilt and rerun.
- The existing Delaunay precision case remains skipped. Full `git diff --check` still reports the
  pre-existing EOF blank line in `LinearConstraintInput.cs`; batch-touched files have no whitespace
  errors. Snapshots do not validate native model changes,
  arbitrary concurrent/reentrant mutation, feasible solutions or proofs; changes followed by restoration
  outside tracked sparse collections may remain invisible. Directly supplying the same batch to multiple
  model constructors still shares disposal, and custom objects remain shallow. Copying and snapshot
  scanning have not been benchmarked. Other real backends, cross-language interoperability and external
  services remain unverified. P2 and overall synchronization remain incomplete.

#### Continuous-relaxation migration

Use `RelaxToContinuous()` and check its `Result` before solving; dispose both the original model and
the returned relaxation. When applying/restoring a result, pass the original dumped matrix and the
actual relaxation to the new overloads; use the corresponding snapshot overload before publishing
evidence without a solution. Direct model constructors take ownership of the supplied constraint batch:
do not hand the same instance to two models; copy the batch or create a relaxation instead. Existing
single-matrix overloads validate only the original dumped matrix. Callers must not modify models while
solving, and validation is not a substitute for checking native solver callbacks or certificates.

中文迁移提示：调用 `RelaxToContinuous()` 后先检查 `Result`，并分别释放原模型和松弛模型；
恢复或回填结果时同时传入原矩阵和实际求解的松弛矩阵，无解证据也要使用对应的双矩阵快照检查。
直接构造模型会接管约束批次，不要将同一批次交给多个模型；应先复制批次或创建松弛模型。原
单矩阵重载只校验原矩阵。求解期间不得并发修改模型，快照也不验证原生回调更改或完整证明。

At this earlier gate P2 was not complete. Of 49 concrete function types, 16 had generic ABS/extremum/rounding/comparison/
range-distance/product/integer-product/limited-conditional APIs, 18 other types had Flt64 behavior,
and 15 remained placeholders. Remaining nested lowering and function prechecks, factory-time callback
isolation, atomic solve failures and build/dump integration beyond the selected Gurobi Benders path,
custom model/writer validation, complete matrix metadata
and export, deferred conditional functions, native-indicator structures, quadratic function semantics,
and remaining natural-name generic APIs also need work.
Full token-list parity, arbitrary callback isolation and all external backends remain open.
Linux native solver capabilities, other solver capabilities and external services remain explicitly
unverified.

### Previous gate: identity provenance ownership

- `ModelElementIdentity` now snapshots its provenance as a read-only collection, including direct
  construction and `with` assignments. Registered bindings, `Entries()` and derived identities no
  longer expose their internal provenance list for mutation. This is a C# safety difference, not a
  claim that the pinned Kotlin collection prevents all mutation. Equality and hashing compare the
  ordered provenance values with the other identity fields, matching Kotlin's list value equality;
  .NET hash codes are not portable or stable serialization digests.
- Registration now returns a failure for a null element, null or blank provenance entries, or a
  provenance collection that cannot be read. The registry itself does not replace an existing
  fallback or binding on those failures. It does not undo external side effects or support concurrent
  or reentrant mutation of a supplied custom collection. Direct construction or a `with` assignment
  does not validate an identity and propagates exceptions from an unreadable source collection;
  use the registry's Result-returning registration for untrusted input. The non-Result lookup methods
  retain their existing null-argument behavior.
- Six new Core tests cover input and returned-collection aliasing, direct construction and `with`,
  default and derived provenance, ordered equality and hashing, invalid input and fallback retention.
  Three initial alias/error tests and a separate equality regression failed before their respective
  fixes. The related report, linear-state and CP selection passes 107 cases; that run preceded a
  test-only analyzer fix, which was included in all full gates below.
- Forced Windows Release builds complete with 0 errors / 743 existing warnings with configured SDKs
  and 0 errors / 718 existing warnings with SDKs isolated. The full default tests pass 3895 / 1
  skipped across 23 runnable assemblies with SDKs, and 3869 / 1 skipped across 22 without; Core has
  1999 passing cases in both. Linux x64 Debian 13 uses the local `postgres:16` system image without a
  database service, the previously verified .NET SDK 10.0.402 archive, a local NuGet cache and no
  network: 0 errors / 718 existing warnings and 3869 / 1 skipped across 22 runnable assemblies,
  including 1999 Core cases. These are default build/contract gates, not native Linux solver or
  external-service acceptance. Only the public API exception-boundary comment was added after the
  full gates; the 14 identity-registry tests were rebuilt and rerun afterward.
- The existing Delaunay precision test remains skipped. No touched source or test file adds a build
  warning; their whitespace was checked separately because they are untracked. The full
  `git diff --check` still reports the pre-existing EOF blank line in `LinearConstraintInput.cs`.
  Identity-list copying and value comparison have not been benchmarked. Raw logs are in ignored
  `logs/`; no real solver integration was rerun for this batch.
- This batch does not propagate IDs or provenance into linear or quadratic matrix variables,
  constraints, or objectives. Aggregate-objective identity, matrix identity validation and
  identity-change invalidation remain open. P2 and overall synchronization are not complete.

#### Identity provenance migration

Treat `ModelElementIdentity.Provenance` as read-only; create a new identity with a new list instead
of mutating a previously returned binding. Equal identity values now compare equal even when their
provenance lists are different instances; order remains significant. Handle failed registration for
null or unreadable input and do not rely on hash codes across processes. Identity construction is
not itself validation, direct construction or `with` propagates collection-read exceptions, and
external callbacks are not rolled back.

中文迁移提示：`ModelElementIdentity.Provenance` 现在为只读快照，需要修改时应以新列表创建
新的身份，而不是改写已返回的绑定。不同列表实例只要来源内容及顺序相同、其他字段一致，身份
便相等；不要将哈希值用于跨进程持久化。注册空对象或无法读取的来源集合时应处理失败结果；
直接构造或 `with` 赋值不等于通过注册校验，来源集合读取异常会向外传播；不可信输入应使用
返回 `Result` 的注册表入口。外部回调副作用不会回滚。

### Previous gate: direct matrix identities

- Linear and quadratic direct dumps carry registered variable, row and objective identities into
  the matrix. Fixed columns are omitted; copies and continuous relaxations preserve identity
  metadata. With complete stable objective sources, an aggregate identity is formed using the pinned
  Kotlin ordering and UTF-8 encoding; otherwise the aggregate is model-local. Anonymous matrices
  without a registry remain supported.
- `ValidateIdentity()` checks row shape, identity kind and scope, origins and provenance,
  namespace/schema agreement and duplicate IDs across element kinds. The existing Gurobi, MOSEK and
  Hexaly native entry points call it before environment creation and writing. Copies in the five
  unavailable adapters also retain variable identity; this is not backend implementation or solver
  acceptance.
- Failed direct dumps roll back newly resolved fallback identities and their counters. A new explicit
  binding invalidates earlier solution mappings, including when the shared registry is used by an
  unrelated model; idempotent registration and fallback lookup do not. At this gate, outer
  build-and-dump commit failure could still retain fallback identities; the following gate addresses
  that case. These checks do not support concurrent or reentrant model mutation.
- JVM UTF-8 replaces an isolated UTF-16 surrogate with `?`. To avoid stable identity collisions,
  C# rejects such registered IDs and downgrades derived identities containing malformed UTF-16 to
  model-local scope. This is a deliberate safety difference from the pinned Kotlin baseline.
- Forced Windows Release builds completed with 0 errors / 743 existing warnings with configured SDKs
  and 0 errors / 718 existing warnings with SDKs isolated. Default tests passed 3940 / 1 skipped
  across 23 assemblies with SDKs and 3910 / 1 skipped across 22 without; Core passed 2040 in both.
  Offline Linux x64 Debian 13, using the local `postgres:16` OS image without a database service,
  the previously verified .NET 10.0.402 SDK and a copied NuGet cache, built with 0 errors / 718
  existing warnings and passed 3910 / 1 skipped across 22 assemblies (Core 2040). This is not a
  fresh restore or native Linux solver acceptance.
- Following the full gates, only a `using` order changed in `QuadraticTetradModel.cs`. The final
  identity selection was rebuilt and passed 55 Core cases. Gurobi 10.0.1 explicit opt-in passed
  six selected cases: four pre-environment checks and two real linear solves, including rejection
  of stale writeback. This is not complete Gurobi acceptance. Hexaly was compiled with the local
  SDK, but no real Hexaly solve was verified.
- The existing Delaunay precision case remains skipped. No identity-related batch file reported a
  new build warning; full `git diff --check` still reports the earlier EOF blank line in
  `LinearConstraintInput.cs`. Raw logs are in ignored `logs/`. Other real backends, full
  cross-language interoperability and performance remain unverified. P2 and overall synchronization
  remain incomplete.

#### Matrix identity migration

Call `ValidateIdentity()` for custom solver writers and check its result before consuming a matrix;
it validates the matrix, not the source model or a solution. Before writeback or publishing evidence,
also use the appropriate `ValidateSolverTokenSnapshot()` or guarded solution API with the original
dumped matrix and, when applicable, the actual solved relaxation. Rebuild after an explicit registry
binding changes. Supply well-formed Unicode IDs and complete provenance when stable identity is
required; do not assume incomplete aggregate objectives are stable. Calls that mutate models or
registries concurrently or reentrantly are outside this contract.

中文迁移提示：自定义求解器 writer 消费矩阵前应检查 `ValidateIdentity()`；它只检查矩阵，
不检查来源模型或求解结果。回填或发布证据前还须传入原始转储矩阵，必要时同时传入实际求解的
松弛矩阵，调用相应的 `ValidateSolverTokenSnapshot()` 或受保护的解恢复接口。显式绑定变化后
应重新转储；稳定 ID 须使用有效 Unicode 和完整来源，不完整的聚合目标仅具模型内身份。
不支持并发或重入修改模型和注册表。

### Previous gate: coordinated identity and token commit

- In both linear and quadratic build-and-dump paths, fallback identity resolution, the final source
  check and auxiliary-token commit now share one synchronous registry transaction. A failed or
  fatal preparation, an exception, a changed source or failed token commit restores fallback
  bindings, ID reservations and collision counters to their pre-dump state. Existing bindings and
  external callback side effects are not rolled back. A successful commit retains the identities
  used by the returned matrix. This is a C# safety strengthening, not a claim that the pinned Kotlin
  registry implements the same coordinated transaction.
- During identity resolution, a reentrant attempt to add or promote a stable binding returns a
  structured failure; an identical existing binding remains idempotent. This prevents a rollback
  from leaving stable bindings disconnected from their ID index. Nested resolution and exception
  paths release the guard; model mutation during build remains unsupported, and callback side effects
  are not isolated.
- Nineteen added Core cases cover the public linear/quadratic build paths in both token modes,
  failure/fatal/exception/null preparation, source changes, token-commit failure, preserved external
  changes, retry, nested reentrant binding and exception recovery. The four public commit-failure
  cases initially leaked fallback entries; the two initial reentrant cases incorrectly allowed new
  bindings. Both red runs are retained under ignored `logs/`. The final related Core selection passes
  248 cases, and all 19 cases are included in the full gates below.
- Windows Release forced builds completed with 0 errors / 743 existing warnings with configured SDKs
  and 0 errors / 718 existing warnings with SDKs isolated. Full tests passed 3959 / 1 skipped across
  23 assemblies with SDKs and 3929 / 1 skipped across 22 without; Core passed 2059 in both. Offline
  Linux x64 Debian 13 used the local `postgres:16` OS image without a database service, the previously
  verified .NET SDK 10.0.402 and a copied NuGet cache: 0 errors / 718 existing warnings and 3929 / 1
  skipped across 22 assemblies, including 2059 Core cases. This is not a fresh network restore or
  native Linux backend acceptance.
- Explicit Gurobi 10.0.1 opt-in passed 457 selected cases, including the new Benders control: an
  induced commit failure returns before native modeling, restores fallback identities and leaves
  helper tokens uncommitted, then a retry completes a real solve. Missing-SDK opt-in produced the
  expected diagnostic and is not a pass. The configured-SDK default Gurobi project was rebuilt and
  passed 27 cases afterward. The selection does not establish complete backend or independent
  Farkas-certificate acceptance.
- The existing Delaunay precision case remains skipped. No batch source or test file added a build
  warning; batch whitespace checks passed, while the full `git diff --check` still reports the
  earlier `LinearConstraintInput.cs` EOF blank line. Registry locking and snapshot-copy costs were
  not measured. Concurrent/reentrant model mutation, arbitrary external callback isolation,
  failures after a successful build-and-dump commit, other real backends, full interoperability
  and performance remain unresolved or unverified. P2 and overall synchronization remain incomplete.

#### Coordinated identity migration

Register new stable identities before model build/dump and handle a failed `Result`; do not add new
stable bindings reentrantly from variable getters or other callbacks while identities are being
resolved. Repeating an identical existing registration remains valid. A failed coordinated build
restores its newly resolved fallback identities and uncommitted helpers, but cannot undo callback
side effects or a solver failure after successful commit. Continue to validate solver snapshots
before applying results or publishing evidence.

中文迁移提示：应在构建或转储前注册新的稳定身份，并检查 `Result`；身份解析期间不要从变量
读取器等回调重入新增稳定绑定。同一既有绑定的幂等注册仍允许。联合构建失败会回滚本次新增的
回退身份及未提交辅助变量，但不会撤销外部回调副作用，也不会撤销成功提交之后的求解失败。
回填结果或发布证据前仍须校验求解快照。

### Previous gate: relation-aware Sigmoid

| Kotlin source | C# entry point | Behavioral evidence | Status |
|---|---|---|---|
| `Sigmoid.kt`, `DiscreteCondition.kt`, `Conditional.kt` | `SigmoidFunction<V>.Create`, `Classify`, `LinearFunctionSymbolAdapter<V>` | `SigmoidFunctionTest`, `ConstraintWriteTransactionTest`, `SigmoidGurobiIntegrationTest` | Bounded linear fallback contract verified; selected Gurobi 10.0.1 solves verified; deferred/native indicator remains a gap |

- The former parameterless Flt64 placeholder is replaced by a generic binary step function, not a
  logistic curve. GT, GE, LT and LE use the same three-valued relation and strict gap as the pinned
  Kotlin source. Missing inputs fail `Classify` but yield `Ok(null)` from `TryEvaluate`; undefined
  gap values also yield `Ok(null)`. Arithmetic or conversion failures return a failed result.
- The implementation delegates bounded lowering, exact expansion, lattice checks, model-aware nested
  preparation, token registration and transactional row writes to the existing `IfFunction<V>`.
  Explicit bounds are enforced on the input; inferred bounds are tracked and invalidate dependent
  models when changed. The legacy `bigM` is checked for a positive finite solver value but never
  substitutes for bounds. The fallback currently adds three helpers and six rows, rather than Kotlin's
  single indicator and deferred/native structure. This is not native-writer parity.
- C# validates at `Create` instead of postponing validation to registration. It inherits If's
  conservative exact-conversion, safe-lattice and nested-source checks and may reject inputs that
  the Kotlin baseline accepts. Direct classification/evaluation does not enforce declared bounds or
  verify nested provenance. In a real solver the default `1e-10` gap can be smaller than feasibility
  tolerance; callers must select a gap appropriate to the solver and model scale. EQ/NE are unsupported.
- A Sigmoid result can be used by another condition and participate in a solve. At this gate its
  indicator was not published as helper evidence. The later conditional evidence gate adds a checked
  subset; missing evidence can still leave a downstream condition uncached. Neither cache is a solve.
- Twenty-six new Core cases cover all four relations and gaps against mechanism and matrix rows,
  Flt64/Flt32/Int64/FltX/RtnX, legacy parameters, invalid ranges and proofs, input snapshots, explicit
  bounds, stale inferred bounds, helper conflicts, missing helpers, nested preparation, and failed,
  fatal or throwing writes with rollback in linear and quadratic models. The nested-consumer assertion
  was updated by the subsequent evidence gate. The related Core selection passes 272 cases. The initial test run could
  not compile against the former non-generic placeholder;
  it demonstrates the missing API, not a behavioral red test.
- Windows Release forced builds finished with 0 errors / 743 existing warnings using configured SDKs,
  and 0 errors / 718 existing warnings with SDKs isolated. Full default tests passed 3985 / 1 skipped
  across 23 assemblies with SDKs and 3955 / 1 skipped across 22 without; Core passed 2085 in both.
  Offline Linux x64 Debian 13 used the previously SHA-512-verified .NET 10.0.402 SDK, a copied NuGet
  cache and the local `postgres:16` OS image without starting a database service: 0 errors / 718
  existing warnings, and 3955 / 1 skipped across 22 assemblies, including 2085 Core cases. This is
  neither a fresh network restore nor Linux native-backend acceptance.
- The Gurobi 10.0.1 explicit opt-in passed 48 real-solve cases for the four relation boundaries,
  infeasible gaps, fixed-column substitution, a nested ABS input and a Sigmoid result used by another
  condition. With the SDK isolated, explicit
  opt-in reported the expected missing-SDK diagnostic; that failure is not a pass. These cases do not
  establish complete Gurobi acceptance. The default configured-SDK project subsequently passed its
  27 tests in the full solution gate.
- The existing Delaunay precision test remains skipped. No batch source or test file added a build
  warning; the repository-wide whitespace check still reports the earlier EOF blank line in
  `LinearConstraintInput.cs`. Full interoperability, other real backends, deferred/native writers,
  other function placeholders and performance remain unverified. P2 and overall synchronization
  remain incomplete.

#### Sigmoid migration

Replace `new SigmoidFunction()` with `SigmoidFunction<V>.Create(condition, converter, ...)`, inspect
the `Result`, and register the returned function through `LinearFunctionSymbolAdapter<V>`. Supply
finite inferred or explicit bounds and a positive, solver-appropriate `strictBoundary`; `delta`
defaults to the gap, while `tolerance` supplies the gap only when it is omitted. An accepted legacy
`bigM` does not supply bounds or change the formulation. Existing code must not assume a single helper,
Kotlin's helper names or native indicator support. Check the result of `Classify` or `TryEvaluate` and
distinguish missing input, undefined gaps and numeric failure as appropriate. A downstream condition
may remain uncached when its helper evidence cannot be verified, even though its model can be solved.

中文迁移提示：原无参 `SigmoidFunction` 占位已移除，改用 `SigmoidFunction<V>.Create` 传入
条件多项式和转换器，检查 `Result` 后通过 `LinearFunctionSymbolAdapter<V>` 注册。需要有限
范围和结合求解器容差及模型尺度选定的正 `strictBoundary`；`delta` 默认等于间隔，未提供间隔
时才使用 `tolerance`。合法的旧 `bigM` 仅校验，不提供范围或改变建模。不要依赖只有一个辅助
变量、Kotlin 的辅助变量名称或原生指示约束。调用 `Classify` 或 `TryEvaluate` 时须区分输入
缺失、间隔内未定义和数值失败；直接求值不验证声明范围或嵌套来源。下游条件虽能在模型中
使用 Sigmoid 结果，证据无法核验时仍可能不缓存下游结果。

### Previous gate: nested conditional preparation evidence

| Kotlin source | C# entry point | Behavioral evidence | Status |
|---|---|---|---|
| `TokenTableRegistrationSupport.kt`, `Conditional.kt`, `Sigmoid.kt` | `TokenTableRegistrationExtensions.Register`, `IfFunction<V>`, `SigmoidFunction<V>` | `NestedConditionalEvidenceTest`, `NestedConditionalPreparationSafetyTest`, `SigmoidGurobiIntegrationTest` | Selected binary-branch cache preparation verified; C# revalidation is a safety difference, not native-writer or complete nested-function parity |

- Token registration now derives helper evidence for a prepared `If` or `Sigmoid` only when the
  current direct and expanded expressions, native and Flt64-encoded classifications, declared
  condition bounds, conversion of row coefficients, and any supplied local helper values agree.
  `Abs` remains restricted to the existing single-variable, zero-offset, unit-coefficient subset.
  Before each model-aware consumer, earlier evidence is rechecked in dependency order; stale
  tracked ABS bounds, changed condition sources or conversions, and conflicting local helpers
  suppress derived evidence. Explicit variable values still take precedence. This revalidation
  is a C# safety measure; the pinned Kotlin registration code does not claim the same mechanism.
- Evidence is an assumption for no-solution symbol-cache preparation, not a solution, a feasibility
  certificate, a fixed column or a linear-relaxation proof. Explicit assignments themselves are not
  proven feasible, `InvokeAsync(... fixedVariables ...)` does not validate variable domains or
  eliminate columns, and other functions such as `IfIn`, `IfThen` and `Imply` are not newly promoted
  to derived evidence. Missing or unprovable inputs may still leave a downstream cache empty.
  A later callback can invalidate an already prepared cache; the checks do not isolate arbitrary
  concurrent/reentrant mutation, restore external side effects, or prove solver tolerance behavior.
- Forty-seven new Core cases cover the four comparisons, both branch values, independent and
  multi-layer chains, `IfIn`/`IfThen`/`Imply` consumers, linear and quadratic model construction, five representative numeric types,
  exact versus rounded bounds, missing and gap inputs, known-helper conflicts, stale tokens,
  source/range/converter changes before consumption, and registration rollback. The original
  36-case selection had 24 failures and 12 passes before implementation; a later reverse-coefficient
  conversion regression independently failed before its fix. The related Core selection before the
  four additional consumer cases passed 183; the consumer selection separately passes 4.
- Gurobi 10.0.1 explicit opt-in passes 49 selected cases. Two existing nested solves now also check
  preparation caches and retained columns; the new continuous-relaxation control obtains objective 0
  while the prepared binary-branch cache is 1. Explicit opt-in without the SDK produces the expected
  missing-SDK diagnostic, not a pass; the isolated default Gurobi project subsequently passes 4.
  These selected cases do not establish complete Gurobi acceptance.
- Windows forced Release builds, before the last four test-only cases, completed with 0 errors and
  the same 743 configured-SDK / 718 isolated-SDK warnings as the previous gate. With those four
  cases included, the full default tests pass 4032 / 1 skipped across 23 assemblies with configured
  SDKs and 4002 / 1 skipped across 22 without; both include 2132 Core cases. No batch file introduced
  a build warning. The pre-existing Delaunay precision case remains skipped.
- Linux x64 Debian 13 uses the previously SHA-512-verified .NET SDK 10.0.402, the local `postgres:16`
  OS image without a running database, a read-only local NuGet cache and no network: forced Release
  build has 0 errors / 718 existing warnings and default tests pass 4002 / 1 skipped across 22
  assemblies, including 2132 Core cases. An initial attempt used the wrong NuGet cache path and failed
  restore with NU1101; it is not counted. This is not a fresh network restore or Linux native-backend
  acceptance.
- Batch whitespace checks pass; the repository-wide check still reports the earlier EOF blank line in
  `LinearConstraintInput.cs`. Rebuilding evidence and repeatedly checking exact expansions incur
  unmeasured costs. Other real backends, full cross-language interoperability, deferred/native
  writers, other function placeholders and performance remain unverified. P2 and the overall sync
  are incomplete.

#### Conditional evidence migration

Do not use a prepared condition cache as an incumbent, a bound, an LP value or a fixed-column
substitution. Preserve and verify actual solver results and select a solver-appropriate gap. Code
which previously observed an empty downstream cache may now observe a verified binary-branch value;
code must still handle empty caches when inputs, current bounds, conversions or helper evidence
cannot be checked. Avoid mutating models, converters or fixed-value maps during registration.

中文迁移提示：`If`/`Sigmoid` 的二值辅助结果仅在直接输入和展开值、原生值与求解器编码、
条件范围、行系数转换及已给出的辅助值均一致时供下游缓存准备使用。消费前按依赖顺序重新
检查证据；显式变量值优先，证据不足时下游仍可能不缓存。该缓存不是 incumbent、可行性证明、
LP 解或固定列替换；`fixedVariables` 本身也不验证值域或消列。不要在注册期间并发或重入修改
模型、转换器或固定值映射；后续回调仍可能使已准备的缓存过期。`IfIn`、`IfThen`、`Imply`
等尚未纳入本批派生证据，求解器容差和其他后端仍须单独验收。

### Current gate: univariate piecewise linear function

| Kotlin source | C# entry point | Behavioral evidence | Status |
|---|---|---|---|
| `UnivariateLinearPiecewise.kt`, `UnivariateLinearPiecewiseFunctionDedicatedTest.kt`, `UnivariateLinearPiecewiseFailureBoundaryTest.kt` | `UnivariateLinearPiecewiseFunction<V>.Create`, `UnivariateLinearPiecewiseFunction.FromPoints`, `LinearFunctionSymbolAdapter<V>` | `UnivariateLinearPiecewiseFunctionTest`, `UnivariatePiecewiseLifecycleTest`, `ConstraintWriteTransactionTest`, `UnivariatePiecewiseGurobiIntegrationTest` | Selected bounded linear-fallback and dependency-lifecycle contracts and Gurobi 10.0.1 solves verified; deferred/native writer and full backend parity remain gaps |

- The former parameterless Flt64 placeholder is replaced with a validated generic function over
  strictly increasing breakpoints, segment slopes and intercepts. `FromPoints` derives the latter
  with floating-point arithmetic. The function snapshots its input monomials and parameter lists,
  exposes an outward result envelope, and writes the linear graph with an input helper, a result
  helper, selectors and complementary inactive binaries. The additional helpers and factory-time
  validation differ from the pinned Kotlin implementation. Automatic Big-M requires a provably
  finite input range and is rounded outward for every inactive segment; the sufficiency of an
  explicit Big-M remains the caller's responsibility.
- Every segment is closed. At a discontinuous shared breakpoint the model admits either adjacent
  segment's result, while direct evaluation chooses the first segment; outside the breakpoints it
  returns no value. `TryEvaluate` distinguishes numeric failure from missing or out-of-domain input.
  Sampling-point slope and intercept arithmetic may round, especially under cancellation, and does
  not promise exact interpolation of all supplied coordinates. Solver feasibility tolerances can
  also affect the graph. Native and solver-value conversions, exact expansion and captured-bound
  checks are deliberately conservative and can reject inputs accepted by Kotlin.
- Model-aware preparation publishes a result only for a unique segment when direct and expanded
  input values, native and encoded values, all rows, current bounds and any known helper values can
  be checked. Earlier evidence is revalidated before a downstream consumer. Shared breakpoints are
  excluded even when the two segments happen to agree; insufficient explicit Big-M or a changed
  conversion also suppresses evidence. The cache is not an incumbent, feasibility certificate, LP
  value or fixed-column substitution, and later callbacks or concurrent/reentrant mutation can still
  invalidate already prepared caches.
- Thirty-eight new Core cases cover graph rows, discontinuities, input and parameter snapshots,
  factory and numeric boundaries, five representative number types, outward rounding, finite and
  stale bounds, nested inputs, helper conflicts, evidence revalidation and sample-point creation.
  Eight further cases cover failed, fatal or throwing row writes and helper changes during writes
  in linear and quadratic models. The related Core selection passes 205 cases. A regression for a
  changed negated-slope conversion failed before its fix by caching a downstream result of 1.
- Gurobi 10.0.1 explicit opt-in passes 17 selected real-solve cases for graph membership, a
  discontinuous breakpoint under both objectives, fixed-column substitution, nested ABS and If,
  and insufficient explicit Big-M. With the SDK isolated, explicit opt-in fails with the expected
  missing-SDK diagnostic; this expected failure is not an integration pass. The default configured
  Gurobi project passes 27 tests in the full solution gate. These results are not complete backend
  acceptance.
- Windows forced Release builds finish with 0 errors and 743 existing warnings with configured SDKs,
  and 0 errors and 718 existing warnings with SDKs isolated in a separate artifacts directory.
  Default tests pass 4078 / 1 skipped across 23 assemblies and 4048 / 1 skipped across 22,
  respectively; Core passes 2178 in both. The isolated default Gurobi project is rechecked after the
  missing-SDK opt-in. The Hexaly and Benchmark test projects discover no tests in either setup;
  the isolated MOSEK project also discovers none. These projects are not counted as passing test
  assemblies. No batch source or test file adds a build warning.
- Offline Linux x64 Debian 13 uses the previously SHA-512-verified .NET SDK 10.0.402, a copy of the
  read-only local NuGet cache and the local `postgres:16` OS image without starting a database:
  forced Release build has 0 errors / 718 existing warnings and default tests pass 4048 / 1 skipped
  across 22 assemblies, including 2178 Core cases; Hexaly, MOSEK and Benchmark discover no tests.
  This is not a fresh network restore or Linux native-backend acceptance. The existing Delaunay
  precision test remains skipped. Batch whitespace
  checks pass; the repository-wide check still reports the earlier EOF blank line in
  `LinearConstraintInput.cs`. Other real backends, full interoperability, native writers and
  performance remain unverified. P2 and overall synchronization remain incomplete.

#### Piecewise migration

Replace `new UnivariateLinearPiecewiseFunction()` with `UnivariateLinearPiecewiseFunction<V>.Create`
or `UnivariateLinearPiecewiseFunction.FromPoints`, check the `Result`, and register the returned
function through `LinearFunctionSymbolAdapter<V>`. Supply a finite input range for automatic Big-M;
if providing Big-M explicitly, validate its sufficiency for the whole model. Do not rely on helper
counts or names, exact sampling-point interpolation, a unique model output at discontinuous shared
breakpoints, or a prepared cache as a solve result. Handle absent evaluation values and numeric
failures separately through `TryEvaluate`, and avoid changing bounds, expressions or converters
during registration.

中文迁移提示：删除原无参 `UnivariateLinearPiecewiseFunction` 占位入口，改用
`UnivariateLinearPiecewiseFunction<V>.Create` 或 `UnivariateLinearPiecewiseFunction.FromPoints`，
检查 `Result` 后通过 `LinearFunctionSymbolAdapter<V>` 注册。自动 Big-M 需要可证明有限的
输入范围；显式 Big-M 的充分性由调用方校验。各段均为闭区间，不连续公共断点处模型可以
选择相邻任一段，而直接求值选前一段。采样点的浮点运算可能舍入，不保证逐点精确插值；
公共断点暂不发布派生缓存证据，即使两段结果相同。缓存不是求解结果、可行性证明或固定
列消元。调用 `TryEvaluate` 时区分缺值、区间外和数值失败，并避免在注册期间修改范围、
表达式或转换器。C# 的精确转换和展开检查可能拒绝 Kotlin 接受的输入。

### Current gate: bivariate piecewise linear function

| Kotlin source | C# entry point | Behavioral evidence | Status |
|---|---|---|---|
| `BivariateLinearPiecewise.kt`, `BivariateLinearPiecewiseFunctionDedicatedTest.kt`, `TrigonometricAndBivariateGenericEvaluateTest.kt` | `BivariateLinearPiecewiseFunction<V>.Create`, `LinearFunctionSymbolAdapter<V>` | `BivariateLinearPiecewiseFunctionTest`, `ConstraintWriteTransactionTest`, `BivariatePiecewiseGurobiIntegrationTest` | Selected triangle-graph contract and Gurobi 10.0.1 solves verified; full structural versioning, other backends and complete function parity remain gaps |

- The former parameterless placeholder is replaced by a checked generic factory. It snapshots
  the input monomial lists and triangle coordinates, rejects non-finite, lossy or unsupported
  conversions and triangles with an exact absolute determinant at or below `1e-12`, and writes
  the graph transactionally using three percentage variables and one selector per triangle.
  `double.MaxValue` and its negative are also rejected. Evaluated inputs must be representable in
  both the native and solver-value forms; the interpolated result must convert back exactly.
- Direct evaluation first evaluates the input polynomials in `V`, then computes barycentric
  interpolation using exact rational arithmetic and rounds the resulting value to the nearest
  Flt64, ties to even. Thus it avoids determinant overflow but does not make the preceding `V`
  polynomial evaluation exact. Exact determinant and boundary comparisons may differ from the
  pinned Kotlin implementation's intermediate floating-point operations. The baseline's `1e-12`
  membership tolerance is retained; consequently direct evaluation can accept a point just
  outside the model's triangle. With overlapping triangles direct evaluation selects the first,
  while the integer model may choose any matching surface.
- **Intentional baseline difference:** Kotlin's `calculateBarycentric` returns weights of vertices
  1 and 2, but `evaluate` applies them as weights of vertices 2 and 3. For vertices
  `(0,0,2)`, `(1,0,10)`, `(0,1,20)`, Kotlin directly evaluates `(0,0)` as `10` while its model
  gives `2`. C# uses the corresponding vertex weights and returns `2`; it matches the graph, not
  that Kotlin direct-evaluation defect. This is not bit-for-bit behavioral compatibility.
- Fixed-input preparation only publishes a cache with a unique matching triangle, consistent
  direct and expanded inputs, exact encoded output and no conflicting known helpers. A downstream
  consumer receives barycentric evidence only if the weights themselves are exactly representable;
  it revalidates that evidence before use. Shared or overlapping surfaces and insufficient evidence
  may therefore leave a cache empty. A cache is not an incumbent, feasibility proof, LP value or
  fixed-column substitution.
- Built-in linear and quadratic mechanism models retain a transactional dependency check for this
  function. A changed nested expansion, conversion or helper range rejects a new dump and an old
  solution; checks also run after pre-commit preparation, rolling back newly prepared tokens and
  fallback identities on failure. External callback mutations themselves are not undone. This is
  a value comparison, not a monotonic structural revision: changing a dependency and restoring it
  can make an old snapshot acceptable again. The univariate function now has the same kind of
  targeted check, but other functions, arbitrary nested source expressions, custom mechanism
  implementations and concurrent or reentrant mutation are not generally covered.
- Fifty-four Core cases cover the bivariate function, transactional row writes and dependency
  rollback, including vertex and interior values, orientation, midpoint rounding, subnormal values,
  degeneracy, lossy conversion, conflicting helpers, nested preparation and stale-input handling.
  The related Core selection passes 473 cases. Four nested-input invalidation cases failed before
  their fix; the corrected pre-commit regression had four failures and two passing range controls
  before the commit check was extended. External callback effects remain outside that rollback.
- Gurobi 10.0.1 explicit opt-in passes 533 selected integration cases, including 14 bivariate cases
  for vertices, interior and exterior points, overlapping surfaces, fixed columns, nested
  functions, LP relaxation and stale source rejection after a real solve. The missing-SDK opt-in
  fails with the expected diagnostic; that expected failure is not an integration pass. These
  selected cases do not constitute complete Gurobi backend acceptance.
- Windows forced Release builds have 0 errors and 743 existing warnings with configured SDKs,
  and 0 errors and 718 existing warnings with the Gurobi, MOSEK, Hexaly, COPT and CPLEX build
  discovery variables removed and separate artifacts. The full default runs pass 4132 / 1 skipped
  across 23 assemblies and 4102 / 1 skipped across 22, respectively; Core passes 2232 in both.
  Hexaly and Benchmark discover no tests in either setup; MOSEK also discovers none with SDK
  discovery removed. Those projects are not counted as passing test assemblies.
- Offline Linux x64 Debian 13 uses the previously SHA-512-verified .NET SDK 10.0.402, a copy of
  the read-only local NuGet cache and the local `postgres:16` OS image without starting a database.
  The forced Release build has 0 errors / 718 existing warnings; default tests pass 4102 / 1 skipped
  across 22 assemblies, including 2232 Core cases. Hexaly, MOSEK and Benchmark discover no tests.
  This is not a fresh network restore, a Linux native-backend acceptance or an external-service test.
- The pre-existing Delaunay precision test remains skipped. Batch whitespace and named-argument
  checks pass; the repository-wide whitespace check still reports the earlier EOF blank line in
  `LinearConstraintInput.cs`. No batch source or test file adds a build warning. The repeated
  exact-arithmetic and dependency scans have not been benchmarked. Other real backends, full
  interoperability, deferred/native writers, remaining placeholders and performance are not
  accepted; P2 and the overall synchronization remain incomplete.

#### Bivariate migration

Replace `new BivariateLinearPiecewiseFunction()` with `BivariateLinearPiecewiseFunction<V>.Create`,
check its `Result`, and register the returned function through `LinearFunctionSymbolAdapter<V>`.
Provide nondegenerate triangles and exact, solver-representable coordinates. Handle missing and
out-of-domain values separately from numeric failures through `TryEvaluate`; do not assume a unique
model output on overlapping surfaces or use the prepared cache as a solver result. Rebuild models
after changing inputs, bounds or converters and do not rely on mutation-and-restore detection.

中文迁移提示：原无参 `BivariateLinearPiecewiseFunction` 占位入口改为
`BivariateLinearPiecewiseFunction<V>.Create`，检查 `Result` 后通过
`LinearFunctionSymbolAdapter<V>` 注册。输入多项式先按 `V` 运算，插值再用精确有理数计算并
按最近偶数舍入为 Flt64；精确数值转换或退化阈值不满足时返回失败。直接求值保留几何容差，
可能接受模型边界外的点；重叠三角形处直接求值选第一个，整数模型可选择任一片面。固定
Kotlin 基线将顶点 1、2 的权重错误用于顶点 2、3；C# 按模型图和对应顶点修正，不是逐位兼容。
下游缓存证据只在可证明且重心值精确可表示时发布，并非求解结果或可行性证明。内置模型对
本函数重查嵌套输入、转换器和辅助范围，提交前失败回滚新准备的 token 与回退身份，但不回滚
外部回调副作用；单变量分段函数现也有针对性检查，但修改后还原、任意嵌套表达式、其余
函数及并发或重入修改尚无完整版本保证。

### Current gate: univariate piecewise dependency lifecycle

- Built-in linear and quadratic mechanism models now retain the univariate function's dependency
  check after its rows are registered. The check re-expands its nested input, revalidates the
  captured helper ranges and, when using automatic Big-M, input bounds, and verifies the converter's
  zero and one as well as both signs of breakpoints, slopes, intercepts, Big-M and flattened row
  values, including coefficients merged from repeated input variables. A stale dependency rejects
  a new matrix dump, old matrix validation and solution application. A change during row writes
  rolls back those rows; a change before model commit rolls back newly prepared tokens and fallback
  identities. External callback side effects are not rolled back.
- This is a targeted safety check, not a monotonic structural version: a changed dependency that is
  restored may make the old snapshot valid again. It does not protect arbitrary expressions or
  other functions, custom mechanism implementations, or concurrent/reentrant mutations. It does
  not establish the sufficiency of caller-supplied Big-M, prove feasibility, or make cached values
  solver results. Numeric comparison treats positive and negative zero as equal, so it does not
  detect a sign-only zero conversion change. Rebuild after changing an input, bound or converter;
  do not rely on restoration.
- Fifty-one new Core cases cover linear and quadratic nested-input and converter invalidation,
  both signs of row values, merged coefficients, explicit and automatic Big-M, identity values,
  write rollback and pre-commit token/identity rollback. The initial 36-case regression failed
  34 cases before the fix; two bound-change controls already passed. Two subsequent merged-row
  regressions also failed before their fix. The related Core selection passes 265 cases. Gurobi
  10.0.1 explicit opt-in passes 22 univariate cases, including five real-solve controls for
  unchanged dependencies and changes to the nested input, converter or merged coefficient.
  With its SDK isolated, explicit opt-in gives the expected missing-SDK error; this is not a
  backend acceptance pass.
- Configured Windows Release rebuild reports 0 errors / 743 existing warnings, and the default
  suite passes 4183 with one existing skipped test across 23 test assemblies. With SDK discovery
  variables removed and separate artifacts, the build reports 0 errors / 718 existing warnings
  and the default suite passes 4153 with one skipped across 22 assemblies. Core passes 2283 in
  each. Hexaly and Benchmark discover no tests in either setup; MOSEK also discovers none when
  isolated, and those projects are not counted as passing assemblies. No batch file adds a build
  warning. The earlier `LinearConstraintInput.cs` end-of-file blank line remains the only
  repository-wide whitespace finding. Repeated exact expansion and row checks are not benchmarked.
- Offline Linux x64 Debian 13, using the previously verified .NET SDK 10.0.402, a read-only local
  NuGet cache copy and the local `postgres:16` OS image without starting a database, builds with
  0 errors / 718 existing warnings and passes 4153 tests with one skipped across 22 assemblies,
  including 2283 Core cases. Hexaly, MOSEK and Benchmark discover no tests. This is not a fresh
  network restore, Linux native-backend acceptance or an external-service test.

中文说明：内置线性和二次模型现在持续复核单变量分段函数的嵌套输入、辅助范围、自动
Big-M 的输入范围，以及转换器中零、一和断点、斜率、截距、Big-M 的正反值；失效时拒绝
重新转储、旧矩阵校验及旧解回填；展开行的合并系数也纳入复核。写行中失效会回滚新增约束，提交前失效会回滚新辅助
token 和回退身份，但不回滚外部回调自身的副作用。该检查不是单调结构版本，修改后还原
可能重新接受旧快照；不覆盖任意表达式、其他函数、自定义模型或并发/重入修改。显式
Big-M 的充分性仍由调用方负责；数值比较将正负零视为相等，不检测仅改变零符号的转换。
建议变更输入、范围或转换器后重建模型。Sin/Cos、延迟和原生 writer、其他真实后端、完整
互通及性能尚未完成或验收，P2 和全量同步均未完成。

### Previous gate: sampled sine and cosine

| Pinned Kotlin source | C# entry point | Contract coverage | Status |
|---|---|---|---|
| `Sin.kt`, `Cos.kt`, `TrigonometricAndBivariateGenericEvaluateTest.kt` | `SinFunction<V>.Create`, `CosFunction<V>.Create`, `LinearFunctionSymbolAdapter<V>` | `TrigonometricFunctionTest`, `TrigonometricGurobiIntegrationTest` | Representable sampled graph and selected Gurobi 10.0.1 solves verified; subsequent `FltX` work is recorded in the current gate below |

- The previous parameterless placeholders are replaced by checked generic factories. Both use the
  pinned Kotlin five-point defaults on `[-pi, pi]`; they are piecewise-linear approximations, not
  direct trigonometric evaluation, and are not extended periodically. Custom `Flt64` points are
  snapshotted, and their slopes and intercepts are computed in `Flt64` before conversion to `V`.
  Invalid, unordered, non-finite or unrepresentable points and nonzero values lost to conversion
  return a structured failure. As with the shared piecewise function, rounding can prevent exact
  interpolation at a sample, adjacent closed segments can disagree at a breakpoint, and direct
  evaluation chooses the first segment while the model can select either.
- The implementation reuses the existing piecewise token and transactional row lifecycle,
  model dependency checks and conservative fixed-input evidence. A downstream condition can
  consume revalidated evidence; a shared breakpoint or conflicting helper may suppress caching.
  Such a cache is not an incumbent, feasibility proof, LP solution or fixed-column elimination.
  Built-in models reject stale nested inputs, bounds and converters, subject to the same
  value-comparison, mutation-and-restore, callback and concurrency limitations documented for
  univariate piecewise functions.
- At this earlier gate, this was deliberately **not full generic parity**: the piecewise implementation required
  exact solver encoding and finite inferred input bounds. The default `FltX` points fail because
  decimal coefficients do not generally have an exactly equal `Flt64` encoding; default integer
  conversion loses nonzero slopes and is rejected. Exact custom `FltX` and integer samples work.
  Default `Flt64`, `Flt32` and `RtnX` are covered at zero and during model construction, not for
  every possible value. Kotlin's deferred structure and native writer remain unimplemented.
  That limitation is superseded for `FltX` by the current gate below, while default integers and
  Kotlin's deferred/native writers remain incomplete.
- Thirty-seven Core cases cover default and custom samples, generic boundaries, snapshots,
  conversion ordering, helper collisions, downstream evidence and linear/quadratic invalidation;
  the related selection passes 331. The initial red run did not compile because the generic APIs
  were missing; it is not evidence of a behavioral test failure. Gurobi 10.0.1 opt-in passes 40
  selected tests, including 18 new real solves for the default graph and custom downstream
  condition; this does not constitute full backend acceptance. The default Gurobi test project
  is rebuilt afterward and passes 27 cases. Explicit opt-in without the SDK reports the expected
  missing-SDK error, which is not counted as an integration pass.
- Windows forced Release builds have 0 errors and 743 existing warnings with configured SDKs, or
  0 errors and 718 existing warnings with solver discovery variables removed and separate
  artifacts. The default suites pass 4220 / 1 skipped across 23 assemblies and 4190 / 1 skipped
  across 22 assemblies, respectively; Core passes 2320 in both. Hexaly and Benchmark discover no
  tests in either setup; MOSEK also discovers none without SDK discovery. No batch source or test
  file adds a warning.
- Offline Linux x64 Debian 13 uses the previously verified .NET SDK 10.0.402, a local NuGet cache
  copy and the `postgres:16` OS image without starting a database. The forced build has 0 errors
  and 760 warnings: the same 718 existing warnings plus 42 `NU1900` warnings because the offline
  restore could not contact NuGet's vulnerability service. The default suite passes 4190 / 1
  skipped across 22 assemblies, including 2320 Core cases; Hexaly, MOSEK and Benchmark discover
  no tests. This is not a network restore, vulnerability audit, Linux native-solver or external
  service acceptance.
- The pre-existing Delaunay precision test remains skipped. Batch whitespace checks pass; the
  repository-wide check still reports the earlier `LinearConstraintInput.cs` EOF blank line.
  At this earlier gate, performance, other real backends, complete interoperability, decimal
  default samples and the remaining function implementations were not accepted.

#### Sin/Cos migration

Replace `new SinFunction()` / `new CosFunction()` with the corresponding `SinFunction<V>.Create`
or `CosFunction<V>.Create`, check the returned `Result`, then register its value through
`LinearFunctionSymbolAdapter<V>`. Supply a finite-bounded input and, if needed, ordered `Flt64`
sampling points whose converted coefficients pass the applicable encoding checks. Handle missing/out-of-domain
values and numeric failures separately via `TryEvaluate`; do not treat the prepared cache as a
solver result. Rebuild the model after changing an input, bound or converter.

中文迁移提示：原无参 `SinFunction` / `CosFunction` 占位入口改为泛型 `Create`，检查
`Result` 后通过 `LinearFunctionSymbolAdapter<V>` 注册。默认点仅近似 `[-pi, pi]`，不会作
周期扩展；自定义 Flt64 采样点先在 Flt64 计算斜率和截距再转换。输入须有限，默认 `FltX`
已支持双数值图校验；会把非零斜率转为零的整数转换仍返回失败，这不是 Kotlin 泛型能力全量对齐。下游缓存仅在证据充分时发布，并非求解
结果；内置模型的失效检查和回滚沿用单变量分段函数的既有边界。延迟及原生 writer、其他
求解器、完整互通和性能仍未验收。

### Previous gate: decimal piecewise and trigonometric graphs

| Pinned Kotlin source | C# entry point | Contract coverage | Status |
|---|---|---|---|
| `UnivariateLinearPiecewise.kt`, `Sin.kt`, `Cos.kt` | `UnivariateLinearPiecewiseFunction<FltX>.Create`, `SinFunction<FltX>.Create`, `CosFunction<FltX>.Create` | `DecimalPiecewiseFunctionTest`, `TrigonometricGurobiIntegrationTest` | Default decimal evaluation and linear/quadratic mechanism construction verified; production generic-to-`Flt64` matrix conversion and full parity remain incomplete |

- Decimal points, slopes and intercepts no longer have to equal their binary encodings exactly.
  Both representations must be finite and independently valid, breakpoint order must remain strict
  in both, and negation must agree in each representation. The auxiliary input range and output
  envelope cover both graphs and the rounded native endpoint values; automatic Big-M covers each
  inactive segment over the actual outward auxiliary input and output ranges in both graphs. The
  selected `FltX` magnitude and its `Flt64` encoding must both meet the computed bound. Explicit
  Big-M sufficiency remains the caller's responsibility. Input polynomial expansion and non-`FltX`
  piecewise values retain their previous exact-encoding checks.
- Direct evaluation uses decimal arithmetic. The encoded solver graph can differ because decimal
  and binary values and arithmetic are not identical, including at shared closed breakpoints. A
  prepared result is cached only when the same unique segment, output equality, all inactive rows,
  bounds and known helper values are proven in both representations. A shared breakpoint, rounded
  result or conflicting helper may suppress caching. Prepared values are not incumbents, feasibility
  proofs, LP solutions or fixed-column elimination.
- Registration checks both native and encoded row expansion. Built-in linear and quadratic models
  also compare the model converter with the function converter for the checked row values and their
  negations, and revalidate them before commit and while validating dependencies. Token preparation
  uses its supplied converter when producing or consuming piecewise evidence. A callback can leave
  an earlier prepared cache behind; it is not reused as downstream evidence without revalidation.
  A custom mechanism model does not expose its converter and cannot receive this additional check.
  Converter validation compares values, not monotonic revisions; mutation followed by restoration,
  signed-zero-only changes, external callback side effects and concurrent/reentrant changes retain
  the existing limitations. Rebuild after changing a source, bound or converter.
- Twenty-eight Core cases cover default sine/cosine, native and encoded endpoint envelopes and
  inactive Big-M inequalities, decimal breakpoint collisions, incompatible converters, cache
  evidence, helper conflicts, and linear/quadratic write and pre-commit rollback. The first five
  cases failed at factory creation before the change; four model-converter and two table-converter
  regressions also failed before their respective fixes. The related Core selection passes 314.
- Gurobi 10.0.1 explicit opt-in passes 157 selected cases, including eight new cases that **copy the
  actual decimal mechanism rows into a test-only `Flt64` model** and solve its graph. This fixture is
  not a production generic-model-to-matrix adapter and does not prove general decimal solve or
  backend parity. Missing-SDK opt-in reports the expected error and is not counted as a pass; the
  subsequent default Gurobi project passes 27 cases with the SDK and four without it.
- Windows Release forced builds report 0 errors / 743 existing warnings with the configured SDKs,
  and 0 errors / 718 existing warnings with SDK discovery removed and separate artifacts. Their
  default suites pass 4248 / 1 skipped across 23 test assemblies and 4218 / 1 skipped across 22,
  respectively, with 2348 Core cases. Linux x64 Debian 13 in an offline `postgres:16` container,
  using the previously checked .NET SDK 10.0.402 and a local package cache copy, reports 0 errors /
  718 existing warnings and 4218 / 1 skipped across 22 assemblies, including 2348 Core cases.
  Offline restore explicitly disables NuGet audit; this is not a vulnerability audit, fresh network
  restore, Linux native-solver or external-service acceptance. The database image is used only for
  its OS dependencies; no database is started.
- The existing Delaunay precision test is still skipped. Hexaly and Benchmark discover no tests in
  both Windows setups; MOSEK also discovers none without SDK discovery and on Linux, so these are
  not counted as passing assemblies. No changed source or test file adds a build warning. The only
  repository-wide whitespace finding remains the earlier `LinearConstraintInput.cs` EOF blank
  line. Default integer samples, other inexact numeric types, production generic matrix conversion,
  deferred/native writers, other real backends, cross-language interoperability and performance
  remain unimplemented or unaccepted. P2 and overall synchronization are not complete.

中文说明：本批仅对 `FltX` 放宽非精确 Flt64 编码，并分别验证原生/编码断点、正反数值、
向外包络及自动 Big-M。输入多项式和其他数值类型仍遵循原有精确要求；默认整数采样会丢失
非零斜率，继续返回失败。直接求值与编码图可能因舍入不同；只有两侧均能证明且辅助值不
冲突时才发布缓存。内置模型和 token 准备现在校验实际转换器，写行或提交前失效会回滚；
自定义机制模型没有公开转换器，不能作同样检查。Gurobi 测试仅通过测试夹具复制 decimal
约束行，不代表已有 `FltX` 生产矩阵导出或完整真实后端验收。离线测试关闭了 NuGet 漏洞
审计；其他求解器、跨语言互通、性能及 P2 后续能力仍未完成。

### Previous gate: pure binary cardinality constraints

| Pinned Kotlin source | C# entry point | Contract coverage | Status |
|---|---|---|---|
| `Cardinality.kt`: `atMostConstraints`, `exactlyConstraints` | `CardinalityConstraints.AtMost<V>`, `CardinalityConstraints.Exactly<V>` | `CardinalityConstraintsTest`, `CardinalityConstraintsGurobiIntegrationTest` | Pure rows and selected Gurobi solves verified; separate indicators are described in the next gate |

- Both factories return a `Result` containing one linear inequality and create no helper variables.
  An empty input accepts only count zero; each repeated binary variable contributes once per
  occurrence. The input list and returned row list are copied into read-only collections, but
  the variable objects themselves are not copied. Invalid counts, null or non-binary inputs,
  inaccessible input collections and invalid numeric conversions return `IllegalArgument`.
- Native and `Flt64`-encoded zero, one, target, total count, predecessor when nonempty, repeated-variable
  coefficients and the normalized negative target must be exactly representable. Supported built-in
  numeric types therefore reject some large or otherwise inexact cardinalities that Kotlin may accept.
  Use the target model's converter, and keep it and variable types unchanged while building and using
  the row: these plain inequalities cannot detect a different model converter or later converter or
  custom-variable mutations. A binary
  declaration is required; the linear relaxation may assign fractional values and is not a Boolean
  count proof.
- Check the factory result, register the input variables and add the returned inequality to a
  linear or quadratic meta-model using its `AddConstraint` method. These factories enforce a hard
  row; they do **not** by themselves create satisfaction indicators. Use the separate
  `AtMostFunction` or `ExactlyFunction` described in the next gate when the result is needed as a
  symbol. Production generic-to-`Flt64` matrix conversion remains a separate unfinished task.
- Eight Core test cases enumerate all binary assignments for up to four distinct inputs and every
  legal count in both linear mechanism models and matrices. They also cover duplicate occurrences,
  linear rows in quadratic mechanism models and matrices, Flt32/FltX/Int64/RtnX, input snapshots,
  invalid inputs and conversions. The initial red run failed to compile because the API did not yet
  exist; it is not evidence of a previous runtime failure. Gurobi 10.0.1 explicit opt-in passes 24
  actual fixed-assignment solves, including repeated variables; missing-SDK opt-in gives the expected
  error and is not a pass. The default Gurobi project is rebuilt and passes 27 cases afterward.
- Windows forced Release builds have 0 errors / 743 existing warnings with configured SDKs and
  0 errors / 718 existing warnings with solver discovery removed and separate artifacts. Default
  tests pass 4256 / 1 skipped across 23 assemblies and 4226 / 1 skipped across 22, respectively;
  Core passes 2356 in both. Linux x64 Debian 13 uses the previously checked .NET SDK 10.0.402,
  a local NuGet cache copy and the `postgres:16` OS image without starting a database: the offline
  forced build has 0 errors / 718 existing warnings, with 4226 / 1 skipped across 22 assemblies,
  including 2356 Core cases. Offline NuGet audit is disabled; this is not an audit, network restore,
  Linux native-backend or external-service acceptance. After these full gates only parameter-list
  formatting and API documentation changed; the Core and Gurobi targeted cases were rebuilt and
  rerun after the formatting change.
- The pre-existing Delaunay precision test remains skipped. Hexaly and Benchmark discover no tests
  in both Windows setups; MOSEK also discovers none without SDK discovery and on Linux, so those
  are not counted as passing assemblies. Batch files introduce no build warnings; the repository-wide
  whitespace check still reports the earlier `LinearConstraintInput.cs` EOF blank line. Other real
  backends, cross-language interoperability and performance remain unaccepted; P2 and the overall
  synchronization remain incomplete.

中文说明：`CardinalityConstraints.AtMost` / `Exactly` 返回一条纯线性约束，不创建辅助
变量；空列表仅允许数量 0，重复变量按出现次数计数。先检查 `Result`，再注册输入变量并
将结果加入线性或二次元模型。输入列表被快照，但变量对象不复制；须使用目标模型的转换器，
并保持二元类型与转换器不变，工厂不会检测不同转换器或追踪之后的变化。数值及归一化负值不能精确表示时会保守拒绝；松弛解
允许分数，不是布尔计数证明。满足指示函数见下一节；泛型矩阵导出及其他真实后端仍未完成或验收。

### Previous gate: binary cardinality satisfaction indicators

| Pinned Kotlin source | C# entry point | Contract coverage | Status |
|---|---|---|---|
| `Cardinality.kt`: `AtMostFunction`, `ExactlyFunction` | `AtMostFunction<V>.Create`, `ExactlyFunction<V>.Create`, `LinearFunctionSymbolAdapter<V>` | `CardinalityFunctionTest`, `CardinalityFunctionLifecycleTest`, `ConstraintWriteTransactionTest`, `CardinalityFunctionGurobiIntegrationTest` | Contract verified; selected Gurobi 10.0.1 solves verified, not complete backend acceptance |

- These factories return a structured result and snapshot the input list. Repeated variables count
  per occurrence, and an empty input accepts only zero. The result is a binary satisfaction
  variable, not the hard constraint returned by the pure-row factories. Register the input variables,
  then add a `LinearFunctionSymbolAdapter<V>` to a linear or quadratic meta-model. Direct evaluation
  returns no value for missing, nonfinite, or nonbinary inputs; it does not itself establish model
  feasibility. Input variables must have a trackable range snapshot; arbitrary custom variable
  implementations without one are conservatively rejected.
- Coefficients, repeated-variable merges, their signs and the model converter's `Flt64` encoding are
  checked for exactness, so inputs accepted by Kotlin can be rejected here. The captured ranges and
  conversions must remain current. Built-in linear and quadratic models recheck these dependencies
  during row writing, before commit, and at dump and solution-application boundaries; failure rolls
  back the affected row transaction or newly prepared tokens. A custom mechanism model does not
  expose its converter, so the same converter comparison is unavailable there. This is value-based
  checking, not a monotonic revision guarantee, and does not protect against arbitrary concurrent or
  reentrant mutation, external callback side effects, mutation followed by restoration, or changes
  that only flip the sign of zero.
- Preparation caches an indicator only when direct and model input values agree, lie in captured
  variable ranges, and all explicitly supplied helper values agree. Inferred helper evidence is
  revalidated before supported model-aware downstream functions consume it; this is not a general
  provenance guarantee for every consumer. The cache is not an incumbent, an LP solution, a
  feasibility proof, or fixed-column elimination. As with Kotlin's integer formulation, its LP
  relaxation can contain fractional indicators and is not a Boolean count proof.
- The Core cases enumerate all assignments for up to three distinct inputs and repeated inputs,
  exercise linear and quadratic models, empty lists, boundaries, snapshots, missing/nonbinary values,
  Flt32/FltX/Int64/RtnX, token conflicts, failed writes, converter changes, commit rollback and
  nested preparation evidence. Gurobi opt-in tests solve 48 fixed-input/output combinations with
  both ordinary and repeated inputs, including use of the result in a downstream row and objective.
  These are selected tests, not full solver-backend acceptance.
- The targeted Core selection passes 36 cases. Its first red run failed to compile because the new
  APIs were absent; a later fixed-input/range regression had one failure and one pass before its
  fix. Gurobi 10.0.1 passes all 48 opt-in cases. A missing-SDK opt-in returns the expected error and
  is not a pass; the rebuilt default no-SDK Gurobi project passes four cases.
- Windows forced Release builds finish with 0 errors / 743 existing warnings with configured SDKs
  and 0 errors / 718 existing warnings without SDK discovery using separate artifacts. After adding
  assertions to the numeric-type test, the Core test project is rebuilt and the full suites pass
  4292 / 1 skipped across 23 assemblies and 4262 / 1 skipped across 22 assemblies, respectively;
  Core contributes 2392 passes in both. Linux x64 Debian 13 offline uses the previously verified
  .NET 10.0.402 SDK, a local package cache and the `postgres:16` OS image without starting a
  database: its forced build has 0 errors / 718 existing warnings, and tests pass 4262 / 1 skipped
  across 22 assemblies, including 2392 Core cases. NuGet audit is disabled for offline restore;
  this does not verify vulnerabilities, network restore, Linux native solvers or external services.
- The existing Delaunay precision test remains skipped; Hexaly, Benchmark and no-SDK MOSEK discover
  no tests and are not counted as passing assemblies. No changed source or test adds a build
  warning. The repository-wide whitespace check still reports the pre-existing EOF blank line in
  `LinearConstraintInput.cs`. Generic production matrix conversion, other real backends,
  cross-language interoperability, performance, P2 and overall synchronization remain incomplete.

中文说明：`AtMostFunction<V>.Create` 与 `ExactlyFunction<V>.Create` 返回满足指示函数，
先检查 Result，注册输入变量，再将函数包装为 `LinearFunctionSymbolAdapter<V>` 加入线性或
二次元模型；它们不同于返回硬约束的 `CardinalityConstraints`。重复变量按出现次数计数，
空输入仅接受数量零。只支持可捕获范围快照的输入变量，系数和 Flt64 编码不精确时保守拒绝。
缓存准备会检查输入范围、转换器和已知辅助变量，并仅向受支持的模型感知下游传播经复核的
证据；缓存不代表可行解。内置模型在写行、提交、转储和回填时检查失效，自定义模型不暴露
转换器而无法做同等检查。并发或重入修改、修改后还原、仅变化零的符号、其他下游函数以及性能尚未全面验证。
Core 专项 36 项及 Gurobi 10.0.1 显式集成 48 项通过；Windows 有／无 SDK 全量分别为
4292／4262 passed、各 1 skipped，Linux 离线为 4262 passed、1 skipped。跳过的是既有
Delaunay 精度测试；缺 SDK、其他真实后端、跨语言互通和性能未计为通过。

### Previous gate: bounded linear positive-part function

| Pinned Kotlin source | C# entry point | Contract coverage | Status |
|---|---|---|---|
| `core/symbol/function/CompositeFunctions.kt`: `PositivePartFunction` | `PositivePartFunction<V>.Create`, `LinearFunctionSymbolAdapter<V>` | `PositivePartFunctionTest`, `FunctionSymbolRegistrationTest`, `ConstraintWriteTransactionTest`, `PositivePartGurobiIntegrationTest` | Contract verified for the documented bounds and encoding; selected Gurobi 10.0.1 solves verified, not complete backend acceptance |

- `PositivePartFunction<V>.Create` returns a structured result for the graph `max(input, 0)`.
  Register the input variables and add the result through `LinearFunctionSymbolAdapter<V>` to a
  linear or quadratic meta-model. The implementation reuses the extremum range and row construction;
  the input polynomial is shallow-snapshotted and nested inputs must be exactly expandable with
  finite, trackable bounds. Direct evaluation does not establish model feasibility.
- Native values, merged coefficients, bounds and their `Flt64` encodings and conversions must be
  exact. This can reject inputs accepted by Kotlin, including a `FltX` coefficient of `0.1m`.
  Preparation deliberately publishes no cached result until complete extremum-helper evidence is
  available. This is a missing preparation capability, not evidence of an incumbent, an LP solution,
  or fixed-column elimination. Production generic-to-`Flt64` matrix conversion remains unfinished.
- Row writes are transactional. Built-in linear and quadratic mechanism models recheck dependencies
  during writing, before commit, and at dump and solution-application boundaries; a failure rolls
  back the affected rows or newly prepared tokens. Custom mechanism models do not expose their
  converter, so equivalent converter checks are unavailable there. These value-based checks do not
  guarantee detection of concurrent or reentrant mutation, mutation followed by restoration, or a
  change only in the sign of zero; external callback side effects are not rolled back.
- The selected Core suite passes 207 cases, covering representative Flt64, Flt32, FltX, Int64 and
  RtnX inputs, constants and affine inputs, nested dependencies, invalid bounds and conversions,
  write/commit rollback and the intentionally absent preparation cache. The first red run failed
  compilation because the API did not yet exist, rather than showing a prior runtime failure.
  Gurobi 10.0.1 opt-in passes 20 real solves across linear and quadratic models, including correct
  and incorrect fixed outputs and an objective. A missing-SDK opt-in returns the expected error and
  is not a pass; the rebuilt default no-SDK Gurobi project passes four cases.
- Windows forced Release builds finish with 0 errors / 743 existing warnings with configured SDKs
  and 0 errors / 718 existing warnings with solver discovery removed and separate artifacts. The
  default suites pass 4320 / 1 skipped across 23 test-bearing assemblies and 4290 / 1 skipped across
  22, respectively; Core contributes 2420 passes in both. Linux x64 Debian 13 offline uses the
  previously verified .NET 10.0.402 SDK, a local package cache and the `postgres:16` OS image
  without starting a database: its forced build has 0 errors / 718 existing warnings, and tests
  pass 4290 / 1 skipped across 22 assemblies, including 2420 Core cases. NuGet audit is disabled
  for offline restore; this does not verify vulnerabilities, network restore, Linux native solvers
  or external services.
- The existing Delaunay precision test remains skipped; Hexaly, Benchmark and no-SDK MOSEK discover
  no tests and are not counted as passing assemblies. The batch files have no new build warnings
  or whitespace errors; the repository-wide whitespace check still reports the earlier EOF blank
  line in `LinearConstraintInput.cs`. Other composite functions, production generic matrix conversion,
  other real backends, cross-language interoperability, performance, P2 and overall synchronization
  remain incomplete or unverified.

中文说明：`PositivePartFunction<V>.Create` 为有界线性输入构建 `max(input, 0)`，先检查
Result，注册输入变量，再通过 `LinearFunctionSymbolAdapter<V>` 加入线性或二次元模型。
输入及 Flt64 编码必须满足精确检查，部分 Kotlin 可接受的输入（如 FltX 系数 `0.1m`）会被
保守拒绝。直接求值不是可行性证明；在完整辅助变量证据实现前，准备阶段不发布缓存。
内置模型在写行、提交、转储及解回填时复核依赖，自定义模型不能做同等转换器检查；并发或
重入修改、修改后还原与零的符号变化不保证检测。Core 相关专项 207 项及 Gurobi 10.0.1
显式集成 20 项通过；Windows 有／无 SDK 全量分别为 4320／4290 passed、各 1 skipped，
Linux 离线为 4290 passed、1 skipped。跳过的是既有 Delaunay 精度测试；其他复合函数、
真实后端、跨语言互通及性能仍未完成或验收。

### Current gate: generic mechanism-to-matrix conversion

| Pinned Kotlin source | C# entry point | Contract coverage | Status |
|---|---|---|---|
| `LinearTriadDumpBuilders.kt`, `QuadraticTetradDumpBuilders.kt`, `DumpHelpers.kt`, `MechanismModel.kt` | `ToLinearTriadModel<V>`, `ToQuadraticTetradModel<V>`, `BuildLinearTriadModelAsync<V>`, `BuildQuadraticTetradModelAsync<V>`, `RestoreSolverSolution`, `ApplySolverSolution` | `GenericMechanismModelDumpTest`, `GenericMechanismGurobiIntegrationTest` | Direct generic conversion and selected Gurobi 10.0.1 solves verified; higher-level generic solver entry points and complete numeric/lifecycle parity remain open |

- Linear and quadratic mechanism models can now be exported to the production `Flt64` matrix
  representation for supported `V`. The atomic build helpers also accept generic meta-models,
  construct the matrix before committing prepared helper tokens and keep the existing failure
  rollback. This supersedes the earlier batch statements that production generic matrix conversion
  was unavailable; it does not make all functions or higher-level solver interfaces generic.
- Coefficients, right-hand sides and objective constants use the numeric type's `ToFlt64()` and
  may round. Nonfinite inputs and individual nonzero values that convert to zero are rejected, but
  fixed-variable substitution and coefficient accumulation also use floating arithmetic and can
  round or underflow. A custom numeric type is responsible for its own conversion semantics;
  these checks are not an exactness or feasibility proof.
- Eliminated fixed values must be valid in their variable domains and round-trip through the model
  converter to the original native value. The matrix carries its mapping and snapshots so
  `RestoreSolverSolution` can restore eliminated columns and `ApplySolverSolution` can apply them
  to the source model. The dump and subsequent snapshot validation check the converter's zero,
  one and fixed-value conversions only; changes affecting other values may not be detected.
  Keep the converter stable and faithful throughout construction, solving and application.
- Before writing tokens, application checks the whole solution vector for finite values and
  convertible values; integer numeric types reject values that cannot be represented exactly,
  including fractional solver values. Non-integer solution conversion can round and does not prove
  exact equality to the solver value. A subsequent callback or conversion failure during actual
  token writing can still leave the model inconsistent; the returned fatal result requires a
  rebuild. Neither restoration nor application proves feasibility or native-model integrity.
- Migrate direct callers by supplying the model's `V` fixed values to the generic dump/build
  methods, checking each `Result`, passing the returned `Flt64` matrix to a compatible solver,
  and applying a successful solution with the original mechanism and matrix. Dispose both the
  mechanism model and matrix returned by a successful build. `IIntoValue<V>` now inherits
  `IFlt64ValueConverter<V>`; existing converters still provide zero, one and both conversion
  directions. The cancellation checkpoints and rollback do not isolate external callback effects
  or support concurrent/reentrant model changes; mutation followed by restoration can evade
  value-based invalidation. Rebuild after changing a model or converter.
- Twenty-one new Core cases cover `FltX`, `Flt32`, `Int64` and `RtnX`, both model kinds, fixed
  columns, solution application, converter invalidation and atomic-build rollback. Core's complete
  suite passes 2441 cases. Gurobi 10.0.1 explicit opt-in passes 32 selected real solves of `FltX`
  sine/cosine models, including linear/quadratic models, fixed columns and feasible/infeasible
  cases. This exercises the production matrix path, not full backend acceptance. Without the SDK,
  explicit opt-in reports the expected missing-SDK error and is not counted as a pass; rebuilding
  the default no-SDK Gurobi test project yields four passing contract tests.
- Windows forced Release builds report 0 errors / 743 existing warnings with configured SDKs and
  0 errors / 718 existing warnings with SDK discovery removed and separate artifacts. Default tests
  pass 4341 / 1 skipped across 23 test-bearing assemblies and 4311 / 1 skipped across 22,
  respectively. Linux x64 Debian 13 uses the previously checked .NET SDK 10.0.402, a local NuGet
  cache copy and the offline `postgres:16` OS image without starting a database: its forced build
  has 0 errors / 718 existing warnings and tests pass 4311 / 1 skipped across 22 assemblies,
  including 2441 Core cases. Offline NuGet audit is disabled; this is not a vulnerability audit,
  fresh network restore, Linux native-backend or external-service acceptance.
- The existing Delaunay precision test remains skipped; Hexaly and Benchmark discover no tests
  in either Windows setup, and MOSEK also discovers none without SDK discovery or on Linux. These
  are not counted as passing assemblies. Batch files have no new build warnings or whitespace
  errors. The repository-wide whitespace check still reports the earlier EOF blank line in
  `LinearConstraintInput.cs`. Higher-level generic solver paths, other functions and real backends,
  cross-language interoperability, performance, P2 and overall synchronization remain incomplete.

中文说明：线性／二次泛型机制模型现在可直接转储为生产 `Flt64` 矩阵；`Build*Async<V>`
在辅助 token 提交前完成转储，失败沿用回滚。此前批次“生产泛型矩阵转换尚未完成”的描述
已被本批覆盖，但更高层泛型求解入口及完整 P2 仍未完成。固定值须通过模型转换器往返恢复，
回填前会先检查全部解值；整数数值类型拒绝无法精确表示的解。系数转换和后续浮点运算可能
舍入或下溢，非整数解的转换也可能舍入；检查只覆盖转换器的零、一和固定值，不能证明所有
值的转换正确。调用方应保持转换器稳定，检查 Result，并分别释放成功构建的模型和矩阵；
外部回调副作用、并发／重入修改与变更后还原不保证隔离或检测，回填不是可行性证明。
Windows 有／无 SDK 全量分别为 4341／4311 passed、各 1 skipped；Linux 离线为 4311 passed、
1 skipped。Gurobi 10.0.1 仅完成 32 个选定真实求解，其他真实后端、互通和性能仍未验收。

### Current gate: report conversion and legacy status mapping

| Pinned Kotlin source | C# entry point | Contract coverage | Status |
|---|---|---|---|
| `core/solver/report/SolveReport.kt` (`convertTo`, `toSolveReport`) | `SolveReportConversionExtensions.ConvertTo<V>`, `SolverOutputReportExtensions.ToSolveReport` | `SolveReportConversionTest`, `SolveReportTest`, `GenericMechanismModelDumpTest`, selected `GenericMechanismGurobiIntegrationTest` | Conversion and legacy status contracts partially verified; production backend termination and proof propagation remain open |

- `SolveReport<Flt64>.ConvertTo<V>` returns a `Result` and converts solution, pool, objective,
  constraint and bound diagnostics, and nested attempts. Statistics stay `Flt64`; status, proof,
  provenance and fingerprints are retained. Converted numeric collections and attempts are
  read-only snapshots, while non-numeric metadata remains shared. Shared nested reports remain
  shared; cycles fail instead of recursing indefinitely. Do not mutate inputs during conversion.
- Compared with Kotlin's direct `IntoValue` mapping, C# rejects NaN, nonzero-to-zero and sign
  changes, and inconsistent reverse conversions. Infinity is accepted only for a non-integer
  target that declares and identifies the corresponding native infinity and converts back
  consistently. Identity `Flt64` conversion preserves negative zero; other conversions do not
  promise to do so. Floating-point conversion may lose precision. These checks do not establish
  the magnitude error of an arbitrary non-integer converter or prove solution feasibility;
  callers must supply a stable, faithful converter.
- For integer targets, report conversion and mechanism-model solution application now compare
  the actual integer against the exact original `Flt64` value, rather than trusting a rounded
  double round trip. Built-in integer types supported by `ExactBoundValue` are checked, including
  `IntX` and `UIntX`; custom integer types without an exact reader are conservatively rejected.
  The writeback check happens before token writes, but later callbacks or converter changes can
  still invalidate state and may require a model rebuild. These checks do not cover legacy
  `FeasibleSolverOutput.ConvertTo` or `IAbstractLinearSolver` / `IAbstractQuadraticSolver`
  generic solve methods, which still call the converter directly.
- Legacy linear and quadratic no-solution outputs now use their `SolverStatus`: infeasible,
  unbounded and infeasible-or-unbounded remain distinct; unusable or unknown statuses produce
  an unknown conclusion and structured backend error. An infeasible legacy status supplies no
  verified IIS or other certificate. The old output lacks the actual termination reason: the
  adapter's `Completed` is a compatibility default, not proof of completion or optimality.
  Native backend propagation of termination, incumbent and proof must precede wiring the
  unified generic report into the production solver path.
- This batch adds 23 Core cases: 12 conversion cases, seven status cases, and four integer
  writeback cases. The eight large-integer/infinity regressions failed before the fix; the final
  selected report and writeback suite passes 94. Gurobi 10.0.1 opt-in passes 32 selected real
  solves, of which 24 feasible cases also convert their reports. An opt-in without the SDK gives
  the expected missing-SDK build error and is not counted as a pass; the default no-SDK Gurobi
  project passes four cases. These results do not constitute full backend acceptance.
- Windows forced Release builds have 0 errors / 743 existing warnings with configured SDKs and
  0 errors / 718 existing warnings with SDK discovery removed and separate artifacts. Default
  tests pass 4364 / 1 skipped in 23 test-bearing assemblies and 4334 / 1 skipped in 22,
  respectively, including 2464 Core cases. The only post-gate source changes are two bilingual
  API remarks; the affected Core project was rebuilt and all 94 selected tests were rerun.
- Linux x64 Debian 13 uses the previously checked .NET 10.0.402 SDK, a local NuGet cache and
  the offline `postgres:16` OS image without starting a database. Its forced build has 0 errors /
  718 existing warnings, and tests pass 4334 / 1 skipped across 22 assemblies, including 2464
  Core cases. Offline NuGet audit is disabled; this does not verify vulnerabilities, a fresh
  network restore, Linux native solvers or external services. Its source snapshot precedes the
  same two comment-only API remarks.
- The existing Delaunay precision test remains skipped. Hexaly and Benchmark discover no tests;
  no-SDK MOSEK also discovers none, so none are counted as passing assemblies. Batch source and
  tests introduce no build warnings or whitespace errors; the repository-wide whitespace check
  still reports the earlier EOF blank line in `LinearConstraintInput.cs`. Complete P2, other real
  backends, cross-language interoperability and performance remain incomplete or unverified.

中文说明：`SolveReport<Flt64>.ConvertTo<V>` 返回 Result，转换解、解池、目标、诊断数值及
嵌套尝试，并保留状态、证明、来源和指纹；统计仍为 Flt64。数值集合及尝试列表为只读快照，
非数值元数据仍共享，转换期间不得并发修改。相较 Kotlin 直接转换，C# 对 NaN、零、符号、
无穷值及反向转换更保守；非整数转换仍可能舍入，检查不能证明自定义转换器的幅值误差或解
可行性。报告转换与机制模型解回填均精确核对整数原值，无法精确读取的自定义整数类型会被
保守拒绝；后续回调或转换器变化仍可能要求重建模型。旧的高层泛型求解和
`FeasibleSolverOutput.ConvertTo` 尚未采用此校验。旧无解输出按状态区分不可行、无界及无法
判断，且不伪造 IIS 证明；旧输出不含真实终止原因，适配得到的 `Completed` 不能作为真实
终止或最优证明。选定 Core 专项 94 项、Gurobi 10.0.1 真实求解 32 项通过；有／无 SDK 的
Windows 全量分别为 4364／4334 passed、各 1 skipped；Linux 离线为 4334 passed、1 skipped。
全量构建后只补充两处双语 API 注释，并重建 Core 重跑 94 项专项。后端真实终止状态与证明
传播、完整泛型求解链路、其他真实后端、跨语言互通及性能仍未完成或验收。

### Current gate: Gurobi native report boundary

| Pinned Kotlin source | C# entry point | Contract coverage | Status |
|---|---|---|---|
| `core/solver/LinearSolver.kt`, `QuadraticSolver.kt`, `report/SolveReport.kt` | `IAbstractLinearSolver` / `IAbstractQuadraticSolver` `InvokeReportAsync` and `SolveReportAsync<V>` | `GurobiSolverContractTest`, Framework dump-solver tests, `GurobiReportIntegrationTest` | Transitional report entries; later serial Framework migration is described below; other combinatorial and non-Gurobi implementations remain open |
| `core-plugin-gurobi/.../GurobiSolver.kt`, `GurobiLinearSolver.kt`, `GurobiQuadraticSolver.kt` | `GurobiLinearSolver`, `GurobiQuadraticSolver`, `GurobiStatusMapping` | `GurobiReportStatusTest`, Gurobi opt-in integration suite | Native report and cancellation partially validated with Gurobi 10.0.1; full backend acceptance remains open |

- `InvokeReportAsync` now returns the native conclusion, termination, solution presence and proof
  separately, including successful reports with no incumbent. Its pool overload attaches solutions
  collected on the feasible/optimal analysis path. With the SDK present and no prior cancellation,
  capacities above `Int32.MaxValue` fail before native initialization.
  `SolveReportAsync<V>` uses the checked `SolveReport.ConvertTo` path. Gurobi's old `InvokeAsync`
  methods adapt this one report pipeline for existing callers and still return a failure if they
  cannot represent a no-incumbent result. Their fallback objective bound and gap do not prove
  optimality. Old generic `SolveAsync<V>` still uses its previous unchecked conversion.
- At this gate other solvers and combinatorial solvers had not implemented the report boundary: the default
  methods return an explicit unsupported `Result`, rather than pretending legacy outputs contain
  native termination information. When the Gurobi SDK is absent, a non-cancelled report request
  returns the environment error; a pre-cancelled request returns a cancelled report without
  initializing an environment. Invoke generic default methods through the solver interface. This
  is a temporary migration interface, not the final unified API. Neither the Gurobi IIS overloads
  nor the Framework column-generation/Benders callers are migrated in this batch.
- An optimal zero-column model may have native `SolCount == 0`; the adapter reads its objective
  and represents the unique empty vector. Native values and objective must be finite; these checks
  do not independently verify feasibility. Native optimal, infeasible, unbounded and
  infeasible-or-unbounded conclusions carry `Claimed`, never `Verified`. The adapter retains
  available runtime, counters, bounds and gap, plus a conservative Gurobi capability descriptor;
  unavailable statistics are absent, and native version/configuration fingerprints are not yet
  populated. Unrecognized native statuses, including unmapped work/memory limits, become
  `BackendFailure` with a structured issue rather than an invented conclusion.
- A pre-cancelled request skips initialization; later checkpoints stop before optimization when
  possible. During optimization a registered native termination request is complemented by the
  native callback's cancellation check, and registration is disposed before the model. Completed
  statuses and specific native limits take precedence over a late cancellation. An interrupted
  status concurrent with both external interruption and cancellation cannot identify which event
  caused the stop. A failed direct termination request is recorded as a report warning; existing
  native callback exception handling still swallows callback exceptions. Initialization/modeling
  and arbitrary user callbacks are not forcibly preempted. The Task-returning Gurobi entry can
  block its caller during native optimization; use a separate worker when nonblocking execution
  is required.
- The C# mapping deliberately gives an explicit native limit precedence over a concurrent
  cancellation, unlike the pinned Kotlin mapping; it maps Gurobi cutoff to objective limit and
  suboptimal to numerical failure. The time-limit/no-incumbent fixture can also be solved as
  infeasible before the clock limit, so it accepts either real native outcome; deterministic status
  tests separately cover the no-incumbent time-limit case. These are documented differences, not a
  cross-language interoperability acceptance.
- The complete Gurobi opt-in suite passes 766 cases on Windows with Gurobi 10.0.1. It includes
  real linear/quadratic optimum, infeasible and unbounded models, zero-column models, solution
  pools, time/node limits, incumbent and no-incumbent outcomes, pre/during/after cancellation,
  native callback and concurrent cancellation, generic conversion failures and legacy regression
  tests. The focused integration class also passes eight repeated runs before the last six
  regression cases were added; this is not exhaustive race or backend capability validation.
- Windows forced Release builds have 0 errors / 743 existing warnings with configured SDKs and
  0 errors / 718 existing warnings with isolated SDK discovery. Default tests pass 4389 / 1 skipped
  in 23 test-bearing assemblies and 4337 / 1 skipped in 22 respectively; Core remains 2464 passed.
  Linux x64 Debian 13 offline with .NET 10.0.402 and a local NuGet cache builds with 0 errors /
  718 existing warnings and passes 4337 tests / 1 skipped in 22 test-bearing assemblies, including
  2464 Core cases. The `postgres:16` system layer is used without starting a database, and offline
  NuGet audit is disabled; this does not validate a fresh restore or vulnerability audit.
  The existing Delaunay precision test is skipped; Hexaly and Benchmark discover no tests, and
  MOSEK also discovers none without SDKs. The batch adds no build warnings or whitespace errors;
  the repository-wide check still reports the previous `LinearConstraintInput.cs` EOF blank line.
  Linux native solvers, other real backends, cross-language interoperability and performance remain
  unverified.

中文说明：Gurobi 线性和二次矩阵求解新增统一报告入口，分别保留原生结论、终止原因、解
存在性与证明强度；有解池时一并返回，泛型报告使用受检转换。零列最优模型即使原生解计数
为零也可返回空解向量。后端结论只标记 `Claimed`，数值有限性检查不等于独立可行性复验。
取消通过原生终止和回调检查协同处理，优化结束后先撤销注册再释放模型；初始化、建模和
任意用户回调不能强制中断，外部中断与取消同时发生时无法确定实际原因。明确的原生限制
优先于并发取消，与固定 Kotlin 的映射不同；未映射的 work/memory 限制返回 BackendFailure。
旧入口仍作临时适配，无 incumbent 时不能表达报告；该批次其他后端和组合求解器明确返回不支持，
旧泛型求解、IIS 与 Framework 编排尚未迁移。本批 Gurobi opt-in 全套 766 项、Windows
有／无 SDK 全量 4389／4337 项通过（各有 1 项既有跳过）；Linux 离线构建 0 errors、718 条
既有警告，4337 passed、1 skipped。离线审计关闭，未启动数据库；这不代表联网恢复、漏洞审计、
Linux 原生求解器、其他后端、互通或性能已验收，整体 P2/P3 和全量同步继续进行。

### Previous gate: serial Framework combinatorial reports

This section records the serial gate before the subsequent parallel report and legacy work below.

| Pinned Kotlin source | C# entry point | Contract coverage | Status |
|---|---|---|---|
| `framework/solver/SerialCombinatorialLinearSolver.kt`, `SerialCombinatorialQuadraticSolver.kt`, `CombinatorialSolveSupport.kt` | `SerialCombinatorialLinearSolver` and `SerialCombinatorialQuadraticSolver`: `SolveCombinatorialReportAsync`, `InvokeReportAsync` | `SerialCombinatorialReportTest`, `GurobiReportIntegrationTest.SerialCombination*` | Ordinary and pool report paths partially verified; later batches address Framework legacy and parallel entries, while Core combinators remain pending |

- The serial report paths validate matrix identity before starting a backend and preserve each
  attempted backend, monotonic elapsed time, structured errors and the selected attempt. A report
  with both a solution and `Incumbent` or `Optimal` presence is selected; cancellation stops
  selection, while a known infeasible or unbounded report stops by default without selecting a
  solution. Stop codes are configurable and snapshotted. `InfeasibleOrUnbounded` does not stop by
  default, as in Kotlin. A nonterminal report without an incumbent remains in the attempt trace;
  if nothing is selected or stopped, the terminal adapter returns `Unknown`/`BackendFailure` with
  attempt errors, or a no-success issue when there are no errors. This is the wrapper's outcome,
  not the backend termination reason. Its no-success message says no *selectable result* was
  produced, rather than Kotlin's inaccurate claim that no report was produced. Attempt error messages are
  forwarded as supplied by the backend; sensitive information must be redacted at its source.
  Errors from earlier attempts remain in the terminal diagnostics even when a later attempt succeeds.
- A pre-cancelled request does not invoke a factory, even with an empty list; unlike the pinned
  Kotlin loop, it therefore returns cancellation for an empty list. A completed backend report is
  retained if cancellation arrives late; a cancelled backend report has no selected attempt even if
  it contains an incumbent. An `OperationCanceledException` is mapped to cancellation only when
  the supplied .NET token is cancelled; concurrent independent interruption remains ambiguous.
  `CancellationToken` carries no reason, so cancelled attempts use a generic reason. Other backend
  exceptions are logged and represented as a generic solving issue. Initialization, callbacks and
  blocking native optimization cannot be forcibly preempted.
- Factory and stop-code collections are snapshotted, but factories create a solver per attempt;
  accessing `Name` or `Config` can separately call factories, and `Config` requires a nonempty list.
  Kotlin's lazy backend instances are
  not reproduced. The attempt and aggregated-error lists are read-only snapshots, but backend
  reports and their nested metadata are retained by reference. The wrapper checks the presence and
  existence of a solution, not its dimensions, objective, feasibility or consistency of all status
  fields; a malformed backend report is not an independently verified solution. Nonstandard
  solution-presence enum values are not selected, unlike Kotlin's `!= None` check.
- Existing legacy `InvokeAsync`, legacy pool/IIS paths, Core and Framework parallel combinators,
  column generation and Benders are not routed through this report path. Other solver report
  implementations, real backend acceptance, interoperability and performance remain open; this is
  not completion of P2, P3 or the overall synchronization.
- This batch adds 38 Framework contract cases, including ordinary/pool forwarding, status-based
  selection, failed and fatal attempts, factory exceptions, cancellation, identity validation,
  checked generic conversion, and factory/stop-code snapshots. Gurobi 10.0.1 opt-in adds ten real
  combination cases for optimal/infeasible models, pools, fallback and cancellation; the complete
  opt-in suite passes 776. An opt-in without the SDK fails with the expected missing-SDK diagnostic
  and is not counted as a pass; the default no-SDK Gurobi project passes five cases. The opt-in
  build is followed by a default Gurobi test run with 50 passed.
- Windows forced Release builds report 0 errors / 743 existing warnings with configured SDKs and
  0 errors / 718 existing warnings with SDK discovery and artifacts isolated. Default tests pass
  4427 / 1 skipped across 23 test-bearing assemblies and 4375 / 1 skipped across 22 respectively;
  the Framework suite has 76 passed. Linux x64 Debian 13 offline with .NET 10.0.402, a local NuGet
  cache and the `postgres:16` system layer (database not started) builds with 0 errors / 718 existing
  warnings and passes 4375 / 1 skipped across 22 assemblies. Offline NuGet audit is disabled; these
  results do not verify a fresh network restore, vulnerability audit or Linux native solvers. The
  existing Delaunay precision test is skipped. Hexaly, Benchmark and no-SDK MOSEK discover no tests
  and are not counted as passing assemblies. The batch adds no build warnings. After the full gates,
  only argument-list formatting changed; the 38 focused cases and the complete Gurobi opt-in suite
  were rebuilt and rerun.

中文说明：Framework 串行线性／二次组合器的普通和解池统一报告入口先校验模型身份，按顺序
保留各后端尝试、耗时、错误、终态和选中项；有解且声明 incumbent／最优才选中，取消及默认的
不可行／无界终态不会虚构已选解。默认不因“不可行或无界”停止；可在构造时指定并快照停止码。
没有可选结果时原始无解报告保留在尝试中，终态适配为 Unknown／BackendFailure；后端错误消息
原样传播，脱敏由来源负责，后续成功时终态诊断仍包含之前尝试的错误。该终态是组合器结果而非后端终止原因；无成功结果的消息改为“未产生
可选结果”，不沿用 Kotlin 在存在无解报告时仍称“未形成报告”的表述。预取消即使没有后端也返回取消
（不同于固定 Kotlin 空列表行为），
.NET 取消令牌不包含原因，轨迹使用通用原因；并发独立中断无法确定真实原因。工厂列表及停止
码快照，名称或配置访问仍可能独立调用工厂，工厂每次尝试创建实例，与 Kotlin 懒实例不同。
空工厂列表不支持访问 `Config`。
尝试和聚合错误列表只读，嵌套后端报告仍共享；不独立验证解的维度、可行性或报告字段一致性。
当时旧求解／解池／IIS、并行组合器、列生成及 Benders 尚未迁移；后续进展见下文。
本批新增 Framework 38 项契约测试，Gurobi 真实组合场景 10 项；Gurobi 10.0.1 opt-in 全套
776 项通过。Windows 有／无 SDK 分别 4427／4375 项通过、各 1 项既有跳过；Linux 离线为
4375 项通过、1 项跳过，构建 0 errors／718 既有警告。全量门禁后仅调整参数列表排版，并重建
重跑 38 项专项和 Gurobi 全套。缺 SDK 的 opt-in 按预期报错，不计通过；
离线审计关闭，未启动数据库，不代表联网恢复、漏洞审计、Linux 原生后端或完整 Gurobi 验收。

### Previous gate: parallel Framework combinatorial reports

| Pinned Kotlin source | C# entry point | Contract coverage | Status |
|---|---|---|---|
| `framework/solver/ParallelCombinatorialLinearSolver.kt`, `ParallelCombinatorialQuadraticSolver.kt`, `CombinatorialSolveSupport.kt` | Framework `ParallelCombinatorialLinearSolver` and `ParallelCombinatorialQuadraticSolver`: `SolveCombinatorialReportAsync`, `InvokeReportAsync` | `ParallelCombinatorialReportTest`, `GurobiReportIntegrationTest.ParallelCombination*` | Ordinary and pool report paths partially verified; Framework legacy paths are covered by the following gate, Core parallel entry points remain unmigrated |

- Both report paths validate identity first, schedule each attempt on a .NET worker even if a
  backend's Task-returning implementation blocks synchronously, and wait for **every** scheduled
  attempt before returning. They retain read-only attempt and aggregated-error snapshots, elapsed
  time, backend reports, proofs and selected pools. `First` means the first incumbent in factory
  order, not the fastest completion. `Best` compares all finite candidate objectives in the model's
  direction and breaks ties in factory order. Signed zeros follow Kotlin's total ordering: `-0`
  precedes `+0` rather than tying. This is the best of those reports, not a global optimality
  claim. When any incumbent has a missing or non-finite objective, C# falls back to the
  first incumbent and reports `FirstFeasible`; unlike the pinned Kotlin sentinel/comparison logic,
  it does not claim that incomparable values were ranked. Invalid mode or objective-direction enum
  values return an input error before starting a backend.
- Only reports with an actual solution and explicit `Incumbent` or `Optimal` presence are
  candidates. Unlike the serial path, a parallel cancelled report with an incumbent can be
  selected, matching Kotlin. Without a candidate, the first known problem conclusion or cancelled
  report in factory order is retained. Other no-incumbent reports remain in the trace; if none has
  a known conclusion, the terminal adapter returns `Unknown`/`BackendFailure` and the accumulated
  errors or a no-selectable-result issue. Fatal results and exceptions do not suppress other
  attempts. Backend error messages are passed through, and exception details are sent to the
  logger; redact sensitive information at the backend and logging boundaries. Ordinary logging
  provider exceptions are suppressed so they cannot replace a solve result, and may lose that log.
- Pre-cancellation avoids all factory calls. Once scheduling starts, cancellation is forwarded to
  every backend but does not discard any scheduled attempt or overwrite a completed report. An
  `OperationCanceledException` is classified as cancellation only if the supplied token is
  cancelled; an independent concurrent interruption remains ambiguous. A cancelled report uses a
  generic reason because .NET tokens carry none. A backend that ignores cancellation can prevent
  the entire call from returning. Parallel workers can also exhaust thread-pool or backend/license
  resources; there is no throttling in this entry point.
- Caller-owned models must stay open and unchanged until the report task finishes. Factories,
  repeated solver instances, callbacks, custom model views and backend dependencies must themselves
  be safe for concurrent use; the wrapper does not copy or synchronize them. The factory list is
  snapshotted and `Name` is lazy, but each attempt calls its factory separately; reading `Name` or
  `Config` can invoke factories independently, `Name` caches initialization exceptions, and
  `Config` requires a nonempty list. These property accesses can throw. This differs
  from Kotlin's lazy backend instances. Backend reports and nested metadata are shared by reference,
  and the wrapper does not independently validate solution dimensions, feasibility, objective
  consistency or proof claims.
- Migration: call `InvokeReportAsync` on the solver interface for the selected terminal report and
  pool, or `SolveCombinatorialReportAsync` on the Framework combinator for the selection and full
  trace. The interface's `SolveReportAsync<V>` uses checked conversion. Existing legacy
  `InvokeAsync` methods are **not** routed through these report paths; the subsequent legacy gate
  aligns their selection and waiting behavior without synthesizing backend reports. Core's parallel
  combinators, legacy generic/IIS and Framework column-generation/Benders callers remain open.
- The batch adds 46 Framework cases covering blocking-worker concurrency, ordering, ties and
  signed zeros, minimum/maximum, incomplete and non-finite objectives, pool/callback/token
  forwarding, failures/Fatal, factory and logger exceptions, cancellation races, identity and
  enum validation, snapshot behavior and checked generic conversion. Four of the eight signed-zero
  regressions failed before the fix; both logger regressions failed before the logging safeguard.
  The 46 cases pass five additional runs; the serial and parallel focused set passes 84. Gurobi
  10.0.1 adds ten opt-in cases with two actual parallel
  backends, optimal/infeasible outcomes, pools, a failed factory and cancellation. Its full opt-in
  suite passes 786. An opt-in with the SDK isolated fails with the required missing-SDK diagnostic
  and is not counted as a pass; subsequent default Gurobi tests pass 50 with SDK and 5 without.
- Windows forced Release builds have 0 errors / 743 existing warnings with SDKs and 0 errors /
  718 existing warnings with isolated SDK discovery and artifacts. Default tests pass 4473 /
  1 skipped across 23 test-bearing assemblies and 4421 / 1 skipped across 22 respectively;
  Framework has 122 passed. Linux x64 Debian 13 offline with .NET 10.0.402, a local NuGet cache
  and the `postgres:16` system layer (no database started in the container) builds with 0 errors /
  718 existing warnings and passes 4421 / 1 skipped across 22 assemblies. NuGet audit is disabled
  for the gates; this is not a fresh network restore or vulnerability audit. The existing Delaunay
  precision test is skipped; Hexaly, Benchmark and no-SDK MOSEK discover no tests. The batch adds
  no build warnings; the repository-wide whitespace check still reports the previous
  `LinearConstraintInput.cs` EOF blank line. Linux native solvers, other real backends,
  cross-language interoperability and performance remain unverified.

中文说明：Framework 并行线性／二次普通及解池报告入口已接通；先校验模型身份，在工作线程
执行所有后端并等待全部结束，保留尝试、错误、证明及选中解池。`First` 按工厂顺序而不是完成
速度选择；`Best` 按最小／最大方向比较有限目标并按工厂顺序处理平局，正负零按 Kotlin 的全序
区分，`-0` 排在 `+0` 之前。任何候选目标缺失或
非有限时保守退回第一个 incumbent，选择理由记为 `FirstFeasible`，不同于固定 Kotlin 的哨兵
比较，避免声称不可比较的结果最优。只有同时有解并明确标记 incumbent／最优的报告才可选；
并行取消报告若仍有 incumbent 可以选中，与串行不同。无候选时保留首个已知结论或取消报告；
没有可选终态时以 Unknown／BackendFailure 汇总错误。后端错误原样传播，异常详情发送给日志，
须在来源和日志边界脱敏；日志提供方普通异常被抑制以免覆盖求解结果，对应日志可能丢失。

预取消不初始化工厂；一旦调度，取消不会丢弃已安排的尝试或覆盖后端已完成的报告。忽略取消的
后端会阻止组合调用返回；大量阻塞求解可能耗尽线程池或许可，入口不做限流。调用方须保持模型
打开且不可并发修改，并确保模型视图、后端实例、工厂和回调可并发使用。工厂列表快照、名称
惰性求值，但每次尝试独立调用工厂，名称／配置访问也可能调用并抛异常；名称初始化异常被缓存，
空工厂不支持 `Config`。内部
后端报告和元数据仍共享，不独立复核解维度、可行性、目标或证明。迁移时通过求解器接口调用
`InvokeReportAsync`，需要完整轨迹时调用 Framework 组合器的 `SolveCombinatorialReportAsync`；
Framework 旧 `InvokeAsync`／解池／IIS 已在下一个批次修复等待和选择，但未转发到报告路径；
Core 并行组合器仍未迁移，不能假定具有新入口的等待及选择语义。

新增 Framework 46 项专项，额外重复 5 次通过，串行／并行关联专项 84 passed；正负零八项中
四项及日志提供方异常两项在各自修复前失败。新增 Gurobi
10.0.1 真实联调 10 项，显式 opt-in 全套 786 passed；缺 SDK 的 opt-in 按预期失败不计通过，
随后默认 Gurobi 项目有／无 SDK 分别 50／5 passed。Windows 有／无 SDK 全量分别 4473／4421
passed、各 1 skipped；Linux 离线 4421 passed、1 skipped。三种构建均 0 errors，既有警告
分别 743／718／718。NuGet 审计关闭，不代表联网
恢复或漏洞审计。其他真实后端、Linux 原生求解、跨语言互通及性能未验收；P2/P3/P4 和全量同步
仍在进行。

### Previous gate: Framework parallel legacy solve entries

| Pinned Kotlin source | C# entry point | Contract coverage | Status |
|---|---|---|---|
| `framework/solver/ParallelCombinatorialLinearSolver.kt`, `ParallelCombinatorialQuadraticSolver.kt` | Framework parallel linear/quadratic `InvokeAsync` ordinary, pool and legacy IIS overloads | `ParallelCombinatorialLegacyTest`, `GurobiReportIntegrationTest.ParallelLegacyEntriesUseRealGurobiBackends` | Selection and lifecycle contract implemented; Gurobi 10.0.1 selected instances verified, other real backends not accepted |

- These legacy entries still call each backend's legacy API, so a backend that has not implemented
  unified reports remains usable. They validate model identity before scheduling, and a
  pre-cancelled call returns `ApplicationStopped` without invoking any factory. Every scheduled
  attempt runs on a worker and is awaited; cancellation is forwarded but does not discard a
  completed success. A nonresponsive backend can therefore prevent return. Once scheduling has
  started, even an attempt that starts after cancellation may initialize its factory. An
  `OperationCanceledException` is treated as cancellation only when the supplied token is
  cancelled; simultaneous unrelated interruption cannot be distinguished.
- `First` selects the first incumbent by factory order, not completion time. `Best` selects among
  finite objectives by model direction, preserves factory order on equal objectives and orders
  signed zero like the report path. If any incumbent objective is missing or non-finite, it falls
  back to the first incumbent. Invalid mode, or invalid direction in `Best`, fails before
  invoking a backend. A selected pool and its output are returned from the same backend, without
  merging or copying them. These are selections among backend outputs, not proofs of global
  optimality or independent feasibility checks.
- The IIS overload preserves the selected `SolverOutput` unchanged. A legacy feasible output or a
  report explicitly containing an incumbent is selectable; if none is selectable, the first
  non-null non-incumbent output is retained, including an unrecognized custom output. If no output
  exists, the first `Fatal` in factory order is returned, otherwise the first `Failed`, otherwise
  `SolverNotFound`. The legacy result cannot retain the other attempts or distinguish all terminal
  states. Null result objects, null feasible solutions and null pool lists become failures;
  solution dimensions, pool contents, evidence, objective correctness and proof claims are not
  independently checked. Error text supplied by a backend is passed through; exception details go
  to the logger, so both boundaries require redaction. Ordinary logging exceptions are suppressed.
- Callers must keep the model open and unchanged until the call finishes and ensure the factories,
  backend instances, model views and callbacks are concurrency-safe. There is no worker or license
  throttling. Factory-list snapshot, `Name`/`Config` behavior and aliasing of nested output data are
  as described for the report gate above. In particular, `Config` still requires a nonempty list;
  dump operations are not covered by this gate. A `LinearTriadModel` passed without an interface
  cast may bind the legacy IIS overload because its concrete parameter is more specific; cast to
  `ILinearTriadModelView` when the ordinary overload is intended.
- Prefer `InvokeReportAsync` or `SolveCombinatorialReportAsync` when real backend termination,
  proof or attempt history is required. The legacy path cannot manufacture that information and
  does not silently fall back between interfaces. Framework serial legacy paths are addressed in
  the following batch; Core parallel combinators and column generation/Benders have not received
  this lifecycle repair. Gurobi's
  current legacy IIS overload itself does not produce IIS evidence; the real test covers passing
  the overload through, not real IIS extraction. Other solvers, interoperable wire fixtures,
  Linux native solvers and performance remain unaccepted; P2/P3/P4 and full synchronization remain
  in progress.
- This batch adds 67 Framework contract cases; all pass five additional runs. The combined
  serial/parallel report and legacy focused set passes 151. The initial nonblocking regression
  group, before the fix, reported 21 failures and 11 passes; the ordinary linear test was later
  corrected to cast the model view so it actually exercises that overload. Gurobi 10.0.1 adds 12
  real ordinary/pool/IIS-overload cases for linear/quadratic feasible and infeasible models; its
  complete opt-in suite passes 798. An opt-in without the SDK produces the expected error and is
  not a pass. Subsequent default Gurobi tests pass 50 with the SDK and 5 without.
- Forced Windows Release builds have 0 errors / 743 existing warnings with SDKs and 0 errors /
  718 existing warnings with SDK discovery and artifacts isolated. Their full default suites pass
  4540 / 1 skipped across 23 test-bearing assemblies and 4488 / 1 skipped across 22 respectively;
  Framework has 189 passed. Linux x64 Debian 13 offline with .NET 10.0.402, local NuGet cache and
  the `postgres:16` system layer (no database started in that container) has 0 errors / 718 existing
  warnings and 4488 passed / 1 skipped across 22 assemblies. NuGet audit is disabled for the offline
  gate; these results do not prove a fresh network restore or vulnerability audit. The existing
  Delaunay precision case is skipped, and Hexaly, Benchmark and no-SDK MOSEK have no discovered
  tests. This batch adds no build warnings. Repository-wide `git diff --check` still reports the
  pre-existing `LinearConstraintInput.cs` EOF blank line.

中文说明：Framework 线性／二次并行旧普通、解池和 IIS 入口继续调用后端旧 API，避免尚未实现
统一报告的后端失效；先校验模型身份，预取消返回 `ApplicationStopped` 且不启动工厂。所有
已调度尝试均在工作线程运行并等待结束；已开始调度后取消不会丢弃已有成功，也可能仍初始化尚未
启动的工厂。忽略取消的后端可能使调用无法返回。`First` 按工厂顺序选择首个 incumbent；
`Best` 按方向比较有限目标，区分正负零，若任一候选目标缺失或非有限则退回首个 incumbent。
选中解池及输出来自同一后端且不复制。IIS 入口保留后端原始 `SolverOutput`；没有候选解时保留
首个非空原始输出，均无输出时优先返回首个 Fatal，否则首个 Failed，空工厂返回 SolverNotFound。
它无法保留所有尝试或真实终止原因，也不独立验证解、解池、目标或证明。后端错误和日志均须在
来源处脱敏；日志提供方普通异常被抑制。

求解期间模型必须保持打开且不被修改，调用方还须保证后端、视图、回调和工厂可并发使用；
入口不限制线程或许可用量。`Config`、`DumpAsync` 不属于本批修复范围。线性具体 `LinearTriadModel`
若未强转为 `ILinearTriadModelView`，重载解析可能选择 IIS 入口。需要真实终态、证明或完整
轨迹时应改用已实现报告的后端及组合报告接口。Framework 串行旧入口已在下批修复；Core 并行
组合器、列生成和 Benders 仍待迁移。Gurobi 当前旧 IIS 重载自身不产出 IIS 证据，本批真实测试仅验证该重载的
传递，不代表 IIS 提取通过。其他真实后端、互通和性能仍未验收。

本批新增 Framework 67 项专项，最终额外重复 5 次通过；串行／并行报告及旧入口关联专项共
152 passed。修复前不含阻塞用例的首组为 21 failed／11 passed；之后修正了普通线性用例的模型
类型转换，确保真正调用普通重载。Gurobi 10.0.1 新增 12 个实际普通／解池／IIS 重载场景，
opt-in 全套 798 passed；隔离 SDK 的 opt-in 按预期报错，不计通过，随后默认测试有／无 SDK 分别
50／5 passed。Windows 有／无 SDK 全量分别 4540／4488 passed、各 1 skipped；Linux 离线
4488 passed、1 skipped。三种强制构建均 0 errors，既有警告分别 743／718／718，本批文件无
新增警告。Linux 容器未启动数据库，离线 NuGet 审计关闭；这不代表联网恢复、安全审计、Linux
原生后端或外部服务验收。既有 Delaunay 用例仍跳过，全仓空白检查仍仅报告既有
`LinearConstraintInput.cs` 文件末尾空行。

### Previous gate: Framework serial legacy solve entries

| Pinned Kotlin source | C# entry point | Contract coverage | Status |
|---|---|---|---|
| `framework/solver/SerialCombinatorialLinearSolver.kt`, `SerialCombinatorialQuadraticSolver.kt` | Framework serial linear/quadratic `InvokeAsync` ordinary, pool and legacy IIS overloads | `SerialCombinatorialLegacyTest`, `SerialCombinatorialReportTest`, `GurobiReportIntegrationTest.SerialLegacyEntriesUseRealGurobiBackends` | Legacy retry/stop contract implemented; selected Gurobi 10.0.1 cases verified, other real backends not accepted |

- The old entries call each backend's old interface rather than manufacturing a report. After
  identity validation, they attempt factories in order, keep the first usable incumbent and its
  original pool, and stop on `Fatal`, `ApplicationStopped`, or a configured error code. The
  default stop set contains infeasible and unbounded, not infeasible-or-unbounded. The factory
  list and stop set are snapshotted; factories are called per attempt, unlike Kotlin's lazy cached
  instances. A non-stop failure or malformed result permits another attempt. A terminal failure
  is returned unchanged; when every attempt fails without a terminal error, the first failure is
  returned. No factories produces `SolverNotFound`. Unlike the report path, the legacy result has
  no complete attempt history or reliable backend termination/proof information.
- For IIS overloads, known raw status and report outputs respect the stop set; a cancelled report
  is terminal even when it contains an incumbent. Otherwise nonterminal diagnostics allow later
  attempts, and the first raw diagnostic is preserved by reference as a fallback if no terminal
  result is found. Fatal or stop-code failures take precedence over that fallback. The original
  `SolverOutputWithIIS<T>` and `SolverOutputWithoutIIS` now inherit `SolverOutput` and implement
  `ISolverOutputWrapper`: nested wrappers are classified while the outer object and evidence remain
  intact. The shared parallel legacy selection also recognizes wrapped incumbents. Null, cyclic,
  throwing or more-than-1024-layer wrapper chains are rejected as invalid; custom wrapper getters
  must be stable and terminate. The legacy `ToSolveReport` adapter still returns a structured
  unsupported report for these wrappers rather than silently dropping their IIS evidence.
  `WithoutIIS` adds no new evidence but does not remove evidence already present in its wrapped
  output; it is not a redaction operation.
- Pre-cancellation and cancellation between attempts prevent later factory calls. A terminal
  result returned by an already-running backend wins over a concurrently cancelled token; when no
  terminal result is available, cancellation takes precedence over fallback diagnostics. An
  `OperationCanceledException` means cancellation only if the supplied token is cancelled;
  unrelated concurrent interruption cannot be distinguished. Backend calls are awaited directly,
  may block the caller, and cannot be forcibly interrupted if the backend ignores cancellation.
  The caller must keep the model open and unchanged, and keep factories, backends, callbacks,
  returned errors and wrapper contents stable. Ordinary logger exceptions are suppressed for both
  legacy and serial report paths; exception details are still sent to the logger and backend error
  text is passed through, so each boundary needs redaction.
- Selection checks only the presence of the expected output/solution and pool list. It does not
  validate every pool item, dimensions, numerical finiteness, feasibility, objective, IIS evidence
  or proof, and it does not copy nested output data. `Config` and `DumpAsync` still use the first
  factory and are not covered by this repair. As with the parallel entry, a concrete linear model
  may bind the IIS overload; cast to `ILinearTriadModelView` for the ordinary overload. Gurobi's
  old IIS entry still does not extract IIS. Core parallel and column-generation combinators,
  Framework column generation/Benders, other real backends, wire interoperability and performance
  remain incomplete or unaccepted; P2/P3/P4 and the full synchronization remain in progress.
- The Framework suite gains 75 cases including serial retry, pool and both quadratic IIS overloads,
  stop-code and factory snapshots, invalid models/results/wrappers, error precedence, logger
  failures and cancellation races, plus wrapped selection in the parallel and report paths. The
  initial 51-case red run had 38 failures and 13 passes. The focused combinatorial set passes 230,
  including three further repeated runs; Core adds one adapter-boundary case. Gurobi 10.0.1 adds
  14 real feasible/infeasible legacy-entry cases and its full explicit opt-in suite passes 812.
  Isolating the SDK makes opt-in fail with the expected missing-SDK error, which is not a pass.
  After the full gates, the `WithoutIIS` documentation and explicit nested-evidence assertions
  were clarified; the rebuilt focused set again passed 230.
- Forced Windows Release builds have 0 errors / 743 existing warnings with SDKs and 0 errors /
  718 existing warnings with isolated SDK discovery and artifacts. Their full default suites pass
  4616 / 1 skipped across 23 test-bearing assemblies and 4564 / 1 skipped across 22 respectively;
  Framework has 264 passed and Core 2465. Linux x64 Debian 13 offline with .NET 10.0.402, a local
  NuGet cache and the `postgres:16` system layer (no database started in that container) builds
  with 0 errors / 718 existing warnings and passes 4564 / 1 skipped across 22 test-bearing
  assemblies. Offline NuGet audit is disabled; these checks do not establish fresh network restore,
  security audit or Linux native solver acceptance. The existing Delaunay precision case remains
  skipped; Hexaly, Benchmark and no-SDK MOSEK discover no tests. This batch introduces no build
  warnings. Repository-wide `git diff --check` still reports the pre-existing
  `LinearConstraintInput.cs` EOF blank line.

中文说明：Framework 串行线性／二次旧普通、解池和 IIS 重载继续调用后端旧 API，先校验模型
身份，再按工厂顺序尝试。遇可用 incumbent、Fatal、`ApplicationStopped` 或配置的停止错误码即
结束，默认停止码为不可行和无界；停止错误按原结果返回，全部可重试失败时返回首个失败，空工厂
返回 `SolverNotFound`。工厂及停止码列表在构造时快照，工厂每次尝试重新执行，不同于 Kotlin
缓存的懒实例。旧入口不保留完整尝试轨迹或真实终止原因、证明；需要这些信息应使用报告入口。

IIS 入口识别原始状态和报告，取消报告即使带 incumbent 也视为终态；其他非终态诊断允许继续
尝试，无终态时保留首个原始诊断，Fatal 或停止码失败优先于该兜底。`SolverOutputWithIIS<T>`、
`SolverOutputWithoutIIS` 现继承 `SolverOutput` 并实现 `ISolverOutputWrapper`；判断嵌套输出时
保留外层对象及证据，并行旧入口也能识别包装的 incumbent。空、循环、抛异常或超过 1024 层的
包装被拒绝；调用方须保证包装 getter 稳定且能结束。现有 `ToSolveReport` 对包装返回结构化
不支持，不会悄悄丢弃 IIS 证据；Gurobi 旧 IIS 本身仍不提取 IIS。
`WithoutIIS` 只是不再附加新证据，不会删除内部输出已有证据，不能用于脱敏。

预取消和尝试间取消不启动后续工厂；已完成的终态优先于并发取消，无终态时取消优先于兜底诊断。
仅在传入令牌已取消时将 `OperationCanceledException` 视为取消。后端直接 await，可能阻塞调用
线程，忽略取消的后端不能强制中止；调用方必须保持模型打开且不修改，并保证工厂、后端、回调、
错误及包装内容稳定。普通日志异常被抑制，异常详情仍发送日志，后端错误原样传播，均需在边界
脱敏。入口不验证解维度、解池内容、数值、目标、可行性、IIS 或证明，也不复制嵌套数据；
`Config`、`DumpAsync` 仍只使用首个工厂。线性具体模型如需普通重载须转为 `ILinearTriadModelView`。

Framework 本批新增 75 项，组合关联专项 230 项通过并额外重复 3 次，初始 51 项红灯为 38 失败／
13 通过；Core 另加 1 项报告适配边界。Gurobi 10.0.1 新增 14 项真实入口测试，显式 opt-in 全套
812 passed；缺 SDK 的 opt-in 按预期报错，不计通过。全量门禁后仅澄清 `WithoutIIS` 注释并补充
嵌套证据断言，重建后关联专项再次 230 passed。Windows 有／无 SDK 全量分别 4616／4564
passed、各 1 skipped；Linux 离线 4564 passed、1 skipped。三种构建均 0 errors，既有警告分别
743／718／718。Linux 容器未启动数据库，离线 NuGet 审计关闭，不代表联网恢复、安全审计或原生
后端验收。Delaunay 既有用例仍跳过，全仓空白检查仍仅报告 `LinearConstraintInput.cs` 的既有 EOF
空行。Core 并行与列生成组合器、Framework 列生成／Benders、其他真实后端、互通及性能仍未完成
或未验收；P2/P3/P4 和全量同步保持进行中。

### Current gate: one home for linear and quadratic combinators

| Pinned Kotlin source | C# entry point | Contract coverage | Status |
|---|---|---|---|
| `framework/solver/ParallelCombinatorialLinearSolver.kt`, `ParallelCombinatorialQuadraticSolver.kt`, `SerialCombinatorialLinearSolver.kt`, `SerialCombinatorialQuadraticSolver.kt` | `Framework.Solver` linear/quadratic parallel and serial combinators | `CombinatorialPublicApiTest`, `CombinatorialDumpBoundaryTest`, serial/parallel report and legacy tests, dump-forwarding tests, `GurobiReportIntegrationTest` | Abstract backend and nesting contracts verified; selected real Gurobi 10.0.1 cases verified, not full backend acceptance |

**Migration / 迁移：**

- Replace `Core.Solver.ParallelCombinatorialLinearSolver` and
  `Core.Solver.ParallelCombinatorialQuadraticSolver` with the identically named classes in
  `Framework.Solver`; add the Framework reference. Use `Framework.Solver.ParallelCombinatorialMode`
  for these classes. The Core column-generation combinator, its interface and its own mode remain;
  they are a separate, incomplete migration and must not be confused with the Framework versions.
- All four Framework linear/quadratic combinators now implement `IAbstractLinearSolver` or
  `IAbstractQuadraticSolver`, not `ILinearSolver` or `IQuadraticSolver`. Update variables, arrays,
  and consumers to the abstract interface. They no longer expose `Config`: configure and inspect
  the actual backend instead. `IReadOnlyList<Func<ILinearSolver>>` and its quadratic counterpart
  remain usable via covariance; an arbitrary factory `IEnumerable` must be materialized, e.g.
  `factories.ToArray()`. Existing instances can still be supplied as an enumerable. Factory lists
  are snapshotted, but factory results are not cached or disposed by the combinator.
- For former Core parallel `First` users, the Framework implementation waits for every scheduled
  backend and selects the first incumbent in factory order, rather than returning the first to
  finish and cancelling the rest. A backend that ignores cancellation can delay or prevent return.
  During parallel solve the model must remain open and unchanged, and the caller must ensure that
  backend instances, factories, views and callbacks are concurrency-safe. The report and legacy
  selection details and limitations are recorded in the preceding gates.
- `DumpAsync` now forwards to the first factory instead of the removed Core combinator's direct
  mechanism-to-matrix conversion; it never retries a later backend. If backend-independent conversion
  is needed, call `ToLinearTriadModel` or `ToQuadraticTetradModel` directly from
  `Fuookami.Ospf.Core.Model.Intermediate`. The combinator forwards
  successful, failed and fatal backend results and callback/token arguments unchanged. An empty
  factory list returns `SolverNotFound`, even with a cancelled token; factory/backend exceptions,
  null tasks/results and null backend instances become a failure, while an
  `OperationCanceledException` maps to `ApplicationStopped` only when the supplied token is
  cancelled. The token is otherwise delegated to the backend without pre-cancellation. Ordinary
  logger failures are suppressed, but exception details may be logged and require redaction.
- Accessing the lazy `Name` property evaluates every factory and may itself throw; its value or
  exception is cached. Solving and dumping invoke the factories again, so do not use `Name` as a
  health check or assume a single initialization. No native writer, independent feasibility/proof
  validation, concurrency quota or full backend lifecycle management is added by this batch.
- Before the change, abstract-only backends caused 14 compile errors and the 40 new dump-boundary
  cases failed. The focused combinatorial/dump suite now passes 291 cases and passes three further
  runs. It includes 59 added cases for the public surface, abstract-only backends, nested reports,
  dump forwarding, empty/failing/null factories, invalid backend tasks/results and logging failures.
  Gurobi 10.0.1's explicit opt-in suite passes 812; opting in without the SDK fails with the expected
  missing-SDK error and is not a pass.
- Forced Windows Release builds have 0 errors and 743 / 718 existing warnings with / without SDKs;
  default suites pass 4675 / 4623, each with one skip, across 23 / 22 test-bearing assemblies.
  Linux x64 Debian 13 offline, using .NET 10.0.402 and a local NuGet cache, builds with 0 errors /
  718 existing warnings and passes 4623 with one skip across 22 assemblies. Framework passes 323
  in both Windows variants. The offline container used the `postgres:16` system layer but did not
  start a database; NuGet audit was disabled. These checks do not establish a fresh network restore,
  vulnerability audit, Linux native solver acceptance or external service acceptance. The existing
  Delaunay precision case remains skipped; Hexaly, Benchmark and no-SDK MOSEK discover no tests.
  This batch adds no build warnings. Repository-wide `git diff --check` still reports the existing
  `LinearConstraintInput.cs` EOF blank line. Column-generation combinators and Benders, other real
  backends, wire interoperability and performance remain incomplete or unaccepted; P2/P3/P4 and
  full synchronization remain in progress.

中文说明：Core 的线性／二次并行类型已移除，使用 Framework 中同名类型并切换为 Framework
的 `ParallelCombinatorialMode`；Core 的列生成组合器、接口及其枚举仍保留且尚未统一。四个
Framework 组合器只实现 `IAbstractLinearSolver` 或 `IAbstractQuadraticSolver`，调用方应改用
抽象接口，并在真实后端配置或读取配置，不再使用组合器的 `Config`。具体接口的只读工厂列表可
协变传入；任意工厂枚举先调用 `ToArray()`。工厂列表会快照，但工厂结果不会缓存或自动释放。

原 Core `First` 是按完成顺序提前返回并取消其余后端，迁移后会等待所有已调度尝试，并按工厂顺序
选首个 incumbent；忽略取消的后端可能使调用无法返回。并行期间须保证模型打开且不修改，后端、
视图、工厂与回调支持并发。报告和旧入口的选择及其限制见前述批次。

`DumpAsync` 改为只调用首个工厂，不重试；原 Core 的直接矩阵转换如仍需要，可直接调用
`Fuookami.Ospf.Core.Model.Intermediate` 中的 `ToLinearTriadModel`／`ToQuadraticTetradModel`。
正常结果、失败、Fatal、回调和取消令牌原样转发；
空工厂即使预取消仍返回 `SolverNotFound`，工厂／后端异常、空任务／结果或空后端返回失败，只有
所传令牌已取消时的 `OperationCanceledException` 映射为 `ApplicationStopped`。组合器不预先取消
非空工厂，由后端处理令牌。普通日志异常被抑制，但异常详情可能写入日志，须在边界脱敏。
惰性 `Name` 会执行所有工厂，可能抛异常并缓存结果或异常；实际求解和转储会再次调用工厂，不能
将 `Name` 当健康检查，也不能假定只初始化一次。本批不增加原生 writer、独立可行性／证明复核、
并发额度或完整后端资源管理。

修改前抽象后端用例出现 14 个编译错误，新增 40 个转储边界用例全部失败；修复后组合与转储关联
专项 291 项通过，额外重复 3 次均通过。本批新增 59 项，覆盖公共接口、仅实现抽象接口的后端、
报告嵌套、转储传参、空／失败／空值工厂、无效后端任务／结果与日志异常。Gurobi 10.0.1 显式
opt-in 全套 812 项通过；缺 SDK 的 opt-in 按预期报错，不计通过。

Windows Release 有／无 SDK 全量分别 4675／4623 通过、各 1 项跳过，覆盖 23／22 个有测试
程序集；Framework 均为 323 项通过。Linux x64 Debian 13 离线使用 .NET 10.0.402 和本机 NuGet
缓存，4623 项通过、1 项跳过，覆盖 22 个有测试程序集。三种强制构建均 0 错误，既有警告分别
743／718／718，本批无新增警告。Linux 使用 `postgres:16` 系统层但未启动数据库，离线 NuGet
审计关闭；这不代表联网恢复、安全审计、Linux 原生求解器或外部服务验收。Delaunay 既有用例
仍跳过，Hexaly、Benchmark 及无 SDK MOSEK 无可运行测试；全仓空白检查仍仅报告既有
`LinearConstraintInput.cs` 的 EOF 空行。列生成组合器与 Benders、其他真实后端、互通及性能仍
未完成或未验收；P2/P3/P4 和全量同步保持进行中。

### Previous gate: first native SCIP linear solves

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core-plugin-scip/ScipLinearSolver.kt` | `ScipLinearSolver.InvokeReportAsync`, legacy `InvokeAsync` | `ScipLinearContractTest`, `ScipLinearIntegrationTest` | Selected Windows SCIP 9.2.4 LP/MIP solves verified; incomplete backend |
| `core-plugin-scip/ScipColumnGenerationSolver.kt`, `ScipBendersDecompositionSolver.kt` | `ScipColumnGenerationSolver.SolveMILPAsync`, `ScipBendersDecompositionSolver.SolveMasterAsync` | `ScipLinearIntegrationTest.FrameworkMasterAppliesNativeSolution` | Selected master/writeback cases verified; dual, pricing and Benders subproblem remain unsupported |

The linear adapter now loads the official SCIP C API, not JSCIP or a managed SCIP DLL. It accepts
the SCIP 9.2 ABI only; Windows 9.2.4 is the one real native version exercised. It looks under
`SCIP_HOME/bin/libscip.dll` on Windows or `SCIP_HOME/lib/libscip.so` on Linux, then uses the native
library search path; macOS uses the corresponding `.dylib` name but is unverified. Required native
dependencies must also be loadable. Missing, incompatible or incomplete libraries return an
environment failure. The library is optional at build time, and no SDK path is hardcoded.

Continuous and built-in integer/binary variables, linear constraints in all three directions,
minimum/maximum objectives, constants and repeated coefficients are supported within the admitted
numeric range. Duplicate coefficients are summed in input order before building native rows and
objectives. Actual infinities on continuous bounds map to SCIP infinity; finite values at or beyond
SCIP's infinity are rejected rather than silently changed. Integer bounds must be finite and within
`[-9007199254740991, 9007199254740991]`; this is a deliberate restriction, not full Kotlin parity.
Binary, ternary, balanced ternary, percentage and unsigned intrinsic domains are intersected with
the supplied bounds. Disjoint bound/type domains return `IllegalArgument`, even though such a domain
could also be modeled as infeasible. Custom variable types, non-finite coefficients and invalid
indices or row metadata are rejected. Modeling and solving use binary64 and SCIP's numerical
tolerances; finite/exact-integer input checks do not independently verify feasibility or a proof.

The report distinguishes optimal, infeasible, unbounded and infeasible-or-unbounded conclusions,
incumbent presence and mapped termination causes. Native proof claims are `Claimed`, never
`Verified`; the main solution is a copied read-only vector, the descriptor records native version,
and LP/MIP are the only advertised model types. Missing finite bounds/gaps remain absent. SCIP
statuses without an exact existing termination enum, including gap, memory and restart limits, map
conservatively to `BackendFailure` with the raw native status in a warning. The legacy feasible-only
entry maps no-incumbent conclusions to errors and refuses to invent a missing bound or gap for a
non-optimal incumbent. Do not mutate or close a model during snapshot capture or solve; no independent
post-solve feasibility check, fingerprint or full source-structure revalidation is added here.
Concurrency between separate native SCIP solves has not been established; callers should serialize
SCIP calls in this batch, including those submitted through a parallel combinator.

**Migration / 迁移：** `ScipLinearSolver` now defaults to `ScipSolverConfig` rather than
`VendorSolverConfig`; the latter is still accepted. Its meta-model dump uses the shared mechanism
conversion without loading SCIP. At this gate an already-cancelled ordinary report returned `Cancelled`
before loading a library, but a still-cancellable token was explicitly rejected; the next gate adds
event-driven cancellation. Warm starts, status callbacks, solution pools, IIS and custom solver
configurations are likewise rejected, not ignored. Mechanism dump rejects its unavailable dumping
callback and concurrent/blocking flags; matrix dump rejects unavailable intermediate dump flags.
The implementation may block the calling thread. The Framework master entries reject model logging
and status callbacks. Column-generation LP and Benders subproblem calls fail explicitly instead of
returning empty duals or Farkas evidence; do not use these partial adapters as complete algorithms.
At that gate, quadratic, CP, native function writers, dual/Farkas, mid-solve cancellation,
checkpoint, other SCIP versions and Linux/macOS native integration remained unimplemented or unverified.

The opt-in command is `dotnet test tests/Core.Plugin.Scip.Tests/Core.Plugin.Scip.Tests.csproj -c Release
-p:RunScipIntegration=true`. On Windows x64 with SCIP 9.2.4 it passes 47 cases, including 19 real
native cases for LP/MIP, offsets and directions, infeasible/unbounded and zero-column models,
built-in domains, invalid native-range inputs, two Framework master/writeback paths and 50 sequential
solves. A prior 14-case native subset passed three additional runs. This is not a native leak or
concurrency stress proof. Both `SCIP_HOME`-only and search-path-only loading passed two selected
cases; an isolated missing-library opt-in failed as expected and is not a pass.

Windows Release default tests pass 4694 with one existing skip across 23 test-bearing assemblies;
isolating SDKs passes 4642 with one skip across 22 assemblies. Linux x64 Debian 13 offline, using
the SHA-512-verified .NET SDK 10.0.402 and a copy of the local NuGet cache, passes 4642 with one
skip across 22 assemblies. Forced builds have 0 errors and 743 / 718 / 718 existing warnings for
Windows with SDKs / Windows without SDKs / Linux without SDKs; this batch adds no warnings. The
Linux container used the `postgres:16` system layer without starting a database; NuGet audit was
disabled, so this is not a network restore, vulnerability audit, Linux native SCIP or service
acceptance. The pre-existing Delaunay precision case remains skipped; Hexaly, Benchmark and
no-SDK MOSEK discover no tests. After those full gates, two explanatory shortcut comments were added
and the opt-in test assertions were reformatted; the SCIP opt-in and default project suites were
rebuilt and rerun, passing 47 and 28 respectively.
P3 and full synchronization remain in progress.

中文说明：首批线性 SCIP 已通过原生 C API 在 Windows 的 9.2.4 上验证代表性 LP/MIP、报告、
空列模型及 Framework 主问题回填；不是 SCIP 全能力验收。仅接受 9.2 ABI；整数边界必须有限且
处于二进制 64 位精确整数范围，原生无穷阈值外的有限数、未知变量类型和损坏矩阵均拒绝。解、
目标、证明仍受 SCIP 浮点容差影响，不做独立复验。当时解池、热启动、IIS、求解回调、中途取消、
对偶与 Farkas 均明确不支持；中途取消的后续进展见下一节。列生成 LP 和 Benders 子问题不会返回空证据冒充成功。模型转储和
求解可能阻塞，调用方须保持模型不变且存活。未知／间隙／内存／重启等未精确映射的终态带原生
状态警告并保守归类为后端失败；Linux 原生库、其他版本、完整并发与资源泄漏未验收。
当前应在调用方串行化 SCIP 求解，包括通过并行组合器发起的多个 SCIP 实例。
全量门禁后补充两处限制说明注释并调整集成测试断言排版，随后重建重跑 SCIP 显式集成／默认
项目，分别通过 47／28 项。

### Previous gate: SCIP event-driven cancellation

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `ScipSolver.kt`, `ScipLinearSolver.kt`, `ScipConstraintProgrammingStatusMapper.kt` | `ScipLinearSolver.InvokeReportAsync`, `InvokeAsync` | `ScipLinearContractTest`, `ScipLinearIntegrationTest` | Selected Windows SCIP 9.2.4 mid-search cancellation verified; event-limited, not full native interruption parity |

An active .NET cancellation token no longer causes immediate rejection of an otherwise valid linear
solve. The adapter checks the token during snapshot capture and native model construction. During
`SCIPsolve`, a SCIP event handler checks it on the solving thread at transformation initialization,
presolve-round, node, LP and solution events, and calls `SCIPinterruptSolve` there. The handler and
its delegates remain alive through `SCIPfree`; cancellation itself does not call native code on the
requesting thread. SCIP 9.2's implementation writes a non-atomic internal `userinterrupt` flag, so
this deliberately avoids the Kotlin binding's direct cross-thread interrupt. It is a behavioral gap:
there is no bounded response time when SCIP spends a long time between events, and a solve may finish
before another event. Identity validation and individual native calls are not interruptible.

The native result takes precedence over a late request: optimal, infeasible, unbounded and
infeasible-or-unbounded remain `Completed`. Native `USERINTERRUPT` is `Cancelled` only when the
adapter successfully requested interruption; otherwise it is `Interrupted`. Other native limits keep
their actual reason even if the token is later cancelled, unlike the pinned Kotlin mapping's broader
override. A cancelled native report retains an available incumbent and never claims a proof. An
earlier cancellation returns an empty cancelled report; the feasible-only legacy entry returns
`ApplicationStopped` when it cannot return an incumbent. The descriptor advertises `Interrupt`,
subject to the event limitation above. At this gate status callbacks, solution pools, warm starts,
IIS, duals, Farkas and cancellation during meta-model dumping remained unsupported. The next gate
adds initial-solution hints.

The Windows SCIP 9.2.4 opt-in suite passes 61 cases, including 23 native cases; an 80-vertex
max-cut MIP is cancelled during native search and the same timed test passed five additional runs.
That test uses a timer and depends on reaching native search before cancellation, so it is not a
deterministic scheduling or leak proof. Tests also cover cancellation during snapshot capture,
native event initialization, a cancelled pigeonhole transformation and its native report,
20 completed solves followed by late cancellation, actual native status mapping and unsupported-model
validation. An isolated missing-library opt-in failed with
`OREngineEnvironmentLost` as expected and is not counted as a pass.

Windows Release with / without configured SDKs passes 4704 / 4652 tests, respectively, with one
existing skip; offline Linux x64 Debian 13 passes 4652 with one skip. Forced builds have 0 errors
and 743 / 718 / 718 existing warnings, with no new warnings in the touched SCIP files. The Linux
container used the `postgres:16` system layer without starting a database, an offline NuGet cache and
disabled NuGet audit; this is not a vulnerability audit or external-service acceptance.
After those full gates, `ReadReport` was made internal for the new transformation test and the
opt-in assertions were extended. The SCIP opt-in / default project suites were rebuilt and rerun,
passing 61 / 38 cases without new warnings; the full gates were not repeated after those edits.

中文说明：线性 SCIP 现可在快照、原生建模和 SCIP 事件回调处响应取消。为避免 SCIP 9.2
非原子原生中断标志的跨线程竞争，中断只在求解线程的事件回调中发起；事件之间的长时间
原生计算无法保证及时停止，结果也可能先完成。已完成的原生结论优先；只有本适配器成功请求
的原生用户中断才映射为 `Cancelled`，其他限制保留真实原因；取消报告保留已有 incumbent，
不伪造证明。这与 Kotlin 的直接跨线程中断及非完成状态覆盖语义不同。Windows 9.2.4 的真实
MIP 中途取消已验证，但定时测试不证明任意调度、并发安全或无泄漏。当时状态回调、解池、热启动、
对偶、Farkas、IIS、二次与 CP 尚未完成；初始解提示的后续进展见下一节。转储期间取消仍不支持，
SCIP 求解仍应由调用方串行化。

### Previous gate: SCIP initial-solution hints

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core-plugin-scip/ScipLinearSolver.kt` | `ModelViewVariable.InitialResult`, `ScipLinearSolver.InvokeReportAsync`, `InvokeAsync` | `ScipLinearContractTest`, `ScipLinearIntegrationTest` | Complete and partial candidate submission verified with Windows SCIP 9.2.4; the backend remains incomplete |

The linear snapshot now retains each supplied initial value. After constructing all original rows,
the native adapter creates a complete solution when every column has a value, or a partial solution
when only some do. It sets only supplied values and submits the candidate through `SCIPaddSolFree`;
unprovided values in a partial solution remain SCIP's unknown value, not zero. With no supplied
values, including a zero-column model, no candidate is created. This differs from the pinned Kotlin
implementation's submission before row creation and its empty complete candidate for zero columns.
The report descriptor now advertises `WarmStart` for this hint interface.

An initial value is a hint, not a fixed variable, an incumbent or a proof. SCIP may reject or not
use it, and the adapter does not expose the native `stored` flag or independently establish
feasibility. Finite out-of-domain or fractional values are passed to SCIP as hints; nonfinite values
are rejected during snapshot capture, and finite values at or beyond SCIP's infinity are rejected
before native model construction. Submission failures return errors, and an allocated candidate is
freed on cancellation or failure. A cancellation before native solving still returns the earlier
empty cancelled report even if a candidate was submitted; event-limited interruption and report
semantics from the preceding gate are unchanged. Native function writers, quadratic/CP starts and
solver-wide warm-start behavior are not implemented by this linear entry point.

Windows SCIP 9.2.4 opt-in passes 84 cases, including 36 native cases; the default SCIP project
passes 48. Tests inspect the actual complete and partial native candidate stores, exercise feasible,
infeasible, fractional, out-of-domain and partial hints, check nonfinite/native-infinity boundaries,
include a continuous LP and partial invalid hints, verify Framework writeback appears in the next
matrix as an initial value before repeating master solves, and verify an infeasible candidate is not
reported as an incumbent when transformation is cancelled. The six initial snapshot checks failed
before implementation. The first native test attempt exposed a non-exported Windows API used only
by the test; that mandatory binding was removed and the native suite then passed. A missing-library
opt-in failed with `OREngineEnvironmentLost` as expected and is not counted as integration success.

Windows Release with / without configured SDKs passes 4714 / 4662 tests, respectively, with one
existing skip across 23 / 22 test-bearing assemblies. Offline Linux x64 Debian 13 passes 4662 with
one skip across 22 test-bearing assemblies. Forced builds have 0 errors and 743 / 718 / 718 existing
warnings; none is new in the touched SCIP files. After those full gates, argument formatting changed
in SCIP source and tests, three opt-in cases and a Framework initial-value assertion were added; the
Windows opt-in / default project
suites were rebuilt and rerun at 84 / 48. Hexaly and Benchmark discover no tests, and no-SDK MOSEK
also discovers none. The
existing Delaunay precision case remains skipped.

The Linux container used the `postgres:16` system layer without starting a database, a copied local
NuGet cache and disabled NuGet audit. Its default suite does not exercise native SCIP, external
services, network restore or vulnerability auditing. Linux/macOS native SCIP and other SCIP versions
remain unverified. Callers must continue to serialize SCIP solves; no concurrency, leak or performance
claim is made. At this gate status callbacks, solution pools, duals, Farkas, IIS, quadratic and CP
remained gaps; the next gate addresses linear solution pools. P3 and full synchronization remain in progress.

中文说明：线性 SCIP 现在接收完整或部分 `InitialResult`，分别提交完整或部分原生候选；部分解
未指定的变量保持未知，不自动填零。候选只是提示，可能被 SCIP 丢弃或不用，不代表固定变量、
已验证可行解或证明；原生是否存储未向调用方暴露。非有限初始值和达到原生无穷阈值的有限值
会被拒绝，其他越界或非整数提示交由 SCIP 处理。初始解在约束建模完成后提交，与 Kotlin 的提交
时机不同；无初始值及零列模型不创建候选。提交失败或取消时释放未转交的解对象，原生求解前取消
仍返回空取消报告。Windows 原生测试覆盖部分解未知值、非法提示及转换阶段取消，重复的 Framework
主问题也可使用前次回填的值；Linux 原生后端尚未验收，P3 和全量同步仍未完成。

### Previous gate: SCIP linear solution pools

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core-plugin-scip/ScipLinearSolver.kt` | `ScipLinearSolver.InvokeReportAsync`, `InvokeAsync`, `SolveSolution<Flt64>.Pool` | `ScipLinearContractTest`, `ScipLinearIntegrationTest` | Linear LP/MIP stored-solution extraction verified with Windows SCIP 9.2.4; not complete backend acceptance |
| `core-plugin-scip/ScipColumnGenerationSolver.kt` | `ScipColumnGenerationSolver.SolveMILPAsync` | `ScipLinearContractTest`, `ScipLinearIntegrationTest` | MILP pool and source-token writeback verified on Windows; LP dual extraction remains unsupported |

For an amount of 0 or 1 the linear entry performs an ordinary solve and returns no alternatives.
For a larger amount it sets `heuristics/dins/solnum`, inspects at most that many entries from
SCIP's stored solutions, skips the native best-solution pointer and deduplicates the other vectors
using `Flt64` equality. The primary solution is separate from `SolveSolution.Pool` and the legacy
linear pool. This follows the pinned Kotlin linear entry, but differs from the Gurobi report pool,
which can include the primary solution. Deduplication is among alternatives; a different native
pointer with the same vector as the primary is not explicitly removed. C# `Flt64` equality treats
positive and negative zero as equal. Requested values beyond `Int32.MaxValue` fail before native
loading rather than being narrowed; SCIP 9.2.4 accepts that maximum for the native parameter.

The parameter influences a SCIP heuristic; it does not request a complete search or increase the
solver's independent storage limit. SCIP may return fewer solutions or none beyond the primary;
the stored candidates are not a guarantee of global best N, enumeration, or distinct objectives.
The adapter validates that extracted values are finite and below SCIP's infinity, but does not
independently establish feasibility, objective values or proofs. A report without an incumbent has
no pool; the legacy entry continues to map unrepresentable or infeasible results to its existing
errors. Only transformed-problem solutions are considered: an original-problem candidate is not
reported as an incumbent. Bounds, gap and LP iterations are read only in the SCIP stages where
their native APIs allow it. Cancellation remains event-limited as described in the earlier gate.

The Framework MILP pool prepends the primary even for amounts 0 and 1, restores every vector to
mechanism-model column order, then applies only the primary to the source tokens. Its output and
returned mutable pool entries do not share the same value buffer. Unsupported model logging and
status callbacks are rejected before dumping, and oversized requests are rejected before model
registration. Reports copy solution vectors and pool metadata before the native SCIP instance is
freed; the capability descriptor declares `SolutionPool` and records the effective DINS parameter.
This does not implement a complete column-generation loop or LP duals.

The initial nine targeted checks failed before implementation. Windows SCIP 9.2.4 opt-in now passes
96 cases, including 47 native cases; the SCIP default project passes 49. Coverage includes primary
and additional solutions, amounts 0/1/2/8, legacy and generic entries, immutable report vectors,
Framework token writeback and options, infeasible and pre-cancelled cases, an unverified original
candidate, transformation and search cancellation, and oversized input. Ten pool-related checks
were repeated three additional times. A no-library opt-in failed with `OREngineEnvironmentLost` as
expected and is not counted as an integration pass. These tests do not prove concurrency safety,
absence of leaks, arbitrary cancellation scheduling, or all possible native status combinations.

Windows Release with / without configured commercial SDKs passes 4715 / 4663 tests, respectively,
with one existing skip across 23 / 22 test-bearing assemblies. Offline Linux x64 Debian 13 passes
4663 with one skip across 22 test-bearing assemblies. Forced builds have 0 errors and 743 / 718 /
718 existing warnings; no new warning appears in the touched SCIP files. After these gates only
public XML remarks were added, followed by rerunning the Windows SCIP opt-in / default project
suites at 96 / 49. The pre-existing Delaunay precision test remains skipped; Hexaly and Benchmark,
and no-SDK MOSEK, have no discoverable tests. The repository-wide `git diff --check` also reports
an existing blank line at EOF in `src/Core/Model/Mechanism/LinearConstraintInput.cs`, outside this
batch; it was not changed here.

The Linux container used the `postgres:16` system layer without starting a database, a local NuGet
cache copy and disabled NuGet audit. Its default gate does not validate native SCIP, external
services, online restore or vulnerability auditing. The Linux SCIP 9.2.4 package download remains
in progress and must pass length and SHA-256 checks before an isolated native run. Linux/macOS native
SCIP, other SCIP versions, cross-language fixtures, performance, concurrency and leak checks remain
unverified. Status callbacks, duals, Farkas, IIS, quadratic and CP remain incomplete. P3 and full
synchronization remain in progress.

中文说明：SCIP 线性报告和旧入口将主解与附加解分开；数量为 0 或 1 时附加解为空，否则设置
`heuristics/dins/solnum`，最多检查请求数量的原生存储项、跳过主解指针，并按 `Flt64` 相等性
去重其他解。不同指针的同值主解没有额外排除，正负零视为相等。此参数不会要求完整搜索或扩容
原生存储，解的数量、全局前 N、枚举完整性和不同目标值均无保证。超过 `Int32.MaxValue` 的请求
在加载原生库前返回参数错误；原始问题阶段未验证的候选不会被当作 incumbent，原生统计仅在
允许的阶段读取。报告在释放 SCIP 前复制数据，但不独立复核解可行性、目标或证明。

Framework MILP 解池即使请求 0 或 1 个解也以主解开头，按机制模型列顺序还原每个向量，并仅将
主解回填 token；输出与可变解池不共享值缓冲区。该批不实现列生成 LP 对偶或完整列生成算法。
Windows 原生 9.2.4 通过 96 项、默认项目通过 49 项；Windows 有／无商业 SDK 全量分别通过
4715／4663 项，Linux 离线默认通过 4663 项，均有 1 项既有跳过。Linux 原生 SCIP 包仍在续传，
原生跨平台、其他 SCIP 版本及其他外部后端不能标为已验收；P3 和全量同步仍在进行。

### Previous gate: unified solver runtime configuration and status callbacks

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core/solver/config/SolverConfig.kt` | `Core.Solver.Config.SolverConfig`, `GurobiSolverConfig`, `CoptSolverConfig`, `ScipSolverConfig` | `SolverConfigContractTest` | Contract verified: one concrete runtime record, Kotlin CPU heuristic, validated ranges, sorted and redacted backend parameters |
| `core/solver/report/SolverReportMetadata.kt` | `SolverReportMetadata.ConfigurationSnapshot`, `Provenance` | `SolverConfigContractTest`, `ScipLinearIntegrationTest`, `GurobiReportIntegrationTest` | Contract verified; duration text still differs from the Kotlin `30s` form, so cross-language configuration fingerprints remain a P4 interop gap |
| `core/solver/Gap.kt`, no-improvement monitor | `SolvingStatusSupport.Gap`, `NoImprovementMonitor` | `SolverConfigContractTest` | Contract verified against the pinned formula, including the objective-direction asymmetry |
| `core-plugin-gurobi/GurobiLinearSolver.kt` | `GurobiSolveCallback`, `GurobiSolver.Configure`, `GurobiSolver.Report` | `GurobiLinearCallbackTest`, `GurobiSolverContractTest`, `GurobiReportIntegrationTest` | Gurobi 10.0.1: time limit, MIP gap, threads, incumbent capture, bound/gap/runtime payloads, no-improvement interrupt and callback-failure abort exercised for real; reported backend version is the pinned SDK version, not a runtime probe |
| `core-plugin-scip/ScipLinearSolver.kt`, `SCIPSolverConfig.kt` | `ScipSolveEvents`, `ScipLinearNativeSolver.ConfigureParameters`, `ScipLinearSolver.InvokeReportCore` | `ScipLinearContractTest`, `ScipLinearIntegrationTest` | Windows SCIP 9.2.4: status callbacks, native parameter application, presolve/seed/deterministic handling and interruptible no-improvement abort verified; concurrent solving is not ported and is recorded as ignored |

`SolverConfig` is now a single concrete record shared by every solver: `TimeLimit` is a `TimeSpan`
defaulting to 30 seconds, `ThreadCount` is a `ulong` derived from the Kotlin processor heuristic
(`<= 16` uses the processor count, `< 24` gives 16, `< 32` gives 24, otherwise 32), `Gap` defaults
to zero, and `ImproveThreshold` defaults to the `Flt64` decimal precision. The five dump flags are
`bool?` so "unspecified" stays distinguishable. `ValidateRuntime` rejects negative durations, a
thread count with no native `int` representation, and negative or non-finite gap, improvement
threshold or interruptible gap with `IllegalArgument`; `NativeThreadCount` exposes the value only
when it is representable. This is a breaking upgrade: `VendorSolverConfig` and the per-plugin
`Gurobi`/`Copt`/`Scip`/`Mosek`/`Hexaly`/`Cplex` configuration subclasses and the public
`connectionConfig` constructor parameters are gone, and every repository call site now passes the
shared record. Vendor-specific values travel in `IBackendConfiguration`, whose `Parameters()` are
sorted ordinally and where a password is only ever published as `<redacted>` with `Sensitive` set.

`ConfigurationSnapshot` and `Provenance` always publish `time`, `threadNum`, `gap` and
`improveThreshold`, add the optional no-improvement and interruptible fields when configured, and
merge backend parameters under a `backend.` prefix. Provenance additionally carries the descriptor,
native version (falling back to `SolverDescriptor.BackendVersion`), effective thread count, SCIP
seed and determinism, and the parameters that were requested but not applied. Durations are
invariant-culture decimal seconds, whereas the pinned Kotlin baseline emits `Duration` text such as
`30s`; that difference is recorded in the public XML remarks and stays open for P4.

The shared status contract keeps the pinned gap formula `(obj - bound + precision) / (obj + precision)`
without renormalising for the objective direction, so a maximisation callback gap can be negative
exactly as in Kotlin. `NoImprovementMonitor` restarts its window whenever the objective or bound
moves by at least `ImproveThreshold`, and suppresses the interrupt before `InterruptibleTime` or
while the current gap is at least `InterruptibleGap`.

Gurobi now installs one `GurobiSolveCallback` for both linear and quadratic models instead of two
near-duplicate classes. It captures incumbents with `GetSolution`, reports objective, bound and
runtime on MIP events, evaluates the no-improvement monitor against native runtime, aborts the
solve when the status callback fails, and keeps native callback exceptions from crashing the solve.
`Configure` always applies `TimeLimit`, `MIPGap` and `Threads` after validation, and compute-server
settings come from `GurobiSolverConfig`. The pinned 10.0.1 managed API exposes no version query
(`GRB.StringParam` has no `Version` and `GurobiDLL` is internal), so the reported backend version is
the build-pinned `10.0.1` rather than a runtime probe; a mismatched native library would not be
reflected here.

SCIP merges cancellation and status monitoring into one `ScipSolveEvents` handler with the same
global event mask and stage gating as the previous batch, reading the primal bound when no
incumbent exists. Native parameters are applied for time limit, gap, thread count, pool size,
presolving setting, random-seed shift, deterministic variable and constraint permutation, and the
already-redacted `NativeParameters` map in ordinal order. **B1231 correction: `SCIPsolveConcurrent` is not ported, but
that is a <b>separate solve entry</b> (several seeds in parallel, first result returned), not a lost parameter — and this
document previously said the provenance records `parallel/maxnthreads` as ignored, which was <b>wrong</b>:
`parallel/maxnthreads` really is applied through `SetIntParameter` and `ParameterFailure` on mismatch, so recording it as
ignored claimed that an applied parameter was not applied. That claim has been removed; the capability difference is
disclosed here in prose instead of in a parameter bag.** The contract layer no
longer rejects status callbacks; it propagates runtime validation, rejects a non-SCIP backend
configuration, deterministic mode with more than one thread, and a seed outside the native integer
range, all before the native library is touched. Model-logging dumps remain unsupported.

Other plugins keep their existing behaviour but now share the unified configuration and dump
semantics. The Hexaly linear adapter no longer feeds a dump flag's hash code into a time limit:
the real Hexaly time limit lands with its own backend batch, because the pinned SDK signature
cannot be verified without `HEXALY_HOME`.

Twenty-two new Core contract cases cover defaults, the processor heuristic, `NativeThreadCount`
bounds, every `ValidateRuntime` branch, backend parameter ordering and redaction, snapshot key
order and prefixes, provenance fallback versus an explicit native version, the gap formula at zero
and at a real pair, and each monitor gate. One new SCIP contract case proves that invalid runtime
parameters, a wrong backend type, deterministic multi-threading and an out-of-range seed are all
rejected before native work, while a valid configuration is not rejected. Four new native SCIP
cases prove per-event callback payloads with monotonic native time, a callback failure that
interrupts the solve, a no-improvement interrupt, and a tiny time limit that is recorded honestly
without fabricating a proof. The related assertions failed against the previous behaviour before
implementation.

Windows SCIP 9.2.4 opt-in passes 101 cases (51 require the native library) and the SCIP default
project passes 50; the four timing-sensitive cases were repeated two extra times. A no-library
opt-in fails as expected with 51 failures and 46 contract passes, all reporting
`OREngineEnvironmentLost`, so the new gates do not mask a missing environment; those failures are
not counted as an integration pass. Gurobi 10.0.1 opt-in passes 812 cases. Windows Release with /
without configured commercial SDKs passes 4738 / 4686 tests with one existing skip across 23 / 22
test-bearing assemblies, and offline Linux x64 Debian 13 passes 4686 with one skip across 22
assemblies. Forced builds report 0 errors and 743 / 0 / 718 warnings: the isolated no-SDK copy
counts no analyzer warnings because its restore has no analyzer packages, which is not evidence
that warnings disappeared. This batch briefly added two IDE0007/IDE0008 style warnings plus two
compile errors, and the first no-SDK run exposed `GurobiSolveCallback.cs` missing from the SDK
exclusion list in `src/Core.Plugin.Gurobi/Core.Plugin.Gurobi.csproj`; all were fixed and the
rebuild is back to the 743 existing warnings.

Not verified or still open: the Gurobi runtime version probe, `SCIPsolveConcurrent`, the native
`ScipSolverCallBack` event surface, the cross-language duration fingerprint, Hexaly time limits,
and the real Gurobi 11, COPT, CPLEX, MindOpt, MOSEK, LINGO and OPTVerse backends. The Linux SCIP
package download stalled again at 27193312 of 40335444 bytes, so Linux native SCIP remains
unverified. SCIP duals, Farkas, IIS, quadratic and CP, the native function writers, remote wire
fixtures and performance measurements stay in later batches.

中文说明：本批把 C# 求解配置收敛为 Kotlin 的单一 `SolverConfig` record：时间限制改用 `TimeSpan`、
线程数改为按 Kotlin CPU 启发式推导的 `ulong`、间隙与改进阈值使用 `Flt64`、五个转储标志改为可空，
并新增 `NativeThreadCount` 与 `ValidateRuntime`。这是破坏式升级：`VendorSolverConfig`、各插件的
配置子类和公共 `connectionConfig` 构造参数已全部删除，仓库内 30 个调用点和相关测试同步迁移；
供应商专有值改由 `IBackendConfiguration` 承载，参数按 Ordinal 排序，密码只以 `<redacted>` 出现。
配置快照与运行来源始终记录 time／threadNum／gap／improveThreshold，后端参数以 `backend.` 前缀合并，
并额外记录线程数、SCIP 种子与确定性标志，以及"已声明但未生效"的忽略参数。时间文本使用不变文化
秒数，与 Kotlin 基线的 "30s" 文本不同，跨语言配置指纹仍未对齐。

Gurobi 用单个 `GurobiSolveCallback` 同时服务线性与二次模型，采集主解、目标、界与运行时，
按原生运行时判定无改进中断，回调失败即中止，且不让原生异常压垮求解；无条件下发 TimeLimit、
MIPGap 与 Threads。因 10.0.1 托管 API 未公开版本查询，报告的后端版本是构建锁定的 10.0.1，
不是运行时探测。SCIP 把取消检查与状态监听合并为一个事件处理器，下发时间、间隙、线程、解池、
预求解、随机种子、确定性置换和已脱敏的原生参数；**B1231 更正：`SCIPsolveConcurrent` 确实未移植，
但它是<b>独立的求解入口</b>（多种子并行、取第一个结果），不是“线程参数没生效”。本段此前写着多线程请求会在
来源中记为忽略参数——那是错的，`parallel/maxnthreads` 已通过 `SetIntParameter` 真正下发，失败还会返回
`ParameterFailure`，把它记成忽略参数等于声称一个已应用的参数没有应用。该声明已删，能力差异改用散文披露，
不再塞进参数表。**契约层不再拒绝状态回调，而是在触碰原生库前拒绝非法运行参数、
错误后端类型、deterministic 多线程与越界种子。其他插件统一使用新配置，Hexaly 移除把转储标志哈希
当作时间秒数的占位，真实时限留待其后端接入批次。

新增 22 项 Core 契约、1 项 SCIP 契约和 4 项 SCIP 原生用例，实施前相应断言均按旧行为失败。
Windows SCIP 9.2.4 opt-in 通过 101 项（51 项需原生库）、默认项目 50 项，缺库 opt-in 按预期失败
51 项并通过 46 项契约，全部返回 `OREngineEnvironmentLost`，不计为验收；Gurobi 10.0.1 opt-in 通过
812 项。Windows 有／无商业 SDK 全量 4738／4686 项、Linux 离线 4686 项，各 1 项既有跳过，强制构建
0 错误、既有警告 743／0／718（无 SDK 副本因未还原分析器包而计数为 0）。Gurobi 运行时版本探测、
SCIP 并发求解、跨语言时长文本指纹、Hexaly 时限与其他真实后端、Linux 原生 SCIP 仍未验收；SCIP
对偶、Farkas、IIS、二次与 CP、原生函数 writer、远程互通与性能收口继续属后续批次，P3 与全量同步
仍在进行。

### Previous gate: SCIP LP duals and Farkas certificates

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core/model/intermediate/LinearTriadModel.kt` (`dual()`, `farkasDual()`, `tidyDualSolution()`) | `LinearTriadModel.Dual`, `LinearTriadModel.FarkasDual`, `LinearTriadModel.TidyDualSolution` | `LinearTriadDualTest` (13 cases) | Contract verified: row multiplier columns own the first `m` indexes, signed bounds per relation, equality rows get positive/negative slack columns, bound dual columns follow primal order, the `normalization` row forces `Σ rhs·y + Σ lb·μ_lb + Σ ub·μ_ub = -1`, and the minimum objective sums multiplier magnitudes |
| `core/model/intermediate/TriadDualSolverSupport.kt` (`solveDual`, `solveFarkasDual`) | `TriadDualSolverSupport.SolveDualAsync`, `TriadDualSolverSupport.SolveFarkasDualAsync` | `LinearTriadDualTest` | Contract verified: one shared build-then-solve flow, missing model or solver and build failures rejected, results tidied against the primal row count, no fabricated zero certificate |
| `core-plugin-scip/ScipColumnGenerationSolver.kt`, `ScipBendersDecompositionSolver.kt` dual capture | `ScipLinearNativeSolver.ExtractDuals` through `SCIPgetDualSolVal`, `ScipLpDualSupport.RowDualsAsync`, `ScipColumnGenerationSolver.SolveLPAsync` | `ScipDualIntegrationTest` (6 cases), `ScipLpDualSupportTest`, `ScipLinearContractTest` | Windows SCIP 9.2.4: per-row LP prices, the `1e-6` strong-duality gate and the explicit dual-model fallback exercised against the native library |
| `core-plugin-scip/ScipBendersDecompositionSolver.kt` infeasible branch (`solveFarkasDual(model, ScipLinearSolver(config))`) | `ScipBendersDecompositionSolver` infeasible branch, `ScipLpDualSupport.ToFarkasSolution` | `ScipDualIntegrationTest` | Windows SCIP 9.2.4: feasibility-cut evidence comes from the explicit Farkas model, mapped to mechanism constraints and validated by the solver-token snapshot |

The dual route matches the pinned Kotlin baseline: `scipModel.getDual(constraint)` is
`SCIPgetDualSolVal(scip, cons, &dualval, &bfeas)`, and that is the primitive the C# adapter binds for row
prices, with the `SCIP_Bool` out-parameter marshalled as `out int` because `SCIP_Bool` is a 4-byte
`unsigned int`. Any row whose return is not `SCIP_OKAY` (which is `+1`; SCIP's `SCIP_RETCODE` enum does not
use 0 for success) is reported as `SCIPgetDualSolVal 第 {row} 行返回 {code}` instead of being silently
zeroed.

This section used to claim that `SCIPgetTransformedCons` returns NULL for every original constraint. That
measurement only covered models that stay feasible. Re-measured in the follow-up diagnostic batch on
instances the presolver falsifies before any LP exists, the call still returns `SCIP_OKAY` but the
out-pointer is NULL exactly when there is no LP row (`nLPs = 0`, infeasibility proven during
transformation or propagation), while every model the LP itself falsifies yields a non-NULL transformed
constraint carrying a real `SCIPgetDualfarkasLinear` multiplier. The transformed route is therefore usable:
`SCIPgetTransformedCons` and `SCIPgetDualfarkasLinear` are bound again and drive the Farkas certificate
path, while `SCIPgetDualsolLinear` stays unused because row prices are read through `SCIPgetDualSolVal`
exactly as Kotlin does.

LP mode keeps the pinned Kotlin configuration: presolving off, heuristics off and
`parallel/maxnthreads = 1`, so each original constraint survives transformation and still carries its own
row value. The C# side reads the values after `SCIPsolve` returns while Kotlin reads them inside the
`analyzingSolution` callback; that is a call-site difference, not a semantic one, and it is only sound
because the LP configuration above is forced rather than requested.

`FarkasDual()` is the pinned `farkasDual()`: one signed column per primal row (`<=` non-negative,
`>=` non-positive, `=` free), two `[0, inf)` slack columns that absolute the multiplier of each equality
row, two bound-dual columns per bounded primal column (free columns contribute none, and unlike
`Dual()` the skip test is `free` rather than `normalized`, exactly as Kotlin does it), one `Equal` balance
row per original column (`Aᵀ` plus coefficient `1` for that column's bound duals, right-hand side `0`,
named `<var>_farkas_dual`), the `normalization` row, and one `<cons>_abs` row per equality row. This
batch fixed two layout defects found while porting: the slack columns used to be created inside the row
loop, so an equality row before the last one shifted every later column index and `TidyDualSolution`,
which reads the first `m` values positionally, returned a certificate whose multipliers were attached to
the wrong constraints; and the free multiplier column of an equality row used to receive an objective
cell, which is not Kotlin's `pos + neg + slacks` magnitude form. Row columns now occupy indexes
`0..m-1`, slacks start at `m`, and bound duals follow, matching the Kotlin index contract.
`FarkasDual_ShouldKeepRowColumnsAlignedWhenEqualityRowComesFirst` and
`BendersSubproblem_ShouldKeepFarkasCertificateAlignedWithLeadingEqualityRow` both fail against the
previous behaviour.

Known divergences from the pinned Kotlin baseline, all deliberate and disclosed:

- **B1222/B1223 scope note:** **`ModelViewVariable` now carries `DualOrigin`
  (`ConstraintSlackOrigin(RowIndex, Relation)`) and `LinearTriadModel.BuildDual()` fills it on every row-dual column;
  and `LinearTriadModel.BuildFarkasDual()` now stamps derived identities on its three generated roles
  (`farkas-constraint`, `farkas-slack`, `farkas-bound`) via the pre-existing `DerivedModelElementIdentity.Create`.**
  **What remains open in this bullet is narrower than it reads: `tidyDualSolution` in C# still returns a row-aligned
  vector keyed by row index and a synthetic `__fallback_{index}`, rather than keying a map by the derived identity —
  so the <b>identity now exists</b> while the <b>keying does not use it yet</b>.** **B1222 补注：`ModelViewVariable` 已有
  `DualOrigin` 字段并在真实对偶构造里被填充，但那只是<b>字段</b>层面的缺口；本条目说的是对偶与 Farkas
  <b>解元素</b>的<b>身份</b>，是另一层，仍未闭合。**
- Dual and Farkas elements are anonymous model-local identities. Kotlin stamps every generated column
  with `dualOrigin` plus a derived stable identity (roles `farkas-constraint`, `farkas-slack`,
  `farkas-bound`, `farkas-balance`, `farkas-normalization`, `farkas-equality`, `farkas-objective`) and
  its `tidyDualSolution` keys the map by `dualOrigin`. The C# version returns a row-aligned vector and
  the caller maps rows back through the mechanism constraint `Origin` by row index; a row with no
  `Origin` gets a synthetic `__fallback_{index}` key instead of being dropped, so the row count and order
  survive but that key is not a model identity.
- Certificates report row multipliers only. Bound multipliers (`*_lb_dual`, `*_ub_dual`) and the
  absolute-value slacks are discarded, which matches the Kotlin map (those columns have no `dualOrigin`)
  but is not a full Farkas proof over bounds: when a contradiction is carried by column bounds, only a
  single row multiplier shows up as non-zero.
- Kotlin keeps zero multipliers in the map. `ToFarkasSolution` drops zeros and fails with
  `OREngineSolvingException` when every multiplier is zero, because an all-zero vector is not evidence.
- Strong duality is checked as `|Σ rhs_i·y_i - objective| <= 1e-6` against the incumbent objective
  including its offset, so any non-zero objective constant or active bound can push the call into the
  explicit dual-model branch, which re-solves with `ScipLinearSolver`.
- The column-generation LP entry returns `ErrorCode.Other` when SCIP produced no incumbent (message
  shaped like the Kotlin "SCIP LP produced no incumbent… problemStatus=…" text). The problem conclusion
  lives in the unified `SolveReport`; callers must not read infeasibility from that error code.

Thirteen Core dual cases now cover the dual and Farkas shapes, the signed column domains, the
normalization row cells, disposed-model and non-finite right-hand-side rejection, hand-computed
certificates for a free-column contradiction and for a leading equality row, and build/solver failure
propagation. Six SCIP integration cases cover an unbounded-relaxation row price, a bound-induced
strong-duality fallback, a fixed-column Benders sub-problem with two row prices, two Farkas certificates
checked against hand arithmetic, and the missing-incumbent LP entry that must not fabricate duals.
Windows with the configured SDKs rebuilds with 0 errors and the existing 743 distinct warnings, runs
4758 default tests (4757 passed, 1 pre-existing skip) across 23 test-bearing assemblies, passes 113
SCIP 9.2.4 opt-in cases and 812 Gurobi 10.0.1 opt-in cases. The isolated no-SDK copy restores, builds
and tests with 0 errors and 718 distinct warnings at 4706 tests (1 skip, 22 assemblies), and the same
totals hold for the offline Linux x64 run (`--network none`, 0 errors, 718 warnings, 4706 tests, 1 skip,
22 assemblies). With the native library absent, the SCIP opt-in project fails 57 cases and passes 56,
and every failure reports `OREngineEnvironmentLost`, so a missing environment is not counted as a pass.
The Linux image used for that gate is now a Debian 13 base with `libicu` installed so the runtime is not
in invariant-globalization mode.

Not verified or still open: SCIP IIS, quadratic and CP surfaces, the native function writers (model
`ExportLP` remains a stub, so the Kotlin `$name.lp` dump path publishes nothing), `feasibility()` and
`elastic()` on the SCIP entries, the Gurobi 11, COPT, CPLEX, MindOpt, MOSEK, Hexaly, LINGO and OPTVerse
backends, Linux native SCIP (the pinned 9.2.4 Debian package download is still 27193312 of 40335444
bytes), cross-language wire fixtures, and performance measurements. The shared Benders and logic-based
Benders engines, dynamic projections, and P4-P9 stay in later batches.

中文说明（本批原文已由后续诊断批更正）：本批把 SCIP 的逐行对偶统一到 Kotlin 基线使用的
`SCIPgetDualSolVal(原始约束)`；`SCIP_Bool` 是 4 字节 `unsigned int`，故 out 参数用 `out int`。原文所记
“实测本实例的 `SCIPgetTransformedCons` 恒返回 NULL”只在保持可行的模型上成立，后续批次复核更正为：仅当预求解
在建 LP 之前就把问题判为不可行（`nLPs = 0`）时出参才为 NULL，由 LP 证伪的模型都能取到非 NULL 的转换约束与
真实的 `SCIPgetDualfarkasLinear` 乘子，因此 transformed 路线可用，Farkas 证书已改走该路线，
`SCIPgetDualsolLinear` 仍不使用。LP 模式沿用 Kotlin 配置（关预求解、关启发式、
`parallel/maxnthreads = 1`），因此原始约束仍带各自的行值；C# 在 `SCIPsolve` 返回后读取，Kotlin 在
`analyzingSolution` 回调内读取，属调用点差异而非语义差异。

`FarkasDual()` 按基线 `farkasDual()` 逐条对齐：每行一个带符号乘子列，等式行补正负松弛列，有界列补
上下界对偶列（跳过判据是 free，而非 `Dual()` 的 normalized），每个原列一条 `Aᵀ` 加界系数的平衡行，
外加归一化行与等式行的绝对值行。本批修掉两处移植缺陷：松弛列原先在行循环内创建，导致非末位的等式行
会把后续列索引整体后移，而 `TidyDualSolution` 按前 m 个位置读取，于是证书乘子被挂到错误的约束上；
等式行自身的自由乘子列原先带目标系数，而 Kotlin 只以 `pos + neg + slacks` 计价幅度。现在行列占
`0..m-1`、松弛列从 m 开始、界对偶列最后，与 Kotlin 索引契约一致；两条新增的"首行即等式"用例在修复前
均失败。

差异披露：对偶与 Farkas 元素使用匿名 model-local 身份，未移植 Kotlin 的 `dualOrigin` 与派生稳定身份，
行到 `MathConstraint` 靠"行索引 + 机制 Origin"，缺 Origin 的行使用 `__fallback_{i}` 占位键；证书只回报
行乘子，丢弃界乘子与绝对值松弛，故矛盾由列界承担时仅出现单行非零乘子；Kotlin 保留零乘子，本实现过滤
零值并在全零时报 `OREngineSolvingException`；强对偶校验为 `|Σ rhs·y − 含偏移目标值| ≤ 1e-6`，任何非零
目标常数或生效界都会走到显式对偶模型回退；列生成的 LP 入口在无 incumbent 时返回 `ErrorCode.Other`，
问题结论只在统一报告中。

测试与门禁：Core 对偶 13 例、SCIP 互通 6 例（含两项手工核算的 Farkas 证书）。Windows 有 SDK 强制重建
0 错误、既有 743 条去重警告，默认全量 4758 项（4757 通过、1 项既有跳过、23 个测试程序集），SCIP 9.2.4
opt-in 113 项、Gurobi 10.0.1 opt-in 812 项全通过。无 SDK 隔离副本 restore/build/test 均 0 错误、718 条
去重警告、4706 项（1 跳过、22 程序集）；Linux x64 离线复测同样 0 错误、718 警告、4706 项，本次改用安装
`libicu` 的 Debian 13 基础镜像，避免不变文化模式失真。缺原生库时 SCIP opt-in 失败 57 项、通过 56 项，
失败全部为 `OREngineEnvironmentLost`，不计为验收。SCIP 的 IIS、二次与 CP、原生函数 writer（`ExportLP`
仍是占位）、`feasibility()`/`elastic()`、其余后端、Linux 原生 SCIP、跨语言 wire fixture 与性能测量继续
属后续批次，P3 与全量同步仍在进行。
### Previous gate: SCIP infeasibility diagnostics and the analyzer SPI

This section records the diagnostic SPI gate. Its note that `feasibility()` and `elastic()` are not ported is
superseded by the current gate below, which delivers both linear transforms; the IIS materialization step still does not use them.

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core/solver/iis/InfeasibilityAnalyzer.kt` (analyzer SPI, `InfeasibilityAnalyzerCapabilities`, `CapabilityAwareInfeasibilityAnalyzer`, `NativeIISAnalyzer`, `FarkasInfeasibilityAnalyzer`, `DelegatingInfeasibilityAnalyzer`, both adapters, `UnavailableInfeasibilityAnalyzer`, `InfeasibilityDiagnosticStrategy`, `InfeasibilityAnalyzerChain`, `InfeasibilityDiagnosticOrchestrator`) | `src/Core/Solver/Iis/InfeasibilityAnalyzer.cs` | `InfeasibilityAnalyzerTest` (15 cases) | Contract verified: strategy order NativeIis → Farkas → ConstraintConflict → ElasticFilter → DeletionFilter with stable ordering, capability filtering (native-IIS flag, Farkas flag, CP `ConflictCore` support level, model-type intersection where either side empty means unrestricted), chain returns the first Ok and keeps the last Failed, Fatal short-circuits, an empty chain fails with `Other`, and `AnalyzeIntoAsync` attaches evidence or maps failures into `Diagnostics.Errors` without touching the problem conclusion |
| `core/solver/iis/IISConfig.kt`, `iis/IISComputingStatus.kt` | `src/Core/Solver/Iis/IisConfig.cs`, `src/Core/Solver/Iis/IisComputingStatus.cs` | `IisConfigTests` (8 cases) | Contract verified: `Time` 30s, CPU-heuristic `ThreadCount` (≤16 cores → cores, <24 → 16, <32 → 24, else 32), nullable `NotImprovementTime` / `InterruptibleTime`, `SlackTolerance` = `Flt64(1e-6)`, `BackendConfiguration`, and the progress record's four remaining/total amounts plus the three IEEE ratios |
| `core/solver/report/ModelElementIds.kt` | `src/Core/Solver/Report/ModelElementIds.cs` | `ModelElementIdTest` (6 cases), `ScipFarkasDiagnosticTest` | Contract verified: variable id = explicit id → `<origin.identifier>:<origin.index>` → `model-local-variable:<index>`; row id = explicit id → `origin-constraint:<key>:<index>` → `model-local-constraint:<name>:<index>` → `model-local-constraint:<index>`; synthesized keys stay pipeline-local and are never promoted to a cross-rebuild identity |
| `core/solver/LinearSolver.kt` / `QuadraticSolver.kt` (`diagnosticAnalyzers`) | `IAbstractLinearSolver.DiagnosticAnalyzers`, `IAbstractQuadraticSolver.DiagnosticAnalyzers` | default interface members | Contract verified: default empty analyzer list, linear scoped to `ILinearTriadModelView`, quadratic to `IQuadraticTetradModelView` |
| `core-plugin-scip/ScipInfeasibilityAnalyzers.kt` (`ScipFarkasInfeasibilityAnalyzer`, `selectedFarkasBounds`) | `src/Core.Plugin.Scip/ScipFarkasInfeasibilityAnalyzer.cs`, `ScipLinearSolver.RunFarkasDiagnosticAsync`, `ScipLinearNativeSolver.ExtractFarkas` | `ScipFarkasDiagnosticTest` (7 opt-in cases) | Windows SCIP 9.2.4: two-row certificate (2 rows, no bounds), packing certificate aggregating 2 rows plus 3 variable bounds into 5 members, transformation-stage contradiction reported as `OREngineSolvingException` instead of invented members, integer columns rejected with `IllegalArgument`, feasible model rejected with `ORModelInfeasible`, diagnostic LP forced off-presolve single-thread with `IisConfig.Time` as its limit |
| `SolverExt.kt` (`solveWithOptionsAndIIS`), `iis/Linear.kt` (`computeIIS`) | `ScipLinearSolver.InvokeAsync(model, solvingStatusCallBack, iisConfig, ct)` | `ScipFarkasDiagnosticTest.LegacyIisEntryCarriesEvidenceInsteadOfRejection`, `ScipLinearContractTest.UnsupportedFeaturesAreNotSilentlyIgnored` | Windows SCIP 9.2.4: the legacy IIS entry no longer rejects the feature. It solves first, keeps the normal output for any non-infeasible model, and on infeasibility runs the orchestrator, returning `LinearInfeasibleSolverOutput` with the original-model snapshot, `IisAvailable = false` and the Farkas evidence in `Diagnostics` |

Three native facts were re-checked against the installed headers because earlier notes in this document had
them wrong. `SCIP_RETCODE` uses `SCIP_OKAY = +1` (`scip/type_retcode.h`), so the adapter's success constant of
`1` is right and a `0` return is a real error. `SCIP_VARTYPE` in 9.2 is BINARY = 0, INTEGER = 1, IMPLINT = 2,
CONTINUOUS = 3 (`scip/type_var.h`) — the earlier notes used the SCIP 8 ordering — so the snapshot mapping and
the `column.Type != 3` "not continuous" test in the diagnostic gate are correct. `SCIP_PARAMSETTING_OFF = 3`
(`scip/type_paramset.h`) with `DEFAULT = 0`. `SCIPhideOutput` does not exist in the 9.2 C headers (it is a
JNI-wrapper convenience), so no binding was added: `SCIPsetMessagehdlrQuiet` already covers it.

`IisConfig` is a deliberate breaking upgrade to Kotlin's `IISConfig`. The previous `Enabled`,
`MaxIterations` and `TimeoutSeconds` fields had no readers anywhere in the repository, so the only call site
that had to change was the configuration test. Migration: `Enabled` has no counterpart (diagnostics run
whenever an `IisConfig` is supplied), `TimeoutSeconds` becomes `Time` (a `TimeSpan`, default 30s),
`MaxIterations` has no counterpart because the pinned baseline limits by time and by no-improvement time,
and new callers that want the old "disabled" behaviour simply must not pass an `IisConfig`.
`IisComputingStatusCallBack` also changed shape before any backend invokes it: the first parameter is now
`isFeasible` (Kotlin's `invoke(isFeasible, time, status)`) instead of an iteration counter, and
`IisComputingStatus` now carries the four remaining/total amounts with the three progress ratios.

Known divergences from the pinned Kotlin baseline, all deliberate and disclosed:

- The legacy elastic IIS engine (`feasibility()` / `elastic()` and the reduced `LinearIISModel`) is not
  ported yet, so `computeIIS`'s materialization step has nothing to materialize. Kotlin's `computeIIS`
  appends `LegacyElasticInfeasibilityAnalyzer` after the backend analyzers and falls back to it; the C#
  legacy entry returns the original-model snapshot with `IisAvailable = false` and the structured Farkas
  evidence in `Diagnostics` instead. For a Farkas-sourced evidence the Kotlin
  `materializeNativeIIS` helper also returns null (it only fires for `NativeIIS`), so only the elastic
  fallback differs, not the native path.
- `Iis` on `LinearInfeasibleSolverOutput` / `QuadraticInfeasibleSolverOutput` is nullable in C# while
  Kotlin's `iis` is a required non-null view. Five existing repository fixtures construct a status-only
  infeasible output for the legacy status mapping and have no view to hand; making the parameter required
  would force those fixtures to invent a model. `IisAvailable` defaults to `false`, matching Kotlin's
  documented meaning: when false, `iis` is only an original-model snapshot.
- `Diagnostics` is `SolveDiagnostics<Flt64>?` in C# versus Kotlin's defaulted non-null `SolveDiagnostics()`.
  The report conversion merges a carried evidence with the synthesized `Unavailable` one, so a null value
  means "nothing was attached" rather than "nothing was wrong".
- The SCIP diagnostic run applies `IisConfig.Time` only. `ThreadCount` is pinned to 1 because LP mode must
  keep transformed rows aligned with the original constraint order, `BackendConfiguration` is inherited from
  the solver's own configuration so a foreign backend cannot leak into the diagnostic instance, and
  `ComputingStatusCallBack` has no loop to drive it until the elastic engine lands.
- Evidence collections are `ISet<T>` in C# (unordered) where Kotlin builds `LinkedHashSet`s. Tests compare
  them order-insensitively; cross-language wire fixtures must normalize evidence ordering in P4.
- `SolverCapabilities` for the SCIP linear backend now claims `Dual` and `Farkas`, keeps `NativeIis = false`
  (SCIP has no native IIS primitive) and `ModelTypes = {LP, MIP}`. The contract test previously asserted
  `Dual == false`, which contradicted the row-price extraction shipped by the prior batch; that assertion is
  corrected here rather than the capability being watered down.

Gate measurements for this batch: Windows Release forced rebuild 0 errors with the existing 743 distinct
warnings; default suite 4779 tests (4778 passed, 1 pre-existing skip) across 23 test-bearing assemblies;
SCIP 9.2.4 opt-in 120/120; Gurobi 10.0.1 opt-in 812/812. The isolated no-SDK copy restores, rebuilds and
tests with 0 errors, 718 distinct warnings and 4727 tests (1 skip, 22 assemblies), and the offline Linux x64
run (`--network none`, local NuGet cache only) reports the same 718 warnings, 4727 tests and
`LINUX_GATE_OK`. With the native library absent the SCIP opt-in project runs 120 cases, 62 fail and 58
pass, and every one of the 62 failures reports `OREngineEnvironmentLost` — either as the returned error or as
the actual value of an expected-code assertion — so a missing environment is never counted as a pass.

Not verified or still open: `LinearIisCompute.ComputeIIS` materialization (needs `feasibility()` /
`elastic()` plus a legacy elastic analyzer), quadratic IIS, `ExportLP` and the native function writers, the
SCIP quadratic and CP surfaces, the Gurobi 11, COPT, CPLEX, MindOpt, MOSEK, Hexaly, LINGO and OPTVerse
backends, Linux native SCIP, cross-language wire fixtures and performance measurements. P3 and the full
synchronization are still in progress.

中文说明：本批把 Kotlin 的不可行诊断 SPI 整体落到 Core：`IInfeasibilityAnalyzer<M>`、
`MaterializedInfeasibilityEvidence<A>`、`IMaterializingInfeasibilityAnalyzer<M,A>`、
`InfeasibilityAnalyzerCapabilities`、`ICapabilityAwareInfeasibilityAnalyzer<M>`、原生 IIS 与 Farkas 两个标记接口、
`DelegatingInfeasibilityAnalyzer`、两个适配器、`UnavailableInfeasibilityAnalyzer`、
`InfeasibilityDiagnosticStrategy`、`InfeasibilityAnalyzerChain` 与 `InfeasibilityDiagnosticOrchestrator`，优先序为
NativeIis → Farkas → ConstraintConflict → ElasticFilter → DeletionFilter，能力过滤按原生 IIS 旗标、Farkas 旗标、
CP `ConflictCore` 支持级别与模型类型交集（任一侧为空即放行），链式执行取首个 Ok、保留末个 Failed、
Fatal 立即短路，空链返回 `Other`；`AnalyzeIntoAsync` 只把证据或错误写进 `Diagnostics`，不改问题结论。
线性与二次求解接口新增 `DiagnosticAnalyzers(IisConfig)` 默认空列表。

原生层新增 `SCIPgetTransformedCons` 与 `SCIPgetDualfarkasLinear` 绑定：转换约束为空指针（预求解在建 LP 前
判明不可行）时按零乘子上报，由 Farkas 分析器如实失败；LP 证伪的模型给出真实逐行乘子。SCIP 9.2.4 实测
7 项：两行矛盾证书（2 行、无变量界）、打包矛盾的“行 + 界”聚合证书（2 行 + 3 界共 5 个成员）、转换阶段矛盾
不伪造成员、整数列拒绝、可行模型拒绝、旧 IIS 入口携带证据且 `ToSolveReport()` 保留 Complete 证据。

三处原生事实已按安装头文件更正记录：`SCIP_OKAY = +1`（`SCIP_RETCODE` 不以 0 表示成功，故代码里的常量 1 正确）、
SCIP 9.2 的 `SCIP_VARTYPE` 顺序是 BINARY=0／INTEGER=1／IMPLINT=2／CONTINUOUS=3（旧笔记用的是 SCIP 8 顺序），
`SCIP_PARAMSETTING_OFF = 3`；9.2 的 C 头文件没有 `SCIPhideOutput`（属 JNI 包装），因此不新增绑定，
`SCIPsetMessagehdlrQuiet` 已等价。

`IisConfig` 是破坏式对齐：旧 `Enabled`／`MaxIterations`／`TimeoutSeconds` 在全仓无读取点，唯一受影响调用点是配置
自身的测试；迁移口径为“传入 `IisConfig` 即启用诊断、不传即不启用”，时限改用 `Time`。`IisComputingStatusCallBack`
首个参数改为 Kotlin 的 `isFeasible`，`IisComputingStatus` 改为四项剩余／总量与三个进度比值，均在后端真正回调前完成。

**B1219 更正：旧 elastic IIS 引擎已移植并接线**——`LegacyElasticInfeasibilityAnalyzer`（152 行）、
`LinearIisModel`（320 行）、`LinearLegacyIisCompute.ComputeLegacyIISAsync` 与
`QuadraticLegacyIisCompute`（612 行）都在，`NotImplementedException`／`TODO`／
`not ported` 标记计数为 **0**；SCIP（`ScipLinearSolver.cs:321`）与 COPT（`CoptLinearSolver.cs:199`）
在真实路径上置 `IisAvailable: true`。
**本段此前写着"尚未移植，故 `IisAvailable = false`"——那是一句过期结论，与源码不符，本批已更正。**
仍然成立的部分：**Gurobi 侧 `IisAvailable` 仍为 `false`**（其证据由 `Diagnostics` 经 `NativeIis` 来源承载），
**这是逐后端的能力差异，不是整体缺口**；Kotlin 的 `materializeNativeIIS` 仅在 `NativeIIS` 来源时物化，
Farkas 来源本来也不会走它。C# 的 `Iis` 与 `Diagnostics`
可空，是为了保留 5 处仅表达状态的旧输出构造点。SCIP 诊断运行只下发 `Time`，线程固定为 1 以保证行序对齐，
后端配置沿用求解器自身配置，进度回调在 elastic 落地前无循环可驱动。证据集合在 C# 是无序 `ISet<T>`，
Kotlin 用 `LinkedHashSet`，故测试按无序比较，跨语言 wire fixture 需在 P4 统一序化。SCIP 线性能力现声明
`Dual`／`Farkas` 为真、`NativeIis` 为假、模型类型 `{LP, MIG}`，此前契约测试断言 `Dual == false` 与上一批已交付的
行价提取相矛盾，本批更正断言而非削弱能力。

门禁实测：Windows Release 强制重建 0 错误、既有 743 条去重警告；默认全量 4779 项（4778 通过、1 项既有跳过、
23 个测试程序集）；SCIP 9.2.4 opt-in 120／120、Gurobi 10.0.1 opt-in 812／812。无 SDK 隔离副本 restore、强制重建
与测试均 0 错误、718 条去重警告、4727 项（1 跳过、22 程序集）；Linux x64 离线复测同样 718 警告、4727 项并输出
`LINUX_GATE_OK`。缺原生库时 SCIP opt-in 跑满 120 项，62 失败／58 通过，62 项失败全部为
`OREngineEnvironmentLost`（或直接返回该码，或在期望码断言里作为实际值出现），环境缺失不计为验收。
`ComputeIIS` 物化、二次 IIS、`ExportLP` 与原生函数 writer、SCIP 二次与 CP、其余后端、Linux 原生 SCIP、
跨语言 wire fixture 与性能测量继续属后续批次，P3 与全量同步仍在进行。

### Previous gate: linear feasibility and elastic derived models

Superseded by the next section: the legacy IIS pipeline now consumes `Elastic(...)`, the row-slack provenance this section
listed as missing exists as `VariableSlack.Constraint`, and the open items below are superseded by the current gate's list.

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core/model/intermediate/LinearTriadModel.kt:1870-2085` (`feasibility()`) | `src/Core/Model/Intermediate/LinearTriadModel.cs` `Feasibility()` + `BuildFeasibility()` | `tests/Core.Tests/Model/Intermediate/LinearTriadElasticFeasibilityTest.cs` (4 cases) | Contract verified: a row whose right-hand side is negative is negated cell by cell and its relation reversed; every row becomes an equality with `abs(rhs)`; a `<=` row gains one `+1` slack, a `>=` row a `-1` slack plus a `+1` artificial, an `=` row only a `+1` artificial; slacks and artificials are continuous `[0, +inf)` with initial result 0, appended after the untouched original columns in ascending index order; the objective is `Minimum` over the artificials only, so a zero optimum is exactly feasibility. The one-sided artificial of an `=` row is the pinned Kotlin semantics and is reproduced as-is rather than "improved" |
| `core/model/intermediate/LinearTriadModel.kt:2086-2094` plus `LinearTriadElasticBuilder.kt` | `LinearTriadModel.Elastic(minmaxSlack, minSlackAmount)` + `BuildElastic()` | same test class (8 cases, including a four-combination theory) | Contract verified: row slacks `<row>_lb_slack` at -1 and `<row>_ub_slack` at +1 while the relation and right-hand side stay unchanged; per-column bound slacks by domain (free, positive-normalized and negative-normalized columns add none); bound rows `x + s >= lb` (`ElasticLowerBound`) and `x - s <= ub` (`ElasticUpperBound`) with the source column bounds opened to infinity; one `Binary [0,1]` indicator per slack with `s - (threshold + precision) * bin >= 0`, `s - bin / precision <= 0` and the `sum(bin) >= count` total row `min_slack_amount`; the `minmax_slack` column with one `minmax - s >= 0` row per slack; the objective becomes `Minimum` over every slack plus the minmax column and drops all original objective cells and the constant; the column order is original, slacks, binaries, minmax |
| `LinearTriadElasticBuilder.kt` | deleted `src/Core/Model/Intermediate/LinearTriadElasticBuilder.cs` | repository-wide reference scan (no call sites) | The internal placeholder builder whose five methods returned empty models behind `// Stub:` comments is removed; `Feasibility()` and `Elastic()` are now the only feasibility/elastic construction path for the linear triad. `src/Core/Model/Intermediate/QuadraticTetradElasticBuilder.cs` stays a placeholder until the quadratic pair lands in a later batch |

Known divergences from the pinned Kotlin baseline, all deliberate and disclosed:

- Derived stable identities are not generated. Kotlin re-labels every derived column and row through
  `withDerivedIdentity` / `derivedConstraintIdentity` / `derivedDiscriminator`; the C# derived elements keep anonymous
  model-local identities, exactly as `Dual()` and `FarkasDual()` already do, and a dedicated identity batch is planned.
  Row-index alignment (derived row `i` for `i < m` corresponds to original row `i`) is the current way back to the source rows.
- Kotlin copies row `origins`, `froms` and `priorities` onto the derived rows. The C# `LinearConstraintBatch` carries
  none of those three lists, so derived rows keep only sign, right-hand side, name and source.
- `VariableSlack` in C# has only `LowerBound` and `UpperBound`, no `constraint` field, so bound slacks reference their
  source column while row slacks carry no slack metadata at all (closed by the next gate: row slacks now carry a
  `ConstraintSlackOrigin` with the source row index and original relation).
- Failure handling: Kotlin returns `copy()` when identity validation fails, C# returns an `IllegalArgument` failure, and
  a closed model returns `ApplicationStopped`, matching the existing `Dual()` / `FarkasDual()` entries. C# additionally
  rejects non-finite right-hand sides, non-finite coefficients, out-of-range column indexes and non-finite bounds that
  would be used as a row right-hand side instead of emitting a NaN model.
- `minSlackAmount` keeps Kotlin's `Pair<UInt64, Flt64>` shape as `(ulong Count, Flt64 Threshold)`, and the precision and
  reciprocal coefficients come from `NumericConstantsRegistry.For<Flt64>().DecimalPrecision`, the same source the
  variable-domain predicates use.

Gate measurements for this batch: Windows Release forced rebuild 0 errors with the existing 743 distinct warnings;
default suite 4791 tests (4790 passed, 1 pre-existing skip) across 23 test-bearing assemblies; SCIP 9.2.4 opt-in
120/120; Gurobi 10.0.1 opt-in 812/812. The isolated no-SDK copy restores, force-rebuilds and tests with 0 errors,
718 distinct warnings and 4739 tests (1 skip, 22 assemblies), and the offline Linux x64 run (`--network none`, local
NuGet cache only) reports the same 718 warnings, 4739 tests and `LINUX_GATE_OK`. With the native library absent the
SCIP opt-in still runs 120 cases with 62 failures, every one of them `OREngineEnvironmentLost`, so a missing
environment is still not counted as a pass.

Not verified or still open: the legacy elastic and deletion filters inside `LinearIisCompute.ComputeIIS` and the
materialized `LinearIISModel` (the two model transforms now exist, the IIS pipeline does not consume them yet), the
quadratic `feasibility()` / `elastic()` pair, `ExportLP` and the native function writers, the SCIP quadratic and CP
surfaces, the Gurobi 11, COPT, CPLEX, MindOpt, MOSEK, Hexaly, LINGO and OPTVerse backends, Linux native SCIP,
cross-language wire fixtures and performance measurements. P3 and the full synchronization are still in progress.

中文说明：本批把 Kotlin 的线性三元派生模型补齐到 Core：`Feasibility()` 对应 `feasibility()`，右端为负的行整体取反并
反转关系，每行改写为等式且右端取绝对值；小于等于行获得系数 +1 的松弛列，大于等于行获得 -1 松弛列与 +1 人工列，
等式行只获得 +1 人工列，目标极小化人工列之和，因此最优值 0 等价于原约束系统可行；等式行的单侧人工列是固定基线
的 Kotlin 语义，本批原样保留而不擅自“改进”。`Elastic(minmaxSlack, minSlackAmount)` 对应 `elastic()`：按关系为每行
追加 `_lb_slack`（-1）与 `_ub_slack`（+1）且保持原关系与右端，非自由且未归一化的变量获得
`x + s >= lb`／`x - s <= ub` 界松弛行并把自身边界放开到无穷，每个松弛配一个 `[0,1]` 二值指示列与
`s - (threshold + precision) * bin >= 0`、`s - bin / precision <= 0` 两行及 `sum(bin) >= count` 总计行，启用最小最大
松弛时追加 `minmax_slack` 列与每个松弛一行的 `minmax - s >= 0`；目标改为全部松弛（含 minmax）之和的极小化，原目标
单元格与常数项全部丢弃，列序为原列、松弛列、二值列、minmax 列。旧的占位构建器
`src/Core/Model/Intermediate/LinearTriadElasticBuilder.cs`（5 个 `// Stub:` 假方法、全仓无调用点）已删除，二次侧的
`QuadraticTetradElasticBuilder.cs` 仍属后续批次。

差异披露：派生行与派生列仍使用匿名 model-local 身份，Kotlin 的 `withDerivedIdentity`／`derivedConstraintIdentity`／
`derivedDiscriminator` 需要专门的稳定身份批次才能在 C# 落地，目前用“派生行 i（i 小于原行数）对应原行 i”的索引对齐回溯；
Kotlin 会复制行 `origins`／`froms`／`priorities`，C# 的 `LinearConstraintBatch` 不承载这三份元数据，故派生行只有关系、
右端、名称与来源；C# 的 `VariableSlack` 只有上下界变量、没有 `constraint` 字段，所以界松弛指回原变量而行松弛不带松弛
元数据。Kotlin 在身份校验失败时返回 `copy()`，C# 统一按 `Dual()`／`FarkasDual()` 的口径返回 `IllegalArgument`，模型已关闭
返回 `ApplicationStopped`，并额外拒绝非有限右端值、非有限系数、越界列索引与用作行右端的非有限变量界。
`minSlackAmount` 保持 Kotlin 的 `Pair<UInt64, Flt64>` 形态即 `(ulong Count, Flt64 Threshold)`，精度与倒数系数取自
`NumericConstantsRegistry.For<Flt64>().DecimalPrecision`，与变量域判定同源。

门禁实测：Windows Release 强制重建 0 错误、既有 743 条去重警告；默认全量 4791 项（4790 通过、1 项既有跳过、23 个测试
程序集）；SCIP 9.2.4 opt-in 120／120、Gurobi 10.0.1 opt-in 812／812。无 SDK 隔离副本 restore、强制重建与测试均 0 错误、
718 条去重警告、4739 项（1 跳过、22 程序集）；Linux x64 离线复测同样 718 警告、4739 项并输出 `LINUX_GATE_OK`。
缺原生库时 SCIP opt-in 仍为 120 项、62 失败，62 项失败全部是 `OREngineEnvironmentLost`，环境缺失不计为验收。
`ComputeIIS` 的 elastic／deletion 过滤与 `LinearIISModel` 物化、二次 `feasibility()`／`elastic()`、`ExportLP` 与原生函数
writer、SCIP 二次与 CP、其余后端、Linux 原生 SCIP、跨语言 wire fixture 与性能测量继续属后续批次，P3 与全量同步仍在进行。

### Previous gate: legacy IIS filtering and materialization

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core/solver/iis/Linear.kt` (`LinearIISModel`, `computeIIS`, `materializeNativeIIS`, `computeLegacyIIS`, `performElasticFiltering`, `relaxSpecificComponents`, `performDeletionFiltering`, `dump`) | `src/Core/Solver/Iis/LinearIisModel.cs` (`LinearIisModel`, `LinearIisResult`, `LinearIisCompute.ComputeIISAsync`, `MaterializeNativeIis`, `ProjectRows`, `ProjectColumn`) and `src/Core/Solver/Iis/LinearLegacyIisCompute.cs` (`ComputeLegacyIISAsync`, `PerformElasticFilteringAsync`, `RelaxSpecificComponentsAsync`, `PerformDeletionFilteringAsync`, `SolveFeasibilityAsync`, `Dump`) | `tests/Core.Tests/Solver/Iis/LinearIisComputeTest.cs` (11 cases) | Contract verified: elastic filtering runs three rounds that only release variable-bound slacks, then inequality and bound slacks, then every slack; each round writes `+inf` to the released slacks and `0` to the rest, re-solves the elastic model and requires an `Feasible` status and a solution vector; a slack only counts as released when its value reaches `IisConfig.SlackTolerance` (default `1e-6`). `ORModelInfeasible` / `ORModelInfeasibleOrUnbounded` failures and `Infeasible` / `InfeasibleOrUnbounded` statuses are "still infeasible", any other failure propagates, any other terminal status becomes `OREngineSolvingException` and a feasible report without a solution becomes `ORSolutionInvalid`. When all three rounds stay infeasible the deletion filter walks the slack columns in ascending index order, zeroes one upper bound at a time, drops the slack when the model turns feasible and restores `+inf` otherwise, notifying after every probe |
| same file (`dump`, `getRelatedConstraints`, `getRelatedVariables`) | `LinearLegacyIisCompute.Dump` + `LinearIisCompute.ProjectRows` / `ProjectColumn` | same test class (bound-only subsystem, equality rows excluded from round two, deletion reduction to a single guard batch, all-infeasible retention, native materialization by stable and positional ids) | Contract verified: the subsystem keeps only the origin rows behind the released row slacks, the origin columns those rows touch plus the columns of released bound slacks, one bound side per released bound slack (the other side opens to `-inf` / `+inf`), the origin sign / right-hand side / name / source, and it is renamed `<origin>_iis`. A `null` guard batch means the subsystem is infeasible on its own (`RelaxedFeasible`); the legacy deletion filter always returns an empty guard batch, so a result that only stays infeasible while rows are retained reports `RelaxedFeasible = false` with zero guard rows |
| same file (`materializeNativeIIS`) | `LinearIisCompute.MaterializeNativeIis` | same test class (rows matched by `DiagnosticConstraintId` and by `constraint-<index>`, single bound side kept, non-native and empty evidence rejected) | Contract verified: only `NativeIis` evidence materializes, rows are matched by diagnostic id or positional `constraint-<index>`, columns are the nonzero columns of those rows plus the evidenced bound and domain columns, only the evidenced bound side survives, and evidence that selects neither rows nor columns returns `null` so the next strategy continues |
| `core/solver/iis/InfeasibilityAnalyzer.kt` (`LegacyElasticInfeasibilityAnalyzer`) | `src/Core/Solver/Iis/LegacyElasticInfeasibilityAnalyzer.cs` | same test class (evidence grade and members) and `tests/Core.Tests/Solver/Iis/InfeasibilityAnalyzerTest.cs` | Contract verified: the analyzer declares `{LP, MIP}` with `Exact = false`, grades a relaxed-feasible subsystem as `ElasticFilter` / `Complete` and a guarded one as `DeletionFilter` / `Partial`, always reports `Heuristic` exactness and validity, `NotChecked` minimality and the `legacy-iis-heuristic` unavailable reason, and lists the subsystem rows, the subsystem columns' surviving bounds and integer domains as members |
| `core/solver/SolverExt.kt:135-223` (`invoke` with an IIS configuration) | `src/Core.Plugin.Scip/ScipLinearSolver.cs` (`Descriptor`, `InvokeAsync(LinearTriadModel, ..., IisConfig?)`) | `tests/Core.Plugin.Scip.Tests/ScipLegacyIisDiagnosticTest.cs` (2 cases) and `ScipFarkasDiagnosticTest.LegacyIisEntryCarriesEvidenceInsteadOfRejection` | Contract verified against SCIP 9.2.4: the descriptor declares `scip` / `SCIP` with `LinearCapabilities` (no native IIS), and an infeasible model solved with an `IisConfig` now returns `LinearInfeasibleSolverOutput` with `IisAvailable = true` and a `LinearIisModel` instead of the original-model snapshot. A two-row contradiction plus one never-violated row reduces to the violated rows only, keeps both free origin columns and reports heuristic elastic-filter evidence; a failed diagnosis still keeps the snapshot, sets `IisAvailable = false` and appends the errors to the existing diagnostics |
| Kotlin `VariableSlack` / `ConstraintInfo` provenance | `src/Core/Model/Basic/ModelView.cs` (`VariableSlack.Constraint`, `ConstraintSlackOrigin`), `src/Core/Model/Intermediate/LinearTriadModel.cs` (`RowSlack`) | `tests/Core.Tests/Model/Intermediate/LinearTriadElasticFeasibilityTest.cs` (12 cases) | Contract verified: every row slack of `Feasibility()` and `Elastic()` records the origin row index and the origin relation of its source row, so a negated row still reports its original relation and both slacks of one equality row compare equal; bound slacks keep pointing at the origin column and carry no source row |

The legacy engine rejects a model that is not a `LinearTriadModel` with `IllegalArgument` and validates the
`IisConfig` first, so a negative time limit, an out-of-range thread count or a negative / non-finite slack tolerance fails
before any solve. The default entry (`LinearIisCompute.ComputeIISAsync`) appends the legacy analyzer after the backend
analyzers, materializes a native-IIS artifact or native-IIS evidence when it has one, and only then lets the legacy engine
build the subsystem itself; a successful orchestration without a model artifact and without native IIS evidence therefore
still returns a reduced model instead of failing.

Known divergences from the pinned Kotlin baseline, all deliberate and disclosed:

- Traceability uses `OriginRows` / `OriginColumns` on the materialized subsystem instead of Kotlin's row `origins` plus
  structural row matching (`sameLinearConstraint`), so no display-name or coefficient equality is used to pick rows.
- Evidence identifiers are the pipeline diagnostic ids (`DiagnosticConstraintId` / `DiagnosticVariableId`), not the
  legacy `legacyConstraintIds` / `legacyVariableIds` construction, so the evidence joins the rest of the C# report path.
- Subsystem columns are renumbered to contiguous positions and every row coefficient is remapped with them, while Kotlin
  keeps the origin column indexes in the projected model. A backend can therefore rebuild the C# subsystem directly.
- Deletion filtering tracks index sets instead of mutating the candidate objects, so "restore the released slack" is one
  bound write; the probe order (ascending elastic column index) and the notification points are unchanged.
- The success branch still carries structured evidence: `Diagnostics.InfeasibilityEvidence` is the evidence that grades
  the returned subsystem, while Kotlin's `invoke` only returns the model. When a backend analyzer produced exact evidence
  but no artifact (SCIP's Farkas certificate), the legacy subsystem becomes the returned model and the reported evidence
  is the legacy grade; the exact certificate is not carried alongside it in this batch.
- The legacy loop reports elapsed time through `IisComputingStatus` but does not keep its own `IisConfig.Time` budget: it
  passes the cancellation token to every solve and relies on the backend's own limit. The `isFeasible` flag of the
  progress callback is always true in C#, so a consumer must read `LinearIisModel.RelaxedFeasible` or the evidence to
  learn whether guards are required.
- `LinearIisModel.ExportLP` is still the inherited stub, and `QuadraticIisCompute` plus
  `src/Core/Model/Intermediate/QuadraticTetradElasticBuilder.cs` remain placeholders for the quadratic and writer batches.

Gate measurements for this batch: Windows Release forced rebuild 0 errors with the same 743 distinct warnings as the
previous batch, and the SCIP opt-in build adds no warning either; default suite 4802 tests (4801 passed, 1 pre-existing skip)
across 23 test-bearing assemblies; SCIP 9.2.4 opt-in 122/122 (the previous 120 plus the two new cases); Gurobi 10.0.1
opt-in 812/812. The isolated no-SDK copy restores, force-rebuilds and tests with 0 errors, 718 distinct warnings and 4750
tests (4749 passed, 1 skip, 22 assemblies), and the offline Linux x64 run (`--network none`, local NuGet cache only)
reports the same 718 warnings, 4750 tests and `LINUX_GATE_OK`. With the native library absent the SCIP opt-in runs 122
cases with 63 failures and 59 passes, every failure `OREngineEnvironmentLost`, so a missing environment is still not
counted as a pass.

Not verified or still open: the quadratic IIS pipeline (`QuadraticIisCompute`, `QuadraticTetradElasticBuilder`), `ExportLP`
and the native function writers, the SCIP quadratic and CP surfaces, the Gurobi 11, COPT, CPLEX, MindOpt, MOSEK, Hexaly,
LINGO and OPTVerse backends, Linux native SCIP, cross-language wire fixtures and performance measurements. P3 and the full
synchronization are still in progress.

中文说明：本批把固定基线 `Linear.kt` 的 legacy IIS 全链路落到 Core。`LinearIisCompute.ComputeIISAsync` 先跑后端诊断
分析器并追加 `LegacyElasticInfeasibilityAnalyzer`：拿到物化 artifact 就用它，`NativeIis` 证据可按诊断 ID 或
`constraint-<index>` 命中行、按证据指出的那一侧变量界保留列后物化，两者都没有时才由 legacy 引擎亲自构建子系统，因此
没有原生 IIS 的后端也能拿到归约后的模型而不是原模型快照。legacy 引擎要求模型是 `LinearTriadModel` 并先校验
`IisConfig`，随后用“至少 2 个非零松弛、阈值取 `SlackTolerance`”的指示约束构建弹性模型，三轮过滤依次只放开界松弛、
不等式行与界松弛、全部松弛，每轮把要放开的松弛上界写成 `+inf`、其余写成 0 后重解，只有取值达到容差的松弛才算被放开；
三轮都不可行时按列索引升序逐个把松弛上界归零，模型变可行就删掉该松弛、仍不可行就恢复 `+inf`，每次探测都回报剩余量。
子系统只保留命中行松弛来源的原行、这些行涉及的原列与命中界松弛所在列，界松弛只保留被证据指出的那一侧，另一侧放开到无穷，
列重编号为连续位置并同步重映射行系数，模型名改为 `<原模型名>_iis`；守卫批次为空表示“去掉这些行就可行”，此时
`RelaxedFeasible` 为 false、证据完整度降为 Partial。

`VariableSlack` 新增 `ConstraintSlackOrigin(RowIndex, Relation)`：`Feasibility()` 与 `Elastic()` 的行松弛现在记录来源行的
原始索引与原始关系符号，所以右端为负被取反的行仍报告原始关系，同一等式行的两条松弛按值相等；界松弛仍指回原变量、不带来源行，
上一批披露的“行松弛没有来源约束元数据”到此关闭。SCIP 侧新增能力描述符（`scip`／`SCIP`，`LinearCapabilities`，无原生 IIS），
旧 IIS 入口在拿到不可行结论时改走统一入口：成功返回 `IisAvailable = true` 的 `LinearInfeasibleSolverOutput` 与
`LinearIisModel`，失败仍保留原模型快照、`IisAvailable = false` 并把错误追加到既有诊断，问题结论本身不变。

差异披露：子系统用 `OriginRows`／`OriginColumns` 回溯原行原列，取代 Kotlin 的行 `origins` 与结构相等匹配
（`sameLinearConstraint`）；证据 ID 统一走 `DiagnosticConstraintId`／`DiagnosticVariableId`，不再复刻 legacy 命名；
子系统列重编号为连续位置并同步重映射行系数，Kotlin 保留原列索引；删除过滤用索引集合而不是原地修改候选对象，探测顺序与回调
时机不变；成功分支仍保留结构化证据（Kotlin 的 `invoke` 只返回模型），因此当后端给出精确 Farkas 证据却没有 artifact 时，
返回的模型是 legacy 子系统、报告的也是 legacy 启发式等级，精确证书本批未与子系统并列携带；legacy 循环只把取消令牌交给每次求解、
不自己维护 `IisConfig.Time` 预算，进度回调的 `isFeasible` 恒为 true，需要读 `RelaxedFeasible` 或证据判断是否依赖守卫行；
`LinearIisModel.ExportLP` 仍是继承的占位，二次 IIS 与 `QuadraticTetradElasticBuilder` 仍是占位，留待后续批次。

门禁实测：Windows Release 强制重建 0 错误、与上一批相同的 743 条去重警告（本批含新文件零新增）；默认全量 4802 项
（4801 通过、1 项既有跳过、23 个测试程序集）；SCIP 9.2.4 opt-in 122／122（= 原 120 + 新增 2 项）、Gurobi 10.0.1 opt-in
812／812。无 SDK 隔离副本 restore、`--no-incremental` 强制重建与测试均 0 错误、718 条去重警告、4750 项（4749 通过、
1 跳过、22 程序集）；Linux x64 离线复测同样 718 警告、4750 项并输出 `LINUX_GATE_OK`。缺原生库时 SCIP opt-in 跑满 122 项、
63 失败／59 通过，63 项失败全部是 `OREngineEnvironmentLost`，环境缺失不计为验收。二次 IIS、`ExportLP` 与原生函数 writer、
SCIP 二次与 CP、其余后端、Linux 原生 SCIP、跨语言 wire fixture 与性能测量继续属后续批次，P3 与全量同步仍在进行。

### Previous gate: linear and quadratic LP export

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core/model/intermediate/LinearTriadModel.kt:447-524` and `:2096-2132` | `src/Core/Model/Intermediate/LinearTriadModel.cs` (`BasicLinearTriadModel.ExportLP`, `LinearTriadModel.ExportLP`), `src/Core/Model/Intermediate/LpWriterSupport.cs` | `tests/Core.Tests/Model/Intermediate/ModelViewLpExportTest.cs` | Contract verified: linear objectives, blank objective names, generated `cons{index}` row names, an empty row as `0`, ±1 coefficient elision, zero-term skipping, the objective constant omission, bounds, binaries, generals and `End` follow the pinned baseline; closed models, null writers, invalid columns and I/O failures return `Result` |
| `core/model/intermediate/QuadraticTetradModel.kt:386-465` and `:1783-1818` | `src/Core/Model/Intermediate/QuadraticTetradModel.cs` (`BasicQuadraticTetradModel.ExportLP`, `QuadraticTetradModel.ExportLP`) | same test class plus `tests/Core.Plugin.Gurobi.Tests/GurobiQuadraticSolverTest.cs` (`Quadratic_Export_Should_Be_Readable_By_Gurobi`) | Contract and Gurobi 10.0.1 integration verified: a negative second column remains a linear sentinel, the other cells become quadratic terms, boundaries follow the pinned literal-infinity rule, and the exported objective plus mixed linear/quadratic constraints are read back and solved by Gurobi with the expected objective |
| `ModelView.export`, file naming and UTF-8 behavior | `src/Core/Model/Basic/ModelViewFileExporter.cs`, `ModelViewExportExtensions` | same Core test class (directory export, UTF-8 without BOM, I/O failure) | Contract verified: a directory is written as `<model>.lp`, an explicit path is treated as a file, and unsupported formats, empty paths and I/O failures return `Result` |

Deliberate divergence: the pinned Kotlin quadratic writer emits bare `x * x` / `x * y` terms, which Gurobi 10.0.1 rejects as
malformed LP syntax. C# keeps the same coefficients, linear sentinel, term signs, zero skipping and bounds, but writes linear
terms outside a quadratic block and uses `x ^ 2` / `x * y` inside `[ ... ]`. This changes the golden text, not the model
semantics, and makes the file readable by the primary backend. No `/2` scaling is emitted because Gurobi was measured to
interpret the block coefficients literally in this form.

The native-function writer entry points are only contract verified by `NativeFunctionWriterRegistry`: the registry validates
writer counts, skips empty batches, preserves writer order and stops at the first failure, but Gurobi, SCIP and the other real
backends do not dispatch delayed native functions through it yet. Quadratic IIS, SCIP quadratic and CP, the remaining real
backends, cross-language wire fixtures and performance measurements remain open.

Gate measurements: Windows Release forced rebuild has 0 errors and the existing 743 distinct warnings; the default suite has
4814 tests (4813 passed, 1 pre-existing skip) across 23 test-bearing assemblies; Gurobi 10.0.1 opt-in is 813/813 and SCIP
9.2.4 opt-in is 122/122. The isolated no-SDK copy restores, force-rebuilds and tests with 0 errors, 718 distinct warnings and
4761 tests (4760 passed, 1 skip, 22 assemblies); the offline Linux x64 run reports the same 718 warnings, 4761 tests and
`LINUX_GATE_OK`. With the native SCIP library absent the opt-in run has 63 failures and 59 passes, and every failure is
`OREngineEnvironmentLost`; missing native or licensed dependencies are not counted as acceptance. Gate root:
`D:\temp\ospf-p3-quadratic-lp-20261030`.

中文说明：本批把固定 Kotlin 基线的线性与二次 LP 导出落到 Core，并补上目录/文件导出入口。线性文本保持空行名 `cons{i}`、
空行 `0`、单位系数省略、零系数跳过、目标常数不输出以及 Bounds/Binaries/Generals 的基线顺序；二次文本仍把第二列小于 0
的单元格视为线性项、其余单元格视为二次项，并保留二次侧的按字面无穷判断。与 Kotlin 的唯一有意差异是二次项语法：
Kotlin 直接写裸 `x * x`/`x * y`，Gurobi 10.0.1 会报 “Malformed term”；C# 因而把线性项留在二次块外，在 `[ ... ]`
内用 `x ^ 2`/`x * y`，不改变系数、符号或模型含义。已新增真实 Gurobi 读回并求解用例，确认目标与混合线性/二次约束
均可解析且目标值正确；黄金文本相应更新。`NativeFunctionWriterRegistry` 仍只是契约层，尚未接入任何真实后端，
不能宣称原生函数 writer 能力完成。二次 IIS、SCIP 二次与 CP、其余真实后端、跨语言 wire fixture 和性能测量继续未验收；
P3 与全量同步仍在进行。

### Current gate: SCIP quadratic models

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core-plugin-scip/ScipQuadraticSolver.kt` (`dump`, `configure`, `solve`, `analyzeStatus`, `analyzeSolution`) | `src/Core.Plugin.Scip/ScipQuadraticSolver.cs` (`InvokeReportAsync`, both legacy `InvokeAsync` overloads), `ScipQuadraticNativeSolver.cs` | `tests/Core.Plugin.Scip.Tests/ScipQuadraticIntegrationTest.cs` (6 opt-in cases), `ScipQuadraticSolverTest.cs` | Integration verified on Windows SCIP 9.2.4: convex QP with an objective offset, quadratic constraints, the legacy single-solution entry, status callbacks whose `IntermediateModel` is the source model, cancellation before the snapshot and during the native search, and full initial hints submitted before quadratic-objective auxiliaries |
| `core/model/intermediate/QuadraticTetradModel.kt`, `core-plugin-scip/ScipQuadraticSolver.kt` objective lowering | `src/Core.Plugin.Scip/ScipQuadraticSnapshot.cs`, `ScipQuadraticNativeSolver.cs` | same integration class plus `ScipQuadraticSolverTest.QuadraticDump_ShouldConvertAndRespectCancellationWithoutSdk` | Contract verified: linear objective cells are accumulated per column; unordered quadratic cells are normalized, duplicate coefficients summed, zero terms removed and ordered by column pair; each quadratic objective term becomes a continuous auxiliary variable and an equality against the product, matching the pinned Kotlin lowering. Constraints support `<`, `>` and `=` through SCIP's `SCIPcreateConsBasicQuadratic`; linear rows use the linear native constructor |
| `core/solver/SolveReport.kt`, SCIP status/proof propagation | `ScipLinearNativeSolver.ReadReport`, `ScipQuadraticNativeSolver.QuadraticCapabilities` | same integration class | Contract and Windows SCIP 9.2.4 integration verified: QP/QCP capability declaration, source-model callback, solution values and objective, result-aware native/parameter failures, cancellation report, solution pool capacity forwarding, and descriptors with SCIP's runtime version. The report carries SCIP's claimed proof; it does not independently verify feasibility, optimality or the certificate |
| Cancellation and initial-solution lifecycle | `ScipQuadraticNativeSolver.Solve` / `Run`, `ScipLinearNativeSolver.SubmitInitialSolution` | same integration class (cancellation and full-hint cases) | Integration verified: partial/full initial hints are submitted after the original rows but before quadratic-objective auxiliary variables are added, so SCIP can validate a full candidate over the original columns without treating zero-valued auxiliaries as user values. A cancellation before or during native work returns a cancelled report rather than inventing an incumbent |

The C# quadratic entry preserves the Kotlin formulation rather than using SCIP's direct quadratic-objective API. It creates one
continuous auxiliary per combined quadratic objective term with the term coefficient as its objective, then posts an equality
auxiliary `- product = 0`. This keeps the same objective sign and sense as the pinned baseline. Duplicate cells are combined before
creation, so the number of auxiliaries is the number of nonzero normalized pairs rather than the number of caller cells. The
snapshot validates model identity, finite coefficients/right-hand sides and indexes, native representability, variable types and
initial values. Constraint priorities remain explicitly unsupported with `Other`, matching the current linear SCIP boundary instead
of silently dropping ordering semantics.

Numerical differences are deliberate and bounded: model values are converted through `Flt64.ToDouble()` at the SCIP boundary,
the snapshot rejects non-finite values, non-zero values that become zero are not separately distinguished, and normal floating
point addition can round or underflow when duplicate cells are combined. The result report reads only the original columns; the
objective auxiliary values are not exposed as model variables. It does not prove that the returned vector is feasible, and SCIP's
optimal/infeasible statuses remain backend claims. Status callbacks receive the caller's intermediate model, not the snapshot or
native auxiliary model. The legacy `IisConfig` overload returns a `QuadraticInfeasibleSolverOutput` and adds a structured
`quadratic-iis-unavailable` issue when the model is infeasible; quadratic IIS is not implemented. CP diagnostics, native function
writers, concurrent SCIP solving, cross-language fixtures and performance measurements remain outside this batch.

Gate measurements: Windows Release forced rebuild has 0 errors and the existing 743 distinct warnings. The default suite has 4814
tests (4813 passed, 1 pre-existing skip) across 23 test-bearing assemblies. Windows SCIP 9.2.4 opt-in is 128/128 (the previous 127
plus the full-initial-hint lifecycle case) and Gurobi 10.0.1 opt-in is 813/813. The isolated no-SDK copy restores, force-rebuilds
and tests with 0 errors, 718 distinct warnings and 4768 tests (4767 passed, 1 skip, 23 assemblies). The offline Linux x64 run
(`--network none`, local NuGet cache only) reports 718 warnings, 4761 tests (4760 passed, 1 skip, 22 assemblies; MOSEK has no
Linux test run) and `LINUX_GATE_OK`. With the native SCIP library absent, the opt-in project runs 128 cases with 68 failures and
60 passes; every failure originates from `OREngineEnvironmentLost`, so missing native dependencies are not counted as acceptance.
Gate root: `D:\temp\ospf-p3-scip-quadratic-20261030`.

中文说明：本批把固定基线 `ScipQuadraticSolver.kt` 的 QP/QCP 路径落到 C#。二次目标沿用 Kotlin 的做法，而不是调用 SCIP
的直接二次目标 API：先把归一化、去重后的每个二次目标项建成一个连续辅助变量，目标系数原样放到辅助变量上，再加一条
`辅助变量 - x*y = 0` 等式；线性目标项仍直接累加到原列，因此最大／最小方向、系数符号与常数项保持基线语义。二次约束通过
`SCIPcreateConsBasicQuadratic` 写入，纯线性行走线性构造器；变量、行、目标单元格、初始提示和原生可表示范围先做快照校验，
约束优先级仍明确返回 `Other`，不会静默丢掉排序语义。目标辅助变量不进入返回解向量，状态回调拿到的仍是调用方的中间模型。

初始解提示的生命周期也已对齐：完整的原列提示在原始约束写入后、二次目标辅助变量创建前提交，让 SCIP 在只有原始变量时验证
候选，而不是把尚未赋值的辅助变量零值混进完整解。取消在快照前或原生求解中均返回 cancelled 报告，不伪造 incumbent；
原生库或参数失败走 Result，能力描述符声明 QP/QCP、回调与中断。报告沿用 SCIP 的结论和 claimed proof，不独立证明解可行或
最优；数值在边界转换为 `double`，重复项累加可能有浮点舍入或下溢。二次 IIS 入口仍返回不可用 issue，CP、原生函数 writer、
SCIP 并发求解、跨语言 fixture 与性能测量仍属后续批次。

门禁实测：Windows Release 强制重建 0 错误、既有 743 条去重警告；默认全量 4814 项（4813 通过、1 项既有跳过、23 个测试
程序集）；Windows SCIP 9.2.4 opt-in 128／128（上一轮的 127 加新增完整初始提示用例）、Gurobi 10.0.1 opt-in 813／813。
无 SDK 隔离副本 restore、`-t:Rebuild` 强制重建与测试均 0 错误、718 条去重警告、4768 项（4767 通过、1 跳过、23 程序集）；
Linux x64 离线复测 718 警告、4761 项（4760 通过、1 跳过、22 程序集，MOSEK 在 Linux 无测试）并输出 `LINUX_GATE_OK`。
缺原生库时 SCIP opt-in 跑满 128 项、68 失败／60 通过，失败均源自 `OREngineEnvironmentLost`，不计为验收。门禁根目录：
`D:\temp\ospf-p3-scip-quadratic-20261030`。

### Current gate: SCIP exact-lowering CP facade

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core-plugin-scip/ScipConstraintProgrammingSolver.kt`, `ScipConstraintProgrammingCompiler.kt`, `ScipConstraintProgrammingStatusMapper.kt` | `src/Core.Plugin.Scip/ScipConstraintProgrammingSolver.cs` (`SolveAsync`, `CreateSession`, `Descriptor`) | `tests/Core.Plugin.Scip.Tests/ScipConstraintProgrammingContractTest.cs`, `ScipConstraintProgrammingIntegrationTest.cs` | Contract verified and Windows SCIP 9.2.4 integration verified: finite-domain CP is lowered exactly to an integer MIP, then solved through `ScipLinearSolver`; the facade declares `Cp` only and every supported CP feature as `ExactLowering`, never as native CP |
| Kotlin CP compiler/status behavior, C# exact lowerer and source-variable projection | `ConstraintProgrammingToLinearModelLowerer`, `MechanismModelBuildExtensions.BuildLinearTriadModelAsync`, `ScipConstraintProgrammingSolver.ExtractSolution` | same integration class | Integration verified: simple integer optimum, exact objective recomputation, fixed values, a false assumption, scalar hints, interval hints, AllDifferent, and allowed tables. Returned values are read from exactly one matrix column whose `Origin` is the lowered source variable, must be integral and inside the source domain, and auxiliary columns are never exposed as CP variables |
| Kotlin CP options, capability/status mapping, cancellation, and unsupported diagnostics | same C# entry point plus `ConstraintProgrammingSolveOptions`, `SolveCancellationToken`, `SolveReport<Int64>` | same contract/integration classes | Contract verified: conflicting option gates, unsupported Circuit/Automaton/Reservoir, conflict cores, native logging, invalid/type-mismatched backend snapshots, sensitive snapshot parameters, and facade-managed native limits fail before touching the native library; caller and option cancellation return `Cancelled` without native work; real solves preserve the SCIP claimed proof instead of inventing a verified proof |

Deliberate boundaries and differences: this is a strict finite-integer MIP lowering facade, not SCIP
native CP. `Circuit`, `Automaton`, `Reservoir`, CP conflict cores, and native logging are unsupported.
`Cumulative` is declared only when `ConstraintProgrammingLoweringPolicy.AllowCumulative` is true.
Backend snapshots are reconstructible only from non-sensitive, explicitly supported parameters; a
redacted sensitive value is rejected rather than treated as applied. Facade-owned `limits/time`,
`limits/gap`, `limits/absgap`, `limits/nodes`, `limits/solutions`, and deterministic/thread settings
cannot be overridden through `NativeParameters`. A false assumption or source conflict is reported as
infeasible with SCIP's claimed proof, not an independently verified conflict core. Native SCIP CP,
quadratic IIS, native function writers, cross-language wire fixtures, and performance measurements
remain outside this batch.

Gate measurements: Windows Release forced rebuild has 0 errors and the existing 743 distinct
warnings; the three new source/test files add no warnings. The default suite has 4819 tests (4818
passed, 1 pre-existing skip) across 23 test-bearing assemblies; the default SCIP assembly has 61
passing cases. Windows SCIP 9.2.4 opt-in is 137/137. Build and test logs are under ignored `logs/`.

中文说明：本批把固定基线的 SCIP CP 求解器落成一个严格有限域 CP 门面，但它不是 SCIP 原生 CP。
门面先把有限整数域、布尔逻辑、reification、稀疏域、AllDifferent、Element、表约束、区间、NoOverlap、
assumptions、提示和增量求解精确降为整数 MIP，再复用现有 SCIP 线性求解器；`Cumulative` 只有显式
`AllowCumulative` 策略才声明为可用，`Circuit`、`Automaton`、`Reservoir`、冲突核和原生日志明确不支持。
描述符只声明 `Cp`，所有支持项都是 `ExactLowering`，未把 JSCIP 的原生能力伪装成 C# 已有能力。

解提取只按 `ModelViewVariable.Origin` 找回每个源 CP 变量恰好一列，拒绝非整数、超源值域或超精确 double
范围的返回值，辅助列不会泄漏到 CP 解；目标值通过源 CP 表达式重新精确求值，避免 SCIP 的 double 目标造成整数
精度损失。固定值、false assumption、标量 hint、interval hint、AllDifferent 和 allowed table 已用真实
SCIP 9.2.4 验证。冲突核、原生日志、敏感 backend 快照、错误 backend 类型、被门面管理的原生限制均在触碰原生库
前返回结构化失败；调用方 token 与 `SolveCancellationToken` 都能得到 cancelled 报告。报告仍保留 SCIP 的 claimed
proof，不伪造 verified 证明或冲突证据。二次 IIS、原生函数 writer、跨语言 fixture、性能测量和真正原生 CP 仍是后续缺口。

门禁实测：Windows Release 强制重建 0 错误、既有 743 条去重警告，新增三个源码/测试文件 0 警告；默认全量
4819 项（4818 通过、1 项既有跳过、23 个测试程序集），SCIP 默认程序集 61 项通过；Windows SCIP 9.2.4
opt-in 137／137。构建与测试日志位于被忽略的 `logs/`。

### Current gate: COPT linear, quadratic and diagnostics

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core-plugin-copt/CoptLinearSolver.kt`, `CoptQuadraticSolver.kt`, `CoptColumnGenerationSolver.kt` | `src/Core.Plugin.Copt/CoptLinearSolver.cs`, `CoptQuadraticSolver.cs`, `CoptNativeSolver.cs`, `CoptSnapshot.cs`, `CoptColumnGenerationSolver.cs` | `tests/Core.Plugin.Copt.Tests/CoptIntegrationTest.cs`, `CoptDualIntegrationTest.cs`, `CoptUnavailableTest.cs` | Integration verified on Windows COPT 8.0.3: continuous, integer, binary, convex QP and QCP models; row duals for LP pricing and Benders sub-problems; exact objective constants, repeated coefficients, solution pools, initial hints, cancellation, status callbacks and time-limit reporting |
| `core-plugin-copt/CoptBendersDecompositionSolver.kt` | `src/Core.Plugin.Copt/CoptBendersDecompositionSolver.cs`, `CoptDualSupport.cs` | same `CoptDualIntegrationTest.cs` | Integration verified for linear sub-problems: feasible cases return row duals and infeasible cases return native Farkas multipliers. COPT's native `>=` convention is preserved: `>=` rows receive positive multipliers and `<=` rows receive negative multipliers; `ReqFarkasRay=1` is set before the native solve |
| COPT infeasibility analysis used by the Kotlin IIS paths | `src/Core.Plugin.Copt/CoptNativeSolver.cs` (`ComputeIis`, `CoptInfeasibilityAnalyzer`) | `tests/Core.Plugin.Copt.Tests/CoptIisIntegrationTest.cs` | Integration verified: `ComputeIIS` produces native evidence and a replayable infeasible projection for LP and finite MIP models; the legacy IIS entry attaches the same native evidence and reports `IisAvailable=true` |
| `core-plugin-copt/CoptSolverCallBack.kt`, `CoptNativePiecewise.kt`, `CoptFunctionCapabilities.kt`, `CoptPiecewiseSelection.kt` | no complete C# port; `CoptSolveCallback.cs` covers progress/status notification only | capability assertions in `CoptIntegrationTest.cs` | Gap: linear/quadratic capability records explicitly leave `Callback=false`. Point-based native callback managers, creating-environment callbacks, reflective SOS2 capability detection, native PWL selection/writing and solver-local PWL fallback have not been ported |

The COPT adapter now rejects statuses `2` (infeasible), `3` (unbounded) and `4` (infeasible or
unbounded) before inspecting residual native solution objects. This fixes a real 8.0.3 regression
where an unbounded LP could retain `HasLpSol=1` and be reported as an incumbent. The declared
capability sets remain narrow: linear models declare `Dual`, `Farkas` and `NativeIis`; quadratic
models do not. Warm start, solution pool and interrupt are declared for both. `Dual`, `Farkas` and
`NativeIis` are integration-tested only on linear models. Native IIS on the tested finite MIP is
verified, but quadratic IIS and quadratic Benders remain unimplemented.

The native status maps to a claimed proof only when the backend reports a completed conclusion;
time, node, iteration, cancellation and interrupt terminations do not fabricate a proof. Reports
read the pinned managed assembly version, which is not a runtime native-library probe. Values cross
the COPT boundary as `double`; repeated coefficients and solution-pool values use that numerical
domain. Free bounds are recognized near `+/-1 / DecimalPrecision` and mapped to the COPT finite
sentinel. No commercial license or large-model behavior was exercised; the environment runs COPT
8.0.3 in its non-commercial size-limited mode.

Gate measurements for this batch: Windows COPT 8.0.3 opt-in `32/32`; isolated no-SDK COPT project
build `0 warnings / 0 errors` and no-SDK tests `13/13`; forced Release solution rebuild `0 errors`
with the existing 743 warnings; default suite `4818 passed / 1 pre-existing skip / 0 failed`.
`Hexaly.Tests` and
`Benchmark.Tests` exposed no runnable tests and are not counted as integration passes. Raw logs are
under ignored `logs/`.

中文说明：本批完成 COPT 8.0.3 的线性/二次真实求解验收，而不是宣称整个 COPT 适配器已经全量同步。
Windows 真机 opt-in 32 项全部通过；无 SDK 时项目可构建，13 项无依赖契约测试通过。LP/MIP、QP/QCP、
线性行对偶、Farkas 证书、LP/MIP 原生 IIS、解池、初始解、取消与状态回调已经落地；报告按原生完成状态给出
claimed proof，不把时间/节点/迭代上限或取消误报为证明。Farkas 保持 COPT 原生 `>=` 符号约定，并显式设置
`ReqFarkasRay=1`。无界模型会排除原生残留 LP 解，不再伪造 incumbent。

能力声明仍严格区分：线性声明 Dual/Farkas/NativeIis，二次不声明这三项；WarmStart/SolutionPool/Interrupt
两者都有。**B1220 更正：point-based 原生回调管理器已移植**（`CplexSolveCallback` 201 行 +
`CplexContinuousCallback`，两者都在 `CplexNativeSolver.cs` 里经 `model.Use(...)` 接线；基线
`CplexSolverCallBack.kt` 为 157 行）。**仍成立的部分**：创建环境回调、SOS2 探测与原生 PWL 写入尚未移植，因此
`Callback=false`；二次 Benders 也未实现。当前仅在非商业规模限制模式下验收，商业许可、大模型、二次 IIS、
原生 PWL 和完整后端验收均未完成。构建/测试原始日志位于被忽略的 `logs/`。

**B1220 补注：上面这段的 `Callback=false` 属于 <b>COPT</b>，不是 CPLEX——两者在同一份文档里用了同一个词，
而结论相反。** **CPLEX 的 `CplexSolveCallback`（201 行）与 `CplexContinuousCallback` 都继承 SDK 的
point-based 回调基类并经 `model.Use(...)` 接线，所以 CPLEX 的 `Callback=false` 已不成立；
COPT 的 `CoptSolveCallback`（148 行）只重写了一个通用的 `callback()`，并不是 point-based 回调族，
所以 COPT 的 `Callback=false` <b>仍然成立</b>。**

**"同一个词在两个后端上结论相反"是这类文档最容易出错的地方：**
**一个读者扫到第一处 `Callback=false` 就以为所有后端都没有回调，而事实是只有一个没有。**
**所以在纠偏时不能按字面全局替换，只能逐个后端核对源码。**

**B1220 addendum: the `Callback=false` above belongs to <b>COPT</b>, not CPLEX — both keywords appear in one document
with opposite conclusions.** **CPLEX's callbacks inherit the SDK's point-based callback bases and are wired through
`model.Use(...)`, so CPLEX's claim no longer holds; COPT's single `callback()` override is not the point-based family, so
COPT's claim <b>still holds</b>.**

**"One word, opposite conclusions on two backends" is where this kind of document most easily goes wrong: a reader who
sees the first `Callback=false` concludes no backend has callbacks, while in fact only one lacks them.** **So a correction
cannot be a global replace — it has to be verified per backend against the source.**

### Current gate: CPLEX linear, quadratic and diagnostics

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core-plugin-cplex/CplexLinearSolver.kt`, `CplexQuadraticSolver.kt`, `CplexColumnGenerationSolver.kt` | `src/Core.Plugin.Cplex/CplexLinearSolver.cs`, `CplexQuadraticSolver.cs`, `CplexNativeSolver.cs`, `CplexSnapshot.cs`, `CplexColumnGenerationSolver.cs` | `tests/Core.Plugin.Cplex.Tests/CplexIntegrationTest.cs`, `CplexDualIntegrationTest.cs`, `CplexUnavailableTest.cs` | Integration verified on Windows CPLEX 12.10.0.0: continuous, integer and binary LP/MIP, QP/QCP, objective offsets, repeated coefficients, solution pools, initial MIP starts, cancellation, status callbacks and time-limit reporting. An unbounded LP never exposes a residual solution as an incumbent; CPLEX's `InfeasibleOrUnbounded` result is preserved instead of being relabelled |
| `core-plugin-cplex/CplexBendersDecompositionSolver.kt`, `CplexColumnGenerationSolver.kt` | `src/Core.Plugin.Cplex/CplexBendersDecompositionSolver.cs`, `CplexColumnGenerationSolver.cs`, `CplexDualSupport.cs` | `tests/Core.Plugin.Cplex.Tests/CplexDualIntegrationTest.cs` | Integration verified for linear pricing and Benders sub-problems: feasible models return native row duals and infeasible models return native Farkas multipliers. Farkas runs with presolve disabled and dual preprocessing enabled; the raw `>=` sign convention is preserved, with positive multipliers for `>=` rows and negative multipliers for `<=` rows. Certificates contain row multipliers only |
| `core-plugin-cplex/CplexSolver.kt`, `CplexSolverCallBack.kt`, `CplexLinearSolver.kt`, `CplexQuadraticSolver.kt` | capability records and `CplexReportSupport.cs`; SDK-free `CplexUnavailable.cs` | same integration and unavailable classes | Contract and integration verified: reports preserve problem conclusion, termination, incumbent presence, claimed proof, statistics and provenance; linear models declare Lp/Mip/Dual/Farkas/WarmStart/SolutionPool/Interrupt and quadratic models declare Qp/Qcp/Mip/WarmStart/SolutionPool/Interrupt. **B1220 correction: the point-based callback manager is ported** (`CplexSolveCallback` plus `CplexContinuousCallback`, wired through `model.Use(...)`), so `Callback=false` no longer applies. A non-empty backend configuration is rejected as unsupported rather than silently ignored |
| `core-plugin-cplex/CplexNativePiecewise.kt`, `CplexFunctionCapabilities.kt`, `CplexPiecewiseSelection.kt` | no complete C# port | capability assertions in `CplexIntegrationTest.cs` | Gap: native SOS2/PWL capability selection and writers are not implemented. Native IIS requests also return an explicit unsupported result; quadratic Benders is not implemented |

The CPLEX adapter is compile-optional: `CPLEX_STUDIO_DIR1210` locates the managed assemblies, and the
default solution still builds when they are absent. Runtime native solves additionally require the
CPLEX DLL directory on `PATH` and a valid license. The tested version is Windows CPLEX 12.10.0.0.
The reported backend version is read from the pinned managed assembly, not probed from the native
library. Values cross the CPLEX boundary as `double`; repeated coefficients can therefore round.
Reports retain CPLEX's claimed proof and do not independently prove feasibility, optimality or a
certificate.

Farkas discovery disables presolve because CPLEX otherwise reports error 1265 instead of a usable
dual ray. Status/no-improvement callbacks also disable presolve so that MIP information callbacks
are observable; this is a deliberate progress-reporting trade-off, not a performance claim. MIP
information callbacks do not fire on every small model even with presolve disabled, so callback
tests use the recorded random 1000-variable fixture. The continuous callback only exposes objective
and time progress because the current callback path has no incumbent vector.

Gate measurements for this batch: Windows CPLEX 12.10 opt-in `24/24`; isolated no-SDK CPLEX project
tests `12/12`; forced Release solution rebuild `0 errors` with the existing 743 warnings; default
suite `4818 passed / 1 pre-existing skip / 0 failed` across 23 test-bearing assemblies. Hexaly,
Benchmark and no-SDK MOSEK exposed no runnable tests and are not counted as integration passes.
Raw logs remain under ignored `logs/`.

中文说明：本批完成 Windows CPLEX 12.10 的线性/二次真实求解与诊断接入，不代表整个 CPLEX 适配器或 P3
已经全量同步。LP/MIP、QP/QCP、目标常数、LP 行对偶、Farkas、列生成定价对偶、Benders 可行子问题对偶、
不可行子问题 Farkas、解池、初始 MIP start、取消、时限和状态回调均已落地。无界模型不会把 CPLEX
残留解对象误报为 incumbent；`InfeasibleOrUnbounded` 也按其原生含义保留。

能力声明严格区分：线性声明 Dual/Farkas/WarmStart/SolutionPool/Interrupt，二次不声明 Dual/Farkas，
只声明 QP/QCP/MIP、WarmStart、SolutionPool 和 Interrupt。状态通知可用，但由于 Kotlin 的完整
**B1220 更正：point-based 回调管理器已移植，`Callback=false` 不再成立**；`NativeIis=false` 仍成立
（传入 IIS 配置会得到明确的不支持错误），这是 CPLEX 后端的真实缺口。
非空远程 backend 配置同样被拒绝，不会静默忽略。SOS2/PWL 原生选择与写入、二次 Benders、商业规模许可、
Linux/macOS 和完整后端验收仍未完成。

Farkas 路径必须关闭 presolve 并启用 dual 预处理，否则 CPLEX 会报错误 1265；状态/无改进监控路径也关闭
presolve 以保证 MIP 信息回调可出现，这是可观察性取舍，不构成性能结论。CPLEX 对无界 LP 可能返回
`InfeasibleOrUnbounded`，报告保留该结论。数值跨边界转换为 `double`，重复系数可能舍入；报告保留 claimed
proof，不独立证明可行性、最优性或证书有效性。

门禁实测：Windows CPLEX 12.10 opt-in 24／24，无 SDK 隔离 12／12；Release 全方案强制重建 0 错误、
743 条既有警告；默认全量 4818 通过、1 项既有跳过、0 失败，共 23 个有测试程序集。Hexaly、Benchmark
和无 SDK MOSEK 仍无可用测试，不计为通过；原始日志位于被忽略的 `logs/`。

### Current gate: MindOPT linear, quadratic and diagnostics

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core-plugin-mindopt/MindoptLinearSolver.kt`, `MindoptQuadraticSolver.kt` | `src/Core.Plugin.Mindopt/MindoptLinearSolver.cs`, `MindoptQuadraticSolver.cs`, `MindoptNativeSolver.cs`, `MindoptSnapshot.cs` | `tests/Core.Plugin.Mindopt.Tests/MindoptContractTest.cs`, `MindoptUnavailableTest.cs`, `MindoptIntegrationTest.cs` | Implementation complete and contract verified for LP/MIP, QP/QCP, objective offsets, report provenance/fingerprints, legacy output mapping, status callbacks, solution pools, limits, warm starts and MIP cancellation. Real integration is unverified because the installed SDK rejects every solve with `License is not valid.` |
| `core-plugin-mindopt/MindoptColumnGenerationSolver.kt`, `MindoptBendersDecompositionSolver.kt` | `src/Core.Plugin.Mindopt/MindoptColumnGenerationSolver.cs`, `MindoptBendersDecompositionSolver.cs`, `MindoptDualSupport.cs` | same integration class | Implemented: LP row duals are read per row and mapped back to `MathConstraint`; feasible Benders sub-problems return row duals; infeasible sub-problems request `HasDualRay`, disable presolve and read the raw `DualSoln` ray as Farkas multipliers. The real certificate sign and validity have not been exercised |
| SDK availability and unsupported surfaces | `Core.Plugin.Mindopt.csproj`, `MindoptUnavailable.cs`, `MindoptReportSupport.cs` | default and `MINDOPT_HOME`-disabled test runs | Contract verified: the default build excludes native implementation files when `$(MINDOPT_HOME)/win64-x86/lib/mindoptcs.dll` is absent and exposes matching linear/quadratic entries that return `OREngineEnvironmentLost`; model dumping remains available. Linear capabilities declare Lp/Mip, Dual, Farkas, WarmStart, SolutionPool and Interrupt; quadratic capabilities declare Qp/Qcp/Mip, WarmStart, SolutionPool and Interrupt. `Callback=false`, and native IIS explicitly returns unsupported |

The pinned SDK is MindOPT 2.3.0 at `C:\environment\solver\mindopt230\2.3.0`. The managed assembly's
file version is misleading, so reports use `win64-x86/bin/mindopt.dll` file metadata and fall back to
the managed assembly version only when native metadata cannot be read. Backend snapshots reject NaN,
non-finite starts, invalid infinite bounds and unsupported backend configuration instead of silently
ignoring them. Values cross the native boundary as `double`; duplicate coefficient addition and
solution-pool deduplication can therefore round. `Callback=false` reflects the missing complete
point-based callback manager; the existing callback path is limited to status notification and MIP
abort. LP cancellation is checked before and after native work, while MIP cancellation uses
`MDOCallback.Abort()`. Native IIS, quadratic Benders, SOS2/PWL writers and the complete callback
manager remain outside this batch.

Gate measurements: default SDK-path contract tests pass `21/21`; opt-in integration builds with
0 errors but passes `21/29` and fails all 8 integration cases with `OREngineEnvironmentLost`
(`License is not valid.`). This is an environment blocker, not acceptance. The forced no-SDK build
has 0 errors in the target project and tests pass `23/23`. The cumulative Release solution build has
0 errors and 723 warnings on the incremental invocation; the current MindOPT sources add no warning.
The default full suite passes `4832` with `1` pre-existing skip across 23 test-bearing assemblies.
Hexaly, Benchmark and no-SDK MOSEK expose no runnable tests and are not counted as passes. Raw logs
are under ignored `logs/`; no source or build artifact from this gate is committed.

中文说明：本批完成 MindOPT 2.3.0 的线性/二次原生入口、统一报告、目标常数、行对偶、
Farkas 证据、解池、初始解、状态回调、取消和 Benders/列生成对偶回填，并将无 SDK 入口改为
显式 `OREngineEnvironmentLost` 门面。默认契约测试 21 项通过，无 SDK 边界 23 项通过；启用真实
集成后 8 项全部被本机无效许可证阻断，因此只能记为“实现完成、集成未验收”，不能宣称 MindOPT
真实后端可用。完整 point-based callback manager、原生 IIS、二次 Benders、SOS2/PWL writer 和
证书符号的真机有效性仍待后续批次；`Callback=false`、`NativeIis=false` 保持如实声明。

### Current gate: MOSEK linear models and diagnostics

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core-plugin-mosek/MosekLinearSolver.kt`, `MosekSolver.kt`, `MosekSolverCallBack.kt` | `src/Core.Plugin.Mosek/MosekLinearSolver.cs`, `MosekSolver.cs`, `MosekSnapshot.cs`, `MosekReportSupport.cs`, `MosekSolverCallBack.cs` | `tests/Core.Plugin.Mosek.Tests/MosekContractTest.cs`, `MosekUnavailableTest.cs`, `MosekIntegrationTest.cs` | Implementation complete for linear LP/MIP: immutable snapshots, standard constraint directions, repeated-coefficient accumulation, objective offsets and sense, initial solutions, time limits, MOSEK absolute gap, thread count, unified `SolveReport<Flt64>`, legacy output mapping, lifecycle/status callbacks and provenance/fingerprints. License-independent contracts cover capability declarations, pre-cancellation, invalid snapshots, unsupported requests and the no-SDK fallback; native solve, initial-solution and report-readback behavior remain unverified because every native solve is blocked by a missing license |
| MOSEK environment and native failure boundary | `MosekSolver.Init`, `Solve`, `AnalyzeStatus`; `MosekReportSupport.ClassifyErrorCode` | same contract/integration classes | The opt-in run exercised environment creation up to native `optimize` and verified that `err_missing_license_file(1008)` maps to `AuthenticationError`. Native status/result-code mapping, MIP `itg` preference, statistics and cancellation during native work could not be exercised. The installed managed SDK is 10.2.11 |
| Capability and unsupported surfaces | `MosekReportSupport.LinearCapabilities`; `MosekLinearSolver` | `MosekContractTest`, integration capability assertions | Declared capabilities are exactly `Lp`, `Mip` and `WarmStart`. `Dual`, `Farkas`, `NativeIis`, `SolutionPool`, `Callback` and `Interrupt` are false. A non-empty backend configuration, no-improvement interruption, solution pool requests and native progress callbacks fail explicitly instead of being silently ignored. Lifecycle and final status callbacks are available, but they are not native progress callbacks |
| SDK-free build boundary | `Core.Plugin.Mosek.csproj`, `MosekUnavailable.cs` | forced `MOSEK_10_2_INSTALLDIR=` build and `MosekUnavailableTest` | Contract verified: without the managed SDK the native implementation files are excluded and linear report/legacy entries return `OREngineEnvironmentLost`; mechanism-to-triad dumping remains available, including its cancellation result |

Deliberate boundary and baseline difference: the pinned Kotlin `MosekLinearSolver.dump` maps
`LessEqual` to `boundkey.lo` and `GreaterEqual` to `boundkey.up`, which reverses both row
relations. The C# adapter uses the model's standard semantics: `LessEqual` maps to
`boundkey.up`, `GreaterEqual` maps to `boundkey.lo`, and equality maps to `boundkey.fx`.
This difference is intentional; copying the baseline mapping would silently change every MIP
or LP constraint. The pinned Kotlin environment initialization, configuration and solution
extraction are placeholders, so this batch implements the native path rather than translating
those placeholders.

The adapter remains linear-only. It does not expose MOSEK quadratic models, duals, Farkas
certificates, native IIS, solution pools, native progress callbacks or in-solve interruption.
Values cross the MOSEK boundary as `double`; repeated coefficients can round, and the report
retains the backend conclusion as a claimed proof without independently verifying feasibility,
optimality or a certificate. MIP status is read from `soltype.itg` before falling back to the
basis status, but no licensed solve was available to confirm that behavior end to end.

Gate measurements: the SDK-path contract suite passes `6/6`; the isolated no-SDK boundary
passes `3/3`; the opt-in project builds with 0 errors and runs 9 cases (`6` contract passes,
`3` native failures). All three native failures return `AuthenticationError` with
`err_missing_license_file(1008)`, so they are environment blockers, not acceptance. The full
Release build has 0 errors and 723 existing warnings. The default full suite has `4831` passes,
1 pre-existing Delaunay skip and 0 failures across 23 test-bearing assemblies; Hexaly and
Benchmark have no runnable tests. Raw MOSEK output is in ignored `logs/mosek-integration.log`;
the license probe is in `logs/mosek-license-probe.log`.

中文说明：本批实现 MOSEK 10.2 线性入口的环境、建模、配置、求解、状态读取、统一报告、
legacy 输出、初始解、时限、绝对 gap、线程数、生命周期回调和 SDK 缺失门面，但原生求解与解读取
因许可证缺失尚未验收。无依赖契约覆盖能力声明、预取消、非法快照、不支持请求和无 SDK 回退。
能力声明严格限定为
`Lp`、`Mip`、`WarmStart`；对偶、Farkas、原生 IIS、解池、原生进度回调和求解中中断均未声明，
相关请求显式失败。无 SDK 时线性入口返回 `OREngineEnvironmentLost`，模型转储仍可用。

固定 Kotlin 基线的约束方向映射相反：`LessEqual` 写成 MOSEK 下界，`GreaterEqual` 写成 MOSEK
上界，会静默反转行关系。C# 保留标准语义，即小于等于使用 `boundkey.up`、大于等于使用
`boundkey.lo`，不照搬该基线缺陷。Kotlin 的环境初始化、配置和解提取本身仍是占位实现，因此本批
直接实现原生路径，而不是翻译占位符。适配器仍只有线性入口，不声明二次、对偶、Farkas、IIS、
解池、原生进度回调或求解中中断；数值跨边界转换为 `double`，报告只保留 backend claimed proof。

门禁实测：有 SDK 契约测试 6／6，无 SDK 边界 3／3；显式集成构建 0 错误，共 9 项，其中 6 项契约
通过、3 项原生用例全部因缺少许可证返回 `AuthenticationError` 和
`err_missing_license_file(1008)`，不计为通过。Release 全方案 0 错误、723 条既有警告；默认全量
4831 通过、1 项既有 Delaunay 跳过、0 失败，共 23 个有测试程序集；Hexaly 和 Benchmark 无可运行
测试。原始集成输出在忽略目录 `logs/mosek-integration.log`，许可证探测见
`logs/mosek-license-probe.log`。因此状态是“实现完成、契约通过、许可证阻塞、真实集成未验收”，
不能宣称 MOSEK 后端全面可用，P3 与全量同步仍未完成。

### Current gate: Hexaly linear, quadratic, column generation and OPM export

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core-plugin-hexaly/HexalyLinearSolver.kt`, `HexalyQuadraticSolver.kt`, `HexalySolver.kt`, `HexalyVariable.kt`, `HexalySolverCallBack.kt` | `src/Core.Plugin.Hexaly/HexalyLinearSolver.cs`, `HexalyQuadraticSolver.cs`, `HexalyNativeSolver.cs`, `HexalySnapshot.cs`, `HexalyReportSupport.cs`, `HexalySolverCallBack.cs` | `tests/Core.Plugin.Hexaly.Tests/HexalyContractTest.cs`, `HexalyUnavailableTest.cs`, `HexalyIntegrationTest.cs` | Implementation complete and contract verified for immutable linear/quadratic snapshots, repeated-coefficient accumulation, objective sense and offsets, continuous/binary/integer domains, finite warm starts, time and thread limits, status/lifecycle callbacks, cancellation, relative-gap stopping, no-improvement stopping, unified `SolveReport<Flt64>`, legacy output mapping, provenance and fingerprints. Real native acceptance is unverified because SDK 13.0.20241120-Win64 cannot open its license |
| `core-plugin-hexaly/HexalyColumnGenerationSolver.kt` | `src/Core.Plugin.Hexaly/HexalyColumnGenerationSolver.cs` | same contract and integration classes | Implemented: MILP, solution-pool request validation, LP relaxation, row-dual readback through `TriadDualSolverSupport`, mechanism-solution writeback and `toLogModel` output of both `<name>.opm` and `<name>.lp`. The native LP-dual path has not run because the license is missing |
| `core/model/mechanism/MetaModelExportSupport.kt` | `MetaModel.ExportAsync`, `MetaModelExportExtensions.ExportToOpmAsync`, `MetaModelOpmExporter` | `tests/Core.Tests/Model/Mechanism/MetaModelOpmExportTest.cs` | Contract verified: readable linear and quadratic objectives/constraints, directory-derived `<model>.opm`, UTF-8 without BOM, unfolded API, closed-model rejection and invalid-path failure. Auxiliary-token registration and exact Kotlin OPM byte identity are not claimed |
| SDK availability and unsupported surfaces | `Core.Plugin.Hexaly.csproj`, `HexalyUnavailable.cs`, `HexalyReportSupport.cs` | default SDK-path and forced no-SDK runs | The managed SDK is optional at build time. Without it, linear/quadratic/column-generation solve entries return `OREngineEnvironmentLost`, descriptors stay null and mechanism-to-triad/tetrad dumping remains available. With it, capabilities declare Lp/Mip or Qp/Qcp/Mip plus WarmStart/Callback/Interrupt; Dual/Farkas/NativeIIS/SolutionPool remain false, and multi-solution requests fail explicitly |

The installed SDK is Hexaly 13.0.20241120-Win64 at `C:\environment\solver\hexaly130`.
Its native optimizer creation fails with
`Cannot open file [C:\environment\solver\hexaly130\license.dat]`; this maps to
`OREngineEnvironmentLost`. The three opt-in native cases therefore fail rather than skip and are
recorded as environment-blocked. The adapter does not fabricate unbounded results: Hexaly
`Infeasible` remains `Unknown` with a numerical warning, while only `Inconsistent` and `Optimal`
claim a completed backend proof. Values cross the native boundary as `double`; duplicate
coefficients and reported objective bounds can round.

Gate measurements: SDK-path contract tests pass `13/13`; the forced no-SDK rebuild passes `4/4`
with no skips; OPM export tests pass `4/4`; the opt-in integration build succeeds but runs
`0/3` because all three native cases return `OREngineEnvironmentLost`. The Hexaly plugin builds
with `0 warnings / 0 errors`; Core retains its existing style warnings and gains none from OPM
export. No performance improvement is claimed.

中文说明：本批完成 Hexaly 线性、二次和列生成入口的原生实现与统一报告接入，并补齐元模型 OPM
诊断导出。默认契约 13 项通过，无 SDK 边界 4 项通过，OPM 4 项通过；显式集成 3 项全部因缺少
`license.dat` 返回 `OREngineEnvironmentLost`，只能记为“实现完成、契约通过、许可证阻塞、真实
后端未验收”。多解、对偶、Farkas、原生 IIS 和解池未声明；`toLogModel` 已验证会同时生成 OPM
与 LP。P3 与全量同步仍未完成。

### Current gate: LINGO and OPTVerse placeholder parity

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `core-plugin-lingo/LingoLinearSolver.kt` | `src/Core.Plugin.Lingo/LingoLinearSolver.cs` | `tests/Core.Plugin.Lingo.Tests/LingoTest.cs` | Aligned to the pinned baseline: the Kotlin source and README are explicit empty placeholders. C# keeps an empty class, does not implement `ILinearSolver`, and declares no model type or diagnostic capability |
| `core-plugin-optverse/OPTVerseLinearSolver.kt` | `src/Core.Plugin.Optverse/OptverseLinearSolver.cs` | `tests/Core.Plugin.Optverse.Tests/OptverseTest.cs` | Aligned to the pinned baseline: the Kotlin source and README are explicit empty placeholders. C# keeps an empty class, does not implement `ILinearSolver`, and declares no model type or diagnostic capability |

The previous C# types implemented `ILinearSolver` while every solve entry returned
`OREngineEnvironmentLost`. That permanently exposed a backend surface that the pinned Kotlin
baseline does not implement. This batch removes the fake interface, configuration, dump methods
and fixed failures instead of inventing SDK behavior outside the baseline. The contract tests
instantiate each placeholder and assert that it is not assignable to `ILinearSolver`.

This is capability parity, not backend acceptance. No SDK probe, native integration test or
capability declaration is added because there is no Kotlin capability to port. The Release solution
build has 0 errors and 723 existing warnings; the default full suite has 4839 passes, 1 pre-existing
Delaunay skip and 0 failures. Both LINGO and OPTVerse contract suites pass one case with no skips.

中文说明：固定 Kotlin 基线的 LINGO 与 OPTVerse 源码和 README 均明确为占位模块。C# 原先实现
`ILinearSolver` 但所有求解入口永久返回 `OREngineEnvironmentLost`，属于扩大了基线并不存在的
后端接口。本批移除伪求解接口、配置、转储入口和固定失败，保留与 Kotlin 一致的空类表面；契约
测试确认两个类型均不可赋值给 `ILinearSolver`。这不构成真实后端验收，不新增 SDK 探测或集成
用例，因为基线没有可移植能力。两个目标测试项目各 1 项通过、0 跳过。本批只关闭这两个固定失败
入口，P3 中其他已记录的能力差距仍按独立批次处理。

### Current gate: shared linear Benders engine

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `LinearBendersAlgorithm.kt`, `LinearBendersCuts.kt`, `BendersDecompositionSolver.kt` | `src/Framework/Solver/LinearBendersAlgorithm.cs`, `LinearBendersModels.cs`, `LinearBendersCuts.cs`, `BendersDecompositionSolver.cs` | `tests/Framework.Tests/Solver/LinearBendersAlgorithmTest.cs`, `LinearBendersCutsTest.cs` | Contract verified for minimization-only master/continuous-recourse partitioning, unique shared bindings, integer-variable fixation, theta exclusion, optimality and feasibility cut construction, independently verified merged incumbents, time/iteration/stall termination, cancellation and proof gating. Only a `Completed + Optimal` master and subproblem with a verified incumbent and converged bound gap can claim optimality; legacy adapters cannot promote output to a proof |
| Gurobi, Gurobi11, SCIP, COPT, CPLEX and MindOPT Benders adapters | `*BendersDecompositionSolver.cs` | `*BendersBudgetContractTest.cs`, `*BendersReportIntegrationTest.cs`, `Gurobi11ContractTest.cs` | Implemented: `WithTimeLimit` returns an independent budgeted copy, master report paths preserve native reports, feasible subproblems retain their unified report, and converted sub-results keep that report. Gurobi/Gurobi11/SCIP/COPT/CPLEX selected report and budget contracts pass. MindOPT report implementation is complete but its native test is blocked by `License is not valid.` and is not counted as integration acceptance |

The engine deliberately does not infer a proof from a feasible incumbent or from a legacy
`SolverOutput`. It rejects missing, duplicate or cross-model bindings, integer recourse variables
that are not fixed by the binding or native bounds, non-finite values and subproblems that do not
return a unified report. `TerminationReason.Stalled` was added for the no-new-cut stop. This batch
does not yet implement logic-based Benders, portable cut checkpoint/restore, cut signature
checkpoint migration, column projection, dynamic row/column lifecycle or cross-language wire
fixtures.

Gate measurements: the isolated Framework Benders tests pass `13/13`; Gurobi budget/report
identity tests pass `2/2`; SCIP, COPT, CPLEX and MindOPT budget contracts each pass; SCIP, COPT and
CPLEX report identity tests pass. The no-SDK Gurobi and Gurobi11 builds both have 0 warnings /
0 errors, with `6/6` and `12/12` tests. The Release solution build has 0 errors and 714 existing
warnings. The default full suite passes `4859` with `1` pre-existing Delaunay skip and `0` failures
across 24 test-bearing assemblies. MindOPT's native Benders report test remains an environment
blocker, not a pass. Raw logs are under ignored `logs/`.

中文说明：本批实现共享线性 Benders 主／子问题装配、割构造、独立合并解复核、预算传播、
过期版本和证明门控。只有主、子问题均为 `Completed + Optimal`、incumbent 已独立复核且 gap
收敛时才声明最优证明；旧后端入口和仅可行解不会被提升为最优证明。Gurobi、Gurobi11、SCIP、
COPT、CPLEX 和 MindOPT 的预算副本及统一报告入口已补齐。Framework 定向 13 项、Gurobi 身份
与预算 2 项、SCIP/COPT/CPLEX/MindOPT 预算契约均通过；MindOPT 真机报告用例被无效许可证阻断，
不计为验收。逻辑 Benders、割检查点、列投影和动态行列生命周期仍属于后续 P4 批次，P4 尚未完成。

### Current gate: shared logic-based Benders engine

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `LogicBasedBenders.kt`, `IntegerNoGoodEncoding.kt` | `src/Framework/Solver/LogicBasedBendersModels.cs`, `LogicBasedBendersEngine.cs`, `IntegerNoGoodCuts.cs` | `tests/Framework.Tests/Solver/LogicBasedBendersTest.cs` | Contract verified for binary and bounded-integer master bindings, CP fixed-value sources, feasible/infeasible/unknown subproblem classification, conflict and no-good feasibility cuts, exact integer no-good encoding, cut validation/deduplication, cancellation, progress callbacks and proof-gated reports |
| `LogicBasedBenders.kt` proof and objective contract | `BendersProofMode.Exact`, `LogicBasedBendersOptions`, `LogicBasedBendersReport.ToSolveReport()` | same test class | Exact mode requires a `Completed + Optimal` master and a verified subproblem terminal state. Every installed cut must be `Global + Verified`. Optimization subproblems additionally require a verified global-optimality cut, a complete objective evaluator, matching objective evidence and master bound convergence before optimality can be claimed |
| CP conflict evidence and master mutation boundary | `VerifiedConflictCore_ShouldProjectOnlyCoreVariables`, `UnverifiedConflictCore_ShouldFallBackToFullNoGood`, `IntegerNoGoodCutEncoder` | same test class | Verified conflict cores project only the master variables named by the core; unverified cores fall back to excluding the full master assignment. Integer no-good auxiliary keys are checked before mutation and newly installed auxiliary variables are rolled back on failure. Big-M values outside the exactly representable `Int64`/IEEE-754 integer range are rejected rather than rounded |

The engine consumes the Core CP solver/session SPI and keeps vendor SDKs outside Framework. Unknown
CP terminal states are never promoted to `Feasible` or `Optimal`. Cuts are normalized and deduplicated
by their stable signature, and the master model's structural revision invalidates stale assignments.
The current no-good encoding intentionally excludes exactly the selected assignment and does not
claim a stronger lattice cut.

Residual boundary at this batch: cut checkpoint/restore and schema migration were not implemented.
Cut insertion prevalidated auxiliary keys and rolled back newly added variables, but the master API
could not yet remove constraints installed before a later constraint in the same cut failed.
`ColumnProjection<V>`, dynamic row/column projection, remote protocol parity and cross-language wire
fixtures also remained P4 work. Batch 5 below supersedes the partial-cut-installation limitation.

Gate measurements: the targeted Framework logic-based Benders suite passes `23/23` with no skips.
The forced Release solution rebuild has `0` errors and `741` existing analyzer/style warnings; no
warning points to a Batch 2 source or test file. The default full suite passes `4882`, skips the one
pre-existing Delaunay case and fails `0` across 24 test-bearing assemblies. Raw logs are under
ignored `logs/p4-logic-benders-build.log`, `logs/p4-logic-benders-test.log` and
`logs/p4-logic-benders-targeted.log`.

中文说明：本批完成共享逻辑 Benders 的二值／有界整数主问题绑定、CP 固定值来源、子问题分类、
冲突割与 no-good 可行性割、精确整数 Big-M 编码、割校验去重、取消、进度回调及统一报告。
`Exact` 模式要求主问题 `Completed + Optimal`、子问题终态已验证、每条割均为 `Global + Verified`；
优化型子问题还必须具备全局最优割、完整目标求值器、目标一致性和主问题上下界收敛证据，未知 CP
终态不会被误报为可行或最优。已验证冲突核只投影相关主变量，未验证冲突核回退为完整赋值
no-good；辅助变量键在修改模型前预校验，安装失败会回滚新增变量。

本批定向 23 项、默认全量 4882 项通过（1 项既有 Delaunay 跳过、0 失败、24 个有测试程序集），
Release 强制重建 0 错误、741 条既有警告且本批文件零警告。割检查点／旧格式迁移尚未实现；
该批次当时仍存在部分约束残留窗口；后续 Batch 5 已通过批量约束回滚和原子割安装关闭该窗口。
`ColumnProjection<V>`、动态行列投影、远程协议和跨语言 wire fixture 在当时仍是 P4 后续工作，
P4 不得标记完成。

### Current gate: column projection and pricing lifecycle

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `ColumnProjection.kt` | `src/Framework/Solver/ColumnProjection.cs`, `ColumnProjectionModels.cs` | `tests/Framework.Tests/Solver/ColumnProjectionTest.cs` | Contract verified for explicit local-decision/block ownership, source-snapshot and column-pool revisions, exact-one master assembly, phase-one artificials, integer selection, refined resources, normalized objectives, local-row and bound validation, identity-checked shadow prices, seed and dual-backed pricing requests, stale registration/batch rejection, direct-master edit detection and solution extraction |
| `ColumnProjectionIndex.kt` | `src/Framework/Solver/ColumnProjectionIndex.cs` | `tests/Framework.Tests/Solver/ColumnProjectionIndexTest.cs` | Contract verified for decision-order-preserving sparse term capture, row/block grouping, wholly-local row classification, source-map capture at the refresh boundary, thread-safe lazy term indexes and refined-resource projection |
| `ColumnProjection.kt` pricing batch boundary | `ColumnPricingBatch<V>`, `ColumnPricingEntry<V>`, `PreparePricingBatchAsync` | both test classes | A batch shares one source revision and preserves requested block order. Every request and local model must be revalidated before asynchronous candidates are accepted; source or master edits invalidate the batch. Batch disposal closes all owned pricing models |

The projection never maintains a second model definition. Master rows and the objective are rebuilt from
the source snapshot, while only decisions explicitly mapped to a block are folded into projected
coefficients. A row is a local pricing hard constraint only when every coefficient belongs to one mapped
block; rows with refined resources are excluded from that check and represented by their original
variables. Shadow prices are matched by actual constraint identity, so an identically named rebuilt
master or a foreign projection cannot reuse a certificate. Maximum objectives are normalized once by
the source snapshot, including `FltX` extended-precision values.

Residual boundary at this batch: `ColumnGenerationBatch` and the complete dynamic row/column lifecycle
were not yet implemented. Cut checkpoint/restore and schema migration, transactional dynamic rows,
remote capability negotiation/task scheduling/stop-resume, and cross-language wire fixtures remained
P4 work. Batch 5 below supersedes the missing `ColumnGenerationBatch`; this batch does not claim a
complete column-generation algorithm, a pricing oracle, joint Benders/column generation, or P4
completion.

Gate measurements: the targeted column-projection suites pass `9/9` with no skips. The forced Release
solution rebuild has `0` errors and `741` existing analyzer/style warnings; no warning points to a
Batch 3 source or test file. The default full suite passes `4891`, skips the one pre-existing Delaunay
case and fails `0` across 24 test-bearing assemblies. Raw logs are under ignored
`logs/p4-column-projection-build.log`, `logs/p4-column-projection-targeted.log` and
`logs/p4-column-projection-test.log`.

中文说明：本批完成共享 `ColumnProjection<V>` 与 `ColumnProjectionIndex<V>`。列决策和配置块必须显式映射，
主问题行与目标只从同一源模型快照重建；稀疏索引在刷新边界捕获源 Map，并保持决策顺序延迟分组。
主问题支持恰选一列、人工变量阶段、整数选择、资源独立补全、局部行与变量范围复核；目标方向在快照层
统一归一化，`FltX` 扩展精度不会经过 `Flt64` 中转。对偶按实际约束身份绑定，同名重建主问题或外部
投影不能复用证书；定价批次共享同源版本，异步候选接收前必须重新校验，列池或主问题直接编辑会使旧批次
失效。定向 9 项、默认全量 4891 项通过（1 项既有 Delaunay 跳过、0 失败、24 个有测试程序集），
Release 强制重建 0 错误、741 条既有警告且本批文件零警告。

该批次剩余边界：`ColumnGenerationBatch` 与完整动态行／列生命周期当时尚未实现；割检查点／旧格式
迁移、事务式动态行、远程能力探测／任务调度／停止恢复、跨语言 wire fixture 仍未完成。后续 Batch 5
已补齐 `ColumnGenerationBatch`；本批不宣称完整列生成算法、定价 oracle、Benders 与列生成联合算法
或 P4 完成。

### Current gate: Benders checkpoint, codec and resume

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `BendersCheckpoint.kt` | `src/Framework/Solver/BendersCheckpoint.cs` | `tests/Framework.Tests/Solver/BendersCheckpointTest.cs` | Implementation complete and contract verified for the standalone schema 1.0 document, legacy schema 1.0 migration, canonical JSON and integrity digest, the Core CP envelope schema 3.0, `BendersCutSerializationSpi`, capture/encode/decode/restore, versioned trace encoding, cut-ID/schema/duplicate validation and structured `Result` failures |
| `LogicBasedBenders.kt` resume boundary | `src/Framework/Solver/LogicBasedBendersEngine.cs`, `LogicBasedBendersModels.cs`, `BendersCheckpoint.cs` | same test class | Contract verified: restored cuts are independently decoded through the domain SPI, Exact mode accepts only `Global + Verified` cuts, the subproblem SHA-256 fingerprint must match the supplied CP snapshot, unknown assumptions/fixed bindings/conflicts are rejected, persisted master state requires an applier, and adapter evidence must match the checkpoint before the master is solved. A historical convergence marker is retained for audit but never promoted to proof |
| Core portable CP checkpoint contract | `src/Core/Solver/ConstraintProgramming/ConstraintProgrammingCheckpoint.cs` | same test class | Contract verified: Benders state can be captured into the schema 3.0 CP envelope with the CP snapshot as the subproblem identity. The standalone Benders document remains schema 1.0 and is deliberately not conflated with the Core envelope version |

The codec stores only portable, independently revalidatable state; backend handles are not serialized. The
engine resumes at the checkpoint's next iteration and reapplies current proof-mode gates after decoding.
Trace entries use the `v1:` prefix and canonical JSON, with `elapsedNanos = TimeSpan.Ticks * 100`; unknown
enums, negative fields, non-increasing or out-of-range iterations and proof-mode mismatches fail explicitly.

Cross-language evidence is limited to the Benders checkpoint fixture in this batch. Its canonical JSON
decodes and re-encodes byte-for-byte in C# and has integrity digest
`a73ccbf538651b356e3e01da979169e3a852ead6fa04f1b28986568fa9824472`. A temporary probe against the pinned
Kotlin tree passed `1/1` for both `decode` and exact `encode`; the probe was removed afterward and the
Kotlin worktree was clean. This does not claim that the complete remote-protocol wire matrix is verified.

Gate measurements: targeted Benders tests pass `38/38` with no skips; the complete Framework suite passes
`383/383`; the forced Release solution rebuild has `0` errors and `741` existing analyzer/style warnings,
with no warning in this batch's source or test files. The default full suite passes `4906`, skips the one
pre-existing Delaunay case and fails `0` across 24 test-bearing assemblies. Raw logs are under ignored
`logs/p4-benders-checkpoint-rebuild.log`, `logs/p4-benders-checkpoint-test.log` and
`logs/p4-benders-checkpoint-framework-test.log`.

Residual boundary at this batch: the master API could still leave earlier constraints from one dynamic
cut installed when a later constraint in the same cut failed. Transactional dynamic row/column
lifecycle, `ColumnGenerationBatch`, remote capability negotiation/task scheduling/stop-resume and the
complete cross-language wire fixture matrix remained P4 work. Batch 5 below closes the partial-cut
installation and `ColumnGenerationBatch` gaps; this batch does not claim P4 or full synchronization
completion.

中文说明：本批完成独立 Benders checkpoint schema 1.0、旧 schema 1.0 迁移、Core CP envelope schema 3.0、
领域割 SPI、捕获／编码／解码／恢复、版本化 trace、割身份校验和主问题恢复证据门控。恢复时所有割必须
重新经 SPI 解码；`Exact` 模式只接受 `Global + Verified` 割，子问题 SHA-256 指纹、assumptions、
fixed bindings、conflicts 和主问题适配器证据必须与 checkpoint 一致。存在主问题持久化状态却没有
applier 时会在求解前失败；历史收敛标记只用于审计，不能直接成为证明。

定向 Benders 38 项、Framework 全量 383 项和默认全量 4906 项均通过（1 项既有 Delaunay 跳过、0 失败、
24 个有测试程序集）；Release 强制重建 0 错误、741 条既有警告，本批文件零警告。Kotlin 规范 fixture
已完成 C# 双向编解码和逐字重编码，并临时在 Kotlin 侧验证 `decode`／`encode` 各 1 项通过；这只覆盖
Benders checkpoint fixture，不代表完整远程 wire matrix。该批次当时仍待补齐事务式动态行／列和
`ColumnGenerationBatch`；后续 Batch 5 已关闭这两项中的批次证据与原子割安装部分，完整动态行／列
生命周期、远程能力探测／调度／停止恢复和完整跨语言 fixture 矩阵仍未完成，P4 与全量同步不得标记
完成。

### Current gate: column-generation batches and atomic cut installation

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `ColumnGenerationBatch.kt` | `src/Framework/Solver/ColumnGenerationBatch.cs` | `tests/Framework.Tests/Solver/ColumnGenerationBatchTest.cs` | Contract verified for `ColumnGenerationCompletion`, value-equal `ColumnGenerationScope` with set semantics for executor slots, immutable candidate/child collections, exact-scope exhaustion certification, recursive child coverage, and explicit no-improving-column certification |
| `MetaModel.kt` constraint rollback | `LinearMetaModel.RollbackConstraintsTo`, `QuadraticMetaModel.RollbackConstraintsTo` | `tests/Core.Tests/Model/Mechanism/MetaModelTest.cs` | Contract verified for invalid rollback positions, tail constraint removal, completed and active constraint-group index trimming, and removal of groups whose range no longer intersects the retained prefix |
| `LogicBasedBenders.kt` cut installation | `LogicBasedBendersEngine.AddCuts` | `tests/Framework.Tests/Solver/LogicBasedBendersTest.cs` | Contract verified for atomic multi-cut installation: the original constraint count is captured before mutation, a later writer failure removes all constraints and auxiliary variables installed by the batch, and accepted/known cut lists are published only after the complete batch succeeds |

An exhausted empty batch certifies only its exact complete scope; it does not by itself prove that no
improving column exists. `CertifiesNoImprovingColumns` requires explicit `NoImprovingColumns = true`,
exhaustion, exact scope identity, non-blank request/model/dual/branch identities, and recursively
certified children. Child requests must equal the parent request or use the parent prefix followed by
`:`, while child group and executor-slot unions must exactly equal the parent domain. An aggregate
certificate therefore cannot hide a budget-limited, interrupted, heuristic, foreign, or incomplete
child result. Candidate columns do not override a separately supplied no-improving-column proof.

`RollbackConstraintsTo` restores both the retained constraint prefix and the constraint-group index
map. `AddCuts` records the pre-installation constraint count and uses this rollback on any later
variable or constraint failure. `knownCuts` and the report's accepted-cut list are updated only after
every constraint in every cut has been installed. The atomic-install test injects a failure on the
second cut and verifies that the model constraints, solver tokens and published cut lists are unchanged.

Gate measurements: the new contract suites pass `9/9` (`8` Framework plus `1` Core). The complete
Framework suite passes `391/391`; the default full suite passes `4915`, skips the one pre-existing
Delaunay case and fails `0` across 24 test-bearing assemblies. The forced Release solution rebuild has
`0` errors and `741` existing analyzer/style warnings, with no warning in a Batch 5 source or test file.
Raw logs are under ignored `logs/p4-column-batch-rebuild.log`,
`logs/p4-column-batch-framework-test.log` and `logs/p4-column-batch-test.log`.

Residual boundary: complete dynamic row/column lifecycle work remains, including transactional
objective-term removal and the remaining add/remove-column orchestration. Remote capability
negotiation/task scheduling/stop-resume and the complete cross-language wire fixture matrix also remain
P4 work. This batch does not claim a complete column-generation algorithm, a pricing oracle, joint
Benders/column generation, P4 completion, or full synchronization completion.

中文说明：本批完成 `ColumnGenerationBatch<C>`、`ColumnGenerationCompletion` 和
`ColumnGenerationScope`。作用域按值比较，执行器时隙使用集合语义；空列集合只有在
`Exhausted + global + 精确作用域` 下才认证完整搜索，不能自动证明无改进列。
`CertifiesNoImprovingColumns` 还要求显式 `NoImprovingColumns = true`，并递归验证全部子请求；
子请求必须绑定同一模型、对偶和分支，其执行器分组与时隙并集必须精确覆盖父作用域。父级证据不能
掩盖预算受限、中断、启发式、外部作用域或未完成的子结果；候选列也不能覆盖独立提供的无改进证明。

`LinearMetaModel` 与 `QuadraticMetaModel` 新增 `RollbackConstraintsTo`，同时恢复约束前缀和约束组
索引，包括活动约束组的低界。`LogicBasedBendersEngine.AddCuts` 在安装前记录约束数量，任一后续变量
或约束失败时回滚本批新增约束与辅助变量；`knownCuts` 和报告已接受割列表只在整个批次成功后发布。
测试在第二条割安装时注入失败，确认模型约束、求解器 token 和对外割列表均保持原状。

本批新增契约测试 `9/9` 通过（Framework 8 项、Core 1 项）；Framework 全量 `391/391`；默认全量
`4915` 项通过，仅跳过 1 项既有 Delaunay 用例，0 失败，覆盖 24 个有测试程序集；Release 强制重建
0 错误、741 条既有警告，本批源文件与测试文件零警告。完整动态行／列生命周期中的目标项事务式移除、
远程能力探测／调度／停止恢复和完整跨语言 wire fixture 矩阵仍未完成，P4 与全量同步不得标记完成。

### Current gate: dynamic write transactions and row/column lifecycle

| Pinned Kotlin source | C# entry point | Evidence | Status |
|---|---|---|---|
| `MetaModel.kt` | `src/Core/Model/Mechanism/MetaModel.cs` | `tests/Core.Tests/Model/Mechanism/MetaModelWriteTransactionTest.cs`, `MetaModelTest.cs` | Contract verified for `IMetaModelWriteTransaction`, opt-in `BeginWrite()` on `IMetaModel<V>` implemented by `LinearMetaModel<V>`/`QuadraticMetaModel<V>`, rejected reentry and closed models, automatic full recovery after a failed commit, `Failed` for a single rollback error and `Fatal` for several, and single-use transactions. Constraint, objective-term and constraint-group removal entry points are covered |
| `LinearModelState.kt` | `src/Core/Model/Mechanism/LinearModelState.cs` | `tests/Core.Tests/Model/Mechanism/LinearModelStateTest.cs`, same test class | Contract verified for immutable revisions/snapshots/rows, validated `RegisterRow`, idempotent `RemoveRow` (unknown rows succeed, closed models return `ApplicationStopped`), write-state capture/restore, and re-registration of an identity freed by row removal |
| `ModelElementIdentityRegistry.kt` | `src/Core/Solver/Report/ModelElementIdentityRegistry.cs` | same test classes | Contract verified for internal `CaptureState`/`RestoreState`, internal `Release(object)` that unbinds the element and drops its ID-index entries, stable `Entries`/`Validate` ordering, and atomic resolution |
| `ColumnProjection.kt`, `ColumnProjectionIndex.kt` | `src/Framework/Solver/ColumnProjection.cs` | `tests/Framework.Tests/Solver/ColumnProjectionTest.cs` | Contract verified for the read-only column pool, replace/add/remove orchestration behind the lifecycle gate, master registration built with `PendingSequence()` before `PublishColumns`, and structured failures for null or throwing column enumerables that leave the pool and the active registration untouched |

A transaction snapshots the token-table transaction, constraints, constraint groups, objectives,
derived row state and the identity registry. It does not freeze external mutable polynomials or
function side effects. `Commit` restores that complete state when it fails, merges a failed restore
into a `Fatal` result, and ends the transaction either way; `Rollback` reports `Failed` for one error
and `Fatal` for several. `BeginWrite` rejects reentry and closed models, while the default
`IMetaModel<V>` members keep returning an "unsupported" failure for models without the lifecycle.

Two fixes are recorded by this gate. The void `RegisterConstraintGroup` now mirrors the checked
`PrepareConstraintGroup` path and ignores non-contiguous reuse instead of overwriting the earlier
range. `RegisterRow` no longer consults the latest snapshot when deciding whether an ID is taken, so a
removed row frees its identity, and `ReleaseConstraintIdentity` unbinds the removed constraint from
the registry so the same ID can be registered again. In `ColumnProjection`, a candidate master is
built with the sequence the publish will produce, so a construction failure keeps the previous pool,
registration and duals.

Gate measurements: the targeted Core run passes `37/37` and the targeted Framework run over
`ColumnProjection|LogicBasedBenders|BendersCheckpoint|ColumnGenerationBatch` passes `60/60`, both with
no failures. The forced Release rebuild reports `0 errors / 741 existing warnings` with zero warnings in
the files touched by this batch, and the default full suite reports `4944 passed / 0 failed / 1 pre-existing
Delaunay skip` across 24 assemblies that contain tests (`Benchmark.Tests` contributes none). Raw logs are
under the ignored files `logs/p4-batch6-followup-rebuild.log` and `logs/p4-batch6-followup-test.log`.

Residual boundary: concurrent or reentrant model mutation remains unsupported; a model whose restore
failed has no defined continued use, and a closed model's token table is already disposed, so full
token restoration is not claimed. Master publication is still two steps (build, then publish); the
failure window is smaller but not strictly atomic, and `Snapshot`, `Pricing` and `RefreshShadowPrice`
do not acquire the lifecycle gate, so the projection is not claimed thread-safe as a whole. The remote
protocol under `src/Framework/Solver/Remote/` remains P4 work; this gate does not enter P5, does not
complete P4 and does not claim full synchronization.

中文说明：本批在 Core 与 Framework 引入动态写事务与行列生命周期。`IMetaModelWriteTransaction` 捕获
token 表事务、约束、约束组、目标、动态行状态和身份注册表；`Commit` 失败自动完整恢复，提交与恢复
都失败合并为 `Fatal`，`Rollback` 单一错误返回 `Failed`、多错误返回 `Fatal`，事务结束后不可复用；
`BeginWrite` 拒绝重入与关闭模型，未实现该生命周期的模型沿用接口默认的“不支持”失败。约束删除、
目标项删除、约束回滚、动态行删除与身份解除均为结构化入口，删除行后同一行 ID 可重新注册。

两处修复一并关闭：void 入口 `RegisterConstraintGroup` 与受检路径一致，非连续复用直接忽略而不覆盖
既有范围；`RegisterRow` 不再用最新快照拒绝已删除的 ID，删除行因此释放身份。`ColumnProjection`
改为先按即将发布的序号构造主问题注册，再发布列池，构造失败时旧列池、旧注册与旧对偶保持不变；
列集合枚举统一返回结构化失败，不逃逸异常。

定向验证：Core `37 passed / 0 failed`；Framework 过滤
`ColumnProjection|LogicBasedBenders|BendersCheckpoint|ColumnGenerationBatch` 为
`60 passed / 0 failed`。Release 全方案强制重建 `0 errors / 741 既有 warnings`，本批改动文件零警告；
默认全方案 `4944 passed / 0 failed / 1 项既有 Delaunay skip`（24 个有测试程序集）。限制：
不支持并发或重入修改；恢复失败后模型能否继续使用未定义；关闭模型的 token 表已释放，不宣称完整
token 恢复；主问题发布仍是两步，非严格原子；`Snapshot`／`Pricing`／`RefreshShadowPrice` 未统一
获取生命周期闸，不宣称整个投影线程安全。远程协议仍待同步，P4 与全量同步不得标记完成。

#### Batch 6 API migration

| Breaking change | Impact | Migration |
|---|---|---|
| `AbstractMetaModel<V>.CaptureConstraintState`/`RestoreConstraintState`/`CaptureWriteState`/`RestoreWriteState` are `private protected abstract` | Only subclasses in this assembly can override them, so an external assembly can no longer derive a usable `AbstractMetaModel<V>` | Use `LinearMetaModel<V>`/`QuadraticMetaModel<V>`, or compose a model instead of subclassing it across assemblies |
| `IMetaModel<V>.RegisterConstraintGroup` semantics tightened: non-contiguous re-registration is ignored | Previously a reused group silently overwrote its recorded range; code relying on that now keeps the original range, and later constraints under the group may be rejected | Close the group first (`IndicesOfConstraintGroup`/`ConstraintsOfGroup`), append constraints contiguously, or register a new group object |
| `LinearModelState<V>.RegisterRow` no longer rejects IDs found in the latest snapshot | A removed row frees its ID, and re-registering that ID creates a new row under a new revision | Refresh the snapshot after removal or re-registration, and do not reuse stale revisions or row objects |
| `IMetaModel<V>` gained default members (`RemoveConstraint`, `RollbackConstraintsTo`, `RemoveObjectiveTerms`, `BeginWrite`) | Third-party implementations still compile, but the default returns an "unsupported" failure | Check the returned `Result`; only the in-repository mechanism models support these operations |

迁移说明：外部程序集若要继续自定义元模型，应改为组合内置模型或在本程序集内继承；`ConstraintId`
不再是一经使用即永占的键——删除行或约束后才可复用，因此下游必须以新快照与新版本为准，不能沿用
旧注册、旧对偶或旧定价批次。

### Current gate: remote protocol wire contract, scheduling and cross-language round-trip

命令（均在 `D:\workspace\ospf\ospf-csharp` 执行，Release）：

```powershell
dotnet build ospf-csharp.sln -c Release --no-restore -t:Rebuild --nologo
dotnet test  ospf-csharp.sln -c Release --no-build --no-restore
```

实测结果（**每次收口后在此更新**；上次更新 Batch 1328）：

| Gate | Result |
|---|---|
| Release 全方案强制重建 | `0 errors`，`1316` 行既有警告（= **658** 条，每条形如两行；B1028 实测，B588 时是 `1434` 行，此后逐步下降，因为空壳文件的 `IDE0022` 随实现消失） |
| 默认全方案测试 | `11472 passed` / `1` 项既有 Delaunay skip / `0 failed`（29 个有测试程序集） |
| 全方案测试（开启 SCIP 集成，`-p:RunScipIntegration=true`） | `11636 passed` / `1` 项既有 Delaunay skip / `0 failed`（多 123 项**真实 SCIP 求解**） |
| `Framework.Csp2d` 进度 | **18 / 18 个主源码文件（全部完成）**：`Ids2`（447）、`Material2`（448）、`Product2`、`Csp2dErrors`（449）、`CuttingProcess2`（450）、`Region2`（451）、`CutTree2`（452）、`Pattern2`（453—454）、`RenderDTO`（455）、`MasterProblem`（456—457）、`Waste`·`Yield`（458）、`Csp2dProblem`（459）、`SheetAssignment`（460）、`PatternGeneration`（461）、`ShelfPatternGenerator2`（462）、`PatternGenerationContext2`（463）、装配与恢复（464）、`Csp2dMilp`·`Csp2dColumnGeneration`（465）。真实 SCIP 端到端已实测（466）、模块 README 已补（467） |
| `Framework.Bpp1d` 进度 | 17 个文件：`PackingProblem`（414）、`PackingContext.Pack`（415）、`PackingKpi`（416）、`PatternGenerationContext`（417）、`IPackingValueAdapter`（418）、`LoadingGeometry`（419）、`DiscreteLoadingValidator`（420）、`DiscreteLoadingVerify`（421）、`ConflictIndex`（422）、`LoadingAggregation`（423）、`LoadingLimits`（423）、`DiscreteLoadingContext`（424）、`LinkAdditionalWeight`（425）、`Bpp1dDiscreteLoading`（425）、`PatternAssignmentPricing`（426）、`PatternAssignmentModelling`（427）、`PatternAssignmentRemoval`（428）、`Bpp1dMilp`（429）、`Bpp1dColumnGeneration`（430）、`TailBinOptimizer`（431）、`Bpp1dCsvDatasetLoader` 与 `PackingRendererAdapter`（432）、`DiscreteLoadingApplicationService`（433） |
| `Framework.Bpp1d` 进度 | 16 个文件：`PackingProblem`（414）、`PackingContext.Pack`（415）、`PackingKpi`（416）、`PatternGenerationContext`（417）、`IPackingValueAdapter`（418）、`LoadingGeometry`（419）、`DiscreteLoadingValidator`（420）、`DiscreteLoadingVerify`（421）、`ConflictIndex`（422）、`LoadingAggregation`（423）、`LoadingLimits`（423）、`DiscreteLoadingContext`（424）、`LinkAdditionalWeight`（425）、`Bpp1dDiscreteLoading`（425）、`PatternAssignmentPricing`（426）、`PatternAssignmentModelling`（427） |
| `Framework.Bpp1d` 进度 | 15 个文件：`PackingProblem`（414）、`PackingContext.Pack`（415）、`PackingKpi`（416）、`PatternGenerationContext`（417）、`IPackingValueAdapter`（418）、`LoadingGeometry`（419）、`DiscreteLoadingValidator`（420）、`DiscreteLoadingVerify`（421）、`ConflictIndex`（422）、`LoadingAggregation`（423）、`LoadingLimits`（423）、`DiscreteLoadingContext`（424）、`LinkAdditionalWeight`（425）、`Bpp1dDiscreteLoading`（425）、`PatternAssignmentPricing`（426） |
| `Framework.Bpp1d` 进度 | 14 个文件：`PackingProblem`（414）、`PackingContext.Pack`（415）、`PackingKpi`（416）、`PatternGenerationContext`（417）、`IPackingValueAdapter`（418）、`LoadingGeometry`（419）、`DiscreteLoadingValidator`（420）、`DiscreteLoadingVerify`（421）、`ConflictIndex`（422）、`LoadingAggregation`（423）、`LoadingLimits`（423）、`DiscreteLoadingContext`（424）、`LinkAdditionalWeight`（425）、`Bpp1dDiscreteLoading`（425） |
| `Framework.Bpp1d` 进度 | 12 个文件：`PackingProblem`（414）、`PackingContext.Pack`（415）、`PackingKpi`（416）、`PatternGenerationContext`（417）、`IPackingValueAdapter`（418）、`LoadingGeometry`（419）、`DiscreteLoadingValidator`（420）、`DiscreteLoadingVerify`（421）、`ConflictIndex`（422）、`LoadingAggregation`（423）、`LoadingLimits`（423）、`DiscreteLoadingContext`（424） |
| `Framework.Bpp1d` 进度 | 11 个文件：`PackingProblem`（414）、`PackingContext.Pack`（415）、`PackingKpi`（416）、`PatternGenerationContext`（417）、`IPackingValueAdapter`（418）、`LoadingGeometry`（419）、`DiscreteLoadingValidator`（420）、`DiscreteLoadingVerify`（421）、`ConflictIndex`（422）、`LoadingAggregation`（423）、`LoadingLimits`（423） |
| `Framework.Bpp1d` 进度 | 9 个文件：`PackingProblem`（414）、`PackingContext.Pack`（415）、`PackingKpi`（416）、`PatternGenerationContext`（417）、`IPackingValueAdapter`（418）、`LoadingGeometry`（419）、`DiscreteLoadingValidator`（420）、`DiscreteLoadingVerify`（421）、`ConflictIndex`（422） |
| `Framework.Bpp1d` 进度 | 8 个文件：`PackingProblem`（414）、`PackingContext.Pack`（415）、`PackingKpi`（416）、`PatternGenerationContext`（417）、`IPackingValueAdapter`（418）、`LoadingGeometry`（419）、`DiscreteLoadingValidator`（420）、`DiscreteLoadingVerify`（421） |
| `Framework.Bpp1d` 进度 | 7 个文件：`PackingProblem`（414）、`PackingContext.Pack`（415）、`PackingKpi`（416）、`PatternGenerationContext`（417）、`IPackingValueAdapter`（418）、`LoadingGeometry`（419）、`DiscreteLoadingValidator`（420） |
| `Framework.Bpp1d` 进度 | 6 个文件：`PackingProblem`（414）、`PackingContext.Pack`（415）、`PackingKpi`（416）、`PatternGenerationContext`（417）、`IPackingValueAdapter`（418）、`LoadingGeometry`（419） |
| `Framework.Bpp1d` 进度 | 5 个文件：`PackingProblem`（414）、`PackingContext.Pack`（415）、`PackingKpi`（416）、`PatternGenerationContext`（417）、`IPackingValueAdapter`（418，来自基线 `bpp` 模块） |
| `Framework.Bpp1d` 进度 | 4 / 10 文件：`PackingProblem`（414）、`PackingContext.Pack`（415）、`PackingKpi`（416）、`PatternGenerationContext`（417） |
| `Framework.Bpp1d` 进度 | 3 / 10 文件：`PackingProblem`（414）、`PackingContext.Pack`（415）、`PackingKpi`（416） |
| `Framework.Bpp1d` 进度 | 领域模型层（B414）+ 物理结果提取 `PackingContext.Pack`（B415）；剩余 8 个文件为应用层/服务层 |
| 新建 `Framework.Bpp1d` | 基线 10 文件约 1391 行、C# 侧原本一行都没有；B414 落地领域模型层（`PackingProblem.cs`，8 项判据） |
| 跨语言互通 | **两侧都真跑**：Kotlin `mvn -o … -Dtest=ElasticShapeParityTest` 与 C# `CrossLanguageElasticParityTests` 断言同一组数字（变量 4 / 约束 3 / 目标 3 / 变量名逐字相同），B413 |
| 真实求解器产出的非零对偶 | `ScipShadowPriceIntegrationTests` 实测需求约束对偶 `0.020`，穿过 `Csp1dShadowPriceLifecycle` 映射后仍非零（B412，关闭 B367） |
| 框架层真实求解 | `ScipColumnGenerationIntegrationTests` 用 `ScipColumnGenerationSolver` 跑通 `Csp1dColumnGeneration` 并收敛（B409） |
| `Framework.Tests` | `684 passed`（远程 wire 契约 165 项） |
| Kotlin 侧探针（`mvn -o -pl ospf-kotlin-core test -Dtest=TmpDigestProbe`） | `1 test / 0 failures`；配置指纹与 C# 逐字一致，跑完已删除 |
| `Core.Tests` | `2584 passed`（Batch 6 新增 4 项） |
| Kotlin 侧探针（`mvn -o -pl ospf-kotlin-framework test -Dtest=TmpCrossLangProbe`） | `2 passed`；跑完已删除，Kotlin 工作区 `git status --short` 为空 |
| Kotlin 侧探针（`-Dtest=TmpSubmitProbe`） | `1 test / 0 failures`；严格解码 C# 产出的提交请求，跑完已删除 |

原始日志：`logs/p4-batch8-*.log`、`logs/p4-batch9-*.log`、`logs/p4-batch10-*.log`、
`logs/p4-batch11-*.log`、`logs/p4-batch12-*.log`、`logs/p4-batch13-*.log`、
`logs/p4-batch14-*.log`、`logs/p4-batch15-*.log`、`logs/p4-batch16-*.log`、
`logs/p4-batch17-*.log`、`logs/p4-batch18-*.log`、`logs/p4-batch19-*.log`、
`logs/p4-batch20-*.log`、`logs/p4-batch21-*.log`、`logs/p4-batch22-*.log`、`logs/p4-batch23-*.log`、`logs/p4-batch24-*.log`、`logs/p4-batch25-*.log`、`logs/p4-batch26-*.log`、`logs/p4-batch27-*.log`、`logs/p4-batch28-*.log`、`logs/p4-batch29-*.log`、`logs/p4-batch30-*.log`、`logs/p4-batch31-*.log`、`logs/p4-batch32-*.log`、`logs/p4-batch33-*.log`、`logs/p4-batch34-*.log`、`logs/p4-batch35-*.log`、`logs/p4-batch36-*.log`、`logs/p4-batch37-*.log`、`logs/p4-batch38-*.log`、`logs/p4-batch39-*.log`、`logs/p4-batch40-*.log`、`logs/p4-batch41-*.log`、`logs/p4-batch42-*.log`、`logs/p4-batch43-*.log`、`logs/p4-batch44-*.log`、`logs/p4-batch45-*.log`、`logs/p4-batch46-*.log`、`logs/p4-batch47-*.log`、`logs/p4-batch48-*.log`、`logs/p4-batch49-*.log`、`logs/p4-batch50-*.log`、`logs/p4-batch51-*.log`、`logs/p5-batch55-*.log`、`logs/p5-batch56-*.log`、
`logs/p5-batch57-*.log`、`logs/p5-batch58-*.log`、`logs/p5-batch59-*.log`、`logs/p5-batch60-*.log`、
`logs/p5-batch61-*.log`、`logs/p5-batch62-*.log`、`logs/p5-batch63-*.log`、`logs/p5-batch64-*.log`、
`logs/p5-batch65-*.log`、`logs/p6-batch66-*.log`、`logs/p6-batch67-*.log`、`logs/p6-batch68-*.log`、`logs/p6-batch69-*.log`、`logs/p6-batch70-*.log`、`logs/p6-batch71-*.log`、`logs/p6-batch72-*.log`、`logs/p6-batch73-*.log`、`logs/p6-batch74-*.log`、`logs/p6-batch75-*.log`、`logs/p6-batch76-*.log`、`logs/p6-batch77-*.log`、`logs/p6-batch78-*.log`、`logs/p6-batch79-*.log`、`logs/p6-batch80-*.log`、`logs/p6-batch81-*.log`、`logs/p6-batch82-*.log`、`logs/p6-batch83-*.log`、`logs/p6-batch84-*.log`、`logs/p6-batch85-*.log`、`logs/p6-batch86-*.log`、`logs/p6-batch87-*.log`、`logs/p6-batch88-*.log`、`logs/p6-batch89-*.log`、`logs/p6-batch90-*.log`、`logs/p6-batch91-*.log`、`logs/p6-batch92-*.log`、`logs/p6-batch93-*.log`、`logs/p6-batch94-*.log`、`logs/p6-batch95-*.log`、`logs/p6-batch96-*.log`、`logs/p6-batch97-*.log`、`logs/p6-batch98-*.log`、`logs/p6-batch99-*.log`、`logs/p7-batch100-*.log`、`logs/p7-batch101-*.log`、`logs/p7-batch102-*.log`、`logs/p7-batch103-*.log`、`logs/p7-batch104-*.log`、`logs/p7-batch105-*.log`、`logs/p7-batch106-*.log`、`logs/p7-batch107-*.log`、`logs/p7-batch108-*.log`、`logs/p7-batch109-*.log`、`logs/p7-batch110-*.log`、`logs/p7-batch111-*.log`、`logs/p7-batch112-*.log`、`logs/p7-batch113-*.log`、`logs/p7-batch114-*.log`、`logs/p7-batch115-*.log`、`logs/p7-batch116-*.log`、`logs/p7-batch117-*.log`、`logs/p7-batch118-*.log`、`logs/p7-batch119-*.log`、`logs/p7-batch120-*.log`、`logs/p7-batch121-*.log`、`logs/p7-batch122-*.log`、`logs/p7-batch123-*.log`、`logs/p7-batch124-*.log`、`logs/p7-batch125-*.log`、`logs/p7-batch126-*.log`、`logs/p7-batch127-*.log`、`logs/p7-batch128-*.log`、`logs/p7-batch129-*.log`、`logs/p7-batch130-*.log`、`logs/p7-batch131-*.log`、`logs/p7-batch132-*.log`、`logs/p7-batch133-*.log`、`logs/p7-batch134-*.log`、`logs/p7-batch135-*.log`、`logs/p7-batch136-*.log`、`logs/p7-batch137-*.log`、`logs/p7-batch138-*.log`、`logs/p7-batch139-*.log`、`logs/p7-batch140-*.log`、`logs/p7-batch141-*.log`、`logs/p7-batch142-*.log`、`logs/p7-batch143-*.log`、`logs/p7-batch144-*.log`、`logs/p7-batch145-*.log`、`logs/p7-batch146-*.log`、`logs/p7-batch147-*.log`、`logs/p7-batch148-*.log`、`logs/p7-batch149-*.log`、`logs/p7-batch150-*.log`、`logs/p7-batch151-*.log`、`logs/p8-batch152-*.log`、`logs/p8-batch153-*.log`、`logs/p8-batch154-*.log`、`logs/p8-batch155-*.log`、`logs/p8-batch156-*.log`、`logs/p8-batch157-*.log`、`logs/p8-batch158-*.log`、`logs/p8-batch159-*.log`、`logs/p8-batch160-*.log`、`logs/p8-batch161-*.log`、`logs/p8-batch162-*.log`、`logs/p8-batch163-*.log`、`logs/p8-batch164-*.log`、`logs/p8-batch165-*.log`、`logs/p8-batch166-*.log`、`logs/p8-batch167-*.log`、`logs/p8-batch168-*.log`、`logs/p8-batch169-*.log`、`logs/p8-batch170-*.log`、`logs/p8-batch171-*.log`、`logs/p8-batch172-*.log`、`logs/p8-batch173-*.log`、`logs/p8-batch174-*.log`、`logs/p8-batch175-*.log`、`logs/p8-batch176-*.log`、`logs/p8-batch177-*.log`、`logs/p8-batch178-*.log`、`logs/p8-batch179-*.log`、`logs/p8-batch180-*.log`、`logs/p8-batch181-*.log`、`logs/p8-batch182-*.log`、`logs/p8-batch183-*.log`、`logs/p8-batch184-*.log`、`logs/p8-batch185-*.log`、`logs/p8-batch186-*.log`、`logs/p8-batch187-*.log`、`logs/p8-batch188-*.log`、`logs/p8-batch189-*.log`、`logs/p8-batch190-*.log`、`logs/p8-batch191-*.log`、`logs/p8-batch192-*.log`、`logs/p8-batch193-*.log`、`logs/p8-batch194-*.log`、`logs/p8-batch195-*.log`、`logs/p8-batch196-*.log`、`logs/p8-batch197-*.log`、`logs/p8-batch198-*.log`、`logs/p8-batch199-*.log`、`logs/p8-batch200-*.log`、`logs/p8-batch201-*.log`、`logs/p8-batch202-*.log`、`logs/p8-batch203-*.log`、`logs/p8-batch204-*.log`、`logs/p8-batch205-*.log`、`logs/p8-batch206-*.log`、`logs/p8-batch207-*.log`、`logs/p8-batch208-*.log`、`logs/p8-batch209-*.log`、`logs/p8-batch210-*.log`、`logs/p8-batch211-*.log`、`logs/p8-batch212-*.log`、`logs/p8-batch213-*.log`、`logs/p8-batch214-*.log`、`logs/p8-batch215-*.log`、`logs/p8-batch216-*.log`、`logs/p8-batch217-*.log`、`logs/p8-batch218-*.log`、`logs/p8-batch219-*.log`、`logs/p8-batch220-*.log`、`logs/p8-batch221-*.log`、`logs/p8-batch222-*.log`、`logs/p8-batch223-*.log`、`logs/p8-batch224-*.log`、`logs/p8-batch225-*.log`、`logs/p8-batch226-*.log`、`logs/p8-batch227-*.log`、`logs/p8-batch228-*.log`、`logs/p8-batch229-*.log`、`logs/p8-batch230-*.log`、`logs/p8-batch231-*.log`、`logs/p8-batch232-*.log`、`logs/p8-batch233-*.log`、`logs/p8-batch234-*.log`、`logs/p8-batch235-*.log`、`logs/p8-batch236-*.log`、`logs/p8-batch237-*.log`、`logs/p8-batch238-*.log`、`logs/p8-batch239-*.log`、`logs/p8-batch240-*.log`、`logs/p8-batch241-*.log`、`logs/p8-batch242-*.log`、`logs/p8-batch243-*.log`、`logs/p8-batch244-*.log`、`logs/p8-batch245-*.log`、`logs/p8-batch246-*.log`、`logs/p8-batch247-*.log`、`logs/p8-batch248-*.log`、`logs/p8-batch249-*.log`、`logs/p8-batch250-*.log`、`logs/p8-batch251-*.log`、`logs/p8-batch252-*.log`、`logs/p8-batch253-*.log`、`logs/p8-batch254-*.log`、`logs/p8-batch255-*.log`、`logs/p8-batch256-*.log`、`logs/p8-batch257-*.log`、`logs/p8-batch258-*.log`、`logs/p8-batch259-*.log`、`logs/p8-batch260-*.log`、`logs/p8-batch261-*.log`、`logs/p8-batch262-*.log`、`logs/p8-batch263-*.log`、`logs/p8-batch264-*.log`、`logs/p8-batch265-*.log`、`logs/p8-batch266-*.log`、`logs/p8-batch267-*.log`、`logs/p8-batch268-*.log`、`logs/p8-batch269-*.log`、`logs/p8-batch270-*.log`、`logs/p8-batch271-*.log`、`logs/p8-batch272-*.log`、`logs/p8-batch273-*.log`、`logs/p8-batch274-*.log`、`logs/p8-batch276-*.log`、`logs/p8-batch277-*.log`、`logs/p8-batch278-*.log`、`logs/p8-batch280-*.log`、`logs/p8-batch281-*.log`、`logs/p8-batch282-*.log`、`logs/p8-batch284-*.log`、`logs/p8-batch285-*.log`、`logs/p8-batch286-*.log`、`logs/p8-batch287-*.log`、`logs/p8-batch288-*.log`、`logs/p8-batch289-*.log`、`logs/p8-batch290-*.log`、`logs/p8-batch291-*.log`、`logs/p8-batch292-*.log`、`logs/p8-batch293-*.log`、`logs/p8-batch294-*.log`、`logs/p8-batch295-*.log`、`logs/p8-batch296-*.log`、`logs/p8-batch297-*.log`、`logs/p8-batch298-*.log`、`logs/p8-batch299-*.log`、`logs/p8-batch300-*.log`、`logs/p8-batch301-*.log`、`logs/p8-batch302-*.log`、`logs/p8-batch303-*.log`、`logs/p8-batch304-*.log`、`logs/p8-batch305-*.log`、`logs/p8-batch306-*.log`、`logs/p8-batch307-*.log`、`logs/p8-batch308-*.log`、`logs/p8-batch309-*.log`、`logs/p8-batch310-*.log`、`logs/p8-batch311-*.log`、`logs/p8-batch312-*.log`、`logs/p8-batch313-*.log`、`logs/p8-batch314-*.log`、`logs/p8-batch315-*.log`、`logs/p8-batch316-*.log`、`logs/p8-batch317-*.log`、`logs/p8-batch318-*.log`、`logs/p8-batch319-*.log`、`logs/p8-batch320-*.log`、`logs/p8-batch321-*.log`、`logs/p8-batch322-*.log`、`logs/p8-batch323-*.log`、`logs/p8-batch324-*.log`、`logs/p8-batch325-*.log`、`logs/p8-batch326-*.log`、`logs/p8-batch327-*.log`、`logs/p8-batch328-*.log`、`logs/p8-batch329-*.log`、`logs/p8-batch330-*.log`、`logs/p8-batch331-*.log`、`logs/p8-batch332-*.log`、`logs/p8-batch333-*.log`、`logs/p8-batch334-*.log`、`logs/p8-batch335-*.log`、`logs/p8-batch336-*.log`、`logs/p8-batch337-*.log`、`logs/p8-batch338-*.log`、`logs/p8-batch339-*.log`、`logs/p8-batch340-*.log`、`logs/p8-batch341-*.log`、`logs/p8-batch342-*.log`、`logs/p8-batch343-*.log`、`logs/p8-batch344-*.log`、`logs/p8-batch345-*.log`、`logs/p8-batch346-*.log`、`logs/p8-batch347-*.log`、`logs/p8-batch348-*.log`、`logs/p8-batch349-*.log`、`logs/p8-batch350-*.log`、`logs/p8-batch351-*.log`、`logs/p8-batch352-*.log`、`logs/p8-batch353-*.log`、`logs/p8-batch354-*.log`、`logs/p8-batch355-*.log`、`logs/p8-batch356-*.log`、`logs/p8-batch357-*.log`、`logs/p8-batch358-*.log`、`logs/p8-batch359-*.log`、`logs/p8-batch360-*.log`、`logs/p8-batch361-*.log`、`logs/p8-batch362-*.log`、`logs/p8-batch363-*.log`、`logs/p8-batch364-*.log`、`logs/p8-batch365-*.log`、`logs/p8-batch366-*.log`、`logs/p8-batch367-*.log`、`logs/p8-batch368-*.log`、`logs/p8-batch369-*.log`、`logs/p8-batch370-*.log`、`logs/p8-batch371-*.log`、`logs/p8-batch372-*.log`、`logs/p8-batch373-*.log`、`logs/p8-batch374-*.log`、`logs/p8-batch375-*.log`、`logs/p8-batch376-*.log`、`logs/p8-batch377-*.log`、`logs/p8-batch378-*.log`、`logs/p8-batch379-*.log`、`logs/p8-batch380-*.log`、`logs/p8-batch381-*.log`、`logs/p8-batch382-*.log`、`logs/p8-batch383-*.log`、`logs/p8-batch384-*.log`、`logs/p8-batch385-*.log`、`logs/p8-batch386-*.log`、`logs/p8-batch387-*.log`、`logs/p8-batch388-*.log`、`logs/p8-batch389-*.log`、`logs/p8-batch390-*.log`、`logs/p8-batch391-*.log`、`logs/p8-batch392-*.log`、`logs/p8-batch393-*.log`、`logs/p8-batch394-*.log`、`logs/p8-batch395-*.log`、`logs/p8-batch396-*.log`、`logs/p8-batch397-*.log`、`logs/p8-batch398-*.log`、`logs/p8-batch399-*.log`、`logs/p8-batch400-*.log`、`logs/p8-batch401-*.log`、`logs/p8-batch402-*.log`、`logs/p8-batch403-*.log`、`logs/p8-batch404-*.log`、`logs/p8-batch405-*.log`、`logs/p8-batch406-*.log`、`logs/p9-bench-partial.log`、`logs/p9-bench-summary.csv`、`logs/results/`、`logs/p8-batch408-*.log`、`logs/p8-batch409-*.log`、`logs/p8-batch410-*.log`、`logs/p8-batch411-*.log`、`logs/p8-batch412-*.log`、`logs/p8-batch413-*.log`、`logs/p8-batch414-*.log`、`logs/p8-batch415-*.log`、`logs/p8-batch416-*.log`、`logs/p8-batch417-*.log`、`logs/p8-batch418-*.log`、`logs/p8-batch419-*.log`、`logs/p8-batch420-*.log`、`logs/p8-batch421-*.log`、`logs/p8-batch422-*.log`、`logs/p8-batch423-*.log`、`logs/p8-batch424-*.log`、`logs/p8-batch425-*.log`、`logs/p8-batch426-*.log`、`logs/p8-batch427-*.log`、`logs/p8-batch428-*.log`、`logs/p8-batch429-*.log`、`logs/p8-batch430-*.log`、`logs/p8-batch431-*.log`、`logs/p8-batch432-*.log`、`logs/p8-batch433-*.log`、`logs/p8-batch434-*.log`、`logs/p8-batch435-*.log`、`logs/p8-batch436-*.log`、`logs/p8-batch437-*.log`、`logs/p8-batch438-*.log`、`logs/p8-batch439-*.log`、`logs/p8-batch440-*.log`、`logs/p8-batch441-*.log`、`logs/p8-batch442-*.log`、`logs/p8-batch443-*.log`、`logs/p8-batch444-*.log`、`logs/p8-batch445-*.log`、`logs/p8-batch446-*.log`、`logs/p8-batch447-*.log`、`logs/p8-batch448-*.log`、`logs/p8-batch449-*.log`、`logs/p8-batch450-*.log`、`logs/p8-batch451-*.log`、`logs/p8-batch452-*.log`、`logs/p8-batch453-*.log`、`logs/p8-batch454-*.log`、`logs/p8-batch455-*.log`、`logs/p8-batch456-*.log`、`logs/p8-batch457-*.log`、`logs/p8-batch458-*.log`、`logs/p8-batch459-*.log`、`logs/p8-batch460-*.log`、`logs/p8-batch461-*.log`、`logs/p8-batch462-*.log`、`logs/p8-batch463-*.log`、`logs/p8-batch464-*.log`、`logs/p8-batch465-*.log`、`logs/p8-batch466-*.log`、`logs/p8-batch467-*.log`、`logs/p8-batch468-*.log`、`logs/p8-batch469-*.log`、`logs/p8-batch470-*.log`、`logs/p8-batch471-*.log`、`logs/p8-batch472-*.log`、`logs/p8-batch473-*.log`、`logs/p8-batch474-*.log`、`logs/p8-batch475-*.log`、`logs/p8-batch476-*.log`、`logs/p8-batch477-*.log`、`logs/p8-batch478-*.log`、`logs/p8-batch479-*.log`、`logs/p8-batch480-*.log`、`logs/p8-batch481-*.log`、`logs/p8-batch482-*.log`、`logs/p8-batch483-*.log`、`logs/p8-batch484-*.log`、`logs/p8-batch485-*.log`、`logs/p8-batch486-*.log`、`logs/p8-batch487-*.log`、`logs/p8-batch488-*.log`、`logs/p8-batch489-*.log`、`logs/p8-batch490-*.log`、`logs/p8-batch491-*.log`、`logs/p8-batch492-*.log`、`logs/p8-batch493-*.log`、`logs/p8-batch494-*.log`、`logs/p8-batch495-*.log`、`logs/p8-batch496-*.log`、`logs/p8-batch497-*.log`、`logs/p8-batch498-*.log`、`logs/p8-batch499-*.log`、`logs/p8-batch500-*.log`、`logs/p8-batch501-*.log`、`logs/p8-batch502-*.log`、`logs/p8-batch503-*.log`、`logs/p8-batch504-*.log`、`logs/p8-batch505-*.log`、`logs/p8-batch506-*.log`、`logs/p8-batch507-*.log`、`logs/p8-batch508-*.log`、`logs/p8-batch509-*.log`、`logs/p8-batch510-*.log`、`logs/p8-batch511-*.log`、`logs/p8-batch512-*.log`、`logs/p8-batch513-*.log`、`logs/p8-batch514-*.log`、`logs/p8-batch515-*.log`、`logs/p8-batch516-*.log`、`logs/p8-batch517-*.log`、`logs/p8-batch518-*.log`、`logs/p8-batch519-*.log`、`logs/p8-batch520-*.log`、`logs/p8-batch521-*.log`、`logs/p8-batch522-*.log`、`logs/p8-batch523-*.log`、`logs/p8-batch524-*.log`、`logs/p8-batch525-*.log`、`logs/p8-batch526-*.log`、`logs/p8-batch527-*.log`、`logs/p8-batch528-*.log`、`logs/p8-batch529-*.log`、`logs/p8-batch530-*.log`、`logs/p8-batch531-*.log`、`logs/p8-batch532-*.log`、`logs/p8-batch533-*.log`、`logs/p8-batch534-*.log`、`logs/p8-batch535-*.log`、`logs/p8-batch536-*.log`、`logs/p8-batch537-*.log`、`logs/p8-batch538-*.log`、`logs/p8-batch539-*.log`、`logs/p8-batch540-*.log`、`logs/p8-batch541-*.log`、`logs/p8-batch542-*.log`、`logs/p8-batch543-*.log`、`logs/p8-batch543-default-*.log`、`logs/p8-batch544-*.log`、`logs/p8-batch544-default-*.log`、`logs/p8-batch545-*.log`、`logs/p8-batch545-default-*.log`、`logs/p8-batch546-*.log`、`logs/p8-batch546-default-*.log`、`logs/p8-batch547-*.log`、`logs/p8-batch547-default-*.log`、`logs/p8-batch548-*.log`、`logs/p8-batch548-default-*.log`、`logs/p8-batch549-*.log`、`logs/p8-batch549-default-*.log`、`logs/p8-batch550-*.log`、`logs/p8-batch550-default-*.log`、`logs/p8-batch551-*.log`、`logs/p8-batch551-default-*.log`、`logs/p8-batch552-*.log`、`logs/p8-batch552-default-*.log`、`logs/p8-batch553-*.log`、`logs/p8-batch553-default-*.log`、`logs/p8-batch554-*.log`、`logs/p8-batch554-default-*.log`、`logs/p8-batch555-*.log`、`logs/p8-batch555-default-*.log`、`logs/p8-batch556-*.log`、`logs/p8-batch556-default-*.log`、`logs/p8-batch557-*.log`、`logs/p8-batch557-default-*.log`、`logs/p8-batch558-*.log`、`logs/p8-batch558-default-*.log`、`logs/p8-batch559-*.log`、`logs/p8-batch559-default-*.log`、`logs/p8-batch560-*.log`、`logs/p8-batch560-default-*.log`、`logs/p8-batch561-*.log`、`logs/p8-batch561-default-*.log`、`logs/p8-batch562-*.log`、`logs/p8-batch562-default-*.log`、`logs/p8-batch563-*.log`、`logs/p8-batch563-default-*.log`、`logs/p8-batch564-*.log`、`logs/p8-batch564-default-*.log`、`logs/p8-batch565-*.log`、`logs/p8-batch565-default-*.log`、`logs/p8-batch566-*.log`、`logs/p8-batch566-default-*.log`、`logs/p8-batch567-*.log`、`logs/p8-batch567-default-*.log`、`logs/p8-batch568-*.log`、`logs/p8-batch568-default-*.log`、`logs/p8-batch569-*.log`、`logs/p8-batch569-default-*.log`、`logs/p8-batch570-*.log`、`logs/p8-batch570-default-*.log`、`logs/p8-batch571-*.log`、`logs/p8-batch571-default-*.log`、`logs/p8-batch572-*.log`、`logs/p8-batch572-default-*.log`、`logs/p8-batch573-*.log`、`logs/p8-batch573-default-*.log`、`logs/p8-batch574-*.log`、`logs/p8-batch574-default-*.log`、`logs/p8-batch575-*.log`、`logs/p8-batch575-default-*.log`、`logs/p8-batch576-*.log`、`logs/p8-batch576-default-*.log`、`logs/p8-batch577-*.log`、`logs/p8-batch577-default-*.log`、`logs/p8-batch578-*.log`、`logs/p8-batch578-default-*.log`、`logs/p8-batch579-*.log`、`logs/p8-batch579-default-*.log`、`logs/p8-batch580-*.log`、`logs/p8-batch580-default-*.log`、`logs/p8-batch581-*.log`、`logs/p8-batch581-default-*.log`、`logs/p8-batch582-*.log`、`logs/p8-batch582-default-*.log`、`logs/p8-batch583-*.log`、`logs/p8-batch583-default-*.log`、`logs/p8-batch584-*.log`、`logs/p8-batch584-default-*.log`、`logs/p8-batch585-*.log`、`logs/p8-batch585-default-*.log`、`logs/p8-batch586-*.log`、`logs/p8-batch586-default-*.log`、`logs/p8-batch587-*.log`、`logs/p8-batch587-default-*.log`、`logs/p8-batch588-*.log`、`logs/p8-batch588-default-*.log`、`logs/p8-batch589-*.log`、`logs/p8-batch589-default-*.log`、`logs/p8-batch590-*.log`、`logs/p8-batch590-default-*.log`、`logs/p8-batch591-*.log`、`logs/p8-batch591-default-*.log`、`logs/p8-batch592-*.log`、`logs/p8-batch592-default-*.log`、`logs/p8-batch593-*.log`、`logs/p8-batch593-default-*.log`、`logs/p8-batch594-*.log`、`logs/p8-batch594-default-*.log`、`logs/p8-batch595-*.log`、`logs/p8-batch595-default-*.log`、`logs/p8-batch596-*.log`、`logs/p8-batch596-default-*.log`、`logs/p8-batch597-*.log`、`logs/p8-batch597-default-*.log`、`logs/p8-batch598-*.log`、`logs/p8-batch598-default-*.log`、`logs/p8-batch599-*.log`、`logs/p8-batch599-default-*.log`、`logs/p8-batch600-*.log`、`logs/p8-batch600-default-*.log`、`logs/p8-batch601-*.log`、`logs/p8-batch601-default-*.log`、`logs/p8-batch602-*.log`、`logs/p8-batch602-default-*.log`、`logs/p8-batch603-*.log`、`logs/p8-batch603-default-*.log`、`logs/p8-batch604-*.log`、`logs/p8-batch604-default-*.log`、`logs/p8-batch605-*.log`、`logs/p8-batch605-default-*.log`、`logs/p8-batch606-*.log`、`logs/p8-batch606-default-*.log`、`logs/p8-batch607-*.log`、`logs/p8-batch607-default-*.log`、`logs/p8-batch608-*.log`、`logs/p8-batch608-default-*.log`、`logs/p8-batch609-*.log`、`logs/p8-batch609-default-*.log`、`logs/p8-batch610-*.log`、`logs/p8-batch610-default-*.log`、`logs/p8-batch611-*.log`、`logs/p8-batch611-default-*.log`、`logs/p8-batch612-*.log`、`logs/p8-batch612-default-*.log`、`logs/p8-batch613-*.log`、`logs/p8-batch613-default-*.log`、`logs/p8-batch614-*.log`、`logs/p8-batch614-default-*.log`、`logs/p8-batch615-*.log`、`logs/p8-batch615-default-*.log`、`logs/p8-batch616-*.log`、`logs/p8-batch616-default-*.log`、`logs/p8-batch617-*.log`、`logs/p8-batch617-default-*.log`、`logs/p8-batch618-*.log`、`logs/p8-batch618-default-*.log`、`logs/p8-batch619-*.log`、`logs/p8-batch619-default-*.log`、`logs/p8-batch620-*.log`、`logs/p8-batch620-default-*.log`、`logs/p8-batch621-*.log`、`logs/p8-batch621-default-*.log`、`logs/p8-batch622-*.log`、`logs/p8-batch622-default-*.log`、`logs/p8-batch623-*.log`、`logs/p8-batch623-default-*.log`、`logs/p8-batch624-*.log`、`logs/p8-batch624-default-*.log`、`logs/p8-batch625-*.log`、`logs/p8-batch625-default-*.log`、`logs/p8-batch626-*.log`、`logs/p8-batch626-default-*.log`、`logs/p8-batch627-*.log`、`logs/p8-batch627-default-*.log`、`logs/p8-batch628-*.log`、`logs/p8-batch628-default-*.log`、`logs/p8-batch629-*.log`、`logs/p8-batch629-default-*.log`、`logs/p8-batch630-*.log`、`logs/p8-batch630-default-*.log`、`logs/p8-batch631-*.log`、`logs/p8-batch631-default-*.log`、`logs/p8-batch632-*.log`、`logs/p8-batch632-default-*.log`、`logs/p8-batch633-*.log`、`logs/p8-batch633-default-*.log`、`logs/p8-batch634-*.log`、`logs/p8-batch634-default-*.log`、`logs/p8-batch635-*.log`、`logs/p8-batch635-default-*.log`、`logs/p8-batch636-*.log`、`logs/p8-batch636-default-*.log`、`logs/p8-batch637-*.log`、`logs/p8-batch637-default-*.log`、`logs/p8-batch638-*.log`、`logs/p8-batch638-default-*.log`、`logs/p8-batch639-*.log`、`logs/p8-batch639-default-*.log`、`logs/p8-batch640-*.log`、`logs/p8-batch640-default-*.log`、`logs/p8-batch641-*.log`、`logs/p8-batch641-default-*.log`、`logs/p8-batch642-*.log`、`logs/p8-batch642-default-*.log`、`logs/p8-batch643-*.log`、`logs/p8-batch643-default-*.log`、`logs/p8-batch644-*.log`、`logs/p8-batch644-default-*.log`、`logs/p8-batch645-*.log`、`logs/p8-batch645-default-*.log`、`logs/p8-batch646-*.log`、`logs/p8-batch646-default-*.log`、`logs/p8-batch647-*.log`、`logs/p8-batch647-default-*.log`、`logs/p8-batch648-*.log`、`logs/p8-batch648-default-*.log`、`logs/p8-batch649-*.log`、`logs/p8-batch649-default-*.log`、`logs/p8-batch650-*.log`、`logs/p8-batch650-default-*.log`、`logs/p8-batch651-*.log`、`logs/p8-batch651-default-*.log`、`logs/p8-batch652-*.log`、`logs/p8-batch652-default-*.log`、`logs/p8-batch653-*.log`、`logs/p8-batch653-default-*.log`、`logs/p8-batch654-*.log`、`logs/p8-batch654-default-*.log`、`logs/p8-batch655-*.log`、`logs/p8-batch655-default-*.log`、`logs/p8-batch656-*.log`、`logs/p8-batch656-default-*.log`、`logs/p8-batch657-*.log`、`logs/p8-batch657-default-*.log`、`logs/p8-batch658-*.log`、`logs/p8-batch658-default-*.log`、`logs/p8-batch659-*.log`、`logs/p8-batch659-default-*.log`、`logs/p8-batch660-*.log`、`logs/p8-batch660-default-*.log`、`logs/p8-batch661-*.log`、`logs/p8-batch661-default-*.log`、`logs/p8-batch662-*.log`、`logs/p8-batch662-default-*.log`、`logs/p8-batch663-*.log`、`logs/p8-batch663-default-*.log`、`logs/p8-batch664-*.log`、`logs/p8-batch664-default-*.log`、`logs/p8-batch665-*.log`、`logs/p8-batch665-default-*.log`、`logs/p8-batch666-*.log`、`logs/p8-batch666-default-*.log`、`logs/p8-batch667-*.log`、`logs/p8-batch667-default-*.log`、`logs/p8-batch668-*.log`、`logs/p8-batch668-default-*.log`、`logs/p8-batch669-*.log`、`logs/p8-batch669-default-*.log`、`logs/p8-batch670-*.log`、`logs/p8-batch670-default-*.log`、`logs/p8-batch671-*.log`、`logs/p8-batch671-default-*.log`、`logs/p8-batch672-*.log`、`logs/p8-batch672-default-*.log`、`logs/p8-batch673-*.log`、`logs/p8-batch673-default-*.log`、`logs/p8-batch674-*.log`、`logs/p8-batch674-default-*.log`、`logs/p8-batch675-*.log`、`logs/p8-batch675-default-*.log`、`logs/p8-batch676-*.log`、`logs/p8-batch676-default-*.log`、`logs/p8-batch677-*.log`、`logs/p8-batch677-default-*.log`、`logs/p8-batch678-*.log`、`logs/p8-batch678-default-*.log`、`logs/p8-batch679-*.log`、`logs/p8-batch679-default-*.log`、`logs/p8-batch680-*.log`、`logs/p8-batch680-default-*.log`、`logs/p8-batch681-*.log`、`logs/p8-batch681-default-*.log`、`logs/p8-batch682-*.log`、`logs/p8-batch682-default-*.log`、`logs/p8-batch683-*.log`、`logs/p8-batch683-default-*.log`、`logs/p8-batch684-*.log`、`logs/p8-batch684-default-*.log`、`logs/p8-batch685-*.log`、`logs/p8-batch685-default-*.log`、`logs/p8-batch686-*.log`、`logs/p8-batch686-default-*.log`、`logs/p8-batch687-*.log`、`logs/p8-batch687-default-*.log`、`logs/p8-batch688-*.log`、`logs/p8-batch688-default-*.log`、`logs/p8-batch689-*.log`、`logs/p8-batch689-default-*.log`、`logs/p8-batch690-*.log`、`logs/p8-batch690-default-*.log`、`logs/p8-batch691-*.log`、`logs/p8-batch691-default-*.log`、`logs/p8-batch692-*.log`、`logs/p8-batch692-default-*.log`、`logs/p8-batch693-*.log`、`logs/p8-batch693-default-*.log`、`logs/p8-batch694-*.log`、`logs/p8-batch694-default-*.log`、`logs/p8-batch695-*.log`、`logs/p8-batch695-default-*.log`、`logs/p8-batch696-*.log`、`logs/p8-batch696-default-*.log`、`logs/p8-batch697-*.log`、`logs/p8-batch697-default-*.log`、`logs/p8-batch698-*.log`、`logs/p8-batch698-default-*.log`、`logs/p8-batch699-*.log`、`logs/p8-batch699-default-*.log`、`logs/p8-batch700-*.log`、`logs/p8-batch700-default-*.log`、`logs/p8-batch701-*.log`、`logs/p8-batch701-default-*.log`、`logs/p8-batch702-*.log`、`logs/p8-batch702-default-*.log`、`logs/p8-batch703-*.log`、`logs/p8-batch703-default-*.log`、`logs/p8-batch704-*.log`、`logs/p8-batch704-default-*.log`、`logs/p8-batch705-*.log`、`logs/p8-batch705-default-*.log`、`logs/p8-batch706-*.log`、`logs/p8-batch706-default-*.log`、`logs/p8-batch707-*.log`、`logs/p8-batch707-default-*.log`、`logs/p8-batch708-*.log`、`logs/p8-batch708-default-*.log`、`logs/p8-batch709-*.log`、`logs/p8-batch709-default-*.log`、`logs/p8-batch710-*.log`、`logs/p8-batch710-default-*.log`、`logs/p8-batch711-*.log`、`logs/p8-batch711-default-*.log`、`logs/p8-batch712-*.log`、`logs/p8-batch712-default-*.log`、`logs/p8-batch713-*.log`、`logs/p8-batch713-default-*.log`、`logs/p8-batch714-*.log`、`logs/p8-batch714-default-*.log`、`logs/p8-batch715-*.log`、`logs/p8-batch715-default-*.log`、`logs/p8-batch716-*.log`、`logs/p8-batch716-default-*.log`、`logs/p8-batch717-*.log`、`logs/p8-batch717-default-*.log`、`logs/p8-batch718-*.log`、`logs/p8-batch718-default-*.log`、`logs/p8-batch719-*.log`、`logs/p8-batch719-default-*.log`、`logs/p8-batch720-*.log`、`logs/p8-batch720-default-*.log`、`logs/p8-batch721-*.log`、`logs/p8-batch721-default-*.log`、`logs/p8-batch722-*.log`、`logs/p8-batch722-default-*.log`、`logs/p8-batch723-*.log`、`logs/p8-batch723-default-*.log`、`logs/p8-batch724-*.log`、`logs/p8-batch724-default-*.log`、`logs/p8-batch725-*.log`、`logs/p8-batch725-default-*.log`、`logs/p8-batch726-*.log`、`logs/p8-batch726-default-*.log`、`logs/p8-batch727-*.log`、`logs/p8-batch727-default-*.log`、`logs/p8-batch728-*.log`、`logs/p8-batch728-default-*.log`、`logs/p8-batch729-*.log`、`logs/p8-batch729-default-*.log`、`logs/p8-batch730-*.log`、`logs/p8-batch730-default-*.log`、`logs/p8-batch731-*.log`、`logs/p8-batch731-default-*.log`、`logs/p8-batch732-*.log`、`logs/p8-batch732-default-*.log`、`logs/p8-batch733-*.log`、`logs/p8-batch733-default-*.log`、`logs/p8-batch734-*.log`、`logs/p8-batch734-default-*.log`、`logs/p8-batch735-*.log`、`logs/p8-batch735-default-*.log`、`logs/p8-batch736-*.log`、`logs/p8-batch736-default-*.log`、`logs/p8-batch737-*.log`、`logs/p8-batch737-default-*.log`、`logs/p8-batch738-*.log`、`logs/p8-batch738-default-*.log`、`logs/p8-batch739-*.log`、`logs/p8-batch739-default-*.log`、`logs/p8-batch740-*.log`、`logs/p8-batch740-default-*.log`、`logs/p8-batch741-*.log`、`logs/p8-batch741-default-*.log`、`logs/p8-batch742-*.log`、`logs/p8-batch742-default-*.log`、`logs/p8-batch743-*.log`、`logs/p8-batch743-default-*.log`、`logs/p8-batch744-*.log`、`logs/p8-batch744-default-*.log`、`logs/p8-batch745-*.log`、`logs/p8-batch745-default-*.log`、`logs/p8-batch746-*.log`、`logs/p8-batch746-default-*.log`、`logs/p8-batch747-*.log`、`logs/p8-batch747-default-*.log`、`logs/p8-batch748-*.log`、`logs/p8-batch748-default-*.log`、`logs/p8-batch749-*.log`、`logs/p8-batch749-default-*.log`、`logs/p8-batch750-*.log`、`logs/p8-batch750-default-*.log`、`logs/p8-batch751-*.log`、`logs/p8-batch751-default-*.log`、`logs/p8-batch752-*.log`、`logs/p8-batch752-default-*.log`、`logs/p8-batch753-*.log`、`logs/p8-batch753-default-*.log`、`logs/p8-batch754-*.log`、`logs/p8-batch754-default-*.log`、`logs/p8-batch755-*.log`、`logs/p8-batch755-default-*.log`、`logs/p8-batch756-*.log`、`logs/p8-batch756-default-*.log`、`logs/p8-batch757-*.log`、`logs/p8-batch757-default-*.log`、`logs/p8-batch758-*.log`、`logs/p8-batch758-default-*.log`、`logs/p8-batch759-*.log`、`logs/p8-batch759-default-*.log`、`logs/p8-batch760-*.log`、`logs/p8-batch760-default-*.log`、`logs/p8-batch761-*.log`、`logs/p8-batch761-default-*.log`、`logs/p8-batch762-*.log`、`logs/p8-batch762-default-*.log`、`logs/p8-batch763-*.log`、`logs/p8-batch763-default-*.log`、`logs/p8-batch764-*.log`、`logs/p8-batch764-default-*.log`、`logs/p8-batch765-*.log`、`logs/p8-batch765-default-*.log`、`logs/p8-batch766-*.log`、`logs/p8-batch766-default-*.log`、`logs/p8-batch767-*.log`、`logs/p8-batch767-default-*.log`、`logs/p8-batch768-*.log`、`logs/p8-batch768-default-*.log`、`logs/p8-batch769-*.log`、`logs/p8-batch769-default-*.log`、`logs/p8-batch770-*.log`、`logs/p8-batch770-default-*.log`、`logs/p8-batch771-*.log`、`logs/p8-batch771-default-*.log`、`logs/p8-batch772-*.log`、`logs/p8-batch772-default-*.log`、`logs/p8-batch773-*.log`、`logs/p8-batch773-default-*.log`、`logs/p8-batch774-*.log`、`logs/p8-batch774-default-*.log`、`logs/p8-batch775-*.log`、`logs/p8-batch775-default-*.log`、`logs/p8-batch776-*.log`、`logs/p8-batch776-default-*.log`、`logs/p8-batch777-*.log`、`logs/p8-batch777-default-*.log`、`logs/p8-batch778-*.log`、`logs/p8-batch778-default-*.log`、`logs/p8-batch779-*.log`、`logs/p8-batch779-default-*.log`、`logs/p8-batch780-*.log`、`logs/p8-batch780-default-*.log`、`logs/p8-batch781-*.log`、`logs/p8-batch781-default-*.log`、`logs/p8-batch782-*.log`、`logs/p8-batch782-default-*.log`、`logs/p8-batch783-*.log`、`logs/p8-batch783-default-*.log`、`logs/p8-batch784-*.log`、`logs/p8-batch784-default-*.log`、`logs/p8-batch785-*.log`、`logs/p8-batch785-default-*.log`、`logs/p8-batch786-*.log`、`logs/p8-batch786-default-*.log`、`logs/p8-batch787-*.log`、`logs/p8-batch787-default-*.log`、`logs/p8-batch788-*.log`、`logs/p8-batch788-default-*.log`、`logs/p8-batch789-*.log`、`logs/p8-batch789-default-*.log`、`logs/p8-batch790-*.log`、`logs/p8-batch790-default-*.log`、`logs/p8-batch791-*.log`、`logs/p8-batch791-default-*.log`、`logs/p8-batch792-*.log`、`logs/p8-batch792-default-*.log`、`logs/p8-batch793-*.log`、`logs/p8-batch793-default-*.log`、`logs/p8-batch794-*.log`、`logs/p8-batch794-default-*.log`、`logs/p8-batch795-*.log`、`logs/p8-batch795-default-*.log`、`logs/p8-batch796-*.log`、`logs/p8-batch796-default-*.log`、`logs/p8-batch797-*.log`、`logs/p8-batch797-default-*.log`、`logs/p8-batch798-*.log`、`logs/p8-batch798-default-*.log`、`logs/p8-batch799-*.log`、`logs/p8-batch799-default-*.log`、`logs/p8-batch800-*.log`、`logs/p8-batch800-default-*.log`、`logs/p8-batch801-*.log`、`logs/p8-batch801-default-*.log`、`logs/p8-batch802-*.log`、`logs/p8-batch802-default-*.log`、`logs/p8-batch803-*.log`、`logs/p8-batch803-default-*.log`、`logs/p8-batch804-*.log`、`logs/p8-batch804-default-*.log`、`logs/p8-batch805-*.log`、`logs/p8-batch805-default-*.log`、`logs/p8-batch806-*.log`、`logs/p8-batch806-default-*.log`、`logs/p8-batch807-*.log`、`logs/p8-batch807-default-*.log`、`logs/p8-batch808-*.log`、`logs/p8-batch808-default-*.log`、`logs/p8-batch809-*.log`、`logs/p8-batch809-default-*.log`、`logs/p8-batch810-*.log`、`logs/p8-batch810-default-*.log`、`logs/p8-batch811-*.log`、`logs/p8-batch811-default-*.log`、`logs/p8-batch812-*.log`、`logs/p8-batch812-default-*.log`、`logs/p8-batch813-*.log`、`logs/p8-batch813-default-*.log`、`logs/p8-batch814-*.log`、`logs/p8-batch814-default-*.log`、`logs/p8-batch815-*.log`、`logs/p8-batch815-default-*.log`、`logs/p8-batch816-*.log`、`logs/p8-batch816-default-*.log`、`logs/p8-batch817-*.log`、`logs/p8-batch817-default-*.log`、`logs/p8-batch818-*.log`、`logs/p8-batch818-default-*.log`、`logs/p8-batch819-*.log`、`logs/p8-batch819-default-*.log`、`logs/p8-batch820-*.log`、`logs/p8-batch820-default-*.log`、`logs/p8-batch821-*.log`、`logs/p8-batch821-default-*.log`、`logs/p8-batch822-*.log`、`logs/p8-batch822-default-*.log`、`logs/p8-batch823-*.log`、`logs/p8-batch823-default-*.log`、`logs/p8-batch824-*.log`、`logs/p8-batch824-default-*.log`、`logs/p8-batch825-*.log`、`logs/p8-batch825-default-*.log`、`logs/p8-batch826-*.log`、`logs/p8-batch826-default-*.log`、`logs/p8-batch827-*.log`、`logs/p8-batch827-default-*.log`、`logs/p8-batch828-*.log`、`logs/p8-batch828-default-*.log`、`logs/p8-batch829-*.log`、`logs/p8-batch829-default-*.log`、`logs/p8-batch830-*.log`、`logs/p8-batch830-default-*.log`、`logs/p8-batch831-*.log`、`logs/p8-batch831-default-*.log`、`logs/p8-batch832-*.log`、`logs/p8-batch832-default-*.log`、`logs/p8-batch833-*.log`、`logs/p8-batch833-default-*.log`、`logs/p8-batch835-*.log`、`logs/p8-batch835-default-*.log`、`logs/p8-batch836-*.log`、`logs/p8-batch836-default-*.log`、`logs/p8-batch837-*.log`、`logs/p8-batch837-default-*.log`、`logs/p8-batch838-*.log`、`logs/p8-batch838-default-*.log`、`logs/p8-batch839-*.log`、`logs/p8-batch839-default-*.log`、`logs/p8-batch840-*.log`、`logs/p8-batch840-default-*.log`、`logs/p8-batch841-*.log`、`logs/p8-batch841-default-*.log`、`logs/p8-batch842-*.log`、`logs/p8-batch842-default-*.log`、`logs/p8-batch843-*.log`、`logs/p8-batch843-default-*.log`、`logs/p8-batch844-*.log`、`logs/p8-batch844-default-*.log`、`logs/p8-batch845-*.log`、`logs/p8-batch845-default-*.log`、`logs/p8-batch846-*.log`、`logs/p8-batch846-default-*.log`、`logs/p8-batch846-pack.log`、`logs/p8-batch847-*.log`、`logs/p8-batch847-default-*.log`、`logs/p8-batch847-pack.log`、`logs/p8-batch848-*.log`、`logs/p8-batch848-default-*.log`、`logs/p8-batch848-pack.log`、`logs/p8-batch849-*.log`、`logs/p8-batch849-default-*.log`、`logs/p8-batch850-*.log`、`logs/p8-batch850-default-*.log`、`logs/p8-batch851-*.log`、`logs/p8-batch851-default-*.log`、`logs/p8-batch852-*.log`、`logs/p8-batch852-default-*.log`、`logs/p8-batch853-*.log`、`logs/p8-batch853-default-*.log`、`logs/p8-batch854-*.log`、`logs/p8-batch854-default-*.log`、`logs/p8-batch855-*.log`、`logs/p8-batch855-default-*.log`、`logs/p8-batch856-*.log`、`logs/p8-batch856-default-*.log`、`logs/p8-batch857-*.log`、`logs/p8-batch857-default-*.log`、`logs/p8-batch858-*.log`、`logs/p8-batch858-default-*.log`、`logs/p8-batch859-*.log`、`logs/p8-batch859-default-*.log`、`logs/p8-batch860-*.log`、`logs/p8-batch860-default-*.log`、`logs/p8-batch861-*.log`、`logs/p8-batch861-default-*.log`、`logs/p8-batch862-*.log`、`logs/p8-batch862-default-*.log`、`logs/p8-batch863-*.log`、`logs/p8-batch863-default-*.log`、`logs/p8-batch864-*.log`、`logs/p8-batch864-default-*.log`、`logs/p8-batch865-*.log`、`logs/p8-batch865-default-*.log`、`logs/p8-batch866-*.log`、`logs/p8-batch866-default-*.log`、`logs/p8-batch867-*.log`、`logs/p8-batch867-default-*.log`、`logs/p8-batch868-*.log`、`logs/p8-batch868-default-*.log`、`logs/p8-batch869-*.log`、`logs/p8-batch869-default-*.log`、`logs/p8-batch870-*.log`、`logs/p8-batch870-default-*.log`、`logs/p8-batch871-*.log`、`logs/p8-batch871-default-*.log`、`logs/p8-batch872-*.log`、`logs/p8-batch872-default-*.log`、`logs/p8-batch873-*.log`、`logs/p8-batch873-default-*.log`、`logs/p8-batch874-*.log`、`logs/p8-batch874-default-*.log`、`logs/p8-batch875-*.log`、`logs/p8-batch875-default-*.log`、`logs/p8-batch876-*.log`、`logs/p8-batch876-default-*.log`、`logs/p8-batch877-*.log`、`logs/p8-batch877-default-*.log`、`logs/p8-batch878-*.log`、`logs/p8-batch878-default-*.log`、`logs/p8-batch879-*.log`、`logs/p8-batch879-default-*.log`、`logs/p8-batch880-*.log`、`logs/p8-batch880-default-*.log`、`logs/p8-batch881-*.log`、`logs/p8-batch881-default-*.log`、`logs/p8-batch882-*.log`、`logs/p8-batch882-default-*.log`、`logs/p8-batch883-*.log`、`logs/p8-batch883-default-*.log`、`logs/p8-batch884-*.log`、`logs/p8-batch884-default-*.log`、`logs/p8-batch885-*.log`、`logs/p8-batch885-default-*.log`、`logs/p8-batch886-*.log`、`logs/p8-batch886-default-*.log`、`logs/p8-batch887-*.log`、`logs/p8-batch887-default-*.log`、`logs/p8-batch888-*.log`、`logs/p8-batch888-default-*.log`、`logs/p8-batch889-*.log`、`logs/p8-batch889-default-*.log`、`logs/p8-batch890-*.log`、`logs/p8-batch890-default-*.log`、`logs/p8-batch891-*.log`、`logs/p8-batch891-default-*.log`、`logs/p8-batch892-*.log`、`logs/p8-batch892-default-*.log`、`logs/p8-batch893-*.log`、`logs/p8-batch893-default-*.log`、`logs/p8-batch894-*.log`、`logs/p8-batch894-default-*.log`、`logs/p8-batch895-*.log`、`logs/p8-batch895-default-*.log`、`logs/p8-batch896-*.log`、`logs/p8-batch896-default-*.log`、`logs/p8-batch897-*.log`、`logs/p8-batch897-default-*.log`、`logs/p8-batch898-*.log`、`logs/p8-batch898-default-*.log`、`logs/p8-batch899-*.log`、`logs/p8-batch899-default-*.log`、`logs/p8-batch900-*.log`、`logs/p8-batch900-default-*.log`、`logs/p8-batch901-*.log`、`logs/p8-batch901-default-*.log`、`logs/p8-batch902-*.log`、`logs/p8-batch902-default-*.log`、`logs/p8-batch903-*.log`、`logs/p8-batch903-default-*.log`、`logs/p8-batch904-*.log`、`logs/p8-batch904-default-*.log`、`logs/p8-batch905-*.log`、`logs/p8-batch905-default-*.log`、`logs/p8-batch906-*.log`、`logs/p8-batch906-default-*.log`、`logs/p8-batch907-*.log`、`logs/p8-batch907-default-*.log`、`logs/p8-batch908-*.log`、`logs/p8-batch908-default-*.log`、`logs/p8-batch909-*.log`、`logs/p8-batch909-default-*.log`、`logs/p8-batch910-*.log`、`logs/p8-batch910-default-*.log`、`logs/p8-batch911-*.log`、`logs/p8-batch911-default-*.log`、`logs/p8-batch912-*.log`、`logs/p8-batch912-default-*.log`、`logs/p8-batch913-*.log`、`logs/p8-batch913-default-*.log`、`logs/p8-batch914-*.log`、`logs/p8-batch914-default-*.log`、`logs/p8-batch915-*.log`、`logs/p8-batch915-default-*.log`、`logs/p8-batch916-*.log`、`logs/p8-batch916-default-*.log`、`logs/p8-batch917-*.log`、`logs/p8-batch917-default-*.log`、`logs/p8-batch918-*.log`、`logs/p8-batch918-default-*.log`、`logs/p8-batch919-*.log`、`logs/p8-batch919-default-*.log`、`logs/p8-batch920-*.log`、`logs/p8-batch920-default-*.log`、`logs/p8-batch921-*.log`、`logs/p8-batch921-default-*.log`、`logs/p8-batch922-*.log`、`logs/p8-batch922-default-*.log`、`logs/p8-batch923-*.log`、`logs/p8-batch923-default-*.log`、`logs/p8-batch924-*.log`、`logs/p8-batch924-default-*.log`、`logs/p8-batch925-*.log`、`logs/p8-batch925-default-*.log`、`logs/p8-batch926-*.log`、`logs/p8-batch926-default-*.log`、`logs/p8-batch927-*.log`、`logs/p8-batch927-default-*.log`、`logs/p8-batch928-*.log`、`logs/p8-batch928-default-*.log`、`logs/p8-batch929-*.log`、`logs/p8-batch929-default-*.log`、`logs/p8-batch930-*.log`、`logs/p8-batch930-default-*.log`、`logs/p8-batch931-*.log`、`logs/p8-batch931-default-*.log`、`logs/p8-batch932-*.log`、`logs/p8-batch932-default-*.log`、`logs/p8-batch933-*.log`、`logs/p8-batch933-default-*.log`、`logs/p8-batch934-*.log`、`logs/p8-batch934-default-*.log`、`logs/p8-batch935-*.log`、`logs/p8-batch935-default-*.log`、`logs/p8-batch661-*.log`、`logs/p8-batch661-default-*.log`、`logs/p8-batch599-*.log`、`logs/p8-batch599-default-*.log`、`logs/p6-final-*.log`
（`logs/` 已被 `.gitignore:34` 忽略）。

#### Batch 7—11 API migration

| Breaking change | Impact | Migration |
|---|---|---|
| `SerializedSolution(bool, double, List<double>, double?)` removed | The old shape lost `9007199254740993` through a `double` round-trip | Construct via the new init-only `SerializedSolution` and read `ObjectiveValueInt64` when the producer is exact |
| `RemoteSolverHttpResumeMode` members `Fresh`/`FromCheckpoint` replaced by `ServerTaskLatestCheckpoint`/`StrictCheckpoint` | Code naming the removed members no longer compiles | Use `ServerTaskLatestCheckpoint` for latest resume; `StrictCheckpoint` is explicitly rejected by the HTTP API |
| `PreemptionMode.Immediate` and `ResumeMode.Fresh`/`StrictCheckpoint` removed; `SliceOutcome` gained `Resumable`/`Cancelled` | These members were never in the pinned baseline and would have mis-encoded on the wire | Use the baseline members only; `STRICT_CHECKPOINT` is an HTTP resume mode, not a `ResumeMode` |
| `TaskComplexity` changed from `Low`/`Medium`/`High` to `Simple`/`Complex`; `TimeSensitivity` to `Realtime`/`NonRealtime`; `TaskStatus` gained 12 members | Serialized values differ from the earlier stub | Re-decode persisted payloads against the baseline member names |
| `ModelData`, `SolvePayload`, `SolverConfig`, `TaskMeta` rebuilt to the baseline field set | `ModelData.RawBytes`/`Format` now participate in `ModelType` inference | Use the `Reference`/`Linear`/`Quadratic`/`Raw` factories |
| `RemoteSolverHttpClient.ResumeAsync` rejects `StrictCheckpoint` without any request | Strict resume now fails fast instead of silently resuming the server's latest checkpoint | Opt into `ServerTaskLatestCheckpoint` explicitly when latest resume is acceptable |
| `RemoteSolverHttpClient.StopAsync` returns `false` unless the task reaches `STOPPED`/`COMPLETED` | A 2xx stop response alone no longer means success | Treat `false` as "not confirmed"; re-poll or inspect the task view |
| `FetchFinalResultAsync` runs `ValidateLinearQuadraticResult` before report mapping | A protocol-invalid envelope now yields `ORSolutionInvalid` instead of a partial result | Fix the producer; v1 stays readable, v2 is strict, future majors are rejected |
| Remote identifier value types (`TaskId`, `SliceId`, `TenantId`, `RequestId`, `ObjectPath`, `BudgetScopeId`, …) serialize as bare strings | They previously serialized as `{"value":"…"}`, which the pinned Kotlin value classes cannot read, so every submit/resume request was structurally incompatible | No source change needed on the .NET side; the converter is registered in `RemoteWireJson.Options` |
| `StartAsync` posts to `POST /api/v1/tasks` with `RemoteTaskSubmitRequest` and decodes the envelope | The old stub posted `{taskId, sliceId, nodeId, tenantId}` to `/tasks/start` and treated any 2xx as success | A rejected or unknown status now yields `ApplicationError` and no handle; supply `requestIdProvider` when the default deterministic id is unsuitable |
| `AwaitSliceEndAsync` sends `quantum` as integer milliseconds | Fractional milliseconds (`quantum=1500.7`) are no longer sent | Round or truncate the `TimeSpan` at the call site if a specific rounding is required |
| Computed properties (`ClaimsExactOptimum`, `ModelData.IsReference`/`IsInline`/`ModelType`, `SolvePayload.ModelRef`) are `[JsonIgnore]` | They no longer appear on the wire, which the pinned Kotlin side would reject with `ignoreUnknownKeys = false` | Read them as .NET properties only |

限制（P4 未收口，不得标记完成）：**B1221 更正——C# 侧<b>有</b>内建 `IObjectStoragePort` 实现**
（`LocalFileObjectStoragePort`，90 行，`NotImplementedException`／`TODO`／`not ported` 计数为 0，
以根目录为参数把对象按路径落到本地文件）；**基线的固定版本里确实只有接口、由调用方注入，
所以这一个是 C# 侧的<b>扩展</b>而不是缺口——但"无内建实现"这句描述已不成立，已删。**
元数据能否真正落盘取决于所选实现；用 `LocalFileObjectStoragePort` 时落盘到它给的根目录，`solver`／`model` 指纹入口在**固定基线里不存在**（基线
`SolveFingerprinting` 只有 `sha256` 与 `configuration`），C# 的 `SolveFingerprinting.Solver` 是扩展
能力而非缺口，已用回归测试锁定其审计值；`Http4kRemoteSolverHttpTransport.kt` 不是缺口（http4k 为
JVM 专属，C# 已有等价的 `HttpClientRemoteSolverHttpTransportPlugin`）；
`RemoteSolverRuntimeConfig.kt` 部分缺口（其 `maxRounds` 在基线客户端里未被使用）；
`resumeMode` 默认值**刻意偏离**基线——基线默认 `STRICT_CHECKPOINT` 且其枚举注释自认"当前 HTTP API
不支持"，照抄会让 `ResumeAsync` 默认必然失败，故 C# 保留可用默认；`RemoteConstraintProgrammingClient` 的 `solveOutput`／`materialize` **已移植**（B1218 核实：
`SolveOutputAsync`／`MaterializeAsync` 均在，`validateExecutionBinding`、`validateSerializedResult`、
`readSerializedSolution`、`validateConstraints`／`validateObjective`、`toReport` 与输出物化分支都有）；
`OspfRemoteModelSerializer` 的 `ToSerializedVariable` 与身份校验**已移植**（B1218 核实：
`ValidateIdentity` 被 7 处引用，变量类型映射覆盖 Binary／Continuous／Integer 三态）；
`RemoteLinearSolver.kt`（339 行）、`RemoteQuadraticSolver.kt`（339 行）、`OspfRemoteModelSerializer.kt`
（480 行）**三份均已移植**，C# 侧行数为 243／219／585，且 `NotImplementedException`／`NotSupportedException`／
`TODO`／`not ported` 标记计数均为 **0**（B1218 核实）。
**B1218 更正：本段此前写着"其余 3 个真实缺口共约 1100 行尚未移植"，与同段前文"已完成"直接矛盾，
且与源码事实不符——那是一句过期结论，本批已删。**
仍然成立的一条：摘要复算**刻意未实现**（`RemoteCpResultArtifact` 的注释写明：基线的摘要是 Kotlin
`Json { encodeDefaults = true }` 的 SHA-256，C# 侧要复算就得逐字节复刻那个序列化，而它尚未对跑验证，
故**在跨语言实测完成前不作为能力宣称**）。
注：`PortableCheckpointModels.kt` 在固定基线里是 Core 类型的纯 typealias，C# 直接使用 Core 类型
即为对齐，不算缺口。跨语言实测已覆盖 `SerializedSolution`、`RemoteTaskSubmitRequest` 与配置指纹，
但调度模型、能力摘要与 CP checkpoint envelope 仍未对跑，不得据此宣称整体互通。

## 扫描目标迁到 `966cafbc`（B1314）

**扫描目标（`scripts/scan-kotlin-baseline.py` 的默认提交）从 `12fdb587` 迁到 `966cafbc`。**
**夹具出处里的 `12fdb587` 保持不动：** **远端语料、线协议、各演示的对齐事实都产自那个提交，把它们一起改掉等于伪造出处。**
**判据 `TerminalStateTests.TheFixtureProvenance_ShouldKeepTheOldBaseline` 钉住这个区分——扫描目标可以往前走，出处不跟着动。**

**Payload: the scan target moved; the provenance did not.** **Fixtures, wire contracts and demo alignments were produced at `12fdb587`; rewriting their
origin to follow the target would forge it.** **A criterion pins the distinction.**
