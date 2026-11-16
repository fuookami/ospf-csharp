# Framework.GanttNetworkScheduling

[English](README.md)

网络路径搜索与甘特业务任务束之间的桥接：一个上下文类 `NetworkBunchGenerationContext`，
以及它所做的完成状态翻译。

## 为什么是独立项目

`Framework.NetworkScheduling` **本来就引用** `Framework.GanttScheduling`：

```
Framework.NetworkScheduling.csproj → Framework.GanttScheduling.csproj
```

把桥接塞进 `Framework.GanttScheduling` 会形成循环引用，所以它单独成一个项目。

## 做什么

`NetworkBunchGenerationContext.Generate` 跑一次 `PathProblem` 搜索，
再通过调用方给的抽取器把每条返回路径转成业务任务束。

**真正的内容是完成状态的翻译**，不是适配循环：

| 路径搜索完成 | 列生成完成 | 理由 |
| --- | --- | --- |
| `Exhausted` | `Exhausted` | 真穷尽 |
| `BudgetLimit` | `BudgetLimit` | 预算耗尽 |
| `Interrupted` | `Interrupted` | 外部中断 |
| `UncertifiedDominance` | **`Heuristic`** | **"跑完了"不等于"证明了"** |
| `UncertifiedValidation` | **`Heuristic`** | 同上 |
| **任意状态 + 有路径被拒** | **`Heuristic`** | "大部分路径可用"不等于"可当作穷尽" |

而 `NoImprovingColumns` **只有在调用方给出独立证据、且完成状态是 `Exhausted` 时才是 `true`**；
其余情况是 `null`（未知），**不是 `false`**。写成 `false` 会让调用方把一次**可能还有更好列**的搜索
读成"确定没有"。

## 不做什么

Demo4 的 `FlightTaskBunchGenerator` **没有**用这个桥接——它跑自己那 248 行标签搜索。
这是有意的结构选择，不是漏调用；它无法验证结果一致，所以只记录、不重写。
见 `docs/kotlin-capability-matrix.md`。

## 验证

```bash
dotnet test tests/Framework.GanttNetworkScheduling.Tests/Framework.GanttNetworkScheduling.Tests.csproj -c Release
```

不主张跨语言等价：只有 `SerializedSolution`、`RemoteTaskSubmitRequest` 与 `configuration`/`sha256`
摘要这三处做过跨实现实测。
