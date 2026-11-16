# Framework.Csp1d

[中文](README_ch.md)

1D cutting-stock on top of the OSPF column-generation framework: materials, products, cutting-plan
generation, and column generation.

Baseline: `ospf-kotlin-framework-csp1d` at Kotlin `12fdb587`.

## Layout

A single assembly with `Application/`, `Domain/`, and `Infrastructure/`. The baseline splits this into
several sub-modules; **this project does not mirror that split**.

## What is actually implemented

| Capability | Status |
| --- | --- |
| Cutting-plan generators `FullSum` / `NSame` / `NSum` | ✅ three real generation paths |
| **DFS generator** | ❌ **deleted** — it was an always-empty shell |
| Slice-template cache (sequential / concurrent) | ◐ **classes and interface exist, no consumers** |
| Material width-index cache | ◐ same, no consumers |
| Cache hit/miss statistics | ✅ fields, KPI keys and increment methods all exist — **but nothing calls them** |

## The most misleading part of this module

The interface (`IGenerationSliceTemplateCache`), both implementations, the statistics fields, and the KPI
keys are **all present**. The one piece wiring them together — `DFSGenerator` — was a shell whose body was
literally `// Stub implementation: DFS search would go here`, and its tests asserted
`result.Should().BeEmpty()` as the expected outcome.

So the cache hit KPI is **always zero**: nothing ever populates it. Do not read "the statistics fields exist"
as "caching works".

The DFS path is genuinely missing: the baseline builds three caches (quantity, width index, slice template)
and runs a real depth-first search. Porting that is a generator rewrite, not a wiring change.

## Verification

```bash
dotnet test tests/Framework.Csp1d.Tests/Framework.Csp1d.Tests.csproj -c Release
```

No claim of cross-language equivalence: only `SerializedSolution`, `RemoteTaskSubmitRequest` and the
`configuration`/`sha256` digest have been measured across the two implementations.
