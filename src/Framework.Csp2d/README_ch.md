# Framework.Csp2d

[English](README.md)

基于 OSPF 列生成框架的二维套裁：物料、产品、图案、切分树、板长分配、图案生成，以及两条求解入口。

基线：Kotlin `12fdb587` 的 `ospf-kotlin-framework-csp2d`。

## 目录结构

单程序集，含 `Application/`、`Domain/`、`Infrastructure/`。基线把它拆成若干子模块（物料模型、
produce、waste、yield、板长分配、图案生成、应用层）；**本项目不照着这个拆**。

几何（`Region2`、`Placement2`、`QuantityPoint2`、`Box2Geometry`）**借自 `Framework.Bpp2d`** 而不是复制一份——
装箱本来就拥有它。

## 实际实现了什么

| 能力 | 状态 |
| --- | --- |
| 物料 / 产品 / 图案 / 方案模型、编号与校验 | ✅ 基线 18 个主源码全部落地 |
| 切分树：两级与三级断头台、级数规则 | ✅ `CutTree2`，`Validate` 真的对照放置做检查 |
| 带刀缝的料域切分 | ✅ `Region2.Cut`，非正刀缝按零处理 |
| 图案生成：货架式（任意排样 / 两级）与三级断头台 | ✅ 真实逐行排样、前缀候选、按工艺出刀序 |
| 主问题：需求、物料、设备、超产、消耗段高 | ✅ 五族约束 + 三个目标 |
| 板长分配：消耗长度、张数、超长 | ✅ 纯函数，不需要访问模型 |
| MILP 路径（`Csp2dMilp`） | ✅ **已对真实 SCIP 实测** |
| 列生成路径（`Csp2dColumnGeneration`） | ✅ **已对真实 SCIP 实测** |
| 按**检验数**驱动列追加 | ❌ **未实现**——见下 |

## 本模块唯一不声称的东西

这里的列生成按**剩余需求**追加图案，而不是按 LP 对偶里的影子价格。
`LpResult.DualSolution` 已经在手边，接上对偶定价是下一步而不是重写——但在接上之前，
**这里的"列生成"就是"按剩余需求重新定价"**，基线的原话也是这么说的。

**不要把"有列生成这个类"读成"检验数定价已经能用"。**

## 有意的简化（每条都留了升级路径）

| 简化 | 天花板 | 升级路径 |
| --- | --- | --- |
| 几何借自 `Framework.Bpp2d` | 套裁依赖装箱项目的引用图 | 出现**第三个**消费者时抽出 `Framework.Geometry2d` |
| `Csp2dSheetAssignmentConfig.OverLengthWeight` 只作为配置 | 超长今天是一条硬约束 | 真的需要软化口径时再加松弛变量 |
| 系数算不出来的图案对主问题贡献**零** | 一个坏图案会静默丢掉它那一列 | 调用方必须先 `Pattern2.Validate`，这就是明文约定 |
| `Material2.AvailableBatches = ulong.MaxValue` **不注册**物料行 | "不限量"与"没配"在模型上无法区分 | 需要这一行时给它一个显式有限上限 |
| `Csp2dId` 同时实现四个生产编号接口 | 语义只在接口层面成立 | 编号必须可区分时拆成按角色的编号类型 |

## 验证方式

```bash
# 默认全量——真实 SCIP 的用例在这里根本不存在
dotnet build tests/Framework.Csp2d.Tests/Framework.Csp2d.Tests.csproj -c Release
dotnet test tests/Framework.Csp2d.Tests/Framework.Csp2d.Tests.csproj -c Release

# 带真实求解器——开关必须**同时**给 build 与 test
dotnet build tests/Framework.Csp2d.Tests/Framework.Csp2d.Tests.csproj -c Release -p:RunScipIntegration=true
dotnet test tests/Framework.Csp2d.Tests/Framework.Csp2d.Tests.csproj -c Release -p:RunScipIntegration=true
```

默认全量 **102** 条，带真实求解器 **105** 条。只给 `test` 传开关什么都不会定义——
`SCIP_INTEGRATION` 常量在 **build** 阶段生效，那些用例会**静默消失**而不是失败。

不声称跨语言等价，也不声称性能提升：这里没有任何一项与 Kotlin 实现做过基准对照。
