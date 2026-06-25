# ospf-csharp

[![GitHub license](https://img.shields.io/badge/license-Apache%20License%202.0-green.svg?style=flat)](http://www.apache.org/licenses/LICENSE-2.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-latest-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)

:us: English | :cn: [简体中文](README_ch.md)

## Introduction

ospf-csharp is the C# / .NET 10 implementation of [ospf](https://github.com/fuookami/ospf) — an optimization solver package framework for mathematical programming (LP/MILP/QP), column generation, branch-and-price, and metaheuristic algorithms.

This project is a 1:1 migration from [ospf-kotlin](https://github.com/fuookami/ospf-kotlin) (Kotlin/JVM), with idiomatic C# adaptations where Kotlin internal DSL features don't translate directly. The [ospf-rust](https://github.com/fuookami/ospf-rust) version serves as a secondary reference.

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
| `Fuookami.Ospf.Benchmark` | BenchmarkDotNet benchmarks (17 benchmarks) | ✅ Complete |
| `Fuookami.Ospf.Starters.*` | NuGet metapackages (Core, Bpp3d, Csp1d, Framework, GanttScheduling) | ✅ Complete |

## Architecture

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

```bash
dotnet run -c Release --project src/Benchmark -- --filter '*'
```

## License

[Apache License 2.0](LICENSE)
