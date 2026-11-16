using Fuookami.Ospf.Math.Algebra.Number;
#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// 求解策略。Solve policy controlling Benders vs MILP path selection.
/// </summary>
/// <param name="PreferBenders">优先使用 Benders 分解 / Prefer Benders decomposition</param>
/// <param name="BendersFallbackToMilp">Benders 失败时回退到 MILP / Fallback to MILP on Benders failure</param>
/// <param name="PartitionColumnGeneration">是否走分区列生成 / Whether to take the partition column-generation path.</param>
/// <remarks>
/// **加在末尾**：位置参数一旦插进中间，所有按位传参的调用点都会被静默改写。
///
/// **Appended last**: inserting a positional parameter mid-record silently rewrites every positional call site.
/// </remarks>
public sealed record SolvePolicy(
    bool PreferBenders = false,
    bool BendersFallbackToMilp = true,
    bool PartitionColumnGeneration = false);

/// <summary>
/// 货物输入数据。Cargo input data for stowage planning.
/// </summary>
/// <remarks>
/// **`RequiresSeparation` 在本仓和基线里都没有读者**（逐个查过引用）：基线只有一条测试断言它存得进去，
/// 生产代码里没有任何一行读它。**这不是缺口，也不是漏接**——它是请求契约的一部分，抄过来是为了请求形状与基线一致。
/// 谁将来要用它（例如"需要分离装载的货物不得同舱"），那是**新功能**，得连着规则本身一起做；
/// **不要为了让它"看起来被用上"而临时接一条约束**——那会凭空造出一条基线没有的规则，而且没有任何判据能说明它对不对。
///
/// **`RequiresSeparation` has no reader in this port or in the baseline** ( checked every reference): the baseline has one test asserting it round-trips, and
/// no production line reads it. **This is neither a gap nor a missed connection** — it is part of the request contract, copied so the request shape matches the
/// baseline. Whoever needs it later (e.g. "cargos requiring separation may not share a position") is adding a **new feature** and has to build the rule with it;
/// **do not attach a constraint just to make it look used** — that invents a rule the baseline does not have, with nothing to say whether it is right.
/// </remarks>
/// <param name="Name">货物名称 / Cargo name</param>
/// <param name="Weight">货物重量 / Cargo weight (kg)</param>
/// <param name="Priority">优先级 / Priority level</param>
/// <param name="Source">始发站 / Source station</param>
/// <param name="Destination">目的站 / Destination station</param>
/// <param name="RequiresSeparation">需要分离装载 / Requires separation loading</param>
/// <param name="Required">必须装载 / Whether loading is mandatory</param>
/// <param name="Order">装载顺序 / The loading order</param>
/// <param name="AllowedPositions">允许的舱位名（`null` = 不限）/ Permitted position names (`null` = unrestricted)</param>
/// <param name="DeckLocation">所属甲板 / The deck it belongs to</param>
public sealed record CargoInput(
    string Name,
    double Weight,
    int Priority,
    string Source,
    string Destination,
    bool RequiresSeparation = false,
    bool Required = false,
    // 补齐基线 `CargoInput` 的三个字段**：`order`、`allowedPositions`、`deckLocation`。
    //
    // 前两个决定**允许指派**：基线先拒掉"`allowedPositions` 里出现未知舱位名"，再按
    // `allowedPositions?.contains(spaceName) != false` 与 `deckLocation == 该舱位所在甲板` 过滤。
    // 没有它们，装载内核**默认每件货都能去任何舱位**——一个不报错、只是把解空间放大的行为。
    //
    // `deckLocation` 还决定 `Item.location`：基线是 `Main → normalMain`、其余 → `lowNotBulk`，
    // 而在 之前这里**恒为主舱**，于是下舱货被当成主舱货。
    //
    // adds the baseline `CargoInput`'s three missing fields.** The first two decide **allowed assignments**: the baseline first rejects an
    // `allowedPositions` entry naming an unknown position, then filters by `allowedPositions?.contains(spaceName) != false` and `deckLocation == the position's
    // deck`. Without them the kernel **lets every cargo go anywhere** — no error, just a wider solution space. `deckLocation` also decides `Item.location`
    // (`Main → normalMain`, everything else → `lowNotBulk`), and before **every item was main deck**, so lower-deck cargo counted as main-deck cargo.
    int? Order = null,
    IReadOnlySet<string>? AllowedPositions = null,
    DeckLocationInput DeckLocation = DeckLocationInput.Main);

/// <summary>
/// 拖车类型输入 / The trailer-type input.
/// </summary>
/// <remarks>
/// 与基线 `TrailerTypeInput` 一一对应；转换在 `AggregationInitializer` 里完成，而不是靠枚举序号对上
/// （两边的顺序可能不同，靠序号对上是等着被悄悄改坏的写法）。
///
/// One-to-one with the baseline's `TrailerTypeInput`. The conversion happens in `AggregationInitializer` rather than relying on enum ordinals, which would silently break if either side
/// were reordered.
/// </remarks>
public enum TrailerTypeInput
{
    /// <summary>硬座 / Hardstand</summary>
    Hardstand,

    /// <summary>中转 / Transit</summary>
    Transit,

    /// <summary>仓库 / Warehouse</summary>
    Warehouse,
}

/// <summary>
/// 拖车输入 / The trailer input.
/// </summary>
/// <param name="Name">拖车名称 / The trailer name</param>
/// <param name="Type">拖车类型 / The trailer type</param>
/// <param name="Order">装载顺序 / The loading order</param>
/// <param name="Items">货物名称 / The cargo names</param>
public sealed record TrailerInput(
    string Name,
    TrailerTypeInput Type,
    int Order,
    IReadOnlyList<string> Items);

/// <summary>
/// 装载位置输入数据。Position input data for cargo stowage.
/// </summary>
/// <param name="Name">位置名称 / Position name</param>
/// <param name="MaxWeight">最大装载重量 / Maximum weight capacity (kg)</param>
/// <param name="LongitudinalArm">纵向力臂 / Longitudinal arm (inch)</param>
/// <param name="LateralArm">横向力臂 / Lateral arm (inch)</param>
/// <param name="SpaceId">所属物理空间 / The physical space it belongs to</param>
/// <param name="PartitionId">所属分区 / The partition it belongs to</param>
/// <param name="LoadingOrder">装载顺序索引 / The loading-order index</param>
/// <param name="Geometry">矩形边界 / The rectangle bounds</param>
/// <param name="StatusCode">舱位状态码 / The position status code</param>
/// <param name="PredicateLoadWeight">谓词装载重量规格 / The predicate load weight specification</param>
/// <param name="LoadedItems">已装载货物名 / Names of the cargo already loaded</param>
/// <param name="MaxLoadCount">最大装载件数 / Maximum item count</param>
/// <param name="Ala">建议装载件数（软目标）/ Advisory item count (soft target)</param>
/// <param name="Alw">建议装载重量（软目标）/ Advisory weight (soft target)</param>
public sealed record PositionInput(
    string Name,
    double MaxWeight,
    double LongitudinalArm,
    double LateralArm,
    // **起补齐基线所需字段**：`spaceId`/`partitionId`/`loadingOrder`/`geometry` 是 `Aircraft.AggregationInitializer` 的
    // 必要输入（舱位归属哪个物理空间、装载顺序索引、矩形边界）。缺它们时那个初始化器只能恒失败。
    // 全部给了默认值，既有两处 `new PositionInput("P1", 10.0, -1.0, -0.5)` 调用不受影响。
    //
    // **Fields the baseline needs, added**: `spaceId`, `partitionId`, `loadingOrder` and `geometry` are required inputs for the aircraft aggregation initializer
    // (which physical space a position belongs to, its loading-order index, its rectangle bounds). Without them that initializer could only fail. All defaulted, so the two existing
    // `new PositionInput("P1", 10.0, -1.0, -0.5)` call sites are unaffected.
    // 补齐舱位状态码**：舱位是 `Loaded`/`Reserved` 时不可用、`Preassigned` 时要调整——这些都从码推出来，
    // 不再由装配器恒写 `stowageNeeded = true`。默认 `Unloaded` 与既有调用一致。
    //
    // adds the position status code**: availability and the adjustment requirement come from the code instead of the assembler
    // hard-coding `stowageNeeded = true`. The `Unloaded` default matches every existing call site.
    string SpaceId = "MAIN",
    string PartitionId = "DEFAULT",
    int LoadingOrder = 0,
    PositionGeometryInput? Geometry = null,
    // **新字段一律加在末尾**：中间插字段会静默改写所有**位置参数**调用的含义——
    // 这里就撞了一次（一个 `string` 落到了状态码槽里，编译还报了"无法从 string 转换"才暴露）。带默认值也不例外。
    //
    // **A new field always goes last**: inserting one mid-list silently reinterprets every **positional** call site — a real instance hit exactly that (a `string` landed in the
    // status-code slot and only surfaced as "cannot convert from string"). A default value does not protect against it.
    PositionStatusCode StatusCode = PositionStatusCode.Unloaded,
    // 补上谓词装载重量规格**：预配载模式下 `Load` 会给每个 `PredicateWeightNeeded` 的舱位建一个谓词重量变量，
    // 它的上界与松弛右端都取自这份规格。没有它，预配载**装配阶段就失败**（：`IllegalArgument` + 点名舱位）。
    // 默认 `null` 与既有调用一致；只有要跑预配载的请求才需要给。
    //
    // adds the predicate load weight specification**: in predistribution mode `Load` creates a predicate weight variable for every position flagged
    // `PredicateWeightNeeded`, and both its upper bound and the slack right-hand side come from this spec. Without it, predistribution **fails during assembly**
    // . The `null` default keeps existing call sites working; only predistribution requests need it.
    PositionPredicateLoadWeightInput? PredicateLoadWeight = null,
    // 补上已装载货物名**：基线 `PositionInput.loadedItems`，装载内核用它填 `LoadingItem.FixedPositionId`。
    //
    // 没有这个字段时 `FixedPositionId` 只能硬编码 `null`，于是**已经装在某个舱位里的货物被当成自由货物**，
    // 求解器可以把它挪走——**而"挪走已装载货物"恰恰是调整模式要最小化的东西**。默认空表与既有调用一致。
    //
    // adds the already-loaded cargo names**: the baseline's `PositionInput.loadedItems`, which the loading kernel turns into
    // `LoadingItem.FixedPositionId`. Without it `FixedPositionId` could only be hard-coded `null`, so **an item already inside a position counted as free** and the
    // solver could move it — **while moving loaded cargo is exactly what adjustment mode minimises**. The empty default keeps existing call sites working.
    IReadOnlyList<string>? LoadedItems = null,
    // 补上基线 `PositionInput` 的三个字段**：`maxLoadCount`、`ala`、`alw`。
    //
    // `maxLoadCount` 是舱位的**最大装载件数**，基线拿它做两件事：校验 `<= 0` 要拒，以及
    // "已装载件数超过它"要拒。 之前本仓把它**硬编码成 `1UL`**（注释写"保守值"）：
    // 一个能装 20 件的舱位被当成只能装 1 件——**不报错，只是把可行解砍掉一大半。**
    //
    // `ala`/`alw` 是**建议装载件数/重量（软目标）**，基线的 `AdviceLoadAmountLimit`/`AdviceLoadWeightLimit`
    // 只收非 `null` 的舱位。模型侧早就实现了，**缺的一直是这两个字段的来路**，于是那两条目标永远为空。
    //
    // adds the baseline `PositionInput`'s three missing fields.** `maxLoadCount` is the position's **maximum item count**: the baseline rejects `<= 0`
    // and rejects "loaded items exceed it". Before this port **hard-coded `1UL`** (the comment said "conservative"), so a position that holds 20 items was
    // treated as holding 1 — **no error, just a solution space cut down by most of its width.** `ala`/`alw` are the **advisory count/weight (soft targets)**, and
    // the baseline's `AdviceLoadAmountLimit`/`AdviceLoadWeightLimit` only collect positions with a non-null value. The model side has been implemented for a long
    // time; **what was missing was where these two come from**, so both objectives were always empty.
    ulong MaxLoadCount = 1UL,
    double? Ala = null,
    double? Alw = null);

/// <summary>
/// 一种布局输入 / One layout input.
/// </summary>
/// <param name="Id">布局标识 / Layout identifier</param>
/// <param name="Positions">可用舱位名 / Permitted position names</param>
public sealed record LoadingLayoutInput(string Id, IReadOnlySet<string> Positions);

/// <summary>
/// 区域或整机恰选一种布局 / Exactly one regional or global layout choice.
/// </summary>
/// <param name="Id">组标识 / Group identifier</param>
/// <param name="Positions">受约束舱位名 / Governed position names</param>
/// <param name="Layouts">可选布局 / Available layouts</param>
public sealed record LayoutGroupInput(string Id, IReadOnlySet<string> Positions, IReadOnlyList<LoadingLayoutInput> Layouts);

/// <summary>
/// 额外业务互斥 / An additional business exclusion.
/// </summary>
/// <remarks>
/// **不覆盖几何冲突，只追加**（基线原话）。顺序无关：`(A,B)` 与 `(B,A)` 是同一条。
/// **Adds to the geometric conflicts, never replaces them** (the baseline says so), and order does not matter: `(A,B)` and `(B,A)` are the same edge.
/// </remarks>
/// <param name="First">首舱位名 / First position name</param>
/// <param name="Second">次舱位名 / Second position name</param>
public sealed record PositionConflictInput(string First, string Second);

/// <summary>重量单位输入 / Weight unit input.</summary>
public enum WeightUnitInput
{
    /// <summary>克 / Gram</summary>
    Gram,
    /// <summary>千克 / Kilogram</summary>
    Kilogram,
}

/// <summary>
/// 舱位谓词装载重量规格输入 / Position predicate load weight specification input.
/// </summary>
/// <remarks>
/// 对齐基线 `PositionPredicateLoadWeight` 的**三元组**，与模型侧的 `PositionPredicateLoadWeight` 同形。
/// **三端都要**：`Plw` 给谓词重量变量的上界，`Min` 给谓词重量松弛的右端，`Max` 参与区间判断——
/// 压成一个数就看不出哪一端是哪一端。
///
/// Mirrors the baseline `PositionPredicateLoadWeight` **triple**, the same shape as the model-side record. **All three ends are needed**: `Plw` bounds the
/// predicate weight variable, `Min` is the slack right-hand side, and `Max` takes part in the interval check — collapsed into one number, there is no way to tell
/// which end is which.
/// </remarks>
/// <param name="Plw">谓词重量上限 / The predicate weight bound</param>
/// <param name="Min">最小装载重量 / The minimum load weight</param>
/// <param name="Max">最大装载重量 / The maximum load weight</param>
public sealed record PositionPredicateLoadWeightInput(double Plw, double Min, double Max);

/// <summary>
/// 舱位矩形边界输入 / Position rectangle bounds input.
/// </summary>
/// <param name="Front">前边界 / Front boundary</param>
/// <param name="Back">后边界 / Back boundary</param>
/// <param name="Left">左边界 / Left boundary</param>
/// <param name="Right">右边界 / Right boundary</param>
public sealed record PositionGeometryInput(double Front, double Back, double Left, double Right);

/// <summary>
/// 独立物理空间输入 / Independent physical space input.
/// </summary>
/// <param name="Id">空间标识 / Space identifier</param>
/// <param name="Location">甲板分类 / Deck classification</param>
public sealed record LoadingSpaceInput(string Id, DeckLocationInput Location);

/// <summary>
/// 甲板分类输入 / Deck classification input.
/// </summary>
public enum DeckLocationInput
{
    /// <summary>主舱 / Main deck</summary>
    Main,
    /// <summary>下舱前段 / Lower forward hold</summary>
    LowForward,
    /// <summary>下舱后段 / Lower aft hold</summary>
    LowAft,
}

/// <summary>
/// 请求长度单位 / Request length unit.
/// </summary>
public enum LengthUnitInput
{
    /// <summary>米 / Meter</summary>
    Meter,
    /// <summary>厘米 / Centimeter</summary>
    Centimeter,
    /// <summary>英寸 / Inch</summary>
    Inch,
}

/// <summary>
/// 飞机类型输入枚举。Aircraft type input enum for request DTO.
/// </summary>
public enum AircraftTypeInput
{
    B737,
    B757,
    B767,
    B747,
    Unknown
}

/// <summary>
/// Benders 自适应配置。Benders decomposition adaptive configuration.
/// </summary>
/// <param name="MinBinaryVariables">最小二元变量数 / Minimum binary variables threshold</param>
/// <param name="MaxIterations">最大迭代次数 / Maximum iterations</param>
/// <param name="Tolerance">收敛容差 / Convergence tolerance</param>
public sealed record BendersAdaptiveConfig(
    int MinBinaryVariables = 4,
    int MaxIterations = 64,
    double Tolerance = 1e-6);

/// <summary>
/// Benders 质量覆盖配置。Benders quality override configuration.
/// </summary>
public sealed record BendersQualityOverrideConfig(
    double? WeakGapMultiplier = null,
    double? WeakGapFloor = null,
    int? IterationPressurePercent = null,
    int? CutDensityMinIterations = null,
    double? CutDensityThreshold = null,
    int? TrajectoryMinSnapshots = null,
    double? TrajectoryStepMultiplier = null,
    double? TrajectoryStepFloor = null,
    long? TimeGuardMinMs = null,
    double? ScoreGapWeight = null,
    double? ScoreTimeWeight = null,
    double? ScoreIterationWeight = null,
    double? ScoreCutDensityWeight = null,
    double? ScoreTrajectoryWeight = null);

/// <summary>
/// 建议打板目标配置。Weight recommendation objective configuration.
/// </summary>
/// <param name="BalancePriority">平衡优先级 / Balance priority weight</param>
/// <param name="PayloadPriority">业载优先级 / Payload priority weight</param>
public sealed record WeightRecommendationObjectiveConfig(
    double BalancePriority = 1000.0,
    double PayloadPriority = 1.0);

/// <summary>
/// 配载请求 DTO。Stowage planning request data transfer object.
/// </summary>
public sealed record RequestDTO(
    string Id,
    IReadOnlyList<CargoInput> Cargos,
    IReadOnlyList<PositionInput> Positions,
    AircraftTypeInput AircraftType = AircraftTypeInput.B737,
    // ****：基线把 `spaces` 与长度单位作为请求的一部分，`AggregationInitializer` 靠它们决定每个舱位落在哪块甲板、
    // 以及把力臂从请求单位换算到机型单位。默认值让既有请求继续可用。
    // ****: the baseline carries `spaces` and the length unit in the request; the aggregation initializer uses them to place each position on a deck and to convert arms from
    // the request unit into the model's. Defaults keep existing requests working.
    IReadOnlyList<LoadingSpaceInput>? Spaces = null,
    LengthUnitInput LengthUnit = LengthUnitInput.Inch,
    SolvePolicy? SolvePolicy = null,
    BendersAdaptiveConfig? BendersAdaptive = null,
    BendersQualityOverrideConfig? BendersQualityOverrides = null,
    WeightRecommendationObjectiveConfig? WeightRecommendationObjective = null,
    // ****：拖车是 `loading_effectiveness` 的输入，只有全载模式用到。默认为空，老请求不改。
    // ****: trailers feed `loading_effectiveness` and are used only in full load. Empty by default so existing requests are unchanged.
    IReadOnlyList<TrailerInput>? Trailers = null,
    double PayloadUpperBound = 20.0,
    double MinPayloadRatio = 0.6,
    double MaxAdjacentLoadGap = 8.0,
    double MaxCumulativeForwardLoad = 20.0,
    double MaxCumulativeBackwardLoad = 20.0,
    double EnvelopeLongitudinalMomentMin = -20.0,
    double EnvelopeLongitudinalMomentMax = 20.0,
    double TargetLongitudinalMoment = 0.0,
    double MaxLongitudinalMomentDeviation = 20.0,
    double MaxLateralImbalance = 12.0,
    // 补上基线 `RequestDTO` 的三个字段**：`weightUnit`、`layoutGroups`、`positionConflicts`。
    //
    // `positionConflicts` 是调用方声明的**业务互斥**，基线把它们并进装载问题的互斥集（**只增不减，不覆盖几何冲突**）。
    // 本仓一直是 `Conflicts: new HashSet<PositionConflict>`——**声明了也白声明，不报错，只是那条业务规则不存在**。
    //
    // `layoutGroups` 是"区域或整机恰选一种布局"的约束组，本仓一直是空表。**空表让"该恰选一种"变成"随便几种"。**
    //
    // `weightUnit` 决定请求里重量数字的单位。**基线默认 `Kilogram` 且真的读它**（舱位承重那一行用 `input.weightUnit`），
    // 所以这里也默认 `Kilogram`，并让货物重量、舱位承重、问题声明三处**都用它**。
    //
    // `weightUnit` decides the unit of the request's weight numbers. **The baseline defaults to `Kilogram` and really reads it** (the position-capacity line
    // uses `input.weightUnit`), so this defaults to `Kilogram` too and lets the item weight, the position cap and the problem declaration **all follow it**.
    //
    // adds the baseline `RequestDTO`'s three missing fields.** `positionConflicts` are the caller's **business exclusions**, which the baseline merges into
    // the loading problem (**adds only, never overriding geometry**). This port always passed `new HashSet<PositionConflict>` — **declaring one did nothing, with no
    // error; the rule simply did not exist.** `layoutGroups` carry "exactly one layout per region or aircraft"; the empty list turned "exactly one" into "any number".
    // `weightUnit` says what unit the request's weight numbers are in, and the baseline really reads it, so it is used for the item weight, the position cap and the
    // problem declaration alike.
    WeightUnitInput WeightUnit = WeightUnitInput.Kilogram,
    IReadOnlyList<LayoutGroupInput>? LayoutGroups = null,
    IReadOnlyList<PositionConflictInput>? PositionConflicts = null)
{
    /// <summary>解析后的求解策略（默认值） / Resolved solve policy with defaults</summary>
    public SolvePolicy ResolvedSolvePolicy => SolvePolicy ?? new SolvePolicy();
    /// <summary>解析后的 Benders 配置（默认值） / Resolved Benders config with defaults</summary>
    public BendersAdaptiveConfig ResolvedBendersAdaptive => BendersAdaptive ?? new BendersAdaptiveConfig();
    /// <summary>解析后的建议打板目标配置（默认值） / Resolved weight recommendation config with defaults</summary>
    public WeightRecommendationObjectiveConfig ResolvedWeightRecommendationObjective => WeightRecommendationObjective ?? new WeightRecommendationObjectiveConfig();

    /// <summary>
    /// 从本请求派生的业务目标参数 / The business objective parameters derived from this request.
    /// </summary>
    /// <remarks>
    /// **对齐基线的 `RequestDTO.parameter`**：基线把 `weightRecommendationObjective` 的两个优先级直接送进 `Parameter`，
    /// 而本仓在此之前到处都是 `new Parameter` —— **请求里的这两个优先级被静默丢弃**，权重推荐的目标系数永远是默认值。
    ///
    /// 这里只映射**基线映射过的那两个**。别的参数留在默认值上，是因为基线也没从请求里取它们；
    /// **不为了让这个属性看起来完整而顺手补几条没有依据的映射。**
    ///
    /// **Mirrors the baseline `RequestDTO.parameter`**: the baseline feeds the two priorities from `weightRecommendationObjective` straight into `Parameter`,
    /// while this port used `new Parameter` everywhere — **both priorities in the request were silently dropped** and the weight-recommendation objective
    /// coefficients were always the defaults. Only the two the baseline maps are mapped here. The rest stay at their defaults because the baseline does not read
    /// them from the request either; **no extra mapping is invented to make this property look complete.**
    /// </remarks>
    public Parameter ResolvedParameter {
        get {
            WeightRecommendationObjectiveConfig objective = ResolvedWeightRecommendationObjective;
            return new Parameter(
                WeightRecommendationBalance: new Flt64(objective.BalancePriority),
                WeightRecommendationPayload: new Flt64(objective.PayloadPriority));
        }
    }
}

/// <summary>
/// 诊断说明。Diagnostic note for structured output.
/// </summary>
/// <param name="Level">诊断级别 / Diagnostic level</param>
/// <param name="Group">诊断分组 / Diagnostic group</param>
/// <param name="Code">诊断代码 / Diagnostic code</param>
/// <param name="Message">诊断消息 / Diagnostic message</param>
public sealed record DiagnosticNote(
    string Level,
    string? Group = null,
    string? Code = null,
    string Message = "");

/// <summary>
/// 配载响应 DTO。Stowage planning response data transfer object.
/// </summary>
/// <param name="Succeed">是否成功 / Whether succeeded</param>
/// <param name="Status">状态描述 / Status description</param>
/// <param name="Objective">目标函数值 / Objective function value</param>
/// <param name="Assignments">装载分配结果 / Loading assignments</param>
/// <param name="Notes">备注列表 / Notes list</param>
/// <param name="Diagnostics">诊断信息 / Diagnostic notes</param>
public sealed record ResponseDTO(
    bool Succeed,
    string Status = "",
    double? Objective = null,
    IReadOnlyList<string>? Assignments = null,
    IReadOnlyList<string>? Notes = null,
    IReadOnlyList<DiagnosticNote>? Diagnostics = null)
{
    /// <summary>
    /// 创建无解响应。Create no-solution response.
    /// </summary>
    public static ResponseDTO NoSolution(string status, IReadOnlyList<string> notes) =>
        new(Succeed: false, Status: status, Notes: notes, Diagnostics: DiagnosticsHelper.BuildStructured(notes));

    /// <summary>
    /// 创建最优解响应。Create optimal solution response.
    /// </summary>
    public static ResponseDTO Optimal(double objective, IReadOnlyList<string>? assignments, IReadOnlyList<string> notes) =>
        new(Succeed: true, Status: "Optimal", Objective: objective, Assignments: assignments, Notes: notes, Diagnostics: DiagnosticsHelper.BuildStructured(notes));
}

/// <summary>
/// KPI 响应 DTO。KPI response data transfer object.
/// </summary>
/// <param name="Succeed">是否成功 / Whether succeeded</param>
public sealed record KPIResponseDTO(bool Succeed);

/// <summary>
/// 装载顺序响应 DTO。Loading order response data transfer object.
/// </summary>
/// <param name="Succeed">是否成功 / Whether succeeded</param>
/// <param name="Status">状态描述 / Status description</param>
/// <param name="Orders">装载顺序列表 / Loading order list</param>
/// <param name="Notes">备注列表 / Notes list</param>
/// <param name="Diagnostics">诊断信息 / Diagnostic notes</param>
public sealed record LoadingOrderResponseDTO(
    bool Succeed,
    string Status = "",
    IReadOnlyList<string>? Orders = null,
    IReadOnlyList<string>? Notes = null,
    IReadOnlyList<DiagnosticNote>? Diagnostics = null)
{
    /// <summary>
    /// 创建成功响应。Create success response.
    /// </summary>
    public static LoadingOrderResponseDTO Success(IReadOnlyList<string> orders, IReadOnlyList<string>? notes = null) =>
        new(Succeed: true, Status: "Optimal", Orders: orders, Notes: notes ?? Array.Empty<string>(), Diagnostics: DiagnosticsHelper.BuildStructured(notes ?? Array.Empty<string>()));
}

/// <summary>
/// 报告响应 DTO。Report response data transfer object.
/// </summary>
/// <param name="Succeed">是否成功 / Whether succeeded</param>
public sealed record ReportResponseDTO(bool Succeed);
