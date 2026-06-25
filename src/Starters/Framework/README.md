# Fuookami.Ospf.Starters.Framework

## [zh]

`Fuookami.Ospf.Starters.Framework` 是 OSPF (Open-Source Production Scheduling Framework) 的框架入门包。

本包在核心包基础上提供完整的调度框架层，包括：

- **Core** -- 调度模型核心（变量、约束、目标函数）
- **Framework** -- 调度框架（调度流程编排、启发式引擎接口、插件系统）
- **Math** -- 数学规划基础
- **Utils** -- 通用工具集

### 快速开始

```bash
dotnet add package Fuookami.Ospf.Starters.Framework
```

### 适用场景

适用于需要完整调度框架能力（流程编排、插件扩展）但不需要特定领域模型（装箱、切割等）的项目。

---

## [en]

`Fuookami.Ospf.Starters.Framework` is the framework starter package for OSPF (Open-Source Production Scheduling Framework).

This package extends the core starter with the full scheduling framework layer, including:

- **Core** -- Scheduling model core (variables, constraints, objectives)
- **Framework** -- Scheduling framework (workflow orchestration, heuristic engine interfaces, plugin system)
- **Math** -- Mathematical programming foundations
- **Utils** -- General-purpose utilities

### Quick Start

```bash
dotnet add package Fuookami.Ospf.Starters.Framework
```

### Use Cases

Suitable for projects that need full scheduling framework capabilities (workflow orchestration, plugin extensions) but do not require domain-specific models (bin packing, cutting stock, etc.).
