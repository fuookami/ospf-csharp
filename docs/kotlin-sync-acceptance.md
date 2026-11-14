# P4—P9 验收对照表（机器可核对）

**这张表的每一条证据都必须能被一条命令复现**：要么点名一个**存在的测试类**，要么给出一组**可数的文件/事实**。
做不到可复现的行，如实标「未核对」，不写"大概没问题"。

判据：`tests/Framework.Bpp3d.Tests/Docs/AcceptanceTableTests.cs`（会在 `tests/` 下逐个核对点名过的测试类是否真的存在）。

## 状态口径

| 记号 | 含义 |
| --- | --- |
| ✅ | **实测通过**：点名的那条判据真的跑过，门禁日志在 `logs/p8-batch*.log` |
| ◐ | **部分**：能力在，但有一块没验证（行内写明是哪块） |
| ❌ | **没做**：能力缺，行内写明缺什么 |
| ⊘ | **当前环境不可达**：需要许可或 SDK，不是代码缺口（行内写明缺什么） |

## 一、P4 共享 Framework 与远程协议

验收标准（计划 §三）：过期定价请求被拒绝；同轮定价共享对偶快照；失败回退重建模型；跨语言 wire fixture 通过。

| # | 验收项 | 证据（可复现） | 状态 |
| --- | --- | --- | --- |
| 4.1 | 过期定价请求被拒绝 | `ColumnProjectionTest`、`ColumnGenerationBatchTest` | ✅ |
| 4.2 | 同轮定价共享对偶快照 | `ColumnProjectionTest`、`ColumnGenerationApplicationServiceTests` | ✅ |
| 4.3 | 割校验 / 去重 / 恢复 | `LinearBendersCutsTest`、`BendersCheckpointTest` | ✅ |
| 4.4 | 逻辑 Benders 引擎 | `LogicBasedBendersTest`、`BendersSubResultCutsTests` | ✅ |
| 4.5 | 动态行与加删列生命周期 | `ConstraintWriteTransactionTest`、`MetaModelWriteTransactionTest`、`TokenRegistrationTransactionTest` | ✅ |
| 4.6 | 远程 wire 契约（`SerializedSolution` / `RemoteTaskSubmitRequest`） | `RemoteWireContractTest`、`RemoteFixtureProvenanceTests` | ✅ |
| 4.7 | **完整跨语言 wire fixture 矩阵** | 只对 `SerializedSolution`、`RemoteTaskSubmitRequest`、`configuration/sha256` 摘要做过真实往返。**B957 提过两块缺口，B1071—B1077 补上了**：基线的 `analysis-contract.tsv`（**跨语言语义契约**）与 `analysis-cases.tsv`（**96 行纯函数用例**）现已**逐字节**搬进 `tests/Core.Tests/Analysis/Fixtures/`，SHA-256 钉住，**契约 10 节 + 用例 6 节共 56 行全部有判据**。**B1111 更正**："`checkpoint-wire-contract.tsv` 没有 C# 这一方"**是错的**：C# 实现了同一套信封（`ConstraintProgrammingCheckpointEnvelope` 带 `integritySha256` 与逐字段 `JsonPropertyOrder`，`SolveCancellation` 带 `taskAbort`/`frameworkLoser`/`remoteStop` 这些**契约里才有的词**）。**产出同名线格式就受同一份契约约束**，否则"跨语言摘要失配"这件事 C# 是唯一不知道的一方。已补 `CheckpointWireContractTests` **4 条**：信封前 17 个字段顺序与契约逐项一致（**变异把 `runId` 从 10 改成 99 → 红，已验**）、取消来源词表只含契约的九个 code、双向往返、**兜底代码 `backend`/`vendorSpecificStop` 原样往返**（`Origin` 是普通 `string` 而非枚举）。**B1112 钉满**：信封 **25 个字段（0—24）全部**顺序一致（B1111 只钉了前 17，**而 `integritySha256` 按顺序算，第 18 个错位与前 17 个一样致命**）；`provenance` 前 7 个字段与契约一致，**且契约本身落后于实现**——`[provenance-field]` 只列到 6，而基线 `ConstraintProgrammingCheckpointV2.kt` 有 10 个（多 `randomSeed`/`deterministic`/`environmentSummary`），**这是契约的缺口、不是该改回去的东西**。**变异（`backendName` 顺序 1→50）→ 红，已验。****B1113 补上 B1111 遗留的未验证项**：兜底代码那条判据此前"能红"未验（`Origin` 是位置参数，改类型产生 26 个编译错误），现用**运行时变异**验证——给 `Origin` 挂只放行七个枚举词的 `JsonConverter`，**`"backend"` 立刻被改写成 `"user"`，判据红，已验**。**而这正是真实风险**：有人为"类型安全"加转换器，**而契约要的是原样保留，两者正好相反**。判据现 5 条 | ◐ |
| 4.8 | CP checkpoint envelope 的跨语言往返 | `ConstraintProgrammingCheckpointWireContractTest`（fixture 与基线**逐字节相同**：1555 字节、摘要 `3f2d2f49…`，可解码、可重编码、可复算摘要） | ✅ |
| 4.9 | 调度模型 / 能力摘要的**字段名** | `RemoteSchedulingWireNameTests`（`SchedulingEstimate` 4 个、`QualityTarget` 5 个、`SchedulingRequest` 15 个、能力摘要 5 个名字，逐字与 `12fdb587` 的 `@SerialName`/属性名一致） | ✅ |
| 4.10 | 调度模型 / 能力摘要的**跨语言往返** | **B1114 拆开核对了这一行，两个半边结论相反。****调度模型那一半是凭空要求**：`ActivitySchedulingProblem` 在基线里**从不经过线格式**（`src/Framework/Solver/Remote/` 下 0 处引用），基线的调度测试全是进程内构造、**没有任何序列化往返**——**"跨语言往返"这件事在基线里不存在，不是本仓缺。****能力摘要那一半已实测**：`RemoteSolverCapabilities`（基线 `ExecutionModels.kt`）**逐字段对应**，`ProbeCapabilitiesAsync` 走真实 HTTP 客户端 + stub transport，断言 `schemaVersion`/`protocolVersions`/`supportedModelTypes`/两个 checkpoint 标志与 URL 方法（`RemoteWireContractTest`，含"envelope code 非 OK 必须失败"）。**所以本行由 ❌ 改为 ✅**：该做的做了，不该做的不存在 | ✅ |

## 二、P5 基础设施插件

验收标准：SQLite 默认集成通过；其他服务具备显式集成入口；翻译失败不得静默改变查询语义。

| # | 验收项 | 证据（可复现） | 状态 |
| --- | --- | --- | --- |
| 5.1 | SQLite 默认集成 | `SqliteRepositoryTests` | ✅ |
| 5.2 | 谓词/关系查询翻译不静默改语义 | `PredicateSchemaGeneratorTests`、`SqlRelationalQueryCompilerTest` | ✅ |
| 5.3 | MongoDB / Redis / Kafka 的显式集成入口 | **入口齐备**：`MongoRepository<>`（143 行）、`RedisClient`（203 行）、`KafkaProducer`/`KafkaConsumer`。**B1102 补上第一条行为判据**：`RedisConfigRoundTripTests`（2 条，纯序列化、**不需要 Redis 服务**）——**十个字段往返全对**，含最容易悄悄丢的 `MasterName`（丢了下哨兵退化成直连**而连接仍成功**）与 `Database`（错索引读写另一个键空间**而不报错**）；空串/null 返回 `null` 而非抛。**变异（`ToJson` 剔掉两个字段）→ 红，已验。****其余仍是"成员在不在"型**：`RedisInterfaceTests` 10 条、`KafkaInterfaceTests`、`MongoRepository` 3 条结构性判据；**Kafka/Mongo 的行为判据需要真实实例**（见 `⊘3`） | ◐ |
| 5.4 | 真实数据库与 broker 往返 | **SQLite 已实测**（`SqliteRoundTripTests`：文件库 + 关连接再开，2 条判据）；MySQL/PostgreSQL **服务器在跑但本机无凭据**；MongoDB/Kafka 无实例（见 `⊘3`） | ◐ |
| 5.5 | PostgreSQL 仓储与提供程序 | `PostgreSqlRepository<>` + Npgsql 9.0.0；`PostgreSqlRepository_Should_Be_Abstract`、`PostgreSqlProvider_ShouldBeConfiguredWithoutALiveDatabase` | ✅ |
| 5.6 | PostgreSQL **连活库往返** | 无服务（`⊘3`）——`UseNpgsql` 只证明配置对，不证明连得上 | ⊘ |

## 三、P6 装箱与下料

验收标准：MILP、列生成或候选池流程按各模块真实能力闭环；含独立几何/制造复核、扩展管线、KPI、CSV 与结果提取。

| # | 验收项 | 证据（可复现） | 状态 |
| --- | --- | --- | --- |
| 6.1 | `Framework.Bpp1d` 列生成闭环 | `Bpp1dColumnGenerationTests` | ✅ |
| 6.2 | `Framework.Csp2d` 真实 SCIP 端到端（MILP / 列生成 / 卷材消耗三路） | `Csp2dSolverIntegrationTests` | ✅ |
| 6.3 | Bpp3d 装箱几何（重叠 / 缺几何 / 越界 / 支撑覆盖 / 装载计划 / 重量） | `PackingGeometryOverlapTests`、`PackingGeometryGuardTest` | ✅ |
| 6.4 | 层分配两条约束（`BinLoadingOrderConstraint` / `TailBinAssignmentConstraint`） | 源码在 `Framework.Bpp3d/Domain/LayerAssignment/service/limits/` | ✅ |
| 6.5 | 独立几何复核 | `Box2Geometry` 合并进 `Framework.Bpp2d.Domain`，重叠语义为"内部重叠"（相切不算） | ✅ |
| 6.6 | KPI / CSV / 结果提取 | **KPI 侧**：`PackingKpi.cs` 与 4 条判据（混单位换算、按放置数而非方案数计数、非长度量纲报错、空结果）。**CSV 侧**：`Bpp1dCsvAndRenderTests` 原有 9 条（空库存、声明库存、多一列、引号内逗号、双写引号、列数不齐、未闭合引号、非法数量、解析器失败）**加 B1097 的 1 条（少一列）**，共 10 条加载器判据 + 5 条渲染判据。**B1097 更正**：我先前写"286 行实现零判据"，**那是错的——已有 9 条**，我只补了一个方向（缺列） | ✅ |

## 四、P7 网络与调度

验收标准：context 注册与生命周期测试通过；跨模型注册态隔离；库存时间语义和路径/列证据正确。

| # | 验收项 | 证据（可复现） | 状态 |
| --- | --- | --- | --- |
| 7.1 | `Framework.NetworkScheduling`（基线 114 个主源码，0 真缺口） | `NetworkSchedulingSolverValueAdapterTest` | ✅ |
| 7.2 | 活动调度上下文与生命周期 | `ActivityContextAndServiceTests`、`ActivityCompiledTests` | ✅ |
| 7.3 | 甘特编译与求解 | `GanttCompilationSolverIntegrationTests` | ✅ |
| 7.4 | Gantt-Network 桥接 | `Framework.GanttNetworkScheduling.Tests` | ✅ |
| 7.5 | 路径/列证据与库存时间语义 | **库存时间语义已实测**：`ActivityMaterialAuditTests` 含"期初量独立判"（后到的入库不能抹掉期初不足）与 `NaN` 库存必须报出。**B1108 修正本行**：原来写的 `ScheduleBudgetTests` **不存在**（另一个被顺带引用的排程日志测试名同样不存在）——**验收表指向了一个不存在的测试，这一行因此不可核对**。真缺口是 `ScheduleBudget.cs`（**450 行**）里的日志状态机：**此前整个排程日志只有 3 条判据、全是"合法日志能往返"，没有一条证明非法日志会被拒**。已补 `ScheduleLogValidationTests` **7 条**：乱序对照组（顺序对通过 / 换序被拒 / `complete` 早于 `dispatch` 被拒）、未声明的算法被拒、负计数被拒、无配对 `complete` 被拒、校验和语义（**只验自洽不防篡改**——基线 `digest()` 同样如此）。**B1109 补 `ActivityProblemCompiler`**：577 行、**全仓测试一次都没提过它**；它做的是编译前的四条校验（时域非空 / 任务·资源·执行三类身份不重复 / 任务不与执行撞名），**失效的症状不是报错而是"编译出一个错的模型"**——重复任务 ID 会让两个活动合并成一个而结果看起来正常。已补 `ActivityProblemCompilerTests` 6 条（含合法对照组），**变异（关掉重复检查）→ 红，已验。****B1110 补 `RelationalQueryExpressionFreezer`**（329 行，同样零判据）：它深拷贝查询计划里的自定义 payload，**失效的症状是"计划在构造之后还会变"**——而计划要拿去缓存、比较、跨线程执行。已补 `RelationalQueryExpressionFreezerTests` **8 条**：改原集合/原字典不影响副本（**只断言"内容相同"不够，浅拷贝也满足**）、标量原样返回、不可复制 payload 被点名且**带层级路径**（`where.operand`）；**变异（序列不再拷贝）→ 红，副本变成 `{"changed","b","c"}`，已验** | ◐ |

## 五、P8 启发式

验收标准：门面参数覆盖、预算、随机流、数值黄金向量、约束排序与失败恢复契约通过。

| # | 验收项 | 证据（可复现） | 状态 |
| --- | --- | --- | --- |
| 8.1 | 算子族落地（`GA`/`GCO`/`GWO`/`HCA`/`HS`/`MVO`/`NS`/`PSO`/`SAA`/`SCA`/`SOA`/`WCA`/`WarSO`/`EVO`） | `HeuristicPolicyTest`、`HyperHeuristicCommitTests` | ✅ |
| 8.2 | runner / `SearchRegistry` / `CompositeSearchSession` | 源码在 `Core.Plugin.Heuristic` | ✅ |
| 8.3 | 策略训练与评估 | `PolicyTrainer<T>.Train` / `PolicyEvaluator<T>.Evaluate` 在位 | ✅ |
| 8.4 | 数值黄金向量 | **随机流**（SplitMix64，种子 0 与 12345 各 3 个数、精确到 1e-15）、**超体积奖励**（2D 与 3D 各一组，来自独立 Python `hv2d`）、**B1099 补拥挤距离**：`CrowdingDistanceTests.Assign_ShouldMatchANumericGoldenVector` 两组手算黄金值（三点组中间点 **2**；四点组 **73/60** 与 **16/15**，**两个数不等，所以同时钉住"逐点算"而非只有总和）**。**此前的判据只查"中间点有限"，而有限对 0.5 与 200 一视同仁** | ✅ |

## 六、P9 示例、发布与性能收口

验收标准：默认 CI、互通测试及代表性真实求解闭环通过；外部未验收项单列；性能结论有可复现证据。

| # | 验收项 | 证据（可复现） | 状态 |
| --- | --- | --- | --- |
| 9.1 | 默认 CI 绿 | `logs/p8-batch918-default-test.log`（9654 passed、0 failed、警告 1318） | ✅ |
| 9.2 | 真后端门禁绿 | `logs/p8-batch918-test.log`（9811 passed） | ✅ |
| 9.3 | Demo5 文档与判据 | `Demo5DocumentationTests`、`Demo5BaselineParityTests` | ✅ |
| 9.4 | Demo4 服务清单 | `Demo4ServiceInventoryTests` | ✅ |
| 9.5 | Demo2 单模型 Benders 链解出分配 | `ThePairFixture_ShouldDecodeItsAssignments`（`Optimal`、`decoded:2`） | ✅ |
| 9.6 | 外部未验收项单列 | `CapabilityMatrixTests`（`## 八、外部未验收项`） | ✅ |
| 9.7 | 门禁数字自洽且可在日志复现 | `GateNumbersTests` | ✅ |
| 9.8 | **性能可复现证据** | **B1101 重测**：`logs/p8-batch1101-benders-console.log`，**3 fork / 3 warmup / 15 迭代**，N = 40—45/数据集。`BendersSolve` 均值 **large 94.310 µs**（CI99.9% 92.964—95.657）、**medium 81.405 µs**（80.236—82.575）、**small 70.230 µs**（68.892—71.568），StdErr 0.33—0.38 µs（**0.4%—0.5%**）。**旧的 `p8-batch915` 是 N=1、StdErr=0、CI=NaN，连"三个数据集有没有区别"都答不了**（它给出 49/49/54，非单调）。**仍不外推**：这是同一台机器上的自身数字，不与 Kotlin 对跑（见 9.9） | ◐ |
| 9.9 | 与 Kotlin 基线的性能对比 | **B1115 查清了"没有对跑"的真实构成**：基线有完整 benchmark 模块（`ospf-kotlin-benchmark/`）与 `compare-benchmark-results.ps1`，**而它只读 JMH 的 `primaryMetric.score/scoreError/scoreUnit/rawData`**；本仓用 BenchmarkDotNet，输出 `Statistics.Mean/StandardError`。**两个格式都对，但不是同一个——格式不通可修，已修。**`logs/b1115-bdn-to-jmh.py` 完成转换，**并用基线自己的脚本验证过**（33 条，输出含均值、样本数、单位与 `ok`）；**判据 `TheBenchmarkFormats_ShouldBeBridgeable` 钉住转换器、`rawData` 字段与本节指向。****仍未做的**：真的跑一次 Kotlin 侧并与本仓并列——**所以本行仍是 ❌，且不宣称任何提升。****B1116 把"为什么不直接跑"也实测了**：`ospf-kotlin` 的 `main` 比固定基线 `12fdb587` **领先 7 个提交**（`81 files changed, 13054 insertions(+), 1586 deletions(-)`），其中**10 个是 `core/src/main` 的生产文件**（含 `MetaModel.kt`、`MechanismModel.kt`——**正是基准测的热路径**）。**所以在 `main` 上跑基准测的不是基线**；而切到 `12fdb587` 违反"不切换 checkout"，故本行不动 | ❌ |
| 9.10 | Demo2 全量补全（基线 **185** 个 `.kt` / C# **175** 文件，B945 实测） | **B1100 把 10 的差算清楚了**：`logs/b1100-demo2-diff.py` 按**主干多重集**比（目录被重命名，路径比不了），得 **38** 个缺额，其中 **33** 由"多个基线类型合并进一个 C# 文件"解释，**5 个真的没有对应物**：`ApplicationResponseMapper`、`ItemAdjustmentLimit`、`LoadingGeometryInput`、`SemanticParameter`、`TryHelpers`。**而其中 `ItemAdjustmentLimit` 在基线里加的行与 `ItemAssignmentLimit` 完全相同且无人调用、`LoadingGeometryInput` 零引用**——**这两条既有判据早已钉住"不抄是对的"（`Demo2RegistrationDeltaTests`、`Demo2DtoRecountTests`、`Demo2GapInventoryTests`）。****所以 185↔175 不是 10 个缺口，是"合并 + 已记录的少数不抄"。** 0 个空壳。**B1103 补上真缺口**：基线 `ApplicationResponseMapper.kt` 的 `solverFailureResponse` 会把 `ORModelInfeasible`/`ORModelInfeasibleOrUnbounded` **映射为 `NoSolution` 并注明已映射**，**本仓原来不看错误码、一律报 `DirectSolveFailed`**——**前者让人查模型，后者让人查后端，方向相反**。已按基线措辞补上（`DirectMilpRunner.SolverFailureResponse`），判据 `Demo2InfeasibleResponseTests` 4 条；**变异（退回一律 `DirectSolveFailed`）→ 两条不可行判据红、两条"其它错误码"仍绿，已验。****B1104 更正**：非不可行分支的状态**照基线是 `"Error"`**（`ResponseDTO(request, error)` 写死 `status = "Error"`、`notes = listOf(error.message)`），**`DirectSolveFailed`/`DomainRegisterFailed`/`ModelBuildFailed` 三个状态在基线里搜不到，是本仓自造的**——已改回 `"Error"`，并让 `CodeOf` 认识基线的 `"NotConverged"`（否则求解器异常被降级成 `ApplicationFailed`）。**B1105 清完**：`DirectMilpRunner` 两处、`FullLoadApplication` 一处也一并改回 `"Error"`，**三个自造状态在生产代码里已 0 产出**，`CodeOf` 里对应分支已删；判据 `TheInventedStatuses_ShouldNotBeProduced`（注释外不许出现这三个串、且 `NoSolution`/`NotConverged` 必须在），**变异（放回一处）→ 红，已验。****B1106 把口径铺开核对**：Demo2 共 **14 个** `NoSolution(...)` 状态串，**只有 3 个在基线里存在**（`NoSolution`/`UnsupportedAircraft`）；其余 11 个是**本仓自造**，但**多数是"未接线"的诚实提示**（`MilpSolverNotSupplied`/`BendersSolverUnavailable`/`BendersNotWiredForMode` 等）——**自造 ≠ 缺陷，缺陷是自造却没人钉**。查下来只剩 `BendersFailed` 无判据，已补 `TheTerminalBendersFailure_ShouldBeDistinctFromTheMissingFallback`（与"缺回退"那条必须是两个状态），**变异（合并）→ 红，已验。****B1107 量了"永真断言"这一层**：全仓 **45 条** `IsAssignableFrom(...).Should()` **永远不会单独变红**——**实测把 `IFloatingNumber<Flt64>` 从 `Flt64` 基接口列表删掉，`tests/Math.Tests` 立刻 1734 个编译错误**，**即"能编译 ⇒ 断言为真"**。它们作为清单有用，**被当判据计数才有害**；已在 `VacuousFailureAssertionBudgetTests` 里加棘轮（基线 **45**，只减不增），**新增一条即红，已验** | ◐ |

## 七、当前环境不可达（不是代码缺口）
**B956 逐条核了这张表**，四处与实测不符，已改：

| 行 | 原写 | 实测 |
| --- | --- | --- |
| 5.4 | 只验证了仓储接口（❌） | SQLite **有真实往返**（关连接再开），其余见 `⊘3`（◐） |
| 9.10 | 基线 **231** / C# **101** | **185** / **175**（B945 数过） |
| ⊘1 | "本机只有…的**部分**许可" | Gurobi/CPLEX/COPT **三个都真跑通过**并已进门禁 |
| ⊘3 | Kafka/MongoDB/Redis 无服务 | 三个**端口开着**，是**凭据**没有；Kafka/MongoDB 才是无实例 |

**`9.10` 那两个数字错得最要紧**：`231`/`101` 是项目早期的读数，而真实是 `185`/`175`。
一份把**差距写成 130 个文件**的表，会让下一个人以为还差一大半——**而实际只差 10 个**。
这和 B948 的 SCIP 计数是同一个形状：**错的数字会改变工作量判断，方向不重要。**

**B956 checked this table row by row and four were wrong.** The `9.10` numbers mattered most: `231`/`101` were early readings while the truth is `185`/`175`. A table
showing a **130-file gap** makes the next reader believe most of the work remains — **when ten files do**. Same shape as B948's SCIP count: **a wrong number changes the
judgement of the work, and the direction does not matter.**


| # | 项 | 缺什么 |
| --- | --- | --- |
| ⊘1 | MOSEK / MindOpt 的**有 SDK** 行为（缺的是**许可**，不是代码） | **B949 实测**：Gurobi 10.0.1（829 通过）、CPLEX 12.10（26）、COPT 8.0.3（37）**都有可用许可并已纳入门禁**；MOSEK 报 `err_missing_license_file(1008)`、MindOpt 报 `License is not valid` |
| ⊘2 | SCIP 之外的 CP 求解后端 | 无 SDK |
| ⊘3 | Kafka / MongoDB 真实往返 | **B950 探测**：9092 与 27017 端口关闭，无实例。MySQL 3306 / PostgreSQL 5432 / Redis 6379 **端口开着但无可用凭据**（`Access denied` / `28P01`） |
| ⊘4 | 与 Kotlin 基线的独占性能对比 | 无独占测量环境；计划 §五 明确"没有独占测量环境时不宣称性能提升" |

**这张表本身不是验收结论**：它是"每一行该用什么去核对"的索引。✅ 的行仍然只说明**点名的那条判据在跑**，
不说明该模块的每一处行为都验证过——那需要逐条判据读代码，表替不了。
