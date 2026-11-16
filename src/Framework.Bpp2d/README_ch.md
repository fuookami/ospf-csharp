# Framework.Bpp2d

[English](README.md)

基于 OSPF 列生成框架的二维装箱（矩形/板材）：货物、板材、层分配、层生成、装箱与渲染。

## 目录结构

单一程序集，一个 `Domain/` 目录（41 个源文件）：货物、板材、层分配、层生成、装箱与渲染都放在一起，
它们的类型可以直接互相引用，不必为每个关注点开一个项目。

主要文件：

| 文件 | 作用 |
| --- | --- |
| `ItemContext.cs`、`SheetModel.cs`、`MaterialModel.cs` | 货物、板材、物料 |
| `LayerAssignmentModel.cs` | 列生成层分配 |
| `LayerGeneration.cs`、`LayerGenerationContext.cs` | 候选层生成，含影子价格排序 |
| `BlaContext.cs`、`BottomUpLeftJustifiedAlgorithm.cs` | 自底向上左对齐放置 |
| `BlockGenerators.cs`、`BlockLoading.cs`、`SpaceSplitAlgorithms.cs` | 块与自由空间构造 |
| `Packer.cs`、`PackingAggregation.cs`、`PackingRendererAdapter.cs` | 最终装箱与渲染 |
| `PackingGeometryGuard.cs` | 渲染前的几何校验 |
| `Box2Geometry.cs`、`RectangleItem2.cs`、`Sheet2Shape.cs` | 几何原语 |

## 影子价格排序

`LayerGenerationContext` 按调用方给的 `ScoreByShadowPrice` 函数
（`Func<Layer<V>, Bpp2dLayerGenerationRequest<V>, V>`）给候选层排序；未提供时退回按面积降序。
`Framework.Bpp3d` 用的是同一套强类型形态——它之前只有一个没人读的 `object?` 占位。

## 验证

```bash
dotnet test tests/Framework.Bpp2d.Tests/Framework.Bpp2d.Tests.csproj -c Release
```

不主张跨语言等价：只有 `SerializedSolution`、`RemoteTaskSubmitRequest` 与 `configuration`/`sha256`
摘要这三处做过跨实现实测。
