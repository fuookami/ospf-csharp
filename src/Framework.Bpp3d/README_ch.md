# Framework.Bpp3d

[English](README.md)

基于 OSPF 列生成框架的三维装箱：货物、箱子、层分配、层生成、装箱与渲染。

## 目录结构

四个目录，按层拆而不是按问题域拆：

| 目录 | 内容 |
| --- | --- |
| `Infra/` | 几何原语、放置类型、渲染 DTO、影子价格键、求解器值适配器 |
| `Domain/` | 货物/箱/层模型、层分配、层生成、装箱、块装载、BLA |
| `Application/` | 列生成编排、配置、执行器、解的补充信息 |
| `Framework.Bpp3d.csproj` | 单一程序集 |

按层拆分是有意的：各领域上下文放在一个程序集里，它们的类型可以直接互相引用，
不必为每个上下文开一个项目。

## 实际实现了什么

| 能力 | 状态 |
| --- | --- |
| 长方体装箱 | ✅ |
| 竖直圆柱（`Axis3.Y`），几何形状感知 | ✅ |
| 水平圆柱候选（`Axis3.X` / `Axis3.Z`） | ✅ |
| 装箱几何守卫（包围盒重叠、圆柱圆盘、支撑覆盖） | ✅ |
| 重叠检测的宽相位扫掠剪枝 | ✅ |
| 渲染 DTO（含装载计划与求解器半径回写） | ◐ 见下 |
| 箱容量 / 深度约束 | ◐ 前置校验，不是模型约束——见下 |
| 影子价格感知的层评分 | ◐ 按件数计，不是按重量——见下 |
| 加载顺序约束、尾箱分配约束 | ❌ 需要 `PreciseAssignment`，本项目没有该模型 |

## 三条如实说明

**1. 容量约束是前置校验，不是模型约束。** `BinCapacityConstraint` 与 `BinDepthConstraint` 在**求解前**
拒绝非法数据，因此它们不进 `MetaModel`，也**不改变优化结果**。超载的箱子两种做法都会拒绝；
若要让约束参与优化、影响装箱形状，调用方得自己加那条行。

**2. 影子价格评分按件数计，不是按重量。** `Bpp3dShadowPriceScoring.ContributionOf` 对每个匹配的货物加 1，
无论 `Bpp3dDemandValue` 带的是什么。因此重量型需求按件计分，不按千克计分。

**3. 没有 PWL 体积路径。** 分段线性体积不建模，因此 `SelectedRadiusOf` 没有"同键多条视为歧义而放弃"
的概念：键冲突时返回其中一个候选，而不是拒绝。

## 验证

```bash
dotnet test tests/Framework.Bpp3d.Tests/Framework.Bpp3d.Tests.csproj -c Release
```

不主张跨语言等价：只有 `SerializedSolution`、`RemoteTaskSubmitRequest` 与 `configuration`/`sha256`
摘要这三处做过跨实现实测。基准夹具只证明夹具覆盖，不证明性能。
