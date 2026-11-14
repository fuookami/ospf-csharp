# 示例 5：VRPTW 分支定价

[English](README.md)

本示例把网络调度框架接到可选的 Gurobi 或 SCIP 列生成求解器上。它接受 Demo17 与 Solomon 风格实例，
携带显式的时间与节点上限，并返回框架求解状态以及航线级的时序、装载、距离与成本明细。

对应基线 `ospf-kotlin-example/.../framework_demo/demo5/`
（`Application.kt`、`infrastructure/Solver.kt`、`infrastructure/adapter/*`）。

## 目录结构

| 文件 | 作用 |
| --- | --- |
| `Application.cs` | `SemanticParameter` / `Input` / `Output` DTO 与 `Solve` 入口 |
| `Demo5SolverFactory.cs` | 后端选择（`gurobi` / `scip`，大小写不敏感） |
| `Demo17InstanceAdapter.cs` | Demo17 实例 → 求解器输入 |
| `Solomon/SolomonDataReader.cs` | Solomon 文本实例读取 |
| `Solomon/SolomonInstanceAdapter.cs` | Solomon 实例 → 求解器输入 |
| `CuttingStockDemo.cs` | 同一套接线的切割下料变体 |

## 求解器选择

`Demo5SolverFactory` 显式枚举后端。**未知名称直接失败并指名它**；基线的
`when (...) { "scip" -> …; else -> Gurobi }` 会**静默退回 Gurobi**，于是拼错一个名字（`"gruobi"`）
要到后端不可用时才以一个不相关的失败形式暴露。名称大小写不敏感。

## 运行方式

本示例通过测试工程驱动，而不是命令行：

```bash
dotnet test tests/Example.Tests/Example.Tests.csproj --filter 'FullyQualifiedName~Demo5'
```

注意：

- 工厂**不探测本机**是否装有 SDK：它返回的求解器对象自己携带"后端是否可用"的状态，
  因此同一份代码不会在不同机器上给出不同的可求解性。缺失 SCIP 原生运行时的情况在真正使用后端时才会暴露。
- 适配器与工厂的测试不需要任何求解器即可运行。

## 对齐说明

C# 移植保留了 DTO 形状与求解入口，但本 README **不主张跨语言等价**：只有 `SerializedSolution`、
`RemoteTaskSubmitRequest` 与 `configuration`/`sha256` 摘要这三处做过跨实现实测。
