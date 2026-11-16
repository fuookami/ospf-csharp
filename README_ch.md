# ospf-csharp

[![GitHub license](https://img.shields.io/badge/license-Apache%20License%202.0-green.svg?style=flat)](http://www.apache.org/licenses/LICENSE-2.0)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-latest-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)

:us: [English](README.md) | :cn: 简体中文

## 简介

ospf-csharp 是 [ospf](https://github.com/fuookami/ospf) 的 C# / .NET 10 实现版本——一个用于数学规划（LP/MILP/QP）、列生成、分支定界和元启发式算法的优化求解器包框架。

本项目是从 [ospf-kotlin](https://github.com/fuookami/ospf-kotlin)（Kotlin/JVM）的 1:1 迁移，在 Kotlin 内部 DSL 特性无法直接转换的地方采用惯用 C# 设计。[ospf-rust](https://github.com/fuookami/ospf-rust) 版本作为次要参考。

## 模块文档

| 模块 | 用途 | 状态 |
|------|------|------|
| `Fuookami.Ospf.Utils` | 通用工具、Result 模式、函数式抽象 | ✅ 完成 |
| `Fuookami.Ospf.MultiArray` | 多维数组基础 | ✅ 完成 |
| `Fuookami.Ospf.Math` | 代数、数字、几何、符号、表达式 | ✅ 完成 |
| `Fuookami.Ospf.Quantities` | 物理量和单位系统 | ✅ 完成 |
| `Fuookami.Ospf.Core` | 优化建模核心（MetaModel、求解器接口） | ✅ 完成 |
| `Fuookami.Ospf.Framework` | 框架抽象（CG/Benders、管道、影子价格） | ✅ 完成 |
| `Fuookami.Ospf.Framework.GanttScheduling` | 甘特调度框架（分支定界） | ✅ 完成 |
| `Fuookami.Ospf.Framework.Csp1d` | 一维切割下料框架 | ✅ 完成 |
| `Fuookami.Ospf.Framework.Bpp2d` | 二维装箱框架 | ✅ 完成 |
| `Fuookami.Ospf.Framework.Bpp3d` | 三维装箱框架 | ✅ 完成 |
| `Fuookami.Ospf.Core.Plugin.Heuristic` | 14 种元启发式算法（GA、PSO、SAA、GWO 等） | ✅ 完成 |
| `Fuookami.Ospf.Core.Plugin.Gurobi` | Gurobi 求解器适配器（原生回调支持） | ✅ 完成 |
| `Fuookami.Ospf.Framework.Plugin` | 持久化（MongoDB/SQLite/MySQL/Redis）、Kafka、Roslyn 源生成器 | ✅ 完成 |
| `Fuookami.Ospf.Example` | 核心演示（17 个）+ 框架演示（9 个） | ✅ 完成 |
| `Fuookami.Ospf.Benchmark` | BenchmarkDotNet 基准测试（12 个基准类、54 个基准） | ✅ 完成 |
| `Fuookami.Ospf.Starters.*` | NuGet 元包（Core、Bpp3d、Csp1d、Framework、GanttScheduling） | ✅ 完成 |

表中状态是一份快照；本仓当前提供什么，以 [docs/kotlin-capability-matrix.md](docs/kotlin-capability-matrix.md) 为准。

## 架构

当前能力状态见 [docs/kotlin-capability-matrix.md](docs/kotlin-capability-matrix.md)。

框架采用分层架构：

```
┌─────────────────────────────────────────────┐
│  应用层（CG/B&P 编排）                       │
├─────────────────────────────────────────────┤
│  领域层（上下文、聚合、模型组件、管道）        │
├─────────────────────────────────────────────┤
│  框架层（求解器接口、CG/Benders 抽象、影子价格）│
├─────────────────────────────────────────────┤
│  核心层（MetaModel、变量、令牌、函数原子、求解器输出）│
├─────────────────────────────────────────────┤
│  数学层（代数、符号、表达式、几何、组合）       │
├─────────────────────────────────────────────┤
│  工具层（Result 模式、函数式、上下文、并行、序列化）│
└─────────────────────────────────────────────┘
```

### 关键设计决策

- **Result 模式无异常**：`Result<T,C,E>` 配合 `Ok`/`Failed`/`Fatal`/`Warn`——所有错误通过返回值传播。
- **INumericConstants 注册表**：替代 Kotlin 的 reified 反射来获取数字类型常量。
- **MetaDualSolution 公开工厂**：无反射桥接——直接通过 `Origin`/`From` 属性构造。
- **Flt64 后缀约定**：泛型公共 API 使用自然名；Flt64 类型兄弟使用 `Flt64` 后缀。
- **求解器无关核心**：`Fuookami.Ospf.Core` 和 `Framework` 零供应商 NuGet 引用。

## 要求

- .NET 10.0 SDK 或更高版本
- Gurobi 10+（可选，用于 Gurobi 求解器适配器）

## 构建

```bash
dotnet build ospf-csharp.sln -c Debug
```

## 测试

```bash
dotnet test ospf-csharp.sln --no-build -c Debug
```

## 基准测试

`src/Benchmark` 是一套 BenchmarkDotNet 基准：**12 个基准类、54 个基准方法**，覆盖多维数组访问、核心热路径、
数学准备、符号归一化、索引构建、堆垛/网络准备，以及列生成与约束规划示例负载。

```bash
dotnet run -c Release --project src/Benchmark -- \
  --filter '*' --dataset small --format json --result results/current-small.json
```

运行入口是 `BenchmarkRunOptions`：它解析参数后转发给 `BenchmarkSwitcher`，参数非法时**以退出码 `2` 结束**、
不会开始跑基准：

| 参数 | 含义 |
| --- | --- |
| `--filter <正则>` | BenchmarkDotNet 名称过滤（默认 `.*`） |
| `--dataset <名称>` | 数据集标签，进入结果文件名与趋势报告 |
| `--format <json\|csv\|markdown\|html>` | 结果格式（默认 `json`） |
| `--result <路径>` | 结果文件，默认 `results/<dataset>-<format>` |
| `--forks`、`--warmup`、`--iterations` | 作业次数，均默认 `1` |

**规模由基准类固定**：`[Params]` 是编译期常量，`--dataset` 只影响输出命名，不改变负载。
说成"能改规模"就是没有实测支撑的性能主张，本仓库不作这种主张。

### 基准趋势对比

`scripts/compare-benchmark-results.ps1` 对比两个 BenchmarkDotNet JSON 结果并产出 Markdown 趋势报告
（差值、样本数、单位变化、新增/消失的基准）：

```powershell
pwsh -File scripts/compare-benchmark-results.ps1 `
  -Baseline results/baseline-small.json `
  -Current results/current-small.json
```

按 `baseline-<dataset>.json` / `current-<dataset>.json` 命名时，报告默认写到当前文件同目录的
`trend-<dataset>.md`；目录模式（`-ResultsDir [-Dataset]`）自动配对。报告**只报告不设门槛**：
基准分数对机器敏感，因此没有性能硬门槛。

脚本自身的自检：

```powershell
pwsh -File scripts/test-compare-benchmark-results.ps1
```

## 验证

```bash
# 全方案 Release 强制重建
dotnet build ospf-csharp.sln -c Release -t:Rebuild

# 默认全量测试
dotnet test ospf-csharp.sln -c Release --no-build
```

最近一次实测门禁（`7450 passed` / `1` 项既有 Delaunay skip / `0 failed` / `0` 错误 / `1454` 行警告）
见 `logs/` 下最近一次门禁的归档。
**这些数字是最近一次已验证批次的快照，不是工作树的永久属性。**

## 对齐程度与已知缺口

本仓库**不**作以下主张：

- **没有性能主张。** 基准套件证明的是"夹具确实走到了目标代码路径"，不证明加速；移植的夹具从不断言耗时。
- **没有通用互通主张。** 跨语言验证只做过三处：`SerializedSolution`、`RemoteTaskSubmitRequest`、
  `configuration` / `sha256` 摘要。
- **没有"文件数对齐"主张。** 模块存在不等于能力存在；以能力矩阵为准，不以项目清单为准。
- 已知缺口（列在 `docs/kotlin-capability-matrix.md` 里）包括：
  本仓库**根本没有 `Framework.Bpp1d` 项目**，故 `PatternColumnBenchmark` 无法移植；`Infra.Item.Weight`
  只透传不参与任何约束；PWL 诊断与形状 DTO 缓存未实现；Demo5 各模块 README 仍缺。

## 许可证

[Apache License 2.0](LICENSE)
