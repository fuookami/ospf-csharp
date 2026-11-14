# Fuookami.Ospf.Starters.GanttNetworkScheduling

## [zh]

`Fuookami.Ospf.Starters.GanttNetworkScheduling` 是 OSPF (Open-Source Production Scheduling Framework) 的
甘特-网络调度入门包：把网络路径搜索的结果接入甘特任务束生成。

本包提供：

- **Framework.GanttNetworkScheduling** -- 网络路径搜索与甘特任务束之间的桥接（`NetworkBunchGenerationContext`）
- **Framework.GanttScheduling** -- 甘特式任务调度（任务、执行器、时隙、任务束）
- **Framework.NetworkScheduling** -- 资源受限路径搜索与航线生成
- **Framework** -- 调度框架（流程编排、启发式引擎、插件系统）
- **Core** -- 调度模型核心
- **Math** -- 数学规划基础
- **Utils** -- 通用工具集

### 快速开始

```bash
dotnet add package Fuookami.Ospf.Starters.GanttNetworkScheduling
```

### 适用场景

适用于需要"先搜路径、再把路径装进时隙"的场景，例如航班任务分配、车辆路径与排班联动。

### 注意

基线 Kotlin 里这一层是 `gantt-scheduling-domain-network-context` 一个 72 行的类。
本包把它单独成包，是因为 `Framework.NetworkScheduling` **本来就引用**
`Framework.GanttScheduling`，把桥接塞进 `Framework.GanttScheduling` 会形成循环引用。

---

## [en]

`Fuookami.Ospf.Starters.GanttNetworkScheduling` is the Gantt-network scheduling starter package for OSPF
(Open-Source Production Scheduling Framework): it connects network path search results into Gantt bunch
generation.

This package provides:

- **Framework.GanttNetworkScheduling** -- the bridge between network path search and Gantt bunches (`NetworkBunchGenerationContext`)
- **Framework.GanttScheduling** -- Gantt-style task scheduling (tasks, executors, slots, bunches)
- **Framework.NetworkScheduling** -- resource-constrained path search and route generation
- **Framework** -- Scheduling framework (workflow orchestration, heuristic engine, plugin system)
- **Core** -- Scheduling model core
- **Math** -- Mathematical programming foundations
- **Utils** -- General-purpose utilities

### Quick Start

```bash
dotnet add package Fuookami.Ospf.Starters.GanttNetworkScheduling
```

### Use Cases

Suitable for scenarios that "search paths first, then fit them into slots", such as flight task assignment
and joint vehicle routing and rostering.

### Note

In the Kotlin baseline this layer is a single 72-line class inside
`gantt-scheduling-domain-network-context`. It is its own package here because
`Framework.NetworkScheduling` **already references** `Framework.GanttScheduling`, so putting the bridge on
`Framework.GanttScheduling` would close a reference cycle.
