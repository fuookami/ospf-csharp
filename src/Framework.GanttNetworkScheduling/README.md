# Framework.GanttNetworkScheduling

[中文](README_ch.md)

The bridge between network path search and Gantt business bunches: one context class,
`NetworkBunchGenerationContext`, and the completion translation it performs.

## Why this is a separate project

`Framework.NetworkScheduling` **already references** `Framework.GanttScheduling`:

```
Framework.NetworkScheduling.csproj → Framework.GanttScheduling.csproj
```

Adding the bridge to `Framework.GanttScheduling` would close a reference cycle, so it lives in its own
project.

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
That is a deliberate structural choice, not a missing call, and it cannot be verified for result
equivalence, so it is recorded rather than rewritten.

## Verification

```bash
dotnet test tests/Framework.GanttNetworkScheduling.Tests/Framework.GanttNetworkScheduling.Tests.csproj -c Release
```

No claim of cross-language equivalence: only `SerializedSolution`, `RemoteTaskSubmitRequest` and the
`configuration`/`sha256` digest have been measured across the two implementations.
