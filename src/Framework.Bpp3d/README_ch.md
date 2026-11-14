# Framework.Bpp3d

[English](README.md)

基于 OSPF 列生成框架的三维装箱：货物、箱子、层分配、层生成、装箱与渲染。

基线：Kotlin `12fdb587` 的 `ospf-kotlin-framework-bpp3d`。

## 目录结构

基线把 BPP3D 拆成**八个子模块**。**本项目不照搬这个拆分**——只有四个目录，且映射不是一一对应：

| 目录 | 内容 | 大致对应 |
| --- | --- | --- |
| `Infra/` | 几何原语、放置类型、渲染 DTO、影子价格键、求解器值适配器 | `bpp3d-infrastructure` |
| `Domain/` | 货物/箱/层模型、层分配、层生成、装箱、块装载、BLA | `bpp3d-domain-*-context`（六个全部） |
| `Application/` | 列生成编排、配置、执行器、解的补充信息 | `bpp3d-application` |
| `Framework.Bpp3d.csproj` | 单一程序集 | — |

**不要把基线的八份 README 当成这四个目录的核对清单。** 拆分方式是有意不同的：
C# 把各领域上下文放在一个程序集里，这样它们的类型可以直接互相引用，不必为每个上下文开一个项目。

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

**1. 约束是前置校验，不是模型约束。** `BinCapacityConstraint` 与 `BinDepthConstraint` 在**求解前**
拒绝非法数据。基线往 `MetaModel` 加一条线性约束，让它参与优化。两者都会拒绝超载的箱子，
但**在优化结果上不同**。不要把它们描述成等价。

**2. 影子价格评分按件数计，不是按重量。** `Bpp3dShadowPriceScoring.ContributionOf` 对每个匹配的货物加 1。
基线按具体的 `Bpp3dDemandValue` 加权（数量值或重量值）。因此重量型需求的评分在这里不同。

**3. 没有 PWL 体积路径。** 基线的 `toSchema` 在 PWL 路径用 `actualRadiusSquared` 消除近似误差。
本项目没有 PWL 体积路径，`SelectedRadiusOf` 也没有"同键多条视为歧义而放弃"的概念。

## 验证

```bash
dotnet test tests/Framework.Bpp3d.Tests/Framework.Bpp3d.Tests.csproj -c Release
```

不主张跨语言等价：只有 `SerializedSolution`、`RemoteTaskSubmitRequest` 与 `configuration`/`sha256`
摘要这三处做过跨实现实测。基准夹具只证明夹具覆盖，不证明性能。
