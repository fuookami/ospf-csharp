# Framework.NetworkScheduling

[English](README.md)

基于 OSPF 框架的资源受限路径搜索与航线生成。

基线：Kotlin `12fdb587` 的 `ospf-kotlin-framework-network-scheduling`。

## 目录结构

单一程序集，含 `Application/`、`Domain/`、`Infrastructure/`。基线把它拆成若干子模块；
**本项目不照搬这个拆分**。

## 提供什么

| 能力 | 位置 |
| --- | --- |
| 资源标签路径搜索 | `Domain/Path/PathSearchContext.cs` |
| 路径问题定义（状态转移、终态、支配） | `Domain/Path/PathSearchContext.cs` |
| 路径编译 | `Domain/PathCompilation/` |
| ESPPRC 定价 | `Domain/RouteGeneration/Service/EspprcPricer.cs` |
| 航线图构造 | `Domain/RouteGeneration/Service/RouteGraphBuilder.cs` |
| 网络图原语 | `Infrastructure/NetworkGraph.cs` |

## 完成状态不等于穷尽

`PathSearchResult.Completion` 区分 `Exhausted` 与 `BudgetLimit`、`Interrupted`、
`UncertifiedDominance`、`UncertifiedValidation`。**搜索跑完了不等于证明了没有更好的**
——后三种按启发式结果计，不是穷尽证明。

空结果集同样不代表穷尽。把"没找到路径"读成"路径不存在"的代码，会安静地接受一次被截断的搜索。

## 调用方

`Framework.GanttNetworkScheduling` 把这些路径适配成业务任务束（earlier）。
注意本模块**本来就引用** `Framework.GanttScheduling`，所以桥接放在独立项目里。

## 验证

```bash
dotnet test tests/Framework.NetworkScheduling.Tests/Framework.NetworkScheduling.Tests.csproj -c Release
```

不主张跨语言等价：只有 `SerializedSolution`、`RemoteTaskSubmitRequest` 与 `configuration`/`sha256`
摘要这三处做过跨实现实测。
