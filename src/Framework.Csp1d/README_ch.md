# Framework.Csp1d

[English](README.md)

基于 OSPF 列生成框架的一维下料：物料、产品、切割方案生成与列生成。

基线：Kotlin `12fdb587` 的 `ospf-kotlin-framework-csp1d`。

## 目录结构

单一程序集，含 `Application/`、`Domain/`、`Infrastructure/`。基线把它拆成若干子模块；
**本项目不照搬这个拆分**。

## 实际实现了什么

| 能力 | 状态 |
| --- | --- |
| 切割方案生成器 `FullSum` / `NSame` / `NSum` | ✅ 三条真实生成路径 |
| **DFS 生成器** | ❌ **已删除**——它原本是个恒返回空的空壳 |
| 切片模板缓存（顺序版/并发版） | ◐ **类与接口都在，但没有使用者** |
| 物料宽度索引缓存 | ◐ 同上，无使用者 |
| 缓存命中/未命中统计 | ✅ 字段、KPI 键、自增方法都在——**但没有东西去调用** |

## 本模块最容易误读的地方

接口（`IGenerationSliceTemplateCache`）、两个实现、统计字段、KPI 键**四样都齐**。
把它们串起来的那一段——`DFSGenerator`——是个空壳，方法体里写的就是
`// Stub implementation: DFS search would go here`，而它的测试把 `result.Should().BeEmpty()`
断言成了预期结果。

所以**缓存命中 KPI 恒为 0**：从来没有任何东西去填充它。
不要把"统计字段存在"读成"缓存生效"。

DFS 路径是**真的缺失**：基线要构造三个缓存（数量缓存、宽度索引缓存、切片模板缓存）并跑真正的深度优先搜索。
移植它属于重写一个生成器，不是接线。

## 验证

```bash
dotnet test tests/Framework.Csp1d.Tests/Framework.Csp1d.Tests.csproj -c Release
```

不主张跨语言等价：只有 `SerializedSolution`、`RemoteTaskSubmitRequest` 与 `configuration`/`sha256`
摘要这三处做过跨实现实测。
