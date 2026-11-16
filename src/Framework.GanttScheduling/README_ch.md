# Framework.GanttScheduling

[English](README.md)

基于 OSPF 框架的甘特式任务调度：任务、执行器、时隙、任务束生成与调度生命周期。

基线：Kotlin `12fdb587` 的 `ospf-kotlin-framework-gantt-scheduling`。

## 目录结构

单一程序集，含 `Application/`、`Domain/`、`Infrastructure/`。基线把它拆成很多子模块（26 份 README）；
**本项目不照搬这个拆分**，因此那些 README 不是这三个目录的核对清单。

## 提供什么

| 能力 | 位置 |
| --- | --- |
| 任务与执行器模型 | `Domain/Task/` |
| 任务束生成（按时隙） | `Domain/BunchGeneration/Service/SlotBasedBunchGenerator.cs` |
| 生产率模型 | `Infrastructure/Productivity.cs`、`QuantityProductivity.cs` |
| 调度应用服务 | `Application/` |

## 已知缺口

**尚未重新审计。** 早前批次记录过：甘特任务束编译、执行器/任务约束与取消目标仍含空实现，
Demo4 未注册束成本管线。**这些记录在中没有重新核实**，因此应视为**未确认**而非现状。

`Framework.GanttNetworkScheduling` 桥接（earlier）是独立项目，
因为 `Framework.NetworkScheduling` 本来就引用本模块。

Demo4 的 `FlightTaskBunchGenerator` 用的是自己手写的标签搜索，不是共享的路径搜索内核。
这是**与基线的结构分叉，不是漏调用**——见 `docs/kotlin-capability-matrix.md`。

## 验证

```bash
dotnet test tests/Framework.GanttScheduling.Tests/Framework.GanttScheduling.Tests.csproj -c Release
```

不主张跨语言等价：只有 `SerializedSolution`、`RemoteTaskSubmitRequest` 与 `configuration`/`sha256`
摘要这三处做过跨实现实测。
