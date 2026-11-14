# Framework.Bpp3d

[中文](README_ch.md)

3D bin packing on top of the OSPF column-generation framework: items, bins, layer assignment,
layer generation, packing, and rendering.

Baseline: `ospf-kotlin-framework-bpp3d` at Kotlin `12fdb587`.

## Layout

The baseline splits BPP3D into eight sub-modules. **This project does not mirror that split** —
it has four directories, and the mapping is not one-to-one:

| Directory | Contains | Roughly corresponds to |
| --- | --- | --- |
| `Infra/` | Geometry primitives, placement types, renderer DTOs, shadow-price keys, solver-value adapters | `bpp3d-infrastructure` |
| `Domain/` | Item/bin/layer models, layer assignment, layer generation, packing, block loading, BLA | `bpp3d-domain-*-context` (all six) |
| `Application/` | Column-generation orchestration, configs, executors, solution enrichment | `bpp3d-application` |
| `Framework.Bpp3d.csproj` | The single assembly | — |

**Do not read the baseline's eight READMEs as a checklist against these four directories.** The split is
different on purpose: C# keeps the domain contexts in one assembly so their types can reference each other
without a project-per-context fan-out.

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

**1. Constraints are pre-checks, not model constraints.** `BinCapacityConstraint` and `BinDepthConstraint`
reject illegal data *before* the solver runs. The baseline adds a linear row to the `MetaModel` so the
constraint participates in optimisation. Both refuse an overweight bin; they differ **in the optimisation
result**. Do not describe them as equivalent.

**2. The shadow-price score counts items, not weight.** `Bpp3dShadowPriceScoring.ContributionOf` adds 1 per
matching item. The baseline weights the contribution by the concrete `Bpp3dDemandValue` (amount value, or
weight value). Weight-type demand therefore scores differently here.

**3. No PWL volume path.** The baseline's `toSchema` uses `actualRadiusSquared` on the PWL path to remove
approximation error. This project has no PWL volume path, and `SelectedRadiusOf` has no notion of "several
results sharing a key are ambiguous, drop them".

## Verification

```bash
dotnet test tests/Framework.Bpp3d.Tests/Framework.Bpp3d.Tests.csproj -c Release
```

No claim of cross-language equivalence: only `SerializedSolution`, `RemoteTaskSubmitRequest` and the
`configuration`/`sha256` digest have been measured across the two implementations. The benchmark fixtures
prove fixture coverage, not performance.
