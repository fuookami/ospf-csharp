# Framework.Bpp2d

[中文](README_ch.md)

2D bin packing (rectangle/sheet) on top of the OSPF column-generation framework: items, sheets,
layer assignment, layer generation, packing, and rendering.

Baseline: `ospf-kotlin-framework-bpp2d` at Kotlin `12fdb587`.

## Layout

A single assembly with one `Domain/` directory (41 source files). The baseline splits this into
several sub-modules; **this project does not mirror that split**, so the baseline's per-module READMEs are
not a checklist against this directory.

Notable files:

| File | Role |
| --- | --- |
| `ItemContext.cs`, `SheetModel.cs`, `MaterialModel.cs` | Items, sheets, materials |
| `LayerAssignmentModel.cs` | Column-generation layer assignment |
| `LayerGeneration.cs`, `LayerGenerationContext.cs` | Candidate layer generation, including shadow-price ranking |
| `BlaContext.cs`, `BottomUpLeftJustifiedAlgorithm.cs` | Bottom-up left-justified placement |
| `BlockGenerators.cs`, `BlockLoading.cs`, `SpaceSplitAlgorithms.cs` | Block and free-space construction |
| `Packer.cs`, `PackingAggregation.cs`, `PackingRendererAdapter.cs` | Final packing and rendering |
| `PackingGeometryGuard.cs` | Geometry validation before rendering |
| `Box2Geometry.cs`, `RectangleItem2.cs`, `Sheet2Shape.cs` | Geometry primitives |

## Shadow-price ranking

`LayerGenerationContext` ranks candidate layers by a caller-supplied `ScoreByShadowPrice` function
(`Func<Layer<V>, Bpp2dLayerGenerationRequest<V>, V>`), falling back to descending area when no scorer is
supplied. This is the strongly typed pattern that `Framework.Bpp3d` copied in Batch 273 — Bpp3d previously
carried an unread `object?` placeholder.

## Verification

```bash
dotnet test tests/Framework.Bpp2d.Tests/Framework.Bpp2d.Tests.csproj -c Release
```

No claim of cross-language equivalence: only `SerializedSolution`, `RemoteTaskSubmitRequest` and the
`configuration`/`sha256` digest have been measured across the two implementations.
