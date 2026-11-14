# Framework.Csp2d

[中文](README_ch.md)

2D cutting stock on top of the OSPF column-generation framework: materials, products, patterns, cut
trees, sheet assignment, pattern generation, and two solver entry points.

Baseline: `ospf-kotlin-framework-csp2d` at Kotlin `12fdb587`.

## Layout

A single assembly with `Application/`, `Domain/`, and `Infrastructure/`. The baseline splits this into
several sub-modules (material model, produce, waste, yield, sheet assignment, pattern generation,
application); **this project does not mirror that split**.

Geometry (`Region2`, `Placement2`, `QuantityPoint2`, `Box2Geometry`) is borrowed from `Framework.Bpp2d`
rather than duplicated — bin packing already owns it.

## What is actually implemented

| Capability | Status |
| --- | --- |
| Material / product / pattern / plan model, ids and validation | ✅ all 18 baseline main sources ported (B447—B465) |
| Cut tree: two-stage and three-stage guillotine, stage rules | ✅ `CutTree2` with real `Validate` against placements |
| Region cutting with kerf | ✅ `Region2.Cut`, non-positive kerf treated as zero |
| Pattern generators: shelf (`Freeform` / two-stage) and three-stage | ✅ real row packing, prefixes, per-process cut trees |
| Master problem: demand, material, machine, over-production, consumed length | ✅ five constraint families + three objectives |
| Sheet assignment: consumed length, sheet count, over-length | ✅ pure function, no model access needed |
| MILP path (`Csp2dMilp`) | ✅ **verified against real SCIP** (B466) |
| Column generation path (`Csp2dColumnGeneration`) | ✅ **verified against real SCIP** (B466) |
| Column addition driven by **reduced costs** | ❌ **not implemented** — see below |

## The one claim this module does not make

Column generation here appends patterns against the **residual demand**, not against the LP dual
prices. `LpResult.DualSolution` is already within reach, so wiring the dual-driven pricing is a
next step rather than a rewrite — but **until it is wired, "column generation" here means residual-driven
repricing**, and the baseline says the same thing in the same words.

Do not read "there is a column generation class" as "reduced-cost pricing works".

## Deliberate simplifications (each with its upgrade path)

| Simplification | Ceiling | Upgrade path |
| --- | --- | --- |
| Geometry borrowed from `Framework.Bpp2d` | cutting stock depends on the bin-packing project graph | extract `Framework.Geometry2d` when a **third** consumer appears |
| `Csp2dSheetAssignmentConfig.OverLengthWeight` is configuration-only | over-length is a hard constraint today | add slack variables when a softened measure is actually required |
| A pattern whose coefficient cannot be read contributes **zero** to the master | one broken pattern silently drops its column | callers must run `Pattern2.Validate` first; that is the documented contract |
| `Material2.AvailableBatches = ulong.MaxValue` registers **no** material row | an "unlimited" material is indistinguishable from a missing one | give it an explicit finite cap if a row is required |
| `Csp2dId` implements all four production id interfaces | semantics live at the interface level only | split into per-role id types if ids must be distinguishable |

## Verification

```bash
# default suite — the real-SCIP cases do not exist here
dotnet build tests/Framework.Csp2d.Tests/Framework.Csp2d.Tests.csproj -c Release
dotnet test tests/Framework.Csp2d.Tests/Framework.Csp2d.Tests.csproj -c Release

# with the real solver — the switch must be on BOTH build and test
dotnet build tests/Framework.Csp2d.Tests/Framework.Csp2d.Tests.csproj -c Release -p:RunScipIntegration=true
dotnet test tests/Framework.Csp2d.Tests/Framework.Csp2d.Tests.csproj -c Release -p:RunScipIntegration=true
```

`102` cases in the default suite, `105` with the real solver. Passing the switch to `test` alone
defines nothing — the `SCIP_INTEGRATION` constant is set at **build** time, so those cases would silently
disappear instead of failing.

No claim of cross-language equivalence and no claim of performance improvement: nothing here was
benchmarked against the Kotlin implementation.
