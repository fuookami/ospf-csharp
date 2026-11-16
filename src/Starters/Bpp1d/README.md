# Fuookami.Ospf.Starters.Bpp1d

## [zh]

`Fuookami.Ospf.Starters.Bpp1d` 是 OSPF (Open-Source Production Scheduling Framework) 的一维装箱 (1D Bin Packing)入门包。

**新增**：基线的 `ospf-kotlin-starter-bpp1d` 一直有，本仓此前**只有框架工程、没有入门包**——
矩阵那张表却把它写成 ✅。这不是"文档写错了一行"，是**装不上**：没有入门包，用这个模块的人得手写五条工程引用。

本包提供以下依赖：

- **Framework.Bpp1d** -- 一维装箱 (1D Bin Packing)领域模型
- **Framework** -- 调度框架（流程编排、启发式引擎、插件系统）
- **Core** -- 调度模型核心
- **Math** -- 数学规划基础
- **Utils** -- 通用工具集

### 快速开始

```bash
dotnet add package Fuookami.Ospf.Starters.Bpp1d
```

## [en]

`Fuookami.Ospf.Starters.Bpp1d` is the OSPF starter package for one-dimensional bin packing.

**Added**: the baseline has had `ospf-kotlin-starter-bpp1d` all along while this port had **the framework project but no starter** — and the
matrix marked the row ✅ anyway. That is not "one wrong row in a document", it is **cannot be installed**: without a starter, anyone using this module writes five
project references by hand.
