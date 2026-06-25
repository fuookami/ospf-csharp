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
| `Fuookami.Ospf.Benchmark` | BenchmarkDotNet 基准测试（17 个基准） | ✅ 完成 |
| `Fuookami.Ospf.Starters.*` | NuGet 元包（Core、Bpp3d、Csp1d、Framework、GanttScheduling） | ✅ 完成 |

## 架构

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

```bash
dotnet run -c Release --project src/Benchmark -- --filter '*'
```

## 许可证

[Apache License 2.0](LICENSE)
