# 能力矩阵

**跟踪版本：`1.1.2`**（与 `Directory.Build.props` 的 `<Version>` 一致）/ **Tracked version: `1.1.2`**, matching `Directory.Build.props`.

一张能一眼看全的表：**哪个模块完整、哪个部分、哪个缺失**。
逐条的能力对照就是本文下面的表；
判定口径见 [comment-hygiene-audit.md](comment-hygiene-audit.md)。

**"部分"与"缺失"都不等于"没有"**：每一格的差异就写在该行里，读者不必另找文档。

图例：**✅ 完整** / **◐ 部分** / **❌ 缺失** / **➖ 不适用**（本仓按设计不提供）

## 一、数学与基础

| 能力 | C# 项目 | 状态 | 备注 |
| --- | --- | --- | --- |
| 数值类型（`FltX`/`Flt64`/`Flt32`/整数/有理） | `Math` | ✅ | |
| 代数概念与算子 | `Math` | ✅ | |
| 符号运算 | `Math.Symbol` | ✅ | 子树替换与移动共用一组变换入口：`Transform`／`TransformScalars`／`TransformBooleans`，**顺序是后序**（`src/Math/Symbol/Expression/ExpressionTransform.cs`） |
| 符号的按名标识 | `Math.Symbol` | ✅ | `NamedSymbol`；反序列化时按名解析，未带 `path:` 前缀的标识符走 `defaultSymbolOf` 回退 |
| 标量表达式的解析 | `Math.Symbol.Expression.Parser` | ✅ | `src/Math/Symbol/Expression/Parser/ScalarExpressionParser.cs`（546 行），25 条判据；优先级链到幂（右结合）为止，比较不链式，三元两支递归 |
| 多项式的词法 | `Math.Symbol.Parse` | ✅ | `PolynomialLexer.cs`，6 条判据 |
| 多项式的语法与规范形 | `Math.Symbol.Parse` | ✅ | `PolynomialParser.cs`，22 条判据；含 `CanonicalPolynomial`、线性与二次入口、不等式 |

| 多维数组 | `MultiArray` | ◐ | **C# 没有通配符泛型**，`AbstractMultiArray<T, *>` 只能显式写约束 |
| 量纲与单位 | `Quantities` | ✅ | `ToDouble()` 在 `Quantity` 上，不在 `FltX` 上 |
| 工具与错误类型 | `Utils` | ✅ | `Result<T,C,E>` 只有 3 参数版本 |
| 按轴归约（`sumAxes`/`cumsumAxis`） | `Math` | ✅ | |

**缺口核对的正确口径：名字对不上只是线索，不是证据。**

把两边的文件名集合做差会得到大量"缺失"，而逐个去查，绝大多数只是**换了文件名**：

| 候选 | 实情 |
| --- | --- |
| `Einsum` | 在 `Math/MultiArray/Einsum/EinsumParser.cs` |
| `CombinatoricsAsync` | 在 `Math/Combinatorics/CombinatoricsAsyncScope.cs` |
| `EscapeTime` | 在 `Math/Fractal/FractalAnalysis.cs` |
| `Gamma` / `Beta` | 在 `Core/Solver/Heuristic/` 的算子实现里 |

**而按名比对连"这一层根本没有"也会漏掉**——`src/Math/Symbol/Parse/` 曾经只有数值解析与两个结果类型，
表达式与多项式的文本入口是**整层缺失**，那属于"能力没有"，不是"名字没有"。

**这条路线的误报率极高：** **一次实测里 91 个"缺失"候选只有 1 个是真缺口（98.9% 误报），** 
**而且它还会漏掉"整层缺失"——** **这正是它只能当线索、不能当证据的原因。**
**判定标准因此是三层：** 文件名对不对得上（线索）→ **这个词在全仓源码里出不出现**（更强的线索）→
**这个能力有没有实现与判据**（唯一的证据）。**前两层只能用来挑候选，第三层才下结论。**

绊线 `SymbolicParsingGapTests` 的方向是反的：**缺口还是缺口时它绿，补上了它红**——
**红了才是提醒"这里那句缺口该改了"。**

**Notation: a missing file name is a lead, not evidence.** Differencing two file-name sets reports many "missing" entries that are simply renamed
elsewhere, and it also misses whole missing layers. **The decision therefore takes three levels: file-name match (a lead), whether the word appears anywhere in this repo's sources (a
stronger lead), and whether the capability has an implementation and criteria (the only evidence).** The `SymbolicParsingGapTests` tripwire runs the other way round: it is green while the
gap exists and red once it is closed, so red is the reminder that this section needs updating.


## 二、核心求解

| 能力 | C# 项目 | 状态 | 备注 |
| --- | --- | --- | --- |
| 元模型与线性模型 | `Core` | ✅ | `Model/Mechanism/MetaModel.cs` + `MetaModelExportSupport`（导出可读 LP/二次）；**但函数符号降级不完整，见 §九 第 3、6 条**——这一行 ✅ 指的是线性/二次的模型结构，不是"函数也能落成行" |
| 求解器接口与注册 | `Core` | ✅ | `Solver/` 下 `ILinearSolver`/`IQuadraticSolver`/`IAbstractLinearSolver`/`IAbstractQuadraticSolver`/`IColumnGenerationSolver`；注册侧由各插件项目 `*SolverPlugin` 提供 |
| 组合求解器 | `Core` | ✅ | `Solver/CombinatorialSolver.cs` + `CombinatorialSolveSupport`/`CombinatorialDumpSupport`；判据 `ComposedSolversTests`、`RunnerSolverTests` |
| **类型对照** | — | — | **"有多少类型本仓没有"这个问题，目前**无法用名字回答**。**写了三个版本的名字对照脚本，得到的数字依次是 **110**（按文件名）／**155**（按类型声明名）／**133**（加后缀与大小写容忍），**而抽查三个都推翻：`Imply`（`src/Core/Symbol/Function/Imply.cs` 就在）、`UnivariateLinearPiecewise`（本仓叫 `…PiecewiseFunction`）、`ModelView` 与 `ConstraintPriority`（都在 `src/Core/Model/Basic/`）。**根因：**Kotlin 与 C# 的命名习惯不同，还有大量 `typealias` 与纯函数文件——名字本来就不该相等。**所以这三个数字**不得作为结论引用**；它们的唯一位置是"一种方法失败了三次"的记录。**判据：** `NameBasedCounterpartScanTests`。 |
| 启发式算子 | `Core.Plugin.Heuristic` | ✅ | P8 收口；算子族见 §二 上一行，**测试 13 个源文件**；另有 `PolicyTrainer`/`PolicyEvaluator` 的训练与评估判据。**删掉了 8 个并行遗留的死桩类**（`EnergyValleyOptimizer`／`GerminalCenterOptimization`／`HillClimbingAlgorithm`／`HarmonySearch`／`NeighborhoodSearch`／`SeagullOptimizationAlgorithm`／`WaterCycleAlgorithm`／`WarStrategyOptimizationAlgorithm`），**它们只返回 `ApplicationError` + `"not implemented"`、从不被构造，而真实实现在组合路径上（`ComposedSolvers`，35 个入口）——`HS.kt` 的组合做法一致。**同时删掉了 `Stub/StubAlgorithmTest.cs` 那 8 条「断言未实现」的判据：它们守着的就是该删的东西。**判据：`HeuristicDeadStubDeletionTests`。 |
**给第二节四行补上了可核对依据，并写清一处张力。**

四行原来都是 ✅ 配空白备注。**空白不等于"没问题"，等于"没人写过它凭什么 ✅"**——
而下一个人要判断"这个模块能不能用"，只能自己去读一遍源码。

**唯一一处真张力在"元模型与线性模型"**：这一行是 ✅，而 §九 第 3、6 条说函数展开层整层缺失。
两者不矛盾，**但只有写清楚才不矛盾**——✅ 指的是线性/二次的模型结构、导出与身份传播，
**不是"函数符号也能落成行"**。留空的行会让人把前者读成后者，然后在写一个带 `Sin` 的约束时撞墙。

**gave the four rows of section two a checkable basis and stated one real tension.** All four were ✅ with an empty remark, and **empty does not mean "fine" — it means
"nobody wrote down why this is ✅"**, leaving the next reader to re-read the source to judge whether the module is usable. **The one real tension is "metamodel and linear
model"**: that row is ✅ while §九 items 3 and 6 say the function expansion layer is entirely missing. The two do not contradict **only if it is spelled out** — the ✅ covers
the linear/quadratic model structure, export and identity propagation, **not "function symbols can also become rows"**. An empty cell invites reading the former as the latter,
and then hits the wall while writing a constraint with a `Sin` in it.


## 三、求解器插件

| 能力 | C# 项目 | 状态 | 备注 |
| --- | --- | --- | --- |
| Gurobi | `Core.Plugin.Gurobi` | ◐ | **能力探测已实现**（`GurobiFunctionCapabilities`，反射 `SetPWLObj`/`AddGenConstrPWL`），**但这份申报没有任何一端连着实际**：三个插件合计申报 **13 项**原生能力，而 `src/` 里**零处**调用原生构造方法（`AddGenConstrAbs`／`AddGenConstrPWL`／`AddSOS`／`AddGenConstrIndicator`／`AddGenConstrMax`／`AddGenConstrAnd` 全部零命中），`Supports(...)` 的**生产调用方也为零**（只有测试在调）。**它问的是"这个 SDK 有没有这个方法"，而读者会理解成"这个后端会不会用原生形式"。**没有解因此算错——它是镜子，不是 bug。**缺的是上层的函数展开层（见下）。判据：`FunctionCapabilityDeclarationTests`。 |
| Gurobi 11 | `Core.Plugin.Gurobi11` | ◐ | **4 个求解器类，委托给 `Core.Plugin.Gurobi`**（`private readonly GurobiSolver _solver`）；没有独立副本。**缺的与 Gurobi 同一处**，但"同上"要说明它是委托而非复制 |
| SCIP | `Core.Plugin.Scip` | ◐ | **真实原生求解已实测**：门禁 `RunScipIntegration=true` 下 `Core.Plugin.Scip.Tests` **149 通过 / 0 失败**，含 `ScipDualIntegrationTest`、`ScipConstraintProgrammingIntegrationTest`、`ScipBendersReportIntegrationTest`、`QuadraticLegacyIisEntryIntegrationTests`、`CrossLanguageElasticParityTests`。仍缺原生 PWL 与函数展开层（见第九节第 3、6 条） |
| CPLEX | `Core.Plugin.Cplex` | ◐ | **原生 SOS2/PWL 已实现但未接线**：`CplexNativePiecewise.cs`（172 行，SOS2 lambda 形式，含点数校验、键映射、重复结果键拒绝）**存在**，且 `CplexFunctionCapabilities` **已申报** `PiecewiseLinearConstraint` 原生支持；**缺的只有生产调用方**。所以这一格不是"未实现"（工作项），而是"申报超过实际"（缺陷）——调用方看到申报会以为走原生形式，实际走通用线性化。IIS 返回显式不支持。判据：`NativePiecewiseWiringGapTests`。 |
| COPT | `Core.Plugin.Copt` | ◐ | **原生 SOS2/PWL 已实现但未接线**：`CoptNativePiecewise.cs` **存在**，且 `CoptFunctionCapabilities` **已申报** `PiecewiseLinearConstraint` 原生支持；**缺的只有生产调用方**。点式原生回调仍未实现。判据：`NativePiecewiseWiringGapTests`。 |
| Mosek | `Core.Plugin.Mosek` | ◐ | 代码在（**1388 行**），缺的是**许可**：实测 `err_missing_license_file(1008)` |
| MindOpt | `Core.Plugin.Mindopt` | ◐ | 代码在（**3009 行**），缺的是**许可**：实测 `License is not valid` |
| Hexaly | `Core.Plugin.Hexaly` | ◐ | 代码在（**2619 行**），缺的是**SDK**：实测 `HEXALY_HOME` 未设、`/bin/Hexaly.NET.dll` 不存在 |
| Lingo | `Core.Plugin.Lingo` | ➖ | C# 侧是占位类型，不声明求解能力 |
| OptVerse | `Core.Plugin.Optverse` | ➖ | C# 侧是占位类型，不声明求解能力 |
**逐行核了第三节，两处要改、一处要答。**

| 行 | 原来写的 | 实测 |
| --- | --- | --- |
| SCIP | "**没有真实原生求解的实测**" | **假**：门禁里 `Scip.Tests` **149 通过**，含对偶、CP、Benders 报告、IIS、跨语言弹性对照 |
| Mosek / MindOpt / Hexaly | 备注**空着** | 三者都有真实代码（1388 / 3009 / 2619 行），**卡在许可或 SDK** 各有实测错误串 |
| Gurobi 11 | "同上" | 本仓是 **4 个类委托给 `Core.Plugin.Gurobi`**，没有独立副本——与"另有一份实现"不是同一件事 |

**一个空着的备注比一个写错的备注更难发现。** 写错的会被人核，
**空着的只会被当成"没什么可说的"**——而这三行恰恰每一行都有话要说，且都是实测过的话。

**checked section three row by row.** The SCIP row claimed "no real native solving was measured" — **false**: the gate runs `Scip.Tests` with **149 passing**, covering duals, CP,
Benders reports, IIS and a cross-language elastic parity test. The Mosek / MindOpt / Hexaly rows left the remark **empty** while all three carry real code (1388 / 3009 / 2619
lines) and are blocked on a licence or SDK, each with a measured error string. And Gurobi 11 is **four classes delegating to Gurobi**, not a separate copy.
**An empty remark is harder to catch than a wrong one**: a wrong one gets checked — that is how and found theirs — **while an empty one reads as "nothing to say"**, and these
three rows each had something measured to say.


**这些插件的共同限制**：C# 侧只验证"没有原生 SDK 时返回显式的不可用结果"，
**没有验证有 SDK 时的实际求解行为**。因此"◐"表示"接口在、能力是子集"，不表示"与别处等价"。

## 四、框架

| 能力 | C# 项目 | 状态 | 备注 |
| --- | --- | --- | --- |
| 求解框架与会话 | `Framework` | ✅ | P8 收口 |
| 网络调度 | `Framework.NetworkScheduling` | ✅ | P7 收口 |
| 甘特调度 | `Framework.GanttScheduling` | ✅ | P7 收口 |
| Gantt-Network 桥接 | `Framework.GanttNetworkScheduling` | ✅ | **独立项目**，因为 `NetworkScheduling` 已引用 `GanttScheduling`，反向加桥接会成环 |
| 桥接接入 Demo4 流程 | — | ❌ | **是分叉不是漏调用**：Demo4 走了一条自己的标签搜索路径，不走束生成，且**两条路的结果无法互相比对** |
| BPP 2D 装箱 | `Framework.Bpp2d` | ✅ | 缺口清单对 `framework-bpp2d` 报 **0 真缺口**；`Box2Geometry` 在此项目内，见"几何 2D" |
| BPP 3D 装箱 | `Framework.Bpp3d` | ◐ | 见下方"Bpp3d 细项" |
| CSP 1D | `Framework.Csp1d` | ◐ | 见下方"CSP1D 细项" |
| BPP 1D | `Framework.Bpp1d` | ✅ | 25 个源文件；缺口清单对 `framework-bpp` 报 **0 真缺口** |
| CSP 2D | `Framework.Csp2d` | ✅ | 21 个源文件，覆盖该模块的全部主源码 |
| 几何 2D | `Framework.Bpp2d` | ◐ | 合并进 Bpp2d 而非独立工程：`Box2Geometry` 在 `Framework.Bpp2d.Domain`，重叠语义统一为"内部重叠"（相切不算重叠） |
| 消息插件（Kafka） | `Framework.Plugin` | ◐ | 有生产者/消费者（`KafkaProducer.cs`、`KafkaConsumer.cs`），**起有真实 broker 往返（三条判据）并修掉 `Dispose` 不 flush 的静默丢失**，**两层缺口**：① **没有真实 broker 往返测试**；② **更糟的一层：`KafkaInterfaceTests.cs` 的 8 条判据全部只是**成员存在性检查**（`IDisposable` 实现没有、`Send`/`SendJson` 方法在不在），**没有一条调用任何方法，因此没有一条会因行为错误而变红**——而文件名让它们看起来像行为覆盖。**一条只会因"删掉一个方法"而变红的判据，本质是编译期检查的副本，而编译器本来就在做那件事。**判据：`KafkaTestSubstanceTests`（① 测试文件仍无真实调用，闭合时要求改写；② 本行必须同时说出两层）。 |
| 持久化：EF Core / SQLite / MySQL / MongoDB / Redis | `Framework.Plugin` | ◐ | **SQLite 有真实往返**；**MySQL / PostgreSQL 有真实往返（各三条判据）**，**MongoDB 也有真实往返（三条判据）**（验收表 `⊘3`）。**Redis 是另一个形状：`RedisClient.cs` 203 行代码**在**，而生产调用方**不在**——只有三个测试文件提到它，而那三个文件**都不构造它**。**所以"Redis 有客户端"成立，"Redis 可用"**已被推翻（本机真跑通了往返，见本行末尾）**。**并且这些测试全是影子检查：`InterfaceVerificationTests`（14 条）、`RedisInterfaceTests`（11 条）、`KafkaInterfaceTests`（8 条）合计 33 条判据全部只问"某个成员在不在"。判据：`ExistenceOnlyTestHabitTests`。 **更正：**本机 `127.0.0.1:6379` 端口开着、`PING` 返回 `+PONG`、无密码——所以"没有真实往返"只是**没人写那条测试**。** `RedisRoundTripIntegrationTest` 现在用生产入口 `Redis.Init` 起客户端并做五种取值形态的真往返（**4 条**，`-p:RunRedisIntegration=true`）；`Redis.Init` 补上了 `LastInitError`，"连不上"与"没配置"不再长得一样（另 **2 条**默认构建判据钉住诊断面）。**这一行不是缺口：** 本项目是框架，**只提供能力、不保证项目内必然有使用的地方**——可用且已实测即算完成；接哪条业务路径是使用方的决定。** |
| 持久化：H2 / JDBC client / Ktorm / MyBatis / MongoDB 翻译层 | — | ❌ | **评估过了，"未评估"这个状态消掉了，结论有两半**——**当时 `src/` 下 `BsonDocument` 出现 0 次，`MongoRepository` 只会 `Filter.Eq("_id", …)`**（计数可复现：`logs/b1117-module-sizes.py`）**。**① 谓词→查询的翻译能力在 C# 侧是有的，只是换了机制**：`SqlRelationalQueryCompiler`（**838 行**）覆盖全部 8 种布尔节点（`BooleanConstant`/`Comparison<>`/`InExpression<>`/`PatternMatch<>`/`NullCheck`/`And`/`Or`/`Not`），**14 条判据**（字面量参数化、通配符、空 IN、EXISTS 子查询、MySQL null 排序模拟、歧义列拒绝等）；EF Core 接表达式树，因此"手拼 SQL 字符串"那一层（`JdbcClientSql.quoteIdentifierPath` 之类）**不需要对应物**。**② Mongo 翻译层已全部补齐并接线**：`MongoScalarTranslator`（**11 条判据**：`$字段` 引用、字段名解析、五个二元运算映射、`-x`→`$multiply:[-1,x]`、**`ABS`/`POWER` 按失败而非自作主张**、`null`→`BsonNull`、深度护栏失败而非漏出）；`MongoOrderByTranslator`（**5 条判据**：升序 1/降序 -1、空排序→`null`、字段解析失败即失败、**多字段顺序保留**、`Nulls` 不静默忽略）；`MongoUpdateTranslator`（**6 条判据**：`$set` 形状、**`SetNull` 写 `BsonNull` 而非 `$unset`**、空赋值→`null`、字段解析失败即失败、**整型保持 `Int32` 否则写进去查不出来**、**`SetFromExpression` 拒绝而非猜**）；`MongoBooleanTranslator`（**四个里最大**，**14 条判据**：三值常量（`Unknown` 归"匹配不到"）、**常量在左时六个运算符全部反转**、引用在左不变、**null 检查同时管"是 null"与"缺失"**、正则元字符转义、**`Like` 先转义再替换通配符**、`$expr` 字段对字段、`NOT` 包整棵子树、不支持的谓词失败、**空逻辑在 C# 里构造不出来**（构造器拒绝空操作数，故那两段分支已删））；**把它们接进真实查询路径**：`MongoRepository.FindAsync(predicate, sortBy, resolver)`——在此之前仓库只会 `Filter.Eq("_id", …)` 与 `Filter.Empty`，**四个翻译器没有被任何生产路径调用过**；**三条判据**钉住最要紧的行为：**翻译失败不退化成"查全部"、不碰集合**（`FindCalls == 0`）、可翻译的谓词确实走到集合、排序失败同样不退化成无序结果。**明确的局限**：这条判据用探针复刻控制流形状（`IMongoDatabase` 有 40+ 成员，已踩过），**未连真实 Mongo 实例**。**仍未实现的**：H2 / Ktorm / MyBatis / JDBC 四个后端（C# 侧用 EF Core 承担同类职责，**已逐项核对（契约层）**：**三个表达式仓储的 `{find, count, exists, update, delete}` 与本仓 `ExpressionRepository` 一一对应，一个不多一个不少**；不等价的那部分同样明确：**用四个方言后端做同一件事，本仓用 EF Core 一个栈承担——是换了一条实现路，不是少了一个操作**；判据 `PersistenceContractEquivalenceTests`**）。**同口径计数**：`framework-plugin` **61** 个主源码 vs 本仓 `Framework.Plugin` **15** 个。**（旧文写"61 vs 48/53，命名口径差异不是缺口"——那是拿一个模块比全部插件项目，口径不同。）**改状态为 ◐ 並写清依据**：**这一格原来是 ❌，而它得 ❌ 的理由是"四个方言后端未实现"——那是"这些具体后端没有"，**不是"持久化能力没有"**。**而这两件事已经分开量过：**Mongo 翻译层已接线（`MongoTranslatorWiringTests`：三个有生产调用方，第四个因仓储用整文档替换而不需要）；**五个调用操作在契约层一一对应（`PersistenceContractEquivalenceTests`）。**所以状态改为 ◐：**能力在、代替实现已核对，缺的是那四个方言后端本身。** | ◐ |
| 对偶解按身份整理（`TidyDualSolutionByIdentity`） | `Core` | ✅ | **新增 `TidyDualSolutionByIdentity`：`tidyDualSolution` 返回 `Map<Constraint, Flt64>`，C# 视图不持有约束对象引用，所以 key 用 `ConstraintId`。**两处新增：**对偶/Farkas 模型保存原模型行身份快照 `PrimalRowIdentities`（`BuildDual` 构造时带上），对偶路径按 `DualOrigin.RowIndex` 回查；行对齐路径直接取本模型 `Constraints.Ids[row]`。**快照而不是引用**：**视图的意义是可复制的快照，持活的原模型会让对偶模型生命周期绑在原模型上，原模型 `Dispose` 后对偶整理就会失败。**记下一条**未测**，而不是把它报成"实测通过"**：**变异实验把实现改成"完全不读 `PrimalRowIdentities`"，22 条判据**全部通过**（**该文件原有 22 条，新增的那一条才是第 23 条**）。原因是测试能造的原模型只有两种——没有行身份（两条路的表都为空，无法区分）与有手写行身份（`Dual` 在身份集合校验那关就失败，走不到整理步骤）。第二种失败不是缺陷：身份必须与注册表自洽，测试里手写的身份不在任何注册表里，`Dual` 拒绝它是对的。**要让这条路径可测，需要能注册手写身份的构造入口或真实走过注册流程的模型——比更大，不做，而是把"未测"写成"未测"。**判据 5 条（并入 `LinearTriadDualTest`）：**无行身份时空表是正确答案**、**有行身份时完整覆盖且键真实不重复**、**快照存在且空列表不是 `null`**、**普通模型无快照**、**行对齐路径的键取自本模型身份（对照判据）**。**第一条是**方向写错**的判据，值得原样记：**初版要求"表必须覆盖每一行"，跑起来红了（期望 3、实得 0）——而红的原因不是实现错了，是**判据前提错了**：测试原模型没给变量和约束设身份，而在 `origins` 为空时同样返回空 map。一个要求"表非空"的判据会逼实现去**伪造**身份，而伪造的身份比没有身份更危险——它会出现在下游账里，却指向不存在的约束。**另一条造模型的教训**：**初版给三行塞手写 `ModelElementIdentity` 后 `Dual` 直接失败——身份不是"填个字符串"，它必须与一份注册表自洽。**
| 约束求值 `NativeConstraintEvaluations` | `Core` | ⚠️ | **两个求值器。两个求值器之间有两处不对称，都照做并在判据里钉住。**第一处：容差的来源不同**：**ABS 走 `finiteSolverDouble(PIECEWISE_TOLERANCE, ...)` 转换之后的值，MAX 用字面量 `1e-6`。数值上相同，结构上不同——`PIECEWISE_TOLERANCE` 一旦改动，ABS 跟着走，MAX 不跟。**的第 8 条“不可测”发现**：**这条差别**当前测不出来**，因为两者数值相同；变异验证：把 ABS 也改成字面量，7 条判据全绿。判据只能断言“两者当前相等”，并在注释里写清“这是巧合而不是同源”。**第二处，也是更要紧的一处：`maxEvaluations` **忽略 `structure.minimum`**：**它永远取 `maxOrNull`，所以一个 `minimum = true` 的结构会被拿去和**最大值**比。**这不是本仓库的偏差，是**缺陷**：**`NativeMaxData` 带着 `minimum`，`restoreValues` 也按它取极值，而 `maxEvaluations` 却没看这个标志——同一份数据在两条路径上被解释成两种含义。**照抄以保持的可比性，并把这一点写成判据。一个被记录下来的缺陷是“已知债”；一个没人测过的缺陷是“潜伏的错”。**这条判据有一个反直觉的性质：如果哪天有人“修正”了它（让求值器看 `minimum`），判据会变红。**变异验证：把 `minimum` 纳入求值的“修正版”让 2 条判据变红。**这正是记录缺陷的判据应有的行为——它不是“实现正确”的证据，而是“实现”的证据。修正时必须同时改判据，那一步是**有意的**，而不是被红条逼出来的。**容差边界是 `<=` 而不是 `<`**：**判据用 `violation` 恰为 `1e-6` 与 `1e-6 + 1e-9` 这一对，只测 0 与一个大值的话两者区分不开。**判据 `NativeConstraintEvaluationsTests` **7 条**：**ABS 求值形状**、**容差边界含等号**、**MAX 忽略 `minimum`（期望 `10`、结果 `3`、违反量 `7`、判为不满足）**、**两条路径对同一标志的两种解释并排断言**、**两处容差当前相等（记录巧合）**、**缺 token 按类分别报**、**空结构列表**。**变异：`<=` 改 `<` → 红 1 条；“修正” `minimum` → 红 2 条；ABS 容差改字面量 → **全绿**（**如实记录，未声称覆盖**）。**一处 C# 细节**：**`ConstraintEvaluation<V>` 的字段名是 `ConstraintId` / `Relation` / `Satisfied`，关系枚举叫 `SolveConstraintRelation`（不是 `ConstraintRelation`）。**

| 未出现类型的**成因分类**（`core` 166 个） | `Core` | ◐ | **"未出现"这三个字掩盖了三种完全不同的情况——真缺、换了机制、改了名，只有第一种要补。** 扫描得到 `core` 命中率 **74.3%**、166 个未出现；脚本 `logs/b1130-classify-absent.py` 按后缀归类（复用同一提取器，避免两处口径漂移）。**构成**：符号层 42 个（`*Function`/`*Materializer`，依赖 `MathFunctionSymbol` 整套生命周期接口）、泛型特化 14 个（`Dyn*Variable`，本仓用 `VariableCombination<T, TType, S>` 泛型表达，不为每种组合建类）；其余为**未归类**，**不四舍五入成一个整数**。 |
**本表记的是能力，不记"某一步补了什么"。** 每一格的差异与证据写在那一行里。


`Framework.Bpp1d` 与 `Framework.Csp2d` 两个工程都在，**缺的只是 **starter 包**本身**。

## 四之一、CSP1D 细项

| 能力 | 状态 | 备注 |
| --- | --- | --- |
| 生成器接口与实现 | ✅ | `DFSGenerator`（153 行）+ `FullSumGenerator` 都在，共用 `GenerationSumSearch` |
| 统计字段与 KPI 键 | ✅ | 名字 |
| **生产调用方** | **⚠️** | `FullSumGenerator` 有（`Demo3/ColumnGenerationDemo`）；**`DFSGenerator` 只有测试在用** |

**上一版整节已撤回。** 上一版写"`DFSGenerator` 是个空壳、已被删除"，**两句话都不对**：

| 上一版说 | 实测 |
| --- | --- |
| `DFSGenerator` 是个空壳 | **153 行实现**，含 `satisfiesPruningConstraints` 与支配剪枝 |
| 已删除 | **文件在**，`FullSumGenerator` 直接调用它的 `GenerationSumSearch` |
| 没有任何使用者 | **有测试在用**（`Csp1dGeneratorPublicSurfaceTests`、`CrossContributionWiringTests`），只是**没有生产实例化** |

**这个区别是要紧的**：没有生产调用方是一个**接线缺口**（框架提供了两种初始模式生成器，示例只用了一种）；
空壳是**代码没写**。前者改一行接线就能验证，后者要写一整段算法——**把前者写成后者，会让人以为工作量比实际大得多，也就没人去接。**

判据 `CapabilityMatrix_ShouldStateTheCsp1dShellGap` 已同步翻转：它原来钉的四条里有三条钉的是错结论。

**withdraws the whole of the previous section.** It said "`DFSGenerator` is a shell and deleted it", and **both halves are wrong**: the file is **153
lines** of real implementation, `FullSumGenerator` calls its `GenerationSumSearch` directly, and while **tests do use it** (`Csp1dGeneratorPublicSurfaceTests`,
`CrossContributionWiringTests`) there is **no production instantiation**. **The distinction matters**: no production caller is a **wiring gap** — the framework offers two
initial pattern generators and the demo uses one — whereas a shell is **unwritten code**. The former is verified by wiring one line; the latter needs a whole algorithm
written. **Writing the former up as the latter makes the work look far bigger than it is, and nobody goes and wires it.** The criterion
`CapabilityMatrix_ShouldStateTheCsp1dShellGap` is flipped accordingly: three of its four pins asserted the wrong conclusion.
## 四之二、Demo2 细项

| | | C# |
| --- | --- | --- |
| 文件数 | **185** 个 `.kt`（`demo2/src/main/`） | **175** 个 `.cs`（**实测**，脚本 `logs/b927-recount.py`） |
| 空壳 | — | **0** 个（**更正**：`FlightPhase.cs` 不是空壳，见下） |
**更正**：这里原来写"**1** 个空壳（`Domain/Aircraft/Model/FlightPhase.cs`）"。去读了那个文件——
它是 `public enum FlightPhase { ZeroFuel, TakeOff, Landing }`，**三个成员一个不缺，能编译、被引用**。
它**不是空壳，是本仓新增的枚举**（没有对应物）。那个词把两件事混了：
类型齐全但没有对应物 = **新增**；类型体里一个成员都没有 = **空壳**。
判据 `ThereShouldBeNoEmptyTypeBodies` 钉后者，`TheFlightPhaseEnum_ShouldKeepItsMembers` 钉前者的三个成员。

**脚本此前把这个词用错了。** `logs/b927-recount.py` 里 `is_shell` 量的是
**"去掉注释后不足 200 字符"**——那是 **长度**，不是空壳。于是它报"1 个空壳"、
文档报"0 个空壳"，**两个数字看起来在打架，其实量的是两件事。**

现在拆成两个，且同时输出：

| 量什么 | 数 | 判法 |
| --- | --- | --- |
| **短文件** | **1** | 去掉注释与 `using` 后不足 200 字符（`FlightPhase.cs`） |
| **空壳** | **0** | 类型体里一个成员都没有 |

`--check` 现在打 `short=1 empty_types=0`，**行尾阈值只管前者**——它管的是长度告警，不是能力缺口。

**correction**: this row used to say **one** shell. read the file: it is `public enum FlightPhase { ZeroFuel, TakeOff, Landing }` — **three members, none missing,
compiles and is referenced**. It is **not a shell but an enum added here** (no counterpart elsewhere). The word conflated two cases: complete type with no counterpart
original = **an addition**; a type body with no members = **a shell**. `ThereShouldBeNoEmptyTypeBodies` pins the latter and `TheFlightPhaseEnum_ShouldKeepItsMembers` the
three members of the former.

**把整张逐项表按实测重写**（`logs/b932-audit.py`）：旧表在 时把下面这些判成"仍是 `// TODO` 空壳"、"空类"、
"连一个成员都没有"，而实测**一个 `TODO` 都没有**、每个都有生产引用。

| 能力 | 状态 | 实测（`logs/b932-audit.py`） |
| --- | --- | --- |
| `Appointment` 预约映射 | ✅ | 85 行、0 个 `TODO`、9 处引用；**注意 `Position` 引用相等 vs 值相等** |
| `BiologicalLimit` 生物兼容性 | ✅ | 44 行、0 个 `TODO`、5 处引用 |
| `Flight` / `Payload` / `TotalWeight` / `MaxLoadWeight` | ✅ | 50 / 213 / 159 / 105 行，**0 个 `TODO`**，引用 25 / 23 / 16 / 6 |
| `Torque` / `MacModel` / `MacAggregation` | ✅ | 237 / 89 / 148 行，**0 个 `TODO`**，引用 17 / 1 / 4 |
| `StowageAggregation` / `PayloadMaximizationAggregation` | ✅ | 246 / 44 行，引用 19 / 2 |
| `SolutionAnalyzer` | ✅ | 252 行、2 处引用 |
| `PipelineListGenerator` | ✅ | 149 行、13 处引用 |
| **分区列生成分派** | ✅ | 分区配置 + 分派 + 目标展平；真 SCIP 实测 `partition_columns=5;iterations=4;root_lp_converged=True` |

**整张旧表已撤回**：保留这段说明是因为"曾经这么判过"要能审计，不是因为它还成立。

**一处仍然真实的缺口**：`Domain/Aircraft/Model/FlightPhase.cs` 是**唯一的空壳**（去掉注释后 135 字符），见缺口清单的 实测。

**The whole table was rewritten from measurement in **: the old one judged these as "still `// TODO` shells", "empty classes", "not even one member" — while
the measurement finds **not a single `TODO`** and a production reference for each. The old table is void; the note stays **so the earlier judgement remains
auditable**. **One gap is still real**: `FlightPhase.cs` is the **only shell** (135 characters after stripping comments), per the measurement.
## 四之三、Starter 包（**曾经是空壳，现已补齐四个**）

| Starter | 引用的工程 | 状态 |
| --- | --- | --- |
| `Starters.Core` | `Core` | ✅ |
| `Starters.Framework` | `Framework` | ✅ |
| `Starters.Bpp1d` | `Framework.Bpp1d` | ✅ **新增** |
| `Starters.Bpp2d` | `Framework.Bpp2d` | ✅ **新增** |
| `Starters.Bpp3d` | `Framework.Bpp3d` | ✅ |
| `Starters.Csp1d` | `Framework.Csp1d` | ✅ |
| `Starters.Csp2d` | `Framework.Csp2d` | ✅ **新增** |
| `Starters.GanttScheduling` | `Framework.GanttScheduling` | ✅ |
| `Starters.GanttNetworkScheduling` | `Framework.GanttNetworkScheduling` | ✅ |
| `Starters.NetworkScheduling` | `Framework.NetworkScheduling` | ✅ **新增** |

**撤回上一版的表。** 上一版列了五行，其中 **`Starters.Bpp1d` / `Starters.Bpp2d` / `Starters.Csp2d` 三个目录根本不存在**，
却都标着 ✅；同时**漏掉了实际存在的 `Core` / `Framework` / 两个 Gantt 包**。

| 上一版说 | 实测 |
| --- | --- |
| `Starters.Bpp1d` ✅ | **目录不存在** 新建 |
| `Starters.Bpp2d` ✅ | **目录不存在** 新建 |
| `Starters.Csp2d` ✅ | **目录不存在** 新建 |
| （未列） | `Starters.Core` / `Starters.Framework` / 两个 Gantt 包一直存在 |
| （未列） | 还有 `starter-network-scheduling`，本仓没有 新建 |

**这不是"文档写错一行"**：没有入门包，用这个模块的人得手写五条工程引用——**装不上**。
七个 starter（`bpp1d`/`bpp2d`/`bpp3d`/`csp1d`/`csp2d`/`gantt-network`/`gantt-scheduling`/`network-scheduling`）现在都有了对应物。
**这五个 starter 曾经是逐字节相同的 csproj**：`AssemblyName` 全是 `Fuookami.Ospf.Starters`，
`ProjectReference` 一个都没有。**同名程序集互相覆盖、装上去不传递任何模块**，而构建与测试全程绿灯。

**这一节钉的是历史那句**，不只是后果：只断言"现在对"会放过"把曾经的空壳写成一直正确"。

**重建说明**：编辑本文件时误删了这一节（替换区间起点选错），内容按
`CapabilityMatrixTests.CapabilityMatrix_ShouldStateTheStarterPackageGap` 与
`CapabilityMatrix_ShouldSayTheProjectsExistAndOnlyStartersAreMissing` 的钉子重建。

**Before these five starters were byte-identical csproj files**: every `AssemblyName` was `Fuookami.Ospf.Starters` and not one had a `ProjectReference`, so
**identically named assemblies overwrote each other and installing one carried no modules** — while build and tests stayed green. **This section pins the history
sentence**, not just the outcome: asserting only "it is correct now" lets "always correct" replace "used to be a shell". **rebuild note**: editing this file
accidentally deleted this section (wrong replacement start); the content was rebuilt from the pins in the two criteria named above.

## 五、Bpp3d 细项（P9 的重点）

| 能力 | 状态 | 备注 |
| --- | --- | --- |
| 装箱几何快照与重叠检测 | ✅ | |
| 圆柱支撑覆盖（水平圆柱） | ✅ | |
| 渲染 DTO 与装载计划 | ✅ | |
| 重量贯通（件 → 箱） | ✅ | |
| 宽相位扫掠（箱对剪枝） | ✅ | |
| 箱容量约束右端项 | ✅ | |
| 容量约束接入最终解 | ◐ | **前置校验，不是模型约束** |
| 箱深度约束右端项 | ✅ | |
| 深度约束接入最终解 | ◐ | 同上 |
| 影子价格感知评分 | ◐ | **只按件数计贡献**，重量型需求口径不同 |
| 圆柱半径回写 | ◐ | 无 PWL 体积路径；无"同键歧义"概念 |
| 加载顺序约束 | ✅ | `BinLoadingOrderConstraint`（落地，依赖 `PreciseAssignment` 已就位） |
| 尾箱分配约束 | ✅ | `TailBinAssignmentConstraint`，两支方向不对称是自带语义 |

## 六、示例、基准与文档

| 能力 | C# 项目 | 状态 | 备注 |
| --- | --- | --- | --- |
| CoreDemo / FrameworkDemo | `Example` | ✅ | Demo2 **185** / C# **175** 个文件、**0 个空壳**；Demo4 与 Demo5 各有独立清单判据 |
| Demo5 双语文档 | `Example` | ✅ | 由 `Demo5DocumentationTests` 钉住 |
| 基准套件 | `Benchmark` | ✅ | **20 个基准**（另有 1 个不计时的转储工具 `CrossVersionSemanticDump.cs`），15 个工作负载一一对应**（补齐最后两个 证伪了"依赖缺失/`internal`"这个理由） **（复审换成 `19c72455`）**：**新比固定多 14 个文件，其中 6 个是新的 JMH 基准，全在 `ospf-kotlin-benchmark`，且**只依赖库内 API**——所以不属于"外部所限"，是**待实现**。** 已实现 1 个：`TokenInvalidateRebuildBenchmark`（3 个基准 × 3 档；large 档实测"重建 2.65 ms"对"稳态读 2.54 µs"，另有 `TokenIndexInvalidationTests` 两条行为判据钉住"结构变更后索引仍正确、被移除的 token 不再解析"）。**6 个新基准已全部有文件；下面这个的待办段在此点名（判据 `TheUnportedNewWorkloads_ShouldBeNamedInTheMatrix` 只钉"必须被写下来"，不要求代码在场）**：`TokenSharedReadBenchmark` **已实现**（三个读方法 + 空调度对照 + `ColdBuildWork` 诊断；`CountingVariable` 照搬为 `AbstractVariableItem` 子类并覆写 `Identifier` 计数；`TokenSharedReadBenchmarkTests` 5 条判据，其中一条钉住"一次完整建表的标识符读数必须不少于 token 数"——否则这个"建表工作量"读数会退化成装饰）、`PrimeMixedExpansionBenchmark` **已实现**（连同同一文件里的 `PrimeReaderLatencyBenchmark`：`CyclicBarrier` 用 .NET `Barrier` 对齐起跑；writer 是唯一扩容者、按 volatile 换代；判据 `ConcurrencyBenchmarkTests` 5 条钉住"混合批次的 reader 半边等于纯 reader""每种形态进度不同""reader 数真的改变负载""writer 在窗内重建过整代""`readyOnly` 不起 writer"；**能力差已写明：本仓基准运行器不产出 JMH `Mode.SampleTime` 的 p95/p99**）、`GenerationScaleBenchmark` **已实现**（3 个生成器 × 3 个场景 × 4 个并行度 × 2 个方案上限；`GenerationScaleBenchmarkTests` 的判据钉住"每个参数维度真的接到夹具上"——生成器看夹具类型、场景看 12 个物料上界是否两两不同、上限看方案数是否被截断）、`NetworkSharedGraphBenchmark` **已实现**（形态 × 规模 × reader 数，五个基准方法；`NetworkSharedGraphBenchmarkTests` 八条判据用精确等式而不是"大于零"——环形形态下每个 reader 的可达集必须正好等于节点数、反向视图出弧必须正好 4、空调度必须恰好发 0…n-1 号）、`CrossVersionSemanticDump` **已实现（三段）**（`NET`／`TOK`／`PRM` 已转储，`--dump` 走它而不计时；`CrossVersionSemanticDumpTests` 七条判据钉住"只含已实现段、布尔小写、非统计行两次运行逐行相同、待办清单与已实现段不重叠且六段齐备、核心数字是真值（π(20000)=2262）、投影段真结构、截断真的截断且规范键不是同一串"**；**起六段齐备：** `PRJ` 用 `ColumnProjection.SnapshotAsync`／`PricingRequestAsync`／`MasterAsync`，`GEN`／`TRUNC` 用 `Domain` 层的 `CuttingPlanCanonicalKey.From(plan)`——**两条"待办"理由实测都不成立，和 `PreciseAssignment`、"环境所限"是同一个错法。**可比性限制已写明：** 用 Kotlin `String.hashCode()` 做摘要，**跨语言不可比**，所以本转储不输出 `hashCode` 摘要——可逐行对比的是计数、排序后原值拼接与布尔量。 |
| 基准趋势脚本 | `scripts/` | ✅ | `checks=29 failures=0` |
| 双语文档（仓库级 README） | 根目录 | ◐ | 已扩到 171 / 158 行；**主题覆盖而非逐行对齐** |
| 其余模块 README | — | ❌ | `src/Starters/*` **十份**（补了 Bpp1d/Bpp2d/Csp2d/NetworkScheduling 四个启动器）；其余模块**未排期**写 README |
| 迁移指南 | `docs/` | ✅ | |
| 能力矩阵（本文件） | `docs/` | ✅ | |
**用同一把尺子过了第四到第七节，四处过时**。前两处与 抓到的是**同一句话的另外两份拷贝**：

| 行 | 原来写的 | 实测 |
| --- | --- | --- |
| 基准套件 | "**九个**基准已实现；两个依赖**缺失项目/`internal` 类型**" | **15 个**。两个"障碍"都不存在：`Framework.Bpp1d` 在、`DFSGenerator` 是 `public`，两个基准已在 补上并实跑 |
| 持久化 | "**只验证了仓储接口**" | SQLite **有真实往返** |
| 其余模块 README | "只有 `src/Starters/*` **五份**" | **十份** |
| BPP 2D / CoreDemo | 备注**空着** | 两行都有可写的事实 |

**一个过时的判断会被抄进更多地方。** 只改了状态文档的 P9 摘要，**而矩阵里这条一模一样的判断还活着**——
两份拷贝里改一份，等于没改：**读者遇到不一致时不会去查哪份新，他只会信对自己更不利的那份。**

**ran the same ruler over sections four through seven and found four stale rows.** The first two are **further copies of the same sentences caught**: the benchmark row still
said "**nine** benchmarks; two blocked by a **missing project / `internal` type**" while there are **15** and neither obstacle exists; the persistence row still said "**only the repository
interface was verified**" while SQLite has a real round trip since . **A stale judgement gets copied into more places than one.** fixed only the status document, **leaving an
identical judgement alive in the matrix** — and fixing one of two copies is not fixing it: **a reader facing the disagreement does not check which is newer, he believes the one that
costs him more.**


## 七、跨语言验证范围（**三处往返实测，六份夹具逐字节相同**）

| 项目 | 状态 |
| --- | --- |
| `SerializedSolution` 往返 | ✅ 实测 |
| `RemoteTaskSubmitRequest` 往返 | ✅ 实测 |
| `configuration` / `sha256` 摘要 | ✅ 实测 |
| **CP checkpoint envelope（`checkpoint-envelope-v3.json`）** | ✅ **逐字节相同**（复核：1555 字节 / SHA-256 `8c252e59…`） |
| **checkpoint wire 契约表（`checkpoint-wire-contract.tsv`）** | ✅ **逐字节相同** |
| **analysis 语义契约（`analysis-contract.tsv` / `analysis-cases.tsv`）** | ❌ **本仓连文件都没有** |

**把这三处的证据强度逐个查清**，并把"fixture 来自"这件事钉住：

| 项目 | 证据形式 | 强度 |
| --- | --- | --- |
| `SerializedSolution` 往返 | 四份 fixture **逐字节复制**自 `12fdb587`（用 `git cat-file` 比对 SHA-256，**四个都 SAME**） | **强**：字节级相同，不依赖任何一侧重新编码 |
| `RemoteTaskSubmitRequest` 往返 | C# 产出 `submit-request-csharp.json`，**由对侧以 `ignoreUnknownKeys = false` 严格解码通过** | **强**：严格模式拒绝未知字段，通过即证明字段集一致 |
| `configuration` / `sha256` 摘要 | 两侧对同一输入**实测一致** | **中**：只覆盖这一个输入，不是全部配置空间 |

**把夹具数了一遍：六份逐字节相同，不是四份。** 只钉了 `RemoteFixtures/` 下那四份，
而 `Core.Tests/Fixtures/analysis-fixtures/` 下那两份**同样是逐字节复制**的，一直没被算进这句话里。

| 夹具 | SHA-256（前 16 位） | 两侧 |
| --- | --- | --- |
| `remote-cp-result-v1.json` | `58fc538acb808728` | **SAME** |
| `remote-cp-result-v2.json` | `ebad2593bdd9279b` | **SAME** |
| `remote-linear-model-v2.json` | `71a086af6dcac0e0` | **SAME** |
| `remote-linear-result-v2.json` | `e76a8e93e75046b7` | **SAME** |
| `checkpoint-envelope-v3.json` | `8c252e592cfa26cb` | **SAME** |
| `checkpoint-wire-contract.tsv` | `6eaffdfbdbbb8521` | **SAME** |

**"只有这三处实测"这句话低估了自己的证据**，而低估的代价与高估一样真实：
4.8 那一行早就写着"fixture **逐字节相同**"，**而第七节还在说只有四份**——两处的账对不上，
下一个人只会信更保守的那个，然后**重做一遍已经做过的事**。

**counted the fixtures: six are byte-identical, not four.** pinned only the four under `RemoteFixtures/`, while the two under
`Core.Tests/Fixtures/analysis-fixtures/` are **equally byte-identical copies** and were never counted. **"Only these three are measured" understated its own evidence**,
and understating costs as much as overstating: row 4.8 already said the checkpoint fixture is **byte-identical**, **while section seven still spoke of four** — the two
accounts disagree, and the next reader trusts the more conservative one and **redoes work that was already done.**
**之前，"逐字节复制"这句话没有任何判据守护。** 改一个空格，几十条解码判据照样全绿——
它们测的是"这份文件能解码"，**不是"这份文件来自"**。现在四个 SHA-256 钉住了。

**除此之外没有任何跨语言互通或性能结论。** 基准夹具只证明"夹具被覆盖"，不证明性能。

**measured how strong each of the three pieces of evidence is** and pinned the claim that the fixtures are the recorded ones. Before that claim was
**unguarded**: one edited space would void the cross-language evidence while every decoding criterion stayed green, because they test "this file decodes", **not "this file
is the recorded one"**. Four SHA-256 values now pin it. **Apart from these three there is no cross-language interoperation or performance conclusion** — the
benchmark fixtures only prove the fixtures are covered, not performance.

## 八、外部未验收项（**单列，不计入上表定级**）

上表的定级依据是"代码在不在、接口通不通、测试覆盖到哪"。**下列项目需要外部环境或真实后端**。

**把这张表整个重测了一遍**（逐个 `Run*Integration=true` 真跑），**四条从"未验收"变成"已验收"**：

| 项目 | 需要的条件 | 现状 |
| --- | --- | --- |
| SCIP 真实求解 | 自带原生运行时 | **✅ 已验收**（46 个门内文件，每次门禁真跑） |
| Gurobi 真实求解 | 商业许可 + 原生运行时 | **✅ 已验收**：`RunGurobiIntegration=true` 跑出 **829 通过** |
| CPLEX 真实求解 | 商业许可 + 原生运行时 | **✅ 已验收**：**26 通过** |
| COPT 真实求解 | 许可 + 运行时 | **✅ 已验收**：**37 通过** |
| Mosek 真实求解 | 许可 + 运行时 | ❌ **SDK 在，licence 缺**：`err_missing_license_file(1008)` |
| MindOpt 真实求解 | 许可 + 运行时 | ❌ **SDK 在，licence 无效**：`License is not valid` |
| Hexaly 真实求解 | 许可 + 运行时 | ❌ **SDK 不在本机**（`HEXALY_HOME` 未设） |
| Gurobi 11 真实求解 | 同上 | ❌ **未试**：`Gurobi110ManagedAssembly` 未设，走的是 10.x 绑定 |
| 数据库持久化往返 | 真实 H2/MySQL/PostgreSQL/MongoDB/Redis/SQLite 实例 | **SQLite 有真实往返**（：**2 条**判据、**文件库** + 关连接再开——`:memory:` 会随连接消失，用它测"落盘"等于没测）；**MySQL / PostgreSQL / Kafka 有真实往返（各三条判据）**（见下）；**MongoDB 未测**（无实例）；**Redis 已实测**（4 条真实往返 + 2 条默认构建判据，见上） |
| 消息插件往返 | 真实 Kafka broker | ✅ **三条判据**（生产→消费且键值都对 / JSON 载荷往返 / 不可达 broker 在超时内返回空而不挂住）；**同时修掉 `Dispose` 不 flush 导致的消息静默丢失** |
| 跨语言互通 | 两侧同版本同时运行 | **只有三处实测**（见第七章） |
| 性能对比 | 两侧同夹具同机器 | **仍然完全没有**，但**夹具覆盖已补齐**：15 个工作负载、本仓 15 个，两个新基准实跑通过 |
**只补了夹具，没有做任何性能结论。** 这两件事必须分开：

| 做了什么 | 能得出什么 |
| --- | --- |
| 补齐两个缺失的基准工作负载，并各实跑一次（`small 1 1 1`，各 6 个基准） | **夹具覆盖齐了**，性能**仍然一句没说** |
| — | 同夹具同机器的两侧对比**仍未做**：需要同时跑 Kotlin 侧，而本仓库不切那个 checkout |

**夹具覆盖是性能对比的必要条件，不是它本身。** 补齐之前，"性能没测过"和"没有可比的东西"分不开；
补齐之后只剩前者——**而前者正是这条偏差的本意。**

**only filled in the fixtures; it draws no performance conclusion.** Those are separate: the two missing workloads now exist and each ran once (`small 1 1 1`,
six benchmarks each), so **the fixture coverage is complete and nothing has been said about performance**. A same-fixture same-machine comparison **still has not been
done**: it needs the Kotlin side running too, and this repository does not switch that checkout. **Fixture coverage is a precondition for a comparison, not the
comparison** — before this, "performance was never measured" and "there is nothing to compare" were indistinguishable; now only the former remains, **which is what
the deviation was about.**
**探测了本机的数据库与 broker**（`logs/b950-probe.md` 记了完整过程），结论和求解器**恰好相反**：

| 目标 | 端口 | 结论 |
| --- | --- | --- |
| SQLite | — | **✅ 不需要服务器**，`:memory:` 真库往返，7 条判据 |
| MySQL | 3306 **开着** | ✅ **真实往返（3 条判据）**：入口连通 / 增查改删 / 不可达报原因；账号 `ospf_test` 由 `C:\environment\infra\ospf-test-setup.ps1` 自建，口令不进仓库 |
| PostgreSQL | 5432 **开着** | ✅ **真实往返（三条判据）**，同 MySQL |
| MongoDB | 27017 **开着**（本机容器 `ospf-infra-mongo`） | ✅ **真实往返（三条判据）**：`ping` 真连通 / 插入-按主键查-替换-删除 / 不可达报原因 |
| Redis | 6379 **开着** | ✅ **真实往返（4 条判据）** |
| Kafka | 9092 **开着**（本机单节点容器 `ospf-infra-kafka`） | ✅ **真实往返（三条判据）**；**并且据此修掉一个真缺陷：`KafkaProducer.Dispose` 不 flush，排队消息静默丢失、topic 偏移停在 0** |

**连接凭据一直在 `C:\environment\infra\.env` 里，Kafka 也早就有实例可起——** **此前"钥匙没有"的判断是错的。**

**MySQL、PostgreSQL、Redis、Kafka 四个后端都有真实往返判据，** **MongoDB 跑在本机容器里、也有三条。** **五个后端里只剩 H2／JDBC client／Ktorm／MyBatis 这几个翻译层没有对应物，理由见上表。**

**The credentials had been in `C:\environment\infra\.env` all along and Kafka only needed starting; the earlier "the key is missing" conclusion was wrong.** **MySQL, PostgreSQL, Redis and
Kafka all have real round-trip criteria, and MongoDB runs in a local container with three of its own.** **Of the five backends only the H2 / JDBC client / Ktorm / MyBatis translation layers have
no counterpart here, for the reasons in the table above.**

**probed this machine's databases and broker**, and the answer is **the opposite of the solver case**: SQLite needs no server and does real round trips through
`:memory:`; MySQL and PostgreSQL **are running** but reject every credential this machine has; MongoDB and Kafka have no instance. **The solver case was "the gate was not
open"; this one is "the key is missing"** — the first is one flag away, the second needs credentials **this machine does not have**, and nobody could run it here.


**上一版把前四行全写成"未验收"。** 那不是"保守"，是**没去开门就把门当成锁着的**：
五个 SDK 都装在本机，四个有可用 licence，而集成门只需要一个 `-p:Run*Integration=true`。

**re-tested this whole table** by opening each gate, and **four rows moved from unverified to verified**. The previous version marked all four unverified — not
conservatism but **assuming a closed door is locked without trying the handle**: five SDKs are installed here, four have a working licence, and the gate only needs
`-p:Run*Integration=true`.
**这张表的意义**：上表里那些"✅"与"◐"回答的是"代码在不在"，
而这里回答的是"我们真的跑过它吗"。**两个问题的答案不一样**。

## 九、已知的系统性偏差

这些偏差**不是某个模块的缺口，而是全局性的**。**逐条实测**（`logs/b933-deviations.py` 与下面的证据），
能证的改成实测、证不了的写明**为什么证不了**——**避免下一个人再花一批去试同一件事**。

| # | 偏差 | 实测结论 | 证据 |
| --- | --- | --- | --- |
| 1 | **前置校验 vs 模型约束** | **仍然成立** | `Framework.Bpp2d` 里 **11 个文件**在求解前报 `IllegalArgument`，而不是把线性约束加进 `MetaModel`（第 12 个是 `Domain/Bpp2dErrors.cs`，它只**映射**错误码，不是校验点）。两者在"非法数据被拒"上相同，**在优化结果上不同** |
| 2 | ~~`PreciseAssignment` 模型缺失~~ | **已撤回** | 模型在 `Framework.Bpp2d` / `Framework.Bpp3d` 的 `LayerAssignment/Model/` 下都有 |
| 3 | **PWL 体积路径缺失** | **仍然成立**，且 **查明缺口比这条写的大得多** | 见下 |
**查清了第 3 条的真实形状。** 这条原来只说"`PackingRendererAdapter` 里 0 处 `PWL`，半径结果没进渲染"——
**那是一个症状，不是一个缺口的大小。** 实测：

| 层 | | C# |
| --- | --- | --- |
| 分段线性**符号** | `UnivariateLinearPiecewise` 等 | **有**（`Core/Symbol/Function/`，且 Demo2 的 `Envelope`/`StowagePosition` 真在用） |
| 函数**展开层** | `FunctionExpansion.kt` **576 行** | **完全没有**：`Core/Solver/Function/` 只有 `FunctionSolverCapabilities.cs` 一个能力声明文件 |
| 插件**原生写入** | Copt 22 处、Cplex 6 处 `PWL` | **没有**；Gurobi 只有**探测**（反射 `SetPWLObj`/`AddGenConstrPWL`） |

**"探测未实现"这个说法把三件事混成了一件。** 探测是最容易的一层，而它已经在了；缺的是中间那 576 行。
一份把缺口语焉不详地写成"未实现"的文档，会让下一个人以为补上反射就够了——**那正好是最不缺的一层。**

**pins down what item 3 really is.** It used to say only "no `PWL` in `PackingRendererAdapter`, the radius result never reaches rendering" — **a symptom, not the
size of the gap**. Measured: the piecewise **symbols** exist here and Demo2 really uses them; the **function expansion layer** (`FunctionExpansion.kt`, **576 lines**)
is **entirely absent** — `Core/Solver/Function/` holds only a capability declaration — and the plugins have no native writes (22 for Copt, 6 for Cplex), while
Gurobi has only **detection**. **"Detection not ported" conflated three things**, and detection is the easiest layer and is already here. A vague "not ported" lets the
next person believe adding reflection is enough — **which is exactly the layer that is not missing.**

| 4 | **通配符泛型缺失** | **仍然成立** | `MultiArray` **11 个**源文件里 **5 个**用 `where` 约束表达 Kotlin 的协变/逆变；C# 没有声明点变体 |
| 5 | **原生后端行为未验证** | **收窄了，但没消失** | 见下 |
| 6 | **`core.analysis` 仍缺多数分析器** | **更新：`effectiveDomain` 已搬（二值性是类型不变量）；三条主链的编排只剩 `analyze` 类本体（需真实后端）** | 见下 |
### 偏差 **四个后端在这个环境里真跑过**

**把六个插件的集成门逐个真跑了一遍**，结论推翻了两版措辞：

| 插件 | SDK 在本机 | 集成门结果 | 证据 |
| --- | --- | --- | --- |
| SCIP | 自带 | **✅ 真跑** | 46 个门内文件，每次门禁都跑 |
| Gurobi | ✅ `$GUROBI_HOME` | **✅ 真跑，829 通过** | 实跑 |
| Cplex | ✅（`CplexManagedDirectory` 默认路径） | **✅ 真跑，26 通过** | 实跑 |
| Copt | ✅ `$COPT_HOME` | **✅ 真跑，37 通过** | 实跑 |
| Mindopt | ✅ `$MINDOPT_HOME` | ❌ **licence 无效** | `License is not valid` |
| Mosek | ✅ `$MOSEK_10_2_INSTALLDIR` | ❌ **licence 缺失** | `err_missing_license_file(1008)` |
| Hexaly | ❌ `HEXALY_HOME` 未设 | 不构建 | `HexalyManagedAssembly` 解析成 `/bin/Hexaly.NET.dll`，不存在 |
| Lingo / OptVerse | — | **没有真后端门** | 只有"不可用"路径 |

**上一版说"只有一个后端真跑过"，那是错的。** 真相是：**五个 SDK 都在本机**，其中四个有可用 licence，
两个没有。之前的措辞把"门没打开"当成了"跑不动"——**门是要显式开的（`Run*Integration=true`），没人开就等于没跑，但那不等于跑不动。**

**这处错误的方向和前两批相反**：那次是把验证写少了，这次是把"能验的"写成了"验不了的"。
**两种都会让下一个人做错决定**——前者以为没测过要重测，后者以为测不了就不去测。

**ran all six plugin integration gates.** The previous version said only one backend really runs here; **that was wrong**. Five SDKs are on this machine and
four have a working licence. **The old wording mistook "the gate is not open" for "it cannot run"** — the gate needs `Run*Integration=true`, and nobody opening it means
it did not run, **which is not the same as cannot run**. This error points the opposite way from : that one under-reported verification, this one wrote "verifiable" as
**"unverifiable"**. **Both make the next person decide wrongly** — the first makes them re-test what was tested, the second makes them skip what they could have run.
**逐条复核了上表**（`python logs/b948-gates.py`），四处的数是错的，已改：

| 项 | 原写 | 实测 | 为什么会错 |
| --- | --- | --- | --- |
| 第 1 条校验点数 | 11 | **11**（成立） | 但没写"排除了错误码映射文件"，别人复现会数到 12 |
| 第 4 条 `MultiArray` 文件数 | 13 | **11** | 多算 2 个 |
| 第 5 条 SCIP 门内文件 | 10 | **46** | **少算 36 个**——这是本节最离谱的一处 |
| 第 5 条 Cplex 门内文件 | 3 | **2** | 多算 1 个 |

**SCIP 那处错得最要紧**：它把"本环境唯一真跑的后端"的覆盖面写小了近五分之四。
一份把实际验证写得**比真少**的文档，会让人以为"这里几乎没测过"——**和夸大一样有害，只是方向相反。**

**re-checked every row above**, and four numbers were wrong: the `MultiArray` file count (13 → **11**), the SCIP gate's file count (10 → **46**), the Cplex
gate (3 → **2**), and item 1 needed its exclusion stated (the twelfth file only **maps** error codes). **The SCIP number mattered most**: it under-reported the coverage of
**the one backend that really runs here** by nearly four fifths. A document that understates what was verified reads as "almost nothing was tested here" — **as harmful as
exaggeration, just in the other direction.**

**所以准确的偏差是**：真后端行为**在 SCIP 上验证过**（门禁每次跑），**其余六个插件只验证了"没有 SDK 时显式不可用"**，
`Lingo` / `OptVerse` 连门都没有。**"验证过"与"能验证"是两件事，原措辞把后者说成了前者。**

### 为什么第 5 条**不能**在这个环境里补完

缺的是**外部许可与 SDK**，不是代码：Gurobi / Mosek 要 licence，Cplex / Copt / Mindopt / Hexaly 要商业 SDK。
**写一个"假后端"来验证真后端行为等于什么都没验证**——那正是 记过的那类缺陷（假脚本满足下界却不满足约束，看起来收敛）。
所以这条留在 `## 八、外部未验收项`，**不改成 ✅**。

**These deviations are global rather than module-level. measured each one**: what can be evidenced becomes a measurement, and what cannot says **why**,
so nobody spends another batch on it. **Item 5's original wording was wrong** — it claimed "every solver plugin only verified the no-SDK path", while in fact every
plugin has a real-backend gate; what is true is that **only SCIP actually runs in this environment** (10 gated files, exercised by every gate), the other six only
prove "explicitly unavailable without an SDK", and Lingo/OptVerse have no gate at all. **"Verified" and "verifiable" are different things, and the old wording
confused them.** It cannot be completed here because what is missing is **external licences and SDKs, not code** — and a fake backend verifies nothing, which is
exactly the defect class recorded. So it stays in section eight, **never marked ✅**.

### 偏差 **`Core/Analysis` 提供什么、不提供什么**

| 侧 | 文件数 |
| --- | --- |
| C# `src/Core/Analysis/` | **53** |

**提供**（`src/Core/Analysis/`，**53** 个文件）：分析状态与能力矩阵（`AnalysisStatus`、`CapabilityMatrix`、
`CriticalityProfile`、`ObjectiveTarget`／`ObjectiveTargetBounds`）、约束活动（`ConstraintActivityAnalyzer`、
`ConstraintActivityReport`、`ActivityStatus`、`ActivityEvidence`）、冲突与最小阻塞集（`ConflictEvidenceSupport`、
`MinimalBlockingSet`、`NativeConflictSeed`、`ConflictExplanation`、`ConflictDeletionCheck`）、扰动（
`ConstraintPerturbationPipeline`、`ConstraintPerturbationReport`）、临界约束分析（`CriticalConstraintAnalysisSession`、
`CriticalConstraintAnalysisReport`）、目标可达性（`TargetFeasibilityAnalyzer`、`TargetFeasibilityReport`、
`TargetOutputStatus`）、定整数灵敏度（`FixedIntegerLpSensitivity`、`FixedIntegerLpGates`、
`LinearSolverFixedIntegerLpSolver`）、修正建议（`CorrectionRecommendation`、`CorrectionProjection`）与
定义域处理（`EffectiveDomain`）。

**不提供**：**模式类分析器（IIS、活动约束、扰动重解）需要真实后端产出的原生证据**。
把它们写成不碰后端的空跑版本，正是本仓反复拒绝的那种"看起来有了"——**报出一个分析结果，
而那个结果不是任何求解器算出来的。** 需要这条能力时，前置条件是有一个能产出原生证据的后端，
而不是先补一个占位实现。

**一条判据的教训**：缺口标记的通病是**钉住一个现象，却以为钉住了整条推理**。
`QuadraticSolver_ShouldExposeNoDiagnosticAnalyzersByDefault` 的因果链表有四行，原先只钉住第 4 行
（"二次 `DiagnosticAnalyzers` 返回空"），而第 3 行（"没有二次 legacy IIS 实现"）早已不成立——
判据一句话都没说。现在的写法要求**那个分析器真的是原生 IIS**，并且**同时**钉"实现存在"与"调用方不存在"：
只钉前者会被读成"这条路通了"。

**一条自我作废的判据**：`QuadraticSolver_ShouldExposeNoDiagnosticAnalyzersByDefault` 的注释写明
"若将来实现二次 IIS，判据会红，提醒同步更新文档"。**它后来确实红了**——那正是它设计要发生的事。


### **二值性不是数值边界**

实现了 `TargetFeasibilityAnalyzer.kt` 的 `effectiveDomain` / `copyDomain`（`EffectiveDomain.cs`）。
`src/Core/Analysis/` 从 39 增至 **40**。

#### 一、六行规则

| 情况 | 结果 |
| --- | --- |
| 没有活跃证据集合 | 原样复制 |
| **`Boolean` 定义域** | **原样复制，永不放开** |
| 区间域，边界活跃 | 保留该边界 |
| 区间域，边界不活跃 | 放开到整数全域 |
| 稀疏域，稀疏活跃 | 保留 |
| 稀疏域，稀疏不活跃 | **降级成区间并放开边界** |

#### 二、`Boolean` 那一行

把它当成"上界 1、下界 0"的区间，就会在边界不活跃时**放开成全域**——
于是一个布尔变量被允许取 42。

**那不是一个数值边界被移除，那是一个类型被抹掉。**

#### 三、判据第一版骗过了自己

第一版给的是"稀疏**活跃**"：那条路不管有没有不变量都原样返回 `Values`，
**于是删掉不变量也不红。**

改成"稀疏**不活跃**"才区分开——**因为只有那一态会让 `Boolean` 落进"降级成区间"的路径。**

**第八次"判据名不副实"。**

变异（删掉不变量）→ 1 红（修判据之后）；变异（稀疏永不降级）→ 1 红；变异（复制共享数组）→ 1 红。

### **一条测不了的分支，写下来比假装测过强**

核对 `objectivesAgree`，并补了相对容差的下界判据。

#### 一、实现，且更严：`difference <= tolerance * max(1.0, |left|)`。
C# 侧相同，**并且多了容差合法性校验**（NaN / 无穷 / 负值一律判不成立）。

#### 二、`max(1.0, |left|)` 的下界是 1，不是 0

少了这个下界，量级接近 0 的两个目标会让容差窗口退化成 0，
**于是"两个都是 0"被判成不相等**——而它们完全相同。

补了 `(0.0, 0.0)` 与 `(0.0, 1e-12)` 两条判据后，**去掉下界的变异红了。**

#### 三、有一条分支测不了，我把它写下来

负容差那条校验**没法用返回值区分**：`difference` 永远非负，
所以 `difference <= tolerance * scale` 在负容差下自然为假——**删掉那道校验，行为不变。**

我试了这条变异，**它没红。** 与其留一条"看起来测了"的断言，**不如把"这条测不了"写下来。**

**"判据测不到的分支"被主动记录过一次先例**（`sync-over-async` 的上限）。

变异（去掉 `max(1.0, …)` 下界）→ 1 红；变异（去掉负容差校验）→ **没红，已如实记录**。

### **有解不等于证明了最优**

实现了 `solveTriad` 里四道判定（`FixedIntegerLpGates.cs`）。`src/Core/Analysis/` 从 38 增至 **39**。

求解本身要真实 LP 后端，**但"什么情况下不许下结论"是纯判定。**

#### 一、四道门

| 门 | 挡住的假结论 |
| --- | --- |
| 证明门控 | **受限求解返回的 incumbent 被读成"已证明最优"** |
| 对偶失败 | 对偶求解失败，却报告"局部有效性" |
| 无可映射对偶 | 模型有约束、一条对偶都没给，却报告"敏感度为零" |
| 单行回映 | **多行降阶冒充单个约束的范围** |

#### 二、证明门控的三态

| 条件 | 状态 | 为什么 |
| --- | --- | --- |
| 真证明了最优 | `Reachable` | 这才是结论 |
| **有 incumbent 但没证** | **`Unknown`** | **"返回了东西"和"证明了最优"隔着整个搜索空间** |
| 什么都没有 | `Unreachable` | 确实解不出来 |

**中间那一态是最容易写错的**：带时间上限的求解器几乎总能返回点什么。

#### 三、"不支持"不等于"未知"

对偶失败给 `Unsupported`，不是 `Unknown`。**这个区别决定了调用方是重试还是放弃。**

#### 四、一条反向对照

只测"有约束没对偶就拒绝"，会让"只要没对偶就拒绝"通过，
**而它把空模型的正确答案判成不支持。**

变异（incumbent 读成证明）→ 1 红；变异（空模型也拒绝）→ 1 红。

### **多给一条比少给一条更难发现**

实现了 `FixedIntegerLpSensitivity.kt` 的 `mapExplicitProvenanceDuals` 与 `mapExplicitProvenanceRanges`
（`ProvenanceDualMapping.cs`）。`src/Core/Analysis/` 从 37 增至 **38**。

#### 一、两条规则，都是"安静地"出错

| 规则 | 写错会怎样 |
| --- | --- |
| **一个来源只能有一条行** | 多条行指向同一来源时**取哪一条都是编的**，必须整条丢弃 |
| **来源必须在 `originalIds` 里** | 否则把中间层自造的行**当成原始约束的敏感度**报出去 |

**两条都是"多给一条"的错误，而多给一条比少给一条更难发现：**
**读者看到有数值就以为验过了。**

#### 二、第三条容易漏的：长度不一致

取 `getOrNull`。**用索引器会抛异常，而抛异常会让整份敏感度报告消失。**
**短的那一侧只是"这行没有来源"，不是错误。**

#### 三、两个映射共用一个实现

`MapDuals` 与 `MapRanges` 都转发到同一个私有 `Map<T, TValue>`。
**这不是抽象，是因为两条规则一模一样**——分开写迟早会有一条忘了查 `originalIds`。
判据里那条"区间映射用同一套规则"就是防这个的**反向对照**。

变异（允许多行来源）→ 2 红；变异（去掉 `originalIds` 检查）→ 2 红；变异（改用索引器）→ 1 红。

### **三态里最容易漏的是"支持"**

不加新函数，**补的是已有函数的判据缺口。**

#### 一、`capabilityAllowed` 逐行一致

```csharp
overrides.TryGetValue(capability, out declared) → return declared != Unsupported;
否则 → return matrix.ModelTypes.Count == 0 || matrix.Support(capability) != Unsupported;
```

**和 `FixedIntegerLpSensitivity.kt` 的 `capabilityAllowed` 一字不差。**

#### 二、但判据只覆盖了两态

"显式不支持"与"空列表"两条写了，**"显式支持"没写。**

于是这样一个实现照样通过：

```csharp
return declared == CapabilitySupport.Supported && matrix.ModelTypes.Count == 0;
```

**它把"声明了支持但矩阵有模型类型限制"判成不支持**——而声明本该赢。

补一条"模型类型受限 + 显式声明支持 → 必须为真"的判据后，这个变异红了。

#### 三、的教训

**"三态"的判据很容易只写坏的那一态。** 坏的那态会让人放心（"我测了拒绝路径"），
**而好的那态没测，等于没测过"声明能压过推断"这句话。**

变异（把显式支持降级）→ 1 红。

### **空和重复是两种错误**

实现了 `CriticalityProfile.kt` 的 `validateTargetBatch`（`CriticalityProfileBuilder.ValidateTargetBatch`）。

#### 一、`Build` / `TryBuild` 早已存在，缺的只有校验入口

实测发现 `CriticalityProfileBuilder` 已有 `Build` 与 `TryBuild`，**`TryBuild` 还保留了底层错误**
（`validation is Failed<…> failed ? failed.Error : …`）——**这部分此前就做对了。**

缺的是 `ValidateTargetBatch`：**批次级校验，不是 profile 级校验。**

#### 二、空和重复必须分开报

| 情况 | 消息 | 调用方该做什么 |
| --- | --- | --- |
| 空列表 | 至少需要一个 `ObjectiveTarget` | **补数据** |
| 重复 | `ObjectiveTarget` 不得重复：`objective:a` | **去重** |

**混成一句话，调用方无法判断该补数据还是该去重。**
所以判据里有一条**反向对照**：只查"是不是失败"会让"所有失败都报成空批次"的实现通过，
**而那会把调用方送去补数据。**

#### 三、变异第一次没编译

第一次变异写坏了语法（`string? duplicate = (string?)null; _ = targets; //` 后面跟着原表达式），
**编译失败 → 跑的是旧二进制 → "通过"不是证据。**
重写成"把阈值从 `> 1` 改成 `> 99`"才是一次能编译的变异，它红了两条。

变异（重复阈值改 99）→ 2 红；变异（空批次报成重复）→ 1 红。

### **九个条件里，两个最容易漏**

实现了 `CorrectionRecommendation.kt` 的三个纯函数到 `CorrectionProjection.cs`。

#### 一、`isVerifiedMinimalConflict` 的九个条件

| 条件 | 排除的假阳性 |
| --- | --- |
| `Status == Unreachable` | **目标其实是可达的** |
| `Validity == Verified` | 后端给的、没复验过的冲突 |
| `MinimalityVerified` | **只跑了部分删除检查** |
| `!BackgroundBlocking` | 阻塞来自固定背景，**删掉哪条成员都没用** |
| 目标一致 + 可行性目标不可达 | **解释的目标和可行性检查的目标不是同一个** |
| `Members` 非空 | 空集合让投影变成空操作 |
| `Members` 去重后数量不变 | **同一来源出现两次，"删一个就够"是假话** |
| 每个成员都能转成不可行成员 | 目标源这类不能"删" |
| 每个成员都在 `AvailableEvidence` 里 | **成员来自另一份证据集** |

**最容易漏的是第 4 条与第 5 条**：前者让"背景挡着"的解释声称每个成员都可删；
**后者让解释去描述一个它没检查过的目标。**

#### 二、排序必须在截断之前

先 `take` 再 `sort` 会留下"前几条"里恰好是最贵的那几条。
**第二排序键是稳定 ID**，否则同成本的结果在两次运行里顺序不同。

变异（去掉背景条件）→ 1 红；变异（去掉目标一致性）→ 1 红；变异（先截断后排序）→ 1 红。

### **结论可以抄下去，依据不能**



#### 一、为什么要单开一份台账

**结论可以抄下去，依据不能。** 汇总表说的是结论，逐条核对说的是依据——**照抄结论而不重核依据，
就会写出无据的"已闭"**。

#### 二、"实现不了"必须分三类

| 类别 | 含义 | 下一步 |
| --- | --- | --- |
| **环境所限** | 代码能搬，本机没许可 / 实例 / SDK | **换个环境就能验** |
| **语言所限** | 依赖 JVM 专有库 | **不是缺口，是等价替换** |
| **需真实后端** | 逻辑已搬，验证必须接真实求解器 | **接上后端才能验** |

**混成一类，读者只能统一当成"做不到"。**

#### 三、一条容易漏的区分

**MySQL 与 PostgreSQL 的服务器是开着的（3306 / 5432 可连）。缺的是凭据，不是服务。**

这条区分决定了下一步是"要个密码"还是"装个数据库"——**代价差一个数量级。**

#### 四、判据怎么守住它

- **每一条引用都必须能解析**：台账里写的 `P7 earlier` 必须在计划里搜得到，**拼错就等于"这条结论是编的"**
- **六个阶段都要有一节**
- **三类原因必须分开写**，且环境所限必须点名具体失败字符串
- **补写的收口必须自报家门**，而且要落在**状态行本身**，段内别处提到不算

变异（改一个引用号）→ 1 红；变异（P6 状态行写成普通"已收口"）→ 1 红。

**第四条第一次没抓住**：段内别处还有"补写"字样，宽松的 `Contains` 照样通过。
收紧到"状态行本身"才红——**第七次"判据名不副实"。**

### **编号的洞是唯一留下的证据**

新增 `PhaseRangeTests`（3 条）钉住 P4—P9 的阶段区间。**它当场发现两件事。**

#### 一、批次编号是全局的，不是每阶段从 1 起

实测：**P4 1—54、P5 55—65、P6 66—98、P7 99—151、P8 152—241、P9 242—1013。**

我第一次写判据时假设"每阶段从 1 开始"，**判据当场红。**
编号是**全局接力棒**，阶段只是它上面切出来的段。

#### 二、834 的正文被自己抹掉了

全局连续性判据报"1—1013 少一个"。查出来是 **834**：
总台账里明明写着"清单文档被自己抹掉又补回（834）"，**而 834 自己的正文却没有了。**

**原因是我写 833 时用整段替换改文件，把 834 那段一起覆盖掉了。**
标题在总台账里留着，正文没了，**于是编号出现一个洞而没有人发现。**

**"文档被自己抹掉"是一种不会报错的失效**：文件仍然合法，判据仍然通过（当时没有这条判据），
**只有编号的连续性还留着证据。**

#### 三、P4 第 1—5 条是已知的历史插页

它们是早期旧格式的条目，正文被插在 P6 区间中间（行 1186—1324），
而 P4 第 6—54 条在正常的 53000 行之后。**P4 因此出现两个块。**

**不改写历史**，而是把例外写成一份**可数的事实**：块序列必须是 `4 → 9 → 8 → 7 → 6 → 5 → 4`，
且 5—9 各只出现一次。**用"跳过"开关会把真正的插队一起放过。**

变异（改一个批次号）→ 1 红；变异（改一个阶段号）→ 2 红。
### **"没证出来"不能写成"已证明"**

实现了 `AssembleNotUnreachable`：目标不是"不可达"时的解释。

#### 一、三种状态，只有一种给空成员

| 目标状态 | `Members` | `Validity` | 消息 |
| --- | --- | --- | --- |
| `Reachable` | **空** | `Verified` | 目标可达，不存在阻塞 |
| `Unknown` | **全部证据** | **`Unknown`** | 目标检查未知 |
| `Unsupported` | **全部证据** | **`Unknown`** | 目标检查不受支持 |

**后两行最容易写错成"和可达一样"**：那会给出 `Members` 为空 + `Verified` 的一份解释，
**而它同时声称"没有冲突"和"已证明"——两句话都是从没做过的分析里编出来的。**

变异（把未知/不受支持当成可达）→ 1 红。

#### 二、反向对照是必须的

只查上一条，一个"永远返回全部证据 + `Unknown`"的实现照样通过，
**而它会把一份完全证明的"目标可达"降级成不确定。** 变异（可达也标 `Unknown`）→ 1 红。

#### 三、`Unreachable` 走这条路要直接拒

它要真去做删除复验。**给一份凑合的解释比抛异常更糟**——读者看不出少了什么。

### **状态和次数都对，只有观察列表不同**

实现了 `ConstraintPerturbationPipeline`（扰动分析的编排）。**所有判定早已单独搬过，这一段只钉顺序。**

#### 一、两条顺序规则

| 规则 | 写反会怎样 |
| --- | --- |
| 前置门在**任何**求解之前 | **一个做不了的分析先花了预算才说做不了** |
| 二分用扫描给出的端点 | 从空区间起步，**二分退化成没有依据的猜测** |

#### 二、变异没抓住，因为两条路径在状态上长得一样

跳过前置门时：`rhsAllowed` 为假，于是每条 delta **记一条 `Unsupported` 观察但一次也不求解**。
`AggregateStatus` 给出 `Unsupported`，`SolveCalls` 是 0——**我原来的两条断言全都通过。**

补一条 `ObservationList.Should().BeEmpty()` + 消息断言才分开：
**有门 → 零观察；无门 → 一条观察。** 同一个变异这才红。

**"判据名不副实"的第二种：**
**两条路径的可观测差异，我挑了不相干的那几个去断言。**

#### 三、夹具也错了一次

`TheRefusedBackend` 第一版只禁了删除测试，**RHS 仍可用**，于是前置门判"继续"。
补一个"两项都不允许"的矩阵才对。

#### 四、`ponytail:` 留了一处上限

扫描用 `sync-over-async`（`GetAwaiter().GetResult()`）。**无 `SynchronizationContext` 的宿主安全**；
若将来有 UI 宿主，按注释升级成 async 扫描——**规则一样，只是 probe 返回 `Task`。**

### **数函数，不数行数**

实现了 `unsupportedExplanation`，并**实测**三个壳的剩余缺口。

#### 一、实测方法与结果

对三个文件逐个取函数名，再到 C# 侧按名字搜：

- `ConflictAnalyzer` —— 函数名 7 个：`sourceOf` / `groupSummaries` / `remapBackendConflict` / `literalKey` / `assumptionSeed` / `nativeSeed` / `unsupportedExplanation` **全部已搬**；**缺 `analyze` 本体**
- `ConstraintPerturbationAnalyzer` —— 10 个已搬（`aggregateStatus` / `capabilityAllowed` / `classifyOutcome` / `deriveRhsSnapshot` / `integerPatternChanged` / `objectiveImprovement` / `refineThreshold` / `requestedDeltas` / `runRemoval` / `RunDeltas`）；缺 `solve(request)` 与 `analyze` 本体
- `CriticalConstraintAnalysisPipeline` —— 五阶段编排已搬；缺 `analyzeAndExplain` 与 `rankConstraintCandidates`

**用列表不用表格，原因是判据按结构取行**：`RemainingGapLedgerTests` 按"第一行以 `| \`文件名\`` 开头"取行，
写成表格会把那张缺口台账的行顶掉。**这是同一个坑的第二次踩，判据又一次当场红。**

**结论：三个壳缺的不是功能，是把 `solve` 接上的那一步。** 这也解释了为什么"剩 3 个分析器"
这个数字一直没动——**它数的是类，不是能力。**

#### 二、`unsupportedExplanation` 的三处刻意差异

| 字段 | 这里 | 正常路径 | 为什么 |
| --- | --- | --- | --- |
| `Validity` | `Unknown` | `Verified` | 什么都没证过 |
| `Minimality` | `NotChecked` | 由收缩定 | 一次复验都没跑 |
| **`Members`** | **等于全部证据** | 收缩后的成员 | **没有依据排除任何一条** |
| **`TargetFixed`** | **`false`** | `true` | **目标没被固定住去验证** |

**`Members` 取全集最容易写错**：空集合看起来更"诚实"，**但它会让下游按"最小冲突为空"去读，
而真相是"这次分析根本没做"。**

变异（`Members` 置空）→ 1 红；变异（`TargetFixed` 改成 `true`）→ 1 红。

### **"没测"第六次，这次它连停都不该停**

实现了固定 delta 扫描 `RunDeltas`。**扰动分析的纯逻辑至此全齐。**

#### 一、三条停止规则

| 情况 | 处理 | 写错会怎样 |
| --- | --- | --- |
| 首个 `Effective` | **记下并停** | 继续扫会把后面的有效 delta 覆盖掉前面的 |
| `NoObservedEffect` | 记下，**继续** | 停在这里会漏掉真正有效的那个 |
| **`Unknown`** | **既不记也不停** | 当成"无效果"伪造下界；**停在这里让扫描提前结束** |

**第三条是那个家族的**。

#### 二、变异第一次没抓住，原因是"被后面覆盖了"

用例 `TheUnknownDelta_ShouldNeitherRecordNorStop` 的序列是 `Unknown → NoEffect → Effective`。
把 `Unknown` 也当成无效果时，**后面那条 `NoEffect` 把被污染的值覆盖掉了**，断言照样通过。

补一条"**末尾是 `Unknown`**"的用例，同一个变异才红。

**这**判据没在测它声称要测的东西。**
四次里有三次是同一个形状：**用例构造得让变异落到一个不影响断言的位置。**

#### 三、`firstEffective` 用 `??=` 是错的

它赋值后**立刻 `break`**。改成 `??=` 而没有 `break`，扫描会继续，
**而报告给出的区间上界会取自一个不是"首个"的 delta。** 变异 → 1 红。

### **"恰好一项不可用"才是判据要测的**

实现了扰动分析的前置门 `Preflight`。

#### 一、两条"整份 Unsupported"路径

| 情况 | 结论 | 为什么 |
| --- | --- | --- |
| 没后端 | 整份 Unsupported | 什么都做不了 |
| **两项都不可用** | 整份 Unsupported | **报告没有可说的东西** |
| 只要求 RHS 且不允许 | 整份 Unsupported | 策略只问了 RHS，而它就是做不了 |
| **一项可用、另一项不可用** | **继续** | **不可用的那项在自己的阶段给出 Unsupported** |

**第二条最容易漏**：只看"没后端"会把"有后端但两项都不允许"漏过去，
**于是分析照样开始，跑完给出一份什么都没有的报告。**

#### 二、也不能过度拒绝

`!rhsUsable && !removalUsable` 里的 `&&` 与 `||` 只差一个字符，**而 `||` 会把纯删除测试无理由地拒掉。**

#### 三、变异没抓住，是判据没在测

最初写的两条"应当继续"用例，**都是两项可用**——于是"任一项不可用就拒绝"的变异照样通过。

**补上"恰好一项不可用"的用例后，同一个变异才红。** 这是第三次出现
"判据名不副实"。

### **"没测"第五次，这次是"没变"**

实现了 `capabilityAllowed` 与 `integerPatternChanged`。`aggregateStatus` 已在 搬过，不重复。

#### 一、任一侧解缺失 → `null`，不是 `false`

**这是"没测不能写成没变"这个家族的第五次**。

写成 `false` 会让报告声称"扰动之后整数解的模式没变"——**而那份解根本没拿到，
于是"没变"这个结论是从空气里来的。**

变异（`null` 改成 `false`）→ 1 红。

#### 二、能力门的两条规则

| 规则 | 写反了会怎样 |
| --- | --- |
| **显式声明优先于矩阵推断** | 声明形同虚设 |
| **模型类型列表为空 = 没有限制** | **一个没声明模型类型的后端被判成什么都不会做** |

第二条尤其容易读反：它是 `Count == 0 || Support(...)`，而"列表为空就拒绝"看起来同样合理。
**而后者会把一个无限制的后端判成无能。** 变异 → 2 红。

#### 三、这次我写了条空转的判据

`TheMissingSolution_ShouldGiveNull` 第一版只调用了一个空方法，**没有任何断言**——
一个什么都不做的实现照样通过。

**这是我一直在抓的那个缺陷，这次是我自己写的。** 改成三条 `Should().BeNull()` 之后，
变异才抓得住。

### **目标可达时不该有"冲突解释"**

实现了 `CriticalConstraintPipeline`（五阶段编排）与 `CriticalPipelineBackends`（委托注入）。
**`CriticalConstraintAnalysisPipeline` 的逻辑到此齐了。**

#### 一、三条编排规则

| 规则 | 顺手写会怎样 |
| --- | --- |
| 阶段 1 失败就整批失败 | **吞掉错误继续跑，报告带着空的活动性结果出去** |
| 短名单为空但有候选要记原因 | 静默跳过，读者以为"没有值得扰动的约束" |
| **冲突只在目标不可达时才跑** | **目标可达还跑冲突，报告带一份"冲突"解释，而目标明明没问题** |

**第三条最容易写错**：把冲突分析当成"阶段 5 的一部分"无条件跑。
一份目标可达的报告里出现"最小阻塞集合"，**读者会去找一个不存在的阻塞。** 变异 → 1 红。

#### 二、漏斗只算一次

它在这里算一次，**扰动的短名单从它来**。算两次就可能与报告里那份不一致——
**而两份漏斗在同一个报告里，读者无法判断该信哪个。**

#### 三、委托注入让编排可测

五个阶段全部以 `Func<…>` 注入，**于是"顺序对不对、什么时候停、错误怎么传"都能在没有后端的情况下钉死。**
阶段错误**原样转发**，不换成一句泛泛的"某阶段失败"——原因就在那个错误里。变异 → 1 红。

#### 四、八条理由

此前的七条加了一条 `ConflictBackendMissing`，`AreReasonsDistinct` 随之覆盖到八条。

### **"我关了它"和"环境里没有它"是两件事**

实现了 `CriticalPipelineStages`：每个阶段"跑不跑、不跑怎么说"的决策。

#### 一、三种阶段，两种理由

| 阶段 | 理由一 | 理由二 |
| --- | --- | --- |
| 扰动 | 调用方关掉了 | **没提供扰动后端** |
| 目标 | 调用方关掉了 | **没提供目标条件** |
| 局部敏感度 | 调用方关掉了 | 没提供固定整数 LP 后端 |

**混成一条，读者就无法区分"我关了它"和"环境里没有它"** ——
前者是配置选择，后者是能力缺失，**而报告的可信度依赖这个区别。**

#### 二、判断顺序决定措辞

**"关掉 → 没后端 → 没候选"。** 先判后端的话，一个"关掉扰动且没有后端"的分析会报"后端未提供"——
**而真正的原因是调用方根本没要它，于是读者会去找一个不需要的后端。** 变异 → 1 红。

目标阶段同理：**说"没提供目标"会让人以为漏传了参数。**

#### 三、一条结构性判据

`AreReasonsDistinct()`：**七条理由两两不同**。合并任意两条就红。

**这条不测某个分支对不对，测的是"这些理由还是七条"。** 变异（把两条合成一条）→ 1 红。

### **"没跑"要同时做两件事**

实现了 `CriticalConstraintAnalysisReportBuilder` 与 `CombineStatus`。

#### 一、最弱环节决定整体

**顺序是 `Reachable` > `Unreachable` > `Unknown` > `Unsupported`，取最弱者。**

| 组合 | 结果 | 为什么 |
| --- | --- | --- |
| `Reachable` + `Unreachable` | `Unreachable` | 证出来的不可能性胜过可达 |
| **`Unreachable` + `Unknown`** | **`Unknown`** | **没结论的阶段比证明弱** |
| `Unknown` + `Unsupported` | `Unsupported` | — |

**第二行是最容易排反的**：`Unreachable` 是"证出来了"，`Unknown` 是"没证出来"。
排反会让一个没结论的阶段**把已证明的结论拉成"未知"，丢掉已有的证明。** 变异 → 1 红。

#### 二、"没跑"要同时做两件事

| 只做一件 | 后果 |
| --- | --- |
| 只记原因，不改状态 | **一份"六个阶段没跑"的报告带着 `Reachable` 出去**——而它意思是"一切正常" |
| 只改状态，不记原因 | 读者不知道少了什么 |

变异（只记原因不改状态）→ 1 红。

#### 三、目标值是**条件**要求

扰动报告为空时不需要值，**非空时才需要**。无条件要求会让"没跑扰动"的正常报告也失败。
变异（改成无条件）→ 1 红。

#### 四、`Build` 最后跑一遍 `Validate`

报告自己说不过去就不交出去——**那四条不变量在这里变成一道出厂检查。**

### **先核对"还剩几个"，再动手**

实现了 `AnalysisBaselineSummary` 与 `CriticalConstraintAnalysisReport`（含 `Validate` / `BlockingSummary` / `HasMinimalBlockingSet`）。

#### 一、此前我记错了一个不存在的文件

此前的收口写着"下一步做 `MinimalConflictAnalyzer`"。**这个文件在根本不存在。**

`git ls-tree 12fdb587 | grep core/analysis/` 一共 27 个文件，分析器只有 5 个：

| 分析器 | C# 侧 |
| --- | --- |
- `ConstraintActivityAnalyzer` —— **存在**
- `TargetFeasibilityAnalyzer` —— **存在**
- `ConflictAnalyzer` —— **缺类本体**（逻辑已拆到 `ConflictEvidenceSupport` / `AssumptionConflictSeed` / `NativeConflictSeed` / `ConflictDeletionCheck` / `ConflictExplanationAssembler`）
- `ConstraintPerturbationAnalyzer` —— **缺类本体**（逻辑已拆到 `ConstraintPerturbationSupport`）
- `CriticalConstraintAnalysisPipeline` —— **缺类本体**（补了它依赖的报告类型）

**用列表不用表格是刻意的**：`RemainingGapLedgerTests` 按"第一行以 `| \`文件名\`` 开头"取行，
我先前写成表格，**把那张缺口台账的行给顶掉了**——判据当场红。

**"还剩 3 个分析器"这句话本身没错，但"缺"指的是缺那个类**，不是缺逻辑。
**此前的措辞让它读起来像"还差三块没写的功能"，实际是"还差三个把已有零件装起来的壳"。**

#### 二、四条不变量

| 不变量 | 违反后报告会说什么 |
| --- | --- |
| schema 版本非空 | 一份无法追溯格式的报告 |
| 有目标身份与方向 | 一份不知道在优化什么的报告 |
| **已证明最优必须带目标值** | **"已证明最优"但报不出最优值** |
| **背景阻塞不得同时声称最小集** | **"最小集是空的"与"阻塞来自背景"同时成立** |

**第三条最关键**：两个字段单独看都合法，**合起来是自相矛盾的一份报告。**

#### 三、三种阻塞摘要分开说

没跑 / 背景阻塞 / 有最小集。**第二种最容易漏**——背景阻塞时 `Members` 里没有约束，
**若走"最小集"那条分支，摘要会声称"0 个原始证据"，而真实原因是固定背景。**

变异（去掉已证明最优不变量）→ 1 红；变异（去掉背景分支）→ 1 红。

### **"没有冲突"不等于"没验证过"**

实现了 `ConflictExplanationAssembler`：把可行性结果与收缩结论拼成一份 `ConflictExplanation`。
**`ConflictAnalyzer` 的逻辑到此齐了。**

#### 一、全集与会员不能混用

`AvailableEvidence` 是全集，`Members` 是收缩后的成员。用 `Members` 当 `AvailableEvidence`，
读者会以为"这些就是全部证据"，**于是那份"最小冲突"看起来像整个模型。** 变异 → 1 红。

#### 二、三条消息互斥，背景阻塞优先

| 条件 | 消息 |
| --- | --- |
| 背景阻塞 | "阻塞来自固定背景，不能形成约束级 MUS" |
| 部分 | "至少一个删除复验未知或未执行" |
| 都不是 | 无 |

先判"部分"的话，背景阻塞那份会带着一条"复验未完成"的消息，**而真正的原因在别处。** 变异 → 1 红。

#### 三、目标可达时仍然 `Verified`

**"没有冲突"不等于"没验证过"。** 填 `Unknown` 会让读者怀疑一份恰恰被完全证明的报告——
**而它正是唯一被完全证明的那种结论。** 变异 → 1 红。

#### 四、我错了两轮

① 第一条判据的 probe 把 `Reachable` 当成"删除"写（同一个坑），改成"候选里还有谁"才对；
② 第二条判据的夹具让 `Members` 为空，**"投影里没有约束"在空集上恒真**，两个标志没法同时为真，
变异自然抓不住。补成两条来源后才成立。

**第二次是"判据自己没在测那件事"，和 同型。**

### **单轮就够，因为判定是单调的**

实现了删除收缩循环 `ConflictDeletionCheck.Shrink`。

#### 一、为什么不用反复迭代

可行性判定是单调的：**某成员删除后已证明可达，继续删别的来源不会让那个候选重新变得不可行。**
所以单轮收缩就够。

#### 二、两条"只标记、不改动"的规则

| 情况 | 处理 |
| --- | --- |
| 预算耗尽 | `Partial`，**不再删任何来源** |
| 复验没结论 | `Partial`，**那个来源留在 `active` 里** |

**第二条最容易写错**：把没结论的来源从 `active` 删掉，报告会少一个真实原因，
而 `members` 是从 `active` 推出来的——**于是整份报告一致地错。**

变异（把没结论的来源删掉）→ 1 红。

#### 三、空约束投影不是约束级 MUS

约束投影为空时，阻塞来自布尔/稀疏值域这类**固定背景**。这种结果有用，
**但不是约束级 MUS，绝不能标成 `Irreducible`。** 变异（忽略背景阻塞）→ 1 红。

#### 四、最终证据门槛

每一个留下来的成员都必须有自己的、target-fixed、已证明可达的删除复验，否则不能声称 `Irreducible`。
单轮收缩本来满足这个条件，**这道门槛是防止未来改动破坏它。**

### **删除后可达，所以要保留**

实现了删除复验的判定 `ConflictDeletionCheck`。**复验循环本身没搬**——它要真的跑一次求解。

#### 一、方向与直觉相反

| 删除后目标 | 结论 | 为什么 |
| --- | --- | --- |
| `Reachable` | **保留** | 没有它目标就够了，**说明它是冲突的一部分** |
| `Unreachable` | **移除** | 删掉它仍不可达，**说明它不是** |
| `Unknown` / `Unsupported` | 都不改，标记部分 | 没测出来，**不能当成任何一端的证据** |

**第一行跟直觉相反**：删除之后变得可达，所以这个来源是必要的。反过来写，整份冲突恰好取补集——
**而且它仍然"看起来像一份冲突"。**

#### 二、第三种：既不移除也不保留

把"没测出来"当成 `Unreachable`，会**把一个真实原因从冲突里删掉**。
于是报告给出的那份"最小冲突"少了一条，**而它看起来完全正常。**

所以复验记录里 `Removed` 与 `Retained` **两个都是 `false`**，另外用 `MakesPartial` 标记。

变异（方向翻转）→ 1 红。

#### 三、零次复验不能说"已是最小"

`Irreducible` 的意思是"每一条都试过了、每一条都必要"。上限为 0 是"不做复验"的合法配置，
**而一次没试过就说它，是凭空断言。** 变异（去掉零次判断）→ 1 红。

### **"顺手补全"会把合法请求变成建模错误**

实现了 `nativeSeed`：直接在派生模型上请求后端自己算冲突，不加 guard 字面量。
**两个种子策略到此都齐了。**

#### 一、只传约束类 activation id

有一段很容易被"顺手补全"的推理：

| 事实 | 顺手补全会做什么 | 后果 |
| --- | --- | --- |
| 每个后端都保证为**约束**建 activation | — | — |
| 原始边界等于安全基础值域时，后端**可以省略**那条 activation | **把边界 id 也传进去** | **一次合法的原生请求变成"未知 id"建模错误** |

所以 `ActivationIds` 只收 `DiagnosticSource.Constraint`，**边界与值域一律不传**。

变异（把边界也传进去）→ 3 红。

#### 二、"不指定"与"指定零个"是两种请求

没有约束类证据时返回 `null` 而不是空集。**部分后端把两者当成不同请求。**

#### 三、不请求冲突，就等于没有冲突

`CollectConflict = true` 必须显式设。**不加它，后端把这次求解当普通求解，返回的冲突永远是 `null`**——
而"没有冲突"和"没请求冲突"看起来一样。变异（关掉它）→ 1 红。


### **变异没抓住，说明判据没在测那件事**

实现了 `assumptionSeed`：给每条约束加 guard 字面量，用假设求解取冲突，再重映射。
**这条路不要求后端暴露原生 IIS 接口**，所以它是两个策略里更通用的那个。

#### 一、派生模型里不能留着原始约束

第一次跑，两个判据都拿不到种子。原因：**我在重建出来的模型上又用同一个 id 加了 guard 约束**——
原始约束还在，`AddConstraint` 撞 id 失败。

更根本的是语义：assumption 派生模型的做法是**"每条约束换成一个 guard 版本"**，不是"在旁边再加一条"。
加在旁边，原始约束仍无条件生效，**假设求解永远不可能通过取反 guard 发现冲突，种子必然为空。**

补了 `WithoutConstraints()`。**这也说明 C# 快照缺的派生手段比想象的多**（`TryWithConstraint` 是 补的）。

#### 二、判据第一次写错了，变异当场暴露

`TheUnprovenInfeasibility_ShouldGiveNoSeed` 第一次只给了 `Infeasible(Claimed)`、**没带冲突**。
去掉证明检查后它**仍然通过**——因为重映射本来就会给空结果。

**这条判据分不清"因为没证明"和"因为没冲突"。** 补上真实冲突 + 一条 `Verified` 反向对照，
再跑同一个变异，这次红了。

**这是第一次由"变异没抓住"反过来修判据。** 以前都是变异抓住了、判据有效；
这次是变异抓不住、**说明判据在测别的东西。**

#### 三、`null` 与空集合的区别

六处失败点都返回 `null` 而不是空集合。**空集合会被读成"模型没有冲突"——完全相反的结论。**
变异（去掉证明检查）→ 1 红；变异（少 guard 一条）→ 1 红。


### **后端会报告你没问的东西**

实现了 `remapBackendConflict` 与 `literalKey`。

#### 一、最后一步是过滤，不是返回

**后端会报告它自己认定的一切，包括调用方没问的东西。**

```csharp
return requested.Where(wanted.Contains).ToArray(); // 不是 return wanted
```

直接返回 `wanted`，**一份"关于目标 X 的冲突解释"里会冒出目标 Y 的成员**——而读的人没有任何线索
知道那不属于这次分析。

**两条判据成对**：一条证明"请求的留下了"，一条证明"没请求的没混进来"。
只查前者，一个"原样返回后端集合"的实现照样通过。变异（去掉过滤）→ 2 红。

#### 二、假设的键必须带 `negated`

少了它，一个假设和它的否定同键——**而"取反之后仍然不可行"和"可行"是相反的结论。**
变异（去掉 `negated`）→ 1 红。

#### 三、未注册的 id 直接跳过

它不在这个模型里，**映射不出原始证据**。塞进去会让报告引用一个不存在的成员。


### **指错位置比不给位置更糟**

实现了 `ConflictAnalyzer` 的 `sourceOf` 与 `groupSummaries`。**两个种子策略（原生 IIS、假设式）没搬。**

#### 一、三种成员，映射互不相同

| 成员 | 来源 | 写错会怎样 |
| --- | --- | --- |
| 约束 | `Constraint` | — |
| 变量边界 | 按 `Side` 分下界/上界 | **合并成一个，用户不知道该松哪边** |
| 变量值域 | `SparseDomain` | **让人去调边界，而问题在值域** |

**指错位置比不给位置更糟**：用户会去改一个改不动的地方。

变异（下界/上界合并）→ 1 红。

#### 二、按组汇总只算有组名的约束

"某组有 2 条冲突"说的是**两条有组名的约束**，不是"其中一条组名未知"。
把它们混进同一行，用户会去查一个不存在的组。所以**组名缺失就不计入任何一组**，不塞进 `null` 行。

#### 三、夹具写错一次，暴露了模型的一个语义

第一次跑出 `Count = 3` 而不是 2：模型的 `GroupName` 是 `group?.Name ?? _currentGroupName`——
**不带 group 的约束会继承"当前组"。** 我把无组那条放在后面注册，它就不再是无组的了。

**判据没错，夹具错了。** 调整注册顺序后通过。**这个语义本身值得记住：它让"无组"变成一个位置相关的属性。**


### **没测的那一半不能说"确认无效果"**

实现了 `refineThreshold`，并把求解依赖抽成一个 `probe` 回调——**于是二分逻辑可以在没有求解器的情况下测透。**

#### 一、三条规则

| 规则 | 顺手写会怎样 |
| --- | --- |
| 中点必须**严格**落在区间内 | **整数中点停在原地，循环空转，区间没变** |
| 探测 `Unknown` 就停 | **下界被推高，报告声称测过那一半** |
| 次数用完如实说 | 报一个"已经收敛"的假结论 |

**第二条是这批的重点。** 把一次没结论的探测当成 `NoObservedEffect`，下界会推到中点，
于是报告声称"在这个 delta 以下确认无效果"——**而那一半根本没测过。**

这和 //是同一个家族：**"没测"不能被写成任何一端的证据。**
这是第四次以不同形状遇到它，**所以这次在代码注释里直接写出了"那一半根本没测过"。**

#### 二、区间 [0, 1] 这个退化情形

没有整数严格落在 0 和 1 之间。不加守卫，八次迭代会全撞在下界上，**次数耗尽、区间没变，
而结果看起来像"收敛了"。** 判据直接断言 `calls == 0`。

#### 三、收敛与未收敛要分开报

只有区间宽度 ≤ 1 才给 `FirstEffective` 并带 `FirstEffectiveDelta`；否则给 `ThresholdInterval`，
**`FirstEffectiveDelta` 留空**——一个没收敛的搜索说不出"首次有效是多少"。

变异（`Unknown` 当成无效）→ 1 红；变异（守卫放宽）→ 1 红。


### **新判据当场揪出一个旧缺陷**

实现了 `runRemoval`。补的前置件是 `ConstraintProgrammingModelSnapshot.TryWithoutConstraint`。

#### 一、这个前置件不能静默成功

**删除测试的意义全在"模型真的少了那条约束"上。** 给一份没少的快照，求出来的目标改善是 0，
**报告会写"删除无效"——而这条约束可能极其关键。**

所以 `TryWithoutConstraint` 找不到就返回 `false`。变异（改成静默用原快照）→ 2 红。

#### 二、预算耗尽与删除无效果，看起来一样

| 情形 | 结论 |
| --- | --- |
| 预算耗尽 | `Unknown`，`RemovalEffective = null` |
| 跑了、没效果 | `NoObservedEffect`，`RemovalEffective = false` |

**这两行在报告表格里长得几乎一样，而含义完全相反。** 变异（`Unknown` 返 `false`）→ 1 红。

#### 三、判据当场揪出 留下的缺陷

第一次跑 `TheExhaustedBudget_ShouldBeUnknown` 就红了：`RemovalEffective` 里有

```csharp
Kind == Removal ? Outcome == Effective : null
```

**`Outcome = Unknown` 时这个表达式给 `false`**——"没测"被写成了"删除无效"。
这正是 我自己写过的那条教训（`null > 0` 读起来像"没有改善"），**换了一个字段又犯了一次。**

改成显式三态：`Effective → true`、`NoObservedEffect → false`、`其余 → null`。

**这条值得记下来的不是修复本身，而是：新判据第一次跑就抓到了旧代码里的同类错误。**

#### 三点五、两条判据打起来，谁对

有一条判据明确写着 `Removal(Unknown).RemovalEffective == false`，理由是"未知不能声称有效"。
的新判据要求 `null`，理由是"没测就没有结论"。**两条都跑，必然红一条。**

| 主张 | 理由 | 站不站得住 |
| --- | --- | --- |
| `false` | 未知不能声称有效 | **理由成立，结论错了** |
| `null` | 没测就没有结论 | 成立 |

**`false` 不是"没有声称有效"，而是"声称了无效"。** 字段是 `bool?`，三态存在的意义就是让"没测"能说出来；
把它压成两态，只看这个字段的调用方就分不清"测了无效"和"没测"。

**所以改的是 那条判据**，理由写在判据旁边。**这是第几次同类更正已经数不清了——
但它说明一件事：这些判据不是在装饰文档，它们会真的互相打起来。**


#### 四、还没搬的

`refineThreshold`（阈值二分）与候选漏斗。删除测试这条链现在是完整的：派生 → 求解 → 分类。


### **扰动 0.5 和扰动 0 不能是同一份模型**

实现了 `deriveRhsSnapshot`，并补上它需要的一个前置件：`ConstraintProgrammingModelSnapshot.TryWithConstraint`。

**那个前置件不是顺手加的**：C# 的快照是 sealed class + 私有构造器，**没有 Kotlin `copy` 那样的派生手段**——
扰动与冲突分析都需要"改一条约束、别的一模一样"的快照，而这在 C# 侧此前**根本做不到**。

#### 一、三种"不能扰动"都显式返回原因

| 情形 | 处理 | 不这么做的后果 |
| --- | --- | --- |
| 约束不存在 | 返回原因 | 给一份没改的快照，**报告照常写改善量** |
| 不是整数比较 | 返回原因 | 拿一个算不出 RHS 的东西当扰动对象 |
| **delta 不是整数** | 返回原因 | **"扰动 0.5"和"扰动 0"给出同一份模型** |

第三条最容易含糊：连续后端用 `0.5` 扰动 RHS 是合理的，但 CP 的 RHS 是 `Int64`。
**悄悄取整会让报告里的 0.5 变成一句谎话。**

#### 二、溢出必须自己查

C# 的 `long` 溢出**默认静默回绕**：扰动一下 RHS 变成负数，**而没有任何一处报错。**
所以加之前先查范围。变异（去掉范围检查）→ 1 红。

#### 三、我删掉了自己刚写的一条判据

原本写了 `TheNonIntegerComparison_ShouldBeRefused`，**但它测的其实是"约束不存在"**——
C# 侧构造不出一条"存在但不是 `IntegerComparison`"的约束，所以那条判据只是把上一个判据重写了一遍。

**名字与内容不符的判据比没有更糟**：它让"这条检查被覆盖了"看起来成立。删掉。

变异（去掉整数性检查）→ 1 红。


### **没跑完和跑不了是两件事**

`ConstraintPerturbationAnalyzer` 的四个纯函数：`RequestedDeltas`、`ObjectiveImprovement`、`ClassifyOutcome`、`AggregateStatus`。
**求解循环与阈值二分没搬。**

#### 一、`AggregateStatus` 有四条规则，每条都能单独出错

| 输入 | 结果 | 不这么做的后果 |
| --- | --- | --- |
| 全 `Unreachable` | `Unreachable` | — |
| **一个 `Unknown`** | **`Unknown`** | **可能可达的报成不可达** |
| `Unsupported` + `Reachable` | `Unknown` | 一部分证据没适用却给出结论 |
| **空 + 预算中止** | **`Unknown`** | **没跑完报成跑不了** |

第二条最重要：**一个可能可达的东西被报成不可达，是最难被后续工作纠正的错误。**
第四条同样：**把预算中止报成 `Unsupported`，会让一次没跑完的分析看起来像"后端不支持"。**

#### 二、`Unknown` 与 `NoObservedEffect` 不能合

`ClassifyOutcome` 里，求解状态不是 `Reachable` 时**即使带着改善量也算 `Unknown`**。
合起来报会让一份缺证据的报告看起来结论明确——**这和 那条是同一个道理，只是换了一层。**

阈值比较是**严格大于**：`improvement == tolerance` 仍在噪声范围内。

#### 三、方向搞反，变差的解看起来是改善

同一个差值 10 → 12，最大化下是 +2、最小化下是 −2。**两条都要查**，只查一条会让"永远 `current - baseline`"通过。

#### 四、重复的 delta 不是两条证据

去重会让"有效 delta 个数"少算——**而重复会让它多算，同样错。** 两者都得钉。

变异（忽略 `Unknown` 观测）→ 1 红；变异（最小化方向翻转）→ 1 红。


### **"没证出来"不是"不可达"**

`TargetFeasibilityAnalyzer.AnalyzeAsync`：派生重建 → 求解 → 按输出分类。
**这是第一次真的调用求解器接口**（虽然判据用的是固定输出的假求解器）。

#### 一、三条分支，三个反直觉处

| 后端说 | 无保留采信会怎样 | 这里怎么做 |
| --- | --- | --- |
| 可行 | 见证可能违反模型 | **复核**，不过就报 `Unknown` |
| 不可行 | **把"没证出来"报成"不可达"** | 只有 `Verified` 才给 `Unreachable` |
| 无结论 | 丢掉一个可用的见证 | 有合格见证仍报 `Reachable` |

第二条最重要：**把"后端没证出来"报成"不可达"，会让一个其实可达的目标被当成死路。**
变异（`verified = true`）→ 1 红。

第一条同样：变异（跳过复核）→ 1 红。

#### 二、`Other` 错误码是"不支持"，不是失败

降阶不支持时后端返回 `Other`。**把它当失败向上抛，调用方会以为求解坏了，而不是这条路走不通。**
判据查 `IsOk` 为真、状态为 `Unsupported`、且 `TargetFixed` 为假（什么都没定下来）。

#### 三、后端抛异常也不向上抛

**后端崩溃是一个要报告的结果，不是一个要传播的异常。** 报 `Unknown` 并带上原因。

#### 四、派生模型一定释放

`finally { derivedModel.Model.Dispose(); }`。**它持有原生句柄，漏一次不是测试变红，而是内存慢慢涨。**

#### 五、这批没搬的

候选漏斗（`buildCandidateFunnel` 那一层）、分析缓存、能力矩阵过滤。
**它们决定"分析哪些目标"，不决定"分类对不对"**——后者是这批的范围。


### **约束检查在目标检查之前**

实现了 `validateTargetWitness`：拿求解器返回的解逐条复核，五项检查按序执行。
**求解器说"可行"不等于可行。**

#### 一、五项检查，每条都要一个反例

| 检查 | 反例 | 不查会怎样 |
| --- | --- | --- |
| 未知变量 | 解里多一个 `ghost` | 报告引用一个不存在的变量 |
| 值域 | `y = 11`（域 `[0, 10]`） | **求解器违约也照收** |
| 原始约束 | `y = 9` 踩 `y &lt;= 8` | **把不可行报成可行** |
| 目标可求值 | 目标缺失 | 边界根本没算过 |
| **目标边界** | `y = 6` 差一个 | "可行"但没够到目标 |

第三条最容易漏：解**满足目标、也在值域内，却踩了模型自己的约束**。
只看目标就报"可行"，会把一个不可行的模型报成可行。

变异：跳过约束检查 → 2 红。

#### 二、顺序也是契约

约束检查在目标检查之前。`y = 9` 违反 `y &lt;= 8`、同时满足 `&gt;= 9`——**报出来的必须是约束那条**，
否则一个踩了约束的解会被写成"只是没到目标"。**理由不同，下一步的处理也不同。**

#### 三、`VariableId` 是结构体

第一版写 `FirstOrDefault(...)` 后判 `is not null`：**结构体的"没找到"是默认实例，不是 `null`**，
于是这条检查永远成立，**所有解都被判成"包含未知变量"。** 先投影成可空再取。

这不是笔误，是**结构体与 `null` 的语义差**；同一形状在别处还会再出现。

#### 四、还没搬的

`analyze` 本体的**求解与分类**（`mapOutput` 的 Feasible/Infeasible 分支、能力矩阵过滤、缓存）。
**这批只保证"报告写可行之前，那个可行被复核过"。**


### **取整方向反了，门槛就低一格**

补了 `build(snapshot, target)`：重建模型再追加一条目标约束，返回 `DerivedModel(model, targetConstraintId)`。

#### 一、取整方向不是细节

| 方向 | 取整 | 6.5 变成 |
| --- | --- | --- |
| `AtLeast` | 向上 | **7** |
| `AtMost` | 向下 | **6** |

取错方向等于**把门槛放低一格**，而报告照样会给出一个像样的结论。

**判据必须两条都查**：只查 `>=` 那条，一个"永远向上取整"的实现照样通过。
验证方式是比较两份派生模型的约束文本**必须不同**，且分别含 7 / 6。

变异（方向翻转）→ 1 红。

#### 二、派生模型只注册目标那一个 objective

如此。多注册其余 objective 不会让求解出错，**但会让"这份派生模型是为哪个目标造的"变得含糊。**
判据只查 `Objectives` 恰好一条。

#### 三、目标约束的 id 要能认出来

`analysis-target:{stableId}`。**没有这个前缀，报告里就分不清哪条约束是分析加的、哪条是模型自带的。**

变异（返回前把模型 `Dispose`）→ 2 红——这同时证明判据走的是**真的模型**，不是一句构造成功的空话。


### **漏一个变量会让模型照样能解**

按 的更正值了重建器：`SnapshotModelRebuilder`，走**表达式 AST 找回变量**这条路。
这是 `ConstraintProgrammingSnapshotBuilder.buildModel` 的**普通路径**（全部成员激活）。

#### 一、没搬的三处，遇到就报错

| 未搬 | 遇到时 |
| --- | --- |
| 区间变量 | 报错 |
| 选择性停用证据时的值域重绑（`activeSources`） | 未实现，调用方拿不到这条重载 |
| 目标过滤（`objectiveFilter`） | 同上 |

**为什么不静默跳过**：少一条约束的模型**照样能解、照样给个数**，而那个数没有任何一处会显得可疑。

#### 二、和一处**故意不同**

在"变量在快照里找不到"时 `continue`，理由是"该变量完全没被引用"。
**但重建器里"找不到"的含义是"我们没能从 AST 里收出来"——这是两件事。**

能安全跳过，是因为它同时遍历了变量、表达式、区间**和约束**；
这批只遍历表达式与目标，**所以"找不到"更可能是我们漏了，而不是它没用。**

改成报错，并在错误里带上变量 id。变异：把报错改回 `continue` → 1 红。

#### 三、线性项这里真的收不了

`ConstraintProgrammingExpression.Term` 只有 `IVariableItem`，**没有 `VariableId`**，而索引的键是 `VariableId`。
**用变量名当键就是猜**——名字不是身份。所以线性项这条分支显式留空，漏掉的变量变成报错。

#### 四、区间守卫这一条**没有判据**

`IntervalVariable` 与 `ConstraintProgrammingModelSnapshot` 的构造器都是**私有**的，测试造不出带区间的快照。
**守卫留在代码里，但没法证明它会响。** 如实记下。

变异（值域被放宽）→ 1 红；变异（找不到就 `continue`）→ 1 红。


### **我把因果搞反了两批**

和 都建立在一个我**没有核对**的因果链上：

> 快照只存了类型名 → 没有反向解析 → 重建时只能猜类型 → 所以要补类型目录和工厂

**这条链的前两步是真的，第三步是假的。**

#### 一、怎么找回变量

第一步不是读 `TypeName`，而是**遍历表达式 AST**，把里面的 `VariableItem` 引用**原样收出来**——
在 C# 侧这一步同样可行：`ConstraintProgrammingExpression.Variable.VariableItem` 持有的是**活对象**。

#### 二、为什么"造一个"不等于"找回"

| | 找回 | 造一个 |
| --- | --- | --- |
| 名字 | `x` | `x` |
| **`Identifier` / `Key`** | **原值** | **新值** |
| 注册结果 | 同一个变量 | **另一个变量** |

名字相同正是这个错误难发现的原因。**注册进模型的是身份，不是名字。**

#### 三、那两批做的东西是不是白做了

不是，但**定位错了**：

| 件 | 原来的说法 | 实际是 |
| --- | --- | --- |
| `VariableTypeCatalog` | 重建器的前置件 | 报告/校验/跨语言对照用的工具 |
| `TryCreate` | 重建器缺的最后一环 | 一个能造变量的工厂，**重建时不该用** |

两批的判据本身仍然有效（类型往返、不兜底、不近似），**错的是它们和重建器的关系。**

#### 四、这批的判据

`SnapshotVariableIdentityTests` 钉住真实事实：**快照里的 `VariableItem` 与原来那个对象是同一个**（`BeSameAs`）。

**这条没做变异验证**：快照在 `BindStableVariableIds` 里重绑的是变量 id，变量项本身仍指向原对象；
要让它变红得改重绑逻辑，代价超过的收益。**如实记下，不假装测过。**


### **工厂要的是实例，不是类型**

> **更正：本附录把工厂说成"重建器缺的最后一环"是错的。**
> 工厂造出来的是**新对象**，`Identifier` / `Key` 都不同——注册进去就是另一个变量。
> **重建器要的是"找回"，不是"造一个"。** 见附录十一。
>
> **correction: calling the factory "the last link the rebuilder was missing" was wrong.** The factory produces a **new object** with different `Identifier` / `Key`, so
> registering it introduces a different variable. **A rebuilder needs to *find* the variable, not *create* one.** See appendix eleven.


补了类型目录 补上它缺的最后一环：`VariableTypeCatalog.TryCreate`——**按稳定名称造一个标量变量**。

八个名称与八个标量类恰好一一对应，不是巧合：

| 名称 | 变量类 |
| --- | --- |
| `Binary` | `BinVar` |
| `Ternary` | `TerVar` |
| `BalancedTernary` | `BTerVar` |
| `Percentage` | `PctVar` |
| `Integer` | `IntVar` |
| `UInteger` | `UIntVar` |
| `Continuous` | `RealVar` |
| `UContinuous` | `URealVar` |

#### 一、判据查的是"造出来的类型对不对"，不是"造出来了"

只断言 `TryCreate` 返回 `true` 什么也没证明：**一个把 `Integer` 造成连续型的工厂同样返回 `true`。**
所以每条都查 `TypeKind.IsIntegerType` 与往返名称。

变异（`Integer` 造成连续型）→ 2 红。

#### 二、不兜底，这一条和目录那条是同一个理由

兜底造一个连续型：**整数模型按 LP 求解，解出来了、值不对、没有一行日志说它换过类型。**
变异（兜底改成静默成功）→ 1 红。

#### 三、还缺什么

**重建器本体仍缺**——`ConstraintProgrammingSnapshotBuilder` 的 440 行逻辑：重建变量、表达式、区间、约束、目标，
再为每个候选追加一条 target 约束。前置件现在齐了（类型目录 + 变量工厂），**剩下的确实是逻辑**。

`.analysis` 里与重建相关的还有 `ConstraintProgrammingAssumptionMapper`（已在 搬完）与
`FixedIntegerProjection`（同批搬完），**所以重建器不需要再补旁支。**


### **快照把变量类型存成了类名，而没人能读回来**

> **更正：本附录把因果搞反了。** 类型名确实没有反向解析，但**根本不走这条路**——
> `buildModel` 从表达式 AST 里**找回**原来的 `IVariableItem` 对象。所以"只能猜类型"这个结论是错的。
> 细节见附录十一。**本附录保留原文以便对照，结论以附录十一为准。**
>
> **correction: this appendix had the causality backwards.** The type name indeed has no parser, but **that path is never taken** — `buildModel` *recovers* the
> original `IVariableItem` objects from the expression AST. The conclusion "it can only guess the type" was wrong. See appendix eleven; this text is kept for comparison.


剩下 4 个 `core.analysis` 分析器（`TargetFeasibilityAnalyzer`、`ConstraintPerturbationAnalyzer`、`ConflictAnalyzer`、`Pipeline` 编排器）
**全部卡在同一个前置件上**：`ConstraintProgrammingSnapshotBuilder`（约 440 行，把不可变快照重建为可变模型）。

量了这个前置件在 C# 缺什么，**结论不是"逻辑没写"，而是"数据不够"**。

#### 一、`TypeName` 被编码、搬运、解码，从来没被用来造变量

快照里一个变量的类型是 `TypeKind.ToString()`——**一个类名**。C# 侧：

| 环节 | 现状 |
| --- | --- |
| 编码 | `entry.Variable.TypeKind.ToString() ?? "unknown"` |
| 快照字段 | `string TypeName` |
| 解码 | 原样搬运 |
| **反向解析** | **不存在** |

而 `RegisterVariable` 要的是**真实的变量实例**，不是一个名字。**所以重建时只能猜类型。**

#### 二、猜错的默认值不报错，只是答案不对

一律当连续型：**整数模型被当成 LP 求解**，解出来了，值不对，**而且没有任何一行日志说它换过类型。**

这是本仓反复记录的那类失效里最难发现的一种——**没有异常、没有警告、只有一个错的数。**

#### 三、这批补的是目录，不是重建器

`VariableTypeCatalog`：八个具体类型 ↔ 稳定名称，`TryResolve` **不提供兜底**。

| 判据 | 为什么 |
| --- | --- |
| 目录名 == `ToString()` | 快照编码用的就是 `ToString()`，**不一致就"存得进、读不出"** |
| 八个全部往返 | 类型集合是封闭的，**第九个说明有人加了类型没加名字** |
| 认不出就失败 | 兜底一个"最像的"正是那种不报错的失效 |
| 目录不含近似项 | `Float64` 之类的近似项**让错的映射看起来是支持的** |

**重建器本身仍然缺**——它还差"类型名 → 变量实例"的工厂（`RegisterVariable` 要实例不要类型），
而那是下一步，不是。

变异：加一条近似项 → 3 红；把兜底改成静默成功 → 2 红。


### **"测了没效果"是证据，"没有结论"不是**

补完 `CriticalConstraintAnalysisReport.kt` 的报告侧：`buildEffectivenessRanking`、`AnalysisGroupSummary`、`summarizeGroups`。
**只缺报告构造器 `CriticalConstraintAnalysisReportBuilder`。**

#### 一、只有 `Unknown` 该被丢掉

`NoObservedEffect` 是一条证据（测过了、没有效果），`Unknown` 不是（求解没有形成结论）。
**把两者一起丢，报告就再也分不清"没测"和"测了没用"。**

而且 `Unknown` 那条**即使报了个数字，那个数字也必须被丢掉**——一个没有结论的求解不是一次测量。
变异：不再排除 `Unknown` → 1 红。

#### 二、反查不到组就归 `null`，不要猜

冲突解释**刻意只保存稳定身份**，它自己不带组名。阻塞约束的组必须从活动性报告反查，
**查不到就归 `null` 组**——猜一个组名会让"某个组阻塞了模型"变成一条没有依据的结论。

变异：让反查失败的 id 也算进 `null` 组之外的组 → 1 红。

#### 三、`Dictionary<string?, …>` 不收 null 键

`null` 是合法的组名（未分组），而字典**不接受 null 键**——第一次跑就抛 `ArgumentNullException`。
改成"列表 + 线性查找"：组数是个位数，代价可以忽略，**而它不会在未分组时抛异常**。

#### 四、又踩了一次 `Equal` 的坑

`Should().Equal("big", "a", "b", "理由")` 会把**结尾那个字符串当成第四个元素**。
这是本仓第四次踩同一个坑，处理方式固定：**先 `Equal(a, b, c)`，再 `HaveCount(n, reason)`。**


### **"没测出来"不是"没改善"**

补了 `CriticalConstraintAnalysisReport.kt` 的有效性侧：`EffectivenessEntry` 与 `EffectivenessRanking`。
当时只补了漏斗那一块，**所以那个文件此前是"部分搬"却记在"整文件搬完"里**——账目在 才对齐。

`buildEffectivenessRanking`、`summarizeGroups`、`AnalysisGroupSummary` 与报告构造器**仍缺**。

#### 一、`null > 0` 是假，而它读起来像"没有改善"

`IsGloballyEffective` 必须同时要求"有值"和"大于零"。只写 `MaxImprovement > 0` 时，
`null` 会得到假——**和"测了、没改善"同一个结果**。

**而实际含义完全不同：一个是没测，一个是测了没有。** 一份缺失证据的报告会因此看起来结论明确。

#### 二、四条校验，四种"数值撑不住"

| 检查 | 不查会怎样 |
| --- | --- |
| schema 非空、目标有限 | 一份没有出处的报告 |
| 身份非空、不重复 | 同一条约束算两次，**改善看起来翻倍** |
| 数值有限、delta 为正 | `NaN` 混进排序，**整个降序失去意义** |
| **局部判决需要对偶值** | 没有对偶值却给"局部有效"，**凭空断言** |

第四条是这批最有价值的一条：**一个没有对偶值的"局部有效"，读起来和一条测出来的结论一模一样。**
变异：去掉这条检查 → 1 红。

#### 三、判据自己的反方向对照

`TheViews_ShouldFilterIndependently` 不只查"全局有效只有一个"，还查"局部有效有两个"
——**因为全局有效的那个也带局部判决，只查前者会让"三个视图其实是同一个"通过。**

变异：把 `null` 也算成有效 → 2 红（含这条视图判据）。


### **三个零值都不是"没配置"**

补了 `CriticalConstraintAnalysisPipeline.kt`（359 行）的选项层：`ConflictAnalysisOptions` 与
`CriticalConstraintAnalysisOptions`。**编排器本身没搬**——它要依次调扰动、目标可行性、冲突三个分析器，
**而这三个的分析器本体都还没搬。**写一个调不到东西的编排器，正是本仓反复拒绝的那种"看起来有了"。

#### 一、三个零值，三种合法含义

| 选项 | 零的含义 | 当非法拒掉会怎样 |
| --- | --- | --- |
| `MaxDeletionChecks = 0` | 保留完整已证明冲突、不缩减 | **调用方拿不到一份本来能给的完整冲突** |
| `CandidateLimit = 0` | 只做筛选，不做昂贵扰动 | **连一次便宜的分析都跑不了** |
| `TimeBudget = 0` | 不花时间 | 与"预算为负"混为一谈 |

**只有负数才是配置错误**——负数既不是"不做"，也不是"做多少"。

#### 二、我上一版把 `CandidateLimit` 的默认写成了 0

默认是 `CandidateFunnelConfig.DEFAULT_CANDIDATE_LIMIT`（32），**不是 0**。
写成 0 的后果是：**默认的流水线只做筛选，永远不做扰动分析，而且没有任何报错。**

判据 `TheRemainingDefaults_ShouldNotDrift` 钉住这个数，并写明"零默认会静默关掉每一个昂贵阶段"。

#### 三、流水线的默认扰动策略不能顺手统一

| 来源 | 默认 | 代价 |
| --- | --- | --- |
| 流水线 | `deltas = []`, `removalTest = true` | 只做删除测试 |
| 策略类型自己 | `deltas = [1.0]` | 跑一串 RHS 扰动 |

**两者差一个数量级，而流水线是自动跑的。** 取重的那个会让"跑一次分析"变成一件很贵的事。
判据同时钉住"流水线默认是空的"和"策略类型自己的默认确实不是空的"——**少了后半句，前半句什么也没区分。**

#### 四、子选项的问题必须冒泡

校验只看自己那一层、把坏掉的子选项放过去，等于**外层说"配置没问题"、里层每次调用都失败**。


**的实测**：4 条判据通过；变异（零被误拒 / 默认策略被换成重的）各 1 红。


### **Gurobi 三个分析器到齐，但判据被后端逼着重写了两次**

| 类 | 状态 |
| --- | --- |
| `GurobiFarkasAnalyzer` | 已补 |
| `GurobiNativeIISAnalyzer` | 已补 |
| `GurobiQuadraticNativeIISAnalyzer` | **已补** |

**这是一个插件文件的第一次"全部到位"。**

#### 一、二次版本不是线性版本的复制品

约束是 `GRBQConstr`，成员判定读 `IISQConstr` 而不是 `IISConstr`。**抄线性那份会在每一行都不算成员的地方
悄悄返回空 IIS**——而那条错误看起来像"这个模型没有不可行子系统"。

变异（`IISQConstr` → `IISConstr`）→ **2 条红**。

#### 二、判据的形状被后端拒了两次

我先后写了两版测试模型，**都被 Gurobi 拒了，理由都是 "Q matrix is not positive semi-definite"**：

| 形状 | 为什么不行 |
| --- | --- |
| `x*y >= 10` | 交叉项对非负变量不是半正定的 |
| `x²+y² >= 10` | 二次型本身是凸的，**但 `>=` 的可行域是球外，整体非凸** |

**"凸二次"不等于"凸约束"——方向决定可行域凸不凸。** 最终用"凸二次上界行 + 线性下界行"的矛盾对。

**也不设 `NonConvex`**：它只处理凸的那一侧。所以判据也只用那一侧，**不为了测试方便去改后端参数。**
改参数能让测试过，但那就不是在测能力了。

#### 三、二次侧只声明原生 IIS

二次约束的 Farkas 证书不是这个插件给得出的。**声明一个给不出的能力，比不声明更糟。**
判据钉住：只有一个分析器，且 `ModelTypes` 恰好是 `Qp` / `Qcp`。


**的实测**：4 条判据在真实 Gurobi 上通过；变异（`IISQConstr` → `IISConstr`）2 红。


### **线性原生 IIS 搬完，`NativeIis` 从 `false` 改 `true`**

| 类 | 状态 |
| --- | --- |
| `GurobiFarkasAnalyzer` | 已补 |
| `GurobiNativeIISAnalyzer` | **已补**，真实后端跑 `computeIIS` |
| `GurobiQuadraticNativeIISAnalyzer` | **仍缺**（二次约束不是同一条路径） |

#### 一、`NativeIis` 改成了 `true`，但理由要说准

我一开始写的理由是"这个标志是过滤器，`false` 会把分析器丢掉"。**这是错的，已经改掉。**

`LinearIisCompute` 传给编排器的是 `solver.Descriptor?.Capabilities`，而 **Gurobi 没有覆写 `Descriptor`**
（默认 `null`），**所以这个标志当前不参与过滤**。改它不是为了让分析器能跑——分析器本来就能跑；
**改它是因为声明和实现对不上了。**

**这条记下来，是因为它差点成了一个"听起来很合理的假因果"。** 一个解释只要足够顺，就不会有人去查。

#### 二、判据查的是"能力真能用"，不是那个布尔值

`TheDeclaredCapability_ShouldBeReachableEndToEnd` 在一份真不可行的模型上走完整条 `ComputeIISAsync` 路径，
**看回来的证据是不是 `NativeIis`**。标志对了但分析器是空壳，这句话就假了。

变异（把 IIS 分析器从 `DiagnosticAnalyzers` 摘掉）→ **4 条红**，其中就包括这条。

#### 三、两个分析器的分工钉在同一个模型上

`TheIntegerModel_ShouldGetAnIisAndNoFarkas`：**同一个整数模型，原生 IIS 必须成功、Farkas 必须拒绝。**
只测一侧，会让"两个分析器其实是同一个"这种错误通过——因为它们都叫"不可行分析"。

#### 四、空 IIS 必须报失败

报一条"已证明不可行但没有任何成员"的证据，下游会读成"模型自己不可行，删什么也没用"
——**而那正是没归约出来的样子。**

#### 五、精确度与最小性同源

`IISMinimal` 同时决定 `Exactness` 与 `Minimality`。标了 `Irreducible` 却给 `Heuristic` 精确度，
会让下游**既以为它最小、又以为它只是启发式**。变异：1 红。


**的实测**：5 条判据在真实 Gurobi 上通过；变异（摘掉分析器 / 改精确度）分别 4 红、1 红。


### **Gurobi 的 Farkas 那条实现出来了，真后端跑过**

有三个类 补了中间那个：

| 类 | 状态 |
| --- | --- |
| `GurobiFarkasAnalyzer` | **已补**，真实 Gurobi 后端跑过 |
| `GurobiNativeIISAnalyzer` | **仍缺**（需要隔离实例上的 `ComputeIIS`） |
| `GurobiQuadraticNativeIISAnalyzer` | **仍缺**（同上，二次模型） |

#### 一、`NativeIis` 仍然是 `false`，这是对的

`GurobiReportSupport` 里那句 `NativeIis: false` **没有改**。两个 IIS 类没搬，能力就是没有，
**提前翻成 `true` 会让上层以为有一条它其实没有的提取路径**。

判据里专门钉了这一点：分析器只声明 `Farkas` + `Lp`，**不声明 `Mip`**——
Farkas 证书证不了混合整数模型不可行。

#### 二、这条判据只在真后端上有意义

4 条判据全部真跑 Gurobi。**Farkas 证书存在与否，只有真后端答得出来**——
假后端给不了 `FarkasProof`，写出来的测试只能验证"我调用了方法"。

| 判据 | 方向 |
| --- | --- |
| 只声明 Farkas + LP | 防"提前声明没实现的能力" |
| 两行矛盾的证书覆盖两行 | 正方向 |
| 整数模型在花后端开销前被拒 | 防"跑完才拒" |
| **可行模型不许被报成不可行** | **反方向：编造证书会让下游把能解的问题当死局** |

**最后一条是这批唯一反着指的判据，也是唯一一条能挡住"证书是编的"的判据。**

#### 三、两处实现、但理由值得写下

- **`DualReductions = 0` 与 `InfUnbdInfo = 1`**：没有这两项，Gurobi 可能返回 "infeasible or unbounded"，
 而那个结论**支撑不了证书**；
- **`FarkasProof` 为零就是失败**：逐行对偶值仍然读得出来，但那些数字不构成证书，
 **据此报"已证明不可行"是编造。**

#### 四、隔离实例的理由

诊断**另起实例**，不继承主求解器。主运行的预处理和分支定界会改变行序与对偶值，
而 Farkas 乘子是逐行的——**行序一旦变了，证书指向的就是别的约束。**


**的实测**：4 条判据在真实 Gurobi 上通过；变异（删整数守卫 / 删状态守卫）各 1 红。


### **按实测重记账，三种口径分开**

**此前那一行写的是"由 3 个增至 18 个，仍缺 3（21）"。这个口径混了两件事**：
"C# 里有没有这个文件"和"那个文件有没有被完整覆盖"。两者不等，因为**有三个文件里的类型
被单独拆成了 C# 文件**——文件数涨了，覆盖的文件数没变。

逐文件比对（`git ls-tree` 对 `ls`，不是凭记忆）：

| 类别 | 数量 | 文件 |
| --- | --- | --- |
| **整文件搬完** | **15** | 其余全部 |
| **只搬报告侧**（分析器本体要真实 CP 后端） | **3** | `TargetFeasibilityAnalyzer`、`ConstraintPerturbationAnalyzer`、`ConflictAnalyzer` |
| **完全未动** | **0** | — |
| **只搬选项层** | **1** | `CriticalConstraintAnalysisPipeline`（选项已补，编排器缺） |
| **合计** | **19** | — |

C# 的 18 个 `.cs` 里，`TargetFeasibilityReport`、`ConstraintPerturbationReport`、`ConflictExplanation`
**在是那三个分析器文件的一部分**，拆出来是为了让不需要后端的部分先落地。
**所以"C# 18 个文件"不等于"覆盖18 个文件"。**

#### 插件分析器：一个搬完了，一个没动

| 文件 | 行数 | 状态 |
| --- | --- | --- |
| `ScipInfeasibilityAnalyzers` | 278 | **已搬完**（1 个类，有 `ScipFarkasDiagnosticTest`） |
| `GurobiInfeasibilityAnalyzers` | 518 | **已搬完**：三个类全部到位，都在真实后端跑过 |

**SCIP 侧此前被记成"部分"，这是错的。** 那个文件只有**一个类** `ScipFarkasInfeasibilityAnalyzer`，
C# 有同名同类实现，且**有两处差异是 C# 自己写明了理由的**：

- `iisConfig.ThreadCount` **不下发**，固定单线程——LP 模式必须保证行序与原始约束对齐；
- `iisConfig.BackendConfiguration` **不下发**，用求解器自身的配置。

**差异是"记录过并给了理由"，不是"漏了"。把有理由的差异记成缺口，和把缺口记成完成一样，都会让工作量判断失真。**

#### Gurobi 那 3 个类是另一回事

`GurobiNativeIISAnalyzer`、`GurobiFarkasAnalyzer`、`GurobiQuadraticNativeIISAnalyzer` **一个都没有 C# 对应**。
而且这不是遗漏：`GurobiReportSupport` 里已经写明 `NativeIis: false`，理由是旧入口 `InvokeAsync(…, IisConfig?)`
**根本没读 `iisConfig`**，只是照常求解。**能力没实现，并且已经公开声明没实现。**

#### 所以真实剩余

**4 个 core 分析器本体 + 3 个 Gurobi 插件分析器**，全部要真实后端。
此前写成"仍缺 3"低估了；写成"缺 6 个文件"又高估了 SCIP 那一块。**两个方向都错过一次了。**


**重记的理由**：一种新的文档缺陷形状——**口径混用**。前六种是"点了名但不存在""存在但没点名"
"照抄旧判断""空备注""代理当证据"；这一种是**两个都成立的事实被算成了一个数**。
文件数涨了、覆盖数没涨，两个数都真，混在一起就都不真了。


### 第 6 条的附录：剩下 8 个，逐条写明缺什么

前面几批已经逐块补了 13 个。**剩下的 8 个不再"待办"，而是逐条记账**——
因为"做不到"和"没做"是两件事，混在一起记，后续就会再翻一遍。

| 文件 | 行数 | 求解引用 | C# 状态 | 缺的前置件 |
| --- | --- | --- | --- | --- |
| `ConflictAnalyzer` | 918 | 7 | **报告已补，分析器缺** | 第 630 行 `solver.solve`、第 755 行 `session.solve` |
| `ConstraintPerturbationAnalyzer` | 959 | 5 | **报告已补，分析器缺** | `activeBackend!!.solve(request)`（第 498、698 行） |
| `TargetFeasibilityAnalyzer` | 915 | 5 | **报告已补，分析器缺** | 第 673 行有真正的 `solver.solve` |
| `CriticalConstraintAnalysisSession` | 341 | 6 | **已补** | ~~要活的求解器实例~~ **更正：不需要后端** |
| `CriticalConstraintAnalysisPipeline` | 359 | 2 | 缺 | 上面四者的组合 |
| `ConstraintProgrammingAnalysisBackends` | 270 | 5 | 缺 | 后端适配胶水，同样要活的求解器 |
| `GurobiInfeasibilityAnalyzers` | 518 | — | 缺 | Gurobi 插件里**没有**任何 `InfeasibilityAnalyzer` |
| `ScipInfeasibilityAnalyzers` | 278 | — | **部分** | 已有 `ScipFarkasInfeasibilityAnalyzer.cs`，其余缺 |

**core 部分 3762 行 + 插件部分 796 行 = 4558 行。**

**更正**：这张表原本把 `CriticalConstraintAnalysisSession` 记成"要活的求解器实例"。
**那是 grep 代理读错了**——唯一带 `ConstraintProgrammingSolver` 的参数**只用来读 `Descriptor`**。
它已补，**不需要后端**；剩余 core 部分因此是 **3421 行**，加上插件 **796 行 = 4217 行**。

**补了 `TargetFeasibilityReport`，没补分析器**：第 673 行是一处真正的 `solver.solve(model, options)`，
之前的 `ConstraintProgrammingSnapshotBuilder` 还要重建可变模型。**那部分要真实 CP 后端，没搬也没空跑。**
`Core/Analysis/` **14 → 15**，缺口 **7 → 6**。

**报告钉的是四个状态不许互相冒充**：`Reachable`/`Unreachable` 是结论，`Unknown`/`Unsupported` 是"没结论"的两种原因。
**`Reachable` 不等于已证明**——求解器在时间上限内找到一个解，只说明目标**可能**可达。

**补了 `ConstraintPerturbationAnalyzer.kt` 前 348 行的纯数据类型**：三个枚举、两个策略、请求、结果、观测、报告。
`Core/Analysis/` **15 → 16**，缺口 **6 → 5**。

**第 349 行起是分析器本体**，含 `activeBackend!!.solve(request)` 与 `backend!!.solve(request)`，
**要真实重新优化后端，没搬也没空跑。**

**判据钉三件事**：

| 判据 | 边界 |
| --- | --- |
| 空操作的策略 | **什么也不会发生，报告却是"分析完成、无发现"**——最坏的一种空跑 |
| 增长因子 = 1 | delta 不变，预算内反复试同一点，**报告只说"没有观察到效果"** |
| `Unknown` 算改善 | 一次没有结论的求解被算成改善，**整份报告偏向"扰动有效"** |

**还有一条容易写错的别名**：`RemovalEffective` 只对删除操作有答案。给 RHS 扰动返回 `false`，
**会被读成"扰动无效"，而实际含义是"这个字段不适用"。**

**补了 `ConflictAnalyzer.kt` 第 31—261 行的报告侧**：`ConflictMinimality`、`ConflictValidity`、
`ConflictVerification`、`ConflictGroupSummary` 与 `ConflictExplanation`。`Core/Analysis/` **16 → 17**，缺口 **5 → 4**。

**第 262 行起是两个分析器**，第 630 行 `solver.solve(...)`、第 755 行 `session.solve(assumptions = ...)`。
**要真实 CP 后端，没搬也没空跑。**

**`ConflictExplanation.Validate()` 挡的每一条，都是"报告看起来有结论，但证据撑不住"的形状**：

| 检查 | 不查会怎样 |
| --- | --- |
| 目标两边一致 | 冲突挂在 A 上、可行性报告挂在 B 上 |
| 证据不含目标 | **目标自己进了"被阻塞成员"，删掉它"冲突就没了"** |
| 证据不重复 | 同一来源算两次，**让最小性看起来更强** |
| 成员是证据的子集 | 成员里出现一条从未激活的证据 |
| `Reachable` 无成员 | 一个可达目标带着一堆"阻塞成员" |
| 背景阻塞不得称最小 | **删任何原始约束都解不开背景阻塞** |
| 不可约要逐成员证明 | **声称最小，却只验了一条** |

**ported lines 31–261 of `ConflictAnalyzer.kt`** — the minimality and validity enums, the verification, the group summary and `ConflictExplanation`. `Core/Analysis/` goes
from **16 to 17** and the gap from 5 to **4**. **From line 262 on are the two analyzers**, with `solver.solve(...)` at 630 and `session.solve(assumptions = ...)` at 755;
**that needs a real CP backend, so it was neither ported nor stubbed.** **Every check in `ConflictExplanation.Validate()` blocks a shape where the report looks concluded but
the evidence does not carry it**: a conflict on one target with a feasibility report on another; **the target itself among the blocking members**, so deleting it "resolves" the
conflict; the same source counted twice, **making minimality look stronger**; a member that was never active; a reachable target carrying blockers; **background blocking claiming
constraint-level minimality** when removing any original constraint cannot lift it; and **irreducibility claimed after verifying only one member.**

**ported the first 348 lines of `ConstraintPerturbationAnalyzer.kt`** — three enums, two policies, the request, the result, the observation and the report.
`Core/Analysis/` goes from **15 to 16** and the gap from 6 to **5**. **From line 349 on is the analyzer itself**, with `activeBackend!!.solve(request)` and
`backend!!.solve(request)`; **that needs a real reoptimization backend, so it was neither ported nor stubbed.** The criteria pin three boundaries: **an operation-less policy does
nothing while the report reads "analysis complete, nothing found"**; **a growth factor of one retries the same delta and only ever reports "no effect"**; and **counting `Unknown` as
an improvement biases the whole report toward "perturbation works".** **One alias is easy to get wrong**: `RemovalEffective` only answers for a removal — returning `false` for an
RHS perturbation **reads as "the perturbation did nothing" when the real meaning is "this field does not apply".**

**`TargetFeasibilityReport` exists without the analyzer**: the analyzer needs a real `solver.solve(model, options)` plus the `ConstraintProgrammingSnapshotBuilder` before it
rebuilds a mutable model. **That needs a real CP backend, so it was neither ported nor stubbed.** `Core/Analysis/` goes from **14 to 15** and the gap from 7 to **6**. **The report
pins that the four statuses may not impersonate one another**, and **`Reachable` is not the same as proven** — a solution found within a time limit only shows the target
**may** be reachable.

**corrects this table**: it had recorded `CriticalConstraintAnalysisSession` as needing a live solver. **That was the grep proxy misleading the reading** — the one parameter
typed `ConstraintProgrammingSolver` **is used only to read its `Descriptor`**. It is ported and **needs no backend**, so the remaining core part is **3421 lines**, plus **796** of
plugin work, for **4217**.

#### 一、依赖链的根只有一个

`Session` → `TargetFeasibilityReport` / `ConflictExplanation` / `ConstraintPerturbationReport` → 三个分析器 → `Pipeline`。

**这一整棵树都挂在 `CriticalConstraintAnalysisSession` 上，而它持有一个活的求解器实例。**
所以顺序不是"先搬小的"，而是**先搬那个能创建会话的**，否则搬来的每个类型都没有构造入口。

#### 二、"求解引用"只是 grep 代理，不是证明

上表的"求解引用"是 `grep -cE '\.solve\(|solveDual|Lowerer|Solver\b'` 的命中数。

**它是代理，不是证据。** **下面这一处就是被它纠正的：** `ConstraintActivityAnalyzer` 命中 0，但真正的理由是
**它的 `buildReport` 只读快照与赋值**，不是"grep 没找到所以不碰后端"。
**这条口径的局限写在这里，免得下次把 0 当成"不需要后端"的证明。**

#### 三、为什么这批不搬

这 8 个里最小的是 270 行的 `ConstraintProgrammingAnalysisBackends`，但它是**后端适配胶水**：
它的"输入"是求解器的回调，"输出"是求解器的状态。**实现出来只会得到一个无法调用的形状。**

**而 `Session` 要先有求解器才能建。**本仓有 Gurobi / Cplex / Copt / SCIP / Hexaly 等真实插件，
**所以这不是"没有后端"，而是"要真实后端"**——它属于集成门禁，不属于无后端批次。

#### 四 的边界

**没有新增源码**。它把 8 个缺口逐条记账并钉住数字，
**目的是让后续从"要真实后端的 4558 行"开始，而不是从"还有 8 个不知道是啥"开始。**

### Appendix to item 6: what each of the remaining eight is missing 

earlier work ported thirteen, block by block. ** stops calling the remaining eight "todo" and books each one individually**, because **"cannot" and "not done" are
different things** and merging them makes the next step re-investigate. **The whole tree hangs on `CriticalConstraintAnalysisSession`, which holds a live solver instance** —
so the order is not "smallest first" but **"the one that can create a session first"**, or every ported type has no entry point. **The "solver refs" column is a grep proxy, not
evidence**: was corrected by it — `ConstraintActivityAnalyzer` scored 0, but the real reason is that **its `buildReport` only reads a snapshot and an assignment**, not that
"grep found nothing, so no backend". **That limitation is written down here so a 0 is not read as proof of backend-freedom next time.** ** adds no source code**: it
books the eight gaps and pins the numbers, **so the next step starts from "4558 lines that need a real backend" rather than "eight things nobody has looked at".**

### 货物属性模型：十二个类型

**复审发现`PackageAttribute.kt` 的十二个类型在 C# 侧**一个都没有**，而三份文档也从未提到 `PackageAttribute`。**所以这一节先记的是"从未被记录"，而不是"待办"。**

**已补齐：** `AbstractCargoAttribute`、`AbstractStackingOnPolicy` + 三个堆叠策略（`Filter`／`CartonContainer`／`Box`）、`AbstractHangingPolicy` + 两个悬挂策略（`Relative`／`Absolute`）、`AbstractDeformationAttribute` + `LinearDeformationAttribute`、`AbstractWeightAttribute` + `WeightAttribute`；`PackageAttribute` 增加 `OverPackageTypes`（堆叠策略的第一道判断）。

**判据 `CargoAttributeModelTests`（9 条）：** 十二个类型都在（反射）；**生产代码里没有构造它们的调用方**（"仅模型、不改行为"的可执行版本）；四道门限各撞一次（包装类型／超重／平顶／层数）；未知高度判否；相对悬挂的面积比；重量与线性变形。

**两处如实记录的局限（不是"已完成"）：**

**① 最大高度的语义不同。** 把 `maxHeight` 存成**长度**（`Quantity&lt;FltX&gt;`），而 C# 现有物品模型把 `MaxHeight` 存成**层数**（`UInt64`）。**把层数当长度会让高度门限静默失效**，所以做成"未知即 null、策略判否"，并在 `ICargoView.MaxHeightLength` 上写明。

**② 圆柱投影还没接上。** `ActualItem` 的底部投影以"宽 × 深"作矩形近似；圆形投影要经过 `ShapeFootprint2.Circle`，那层还没接到物品视图上。

**边界：** **这些类型在只被 `PackageAttribute.kt` 自身读取（`MaterialPacker.kt` 只在构造时填默认值），所以补进 C# 后算法行为不变。** **五个策略槽位**（`CargoAttribute`／`WeightAttribute`／`DeformationAttribute`／`HangingPolicy`／`StackingOnPolicy`）与递归层数计算也已补上；**有效层数按合成 `min(packageMaxLayer, weightAttribute.maxLayer)`，位置参数因此改名 `PackageMaxLayer`。**

### 箱型目标：两个 `*Minimization` 与一条过期的"实现不了"

**`Domain/Packing/MixedBinObjectives.cs` 的抬头原来写着"的两个 `*Minimization` 类依赖 `PreciseAssignment`（C# 侧不存在）"。****这句话**早就不成立**：`src/Framework.Bpp3d/Domain/LayerAssignment/Model/PreciseAssignment.cs` 一直在仓库里。****第 622 行那张"已撤回"的表就是同一个结论的第一次撤回——但注释没跟着改，于是它在下游又活了一轮。**

**已补齐（`Domain/LayerAssignment/service/limits/MixedBinObjectives.cs`）：**`BinVolumeMinimization`（系数 = 权重 × 箱型体积）、`BinTypeCostMinimization`（系数 = 权重 × 该箱型成本）、`PerTypeBinCost.Of`。

**三个跳过条件逐条照搬：** **成本表为空、权重为零、该箱型未配置成本——第三种**不进多项式**，而不是以 0 系数进去（0 项在求和里是噪声，在报告里会被读成"这个箱型免费"）。**"配置为零成本"同样跳过**，这是`mapIndexedNotNull` 的另一半。

**判据 `MixedBinObjectiveTests`（5 条）：** 过期断言已消失且更正被记录、`PreciseAssignment` 确实在仓库里、三个跳过条件、零成本也跳过、体积系数按权重缩放、缺省成本由调用方决定。

**边界：** **只产出"哪个箱拿到什么系数"，把系数注册进多项式仍是模型侧的事**（与 `BinAmountMinimization` 等既有目标同为标记式实现）。


### 第四个后端：MongoDB 收口

**这一条本来被写成"MongoDB 本机无实例，仍未验证"。** **用户追问一句"是 docker 中没有吗"，核实之后：** **Docker 里没有容器、本机没有镜像、`compose.yaml` 只声明了 mysql/postgresql/redis/rabbitmq。** **所以"没有实例"是对的，但成因是"没人起"——和 Kafka 一样，是观察被当成了结论。**

**`ospf-infra-mongo`（mongo:7，绑定 127.0.0.1:27017）上有三条判据用与另外三个后端同一套形状。**

#### 判据里有一处不能偷懒

**"入口能连上"不能用"`MongoClient` 构造成功"当证据：** **它的构造是惰性的，服务器不在也照样返回对象。** **所以判据真发一条 `ping`，并断言回复里的 `ok` 是 1。**

**"不可达报原因"那条把 `ServerSelectionTimeout` 压到 3 秒**，让失败有界——**否则驱动默认等 30 秒，一次排障跑成半分钟。**

#### 四个后端现在全部实测

| 后端 | 判据 |
| --- | --- |
| SQLite | 文件库 + 关连接再开，两条 |
| Redis | 入口连通 / 五种值形状 / TTL / 不可达，四条 |
| MySQL、PostgreSQL | 各三条 |
| Kafka | 三条；并修掉 `Dispose` 不 flush 导致的静默丢失 |
| MongoDB | `ping` / 插入-查-替换-删除 / 不可达，三条 |

**证据：** **`logs/p9-batch1323-backends.log`（14 passed = 12 条往返 + 2 条护栏）。**

### 转储六段齐备，并抓出两处"看起来有、其实没有"

**`PRJ`／`GEN`／`TRUNC` 补上之后 `--dump` 输出 119 行：** **`NET` 18、`PRJ` 16、`GEN` 36、`TRUNC` 36、`TOK` 8、`PRM` 5。**

#### 两段"待办"的理由都不成立

**· `PRJ`：** "快照类型名不同、字段需对齐"——**而 `SnapshotAsync`／`PricingRequestAsync`／`MasterAsync`、`LinearModelSnapshot`（Rows／Variables／Objective／ObjectiveConstant）、`ColumnPricingRequest.ReducedCost` 一个不缺。**

**· `GEN`／`TRUNC`：** "`canonicalKey()` 在服务层"——**而 `CuttingPlanCanonicalKey.From(plan)` 就在 `Domain/CuttingPlanGeneration/model/`，`CuttingPlan&lt;V&gt;` 直接实现 `ICuttingPlanForCanonicalKey`。** **拿不到的是服务层那层包装，不是键本身。**

#### 判据抓到两处"看起来有、其实没有"

**① 摘要没有信息：** 第一版用 `record.ToString()` 渲染规范键，而它的两个集合成员是 `IReadOnlyList`——**`ToString` 打印集合的类型名，于是前三个方案的摘要完全相同。** **"格式正确、内容为零"的摘要会安静通过格式类判据。**

**② 登记数字自相矛盾：** 第一版 `rows` 取 `Model.Constraints.Count`、`convexities` 取 `RefinedResources.Count`，理由是"C# 侧没有同名集合"——**而 `Rows` 与 `Convexities` 都在登记类上，我只看了半边。** **132 行对 0 个凸组合这个数字自己露了馅；现在是 `rows=128|convexities=4|columnVars=4`。**

#### 同进程两次运行不同，但跨进程逐字节相同

**实测：两个独立进程各跑一次，输出差异 0 行；同一进程跑两次则不同。** **两处原因都是进程级状态，不是"不稳定"：**

**· `GEN`／`TRUNC` 的 `stats=` 行带生成器缓存计数（`MaterialSliceTemplateCacheHits` 等），是进程级累计；**

**· 行 `Id` 形如 `model-local-row:<进程级计数器>:<序号>`，所以 `PRJ` 改打 `source=r0`——稳定身份，且比那个 id 更可跨语言比对。**

**判据因此钉"非统计行逐行相同"，并断言 `stats=` 行确实存在，免得例外被悄悄扩大成整份输出。**

**证据：** **`logs/p9-batch1324-dump.log`（119 行）。**

### 策略槽位与递归层数：几何收成一个谓词

**`PackageAttribute` 的两块剩余：** **五个槽位 + `maxLayer = min(packageMaxLayer, weightAttribute.maxLayer)`；伴生对象里的 `layerLayer`／`layerHeight`／`layer`。**

#### 一、有效层数的名字问题

**C# 原来的位置参数就叫 `MaxLayer`，于是"包材层数"与"有效层数"共用了一个名字，合成规则没有地方落脚。** **改名 `PackageMaxLayer`，`MaxLayer` 改为合成结果；没配重量属性时两者相等，既有 14 处构造点行为不变。**

#### 二、递归与几何分开

**用 `QuantityPlacement3&lt;Item, FltX&gt;` 加两个几何助手（`topItemPlacements`／`filterBottomOverlapped`），而 C# 侧 `QuantityPlacement3` 在 `Application` 层、那两个助手没有对应物。** **但递归本身只问三件事：类型、高度、谁压在谁上面。** **所以几何收成一个谓词 `Covers`，由调用方实现；递归、同类型过滤、取最大值三条语义逐行照搬。**

**一处说明白的偏离：** **用协程并行求各分支最大值，这里是串行——结果相同（都是最大值），并行度属于调用方。** **另有 `ponytail:` 标注的 64 层深度上限：真实的垛不会那么深，而互相压住的环会让递归停不下来。**

#### 三、判据（`PackageAttributeSlotsTests`，5 条）

**有效层数只能取小且不改写包材层数；五个槽位都可空、都能挂上；三层同类型堆叠给出层数 3 与 3 倍高度；类型不同不计层；空底部集合给零。** **判据不伪造几何，而是按 y 坐标判定"压在上面"的最小放置。**

**一处判据自己的错：** **第一版把待计算的 `box` 也放进了底部集合，于是算法把它当成一块同类型底板、返回 1 而判据期望 0。** **入参本来就是 bottoms——错在测试，不在实现。**

### 真实几何版的两个助手，以及一处语义分歧

**此前说"C# 侧没有 `topItemPlacements`／`filterBottomOverlapped` 的对应物"——错了两层：**

**① 底面重叠已经有等价物：** `PlacementFactory.OverlappedOnBottom`（同样比 X/Z 交叠），高度就是 `Position.Y + Unit.Height`。我搜的是两个包装名，而 C# 侧叫别的名字，搜不到就当成没有。**

**② 更糟的是 `PlacementTyping.cs` 里本来就有同名方法，而语义不同：** **旧的 `TopItemPlacements` 取"全局最高一层的所有放置"，取"没有任何底面重叠且更高的放置压在它上面的放置"。** **一高一低并列时两者答案不同——两件都算顶层，旧实现只返回高的那件。** **旧实现全仓零调用方（实测），所以按替换，而不是并存两个同名不同义的方法。**

#### 现在的关系拆成两条，而不是一条

**`OverlapsBottom`（底面投影重叠，与高度无关）：** 递归用它，等价于`filterBottomOverlapped`；**`IsAbove`（谁更高）：** 顶层集合用它，等价于 `topPlacements` 的第二条。****第一版把两者合成一个 `Covers`（"贴着才算压在上面"），于是三层堆叠只数出两层——**两个关系不同，合成一个就丢信息。**

**`Covers` 抽象按用户要求保留：** **递归仍然只认这两个谓词，真实几何通过 `PlacementTyping.AsStackPlacement` 接进去，判据里的最小替身照旧可用。**

#### 判据 `PlacementTypingTests`（4 条）

**一高一低并列时两件都算顶层（旧语义在此分道）；底面重叠只认投影不认高度；真实几何接进递归给出三层三倍高；侧放的箱子不共享投影。**

### 两个混沌吸引子回到标准式

**Kotlin 的 `966cafbc` 修了 `AizawaAttractor` 与 `WimolBanlueAttractor` 的公式，而 C# 侧搬的是**修复前**那一版——于是同一个错误在这里又活了一轮，五个偏离点全在：**

| 位置 | 修复前（C# 原样搬来的） | 标准式 |
| --- | --- | --- |
| Aizawa `dx` | 减的是完整的 `dy` | 减 `δ·y` |
| Aizawa `dz` 线性项 | `α·x` | `α·z` |
| Aizawa 输出顺序 | `(dx, dz, dy)` | `(dx, dy, dz)` |
| Wimol-Banlue `dy` | `-z·tan(x)` | `-z·tanh(x)` |
| Wimol-Banlue `dz` | `\|x\|` | `\|y\|` |

**判据不复述实现：** **它在测试里按标准式独立算一遍，再与实现的一步结果比较。** **复述实现的判据只能证明"代码没变"，证明不了"公式对"。**

#### 四条判据，其中两条钉的是"为什么"

**Aizawa 两步：** 一步等于标准式；另取 `x=0, y=0` 的对照点——**那里 `dx=dy=0` 而 `dz≠0`，输出顺序被置换时这一条必红。**

**Wimol-Banlue 两步：** 一步等于标准式；**从 `x` 靠近 `π/2`（`tan` 的极点）走两百步，三个分量都必须有限且小于 1000**——**钉的是 `tanh` 的有界性，而不是"改成了 tanh"。**

**变异验证：** **把 `Tanh` 改回 `Tan`，`WimolBanlue_ShouldMatchTheStandardForm` 立刻红**（第二分量 -0.4137 而非 -0.4056）。**一并记下一条操作坑：** 用 `mv` 还原文件后**时间戳比产物旧，MSBuild 判定"无需重建"**，必须先 `touch` 再跑。

**证据：** `tests/Math.Tests/Chaotic/AttractorFormulaSyncTests.cs`（4 条）。

### ONNX 插件：**未实现，等依赖决策**（来源 Kotlin `dee897d2`）

**这一条不是"没找到"，也不是"环境所限"，而是**一个需要产品决定的依赖问题**——所以它在这里，而不是在待办清单里悄悄挂着。**

| 项 | 实测 |
| --- | --- |
| 规模 | 48 文件、**+9851/−1550** |
| 内容 | `OnnxGraphParser`、`OnnxLowering`、`OnnxInference`、`OnnxAttribute`、`OnnxDataType`、`OnnxTensorConstant`／`Expr`／`Info`／`Operators`、`OnnxSubgraph`、`OnnxActivation`、`OnnxHalfPrecision`、`OnnxShapeOperators`、`OnnxError` 等 36 个文件 |
| 声明的依赖 | **两个**：`onnxruntime`、`onnx-protobuf_2.13` |
| C# 侧现状 | **零 ONNX 代码**，无 `Microsoft.ML.OnnxRuntime` 引用 |

**容易漏掉的那一半：** **除了推理运行时，还要能解析 ONNX 模型文件本身。** **Kotlin 用 `onnx-protobuf_2.13`；C# 侧对应物要么是某个 ONNX 模型包，要么自己用 protobuf 生成类。** **所以真要实现是"两个新依赖"，不是一个。**

**已选定选项 1（全量实现），并已开始落地。** **实测改变了两处原先的估计：**

- `Microsoft.ML.OnnxRuntime` 1.20.1 **能还原也能用**（`InferenceSession` 实测可构造）；
- **但该包不含 ONNX 模型协议类型**（`Onnx.*` 类型数 = 0），NuGet 上也没有 `Onnx`／`Onnx.Protobuf` 这类包。
- **所以"两个依赖"实际是三个：** `Microsoft.ML.OnnxRuntime`（推理）+ `Google.Protobuf` + 自己用 `onnx.proto` 生成的模型类。

**已落地（`src/Core.Plugin.Onnx`，16 批）：**

- **数据层：** `OnnxDataType`（14 类型）、`OnnxError`（13 变体）、`OnnxTensorConstant`、`ProtoReader`（手写线格式读取器）、`OnnxModelParser`（模型结构，字段号逐条判据）；
- **语义层：** `OnnxPiecewiseApproximation`（切分策略）、`OnnxActivationSemantics`（八算子语义与默认值）、`OnnxShape`／`OnnxShapeOperators`（形状算术与六个算子）、`OnnxGraphValidator`（拓扑序与可用性）、`OnnxConstantFolding`、`OnnxOutputSelection`、`OnnxImportOptions`；
- **`2e72a17c` 的中间变量实现：** 线性与二次表达式符号都有开关、辅助变量、令牌注册与 `RealizationConstraint()`（二次版左侧为二次多项式）。
- **降阶层：** `OnnxOperatorRegistry`（算子表与前置检查）、`OnnxActivationLowering`（精确算子化简）、`OnnxApproximationLowering`（近似算子切分与符号）、`OnnxInputBounds`（界判定）、`OnnxAffineChain`（仿射链求值）、`OnnxAffineModelBuilder`（仿射 → 线性模型）；
- **模型层：** `OnnxBoundsConstraints`（输入界 → 闭区间约束对）+ `OnnxModelAttachment`（挂进 `LinearMetaModel` 并走框架管线求解，已在 Gurobi 上验证界真的生效）。
- **`If` 落地接线：** `OnnxIfLowering`（选分支 + 压/弹作用域 + 走分支体，输出绑在外层；失败也弹作用域）。
- **分段激活：** `OnnxSymbolicActivation.Piecewise`（`Sigmoid`／`Tanh`／`Softplus`／`Elu`，对齐界来源、1e6 上限、退化为常数与均匀插值）；Gurobi 上最小值 `sigmoid(-5)`。
- **符号激活（`Relu`）：** `OnnxSymbolicActivation` 把正部包成系数为一的单项式，"先登记函数符号、后展平"的顺序由 `OnnxModelAttachment` 保证；Gurobi 上两条成对判据验证（负半轴 0、正半轴 2）。
- **激活的常量折叠：** 八个激活算子在常量输入上折叠；**符号输入明确拒绝**（需要函数符号，属模型层）。
- **逐节点求值：** `OnnxGraphEvaluator`（在上下文里走一张图，覆盖 Identity/Cast/Add/Sub/Neg/Mul-常数，中间结果留在上下文里；作用域由调用方负责）。
- **降阶上下文：** `OnnxLoweringContext`（名字 → 表达式/形状，作用域读外写内、遮蔽与泄漏边界）。
- **`Scan` 落地接线：** `OnnxScanLowering`（每轮一个切片、切片数与长度必须一致、作用域每轮干净）。
- **`Scan` 契约：** `OnnxScanExpansion`（长度取输入首维、切片对齐、状态四数一致、与 Loop 共用规模预算）。
- **`Loop` 落地接线：** `OnnxLoopLowering`（每轮干净作用域、迭代变量为当轮常数、种子回写；**就地更新已支持（`TryRebind`）**）。
- **`Loop` 契约：** `OnnxLoopExpansion`（次数常量、携带值四个数目必须一致、展开规模预算 10000 节点）。
- **`If` 展开：** `OnnxIfExpansion`（常量条件选分支、按位置对齐输出、分支与声明个数必须一致）。
- **子图判定：** `OnnxSubgraphPolicy`（`If`/`Loop`/`Scan` 的可展开性、254/256 上限、嵌套按乘积）；**展开本身未实现**。
- **二次目标：** `OnnxQuadraticProduct` 的乘积经 `QuadraticFlattenData` 挂进 `QuadraticMetaModel` 并求解；`(x−3)²` 在 `[0,10]` 上最小 0、`[5,10]` 上最小 4（成对判据）。
- **二次乘积：** `OnnxQuadraticProduct`（两个一次式相乘成 `QuadraticPolynomial`，按无序对合并同类项）；**挂进元模型未做**。
- **形状算子：** `OnnxShapeTransform`（`Reshape`／`Flatten`／`Squeeze`／`Unsqueeze` 顺序保持、可落地；**`Transpose` 因重排无处可写而在多元素张量上拒绝**；`Concat` 同样需要逐元素表示）。
- **端到端：** 带 `If` 的模型从字节 → 解析 → 前置检查 → 求值 → 多项式，条件真/假各走一条、与手算一致；**导入器对含控制流的图自动转入求值路径**；`If`／`Loop`／`Scan` 均可从字节走到多项式，求值路径按符号合并同名项（`x + x` 不会变成两列）。
- **函数符号登记顺序：** **`OnnxModelAttachment` 先登记函数符号、再展平**，**因为展平之后符号已经变成多项式的项，再登记就找不回对应关系。** 这条顺序对逐元素展开同样成立。
- **覆盖范围之外的算子：** `Conv`／`MaxPool`／`AveragePool` **也没有**（降阶分派与全模块搜索都无实现），**所以不是实现缺口；`PositionalConsumers` 里列出它们只为判断"下游是否按位置读"。**
- **二次域批量维：** `MatMul` 的批量前缀按 `BroadcastShape` 广播、**展开下标与一次域共用一份**、逐批量相乘。
- **二次域到求解器：** ONNX 二次域产出的多项式**挂进 `QuadraticMetaModel` 并用 Gurobi 求解**；**变量必须取自导入结果**（`RealVar("x")` 两次是不同实例，元模型表里只有一个）。
- **二次域 `Mul`：** 两侧含变量时按位置进入 `OnnxQuadraticProduct`；**"逐元素还是矩阵乘"由元素个数与内容决定，而不是形状。**
- **二次域 `Gemm`：** `OnnxQuadraticElementList.Gemm`（转置→二次相乘→`alpha`→`beta·C`；**`C` 可一次可二次，二者都含二次项时按三次拒绝**）。
- **二次域求值：** `OnnxImportMode.Quadratic` 让求值器按二次域算 `MatMul`，**结果走 `OutputQuadraticElements`；二次绑定喂给一次算子会被拒绝。**
- **二次域本体：** `OnnxQuadraticElementList`（**只在一处产生：两侧含变量的二维矩阵乘**；**同类项合并、批量维拒绝**）。
- **`inputBindings` 的强制性：** **默认入口在仿射链上拒绝多元素输入并点名 `inputBindings`；** **形状算子与控制流路径保留自己的拒绝（消息更具体）。**
- **批量维：** `MatMul` 的批量前缀按 `OnnxShape.BroadcastShape` 广播、**`ExpandTo` 按 numpy 规则展开（源缺这一维按重复处理）**，再逐批量相乘。
- **归约：** `OnnxElementList.ReduceSum`（空轴=全部、负轴归一、越界拒绝、`keepdims` 决定 `[1,1]` 还是标量）；**轴可来自属性，也可来自常量输入。**
- **常量权重：** `initializer` 绑成逐元素常量列表并把 `MatMul`／`Gemm` 接进求值器；**混合情形按"把一个元素乘上另一侧的常数"计算，并去掉零系数项**。
- **`Gemm`：** `OnnxElementList.Gemm`（转置→乘→`alpha`→`beta·C`，同一顺序；`[N]` 偏置按行广播）。
- **矩阵乘：** `OnnxElementList.MatMul`（秩 1 提升后压维、内维校验、一侧常量时乘加；**两侧含变量与批量维都明确拒绝**）。
- **采集：** `OnnxElementList.Gather(indices, axis)`（**索引顺序即结果顺序**、负数按轴长归一、越界拒绝、按 `outer × 索引 × inner` 收集）；求值器要求索引是静态常量。
- **多输出与分片：** `Split` 的每一段各自绑名（`EvaluateSplit`），**"只绑第一段"这种半成品会被判据抓住**。
- **分片算法：** `OnnxElementList.Split(axis, sizes, count)`（游标逐段前进、均分除不尽拒绝、段长和与非正段长校验）。
- **拼接：** `OnnxElementList.Concat(axis)`（对齐`concat` 的循环结构、负数轴归一、非拼接维一致、**按行交错而不是整块接上**）。
- **形状传播：** `OnnxShapePropagation` 沿生命周期算子推元素个数（`Add`／`Sub`／`Mul`／`Sum` 走广播、`Neg`／`Identity`／`Cast` 传下去、常量取自 `dims`），**认不出的算子一律"推不出来"而不是当 1**；**仿射链据此拒绝多元素输出，包括没声明形状的那种。**
- **列表语义：** 调用方给每元素符号（`inputBindings`）→ 逐元素运算、形状算子、`Scan` 全程按元素；**输出按 `Output(index)`／`OutputElements` 取**；**元素符号个数与声明元素数不符时在 `bindings` 阶段拒绝**。
- **列表语义：** 逐元素算子按位置配对（含标量广播）、**形状多元素而无元素列表的绑定拒绝**、**多元素图输出拒绝而非取第一个位置**；元素列表随绑定一路写下去（早前它在绑定处被丢掉，判据抓到了）。
- **扫描输入展开：** 导入器只把 `Scan` 的扫描输入按元素展开（`name_i`），其余输入不变；**"不偷偷展开"有判据钉住。**
- **元素列表接线：** 绑定带可选 `OnnxElementList`；**`Transpose` 有元素就真重排、`Scan` 有元素就真切片，没有时才回到"可观察性"判定。**
- **运行期切片：** `OnnxScanLowering.LowerWithElements`（长度取首维、每轮 `SliceLeading`、切片可含变量；**单元素切片的限制被明确拒绝而不是硬塞**）。
- **重排何时可见：** 求值器沿消费链判断——**逐元素消费不看位置，重排是空操作；按位置读者（`MatMul`／`Concat`／`Gather`／池化）才让它可见。**
- **逐元素表示：** `OnnxElementList`（形状 + 逐位置表达式，`Transpose` 按坐标重排、`SliceLeading` 沿首维切片，**纯函数、已用元素自身编号钉住**；尚未接进求值器）。
- **字段号与版本：** `OnnxSchemaVersion`（32 条字段号、`VerifiedIrVersion = 8`、`NeedsVersionBranch` 恒 false 且前提有判据、另一版本按字段号前向读取并如实报"未验证"）。
- **推理：** `OnnxInference`（接 `Microsoft.ML.OnnxRuntime`，只做输入名/形状核对、错误转译与生命周期收束；用手工编码的 `z = x + y` 模型真跑对照手算）。
- **单一入口：** `OnnxImporter`（解析 → 图校验 → 前置检查 → 界解析 → 仿射求值，失败时报告发生在哪一步）。
- **二次目标：** `OnnxQuadraticProduct` 的乘积经 `QuadraticFlattenData` 挂进 `QuadraticMetaModel` 并求解；`(x−3)²` 在 `[0,10]` 上最小 0、`[5,10]` 上最小 4（成对判据）。
- **二次乘积：** `OnnxQuadraticProduct`（两个一次式相乘成 `QuadraticPolynomial`，按无序对合并同类项）；**挂进元模型未做**。
- **形状算子：** `OnnxShapeTransform`（`Reshape`／`Flatten`／`Squeeze`／`Unsqueeze` 顺序保持、可落地；**`Transpose` 因重排无处可写而在多元素张量上拒绝**；`Concat` 同样需要逐元素表示）。
- **端到端：** 一个 ONNX 图经仿射链组装成线性模型后**真的被求解器解出**，最优值与手算一致（`OnnxAffineEndpointGurobiIntegrationTest`）。

**判据 131 条**（ONNX 测试工程）**+ 2 条端到端求解判据**，覆盖"不抛异常却会读错/算错"的那一类错误。

**明确未落地（不是"没找到"，是没做）：**

- **模型层接线：** 把输入界真正注册到变量、把降阶结果挂进 `MechanismModel`——**跨层，需要模型机制**；
- **子图展开**（`OnnxSubgraph` 586 行）：判定已落地，`If`／`Loop`／`Scan` 三类控制流都已能真正展开（含逐节点求值）；激活算子只支持常量折叠（符号输入需函数符号）；形状与归约算子的逐节点求值尚未实现；
- **形状与归约算子接进模型：** 目前只做形状推导，不改写模型形状。

## ONNX 实现：已完成与未实现（逐项，含理由与升级路径）

**这一段先立一条所有项共同的前提：** **函数符号的登记顺序（先登记、后展平）** **在补做任何一项时都必须仍然成立——**
**展平之后符号已经变成多项式的项，再登记就找不回对应关系，** **而那种错只会表现为"某个函数符号不见了"。**

**This paragraph states one premise that holds for every item below: the registration order of function symbols (register first, flatten afterwards) has to survive whatever gets built — after flattening the
symbols are already terms of a polynomial and registering them afterwards cannot recover the correspondence, and that failure only shows up as "a function symbol went missing".**

**这份清单存在的理由：** **未实现项若不逐条写清"为什么没做、怎么补上、影响谁"，** **下一个人只能从代码里猜——而猜错的方向通常是"以为已经支持了"。**

| # | 未实现项 | 为什么没做 | 升级路径 | 影响 |
| --- | --- | --- | --- | --- |
| 1 | ~~其余激活的符号输入（`Sigmoid`／`Tanh`／`Softplus`／`Elu`）~~ **已补做** | — | 按`approximated`（`OnnxPiecewiseApproximation` + `UnivariateLinearPiecewiseFunction`）走分段线性：界从变量自己取、`>1e6` 视作缺失、`lower == upper` 塌缩成常数、断点均匀且在断点上精确插值 | **已支持**：分段 `Sigmoid` 在 Gurobi 上求解，最小值 `sigmoid(-5) ≈ 0.0067` |
| 3 | **覆盖范围之外：`Conv` / `MaxPool` / `AveragePool`** | **`dee897d2` 的降阶分派里没有这三个算子，** 整个 ONNX 模块搜 `Conv`／`Pool` 只命中 `Convert` 一词。**所以这不是 C# 的缺口，而是两侧都不覆盖的算子范围** | **没有升级路径可写：**两侧都不覆盖，补它等于扩展范围而不是实现** | **不在实现范围内**：把它当作待补项会去实现本身没有的东西 |
| 2 | ~~二次域的批量维~~ **已补做** | — | **与一次域同一套广播：**下标数学共用 `ExpandIndices`，两个域各自用自己的元素去取；**不相容的批量维仍然拒绝** | **已支持**：`[2,1,2]·[2,2,1]` 得 `[2,1,1]`（每批量两个二次项），批量 3 对 2 被拒 |
| 2 | **非目标：逐名对齐**（`OnnxCallbackAdapter` 377 行／`OnnxFunctionalModel` 等模型对象） | **C# 侧并不存在与 Kotlin 一一对应的模型对象：** 职责由 `OnnxModelAttachment` ＋ `OnnxAffineModelBuilder` 承担，**逐名对齐只会造出空壳。** 要做的是能力对齐，而能力已对齐 | **不需要补：**能力已对齐，逐名对齐没有可补的东西；** 若将来 C# 侧真的引入对应对象，那时再谈名字 | **没有人会去找不存在的对象，也不会有人为了对齐名字而造空壳** |
| 3 | ~~`Scan` 运行期切片~~ **已支持** | — | 长度取首维、每轮 `SliceLeading`，**求值器在扫描输入带元素列表时直接用它** | **已支持**：三片 `x0`／`x1`／`x2` 得 `x0 + x1 + x2`；**导入器路径仍要求常量扫描输入**（因为导入器还构造不出元素列表，与第 2 项同一层） |
| 5 | ~~`ir_version` 的版本分支~~ **已定案** | — | 不分支：protobuf 字段号发布后不可改，新版本只追加字段；前提是"按字段号分派"与"未知字段被跳过"，两者各有判据 | **不支持按版本改语义**；`OnnxSchemaVersion` 列出 32 条字段号并如实报告某版本是否验证过 |
| 6 | ~~二次目标挂进元模型并求解~~ **已补做** | — | 二次的 `AddObject` 在 `QuadraticMetaModel` 上；`ToQuadraticTetradModel` 的 `fixedVariables` 是"待固定"而非"参考点" | **已支持**：`(x−3)²` 在 `[0,10]` 上最小 0（x=3）、在 `[5,10]` 上最小 4，两条 Gurobi 判据成对验证 |

**已完成的部分（供对照，不重复叙述细节）：** 数据层（`OnnxDataType`／`OnnxHalfPrecision`／`OnnxError`／`OnnxTensorConstant`／`ProtoReader`／`OnnxModelParser`（含 `AttributeProto.g` 子图解析））、语义层（切分策略／激活语义／形状算术与六个算子／图校验／常量折叠／输出聚合／导入选项）、降阶层（算子表与前置检查／精确算子化简／近似算子降阶／界判定与界约束／仿射链／模型组装／模型挂接）、推理层（`OnnxInference`，用运行时真跑 `z = x + y` 对照手算）、控制流（三份契约 + 三条展开接线，**含就地更新的循环体：`TryRebind` 区分"声明"与"覆盖"**）、单一入口（`OnnxImporter`，按固定次序报告失败发生在哪一步）。

### 成员级审计的结论：三类误差，没有一个是真缺口

**做法**：按**类型名**把类与 C# 的类对上（`Capacity` ↔ `ICapacity` 的 `I` 前缀约定），
再逐成员比对。**泛型参数经签名传入的必须排除**，否则每个泛型类型都会报出一串假缺口。

**三类噪声**，逐条核实后**没有一个是真缺口**：

| 噪声来源 | 表现 |
| --- | --- |
| `typealias` | 把两个名字指向同一类型，按名比对会得到一个"缺类" |
| 命名差异 | 叫 `TaskSchedulingResourceUsage`，C# 拆成两个类型 |
| 泛型参数当成员 | 类型参数出现在公开签名里，被当成"成员缺失" |

**一处真删除**：`CapacityQuantityTypes` 曾经声明"quantity type utility methods"**而一个成员都没有**，
现在已不存在。**空声明比缺失更难发现**——它让"这个能力有实现"这个判断成立，而实际上没有。
