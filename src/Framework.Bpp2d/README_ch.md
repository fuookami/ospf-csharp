# Framework.Bpp2d

[English](README.md)

基于 OSPF 列生成框架的二维装箱（矩形/板材）：货物、板材、层分配、层生成、装箱与渲染。

基线：Kotlin `12fdb587` 的 `ospf-kotlin-framework-bpp2d`。

## 目录结构

单一程序集，一个 `Domain/` 目录（41 个源文件）。基线把它拆成若干子模块；
**本项目不照搬这个拆分**，因此基线那些子模块 README **不是这个目录的核对清单**。

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
这正是 `Framework.Bpp3d` 在 earlier 照搬的强类型形态——Bpp3d 之前只有一个没人读的 `object?` 占位。

## 验证

```bash
dotnet test tests/Framework.Bpp2d.Tests/Framework.Bpp2d.Tests.csproj -c Release
```

不主张跨语言等价：只有 `SerializedSolution`、`RemoteTaskSubmitRequest` 与 `configuration`/`sha256`
摘要这三处做过跨实现实测。
