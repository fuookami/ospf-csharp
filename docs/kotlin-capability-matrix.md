# 能力矩阵

一张能一眼看全的表：**哪个模块完整、哪个部分、哪个缺失**。
逐条的能力对照在 [kotlin-sync-status.md](kotlin-sync-status.md)（上千行表格）；
迁移时的坑在 [kotlin-migration-guide.md](kotlin-migration-guide.md)。

基线固定为 `ospf-kotlin` 的 `12fdb587`。**"部分"与"缺失"都不是等价移植**，具体差异见状态文档。

图例：**✅ 完整** / **◐ 部分** / **❌ 缺失** / **➖ 不适用**（基线有此模块但 C# 侧按设计不提供）

## 一、数学与基础

| 能力 | C# 项目 | 状态 | 备注 |
| --- | --- | --- | --- |
| 数值类型（`FltX`/`Flt64`/`Flt32`/整数/有理） | `Math` | ✅ | |
| 代数概念与算子 | `Math` | ✅ | |
| 符号运算 | `Math.Symbol` | ✅ | 子树替换与移动共用一组变换入口：`ExpressionTransform`（B961 补，基线 113 行）；**另更正一处方法论：按文件名比出来的"91 个缺失"绝大多数是误报** |
| 多维数组 | `MultiArray` | ◐ | **C# 无通配符泛型**，`AbstractMultiArray<T, *>` 只能显式写约束 |
| 量纲与单位 | `Quantities` | ✅ | `ToDouble()` 在 `Quantity` 上，不在 `FltX` 上 |
| 工具与错误类型 | `Utils` | ✅ | `Result<T,C,E>` 只有 3 参数版本 |
| 按轴归约（`sumAxes`/`cumsumAxis`） | `Math` | ✅ | Batch 257 补齐 |
**B961 的教训比缺口重要：按文件名比，会报出九十一个假缺口。**

把基线 `ospf-kotlin-math/src/main` 的 **320** 个文件名与本仓 `src/**/*.cs` 做差，得到 **91 个"缺失"**。逐个去查，**绝大多数在别的文件里**：

| 候选 | 实情 |
| --- | --- |
| `Einsum` | 在 `Math/MultiArray/Einsum/EinsumParser.cs` |
| `CombinatoricsAsync` | 在 `Math/Combinatorics/CombinatoricsAsyncScope.cs` |
| `EscapeTime` | 在 `Math/Fractal/FractalAnalysis.cs` |
| `Gamma` / `Beta` | 在 `Core/Solver/Heuristic/` 的算子实现里 |
| **`ExpressionTransform`** | **B961 时真的没有**；**B1081 已补**——`src/Math/Symbol/Expression/ExpressionTransform.cs` 存在，**但当时还缺布尔 `Transform` 那个重载**（基线的 `BooleanExpression.transform`，先重建标量子树再交给变换器），B1081 一并补上。**基线那步用星投影 `ScalarExpression<*>`，C# 没有星投影，所以 C# 版带一个类型参数 `T`**——**而它不能瞎填：填 `decimal` 会让 `Comparison<double>` 落进叶子分支，"重建"就成了空转（第一版就是这么错的）** |

**"名字对不上"不是证据，只是线索。** 91 个里只有 1 个是真的，**误报率 98.9%**。

**B1082 重测：上面那组数（320 / 91 / 1）是 B961 的测量，已经过期了。**
`logs/b1082-math-gap.py` 现算的结果：

| 级 | 判法 | 数 |
| --- | --- | --- |
| 基线 `ospf-kotlin-math/src/main` 名数 | — | **284** |
| 一级 | 文件名对不上 | **61** |
| 二级 | **全仓源码里连这个词都不出现** | **25** |

**"91 个里只有 1 个是真的"这句话现在既不是 91，也不该只报 1。**
**而 B1081 补掉的那个 `ExpressionTransform` 恰好是当时唯一确认为真的那个**——
**说明那一次核对是准的，只是数字会随 C# 增长而变，而文档没跟着动。**

**二级那 25 个里，B1083 逐个核到最像缺口的一组：符号文本解析。**

| 基线（`src/main`） | 行数 | C# |
| --- | --- | --- |
| `math/symbol/expression/parser/ScalarParser.kt` | **713** | **B1272 更正：早已搬**（`src/Math/Symbol/Expression/Parser/ScalarExpressionParser.cs`，546 行）。**基线三个入口 `parse`／`parseScalarExpression`／`parseScalarExpressionOrNull` 全有**，并有 **25 条**行为测试（`ScalarExpressionParserTests.cs`）。**原矩阵这一格写"无"，而错因是**路径**——原写作 `symbol/parse/ScalarParser.kt`，而基线实际在 math 模块的 expression/parser 下；"在那个目录里没找到"被记成了"不存在"。**判据：`ScalarParserCoverageCorrectionTests`。 |
| `symbol/parse/PolynomialLexer.kt` | **281** | **B1084 已搬**：`src/Math/Symbol/Parse/PolynomialLexer.cs`（6 条判据） |
| `symbol/parse/PolynomialParser.kt` | **622** | **B1085 语法层、B1086 `CanonicalPolynomial`、B1087 不等式、B1088 线性/二次入口、B1089 `LinearInequality`、B1090 `QuadraticInequalityOf`**（`PolynomialParser.cs`，**22 条判据**——B1274 更正：原写 24，实测 `PolynomialParserTests.cs` 是 22）；**该文件全部入口已搬** |

**C# 的 `src/Math/Symbol/Parse/` 只有两样东西：数值解析（`INumberParser<T>` / `Int64NumberParser`）
和两个结果类型（`ParseIssue` / `ParseResult`）。**
**表达式与多项式的文本入口原本都没有实现——不是"名字换了"，是"这一层没有"。**
**B1084 多项式词法，B1085 语法核心，B1086 `CanonicalPolynomial`，B1087 不等式，B1090 二次不等式降低；B1091 表达式词法器的算术运算符，B1092 标量算术解析层，B1093 比较/逻辑/三元，B1094 `if-then-else`，B1095 函数调用与 `math.PI`/`math.E`。**

**`ScalarParser.kt`（713 行）到此全部有对应物**，整个文件此时 25 条判据钉着树形与结合性（其中算术那一段 9 条，见 B1092）。
**B1096 更正一处判据错误**：我一直以为"模式匹配"是 `ScalarParser.kt` 的最后一块，**而它在基线里根本不在那个文件里——它在布尔侧 `Parser.kt`（7 处 `PatternMatch`），C# 的 `Parser.cs` 早就实现了（6 处）。****我钉的是一个不存在的缺口。** 那条绊线已改成钉**优先级链的九层结构**，不再随进度收窄。

**B1095 起 `ScalarParser.kt` 只剩模式匹配没搬**：`like`/`contains`/`prefix`/`suffix`/`regex`/`exact` 六种。**整个文件此时共 25 条判据。****B1276 撤回 B1275 写在这里的一句话：****上一批写的是「那六种模式匹配一条都没有——既没实现，也没判据」，而那是**错的**。****实测（全部在 `src/Math/Symbol/Expression/` 下）：****① 词法层 `Lexer.cs` 认这六个关键字；② `TokenType` 有六个成员，`ToPatternMatchMode()` 把它们映射到 `PatternMatchMode`；③ `Parser.cs` 第 159—187 行真的消费它们、走到 `ParsePatternMatch`；④ `BooleanExpression.PatternMatch` 与 `ExpressionDsl.Like/LikePrefix/…` 都在；⑤ `BooleanParserTest.ParseLikeExpression_shouldWork` 断言 `name like 'John%'` → `PatternMatch<object>`。****所以六个模式算子是**全链路已实现且有测试**的。****B1275 之所以判错，是因为它只在 `ScalarExpressionParser.cs`（算术那一层）里搜关键字——而模式匹配是**布尔表达式**那一层的事。** **又一次「在错误的文件里没找到，被记成不存在」。**函数名剥 `math.` 前缀只在这一处做，`math.PI`/`math.E` 就地换成数值。**
**绊线已收窄到只钉"标量解析器有没有造 `PatternMatch`"。**

**B1094 起 `ScalarParser.kt` 的优先级链已完整**：`ternary/if-then-else → logicalOr → logicalAnd → comparison → additive → multiplicative → unaryMinus → power(右结合) → unaryPlus`，**B1094 当时是 20 条判据；B1275 实测该文件现为 25 条（后续 B1095—B1096 又加了模式匹配与函数调用那几段）。`if` 与三元落到同一个 `ScalarConditional` 节点。**
**B1094 顺带修了一处真缺口**：括号里原来只解析到加法层，**于是 `(if a then b else c fi)` 与 `(a ? b : c)` 都被拒。**
**仍缺：模式匹配（`like`/`contains`/`prefix`/`suffix`/`regex`/`exact`）与函数调用——绊线钉的是"标量解析器有没有造 `ScalarFunction`/`PatternMatch`"。**

**B1093 起 `ScalarParser.kt` 的优先级链到三元为止都在**：`ternary → logicalOr → logicalAnd → comparison → additive → multiplicative → unaryMinus → power(右结合) → unaryPlus`。**新增节点 `ScalarBoolean`（布尔包成标量，基线的同名物）与 `ScalarConditional`（三元）。**
**比较不链式**（`if` 不是 `while`，`1 &lt; 2 &lt; 3` 被 `Eof` 检查拒掉）；**三元两支都递归，故右结合**。
**仍缺：`if-then-else`、模式匹配（`like`/`contains`/`prefix`/`suffix`/`regex`/`exact`）、函数调用——绊线只钉 `if-then-else`。**

**B1092 起 `ScalarParser.kt` 的算术那一段（优先级链 `additive → multiplicative → unaryMinus → power(右结合) → unaryPlus`）已在 `Expression/Parser/ScalarExpressionParser.cs`，**算术那一段 9 条判据**钉着树形而非"解析成功"（B1275 核对：该文件此处指第 1—9 个测试方法，整文件现为 25 条）。**
**仍缺：三元 `?:`、`if-then-else`、逻辑与模式匹配——绊线已改为只钉这四项。**

**B1092 also added `Lexer.LexMode`：标量模式下 `-` 不作数字前缀，`-2^2` 才能读成 `-(2^2)`。**

**B1091 更正一处误判：表达式解析栈 `expression/parser/`（`Token.cs`/`Lexer.cs`/`Parser.cs`，701 行）一直就在仓里，只是**只覆盖布尔那一侧**。**这一批补的是词法器里的算术/条件/逻辑运算符与四个条件关键字**；**缺的是标量那一层（`ScalarParser.kt`），不是整条栈。**

**B1091 corrects a misjudgement: the expression parser stack was in the repo all along** (701 lines), **covering only the boolean side**. **This batch added the arithmetic, conditional and logical operators plus the four conditional keywords to the lexer**; **what is missing is the scalar layer, not the whole stack.**

**B1086 顺带修掉一个更底层的缺口**：C# 此前**没有任何"按名字标识"的符号类型**（只有 `PathSymbol`），
于是 `SymbolOfSerializedIdentifier` 对任何不带 `path:` 前缀的标识符**直接抛异常**——
**而多项式解析出来的记号全是这种。** 补了 `NamedSymbol`，并把那个分支改成基线的 `defaultSymbolOf` 回退。
**hex/JSON 那几段仍然留给 phase 13，缺的从来不是它们。**

**这是"名字对不上"里少数几个真的**：类型在、结果类型在、数值解析在，
**只有把字符串变成符号表达式的那一层不在。**

绊线 `SymbolicParsingGapTests` 的方向是反的：**缺口还是缺口时它绿，补上了它红**——
**红了才是提醒"文档里那句缺口该改了"。**
 已经验过的一个反例：`NumericInteger` 在基线里是
**带默认实现的泛型接口**（`inc`/`dec`/`lg`/`sqr` 都有实现），C# 用别的形式表达，
**所以它"不出现"但不是缺口。** **把候选从六十几个压到二十几个，
把逐个核对的成本降到可做——而那一步只能人做。**
这与 B942 放弃逐文件比对是同一个理由——**而 B942 当年只是"放弃"，这一批把它量出来了。**

补上的这个是真缺口：`ScalarExpression`/`BooleanExpression` 上没有递归重建入口，"把某个子树换成另一个"在本仓做不到。
现在三组入口都在（`Transform`/`TransformScalars`/`TransformBooleans`），**顺序是后序**。

**B961's lesson matters more than its gap: a name-based diff reports ninety-one phantom gaps.** Differencing the baseline's **320** math file names against this repo yields
**91 "missing"**, and checking each shows **almost all live inside differently-named files** — `Einsum`, `CombinatoricsAsync`, `EscapeTime`, `Gamma`, `Beta` are all present.
**A name mismatch is a lead, not evidence**: one of ninety-one was real, a **98.9% false-positive rate** — the reason B942 abandoned per-file comparison, and **B942 only abandoned it
while this batch measured it.** The one real gap is genuine: neither node hierarchy had a recursive rebuild entry, so replacing a subtree was **impossible here**.


## 二、核心求解

| 能力 | C# 项目 | 状态 | 备注 |
| --- | --- | --- | --- |
| 元模型与线性模型 | `Core` | ✅ | `Model/Mechanism/MetaModel.cs` + `MetaModelExportSupport`（导出可读 LP/二次）；**但函数符号降级不完整，见 §九 第 3、6 条**——这一行 ✅ 指的是线性/二次的模型结构，不是"函数也能落成行"（B963） |
| 求解器接口与注册 | `Core` | ✅ | `Solver/` 下 `ILinearSolver`/`IQuadraticSolver`/`IAbstractLinearSolver`/`IAbstractQuadraticSolver`/`IColumnGenerationSolver`；注册侧由各插件项目 `*SolverPlugin` 提供（B963） |
| 组合求解器 | `Core` | ✅ | `Solver/CombinatorialSolver.cs` + `CombinatorialSolveSupport`/`CombinatorialDumpSupport`；判据 `ComposedSolversTests`、`RunnerSolverTests`（B963） |
| **基线类型对照（B1277）** | — | — | **"基线里有多少类型本仓没有"这个问题，目前**无法用名字回答**。****B1277 写了三个版本的名字对照脚本，得到的数字依次是 **110**（按文件名）／**155**（按类型声明名）／**133**（加后缀与大小写容忍），**而抽查三个都推翻：`Imply`（`src/Core/Symbol/Function/Imply.cs` 就在）、`UnivariateLinearPiecewise`（本仓叫 `…PiecewiseFunction`）、`ModelView` 与 `ConstraintPriority`（都在 `src/Core/Model/Basic/`）。****根因：** **Kotlin 与 C# 的命名习惯不同，基线里还有大量 `typealias` 与纯函数文件——名字本来就不该相等。****所以这三个数字**不得作为结论引用**；它们的唯一位置是"一种方法失败了三次"的记录。****判据：** `NameBasedCounterpartScanTests`。 |
| 启发式算子 | `Core.Plugin.Heuristic` | ✅ | P8 收口；算子族见 §二 上一行，**测试 13 个源文件**（B1265 更正计数口径，B1271 新增一个）；另有 `PolicyTrainer`/`PolicyEvaluator` 的训练与评估判据（B963）。**B1270 删掉了 8 个并行遗留的死桩类**（`EnergyValleyOptimizer`／`GerminalCenterOptimization`／`HillClimbingAlgorithm`／`HarmonySearch`／`NeighborhoodSearch`／`SeagullOptimizationAlgorithm`／`WaterCycleAlgorithm`／`WarStrategyOptimizationAlgorithm`），**它们只返回 `ApplicationError` + `"not implemented"`、从不被构造，而真实实现在组合路径上（`ComposedSolvers`，35 个入口）——与基线 `HS.kt` 的组合做法一致。****同时删掉了 `Stub/StubAlgorithmTest.cs` 那 8 条「断言未实现」的判据：它们守着的就是该删的东西。**判据：`HeuristicDeadStubDeletionTests`。 |
**B963 给第二节四行补上了可核对依据，并写清一处张力。**

四行原来都是 ✅ 配空白备注。**空白不等于"没问题"，等于"没人写过它凭什么 ✅"**——
而下一个人要判断"这个模块能不能用"，只能自己去读一遍源码。

**唯一一处真张力在"元模型与线性模型"**：这一行是 ✅，而 §九 第 3、6 条说函数展开层整层缺失。
两者不矛盾，**但只有写清楚才不矛盾**——✅ 指的是线性/二次的模型结构、导出与身份传播，
**不是"函数符号也能落成行"**。留空的行会让人把前者读成后者，然后在写一个带 `Sin` 的约束时撞墙。

**B963 gave the four rows of section two a checkable basis and stated one real tension.** All four were ✅ with an empty remark, and **empty does not mean "fine" — it means
"nobody wrote down why this is ✅"**, leaving the next reader to re-read the source to judge whether the module is usable. **The one real tension is "metamodel and linear
model"**: that row is ✅ while §九 items 3 and 6 say the function expansion layer is entirely missing. The two do not contradict **only if it is spelled out** — the ✅ covers
the linear/quadratic model structure, export and identity propagation, **not "function symbols can also become rows"**. An empty cell invites reading the former as the latter,
and then hits the wall while writing a constraint with a `Sin` in it.


## 三、求解器插件

| 能力 | C# 项目 | 状态 | 备注 |
| --- | --- | --- | --- |
| Gurobi | `Core.Plugin.Gurobi` | ◐ | **能力探测已移植**（`GurobiFunctionCapabilities`，反射 `SetPWLObj`/`AddGenConstrPWL`），**但这份申报没有任何一端连着实际（B1268 补）**：三个插件合计申报 **13 项**原生能力，而 `src/` 里**零处**调用原生构造方法（`AddGenConstrAbs`／`AddGenConstrPWL`／`AddSOS`／`AddGenConstrIndicator`／`AddGenConstrMax`／`AddGenConstrAnd` 全部零命中），`Supports(...)` 的**生产调用方也为零**（只有测试在调）。**它问的是"这个 SDK 有没有这个方法"，而读者会理解成"这个后端会不会用原生形式"。****没有解因此算错——它是镜子，不是 bug。**缺的是上层的函数展开层（见下）。判据：`FunctionCapabilityDeclarationTests`。 |
| Gurobi 11 | `Core.Plugin.Gurobi11` | ◐ | **是 4 个求解器类，委托给 `Core.Plugin.Gurobi`**（`private readonly GurobiSolver _solver`）；基线是 22 个文件的独立副本。**缺的与 Gurobi 同一处**，但"同上"要说明它是委托而非复制（B959） |
| SCIP | `Core.Plugin.Scip` | ◐ | **真实原生求解已实测**（B959 更正）：门禁 `RunScipIntegration=true` 下 `Core.Plugin.Scip.Tests` **149 通过 / 0 失败**，含 `ScipDualIntegrationTest`、`ScipConstraintProgrammingIntegrationTest`、`ScipBendersReportIntegrationTest`、`QuadraticLegacyIisEntryIntegrationTests`、`CrossLanguageElasticParityTests`。仍缺原生 PWL 与函数展开层（见第九节第 3、6 条） |
| CPLEX | `Core.Plugin.Cplex` | ◐ | **原生 SOS2/PWL 已实现但未接线**（B1265 更正）：`CplexNativePiecewise.cs`（172 行，SOS2 lambda 形式，含点数校验、键映射、重复结果键拒绝）**存在**，且 `CplexFunctionCapabilities` **已申报** `PiecewiseLinearConstraint` 原生支持；**缺的只有生产调用方**。所以这一格不是"未实现"（工作项），而是"申报超过实际"（缺陷）——调用方看到申报会以为走原生形式，实际走通用线性化。IIS 返回显式不支持。判据：`NativePiecewiseWiringGapTests`。 |
| COPT | `Core.Plugin.Copt` | ◐ | **原生 SOS2/PWL 已实现但未接线**（B1265 更正）：`CoptNativePiecewise.cs` **存在**，且 `CoptFunctionCapabilities` **已申报** `PiecewiseLinearConstraint` 原生支持；**缺的只有生产调用方**。点式原生回调仍未移植。判据：`NativePiecewiseWiringGapTests`。 |
| Mosek | `Core.Plugin.Mosek` | ◐ | 代码在（**1388 行**），缺的是**许可**：B949 实测 `err_missing_license_file(1008)` |
| MindOpt | `Core.Plugin.Mindopt` | ◐ | 代码在（**3009 行**），缺的是**许可**：B949 实测 `License is not valid` |
| Hexaly | `Core.Plugin.Hexaly` | ◐ | 代码在（**2619 行**），缺的是**SDK**：B949 实测 `HEXALY_HOME` 未设、`/bin/Hexaly.NET.dll` 不存在 |
| Lingo | `Core.Plugin.Lingo` | ➖ | **基线本身未实现**，C# 侧是占位类型，不声明求解能力 |
| OptVerse | `Core.Plugin.Optverse` | ➖ | **基线本身未实现**，C# 侧是占位类型，不声明求解能力 |
**B959 逐行核了第三节，两处要改、一处要答。**

| 行 | 原来写的 | 实测 |
| --- | --- | --- |
| SCIP | "**没有真实原生求解的实测**" | **假**：门禁里 `Scip.Tests` **149 通过**，含对偶、CP、Benders 报告、IIS、跨语言弹性对照 |
| Mosek / MindOpt / Hexaly | 备注**空着** | 三者都有真实代码（1388 / 3009 / 2619 行），**卡在许可或 SDK**，B949 各有实测错误串 |
| Gurobi 11 | "同上" | 本仓是 **4 个类委托给 Gurobi**，基线是 **22 文件独立副本**——不是同一件事 |

**一个空着的备注比一个写错的备注更难发现。** 写错的会被人核（B955/B958 就是这么抓出来的），
**空着的只会被当成"没什么可说的"**——而这三行恰恰每一行都有话要说，且都是实测过的话。

**B959 checked section three row by row.** The SCIP row claimed "no real native solving was measured" — **false**: the gate runs `Scip.Tests` with **149 passing**, covering duals, CP,
Benders reports, IIS and a cross-language elastic parity test. The Mosek / MindOpt / Hexaly rows left the remark **empty** while all three carry real code (1388 / 3009 / 2619
lines) and are blocked on a licence or SDK, each with a measured error string from B949. And Gurobi 11 is **four classes delegating to Gurobi**, not a copy of the baseline's 22 files.
**An empty remark is harder to catch than a wrong one**: a wrong one gets checked — that is how B955 and B958 found theirs — **while an empty one reads as "nothing to say"**, and these
three rows each had something measured to say.


**这些插件的共同限制**：C# 侧只验证"没有原生 SDK 时返回显式的不可用结果"，
**没有验证有 SDK 时的实际求解行为**。因此"◐"表示"接口在、能力子集"，不表示"与基线等效"。

## 四、框架

| 能力 | C# 项目 | 状态 | 备注 |
| --- | --- | --- | --- |
| 求解框架与会话 | `Framework` | ✅ | P8 收口 |
| 网络调度 | `Framework.NetworkScheduling` | ✅ | P7 收口 |
| 甘特调度 | `Framework.GanttScheduling` | ✅ | P7 收口 |
| Gantt-Network 桥接 | `Framework.GanttNetworkScheduling` | ✅ | B278；**独立项目**，因为 `NetworkScheduling` 已引用 `GanttScheduling`，反向加桥接会成环 |
| 桥接接入 Demo4 流程 | — | ❌ | **是分叉不是漏调用**：Demo4 走了一条自己的标签搜索路径，与基线的束生成实现不同，且**无法验证结果一致** |
| BPP 2D 装箱 | `Framework.Bpp2d` | ✅ | 缺口清单对 `framework-bpp2d` 报 **0 真缺口**（56 个基线主源码）；`Box2Geometry` 在此项目内，见"几何 2D" |
| BPP 3D 装箱 | `Framework.Bpp3d` | ◐ | 见下方"Bpp3d 细项" |
| CSP 1D | `Framework.Csp1d` | ◐ | 见下方"CSP1D 细项" |
| BPP 1D | `Framework.Bpp1d` | ✅ | 25 个源文件；缺口清单对 `framework-bpp` 报 **0 真缺口**（B469 按命名空间对齐） |
| CSP 2D | `Framework.Csp2d` | ✅ | 21 个源文件，基线 18 个主源码全部落地（B465） |
| 几何 2D | `Framework.Bpp2d` | ◐ | 合并进 Bpp2d 而非独立工程：`Box2Geometry` 在 `Framework.Bpp2d.Domain`，重叠语义统一为"内部重叠"（相切不算重叠） |
| 消息插件（Kafka） | `Framework.Plugin` | ◐ | 有生产者/消费者（`KafkaProducer.cs`、`KafkaConsumer.cs`），**B1307 起有真实 broker 往返（三条判据）并修掉 `Dispose` 不 flush 的静默丢失**，**两层缺口**：① **没有真实 broker 往返测试**（B1266 复核确认）；② **更糟的一层：`KafkaInterfaceTests.cs` 的 8 条判据全部只是**成员存在性检查****（`IDisposable` 实现没有、`Send`/`SendJson` 方法在不在），**没有一条调用任何方法，因此没有一条会因行为错误而变红**——而文件名让它们看起来像行为覆盖。**一条只会因"删掉一个方法"而变红的判据，本质是编译期检查的副本，而编译器本来就在做那件事。**判据：`KafkaTestSubstanceTests`（① 测试文件仍无真实调用，闭合时要求改写；② 本行必须同时说出两层）。 |
| 持久化：EF Core / SQLite / MySQL / MongoDB / Redis | `Framework.Plugin` | ◐ | **SQLite 有真实往返**（B950：文件库 + 关连接再开）；**MySQL / PostgreSQL 在 B1307 补了真实往返（各三条判据）**，**MongoDB 在 B1308 也补了真实往返（三条判据）**（验收表 `⊘3`）。**Redis 是另一个形状（B1267 补）：`RedisClient.cs` 203 行代码**在**，而生产调用方**不在**——只有三个测试文件提到它，而那三个文件**都不构造它**。****所以"Redis 有客户端"成立，"Redis 可用"**在 B1298 已被推翻（本机真跑通了往返，见本行末尾的更正）**。****并且这些测试全是影子检查：`InterfaceVerificationTests`（14 条）、`RedisInterfaceTests`（11 条）、`KafkaInterfaceTests`（8 条）合计 33 条判据全部只问"某个成员在不在"。判据：`ExistenceOnlyTestHabitTests`。 **B1298 更正：** **本机 `127.0.0.1:6379` 端口开着、`PING` 返回 `+PONG`、无密码——所以"没有真实往返"只是**没人写那条测试**。** `RedisRoundTripIntegrationTest` 现在用生产入口 `Redis.Init` 起客户端并做五种取值形态的真往返（**4 条**，`-p:RunRedisIntegration=true`）；`Redis.Init` 补上了 `LastInitError`，"连不上"与"没配置"不再长得一样（另 **2 条**默认构建判据钉住诊断面）。**这一行不是缺口：** 本项目是框架，**只提供能力、不保证项目内必然有使用的地方**——可用且已实测即算完成；接哪条业务路径是使用方的决定。** |
| 持久化：H2 / JDBC client / Ktorm / MyBatis / MongoDB 翻译层 | — | ❌ | **B1117 评估过了，"未评估"这个状态消掉了，结论有两半**——**当时 `src/` 下 `BsonDocument` 出现 0 次，`MongoRepository` 只会 `Filter.Eq("_id", …)`**（计数可复现：`logs/b1117-module-sizes.py`）**。****① 谓词→查询的翻译能力在 C# 侧是有的，只是换了机制**：`SqlRelationalQueryCompiler`（**838 行**）覆盖全部 8 种布尔节点（`BooleanConstant`/`Comparison<>`/`InExpression<>`/`PatternMatch<>`/`NullCheck`/`And`/`Or`/`Not`），**14 条判据**（字面量参数化、通配符、空 IN、EXISTS 子查询、MySQL null 排序模拟、歧义列拒绝等）；EF Core 接表达式树，因此基线的"手拼 SQL 字符串"那一层（`JdbcClientSql.quoteIdentifierPath` 之类）**不需要对应物**。**② Mongo 翻译层 B1118—B1122 已全部补齐并接线**：`MongoScalarTranslator`（基线 209 行，**11 条判据**：`$字段` 引用、字段名解析、五个二元运算映射、`-x`→`$multiply:[-1,x]`、**`ABS`/`POWER` 按基线失败而非自作主张**、`null`→`BsonNull`、深度护栏失败而非漏出）；`MongoOrderByTranslator`（基线 83 行，**5 条判据**：升序 1/降序 -1、空排序→`null`、字段解析失败即失败、**多字段顺序保留**、`Nulls` 不静默忽略）；`MongoUpdateTranslator`（基线 120 行，**6 条判据**：`$set` 形状、**`SetNull` 写 `BsonNull` 而非 `$unset`**、空赋值→`null`、字段解析失败即失败、**整型保持 `Int32` 否则写进去查不出来**、**`SetFromExpression` 拒绝而非猜**）；`MongoBooleanTranslator`（基线 422 行，**四个里最大**，**14 条判据**：三值常量（`Unknown` 归"匹配不到"）、**常量在左时六个运算符全部反转**、引用在左不变、**null 检查同时管"是 null"与"缺失"**、正则元字符转义、**`Like` 先转义再替换通配符**、`$expr` 字段对字段、`NOT` 包整棵子树、不支持的谓词失败、**空逻辑在 C# 里构造不出来**（构造器拒绝空操作数，故基线那两段分支已删））；**B1122 把它们接进真实查询路径**：`MongoRepository.FindAsync(predicate, sortBy, resolver)`——在此之前仓库只会 `Filter.Eq("_id", …)` 与 `Filter.Empty`，**四个翻译器没有被任何生产路径调用过**；**三条判据**钉住最要紧的行为：**翻译失败不退化成"查全部"、不碰集合**（`FindCalls == 0`）、可翻译的谓词确实走到集合、排序失败同样不退化成无序结果。**明确的局限**：这条判据用探针复刻控制流形状（`IMongoDatabase` 有 40+ 成员，B1103 已踩过），**未连真实 Mongo 实例**。**仍未移植的**：H2 / Ktorm / MyBatis / JDBC 四个后端（C# 侧用 EF Core 承担同类职责，****B1294 已逐项核对（契约层）**：**基线三个表达式仓储的 `{find, count, exists, update, delete}` 与本仓 `ExpressionRepository` 一一对应，一个不多一个不少**；不等价的那部分同样明确：**基线用四个方言后端做同一件事，本仓用 EF Core 一个栈承担——是换了一条实现路，不是少了一个操作**；判据 `PersistenceContractEquivalenceTests`**）。**同口径计数**：基线 `framework-plugin` **61** 个主源码 vs 本仓 `Framework.Plugin` **15** 个。**（旧文写"61 vs 48/53，命名口径差异不是缺口"——那是拿一个模块比全部插件项目，口径不同。）** **B1296 改状态为 ◐ 並写清依据**：**这一格原来是 ❌，而它得 ❌ 的理由是"四个方言后端未移植"——那是"这些具体后端没有"，**不是"持久化能力没有"**。**而这两件事已经分开量过：****Mongo 翻译层已接线（`MongoTranslatorWiringTests`：三个有生产调用方，第四个因仓储用整文档替换而不需要）；**五个调用操作在契约层一一对应（`PersistenceContractEquivalenceTests`）。****所以状态改为 ◐：** **能力在、代替实现已核对，缺的是那四个方言后端本身。** | ◐ |
| P9 **MIP 驱动 CP 的真实端到端实测**（Gurobi 上跑通，`任意`二字落到实处） | — | ✅ | **B1263 只用脚本化假求解器证明了"线性解被翻译成 CP 解"——那证明的是翻译层，不证明整条路。假求解器返回的是我自己构造的 `SolveReport`，它不可能暴露"降阶模型 Gurobi 解不了"或"列索引对不上"。****本批补的是真实端到端：Gurobi 后端上 `x + y = 7`、`x = 3`（`0..10`），唯一解 `x = 3, y = 4`，按 ID 核对到具体值。****过程中真实抓到一个缺陷**：`GurobiLinearSolver.Descriptor` 显式返回 `null`，而我把它当成了构造前提——直接抛异常。**那会让这个类对它唯一存在的理由不可用。改为回退到 `Name`。****而我第一版判据写错了，错法值得记**：用 `x ≤ 3` 断言 `x == 3`，红了报 `x = 0`，我一度以为"约束被静默丢了"。**其实 `x = 0, y = 7` 完全合法——它同时满足两条约束，无目标函数时任意可行点都对。****所以红的是我的断言，不是代码；而它还暴露了判据没说清"需要唯一性"。改成等式后才真在测该测的东西。****另修一条自伤判据**：`TheGenericClass_ShouldNotKnowAnyConcreteBackend` 原来全文件搜 `GurobiLinearSolver`，被自己说明"多数后端不填 `Descriptor`"的注释弄红。**改为只检查代码行——一条会因为解释自己而变红的判据，会训练人少写解释，那是反向激励。****变异：把按 `Origin` 引用匹配改成恒真 → 端到端判据红；在类内插入真实的 `"ScipLinearSolver"` 引用 → 名单判据红。均已验。** | ✅ |
| P9 **`MipBackedConstraintProgrammingSolver` 已实现**（任意 MIP 后端现在都能当 CP 求解器） | — | ✅ | **B1263 实现了 B1262 定下的设计。它很短——因为编码层早就有了，SCIP 的 CP 本来就是由它驱动的；这个类只做两件事：把 lowerer 接到**调用方给的** `IAbstractLinearSolver` 上，再把线性解翻译回 CP 解。****所以在 B1262 之前，"Gurobi 不能做 CP"这句判断的真实含义不是"缺一层算法"，而是"缺一个构造函数参数"——而那句判断只有在真正写出来之后才算被验证。****一条明确拒绝**：`CollectConflict` 被拒绝而非静默忽略（B1261 的教训的第二次应用）。**两处已知限制写在明面上**：① 输出不带 `SolveReport`（线性是 `SolveReport<Flt64>`，CP 要 `SolveReport<Int64>`，中间那层逐字段翻译本批没做）；② 不施加 hint，并以 `HintsApplied => false` 显式标记——**提示被忽略比被拒绝更危险：忽略它求解仍成功，只是慢一些或拿到不同的解，而没有任何信号。****判据三条**：① **拒绝时信息点名"MIP 驱动"这条路并指出原生冲突该找 SCIP**（随便一条错误信息也能让它失败，而那对调用方毫无用处）；② **线性解被翻译成 CP 解且只含 CP 变量**——**同时检查"按引用匹配 `Origin` 定位列"与"辅助变量不外露"**，只检查解值会漏掉后者：一个把辅助变量也塞进去的解，值可能全对而形状是错的；③ **不可行不报成"没有解"**——两者修法相反，把后者报成前者会让真正的 bug 看起来像一次正常的不可行结论。****两条 B1262 的判据按它们自己写下的要求翻转**：`OnlyScip_...` → 现在钉**名单**（第三条后端出现时必须点名）；`TheGenericClass_ShouldBeAbsent...` → 改为**"它不认识任何具体后端"**（不能出现 `ScipLinearSolver`／`GurobiLinearSolver`），**因为写死一个后端就退化成 SCIP 那份代码的复制品，而复制品不会跟着后端一起修。** | ✅ |
| P9 **`MipBackedConstraintProgrammingSolver` 缺口量化**（8 文件块；本批只量化与钉住，不写实现） | — | ◐ | **B1262 是逐项对照里唯一"真的要写实现"的块**（基线 503 行 + 1 个核心测试 + 6 个 Gurobi／SCIP 插件与集成测试文件）。**这一批不写它，而是把缺口量化、把依赖关系钉住——这块比前五块大一个量级，而半成品比不做更糟。****调查结果有一件事与预期相反：编解码那一层**已经有了**（`ConstraintProgrammingToLinearModelLowerer`），而且 SCIP 的 CP 支持**正是**由它驱动的。也就是说 SCIP 的 CP 能力实际上是"MIP 驱动"的——只是那个能力被写死在`ScipConstraintProgrammingSolver` 里、绑在 `ScipLinearSolver` 上，而不是抽成一个接受任意`IAbstractLinearSolver` 的核心类。****所以缺口是：Gurobi／Cplex／Copt 没有 CP 能力，而它们本来只差"把 lowerer 接到自己的线性求解器上"。****判据两条**：① **只有 SCIP 实现 `IConstraintProgrammingSolver`**（钉的是**能力分布**而非"某个类存在"，接上第二个后端时必须列全）；② **通用类不在、而底座两件都在**——只断言前者会读成"CP 完全没做"，只断言后者会读成"CP 已经通了"，**两条一起才说出真实状态**。**变异：往 Gurobi 的线性求解器文件里塞一行含 `: IConstraintProgrammingSolver` 的注释 → ① 红，已验。****下一批的设计已经写定**：`MipBackedConstraintProgrammingSolver(IAbstractLinearSolver, ConstraintProgrammingLoweringPolicy)`，流程 = `Lower(model)` → `LinearMechanismModel<Flt64>.InvokeAsync` → `ToLinearTriadModel()` →`linearSolver.InvokeAsync` → 按输出映射 `ConstraintProgrammingFeasibleOutput`／`InfeasibleOutput`，**并且对 `CollectConflict` 明确拒绝**（MIP 后端拿不到原生冲突，B1261 记过"别把这件事说成求解器的问题"）。 | ◐ |
| P9 **`ConstraintProgrammingConflictAnalyzer` 补齐**（CP 原生冲突证据终于能取到） | — | ✅ | **B1261 按 B1260 的经验先查依赖，结果又是"只差适配器"：**`IConstraintProgrammingSolver`、`ConstraintProgrammingInfeasibleOutput`、`ConstraintProgrammingConflict`、`SolveDiagnostics.InfeasibilityEvidence`、`CollectConflict`／`ShrinkConflict` **全都在**。**所以缺口不是"CP 冲突证据拿不到"，而是"没有人去拿"。****与弹性过滤那条路的实质区别：弹性过滤是启发式（`Exact = false`、`Heuristic`、`Partial`），而这个分析器取的是求解器**原生**报出的冲突——`Exact = true`、`Validity.Verified`。两者证据强度不同，而调用方靠 `Capabilities` 与 `InfeasibilityEvidence` 上的字段决定信不信；报成同一档会让"机器验证过的冲突"与"松弛过滤的猜测"在矩阵上无法区分。****关键决定：`CollectConflict` 与 `ShrinkConflict` 在分析器里被**强制打开**，不沿用调用方设置**——一个"冲突分析器"在不收集冲突时什么也分析不出来，而失败会以"CP 输出缺少结构化 conflict 证据"出现，**看起来像求解器的问题，其实是分析器没把开关打开**。**判据用一个会记录收到选项的假求解器直接核对开关**：只断言"最终拿到了证据"不够——那只能证明这一次凑巧通了。**变异：改成沿用调用方设置 → 该判据红，已验。****另两条判据**：② 证据档位是 `Verified` 且 `Source = ConstraintConflict`；③ 求解器报可行时说清"未返回不可行输出"而不是"缺少结构化证据"——**两种情况的修法完全不同**（前者重新检查模型，后者检查收集开关），所以错误信息的内容本身要被断言。 | ✅ |
| P9 **`LegacyElasticQuadraticInfeasibilityAnalyzer` 补齐**（二次 IIS 终于能经框架调用） | — | ✅ | **B1260 逐项对照 `InfeasibilityAnalyzer.kt`（782 行）。这一块**不是整块缺失，是半块缺失**：`QuadraticLegacyIisCompute`（含 `ComputeLegacyIISAsync`）、`QuadraticIisModel`、`QuadraticElasticFilterResult`、`QuadraticIisEvidenceSource`、二次的 `DiagnosticConstraintId` 重载、以及线性的 `LegacyElasticInfeasibilityAnalyzer`**全都在**。缺的只是这个适配器类。****所以没有它，整套二次 IIS 机器无法经分析器框架被调用——一个能力齐备但没有入口的东西，在能力矩阵上与"没有这个东西"是同一格。****与线性版的一处实质差别：线性版用 `iis.RelaxedFeasible` 在 `Complete` 与 `Partial` 之间选，而 `QuadraticIisModel` 只有三个字段（`Subsystem`／`OriginRows`／`OriginColumns`），没有这个标志。所以二次版**一律报 `Partial`**——在一个拿不到依据的地方报弱的那一边；报强的那一边会把"没检查"说成"检查过"。****这不是缺陷而是信息在传递中丢失**：`ComputeLegacyIISAsync` 内部知道松弛是否可行，只是没有把它带出来；修它要改返回类型并牵动所有调用方，所以本批选择报弱并写明原因。****判据两条**：① **反射**确认它实现了 `ICapabilityAwareInfeasibilityAnalyzer<IQuadraticTetradModelView>` 与`IMaterializingInfeasibilityAnalyzer<IQuadraticTetradModelView, QuadraticIisModel>`——**接口实现由编译器保证，而"能被当成那个接口用"才是要说的事**；② 固定 `Partial` 且**理由仍成立**（二次 IIS 模型仍是三字段）。**变异：把 `Partial` 改成 `Complete` → ② 红，已验。****仍未同步**：`ConstraintProgrammingConflictAnalyzer`（同类缺口：CP 冲突分析器，本仓零文件）。 | ✅ |
| P9 **`QuadraticIntermediateSymbols` 工厂对象：不移植，理由与 B1258 不同** | — | ✅ | **B1259 逐项对照 `SymbolCombination.kt`（843 行）。按 B1258 的经验"先数叶子"：`IAbstractSymbolCombination`、`SymbolCombination`、`QuantitySymbolCombination` 本仓**全都有**。缺的只有 `QuadraticIntermediateSymbols`——一个 `data object`，十个 `operator fun invoke` 重载。****两条理由，都比"没人做"更具体：****① 零生产调用方**：基线里 `git grep` 只命中它自己的文件与一个测试。**② 那十个重载的**返回类型是 typealias****（`typealias QuadraticExpressionSymbols1<V> =SymbolCombination<QuadraticExpressionSymbol<V>, Shape1>`），而 C# 用结构化类型表达它——`SymbolCombination<QuadraticExpressionSymbol, Shape1>` 本来就写得出来，不需要造一个名字。**所以工厂缺的不只是"使用者"，还缺"一个 C# 需要的类型"。****本仓已为同一件事写过一次：`Framework.Bpp2d` 的 `Capacity` 注释说「`LinearIntermediateSymbols1` 是C# 无法表达的 Kotlin typealias，所以改用 `IReadOnlyList<...>`」。B1259 是那条先例的第二次应用。****判据两条**：① **空二次符号组合现在就能构造**（`SymbolCombination` 的 `(name, shape, ctor)` 三参构造 +构造时写入 `GroupIdentifier`／`IndexInGroup`），**并用两个组合核对组标识互不相同**——**这是"工厂只是糖"的实证：如果空组合造不出来，缺的就是能力；造得出来，缺的只是十个便捷重载**；② 该工厂对象仍不存在（缺口判据，加进来时要求改写理由为"那个具体的调用方"）。**变异时踩了一次：第一次删 `quad.GroupIdentifier = Identifier;` 判据没有红——不是判据弱，是那次替换**根本没生效**（缩进不匹配，而脚本没校验）。改用正则并打印 before/after 计数后立刻红。**"变异没红"的第一嫌疑人应当是"变异没生效"**，尤其当替换是把一整段删掉的时候。 | ✅ |
| P9 **`CompositeMathFunction` 基类：有理由地不移植**（含三条可执行理由） | — | ✅ | **B1258 逐项对照 `CompositeFunctions.kt`（739 行），结论是一个"不做"的决定——而它必须带理由，否则下一个人会重做同样的调查。****基线那个基类只做三件事，本仓情况各不相同：**| 那一件事 | 本仓状态 | 结论 || --- | --- | --- || `helperVariables` 拼接 | 每个叶子函数各自持有 | 已有，不需要基类 || `registerFunctionsAtomically`（全成或全不成） | 无 buffered 注册，但**不可观测** | 不移植 || 注册前校验捕获的输入边界 | 叶子函数处**完全没有** | **移植会得到一个没人赋的字段** |**第二条为什么不可观测**：基线需要 buffered 注册，是因为 `registerConstraints` 可能被调用在**已存活**的模型上（如重注册），那时"失败后留下半截行"会被看到；**而本仓每次 `InvokeAsync` 建新模型、失败即整个丢弃，半截行没有观察者。****第三条为什么不能照搬**：B1256 已查明 `CapturedInputBounds` 一类字段在本仓**没有任何生产者**——照搬校验就是在一个恒为 null 的字段上加检查，正是 B1256 点名批评的"永不触发的检查"。****判据 `CompositeMathFunctionPortDecisionTests` 两条**：① 无该基类、且叶子签名无 `inputBounds`（缺口判据，开始移植时会红）；② `TruncateConstraintsTo` 这个原语已在手——**若哪天需要一个可观测的原子注册，不必从零设计 buffered 模型**；同时钉住"现在没有这个需要"。**叶子函数（`PositivePart`／`Clamp`／`DeadZone`／`L1`·`L∞` 距离／`Range`）本仓全都在**，缺的只是基类。**变异：往叶子文件里塞一行含 `class CompositeMathFunction` 的注释 → ① 红，已验。** | ✅ |
| P9 **`SolveExperiment*`／`BatchSolveExperiment*` 整块补齐**（此前一个文件都没有） | — | ✅ | **B1257 按基线 `OfflineExperiment.kt`（117 行）补上这块编排层。此前本仓对 `SolveExperiment`／`BatchSolveExperiment` 的存在为**零文件**，而它依赖的 `SolveReport`／`SolveIssue`／`SolveIssueCategory`／`AuditFingerprint`／`SolverProvenance` 全都在——**缺的是编排，不是底座。**补的是 `SolveExperimentCase<I>`、`SolveExperimentResult<V>`、`BatchSolveExperimentReport<V>`、`BatchSolveExperimentRunner`，以及 `GeneratedMilpSampleMetadata<V>`、`OfflineSolveObservation<V>`。****这一块的价值全在两条确定性保证上，而两条都不能靠"读起来对"确认：① 场景按 `Id` **排序后**调度（输入顺序无影响）；② 结果按 `Id` **排序后**返回（并发程度无影响）。判据因此必须真的打乱输入、真的并发跑。****初稿里我在结果上又做了一次 `OrderBy`——那是冗余的**：结果按"已排序列表里的槽位"写入，必然有序。**冗余的那一次会让顺序保证看起来依赖它，于是真正该被保护的机制（槽位对应）反而没人看。已删。变异：去掉前置排序 → 顺序判据红，已验。****失败不抛**：一个场景失败变成一条带 `SolveIssue` 的结果、其余继续跑完。判据同时断言"失败那条在结果里、`Report` 为 null、错误变成 issue、其余仍成功"——**只断言"没有抛异常"不够：静默丢掉失败场景也满足那一条。****`ConcurrencyBudget` 记的是**生效值**而非传入值**（传 0 时抬到 1），否则读报告的人会以为这批是串行跑的。判据用 `Theory` 覆盖 0／1／3 并**实测峰值并发**。**`OfflineSolveObservation` 的类型注释沿用了基线的约束"不携带生产求解控制指令"**：本仓目前没有消费者，所以该约束现在靠"没有消费者"成立——**有消费者那天必须先回答"它能不能影响配置"。****仍未同步的同级大块**：`InfeasibilityAnalyzer`（782 行）、`CompositeFunctions`（739 行）、`SymbolCombination`（843 行）、`MipBackedConstraintProgrammingSolver`（8 个文件）。 | ✅ |
| P4 **无生产者输入字段审计**：四个字段在生产路径上无人赋值 | — | ✅ | **B1256 做的是 B1255 结尾提的那件事（查"搬过去了但没人读"），结果比预期更糟一档：不是"没人读"，而是**没人写**。****四个字段在 `src/` 里**没有任何赋值**，只有测试会设：**`UnivariateLinearPiecewiseStructure.CapturedInputBounds`（缺了**拒绝**物化）、`UnivariateLinearPiecewiseStructure.ExplicitM`（缺了退回自动推导）、`MaxStructure.CapturedInputBounds`（缺了**静默跳过**检查）、`IndicatorStructure.CapturedInputBounds`（同前）。****前两个"拒绝"是可接受的行为——它把缺口暴露出来。后两个"静默跳过"才是危险的：检查写在代码里、看着像防住了，而它**永远不会触发**，因为那个字段在生产路径上恒为 null。一条永不触发的检查比没有检查更糟——它让人以为这里有防护。****而"隐形"的那两个恰好是已经能真实求解的两个：21 次真实求解全部通过，而那两处检查一次都没跑过。这不让那 21 次求解失效——它们证明的是"行压出来的答案对"，而不是"边界证明被校验过"。把这两件事分开说，是这一批要留下的东西。****判据两条**：① 这两个字段在 `src/` 里仍无赋值（缺口判据，补上生产者时会红）；② 三种失效方式分开——PWL 返回失败、Max 用 `if (captured is null) return Ok()` 绕过、Indicator 用 `is not null` 绕过。**变异：人为加一处赋值 → ① 红，已验。****这批不修字段，只把它们钉住。** 补生产者要先回答"谁该捕获边界"，而那正是 B1254 记下的 `BoundDependencySnapshot` 读不回来的问题——属于另一层。 | ✅ |
| P4 极值结构的 **`Minimum` 方向**：此前无人读它，现修正并真实求解验证 | — | ✅ | **B1255 起点是一个具体缺陷：`MaxStructure.Minimum` 这个属性**没有任何地方读它**——声明了、`MaxFunction` 的投影也在填（B1252），而生成逻辑无论方向都产 MAX 行。于是 `min` 被当成 MAX 物化：**模型合法、能解，只是答案是最大值**。****这是延迟物化这条线里"投影把方向搬过去了、下游没人看"的第一个实例**——B1252／B1253 两次防的是"投影漏搬方向"，这一次是"搬了但没人读"。****为什么不做第二套实现**：**MAX 的两组行同乘 −1 即得 MIN 的两组，所以一个符号因子够了。Kotlin 基线里 `generateMinConstraints` 是独立的一段（自己的消息、自己的循环），本仓把两者合成了一个实现——合并没问题，问题在合并时"方向"这个参数没接上。****写这段时踩了两次符号陷阱，都是求解器替我发现的：****① 复用同一个 helper 给 Big-M 取符号 → MAX 立即无解。因为原始 MAX 行里 Big-M **不带负号**（`result − input + M·s ≤ M`），而输入项系数带负号，两者极性相反。****② 把 Big-M 一起取负 → MIN 无解。正确的 MIN 第二组是 `input − result ≤ M(1 − s)`：选中时收紧成 `result ≥ input`，未选中时松弛到 `result ≥ input − M`——**松弛是往下而不是往上**，这正是"整体取负"错掉的唯一一处。****所以"整体取负"这个想法只错了 Big-M 那一项，而它错了两次、两个方向各一次。****判据两半都有**：结构判据（默认套件、快，看符号）断言 `result` 系数随方向翻号而 Big-M **不翻**；求解判据（Gurobi／CPLEX／COPT 各一条，固定 x₁=1、x₂=3、最小化 y，正确答案 1；按 MAX 物化会得 3）。**两半都要：结构判据只看符号，求解判据才证明"这组行压出来的答案是对的"。****行名保留 MAX 的旧形状不动**（既有判据钉着它）；MIN 两族的语义对调，所以名字也对调。**变异：把 `Minimum` 重新忽略 → 结构判据红，已验。** | ✅ |
| P4 **四种未投影结构的断点逐条查清并钉住**（本批结清一个悬了两批的问题） | — | ✅ | **B1253 结尾说"`CapturedInputBounds` 由谁填必须回答"。这一批回答：**没有人填，而且现在也填不了。******`UnivariateLinearPiecewiseFunction` 确实捕获了它（`_inputDependencies`），但那个类型是 `internal` 的 `BoundDependencySnapshot`，只公开一个 `IsCurrent()`——它只能回答"边界还是当初那些吗"，**不能回答"当初是哪些"**。****所以投影要填这道证明，只能拿当前范围充数，而这个"证明"就恒为真、物化器那道检查变成形式。****结论：PWL 的延迟物化对真实函数符号**目前不可用**，不是"还没写"。****四种断点归成两类：****① 符号没保留物化需要的东西**——`XorFunction` 没存二元输入；`MaskingFunction` 存了 `Input`／`Mask`／`ResultVar` 但没存 `ConditionBounds`，而 `MaskingStructure` 要一个 `ConditionalValue`。****② 前提捕获了却拿不到，或根本没有源**——`PWL` 如上；`IndicatorStructure` **没有对应的函数符号**，仓库里只有几个行生成器辅助类。****四者都不是"写一个投影就能解决"，所以它们不在派生分派里，也不该在。****判据 `DeferredProjectionBlockersTests` 四条把它们钉成可执行事实**：没有 `IndicatorFunction`；`MaskingFunction` 不含 `ConditionBounds`；快照仍是 `internal` 且无只读出口；分派恰好六种且四种不在其中。****判据带名字而不是只数数目**，失败信息自己说清是哪一种变了。**变异：把快照改成 `public` → 红 1 条，已验。****本批是结清而不是"再做一点"**：把"一直挂着"换成一个明确结论与四条可核的理由。 | ✅ |
| P4 投影扩到六种，**`Xor` 无投影是一个具体发现**（`And`／`Or`／`Not` 新增） | — | ✅ | **B1253 加上 `And`／`Or`／`Not`。三个是互不相关的 sealed 类，没有共同基类**——四个扩展各自一行，body 共用一个 helper；**而 `Not` 只存单个 `BinaryInput`（不是列表），要包一层单元素数组，这一处差异正是"不共用基类"的代价。****运算方向是 `Min`／`Max` 那个陷阱的第二个实例**：三个类互不相关，所以"投影调到哪个 body"决定运算种类；**若三个扩展都误调到同一分支，模型照样能解，只是把 `and` 当成了 `or`。****判据三种各建一个符号分别核对 `Operation`；变异：把 `or` 投影成 `and` → 只有 `or` 那条红。****`Xor` 没有投影，而且这不是遗漏：`XorFunction` 只存多项式、结果列、指示列、副列、容差与严格边界，没存二元输入**——而 `And`／`Or`／`Not` 都存了（`BinaryInputs`／`BinaryInput`）。**所以这条路在符号那一侧就断了：不是投影没写，是符号没保留物化需要的东西。**补它要先在 `XorFunction` 里保存二元输入，那会牵动 XOR 的急切约束生成；**而 XOR 在 n ≥ 3 时本身还有一条基线缺陷（B1195），所以这一处不急着动。****投影返回 `null` 而不是抛**：输入不是二元变量时 `BinaryInputs` 为 null，此时不派生、继续走急切注册。**若选择抛，"用户写了一个输入不是二元的 `and`"会在构建时炸，而它此前一直能建**——判据 `NonBinaryInputs_ShouldDeriveNothingAndKeepTheEagerRows` 钉的就是"新旧两条路都还在"。**仍未投影的三种**：`BivariateLinearPiecewise`、`Indicator`、`Masking`。 | ✅ |
| P4 投影从一种扩到三种（`Abs`／`Max`·`Min`／`Semi`），分派集中成一处 `switch` | — | ✅ | **B1252 把投影扩到 `Max`·`Min` 与 `Semi`，并把构建路径里的分派从"再加一个 `if`"改成一处 `switch`——加一种结构就是加一个分支。****`Semi` 是机械搬运**（五个字段一一对应），**而它值得单独一个扩展，恰恰因为它是机械的：如果连这一种都要在构建路径里特殊处理，投影这套东西的形状就不对。****`Max`／`Min` 有两处不是机械搬运：****① Big-M 必须**重算****——`MaxFunction` 只留了 `ExplicitBigM`（可能为 null），构造时算出的那组没存下来；投影用同一个 `BigMs` 静态方法重算，**"同一个方法"是重点：换种算法就得另一组大 M，模型照样能解，只是边界不同。****② `Min` 与 `Max` 是同一个类的两个构造**，投影必须读方向，否则 `min` 会被当成 `max` 物化、模型照样能解而答案错。**为此把 `ExplicitBigM` 的可见性从 `private protected` 放宽到 `public`，并新增只读属性 `IsMinimum`**——放宽可见性比"把算出的那组存下来"更小（后者要多一个字段、多一处赋值、多一条永远要成立的不变量）。**判据分别建 `Min` 与 `Max` 两个符号**（Theory 两个方向），**变异：把方向硬写成 `false` → 只有 `min` 那条红**，说明两个方向各自独立被覆盖。**`Semi` 的判据用非默认上下界（2 与 7）**——用默认值验，漏搬也看不出来。**仍未投影的四种**：`BivariateLinearPiecewise`、`BinaryLogic`、`Indicator`、`Masking`，继续只走急切注册。 | ✅ |
| P4 构建路径自动派生结构，并跳过急切注册（**派生与跳过一起做**） | — | ✅ | **B1251 补上 B1250 留下的缺口：投影有了、没人调用。而补的方式不是"加一个调用"，是"派生与跳过一起做"。****只接投影会得到两套描述同一个 `abs` 的行**——`AbsFunction` 注册时会同时生成自己的急切约束。**派生与跳过共用一个集合，是为了让两者不可能对不上。****核心判据数的是**行数****：**延迟模型在物化前应该是 **0** 行**（那唯一的 `abs` 已交给物化），而 `Eager` 下仍 > 0。**"两套行"这个缺陷不产生任何错误、求解也成功，唯一痕迹就是行数多一倍。****变异：只派生不跳过 → 红 1 条（正是那条数行数的），已验。****写这段时踩到三件事，都是实现细节但有解释价值：****① 模型符号表里装的是**适配器**（`LinearFunctionSymbolAdapter`），不是裸函数符号**——要先解包 `.Function` 才能投影；而**跳过用的仍是表里那个适配器**，因为注册阶段遍历的就是这张表。**② `AbsFunction<Flt64>` 是类，不能直接对 `IIntermediateSymbol` 做模式匹配（CS8121）**，需两步：先匹配接口再下转到类。**③ `metaModel.Converter` 是 `IFlt64ValueConverter<V>`，不是 `IIntoValue<Flt64>`，两者不能互转。`V = Flt64` 时它**按构造恒等**（`IdentityFlt64Converter.IntoValue` 原样返回），所以用 `IIntoValue<Flt64>.Identity` 是同一个函数而不是近似。****缺口判据第七次生效**：B1250 那条 `TheBuildPath_ShouldStillNotDeriveStructures` 变红并被改成`TheBuildPath_ShouldNowDeriveAndSkip`。**它当初把"为什么不能直接接"写成三条可执行事实，于是改它的人必须同时面对"跳过"那半边——而缺了跳过就会有两套行。****仍只覆盖 `Abs`**：其余六种函数符号没有投影，继续走急切注册，行为与今天一致。 | ✅ |
| P4 真实函数符号 → 延迟结构快照的投影（`AbsFunction` → `AbsStructure`） | — | ✅ | **B1250 起的是这条路。在此之前，十三批的所有判据都手工构造 `DeferredFunctionStructure`，而生产路径上没有任何地方构造它——"手工造的结构能物化"与"用户写的函数能被物化"是两件事。****投影几乎是机械的：`AbsFunction` 早就把物化需要的东西带在身上**（`ResultVar`、`PositiveVar`、`NegativeVar`、`SignVar`、两个 Big-M、输入多项式），且它们的范围在大 M 推导时就设好了——**这正是 B1244 那条"辅助变量必须非负"的契约在生产路径上不会踩坑的原因。****转换器由调用方传入而不是让函数符号回去问模型**：它是模型级的，而函数符号不拥有模型。****判据钉的是"同一个变量"而不是"相等的变量"（`BeSameAs`）**：**`AbsFunction` 构造时已把这些变量注册进大 M 推导，投影若新造一套，物化行会引用从未进过模型的变量——`AddConstraint` 会报"variable … is not registered"（B1239 踩过）。值语义下两者不可区分，注册表里差别是决定性的。****变异：让投影新造一个变量 → 红 2 条，已验。****另有缺口判据 `TheBuildPath_ShouldStillNotDeriveStructures`：投影有了，但没人调用它。而它此时不能直接接上——`AbsFunction` 注册时会同时生成自己的急切约束（`RegisterFunctionConstraints`），若不关掉那一份，模型里会出现两套描述同一个 `abs` 的行。****所以缺口不是"少调一次函数"，而是"还缺一个『这个符号已交给延迟物化』的开关"。这句话决定了下一批的起点，也解释了为什么本批停在这里。** | ✅ |
| P4 七种延迟结构的真实求解覆盖扩到**三个后端**（Gurobi／CPLEX／COPT） | — | ✅ | **B1249 把 B1248 那六条真实求解复制到 CPLEX 与 COPT，各自六条一次全部通过**（CPLEX 6/6、COPT 6/6，加上 Gurobi 的 7/7）。****B1245 只把 `abs` 复制到这两个后端，另外六种在它们上面仍然没跑过——而"`abs` 能解"推不出"`max` 能解"：两者共用物化框架，不共用行生成。****七种结构 × 三个后端 = 21 次真实求解，现在全部通过。****仍未求解验证的边界（矩阵这一行只说这三个后端，不说"所有后端"）：SCIP 的门控是集成测试那一档，需另走一次；Hexaly／Mindopt／Mosek 本机无运行时——后者属于**本机未试**，不是**已验证为不能**。****新增两个门控文件使 `CapabilityMatrixRemeasuredTests` 记录的文件数 CPLEX 3→4、COPT 5→6，已同步。这是第四次遇到同一机制。****变异（沿用 B1248 已验的那次）：把策略改成 `Eager` 即不物化 → 六条全红。本批是同一文件的复制，行为一致，故未再单独做变异。** | ✅ |
| P4 七种延迟结构的**真实求解**覆盖（Gurobi） | — | ✅ | **B1248 把求解验证从一种扩到七种**：`max`、`piecewise`、`binary-logic`、`semi`、`indicator`、`masking`各补一条真实求解，六条一次全部通过。**"一种结构能解"推不出"每种结构能解"——七种的行结构各不相同。****每条判据的共同设计仍是"行承重"**：目标最优值只能由 fallback 行压出来。**变异验证：把策略改成 `Eager`（即不物化）→ 六条全红**，说明行确实承重。**写这六条时暴露的构造契约**：**`MaxStructure` 的选择子与 Big-M 数量必须与输入数量一致**（两个输入 → 两个选择子、两个 Big-M），否则报"MAX inputs, selectors and Big-M values must have matching non-empty sizes"。**这一条此前没写下过——因为 `max` 从未被真跑过。****新增一个门控文件使 `CapabilityMatrixRemeasuredTests` 记录的 GUROBI 门禁文件数 28→29，已同步。这是第三次遇到同一机制。****仍未求解验证的**：**SCIP（门控是集成测试那一档）；Hexaly／Mindopt／Mosek 本机无运行时。****本行写的是"在 Gurobi 上七种都求解验证过"，不是"在所有后端上"。** | ✅ |
| P4 七种延迟结构：**七种全部端到端覆盖**（B1247 收口） | — | ✅ | **B1247 把 `masking` 纳入 `Theory`，覆盖从六种变成七种——「七种都通」这句话现在成立。****`masking` 的构造前提是两件具体的事：`ConditionBounds` 只是上下界两个值（且构造时校验下界不大于上界），而 `mask` 必须是**二元列**。两者都不在构造函数签名里被强调，写判据时才碰出来。****B1246 那条「六种覆盖、第七个是 Masking」的缺口判据在这一批**自己变红**（它报「masking 不该出现」），被改成 `AllSeven_ShouldBeCovered`——这是缺口判据第六次生效。****而这次它多挡了一件事：如果只加了一行 `InlineData("masking")` 却没让它跑通，红的会是 `Theory` 里 `masking` 那一格，而不是这条判据——**红的位置说清是哪一种失败**（「没跑通」与「没更新文档」）。**判据 **10 条**（`DeferredFunctionStructureCoverageTests`）：7 条 Theory（七种各一）+ 两张表一致 +拒绝时说清原因 + PWL 契约与覆盖 + 七种齐全。**变异：删掉 `masking` 的 `InlineData` → 红 1 条，已验。****覆盖层次的如实说明**：**七种都做到了「经真实构建路径 → 转换时真的物化 → 行数增加」；真实求解只在 `abs` 上验过，且验了三个后端（Gurobi／CPLEX／COPT）。其余六种**没有**跑过求解——所以本行不等于「七种都被求解验证过」。****剩余**：**六种结构未跑真实求解；SCIP 未跑同类求解；Hexaly／Mindopt／Mosek 本机无运行时。** | ✅ |
| P4 七种延迟结构：**六种端到端覆盖**，一名缺口写明（B1246） | — | ✅ | **B1246 把 `piecewise` 与 `indicator` 纳入 `Theory`，覆盖从四种扩到六种。剩下的恰好是 `masking`。****补 PWL 之前踩了两个前提，都是靠编译器与运行才发现的：****① `CapturedInputBounds` 在 PWL 上是 `Result<LinearPolynomialBounds, …>?`，在 `Indicator` 上是裸的`LinearPolynomialBounds?`——两个**同名属性类型不同**，而这一点只有编译器会说；**② 补上边界证明还不够，输入变量**自己的范围**也必须落在证明之内**，否则 `ValidateFallbackInputBounds` 报 "input bounds … exceed the captured Big-M proof"。**于是「有边界证明」与「输入真的落在证明里」是两件事，缺哪一件都过不去。****覆盖集合被写成判据而不是留在 `Theory` 行数里**：少一个 `InlineData` 不会有人注意（"大概就是这样吧"），而一条判据说"这六个必须在、第七个必须不在"，少一个红、多一个也红。**`masking` 没覆盖的原因是具体可核的**：它要一个 `ConditionalValue` 配一个 `ConditionBounds`，两个额外的值对象本批没做。**注释里写的是"没做 + 为什么"，不是"暂不支持"——前者下一个人能接着做，后者只会让他以为做不了。**判据 **9 条**（`DeferredFunctionStructureCoverageTests`）：6 条 Theory + 两张表一致 + 拒绝时说清原因 +PWL 契约与覆盖 + 六/七覆盖集合。**变异：删掉 `piecewise` 的 `InlineData` → 红 2 条，已验。****剩余**：**`masking` 未端到端；SCIP 未跑同类真实求解；Hexaly／Mindopt／Mosek 本机无运行时。** | ✅ |
| P4 延迟物化后的真实求解扩到三个后端（Gurobi／CPLEX／COPT） | — | ✅ | **B1245 把 B1244 那条真实求解判据复制到 CPLEX 与 COPT，两个后端都以目标值 = 3 通过。****为什么必须逐后端跑：「Gurobi 能解」不等于「四个后端都能解」。接线是**共享**的（`ToLinearTriadModel` 一处），而后端是**分开**的——共享的那一层对了，不能推出每条后端路径都对。****交接时踩了一个操作坑并记下来**：**用 `-p:DefineConstants=CPLEX_INTEGRATION` 直接覆盖常量会把插件自己的条件编译守卫一起改掉，于是真实实现与「不可用」桩同时参与编译，报一堆 CS0101 重复定义。正确写法是门禁脚本里的 `-p:RunCplexIntegration=true` / `-p:RunCoptIntegration=true`。「把常量塞进去」与「打开项目定义好的那个开关」不是同一件事，而前者的失败形态是一屏编译错误、看不出跟门控有关。****新增两个门控文件使 `CapabilityMatrixRemeasuredTests` 记录的文件数从 CPLEX 2→3、COPT 4→5，已同步。****仍未跑真实求解的后端：SCIP（门控方式是集成测试那一档，需另走）、Hexaly／Mindopt／Mosek（本机无运行时）。这三种属于**本机未试**，不是**已验证为不能**。****剩余**：**PWL 未端到端覆盖；Indicator／Masking 未逐一端到端；SCIP 未跑同类真实求解；Hexaly／Mindopt／Mosek 本机无运行时。** | ✅ |
| P4 延迟物化后**真实求解一次**（Gurobi 集成，31 个门内文件） | — | ✅ | **B1244 补这条线上最大的空白：此前所有判据止于"转换成三元模型"，真实求解一次都没跑过。****判据设计要点是"行承重"**：**目标是最小化 `y`，约束是 `x = −3`，唯一把 `y` 压到 3 的东西就是 `abs` 的 fallback 行。若那几行没被物化进去，`y` 就**没有下界**——求解给出无界或一个远离 3 的值，而**不会报错**。所以"断言目标值等于 3"不是顺便看看解对不对，而是**行是否存在的唯一证据**：少那几行，模型依然合法、依然能解，只是答案不对。****实测结果：目标值 = 3，通过。****而跑通它之前踩了两个没人写下来的构造契约，两个都是真跑才暴露的：****① `sign` 必须是二元变量**（`BinVar`）——连续 `sign` 下模型直接不可行；**② `pos`/`neg` 必须给下界**——fallback 行**不**约束这两个辅助变量非负，它们依赖变量自己的范围；不给下界时 `y = pos + neg` 一路走到负无穷（实测解出 **−4.5e15**，而不是报错）。****第二条尤其值得记下来：一行正确的等式，配上一个没设范围的辅助变量，产生的是一个"合法但错得离谱"的模型。生产路径不会踩到它，因为辅助变量由函数符号构造时自带范围；而任何手工造结构的调用方都会。****本批还发现新增门控测试文件会改变 `CapabilityMatrixRemeasuredTests` 记录的 GUROBI 门禁文件数（27 → 28），这是加门控文件的固定代价，已同步。**判据 **1 条**（`DeferredMaterialisationSolveGurobiIntegrationTest`，`GUROBI_INTEGRATION` 门控）。**剩余**：**PWL 未端到端覆盖；Indicator／Masking 未逐一端到端；其余后端未跑同类真实求解。** | ✅ |
| P4 七种延迟结构逐种走通与两张分派表对齐 | — | ✅ | **B1243 把"一种通了"扩到"每一种都通了"：`Abs`、`Max`、`Semi`、`BinaryLogic` 四种用 `Theory` 逐一走真实链路（`InvokeAsync` 建模型 → `ToLinearTriadModel` → 断言行数变多）。****为什么必须逐种测：`SupportsDeferredFallback` 是一次类型分派，物化器是另一套类型分派，两张表各自维护，而没有任何东西保证它们一致。判据把"两张表在七种上完全一致"钉住——少一种是缺陷、多声明一种也是缺陷。****本批先犯了一个错并当场改正**：**我据"只看到五个 `Unsupported(...)`"推断"只有五个物化器"，进而写了一判据说"差的那两种是 Indicator 与 Masking"——判据当场变红，真实分派在 `CoreMaterializer` 里共有七个分支。"看不到 `Unsupported`"≠"没有分支"：`Unsupported` 是默认分支的形状，正常分支根本不写它；数默认分支的个数，数出来的是"漏了谁"，不是"支持了谁"。****PWL 本批覆盖不到，而这不是缺陷是前提**：物化检查 `CapturedInputBounds`，为 null 时报"has no input bounds proof"，而它只能由真实符号路径在构建时捕获，手工造的结构永远没有。要走通必须从真的 PWL 函数符号生成结构——本批没做，而"本批没做"被单独写成一条判据，否则下一个人会以为七种都测过了。****BinaryLogic 的输入与结果必须是二元类型**（`BinVar`，不是 `RealVar`），否则物化报"invalid binary logic structure inputs"；**PWL 的转换器是 init 属性而不是构造参数**，不传就是 null。这两点都是写判据时才发现的。**判据 **7 条**（`DeferredFunctionStructureCoverageTests`）：4 条 Theory + 两张表一致 + 拒绝时说清原因 + PWL 前提。**变异：删掉 `AbsStructure` 的物化分支 → 红 2 条（表一致判据 + abs 端到端），已验。****剩余**：**PWL 未端到端覆盖（需真实函数符号）；Indicator／Masking 未逐一端到端；真实求解仍未验证。** | ✅ |
| P4 构建路径供给延迟策略与结构（**链路终于可达**） | — | ✅ | **B1242 给唯一的生产构建路径 `InvokeAsync` / `InvokeBeforeCommitAsync` 补上两个参数，并真的传到模型构造点。在此之前，六批做的延迟物化链**没有任何生产模型能走上**——构造点不传策略也不传结构，于是每个模型都是 `Eager`、都没有待展开的东西。****默认值仍是 `Eager` 与 `null`，这是有意的：调用方不显式要延迟展开，就不该得到它。缺口补上不等于行为改变——改变的只是"想用的人现在能用了"。****B1241 那条缺口判据在这一批**变红**，被改成断言两个参数确实被转发。这正是它的设计意图：它描述"现在缺什么"，于是"补上了"无法静默通过。如果它当初写成"构造点应该传这两个参数"（一条正常判据），它在 B1241 就会是红的，而一个长期红的判据会被改成 `Should().BeTrue()`，于是六批之后没人知道它原本在等什么。****新增端到端判据 `ADeferredModelFromTheBuildPath_ShouldGainRowsWhenConverted`：模型由 `InvokeAsync` 建（于是有来源快照）、结构由参数传入（于是有待展开的东西）、转换于是真的会展开、行数差可数。这条判据在 B1241 之前**写不出来**——不是没人写，而是写出来也跑不起来。这是"一条判据跑不起来"作为线索的第二次收获：上一次指向副本丢快照，这一次指向构建路径不供给输入。**判据 **6 条**（`DeferredFunctionConversionWiringTests`）。**变异：让构造点不再传这两个参数 → 红 2 条（结构判据 + 端到端判据），已验。****至此这条线在原理上闭合**：策略与结构能进、转换时真的展开、行数可测。**剩余**：**真实求解路径（各插件 `SolveLinear`）尚未被端到端验证；`AbsStructure` 之外的六种结构未逐一走通。** | ✅ |
| P4 延迟物化接入转换咽喉（`ToLinearTriadModel`）与来源快照继承 | — | ✅ | **B1241 把物化接到 `ToLinearTriadModel`——七个后端插件、十四个调用点唯一的转换咽喉。接在这里的理由不是方便，而是正确性：三元模型装不下延迟结构。一个带延迟结构的机制模型被直接转换，得到的不是一个"稍后展开"的三元模型，而是一个**少了那几段约束**的三元模型——而它照样能求解、照样给解。****`configPolicy` 缺省为 `DeferredNativeFirst` 而不是与 `SolverConfig` 一致的 `Eager`：带配置的求解器显式传自己的值（那种情况下 `Eager` 是用户的明确选择），而检查类调用点不传——如果缺省也是 `Eager`，那些调用点会跳过展开、得到缺约束的三元模型。默认必须是安全那一侧。七个后端的 `DumpAsync` 全部改为传 `Config.FunctionExpansionPolicy`，按文件枚举验证而不是抽查。****本批抓到一个会让整条接线必然失败的缺陷**：**`_sourceSnapshot` 只在构建路径赋值，而 `Materialize` 用 `new LinearMechanismModel(&lt;V&gt;)` 造副本——副本没有快照，于是 `ValidateSourceSnapshot` 返回"来源模型快照缺失"。物化对、转换对、接线位置也对，而串起来必然失败。抓到它的不是"测了物化"也不是"测了转换"，而是测了物化之后再转换——而那条判据**跑不起来**，因为测试自己造的模型同样没有快照。修法是新增 `InheritSourceSnapshotFrom`。****而本批真正的结论比修复更大：没有**任何**生产模型带得来延迟结构。`InvokeAsync` 唯一的模型构造点不传 `functionExpansionPolicy` 也不传 `deferredFunctionStructures`，所以真实求解永远走 `Eager` 那一支。六批的东西全在，而没有一条生产路径能把它们喂进去——而这种状态的可怕之处是它**全绿**。缺口由 `TheBuildPath_ShouldNotYetSupplyTheStructures` 钉住。**判据 **5 条**（`DeferredFunctionConversionWiringTests`）：**唯一构建路径还不给这两个参数（缺口判据）**、**七个后端都传配置策略**、**转换默认值是安全那一侧**、**转换里先物化再转换（顺序就是正确性论证本身）**、**副本继承来源快照**。**变异三处全部命中：抽掉一个后端的传参 → 红；删掉快照继承 → 红；把物化调用去掉 → 红。****剩余缺口**：**构建路径不供给延迟结构，因此整条链仍不可达。** | ✅ |
| P4 配置侧 `FunctionExpansionPolicy` 与求解入口判定（`DeferredFunctionSolveEntry`） | — | ✅ | **B1240 按基线 `CoptLinearSolver.kt` 的入口判断补：两个策略，任一为 `Eager` 就走常规求解。****"任一为 Eager 就短路"不是偷懒，而是"两个开关里更保守的那个说了算"**：**配置说"立刻展开"时，某个模型说自己可以延迟，也不能推翻配置——否则"全局关掉延迟"这个动作在个别模型上会失效。****这个判断被单独做成函数，而不是写进求解器**：**它是那条语义的唯一实现点；写进求解器之后每个后端各写一遍，而四个后端各写一遍的东西，迟早会有一处写反。****写反的后果**不可见****：**配置说"立刻展开"、模型说"可以延迟"，如果写成"任一是非 `Eager` 就延迟"，那么"全局关掉延迟"在个别模型上失效，而求解仍然成功，只是走了另一条路径。****判据必须覆盖**四种**组合，因为这是一个二选一的短路**：**只测两种会把"任一"这个真正的规则漏掉——而漏掉它的实现看起来是对的（`&&` 与 `||` 在两种组合下无法区分）。****变异：`&&` 改成 `||` → **红 2 条**，已验。****本批**没有**把 `Materialize` 接进求解器，原因写清楚**：**接线需要一条从 `ILinearTriadModelView` 拿到策略的通路，而那个接口只有继承自 `IModelView` 的成员，没有策略；而求解入口拿到的是**视图**，不是模型。于是两半都在、但没接上——这与"只做了一半"是两种不同的状态，只有后者需要补实现。**判据 **6 条**（`DeferredFunctionSolveEntryTests`）：**4 条 Theory（四种策略组合）**、**配置默认 `Eager`**、**两半都存在但未被求解器读取（断言没有任何 `*Solver.cs` 调用 `Materialize`）**。**最后那条钉的正是"别假装接线完成"——这个方向已经因此栽过两次（B1235、B1239）。****剩余缺口**：**视图侧缺少策略访问点，因此 `Materialize` 仍未被任何求解入口调用。** | ✅ |
| P4 延迟函数物化的整体入口（**四段合一**，`DeferredFunctionMaterialization`） | — | ✅ | **B1239 补完基线四段函数的最后两段，于是四段现在都在一个函数里：****`Eager` 直接返回原模型（B1236）→ 过滤待处理结构（B1237／B1238 三个条件）→逐结构校验输入边界（本批）→ 复制模型、搬区域记录、调用驱动（本批）。****第三段只对两类结构做事**：**`UnivariateLinearPiecewiseStructure` 与 `MaxStructure` 需要校验输入边界，别的结构直接通过。这不是遗漏——只有这两个结构的 fallback 用到创建时捕获的输入范围，范围失效时它们的 `bigM` 就不再成立；校验别的结构是在检查一件不存在的事。****第四段先复制再物化而非原地改**：**基线注释写"保留源模型"，而"保留"有两层意思——失败时不污染（B1200 的事务解决）、**成功时也不改**（只能靠复制）。只做前者会让"再物化一次"看到一个已被改过的源。****本批最重要的发现不在"补完"，而在核对第三、四段时才看见的一件事：驱动把区域记录放进**返回值**，而返回值的唯一消费者是测试——于是 `DeferredFunctionConstraintRegions`在真实路径上永远是空的，B1238 刚补的第三个过滤条件因此**永远不会生效**。为此给 `IDeferredFunctionFallbackTarget` 加了 `RecordRegion`，让区域真正写进模型。****"过滤条件依赖的数据永远为空"是一种看起来对的错误**：**过滤逻辑正确、判据通过（因为判据自己造了那个非空集合），而真实路径上集合一直是空的。测"每一层都对"无法发现"层与层之间没接上"——需要一条判据从用户入口进去、在另一头断言。****修这个问题时还踩到一个真实限制**：**物化产出的约束引用了变量，而 `AddConstraint` 通过 token 表解析它们；测试里新建的变量没注册到 meta model，于是报 `variable y is not registered`。这是装配问题而不是实现缺陷，但它说明"物化能跑通"依赖变量已注册这一前提——已写进判据注释。**判据 **6 条**（`DeferredFunctionMaterializationTests`）：**`Eager` 时返回同一个引用（引用相等，因为"有没有发生物化"就是"回来的是不是同一个对象"）**、**空待处理集同样返回同一引用**、**物化后的模型真的带区域记录（跑完整入口，不是直接调驱动）**、**源模型行数不变**、**四段确实在同一处**、**驱动通过 target 记录区域**。**变异：把区域只放进返回值（复原 B1200 的缺陷）→ 红 2 条，已验。****剩余缺口**：**`Materialize` 尚未从求解器入口被调用——真实求解路径仍未经过它；`FunctionExpansionPolicy` 也还没有求解器配置侧的入口。** | ✅ |
| P4 `DeferredFunctionConstraintRegions` 与第三个过滤条件（**三样状态补齐**） | — | ✅ | **B1238 补上三样缺失状态的最后一样，并把 B1237 缺的第三个过滤条件接上。同时**删掉了那个临时开关** `skipBoundKeyFilter`。****为什么要删：它是 B1237 的交付之一，而它的用途是"让缺口在被使用的那一刻出现"。缺口补上之后，开关如果留着，就变成了一个**可以合法地物化两次**的入口——那比没有开关更糟，因为它让错误看起来是被允许的。所以"临时开关"这个做法有一个必须履行的后半部分：补上之后把它删掉。只做前半部分的临时开关会沉淀成永久缺陷。****缺第三个条件的后果不是"少做一件事"，而是**多做一次**：同一个结构被物化两次，于是模型里出现两条描述同一个 `y` 的约束。而求解器不会报错——两条都满足的解是存在的（就是本来那个解），所以求解"成功"，只是模型比应有的多了一段。这类缺陷的发现方式是"模型的约束数不对"，而不是"求解失败"，所以判据必须数**待处理集合的成员**。****区域记录用行号而不只是结构引用，因为它同时要回答"这些约束在模型的哪一段"——只记结构能回答"做过了"，记不下"做在哪"。****区域只追加、不回删**，而 `TruncateConstraintsTo` 会删行——**两者一旦不一致，区域就会指向一段不存在的行**。所以调用点必须把它们放在同一层事务里；**B1235 的截断原语目前只截行、不截区域，这一点写进了代码注释，是一个真实的限制。****两条缺口判据在这一批同时变红并被收尾**：**B1236 那条（"两个集合都不在"）改成断言**接线完备性**——三段判定都只读模型自己的状态、没有一段在等一个不存在的字段；B1237 那条（"只剩最后一样"）改成断言三样都在。****一条缺口判据的终点，就是它不再会因为"变好"而变红。**判据 **7 条**（`DeferredFunctionPendingFilterTests`）+ **6 条**（`DeferredFunctionMaterializationGateTests`）：**新增"已物化过的结构不再进入待处理集合"**、**"临时开关应该已经不存在"**、**"三样状态现在都在"**、**"入口判定只建立在真实状态上"**。**变异：删掉第三个条件（复原 B1237 的缺陷）→ 红 1 条，已验。****剩余缺口**：**物化的第三、四段（逐结构校验并真正追加约束）未接到求解路径；`materializeMechanismFunctionFallbacks` 作为一个整体入口仍未存在。** | ✅ |
| P4 `DeferredFunctionStructures` 与待处理过滤（**四段中的第二段**） | — | ✅ | **B1237 补三样缺失状态里的第二样，以及基线四段函数的第二段。****这一段有三个条件，本批只实现**两个**，第三个依赖仍缺的 `DeferredFunctionConstraintRegions`：****`supportsDeferredFallback()`（结构自身）→ 已做；结果键不在 `nativeFunctionKeys` 里（调用方参数）→ 已做；结果键不在已绑定区域里 → 未做。****第三个条件的缺席会让同一个结构被物化**两次**——一次在区域里、一次在这里。所以这个函数目前**不能**接到真实求解路径上。****而"缺一个条件"的正确写法与"缺三个条件"不同：****前者要给一个能被看到的开关，后者只要写"未实现"。所以 `Pending` 有一个显式为 `true` 的 `skipBoundKeyFilter` 参数，缺省 `false`——"接进来"这个动作必须先显式指定它一次，也就必须先读到那段说明。****用了 `NotSupportedException` 而不是返回一个"可能重复"的结果**：**后者会在两条重复约束里静默求解成功，而那种成功没有意义。****集合的 `null` 与空不等价**：**`null` 表示"调用方没提供"，空集合表示"提供了，是空的"。构造参数接受 `null` 以便旧调用点不必改，而属性永远返回非 `null`——读的一方不该被迫区分这两件事。****默认空而不是必传**：**绝大多数模型没有延迟结构，把"没有"与"有但空"分开会让每个调用点写一次不携带信息的 `Array.Empty`。**判据 **5 条**（`DeferredFunctionPendingFilterTests`）：**无结构时集合为空而非 `null`**、**过滤保留原顺序（顺序决定追加行的次序，换序会让同一输入产生两份模型）**、**原生建模的键被排除**、**不显式承认缺了过滤器就抛错**、**只剩最后一样缺口**。**变异：关掉"必须显式承认"的检查 → 红 1 条；删掉原生键排除 → 红 1 条，均已验。****B1236 的缺口判据如期变红并已更新**（它现在说"只剩最后一样"）——这是缺口判据机制的第三次生效。**剩余缺口**：**`DeferredFunctionConstraintRegions` 未挂到模型上；四段的第三、四段未做。** | ✅ |
| P4 `FunctionExpansionPolicy` 与物化入口守卫（**四段中的第一段**） | — | ✅ | **B1236 补三样缺失状态里的第一样，而它是唯一**不依赖求解路径**的一样**：**`FunctionExpansionPolicy` 只被读、不被写，"该不该现在展开"这个判断可以在任何求解器都不在场时完成。****所以它先来——补上它，基线四段函数的第一道守卫（`policy == Eager` 时原样返回）就成为可测的行为，而不必先动求解路径。****只做四段中的一段不是拆碎交付，而是让剩余缺口保持清晰**：| 段 | 依赖 | 本批 || --- | --- | --- || `policy == Eager` → 原样返回 | **只有策略属性** | ✅ || 收集已被区域占用的结果键 | `DeferredFunctionConstraintRegions` | 未做——模型上没有这个集合 || 过滤待处理结构 | `DeferredFunctionStructures` | 未做——模型上没有这个集合 || 逐个校验并物化 | 上面两样 + 驱动 | 未做——依赖前两行 |**现在缺的是两个集合，而不是"整个函数"。****默认值 `Eager` 不是随手定的**：**它是"什么都不延迟"，也就是最保守的一档——一个没配置过的模型不会突然走上延迟物化那条更复杂的路径。反过来默认 `Auto` 不会错，但会把"延迟"从一个**策略**变成一种**默认行为**。**判据 **5 条**（`DeferredFunctionMaterializationGateTests`）：**默认策略是 `Eager` 且默认跳过物化**、**2 条 Theory（非 `Eager` 不跳过）**、**策略只有一个 getter**、**剩余缺口仍被记录（断言模型上仍无那两个集合）**。**最后那条钉的是"缺口仍在"这个事实本身**——**一个只做第一步的实现，如果不写清楚后面还有三步，下一个人会以为它是完整的，而它恰恰在"模型有延迟结构"时才暴露。****变异：默认值改成 `Auto` → 红 1 条；`ShouldSkip` 恒返回 `true` → 红 2 条，均已验。****剩余缺口**：**`DeferredFunctionStructures` 与 `DeferredFunctionConstraintRegions` 仍未挂到模型上；`materializeMechanismFunctionFallbacks` 的后三段未做。** | ✅ |
| P4 延迟函数 fallback 的模型适配器（**接线的一半**，`LinearMechanismFallbackTarget`） | — | ✅ | **B1235 补的是**接线**，不是能力**：**B1200 已经把驱动、事务、回滚、五个 materializer 都写好了，而它们此前**只在测试里被调用**——`IDeferredFunctionFallbackTarget` 在整个产品代码里**没有任何实现类**。****"能力齐备但没有调用方"是一种不会报错的缺口**：**代码全部编译、判据全部绿，因为判据直接调那个能力；而真实路径从来不经过它。它和 B1232 的"读得有、写不出"同族但方向相反——那里缺一个实现，这里是实现齐全而没有入口。后者的判据必须是"谁调用了它"，而不是"它对不对"。****本批交付两样：** **`LinearMechanismFallbackTarget`（真模型的 target 适配器）与`LinearMechanismModel.TruncateConstraintsTo`（截断原语）。****截断原语放在模型上而不是让适配器直接改 `_constraints`**：**"截断到一个行数"是只有模型能正确处理的事——同一个模型上还有依赖表、符号表与延迟区域记录，它们必须与行数一致；让外部代码去 `RemoveRange`，等于把这个不变量交给外部维护。****越界的回滚点是真错误而非"没有东西要回滚"**：**它说明持有 checkpoint 的一方算错了行数，静默接受会让模型停在一个既不是开始也不是结束的状态。****本批**没有**把整条链路接通，原因写明而不留白**：**接线需要模型持有三样目前**不存在**的东西——`FunctionExpansionPolicy`、`DeferredFunctionStructures`、`DeferredFunctionConstraintRegions`；它们目前只是 `DeferredFunctionStructures.cs` 里的类型定义，从未挂到模型上。给模型加这三样状态并改求解路径，是一次比这一批大得多的改动。**判据 **3 条**（`LinearMechanismFallbackTargetTests`）：**接口恰好有一个产品实现（钉"谁实现了它"）**、**截断原语在模型上而非适配器直接改列表**、**用真模型跑追加→截断到 1 行→越界两次**。**变异：回滚改成清空到 0 → 红 1 条；越界检查关掉 → 红 1 条，均已验。****剩余缺口（明确记录）**：**模型侧三样状态 + 求解路径接线未做；`DeferredFunctionFallbackMaterializers.Core`仍无产品调用方。** | ✅ |
| P4 COPT / CPLEX 原生能力探测（`CoptFunctionCapabilities`、`CplexFunctionCapabilities`） | — | ✅ | **B1234 把 B1232／B1233 留下的那层区分写进用户能读到的地方**：**PWL 写侧已实现，而"求解器真的接受了它"从未实测。`Detect` 回答的正好是前者——它问的是"这个 SDK 有没有这个签名"，不是"我们写进去的约束被采纳了没有"。两者相差一次真跑，而本批不声称跨过它。****两个后端的方法名不同，这不是笔误**：**COPT 用 `AddSos` 返回类 `Sos`（一个方法用 `type` 参数同时支撑 SOS1／SOS2）；CPLEX 用专用的 `AddSOS2` 返回接口 `ISOS2`。"两个后端都支持 SOS2"是对的，"它们用同一个方法支持 SOS2"是错的——合写一个探测函数会让其中一侧永远返回 `false`，而 `false` 在能力声明里的意思是"这个 SDK 不支持"，是一个看起来合理的错答案。"探测失败"与"不支持"在报告上是同一个词，而原因完全不同。****探测按签名匹配而非按名字**（`GetMethod` 而非过滤名字）：`AddSos` 有三个重载、`AddSOS2` 有五个，按名字会命中任意一个。**这一层比 `GurobiFunctionCapabilities` 严格**——那里只查名字并记了 `ponytail:` 天花板，因为基线的 COPT 探测就查签名。**CPLEX 只声明 SOS2 不声明 SOS1**：`AddSOS2` 就是 SOS2，没有一条能创建 SOS1 的专用方法被探到；这里的 `Sos1` 不是"还没测"而是"没有证据"，而两者在能力声明里都必须读作"不声明"。**判据 **6 条**（`CoptFunctionCapabilitiesTests` 3 + `CplexFunctionCapabilitiesTests` 3）：**无 SDK 时零声明且版本保持 `null`**、**探测路径不得声称已验证（只看代码行不看注释）**、**按签名匹配**、**两个后端探测不同方法名**、**CPLEX 不声明 SOS1**。**变异：CPLEX 改探 `AddSos` → 红 1 条；无 SDK 时返回 `""` 而非 `null` → 红 1 条，均已验。****未覆盖**：**"探测到签名"仍需一次真跑才能升级为"求解结果已实测"，本批不越过这条线。** | ✅ |
| P4 CPLEX 原生 PWL 写侧（同形，**但多两条防御**，`CplexNativePiecewise`） | — | ✅ | **B1233 按基线 `CplexNativePiecewise.kt`（135 行）补写侧，写出来的东西与 B1232 的 COPT 完全一样**（λ 组、`Σλ=1`、`x`／`y` 两条 linking、`SOS2`）。**但校验比 COPT 多两条，而这不是疏忽。****“两个后端的写入器结构相同”很容易被读成“校验也应该相同”，而实际上每一条校验都在回答那个后端**特有的**失败方式**：**COPT 需要无穷哨兵，因为它的 λ 界由哨兵推导；CPLEX 不需要哨兵，但它会直接接受非有限的断点、写出一行 NaN——那是 COPT 侧不会发生的失败。****所以合并这两个写入器是错的：它们的**形状**一样，**防御**不一样。****“断点严格递增”这一条尤其值得单独看**：**它不是“数据好看”的要求，而是 SOS2 的**前提**。SOS2 说“至多两个相邻 λ 非零”，而“相邻”由断点数组的**下标**定义；一组乱序断点会让“相邻”指向错误的两个点，于是插值在错误的区间上进行——模型可解，折线是错的。****“严格”也是关键**：**两个**相等**的相邻断点会让“相邻”指代一个零长度区间——那不是“退化成常数”，而是“落在一个不存在的区间里”。**判据 **11 条**（`CplexNativePiecewiseContractTests`）：**4 条 Theory（非有限值）+ 2 条 Theory（非递增）**+ **3 条结构校验 + 1 条合法通过 + 1 条“CPLEX 的校验是 COPT 的撇号”**。**最后那条钉的是**差别的存在**，防的是一种很自然的“整理”：有人看到两个写入器形状相同、把校验拿去复用，于是 CPLEX 失去有限性检查、或 COPT 多出一个它没有的哨兵参数。****变异：删掉递增校验 → 红 3 条；删掉有限性校验 → 红 4 条，均已验。****既有判据 `PwlLayerClaimsTests.ThePlugins_ShouldStillOnlyDetect` 再次翻转**：**B1232 那一批它断言“Cplex 仍然没有”，本批两个后端都有了，于是改成“两者都必须有”。判据的方向跟着缺口走——缺口在哪一侧，断言就指向哪一侧；缺口消失，断言改成“必须有”。****未覆盖（同 B1232）**：**“CPLEX 真的接受了这条 SOS2”需真跑 CPLEX 才能行为化，本批未验证；能力文档据此只声明“写侧已实现”，不声明“求解结果已实测”。** | ✅ |
| P4 COPT 原生 PWL 写侧（λ 组 + 三条行 + SOS2，`CoptNativePiecewise`） | — | ✅ | **B1232 补的是**写侧**：把 `x/y` 断点翻译成 COPT 里的 λ 变量组、`Σλ=1`、`x` 与 `y` 两条 linking 行、以及一条 `SOS2(λ, x 断点)`。读侧（报告里的求值与指纹）B1211—B1214 早就有了。****“读得有、写不出”是一种容易被忽略的半成品**：**报告能列出 PWL 结构、能算指纹、能校验列，而求解器从来没收到过任何一条 PWL 约束——于是那些报告描述的是一份**从未生效**的模型。它不报错，因为读侧读的是自己的数据；写侧缺失不产生任何信号。****SOS2 是唯一真正需要求解器配合的一条，理由写在注释里**：**缺了它，那四条行描述的是断点的**凸包**而不是折线——λ 可以是任意凸组合，`y` 能取到凸包内部的任意值。模型仍然可解，结果却是错的。****校验必须是前置的一整遍**（基线同样如此）：**中途失败会留下一半的模型改动，而 COPT 的 `AddVar` 不能回滚。**基线四项校验全部落地：无穷哨兵有限为正、输入/结果键不同且结果键不重复、断点至少两个且与函数值等长、两个键都必须在已映射变量集内。**其中“两个 PWL 抢同一个结果变量”比“键不同”更隐蔽：单看每一条都合法，冲突只在它们之间，而后果是两条折线同时决定一个 `y`。**判据 **11 条**（`CoptNativePiecewiseContractTests`）：**4 条 Theory（无效哨兵）+ 6 条校验拒绝 + 1 条合法通过**+ **1 条结构判据（先校验、且写 SOS2）**。**合法通过那条是上面六条的反面：没有它，把 `Validate` 写成永远失败也能让上面全绿。****变异：删掉 `AddSos` 调用 → 红 1 条；关掉未映射键检查 → 红 1 条，均已验。****未覆盖（记入缺口）**：**“COPT 真的接受了这条 SOS2”只能靠真跑 COPT 才行为化，本批未验证——能力文档据此只声明“写侧已实现”，不声明“求解结果已实测”。** | ✅ |
| P4 SCIP 的“未生效参数”披露更正（1 个源码，`ScipLinearNativeSolver`） | — | ✅ | **B1231 更正一处**不会让任何行为判据变红**的错误**：**`parallel/maxnthreads` 被记进 `ignoredParameters`，而它**已经生效**——上面第 278—280 行确实调用了 `SetIntParameter` 并在失败时返回 `ParameterFailure`。****`ignoredParameters` 的文档语义是“已声明但**未生效**的参数”，于是报告里出现了一个不可能的句子：一个被成功设下去、失败时还会报错的参数，同时被声称没有生效。****线程上限真的被设下去了，求解行为完全正确；错的只是那句关于自己的话。****基线里没有 `SCIPsolveConcurrent`——它是 Kotlin 侧并不存在的入口名。真实的差异是另一件事：`SCIPsolveConcurrent` 是一个**独立的求解入口**（多个种子并行跑并返回第一个结果），C# 走常规 `SCIPsolve` 只设线程上限。区别是“并行几路、结果怎么选”，不是“线程参数有没有生效”。****本批一度写错的第二个方向也记下来**：**我起初把差异塞成 `effective["concurrentSolving"]`，那就凭空造出一个 SCIP 没有的参数名，读取方会以为它是一个可以下发的设置。“不是参数的事实”该写在文档里，不该伪装成参数——这一批只做一件事：把错误的披露从 `ignored` 里删掉。**判据 2 条（`ScipIgnoredParameterDisclosureTests`）：**已生效的参数不得出现在 `ignoredParameters` 里**、**两个参数表都不得携带伪造的参数名**。**判据读源码而不是跑求解，因为行为是对的。****变异：把 `parallel/maxnthreads` 记回 `ignored` → 红 1 条；塞入 `effective["concurrentSolving"]` → 红 1 条，均已验。** | ✅ |
| P4 最后三处自解析来源键接上共享实现（**净删 3 份**） | — | ✅ | **B1230 收口：Gurobi 列生成、Gurobi Benders 分解，以及 Hexaly 一处内联的`Origin ?? new FallbackMathConstraint(index)`。****最后那一处值得单独看：它连函数都没抽出来，只是一行 `??`。****这种形态最不容易被“找重复实现”发现，因为它不是一个**声明**，而是一个**表达式**——而前四批的结构判据找的都是“声明”。****所以这一批的判据换了个锚：不数声明，数 `new FallbackMathConstraint(` 的出现次数。它必须恰好出现在共享解析器里一次——因为那是唯一知道“缺源时该造什么”的地方。判据的锚点决定了它能看见什么：前几批数“声明”，于是看不见内联表达式；这一批数“构造”，于是看见了。**判据增至 **30 条**（`FallbackMathConstraintSingleDefinitionTests`）：**新增“占位键只在共享解析器里构造”**与**“六个后端都不再自己解析来源键，含内联表达式（6 条 Theory）”**。**变异：在 Hexaly 恢复内联 `?? new FallbackMathConstraint(...)` → 红 2 条（已验）。****全仓 `new FallbackMathConstraint(` 现在只剩 1 处，在 `MetaConstraint.cs`。** | ✅ |
| P4 Farkas 整理合并为一份（**净删 4 份重复**，`MathConstraintOrigins`） | — | ✅ | **B1229 闭掉最后一份对偶辅助函数的重复。四个后端的 `ToFarkasSolution` 逐字相同，只有错误消息里的后端名不同。****这一批的核心判断是“该不该合成一个带开关的函数”，结论是不该**：**它与 `ToDualSolution` 的差别有**两点**——零值在此被丢弃（那里保留）、全零在此是失败（那里是合法空集）。一个布尔开关会表达成 `bool dropZeros`，而这两处同时涉及“丢不丢”与“全丢算不算错”两个独立语义；用一个布尔把它们绑在一起，会让“丢零但不算错”和“不丢零但算错”这两种组合无法表达。****语义依据也写进了注释**：**对偶值的零是信息（“这条约束没有紧”），Farkas 乘子的零不是（“这个乘子不参与证书”）；一个全零的 Farkas 向量不是证据，而一个全零的对偶向量照样是对偶。****`backendName` 是唯一按后端变化的参数，而它容易被“传了但没用”**：**合并时最常见的退化是把 `$"{backendName} ..."` 写成固定字符串，编译器不会抱怨（参数确实被接收了），而后果是可诊断性的丧失——用户看到“未提供可用的 Farkas 证书”却不知道是哪个后端。当四个后端共用一份实现时，这句话必须自己说出是谁。所以专门加了一条判据钉它。**判据增至 **23 条**（`FallbackMathConstraintSingleDefinitionTests`）**+ 7 条（`ScipLpDualSupportTest`）**：**新增“Farkas 整理恰好一个定义”**、**“四个后端都不再声明自己的 Farkas 整理（4 条 Theory）”**、**“后端名真的进了错误消息”**。**既有的零值丢弃与全零失败判据全绿**，本批只换了实现位置。**变异：从消息里去掉 `backendName` → 红 1 条；给 MindOPT 重新塞一份私有 `ToFarkasSolution` → 红 1 条，均已验。** | ✅ |
| P4 来源映射与对偶整理合并为一份（**净删 8 份重复**，`MathConstraintOrigins`） | — | ✅ | **B1228 把四个后端的 `OriginMap` 与 `ToDualSolution` 合并到 `Core`，与 B1226／B1227 同一动机。****这一批的可行性是上一批**先不合并**换来的**：**B1227 只合了那个纯函数，理由是“其余部分依赖各自的泛型特化”；本批核对下来，这两个函数依赖的泛型特化**完全相同**——四个插件都用同一个 `LinearMechanismModel&lt;Flt64&gt;`与 `IConstraint&lt;Flt64, LinearCategory&gt;`。所以上一批那个理由对 `Key` 成立、对这两个函数不成立，而这恰恰验证了当时的判断方式：不确定时先不合，核对之后再合，而不是先合再发现类型对不上。****`OriginMap` 允许空洞是设计而不是遗漏**：**机制生成的约束（松弛行、界行、归一化行）没有原始约束可指，却仍然占一行；`ResolveOriginKey` 正是为这些空洞准备占位键的。****`ToDualSolution` 与 `ToFarkasSolution` 的关键差别是**保留零值****：**对偶值里的零有意义（“这条约束没有紧”），而 Farkas 乘子里的零没有意义（“这个乘子不参与证书”）。同一个向量长度，两种不同的取舍——所以是两个函数而不是一个带开关的函数。**判据增至 **18 条**（`FallbackMathConstraintSingleDefinitionTests`）：**新增“来源映射与对偶整理各只有一个定义”**与**“四个后端都不再声明自己的来源映射（4 条 Theory）”**。**既有行为判据 `ScipLpDualSupportTest.ToDualSolution_ShouldCoverEveryRowAndUseAPlaceholderWithoutOrigin` 全绿**，**它已覆盖“无来源时用占位键”这条语义**，本批只是把实现换了位置。**变异：给 CPLEX 重新塞一份私有 `OriginMap` → 红 1 条（已验）。** | ✅ |
| P4 行号到来源键的解析合并为一份（**净删 4 份重复**，`MathConstraintOrigins`） | — | ✅ | **B1227 与 B1226 同源：占位键只剩一份定义之后，造占位键的那段三元表达式还在四个后端各写了一遍**（COPT／CPLEX／Mindopt 的 `XxxDualSupport` 与 SCIP 的 `ScipLpDualSupport`，逐字相同）。****这个函数**不返回 `Result`**，原因写进了它的注释**：**它回答“这个位置该用哪个键”，而每个位置都有答案（有来源就用来源，没有就造一个可识别的占位键）。一个永远有答案的查询用 `Result` 包装，只会让四个调用方各写一遍解包。****越界与空来源必须容忍而不是抛错**：**`originMap` 的长度来自机制模型的约束数，而对偶向量的长度可能来自求解器返回的**对偶模型**（列数 = 原行数 + 界对偶列 + 松弛列），两者本就不等。越界意味着“这一行没有对应的原约束”，而那不是错误——它是对偶模型比原模型多出来的那些行的正常状态。****这一批**只合那一个纯函数，没有整文件合并**，理由写进了判据注释**：**四个 `XxxDualSupport` 的其余部分依赖各自的 `LinearMechanismModel` 泛型特化与各自的后端结果类型，整文件合并会牵动 18 个调用文件；而那个三元表达式不依赖任何后端类型。“同一个形状的重复”不等于“同样大的一步”，B1226 与 B1227 的差别就是证据。**判据增至 **13 条**（`FallbackMathConstraintSingleDefinitionTests`）：**新增“解析函数恰好一个定义”**与**“四个后端都不再自己解析（4 条 Theory）”**。判据同样是结构的——**行为在四份实现上都是绿的，因为它们都正确**。**变异：给 COPT 重新塞一份私有 `Key` → 红 1 条（已验）。** | ✅ |
| P4 缺源占位键合并为一份（**净删 5 份重复**，`Core/Model/Mechanism/MetaConstraint.cs`） | — | ✅ | **B1226 把六个后端各自的 `FallbackMathConstraint` 合并到 `Core`。****合并前六份**逐字相同**，而其中四份用了四个不同的类名前缀**（`CoptFallbackMathConstraint`／`CplexFallbackMathConstraint`／`MindoptFallbackMathConstraint`／`HexalyFallbackMathConstraint`），**另外两份都叫 `FallbackMathConstraint` 却在不同命名空间里。****所以“它们其实一样”这件事在阅读时看不出来**——**而这正是重复最贵的形态：六份相同的代码并不比一份贵六倍，它贵在“没人知道它们相同，于是没人敢改任何一份”。****这个类型的用途是真实的**：**对偶/Farkas 整理按“来源约束”建字典，而有些行没有来源（对偶模型新建的行、界对偶列）。那些行不能凭空消失——消失会让行数变化，调用方按行号回查时就错位了。于是它们得到一个**可识别但非模型身份**的键：`__fallback_` 前缀让任何人一看就知道“这一行没有来源”。****判据是**结构**判据而非行为判据，而在这件事上它是唯一有效的形状**：**行为判据在六份实现上都是绿的，因为六份都正确。**判据 8 条（`FallbackMathConstraintSingleDefinitionTests`）：**全仓恰好一个占位键声明**、**共享实现落在 `Core` 而不是任何插件**、**六个插件各自都不再含私有副本（6 条 Theory）**。**核对了 `ToFarkasSolution` 的零值丢弃与全零失败语义：实现与文档声明一致，且早已有判据覆盖（`ScipLpDualSupportTest.ToFarkasSolution_ShouldDropZerosAndRejectUnusableCertificates`），本批未改。****变异：给 SCIP 重新塞一份私有副本 → 红 2 条（已验）。****一处**未覆盖**如实记下**：**把 `Core` 里那份从 `public` 改成 `internal` 判据不变红——因为那条判据钉的是“在哪个文件”，不是可访问性。可访问性由编译保证（六个插件都在用），所以没有另加判据。** | ✅ |
| P4 让“未测”的那条对偶路径变成可测（0 个产品源码，**仅测试模型**） | — | ✅ | **B1224 把“对偶路径是否真的读 `PrimalRowIdentities`”记为**未测**，理由是“需要能注册手写身份的构造入口，比本批更大”。B1225 证明那个判断**是错的**。****真正的阻碍只有三个字段：读 `ValidateIdentity` 源码发现，`Stable` 作用域**必须**带命名空间、schema 与非空来源，而 B1224 的测试身份只给了 `Kind`/`Id`/`Scope`。改用 `ModelLocal` 作用域后 `Dual()` 直接通过——模型内身份不需要注册表，而它在这个判据里与稳定身份等价。****于是 B1224 那条“全绿也区分不出”的变异（完全不读 `PrimalRowIdentities`）现在**被抓住了**：`TidyDualSolutionByIdentity_OnDualPath_ShouldKeyByPrimalIdentities` 红。第二个变异（快照置空）红 2 条。****这一批没有改任何产品代码是对的**：**问题从来不在产品侧，而在测试模型少给了三个字段。****“做不到”这个结论必须先读一遍拒绝它的那段代码，否则它只是“我没找到办法”的另一种说法。****教训是双向的**：**B1224 记下“未测”是做对了（没有把未测报成实测），但把原因归为“需要更大的改动”是错的——归因错了会让下一个人去做一个不需要的大改动。“未测”要配上“为什么未测”，而且那个“为什么”本身也要被验证。**判据 `LinearTriadDualTest` 增至 **23 条**（**B1286 实测更正：该文件只有 23 个用例；B1225 当时写的 24 是多数了一条**）：**新增 1 条对偶路径按原身份建键的判据**，并把 B1224 的“未测”说明改写为“已测”说明（保留原始阻塞点的记录）。**变异：忽略 `PrimalRowIdentities` → 红 1 条（B1224 时全绿）；快照置空 → 红 2 条。** | ✅ |
| P4 对偶解按身份整理（1 个源码，`LinearTriadModel`） | — | ✅ | **B1224 新增 `TidyDualSolutionByIdentity`：基线 `tidyDualSolution` 返回 `Map<Constraint, Flt64>`，C# 视图不持有约束对象引用（B1222 的表示差异），所以 key 用 `ConstraintId`。****两处新增：** **对偶/Farkas 模型保存原模型行身份快照 `PrimalRowIdentities`（`BuildDual()` 构造时带上），对偶路径按 `DualOrigin.RowIndex` 回查；行对齐路径直接取本模型 `Constraints.Ids[row]`。****快照而不是引用**：**视图的意义是可复制的快照，持活的原模型会让对偶模型生命周期绑在原模型上，原模型 `Dispose` 后对偶整理就会失败。****本批记下一条**未测**，而不是把它报成"实测通过"**：**变异实验把实现改成"完全不读 `PrimalRowIdentities`"，22 条判据**全部通过**（**B1286 更正：B1224 当时该文件是 22 条，B1225 加的那一条才是第 23 条**）。原因是测试能造的原模型只有两种——没有行身份（两条路的表都为空，无法区分）与有手写行身份（`Dual()` 在身份集合校验那关就失败，走不到整理步骤）。第二种失败不是缺陷：身份必须与注册表自洽，测试里手写的身份不在任何注册表里，`Dual()` 拒绝它是对的。****要让这条路径可测，需要能注册手写身份的构造入口或真实走过注册流程的模型——比本批更大，本批不做，而是把"未测"写成"未测"。**判据 5 条（并入 `LinearTriadDualTest`）：**无行身份时空表是正确答案**、**有行身份时完整覆盖且键真实不重复**、**快照存在且空列表不是 `null`**、**普通模型无快照**、**行对齐路径的键取自本模型身份（对照判据）**。**第一条是本批**方向写错**的判据，值得原样记：** **初版要求"表必须覆盖每一行"，跑起来红了（期望 3、实得 0）——而红的原因不是实现错了，是**判据前提错了**：测试原模型没给变量和约束设身份，而基线在 `origins` 为空时同样返回空 map。一个要求"表非空"的判据会逼实现去**伪造**身份，而伪造的身份比没有身份更危险——它会出现在下游账里，却指向不存在的约束。****另一条造模型的教训**：**初版给三行塞手写 `ModelElementIdentity` 后 `Dual()` 直接失败——身份不是"填个字符串"，它必须与一份注册表自洽。** | ⚠️ |
| P4 Farkas 生成列的派生身份（1 个源码，`LinearTriadModel`） | — | ✅ | **B1223 按基线 `LinearTriadModel.kt` 的 `withDerivedIdentity` 调用点，给三种 Farkas 角色补上派生身份**：**`farkas-constraint`（行乘子）、`farkas-slack`（等式行正负松弛）、`farkas-bound`（界乘子）。****底层 `DerivedModelElementIdentity.Create` 早就在、`MatrixIdentitySupport` 一直在用；缺的只是 Farkas 这条路径从不调用它。所以这不是“实现新能力”，而是“把已有能力接到漏掉的路径上”。****判别串照抄基线 `derivedDiscriminator` 的语义**：**源 ID 为空则用 model-local 判别串（`row` / `row:positive` / `row:negative` / `col:lower` / `col:upper`），否则用稳定判别串（`0` / `positive` / `negative` / `lower` / `upper`）。****源元素也照抄**：**行角色取第 `row` 行约束的身份，`farkas-bound` 取第 `col` 个**原变量**的身份（不是对偶列）——基线的 `boundVariables` 遍历的是 `this.variables[index]`。****顺带修了一个真实缺陷**：**`ModelViewVariable.WithIdentity` 会丢掉 `DualOrigin`（B1222 刚加的字段），所以每次派生身份都会把对偶来源清掉——已补上传递。**判据 3 条（并入 `LinearTriadDualTest`）：**每条生成列的身份必须**互不相同**、三个角色都在、同一个原模型求两次身份必须完全相同（确定性）。****“有身份”不够，必须判“互不相同”**：**一个所有列共用一个身份的实现在“有身份”下全绿，而它比没有身份更糟——下游按身份聚合时会把所有 Farkas 列合成一条。****同理“三个角色都在”比“有身份”更接近本批真正做的事**：**三种角色来自三处不同调用点，只接一种的实现能通过“有身份”。****判据前提被有意改变的一例**：**`AssertRowDual` 此前被 Farkas 断言复用并断言 `Identity` 为 null，Farkas 列有了身份后那两处就红了。红的原因是实现按基线前进、不是退化，所以是**拆分判据**（`AssertRowDual` 给对偶列、`AssertFarkasColumn` 给 Farkas 列），不是回退实现。****变异：把所有行乘子的判别串固定为 `0` → 红 1 条；去掉 `farkas-bound` 角色 → 红 2 条，均已验。** | ✅ |
| P4 对偶约束来源 `DualOrigin`（1 个源码，`ModelViewVariable`） | — | ✅ | **B1222 按基线 `ModelView.kt` 的 `dualOrigin: Constraint<Flt64, *>?` 补上 C# 侧缺失的字段。****基线的类型是“那个约束对象本身”，C# 的类型是“行号 + 关系”**（`ConstraintSlackOrigin(RowIndex, Relation)`）。**这不是简化，而是两侧模型表示的差异**：**Kotlin 的 `ModelView` 持有 `Constraint` 对象引用，C# 的 `ModelViewVariable` 只持有索引与关系，因为 C# 的 `ModelView` 是按列/行组织的扁平结构。在这里引入一个约束对象引用会双向绑定视图与模型，而视图的全部意义是“可复制的快照”。能表达的信息是同的**：**一个对偶变量对应哪一行、那一行是什么关系——这两件事决定了它的符号与它能承载的对偶值范围；引用本身在视图之外没有用处，因为视图不修改模型。****所以这是表示差异而不是缺口，但它必须被记下来：“C# 没有 `dualOrigin`”这句话在补上之前是真的，现在不真了。****填充点在真实路径上**：`LinearTriadModel.BuildDual()` 的行对偶列传入 `new ConstraintSlackOrigin(row, constraints.Signs[row])`。****判据 2 条**（`LinearTriadDualTest`）：**真实 `Dual()` 路径上每个行对偶列的 `DualOrigin.RowIndex` 必须是 `i`、关系必须等于原模型第 `i` 行的关系，且界对偶列必须无来源**；**普通变量必须保持 `null` 不伪造。****一条被判据否定掉的判据**：**初版还写了两条（`Copy()` 保留、`WithDomain()` 保留），而把这两处传递删掉会让**编译失败**——它们是编译期保证，不是运行时行为。“一条不能变红的判据比没有更糟”，所以整份文件被删掉，只把真正测行为的两条移进 `LinearTriadDualTest`。****变异：把 `dualOrigin` 改传 `null` → 红 1 条（已验，编译通过）。****数值判据抓不到这条**：**如果哪天对偶列顺序变了而来源没跟着变，所有依赖边界的数值判据仍绿，只有这条会红。** | ✅ |
| P4 “判为非缺口”类声明的纠偏（0 个新源码，**纯文档**） | — | ✅ | **B1221 核对 P4 限制段里的三条“判为非缺口”声明。****第一条过期**：**“C# 侧无内建 `IObjectStoragePort` 实现”——而 `LocalFileObjectStoragePort`（90 行，`: IObjectStoragePort`，空壳标记为 0）一直在。****这一条比前几批的更微妙：它是“扩展而非缺口”这个判断的一部分。判断本身是对的（基线固定版本确实只有接口、由调用方注入），但支撑它的那句事实描述是错的。****“结论对、依据错”比“结论错”更难发现**：**因为结论对，所有行为判据都绿着；而依据错，意味着以后再有人引用这个依据时会得到反的结论。****第二条**经核对成立、未改****：**“`solver`／`model` 指纹入口在固定基线里不存在”——读 `12fdb587` 的 `SolveFingerprinting` 确认只有 `sha256` 与 `configuration` 两个函数，外加一个私有 `escape`。****核对的方式是 `git -C ../ospf-kotlin cat-file -p 12fdb587:<path>`——“不切换 checkout”与“读固定提交”并不冲突，`cat-file` 正是两者的交集。**判据 `RemotePortingClaimTests` 增至 **13 条**：**新增 2 条**——**`LocalFileObjectStoragePort` 必须存在且真的 `: IObjectStoragePort`**、**基线 `SolveFingerprinting` 只有两个入口（且不得出现 `fun solver(`／`fun model(`）**。**第二条判据**核对的是基线**而不是 C#，因为那句声明的主语是基线**：**“`solver` 入口在基线里不存在”只能用基线来验，用 C# 验是对错了对象。****变异：去掉 `: IObjectStoragePort` → 红 1 条；把基线路径改错 → 红 1 条，均已验。****一条“经核对成立”的声明与一条“经核对已纠正”的声明同样重要**：**本批三条里两条改了、一条保留了原文——比例本身是信息。** | ✅ |
| P4 求解器回调的移植声明纠偏（0 个新源码，**纯文档**） | — | ✅ | **B1220 发现同一个词在同一份文档里对两个后端给出**相反**的结论。****`Callback=false` 出现在 CPLEX 与 COPT 两处，而事实是：****CPLEX 已移植**（`CplexSolveCallback` 201 行 + `CplexContinuousCallback`，两者都继承 SDK 的 point-based 回调基类并经 `model.Use(mipCallback)`／`model.Use(continuousCallback)` 接线；基线 `CplexSolverCallBack.kt` 为 157 行）；**COPT 未移植**（`CoptSolveCallback` 148 行只重写一个通用 `callback()`，不是 point-based 回调族）。****“同一个词在两个后端上结论相反”是这类文档最容易出错的地方**：**一个读者扫到第一处 `Callback=false` 就以为所有后端都没有回调，而事实是只有一个没有。所以纠偏不能按字面全局替换，只能逐个后端核对源码——本批四处更正都是逐后端核过的。****仍然成立的部分被保留**：**COPT 的 `Callback=false`、COPT 的二次 Benders 未实现、两个后端各自的 `NativeIis=false`、创建环境回调与 SOS2/PWL 写入未移植。****“有实现”与“被用上”是两件事**（B1122 记过同一条）：**判据第一条最先查的是“文件里有 `model.Use(`”，变异（删掉两处回调接线）**没变红**——因为同一文件还有 `model.Use(new Aborter())`。改成查 `model.Use(mipCallback)` 与 `model.Use(continuousCallback)` 这两个具体调用之后才抓住。“文件里有某个调用”与“这块东西被用上”之间差得很远，而前者看起来像是后者的证据。**判据 `RemotePortingClaimTests` 增至 **11 条**：**新增 2 条成对判据**——**CPLEX 的两个回调必须存在且被具体接线**、**COPT 仍只有一个通用 `callback()`**。**两条必须成对存在**：**只钉 CPLEX 已移植会让人以为 COPT 也已移植；只钉 COPT 未移植会让人以为 CPLEX 也没有。****变异：删掉 CPLEX 两处接线 → **初版全绿**，收紧后红 1 条；给 COPT 加第二个 override → 红 1 条，均已验。** | ✅ |
| P4 elastic IIS 引擎的移植声明纠偏（0 个新源码，**纯文档 + 注释**） | — | ✅ | **B1219 纠了三处同种失效的过期结论：代码做完了，描述停在立项那天。****第一处（状态文档）**：**“旧 elastic IIS 引擎（`feasibility()`／`elastic()` 与归约后的 `LinearIISModel`）尚未移植，故 `IisAvailable = false`”——实际 `LegacyElasticInfeasibilityAnalyzer`（152 行）、`LinearIisModel`（320 行）、`LinearLegacyIisCompute.ComputeLegacyIISAsync`、`QuadraticLegacyIisCompute`（612 行）全在且已知线（SCIP `ScipLinearSolver.cs:321` 与 COPT `CoptLinearSolver.cs:199` 置 `IisAvailable: true`）。****仍然成立的部分**：**Gurobi 侧 `IisAvailable` 仍为 `false`（其证据由 `Diagnostics` 经 `NativeIis` 来源承载），这是逐后端的能力差异，不是整体缺口。****第二处（类注释）**：**`QuadraticLegacyIisCompute` 的头注释写着“B398 定位到的最大单块缺失……本批只建立类型与入口骨架”，而文件有 612 行、`ComputeLegacyIISAsync` 已实现、空壳标记为 0。****第三处（文件首行）**：**同上那句“类型与入口骨架 / Types and entry skeleton”。****这一处是被新判据**当场抓住**的**：**我先只改了 `remarks` 块，判据立刻在文件首行红了一次——说明“同一句话在文件里出现两次”这种事，靠人眼扫一遍很容易漏掉第二次。****“612 对 846”不等于“还缺 234 行”**：**C# 用了不同的类型与错误表达，行数不是可比单位；能比的是入口与函数覆盖。判据与注释都写明这一点。**判据 `RemotePortingClaimTests` 增至 **9 条**：**新增 4 条 Theory（四个 IIS 文件必须存在且无空壳标记）与 1 条“已实现的文件不得自称骨架”**。**一次自指**：**我写的更正注释里出现了 `NotImplementedException`／`TODO`／`not ported` 三个词，于是判据把那段注释本身当成了空壳标记。改成“空壳标记计数为 0”后通过——“解释规则”这件事本身经常违反规则，而判据对此有意见是对的。****中文注释里的过期结论用英文标记扫不出来**：**英文标记（`TODO`／`not ported`）能扫，但“本批只建立类型与入口骨架”一个都匹配不到——而它比任何 TODO 更误导，因为它让读者以为整个文件是空的。补一条钉中文原话的判据，填的正是那个洞。****变异：`remarks` 里重新加“骨架”一句 → 红 1 条；给 `LinearIisModel` 加 `TODO` → 红 1 条，均已验。** | ✅ |
| P4 远程三份文件的移植声明纠偏（0 个新源码，**纯文档**） | — | ✅ | **B1218 是一批文档纠偏：状态文档里那句“其余 3 个真实缺口共约 1100 行尚未移植”（`RemoteLinearSolver.kt` 339、`RemoteQuadraticSolver.kt` 339、`OspfRemoteModelSerializer.kt` 480）与同段前文“已完成”直接矛盾，也与源码事实不符。****核实结果**：**三份都已存在且无空壳**（C# 侧 243／219／585 行；`NotImplementedException`／`NotSupportedException`／`TODO`／`not ported` 标记计数均为 **0**）；`RemoteConstraintProgrammingClient` 的 `solveOutput`／`materialize` 已移植；`OspfRemoteModelSerializer.ToSerializedVariable` 与身份校验已移植（`ValidateIdentity` 被 7 处引用）。****一句话过期了很久，而它**不可能被任何行为判据发现**：所有测试都绿着，因为代码是对的；错的是对代码的描述。****一条过期的“尚未移植”比一条过期的“已移植”更危险：前者会让下一轮**重做一遍**，而重做时可能把已有的正确实现覆盖成一份新的。****所以本批留下的判据不是“行为对”，而是“描述与文件系统一致”**：`RemotePortingClaimTests` **4 条**——**三个被声明为已移植的文件必须存在且无空壳标记**（3 条 Theory）、**状态文档不再把那三份说成尚未移植**。**仍然成立的一条**：**摘要复算刻意未实现**（基线的摘要是 Kotlin `Json { encodeDefaults = true }` 的 SHA-256，C# 要复算就得逐字节复刻那个序列化，而它尚未对跑验证，故在跨语言实测完成前不作为能力宣称）。****变异：重新引入旧句 → **初版按字符窗口判断，全绿**，改按**行**判断后红 1 条；给文件加 `not ported` 标记 → 红 1 条，均已验。****一次“窗口判据”的教训**：**初版用“往前看 200 个字符里有没有『此前写着』”，一跑就放过了一个真实违规——因为注入点紧接在纠偏记录后面，两百个字符就跨过去了。按行判断的语义是“这一行本身是不是一句声明”，比“距离多近”精确，且不随周围段落长度变化。** | ✅ |
| 指示器与遮盖接入指纹（`nativeFingerprint` 补齐，1 个） | — | ✅ | **B1217 把 B1216 的 `NativeIndicatorData` 与 B1215 的 `NativeMaskingData` 接进 `NativeFingerprint`，补齐基线 schema 阶梯里此前缺席的**九个分支**：`-functions-masking-1/2` 与 `-functions-indicator-2..8`。****指示器阶梯从 `-indicator-8` 逐级降到 `-indicator-2`**：**差值(8) → 合取(7) → 零带(6) → 蕴含(5) → 条件数值(4) → 等价结果(3) → 普通指示(2)。遮盖压在二进制逻辑与指示器之上，有 `definition` 时升到 `-masking-2`。****指示器压在 MAX 之上**：**基线阶梯里 `max-1` 位于指示器之后，所以两者同时非空时由指示器胜出。这一条很容易写反——直觉上“MAX 更复杂”，但阶梯看的是**顺序**不是复杂度。****两个行形状的关键差别**：**`positive-indicator` 用 `gap=`；`zero-band` 用 `inside=` 与 `outside=`，多一个 `side=`，且布尔字段的名字也不同（`positive-on-zero` 对 `outside-on-zero`）。这些字段名**完全不参与计算**，但它们是指纹文本的全部——一个改名的字段会让指纹变化而所有行为判据仍绿。****所以判据用基准哈希钉住两个行形状，而不是逐字段断言文本（后者太长且脆）。****指示器的输入项按**列号**排序而非按键排序**：**指纹描述的是“这个模型”，而列号是模型内部的编号；按键排序会让同一个模型在不同列的排布下给出不同指纹。基线的 `sortedBy { it.first }` 排的正是列号。****两个参数做成可选**（`indicators = null`、`maskings = null`）：**B1214 时它们还不存在，本批接进来但不破坏既有的 26 处调用点；空集合与 `null` 等价。**判据 `NativeFingerprintIndicatorTests` **6 条**：**七级阶梯逐级**、**相邻对叠加（见下）**、**遮盖压过一切且定义升档**、**指示器压过 MAX**、**两个行形状的基准哈希**、**缺列按种类报**、**输入项顺序无关**。**变异：7/8 两档对调 → **初版全绿**，补相邻对算例后红 1 条；MAX 压过指示器 → 红 1 条，均已验。****一次“单特性算例对顺序不敏感”的教训**：**初版阶梯判据每次只打开一个特性，把 7 与 8 对调后七条**全绿**——因为每次只有一个特性打开，谁先谁后都选到同一个分支。必须补“两者同时打开”的相邻对算例才抓得住。这已经是本方向第二次遇到“叠加才测得出顺序”（B1214 的阶梯判据也是这个形状）。** | ✅ |
| 指示结构 `NativeIndicator`（`prepareNativeIndicator` / `prepareIndicator`，1 个） | — | ⚠️ | **B1216 按基线 `NativeIndicator.kt` 的 `prepareNativeIndicator` / `prepareIndicator` 补，含五个数据类（`NativeIndicatorData`、`NativeDifferenceIndicatorData`、`NativeConjoinedIndicatorData`、`NativeZeroBandData`，及 B1215 已补的 `NativeConditionalValueData`）。****核心是一张**保留列集合**，而它必须在递归**之前**完整**：**`resultKeys` 收集结果列、等价结果、零带侧列、合取的两个结果列、差值的两个结果列；然后子条件的 `terms` 不许碰它，本结构的输入也不许碰它。****为什么必须先算再递归**：**子条件的合法性取决于“它有没有碰到本族保留列”，而那个集合只有看完了合取/差值/零带的声明之后才完整——先递归再收集，就会让一个子条件在还不知道自己不许碰哪些列的情况下通过检查。****两套集合的不对称（照抄基线）**：**`conditionalValue.ResultKey` 与 `impliedCondition.ResultKey` **不在** `resultKeys` 里，而它们被**单独**列进主输入的禁碰名单。所以一个变量可以同时是“条件数值结果”与“某一族的等价结果”而不冲突，而它如果是主输入的项就会被拒。****`positiveOnZero` 与蕴含后件不能并存**：**前者让结果 0 激活正分支，后者让前件激活后件——两者对同一个 0 给出相反的约定。****`tolerance <= 0` 与 `bigM <= 0` 都拒**：**非正的阈值让“真分支”无从定义，非正的 M 让 fallback 无从松弛。条件区间缺省时回落到 `±M`。**判据 `NativeIndicatorTests` **9 条**：**基本形状**、**零带严格性归上游（见下）**、**阈值与 M 严格为正**、**输入不许碰五类保留列**、**子条件不许碰保留列**、**`positiveOnZero` 与后件冲突**、**条件数值不能在保留列里**、**条件区间回落与包络越界**、**拒绝的归属记录**。**变异：去掉包络检查 → 红 2 条；**另两个变异（零带严格性、等价结果收集）全绿**——因为它们的行为由上游 `GenerateConstraints` 先拒掉。****第十条“不可测”发现，而且是**惰性重复****：**`IndicatorStructure.GenerateConstraints` 已在更早处检查结果列是二值、零带容差 `< tolerance`、等价结果与输入重合、合取/差值结果与输入重合，而本函数**第一步**就是调用它——所以本层的同名检查全部不可达。本实现照抄基线的重复（基线也有两层）故保留，但判据明确写出**是哪一层**在生效，否则下一个人会以为本层被覆盖了。****本层真正独有的只有五条**：**数值可用性与 `maximumMagnitude`、包络落在条件区间内、子条件递归后的 `terms` 不碰保留列、`positiveOnZero` 与后件冲突、后件常数与阈值的关系。****“同一件事有两道检查”与“两道检查都必要”是两件事。** | ⚠️ |
| 条件数值与门控 `NativeMasking`（`prepareConditionalValue` + `prepareNativeMasking`，1 个，**部分**） | — | ⚠️ | **B1215 按基线 `NativeIndicator.kt` 的 `prepareConditionalValue` 与 `NativeMasking.kt` 的 `prepareNativeMasking` / `prepareMasking` 补。****范围证明是这两格的实质**：**不能说“这个表达式在 `[lower, upper]` 内”，除非把每个变量的真实上下界代进去算出一个包络。****算法是“按系数符号取对应的那一端”**：`actualLower = constant + Σ (coeff > 0 ? coeff×lb : coeff×ub)`，`actualUpper = constant + Σ (coeff > 0 ? coeff×ub : coeff×lb)`。**负系数要把上下界对调——否则一个负系数会把“最坏情形”算成“最好情形”。****判据的关键在于“一个算例分不开两种实现”**：**`x ∈ [1, 10]`、表达式 `-2x`、声明 `[-20, -2]` 时，对调与不对调**都通过**（不对调会得到 `actualLower > actualUpper`，而检查 `actualLower < lower || actualUpper < upper` 两侧都为假，于是误判通过）。必须再加一个**声明收紧**的算例 `[-20, -3]`：正确实现算出 `actualUpper = -2 > -3` 而拒绝，不对调的实现算出 `-20 ≤ -3` 而通过。****包络是**保守的****：**它假设所有变量可以同时取到各自极值，而真实可行域里它们可能被别的约束连在一起。所以“包络在范围内”是充分不必要条件，包络越界时会**误拒**。这是可接受的：拒绝一个能用的模型比接受一个不能用的模型安全。判据把这个“误拒”写成断言（`x, y ∈ [0,10]`、表达式 `x + y`，声明 `[0, 20]` 通过、`[0, 15]` 被拒），而不是把它当成缺陷绕过。****`lower > upper` 被拒**（区间本身是空的）；**结果变量出现在自己的输入里被拒**（`result = result + 1` 无法用包络证明，那样的证明是循环的）；**零系数在返回前被丢掉**（不影响包络，但会让指纹与渲染带上无意义的项）。**门控的定义不能引用 mask 自己或结果列**（那是循环，写成等式会直接不可行）。判据 `NativeMaskingTests` **8 条**：**负系数对调上下界（两个算例）**、**正系数不对调**、**`LowerBound is null` 守卫不可达（并补一条可达的包络越界）**、**结果在自己输入里被拒**、**零系数被丢**、**包络的保守性（误拒）**、**定义不能自引用**、**定义可选**。**变异：系数不对调 → 红 1 条；不丢零系数 → 红 1 条；`null` 守卫改 `continue` → **全绿**（见下，已改为不可达证明）。****第九条“不可测”发现**：**`RealVar` 的 `Range` **总是**带默认上下界（约 `±4.5e15`），所以 `LowerBound is null` 在真实变量上永远不成立——那个 `if` 是一条纯防御分支，靠公开 API 构造不出它的前提。原判据“无界变量被拒”一跑就绿，而变异也绿，原因正是它走的是正常路径。判据改为断言“真实变量的上下界都存在”，把“那行走不到”从推测变成有断言支撑的事实，并补一条真正可达的拒绝路径（包络越界）。** | ⚠️ |
| 全种类指纹 `NativeFingerprint`（`nativeFingerprint`，1 个，**部分**） | — | ⚠️ | **B1214 按基线 `NativePiecewiseReport.kt` 的 `nativeFingerprint` 补，覆盖快路径与 PWL / ABS / MAX / 半连续 / 二值逻辑五类结构行。****缺口（明确记录，不静默丢弃）**：**基线这个函数还接受 `indicators` 与 `maskings`，而 `NativeIndicatorData` / `NativeMaskingData` 在 C# 侧**还不存在**。****那两个类型各自带着一整族嵌套语义**（**`zeroBand` 的侧列、`conjunction`、`difference`、`impliedCondition`、`conditionalValue`、`equivalentResultKeys`，以及 `definition`**），**不是“补两个 record”就够的。本批**不接受**这两个参数，而不是接受之后忽略它们——“接受但忽略”会让指纹看起来覆盖了指示器而实际没有，那是最坏的一种：一个安静的谎。****同理，schema 阶梯里 `-functions-indicator-2..8` 与 `-functions-masking-1/2` 七个分支本批**不存在**：它们的前提在本签名下不可达。七个分支是**缺的**，不是被合并成别的分支。****快路径**：**所有非 PWL 结构都为空时直接交给 `piecewiseFingerprint`，其 schema 是 `{backend}-pwl-1`，而慢路径最低是 `{backend}-abs-1` / `{backend}-pwl-abs-1`——所以同一份 PWL 数据走两条路会得到**不同**的 schema 版本。这不是笔误：快路径说的是“只有 PWL”，慢路径说的是“PWL 加上别的东西”。****慢路径的 PWL 行还多一个 `kind=pwl|` 前缀，所以连行内容都不同；而慢路径的换行是**无条件**的，与 `piecewiseFingerprint` 里那个条件换行相反——两条路径在同一件事上有不同写法，而它们各自自洽。****阶梯的顺序本身就是语义**：**二值逻辑压过半连续，半连续压过 MAX，MAX 压过“只有 ABS”/“PWL+ABS”。所以要测的不是“某一个分支对不对”，而是**相邻两个同时非空时谁赢**——一个只按优先级表写出来的实现，最可能的错是把顺序写反，而单类算例对顺序完全不敏感。****半连续边界必须**严格为正****：**`lower <= 0` 被拒，不是 `< 0`——下界必须为正，否则 `0` 与“未启用”分不开。****MAX 的输入项按列号排序后才渲染，所以项的顺序不影响指纹。**判据 `NativeFingerprintTests` **8 条**：**快路径与手工调 `piecewiseFingerprint` 结果相同**、**阶梯逐对叠加**、**慢路径加 `kind` 前缀**、**慢路径完整文本被基准哈希钉住**、**半连续边界四种情形**、**MAX 项顺序无关**、**缺列按种类报**、**未移植的 schema 分支确实缺席而非被并进别处**。**变异：阶梯顺序对调 → 红 1 条；`<= 0` 改 `< 0` → 红 1 条；慢路径行文本改名 → 初版判据**全绿**，补一条基准哈希判据后红 1 条（见下）。****一次“没抓住”的变异**：**原判据只断言“快路径指纹 ≠ 慢路径指纹”，把 `kind=pwl` 改名后仍全绿——因为两条路径本来就不同，前缀改不改都不影响“不同”。补一条基准哈希判据（`slow.Value` 必须等于一个固定 64 位十六进制串）之后才抓住。指纹本来就该是确定性的，而“确定性”最强的表述就是“它等于这个值”。** | ⚠️ |
| PWL 指纹 `NativePiecewiseFingerprint`（`piecewiseFingerprint` + `numberListText`，1 个） | — | ⚠️ | **B1213 按基线 `NativePiecewiseReport.kt` 的这两格补。****`nativeFingerprint` 还需要 `NativeIndicatorData` 与 `NativeMaskingData`，留到后续批次。****行在拼之前被**排序****：**同一个结构集合无论以什么顺序给出，指纹相同。这不是“顺手排一下”——它是“指纹描述的是**集合**而不是**列表**”这个语义的直接体现；不排序的话，一个仅仅顺序不同的模型会得到不同的指纹，而审计指纹的用途恰恰是“这两个模型是不是同一个”。****空列表**不加换行****：**先 append 上一阶段指纹，再按 `if (structureLines.isNotEmpty())` 决定是否加换行，所以“没有 PWL 结构”时的内容就是上一阶段的指纹本身。****这个差别只影响一个字符，但它决定“无结构”与“结构列表为空”是不是同一个状态——而在这个函数里它们确实是同一个状态，所以**不该**多出那个换行。****后端名只进 `SchemaVersion`，**不进摘要****：**这是判据跑红之后查出来的事实。****`SolveFingerprinting.Sha256` 只哈希内容，`schemaVersion` 只是随返回值带出去的元数据；所以 `gurobi` 与 `cplex` 在同样的结构上给出相同的 `Value`，而 `SchemaVersion` 分别是 `gurobi-pwl-1` / `cplex-pwl-1`。****这不是缺陷，而是分工：“摘要回答模型是否相同，schema 版本回答是谁算的”。它决定“怎么比较两个指纹”——只比 `Value` 的调用方会认为两个后端的指纹相同，而它们在“同一个模型”这个意义上确实相同。判据把两件事分开断言。****渲染与区域设置无关**：**`"R"` 格式配 `InvariantCulture`；判据在 `de-DE` 下跑一遍并断言与不变量文化相同**（**在小数点用逗号的文化下，渲染出的列表会与逗号分隔符混淆，指纹就随机器而变**）。**非有限值被拒，且消息点名是第几个**：**`NaN`/无穷会让“同一个模型”的指纹取决于它被创建时的具体数值，而那是不可复现的——而指纹的全部意义就是可复现。****本实现**不**声称与 Kotlin 的指纹字节一致**：**`Double.toString()`（Kotlin）与 `"R"` 格式（.NET）两边都是“最短可往返表示”，但指数写法与末尾零的处理**不保证**逐字符相同。****所以“同一个模型在两侧得到同一个指纹”这件事**没有被测量过**，而本方向的原则是不宣称未实测的跨语言互通。判据只断言“本实现自洽”，并用一条**反声明**把这一点写进测试里，防止将来有人把它当成既成事实。**判据 `NativePiecewiseFingerprintTests` **9 条**：**顺序不影响指纹**、**空列表哈希裸指纹**、**不同内容给出不同指纹**、**上一阶段指纹参与哈希**、**非有限值被拒并点名下标**、**渲染与文化无关**、**缺列两个方向**、**后端名只进 schema 版本**、**自洽但不声称跨语言**。**变异：不排序 → 红 1 条；无条件加换行 → 红 1 条；去掉有限性检查 → 红 1 条，均已验。****一次真实的判据错误**：**初版判据写“不同后端给出不同指纹”，一跑就红——因为它假设了 `schemaVersion` 参与哈希。这个假设是错的，已按实测事实改写。** | ⚠️ |
| PWL 求值与 ABS 列校验 `NativePiecewiseEvaluation`（`piecewiseEvaluations` + `validateNativeAbsColumns`，1 个） | — | ✅ | **B1212 按基线 `NativePiecewiseReport.kt` 的这两个函数补。****插值用的是**夹紧之后**的输入**：**`selectSegment` 在端点容差内把输入夹到边界，并把夹紧后的值放进 `SegmentSelection.Input`；`piecewiseEvaluations` 用的是那个值。****所以一个比定义域下界小 `5e-7` 的输入，插值是在**边界上**做的。****这与“报告值就是求解器值”一致：求解器在边界上满足了约束，报告就该在边界上评它；拿原始输入去插值，会得到一个求解器从未见过的点。****判据用一个斜率非零的算例让差别显形**（**段 `[0, 10]`、值 `[0, 20]`、输入 `-5e-7`：夹紧后给出 `0`，用原始输入会给出 `-1e-6`**），**并且直接比较 `Lhs` 而不靠 `Satisfied`——因为两者只差一个容差量级。****容差的来源在这里是**第三种****：**`absEvaluations` 与 `piecewiseEvaluations` 同源（`finiteSolverDouble(PIECEWISE_TOLERANCE, …)`），`maxEvaluations` 用字面量 `1e-6`。三个求值器里两个同源、一个不同源——照抄并记录这处分歧。****分母可以是零**：**`fraction` 除以 `upper - lower`。****`prepareNativePiecewise` 会拒绝非严格递增的断点，但 `NormalizedPiecewise` 可以被直接构造——那时这一层是唯一会拦的地方，而它拦的方式是“插值结果非有限”。****值列表比段数短时被拒**：**同样只在直接构造时可达，而“越界”比“插出个错值”更容易查。****`validateNativeAbsColumns` 与 B1209 的“解里不该有选择列”是同一件事的两个层次**：**那里查**值**，这里查**列**；一条在解投影之后，一条在列映射阶段——两处都需要，不能互相替代。****输入与结果必须**在**、三种辅助列必须**不在**，而三种辅助列各测一次**（**只测一种的话另外两条分支可以整段缺失**）。判据 `NativePiecewiseEvaluationTests` **7 条**：**段内插值**、**插值公式用差值**（**中点给出两端平均，区分“插值”与“加权和”**）、**用夹紧后的输入**、**值太少被拒**、**ABS 列校验两个方向加三种辅助列**、**缺 token 按类报**、**空结构列表**。**变异：用原始输入插值 → 红 1 条；插值公式漏减 `lowerValue` → 红 2 条；去掉辅助列检查 → 红 1 条，均已验。****一处重复踩的坑**：**`Math.Max` 又被 `Fuookami.Ospf.Math` 遮蔽，需全限定 `global::System.Math.Max`。** | ✅ |
| 约束求值 `NativeConstraintEvaluations`（`absEvaluations` + `maxEvaluations`，1 个） | — | ⚠️ | **B1211 按基线 `NativePiecewiseReport.kt` 的两个求值器补。两个求值器之间有两处不对称，都照抄基线并在判据里钉住。****第一处：容差的来源不同**：**ABS 走 `finiteSolverDouble(PIECEWISE_TOLERANCE, ...)` 转换之后的值，MAX 用字面量 `1e-6`。数值上相同，结构上不同——`PIECEWISE_TOLERANCE` 一旦改动，ABS 跟着走，MAX 不跟。****本批的第 8 条“不可测”发现**：**这条差别**当前测不出来**，因为两者数值相同；变异验证：把 ABS 也改成字面量，7 条判据全绿。判据只能断言“两者当前相等”，并在注释里写清“这是巧合而不是同源”。****第二处，也是更要紧的一处：`maxEvaluations` **忽略 `structure.minimum`****：**它永远取 `maxOrNull()`，所以一个 `minimum = true` 的结构会被拿去和**最大值**比。****这不是本仓库的偏差，是**基线缺陷****：**`NativeMaxData` 带着 `minimum`，`restoreValues` 也按它取极值，而 `maxEvaluations` 却没看这个标志——同一份数据在两条路径上被解释成两种含义。****本批照抄以保持与基线的可比性，并把这一点写成判据。一个被记录下来的缺陷是“已知债”；一个没人测过的缺陷是“潜伏的错”。****这条判据有一个反直觉的性质：如果哪天有人“修正”了它（让求值器看 `minimum`），判据会变红。****变异验证：把 `minimum` 纳入求值的“修正版”让 2 条判据变红。****这正是记录缺陷的判据应有的行为——它不是“实现正确”的证据，而是“实现与基线一致”的证据。修正时必须同时改判据，那一步是**有意的**，而不是被红条逼出来的。****容差边界是 `<=` 而不是 `<`**：**判据用 `violation` 恰为 `1e-6` 与 `1e-6 + 1e-9` 这一对，只测 0 与一个大值的话两者区分不开。**判据 `NativeConstraintEvaluationsTests` **7 条**：**ABS 求值形状**、**容差边界含等号**、**MAX 忽略 `minimum`（期望 `10`、结果 `3`、违反量 `7`、判为不满足）**、**两条路径对同一标志的两种解释并排断言**、**两处容差当前相等（记录巧合）**、**缺 token 按类分别报**、**空结构列表**。**变异：`<=` 改 `<` → 红 1 条；“修正” `minimum` → 红 2 条；ABS 容差改字面量 → **全绿**（**如实记录，未声称覆盖**）。****一处 C# 细节**：**`ConstraintEvaluation<V>` 的字段名是 `ConstraintId` / `Relation` / `Satisfied`，关系枚举叫 `SolveConstraintRelation`（不是 `ConstraintRelation`）。** | ⚠️ |
| 值还原 `NativeValueRestoration`（`restoreValues` + `maxInputValues`，1 个） | — | ✅ | **B1210 按基线 `NativePiecewiseReport.kt` 的 `restoreValues` 与 `maxInputValues` 补。****五路查找的**顺序就是语义****：`nativeValues[key] ?? selectors[key] ?? absHelpers[key] ?? maxHelpers[key] ?? semiHelpers[key]`。**`??` 问的是“这个键**存在**吗”，不是“它的值是真值吗”——所以一个值为 `Flt64.zero` 的键照样命中。而“第一个命中就赢”意味着同一个键出现在两路里时，**前一路静默胜出**。****这不是理论问题**：**B1209 发现 `absHelperValues` **不**拒绝“解里含有辅助列”，所以一个解里带着陈旧的 `positiveKey`，`nativeValues` 会赢——那个陈旧的值会原样进到还原结果里，而重建出来的正确值被丢掉。****选择列没有这个问题，因为 `selectorValues` 会先把“解里含选择列”拒掉。所以同一份代码里一类辅助列是安全的、另一类不是，差别来自上一层的一道检查。****“顺序即语义”的分量在于：某一层的宽松会被下一层的顺序放大成错值。**判据把两者并排断言：**同一个陈旧的辅助值，作为 ABS 辅助列时胜出（`999` 而非重建的 `5`），作为选择列时被拒绝。****还原结果按 `originalTokens` 的顺序排列**，所以它可以逐位贴回调用方的 token 列表。**拼错的顺序不会报错，只会让每个变量的值贴到别的变量上——所以这一步必须按 token 走，而不是按字典的迭代顺序。判据用“列顺序与 token 顺序相反”的算例钉住它。****MAX 选择列按最大值重建，并列时取**第一个****（**基线的 `indexOf(maxOrNull())` 语义**）：**并列时选哪一支不影响目标值，但它影响求解器看到的选择变量值，而那是可复现性的一部分。****半连续指示列按“结果是否够到下界”重建**：**`>=` 而不是 `>`，所以恰好等于下界也算够到。****找不到值的 token 被**点名****（**消息里带 `identifier:index`**）：**不说知道是哪个 token，调用方还得自己去找。**判据 `NativeValueRestorationTests` **8 条**：**结果按 token 顺序**、**陈旧 ABS 辅助列胜出**（**与选择列被拒并排**）、**辅助列能被重建贴回**、**MAX 选择列取极值与并列取第一**、**半连续指示列用 `>=`**、**缺值 token 被点名**、**`MaxInputValues` 拒缺项与非有限**、**空 token 列表**。**变异：不查 ABS 辅助列 → 红 1 条；并列取最后一个 → 红 1 条；ABS 辅助列挪到 `nativeValues` 之前 → 红 1 条，均已验。****一处删除后的空行清理**：**`maxHelpers` 赋值处留了一句变异注释残留，已删除。** | ✅ |
| 辅助列重建 `NativeHelperReconstruction`（`selectorValues` + `absHelperValues`，1 个） | — | ✅ | **B1209 按基线 `NativePiecewiseReport.kt` 的 `selectorValues` 与 `absHelperValues` 补。****两个函数做的事看起来一样：从解里算出那些**不在解里**的列。但它们的严格程度差得很远，而那个差别有理由。**| | `selectorValues` | `absHelperValues` || --- | --- | --- || **解里出现了辅助列** | **拒绝** | **不检查** || **重键冲突** | **检查** | **不检查** |**为什么选择列要拒绝“解里含有它”**：**选择列在建模时被消掉了，一个“含选择列的解”意味着调用方给的是**消元之前**的向量；那时的选择列值与这里重建出来的可能不一致，而同一个量有两个来源是最难查的一类错。****为什么 ABS 不检查这一条**：**ABS 的辅助列同样是消掉的，但基线没写这条检查；单方面加上会让两侧行为分岔。本批照抄，并把它记为一处**已知的不对称**。****为什么 ABS 不需要冲突检查**：**跨结构的辅助键唯一性已由 `NativeCombinedKeyValidator` 保证。而 `selectorValues` 的冲突检查在正常流水线里**也**触发不到——跨结构的选择键同样被那一层拒掉了；它是一道**防御性**检查，只有在直接调用这个函数时才可能命中。****“在正常流水线里触发不到”不等于“不用测”**：**这个函数是一个公开入口，而它的契约是“相同的键给相同的值”。判据直接构造那个情形。****零点落在非负那一侧**：**`input < 0` 取 `0`，否则取 `1`，所以 `input == 0` 时符号是 `1`，而正部是 `0`。** **三者满足 `input = positive - negative` 与 `|input| = positive + negative`，彼此自洽。****如果符号在零点取 `0`，这套关系**仍然**成立——所以它不是靠等式推出来的，而是**约定的**；约定必须与建模侧的 `sign` 列一致，否则求解器的 `sign` 与报告重建的会差一。**判据 `NativeHelperReconstructionTests` **9 条**：**选中的段在选择列上是 1**、**解里含选择列被拒**、**缺输入与缺结果的消息区分得开（两个函数各两例）**、**选择键冲突被报出**、**ABS 正负部按符号切分**、**零点符号为 `1`**、**ABS 不拒绝解里含辅助列（并覆盖旧值）**、**空结构列表给出空结果**。**变异：零点取 `0` → 红 1 条；去掉“解里不该有选择列” → 红 1 条；去掉冲突检查 → 红 1 条，均已验。****一处 IDE 规则**：**单语句方法体要求写成表达式主体（`IDE0022`）。** | ✅ |
| 列对齐 `NativeColumnProjection`（`nativeColumns`，1 个） | — | ✅ | **B1208 按基线 `NativePiecewiseReport.kt` 的 `nativeColumns` 补。****基线的签名是 `nativeColumns(model: LinearTriadModel)`，但函数体只用到 `model.variables` 与`model.tokensInSolver`，所以这里**不**绑定具体模型类型，而是接收这两个列表**：**这不只是“更方便”——它让这一格可以被独立测，而不是必须造一个完整模型。****（B1207 曾判断这一格需要先补视图层，理由是 `ModelViewVariable` 没有 `Origin`；实测它**有** `Origin`，那个判断是错的，本批已更正。）****三道检查，顺序有理由**：**① 变量数 == token 数**（**先确认两个列表可以逐位比较，否则后面全是越界**）；**② token 键互不相同**（**键要当字典的键用，重复会让后写的覆盖先写的，于是列号悄悄少一个**）；**③ `origin` 非空时必须等于 token 键**（**只比“有的那些”**）。****第 3 条是“`origin != null &amp;&amp; origin.key != key` 即拒”，而不是“`origin` 必须存在”**：**手工构造的模型没有 origin，那是**合法**情形，不是错误——把它当成错误会让所有手工模型都无法走 native 路径，而那正是本方向大量判据的构造方式。****第 3 条抓的是“变量列与 token 列错位”**：**两列长度相同、键各自唯一，但顺序不一样——那么按列号取到的值属于另一个变量。这种错位不会让任何一步报错，只会让报告里的数字张冠李戴；它是静默的，所以必须显式抓。****列号取的是**列表下标**，不是 token 自己的 `SolverIndex`**：**基线的 `keys.withIndex()` 用的是列表下标。判据构造 `SolverIndex` 为 `5`/`9` 而列表位置为 `0`/`1` 的输入，正确实现给 `0`/`1`。**判据 `NativeColumnProjectionTests` **7 条**：**对齐正确给出列号**、**缺 `origin` 合法**、**列错位被拒**、**数量检查在先**（**用一个同时“数量不符”且“键重复”的输入区分顺序**）、**token 键重复被拒**、**列号来自列表顺序而非 `SolverIndex`**、**空输入通过**。**变异：跳过错位检查 → 红 1 条；去掉重复检查 → 红 1 条；用 `SolverIndex` → 红 1 条，均已验。****一处 C# 细节**：**`Token<V>` 的泛型参数是 `Flt64` 而不是某个“Flt64Type”**；**`Token` 的构造函数要一个 `Dictionary<object, Action<bool>>` 作刷新回调表。** | ✅ |
| 解向量投影 `NativeSolutionProjection`（`valuesByKey`，1 个） | — | ◐ | **B1207 按基线 `NativePiecewiseReport.kt` 的 `valuesByKey` 与 `finiteSolverDouble` 补。`nativeColumns` 依赖 `LinearTriadModel` 的 `origin` 与 token 概念，而 C# 侧的 `ModelViewVariable` 没有 `Origin`，所以那一格留到后续批次。****这个函数最重要的一点：数值检查是一道**闸门**，不是一次**转换**。****实现里调用了 `finiteSolverDouble(value, ...)`，但**丢掉了它的返回值**，存进结果的是**原来那个 `Flt64`**。****这个区别在 `SolveValueConversionPolicy.AllowRounding` 下会显形：转换会四舍五入到 15 位，而存下来的没有。****这不是一个 bug——报告层本来就该报求解器给的数，而不是把它磨一遍再报。但它确实意味着“检查通过”与“结果被转换过”是两句话，把它们混成一句会让“报告值 == 求解器值”这个不变量悄悄消失。****判据用一个四舍五入会变形的值（`0.1 + 0.2` 的双精度表示）在 `AllowRounding` 下跑一遍，断言拿到的是原来那个数；并先断言 `Math.Round(raw, 15) != raw`，否则这条判据区分不了“舍了”与“没舍”。****两道检查不能互相替代**：**长度检查在前（`values.Count != columns.Count`），列号检查在后。长度对上之后列号仍可能越界——两个键、两个值、列号 `0` 与 `7`。****一处 C# 与 Kotlin 的真实差异**：**Kotlin 的 `getOrNull` 对越界返回 `null`，C# 的索引器对**负**下标直接抛 `ArgumentOutOfRangeException`。****所以 `< 0` 这一半在 C# 里是**必需**的，而不是防御性冗余——变异验证：去掉它，判据以异常而不是 `Failed` 变红。****一处有意的差异**：**基线用 `LinkedHashMap` 保留插入顺序，C# 侧用普通字典；下游全部按键查值，没有一处依赖顺序，所以没有行为后果——但它是**有意的**，不是忘了。**判据 `NativeSolutionProjectionTests` **6 条**：**值按键摊回（列号任意排列）**、**非有限值被拒（`∞` 与 `NaN`）**、**存的是原始值而非四舍五入后的**、**长度不匹配先报**、**列号越界被拒且消息给出列号（正负各一例）**、**空输入通过**。**变异：存转换后的 `double` → 红 1 条；去掉长度检查 → 红 1 条；去掉负列号判断 → 红 1 条（以异常形式），均已验。** | ◐ |
| 跨结构键校验 `NativeCombinedKeyValidator`（`validateCombinedStructureKeys` + `keyText`，1 个） | — | ✅ | **B1206 按基线 `NativePiecewiseReport.kt` 的 `validateCombinedStructureKeys` 与 `keyText` 补。****这是唯一一处同时看 PWL、ABS、MAX 三类结构的检查，而它做的是**两件不同的事****：**登记**（**哪些列被“输出或辅助”占用了，且只能被占用一次**）与**引用**（**有没有哪个结构的**输入**读了那些会被消掉的辅助列**）。**登记的范围是“结果列 + 辅助列”，**不含输入列****：**所以三个结构共用同一个输入列是合法的——那正是“同一个量的三个不同函数”这个正常情形。****引用检查的范围是“被省略的辅助列”**：**PWL 选择列、MAX 选择列、ABS 的正部/负部/符号列。这些列在建模后**会被消掉**（它们的值由其他列决定），所以任何拿它们当输入的结构都会在消元之后留下一个悬空引用。****而 PWL 与 ABS 的**结果**列不在这个集合里**：**结果列是真实输出，不会被消掉，所以拿别人的结果当自己的输入是合法的——这正是“链式函数”这个正常情形。****`owners` 是**一个**字典，三类共用**：**所以“PWL 的选择列正好是 MAX 的结果列”这种跨类冲突也会被发现。****失败消息只给出**重复的那个键**，不给出**谁先占用了它**——这是一个真实的诊断缺口。****而 `owners` 字典里本来就有两个占用者的标签：第一个已经存在，第二个就在手上。****本批照抄基线不改消息格式，但把“消息缺什么”断言成判据，让它成为一个被记录的事实。****`keyText` 是 `identifier:index` 两段**：**只给 `identifier` 的话，一个组合变量的多个维度会打印成同一个字符串。**判据 `NativeCombinedKeyValidatorTests` **9 条**：**三类互不相干通过**、**结果列跨结构重复被拒**、**共用输入列合法（含**两个 PWL 共用**这一必需算例）**、**读别人结果合法但读别人辅助列被拒**、**三类结构各自的“读辅助列”都能被报出（三条引用检查各测一次）**、**跨类键重复被拒**、**消息只给键不给占用者**、**`KeyText` 两段**、**空输入通过**。**变异：正部列不算辅助列 → 红 1 条；MAX 选择列不算辅助列 → 红 1 条；把输入列也登记进 `owners` → **首次全绿**（**算例里只有一个 PWL 会登记输入列，撞不上**），补“两个 PWL 共用输入”算例后红，均已验。****一处 C# 坑**：**`UInt64` 在 `Fuookami.Ospf.Math.Algebra.Number` 与 `System` 之间歧义，需全限定。** | ✅ |
| 原生 MAX primitive `NativeMax` + `NativeMaxData` / `NativeMaxInputData`（`prepareNativeMax`，3 个） | — | ✅ | **B1205 按基线 `solver/NativeMax.kt`（162 行）的 `prepareNativeMax` 补。****这一层最要紧的一条是 Big-M 的**覆盖**检查**：`required = max(所有输入的 upperBound) - 本输入的 lowerBound`，**也就是“本输入可以比全局最大值低多少”。****未选中的那支要求 `result <= input + M`，而 `result` 至少要够到全局最大值 `maxUpper`，所以 `M` 必须补上 `maxUpper - lower` 这段距离。****`bigM < required` 即拒——所以 `bigM == required` **刚好**通过。****这个边界与 `NativeAbs` 的严格上界方向相反**：**那里“恰好等于上限”没有余量，这里“恰好等于需求量”是够用的（没有别的项会再吃掉这段余量）。****判据必须测 `10` 与 `9.99` 这一对**：**只测 `100` 通过的话，把 `<` 写成 `<=` 或干脆不查都发现不了。****Big-M 还必须 `> 0`**：**`0` 在这里没有意义——一个恒定输入的 `required` 为 `maxUpper - lower`，只在它自己就是最大值时才是 0，而那时它不需要 fallback。****输入里不能出现自己的结果列或选择列**：**一个“最大值函数引用了自己的结果”的结构会写出`result >= result` 这样的行，形式上满足、什么都不约束。****同一变量的多个单项会被**合并**，然后**丢掉零系数项****：**所以 `x - x` 合并成零之后既不进 `Terms`，也不参与上下界累加——它不会因为 `x` 缺界而报错，因为它根本不贡献。****负系数把上下界**翻转****：**`-x` 在 `x ∈ [2, 7]` 上的范围是 `[-7, -2]`。若写成“下界永远用变量的下界”，会得到 `lower = -2`、`upper = -7`——一个不可能的区间。判据的两个界都非零，所以“忘了翻转”与“翻转了”给出完全不同的数。****`minimum` 标志被原样带出**：**primitive 层不区分 max 与 min，只把这个标志转交出去**（**Big-M 的覆盖要求与方向无关**）。判据 `NativeMaxTests` **9 条**：**合格结构转换**、**覆盖边界（`10` 通过 / `9.99` 拒 / `100` 通过）**、**Big-M 必须为正**、**输入不能自引用（结果列与选择列各一例）**、**负系数翻转上下界**、**同变量单项合并**、**合并成零的项被丢弃**、**数量与键检查（三种）**、**`minimum` 标志透传**。**变异：覆盖边界改成 `<=` → 红 1 条；负系数不翻转 → 红 1 条；去掉自引用检查 → 红 1 条，均已验。****一处仓库事实**：**没有 `BoundedReal` 之类的便捷类型，界是通过 `Range.Geq` / `Range.Leq` 设的**；**本批判据全靠真实的界，所以造了一个 `Bounded` 工厂——这是必需的而不是方便的。** | ✅ |
| 归一化 `NativePrimitiveNormalizer` + `NormalizedPiecewise` / `NormalizedAbs`（`normalizePiecewiseStructures` + `normalizeAbsStructures`，3 个） | — | ◐ | **B1204 按基线 `NativePiecewiseReport.kt` 的两个 normalizer 补。`validateCombinedStructureKeys` 依赖 `NativeMaxData`，留到后续批次。****两层唯一性检查管的不是同一件事**：**primitive 层查“一个结构内部各键互不相同”，归一化层查“结果键在**整个列表**里唯一”。两者都不能省——一个结构自己没问题，仍然可能和另一个结构抢同一列。****而“输入列”**不在**跨结构唯一性里**：**PWL 只登记 `resultKey`，ABS 只登记`resultKey`/`positive`/`negative`/`sign` 四个。****所以两个 ABS 共用同一输入列是**允许**的——`abs(x)` 可以合法地出现两次（结果列不同），这是真实需求：同一个量的绝对值可能被两个维度分别引用。****PWL 的选择变量数量检查是**有条件的**（`selectorKeys.isNotEmpty() &amp;&amp; ...`），所以**零个选择变量能通过归一化**；而 B1199 的行生成里 `SelectorVariables.Count != segments` 直接拒绝。****两层严格程度不同，而宽松的那一层在前**——**这不是矛盾（归一化只保证“数据形状可用”，行生成才保证“能建出约束”），但它意味着一个零选择的快照会走到很后面才被拒。本批照抄基线，并把这件事写成判据，让它成为一个被记录的事实。****primitive 层的错误被原样转发**：**一个“断点非递增”的结构最终拿到的仍是`breakpoints must be strictly increasing`，而不是笼统的“归一化失败”——丢掉下层消息等于把排查起点往上挪了一层。****ABS 侧的结构内五键唯一检查与 `NativeAbs.Prepare` 重复**：**两者用同一个判据，谁也拦不住对方拦不住的；本批照抄基线保留，并标注它是**无害但惰性**的重复。**判据 `NativePrimitiveNormalizerTests` **8 条**：**合格列表被归一化**、**零选择通过归一化但行生成拒绝**（**两层实际行为并排断言**）、**选择数量不符被拒**、**同结构内选择重复被拒**、**跨结构结果键重复被拒**、**共用输入列允许但共用结果列不允许**、**primitive 错误原样转发**、**空列表成功**。**变异：去掉“非空”前置条件 → 红 1 条；不查跨结构结果键 → 红 1 条；把输入列也登记进跨结构唯一性 → 红 1 条，均已验。** | ◐ |
| 原生 PWL primitive `NativePiecewise` + `NativePiecewiseData`（`prepareNativePiecewise`，2 个） | — | ✅ | **B1203 按基线 `solver/NativePiecewise.kt` 的 `NativePiecewiseData` 与 `prepareNativePiecewise` 补。****这一层**不**碰求解器边界与 fallback 选择变量的使用情况**（**基线注释明说“有意不处理”**），**只回答一个问题：这个结构能不能被翻译成一组 `x/y` 断点。****断点必须**严格**递增：`x[i-1] >= x[i]` 即拒**（**相等也拒——两个断点重合会造出零宽分段，而零宽分段在 `selectSegment` 的查找里会让同一段既含左端又含右端**）。**`y` 点由每段的两个端点算出，并顺带检查**连续性****：**第 `i` 段在 `x[i]` 处的值必须等于 `yPoints[i]`。****所以一个“断点处跳变”的分段线性会被拒——即使每一段自己都是合法的直线**：**native primitive 只吃一组 `(x_i, y_i)` 点，没有“每段一个截距”的概念，不连续的分段线性在这种表示下**无法表达**，所以必须在转换时拒绝，而不是被静默压成一条折线——后者会让“两段在断点处差 3”这个事实消失。****`maximumMagnitude` 用 `abs(value) < maximumMagnitude`，即严格上界**（**与 `NativeAbs` 一致**），**`NaN` 被单独拒**。**与 B1202 的对照被写成了判据**：**同一份乱序断点在 primitive 层得到 `breakpoints must be strictly increasing`（准确，指向结构），在报告层却得到 `outside its domain`**（**因为 `upper` 取**最后一个**断点，对 `[0, 10, 5]` 来说上界被当成 `5`，于是 `7` 被判越界——尽管第一段 `[0, 10]` 实际覆盖它**）。**后者比“死分支”更值得记：它不是死代码，而是一条会真的发出来、并且把人引向错误方向的消息。****我第一版判据断言的是 `no valid segment`，实测拿到 `outside its domain`；改判据前先判断是判据错还是实现错——这里判据错，实现与基线一致。**判据 `NativePiecewiseTests` **9 条**：**`y` 点来自段端点**、**首项非零必须被真的写入**、**断点严格递增（逆序与相等各一例）**、**不连续被拒**、**输入必须是单位裸变量（三种偏离）**、**数量不匹配（三种）**、**缺转换器被拒且前缀区分得开**、**`maximumMagnitude` 严格且拒 `NaN`**、**两层消息质量对照**。**变异：相等放行 → 红 1 条；关掉连续性检查 → 红 1 条；不写 `yPoints[0]` → **首次全绿**（**算例首项恰为 0，与数组默认值相同**），补非零首项算例后红，均已验。****一处已知坑**：**`UnivariateLinearPiecewiseStructure` 没有 `InputVariableKeyOrNull`（那是 `AbsStructure` 上的），裸变量判定要就地写。** | ✅ |
| 分段选择 `NativePiecewiseReport.SelectSegment` + `SegmentSelection`（`selectSegment`，2 个） | — | ◐ | **B1202 按基线 `solver/NativePiecewiseReport.kt` 的 `selectSegment`、`finiteSolverDouble(s)` 与 `PIECEWISE_TOLERANCE` 补。`NativePiecewiseReport.kt` 共 1225 行，本批只补**分段选择**这一格（`absEvaluations`、`maxEvaluations`、指纹与列校验等仍在后面的批次）。****容差只在定义域的两个**端点**起作用，内部断点没有容差**：**`value < lower` 时若差额不超过 `1e-6`，值被**夹到** `lower`；内部断点不在这个机制里——稍微越过内部断点的点直接落进相邻那段，既不拒绝也不夹紧。****返回的是夹紧之后的值**：**比下界小 `1e-9` 的输入报回来恰好等于下界。如果调用方拿返回值去算，它算的是一个**没被要求过**的数。****内部断点归**前**一段**：**查找是“第一个满足 `bp[i] &lt;= value &lt;= bp[i+1]` 的 `i`”，两端闭合。这个约定必须有，因为分段线性在断点处连续当且仅当两段在该点值相等；不相等时该点有两个“正确答案”，而报告必须**确定**地给出一个。****断点数检查排在数值检查之前**：**“结构是否成立”与“这个数能不能用”是两个问题，前者在前——一个只有一个断点的结构无论输入多合法都产不出分段。判据用 `NaN` 输入配单断点来区分顺序。****第七条“不可测”发现，而这一条能被**证明****：**`no valid segment` 这一支**不可达**。****论证**：**端点夹紧之后总有 `b_0 &lt;= v &lt;= b_n`。取最小的 `i` 使 `b_i &gt;= v`（`b_n &gt;= v` 保证存在）。若 `i &gt;= 1`，由最小性 `b_{i-1} &lt; v`，于是第 `i-1` 段必然含 `v`；若 `i == 0`，则 `v == b_0`，此时要后面所有段都漏需 `b_j &lt; v` 对全部 `j &gt;= 1` 成立，特别地 `b_n &lt; v`，与 `v &lt;= b_n` 矛盾。****基线保留这段死代码，本仓库照抄；判据不改它，而是**把不可达本身记下来**并抽样验证。一个没有判据的死代码下次读代码时会被当成“还有情形没处理”。****一处已知的消息误导**：**`selectSegment` 不检查断点单调性，所以乱序断点会落到 `no valid segment`（看起来像输入的错）而不是“断点无序”（结构的错）。本批照抄基线不加校验——单方面加一条基线没有的校验会让两侧行为分岔。**判据 `NativePiecewiseReportTests` **7 条**：**段内输入选对段**、**内部断点归前一段**、**端点容差夹紧并返回夹紧值**、**容差不作用于内部**（**`5.0 + 1e-9` 的 `Input` 不被夹成 `5.0`**）、**断点数检查在前**、**`no valid segment` 不可达**、**断点处选择确定**。**变异：上端改开区间 → 红 2 条；容差内不夹紧 → 红 1 条；容差错误延伸到内部断点 → 红 1 条，均已验。****一处 XML 注释坑**：**文档注释里的裸 `<` 会触发 `CS1570`，必须写成 `&lt;`**（**成对的 `**` 除外**）。 | ◐ |
| 原生绝对值 `NativeAbs` + `NativeAbsData`（`prepareNativeAbs`，2 个） | — | ✅ | **B1201 按基线 `solver/NativeAbs.kt` 的 `NativeAbsData` 与 `prepareNativeAbs` 补。****`maximumMagnitude` 是**严格**上界：`|M| >= maximumMagnitude` 被拒，而不是 `> maximumMagnitude`**：**刚好等于 SDK 上限的值没有留出任何余量，求解器加上别的项之后就会溢出。****判据用 `max = 100` 与 `M = 100` 这一对边界**（**恰好相等被拒、`99.9` 通过**），**只测一侧的话 `>` 与 `>=` 就区分不开。****`maximumMagnitude` 自身也要验：`0`、负数、`NaN` 都被拒**：**`NaN` 那一支最要紧——`|M| >= NaN` 恒为假，不单独拒绝的话一个 `NaN` 上限会让**所有** Big-M 都通过校验。****键的唯一性包含输入列**：**结果列与输入是同一列时方程仍然有解，只是那个解不是 `abs`。****输入必须是 `1·x + 0`，三种偏离各一例**（**系数不是 1、常数不是 0、不是单项**）。**数值层复用 `AbsStructure.InputVariableKeyOrNull`。****第六批“不可测”发现**：**求解器层的复检（“转换之后系数与常数还是 1 与 0”）在本仓库**当前不可测****——**仓库里只有一个 `IIntoValue<Flt64>` 实现（内部的恒等转换器），两层永远一致，构造不出“两层不一致”的输入。****我最初把它写成 `one` 的往返，那在任何转换器下都等于 `1.0`——一个恒等式，什么都检查不到。改成读实际值之后逻辑是对的，但判据仍然打不到；变异验证证实：退回成往返，八条判据全绿。****所以本批**不声称**这个分支被覆盖。保留它的理由：`IIntoValue` 是公开注入点，未来会出现缩放转换器，那时这一层必须已经在按求解器的数值验。**判据 `NativeAbsTests` **8 条**：**合格结构转换出五个字段**、**严格上界**、**`maximumMagnitude` 自身校验**、**输入必须是单位裸变量**、**键唯一性含输入**、**两个 Big-M 的边界**、**失败消息带 `invalid native ABS data:` 前缀**、**求解器层复检不可测**（**把不可测本身写成判据**）。**变异：严格上界放宽成 `>` → 红 1 条；不单独拒绝 `NaN` → 红 1 条；求解器层退回成往返 → **全绿**（**如实记录，未声称覆盖**），均已验。****一处 IDE 规则细节**：**`new T(...)` 用 `var`（IDE0007），其余用显式类型（IDE0008）——两条规则方向相反。** | ✅ |
| 事务化驱动 `IDeferredFunctionFallbackTarget` + `DeferredFunctionFallbackDriver`（2 个） | — | ✅ | **B1200 按基线 `DeferredFunctionFallback.kt` 的 `materializeDeferredFunctionFallbacks` 与 `rollbackErrors` 补。****整个循环是一个事务：任一结构失败就回滚到**开始时的行数**，而不是回滚一步。****回滚目标是 `checkpoint`（循环开始前的行数），不是“上一个结构之前”**：**差别在于已经成功的那些结构也要被撤掉——否则调用方拿到一个“部分成功”的模型层，而它既不是全有也不是全无。****失败有三种结局，而第三种最容易被忽略**：**物化/追加失败且回滚成功 → `Failed`（只带原始错误）；物化/追加失败且回滚**也**失败 → `Fatal`（带**两个**错误）；全部成功 → `Ok`（每个结构一个区域）。****第二种从 `Failed` 升级成 `Fatal` 的理由**：**`Failed` 的语义是“可以再试一次”，而回滚失败意味着模型层停在未知状态——再试一次不会更糟，但也不会更好。****两个错误都要留**：**原始错误说明“为什么开始回滚”，回滚错误说明“为什么现在回不去”；只留一个，下一次排查就得靠猜。****`Append` 与 `Rollback` 都返回“可能失败”而不是 `void`**：**目标可能是一个已经写完的模型层（拒绝追加）、也可能是一个固定容量的缓冲区（溢出）。****起点必须在追加**之前**记录**：**顺序反过来的话区域会指到上一个结构去。**判据 `DeferredFunctionFallbackDriverTests` **5 条**：**失败时回滚到开始而非一步**（**记录 `Rollback` 实参，断言是起始值 5 而非中间值**）、**回滚失败升级成 `Fatal` 且两个错误都在**、**全部成功时区域连续**（**后一个的起点等于前一个的末端，同时验证了起点记在追加之前与行数是实际值**）、**追加失败也回滚**、**空列表是成功的空事务**（**不追加、不回滚**）。**变异：回滚到“当前”行数 → 红 1 条；回滚失败降级成 `Failed` → 红 1 条；`first` 记在追加之后 → 红 1 条，均已验。****一处已知坑**：**`Result<T, C, E>` 自带 `Select` 扩展，遮蔽了 `IEnumerable.Select`——用显式循环。****一处我自己的脚本错**：**判据的假物化器用“第 N 个结构”做键，但默认项让第二个结构意外成功；改成显式步骤列表才表达得出“第二个失败”。** | ✅ |
| PWL 行生成 `UnivariateLinearPiecewiseStructure.GenerateConstraints` + `MaterializeFallbackConstraints` | — | ✅ | **B1199 按基线 `DeferredFunctionFallback.kt` 的 `generateUnivariateLinearPiecewiseConstraints` 补。物化器里**最后一格**补上，七格全满。****`1 + 4n` 行**：`_select_one`（`Σsel = 1`）+ 每段 `_seg_i_lb`（`input + M(1−sel_i) ≥ bp_i`）、`_seg_i_ub`（`input ≤ bp_{i+1} + M(1−sel_i)`）、`_seg_i_eq_ub` / `_seg_i_eq_lb`（结果被夹在该段直线两侧）。****未选中时每条都松弛 `M`，所以 `M` 必须覆盖“未选中那段的解离这条行有多远”——而每段需要的量不同，这就是逐段算 `M` 的理由。****自动 `M` 取五个量的最大值**：本段下断点之下/上断点之上的输入域伸出量、本段最低输出离全局输出上界的距离、本段最高输出离全局输出下界的距离，以及 **`fallbackM`（地板）**。**最后那一项不是松弛量而是**地板****：**前四项有可能全为零（输入域恰好在段内、输出界恰好贴着直线端点），那时 `M = 0`，而“松弛零”其实不松弛——未选中的段照常生效，模型不可行。****这个地板只有在**大输入域**上才与“类型修复”区分得开**：**`PiecewiseBigM.Resolve` 本来就会把 0 修复成`Minimum = 1.0`，所以在 `[0, 1]` 这样的域上两者给出的值恰好都是 1。用 `[-1000, 1000]` 时地板给 1000、单靠修复只给 1。**判据 `UnivariateLinearPiecewiseStructureTests` **14 条**（**B1191 的 7 条 + 本批 7 条**）：**`1 + 4n` 行与族名**、**选中段给出正确值**（**偏离直线时**上下两侧都测****）、**选择行是等式**、**显式 M 必须为正**（**0 与 −5 被拒、100 通过**）、**地板把自动 M 抬到输入域量级**、**缺转换器与缺范围给不同消息**、**PWL 现在能真正物化**。**变异：等式改 `≤ 1` → 红 2 条；丢掉地板 → 红 1 条（**补判据后才红**）；`eq_lb` 松弛方向反 → 红 1 条（**同上**），均已验。****三个变异里有两个第一次全绿，原因不同但都属于“判据的盲区”**：**地板那一处被“类型修复”掩盖了（喂的输入域太小，两者数值相同）；`eq_lb` 那一处是我只测了直线的一侧。****还有一处更早的错**：**初始版用 `result = 999` 测“全零选择”，而 `M = 100` 时松弛后的等式上界约 105，`999` 被等式行自己挡住了——那测的是“M 够不够大”而不是“等式在不在”（MAX 那批的同一个教训）。** | ✅ |
| MAX 行生成 `MaxStructure.GenerateConstraints` + `MaterializeFallbackConstraints` | — | ✅ | **B1198 按基线 `DeferredFunctionFallback.kt` 的 `generateMaxConstraints` 与`materializeMaxFallbackConstraints` 补。** **物化器里“未移植”的格子从两格剩一格（只剩 PWL）。****`2n + 1` 行，三族**：`_max_lb_i`（`result − input_i ≥ 0`）、`_max_ub_i`（`result − input_i + M_i·sel_i ≤ M_i`）、`_max_select_one`（`Σsel = 1`）。****下界族单独存在时 `result` 可以任意大，所以上界族必需；而上界族只在被选中那支紧。****`_max_select_one` 是**等式**而不是 `≤ 1`，这个区别是决定性的**：**换成 `≤ 1`，全零选择就可行，于是所有上界都松弛（`result ≤ input_i + M_i`），`result` 只需 `≥ max` 就可以任意大。** **等式钉住“至少一个被选中”，配上二值选择才给出确切的一个。****`M_i` 必须足够大，而这一层不知道“足够”是多少**：**未选中的那支要求 `result ≤ input_i + M_i`，所以 `M_i` 至少要覆盖 `max − input_i`。`M_i` 偏小不会报错，只会把真正的最大值切掉——而结果仍然可行、求解成功。** **来源是创建快照时捕获的输入范围。****物化顺序是“先校验输入范围证明，再生成行”，与基线一致**：**`M_i` 的正确性依赖那个证明；先生成再校验的话，一个证明已失效的快照会先产出一组行。****`M_i` 只要求非负，与 `AbsStructure` 一致**（`PiecewiseBigM` 要求严格正，那里的 M 是松弛量）。判据 `MaxStructureTests` **13 条**（**B1189 的 8 条 + 本批 5 条**）：**`2n + 1` 行与三族名字**、**选中支给出最大值**（**两侧都断言：小于最大值违反下界、大于最大值违反选中上界**）、**选择行是等式**（**用 `M = 2000` 才检验得到：全零选择下 `result = 1000` 满足其它所有行，只有等式挡着**）、**五个校验分支**（**空输入、选择变量少一、M 少一、负 M、重复键**）、**MAX 现在能真正物化**。**变异：等式改 `≤ 1` → 红 2 条；丢掉 `M·sel` → 红 1 条；删掉下界族 → 红 2 条，均已验。****同名遮蔽又一次**：**`BigMValues` 属性遮蔽了同名静态类，要 `global::` 全限定（B1189 的同类问题）。****又一次把“可行”当成“紧”**：**下界行在 `result = 7`、`x = 3` 时残差是 `+4` 而它是满足的——同一处错误在 B1197 刚犯过。改用按比较方向判断的 `Feasible` helper。** | ✅ |
| ABS 行生成 `AbsStructure.GenerateConstraints` + `MaterializeFallbackConstraints` | — | ✅ | **B1197 按基线 `DeferredFunctionFallback.kt` 的 `generateAbsConstraints` 与`materializeAbsFallbackConstraints` 补。** **B1196 里 ABS 是“未移植”的三格之一，本批补上，所以物化器的分法从 4/3 变成 5/2。****四行两两一组**：`_abs_result`（`result − pos − neg = 0`）、`_abs_decompose`（`input − pos + neg = 0`）、`_abs_pos_ub`（`pos − posM·sign ≤ 0`）、`_abs_neg_ub`（`neg + negM·sign ≤ negM`）。****两条上界互补：`sign = 1` 时 `pos` 只受 `posM` 约束、`neg` 被压到 `≤ 0` 且无下界；`sign = 0` 时反过来。****`_abs_neg_ub` 的右侧是 `negM` 而不是 `0`——写成 `0` 会让 `sign = 0` 时也压成 `neg ≤ 0`，于是负输入完全无法表示。****“两侧非负”是调用方的前提，不是这四行给的**：**四行里没有一行给 `pos ≥ 0` 或 `neg ≥ 0`。取 `x = 5`、`result = 3` 则 `pos = 4`、`neg = −1`，四行全满足而 `result ≠ |x|`。****`CapturedHelperBounds` 存在的理由正是这个：辅助变量的域由调用方声明，这一层拿不到。判据**断言这个漏洞存在**，而不是假装不存在。****两个 Big-M 只要求非负、不要求严格正**：**`posM = 0` 意味着“输入不可能为正”，那是对输入的声明。与 `PiecewiseBigM` 要求显式 M 为正不同——那里的 M 是松弛量，这里的 M 是域的上界。****四个键必须互不相同**：**同一列扮演两个角色时两条等式仍有解，只是那个解不是 `abs`。**判据 `AbsStructureTests` **16 条**（**B1188 的 8 条 + 本批 8 条**）：**四行与名字**、**正侧复现输入**、**负侧复现绝对值**（**含 `neg = negM` 的紧点**）、**“两侧非负”是前提**（**断言漏洞存在**）、**键唯一性**、**负 M 被拒 / 零被接受**、**不可用输入被拒**、**ABS 现在能真正物化**。**变异：`neg` 上界右侧写成 0 → 红 2 条；分解行符号反 → 红 2 条；删掉键唯一性 → 红 1 条，均已验。****一处我自己的错**：**等式行与不等式行都要求“残差为 0”——上界在非紧时残差本来就小于 0，要求它等于 0 是把“可行”错当成“紧”。改成等式要求 `= 0`、上界要求 `≤ 0` 之后才对。****另一处**：**物化器判据里的 ABS helper 把三个辅助列都传成同一个 `Bit`，被键唯一性校验拒了。****变异第一次也没落地**（缩进写错，`AssertionError`），按行号重做。** | ✅ |
| 物化器层 `IDeferredFunctionFallbackMaterializer` + 六个物化器 + `IndicatorStructure`/`MaskingStructure` 的 `MaterializeFallbackConstraints`（8 个） | — | ⚠️ | **B1196 按基线 `DeferredFunctionFallback.kt` 的六个物化器补。****失败有三种，而它们必须被分开**：**结构类型不在本物化器范围内 → `unsupported ... structure: X`；类型对但行生成还没移植 → `... row generation is not ported yet: X`；类型对、行生成在、那一层自己失败 → **把内层错误原样传出**。****把前两种合成一条，调用方就分不出“我用错了物化器”与“这个能力还没有”——前者是调用方的错，后者是本仓库的缺口，修法完全不同。****第三种不能折成任何一种**：**内层失败带着具体原因（例如二值逻辑的输入非法），折成“不支持”会让它消失。****本批盘点出一个事实：七个结构里**四个能真正生成行，三个不能****：**`SemiStructure` / `BinaryLogicStructure` / `IndicatorStructure` / `MaskingStructure` 能；`UnivariateLinearPiecewiseStructure`（B1191 只做了校验）/ `AbsStructure`（B1188 只做了边界快照）/`MaxStructure`（B1189 只做了校验）不能。****物化层的完整性**由行生成决定，而不是由分派表决定**：分派表全绿而三个结构物化不了，是一个看起来完整、实际空着三格的层。****未知类型必须落到最后那一支，而不是静默返回空约束**：**“静默返回空”是最危险的失败方式——模型照常建立、求解成功、只是少了那个函数的行。****专用物化器只接受自己的类型**：**“顺手”接下外来类型会让 core 的分派形同虚设，而错误出现在别处。**判据 `DeferredFunctionFallbackMaterializersTests` **5 条**：**类型不对与未移植给不同消息**、**七个结构分成四/三**、**内层失败原样传出**（**二值逻辑的非二值输入**）、**专用物化器拒绝外来类型**（**双向各一例 + 各自的正确类型**）、**未知类型不返回空列表**（**消息含 `UnknownStructure`**）。**变异：未移植折成类型不对 → 红 2 条；未知类型返回空 → 红 1 条；`Semi` 顺手接下二值逻辑 → 红 1 条，均已验。****两处构造错**：**`AbsStructure` 要三个辅助列与两个 Big-M、`MaxStructure` 要 M 列表；`IndicatorStructure` 的 `ResultVariable` 其实是**指示列**（必须二值），我一开始传了实数列。****`IDE0008` 又一次（判据里的 `var core`），已修。** | ⚠️ |
| 二值逻辑结构 `BinaryLogicStructure` + `BinaryLogicOperation`（2 个）+ 分派分支 | — | ⚠️ | **B1195 按基线 `BinaryLogicStructure.kt` 的 `BinaryLogicStructure` 与 `BinaryLogicOperation` 补，但 AND、OR 的求和行**按真值表修正，未逐字照抄基线**。****核对方法：把每个二值赋值代进全部行，比较"可行集"与真值表。****基线 AND 的求和行 `Σinput − n·result ≥ 1 − n` 在 `n = 2` 时错误接受"两个 1、结果 0"；基线 OR 的求和行 `Σinput − result ≤ 0` 方向反了（它说的是 `result ≥ Σinput`），`n = 2` 时把正确的 `(1,1,1)` 排除。****仓库里已有的 `AndFunction` / `OrFunction` 用的是正确形式**（`Σinput − result ≤ n − 1`、`result − Σinput ≤ 0`），**逐组核对通过，所以本批按仓库已有的正确编码实现，并在源码注释里写明差异。****XOR 则保持基线的三族行不变，因为这不是"方向写反"而是**表达力不足****：**奇偶性是非凸的，三族线性不等式在 `n ≥ 3` 时双向出错——`(1,1,1,0)` 被错误接受，`(1,1,1,1)` 被错误拒绝（后者是唯一正确的赋值）。****精确编码需要一个辅助整数列（`Σinput − result = 2k`），而 `DeferredFunctionFallbackConstraints`只承载行、不承载列定义。** **`ponytail:` 注释记下这个上限与升级路径。**判据 `BinaryLogicStructureTests` **8 条**：**AND 真值表**、**OR 真值表**、**NOT 只有一行且是等式**、**三输入 XOR 已知松弛**（**双向都断言，含"被错误拒绝的正确赋值"**）、**两输入 XOR 不过度约束**、**四种无效输入都被拒**（**空输入、非二值输入、非二值结果、NOT 多输入**）、**AND 与 OR 在角上不同**、**BINARY 已接入分派表**。**变异：AND 求和行方向反 → 红 2 条；OR 求和行改回基线写法 → 红 2 条；XOR 成对行方向反 → 红 2 条，均已验。****一处我自己的算错**：**把 `(1,0,1)` 当成"错误的赋值"，而它有两个 1、异或显然是 0——判据变红的原因是我的期望错了，不是实现错了。** | ⚠️ |
| 掩码结构 `MaskingStructure`（1 个）+ 分派分支 | — | ✅ | **B1194 按基线 `MaskingStructure.kt` 的 `MaskingStructure` 补。****mask 列可能是**共享**的，而共享决定了谁保留它**：**`MaskDefinition` 非空 → 保留 mask（本结构定义它，是所有者）；为空 → 不保留（它由别的结构定义，这里再登记一次会让同一个列被登记两遍）。****“这个列我用到了”与“这个列归我”是两件事。`RetainedResultVariables` 回答的是后者。****定义行是 `mask − definition = 0`，而且排在条件值的行**之前****：**顺序本身不是语义，但如果有人按下标切行、或按“前 N 行是本结构的定义”做假设，顺序就成了契约。****差值取 `mask − definition` 而不是反向**：**等式两边取反等价，不影响正确性；但若哪天改成不等式，方向就成了语义——现在写成减数在前是有意的。****mask 多项式是 `1·mask + 0`，系数取自转换器而不是字面 `1`。****输入清单除条件值的输入外，还要含 mask 多项式与（若有）定义**：**mask 的定义是一个输入多项式，不报出来会让它的变量范围没人收集、自动 Big-M 偏小、行切掉合法解。**判据 `MaskingStructureTests` **7 条**：**只在定义时保留 mask**（**两侧都测：1 个 vs 2 个列**）、**共享时只加条件值的四行、无 `_mask_eq`**、**定义行排在最前且固定“系数 1、右侧 0”**（**`mask = 0` 残差 0、`mask = 1` 残差 1**）、**定义里的常数项被带上**（**`mask = 3` 处紧、`mask = 0` 处残差 −3**）、**mask 多项式是单位系数**、**输入清单含 mask 多项式与定义**（**2 个 vs 3 个**）、**MASKING 已接入分派表**（**含“mask 是保留列不是辅助列”一条**）。**变异：共享时也保留 mask → 红 1 条；丢掉定义常数项 → 红 1 条；定义行移到末尾 → 红 2 条，均已验。****一处笔误先修掉**：**判据的文档注释里混进了一个多余的引号，会让后续行被当成字符串的一部分；构建前发现并修正。** | ✅ |
| 分段输出范围 `PwlOutputBoundsCalculator` + `PwlOutputBounds`（`calculatePwlOutputBounds`，2 个） | — | ✅ | **B1193 按基线 `DeferredFunctionFallback.kt` 的 `calculatePwlOutputBounds` 与 `PwlOutputBounds` 补。****只在本段的两个端点取值、不取内部——而这只对**线性**分段成立。****一段线性函数在闭区间上的极值必定落在端点，所以端点够了；如果哪天分段变成二次，同一个实现会静默地少掉内部极值——而那正是二次与线性的唯一区别。****这条前提用 `ponytail:` 注释记在源码里：只支持线性分段，支持二次分段时必须改成求导或三点采样。****断点的严格递增只比较**相邻**对，而这对全局严格递增是充分的**（**传递性**）。**`Breakpoints.Count < 2` 那一支不是冗余**：**少了它，`Count == 1` 配空斜率表会先通过尺寸检查，然后在端点求值那一步以“输出非有限或无界”失败——那条消息指向数值，而真正的原因是“没有段可算”。**判据 `PwlOutputBoundsCalculatorTests` **7 条**：**两段的端点极值**（**下界与上界都断言**）、**中段低于两端时极值在中段**（**专门对准“只看首末段”的实现，那种实现会给 `[0, 0]` 而模型照常可行**）、**单调情形与首末端点一致**、**数量不匹配与“没有段”给不同消息**（**斜率少一、截距少一各一例**）、**断点严格递增**（**相等、递减、正确的宽跨度三点**）、**任何一处不可用值都被拒**（**三个列表各一例**）、**端点乘积溢出被拒**（**逐项检查通过、组合起来失败，只被最后那道输出检查挡住**）。**变异：只看前两个端点 → 红 2 条；相等被接受 → 红 1 条；删掉“至少两个断点” → 红 1 条（**得到的正是那条误导性消息**），均已验。****第一个变异第一次没落地**：**`Math.Min` 里的 `Math` 被 `Fuookami.Ospf.Math` 命名空间遮蔽，2 个编译错误；改用 `global::System.Math.Min` 之后才变红。** | ✅ |
| 分段 Big-M `PiecewiseBigM`（`resolvePwlBigM`） | — | ✅ | **B1192 按基线 `DeferredFunctionFallback.kt` 的 `resolvePwlBigM` 补。****同一个非正候选值，在两条路径上得到相反的结果**：**自动路径（`explicit = false`）把它规范化成正的、通过；显式路径（`explicit = true`）直接拒绝。****自动 M 是一个**推导出来的下界估计**——它可以合理地是 0 或负数（比如“没有数据”时），而把它规范化成一个安全的正值不会改变任何语义。****显式 M 是**调用方的声明**。声明一个非正的 Big-M 是矛盾：“所有违反情形的偏差都 ≤ 0”意味着那些约束根本没有放松余量。****这时规范化会悄悄改掉调用方的声明，而规范化之后的模型可能可行、求解成功——只是它与调用方要求的不一样。所以：可修复的就修复，不可修复的就拒绝。****`explicit` 只影响“非正”这一条，不影响“可用性”**：**两边都查可用性。一个把它读成“只有显式才查可用性”的实现，会让自动路径上的 `NaN` 被夹到 `Minimum` 而通过——那是一个凭空造出来的 Big-M。****判据断言“修复结果为正”而不是某个具体值**：**`Minimum` 是一个可以调整的实现常量，判据不该把它写死。**判据 `PiecewiseBigMTests` **4 条**：**同一个非正值两条路径结果相反**（**`0` 与 `−5` × 两条路径，四种组合**）、**自动修复产出正值**、**正的候选值两条路径都原样通过**（**不对 1000 做任何改写**）、**不可用候选值两条路径都拒绝**（**`NaN` 与 `+∞` 各两例**）。**变异：非正值两条路径都拒绝 → 红 2 条；两条路径都修复 → 红 1 条；只有显式才查可用性 → 红 1 条，均已验。****“正的候选值原样通过”这一条是补覆盖的**：**不测它的话，一个“永远拒绝”的实现能通过上一条的后半段，一个“永远夹到 `Minimum`”的实现能通过前半段。** | ✅ |
| 约束区域 `DeferredFunctionConstraintRegion` + 范围捕获 `PiecewiseInputBounds` + 分段线性 `UnivariateLinearPiecewiseStructure`（**限 `ValidateFallbackInputBounds`**，3 个） | — | ✅ | **B1191 按基线 `FunctionExpansion.kt` 的 `DeferredFunctionConstraintRegion`、`UnivariateLinearPiecewiseStructure`（仅校验部分）与 `DeferredFunctionFallback.kt` 的 `capturePwlInputBounds` 补。****`LastConstraintIndex` 是**排他**索引**：**`[2, 5)` 的首索引 `2`、行数 `3`、末索引 `5`，最后一行在 `Last − 1 = 4`。** **写成 `First + Count − 1` 就成了闭区间，而名字里没有 `inclusive`——两者差 1，在“按下标切行”的代码里不会报错，只会错一位。****判据钉不变量 `Last − First == Count` 而不是具体数值**（**数值会随 `First` 一起变，错误公式看起来仍成立**），**并断言空区域的末端等于起点。****`ValidateFallbackInputBounds` 的**第一件事**是看 `ExplicitM`**：**显式给了 Big-M 时，整套范围证明都不需要——直接通过。****理由：自动 M 从输入范围推出，必须证明“范围没有被后续约束放宽”；显式 M 是调用方给的数，与输入范围无关。****“不需要”不等于“跳过检查”：它意味着后面三步的前提都不成立，所以那个分岔必须在最前面。把它放到后面，一个带显式 M 而缺转换器的快照会被判为非法——而它其实是合法的。****两条缺失（缺转换器 / 缺证明）分别有自己的消息**：**都是 `IllegalArgument`，共用一条会让调用方分不出原因。****捕获本身失败时那条错误被原样传出**，**不折成“没有证明”——后者会让原因消失。****第四条“不可测”发现**：**`PiecewiseInputBounds.Capture` 的第二层（“范围可用”）经 `FiniteBounds` **不可达****：**试过 `1e15`–`1e300` 的常量与 `1e100`–`1e308` 的系数，凡是 `FiniteBounds` 返回非 `null` 的 `IsUsable` 都接受，凡是不可以的 `FiniteBounds` 自己就返回 `null`。****保留那一层是对的（`FiniteBounds` 的行为不是本函数的契约），但判据不假装能测到它。**判据 `UnivariateLinearPiecewiseStructureTests` **7 条**：**区域末端排他**（**不变量 + 空区域**）、**显式 M 既不需要转换器也不需要证明**、**两条缺失各自的消息**、**捕获失败原样传出**、**收缩可以 / 扩大不行**、**捕获只第一层可达**（**无穷常量与溢出系数两例，含“自由变量有默认界”一例**）、**PWL 已接入分派表**。**变异：删掉显式 M 短路 → 红 1 条；末端改闭区间 → 红 1 条；捕获失败折成缺失 → 红 1 条，均已验。****一处我自己的输入错**：**“扩大”用例我写的是“常量 −5 越过下界”，而 helper 的常量其实是 `0`，落在 `[0, 10]` 内——判据变红，红的原因是输入选错。改成 `11` 才对。** | ✅ |
| 半连续结构 `SemiStructure` + fallback 包装 `DeferredFunctionFallbackConstraints`（2 个） | — | ✅ | **B1190 按基线 `FunctionExpansion.kt` 的 `SemiStructure` 与 `DeferredFunctionFallback.kt` 的`DeferredFunctionFallbackConstraints` 补。****半连续的意思是“要么是 0，要么落在 `[L, U]` 里”，而两条行**只在成对时才强制 0****：**指示 = 0 时，上界行只说“不超过 0”、下界行只说“不小于 0”——单独任何一行都只是半边。****这不是“加一条 `result = 0` 的分支”，而是让两条行在指示 = 0 时**都收紧到 0**、在指示 = 1 时**都放宽到一个区间**。****系数互换在指示 = 0 时**看不出来****（两者都是 0），只有指示 = 1 时才暴露——**所以判据必须同时测两个指示值。****没有校验 `L > 0`，而这不是遗漏**：**基线在 `materializeFallbackConstraints` 里也不校验。****但 `L ≤ 0` 会让“半连续”退化成“普通有界变量”——那时指示 = 1 的区间本身包含 0，“要么 0 要么 ≥ L”就没有区分力。本实现不做基线没有的校验，但把后果写清。****`MaterializeFallbackConstraints` 直接返回 `Flt64` 行，不做一次假转换**：**基线经过 `convertPwlConstraintsToFlt64`，那个转换器存在的理由是基线对 `V` 泛型；本实现是 `Flt64` 专用，行本来就是 `Flt64`——转换是恒等。****这个差别写在方法注释里，而不是悄悄省掉一个调用。**判据 `SemiStructureTests` **6 条**：**指示 = 0 时两行一起钉在 0 上**（**0 处两行都紧、`±1` 各违反一行——三步缺一步都证明不了“成对”**）、**指示 = 1 时放开到 `[L, U]`**（**两端点紧、两端外排除**）、**系数不能互换**（**正确结构 `[3, 10]` 与互换后 `U = 3` 各一例**）、**辅助列只有指示列**、**fallback 返回包装而不是裸列表**、**SEMI 已接入分派表**（**含“没有输入多项式”一条**）。**变异：上界行用下界系数 → 红 2 条（而 `TheIndicatorZero` **不红**，正说明互换在指示 = 0 时看不出来）；比较方向互换 → 红 1 条；下界行丢掉指示项 → 红 1 条，均已验。** | ✅ |
| MAX 结构 `MaxStructure`（1 个）+ 分派分支 | — | ✅ | **B1189 按基线 `FunctionExpansion.kt` 的 `MaxStructure` 补。****`max` 需要每个候选一个选择列，而不是一个**：**`SelectorVariables` 与 `Inputs` 一一对应，`BigMValues` 也是。** **一个“当前最大值”列无法表达“是哪一个候选取到最大”，而那正是后续按候选处理的地方。****`ValidateFallbackInputBounds` 的方向是“当前必须落在捕获之内”**：**收缩可以**（**Big-M 上界仍然有效**），**扩大拒绝**（**Big-M 可能不再是上界**）。****这个方向不对称，所以判据必须两个方向都测**：**只测“扩大被拒”的话，一个“永远拒绝”的实现也能通过；只测“收缩可以”的话，一个“永远接受”的实现也能通过。****数量不匹配的检查在 `try` **之外****：**否则 `Inputs[position]` 会抛下标越界，而那个异常被 `catch` 转成一条完全不同的消息——调用方看到“校验失败”而真正的原因是“数量对不上”。****`null` 条目表示“这个输入当时没有证明”，跳过它，而不是当成“证明为零”。****一处 `using`/遮蔽问题**：**记录自己的属性 `BigMValues`（`IReadOnlyList<Flt64>`）遮蔽了同名的静态类，必须全限定到 `global::...Symbol.Function.BigMValues`。**判据 `MaxStructureTests` **8 条**：**收缩仍然通过**、**扩大被拒**（**只越下界、只越上界、两侧同时三例**）、**数量不匹配报自己的消息**（**并断言消息里含 `input-bound proofs for`**）、**未捕获时直接通过**、**`null` 条目被跳过**、**选择列按顺序报出**、**多输入时 `InputOrNull` 返回 `null`**（**单输入则返回那个**）、**MAX 已接入分派表**。**变异：去掉下界方向 → 红 1 条；删掉数量检查 → 红 1 条（实际得到的是“下标越界被 catch 转译”的消息，正是那条注释警告的情形）；`null` 当成零 → 红 1 条，均已验。****第三个变异第一次全绿**：**我的用例里输入常量是 `0`，恰好落在 `[0, 0]` 里。改成 `7` 之后才红——判据的输入必须**远离**那个错误值，否则它证明不了任何事。** | ✅ |
| ABS 结构 `AbsStructure` + 范围快照 `AbsHelperBoundsSnapshot`（2 个） | — | ✅ | **B1188 按基线 `FunctionExpansion.kt` 的 `AbsStructure`、`AbsHelperBoundsSnapshot` 补。****`|x|` 需要**三个**辅助列，而不是一个**：**正部、负部、正负部选择。****结果等于正部 + 负部，而选择列决定当前走哪一支。****只有正部与负部两列也能表达结果，但无法表达“这个点落在哪一支”——而那正是后续需要按分支处理的地方。****`InputVariableKeyOrNull` 要求输入是一个**裸变量****：**恰好一个 monomial、系数为 1、常数为 0。****三条中少任何一条，返回 `null` 而不是“尽力而为”。****为什么必须这么严**：**结果被用来“按输入变量索引 helper”。****如果系数是 2，那个 helper 属于 `2x` 而不是 `x`，两个不同的结构会被索引到同一个键上——后写入的悄悄覆盖前一个。** **覆盖不是错误：字典写入永远成功，它只是让一个 helper 消失。****符号不是变量列时也返回 `null` 而不是硬转型**：**`|sin(x)|` 的输入里那个符号是函数**；**硬转型会抛，而抛出去会让整个建模失败——“输入不是裸变量”本来就是可预期的结果。****`AbsHelperBoundsSnapshot` 的两个字段都可空，而“可空”是全部信息量**：**`null` 表示“当时没有已知界”，不是“界是零”。** **改成非空会让 fallback 证明认为辅助变量只可能在 0 上。****本批同时补了分派表的三处 `AbsStructure` 分支**（**结果列、输入、输入清单、辅助列、延迟展开**），**因为 B1187 记录了“漏加分支不报错、只是少一列”这条危险。**判据 `AbsStructureTests` **8 条**：**系数不是 1 时不给键**（**`2` 与 `−1` 两例，并对照系数 1 给出键**）、**常数不是 0 时不给键**、**monomial 不是恰好一个时不给键**（**零个与两个都测**）、**符号不是变量时不给键**、**三个辅助列顺序固定且不含结果列**、**`null` 界与零界是不同的事实**、**两个 Big-M 按顺序给出**、**ABS 已接入分派表**（**五个函数各一例**）。**变异：丢掉系数检查 → 红 1 条；“恰好一个”弱化成“非空” → 红 1 条；删掉分派分支 → 红 1 条，均已验。****第二次变异第一次没落地**：**`if (count != 1)` 改成 `if (count == 0)` 时多删了一个大括号，2 个编译错误；重做成干净的单点替换后才变红。** | ✅ |
| 展开策略 `FunctionExpansionPolicy` + 分派表 `DeferredFunctionStructures`（6 个） | — | ✅ | **B1187 按基线 `FunctionExpansion.kt` 的五个 `DeferredFunctionStructure` 扩展函数与`FunctionExpansionPolicy` 补。****这五个函数是“标记接口”付出代价的地方**：**接口本身没有成员（B1180 已说明理由：它只做类型层面的集合划分），于是**所有具体信息都要靠一次类型分派取出来**。****分派里每个 `else` 分支都是真实的语义，不是占位**：**`ResultVariableOrNull` 对不认识的结构返回 `null`，表达的正是“这个结构没有保留结果列”。****本实现只覆盖本程序集已有的结构（`IndicatorStructure`）。****其余结构在基线里各有分支，但它们在本仓库还不存在——**等它们落地时必须同时在这里加分支**。****漏加不会编译失败，只会让那个结构静默地报“没有结果列”，而调用方拿到 `null` 后通常走“不需要保留”的分支，于是模型少了一列。****这是本文件唯一的真实危险。判据把它写进测试，但它**不是**回归保护——漏加分支时它仍然全绿，它只是把“这就是当前语义”记下来，让下一个加结构的人看见还欠一个分支。****`IndicatorStructure` 报出自己的输入**连同附加条件的输入****（**条件值、蕴含前件、合取条件、差值条件**）：**只报主输入会让那些条件的变量范围没人收集，而自动 Big-M 依赖这份清单，于是 M 偏小、行会切掉合法解。****`HelperVariablesOrEmpty` 对指示结构**为空****：**零带方向列、等价列、合取/差值结果已由`RetainedResultVariables` 报出，重复会登记两次——而“登记两次”在多数调用方那里表现为重复行，不是错误。****策略的默认值必须是 `Eager`**：**`default(FunctionExpansionPolicy) == Eager`，而 `Eager` 的值是 `0`。把 `DeferredNativeFirst` 挪到首位，所有没显式赋值的调用点都会静默改成“先试原生”。**判据 `DeferredFunctionStructuresTests` **6 条**：**未登记结构报告缺省**（**五个函数各一例**）、**指示结构报出全部附加输入**（**主 + 条件值 + 蕴含 = 3 个，按位置核对**）、**合取与差值的条件输入也计入**、**结果列与延迟展开能力**、**辅助列不重复保留列**、**策略默认值是 `Eager` 且枚举共 3 个值**。**变异：丢掉蕴含输入 → 红 1 条；辅助列改成重复保留列 → 红 1 条；枚举首位换成 `DeferredNativeFirst` → 红 1 条，均已验。** | ✅ |
| 指示结构 `IndicatorStructure` + `ConjoinedIndicator` + `DifferenceIndicator`（3 个） | — | ✅ | **B1186 按基线 `IndicatorStructure.kt` 补，三者循环依赖，同置一文件。****合取需要**三**行链接，不是两行**：**`_link_ge`（`result ≤ primary`）与 `_link_le`（`result ≤ conditionResult`）只说“合取不能比任一支大”。****只有这两行的话，`result = 0` 永远可行——合取就被常量 0 替换了，而模型完全可行、目标值合理。****`_link_lb`（`result ≥ primary + conditionResult − 1`）补上“也不能比任一支小”。****差值的第二条行不是冗余**：**`_bter_result`（`result − primary + conditionResult = 0`）允许`primary = conditionResult = 1`、`result = 0`，那种组合把“两个同时成立”表示成 0，与“两个都不成立”混淆；`_bter_exclusive`（`primary + conditionResult ≤ 1`）排除它。****校验顺序是语义的一部分**：**合取/差值与其它附加结果不能共存 → 先生成合取/差值行 →再检查零容差带组合。** **两步都是 `IllegalArgument`，调换只让错误消息出现在对方的场景里。****“三个列互不相同”按**标识**比较，不按名字**：**两个同名 `BinVar` 在这个模型层是不同的列。****`RetainedResultVariables` 的顺序固定**（**结果列 → 等价列 → 条件值 → 蕴含 → 零带 → 合取 → 差值**）。**判据 `IndicatorStructureTests` **9 条**：**合取需要第三行**（**两真时 `result = 1` 必需、`result = 0` 必须违反 `_link_lb`；任一支为 0 时 `result = 0` 可行**）、**前两行各限一个方向**、**三列按标识而非名字区分**（**同名两个变量被接受、同一列被拒**）、**校验顺序**（**合取 + 零带 + 条件值同时违反，报出的是合取那条**）、**合取与差值互斥**、**差值的等式允许两真而排他行排除它**、**保留列顺序固定**、**非二值结果被拒**、**非正容差被拒**。**变异：`_link_lb` 右侧从 `−1` 改成 `0` → 红 1 条；`_bter_exclusive` 右侧从 `1` 改成 `2` → 红 1 条；校验顺序调换 → 红 1 条，均已验。****两次“变异没落地/判据太弱”**：**① 删整行第三行的变异产生 4 个编译错误（改用最小单点修改右侧常量）；② 顺序变异的判据第一次只加了 `ZeroBand`，而零带检查还要求一个附加项非空——四个都是空的，那一步根本不触发，于是调换后全绿。补齐 `ConditionalValue` 之后才红。****“判据里构造了会失败的输入”与“判据里构造了**让那个检查真的会失败**的输入”是两件事。** | ✅ |
| 非零指示行 `ZeroIndicatorRows.BuildNonzero`（`nonzeroIndicatorConstraints`） | — | ✅ | **B1185 按基线 `BigM.kt` 的 `nonzeroIndicatorConstraints` 补。****同一个看起来一样的 `indicator` 参数，两个函数里含义相反**：**零值检测说“指示 = 1 时落在带内”，非零检测说“指示 = 1 时落在带外”。****所以紧的那一侧对调**：**带行在**指示 = 0** 时紧，外行在**指示 = 1** 时紧。****`_out_lb` 的右侧是 `strict − 2·outM`，不是 `strict − outM`**：**那一行同时带 `ind` 与 `side` 两个 `−outM` 项，指示 = 1、`side = 1` 时两者相加正好抵消，于是落到 `strict`。****只减一个 `outM` 会让紧点偏移整个 `outM`——而模型仍然可行。****`_out_ub` 的右侧是 `−strict + outM`**，**同一类抵消的另一半。****两套行共用同一份输入校验**（**二值、`side ≠ indicator`、`strict > tolerance`**），**因为极性的差别不该带来校验的差别。**判据 `ZeroIndicatorRowsTests` **10 条**（**+4**）：**非零极性相反**（**指示 = 0 时夹进 `[−2, 2]`，指示 = 1 时 `side` 选方向，并核对四个行名**）、**外行减两次 `outM`**（**`x = ±4` 紧、`x = ±3` 排除**）、**带行在指示 = 1 时放开**、**非零沿用同一套输入校验**（**`strict = tolerance` 与自方向列两例**）。**变异：只减一个 `outM` → 红 2 条；`_band_ub` 极性反了 → 红 1 条；`_out_ub` 右侧丢掉 `+outM` → 红 2 条，均已验。****第二个变异第一次没落地**：**用“往上找 12 行”的方式定位，改到了 `Build` 里那处同形代码；而 `Build` 已有判据覆盖它——测试仍全绿，说明改的那一处**不是**我瞄准的地方。****改成唯一锚点（连右侧常量与行名一起匹配）之后才真正落地。** | ✅ |
| 关系指示行 `RelationIndicatorRows`（1 个） | — | ✅ | **B1184 按基线 `Conditional.kt` 的 `relationIndicatorConstraints` 补。****这是本方向第一个**不用 big-M** 的指示编码**：**条件本身有已知范围 `[qLower, qUpper]` 时，松弛量可以**精确算出**——它就是“真分支的边界到范围边界”的距离。****`lowerAdjustment = trueLower − qLower`、`upperAdjustment = qUpper − falseUpper`。****两行**：**`_lower` 是 `q − lowerAdjustment·ind ≥ qLower`（指示 = 1 时 `q ≥ trueLower`，为 0 时放开到 `q ≥ qLower`）；**`_upper` 是 `q − upperAdjustment·ind ≤ falseUpper`（指示 = 0 时 `q ≤ falseUpper`，为 1 时放开到 `q ≤ qUpper`）。****两行在指示 = 1 与 = 0 时**互换谁紧谁松****——**这正好表达“指示为 1 时条件成立、为 0 时不成立”。****归一化后的语义要先说清**：**`relation` 相对**零**，`bounds` 只是 `q` 的范围**（**`GE`/`LE` 真分支 `q ≥ 0`、假分支 `q ≤ −δ`；`GT`/`LT` 真分支 `q ≥ δ`、假分支 `q ≤ 0`**），**所以 `bounds` 必须跨过零，否则一个分支为空。****三处校验都不是形式**：**① 指示必须二值；② 多项式不得包含指示变量自己（否则 `ind` 既在被判定的式子里又在判定它）；③ 取负后仍要可用。**判据 `RelationIndicatorRowsTests` **7 条**：**两行落在条件自己的边界上**（**并断言 `x = −1` 被排除**）、**两行互换松紧**（**两条行 × 两个指示值四处**）、**`LE` 先取负**、**`GT`/`LT` 留空带**（**`x = 1` 落在空带里，两个分支都不接受**）、**自指多项式被拒**、**非二值指示被拒**、**四种关系都能建出来**（**`LT` 的紧点在 `x = −δ`，与 `GT` 符号相反**）。**变异：两个调整量互换 → 红 2 条；不取负 → 红 2 条，均已验。****两次我自己的输入/算术错**：**① 第一版用 `[0, 10]` 测 `GE`，假分支为空、条件恒真，两行退化成与指示无关；② helper 的第二个参数是 `strictBoundary`，我全传了 `0.0`，于是每个判据都拿到 `Failed`，而 `.Value` 在 `Failed` 上是 `null`，报出来是 `NullReferenceException`，指向 `Rows(...)` 那一行。****“参数位置对了、值传错了”是这种 helper 形状的代价：三个同类型参数排在一起，编译器帮不上忙。** | ✅ |
| `simpleIndicatorConstraints` 的 `EQ` 分支（接零容差带） | — | ✅ | **B1183 把 B1181 里被拒绝的 `EQ` 接到 B1182 的零容差带上。****`EQ` 曾经与 `LT`/`GT`/`NE` 一起被拒绝——那是 B1181 时零容差带还没就位的后果，不是基线的行为。****`diff == 0` 的“偏离”有两个方向，所以需要方向列。****调用方不给 `sideVar` 时临建一个（基线也是 `BinVar("${namePrefix}_side")`）。****而**临建的列必须被调用方登记**——否则它是自由的，方向就成了任意值，而模型完全可行。****这条限制写进了判据的注释里，因为“临建就完事了”是最容易得出的错误结论。****`LT`/`GT`/`NE` 仍然被拒绝**：**基线对它们没有编码，本实现也不假装有。**判据 `SimpleIndicatorRowsTests` **9 条**（**+2**）：**`EQ` 走零容差带**（**四条行按名字核对，并断言带内 `x = ±2` 紧**）、**`EQ` 不传方向列时临建一个**（**只断言四条行，不断言登记——登记是调用方的责任**）。**变异：`EQ` 回到被拒绝 → 红 1 条，已验。****一处编译错误值得记**：**判据里用 `rows.Select(...)` 时与 `Result` 上的 `Select` 扩展重载冲突**（**`CS0411`：无法推断类型参数**），改成显式循环后通过。****`Result` 有 `Select` 这件事，会让所有在结果集合上写 LINQ 的代码突然歧义。** | ✅ |
| 零值指示行 `ZeroIndicatorRows`（2 个） | — | ✅ | **B1182 按基线 `BigM.kt` 的 `zeroIndicatorConstraints`、`safeZeroIndicatorConstraints` 补。****四条行，而**第二个二值（`sideVar`）只在“偏离”那一侧出现****：**`_zero_band_ub`/`_zero_band_lb` 在指示 = 1 时把值夹进 `[−tolerance, tolerance]`；`_zero_out_lb` 在指示 = 0 且 `side = 1` 时强制 `poly ≥ strictBoundary`；`_zero_out_ub` 在指示 = 0 且 `side = 0` 时强制 `poly ≤ −strictBoundary`。****为什么需要 `sideVar`**：**“偏离零”有两个方向，一个二值指示只能表达“是或不是”，说不清“往哪边偏”。** **没有它，`poly = +5` 与 `poly = −5` 会被这套行混成一个状态。****`bandM = bigM + |tolerance|` 与 `outM = bigM + |strictBoundary|` 是两个**不同**的松弛量**：**用同一个会让其中一侧多松或过紧，而两侧的间隔本来就不同。****`strictBoundary` 必须严格大于 `tolerance`**：**否则“带内”与“带外”在边界上重叠，一个点可以同时声称两者——那种重叠不会让模型不可行，只会让它有第二个解，而那是最难查的一类错误。****同一个变量当指示与方向列被拒绝**：**两个二值退化成一个。**判据 `ZeroIndicatorRowsTests` **6 条**：**指示 = 1 时夹进带内**（**`x = ±2` 紧、`x = ±3` 排除，四点**）、**`side` 选方向**（**`side = 1` 走正向、`side = 0` 走负向，各两个点**）、**两个松弛量不同**（**`102` 与 `104`，方向列 `−104`**）、**`strictBoundary ≤ tolerance` 被拒**（**相等与更小两例**）、**自方向列被拒**、**四行的名字与方向**。**变异：`outM` 复用 `bandM` → 红 1 条；删掉重叠检查 → 红 1 条；方向列符号反了 → 红 2 条，均已验。****第三个变异第一次没落地**：**锚点缩进不匹配，替换脚本抛 `AssertionError`，而我又先跑了测试——看到的是上一次构建的二进制。****改成按行号精确替换（第 157 行）之后才真正变红。** | ✅ |
| 简单指示行 `SimpleIndicatorRows`（2 个） | — | ✅ | **B1181 按基线 `BigM.kt` 的 `simpleIndicatorConstraints`（LE/GE 分支）与 `nonnegativeIndicatorConstraints` 补。****`simpleIndicatorConstraints` 先做**差分**：`diff = lhs − rhs`，之后所有行只针对 `diff`。****这一步容易被写成“直接把 `lhs` 当多项式”——那会让 `rhs` 的项整块消失，而模型仍然可行（少了一个约束项，不是矛盾），只是条件算错了。****`_violated` 行用 `relaxBigM(bigM, strictBoundary)`，不是 `bigM`**：**`strictBoundary` 是违反侧的间隔，照抄 `bigM` 会让违反侧多松 `strictBoundary`**（**与 B1180 的下界行同一个理由**）。**`nonnegativeIndicatorConstraints` 是“非负检测”，方向与正向指示相反**：**指示 = 1 时 `poly ≥ 0`（允许等于零），指示 = 0 时 `poly ≤ −tolerance`（严格负）。****`≥ 0` 而不是 `≥ tolerance`**：**两个问题的分界线不同。****`EQ` 需要零容差带（`sideVar`），`LT`/`GT`/`NE` 基线直接拒绝**——本批例如实拒绝。判据 `SimpleIndicatorRowsTests` **7 条**：**差分含 `rhs` 的常数**（**`x ≤ 5`，`x = 5` 处紧**）、**`rhs` 带变量时那些项也进差分**（**`x ≤ y + 5`，`x = 8`、`y = 3` 处紧**）、**指示 = 1 时差分不超过容差**（**含指示 = 0 时放开**）、**违反侧用 `bigM + strictBoundary`**（**系数是 `102` 而不是 `100`，并有紧点断言**）、**`GE` 分支的方向与容差**、**非负检测指示 = 1 时允许零**（**并断言指示 = 0 时零不被接受**）、**`EQ`/`LT`/`GT`/`NE` 被拒**。**变异：违反侧照抄 `bigM` → 红 1 条；跳过 `rhs` 的 monomial → 红 1 条，均已验。****第二条是补出来的**：**第一版只用 `x ≤ 5` 测差分，而 `5` 是 `rhs` 的**常数**——`rhs` 的 monomial 列表是空的，“跳过 rhs 的 monomial”对它是无操作，变异因此全绿。****补了 `x ≤ y + 5` 之后才红。****一处我自己的算术错误**：**第一版判据把违反侧的紧点写成 `x = −97`（把 `bigM` 当成唯一项、忘了右侧的 `2`），实际是 `x = −95`（指示 1）与 `x = 7`（指示 0）。判据红的原因是我的算术，不是实现。****未做**：`EQ` 分支（需要零容差带）、`relationIndicatorConstraints`、安全包装族——**它们与 `IndicatorStructure`（207 行）一起构成后续批次。** | ✅ |
| 正向指示行 `PositiveIndicatorRows`（1 个）+ 标记接口（1 个） | — | ✅ | **B1180 按基线 `BigM.kt` 的 `positiveIndicatorConstraints`、`safePositiveIndicatorConstraints`、`relaxBigM` 补，并补 `FunctionExpansion.kt` 的空标记接口 `DeferredFunctionStructure`。****两行，方向相反，而两侧的 Big-M **不是同一个数****：**`_positive_ub` 用 `−bigM`（指示 = 0 时 `poly ≤ 0`，为 1 时放开到 `poly ≤ bigM`）；`_positive_lb` 用 `−(bigM + |tol|)`（指示 = 1 时 `poly ≥ tolerance`，为 0 时恰好 `poly ≥ −bigM`）。****推导**：**下界行要的是 `poly ≥ tolerance − M·(1 − ind)`。照抄 `M` 会在指示 = 0 时得到`poly ≥ tolerance − M`——那是 `−M` 加一个 tolerance，比 `−M` 更松；用 `M + |tol|` 则恰好是 `poly ≥ −M`。****所以这不是风格差异，是“松弛得刚好”与“多松一个 tolerance”的差异：两种写法都不切合法解，但松的那一种让松弛变弱，而且看不出原因。****`|margin|` 而不是 `margin`**：**tolerance 为负会让 `bigM + tol &lt; bigM`，于是下界行在指示 = 0 时反而更紧、可能切掉合法解——本实现直接拒绝负 tolerance，同时 `RelaxBigM` 自身取绝对值。**判据 `PositiveIndicatorRowsTests` **7 条**：**指示 = 1 时 `poly ≥ tolerance`**（**`x = 3` 满足、`x = 1` 违反**）、**指示 = 0 时 `poly ≤ 0`**（**三点**）、**下界行在指示 = 0 时恰好放开到 `−bigM`**（**`x = −100` 紧、`x = −101` 排除**）、**上界行在指示 = 1 时恰好放开到 `bigM`**（**`x = 100` 紧、`x = 101` 排除**）、**负 tolerance 被拒**、**非正 Big-M 被拒**（**`0` 与 `−5` 两例**）、**两行的名字与方向**。**变异：下界行照抄 `bigM` → 红 1 条；上界行用松弛后的 M → 红 1 条，均已验。****第二条是补出来的**：**我第一次只钉住下界的放宽量，把上界换成 `lowerRelaxM` 后判据全绿——因为没有任何判据观察过“指示 = 1 时上界放开到哪里”。补了 `TheUpperRow_ShouldRelaxToExactlyBigM` 之后才红。****未做**：`nonnegativeIndicatorConstraints`、`simpleIndicatorConstraints`、`relationIndicatorConstraints`、零容差带与安全包装族——**它们与 `IndicatorStructure`（207 行）一起构成后续批次。** | ✅ |
| 函数使用语境 `FunctionUsageSummary`（4 个） | — | ✅ | **B1179 按基线 `FunctionExpansion.kt` 的 `FunctionUsageSummary`、`FunctionUsageLocation`、`FunctionInputShape`、`PiecewiseContinuity` 补。****四个布尔是“收集到的事实”，三个派生属性是“据此做的决定”——分开它们让“何时保留结果变量”只有一处规则。****`Location` 的判定顺序不可交换**：**`Nested` 与 `External` 排在 `ObjectiveAndConstraint` 之前，因为它们是最强的保留理由。****一个既出现在目标、又出现在约束、同时被别的结构引用的函数，位置必须报 `Nested`；把顺序调换会让它报“目标与约束都有”，调用方据此省掉结果变量，模型就错了。****`Nested` 又优先于 `External`**——**这个差别只有在两者**同时**为真时才看得见，所以单独有一条判据钉住它。****`RequiresResultVariable` 里 `(InObjective &amp;&amp; InConstraint)` 是**冗余**的**（**`InConstraint` 已覆盖**），**基线保留它，只为让“同时出现在两边”这个情形写在表达式里可见。冗余不是错误，但必须标出来。****`SupportsObjectiveOnlyNative` 要求 `SingleVariable`**：**多变量的线性表达式无法被“仅目标”的原生接口表达，那个接口只接受单变量分段函数。****`Unknown` 与 `Discontinuous` 是两件事**：**前者“没查过”，后者“知道有断点”**；**把前者当后者会拒绝本可展开的函数，把后者当后者会生成一条断点处的错误行。**判据 `FunctionUsageSummaryTests` **8 条**：**被引用优先于“目标与约束都有”**（**`Nested` 与 `External` 各一例**）、**其余四种位置**、**`Nested` 优先于 `External`**（**两者同时为真**）、**保留结果变量的四种理由**（**并断言“仅目标”不保留**）、**仅目标原生要求单变量**（**`LinearExpression` 与 `Unknown` 各一例**）、**目标与约束都有时不走仅目标原生**、**`Unknown` 与 `Discontinuous`/`LinearExpression` 是不同值**、**默认值全是“未收集”**。**变异：位置判定顺序调换 → 红 1 条；`Nested`/`External` 互换 → 红 1 条，均已验。第二个变异只被那条专门判据捕获——如果只有第一条判据，互换会全绿。** | ✅ |
| 条件结果 `ConditionalValue`（1 个） | — | ✅ | **B1178 按基线 `model/intermediate/ConditionalValue.kt` 补。指示列为 1 时取输入，否则为 0。****四条行是“两个分支各两条”，而两个分支的方向相反**：**`_zero_ub`：`result − upper·indicator ≤ 0`；`_zero_lb`：`result − lower·indicator ≥ 0`；`_then_ub`：`−input + result − lower·indicator ≤ −lower`；`_then_lb`：`−input + result − upper·indicator ≥ −upper`。****指示 = 0 时 `_zero_*` 把 `result` 夹成 0；指示 = 1 时 `_then_*` 把它夹成 `input`，而 `_zero_*` 退化成输入的上下界。****所以 `bounds` 不是“输入的值域”，而是**证明****：**它让 `_zero_*` 在指示 = 1 时不构成额外限制；给一个过窄的 `bounds`，模型要么不可行、要么切掉合法解。****三条校验都不是形式**：**① `resultVariable ≠ indicator`**——**同一个变量当结果与指示时，两个分支要求它同时等于 0 与 `input`，四条行直接冲突；****② 指示必须是二值**——**连续指示会让两个分支同时“部分激活”，`result` 变成 0 与 `input` 之间的某个值——模型可行，结果是捏造出来的；****③ `lower ≤ upper`**——**倒序范围把 `result` 夹进空区间。****只支持 `Flt64`**：**数值可用性检查本身就是 `Flt64` 的，`V` 版本只能靠 `ToString` 再过一遍——那会静默丢精度。**判据 `ConditionalValueTests` **6 条**：**四条行的名字与方向**、**指示 = 0 时把结果夹成 0**（**并验证 `result = 1` 与 `result = −1` 各被一侧排除，所以 0 是唯一解**）、**指示 = 1 时把结果夹成输入**（**`result = input` 处两条残差为 0，`result = 0` 被 `_then_lb` 拒绝，`result = 12` 被 `_then_ub` 拒绝**）、**结果与指示同一变量被拒**、**连续指示被拒**、**倒序范围在构造层被拒**。**变异：删掉二值检查 → 红 1 条；`_zero_ub` 系数符号反了 → 红 1 条，均已验。****判据自己错过一次**：**我第一版在 `result = 0` 处断言 `_then_lb` 的残差 ≥ 6，实际是 `−6`**——**残差必须带 `result` 代入才说得清方向；改成“在 `result = input` 处残差为 0、在错的一侧残差为负”之后通过。** | ✅ |
| 全局约束门面 `GlobalConstraintFunctions`（1 个） | — | ✅ | **B1177 按基线 `GlobalConstraintFunctions.kt` 补。这是一个**纯外观**：每个方法都只把调用转给已存在的实现，所以它的价值不在逻辑，而在“顺序”与“校验点”。****三个入口各自转给自己的 `Create`，而三者都做了**同样的**非空检查。****我最初以为只有 `AllDifferent` 查、另外两个不查——那是猜的，跑出来三条都拒绝空输入。门面这里不重复那层检查：重复只会让错误消息多一层翻译，而“三处一致”由判据统一钉住。****`Lower` 的两个重载不是重复**：**一个收可变模型、一个收不可变快照；收模型的那个内部先 `Snapshot()` 再转给收快照的那个——两条路走**同一条**实现，而不是两份可能漂移的代码。****`null` 策略表示严格**：**`ConstraintProgrammingLoweringPolicy` 是记录而不是枚举，不能当默认参数值；“默认宽松”会让调用方在不知情的情况下拿到一个未经校验的模型。****`null` 参数抛 `ArgumentNullException` 而不是返回失败结果**：**`null` 是调用方的编程错误，`Enumerable.Empty()` 才是“空但合法”；混同两者会让空集合悄悄变成 `null` 的替身，而 `null` 通常很久之后才炸。**判据 `GlobalConstraintFunctionsTests` **6 条**：**`AllDifferent` 拒绝空输入**、**`NoOverlap`/`Cumulative` 也拒绝空输入**（**一条判据覆盖两者，比三条各自为政更能说明“它们一致”**）、**有内容的 `Cumulative` 建得出来**、**`null` 参数抛而不是返回失败**、**两个 `Lower` 重载对同一模型给出一致结论**、**`null` 策略等于严格策略**。**变异：`AllDifferent` 绕过工厂直接构造 → 红 1 条，已验。****第二处变异（`null` 策略映射到宽松）编译失败（2 个错误），按纪律不算证据。** | ✅ |
| 二次掩码 `QuadraticMaskingFunction`（1 个） | — | ✅ | **B1176 按基线 `QuadraticMasking.kt` 补。结构是“先桥接、再掩码”**：**二次输入 `p` 先被桥接变量 `y` 取代（`y − p = 0`），`y` 再作为线性输入交给 `MaskingFunction`。****这一步不是优化，是必需的**：**`MaskingFunction` 只接受线性输入，直接把二次项塞进去无处分派——而“无处安放”有两种结局：抛异常（好），或静默丢项（坏，且当时不报错）。****桥接变量必须有范围**：**`MaskingFunction` 要从输入界推 Big-M 定掩码上下界，无界输入会让它退回 `1e6`——能跑，但界宽了 6 个数量级，松弛变松、求解变慢，而且不报错。****桥接等式必须真的写进模型**：**不写的桥接变量是自由的，掩码读一个任意值，而模型完全可行、目标值合理。****只支持 `Flt64`，与仓库里其他二次函数一致**：**桥接本身就是 `Flt64` 的，把外层写成泛型只会换来一次`V → Flt64` 的字符串转换，而那在 `V` 是定点数或高精度数时会静默丢精度。基线的泛型是它自己转换器体系的一部分，这里没有对应的东西——所以不假装有。**判据 `QuadraticMaskingFunctionTests` **5 条**：**桥接变量被 `x²` 的实际界界住**（**上界 `100`、下界 `−100`**）、**桥接等式与掩码行都进模型**（**按名字核对 `qm_bridge_row_0`**）、**掩码为 0 给零**、**掩码为 1 跟随桥接值**、**输入符号含掩码**。**变异：桥接等式不写进模型 → 红 1 条，已验。****第二处变异（直接传二次输入）编译失败（2 个错误），按纪律不算证据，已如实记录。****一处实测发现的界宽问题**：**`QuadraticBounds.Of` 用四角取最值，所以 `x²` 在 `[−10, 10]` 上得到`[−100, 100]` 而不是 `[0, 100]`——「平方非负」它测不出来**（**B1163 已记录同一限制**）。**后果是桥接变量的界比实际宽一倍：能跑、不报错，只是松弛略松。已写进判据而不是假装下界是 0。** | ✅ |
| 归一化模型 `NormalizedModel`（6 个） | — | ✅ | **B1175 按基线 `solver/report/NormalizedModel.kt` 的六个记录与 `validateNormalizedIdentityMetadata` 补。****这一族记录存在的理由是**稳定指纹****：**同一个模型重建两次，变量、约束、目标的顺序可能不同，而指纹必须相同。****所以所有数值以**字符串**保存，而不是 `double`**——**`double` 一旦参与往返，“同一个数”会因为格式化差异而变成两个指纹。****标识一致性校验挡的是“同一个模型里出现两个命名空间”**：**命名空间进指纹，两个都出现时指纹里同时含两者，而“哪个元素说了哪个”取决于遍历顺序——于是同一个模型能产生两个指纹。****判定是“去重后多于一个”，不是“全都有值”**：**要求所有元素都填，会让“只标了模型级命名空间”这种正常情形被拒绝。****空白串按“没说”处理**：**`""` 与 `null` 都表示未指定；把空白串当成一个命名空间，会让 `""` 与 `"A"` 被判为不一致——那是无意义的失败。****命名空间与 schema 是两条独立的检查**，**混淆它们会让失败指向错误的原因。**判据 `NormalizedModelTests` **8 条**：**命名空间不一致被拒**（**断言消息点了 "namespace"**）、**schema 不一致被单独报告**（**断言消息点了 "schema"**）、**一致时通过**、**空白串按“没说”处理**（**`""`、`"   "`、`null` 三例**）、**只有一个元素说了命名空间也算一致**、**不一致的模型仍被创建但带失败标记**、**数值原样往返不经过 `double`**、**二次项缺省为空**。**变异：删掉命名空间检查 → 红 2 条；空白串当成命名空间 → 红 1 条；schema 检查复用命名空间集合 → 红 1 条，均已验。****一处措辞修正**：**我原本写“不一致的模型根本没有机会被创建”，那是不准确的——它被创建了，只是带着一个失败标记。****这个区别重要**：**拒绝构造会让调用方无法**报告**问题（拿不到对象来诊断），而带标记的构造让他们既拿到模型、又拿到结论。****未做**：`canonicalText()` 与 `identityValidation` 之外的派生入口——**规范文本要等指纹格式稳定后再补，现在补一个格式会立刻成为兼容负担。** | ✅ |
| 离散条件归一化装配 `normalizeDiscreteCondition`（2 个） | — | ✅ | **B1174 按基线 `DiscreteCondition.kt` 的 `normalizeDiscreteCondition` 补装配本体，并补齐 `transformForRelationIndicator` 与 `hasSafeStrictPositiveLatticeProof`。****这一批只做装配**：**归一化范围（B1173）、折叠判断（B1171）、格点证明（B1172）三块都已就位。****装配仍有两处容易错，而两处都不报错**：**① 范围的变换必须与多项式变换用**同一个** `strictBoundary`**——**多项式走 `p + δ`、范围却只平移 `0`，两者错开 `δ`，分支存在性判断在一个偏移过的区间上做，结论可能反；****② 折叠优先于范围重算**——**反过来的话，折叠给出的"这个分支不可能"会被覆盖，一个本可省掉的指示变量又被生成，不报错、只是白算。****`GT`/`LT` 的格点证明与 `GE`/`LE` 不同**：**前者证"严格边界 `δ` 不落在 `q = 0` 与最近正格点之间"，后者证"平移后仍等价"；`LT` 先归一化为 `q = −p` 再证。**判据 `DiscreteConditionNormalizationTests` **8 条**：**`GE` 范围与多项式同步平移**（**`[−5,3]`+`δ=2` → `[−3,5]`**）、**`LT` 取负并交换端点**、**折叠的真值决定两个分支**、**折不了时按范围算**、**格点未证时拒绝**（**`p=3x+1`、`δ=2`、`delta=3`**）、**`delta ≤ δ` 不需要证明**、**`EQ`/`NE` 被拒**、**非正 `delta`/`δ` 被拒**。**变异：范围忘了平移 δ → 红 1 条（差值正是 2.0），已验。****一处诚实的结论（来自一次全绿的变异）**：**"折叠优先"这个 `??` 的优先级测不出来——穷举四个关系 × 924 组范围 × 四个 `δ` 值，折叠与重算**没有一处不一致**。****所以那两侧恒等价，保留它是为了可读性与基线结构对应，不是行为差异；判据已改为只断言"折叠的真值决定两个分支"。** | ✅ |
| 条件范围归一化 `NormalizedCondition`（3 个） | — | ✅ | **B1173 按基线 `Conditional.kt` 的 `NormalizedCondition`、`normalizedCondition`、`validateConditionBounds`、`conditionBranchCoverage` 补 `ConditionNormalization`。****"归一化"是把四种关系折算成"严格真"的形态**：**`LT`/`LE` 先把多项式取负（负侧变成正侧）；`GT`/`LT` 的真分支下界是 `δ`、`GE`/`LE` 的则是 `0`。折算之后下游只处理一种形态。****`NegatePolynomial` 是最容易被忽略的字段**：**忘了取负会让整个真假判断反向，而模型仍然可行——只是条件被取反了。****三件事依次检查**：**① 端点有限可编码；② `lower ≤ upper`；③ 真假分支之间必须有正间隔。****"两个分支都空"单独报错**：**范围与真假分支都不相交时无解，报错而不是返回"两个都是 `false`"**（**后者会让调用方以为"条件恒假"，而实际是"这个范围不合法"**）。判据 `ConditionNormalizationTests` **8 条**：**`LT`/`LE` 报告并取负**（**含 `QLower`/`QUpper` 的具体值**）、**严格关系把真边界放在 `δ`**、**`δ = 0` 被边界检查拒绝**（**并断言消息点了 "positive"**）、**`FalseUpper &lt; TrueLower` 在四种关系下恒成立**、**倒序范围在构造层被拒**、**分支存在性跟随边界**、**与两个分支都不相交时报"neither"**、**`Validate` 与 `Normalize` 一致**。**变异：取负的关系写反 → 红 2 条，已验。****一处诚实的结论（来自一次判据失败）**：**我先用 `δ = 0` 去测"正间隔"检查，但 `δ = 0` 先被"边界必须为正"挡住，于是删掉正间隔检查后判据仍全绿——那条判据测的是边界检查。****要让正间隔成为唯一拦截者需要 `δ &gt; 0` 而 `TrueLower == FalseUpper`，这在四种关系下都不可能**（**`GT` 下 `TrueLower = δ`、`FalseUpper = 0`**）。**所以那条 `return Fail` 在当前形态下不可能被触发，它是防御性代码。****判据改为断言"四种关系下间隔恒正"，而不是假装能测到那条分支。** | ✅ |
| 离散条件格点 `DiscreteConditionLattice`（4 个） | — | ✅ | **B1172 按基线 `DiscreteCondition.kt` 的 `effectiveDiscreteConditionMonomials`、`isIntegerValuedDiscreteConditionNumber`、`discreteConditionCoefficientGcd`、`hasSafeDiscreteShiftProof` 四个辅助函数补。****补它的原因是一个已发现的缺口**：**B1168 的 `ProveShift` 只检查了"步长 ≥ δ"，而基线还要检查"常数到会被误判那一侧最近格点的距离 ≥ δ"。****前者是必要条件，不是充分条件。****反例（本批的锚点）**：**`p = 3x + 2`、`δ = 2`，取值 `2 + 3ℤ = …, −1, 2, 5, …`。步长 `3 ≥ δ = 2` 通过，但 `p` 能取到 `−1`，而 `−1 > −2` **成立**、`−1 ≥ 0` **不成立**——`p + δ > 0` 与 `p ≥ 0` 在 `p = −1` 处给出不同答案。****只有余数距离检查能挡住它。****四处**：**① 同变量系数先合并再丢零项**（**`2x` 与 `−2x` 合并后是 0，那个变量根本不参与格点；不合并会算出步长 2**）；**② 整数性检查先排除 `NaN` 与无穷**（**靠检查而不是靠比较的巧合**）；**③ 无非零系数返回 `null`**（**格点退化成一点，与"步长是 0"不同**）；**④ 余数距离按关系分侧**（**`GE` 挡下方、`LE` 挡上方**）。判据 `DiscreteConditionLatticeTests` **9 条**：**歪斜格点被拒**（**本批唯一真正新增的判据**）、**对齐格点通过**、**步长太小被拒**、**`GT`/`LT` 不走这条路**、**同变量系数先合并**（**`2x` 与 `−2x` 抵消后无有效项**）、**无变量退回常数检查**、**连续变量不是整数格点**、**非整数系数不是格点**、**整数性排除 `NaN`/无穷**。**变异：删掉余数距离检查 → 红 1 条；不丢零项 → 红 1 条，均已验。****未做**：`normalizeDiscreteCondition` 本体——**它现在只剩"按上面四项判断该不该拒绝"的装配，留待下一批**。 | ✅ |
| 离散条件折叠 `boundsAvoidDiscreteConditionGap`（2 个） | — | ✅ | **B1171 按基线 `DiscreteCondition.kt` 的 `boundsAvoidDiscreteConditionGap` 与`DiscreteConditionLinearization` 补 `DiscreteConditionGap.AvoidsGap` 与线性化记录。****折叠回答的是"还要不要生成指示约束"**：**界已定死时指示变量没有意义——直接给真值，省掉一个二元变量与两条行。****四条关系**不对称**，这是本函数唯一的难点**：**`GE` 判真需 `lower ≥ 0`、判假需 `upper ≤ −δ`；`GT` 判真需 `lower ≥ δ`、判假需 `upper ≤ 0`。****`GE` 与 `GT` 只差"是否允许相等"**：**`p ≡ 0` 时 `GE` 为真、`GT` 为假——写成一样会让 `0 > 0` 判为真。****`δ` 只出现在"判假"一侧**：**假分支要离真分支至少 `δ`，否则太靠近边界、不算假。****`EQ`/`NE` 折不了，返回 `null`**：**`EQ` 要看两个方向，`NE` 是析取。****`null` 与"两个分支都可能"是同一个意思——不是"不知道"，是"需要指示变量"。****线性化记录五个字段里 `FixedValue` 与两个 `Possible` 是冗余的**：**`FixedValue` 非空时恰好一个 `Possible` 为真；基线两个都存，因为读的一方常只关心其中一个，而"推回去"多一次空判断。这个冗余换来调用点的直接性，不是可随便删的东西。**判据 `DiscreteConditionGapTests` **7 条**：**`GE` 与 `GT` 在零处不同**（**`GE` 真、`GT` 假**）、**`LE` 在上界不大于 0 时为真**、**`LT` 需要 `upper ≤ −δ`**、**判假尊重 `δ`**（**`upper = −0.5` 定不下来、`−1` 才判假**）、**跨界返回 `null`**、**`EQ`/`NE` 从不折叠**、**记录携带五个字段**。**变异：`GT` 借用 `GE` 的判真条件 → 红 1 条；判假忽略 `δ` → 红 1 条，均已验。****判据自己错过一次**：**我以为 `GT` 在 `p ≡ 0` 时返回 `null`，实际返回**假****（**`upper = 0 ≤ 0` 说明条件恒不严格为正，假分支确定**）——**那是我的期望错，已按实际改正。****未做**：`effectiveDiscreteConditionMonomials`、`isDiscreteConditionDomainPolynomial`、`isDiscreteConditionPolynomial`、`discreteConditionCoefficientGcd`、`hasSafeStrictPositiveLatticeProof`——**五个辅助函数与 `normalizeDiscreteCondition` 本体留待后续批次。** | ✅ |
| 固定变量等式 `fixedVariableEquality`（1 个） | — | ✅ | **B1169 按基线 `DiscreteCondition.kt` 的 `fixedVariableEquality` 补 `FixedVariableEquality.Of`。****一行 `<c>variable = value</c>`，但两处容易漏**：**① 系数必须是 `one`、常数必须是 `zero`，不能互换；且 `one`/`zero` 由调用方传入而不是现造**（**为了让它在定点数与高精度数上等于调用方的单位元**）。**② 右端是 `value`、左端是变量；方向反了在 `EQ` 下**数值上完全等价**，所以这是一个"改了也测不出来"的错误**——**只有系数与常数项的位置能分开。****本函数不做可用性检查，基线也不做**：**它只构造一行，检查在调用方。这一点写成判据（`NaN` 原样通过）是为了让"没有检查"成为**已记录的行为**，而不是可能被漏掉的地方。**判据 `FixedVariableEqualityTests` **5 条**：**变量被钉在值上**、**系数与常数不能互换**、**调用方的单位元被真的用上**（**用 `2` 当单位元，现造 `1` 会读到 1**）、**null 变量抛出**、**非有限值原样通过**。**变异：`one`/`zero` 互换 → 红 2 条；左右两侧互换 → 红 2 条，均已验。****未做**：`normalizeDiscreteCondition`（**依赖 521 行的 `DiscreteConditionLinearization` 与`boundsAvoidDiscreteConditionGap`、`hasSafeStrictPositiveLatticeProof` 两个证明函数**）——**留待后续批次，不与 521 行的数据结构同批。** | ✅ |
| 严格正变换 `StrictPositiveCondition`（2 个） | — | ✅ | **B1168 按基线 `DiscreteCondition.kt` 的 `StrictPositiveCondition` 与 `toStrictPositiveCondition`两个重载补 `StrictPositiveCondition` 记录与 `StrictPositiveTransformation`。****四条规则**：**`GT` 不变、`GE` 加 `δ`、`LT` 取负、`LE` 取负后加 `δ`。****`GE` 那一行是全部难点**：**`p ≥ 0` 与 `p &gt; 0` 不是同一个条件，而 `p + δ &gt; 0` 等价于 `p &gt; −δ`——两者只在 `p` 落在步长 `δ` 的格点上时相同。****所以 `δ ≠ 1` 时基线要求证明格点对齐**：**所有非零系数须为整数值且乘整数型变量，格点步长是它们的最大公约数，`δ` 不得超过它。****证不出来时退回一个更弱的检查**（**常数项与 0 同侧或离 0 至少 `δ`**）——**基线明确这是"未被证伪"而不是"已证明"。****`EQ`/`NE` 拒绝**：**`p ≠ 0` 是"或正或负"，一个严格正条件表达不了；挑一个方向会让一半解消失而模型仍可行。**判据 `StrictPositiveTests` **9 条**：**`GE` 平移而 `GT` 不动**、**`LE` 先取负再平移**（**顺序反了差 `2δ`**）、**`LT` 只取负**、**`EQ`/`NE` 被拒**、**`δ` 超过格点步长被拒**（**`2x + 1` 上 `δ = 3` 会放进 `p = −1`**）、**连续变量退回常数检查**（**常数 `−0.5` 落在 `(−1, 0)` 里不安全、`0.5` 安全**）、**非正 `δ` 被拒**、**不等式重载的常数项也减**（**`x + 3 ≤ 1` 的差值是 `x + 2`，漏掉常数项得 `x`**）、**包装类型保留变换后的多项式**。**变异：`GE` 不平移 → 红 2 条；删掉格点检查 → 红 1 条，均已验。****未做**：`DiscreteConditionLinearization`（521 实义行）与 `normalizeDiscreteCondition`/`fixedVariableEquality`（181 实义行）——**两个大件留待后续批次。****按类统计**：**`DiscreteCondition.kt` 933 行里，本批收口 3 + 52 行的部分，剩 702 行。** | ✅ |
| 极差 `RangeFunction`（1 个） | — | ✅ | **B1167 按基线 `CompositeFunctions.kt` 的 `RangeFunction` 补**（**该文件里其余五个类——`PositivePart`/`Clamp`/`DeadZone`/`L1Distance`/`LInfinityDistance`——C# 里早已实现，本批只剩这一个**）。**语义是 `max(inputs) − min(inputs)`，**不是**"两两之差的最大值"。****两者在两个输入时相同，三个及以上才分得开**——**而且只在"极值不在首尾"的取值上分得开**：**`[10, 0, 3]` 的极差是 `10`，而"首尾相减"给 `|3 − 10| = 7`。****本类不含任何极值编码**：**它构造一个 `MaxFunction` 与一个 `MinFunction`，再把两者相减。****`Evaluate` 按定义直接算，不走 `max`/`min`**：**这条路径与编码无关，所以能独立验证编码。****空输入拒绝而不是给 0**：**空集的极差没有定义，给 0 会让"漏了一个输入"看起来像"极差是零"。**判据 `RangeFunctionTests` **7 条**：**三个输入时 `max − min`**、**极值不在首尾时也正确**（**唯一能分开"真极差"与"首尾相减"的取值**）、**单输入为 0**、**空输入被拒**、**结果式是相减而不是相加**（**取负后系数应成对出现**）、**`max` 与 `min` 的约束都注册**（**按名核对 `_max` 与 `_min`**）、**输入符号为空**（**全是常数**）。**变异：`max + min` → 红 1 条；只扫描第一个输入 → 红 2 条，均已验。****编译期一处**：**缺 `using Fuookami.Ospf.Math.Algebra.Concept;` 会让 `IRing`/`IField`/`IRealNumber`全部解析失败，报错却指向"泛型参数不能转换"——与实际原因隔了一层。** | ✅ |
| 非折叠蕴含 `ImpliedCondition`（1 个） | — | ✅ | **B1166 按基线 `ImpliedCondition.kt` 补，并抽出 `ConditionPolynomials` 承载可用性检查。****语义**：**前置为 1 时要求 `input ∈ [lower, upper]`；前置为 0 时两行不起作用。****三行**：**下界门控、上界门控、以及 `antecedent − indicator ≤ 0` 的链接行。****`lowerAdjust = tolerance − lower`，**不是** `lower`**：**用 `lower` 会让下界行在前置为 0 时仍要求`input ≥ lower`——蕴含退化成合取。** **取 `lower = −10`、`tolerance = 1` 时右端应为 `−21`；写成 `lower` 会得到 `0`，两者相差 21。****第三行是必需的**：**只写两条门控时 `indicator` 自由，求解器可以为了满足区间把它设成 1，于是"蕴含"变成"永远成立"。****基线两条界检查照抄**：**`lower &gt; 0` 与 `upper &lt; tolerance` 都拒绝**——**这两种情形下偏移量变成 0 或负数，门控系数消失，前置为假时不再松弛。****每行还要检查"实际差值"而不只是两端**：**两个 `1e300` 各自可用，相减却溢出成 `inf`。**判据 `ImpliedConditionTests` **7 条**：**三行且第三行是链接**、**前置为假时下界门控确实松弛**（**右端 `−21` 而不是 `0`，本批最重要的一条**）、**上界行的前置系数是 `+upper`**（**写反会让前置成立时反而更松**）、**同一变量兼两角被拒**（**链接行会退化成 `0 ≤ 0`**）、**非二元被拒**、**非正容差被拒**、**`lower &gt; 0` 与 `upper &lt; tolerance` 被拒**。**变异：`lowerAdjustment` 写成 `Bounds.Lower` → 红 1 条，已验。****未做**：`RangeFunction`（571 实义行）、`DiscreteCondition` + `StrictPositiveCondition`（769 实义行）——**三个都是大件，留待后续批次；本批只收口 `ImpliedCondition` 与它依赖的可用性检查。** | ✅ |
| 非线性逼近的真实函数基准（`originalValue`） | — | ✅ | **B1165 按基线 `NonlinearPiecewise.kt` 基类的 `originalValue`/`originalValueFlt64` 补。****四个分段逼近函数（`exp`/`ln`/`1/x`/`x^p`）此前在 C# 里都已实现，但**只返回逼近值**，没有真实值作对照——于是"逼近误差"根本无法测量。****"误差在断点处为零"是一个陷阱**：**线性插值在每个断点处与真实函数**恰好重合**，所以"抽查几个断点"永远测不出段数不足。****三处必须检查，基线三处都检查了**：**① 输入非有限；② 输入落在定义域外；③ 真实值非有限。漏掉 ① 会让 `NaN` 传进误差计算，而 `NaN` 与任何数比较都是 `false`，让"误差过大"的判据**静默通过**。****新增 `OriginalValue(double)`、`ApproximationError(double, double)`、`DomainLower`/`DomainUpper`**（**四个类各一份，共享逻辑在 `NonlinearSourceValue` 里**）。**抽样端点存成 `double` 字段**：**`Point<Dim2,V>[i]` 返回 `V`，而 `V` 没有 `ToDouble()`**。判据 `NonlinearSourceValueTests` **6 条**：**断点处误差恰好为零、断点之间明显大于零**（**同时证明两件事：`OriginalValue` 真在算真实函数，且"断点处为零"不是逼近质量的证据**）、**段数增加误差减小**（**同时断言粗段误差非零，否则"误差恒 0"会让这条平凡成立**）、**域外被拒**（**含 `NaN` 输入**）、**真实值就是真实函数**（**`ln 2` 与 `exp 1`，`1e-12` 容差**）、**非有限逼近值被拒**（**`NaN > 容差` 是 `false`**）、**定义域与采样一致**。**变异：`OriginalValue` 返回输入本身 → 红 2 条，已验。****未补 `NonlinearPiecewiseApproximationFunction` 抽象基类**：**C# 的四个类各自委托给`UnivariateLinearPiecewiseFunction`，抽象层只是命名差异，不构成能力缺口。** | ✅ |
| 二次不等式与两个松弛（3 个） | — | ✅ | **B1164 按基线 `QuadraticInequality.kt`、`QuadraticSlack.kt`、`QuadraticSlackRange.kt` 补`QuadraticInequalityFunction`、`QuadraticSlackFunction`、`QuadraticSlackRangeFunction`。****三者讲同一件事：把"差了多少"变成非负量。****`QuadraticInequality` 是第八个"桥接后委托"**，**不同点是委托目标返回**真值**（0/1）而不是多项式值。****`QuadraticSlack = max(l − r, 0) + max(r − l, 0) = |l − r|`**（**名字读起来像有符号差，实际是无符号的**）。**`QuadraticSlackRange = max(lower − x, 0) + max(x − upper, 0)`**，区间内为 0。**后两者都由两个 `QuadraticPositivePartFunction`（B1163）拼成，没有自己的极值编码。****最容易写错的是"只取一侧"**：**`max(l − r, 0)` 单独用会在 `l &lt; r` 时给 0；区间违背量只留一侧会在另一半恒为 0——两者都不报错。****`lower &gt; upper` 必须拒绝**：**否则区间为空、两侧违背量同时为正、函数处处非零，而模型仍可行、只是目标值莫名其妙地大。**判据 `QuadraticSlackFunctionTests` **8 条**：**松弛无符号**（**`x = −3` 给 3 而不是 0**）、**区间内为 0 且两侧各给距离**、**两半都覆盖**（**专盯"只算一侧"；只用 0 与 2 测两个实现都会过**）、**二次不等式给 0/1**、**倒序区间被拒**、**两个正部各自注册行**（**按名核对 `_positive` 与 `_negative`**）、**结果多项式是两半之和**、**桥接行进模型**。**变异：只保留左侧正部 → 红 2 条（`TheSlack_ShouldBeUnsigned`、`TheSlackRange_ShouldCoverBothHalves`），已验。****判据变量仍是共享实例**（**新建 `RealVar` 会让求值恒返回 `null`**）。 | ✅ |
| 二次绝对值与正部（2 个） | — | ✅ | **B1163 按基线 `QuadraticAbs.kt`、`QuadraticPositivePart.kt` 补 `QuadraticAbsFunction`、`QuadraticPositivePartFunction`。****`QuadraticAbs` 是第七个"桥接后委托"的适配器**（**`AbsFunction` 接受线性多项式，所以桥接之后 `|y|` 与 `|p|` 同值——只要桥接等式真的成立**）。**`QuadraticPositivePart` 用的是基线恒等式 `max(p, 0) = −min(−p, 0)`**：**`min` 由 `exact = true` 的`QuadraticMinFunction` 实现，本类没有自己的极值编码。****三处容易写错且都不报错**：**① 忘了外层取负（得到负部而不是正部）；② `−p` 只取负 monomial 而漏掉常数项；③ `exact = false`（`min` 变松弛，结果偏小）。****`selectorVars` 在输入必然定号时为空**：**那时 `min` 不需要选择器，界已确定 `p` 与 0 的大小关系。**判据 `QuadraticAbsFunctionTests` **8 条**：**绝对值两侧都给正**、**绝对值不是恒等**（**只测正值无法区分 `|p|` 与 `p`**）、**正部在负输入处是 0**（**正部与负部的分界点**）、**常数项也被取负**（**`p = x + 5` 在 `x = −10` 处正部为 0；漏掉常数项会得到 −15**）、**必然定号时无选择器**、**`ResultPolynomial` 是 `−resultVar`**、**零点归一**（**`−0` 与 `0` 相等但可观察不同**）、**桥接行进模型**。**变异：`ResultPolynomial` 去掉外层负号 → 红 1 条；常数项不取负 → 红 1 条，均已验。****一次未命中的变异**：**锚点用 `StopIteration` 报错、什么也没改，全绿——那是"变异没打中"，不是"判据不灵"，已改成按行号变异并重测。****发现一处真实能力局限（未在本批修复）**：**`QuadraticBounds.Of` 对 `x·x` 用四角法，在 `[−10, 10]` 上给出 `[−100, 100]`，看不到"平方恒非负"** → **判据改用界明确的线性输入来测定号分支。** | ✅ |
| 二次极值三兄弟（3 个） | — | ✅ | **B1162 按基线 `QuadraticMax.kt`、`QuadraticMaxMin.kt`、`QuadraticMinMax.kt` 补 `QuadraticMaxFunction`、`QuadraticMaxMinFunction`、`QuadraticMinMaxFunction`。****与 B1161 的 `QuadraticIf` 系列同形**：**桥接每个二次多项式，再交给已有的线性 `Max`/`MaxMin`/`MinMax`。极值编码本身没有任何新增。****`max min` 与 `min max` 不是同一个量**：**对两个输入 `x` 与 `−x`，`max min = −|x|`、`min max = |x|`——两者互为相反数。****所以判据必须给出**不同**的期望值，否则"写反了"测不出来。****空输入时基线塞一个零多项式**（`inputs.ifEmpty`）：**不是拒绝，而是让 `MaxFunction` 不必处理空表；代价是"空输入"与"输入一个常数 0"无法区分。**判据 `QuadraticExtremumFunctionTests` **6 条**：**`max` 取最大**（**三点：`±3` 与 `0`**）、**`max min` 与 `min max` 符号相反**（**唯一能分开两者的判据**）、**桥接行按名字核对两条都在**、**空输入补零**、**三个类委托给三个不同的线性类**（**只有具体类型能区分它们**）、**桥接是无界 `RealVar`**。**变异：`max min` 委托给 `MaxFunction` → 红 2 条（含符号判据），已验。****第一次尝试把 `min max` 改成 `MaxMinFunction`，编译失败（类型不匹配）——那不算证据，已换成能编译的变异。****判据变量仍是共享实例**（**本方向第四次强调：新建 `RealVar` 会让求值恒返回 `null`**）。 | ✅ |
| 二次条件三件套（3 个） | — | ✅ | **B1161 按基线 `QuadraticIf.kt`、`QuadraticIfThen.kt`、`QuadraticIfIn.kt` 补 `QuadraticIfFunction`、`QuadraticIfThenFunction`、`QuadraticIfInFunction`。****三者都只做一件事**：**把二次多项式桥接成一个实变量（等式 `y − p = 0`），再交给**已有的**线性`IfFunction`/`IfThenFunction`/`IfInFunction`。** **分支编码本身没有任何新增。****桥接等式必须是**二次**行**：**第一版写成 `LinearInequality`，于是"二次项无处安放"直接抛异常——而若当时选择"丢项"，条件会静默变成错的且无一步报错。****桥接行必须真的写进模型**：**否则桥接变量自由、不出现在任何约束里，分支读的是任意值，而 `Inner` 的约束全可满足、模型可行、目标值合理。****桥接变量必须有范围**：**`IfFunction.Create` 要从条件界推出大 M，无界条件被它拒绝，而拒绝的表现是 `.Value` 为 `null`（不是抛异常）。****桥接变量必须是无界 `RealVar`**：**`URealVar` 会在求解器边界把负值截断成 0，于是 `x &lt; 0` 这类条件永远为假。**判据 `QuadraticIfFunctionTests` **7 条**：**桥接行按名字核对真的进模型**（**见下**）、**桥接等式引用桥接变量且系数为 1**、**求值走二次条件**（**`x = 3` 与 `x = −3` 都给 1、`x = 0` 给 0——两个点合起来才证明用的是 `x²` 而不是 `x`**）、**`IfThen` 取二次取值**、**`IfIn` 判二次输入**、**桥接是无界 `RealVar`**、**倒序区间被拒**。**变异：桥接行不注册 → 红 1 条（补判据前是 0 条），已验。****第一次那条判据用 `BeGreaterThan(1)` 数总行数，于是"删掉桥接行"全绿——分支行本身就不止一条。****改成按行名核对 `_bridge_row_0` 之后才抓到。****判据自己错过两次**：**① `Values` 新建 `RealVar` 实例导致对象身份不匹配（本方向第三次）；② `strictBoundary = 0` 落在条件下界上被 `IfFunction.Create` 拒绝。****另有一处我的期望错**：**`IfInFunction` 在区间外返回 `null` 而非 0（仓库既有语义），已按实际改正判据。** | ✅ |
| CVaR 上镜图 `CvarEpigraphFunction`（1 个） | — | ✅ | **B1160 按基线 `Cvar.kt` 补。与 `CvarFunction` 的差别就是这一批的全部内容**：**`CvarFunction` 的 `ResultPolynomial` 已经是最小值；本类的 `ResultPolynomial` 是一个含 `θ` 的上界式。****关键性质：上界式在任意 `θ` 处给上界，只在最优阈值处取等号、其余处严格大于。****所以"把 `ResultPolynomial` 当 CVaR 用"会高估**——**它必须进目标被最小化，`θ` 才会到最优处。****编码只有一组行 `Lᵢ − θ − uᵢ ≤ 0`**：**`uᵢ ≥ 0` 由 `URealVar` 的范围承担而不是再写一条行**，**两条合起来才是 `max`。** **少 `uᵢ ≥ 0` 则 `uᵢ` 可取下界 0（目标偏大）；**少 `Lᵢ − θ − uᵢ ≤ 0` 则 `uᵢ` 可以任意小（约束被违反）。****`θ` 的范围取所有损失界的并**：**窄了会把最优阈值排除，最优解落在边界上、结果偏大而模型仍可行。**判据 `CvarEpigraphFunctionTests` **10 条**：**上界式在非最优阈值处严格大于 CVaR**、**最优阈值处取等号**（**只证"≥"而不能达到就只是个大数**）、**与 `CvarFunction` 同值**、**`α = 0` 退化成平均值 −1**（**可手算的锚点**）、**每情景一条行**、**`uᵢ` 由范围承担非负**（**行数等于情景数；另写非负行会数出 8 而非 4**）、**`θ` 的范围覆盖所有情景**、**`α = 1` 被拒**、**概率和必须为 1**、**注册恰好写 4 行**。**变异：分母写成 `alpha` → 红 1 条（`α = 0` 锚点），已验。****判据自己错过一次**：**损失是常数、没有 monomial，`Monomials[0]` 越界** → **改从 `InputSymbols` 取**。 | ✅ |
| 二次区间归属 + 二次桥接（2 个，**空壳转实体**） | — | ✅ | **B1159 按基线把 `QuadraticInStepRangeFunction` 与 `QuadraticLinearFunction` 两个空壳换成真实编码。****区间归属是三段语义**：**在内给 `x`、明确在外（`≤ lower−tol` 或 `≥ upper+tol`）给 0、容差带内给 `null`。** **第三段是核心：把容差带并进任意一侧都会让"明确在外"失去意义**（**并进"在内"则越界一点点就返回越界值；并进"在外"则边界附近一律给 0**）。**容差只出现在 `below`/`above` 的界上，不在 `inside` 上**——**若 `inside` 也吃容差，区间本身变宽而带消失。****三个状态变量 `inside`/`below`/`above` 互斥且完备（`sum = 1`）**：**没有它三者可同时为 0，于是 `y` 的两侧约束全松弛，`y` 变自由变量而模型仍可行。****二次桥接在输入已线性时什么都不做**：**不建 `y`、不写行、辅助变量为空**——**一条 `y = p` 的等式对线性的 `p` 只是抄一遍。****非线性时用无界 `RealVar` 而非 `URealVar`**——**基线注释明确记了 `URealVar` 会在求解器边界把负值静默截断成 0，于是多项式在整个负半轴全错而模型照样求解成功。****两个类的 `InputSymbols` 都返回 `null`**：**仓库契约是"不实现 `IHasResultPolynomial` 的函数必须返回 `null`"**，**声明成集合会让 `FunctionInputContractTest` 直接红。**判据 **13 条**：**区间内三个点（含两端）**、**明确在外两点**、**容差带内给 `null`**（**唯一区分三段与两段的判据**）、**带的外沿属于"在外"**、**`sum = 1`**、**容差只加宽外侧行**（**须先各自抵消大 M 再比较——**两行右端差是 `2m − tol` 而不是 `tol`**）、**倒序界被拒**、**非正容差被拒**、**九行真的注册**；**线性输入不建桥且不写行**（**同时看"无辅助变量"与"注册零行"——只看"注册成功"时多写一条等式也会通过**）、**非线性输入一条等式**、**负值不被截断**、**求值即多项式**。**变异：容差带并入"在外"（第 331 行 `return null` → `return Zero`）→ 红 1 条；线性输入也建桥 → 红 1 条。均已验。****判据自己错过两次**：**① 无界变量让大 M 变成 `9e15`，`1e-6` 的容差差被浮点精度吞掉**；**② 两行右端差算成 `tol` 而实际是 `2m − tol`（实测差 20）。** | ✅ |
| 二次掩码 `QuadraticMaskingRangeFunction`（1 个，**空壳转实体**） | — | ✅ | **B1158 按基线 `QuadraticMaskingRange.kt` 把第二个空壳换成真实编码。****四行**：**1 `y − p + M·z ≤ M`、2 `y − p − M·z ≥ −M`**（`z = 0` 时合起来给 `y = p`）；**3 `y ≤ M·z`、4 `y ≥ −M·z`**（`z = 0` 时把 `y` 钉在 0）。**1、2 在 `z = 0` 时给 `y = p`，而 `y` 本该是 0——所以 3、4 才是这一批的关键。****只有 1、2 而没有 3、4 时，"掩码关掉"这个语义完全失效，而模型仍然可行。****`z = 1` 时 1、2 给 `y = p`，3、4 松弛成 `y ≤ M` 与 `y ≥ −M`。**判据 `QuadraticMaskingRangeFunctionTests` **7 条**：**两个分支都测**（**只测一个时分支互换会通过**）、**二次输入（`x²`）也算对**、**`z = 0.5` 给 `null`**、**四行真的被注册**（**数模型约束条数 = 4**）、**四行的名字与方向固定**、**`z = 0` 时 3、4 把 `y` 夹成 0**（**看 `M` 的系数符号，**而不是只看行存在——后者会让右端写成常数的实现通过**）、**大 M 必须为正**（**`M = 0` 时 3、4 变成 `y ≤ 0` 与 `y ≥ 0`，掩码"永远关掉"，与 1、2 在 `z = 1` 时的要求冲突**）。**变异：只注册前两行 → 红 1 条（数出 2 而非 4）；分支互换 → 红 1 条。均已验。****同时把二次求值与区间推导抽成 `QuadraticBounds`**，`QuadraticMin` 的两份副本已收敛。**并从线性注册表移除本函数**：**它只在二次模型下有效，而那条判据用的是 `LinearMechanismModel`**——**`QuadraticMin` 之前"通过"是因为空多项式的 `SmallestLowerBound()` 返回 0 而非 `null`，那是偶然。** | ✅ |
| 二次下包络 `QuadraticMinFunction`（1 个，**空壳转实体**） | — | ✅ | **B1157 按基线 `QuadraticMin.kt` 把原本的**空壳**换成真实编码。****在此之前它是空壳**：**`Evaluate` 恒返回 `null`、`RegisterConstraints` 什么都不做。****它能编译、有名字、能进注册表，所以"类型存在"的扫描把它算作已覆盖——而它对任何模型都没有作用。****"实现存在"与"实现被使用"不是一回事；这里连"实现存在"都不成立。****编码两段**：**下界 `y ≤ pᵢ` 逐项总是有**；**上界 `y ≥ pᵢ − Mᵢ(1−uᵢ)` 与 `Σuᵢ = 1` 仅 `exact` 模式。****只有下界时 `y` 是"不大于所有 pᵢ"的任意值，求解器会把它推走或留在任意可行处，`y` 根本不是 min。****大 M 取 `Mᵢ = pᵢ.upper − minLower`**——**"第 i 项最多比最小值高出多少"，这是紧的；****全局常数会让界宽的项上界约束失效。** **`M = 0` 时取 1 的下限，否则上界行退化成 `y ≥ pᵢ` 与下界行一起把 `y` 钉死。****`Σuᵢ = 1` 不能少**：**没有它多个 `uᵢ` 可同时为 1，于是 `y ≥ pᵢ` 对所有 i 同时成立，`y` 被迫 ≥ max。****非精确模式不产生 `u`**——**否则它们被注册却永远不被约束，成为求解器里的自由变量。**判据 `QuadraticMinFunctionTests` **8 条**：**求值真的取最小值**（**空壳上直接红**）、**`Σuᵢ = 1`**、**下界行逐项取反并加 result**、**非精确模式无上界行也无 `u`**、**大 M 逐项不同**、**显式大 M 必须为正**、**空表被拒**、**注册真的把 5 条行写进模型**（**见下**）。**变异：删掉 `Σuᵢ = 1` 的注册 → 红 1 条（补判据前是 0 条）；`Evaluate` 改回 `null` → 红 1 条。均已验。****第一次删 `Σuᵢ = 1` 时 7 条全绿**——**因为其余判据只看 `OneHotRow()` 的**构造**而不看它有没有被**注册**。****"构造出一个正确的行"与"这个行被交给模型"是两件事，而空壳的特征正是后者缺失：它照样能构造、照样编译。** | ✅ |
| 互补约束 `ComplementarityFunction`（1 个） | — | ✅ | **B1156 补 `ComplementarityFunction`（基线 `Complementarity.kt`）。****六条约束、一个二值变量 `s`**：1–4 是两侧的域约束，**5 是 `x − xU·s ≤ 0`、6 是 `y + yU·s ≤ yU`。****第 5、6 行各自只有一个大 M 项，靠非负性把另一侧逼到 0**：**`s = 0` 时第 5 行给 `x ≤ 0`，配合 `x ≥ 0` 得 `x = 0`。****所以两侧区间必须非负**——**负的 `xL` 会让 `s = 0` 时 `x = 0` 不成立，于是互补性静默失效（两侧都能为正）。****两个大 M 的符号相反**（`−xU` 与 `+yU`）——**写成都用负号会让 `s = 1` 时 `y` 反而被放大。****`Evaluate` 返回的是违反指示而不是满足指示**：**两侧同时严格为正时返回 1**——**这是基线语义，也是最容易读反的一处**（**读成"满足时返回 1"就完全相反**）。判据 `ComplementarityFunctionTests` **7 条**：**两个大 M 符号相反**、**`s = 0` 时第 5 行把 `x` 逼到 0**（**同时测 `x = 0` 成立与 `x = xU` 不成立——**只测前者时一条平凡的 `x ≤ xU` 也能通过**）、**`s = 1` 时第 6 行把 `y` 逼到 0**、**`Evaluate` 四个点的违反指示**、**负的界被拒**、**越界输入给 `null`**、**四条域约束的端点**。**变异：第 6 行大 M 符号翻成负 → 红 2 条，已验。****同时把 `FiniteBoundsOf` 从 `Select`/`Element` 的两份副本收敛为 `PolynomialBounds` 一处**（**第三份出现时才抽，而不是第二份**）。 | ✅ |
| 两种定价：累进 + 全量折扣（2 个） | — | ✅ | **B1155 补 `IncrementalTariff` 与 `AllUnitsDiscountFunction`（基线 `Tariff.kt`）。****两者在第一个区间内完全相同**——**所以只测小值的判据分不开它们。****分开它们必须看断点之上**：99 件时两者都给 990；**100.001 件时累进给 1000.005、全量折扣给 500.005。****累进在断点处连续**（越过断点只有断点之后的部分享受新费率）；**全量折扣在断点处跳降**（整批改按新单价，100 件的总价可能低于 99 件）——**这不是 bug，是定义。****`boundaryGap = 0` 时断点本身属于低档**（与基线 `firstOrNull` 一致），**跳降发生在断点之上**。判据 `TariffTests` **10 条**：**跨断点比较**、**第二段内部取值点**（**见下**）、**第一个区间内两者相同**（**这说明上一条为什么必须跨界**）、**累进在断点处连续**、**全量折扣在断点处跳降**、**断点必须严格递增**、**费率数 = 区间数**、**负费率被拒**（**负费率是套利不是折扣，而分段函数照样构造成功、求解器照样报"更优"**）、**低于第一断点越界**、**`baseCost` 是起点而非每档附加费**（**加错位置会让第一档偏常数而后续增量仍正确，于是"增量对不对"的判据全绿**）。**变异（累进误实现成"每断点重算 数量 × 该段费率"）→ 红 2 条，已验。****但第一次只有 1 条红**：**那个变异在 99 与 100 处恰好与正确实现相同**（990 与 1000），**于是"跨断点比较"全绿，只有 `baseCost` 那条红** → **因此补了"第二段内部取值点"判据**（**150 处正确值 1250，误实现给 1000**）。 | ✅ |
| 表查找 `ElementFunction`（1 个） | — | ✅ | **B1154 补 `ElementFunction`（基线 `Element.kt`）。****编码三段**：① one-hot `Σ selector_i = 1`；② **每个表项一组屏蔽边**（与 `SelectFunction` 同模式，只是被屏蔽的是表项本身而非两分支之差）；③ **两条等式**——`index − Σ i·selector_i = lowerIndex` 与 `result − Σ z_i = 0`。**最容易漏的是 ③ 的第一条**：**没有它 `selector` 可以是任何 one-hot 组合，结果只是"某个表项的值"，与 `index` 完全脱钩——模型仍可行、目标值仍合理，只是答案与输入无关。****没有 one-hot 则 `Σ z_i` 把所有表项加起来**，结果等式仍成立而值是和的。**`lowerIndex` 是偏移不是截断**：`lowerIndex = 1` 表示下标从 1 起，内部槽位仍从 0 数——**搞混就是差一位，而"差一位"在任何"结果落在表项范围内"的判据下都不会红。**判据 `ElementFunctionTests` **8 条**：**逐个下标都测**（**只测一个时"永远返回第 0 项"会通过**）、**`lowerIndex` 偏移**（**同时测表内与表外四个下标**）、**下标等式按槽位加权**（`index` 系数 `+1`、`selector_i` 系数 `−i`、右端 `lowerIndex`）、**one-hot 恰好为 1**、**四条屏蔽边中 `lower`/`upper` 的位置与 `Select` 一致**、**`selector = 0` 时允许被屏蔽值为 0**、**`selector = 1` 时把值夹成表项值**、**连续下标被拒**（**连续下标会让 one-hot 加下标等式把 `index` 逼成整数，看起来"自动取整"，实际是模型替调用方做四舍五入**）。**变异：槽位权重全用 1 → 红；`lowerIndex` 未用于槽位查找 → 红（下标 1 得 7 而非 4），均已验。** | ✅ |
| 二值选择 `SelectFunction`（1 个） | — | ✅ | **B1153 补 `SelectFunction`（基线 `Select.kt`）。****编码的关键是"被屏蔽的差"**：令 `d = then − otherwise`、辅助变量 `z = mask·d`，于是 `result = otherwise + z`——**一条等式就够，不需要两条分支不等式。****`z = mask·d` 用四条边夹住而不是等式**：`z ≤ dU·mask`、`z ≥ dL·mask`、`z ≤ d + dL(1−mask)`、`z ≥ d + dU(1−mask)`——**`mask ∈ {0,1}` 时它们把 `z` 夹成恰好 `mask·d`。****`dL` 与 `dU` 的位置不能互换**：`zero_upper` 用 `dU`、`one_lower` 用 `dU`；**写错会让 `mask = 0` 时 `z` 被迫为负。**判据 `SelectFunctionTests` **8 条**：**两个分支都测**（**只测一条时分支互换会通过**）、**`mask = 0.5` 给 `null` 而非插值**（**四条边在 0.5 处仍可满足，给出看起来像插值的数，所以语义路径必须显式拒绝**）、**四条边的顺序与方向固定**、**`dL`/`dU` 位置是这两条边的指纹**（**用常数分支时 `dL = dU`，交换看不出来；所以改用 `then ∈ [1,10]`、`otherwise ∈ [3,4]` 得 `dL = −3`、`dU = 7`**）、**`mask = 0` 时四条边允许 `z = 0`**、**`mask = 1` 时把 `z` 夹成 `d`**、**结果等式是 `result − z − otherwise = 0`**、**连续选择变量被拒**。**变异：`zero_upper` 用 `dL` → 红 2 条；分支互换 → 红（`mask=1` 得 3 而非 10），均已验。****判据自己也错过一次**：**第一版把 `d` 的值同时代入 `x` 与 `y`，于是 `−x + y` 算成 7 而不是 0** → **改成分别代入 `x` 与 `y`。** | ✅ |
| McCormick 包络 + `ConditionBounds`（2 个） | — | ✅ | **B1152 补 `ConditionBounds`（基线 `Conditional.kt`）与 `McCormickEnvelopeFunction`（基线 `McCormickEnvelope.kt`）。****`ConditionBounds` 是 `SelectFunction` / `ElementFunction` / `McCormickEnvelope` 三者共同的入参类型，所以先补它并在同一批接上真实消费者**——**只加两字段 record 而没有使用者，那是骨架不是能力。****四条边的配对不是随便配的**：**两条 `≥` 用同角配对（`(xL,yL)` 与 `(xU,yU)`）、两条 `≤` 用交叉配对（`(xU,yL)` 与 `(xL,yU)`）；配错会得到一个不含 `x·y` 的多面体，松弛"合法"但无效，而求解器只会报告更好的目标值。****包络不是等式**：`w` 只在因子到端点时等于 `x·y`，内部是松弛的，**所以只能用于松弛不能用来算乘积**。判据 `McCormickEnvelopeTests` **7 条**：**四条边的顺序、名字与方向固定**（**顺序不稳会让逐条核对系数退化成"找到就行"**）、**右端项是配对方式的指纹**（`≥` 行 `−xL·yL`/`−xU·yU`、`≤` 行 `−xU·yL`/`−xL·yU`）、**每条边的系数是 `w − yScale·x − xScale·y`**（**两个缩放因子写反会让四条边对称化，于是 `x`/`y` 交换后结果不变**）、**四个角代入 `w = x·y` 时四条边全部成立**（**这是包络的定义性质**）、**结果范围是四个角乘积的包围**（`[-6, 8]`；**只用同角会得 `[3, 8]`**）、**越界输入返回 null 而非夹到端点**（**夹到端点给一个看起来合法的数，对应另一个输入**）、**`ConditionBounds` 拒绝上下界写反**（**空区间会让四条不等式互相矛盾，求解器只说"不可行"**）。**变异：`≥` 行改交叉配对 → 红（角点 `(-1,4)` 处 `lhs=-15` 违反 `rhs=6`）；缩放因子写反 → 红（系数 1 而非 3），均已验。** | ✅ |
| sigmoid 近似 + 零带载体（2 个） | — | ✅ | **B1151 补 `LogisticApproximationFunction`（基线 `NonlinearPiecewise.kt`）与 `ZeroBand`（基线 `ZeroBand.kt`）。****sigmoid 复用 B1148 的 `NonlinearSampling`**：`σ(z)`、`z = k(x−m)`。**中点搞错曲线平移、陡度搞错曲线变平或反号，两者都不报错**，**而"值都在 0 与 1 之间"对任何 sigmoid 形态的实现都成立、分不开它们。**判据 `LogisticAndZeroBandTests` **6 条**：**中点处恰为 0.5**（**`σ(0)=0.5` 对任何 `k` 成立，所以这条只测中点不测陡度**）、**陡度真的被用上**（**同一 `x` 上 `k=1` 给 0.2689、`k=4` 给 0.0180**）、**中点左移让同一 `x` 上的值变大**（**前一条固定中点改陡度、这一条固定陡度改中点；两条合起来才分得开"两个参数写反了"**）、**极端输入下有限且落在 [0,1]**、**两支在 0 处连续**、**`ZeroBand` 原样保留容差（不取绝对值）**。**变异：中点误用 → 红；陡度被忽略 → 红（0.2689 而非 0.0180），均已验。****"极端处仍可信"这条判据我第一版写错了**：**断言"永不取到 0 与 1"，实测 `σ(-800)` 下溢成 `0.0`、`σ(700)` 舍入成 `1.0`** → **改成只钉合约（有限、在 [0,1]），并明确写出：两支的好处在于中间量不是无穷，而中间量不可从结果观察，所以这条判据不声称验证了那件事。****`ZeroBand` 是纯数据载体、不校验**——**"负容差要拒绝"的校验留在使用点（`DeadZoneFunction` 已拒绝负 delta）**，**因为载体拿不到值转换器因而拿不到零。** | ✅ |
| 顺序统计量（2 个） | — | ✅ | **B1150 补 `KthLargestFunction` / `TopKSumFunction`**（基线 `OrderStatistic.kt`）。**两者都靠一个比较网络把输入降序排列**（每对比较 = 一个 `MaxFunction` + 一个 `MinFunction`），**所以"第 k 大"就是"排序后第 k 个"。****代价 O(n²)：n 个输入要 n(n−1) 个函数**——**这是基线的选择，本仓照做**，**因为顺序统计量没有更省的精确线性编码**（`ponytail:` 输入通常个位数；若上百需要的是排序网络而不是加大 bigM）。**边界差一是定义上的差别**：`KthLargest` 的 `k ∈ [0, n−1]`、`TopKSum` 的 `k ∈ [0, n]`——**"前 n 大之和"有意义（就是全部之和），"第 n 大"不存在**；**`k` 从 0 开始**（**从 1 开始会让 `k=0` 与 `k=n` 都越界，而越界检查会差一位**）。**`k = 0` 与 `k = n` 都跳过排序**，**省掉 n(n−1) 个函数的辅助变量与约束**（**基线就有的优化**）。判据 `OrderStatisticFunctionTests` **7 条**：**`k=0` 给最大值、`k=2` 给最小值**（两端同时钉住起点）、**"第 k 大"不是"前 k 大之和"**（**`k=1` 时 3 vs 8；两者共用网络，所以"取错了一行"在单条判据下可能通过**）、**两个边界同时测"允许"与"拒绝"**、**两端 `InnerFunctions` 为空而中间不为空**（**去掉分支后评估值仍正确、模型却多出无用变量**）、**`k=0` 的求和是常数 0**、**负值与重复值按值而非按绝对值降序**、**空输入被拒**。**变异：降序改升序 → 红（得 1）；`TopKSum` 两端也排序 → 红，均已验。** | ✅ |
| 距离函数（2 个） | — | ✅ | **B1149 补 `L1DistanceFunction` / `LInfinityDistanceFunction`**（基线 `CompositeFunctions.kt`）。**两者只差最后一步（求和 vs 取最大），所以"逐项相减再取绝对值"共享一份**（`DistanceTerms`），**而不是复制两遍。****唯一真正的坑是方向**：**`|a−b|` 与 `|a|−|b|` 在符号相反时符号相反**——**而"距离非负"这条不足以分开两者**（`a=3, b=-1` 时分别给 4 与 2，两者都非负）。判据 `DistanceFunctionTests` **6 条**：**符号相反仍为正**（**`|3−(−1)|=4`，`|3|−|−1|=2`**）、**L1 是多分量之和**（**2 与 5 贡献 → 7；若取最大则 5**）、**L∞ 取最大而非求和**（**同一组输入 → 5**；**两条判据用同一组数、期望不同，于是"两个函数实现成同一个东西"会立刻暴露**）、**分量数不等时抛异常而非按短的截断**（**截断让多出的分量静默消失、距离偏小，而"偏小"在任何"距离非负/有界"判据下都不会红**）、**零分量被拒**、**L∞ 的内层比 L1 多一个（取最大）**（**否则辅助变量与约束会缺一条，而评估值仍然正确**）。**变异：`|a−b|` 改成 `|a+b|` → 红（算得 2）；L1 求和改成取最大 → 红（得 5），均已验。****另一条变异（`Abs()` 前置）导致编译失败，不计为证据。** | ✅ |
| 非线性分段函数（3 个） | — | ✅ | **B1148 补 `LogFunction` / `PowerFunction` / `ReciprocalFunction`**（基线 `NonlinearPiecewise.kt`）。**三者只差一个源函数，采样逻辑共享一份**（`NonlinearSampling`），而不是复制三遍。**护栏分三条，而不是一条**：① `ln` 要求下界严格大于 0；② `1/x` 的区间不能跨过 0、也不能以 0 为端点；③ **`x^e` 的护栏是两条而不是一条**——**`e < 0` 且区间含 0**、**`x < 0` 且 `e` 非整数**；**把 ③ 写成"区间必须在 0 右侧"会把 `x²` 在 `[-1,1]` 上这种完全合法的情况拒掉，而平方与立方项在优化模型里很常见。****关键发现：`FromPoints` 自己用 `FunctionEvaluation.IsUsable` 拒绝非有限采样值**，**所以"删掉护栏判据仍绿"并不意味着护栏没用——它意味着那条判据测的是第三层。****变异实验证实：只删 `1/x` 与 `x^e` 的跨零护栏（`FromPoints` 保留）时判据变红**，**因为 `segments=2` 让采样点为 -1、0.5、2，没有一个落在 0 上、于是没有任何采样值是 ∞——这才是只有显式护栏能拦下的情况。**判据 `NonlinearPiecewiseFunctionTests` **9 条**：三条护栏各 1–2 条、**"该收的收了"**（**前五条都在测"该拒的拒了"，一个无条件失败的实现能让它们全绿**）、**采样端点精确落在区间端点上且值等于源函数**（**靠累加会差一点点，而断点必须严格递增**）。**两条跨零变异均已验红。** | ✅ |
| 混沌映射扩充之二（3 个 + 3 迭代器） | — | ✅ | **B1147 补 `RabinovichFabrikantAttractor` / `WindmiAttractor` / `HopfractionalMap` 与迭代器**，**至此 `math` 的 12 项真算法缺口全部补完。****三处坑都不会报错**：① **Rabinovich–Fabrikant 的 `γ` 只乘第一条方程，而两条都带 `xz` 项**——**把 `γ` 设成 0 时 `dy` 仍必须有 `xz`，这是能分开两种写法的判据**；② **Windmi 的 `dz` 里 `e` 乘的是 `x²`**，不是 `xz` 也不是 `xy`——**取 `z=0` 时 `xz=0` 而 `x²=4`，两者立刻分开**；③ **Hopfractional 的 `(k·x)^q` 在负底数配分数指数时是 NaN**——**基线不做保护，这里也不做，但把限制写下来，因为"轨迹突然全变 NaN"会被误当成发散**。**两个失效参数**：**Windmi 的 `g`（默认 0.5）、Hopfractional 迭代器的 `iterations`（默认 1000）**——**两者都在签名里、都不出现在任何方程里**（与 Sprott 的 `b` 同类），**判据钉住"改它不改变输出"，以免被当成旋钮。**判据 `RabinovichWindmiHopfractionalTests` **9 条**：**γ 不进第二条方程**、一步与手算一致、**e 乘 x²**、**g 不影响轨迹**、Windmi 一步与手算一致、**q=2 时负值可算**、**k≠s 时分子分母不抵消**（**前一条在 `k=s` 时任何忽略 k、s 的实现都会通过，必须另有一条**）、**iterations 不影响输出**、**迭代器先返回再推进**。**变异（`e` 改成乘 `x·z`）→ 红，`-0.008` 变成 `0.0`；变异（`γ` 泄漏到 `dy`）→ 红，均已验。** | ✅ |
| 混沌映射扩充（3 个 + 3 迭代器） | — | ✅ | **B1146 补 `SinaiMap` / `SprottAttractor` / `WangChenAttractor` 与迭代器**（基线同名 `.kt`）。**`math` 的 73 项未出现里，只有 12 项是真算法（6 张映射 + 6 个迭代器），其余是 KotlinX 序列化器（40 项）与类型别名——本仓用 `System.Text.Json`，序列化机制不同，不是缺口。****三处坑都不会报错**：① **Sinai 的 `mod 1` 是取小数部分，不是绝对值**——**坐标 -0.2 时取小数给 0.8，取绝对值给 0.2，环面上那是两个不同的点**；**两种写法都让结果"看起来正常"，只有钉住具体值才分得开**；② **基线 `SprottAttractor` 声明了参数 `b`（默认 28.0）而公式里从未使用它**——**保留是因为那是签名，但"改 b 不改变轨迹"必须可测**，**否则下一个人会去调 b 并以为在调系统**；③ **Wang–Chen 的 `dw = −yz` 里没有 `w`**——**写成 `dw = −δw` 会让 `w` 越大变化越快**，**而"轨迹有界"这种弱检查看不出来**。判据 `SinaiSprottWangChenTests` **7 条**：**负坐标映到 0.8 而非 0.2**、**两坐标留在 [0,1]**、**Sprott 的 b 不影响轨迹**、**Sprott 一步与手算一致**、**Wang–Chen 只差 w 的两个初值变化量相同**、**四分量与手算一致**、**迭代器先返回再推进**（**顺序反了会让整条轨迹偏移一步，而"有界/混沌"都不会红**）。**变异（`mod 1` 改成 `Abs`）→ 红，值跑到 1.0746；变异（`dw` 改成 `−δw`）→ 红 2 条，均已验。** | ✅ |
| **`quantities` 的 28 个"未出现"逐个核对：全是拼写差异，真缺口 0** | — | ✅ | **B1145 纯核对批次（不新增源码）。** **扫描器把"名字是否作为子串出现"当作"能力是否具备"**，于是基线 `BitPerSecond` 与本仓 `BitsPerSecond` 被判成缺失。**这个数字此前一直躺在清单里，看起来像 28 个待办——实际是 0 个。****逐个核对的结果：24 项是复数差异**（`KilogramPerLiter` → `KilogramsPerLiter` 等），**4 项是不规则复数**（`FootPerSecond` → `FeetPerSecond`、`InchPerSecond` → `InchesPerSecond`），**1 项是插入 `Of`**（`MillimeterMercury` → `MillimeterOfMercury`）。判据 `BaselineNamingVariantsTests` **4 条**：**每个对照名的目标单位真的存在**、**没有两个基线名字指向同一个单位**（**一个单位冒充两个概念是这张表能犯的最严重的错**）、**表长恰好 28**（**挡住"表被改了一半"——删一行不会让任何单位消失，前两条都不红**）、**归一化后两者必须相等**（**去掉 `Of`、按 CamelCase 分词、每词去尾部 `s`、`feet`/`inches` 折回**——**这正是这 28 对被挑出来的规则，也是唯一能挡住错配的一条**）。**变异（`FootPerSecond` 指向 `MilesPerHour`）→ 红 2 条，已验。****判据自身也错过一次**：第一版用了"本仓拼写必须更长"的规则，**被 `FootPerSecond`/`FeetPerSecond` 长度相同这一个反例打红**——**`foot → feet` 是元音替换，不是加后缀。** | ✅ |
| 面积扩充（4 个）+ Duration 转换器（2 个） | — | ✅ | **B1144 补 4 个面积单位与 2 个 Duration 转换器**（基线 `Area.kt` / `DurationExtensions.kt`）。**两个转换器对"负时长"的处理必须不同**：**`IntX` 能表示负数所以截断即可；`UIntX` 不能，所以负数必须拒绝**——**把 -5 秒变成 0 秒会让"早于基准点"的信息悄悄消失，而调用方看到一个合法的 0，无法区分"正好在基准点"与"早于基准点 5 秒"。****面积是长度的平方，所以小数点移两位对应 100 倍而不是 10 倍**——**把它当"像长度那样每级 10 倍"会让每级差一个数量级。****`SquareChain`/`SquareRod` 里的 "square" 修饰的是长度单位**：一个平方杆是 1 rod 见方（≈25.29 m²），**而平方链 = 404.68564 m² 恰好是十分之一英亩，这是英国土地测量的基础恒等式。**判据 `AreaAndDurationConverterTests` **9 条**：**平方链是十分之一英亩**、平方杆 = 1 rod 见方、**面积十进制阶梯每级 100 倍**、IntX 截断到整秒、**UIntX 拒绝负时长**、**IntX 接受负时长**（两条成对：都拒则"能表示负数"没用上，都收则 UIntX 溢出）、往返不丢整秒。**变异（UIntX 把负时长截成 0）→ 红；变异（平方分米用 0.1，即按长度处理）→ 红，均已验。** | ✅ |
| 角度 / 力 / 功率扩充（10 个）**+ 两处基线问题 + 一处同名不同值** | — | ✅ | **B1143 补角度(4 + 2 基线拼写别名) / 力(2) / 功率(4)。****两处基线问题**：① **基线写 `RightAngle = Radian / 4.0`，而直角是 π/2 弧度**——正确式子是 `RoundAngle / 4`；照抄得 0.25 rad（约 14.3°），**而"Gradian 是直角的百分之一"仍会绿（两者一起错，比值不动）**；② **基线写 `UKHorsepower = Watt * 550`，而 550 是 ft·lbf/s 的数值，漏了换算**（550 × 0.3048 × 4.4482216152605 = 745.69987 W），**它恰好等于机械马力。****一处同名不同值**：基线 `Horsepower` 是 735 W（公制），本仓的是 745.69987 W（机械），**同一个 `hp` 符号差 1.4%**——**本仓既有 `Horsepower` 保持机械马力不动（改掉会让所有既有调用静默偏移 1.4%），基线的 735 以 `MetricHorsepower` 名字加入。**判据 `AngleForcePowerUnitsTests` **10 条**：**90 度恰好是一个直角**（最强一条，**同时钉住 `Degree` 与 `RightAngle`，基线写法会红成 14.3 对 90**）、四个直角是周角、周角 2π、**百分度是直角的百分之一（单独不能发现基线错误，必须与上一条并存）**、毫弧度与拼写别名、克力是千克力的千分之一、千磅力是磅力千倍、**UK 马力 = 机械马力（钉 550 × 0.3048 × 4.4482216152605）**、**机械马力比公制大 1.4% 且名字可区分**、焦耳每秒/牛顿米每秒同值不同名。**变异（`RightAngle` 改回基线 0.25 rad）→ 红 2 条；变异（`UKHorsepower` 抄成 550 W）→ 红，均已验。** | ✅ |
| 电荷 / 电容 / 带宽 / 频率扩充 | — | ✅ | **B1142 补 `ElectricCharge`(6) / `Bandwidth`(5) / `Frequency.CyclePerHour`(1)，并新建 `CapacitanceUnits`(5)。****关键判断：基线把法拉系列写在 `ElectricCharge.kt` 里，却声明 `Capacitance` 量纲**——**电荷是 C，电容是 C/V，两者不是同一个量。跟着文件名会建成电荷单位，跟着声明的量纲才对。**所以法拉独立成 `CapacitanceUnits`，用 **`Coulomb.Divide(Volt)` 让量纲自己算出来**，而不是手写一个"看起来是电容"的包装。**`AmpereSecond` 与库仑数值相同却必须独立成对象**（电化学写 A·s、电学写 C）——**做成别名会让按名字查 A·s 得到 "coulomb"**，所以判据用比值 + 名字不同，而不是 `BeSameAs`。**`CyclePerHour` 是 1/3600 Hz**（基线 `Hertz * Second.to(Hour)`，而 `Second.to(Hour)` = 1/3600）——**每小时一次当然比每秒一次慢，方向必须是除。**判据 `ChargeCapacitanceBandwidthTests` **8 条**：**法拉量纲是 `Capacitance` 且与电荷不同**、法拉数值 = 库仑/伏特、电容阶梯每级 1000 倍（**pF 与 nF 是射频最常用量级，nano/pico 之间错一个数量级最贵**）、**安秒与库仑同值但名字不同**、安时阶梯、**KB/s 是 1000 B/s 不是 1024**（**与 B1138 那个 KB/KiB 错误同源，带宽从 `InformationUnits.Kilobyte` 派生，一条判据同时护住两处**）、带宽阶梯、**每小时一次比赫兹慢 3600 倍**。**变异（法拉改用电荷量纲）→ 红，量纲变 `I^1·T^1`；变异（`CyclePerHour` 改成乘法）→ 红，均已验。** | ✅ |
| 电学三件套：电流 / 电压 / 电阻 | — | ✅ | **B1141 新建 `CurrentUnits`**（基线 `Current.kt` 的四个单位本仓**一个都没有独立文件**，`Ampere` 只作为 SI 基准存在），并补 `Voltage.Megavolt` 与 `Resistance` 的基线命名别名 `Kiloohm`/`Megaohm`（本仓原用 `Kilohm`/`Megohm`）。**为什么三个一起补**：基线里 `Volt = Watt / Ampere`、`Ohm = Volt / Ampere`——**缺了电流，电压与电阻的推导无从谈起。****别名不是冗余**：**按名字查找的调用方只认其中一个拼写**；**而"数值相等、对象不同"在地图/字典按对象键查找时会变成两个条目**，所以别名用 `BeSameAs` 判据钉住而不是比值。判据 `ElectricalUnitsTests` **9 条**：电流阶梯每级 1000 倍、**安培就是 SI 基准本身（`BeSameAs`）**、**基线电阻拼写指向同一个对象**、电阻与电压阶梯每级 1000 倍、微伏/毫伏是十进制分数、**电压顺序单调**（**唯一能发现"某级用了 micro 而不是 mega"的东西**，**那种错误下数值仍然"是个电压"，只是方向错了**）、清单与基线一致。**变异（`Kiloohm` 另造一个 `DerivedUnit` 而非别名）→ 红，已验。** | ✅ |
| 压力单位扩充（5 个）**+ 拒绝照抄基线的一处错误** | — | ✅ | **B1140 补 5 个压力单位**（基线 `Pressure.kt`）：`Hectopascal` / `StandardAtmosphericPressure` / `MeterMercury` / `InchOfMercury` / `Millibar`（现存 8 → **13**）。**其中 `MeterMercury` 没有照抄基线，因为基线的公式与它自己的注释矛盾**：**基线写 `标准大气压 × 0.76`，得到 77007 Pa，于是 mmHg = 77.007 Pa；而它自己在上方注释里写着 "1 mmHg ≈ 133.322 Pa"。****0.76 米汞柱恰好压出一个标准大气压，所以要从大气压"除"回 0.76 米（133322.37 Pa），不是乘。**本仓按注释与物理事实实现（除法），**并让 mmHg 由 mHg 派生**，使"千分之一"成为恒等式而不是巧合（原值是字面量 133.322）。**这是本方向首个"基线自身有矛盾"的案例**——**同步不等于照抄：基线也是代码，也可能错。**判据 `ExtendedPressureUnitsTests` **9 条**：**米汞柱大于大气压**（方向判据）、**米汞柱 = 大气压 / 0.76**、**mmHg ≈ 133.322**（钉基线自己的注释，照抄公式会差 42%）、**mmHg 是 mHg 的千分之一（恒等式）**、**inHg ≈ 3386.39 且小于 mHg**、**毫巴 = 100 Pa = 巴的千分之一**、**百帕 = 毫巴**（气象领域两套说法同一大小）、**StandardAtmosphericPressure 与既有 Atmosphere 同值**（不能漂开）、清单覆盖。**变异（`MeterMercury` 改回基线的乘法）→ 红 2 条，已验。** | ✅ |
| 能量单位扩充（10 个） | — | ✅ | **B1139 补 10 个能量单位**（基线 `Energy.kt`，现存 7 个 → **16 个**）：`Gigajoule`/`Erg`/`BritishThermalUnit`/`WattHour`/`MegawattHour`/`GigawattHour`/`Kilo`·`Mega`·`Gigaelectronvolt`。**全是焦耳的固定倍数，风险不在公式而在三处语义**：**① `WattHour` 是 `KilowattHour` 的千分之一**，方向反了就大 10⁶ 倍，**而"千瓦时大于瓦时"这个定性判断仍然成立**——**所以判据必须钉比值，不能只钉大小关系**；**② `GigawattHour` 用 `Scale.mega` 而名字是 giga**——**因为基准是 `KilowattHour`（10³ Wh），10³ × 10⁶ = 10⁹**，**改成 `Scale.giga` 会得到 10¹² Wh，与名字差三个数量级而单位仍"看起来正常"**；**③ `Erg` 是 1e-7 J**，数量级最容易写错。判据 `ExtendedEnergyUnitsTests` **9 条**：**瓦时是千瓦时的千分之一**、**瓦时恰好 3600 J**（**掉一个零会变成瓦秒，而瓦秒数值上等于焦耳，判据会全绿**）、**吉瓦时是 10⁹ Wh**、**瓦时阶梯每级 1000 倍**、**尔格 1e-7 J 且大于电子伏**（**"erg 小于 eV"是个错误断言，而我第一版恰好写反了——它也会绿，因为读起来像补充说明**）、**BTU 用 IT 制 1055.06**（热化学制 1054.35，差 0.07%）、电子伏阶梯、焦耳阶梯、清单与基线一致。**变异（`GigawattHour` 改用 `Scale.Giga`）→ 红 2 条，已验。** | ✅ |
| **信息单位扩充 + 一个真实错误修复**（18 个） | — | ✅ | **B1138 补全 18 个信息单位**（基线 `Information.kt`），**同时修掉一个真实错误**：**本仓原来的 `Kilobyte` 用的是 1024（`Kibibyte` 的值），而符号写着 `KB`**；`Megabyte`/`Gigabyte` 同样错（1024²、1024³）。**基线 `Kilobyte` 是 `Scale.kilo × 8 = 8000`。****这类错误最难发现，因为"KB 是 1000 还是 1024"在业界本来就被混用，而"符号与数值对不上"从外部调用完全看不出来。**现在十进制（`KB`/`MB`/`GB`/`TB`/`PB`/`EB`）与二进制（`KiB`/`MiB`/`GiB`/`TiB`）两套都建全。判据 `InformationUnitsTests` **8 条**：**KB 是 1000 字节**（回归判据）、KiB 是 1024、**两套比值按 `1.024^n` 增长**、**十进制每级 1000 倍**、**二进制每级 1024 倍**、**比特单位不带 8 这个因子**（误乘 8 会让"1 kbit"变成 8000 bit 而所有字节判据仍全对）、**两套符号必须不同**（`KiB` 含 `i`）、清单与基线一致。**变异（KB 改回 1024，即原错误）→ 红 2 条，已验。****本批判据自己也算错过一次**：第一版写"每级都差 1.024"，实测第二级是 1.048576——**比值是 `(1024/1000)^n` 而不是常数，我凭直觉给了个常数。** | ✅ |
| 时间 / 质量 / 体积单位扩充（30 个） | — | ✅ | **B1137 补 `Time`(2：`Week`/`Year`) / `Mass`(12) / `Volume`(16) 共 30 个单位**（基线 `Time.kt`/`Mass.kt`/`Volume.kt`）。**这一批的风险集中在"同名不同量"**：**三套吨**（长吨 2240 lb ≈ 1016 kg / 短吨 2000 lb ≈ 907 kg / 公吨 1000 kg）——**公吨落在短吨与长吨之间，按"英制更大"的直觉去排会把公吨排最后，那是错的**；**两套盎司**（常衡 1/16 lb / 金衡 1/12 lb）——**分母更小所以金衡更重，与"金衡更贵所以更小"的直觉相反，金银报价用金衡，用错每盎司差 9.7%**；**两套加仑**（UK 4.54609 L / US 3.78541 L，比值 1.2009）——**而液量盎司方向相反：英制加仑更大而英制液量盎司更小，因为一英制加仑 160 盎司、一美制 128 盎司**。判据 `ExtendedTimeMassVolumeUnitsTests` **10 条**：三套吨互不相同且有序、**金衡比常衡重**、**UK 加仑大 20%**、**UK 液量盎司反而更小**、**年用儒略年 365.25 天**、周恰好七天、**升与立方分米相等**（两条构造路径必须落在同一个数）、三个类别清单与基线一致。**变异（UK 加仑改成美制值）→ 红，比值变 1.0；变异（金衡盎司改成常衡值）→ 红，均已验。****又一次踩到"因子是相对量不是绝对量"**：`Ton` 的因子是 1000 而非 1，与 B1127 的密度判据同一个坑。 | ✅ |
| 条件风险价值（`CvarFunction`） | — | ✅ | **B1136 补 `CvarFunction`**（基线 `Cvar.kt`，340 行），定义是 **`min over t of [ t + Σ p_i · max(L_i - t, 0) / (1 - alpha) ]`**——**在最坏的 (1-alpha) 尾部上的平均损失。****三个容易混淆的量**：平均值（全部情景）、VaR（一个分位点）、CVaR（尾部平均）——**判据钉的是"CVaR 严格大于平均值且不高于最坏情况"，这条同时否掉"写成平均值"与"写成分位点"。**判据 `CvarFunctionTests` **7 条**：**CVaR 落在平均值与最坏情况之间**、**所有情景相等时 CVaR 等于该值**（任何系数写错都会偏离）、**alpha 越大越靠近最坏**（把分母写成 alpha 会让趋势反转）、**alpha = 0 时等于平均值**（恒等式，同时验证分母与权重）、alpha 越界拒绝（**alpha=1 时 1-alpha 为零，除法不抛异常而是变 inf/NaN 悄悄污染目标函数**）、概率之和不为 1 拒绝、情景数与概率数不等拒绝。**变异（分母写成 alpha）→ 红 2 条；变异（去掉 `max(...,0)`）→ 红 2 条，均已验。****约束编码交给 `MaxFunction` 而不是把差值展开成线性项**——**展开会让"超出多少"变成"差值多少"，负的一半也算进去。** | ✅ |
| argmax / argmin（`ArgMaxFunction` / `ArgMinFunction`） | — | ✅ | **B1135 补两个**（基线 `OrderStatistic.kt`），复用已有 `MaxFunction` / `MinFunction` 的 `SelectorVars`。**与 `max`/`min` 的区别只有一个，而它足够让人写错：`max` 给值，`argmax` 给"哪个"。**下标 = **选择器变量的加权和**，第 i 个系数是 i，**第 0 个不参与**（系数 0，加进去是多一个零项）。**结果多项式常数项必须是 0**——**下标 0 对应"全是零"，不能靠常数项表达。**判据 `ArgExtremumFunctionTests` **7 条**：argmax/argmin 各自返回正确下标、**返回的是下标不是值**（候选值取 100/200/150，**最大值 200 远不是合法下标**）、**并列取先出现的**（同输入给不同下标 = 结果不可复现）、**结果多项式无常数项**、**第 0 个选择器不参与**、少于一个候选拒绝。**变异（返回最大值而非下标）→ 红 2 条，已验。****"返回下标不是值"这条判据的候选值必须选成"值域与下标值域不相交"**——**用 1/5/3 作候选，最大值 5 恰好是合法下标且正好是答案，写错也测不出来。** | ✅ |
| 固定费用函数（`FixedChargeFunction`） | — | ✅ | **B1134 补 `FixedChargeFunction`**（基线 `Tariff.kt`），形状是 **`cost = fixedCost · active + unitCost · activity`**，`active` 是 0/1 变量——选址、机组组合、批量折扣的起订费都是这个形状。**它和普通线性成本只差一个激活项**，而**少了那一项模型会说"不启用也免费"**：**目标函数看起来完全正常，只是每个设施都不开。**约束经 **`IndicatorSupport.AddConstraints`** 写入（不是直接往模型上加）——**它负责事务与"写完之后再校验界依赖"的顺序，直接加会绕过这两件事。**判据 `FixedChargeFunctionTests` **8 条**：不启用为零、**启用但空闲仍付固定成本**、启用且活动付两部分、**非 0/1 的启用值返回 `null`**（0.5 不是"部分启用"，是模型还不该被求值）、**未启用却活动返回 `null`**、**启用却超出区间返回 `null`**、**负固定成本当场拒绝**（会变成"启用反而省钱"，求解器把每个变量都打开）、**活动量只给一个界要拒绝**（缺一条边会让"启用但不活动"漏过去）。**变异（固定成本乘活动量而非启用变量）→ 红，`100` 变成 `0`，已验。** | ✅ |
| 死区函数（`DeadZoneFunction`） | — | ✅ | **B1133 补 `DeadZoneFunction`**（基线 `CompositeFunctions.kt`），形状是 **`max(|x| - delta, 0)`**——机械间隙、传感器死区、控制器容差带都是这个形状。**实现上不另写编码**：基线自己也是 `AbsFunction` 再 `MaxFunction` 拼的，**这里照做**——**手写会产生第二套 Big-M，而两套 Big-M 在同一模型里会互相挤掉对方的可行域。**判据 `DeadZoneFunctionTests` **6 条**：死区内为 0、**边界上恰好是 0**、超出后返回超出量、**函数关于 0 对称**、**负 delta 当场拒绝**、零 delta 退化成 `|x|`。**变异（去掉绝对值）→ 红 2 条；变异（去掉负 delta 护栏）→ 红，均已验。****"对称"那条是"负的一侧忘了取绝对值"的直接判据**：少了它负侧恒为 0 而正侧完全正常，**只测正数永远发现不了。** | ✅ |
| 指数函数的分段线性近似（`ExpFunction`） | — | ✅ | **B1132 补 `ExpFunction`**（基线 `NonlinearPiecewise.kt`），16 段等距断点，`FromPoints<V>` 构造。**它不是"指数函数"，是"指数函数在给定区间上的分段线性近似"**——**这个区别决定了两件事：断点上误差恰好为零，段中间有误差且段数越少越大。****所以"抽查几个断点"永远看不出段数不够。**判据 `ExpApproximationTests` **4 条**：断点精确等于 `exp`、**最宽段随段数变窄**、空/倒置/零段拒绝、断点数随段数变化。**变异（段宽不随段数变化）→ 红，已验。****一条重要约束已写进判据内文档**：`bigM` 为空时构造会推导输入多项式的有限界，**而无界变量推导不出界，构造就失败**——**实测带 `RealVar` 的输入会让 `CosFunction.Create` 也失败**，**所以"构造"与"对变量建模"是两步。****另记一次判据被推翻**：我最初写的"段中点误差随段数下降"**不成立**，**因为两段的中点 x=1 与六十四段的中点 x=1/32 不是同一个点，比的是 `exp` 本身而不是近似质量**，**实测粗分段"误差"反而更小**——已换成与 x 无关的"最宽段"。 | ✅ |
| **函数层已存在，缺的是单个函数**（B1130 结论的修正） | — | ✅ | **B1131 先验证 B1130 的结论，结果把它推翻了一半**：B1130 说"`core` 剩下的缺口不是少几个类，是少几层"，**而实测函数层已经存在**——`IMathFunctionSymbol`、`IHasResultPolynomial`、`FunctionSymbol.cs` 与 **`src/Core/Symbol/Function/` 下的 52 个函数类**（`Abs`/`And`/`Ceiling`/`Cos`/`Floor`/`Max`/`Min`…）都在，**缺的是具体函数，不是那一层。****所以补一个函数是小增量**：新增 `ClampFunction`（基线 `CompositeFunctions.kt`），**直接用已有的 `MaxFunction` 与 `MinFunction` 拼**（`max(input, lower)` 再 `min(…, upper)`，与基线一致），**不另写一套"夹"的编码**——**手写会产生第二套 Big-M，而两套 Big-M 在同一模型里会互相挤掉对方的可行域。**判据 `ClampFunctionTests` **6 条**：区间内原样、低于下界抬升、高于上界压低、**边界值本身不变**、**`lower > upper` 当场拒绝**（空区间会让整个模型不可行，而报错会指向求解器）、**`lower == upper` 合法**（"夹到定值"是正当用法）。**变异（去掉下界判断）→ 红 2 条，已验**；**另记：先试的"`<` 改 `<=`"不红，因为两者行为相同——那是无效变异，不是判据失败。****仍然未核实的**：`core` 其余未归类项、符号层的 42 个具体函数 | ✅ |
| 未出现类型的**成因分类**（`core` 166 个） | — | ◐ | **B1130 做的是"把 `未出现` 拆开"**：B1124 扫出 `core` 命中率 **74.3%**、166 个未出现，**而"未出现"这三个字掩盖了三种完全不同的情况——真缺、换了机制、改了名，只有第一种要补。**脚本 `logs/b1130-classify-absent.py` 按后缀归类（复用 B1124 的提取器，避免两处口径漂移）。**实测构成**：**符号层 42 个**（`*Function`/`*Materializer`，依赖 `MathFunctionSymbol` 一整套生命周期接口）、**泛型特化 14 个**（`Dyn*Variable`，**C# 直接用 `VariableCombination<T, TType, S>` 泛型表达，不需要为每种组合建类**，**已核实：`CombinationVariableItem`/`VariableCombination` 在 C# 各出现 10/5 次**）、**求解器绑定 5 个**（`*Snapshot`/`*Checkpoint`）、**未归类 105 个**。**已逐个核实的三处结论**：① `plugin` 的 82 个里 `PatternMatchPolicy` 等方法签名带 `ColumnDeclaring<*>`（**Ktorm 特有类型**），属 ORM 结构性差异；② `DynRealVariable` 是 `DynVariableCombination<Flt64, Continuous>` 的子类（**Kotlin 具名特化**），C# 用泛型承担；③ `IndicatorStructure` 207 行、`DiscreteCondition` 933 行，**都强依赖符号层，不是孤立类型。****这一批不补类型，只把"未出现"的成因量化**——**因为"105 个未归类"里有多少是真缺口，此前没人量过。** | ◐ |
| 其余七个量纲（一次补齐） | — | ✅ | **B1129 补齐 `quantities` 最后七个量纲共 19 个单位**：`SolidAngle`(2) / `Stress`(7) / `Wavenumber`(1) / `CatalyticActivity`(4) / `LuminousIntensity`(1) / `AngularVelocity`(2) / `AngularAcceleration`(2)，并补上 `Stress` 所依赖的四个面积单位（`SquareInch`/`SquareFoot`/`SquareYard`/`SquareMile`）。**三个"不是十进倍数"的换算都钉住了**：**`SquareDegree` 是 `(π/180)²`**（写成 0.0003 会让全天区覆盖从 41252.96 平方度变成 41887.9，差 1.5%）、**`EnzymeUnit` 是 `1/60000000` 开特**（1 U = 1 µmol/min，换算里同时有 10⁻⁶ 与 60）、**`DegreePerSecond` 与 `RadianPerSecond` 的比值是 `π/180` 而非 `1/360`**（差 6.28 = `2π`）。另有两条钉住"用对来源"：**`Candela` 必须 `BeSameAs(SIBaseUnits.Candela)`**（不是复制一个）、**`psi` 必须用 `PoundForce` 而非 `Pound`**（差 9.8 倍）。判据 `RemainingDimensionUnitsTests` **13 条**。**变异（平方度改 0.0003）→ 红；变异（酶单位改 1e6）→ 红，均已验。****至此 `quantities` 的 35 个基线量纲文件全部有对应。** | ✅ |
| 动量 / 力矩 / 面密度单位 | — | ✅ | **B1128 补三个量纲共 5 个单位**：`MomentumUnits`（**1 个**：`KilogramMetersPerSecond`，按 `kg·m/s` 而非 `N·s`）、`TorqueUnits`（**2 个**：`NewtonMeter`/`KilogramForceMeter`）、`SurfaceDensityUnits`（**2 个**：`KilogramsPerSquareMeter`/`GramsPerSquareMeter`，差 1000 倍而名字只差一个词）。判据 `MomentumTorqueSurfaceDensityUnitsTests` **5 条**，其中 **1 条是诚实记录的"做不到"**：**力矩与能量在 SI 里同量纲，且本仓 `EnergyUnits.Joule` 的定义正是 `Newton × Meter`**，**`DerivedUnit` 的名称又是构造时传入的字面量、与来源无关**——**所以"`NewtonMeter` 是不是指向了 `Joule`"在这个实现里不可观测。**试了三版（比 `Quantity`、比 `Name`/`Symbol`、比 `ConversionRule`）**都挡不住**，**已把结论写成判据内文档而不是留一条永远绿的断言。****变异（kgf·m 改用牛顿）→ 红，已验。****仍然未核实的**：`quantities` 其余 7 个量纲、`plugin` 的 82 个 | ✅ |
| 流量与质量密度单位 | — | ✅ | **B1127 继续补量纲**：新建 `FlowRateUnits.cs`（**3 个**：`CubicMetersPerSecond`/`LitersPerSecond`/`LitersPerMinute`）与 `MassDensityUnits.cs`（**4 个**：`KilogramsPerCubicMeter`/`KilogramsPerLiter`/`KilogramsPerCubicCentimeter`/`GramsPerCubicCentimeter`）。**这一批的两个风险都不是"算错"，而是"拿错"**：`LitersPerMinute` 与 `LitersPerSecond` **只差一个词，混用差 60 倍**；`GramsPerCubicCentimeter` 与 `KilogramsPerCubicMeter` **描述同一杯水，数值差 1000 倍**——**而 1 这个数看起来完全正常，只是单位错了。**判据 `FlowRateAndMassDensityUnitsTests` **5 条**：两组齐全且总数相符、**L/min 正好是 L/s 的六十分之一**、**g/cm³ 是 kg/m³ 的 1000 倍**、**升与立方厘米相差 1000 倍**。**变异（L/min 改用秒）→ 红；变异（g/cm³ 改用千克）→ 红，均已验。****仍然未核实的**：`quantities` 其余 10 个量纲、`plugin` 的 82 个 | ✅ |
| 速度与加速度单位 | — | ✅ | **B1126 接着核实 `quantities`**：基线的量纲有 **35 个文件**，本仓 `Quantities/Unit` 只有 **19 个类别**；其中 13 个量纲（`Acceleration`/`AngularVelocity`/`FlowRate`/`MassDensity`/`Momentum`/`SolidAngle`/`Stress`/`SurfaceDensity`/`Torque`/`Wavenumber`/`CatalyticActivity`/`LuminousIntensity`/`AngularAcceleration`）**在 `Dimensions.cs` 里已有定义，但一个单位都没建**——**"量纲有、单位缺"是一种不会报错的缺口。****本批补两个量纲**：速度 **4 → 14**（补 `CentimetersPerSecond`/`KilometersPerSecond`/`InchesPerSecond`/`FeetPerSecond`/**`Mach` 按 340.3**/**四个地区节**/**`LightSpeed` = 光秒每秒**）、加速度 **0 → 6**（新建 `AccelerationUnits.cs`，含 **`StandardGravity` 9.80665 定义值**）。判据 `SpeedAndAccelerationUnitsTests` **5 条**：14 个速度单位齐全且总数相符、**四个地区节的 `Scale` 求值必须等于对应海里除以 3600**、马赫用 340.3、光速等于每秒一光秒、6 个加速度单位齐全且 9.80665 精确。**变异（UK 节改用国际海里）→ 红，已验。****仍然未核实的**：`quantities` 其余 11 个量纲、`plugin` 的 82 个 | ✅ |
| 长度单位（`quantities` 里最大的一块） | — | ✅ | **B1125 逐条核实 `quantities` 的 167 个未出现类型，最大一块是长度单位**：**基线 `Length.kt` 有 33 个，本仓 `LengthUnits` 只有 10 个**，**而 `docs/` 里一次都没提过**——**"少 23 个"与"够用了"在文档上长得一样。**已补 22 个（`Picometer`/`Decimeter`/`Decameter`/`Hectometer`、**四个地区海里 FR 1853.27 / UK 1854.55 / RU 1855.78 / US 1851.01**、`Fathom`/`Cable`/`Chain`/`Rod`、`AstronomicalUnit` 149597870700、`LightSecond`/`Minute`/`Hour`/`Day`、**`LightYear` 用儒略年 365.25 天**、`Parsec` 30856775814913673 与三档 `kilo`/`mega`/`giga`）。**换算因子逐条抄自基线表达式，一个数都没有自己算。**判据 `LengthUnitsCoverageTests` **5 条**：33 个单位齐全且总数相符、**四个地区海里必须互不相同**、光年用儒略年、三档秒差距是千倍关系、天文单位是定义值。**变异（US 海里改成 1852）→ 红；变异（光年改成 365 天）→ 红，均已验。****仍然未核实的**：`quantities` 另外 145 个未出现类型、`plugin` 的 82 个 | ✅ |
| 类型面对齐（按**公开类型名**扫 22 个模块） | — | ◐ | **B1124 换了口径**：B1117 按文件数扫，**而文件数相等不代表内容相等**——一个文件可以有三个类型，也可以一个都没有。这一版提取基线每个模块的公开类型名（剔掉 `internal`），**逐一在本仓 `src/**/*.cs` 里查找**（`logs/b1124-type-coverage.py`，可复现）。**命中率**：`geometry2d` 100% / `network-scheduling` 98.8% / `csp1d` 95.7% / `example` 93.8% / `framework` 92.5% / `csp2d` 92.3% / `bpp2d` 91.2% / `bpp1d` 90% / `gantt` 89.2% / `multiarray` 88.9% / `math` 87.3% / `bpp3d` 76.8% / **`utils` 75.3%** / `core` 74.3% / `quantities` 45.6% / **`plugin` 18.8%**。**本批已修一处**：`Variant` 基线有 **2—20 共 19 档**，本仓只有 2—8，**已补齐 9—20**（`utils` 命中率 62.9% → 75.3%），判据 `VariantCoverageTests` **4 条**（档位到 20、参数个数与名字一致、`Match` 分支数与档位一致、**高元数真的构造并 `Match` 一遍**）；**变异（删掉 `Variant19`）→ 红，已验**。**未核实的**：`quantities` 45.6%（167 个未出现，多为单位类型）、`plugin` 18.8%（82 个，多为 H2/Ktorm/MyBatis/JDBC 后端）、`core` 74.3%（166 个）——**"未出现"不等于"缺"**，可能改名或合并，**每一条都要人工看** | ◐ | **B1117 评估过了，"未评估"这个状态消掉了，结论有两半**（计数可复现：`logs/b1117-module-sizes.py`）**。****① 谓词→查询的翻译能力在 C# 侧是有的，只是换了机制**：`SqlRelationalQueryCompiler`（**838 行**）覆盖全部 8 种布尔节点（`BooleanConstant`/`Comparison<>`/`InExpression<>`/`PatternMatch<>`/`NullCheck`/`And`/`Or`/`Not`），**14 条判据**（字面量参数化、通配符、空 IN、EXISTS 子查询、MySQL null 排序模拟、歧义列拒绝等）；EF Core 接表达式树，因此基线的"手拼 SQL 字符串"那一层（`JdbcClientSql.quoteIdentifierPath` 之类）**不需要对应物**。**② Mongo 翻译层 B1118 开始补**：`MongoScalarTranslator`（基线 209 行）已移植并**11 条判据**（`$字段` 引用、字段名解析、五个二元运算映射、`-x`→`$multiply:[-1,x]`、**`ABS`/`POWER` 按基线失败而非自作主张**、`null`→`BsonNull`、深度护栏失败而非漏出）；`MongoOrderByTranslator`（基线 83 行）**5 条判据**（升序 1/降序 -1、空排序→`null`、字段解析失败即失败、**多字段顺序保留**、`Nulls` 不静默忽略）；`MongoUpdateTranslator`（基线 120 行）**6 条判据**（`$set` 形状、**`SetNull` 写 `BsonNull` 而非 `$unset`**、空赋值→`null`、字段解析失败即失败、**整型保持 `Int32` 否则写进去查不出来**、**`SetFromExpression` 拒绝而非猜**）；`MongoBooleanTranslator`（基线 422 行，**四个里最大**）**14 条判据**：三值常量（`Unknown` 归"匹配不到"）、**常量在左时六个运算符全部反转**、引用在左不变、**null 检查同时管"是 null"与"缺失"**、正则元字符转义、**`Like` 先转义再替换通配符**、`$expr` 字段对字段、`NOT` 包整棵子树、不支持的谓词失败、**空逻辑在 C# 里构造不出来**（构造器拒绝空操作数，故基线那两段分支已删）。**四个 Mongo 翻译器现已全部到位**，判据合计 **38 条**；**B1122 把它们接进真实查询路径**：`MongoRepository.FindAsync(predicate, sortBy, resolver)`——在此之前仓库只会 `Filter.Eq("_id", …)` 与 `Filter.Empty`，**四个翻译器没有被任何生产路径调用过**。**三条判据**钉住最要紧的行为：**翻译失败不退化成"查全部"、不碰集合**（`FindCalls == 0`）、可翻译的谓词确实走到集合、排序失败同样不退化成无序结果。**明确的局限**：这条判据用探针复刻控制流形状（`IMongoDatabase` 有 40+ 成员，B1103 已踩过），**未连真实 Mongo 实例** | ❌ |基线 `persistence-mongodb` 有 `MongoBooleanTranslator`（**422 行**）、`MongoScalarTranslator`（**209**）、`MongoUpdateTranslator`（**120**）、`MongoOrderByTranslator`（**83**），**基线侧共 18 条离线判据**；**C# 里四个都没有，且 `src/` 下 `BsonDocument` 出现 0 次**——`MongoRepository` 只会 `Filter.Eq("_id", …)` 与 `Filter.Empty`。**同口径计数**：基线 `framework-plugin` **61** 个主源码 vs 本仓 `Framework.Plugin` **15** 个。**（旧文写"61 vs 48/53，命名口径差异不是缺口"——那是拿一个模块比全部插件项目，口径不同。）** | ❌ |
| 持久化：PostgreSQL | `Framework.Plugin` | ✅ | **B926 补**：`PostgreSqlRepository<>` + Npgsql 9.0.0；**连活库往返未验**（`⊘`） |
| 持久化表达式源生成 | `Framework.Plugin` | ✅ | `PredicateSchemaGenerator` |

`Framework.Bpp1d` 与 `Framework.Csp2d` 两个工程都在，**缺的只是 **starter 包**本身**（B918 实测；见四之三）。

## 四之一、CSP1D 细项

| 能力 | 状态 | 备注 |
| --- | --- | --- |
| 生成器接口与实现 | ✅ | `DFSGenerator`（153 行）+ `FullSumGenerator` 都在，共用 `GenerationSumSearch` |
| 统计字段与 KPI 键 | ✅ | 名字与基线一致 |
| **生产调用方** | **⚠️** | `FullSumGenerator` 有（`Demo3/ColumnGenerationDemo`）；**`DFSGenerator` 只有测试在用** |

**B946 撤回上一版整节。** 上一版写"`DFSGenerator` 是个空壳，B281 已删除"，**两句话都不对**：

| 上一版说 | 实测（B946） |
| --- | --- |
| `DFSGenerator` 是个空壳 | **153 行实现**，含 `satisfiesPruningConstraints` 与支配剪枝 |
| B281 已删除 | **文件在**，`FullSumGenerator` 直接调用它的 `GenerationSumSearch` |
| 没有任何使用者 | **有测试在用**（`Csp1dGeneratorPublicSurfaceTests`、`CrossContributionWiringTests`），只是**没有生产实例化** |

**这个区别是要紧的**：没有生产调用方是一个**接线缺口**（框架提供了两种初始模式生成器，示例只用了一种）；
空壳是**代码没写**。前者改一行接线就能验证，后者要写一整段算法——**把前者写成后者，会让人以为工作量比实际大得多，也就没人去接。**

判据 `CapabilityMatrix_ShouldStateTheCsp1dShellGap` 已同步翻转：它原来钉的四条里有三条钉的是错结论。

**B946 withdraws the whole of the previous section.** It said "`DFSGenerator` is a shell and B281 deleted it", and **both halves are wrong**: the file is **153
lines** of real implementation, `FullSumGenerator` calls its `GenerationSumSearch` directly, and while **tests do use it** (`Csp1dGeneratorPublicSurfaceTests`,
`CrossContributionWiringTests`) there is **no production instantiation**. **The distinction matters**: no production caller is a **wiring gap** — the framework offers two
initial pattern generators and the demo uses one — whereas a shell is **unwritten code**. The former is verified by wiring one line; the latter needs a whole algorithm
written. **Writing the former up as the latter makes the work look far bigger than it is, and nobody goes and wires it.** The criterion
`CapabilityMatrix_ShouldStateTheCsp1dShellGap` is flipped accordingly: three of its four pins asserted the wrong conclusion.
## 四之二、Demo2 细项

| | 基线 | C# |
| --- | --- | --- |
| 文件数 | **185** 个 `.kt`（`demo2/src/main/`） | **175** 个 `.cs`（**B927 实测**，脚本 `logs/b927-recount.py`） |
| 空壳 | — | **0** 个（**B945 更正**：`FlightPhase.cs` 不是空壳，见下） |
**B945 更正**：这里原来写"**1** 个空壳（`Domain/Aircraft/Model/FlightPhase.cs`）"。B945 去读了那个文件——
它是 `public enum FlightPhase { ZeroFuel, TakeOff, Landing }`，**三个成员一个不缺，能编译、被引用**。
它**不是空壳，是本仓新增的枚举**（基线没有对应物）。那个词把两件事混了：
类型齐全但没有基线对应物 = **新增**；类型体里一个成员都没有 = **空壳**。
判据 `ThereShouldBeNoEmptyTypeBodies` 钉后者，`TheFlightPhaseEnum_ShouldKeepItsMembers` 钉前者的三个成员。

**B1080 更正：脚本此前也把这个词用错了。** `logs/b927-recount.py` 里 `is_shell` 量的是
**"去掉注释后不足 200 字符"**——那是 **长度**，不是空壳。于是它报"1 个空壳"、
文档报"0 个空壳"，**两个数字看起来在打架，其实量的是两件事。**

现在拆成两个，且同时输出：

| 量什么 | 数 | 判法 |
| --- | --- | --- |
| **短文件** | **1** | 去掉注释与 `using` 后不足 200 字符（`FlightPhase.cs`） |
| **空壳** | **0** | 类型体里一个成员都没有 |

`--check` 现在打 `short=1 empty_types=0`，**行尾阈值只管前者**——它管的是长度告警，不是能力缺口。

**B945 correction**: this row used to say **one** shell. B945 read the file: it is `public enum FlightPhase { ZeroFuel, TakeOff, Landing }` — **three members, none missing,
compiles and is referenced**. It is **not a shell but an enum added here** (the baseline has no counterpart). The word conflated two cases: complete type with no baseline
original = **an addition**; a type body with no members = **a shell**. `ThereShouldBeNoEmptyTypeBodies` pins the latter and `TheFlightPhaseEnum_ShouldKeepItsMembers` the
three members of the former.

**B932 把整张逐项表按实测重写**（`logs/b932-audit.py`）：旧表在 B286 时把下面这些判成"仍是 `// TODO` 空壳"、"空类"、
"连一个成员都没有"，而实测**一个 `TODO` 都没有**、每个都有生产引用。

| 能力 | 状态 | 实测（`logs/b932-audit.py`） |
| --- | --- | --- |
| `Appointment` 预约映射 | ✅ | 85 行、0 个 `TODO`、9 处引用；**注意 `Position` 引用相等 vs 基线值相等** |
| `BiologicalLimit` 生物兼容性 | ✅ | 44 行、0 个 `TODO`、5 处引用 |
| `Flight` / `Payload` / `TotalWeight` / `MaxLoadWeight` | ✅ | 50 / 213 / 159 / 105 行，**0 个 `TODO`**，引用 25 / 23 / 16 / 6 |
| `Torque` / `MacModel` / `MacAggregation` | ✅ | 237 / 89 / 148 行，**0 个 `TODO`**，引用 17 / 1 / 4 |
| `StowageAggregation` / `PayloadMaximizationAggregation` | ✅ | 246 / 44 行，引用 19 / 2 |
| `SolutionAnalyzer` | ✅ | 252 行、2 处引用 |
| `PipelineListGenerator` | ✅ | 149 行、13 处引用 |
| **分区列生成分派** | ✅ | B928 分区配置 + B929 分派 + B930 目标展平；真 SCIP 实测 `partition_columns=5;iterations=4;root_lp_converged=True` |

**B932 撤回整张旧表**：保留这段说明是因为"曾经这么判过"要能审计，不是因为它还成立。

**一处仍然真实的缺口**：`Domain/Aircraft/Model/FlightPhase.cs` 是**唯一的空壳**（去掉注释后 135 字符），见缺口清单的 B927 实测。

**The whole table was rewritten from measurement in B932**: the old one judged these as "still `// TODO` shells", "empty classes", "not even one member" — while
the measurement finds **not a single `TODO`** and a production reference for each. The old table is void; the note stays **so the earlier judgement remains
auditable**. **One gap is still real**: `FlightPhase.cs` is the **only shell** (135 characters after stripping comments), per the B927 measurement.
## 四之三、Starter 包（**B287 前是空壳**；**B947 补齐四个**）

| Starter | 引用的工程 | 状态 |
| --- | --- | --- |
| `Starters.Core` | `Core` | ✅（B947 实测） |
| `Starters.Framework` | `Framework` | ✅（B947 实测） |
| `Starters.Bpp1d` | `Framework.Bpp1d` | ✅ **B947 新增** |
| `Starters.Bpp2d` | `Framework.Bpp2d` | ✅ **B947 新增** |
| `Starters.Bpp3d` | `Framework.Bpp3d` | ✅ |
| `Starters.Csp1d` | `Framework.Csp1d` | ✅ |
| `Starters.Csp2d` | `Framework.Csp2d` | ✅ **B947 新增** |
| `Starters.GanttScheduling` | `Framework.GanttScheduling` | ✅（B947 实测） |
| `Starters.GanttNetworkScheduling` | `Framework.GanttNetworkScheduling` | ✅（B947 实测） |
| `Starters.NetworkScheduling` | `Framework.NetworkScheduling` | ✅ **B947 新增** |

**B947 撤回上一版的表。** 上一版列了五行，其中 **`Starters.Bpp1d` / `Starters.Bpp2d` / `Starters.Csp2d` 三个目录根本不存在**，
却都标着 ✅；同时**漏掉了实际存在的 `Core` / `Framework` / 两个 Gantt 包**。

| 上一版说 | 实测（B947） |
| --- | --- |
| `Starters.Bpp1d` ✅ | **目录不存在**，B947 新建 |
| `Starters.Bpp2d` ✅ | **目录不存在**，B947 新建 |
| `Starters.Csp2d` ✅ | **目录不存在**，B947 新建 |
| （未列） | `Starters.Core` / `Starters.Framework` / 两个 Gantt 包一直存在 |
| （未列） | 基线还有 `starter-network-scheduling`，本仓没有，B947 新建 |

**这不是"文档写错一行"**：没有入门包，用这个模块的人得手写五条工程引用——**装不上**。
基线七个 starter（`bpp1d`/`bpp2d`/`bpp3d`/`csp1d`/`csp2d`/`gantt-network`/`gantt-scheduling`/`network-scheduling`）现在都有了对应物。
**B287 之前这五个 starter 是逐字节相同的 csproj**：`AssemblyName` 全是 `Fuookami.Ospf.Starters`，
`ProjectReference` 一个都没有。**同名程序集互相覆盖、装上去不传递任何模块**，而构建与测试全程绿灯。

**这一节钉的是历史那句**，不只是后果：只断言"现在对"会放过"把曾经的空壳写成一直正确"。

**B932 重建说明**：B932 编辑本文件时误删了这一节（替换区间起点选错），内容按
`CapabilityMatrixTests.CapabilityMatrix_ShouldStateTheStarterPackageGap` 与
`CapabilityMatrix_ShouldSayTheProjectsExistAndOnlyStartersAreMissing` 的钉子重建。

**Before B287 these five starters were byte-identical csproj files**: every `AssemblyName` was `Fuookami.Ospf.Starters` and not one had a `ProjectReference`, so
**identically named assemblies overwrote each other and installing one carried no modules** — while build and tests stayed green. **This section pins the history
sentence**, not just the outcome: asserting only "it is correct now" lets "always correct" replace "used to be a shell". **B932 rebuild note**: editing this file
accidentally deleted this section (wrong replacement start); the content was rebuilt from the pins in the two criteria named above.

## 五、Bpp3d 细项（P9 的重点）

| 能力 | 状态 | 备注 |
| --- | --- | --- |
| 装箱几何快照与重叠检测 | ✅ | B258 |
| 圆柱支撑覆盖（水平圆柱） | ✅ | B261 |
| 渲染 DTO 与装载计划 | ✅ | B262 |
| 重量贯通（件 → 箱） | ✅ | B263 |
| 宽相位扫掠（箱对剪枝） | ✅ | B265 |
| 箱容量约束右端项 | ✅ | B269 |
| 容量约束接入最终解 | ◐ | **前置校验，不是模型约束**（B270） |
| 箱深度约束右端项 | ✅ | B271 |
| 深度约束接入最终解 | ◐ | 同上（B271） |
| 影子价格感知评分 | ◐ | **只按件数计贡献**，重量型需求口径与基线不同（B273） |
| 圆柱半径回写 | ◐ | 无 PWL 体积路径；无"同键歧义"概念（B274） |
| 加载顺序约束 | ✅ | `BinLoadingOrderConstraint`（B633 落地，依赖 `PreciseAssignment` 已就位） |
| 尾箱分配约束 | ✅ | `TailBinAssignmentConstraint`，两支方向不对称是基线自带语义 |

## 六、示例、基准与文档

| 能力 | C# 项目 | 状态 | 备注 |
| --- | --- | --- | --- |
| CoreDemo / FrameworkDemo | `Example` | ✅ | Demo2 基线 **185** / C# **175** 个文件、**0 个空壳**（B945/B957）；Demo4 与 Demo5 各有独立清单判据 |
| Demo5 双语文档 | `Example` | ✅ | B268，由 `Demo5DocumentationTests` 钉住 |
| 基准套件 | `Benchmark` | ✅ | **20 个基准**（另有 1 个不计时的转储工具 `CrossVersionSemanticDump.cs`），与基线 15 个工作负载一一对应**（B952 补齐最后两个，B957 证伪了"依赖缺失/`internal`"这个理由） **B1299（复审换成 `19c72455`）**：**新基线比固定基线多 14 个文件，其中 6 个是新的 JMH 基准，全在 `ospf-kotlin-benchmark`，且**只依赖库内 API**——所以不属于"外部所限"，是**待移植**。** 已移植 1 个：`TokenInvalidateRebuildBenchmark`（B1299，3 个基准 × 3 档；large 档实测"重建 2.65 ms"对"稳态读 2.54 µs"，另有 `TokenIndexInvalidationTests` 两条行为判据钉住"结构变更后索引仍正确、被移除的 token 不再解析"）。**6 个新基准已全部有文件；下面这个的待办段在此点名（判据 `TheUnportedNewWorkloads_ShouldBeNamedInTheMatrix` 只钉"必须被写下来"，不要求代码在场）**：`TokenSharedReadBenchmark` **已移植**（B1302：三个读方法 + 空调度对照 + `ColdBuildWork` 诊断；基线的 `CountingVariable` 照搬为 `AbstractVariableItem` 子类并覆写 `Identifier` 计数；`TokenSharedReadBenchmarkTests` 5 条判据，其中一条钉住"一次完整建表的标识符读数必须不少于 token 数"——否则这个"建表工作量"读数会退化成装饰）、`PrimeMixedExpansionBenchmark` **已移植**（B1301，连同同一基线文件里的 `PrimeReaderLatencyBenchmark`：`CyclicBarrier` 用 .NET `Barrier` 对齐起跑；writer 是唯一扩容者、按 volatile 换代；判据 `ConcurrencyBenchmarkTests` 5 条钉住"混合批次的 reader 半边等于纯 reader""每种形态进度不同""reader 数真的改变负载""writer 在窗内重建过整代""`readyOnly` 不起 writer"；**能力差已写明：本仓基准运行器不产出 JMH `Mode.SampleTime` 的 p95/p99**）、`GenerationScaleBenchmark` **已移植**（B1300：3 个生成器 × 3 个场景 × 4 个并行度 × 2 个方案上限；`GenerationScaleBenchmarkTests` 的判据钉住"每个参数维度真的接到夹具上"——生成器看夹具类型、场景看 12 个物料上界是否两两不同、上限看方案数是否被截断）、`NetworkSharedGraphBenchmark` **已移植**（B1303：形态 × 规模 × reader 数，五个基准方法；`NetworkSharedGraphBenchmarkTests` 八条判据用精确等式而不是"大于零"——环形形态下每个 reader 的可达集必须正好等于节点数、反向视图出弧必须正好 4、空调度必须恰好发 0…n-1 号）、`CrossVersionSemanticDump` **已移植（三段）**（B1304：`NET`／`TOK`／`PRM` 已转储，`--dump` 走它而不计时；`CrossVersionSemanticDumpTests` 七条判据钉住"只含已实现段、布尔小写、非统计行两次运行逐行相同、待办清单与已实现段不重叠且六段齐备、核心数字是真值（π(20000)=2262）、投影段真结构、截断真的截断且规范键不是同一串"**；**B1309 起六段齐备：** `PRJ` 用 `ColumnProjection.SnapshotAsync`／`PricingRequestAsync`／`MasterAsync`，`GEN`／`TRUNC` 用 `Domain` 层的 `CuttingPlanCanonicalKey.From(plan)`——**两条"待办"理由实测都不成立，和 `PreciseAssignment`、"环境所限"是同一个错法。**可比性限制已写明：** 基线用 Kotlin `String.hashCode()` 做摘要，**跨语言不可比**，所以本转储不输出 `hashCode` 摘要——可逐行对比的是计数、排序后原值拼接与布尔量。 |
| 基准趋势脚本 | `scripts/` | ✅ | B264，`checks=29 failures=0` |
| 双语文档（仓库级 README） | 根目录 | ◐ | 已扩到 171 / 158 行；**主题覆盖而非逐行对齐** |
| 其余模块 README | — | ❌ | `src/Starters/*` **十份**（B947 补了 Bpp1d/Bpp2d/Csp2d/NetworkScheduling 四个启动器）；其余模块**未排期**写 README |
| 迁移指南 | `docs/` | ✅ | B276 |
| 能力矩阵（本文件） | `docs/` | ✅ | B277 |
**B960 用同一把尺子过了第四到第七节，四处过时**。前两处与 B955 抓到的是**同一句话的另外两份拷贝**：

| 行 | 原来写的 | 实测 |
| --- | --- | --- |
| 基准套件 | "**九个**基准已移植；两个依赖**缺失项目/`internal` 类型**" | **15 个**。两个"障碍"都不存在：`Framework.Bpp1d` 在、`DFSGenerator` 是 `public`，两个基准已在 B952 补上并实跑 |
| 持久化 | "**只验证了仓储接口**" | SQLite **有真实往返**（B950） |
| 其余模块 README | "只有 `src/Starters/*` **五份**" | **十份**（B947） |
| BPP 2D / CoreDemo | 备注**空着** | 两行都有可写的事实 |

**一个过时的判断会被抄进更多地方。** B955 只改了状态文档的 P9 摘要，**而矩阵里这条一模一样的判断还活着**——
两份拷贝里改一份，等于没改：**读者遇到不一致时不会去查哪份新，他只会信对自己更不利的那份。**

**B960 ran the same ruler over sections four through seven and found four stale rows.** The first two are **further copies of the same sentences B955 caught**: the benchmark row still
said "**nine** benchmarks; two blocked by a **missing project / `internal` type**" while there are **15** and neither obstacle exists; the persistence row still said "**only the repository
interface was verified**" while SQLite has a real round trip since B950. **A stale judgement gets copied into more places than one.** B955 fixed only the status document, **leaving an
identical judgement alive in the matrix** — and fixing one of two copies is not fixing it: **a reader facing the disagreement does not check which is newer, he believes the one that
costs him more.**


## 七、跨语言验证范围（**三处往返实测，六份夹具逐字节相同**）

| 项目 | 状态 |
| --- | --- |
| `SerializedSolution` 往返 | ✅ 实测 |
| `RemoteTaskSubmitRequest` 往返 | ✅ 实测 |
| `configuration` / `sha256` 摘要 | ✅ 实测 |
| **CP checkpoint envelope（`checkpoint-envelope-v3.json`）** | ✅ **逐字节相同**（B958 复核：1555 字节 / SHA-256 `8c252e59…`） |
| **checkpoint wire 契约表（`checkpoint-wire-contract.tsv`）** | ✅ **逐字节相同**（B958 复核 SHA-256 `6eaffdfb…`） |
| **analysis 语义契约（`analysis-contract.tsv` / `analysis-cases.tsv`）** | ❌ **本仓连文件都没有**（B957） |

**B951 把这三处的证据强度逐个查清**，并把"fixture 来自基线"这件事钉住：

| 项目 | 证据形式 | 强度 |
| --- | --- | --- |
| `SerializedSolution` 往返 | 四份 fixture **逐字节复制**自 `12fdb587`（B951 用 `git cat-file` 比对 SHA-256，**四个都 SAME**） | **强**：字节级相同，不依赖任何一侧重新编码 |
| `RemoteTaskSubmitRequest` 往返 | C# 产出 `submit-request-csharp.json`，**由固定 Kotlin 侧 `ignoreUnknownKeys = false` 严格解码通过** | **强**：严格模式拒绝未知字段，通过即证明字段集一致 |
| `configuration` / `sha256` 摘要 | C# 与固定 Kotlin 侧对同一输入**实测一致** | **中**：只覆盖这一个输入，不是全部配置空间 |

**B958 把夹具数了一遍：六份逐字节相同，不是四份。** B951 只钉了 `RemoteFixtures/` 下那四份，
而 `Core.Tests/Fixtures/analysis-fixtures/` 下那两份**同样是逐字节复制**的，一直没被算进这句话里。

| 夹具 | SHA-256（前 16 位） | 两侧 |
| --- | --- | --- |
| `remote-cp-result-v1.json` | `58fc538acb808728` | **SAME** |
| `remote-cp-result-v2.json` | `ebad2593bdd9279b` | **SAME** |
| `remote-linear-model-v2.json` | `71a086af6dcac0e0` | **SAME** |
| `remote-linear-result-v2.json` | `e76a8e93e75046b7` | **SAME** |
| `checkpoint-envelope-v3.json` | `8c252e592cfa26cb` | **SAME**（B958 新增钉） |
| `checkpoint-wire-contract.tsv` | `6eaffdfbdbbb8521` | **SAME**（B958 新增钉） |

**"只有这三处实测"这句话低估了自己的证据**，而低估的代价与高估一样真实：
4.8 那一行早就写着"fixture 与基线**逐字节相同**"，**而第七节还在说只有四份**——两处的账对不上，
下一个人只会信更保守的那个，然后**重做一遍已经做过的事**。

**B958 counted the fixtures: six are byte-identical, not four.** B951 pinned only the four under `RemoteFixtures/`, while the two under
`Core.Tests/Fixtures/analysis-fixtures/` are **equally byte-identical copies** and were never counted. **"Only these three are measured" understated its own evidence**,
and understating costs as much as overstating: row 4.8 already said the checkpoint fixture is **byte-identical**, **while section seven still spoke of four** — the two
accounts disagree, and the next reader trusts the more conservative one and **redoes work that was already done.**
**B951 之前，"逐字节复制"这句话没有任何判据守护。** 改一个空格，几十条解码判据照样全绿——
它们测的是"这份文件能解码"，**不是"这份文件来自基线"**。现在四个 SHA-256 钉住了。

**除此之外没有任何跨语言互通或性能结论。** 基准夹具只证明"夹具被覆盖"，不证明性能。

**B951 measured how strong each of the three pieces of evidence is** and pinned the claim that the fixtures come from the baseline. Before B951 that claim was
**unguarded**: one edited space would void the cross-language evidence while every decoding criterion stayed green, because they test "this file decodes", **not "this file
came from the baseline"**. Four SHA-256 values now pin it. **Apart from these three there is no cross-language interoperation or performance conclusion** — the
benchmark fixtures only prove the fixtures are covered, not performance.

## 八、外部未验收项（**单列，不计入上表定级**）

上表的定级依据是"代码在不在、接口通不通、测试覆盖到哪"。**下列项目需要外部环境或真实后端**。

**B949 把这张表整个重测了一遍**（逐个 `Run*Integration=true` 真跑），**四条从"未验收"变成"已验收"**：

| 项目 | 需要的条件 | 现状 |
| --- | --- | --- |
| SCIP 真实求解 | 自带原生运行时 | **✅ 已验收**（46 个门内文件，每次门禁真跑） |
| Gurobi 真实求解 | 商业许可 + 原生运行时 | **✅ 已验收（B949）**：`RunGurobiIntegration=true` 跑出 **829 通过** |
| CPLEX 真实求解 | 商业许可 + 原生运行时 | **✅ 已验收（B949）**：**26 通过** |
| COPT 真实求解 | 许可 + 运行时 | **✅ 已验收（B949）**：**37 通过** |
| Mosek 真实求解 | 许可 + 运行时 | ❌ **SDK 在，licence 缺**：`err_missing_license_file(1008)` |
| MindOpt 真实求解 | 许可 + 运行时 | ❌ **SDK 在，licence 无效**：`License is not valid` |
| Hexaly 真实求解 | 许可 + 运行时 | ❌ **SDK 不在本机**（`HEXALY_HOME` 未设） |
| Gurobi 11 真实求解 | 同上 | ❌ **未试**：`Gurobi110ManagedAssembly` 未设，走的是 10.x 绑定 |
| 数据库持久化往返 | 真实 H2/MySQL/PostgreSQL/MongoDB/Redis/SQLite 实例 | **SQLite 有真实往返**（B950：**2 条**判据、**文件库** + 关连接再开——`:memory:` 会随连接消失，用它测"落盘"等于没测）；**MySQL / PostgreSQL / Kafka 在 B1307 补了真实往返（各三条判据）**（见下）；**MongoDB 未测**（无实例）；**Redis 已实测**（B1298，4 条真实往返 + 2 条默认构建判据，见上） |
| 消息插件往返 | 真实 Kafka broker | ✅ **B1307：三条判据**（生产→消费且键值都对 / JSON 载荷往返 / 不可达 broker 在超时内返回空而不挂住）；**同时修掉 `Dispose` 不 flush 导致的消息静默丢失** |
| 跨语言互通 | 两侧同版本同时运行 | **只有三处实测**（见第七章） |
| 性能对比 | 两侧同夹具同机器 | **仍然完全没有**，但**夹具覆盖已补齐**（B952）：基线 15 个工作负载、本仓 15 个，两个新基准实跑通过 |
**B952 只补了夹具，没有做任何性能结论。** 这两件事必须分开：

| 做了什么 | 能得出什么 |
| --- | --- |
| 补齐两个缺失的基准工作负载，并各实跑一次（`small 1 1 1`，各 6 个基准） | **夹具覆盖齐了**，性能**仍然一句没说** |
| — | 同夹具同机器的两侧对比**仍未做**：需要同时跑 Kotlin 侧，而本仓库不切那个 checkout |

**夹具覆盖是性能对比的必要条件，不是它本身。** 补齐之前，"性能没测过"和"没有可比的东西"分不开；
补齐之后只剩前者——**而前者正是这条偏差的本意。**

**B952 only filled in the fixtures; it draws no performance conclusion.** Those are separate: the two missing workloads now exist and each ran once (`small 1 1 1`,
six benchmarks each), so **the fixture coverage is complete and nothing has been said about performance**. A same-fixture same-machine comparison **still has not been
done**: it needs the Kotlin side running too, and this repository does not switch that checkout. **Fixture coverage is a precondition for a comparison, not the
comparison** — before this, "performance was never measured" and "there is nothing to compare" were indistinguishable; now only the former remains, **which is what
the deviation was about.**
**B950 探测了本机的数据库与 broker**（`logs/b950-probe.md` 记了完整过程），结论和求解器**恰好相反**：

| 目标 | 端口 | 结论 |
| --- | --- | --- |
| SQLite | — | **✅ 不需要服务器**，`:memory:` 真库往返，7 条判据 |
| MySQL | 3306 **开着** | ✅ **B1307 真实往返（3 条判据）**：入口连通 / 增查改删 / 不可达报原因；账号 `ospf_test` 由 `C:\environment\infra\ospf-test-setup.ps1` 自建，口令不进仓库 |
| PostgreSQL | 5432 **开着** | ✅ **B1307 真实往返（三条判据）**，同 MySQL |
| MongoDB | 27017 **开着**（B1308 起的容器 `ospf-infra-mongo`） | ✅ **B1308 真实往返（三条判据）**：`ping` 真连通 / 插入-按主键查-替换-删除 / 不可达报原因 |
| Redis | 6379 **开着** | ✅ **B1298 真实往返（4 条判据）** |
| Kafka | 9092 **开着**（B1307 起的单节点容器 `ospf-infra-kafka`） | ✅ **真实往返（三条判据）**；**并且据此修掉一个真缺陷：`KafkaProducer.Dispose` 不 flush，排队消息静默丢失、topic 偏移停在 0** |

**这段文字原来是"门没开"与"钥匙没有"的对比，而 B1307 证明后者也是错的：** **钥匙一直在 `C:\environment\infra\.env` 里，只是没人去看；**
**Kafka 也不是"没有实例"，而是没人起。** **所以这里更正成：** **B1298 与 B1307 之后，MySQL、PostgreSQL、Redis、Kafka 四个后端都有真实往返判据；
B1308 起了一个本机容器，MongoDB 也补上了三条判据（见下一节）。**

**This paragraph used to contrast "the gate was not open" with "the key is missing", and B1307 shows the second one was wrong too: the key had been in
`C:\environment\infra\.env` all along and nobody looked, and Kafka had never been started rather than being absent.** **After B1298 and B1307, MySQL,
PostgreSQL, Redis and Kafka all have real round-trip criteria; MongoDB still has no instance and is still unverified.**

**B950 probed this machine's databases and broker**, and the answer is **the opposite of the solver case**: SQLite needs no server and does real round trips through
`:memory:`; MySQL and PostgreSQL **are running** but reject every credential this machine has; MongoDB and Kafka have no instance. **The solver case was "the gate was not
open"; this one is "the key is missing"** — the first is one flag away, the second needs credentials **this machine does not have**, and nobody could run it here.


**上一版把前四行全写成"未验收"。** 那不是"保守"，是**没去开门就把门当成锁着的**：
五个 SDK 都装在本机，四个有可用 licence，而集成门只需要一个 `-p:Run*Integration=true`。

**B949 re-tested this whole table** by opening each gate, and **four rows moved from unverified to verified**. The previous version marked all four unverified — not
conservatism but **assuming a closed door is locked without trying the handle**: five SDKs are installed here, four have a working licence, and the gate only needs
`-p:Run*Integration=true`.
**这张表的意义**：上表里那些"✅"与"◐"回答的是"代码在不在"，
而这里回答的是"我们真的跑过它吗"。**两个问题的答案不一样**。

## 九、已知的系统性偏差

这些偏差**不是某个模块的缺口，而是全局性的**。**B933 逐条实测**（`logs/b933-deviations.py` 与下面的证据），
能证的改成实测、证不了的写明**为什么证不了**——**避免下一个人再花一批去试同一件事**。

| # | 偏差 | 实测结论 | 证据 |
| --- | --- | --- | --- |
| 1 | **前置校验 vs 模型约束** | **仍然成立**（B948 复核：**11 个校验点**） | `Framework.Bpp2d` 里 **11 个文件**在求解前报 `IllegalArgument`，而不是把线性约束加进 `MetaModel`（第 12 个是 `Domain/Bpp2dErrors.cs`，它只**映射**错误码，不是校验点）。两者在"非法数据被拒"上相同，**在优化结果上不同** |
| 2 | ~~`PreciseAssignment` 模型缺失~~ | **已撤回（B918）** | 模型在 `Framework.Bpp2d` / `Framework.Bpp3d` 的 `LayerAssignment/Model/` 下都有 |
| 3 | **PWL 体积路径缺失** | **仍然成立**，且 **B954 查明缺口比这条写的大得多** | 见下 |
**B954 查清了第 3 条的真实形状。** 这条原来只说"`PackingRendererAdapter` 里 0 处 `PWL`，半径结果没进渲染"——
**那是一个症状，不是一个缺口的大小。** 实测：

| 层 | 基线 | C# |
| --- | --- | --- |
| 分段线性**符号** | `UnivariateLinearPiecewise` 等 | **有**（`Core/Symbol/Function/`，且 Demo2 的 `Envelope`/`StowagePosition` 真在用） |
| 函数**展开层** | `FunctionExpansion.kt` **576 行** | **完全没有**：`Core/Solver/Function/` 只有 `FunctionSolverCapabilities.cs` 一个能力声明文件 |
| 插件**原生写入** | Copt 22 处、Cplex 6 处 `PWL` | **没有**；Gurobi 只有**探测**（反射 `SetPWLObj`/`AddGenConstrPWL`） |

**"探测未移植"这个说法把三件事混成了一件。** 探测是最容易的一层，而它已经在了；缺的是中间那 576 行。
一份把缺口语焉不详地写成"未移植"的文档，会让下一个人以为补上反射就够了——**那正好是最不缺的一层。**

**B954 pins down what item 3 really is.** It used to say only "no `PWL` in `PackingRendererAdapter`, the radius result never reaches rendering" — **a symptom, not the
size of the gap**. Measured: the piecewise **symbols** exist here and Demo2 really uses them; the **function expansion layer** (`FunctionExpansion.kt`, **576 lines**)
is **entirely absent** — `Core/Solver/Function/` holds only a capability declaration — and the plugins have no native writes (Copt 22, Cplex 6 in the baseline), while
Gurobi has only **detection**. **"Detection not ported" conflated three things**, and detection is the easiest layer and is already here. A vague "not ported" lets the
next person believe adding reflection is enough — **which is exactly the layer that is not missing.**

| 4 | **通配符泛型缺失** | **仍然成立**（B948 更正：**11** 个源文件，不是 13） | `MultiArray` **11 个**源文件里 **5 个**用 `where` 约束表达 Kotlin 的协变/逆变；C# 没有声明点变体 |
| 5 | **原生后端行为未验证** | **收窄了，但没消失** | 见下 |
| 6 | **`core.analysis` 仍缺多数分析器** | **B1023 更新：`effectiveDomain` 已搬（二值性是类型不变量）；三条主链的编排只剩 `analyze` 类本体（需真实后端）** | 见下 |
### 第 5 条：**四个后端在这个环境里真跑过**（B949 实测，不是推测）

**B949 把六个插件的集成门逐个真跑了一遍**，结论推翻了两版措辞：

| 插件 | SDK 在本机 | 集成门结果 | 证据 |
| --- | --- | --- | --- |
| SCIP | 自带 | **✅ 真跑** | 46 个门内文件，每次门禁都跑 |
| Gurobi | ✅ `$GUROBI_HOME` | **✅ 真跑，829 通过** | B949 实跑 |
| Cplex | ✅（`CplexManagedDirectory` 默认路径） | **✅ 真跑，26 通过** | B949 实跑 |
| Copt | ✅ `$COPT_HOME` | **✅ 真跑，37 通过** | B949 实跑 |
| Mindopt | ✅ `$MINDOPT_HOME` | ❌ **licence 无效** | `License is not valid` |
| Mosek | ✅ `$MOSEK_10_2_INSTALLDIR` | ❌ **licence 缺失** | `err_missing_license_file(1008)` |
| Hexaly | ❌ `HEXALY_HOME` 未设 | 不构建 | `HexalyManagedAssembly` 解析成 `/bin/Hexaly.NET.dll`，不存在 |
| Lingo / OptVerse | — | **没有真后端门** | 只有"不可用"路径 |

**上一版说"只有一个后端真跑过"，那是错的。** 真相是：**五个 SDK 都在本机**，其中四个有可用 licence，
两个没有。之前的措辞把"门没打开"当成了"跑不动"——**门是要显式开的（`Run*Integration=true`），没人开就等于没跑，但那不等于跑不动。**

**这处错误的方向和前两批相反**：B948 那次是把验证写少了，这次是把"能验的"写成了"验不了的"。
**两种都会让下一个人做错决定**——前者以为没测过要重测，后者以为测不了就不去测。

**B949 ran all six plugin integration gates.** The previous version said only one backend really runs here; **that was wrong**. Five SDKs are on this machine and
four have a working licence. **The old wording mistook "the gate is not open" for "it cannot run"** — the gate needs `Run*Integration=true`, and nobody opening it means
it did not run, **which is not the same as cannot run**. This error points the opposite way from B948: that one under-reported verification, this one wrote "verifiable" as
**"unverifiable"**. **Both make the next person decide wrongly** — the first makes them re-test what was tested, the second makes them skip what they could have run.
**B948 逐条复核了上表**（`python logs/b948-gates.py`），四处的数是错的，已改：

| 项 | 原写 | 实测 | 为什么会错 |
| --- | --- | --- | --- |
| 第 1 条校验点数 | 11 | **11**（成立） | 但没写"排除了错误码映射文件"，别人复现会数到 12 |
| 第 4 条 `MultiArray` 文件数 | 13 | **11** | 多算 2 个 |
| 第 5 条 SCIP 门内文件 | 10 | **46** | **少算 36 个**——这是本节最离谱的一处 |
| 第 5 条 Cplex 门内文件 | 3 | **2** | 多算 1 个 |

**SCIP 那处错得最要紧**：它把"本环境唯一真跑的后端"的覆盖面写小了近五分之四。
一份把实际验证写得**比真少**的文档，会让人以为"这里几乎没测过"——**和夸大一样有害，只是方向相反。**

**B948 re-checked every row above**, and four numbers were wrong: the `MultiArray` file count (13 → **11**), the SCIP gate's file count (10 → **46**), the Cplex
gate (3 → **2**), and item 1 needed its exclusion stated (the twelfth file only **maps** error codes). **The SCIP number mattered most**: it under-reported the coverage of
**the one backend that really runs here** by nearly four fifths. A document that understates what was verified reads as "almost nothing was tested here" — **as harmful as
exaggeration, just in the other direction.**

**所以准确的偏差是**：真后端行为**在 SCIP 上验证过**（门禁每次跑），**其余六个插件只验证了"没有 SDK 时显式不可用"**，
`Lingo` / `OptVerse` 连门都没有。**"验证过"与"能验证"是两件事，原措辞把后者说成了前者。**

### 为什么第 5 条**不能**在这个环境里补完

缺的是**外部许可与 SDK**，不是代码：Gurobi / Mosek 要 licence，Cplex / Copt / Mindopt / Hexaly 要商业 SDK。
**写一个"假后端"来验证真后端行为等于什么都没验证**——那正是 B887 记过的那类缺陷（假脚本满足下界却不满足约束，看起来收敛）。
所以这条留在 `## 八、外部未验收项`，**不改成 ✅**。

**These deviations are global rather than module-level. B933 measured each one**: what can be evidenced becomes a measurement, and what cannot says **why**,
so nobody spends another batch on it. **Item 5's original wording was wrong** — it claimed "every solver plugin only verified the no-SDK path", while in fact every
plugin has a real-backend gate; what is true is that **only SCIP actually runs in this environment** (10 gated files, exercised by every gate), the other six only
prove "explicitly unavailable without an SDK", and Lingo/OptVerse have no gate at all. **"Verified" and "verifiable" are different things, and the old wording
confused them.** It cannot be completed here because what is missing is **external licences and SDKs, not code** — and a fake backend verifies nothing, which is
exactly the defect class B887 recorded. So it stays in section eight, **never marked ✅**.

### 第 6 条：**`core.analysis` 在文档里只出现过一次，而那一次说的是"criticality profile"（B957）**

B957 数了两侧：

| 侧 | 文件数 | 说明 |
| --- | --- | --- |
| 基线 `core/analysis/` | **19** | `ActivityEvidence`、`ConflictAnalyzer`、`ConstraintActivityAnalyzer`、`TargetFeasibilityAnalyzer`、`FixedIntegerLpSensitivity`… |
| 基线插件分析器 | **2** | `GurobiCriticalConstraintAnalysis.kt`、`ScipCriticalConstraintAnalysis.kt` |
| C# `Core/Analysis/` | **3** | `AnalysisStatus`、`CriticalityProfile`、`ObjectiveTarget` |
| C# 插件分析器 | **2**（B1280／B1281 各接一端） | **Gurobi 的 RHS 区间已实现**（B1282：主求解里 `Presolve = 0` + `Method = 1` 读 `SARHSLow`／`SARHSUp`，按行序配对，`±inf`／`NaN`／哨兵折算成无边），**真跑判据 `GurobiSensitivityRangeIntegrationTest` 断言区间夹住 RHS**；**扰动已两端接入**（B1283：`GurobiPerturbationBackend` 走 `MipBacked` 套线性后端，`ScipPerturbationBackend` 给原生 CP 求解器；判据 `GurobiPerturbationIntegrationTest` 真跑并断言观测列表非空）；**SCIP 的区间仍为 0，且已写成公开常量。** | **框架侧 8 个文件全在**：`CriticalConstraintAnalysisOptions`／`…Report`／`…ReportBuilder`／`…FullReport`／`CriticalAnalysisReports`／`ConstraintPerturbationPipeline`／`…Report`／`…Support`，能力枚举有 `FixedIntegerLpSensitivity`，报告结构有 `FixedIntegerLpSensitivityReport` 字段。****缺的是后端那 6 个公开函数**：`GurobiCriticalConstraintAnalysis.kt`（3 个）与`ScipCriticalConstraintAnalysis.kt`（3 个）——固定整数 LP 灵敏度采集、扰动后端、以及把两者编排成 pipeline 的入口。**它们全部要真实求解器的对偶值或边界重解，所以缺它们等于"这套框架目前没有任何后端接得上"。****判据：`CriticalConstraintBackendGapTests`。** |

**缺 18 个，其中一个插件分析器都没有。**
**B1079 更新：上面那张表是 B957 的测量，不是现状。** 当前实测：

| 侧 | 文件数 | 说明 |
| --- | --- | --- |
| 基线 `core/analysis/` | **19** | 由 `scripts/scan-kotlin-baseline.py` 从固定提交 `12fdb587` 的 `git ls-tree` 现算，不写死 |
| C# `src/Core/Analysis/` | **53**（B1279 增 `LinearSolverFixedIntegerLpSolver`） | 文件名多于基线，**因为 Kotlin 的 `object`/`companion object` 成员在 C# 里落成独立静态类** |
| 基线名字未解析数 | **0** | 门禁第一步每批都跑，四个维度（函数/类/枚举/属性）全部解析 |

**"缺 18 个"这句话现在是假的**，而且它是那种**读起来像个已交代的欠账**的假：
**没有人会去核实一个写着具体数字的缺口，因为数字让它看起来很核实过了。**

**The table above is B957's measurement, not the current state.** Current: the baseline count is derived at gate time from `git ls-tree` of the pinned commit rather than hardcoded, C# holds 52 files (**more than the baseline, because Kotlin `object`/`companion object` members land as separate static classes in C#**), and the gate's first step reports zero unresolved names across four dimensions.
**"18 missing" is now false, and false in the way that reads as an accounted debt** — **nobody checks a gap that carries a specific number, because the number makes it look already checked.**

**这个缺口此前没有任何一行记录。** 状态文档里 `core.analysis` 只在一个长句子里出现过一次，那句列出的是
"criticality profile"——**恰好是三个已经搬过来的类型之一。** 换句话说：文档点名的那部分在，
**没被点名的那 18 个不在，而文档没说。**

**为什么这条值得单列**：它不是"某个模块差一点"，而是**一个整块能力在盘点里隐形了**。
B955 撤掉的那两条假缺口是"点了名但不存在"；这一条正相反——**存在，但没点名**。
两种都会让工作量判断失真，方向相反而已。

**为什么这一批只记录不补**：18 个文件里 `ConflictAnalyzer`/`ConstraintActivityAnalyzer`/`TargetFeasibilityAnalyzer`
都依赖各求解器的原生证据（IIS、活动约束、扰动重解），**而这类证据只有真实后端给得出**。
把它们先写成不碰后端的空跑版本，正是本仓反复拒绝的那种"看起来有了"。

**B964 补了不依赖原生后端的三个**：`ActivityStatus`（6 个成员，顺序与基线一致）、`ActivityEvidence`（13 个字段，**浮点值与 `Int64` 精确字符串并存**）、`DiagnosticSource`（6 种来源 + `AsInfeasibilityMember` 投影）。
C# 的 `Core/Analysis/` 因此由 **3 个增至 6 个**，缺口由 18 收窄到 **15**。

**为什么只做这三个**：剩下 15 个里 `ConflictAnalyzer`/`ConstraintActivityAnalyzer`/`TargetFeasibilityAnalyzer` 等
都要 IIS、活动约束、扰动重解这类**只有真实后端给得出**的证据。**先把不依赖后端的做完，是唯一不需要假装的一步。**

**B965 又补了 `ConstraintActivityReport`（基线 126 行）**：`ConstraintActivity` / `ActivityGroupSummary` / 报告本体。
它只依赖 B964 那三个类型，**同样不需要原生后端**。`Core/Analysis/` 因此由 **6 个增至 7 个**，缺口 **15 → 14**。

**一条判据自己的教训**：B965 第一次变异把查找改成"匹配 `null`"，**判据没红**——因为查找走的是已经过滤过的
`ConstraintActivities`，那个分支永远走不到。改成让它走 `Activities`（未过滤），**红了**。
**"变异没咬住"有两种可能：判据太弱，或者变异点根本不可达。** 后者更危险，因为它看起来像判据是好的。

**B966 又补了 `CapabilityMatrix`（基线 297 行）**：`AnalysisCapability`（13 项）、`CapabilitySupport`（3 档）、
矩阵本体与 `From(SolverCapabilities)` 构造、`UnsupportedDeclaredCpFeatures` 判定。`Core/Analysis/` **7 → 8**，缺口 **14 → 13**。

**这一行钉的是推断链**：一个操作能不能用取决于它的上游能不能用——`AdaptivePerturbation` 跟着 `RhsPerturbation`、
`Mus` 跟着 `Conflict`。**链上错一步，上层就会选一条不存在的路径。**

**一处刻意的保守**（基线原本就写明了）：`FallbackSatisfactionOnly` 只在后端**声明了 CP 满足路径**时才成立。
从任意模型类型推导，会让只支持线性/MIP 的后端**声称一个它兑现不了的回退**。

**B967 补了 `FixedIntegerLpSensitivity` 的协议半边**：`LocalSensitivityScope`、`SensitivityRange`、
`LocalConstraintSensitivity`、`FixedIntegerLpSolveResult`、`FixedIntegerLpSensitivityOptions`、
`FixedIntegerLpRequest`、`FixedIntegerLpSensitivityReport`。`Core/Analysis/` **8 → 9**，缺口 **13 → 12**。

**只搬了不碰后端的那一半**：基线的求解实现（`solveTriad`、`solveDual`、`LoweredFixedIntegerLpSolver`）**要真实 LP 后端**，
这一批没有搬，也没有写一个空跑的 `FixedIntegerLpBackend` 实现。**接口本身没搬**——没有实现者的接口只是装饰。

**这一层的职责是"让不合法的值进不去"**：范围下界不得大于上界、端点必须有限、`NaN` 阈值要被拒、
目标值与对偶值必须有限。**只查有限性会让 `[5, 3]` 通过，而它覆盖的区间是空的。**

**B968 补了 `DiagnosticActivation`（基线 316 行）**：`ActivationState`、`DiagnosticActivation`、
`ConflictExtractionTier`（含三档选择）与 `DiagnosticActivationSet`。`Core/Analysis/` **9 → 10**，缺口 **12 → 11**。

**这一行钉的是证据边界**：哪些来源算"在场"、后端返回的成员里哪些能穿过这层。
**回映必须以激活集合为唯一来源**——反过来的写法（遍历后端成员再反查）会把**求解器自造的辅助元素**放进公开报告。

**三档选择的硬要求**：`Conditional` **不许冒充原生**。只有 `IsNative` 或显式 `Supported` 才进前两级；
否则一个"可能支持"的声明会让上层走一条**实际没实现的**路径。

**B969 补了 `CriticalConstraintAnalysisReport` 的漏斗块**（基线 872 行里的前 340 行左右）：`CandidateTier`、
`CandidateFunnelConfig`、`ConstraintCandidate`、`CandidateFunnelRanking` 与 `classifyCandidate`/`buildCandidateFunnel`。
`Core/Analysis/` **10 → 11**，缺口 **11 → 10**。

**只搬了漏斗**：基线的另外两块要 `ConstraintPerturbationReport` 与 `ConflictExplanation`，
这两个都住在**带后端求解**的文件里。**没搬的部分没有空跑版本。**

**判据钉的是"没有证据就不许分层"**：active 但没有对偶 → `Unclassified`（**不是 TierB**），
`Violated`/`Unknown` → `Unclassified`。**把"未知"读成"不重要"，会让一份没有证据的报告给出结论。**

**B970 补了 `ConstraintActivityAnalyzer` 的核心**（基线 566 行）：整数比较的精确松弛、变量两侧边界、
稀疏值域、`AllDifferent` 的自然 margin、组汇总。`Core/Analysis/` **11 → 12**，缺口 **10 → 9**。

**只搬纯计算入口**：基线的三个 `analyze(session, …)` 重载要 `CriticalConstraintAnalysisSession`（缓存与生命周期），
那一层还没搬，**没搬的重载没有空跑版本**。

**判据钉的是"精确值分类、标量值展示"这条分工**：分类用 `BigInteger` 精确算，标量只用于展示。
`9007199254740993` 走一趟 `double` 会变成 `9007199254740992`——**作为约束边界那是两个不同的模型。**

**`Unknown` 与 `Inactive` 是两件事**：前者是"没证据"，后者是"有证据且不活跃"。
合并会让一份没有解的报告看起来像"模型很松"。

**B971 补了 `CorrectionRecommendation` 的前 325 行**：`RelaxabilityPolicy`、`CorrectionCandidate`、
`RelaxationCost`、`CorrectionSet`、`AlternativeImprovementPlan` 与四个构造器。`Core/Analysis/` **12 → 13**，缺口 **9 → 8**。

**尾部约 110 行没搬**：它们要 `ConflictExplanation`，而它住在**带后端求解**的 `ConflictAnalyzer.kt` 里。
**没搬的那部分没有空跑版本。**

**判据钉的是两件"说了就不能反悔"的事**：

| 承诺 | 边界 |
| --- | --- |
| "这是最小修正集" | **策略一旦截断输入，最小性必须降级**——删除验证只覆盖截断前的全集 |
| "按成本排序" | **总成本必须与成员一致**，否则排序给出成员解释不了的顺序 |

**还有一条容易漏的**：权重与松弛各自有限、乘积溢出时成本是 `Infinity`，按成本排序会**把它排到最后**——
**看起来像"最便宜的方案"。**

**B971 ported the first 325 lines of `CorrectionRecommendation`**: the policy, the candidate, the cost, the correction set, the improvement plan and the four builders.
`Core/Analysis/` goes from **12 to 13** and the gap from 9 to **8**. **The last ~110 lines did not come over**: they need `ConflictExplanation`, which lives inside the
**backend-solving** `ConflictAnalyzer.kt`, and **nothing was stubbed in their place.** The criteria pin **two claims that cannot be taken back**: **"this is a minimal correction
set"** must be downgraded **the moment the policy truncates the input** (deletion verification only covered the untruncated universe), and **"ranked by cost"** requires **the total
to agree with the members**. **One more easy-to-miss case**: when each factor is finite but the product overflows, the cost is `Infinity`, and a cost ranking puts it **last** —
**which reads as "the cheapest option".**
**B970 ported the core of `ConstraintActivityAnalyzer`** (566 baseline lines): exact slack for integer comparisons, both variable bounds, sparse domains, the natural
`AllDifferent` margin and the group summaries. `Core/Analysis/` goes from **11 to 12** and the gap from 10 to **9**. **Only the pure entry point came over**: the baseline's three
`analyze(session, …)` overloads need `CriticalConstraintAnalysisSession` (cache and lifecycle), which is not ported yet, and **the missing overloads were not stubbed**.
**The criteria pin "classify exactly, display a scalar"**: classification uses `BigInteger`, the scalar is for display. `9007199254740993` through a `double` becomes
`9007199254740992` — **as a constraint boundary those are two different models.** **`Unknown` and `Inactive` are different things**: no evidence versus evidence of inactivity,
and merging them makes a report with no solution look like "the model is loose".
**B969 ported the funnel block of `CriticalConstraintAnalysisReport`** — roughly the first 340 of its 872 baseline lines. `Core/Analysis/` goes from **10 to 11** and the gap from
11 to **10**. **Only the funnel came over**: the other two blocks need `ConstraintPerturbationReport` and `ConflictExplanation`, both of which live inside files that **do solve on a
backend**, and **nothing was stubbed in their place**. **The criteria pin "no evidence, no tier"**: active without a dual is `Unclassified` (**not TierB**), and `Violated`/`Unknown`
stay `Unclassified`. **Reading "unknown" as "unimportant" lets a report with no evidence produce a verdict.**
**B968 added `DiagnosticActivation` (316 baseline lines)**: the activation state, the activation record, `ConflictExtractionTier` with its three-tier selection, and the activation
set. `Core/Analysis/` goes from **9 to 10** and the gap from 12 to **11**. **What the criteria pin is the evidence boundary**: which sources count as present, and which backend
members may cross it. **Remapping has to be driven by the activation set alone** — walking the backend members and looking them up lets **a solver-made auxiliary element** into a
public report. **The three-tier selection has a hard requirement**: `Conditional` **never masquerades as native**; only `IsNative` or an explicit `Supported` reaches the first two
tiers, or a "might support" declaration sends the layer above down **a path that is not implemented.**
**B967 ported the protocol half of `FixedIntegerLpSensitivity`**: the scope, the range, the per-constraint sensitivity, the solve result, the options, the request and the
report. `Core/Analysis/` goes from **8 to 9** and the gap from 13 to **12**. **Only the half that touches no backend came over**: the baseline's solving side (`solveTriad`,
`solveDual`, `LoweredFixedIntegerLpSolver`) **needs a real LP solver**, so it was neither ported nor stubbed — **and the hook interface itself was not ported either, because an
interface with no implementer is decoration.** **This layer's job is keeping invalid values out**: the lower end must not exceed the upper, endpoints must be finite, a `NaN`
threshold is refused, objectives and duals must be finite. **Checking finiteness alone lets `[5, 3]` through, and the interval it covers is empty.**
**B966 added `CapabilityMatrix` (297 baseline lines)**: `AnalysisCapability` (thirteen), `CapabilitySupport` (three levels), the matrix with its `From(SolverCapabilities)`
constructor and the `UnsupportedDeclaredCpFeatures` verdict. `Core/Analysis/` goes from **7 to 8** and the gap from 14 to **13**. **What the criteria pin is the inference chain**:
whether an operation is usable depends on its upstream — `AdaptivePerturbation` follows `RhsPerturbation`, `Mus` follows `Conflict` — and **one wrong link makes the layer above
pick a path that does not exist.** **One deliberate conservatism the baseline already spelled out**: `FallbackSatisfactionOnly` holds only when the backend **declares a CP
satisfaction path**; deriving it from any model type would let a linear/MIP-only backend **advertise a fallback it cannot deliver.**
**B965 added `ConstraintActivityReport` (126 baseline lines)** — `ConstraintActivity`, `ActivityGroupSummary` and the report itself. It depends only on B964's three types and
**equally needs no native backend**, so `Core/Analysis/` goes from **6 to 7** and the gap from 15 to **14**. **A lesson from the criterion itself**: the first mutation made the
lookup "match `null`" and **stayed green**, because the lookup walks the already-filtered `ConstraintActivities` and that branch is unreachable. Routing it through the unfiltered
`Activities` **turned it red**. **"The mutation did not bite" has two causes: the criterion is weak, or the mutation point is unreachable** — and the latter is more dangerous,
because it looks like the criterion works.
**B964 added the three that need no native backend**: `ActivityStatus` (six members in the baseline order), `ActivityEvidence` (thirteen fields, **the float and the exact
`Int64` spelling side by side**) and `DiagnosticSource` (six source kinds plus the `AsInfeasibilityMember` projection). `Core/Analysis/` therefore goes from **3 to 6**, and the
gap narrows from 18 to **15**. **Why only these three**: the remaining fifteen depend on IIS, active constraints or perturbation re-solving — evidence **only a real backend
produces**. **Doing the backend-free ones first is the only step that needs no pretending.**
**B957 counted both sides.** The baseline has **19** files under `core/analysis/` plus **2** plugin analyzers; C# has **3** under `Core/Analysis/` and **0** plugin analyzers
— **18 missing, not one plugin analyzer among the present ones.** The gap had **no row anywhere**: `core.analysis` appeared once in the status document inside a long
sentence whose list named "criticality profile" — **one of the three types that were ported**. In other words the named part is present, **the unnamed 18 are not, and the
document did not say so.** B955 retracted two gaps that were named but did not exist; this is the opposite shape — **it exists and was never named**, and both distort the
judgement of the work. **This batch records it and does not port it**: the three pattern analyzers depend on native evidence (IIS, active constraints, perturbation
re-solving) that only a real backend produces, and a backend-free stub for them is exactly the "it looks like it is there" this repository keeps refusing.

#### 四、一个缺口标记的两种过期方式

B397 留下一个判据 `QuadraticSolver_ShouldExposeNoDiagnosticAnalyzersByDefault`，注释写着
"若将来实现二次 IIS，判据会红，提醒同步更新文档"。**它确实红了。**

但它那个文件的因果链表有四行，**只有第 4 行被钉住**：

| 行 | B397 的说法 | 现在 |
| --- | --- | --- |
| 2 | `Elastic()` 只有测试调用 | **仍成立**（调用它的是 `QuadraticLegacyIisCompute`，而后者没有 src 调用方） |
| 3 | C# 没有二次 legacy IIS | **不成立**：`QuadraticLegacyIisCompute.cs` 已存在，三个入口 |
| 4 | 二次 `DiagnosticAnalyzers` 返回空 | **不成立**：B981 起返回原生 IIS 分析器 |

**第 3 行早就变了，判据一句话都没说。** 这是缺口标记的通病——**钉住一个现象，却以为钉住了整条推理。**

改写时保留了原有的锋芒：不只断言"列表非空"，还要求**那个分析器真的是原生 IIS**；
另一条同时钉"实现存在"和"调用方不存在"——**只钉前者会读成"这条路通了"**。

变异（给 `QuadraticLegacyIisCompute` 加一个 src 引用）→ 1 红。

### 第 6 条的附录四十五：**二值性不是数值边界（B1023）**

搬了 `TargetFeasibilityAnalyzer.kt` 的 `effectiveDomain` / `copyDomain`（`EffectiveDomain.cs`）。
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

### 第 6 条的附录四十四：**一条测不了的分支，写下来比假装测过强（B1022）**

本批核对 `objectivesAgree`，并补了相对容差的下界判据。

#### 一、实现与基线一致，且更严

基线：`difference <= tolerance * max(1.0, |left|)`。
C# 侧相同，**并且多了容差合法性校验**（NaN / 无穷 / 负值一律判不成立）。

#### 二、`max(1.0, |left|)` 的下界是 1，不是 0

少了这个下界，量级接近 0 的两个目标会让容差窗口退化成 0，
**于是"两个都是 0"被判成不相等**——而它们完全相同。

补了 `(0.0, 0.0)` 与 `(0.0, 1e-12)` 两条判据后，**去掉下界的变异红了。**

#### 三、有一条分支测不了，我把它写下来

负容差那条校验**没法用返回值区分**：`difference` 永远非负，
所以 `difference <= tolerance * scale` 在负容差下自然为假——**删掉那道校验，行为不变。**

我试了这条变异，**它没红。** 与其留一条"看起来测了"的断言，**不如把"这条测不了"写下来。**

**这是本会话第二次主动记录"判据测不到的分支"**（第一次是 B1011 的 `sync-over-async` 上限）。

变异（去掉 `max(1.0, …)` 下界）→ 1 红；变异（去掉负容差校验）→ **没红，已如实记录**。

### 第 6 条的附录四十三：**有解不等于证明了最优（B1021）**

搬了 `solveTriad` 里四道判定（`FixedIntegerLpGates.cs`）。`src/Core/Analysis/` 从 38 增至 **39**。

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

### 第 6 条的附录四十二：**多给一条比少给一条更难发现（B1020）**

搬了 `FixedIntegerLpSensitivity.kt` 的 `mapExplicitProvenanceDuals` 与 `mapExplicitProvenanceRanges`
（`ProvenanceDualMapping.cs`）。`src/Core/Analysis/` 从 37 增至 **38**。

#### 一、两条规则，都是"安静地"出错

| 规则 | 写错会怎样 |
| --- | --- |
| **一个来源只能有一条行** | 多条行指向同一来源时**取哪一条都是编的**，必须整条丢弃 |
| **来源必须在 `originalIds` 里** | 否则把中间层自造的行**当成原始约束的敏感度**报出去 |

**两条都是"多给一条"的错误，而多给一条比少给一条更难发现：**
**读者看到有数值就以为验过了。**

#### 二、第三条容易漏的：长度不一致

基线取 `getOrNull`。**用索引器会抛异常，而抛异常会让整份敏感度报告消失。**
**短的那一侧只是"这行没有来源"，不是错误。**

#### 三、两个映射共用一个实现

`MapDuals` 与 `MapRanges` 都转发到同一个私有 `Map<T, TValue>`。
**这不是抽象，是因为两条规则一模一样**——分开写迟早会有一条忘了查 `originalIds`。
判据里那条"区间映射用同一套规则"就是防这个的**反向对照**。

变异（允许多行来源）→ 2 红；变异（去掉 `originalIds` 检查）→ 2 红；变异（改用索引器）→ 1 红。

### 第 6 条的附录四十一：**三态里最容易漏的是"支持"（B1019）**

本批不加新函数，**补的是已有函数的判据缺口。**

#### 一、`capabilityAllowed` 与基线逐行一致

```csharp
overrides.TryGetValue(capability, out declared) → return declared != Unsupported;
否则 → return matrix.ModelTypes.Count == 0 || matrix.Support(capability) != Unsupported;
```

**和 `FixedIntegerLpSensitivity.kt` 的 `capabilityAllowed` 一字不差。**

#### 二、但判据只覆盖了两态

B1007 写了"显式不支持"与"空列表"两条，**没写"显式支持"。**

于是这样一个实现照样通过：

```csharp
return declared == CapabilitySupport.Supported && matrix.ModelTypes.Count == 0;
```

**它把"声明了支持但矩阵有模型类型限制"判成不支持**——而声明本该赢。

补一条"模型类型受限 + 显式声明支持 → 必须为真"的判据后，这个变异红了。

#### 三、这一批的教训

**"三态"的判据很容易只写坏的那一态。** 坏的那态会让人放心（"我测了拒绝路径"），
**而好的那态没测，等于没测过"声明能压过推断"这句话。**

变异（把显式支持降级）→ 1 红。

### 第 6 条的附录四十：**空和重复是两种错误（B1018）**

搬了 `CriticalityProfile.kt` 的 `validateTargetBatch`（`CriticalityProfileBuilder.ValidateTargetBatch`）。

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

### 第 6 条的附录三十九：**九个条件里，两个最容易漏（B1017）**

搬了 `CorrectionRecommendation.kt` 的三个纯函数到 `CorrectionProjection.cs`。

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

### 第 6 条的附录三十八：**结论可以抄下去，依据不能（B1016）**

新增 `docs/kotlin-sync-ledger.md`：P4—P9 的逐阶段核算台账，配 `PhaseLedgerTests`（4 条）。

#### 一、为什么要单开一份台账

`docs/kotlin-sync-status.md` 说的是**结论**；这份台账说的是**依据**。

**结论可以抄下去，依据不能。** 状态文档那句"P4 through P8 are closed"就是抄了多轮的结论，
**而它对 P6/P8 一度是无据的**（B1015 才补上收口条目）。

#### 二、"搬不了"必须分三类

| 类别 | 含义 | 下一步 |
| --- | --- | --- |
| **环境所限** | 代码能搬，本机没许可 / 实例 / SDK | **换个环境就能验** |
| **语言所限** | 基线依赖 JVM 专有库 | **不是缺口，是等价替换** |
| **需真实后端** | 逻辑已搬，验证必须接真实求解器 | **接上后端才能验** |

**混成一类，读者只能统一当成"做不到"。**

#### 三、一条容易漏的区分

**MySQL 与 PostgreSQL 的服务器是开着的（3306 / 5432 可连）。缺的是凭据，不是服务。**

这条区分决定了下一步是"要个密码"还是"装个数据库"——**代价差一个数量级。**

#### 四、判据怎么守住它

- **每一条引用都必须能解析**：台账里写的 `P7 Batch 151` 必须在计划里搜得到，**拼错就等于"这条结论是编的"**
- **六个阶段都要有一节**
- **三类原因必须分开写**，且环境所限必须点名具体失败字符串
- **补写的收口必须自报家门**，而且要落在**状态行本身**，段内别处提到不算

变异（改一个引用号）→ 1 红；变异（P6 状态行写成普通"已收口"）→ 1 红。

**第四条第一次没抓住**：段内别处还有"补写"字样，宽松的 `Contains` 照样通过。
收紧到"状态行本身"才红——**第七次"判据名不副实"。**

### 第 6 条的附录三十七：**编号的洞是唯一留下的证据（B1014）**

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

**本批不改写历史**，而是把例外写成一份**可数的事实**：块序列必须是 `4 → 9 → 8 → 7 → 6 → 5 → 4`，
且 5—9 各只出现一次。**用"跳过"开关会把真正的插队一起放过。**

变异（改一个批次号）→ 1 红；变异（改一个阶段号）→ 2 红。
### 第 6 条的附录三十六：**"没证出来"不能写成"已证明"（B1012）**

搬了 `AssembleNotUnreachable`：目标不是"不可达"时的解释。

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

### 第 6 条的附录三十五：**状态和次数都对，只有观察列表不同（B1011）**

搬了 `ConstraintPerturbationPipeline`（扰动分析的编排）。**所有判定早已单独搬过，这一段只钉顺序。**

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

**这是本会话第五次"判据名不副实"**，也是第二次（B1008 之后）在同一种地方栽倒：
**两条路径的可观测差异，我挑了不相干的那几个去断言。**

#### 三、夹具也错了一次

`TheRefusedBackend` 第一版只禁了删除测试，**RHS 仍可用**，于是前置门判"继续"。
补一个"两项都不允许"的矩阵才对。

#### 四、`ponytail:` 留了一处上限

扫描用 `sync-over-async`（`GetAwaiter().GetResult()`）。**无 `SynchronizationContext` 的宿主安全**；
若将来有 UI 宿主，按注释升级成 async 扫描——**规则一样，只是 probe 返回 `Task`。**

### 第 6 条的附录三十四：**数函数，不数行数（B1010）**

搬了 `unsupportedExplanation`，并**实测**三个壳的剩余缺口。

#### 一、实测方法与结果

对三个基线文件逐个取函数名，再到 C# 侧按名字搜：

- `ConflictAnalyzer` —— 函数名 7 个：`sourceOf` / `groupSummaries` / `remapBackendConflict` / `literalKey` / `assumptionSeed` / `nativeSeed` / `unsupportedExplanation` **全部已搬**；**缺 `analyze` 本体**
- `ConstraintPerturbationAnalyzer` —— 10 个已搬（`aggregateStatus` / `capabilityAllowed` / `classifyOutcome` / `deriveRhsSnapshot` / `integerPatternChanged` / `objectiveImprovement` / `refineThreshold` / `requestedDeltas` / `runRemoval` / `RunDeltas`）；缺 `solve(request)` 与 `analyze` 本体
- `CriticalConstraintAnalysisPipeline` —— 五阶段编排已搬；缺 `analyzeAndExplain` 与 `rankConstraintCandidates`

**用列表不用表格，原因同 B1003**：`RemainingGapLedgerTests` 按"第一行以 `| \`文件名\`` 开头"取行，
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

### 第 6 条的附录三十三：**"没测"第六次，这次它连停都不该停（B1009）**

搬了固定 delta 扫描 `RunDeltas`。**扰动分析的纯逻辑至此全齐。**

#### 一、三条停止规则

| 情况 | 处理 | 写错会怎样 |
| --- | --- | --- |
| 首个 `Effective` | **记下并停** | 继续扫会把后面的有效 delta 覆盖掉前面的 |
| `NoObservedEffect` | 记下，**继续** | 停在这里会漏掉真正有效的那个 |
| **`Unknown`** | **既不记也不停** | 当成"无效果"伪造下界；**停在这里让扫描提前结束** |

**第三条是那个家族的第六次**（B983 / B992 / B994 / B999 / B1007 之后）。

#### 二、变异第一次没抓住，原因是"被后面覆盖了"

用例 `TheUnknownDelta_ShouldNeitherRecordNorStop` 的序列是 `Unknown → NoEffect → Effective`。
把 `Unknown` 也当成无效果时，**后面那条 `NoEffect` 把被污染的值覆盖掉了**，断言照样通过。

补一条"**末尾是 `Unknown`**"的用例，同一个变异才红。

**这已经是本会话第四次"判据没在测它声称要测的东西"**（B998 / B1002 / B1008 之后）。
四次里有三次是同一个形状：**用例构造得让变异落到一个不影响断言的位置。**

#### 三、`firstEffective` 用 `??=` 是错的

基线里它赋值后**立刻 `break`**。改成 `??=` 而没有 `break`，扫描会继续，
**而报告给出的区间上界会取自一个不是"首个"的 delta。** 变异 → 1 红。

### 第 6 条的附录三十二：**"恰好一项不可用"才是判据要测的（B1008）**

搬了扰动分析的前置门 `Preflight`。

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

我最初写的两条"应当继续"用例，**都是两项可用**——于是"任一项不可用就拒绝"的变异照样通过。

**补上"恰好一项不可用"的用例后，同一个变异才红。** 这是本会话第三次出现
"判据名不副实"（B998 / B1002 之后）。

### 第 6 条的附录三十一：**"没测"第五次，这次是"没变"（B1007）**

搬了 `capabilityAllowed` 与 `integerPatternChanged`。`aggregateStatus` 已在 B992 搬过，本批不重复。

#### 一、任一侧解缺失 → `null`，不是 `false`

**这是"没测不能写成没变"这个家族的第五次**（B983 / B992 / B994 / B999 之后）。

写成 `false` 会让报告声称"扰动之后整数解的模式没变"——**而那份基线解根本没拿到，
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

### 第 6 条的附录三十：**目标可达时不该有"冲突解释"（B1006）**

搬了 `CriticalConstraintPipeline`（五阶段编排）与 `CriticalPipelineBackends`（委托注入）。
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

上一批的七条加了一条 `ConflictBackendMissing`，`AreReasonsDistinct` 随之覆盖到八条。

### 第 6 条的附录二十九：**"我关了它"和"环境里没有它"是两件事（B1005）**

搬了 `CriticalPipelineStages`：每个阶段"跑不跑、不跑怎么说"的决策。

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

### 第 6 条的附录二十八：**"没跑"要同时做两件事（B1004）**

搬了 `CriticalConstraintAnalysisReportBuilder` 与 `CombineStatus`。

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

#### 三、基线目标值是**条件**要求

扰动报告为空时不需要基线值，**非空时才需要**。无条件要求会让"没跑扰动"的正常报告也失败。
变异（改成无条件）→ 1 红。

#### 四、`Build` 最后跑一遍 `Validate`

报告自己说不过去就不交出去——**B1003 的四条不变量在这里变成一道出厂检查。**

### 第 6 条的附录二十七：**先核对"还剩几个"，再动手（B1003）**

搬了 `AnalysisBaselineSummary` 与 `CriticalConstraintAnalysisReport`（含 `Validate` / `BlockingSummary` / `HasMinimalBlockingSet`）。

#### 一、上一批我记错了一个不存在的文件

B1002 的收口写着"下一批做 `MinimalConflictAnalyzer`"。**这个文件在基线里根本不存在。**

`git ls-tree 12fdb587 | grep core/analysis/` 一共 27 个文件，分析器只有 5 个：

| 分析器 | C# 侧 |
| --- | --- |
- `ConstraintActivityAnalyzer` —— **存在**
- `TargetFeasibilityAnalyzer` —— **存在**
- `ConflictAnalyzer` —— **缺类本体**（逻辑已拆到 `ConflictEvidenceSupport` / `AssumptionConflictSeed` / `NativeConflictSeed` / `ConflictDeletionCheck` / `ConflictExplanationAssembler`）
- `ConstraintPerturbationAnalyzer` —— **缺类本体**（逻辑已拆到 `ConstraintPerturbationSupport`）
- `CriticalConstraintAnalysisPipeline` —— **缺类本体**（本批补了它依赖的报告类型）

**用列表不用表格是刻意的**：`RemainingGapLedgerTests` 按"第一行以 `| \`文件名\`` 开头"取行，
我先前写成表格，**把那张缺口台账的行给顶掉了**——判据当场红。

**"还剩 3 个分析器"这句话本身没错，但"缺"指的是缺那个类**，不是缺逻辑。
**B1002 的措辞让它读起来像"还差三块没写的功能"，实际是"还差三个把已有零件装起来的壳"。**

#### 二、四条不变量

| 不变量 | 违反后报告会说什么 |
| --- | --- |
| schema 版本非空 | 一份无法追溯格式的报告 |
| 基线有目标身份与方向 | 一份不知道在优化什么的报告 |
| **已证明最优必须带目标值** | **"已证明最优"但报不出最优值** |
| **背景阻塞不得同时声称最小集** | **"最小集是空的"与"阻塞来自背景"同时成立** |

**第三条最关键**：两个字段单独看都合法，**合起来是自相矛盾的一份报告。**

#### 三、三种阻塞摘要分开说

没跑 / 背景阻塞 / 有最小集。**第二种最容易漏**——背景阻塞时 `Members` 里没有约束，
**若走"最小集"那条分支，摘要会声称"0 个原始证据"，而真实原因是固定背景。**

变异（去掉已证明最优不变量）→ 1 红；变异（去掉背景分支）→ 1 红。

### 第 6 条的附录二十六：**"没有冲突"不等于"没验证过"（B1002）**

搬了 `ConflictExplanationAssembler`：把可行性结果与收缩结论拼成一份 `ConflictExplanation`。
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

#### 四、这一批我错了两轮

① 第一条判据的 probe 把 `Reachable` 当成"删除"写（和 B1001 同一个坑），改成"候选里还有谁"才对；
② 第二条判据的夹具让 `Members` 为空，**"投影里没有约束"在空集上恒真**，两个标志没法同时为真，
变异自然抓不住。补成两条来源后才成立。

**第二次是"判据自己没在测那件事"，和 B998 同型。**

### 第 6 条的附录二十五：**单轮就够，因为判定是单调的（B1001）**

搬了删除收缩循环 `ConflictDeletionCheck.Shrink`。

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

### 第 6 条的附录二十四：**删除后可达，所以要保留（B1000）**

搬了删除复验的判定 `ConflictDeletionCheck`。**复验循环本身没搬**——它要真的跑一次求解。

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

### 第 6 条的附录二十三：**"顺手补全"会把合法请求变成建模错误（B999）**

搬了 `nativeSeed`：直接在派生模型上请求后端自己算冲突，不加 guard 字面量。
**两个种子策略到此都齐了。**

#### 一、只传约束类 activation id

基线里有一段很容易被"顺手补全"的推理：

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


### 第 6 条的附录二十二：**变异没抓住，说明判据没在测那件事（B998）**

搬了 `assumptionSeed`：给每条约束加 guard 字面量，用假设求解取冲突，再重映射。
**这条路不要求后端暴露原生 IIS 接口**，所以它是两个策略里更通用的那个。

#### 一、派生模型里不能留着原始约束

第一次跑，两个判据都拿不到种子。原因：**我在重建出来的模型上又用同一个 id 加了 guard 约束**——
原始约束还在，`AddConstraint` 撞 id 失败。

更根本的是语义：assumption 派生模型的做法是**"每条约束换成一个 guard 版本"**，不是"在旁边再加一条"。
加在旁边，原始约束仍无条件生效，**假设求解永远不可能通过取反 guard 发现冲突，种子必然为空。**

补了 `WithoutConstraints()`。**这也说明 C# 快照缺的派生手段比想象的多**（`TryWithConstraint` 是 B993 补的）。

#### 二、判据第一次写错了，变异当场暴露

`TheUnprovenInfeasibility_ShouldGiveNoSeed` 第一次只给了 `Infeasible(Claimed)`、**没带冲突**。
去掉证明检查后它**仍然通过**——因为重映射本来就会给空结果。

**这条判据分不清"因为没证明"和"因为没冲突"。** 补上真实冲突 + 一条 `Verified` 反向对照，
再跑同一个变异，这次红了。

**这是本会话第一次由"变异没抓住"反过来修判据。** 以前都是变异抓住了、判据有效；
这次是变异抓不住、**说明判据在测别的东西。**

#### 三、`null` 与空集合的区别

六处失败点都返回 `null` 而不是空集合。**空集合会被读成"模型没有冲突"——完全相反的结论。**
变异（去掉证明检查）→ 1 红；变异（少 guard 一条）→ 1 红。


### 第 6 条的附录二十一：**后端会报告你没问的东西（B997）**

搬了 `remapBackendConflict` 与 `literalKey`。

#### 一、最后一步是过滤，不是返回

**后端会报告它自己认定的一切，包括调用方没问的东西。**

```csharp
return requested.Where(wanted.Contains).ToArray();   // 不是 return wanted
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


### 第 6 条的附录二十：**指错位置比不给位置更糟（B996）**

搬了 `ConflictAnalyzer` 的 `sourceOf` 与 `groupSummaries`。**两个种子策略（原生 IIS、假设式）没搬。**

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


### 第 6 条的附录十九：**没测的那一半不能说"确认无效果"（B995）**

搬了 `refineThreshold`，并把求解依赖抽成一个 `probe` 回调——**于是二分逻辑可以在没有求解器的情况下测透。**

#### 一、三条规则

| 规则 | 顺手写会怎样 |
| --- | --- |
| 中点必须**严格**落在区间内 | **整数中点停在原地，循环空转，区间没变** |
| 探测 `Unknown` 就停 | **下界被推高，报告声称测过那一半** |
| 次数用完如实说 | 报一个"已经收敛"的假结论 |

**第二条是这批的重点。** 把一次没结论的探测当成 `NoObservedEffect`，下界会推到中点，
于是报告声称"在这个 delta 以下确认无效果"——**而那一半根本没测过。**

这和 B983/B992/B994 是同一个家族：**"没测"不能被写成任何一端的证据。**
这是第四次以不同形状遇到它，**所以这次在代码注释里直接写出了"那一半根本没测过"。**

#### 二、区间 [0, 1] 这个退化情形

没有整数严格落在 0 和 1 之间。不加守卫，八次迭代会全撞在下界上，**次数耗尽、区间没变，
而结果看起来像"收敛了"。** 判据直接断言 `calls == 0`。

#### 三、收敛与未收敛要分开报

只有区间宽度 ≤ 1 才给 `FirstEffective` 并带 `FirstEffectiveDelta`；否则给 `ThresholdInterval`，
**`FirstEffectiveDelta` 留空**——一个没收敛的搜索说不出"首次有效是多少"。

变异（`Unknown` 当成无效）→ 1 红；变异（守卫放宽）→ 1 红。


### 第 6 条的附录十八：**新判据当场揪出一个旧缺陷（B994）**

搬了 `runRemoval`。补的前置件是 `ConstraintProgrammingModelSnapshot.TryWithoutConstraint`。

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

#### 三、判据当场揪出 B983 留下的缺陷

第一次跑 `TheExhaustedBudget_ShouldBeUnknown` 就红了：`RemovalEffective` 里有

```csharp
Kind == Removal ? Outcome == Effective : null
```

**`Outcome = Unknown` 时这个表达式给 `false`**——"没测"被写成了"删除无效"。
这正是 B983 我自己写过的那条教训（`null > 0` 读起来像"没有改善"），**换了一个字段又犯了一次。**

改成显式三态：`Effective → true`、`NoObservedEffect → false`、`其余 → null`。

**这条值得记下来的不是修复本身，而是：新判据第一次跑就抓到了旧代码里的同类错误。**

#### 三点五、两条判据打起来，谁对

B975 有一条判据明确写着 `Removal(Unknown).RemovalEffective == false`，理由是"未知不能声称有效"。
B994 的新判据要求 `null`，理由是"没测就没有结论"。**两条都跑，必然红一条。**

| 主张 | 理由 | 站不站得住 |
| --- | --- | --- |
| `false` | 未知不能声称有效 | **理由成立，结论错了** |
| `null` | 没测就没有结论 | 成立 |

**`false` 不是"没有声称有效"，而是"声称了无效"。** 字段是 `bool?`，三态存在的意义就是让"没测"能说出来；
把它压成两态，只看这个字段的调用方就分不清"测了无效"和"没测"。

**所以改的是 B975 那条判据**，理由写在判据旁边。**这是本会话第几次同类更正已经数不清了——
但它说明一件事：这些判据不是在装饰文档，它们会真的互相打起来。**


#### 四、还没搬的

`refineThreshold`（阈值二分）与候选漏斗。删除测试这条链现在是完整的：派生 → 求解 → 分类。


### 第 6 条的附录十七：**扰动 0.5 和扰动 0 不能是同一份模型（B993）**

搬了 `deriveRhsSnapshot`，并补上它需要的一个前置件：`ConstraintProgrammingModelSnapshot.TryWithConstraint`。

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


### 第 6 条的附录十六：**没跑完和跑不了是两件事（B992）**

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
合起来报会让一份缺证据的报告看起来结论明确——**这和 B984 那条是同一个道理，只是换了一层。**

阈值比较是**严格大于**：`improvement == tolerance` 仍在噪声范围内。

#### 三、方向搞反，变差的解看起来是改善

同一个差值 10 → 12，最大化下是 +2、最小化下是 −2。**两条都要查**，只查一条会让"永远 `current - baseline`"通过。

#### 四、重复的 delta 不是两条证据

去重会让"有效 delta 个数"少算——**而重复会让它多算，同样错。** 两者都得钉。

变异（忽略 `Unknown` 观测）→ 1 红；变异（最小化方向翻转）→ 1 红。


### 第 6 条的附录十五：**"没证出来"不是"不可达"（B991）**

`TargetFeasibilityAnalyzer.AnalyzeAsync`：派生重建 → 求解 → 按输出分类。
**这是本会话第一次真的调用求解器接口**（虽然判据用的是固定输出的假求解器）。

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


### 第 6 条的附录十四：**约束检查在目标检查之前（B990）**

搬了 `validateTargetWitness`：拿求解器返回的解逐条复核，五项检查按序执行。
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


### 第 6 条的附录十三：**取整方向反了，门槛就低一格（B989）**

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

基线如此。多注册其余 objective 不会让求解出错，**但会让"这份派生模型是为哪个目标造的"变得含糊。**
判据只查 `Objectives` 恰好一条。

#### 三、目标约束的 id 要能认出来

`analysis-target:{stableId}`。**没有这个前缀，报告里就分不清哪条约束是分析加的、哪条是模型自带的。**

变异（返回前把模型 `Dispose`）→ 2 红——这同时证明判据走的是**真的模型**，不是一句构造成功的空话。


### 第 6 条的附录十二：**漏一个变量会让模型照样能解（B988）**

按 B987 的更正值了重建器：`SnapshotModelRebuilder`，走**表达式 AST 找回变量**这条路。
这是 `ConstraintProgrammingSnapshotBuilder.buildModel` 的**普通路径**（全部成员激活）。

#### 一、没搬的三处，遇到就报错

| 未搬 | 遇到时 |
| --- | --- |
| 区间变量 | 报错 |
| 选择性停用证据时的值域重绑（`activeSources`） | 未实现，调用方拿不到这条重载 |
| 目标过滤（`objectiveFilter`） | 同上 |

**为什么不静默跳过**：少一条约束的模型**照样能解、照样给个数**，而那个数没有任何一处会显得可疑。

#### 二、和基线的一处**故意不同**

基线在"变量在快照里找不到"时 `continue`，理由是"该变量完全没被引用"。
**但重建器里"找不到"的含义是"我们没能从 AST 里收出来"——这是两件事。**

基线能安全跳过，是因为它同时遍历了变量、表达式、区间**和约束**；
这批只遍历表达式与目标，**所以"找不到"更可能是我们漏了，而不是它没用。**

改成报错，并在错误里带上变量 id。变异：把报错改回 `continue` → 1 红。

#### 三、线性项这里真的收不了

`ConstraintProgrammingExpression.Term` 只有 `IVariableItem`，**没有 `VariableId`**，而索引的键是 `VariableId`。
**用变量名当键就是猜**——名字不是身份（B987 刚吃过这个亏）。所以线性项这条分支显式留空，漏掉的变量变成报错。

#### 四、区间守卫这一条**没有判据**

`IntervalVariable` 与 `ConstraintProgrammingModelSnapshot` 的构造器都是**私有**的，测试造不出带区间的快照。
**守卫留在代码里，但这一批没法证明它会响。** 如实记下。

变异（值域被放宽）→ 1 红；变异（找不到就 `continue`）→ 1 红。


### 第 6 条的附录十一：**我把因果搞反了两批（B987）**

B985 和 B986 都建立在一个我**没有核对**的因果链上：

> 快照只存了类型名 → 没有反向解析 → 重建时只能猜类型 → 所以要补类型目录和工厂

**这条链的前两步是真的，第三步是假的。**

#### 一、基线怎么找回变量

`buildModel` 第一步不是读 `TypeName`，而是：

```kotlin
collectExpressionVariables(snapshot.expressions.map { it.expression }, sourceVariables)
```

它遍历**表达式 AST**，把里面的 `IVariableItem` 引用**原样收出来**。C# 侧同样可行：
`ConstraintProgrammingExpression.Variable.VariableItem` 持有的是**活对象**。

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
要让它变红得改重绑逻辑，代价超过这一批的收益。**如实记下，不假装测过。**


### 第 6 条的附录十：**工厂要的是实例，不是类型（B986）**

> **B987 更正：本附录把工厂说成"重建器缺的最后一环"是错的。**
> 工厂造出来的是**新对象**，`Identifier` / `Key` 都不同——注册进去就是另一个变量。
> **重建器要的是"找回"，不是"造一个"。** 见附录十一。
>
> **B987 correction: calling the factory "the last link the rebuilder was missing" was wrong.** The factory produces a **new object** with different `Identifier` / `Key`, so
> registering it introduces a different variable. **A rebuilder needs to *find* the variable, not *create* one.** See appendix eleven.


B985 补了类型目录，B986 补上它缺的最后一环：`VariableTypeCatalog.TryCreate`——**按稳定名称造一个标量变量**。

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

`.analysis` 里与重建相关的还有 `ConstraintProgrammingAssumptionMapper`（已在 B977 搬完）与
`FixedIntegerProjection`（同批搬完），**所以重建器不需要再补旁支。**


### 第 6 条的附录九：**快照把变量类型存成了类名，而没人能读回来（B985）**

> **B987 更正：本附录把因果搞反了。** 类型名确实没有反向解析，但**基线根本不走这条路**——
> `buildModel` 从表达式 AST 里**找回**原来的 `IVariableItem` 对象。所以"只能猜类型"这个结论是错的。
> 细节见附录十一。**本附录保留原文以便对照，结论以附录十一为准。**
>
> **B987 correction: this appendix had the causality backwards.** The type name indeed has no parser, but **the baseline never takes that path** — `buildModel` *recovers* the
> original `IVariableItem` objects from the expression AST. The conclusion "it can only guess the type" was wrong. See appendix eleven; this text is kept for comparison.


剩下 4 个 `core.analysis` 分析器（`TargetFeasibilityAnalyzer`、`ConstraintPerturbationAnalyzer`、`ConflictAnalyzer`、`Pipeline` 编排器）
**全部卡在同一个前置件上**：`ConstraintProgrammingSnapshotBuilder`（基线约 440 行，把不可变快照重建为可变模型）。

B985 量了这个前置件在 C# 缺什么，**结论不是"逻辑没写"，而是"数据不够"**。

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
而那是下一步，不是这一批。

变异：加一条近似项 → 3 红；把兜底改成静默成功 → 2 红。


### 第 6 条的附录八：**"测了没效果"是证据，"没有结论"不是（B984）**

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


### 第 6 条的附录七：**"没测出来"不是"没改善"（B983）**

补了 `CriticalConstraintAnalysisReport.kt` 的有效性侧：`EffectivenessEntry` 与 `EffectivenessRanking`。
B969 当时只补了漏斗那一块，**所以那个文件此前是"部分搬"却记在"整文件搬完"里**——账目在 B983 才对齐。

`buildEffectivenessRanking`、`summarizeGroups`、`AnalysisGroupSummary` 与报告构造器**仍缺**。

#### 一、`null > 0` 是假，而它读起来像"没有改善"

`IsGloballyEffective` 必须同时要求"有值"和"大于零"。只写 `MaxImprovement > 0` 时，
`null` 会得到假——**和"测了、没改善"同一个结果**。

**而实际含义完全不同：一个是没测，一个是测了没有。** 一份缺失证据的报告会因此看起来结论明确。

#### 二、四条校验，四种"数值撑不住"

| 检查 | 不查会怎样 |
| --- | --- |
| schema 非空、基线目标有限 | 一份没有出处的报告 |
| 身份非空、不重复 | 同一条约束算两次，**改善看起来翻倍** |
| 数值有限、delta 为正 | `NaN` 混进排序，**整个降序失去意义** |
| **局部判决需要对偶值** | 没有对偶值却给"局部有效"，**凭空断言** |

第四条是这批最有价值的一条：**一个没有对偶值的"局部有效"，读起来和一条测出来的结论一模一样。**
变异：去掉这条检查 → 1 红。

#### 三、判据自己的反方向对照

`TheViews_ShouldFilterIndependently` 不只查"全局有效只有一个"，还查"局部有效有两个"
——**因为全局有效的那个也带局部判决，只查前者会让"三个视图其实是同一个"通过。**

变异：把 `null` 也算成有效 → 2 红（含这条视图判据）。


### 第 6 条的附录六：**三个零值都不是"没配置"（B982）**

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

基线的默认是 `CandidateFunnelConfig.DEFAULT_CANDIDATE_LIMIT`（32），**不是 0**。
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


**B982 的实测**：4 条判据通过；变异（零被误拒 / 默认策略被换成重的）各 1 红。


### 第 6 条的附录五：**Gurobi 三个分析器到齐，但判据被后端逼着重写了两次（B981）**

| 基线类 | 状态 |
| --- | --- |
| `GurobiFarkasAnalyzer` | 已补（B979） |
| `GurobiNativeIISAnalyzer` | 已补（B980） |
| `GurobiQuadraticNativeIISAnalyzer` | **已补（B981）** |

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

**基线也不设 `NonConvex`**：它只处理凸的那一侧。所以判据也只用那一侧，**不为了测试方便去改后端参数。**
改参数能让测试过，但那就不是在测基线的能力了。

#### 三、二次侧只声明原生 IIS

二次约束的 Farkas 证书不是这个插件给得出的。**声明一个给不出的能力，比不声明更糟。**
判据钉住：只有一个分析器，且 `ModelTypes` 恰好是 `Qp` / `Qcp`。


**B981 的实测**：4 条判据在真实 Gurobi 上通过；变异（`IISQConstr` → `IISConstr`）2 红。


### 第 6 条的附录四：**线性原生 IIS 搬完，`NativeIis` 从 `false` 改 `true`（B980）**

| 基线类 | 状态 |
| --- | --- |
| `GurobiFarkasAnalyzer` | 已补（B979） |
| `GurobiNativeIISAnalyzer` | **已补（B980）**，真实后端跑 `computeIIS` |
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


**B980 的实测**：5 条判据在真实 Gurobi 上通过；变异（摘掉分析器 / 改精确度）分别 4 红、1 红。


### 第 6 条的附录三：**Gurobi 的 Farkas 那条搬过来了，真后端跑过（B979）**

基线 `GurobiInfeasibilityAnalyzers.kt`（518 行）有三个类，B979 补了中间那个：

| 基线类 | 状态 |
| --- | --- |
| `GurobiFarkasAnalyzer` | **已补（B979）**，真实 Gurobi 后端跑过 |
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

#### 三、两处照基线实现、但理由值得写下

- **`DualReductions = 0` 与 `InfUnbdInfo = 1`**：没有这两项，Gurobi 可能返回 "infeasible or unbounded"，
  而那个结论**支撑不了证书**；
- **`FarkasProof` 为零就是失败**：逐行对偶值仍然读得出来，但那些数字不构成证书，
  **据此报"已证明不可行"是编造。**

#### 四、隔离实例的理由

诊断**另起实例**，不继承主求解器。主运行的预处理和分支定界会改变行序与对偶值，
而 Farkas 乘子是逐行的——**行序一旦变了，证书指向的就是别的约束。**


**B979 的实测**：4 条判据在真实 Gurobi 上通过；变异（删整数守卫 / 删状态守卫）各 1 红。


### 第 6 条的附录二：**按实测重记账，三种口径分开（B978）**

**此前那一行写的是"由 3 个增至 18 个，仍缺 3（基线 21）"。这个口径混了两件事**：
"C# 里有没有这个文件"和"基线那个文件有没有被完整覆盖"。两者不等，因为**有三个基线文件里的类型
被单独拆成了 C# 文件**——文件数涨了，覆盖的基线文件数没变。

B978 逐文件比对（`git ls-tree` 对 `ls`，不是凭记忆）：

| 类别 | 数量 | 基线文件 |
| --- | --- | --- |
| **整文件搬完** | **15** | 其余全部 |
| **只搬报告侧**（分析器本体要真实 CP 后端） | **3** | `TargetFeasibilityAnalyzer`、`ConstraintPerturbationAnalyzer`、`ConflictAnalyzer` |
| **完全未动** | **0** | —（B982 起 `Pipeline` 已搬选项层） |
| **只搬选项层** | **1** | `CriticalConstraintAnalysisPipeline`（选项已补，编排器缺） |
| **合计** | **19** | — |

C# 的 18 个 `.cs` 里，`TargetFeasibilityReport`、`ConstraintPerturbationReport`、`ConflictExplanation`
**在基线里是那三个分析器文件的一部分**，拆出来是为了让不需要后端的部分先落地。
**所以"C# 18 个文件"不等于"覆盖基线 18 个文件"。**

#### 插件分析器：一个搬完了，一个没动

| 基线文件 | 行数 | 状态 |
| --- | --- | --- |
| `ScipInfeasibilityAnalyzers` | 278 | **已搬完**（1 个类，有 `ScipFarkasDiagnosticTest`） |
| `GurobiInfeasibilityAnalyzers` | 518 | **已搬完（B979—B981）**：三个类全部到位，都在真实后端跑过 |

**SCIP 侧此前被记成"部分"，这是错的。** 基线那个文件只有**一个类** `ScipFarkasInfeasibilityAnalyzer`，
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


**B978 重记的理由**：一种新的文档缺陷形状——**口径混用**。前六种是"点了名但不存在""存在但没点名"
"照抄旧判断""空备注""代理当证据"；这一种是**两个都成立的事实被算成了一个数**。
文件数涨了、覆盖数没涨，两个数都真，混在一起就都不真了。


### 第 6 条的附录：剩下 8 个，逐条写明缺什么（B972）

前面几批已经逐块补了 13 个。**剩下的 8 个这一批不再"待办"，而是逐条记账**——
因为"做不到"和"没做"是两件事，混在一起记，下一批就会再翻一遍。

| 基线文件 | 行数 | 求解引用 | C# 状态 | 缺的前置件 |
| --- | --- | --- | --- | --- |
| `ConflictAnalyzer` | 918 | 7 | **报告已补（B976），分析器缺** | 第 630 行 `solver.solve`、第 755 行 `session.solve` |
| `ConstraintPerturbationAnalyzer` | 959 | 5 | **报告已补（B975），分析器缺** | `activeBackend!!.solve(request)`（第 498、698 行） |
| `TargetFeasibilityAnalyzer` | 915 | 5 | **报告已补（B974），分析器缺** | 第 673 行有真正的 `solver.solve` |
| `CriticalConstraintAnalysisSession` | 341 | 6 | **已补（B973）** | ~~要活的求解器实例~~ **更正：不需要后端** |
| `CriticalConstraintAnalysisPipeline` | 359 | 2 | 缺 | 上面四者的组合 |
| `ConstraintProgrammingAnalysisBackends` | 270 | 5 | 缺 | 后端适配胶水，同样要活的求解器 |
| `GurobiInfeasibilityAnalyzers` | 518 | — | 缺 | Gurobi 插件里**没有**任何 `InfeasibilityAnalyzer` |
| `ScipInfeasibilityAnalyzers` | 278 | — | **部分** | 已有 `ScipFarkasInfeasibilityAnalyzer.cs`，其余缺 |

**core 部分 3762 行 + 插件部分 796 行 = 4558 行。**

**B973 更正**：这张表原本把 `CriticalConstraintAnalysisSession` 记成"要活的求解器实例"。
**那是 grep 代理读错了**——唯一带 `ConstraintProgrammingSolver` 的参数**只用来读 `Descriptor`**。
它已补，**不需要后端**；剩余 core 部分因此是 **3421 行**，加上插件 **796 行 = 4217 行**。

**B974 补了 `TargetFeasibilityReport`，没补分析器**：基线第 673 行是一处真正的 `solver.solve(model, options)`，
之前的 `ConstraintProgrammingSnapshotBuilder` 还要重建可变模型。**那部分要真实 CP 后端，没搬也没空跑。**
`Core/Analysis/` **14 → 15**，缺口 **7 → 6**。

**报告钉的是四个状态不许互相冒充**：`Reachable`/`Unreachable` 是结论，`Unknown`/`Unsupported` 是"没结论"的两种原因。
**`Reachable` 不等于已证明**——求解器在时间上限内找到一个解，只说明目标**可能**可达。

**B975 补了 `ConstraintPerturbationAnalyzer.kt` 前 348 行的纯数据类型**：三个枚举、两个策略、请求、结果、观测、报告。
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

**B976 补了 `ConflictAnalyzer.kt` 第 31—261 行的报告侧**：`ConflictMinimality`、`ConflictValidity`、
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

**B976 ported lines 31–261 of `ConflictAnalyzer.kt`** — the minimality and validity enums, the verification, the group summary and `ConflictExplanation`. `Core/Analysis/` goes
from **16 to 17** and the gap from 5 to **4**. **From line 262 on are the two analyzers**, with `solver.solve(...)` at 630 and `session.solve(assumptions = ...)` at 755;
**that needs a real CP backend, so it was neither ported nor stubbed.** **Every check in `ConflictExplanation.Validate()` blocks a shape where the report looks concluded but
the evidence does not carry it**: a conflict on one target with a feasibility report on another; **the target itself among the blocking members**, so deleting it "resolves" the
conflict; the same source counted twice, **making minimality look stronger**; a member that was never active; a reachable target carrying blockers; **background blocking claiming
constraint-level minimality** when removing any original constraint cannot lift it; and **irreducibility claimed after verifying only one member.**

**B975 ported the first 348 lines of `ConstraintPerturbationAnalyzer.kt`** — three enums, two policies, the request, the result, the observation and the report.
`Core/Analysis/` goes from **15 to 16** and the gap from 6 to **5**. **From line 349 on is the analyzer itself**, with `activeBackend!!.solve(request)` and
`backend!!.solve(request)`; **that needs a real reoptimization backend, so it was neither ported nor stubbed.** The criteria pin three boundaries: **an operation-less policy does
nothing while the report reads "analysis complete, nothing found"**; **a growth factor of one retries the same delta and only ever reports "no effect"**; and **counting `Unknown` as
an improvement biases the whole report toward "perturbation works".** **One alias is easy to get wrong**: `RemovalEffective` only answers for a removal — returning `false` for an
RHS perturbation **reads as "the perturbation did nothing" when the real meaning is "this field does not apply".**

**B974 ported `TargetFeasibilityReport` and not the analyzer**: baseline line 673 is a real `solver.solve(model, options)` and the `ConstraintProgrammingSnapshotBuilder` before it
rebuilds a mutable model. **That needs a real CP backend, so it was neither ported nor stubbed.** `Core/Analysis/` goes from **14 to 15** and the gap from 7 to **6**. **The report
pins that the four statuses may not impersonate one another**, and **`Reachable` is not the same as proven** — a solution found within a time limit only shows the target
**may** be reachable.

**B973 corrects this table**: it had recorded `CriticalConstraintAnalysisSession` as needing a live solver. **That was the grep proxy misleading the reading** — the one parameter
typed `ConstraintProgrammingSolver` **is used only to read its `Descriptor`**. It is ported and **needs no backend**, so the remaining core part is **3421 lines**, plus **796** of
plugin work, for **4217**.

#### 一、依赖链的根只有一个

`Session` → `TargetFeasibilityReport` / `ConflictExplanation` / `ConstraintPerturbationReport` → 三个分析器 → `Pipeline`。

**这一整棵树都挂在 `CriticalConstraintAnalysisSession` 上，而它持有一个活的求解器实例。**
所以顺序不是"先搬小的"，而是**先搬那个能创建会话的**，否则搬来的每个类型都没有构造入口。

#### 二、"求解引用"只是 grep 代理，不是证明

上表的"求解引用"是 `grep -cE '\.solve\(|solveDual|Lowerer|Solver\b'` 的命中数。

**它是代理，不是证据。** B970 就是被它纠正的：`ConstraintActivityAnalyzer` 命中 0，但真正的理由是
**它的 `buildReport` 只读快照与赋值**，不是"grep 没找到所以不碰后端"。
**这条口径的局限写在这里，免得下次把 0 当成"不需要后端"的证明。**

#### 三、为什么这批不搬

这 8 个里最小的是 270 行的 `ConstraintProgrammingAnalysisBackends`，但它是**后端适配胶水**：
它的"输入"是求解器的回调，"输出"是求解器的状态。**搬过来只会得到一个无法调用的形状。**

**而 `Session` 要先有求解器才能建。**本仓有 Gurobi / Cplex / Copt / SCIP / Hexaly 等真实插件，
**所以这不是"没有后端"，而是"要真实后端"**——它属于集成门禁，不属于无后端批次。

#### 四、B972 的边界

这一批**没有新增源码**。它把 8 个缺口逐条记账并钉住数字，
**目的是让下一批从"要真实后端的 4558 行"开始，而不是从"还有 8 个不知道是啥"开始。**

### Appendix to item 6: what each of the remaining eight is missing (B972)

Earlier batches ported thirteen, block by block. **This batch stops calling the remaining eight "todo" and books each one individually**, because **"cannot" and "not done" are
different things** and merging them makes the next batch re-investigate. **The whole tree hangs on `CriticalConstraintAnalysisSession`, which holds a live solver instance** —
so the order is not "smallest first" but **"the one that can create a session first"**, or every ported type has no entry point. **The "solver refs" column is a grep proxy, not
evidence**: B970 was corrected by it — `ConstraintActivityAnalyzer` scored 0, but the real reason is that **its `buildReport` only reads a snapshot and an assignment**, not that
"grep found nothing, so no backend". **That limitation is written down here so a 0 is not read as proof of backend-freedom next time.** **This batch adds no source code**: it
books the eight gaps and pins the numbers, **so the next batch starts from "4558 lines that need a real backend" rather than "eight things nobody has looked at".**

### 货物属性模型：十二个类型（B1305 补齐）

**复审（B1297）发现基线 `PackageAttribute.kt` 的十二个类型在 C# 侧**一个都没有**，而三份文档也从未提到 `PackageAttribute`。**所以这一节先记的是"从未被记录"，而不是"待办"。**

**已补齐：** `AbstractCargoAttribute`、`AbstractStackingOnPolicy` + 三个堆叠策略（`Filter`／`CartonContainer`／`Box`）、`AbstractHangingPolicy` + 两个悬挂策略（`Relative`／`Absolute`）、`AbstractDeformationAttribute` + `LinearDeformationAttribute`、`AbstractWeightAttribute` + `WeightAttribute`；`PackageAttribute` 增加 `OverPackageTypes`（堆叠策略的第一道判断）。

**判据 `CargoAttributeModelTests`（9 条）：** 十二个类型都在（反射）；**生产代码里没有构造它们的调用方**（"仅模型、不改行为"的可执行版本）；四道门限各撞一次（包装类型／超重／平顶／层数）；未知高度判否；相对悬挂的面积比；重量与线性变形。

**两处如实记录的局限（不是"已完成"）：**

**① 最大高度的语义不同。** 基线把 `maxHeight` 存成**长度**（`Quantity&lt;FltX&gt;`），而 C# 现有物品模型把 `MaxHeight` 存成**层数**（`UInt64`）。**把层数当长度会让高度门限静默失效**，所以做成"未知即 null、策略判否"，并在 `ICargoView.MaxHeightLength` 上写明。

**② 圆柱投影还没接上。** `ActualItem` 的底部投影以"宽 × 深"作矩形近似；圆形投影要经过 `ShapeFootprint2.Circle`，那层还没接到物品视图上。

**边界：** **这些类型在基线里只被 `PackageAttribute.kt` 自身读取（B1297 实测 `MaterialPacker.kt` 只在构造时填默认值），所以补进 C# 后算法行为不变。** **B1310 起：** 五个策略槽位（`CargoAttribute`／`WeightAttribute`／`DeformationAttribute`／`HangingPolicy`／`StackingOnPolicy`）与递归层数计算也已补上；**有效层数按基线合成 `min(packageMaxLayer, weightAttribute.maxLayer)`，位置参数因此改名 `PackageMaxLayer`。**

### 箱型目标：两个 `*Minimization` 与一条过期的"搬不了"（B1306）

**`Domain/Packing/MixedBinObjectives.cs` 的抬头原来写着"基线里的两个 `*Minimization` 类依赖 `PreciseAssignment`（C# 侧不存在）"。****这句话**早就不成立**：`src/Framework.Bpp3d/Domain/LayerAssignment/Model/PreciseAssignment.cs` 一直在仓库里。****第 622 行那张"已撤回"的表（B918）就是同一个结论的第一次撤回——但注释没跟着改，于是它在下游又活了一轮。**

**已补齐（`Domain/LayerAssignment/service/limits/MixedBinObjectives.cs`）：**`BinVolumeMinimization`（系数 = 权重 × 箱型体积）、`BinTypeCostMinimization`（系数 = 权重 × 该箱型成本）、`PerTypeBinCost.Of`。

**三个跳过条件逐条照搬基线：** **成本表为空、权重为零、该箱型未配置成本——第三种**不进多项式**，而不是以 0 系数进去（0 项在求和里是噪声，在报告里会被读成"这个箱型免费"）。**"配置为零成本"同样跳过**，这是基线 `mapIndexedNotNull` 的另一半。

**判据 `MixedBinObjectiveTests`（5 条）：** 过期断言已消失且更正被记录、`PreciseAssignment` 确实在仓库里、三个跳过条件、零成本也跳过、体积系数按权重缩放、缺省成本由调用方决定。

**边界：** **这一批只产出"哪个箱拿到什么系数"，把系数注册进多项式仍是模型侧的事**（与 `BinAmountMinimization` 等既有目标同为标记式实现）。


### 第四个后端：MongoDB 收口（B1308）

**这一条本来被写成"MongoDB 本机无实例，仍未验证"。** **用户追问一句"是 docker 中没有吗"，核实之后：** **Docker 里没有容器、本机没有镜像、`compose.yaml` 只声明了 mysql/postgresql/redis/rabbitmq。** **所以"没有实例"是对的，但成因是"没人起"——和 Kafka 一样，是观察被当成了结论。**

**B1308 起了 `ospf-infra-mongo`（mongo:7，绑定 127.0.0.1:27017），三条判据用与另外三个后端同一套形状。**

#### 判据里有一处不能偷懒

**"入口能连上"不能用"`MongoClient` 构造成功"当证据：** **它的构造是惰性的，服务器不在也照样返回对象。** **所以判据真发一条 `ping`，并断言回复里的 `ok` 是 1。**

**"不可达报原因"那条把 `ServerSelectionTimeout` 压到 3 秒**，让失败有界——**否则驱动默认等 30 秒，一次排障跑成半分钟。**

#### 四个后端现在全部实测

| 后端 | 批次 | 判据 |
| --- | --- | --- |
| SQLite | B950 | 文件库 + 关连接再开，两条 |
| Redis | B1298 | 入口连通 / 五种值形状 / TTL / 不可达，四条 |
| MySQL、PostgreSQL | B1307 | 各三条 |
| Kafka | B1307 | 三条；并修掉 `Dispose` 不 flush 导致的静默丢失 |
| MongoDB | B1308 | `ping` / 插入-查-替换-删除 / 不可达，三条 |

**证据：** **`logs/p9-batch1323-backends.log`（14 passed = 12 条往返 + 2 条护栏）。**

### 转储六段齐备，并抓出两处"看起来有、其实没有"（B1309）

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

**· 行 `Id` 形如 `model-local-row:<进程级计数器>:<序号>`，所以 `PRJ` 改打 `source=r0`——稳定身份，且比基线那个 id 更可跨语言比对。**

**判据因此钉"非统计行逐行相同"，并断言 `stats=` 行确实存在，免得例外被悄悄扩大成整份输出。**

**证据：** **`logs/p9-batch1324-dump.log`（119 行）。**

### 策略槽位与递归层数：几何收成一个谓词（B1310）

**基线 `PackageAttribute` 的两块剩余：** **五个槽位 + `maxLayer = min(packageMaxLayer, weightAttribute.maxLayer)`；伴生对象里的 `layerLayer`／`layerHeight`／`layer`。**

#### 一、有效层数的名字问题

**C# 原来的位置参数就叫 `MaxLayer`，于是"包材层数"与"有效层数"共用了一个名字，合成规则没有地方落脚。** **改名 `PackageMaxLayer`，`MaxLayer` 改为合成结果；没配重量属性时两者相等，既有 14 处构造点行为不变。**

#### 二、递归与几何分开

**基线用 `QuantityPlacement3&lt;Item, FltX&gt;` 加两个几何助手（`topItemPlacements`／`filterBottomOverlapped`），而 C# 侧 `QuantityPlacement3` 在 `Application` 层、那两个助手没有对应物。** **但递归本身只问三件事：类型、高度、谁压在谁上面。** **所以几何收成一个谓词 `Covers`，由调用方实现；递归、同类型过滤、取最大值三条语义逐行照搬。**

**一处说明白的偏离：** **基线用协程并行求各分支最大值，这里是串行——结果相同（都是最大值），并行度属于调用方。** **另有 `ponytail:` 标注的 64 层深度上限：真实的垛不会那么深，而互相压住的环会让递归停不下来。**

#### 三、判据（`PackageAttributeSlotsTests`，5 条）

**有效层数只能取小且不改写包材层数；五个槽位都可空、都能挂上；三层同类型堆叠给出层数 3 与 3 倍高度；类型不同不计层；空底部集合给零。** **判据不伪造几何，而是按 y 坐标判定"压在上面"的最小放置。**

**一处判据自己的错：** **第一版把待计算的 `box` 也放进了底部集合，于是算法把它当成一块同类型底板、返回 1 而判据期望 0。** **基线的入参本来就是 bottoms——错在测试，不在移植。**

### 真实几何版的两个助手，以及一处语义分歧（B1311）

**上一批说"C# 侧没有 `topItemPlacements`／`filterBottomOverlapped` 的对应物"——错了两层：**

**① 底面重叠已经有等价物：** `PlacementFactory.OverlappedOnBottom`（同样比 X/Z 交叠），高度就是 `Position.Y + Unit.Height`。我搜的是两个包装名，而 C# 侧叫别的名字，搜不到就当成没有。**

**② 更糟的是 `PlacementTyping.cs` 里本来就有同名方法，而语义与基线不同：** **旧的 `TopItemPlacements` 取"全局最高一层的所有放置"，基线取"没有任何底面重叠且更高的放置压在它上面的放置"。** **一高一低并列时两者答案不同——基线两件都算顶层，旧实现只返回高的那件。** **旧实现全仓零调用方（实测），所以按基线替换，而不是并存两个同名不同义的方法。**

#### 现在的关系拆成两条，而不是一条

**`OverlapsBottom`（底面投影重叠，与高度无关）：** 递归用它，等价于基线的 `filterBottomOverlapped`；**`IsAbove`（谁更高）：** 顶层集合用它，等价于 `topPlacements` 的第二条。****第一版把两者合成一个 `Covers`（"贴着才算压在上面"），于是三层堆叠只数出两层——**两个关系不同，合成一个就丢信息。**

**`Covers` 抽象按用户要求保留：** **递归仍然只认这两个谓词，真实几何通过 `PlacementTyping.AsStackPlacement` 接进去，判据里的最小替身照旧可用。**

#### 判据 `PlacementTypingTests`（4 条）

**一高一低并列时两件都算顶层（旧语义在此分道）；底面重叠只认投影不认高度；真实几何接进递归给出三层三倍高；侧放的箱子不共享投影。**

### 两个混沌吸引子回到标准式（B1313，来源 Kotlin `966cafbc`）

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

### ONNX 插件：**未移植，等依赖决策**（来源 Kotlin `dee897d2`）

**这一条不是"没找到"，也不是"环境所限"，而是**一个需要产品决定的依赖问题**——所以它在这里，而不是在待办清单里悄悄挂着。**

| 项 | 实测 |
| --- | --- |
| 规模 | 48 文件、**+9851/−1550** |
| 内容 | `OnnxGraphParser`、`OnnxLowering`、`OnnxInference`、`OnnxAttribute`、`OnnxDataType`、`OnnxTensorConstant`／`Expr`／`Info`／`Operators`、`OnnxSubgraph`、`OnnxActivation`、`OnnxHalfPrecision`、`OnnxShapeOperators`、`OnnxError` 等 36 个文件 |
| 声明的依赖 | **两个**：`onnxruntime`、`onnx-protobuf_2.13` |
| C# 侧现状 | **零 ONNX 代码**，无 `Microsoft.ML.OnnxRuntime` 引用 |

**容易漏掉的那一半：** **除了推理运行时，还要能解析 ONNX 模型文件本身。** **Kotlin 用 `onnx-protobuf_2.13`；C# 侧对应物要么是某个 ONNX 模型包，要么自己用 protobuf 生成类。** **所以真要移植是"两个新依赖"，不是一个。**

**已选定选项 1（全量移植），并已开始落地。** **实测改变了两处原先的估计：**

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
- **分段激活：** `OnnxSymbolicActivation.Piecewise`（`Sigmoid`／`Tanh`／`Softplus`／`Elu`，对齐基线的界来源、1e6 上限、退化为常数与均匀插值）；Gurobi 上最小值 `sigmoid(-5)`。
- **符号激活（`Relu`）：** `OnnxSymbolicActivation` 把正部包成系数为一的单项式，"先登记函数符号、后展平"的顺序由 `OnnxModelAttachment` 保证；Gurobi 上两条成对判据验证（负半轴 0、正半轴 2）。
- **激活的常量折叠：** 八个激活算子在常量输入上折叠；**符号输入明确拒绝**（需要函数符号，属模型层）。
- **逐节点求值：** `OnnxGraphEvaluator`（在上下文里走一张图，覆盖 Identity/Cast/Add/Sub/Neg/Mul-常数，中间结果留在上下文里；作用域由调用方负责）。
- **降阶上下文：** `OnnxLoweringContext`（名字 → 表达式/形状，作用域读外写内、遮蔽与泄漏边界）。
- **`Scan` 落地接线：** `OnnxScanLowering`（每轮一个切片、切片数与长度必须一致、作用域每轮干净）。
- **`Scan` 契约：** `OnnxScanExpansion`（长度取输入首维、切片对齐、状态四数一致、与 Loop 共用规模预算）。
- **`Loop` 落地接线：** `OnnxLoopLowering`（每轮干净作用域、迭代变量为当轮常数、种子回写；**就地更新已支持（B1355：`TryRebind`）**）。
- **`Loop` 契约：** `OnnxLoopExpansion`（次数常量、携带值四个数目必须一致、展开规模预算 10000 节点）。
- **`If` 展开：** `OnnxIfExpansion`（常量条件选分支、按位置对齐输出、分支与声明个数必须一致）。
- **子图判定：** `OnnxSubgraphPolicy`（`If`/`Loop`/`Scan` 的可展开性、254/256 上限、嵌套按乘积）；**展开本身未移植**。
- **二次目标：** `OnnxQuadraticProduct` 的乘积经 `QuadraticFlattenData` 挂进 `QuadraticMetaModel` 并求解；`(x−3)²` 在 `[0,10]` 上最小 0、`[5,10]` 上最小 4（成对判据）。
- **二次乘积：** `OnnxQuadraticProduct`（两个一次式相乘成 `QuadraticPolynomial`，按无序对合并同类项）；**挂进元模型未做**。
- **形状算子：** `OnnxShapeTransform`（`Reshape`／`Flatten`／`Squeeze`／`Unsqueeze` 顺序保持、可落地；**`Transpose` 因重排无处可写而在多元素张量上拒绝**；`Concat` 同样需要逐元素表示）。
- **端到端：** 带 `If` 的模型从字节 → 解析 → 前置检查 → 求值 → 多项式，条件真/假各走一条、与手算一致；**导入器对含控制流的图自动转入求值路径**；`If`／`Loop`／`Scan` 均可从字节走到多项式，求值路径按符号合并同名项（`x + x` 不会变成两列）。
- **函数符号登记顺序：** **`OnnxModelAttachment` 先登记函数符号、再展平**，**因为展平之后符号已经变成多项式的项，再登记就找不回对应关系。** 这条顺序对逐元素展开同样成立。
- **覆盖范围之外的算子：** `Conv`／`MaxPool`／`AveragePool` **基线也没有**（降阶分派与全模块搜索都无实现），**所以不是移植缺口；`PositionalConsumers` 里列出它们只为判断"下游是否按位置读"。**
- **二次域批量维（B1388）：** `MatMul` 的批量前缀按 `BroadcastShape` 广播、**展开下标与一次域共用一份**、逐批量相乘。
- **二次域到求解器（B1387）：** ONNX 二次域产出的多项式**挂进 `QuadraticMetaModel` 并用 Gurobi 求解**；**变量必须取自导入结果**（`RealVar("x")` 两次是不同实例，元模型表里只有一个）。
- **二次域 `Mul`（B1386）：** 两侧含变量时按位置进入 `OnnxQuadraticProduct`；**"逐元素还是矩阵乘"由元素个数与内容决定，而不是形状。**
- **二次域 `Gemm`（B1385）：** `OnnxQuadraticElementList.Gemm`（转置→二次相乘→`alpha`→`beta·C`；**`C` 可一次可二次，二者都含二次项时按三次拒绝**）。
- **二次域求值（B1384）：** `OnnxImportMode.Quadratic` 让求值器按二次域算 `MatMul`，**结果走 `OutputQuadraticElements`；二次绑定喂给一次算子会被拒绝。**
- **二次域本体（B1383）：** `OnnxQuadraticElementList`（**只在一处产生：两侧含变量的二维矩阵乘**；**同类项合并、批量维拒绝**）。
- **`inputBindings` 的强制性（B1382）：** **默认入口在仿射链上拒绝多元素输入并点名 `inputBindings`；** **形状算子与控制流路径保留自己的拒绝（消息更具体）。**
- **批量维（B1381）：** `MatMul` 的批量前缀按 `OnnxShape.BroadcastShape` 广播、**`ExpandTo` 按 numpy 规则展开（源缺这一维按重复处理）**，再逐批量相乘。
- **归约（B1380）：** `OnnxElementList.ReduceSum`（空轴=全部、负轴归一、越界拒绝、`keepdims` 决定 `[1,1]` 还是标量）；**轴可来自属性，也可来自常量输入。**
- **常量权重（B1379）：** `initializer` 绑成逐元素常量列表并把 `MatMul`／`Gemm` 接进求值器；**混合情形按"把一个元素乘上另一侧的常数"计算，并去掉零系数项**。
- **`Gemm`（B1378）：** `OnnxElementList.Gemm`（转置→乘→`alpha`→`beta·C`，与基线同一顺序；`[N]` 偏置按行广播）。
- **矩阵乘（B1377）：** `OnnxElementList.MatMul`（秩 1 提升后压维、内维校验、一侧常量时乘加；**两侧含变量与批量维都明确拒绝**）。
- **采集（B1376）：** `OnnxElementList.Gather(indices, axis)`（**索引顺序即结果顺序**、负数按轴长归一、越界拒绝、按 `outer × 索引 × inner` 收集）；求值器要求索引是静态常量。
- **多输出与分片（B1374／B1375）：** `Split` 的每一段各自绑名（`EvaluateSplit`），**"只绑第一段"这种半成品会被判据抓住**。
- **分片算法（B1374）：** `OnnxElementList.Split(axis, sizes, count)`（游标逐段前进、均分除不尽拒绝、段长和与非正段长校验）。
- **拼接（B1373）：** `OnnxElementList.Concat(axis)`（对齐基线 `concat` 的循环结构、负数轴归一、非拼接维一致、**按行交错而不是整块接上**）。
- **形状传播（B1372）：** `OnnxShapePropagation` 沿生命周期算子推元素个数（`Add`／`Sub`／`Mul`／`Sum` 走广播、`Neg`／`Identity`／`Cast` 传下去、常量取自 `dims`），**认不出的算子一律"推不出来"而不是当 1**；**仿射链据此拒绝多元素输出，包括没声明形状的那种。**
- **列表语义（B1370／B1371）：** 调用方给每元素符号（`inputBindings`）→ 逐元素运算、形状算子、`Scan` 全程按元素；**输出按 `Output(index)`／`OutputElements` 取**；**元素符号个数与声明元素数不符时在 `bindings` 阶段拒绝**。
- **列表语义（B1370 基础）：** 逐元素算子按位置配对（含标量广播）、**形状多元素而无元素列表的绑定拒绝**、**多元素图输出拒绝而非取第一个位置**；元素列表随绑定一路写下去（早前它在绑定处被丢掉，判据抓到了）。
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
- **子图展开**（`OnnxSubgraph` 586 行）：判定已落地，`If`／`Loop`／`Scan` 三类控制流都已能真正展开（含逐节点求值）；激活算子只支持常量折叠（符号输入需函数符号）；形状与归约算子的逐节点求值尚未移植；
- **形状与归约算子接进模型：** 目前只做形状推导，不改写模型形状。

## ONNX 移植：已完成与未移植（逐项，含理由与升级路径）

**这一段先立一条所有项共同的前提：** **函数符号的登记顺序（先登记、后展平）** **在补做任何一项时都必须仍然成立——**
**展平之后符号已经变成多项式的项，再登记就找不回对应关系，** **而那种错只会表现为"某个函数符号不见了"。**

**This paragraph states one premise that holds for every item below: the registration order of function symbols (register first, flatten afterwards) has to survive whatever gets built — after flattening the
symbols are already terms of a polynomial and registering them afterwards cannot recover the correspondence, and that failure only shows up as "a function symbol went missing".**

**这份清单存在的理由：** **未移植项若不逐条写清"为什么没做、怎么补上、影响谁"，** **下一个人只能从代码里猜——而猜错的方向通常是"以为已经支持了"。**

| # | 未移植项 | 为什么没做 | 升级路径 | 影响 |
| --- | --- | --- | --- | --- |
| 1 | ~~其余激活的符号输入（`Sigmoid`／`Tanh`／`Softplus`／`Elu`）~~ **已补做（B1361）** | — | 按基线的 `approximated`（`OnnxPiecewiseApproximation` + `UnivariateLinearPiecewiseFunction`）走分段线性：界从变量自己取、`>1e6` 视作缺失、`lower == upper` 塌缩成常数、断点均匀且在断点上精确插值 | **已支持**：分段 `Sigmoid` 在 Gurobi 上求解，最小值 `sigmoid(-5) ≈ 0.0067` |
| 3 | **覆盖范围之外：`Conv` / `MaxPool` / `AveragePool`** | **基线 `dee897d2` 的降阶分派里没有这三个算子，** 整个 ONNX 模块搜 `Conv`／`Pool` 只命中 `Convert` 一词。**所以这不是 C# 的缺口，而是两侧都不覆盖的算子范围** | **没有升级路径可写：** **两侧都不覆盖，补它等于扩展基线范围而不是移植** | **不在移植范围内**：把它当作待补项会去实现基线本身没有的东西 |
| 2 | ~~二次域的批量维~~ **已补做（B1388）** | — | **与一次域同一套广播：** **下标数学共用 `ExpandIndices`，两个域各自用自己的元素去取；** **不相容的批量维仍然拒绝** | **已支持**：`[2,1,2]·[2,2,1]` 得 `[2,1,1]`（每批量两个二次项），批量 3 对 2 被拒 |
| 2 | **非目标：逐名对齐**（`OnnxCallbackAdapter` 377 行／`OnnxFunctionalModel` 等模型对象） | **C# 侧并不存在与 Kotlin 一一对应的模型对象：** 职责由 `OnnxModelAttachment` ＋ `OnnxAffineModelBuilder` 承担，**逐名对齐只会造出空壳。** 要做的是能力对齐，而能力已对齐 | **不需要补：** **能力已对齐，逐名对齐没有可补的东西；** 若将来 C# 侧真的引入对应对象，那时再谈名字 | **没有人会去找不存在的对象，也不会有人为了对齐名字而造空壳** |
| 3 | ~~`Scan` 运行期切片~~ **已支持（B1367／B1368）** | — | 长度取首维、每轮 `SliceLeading`，**求值器在扫描输入带元素列表时直接用它** | **已支持**：三片 `x0`／`x1`／`x2` 得 `x0 + x1 + x2`；**导入器路径仍要求常量扫描输入**（因为导入器还构造不出元素列表，与第 2 项同一层） |
| 5 | ~~`ir_version` 的版本分支~~ **已定案（B1364）** | — | 不分支：protobuf 字段号发布后不可改，新版本只追加字段；前提是"按字段号分派"与"未知字段被跳过"，两者各有判据 | **不支持按版本改语义**；`OnnxSchemaVersion` 列出 32 条字段号并如实报告某版本是否验证过 |
| 6 | ~~二次目标挂进元模型并求解~~ **已补做（B1360）** | — | 二次的 `AddObject` 在 `QuadraticMetaModel` 上；`ToQuadraticTetradModel` 的 `fixedVariables` 是"待固定"而非"参考点" | **已支持**：`(x−3)²` 在 `[0,10]` 上最小 0（x=3）、在 `[5,10]` 上最小 4，两条 Gurobi 判据成对验证 |

**已完成的部分（供对照，不重复叙述细节）：** 数据层（`OnnxDataType`／`OnnxHalfPrecision`／`OnnxError`／`OnnxTensorConstant`／`ProtoReader`／`OnnxModelParser`（含 `AttributeProto.g` 子图解析））、语义层（切分策略／激活语义／形状算术与六个算子／图校验／常量折叠／输出聚合／导入选项）、降阶层（算子表与前置检查／精确算子化简／近似算子降阶／界判定与界约束／仿射链／模型组装／模型挂接）、推理层（`OnnxInference`，用运行时真跑 `z = x + y` 对照手算）、控制流（三份契约 + 三条展开接线，**含就地更新的循环体：`TryRebind` 区分"声明"与"覆盖"**）、单一入口（`OnnxImporter`，按固定次序报告失败发生在哪一步）。
