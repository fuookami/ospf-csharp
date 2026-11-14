# Framework.NetworkScheduling

[中文](README_ch.md)

Resource-constrained path search and route generation on top of the OSPF framework.

Baseline: `ospf-kotlin-framework-network-scheduling` at Kotlin `12fdb587`.

## Layout

A single assembly with `Application/`, `Domain/`, and `Infrastructure/`. The baseline splits this into
several sub-modules; **this project does not mirror that split**.

## What it provides

| Capability | Where |
| --- | --- |
| Resource-label path search | `Domain/Path/PathSearchContext.cs` |
| Path problem definition (transitions, terminals, dominance) | `Domain/Path/PathSearchContext.cs` |
| Path compilation | `Domain/PathCompilation/` |
| ESPPRC pricing | `Domain/RouteGeneration/Service/EspprcPricer.cs` |
| Route graph construction | `Domain/RouteGeneration/Service/RouteGraphBuilder.cs` |
| Network graph primitives | `Infrastructure/NetworkGraph.cs` |

## Completion status is not exhaustion

`PathSearchResult.Completion` distinguishes `Exhausted` from `BudgetLimit`, `Interrupted`,
`UncertifiedDominance` and `UncertifiedValidation`. **A search that finished is not a search that proved
there is nothing better** — the last three count as heuristic results, not as an exhaustion certificate.

An empty result set likewise does not mean exhaustion. Code that reads "no paths found" as "no paths exist"
will silently accept a truncated search.

## Consumers

`Framework.GanttNetworkScheduling` adapts these paths into business bunches (Batch 278). Note that
This module **already references** `Framework.GanttScheduling`, which is why the bridge lives
in its own project.

## Verification

```bash
dotnet test tests/Framework.NetworkScheduling.Tests/Framework.NetworkScheduling.Tests.csproj -c Release
```

No claim of cross-language equivalence: only `SerializedSolution`, `RemoteTaskSubmitRequest` and the
`configuration`/`sha256` digest have been measured across the two implementations.
