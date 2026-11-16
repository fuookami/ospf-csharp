# Framework.Bpp3d

[中文](README_ch.md)

3D bin packing on top of the OSPF column-generation framework: items, bins, layer assignment,
layer generation, packing, and rendering.

Baseline: `ospf-kotlin-framework-bpp3d` at Kotlin `12fdb587`.

## Layout

Four directories, split by layer rather than by problem family:

| Directory | Contains |
| --- | --- |
| `Infra/` | Geometry primitives, placement types, renderer DTOs, shadow-price keys, solver-value adapters |
| `Domain/` | Item/bin/layer models, layer assignment, layer generation, packing, block loading, BLA |
| `Application/` | Column-generation orchestration, configs, executors, solution enrichment |
| `Framework.Bpp3d.csproj` | The single assembly |

The split is by layer on purpose: every domain context lives in one assembly, so their types can reference
each other without a project-per-context fan-out.

## What is actually implemented

| Capability | Status |
| --- | --- |
| Cuboid packing | ✅ |
| Vertical cylinder (`Axis3.Y`) with shape-aware geometry | ✅ |
| Horizontal cylinder candidates (`Axis3.X` / `Axis3.Z`) | ✅ |
| Packing geometry guard (AABB overlap, cylinder disc, support coverage) | ✅ |
| Broad-phase sweep pruning for overlap detection | ✅ |
| Renderer DTO with loading plans and solver-selected radius writeback | ◐ see below |
| Bin capacity / depth constraints | ◐ pre-check, not model constraint — see below |
| Shadow-price-aware layer scoring | ◐ counts items, not weight — see below |
| Loading-order and tail-bin-assignment constraints | ❌ need `PreciseAssignment`, which does not exist here |

## Three honest caveats

**1. Capacity constraints are pre-checks, not model constraints.** `BinCapacityConstraint` and
`BinDepthConstraint` reject illegal data *before* the solver runs, so they never enter the `MetaModel` and
never change the optimisation result. An overweight bin is refused either way; a model that expects the
constraint to shape the packing has to add the row itself.

**2. The shadow-price score counts items, not weight.** `Bpp3dShadowPriceScoring.ContributionOf` adds 1 per
matching item, whatever the `Bpp3dDemandValue` carries. Weight-type demand therefore scores by headcount,
not by kilogram.

**3. There is no PWL volume path.** Piecewise-linear volume is not modelled, so `SelectedRadiusOf` has no
notion of "several results sharing a key are ambiguous, drop them": a key collision yields one of the
candidates, not a refusal.

## Verification

```bash
dotnet test tests/Framework.Bpp3d.Tests/Framework.Bpp3d.Tests.csproj -c Release
```

No claim of cross-language equivalence: only `SerializedSolution`, `RemoteTaskSubmitRequest` and the
`configuration`/`sha256` digest have been measured across the two implementations. The benchmark fixtures
prove fixture coverage, not performance.
