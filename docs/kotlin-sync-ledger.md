# P4—P9 阶段核算台账 / Phase Accounting Ledger

**这份台账由 B1016 建立。** 它回答一个问题：**每个阶段到底"搬完了什么、还缺什么、缺的那部分为什么搬不了"。**

**This ledger was created in B1016.** It answers one question: **for each phase, what was ported, what is missing, and why the missing part cannot be ported.**

---

## 一、逐阶段核算

### P4：远程求解协议与适配（Batch 1—54）

| 项 | 结论 | 依据 |
| --- | --- | --- |
| 基线文件数 | `solver/remote` 共 **23 个** | `P4 Batch 54` 逐文件表 |
| 已移植 | 21 个 | 同上 |
| 非缺口 | 2 个（`Http4kRemoteSolverHttpTransport.kt` JVM 专有，C# 用 `HttpClient`） | 同上 |
| **状态** | **已收口** | `P4 Batch 54` 的「P4 收口结论」节 |

**不可搬项及其原因**：`Http4kRemoteSolverHttpTransport` —— **依赖 JVM 的 http4k 库**，
C# 侧的等价物是 `HttpClient`，**这不是"缺一个类"，是同一个能力的另一种写法。**

### P5：启发式与元启发式（Batch 55—65）

| 项 | 结论 |
| --- | --- |
| 收口条目 | `P5 Batch 65：不可复制 payload 拒绝 + P5 收口盘点` |
| **状态** | **已收口** |

### P6：求解报告与模型身份（Batch 66—98）

| 项 | 结论 |
| --- | --- |
| 收口条目 | `### P6 阶段收口（B1015 补写）` |
| **状态** | **已收口，但收口条目是 B1015 补写的** |

**这一行必须留着**：状态文档从某个批次起就写着"P4 through P8 are closed"，
**而 P6 从来没有收口条目**。B1015 补上了证据，**但那句声明在此之前对 P6 是无据的。**

### P7：网络调度（Batch 99—151）

| 项 | 结论 |
| --- | --- |
| 收口条目 | `P7 Batch 151：P7 收口 —— network_scheduling 140/140 文件全量映射` |
| 覆盖 | **140/140 文件** |
| **状态** | **已收口** |

### P8：共享框架与装箱下料（Batch 152—241）

| 项 | 结论 |
| --- | --- |
| 收口条目 | `### P8 阶段收口（B1015 补写）` |
| **状态** | **已收口，但收口条目是 B1015 补写的**（同 P6） |

### P9：文档、基准与收尾（Batch 242—进行中）

| 项 | 结论 |
| --- | --- |
| 当前批次 | 见 `docs/kotlin-sync-status.md` 的「上次更新」 |
| **状态** | **进行中** |

---

## 二、"搬不了"的三类原因

**这三类必须分清**，因为它们的下一步动作完全不同：

| 类别 | 含义 | 下一步 |
| --- | --- | --- |
| **环境所限** | 代码能搬，但本机没有许可 / 实例 / SDK | **换个环境就能验** |
| **语言所限** | 基线依赖 JVM 专有库 | **不是缺口，是等价替换** |
| **需真实对偶 / assumption 来源** | 编排已搬（B1041/1049/1050 用假后端实测），**但假后端拿不出真实对偶与冲突种子** | **接上真实后端才能验这一层** |

### 环境所限的实测清单

| 项 | 缺什么 | 实测证据 |
| --- | --- | --- |
| Mosek | licence 文件 | `err_missing_license_file(1008)` |
| MindOpt | 有效 licence | `License is not valid` |
| Hexaly | SDK | `HEXALY_HOME` 未设 |
| Gurobi 11 | 托管程序集 | `Gurobi110ManagedAssembly` 未设，走 10.x 绑定 |
| MySQL | 凭据 | `Access denied for user 'root'@'172.18.0.1'` |
| PostgreSQL | 凭据 | `28P01: password authentication failed` |

**注意：MySQL 与 PostgreSQL 的服务器是开着的（3306 / 5432 可连）。**
**缺的是凭据，不是服务** —— 这条区分很重要，它决定了"换个凭据就能验"而不是"要装一个数据库"。

### 语言所限的实测清单

| 基线 | C# 等价物 |
| --- | --- |
| `Http4kRemoteSolverHttpTransport.kt` | `HttpClient` |
| H2 / JDBC client / Ktorm / MyBatis | **未评估** .NET 侧等价替代（EF Core / Dapper 等） |

**第二行是唯一一条"还没评估"的**，因此它在能力矩阵里被标成 ❌ 并点名"未评估"。

### 需真实对偶 / assumption 来源的实测清单

**这一节在 B1051 被改写过，因为原来的写法把话说大了。**

原来那三行都写的是"本体得接真后端才能验"这个意思（原文已删）。**B1041、B1049、B1050 逐条推翻了这句话：**
敏感性编排用假 `IFixedIntegerLpBackend` 跑通了；目标可达性的整个判定层
（见证校验、位置映射、未知提升）**一个求解器都不需要。**

**假后端能证明什么、不能证明什么，必须分开说：**

| | 假后端能证明 | 假后端证明不了 |
| --- | --- | --- |
| 编排 | 顺序、早退、状态映射、丢弃语义 | — |
| 证据 | — | **对偶值本身、unsat core 本身** |

| 项 | 逻辑状态 | 真正缺什么 |
| --- | --- | --- |
| `ConflictAnalyzer.analyze` | 门控 + 删除收缩 + 见证 + 映射全搬 | **真实 solver 的 assumption / unsat-core 抽取** |
| `ConstraintPerturbationAnalyzer.analyze` | 判定 + 编排全搬 | **真实求解器给出的目标值变化** |
| `FixedIntegerLpSensitivityAnalyzer.analyze` | 编排全搬（B1041） | **真实 LP 对偶值** |

**"缺类本体"这个说法已经不成立了。** 缺的是**证据的来源**，不是**编排的代码**。


---

## 三、`core/analysis` 的 19 个基线文件逐一对表

**基线 `core/analysis/` 共 19 个 `.kt` 文件；C# 侧 `src/Core/Analysis/` 共 52 个 `.cs` 文件。**

文件数不等**不是缺口**：C# 侧把一部分逻辑拆成了独立的 Support / Seed / Assembler 文件。
**下面是逐个对表的结论。**

| 基线文件 | C# 对应 | 结论 |
| --- | --- | --- |
| `ActivityEvidence.kt` | `ActivityEvidence.cs` | 同名 |
| `ActivityStatus.kt` | `ActivityStatus.cs` | 同名 |
| `AnalysisStatus.kt` | `AnalysisStatus.cs` | 同名 |
| `CapabilityMatrix.kt` | `CapabilityMatrix.cs` | 同名 |
| `ConflictAnalyzer.kt` | `ConflictEvidenceSupport` / `ConflictDeletionCheck` / `AssumptionConflictSeed` / `NativeConflictSeed` / `ConflictExplanationAssembler` / `BudgetGate` / `MinimalBlockingSet` / `ConflictAnalysisPreflight` | 逻辑全搬（`budgetExceeded` 于 B1030、`MinimalBlockingSet` 于 B1032、`analyze` 前三道门于 B1042）；`analyze` 门控与收缩全搬；**缺真实 solver assumptions** |
| `ConstraintActivityAnalyzer.kt` | `ConstraintActivityAnalyzer.cs` | 同名 |
| `ConstraintActivityReport.kt` | `ConstraintActivityReport.cs` | 同名 |
| `ConstraintPerturbationAnalyzer.kt` | `ConstraintPerturbationSupport` / `ConstraintPerturbationPipeline` / `PerturbationObservationFactory` | 逻辑与编排全搬（`unknownObservation` 于 B1031）；`analyze` 编排全搬；**缺真实求解器的目标值变化** |
| `ConstraintProgrammingAnalysisBackends.kt` | `ConstraintProgrammingAnalysisBackends.cs` | 同名；四个成员全搬 |
| `CorrectionRecommendation.kt` | `CorrectionRecommendation.cs` / `CorrectionProjection.cs` | 全搬 |
| `CriticalConstraintAnalysisPipeline.kt` | `CriticalPipelineStages` / `CriticalConstraintPipeline` | 五阶段编排全搬；缺 `analyzeAndExplain` 与 `rankConstraintCandidates` |
| `CriticalConstraintAnalysisReport.kt` | `CriticalConstraintAnalysisFullReport.cs` / `CriticalConstraintAnalysisReportBuilder.cs` | 同名 + 拆出 |
| `CriticalConstraintAnalysisSession.kt` | `CriticalConstraintAnalysisSession.cs` | 同名 |
| `CriticalityProfile.kt` | `CriticalityProfile.cs` / `CriticalityProfileAnalysisResult.cs` | 同名 + 拆出；`ValidateTargetBatch` 于 B1018、批次结果类型于 B1035 |
| `DiagnosticActivation.kt` | `DiagnosticActivation.cs` | 同名；成员全搬 |
| `DiagnosticSource.kt` | `DiagnosticSource.cs` | **B1071 更正**：原写"同名"，而 `VariableBound.Kind` 曾是契约外的第六个词、且与自身 `StableId` 不一致；已改为按边界方向派生 |
| `FixedIntegerLpSensitivity.kt` | `FixedIntegerLpSensitivity.cs` / `FixedIntegerLpGates.cs` / `ProvenanceDualMapping.cs` / `FixedIntegerLpSensitivityAnalyzer.cs` | 判定与编排全搬（`analyze` 于 B1041）；**降阶适配器于 B1066 补**；**求解器直连那个后端卡在求解输出层没有对偶**（B1067 起有绊线判据） |
| `ObjectiveTarget.kt` | `ObjectiveTarget.cs` | **B1059 更正：原写"同名"，而 8 个工厂方法一直缺**——相对 2 + 预算单位 2 + 别名 4，已补 |
| `TargetFeasibilityAnalyzer.kt` | `TargetFeasibilityAnalyzer.cs` / `EffectiveDomain.cs` | `effectiveDomain` / `copyDomain` 已于 B1023 补；**三个变量收集器判为非缺口**（`Variables` 已覆盖 14 变体） |

**合计：19 / 19 有对应；其中 3 个集中式分析器的编排已搬并用假后端实测过（B1041/1049/1050），
真正还缺的是真实后端给出的对偶、assumption 与目标值变化。**

---

## 四、查过之后"不是缺口"的四项

**这三项看起来缺，实测都已经在别处提供了。** 记在这里，免得下一个人重新写一遍。

| 基线函数 | 实测结论 | 依据 |
| --- | --- | --- |
| `collectConstraintVariables` / `collectExpressionVariables` / `collectLiteralVariable` | **不是缺口**：`ConstraintProgrammingConstraint.Variables` 已在全部 **14 个变体**上覆盖，且基类是 `abstract`（漏写直接编译失败） | `PhantomGapTests`（3 条） |
| `ConstraintProgrammingPerturbationBackend.solve` | **不是缺口**：薄封装，逻辑已在 `ConstraintPerturbationPipeline` 里 | B1019 |
| `capabilityAllowed`（`FixedIntegerLpSensitivity` 私有副本） | **不是缺口**：与 `ConstraintPerturbationSupport.CapabilityAllowed` 逐行一致 | B1019 |
| `ConflictAnalysisOptions.validation` | **不是缺口**：`Validate()` 两条条件齐全，且 `CriticalAnalysisOptionsTests` 已钉零值合法与负值非法 | B1033 |

**"缺一个函数"有两种可能：真的没写，和"别处已经提供了"。**
后者如果只靠人记着，**下一任维护者会重新写一遍遍历逻辑——而那份遍历会漏掉后来新增的变体。**

**"A missing function" has two explanations: it was never written, or something else already provides it.** If the second only lives in someones memory, the next maintainer writes the
traversal again — **and that traversal will miss whatever variants were added later.**

---

## 四之二、判"不是缺口"的三步法

**"这个函数没搬"这个判断本身要经过三步，跳过任何一步都会误判。**

| 步 | 问什么 | 判错的后果 |
| --- | --- | --- |
| 1 | **同名或近名的实现存在吗？** | 把"拆到别的文件"读成"没写" |
| 2 | **它的每个条件都覆盖了吗？** | 存在但缺一条分支，比完全没有更难发现 |
| 3 | **它有判据吗？** | 存在、完整、但没人钉住——**下一次改动就能悄悄改坏它** |

**第 3 步最容易被跳过**，因为前两步通过之后"看起来就没事了"。
**而 B1032 与 B1033 各有一项是因为第 3 步才被确认的：**

| 项 | 第 1 步 | 第 2 步 | 第 3 步 |
| --- | --- | --- | --- |
| `MinimalBlockingSet` | 只有属性别名 | **别名丢了最小性** | 无 |
| `ConflictAnalysisOptions.validation` | `Validate()` 存在 | 两条条件都在 | **`CriticalAnalysisOptionsTests` 已钉零值与负值** |

**第一项三步都不过 → 补类型（B1032）。第二项三步都过 → 记下来，不重写（B1033）。**

**The third step is the one most often skipped**, because passing the first two makes everything look fine. **And it is what separated B1032 from B1033:** the first failed all three steps and got a type; the second passed all three and got recorded instead of rewritten.

---

## 四之三、第 1 步要搜"成员名"，不是"容器名"

**B1036 我搜错了东西，于是写了一份重复实现。**

我搜 `ConstraintProgrammingAssumptionMapper` 与 `FixedIntegerProjection`（两个 Kotlin `object` 的名字），
两个都"缺"，于是往 `ConstraintProgrammingAnalysisBackends` 里加了 `Assumptions` 与 `FixedValues`。
**编译失败：这两个成员早就在了，在第 99 行和第 127 行。**

**为什么名字对不上而行为一样**：Kotlin 的 `object` / `companion object` 在 C# 侧被压平成静态类，
**所以"容器"没了而"成员"还在。搜容器名必然搜不到。**

| 搜什么 | 例子 | 结果 |
| --- | --- | --- |
| **容器名**（Kotlin `object`） | `FixedIntegerProjection` | **假缺口** |
| **成员名**（函数名） | `FixedValues` | 命中第 127 行 |

**Kotlin `object` and `companion object` are flattened into static classes on the C# side**, so **the container disappears while the member remains. Searching the container name is guaranteed to miss.**

**这条错误的代价**：一份重复实现、一次编译失败、一次回滚。**收益**：`FixedValues` 与 `Assumptions` 现在有名字可搜了。

**The cost**: a duplicate implementation, a compile failure and a rollback. **The gain**: `FixedValues` and `Assumptions` are now searchable by name.

---

## 五、这份台账的用法

**每次交接时，先读这一节，再读 `docs/kotlin-sync-status.md`。**

理由：状态文档说的是**结论**，这份台账说的是**依据**。
**结论可以抄下去，依据不能。**

**On every hand-off, read this section before the status document.** The status states conclusions; this ledger states their basis. **Conclusions can be copied forward; a basis cannot.**

## 六、轮次上限交接（B1028 收口时）

**这份台账写于 1000 轮上限用完时。** 目标**没有完成**，下面是下一个会话需要的全部事实。

### 6.1 已经稳定的事实

| 事实 | 值 | 怎么核 |
| --- | --- | --- |
| Release 重建 | `0 errors` / **`1316`** 行警告 | `PhaseLedgerTests.TheWarningCount_ShouldMatchTheMeasurement` |
| 集成全量 | **`10247 passed`** / 0 failed | `logs/p8-batch1028-test.log` |
| 默认全量 | **`10083 passed`** / 0 failed | `logs/p8-batch1028-default-test.log` |
| 三个真实后端 | Gurobi **843** / Cplex **26** / Copt **37** | `logs/p8-batch1028-*.log` |
| 文档钉 | **`93/93`** | 见下方筛选式 |

### 6.2 每一批的固定流程

1. 从固定基线读源码：`git -C ../ospf-kotlin cat-file -p 12fdb587:<path>`。**不切换 checkout。**

   **先跑 `python scripts/scan-kotlin-baseline.py`**（B1069 起，门禁脚本第一步）。
   **它把"还缺什么"从印象变成列表**：19 个分析文件的函数 / 类 / 枚举成员 / 属性逐名核对。
   **未解释的名字会非零退出，门禁在编译之前就停。**
   **理由：`"少搬了一个类型"和"编译不过"是两类问题，而前者编译是过得去的。**

2. 搬一段**纯逻辑**（不碰求解器的判定/映射/门控）。
3. 写**最小复现判据**，并做**变异验证**：故意改坏，确认判据真的红。
   **变异必须能编译**——编译失败的变异跑的是旧二进制，"通过"不是证据。
4. 更新 `docs/kotlin-capability-matrix.md`（附录）、`logs/kotlin-sync-gap-inventory.md`、`logs/kotlin-sync-plan.md`、`docs/kotlin-sync-status.md`。
5. 跑门禁脚本 `logs/gNNNN.ps1`，归档 `logs/tmpNNNN-*.log` → `logs/p8-batchNNNN-*.log`。
   **脚本本身也按批次号保留，不删**（B1058 起）。
   **理由是可复核**：只有日志没有脚本时，"这些数字是怎么算出来的"无法复查，
   **而一个把 `Passed:` 正则写错的脚本会永远数出 0——0 看起来像"这批没有测试"。**
   判据 `GateScriptContractTests` 读最近一份脚本来确认它仍在数这些东西。

6. 警告数**必须回到 1316**。多出来的每一条都要修掉，不是接受。

### 6.3 还没做的事（按优先级）

| 项 | 为什么没做 | 怎么做 |
| --- | --- | --- |
| `LinearSolverFixedIntegerLpBackend` | **求解输出层没有对偶字段**（`src/Core/Solver/Output/` 零 dual，B1067 绊线钉住） | 先给求解输出加对偶，再搬这个适配器 |
| 三个集中式分析器的 `analyze` **证据来源** | **编排已搬并用假后端实测**（B1041/1049/1050），假后端拿不出真实对偶与 unsat core | 接上 SCIP 的 conflict 能力，只为**取到证据**，不是为写编排 |
| 跨语言互通实测 | **契约层已实测，运行层未实测。** B1071—B1076 把基线 `analysis-fixtures/` 的 `analysis-contract.tsv` 与 `analysis-cases.tsv` **逐字节**搬进来（SHA-256 钉住），并逐节断言：契约 10 节全部有判据，用例表 6 节共 56 行全部有判据。**未实测的部分**：没有 Kotlin↔C# 双跑进程，没有 integritySha256 对齐，没有求解输出互换。**"契约一致"不等于"两端跑出同一个结果"。** | 要真做运行层，需要两边同时起进程的 fixture；契约层到此为止 |
| 性能对比 | **没有隔离环境**，本机负载不可控 | 单独机器或容器 |
| MySQL / PostgreSQL 活库往返 | **服务器开着，凭据不在** | 拿到凭据即可，不用装数据库 |
| Mosek / MindOpt / Hexaly / Gurobi 11 | 许可或 SDK 不在本机 | 换环境 |

**前三项属于"需要真实后端"或"需要双跑环境"，不是"还没写"。**
**后三项属于"环境所限"，代码已经就位。**

### 6.5 基线函数名的逐名判定（B1059—B1064）

**做法**：把 19 个 `src/main/.../core/analysis/*.kt` 里每个 `fun` 名字抓出来，
**逐个在 `src/Core/Analysis/*.cs` 里找落脚点。** 手写对表漏过东西，**这次是数出来的。**

16 个名字对不上，判定如下。**标"缺口"的四行各有一批把它补上了。**

| 基线名字 | 判定 | 落点 / 补上的批次 |
| --- | --- | --- |
| `atLeast` / `atMost` 之外那 8 个工厂 | **缺口** | **B1059**：相对 2 + 预算单位 2 + 别名 4 |
| `activeDomains` | **缺口** | **B1061**：`EffectiveDomain.ActiveDomains` |
| `rebindExpression` / `rebindConstraint` / `rebindInterval` | **缺口** | **B1061**：`EffectiveDomain.RebindDomain` + `Rebuild(activeSources)` |
| `rankConstraintCandidates` | **缺口** | **B1062**：`CriticalPipelineStages.RankConstraintCandidates` |
| `inferredSupport` | 非缺口 | C# 叫 `InferSupport`，逐支一致 |
| `criticalityClassifications` | 非缺口 | `CriticalityProfile.Classify`，连 `total > 1` 都在 |
| `analyzeCriticalityTargets` | 编排 + 纯逻辑 | 编排要真 pipeline；**聚合段于 B1063 补**（`AggregatePlans`） |
| `buildCriticalityProfile` / `tryBuildCriticalityProfile` | 非缺口 | `CriticalityProfileBuilder.Build` / `TryBuild` |
| `analyzeAndExplain` | 非缺口 | `analyze` + `BlockingSummary()`，三支一致 |
| `mapOriginalDuals` | 非缺口 | `ProvenanceDualMapping.MapRanges` + `RemapSingleRowRanges` |
| `propagateBuilderFailure` | 非缺口 | `PropagateBackendFailure` |
| `collectConstraintVariables` / `collectExpressionVariables` / `collectLiteralVariable` | 非缺口 | B1023 已判：`Variables` 覆盖 14 变体 |

**一条已知的有意分歧**：`BlockingSummary()` 的英文半句，基线写成
`Minimal blocking set ($minimality)`，而 `$minimality` 是中文——
**输出会是"Minimal blocking set (已验证最小)"。** C# 侧按语言分开取词。
**这不是"照搬"，是有意修掉的**，记在这里以免下次被当成不一致。

### 6.4 交接时最该读的三句

1. **"没测出来"不能写成"没测出结论"** —— 出现过 6 次（B983 / B992 / B994 / B999 / B1007 / B1009）。
2. **"判据名不副实"** —— **B1057 更新：实测已到第 12 次。**

   前 8 次：B998 / B1002 / B1008 / B1009 / B1011 / B1015 / B1016 / B1023。
   后 4 次：B1040 / B1043 / B1051 / B1054。

   **每次的形状都一样："用例让变异落在不影响断言的位置"。**
   **而每一次它都是绿的——判据绿得越自然，越没人去查它测的是什么。**

3. **"文档被自己抹掉"不会报错** —— B1014 的 834 号洞、B1015 的假收口、B1025 的过期入口、B1026 的旧标题。**只有编号连续性和"入口必须活着"这类判据能发现它们。**
