# ospf-csharp

[![GitHub license](https://img.shields.io/badge/license-Apache%20License%202.0-green.svg?style=flat)](http://www.apache.org/licenses/LICENSE-2.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-latest-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)

:us: English | :cn: [简体中文](README_ch.md)

## Introduction

ospf-csharp is the C# / .NET 10 implementation of [ospf](https://github.com/fuookami/ospf) — an optimization solver package framework for mathematical programming (LP/MILP/QP), column generation, branch-and-price, and metaheuristic algorithms.

It belongs to the [ospf](https://github.com/fuookami/ospf) family and keeps the same capability surface, with idiomatic C# adaptations where Kotlin internal DSL features don't translate directly. The [ospf-rust](https://github.com/fuookami/ospf-rust) version serves as a secondary reference.

## Module Documentation

| Module | Purpose | Status |
|--------|---------|--------|
| `Fuookami.Ospf.Utils` | Common utilities, Result pattern, functional abstractions | ✅ Complete |
| `Fuookami.Ospf.MultiArray` | Multi-dimensional array foundation | ✅ Complete |
| `Fuookami.Ospf.Math` | Algebra, numbers, geometry, symbols, expressions | ✅ Complete |
| `Fuookami.Ospf.Quantities` | Physical quantities and unit system | ✅ Complete |
| `Fuookami.Ospf.Core` | Optimization modeling core (MetaModel, solver interfaces) | ✅ Complete |
| `Fuookami.Ospf.Framework` | Framework abstractions (CG/Benders, pipelines, shadow prices) | ✅ Complete |
| `Fuookami.Ospf.Framework.GanttScheduling` | Gantt scheduling framework (Branch-and-Price) | ✅ Complete |
| `Fuookami.Ospf.Framework.Csp1d` | 1D cutting stock framework | ✅ Complete |
| `Fuookami.Ospf.Framework.Bpp2d` | 2D bin packing framework | ✅ Complete |
| `Fuookami.Ospf.Framework.Bpp3d` | 3D bin packing framework | ✅ Complete |
| `Fuookami.Ospf.Core.Plugin.Heuristic` | 14 metaheuristic algorithms (GA, PSO, SAA, GWO, etc.) | ✅ Complete |
| `Fuookami.Ospf.Core.Plugin.Gurobi` | Gurobi solver adapter (native callback support) | ✅ Complete |
| `Fuookami.Ospf.Framework.Plugin` | Persistence (MongoDB/SQLite/MySQL/Redis), Kafka, Roslyn source generator | ✅ Complete |
| `Fuookami.Ospf.Example` | Core demos (17) + framework demos (9) | ✅ Complete |
| `Fuookami.Ospf.Benchmark` | BenchmarkDotNet benchmarks (54 benchmarks in 12 classes) | ✅ Complete |
| `Fuookami.Ospf.Starters.*` | NuGet metapackages (Core, Bpp3d, Csp1d, Framework, GanttScheduling) | ✅ Complete |

The status column is a snapshot; what this repository provides today is tracked in [docs/kotlin-capability-matrix.md](docs/kotlin-capability-matrix.md).

## Architecture

Current capability status is tracked in [docs/kotlin-capability-matrix.md](docs/kotlin-capability-matrix.md).

The framework follows a layered architecture:

```
┌─────────────────────────────────────────────┐
│  Application Layer (CG/B&P orchestration)   │
├─────────────────────────────────────────────┤
│  Domain Layer (contexts, aggregations,      │
│  model components, pipelines)               │
├─────────────────────────────────────────────┤
│  Framework Layer (solver interfaces,        │
│  CG/Benders abstractions, shadow prices)    │
├─────────────────────────────────────────────┤
│  Core Layer (MetaModel, variables, tokens,  │
│  function atoms, solver output)             │
├─────────────────────────────────────────────┤
│  Math Layer (algebra, symbols, expressions, │
│  geometry, combinatorics)                   │
├─────────────────────────────────────────────┤
│  Utils Layer (Result pattern, functional,   │
│  context, parallel, serialization)          │
└─────────────────────────────────────────────┘
```

### Key Design Decisions

- **Result pattern, no exceptions**: `Result<T,C,E>` with `Ok`/`Failed`/`Fatal`/`Warn` — all errors propagate via return values.
- **INumericConstants registry**: Replaces Kotlin's reified reflection for number type constants.
- **MetaDualSolution public factory**: No reflection bridge — direct construction via `Origin`/`From` properties.
- **Flt64-suffix convention**: Generic public API uses natural names; Flt64-typed siblings use `Flt64` suffix.
- **Solver-agnostic core**: `Fuookami.Ospf.Core` and `Framework` have zero vendor NuGet references.

## Requirements

- .NET 10.0 SDK or later
- Gurobi 10+ (optional, for Gurobi solver adapter)

## Build

```bash
dotnet build ospf-csharp.sln -c Debug
```

## Test

```bash
dotnet test ospf-csharp.sln --no-build -c Debug
```

## Benchmarks

`src/Benchmark` holds a BenchmarkDotNet suite: 54 benchmarks across 12 benchmark classes covering
multi-array access, core hot paths, math preparation, symbolic normalisation, index building,
pile/network preparation and the column-generation / constraint-programming demo workloads.

```bash
dotnet run -c Release --project src/Benchmark -- \
  --filter '*' --dataset small --format json --result results/current-small.json
```

The run entry point is `BenchmarkRunOptions`, which parses the arguments and forwards them to
`BenchmarkSwitcher`. Invalid arguments exit with code `2` instead of running anything:

| Argument | Meaning |
| --- | --- |
| `--filter <regex>` | BenchmarkDotNet name filter (default `.*`) |
| `--dataset <name>` | Dataset tag; in the result file name and the trend report |
| `--format <json\|csv\|markdown\|html>` | BenchmarkDotNet result format (default `json`) |
| `--result <path>` | Result file; defaults to `results/<dataset>-<format>` |
| `--forks`, `--warmup`, `--iterations` | Job counts, all default to `1` |

**Scale is fixed per benchmark.** `[Params]` values are compile-time constants in the benchmark class;
`--dataset` only labels the output, it does not change the workload. Saying otherwise would be a false
performance claim, and this repository does not make one.

### Benchmark trend comparison

`scripts/compare-benchmark-results.ps1` compares two BenchmarkDotNet JSON files and writes a Markdown
trend report (delta, sample counts, unit changes, benchmarks that appeared or disappeared):

```powershell
pwsh -File scripts/compare-benchmark-results.ps1 `
  -Baseline results/baseline-small.json `
  -Current results/current-small.json
```

With the `baseline-<dataset>.json` / `current-<dataset>.json` naming the report is written to
`trend-<dataset>.md` next to the current file; directory mode (`-ResultsDir [-Dataset]`) pairs the files
automatically. The report is **report-only**: benchmark scores are machine-sensitive, so there is no
performance gate.

Self-check for the script itself:

```powershell
pwsh -File scripts/test-compare-benchmark-results.ps1
```

## Verification

```bash
# Release rebuild of the whole solution
dotnet build ospf-csharp.sln -c Release -t:Rebuild

# Default test suite
dotnet test ospf-csharp.sln -c Release --no-build
```

The latest measured gate (`7450 passed`, `1` pre-existing Delaunay skip, `0 failed`, `0` errors,
`1454` warning lines) comes from the most recent gate run; `logs/` keeps that run's archives. **Treat those numbers as a snapshot of the last verified batch, not as
a permanent property of the tree.**

## Parity and Known Gaps

What this repository does **not** claim:

- **No performance claims.** The benchmark suite proves that a fixture exercises the intended code path.
  It does not prove a speedup, and the ported fixtures never assert timings.
- **No general interoperability claim.** Cross-language validation was measured in exactly three places:
  `SerializedSolution`, `RemoteTaskSubmitRequest`, and the `configuration` / `sha256` digest.
- **No "file count parity" claim.** A module existing is not the same as its capabilities existing; the
  capability map is the authority, not the project list.
- Known gaps (listed in `docs/kotlin-capability-matrix.md`) include: `Framework.Bpp1d` is present but
  `PatternColumnBenchmark` has no implementation here; `Infra.Item.Weight` is carried but takes part in no
  constraint; PWL diagnostics and the shape-DTO cache are not implemented; Demo5 module READMEs are still missing.

## License

[Apache License 2.0](LICENSE)
