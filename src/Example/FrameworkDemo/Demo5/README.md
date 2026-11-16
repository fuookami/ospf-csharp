# Demo 5: VRPTW Branch-and-Price

[中文](README_ch.md)

This demo wires the network-scheduling framework to a selectable Gurobi or SCIP column-generation
solver. It accepts Demo17 and Solomon-style instances, carries explicit time and node limits, and
returns framework solve statuses with route-level timing, load, distance and cost details.

A demo that drives the framework through a solver factory: route-level timing, load, distance and
cost reporting on top of the scheduling domain.

## Layout

| File | Role |
| --- | --- |
| `Application.cs` | `SemanticParameter` / `Input` / `Output` DTOs and the `Solve` entry point |
| `Demo5SolverFactory.cs` | Backend selection (`gurobi` / `scip`, case-insensitive) |
| `Demo17InstanceAdapter.cs` | Demo17 instance → solver input |
| `Solomon/SolomonDataReader.cs` | Solomon text instances |
| `Solomon/SolomonInstanceAdapter.cs` | Solomon instance → solver input |
| `CuttingStockDemo.cs` | The cutting-stock variant of the same wiring |

## Solver selection

`Demo5SolverFactory` enumerates the backends explicitly. An **unknown name fails and names itself**;
a catch-all `else` branch would silently fall back to Gurobi, leaving a typo (`"gruobi"`) to surface later
as an unrelated backend-unavailable failure. Names are case-insensitive.

## Running

The demo is exercised by the test project rather than by a CLI:

```bash
dotnet test tests/Example.Tests/Example.Tests.csproj --filter 'FullyQualifiedName~Demo5'
```

Notes:

- The factory **does not probe the machine** for an SDK: it returns a solver object whose own state records
  whether the backend is available, so the same code does not report different solvability on different
  machines. A missing SCIP native runtime therefore surfaces when the backend is actually used.
- Neither backend is required for the adapter and factory tests — those run without any solver present.

## Parity note

The C# port keeps the DTO shapes and the solve entry point, but this README makes **no claim of
cross-language equivalence**: only `SerializedSolution`, `RemoteTaskSubmitRequest` and the
`configuration`/`sha256` digest have been measured across the two implementations.
