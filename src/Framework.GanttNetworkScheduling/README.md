# Framework.GanttNetworkScheduling

[中文](README_ch.md)

The bridge between network path search and Gantt business bunches.

Baseline: `ospf-kotlin-framework-gantt-scheduling/gantt-scheduling-domain-network-context` at Kotlin
`12fdb587` (one 72-line class, `NetworkBunchGenerationContext`).

## Why this is a separate project

`Framework.NetworkScheduling` **already references** `Framework.GanttScheduling`:

```
Framework.NetworkScheduling.csproj → Framework.GanttScheduling.csproj
```

Adding the bridge to `Framework.GanttScheduling` would close a reference cycle. In the baseline it is a separate module anyway,
so this project exists for the same reason.

## What it does

`NetworkBunchGenerationContext.Generate` runs a `PathProblem` search, then converts each returned path into
a business bunch through a caller-supplied extractor.

**The real content is the completion translation**, not the adapter loop:

| Path search completion | Column generation completion | Why |
| --- | --- | --- |
| `Exhausted` | `Exhausted` | genuinely exhausted |
| `BudgetLimit` | `BudgetLimit` | budget ran out |
| `Interrupted` | `Interrupted` | externally interrupted |
| `UncertifiedDominance` | **`Heuristic`** | **"it finished" is not "it was proved"** |
| `UncertifiedValidation` | **`Heuristic`** | same |
| **any status + a rejected path** | **`Heuristic`** | "most paths usable" is not "may be treated as exhausted" |

And `NoImprovingColumns` is `true` **only when the caller supplies independent evidence and the completion
is `Exhausted`**; otherwise it is `null` (unknown), **not `false`**. Writing `false` there would let a caller
read a search that **might still have better columns** as "definitely none".

## What this does not do

Demo4's `FlightTaskBunchGenerator` does **not** use this bridge — it runs its own 248-line label search.
That is a structural divergence from the baseline and cannot be verified for result equivalence, so it was
recorded rather than rewritten. See `docs/kotlin-capability-matrix.md`.

## Verification

```bash
dotnet test tests/Framework.GanttNetworkScheduling.Tests/Framework.GanttNetworkScheduling.Tests.csproj -c Release
```

No claim of cross-language equivalence: only `SerializedSolution`, `RemoteTaskSubmitRequest` and the
`configuration`/`sha256` digest have been measured across the two implementations.
