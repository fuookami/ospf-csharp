#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 装载位置状态。Stowage position status flags.
/// </summary>
/// <summary>
/// 舱位状态码 / Position status code.
/// </summary>
/// <remarks>
/// **与货物状态是两套东西**：货物用 `ItemStatus`（`Reserved`/`Optional`/`Preassigned`/`Loaded`/`AdjustmentNeeded`），舱位用自己的四码。
/// 把两者合成一个枚举会把 `Loaded` 与 `Reserved` 压成同一个状态——**两者都不可用、都不需要装载决策**，
/// 于是"已装货"和"被预留"在模型里再也分不开。
///
/// **Separate from the item status**: cargos use `ItemStatus` and positions use their own four codes. Merging them collapses `Loaded` and `Reserved` into one
/// state — **both are unavailable and both need no stowage decision** — so "already loaded" and "reserved" become indistinguishable in the model.
/// </remarks>
public enum PositionStatusCode
{
    /// <summary>舱位已装载 / Position is loaded</summary>
    Loaded,

    /// <summary>舱位未装载 / Position is unloaded</summary>
    Unloaded,

    /// <summary>舱位已预分配 / Position is preassigned</summary>
    Preassigned,

    /// <summary>舱位已预留 / Position is reserved</summary>
    Reserved
}

public sealed class PositionStatus
{
    /// <summary>状态码 / The status code</summary>
    public PositionStatusCode Code { get; }
    /// <summary>是否需要配载 / Whether stowage is needed</summary>
    public bool StowageNeeded { get; }
    /// <summary>是否需要调整 / Whether adjustment is needed</summary>
    public bool AdjustmentNeeded { get; }
    /// <summary>是否需要谓词重量 / Whether a predicate weight is needed</summary>
    public bool PredicateWeightNeeded { get; }
    /// <summary>是否需要推荐重量 / Whether a recommended weight is needed</summary>
    public bool RecommendedWeightNeeded { get; }

    /// <summary>舱位是否可用于装载 / Whether the position is available for stowage</summary>
    /// <remarks>
    /// 由状态码决定：`Loaded` 与 `Reserved` 都不可用。**不是从 `StowageNeeded || AdjustmentNeeded` 推出来的**——
    /// 两者在四个码上碰巧等价，但"可用"是码的属性，不是装载需求的导数。
    ///
    /// Decided by the code: `Loaded` and `Reserved` are both unavailable. **Not derived from `StowageNeeded || AdjustmentNeeded`** — the two coincide on the
    /// four codes, but availability is a property of the code, not a derivative of the stowage requirement.
    /// </remarks>
    public bool Available => Code is not (PositionStatusCode.Loaded or PositionStatusCode.Reserved);

    /// <summary>舱位是否不可用 / Whether the position is unavailable</summary>
    public bool Unavailable => !Available;

    /// <summary>
    /// 旧的四布尔构造（**会丢状态码**，仅供既有测试使用）/ Legacy four-flag constructor (**loses the code**, for existing tests only).
    /// </summary>
    /// <remarks>
    /// `AdjustmentNeeded` ⇒ `Preassigned`、`StowageNeeded` ⇒ `Unloaded`、其余 ⇒ `Loaded`——**推不出 `Reserved`**。
    /// 生产路径一律走 `From`，那里状态码是输入而不是猜出来的。`ponytail:` 这个重载只为不改几十个既有测试而留，
    /// 一旦测试改用 `From` 就删掉它。
    ///
    /// Maps `AdjustmentNeeded` to `Preassigned`, `StowageNeeded` to `Unloaded` and everything else to `Loaded` — **`Reserved` cannot be recovered**.
    /// Production always goes through `From`, where the code is given rather than guessed. `ponytail:` this overload exists only to avoid touching dozens of
    /// existing tests; delete it once they move to `From`.
    /// </remarks>
    public PositionStatus(bool stowageNeeded, bool adjustmentNeeded, bool predicateWeightNeeded, bool recommendedWeightNeeded)
        : this(
            adjustmentNeeded
                ? PositionStatusCode.Preassigned
                : stowageNeeded
                    ? PositionStatusCode.Unloaded
                    : PositionStatusCode.Loaded,
            stowageNeeded,
            adjustmentNeeded,
            predicateWeightNeeded,
            recommendedWeightNeeded) {
    }

    private PositionStatus(
        PositionStatusCode code,
        bool stowageNeeded,
        bool adjustmentNeeded,
        bool predicateWeightNeeded,
        bool recommendedWeightNeeded) {
        Code = code;
        StowageNeeded = stowageNeeded;
        AdjustmentNeeded = adjustmentNeeded;
        PredicateWeightNeeded = predicateWeightNeeded;
        RecommendedWeightNeeded = recommendedWeightNeeded;
    }

    /// <summary>
    /// 按状态码推导舱位状态（对齐基线 `PositionStatus.invoke`）/ Derive the status from the code (mirrors the code `PositionStatus.invoke`).
    /// </summary>
    /// <param name="location">舱位所属区域 / The position location</param>
    /// <param name="code">状态码 / The status code</param>
    /// <param name="stowageMode">装载模式 / The stowage mode</param>
    /// <returns>舱位状态 / The position status</returns>
    /// <remarks>
    /// 逐条对齐基线：`available = code != Loaded / code != Reserved`；`stowageNeeded = code == Unloaded || code == Preassigned`；
    /// `adjustmentNeeded = code == Preassigned`；**谓词/推荐重量只在"可用 / 要装载决策 / 主舱"且模式匹配时才为真**——
    /// 三个条件缺一不可，少判一个就会给下舱或已装载的舱位生成没有意义的重量变量。
    ///
    /// Member for member with the code: `available = code != Loaded / code != Reserved`; `stowageNeeded = code == Unloaded || code == Preassigned`;
    /// `adjustmentNeeded = code == Preassigned`; and **the predicate/recommended weight flags are true only when "available / decision needed / main deck"**
    /// and the mode matches — all three conditions are required, and dropping one fabricates meaningless weight variables for lower-deck or loaded positions.
    /// </remarks>
    public static PositionStatus From(PositionLocation location, PositionStatusCode code, StowageMode stowageMode) {
        ArgumentNullException.ThrowIfNull(location);
        bool available = code is not (PositionStatusCode.Loaded or PositionStatusCode.Reserved);
        bool stowageNeeded = code is PositionStatusCode.Unloaded or PositionStatusCode.Preassigned;
        bool adjustmentNeeded = code == PositionStatusCode.Preassigned;
        bool additionalWeightNeeded = available && stowageNeeded && location.Main;
        return new PositionStatus(
            code,
            stowageNeeded,
            adjustmentNeeded,
            additionalWeightNeeded && stowageMode == StowageMode.Predistribution,
            additionalWeightNeeded && stowageMode == StowageMode.WeightRecommendation);
    }
}

/// <summary>
/// 装载位置最大装载重量（支持分段线性）/ Position maximum load weight, with piecewise-linear support.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `PositionMaximumLoadWeight`。它有两种形态：**单一常数**（最常见）与**按零油重分段的阶跃**——
/// 后者是因为同一个舱位在轻载和重载时的允许装载量不同，重载时要给别的舱位留余量。
///
/// 与三处差异，都记在这里而不是埋着：
///
/// 1. **阶跃而不是插值**。那条平滑的 `UnivariateLinearPiecewiseFunction` 路径**被注释掉了**，实际生效的是
///    `invoke(zfw)` 里的分段查找（`zfw` 落在哪段就用哪段的 `mlw`）。C# 沿用**生效的那一条**，不沿用注释掉的那条。
/// 2. **构造时校验段序**。基线 `assert(piecewise.isNotEmpty)` 在 Kotlin 里默认关闭；C# 的 `ArgumentOutOfRangeException`
///    在 Release 下也生效。分段为空是调用方错误，静默接受会得到一个"永远返回 0"的规格。
/// 3. **`mlw` 取各段最大值**，与基线 `maxWithThreeWayComparatorOrNull` 一致；段按 `minZfw` 升序排。
///
/// Two forms: a **single constant** (the common case) and a **step function by zero-fuel weight** — the same position allows less when the aircraft is
/// heavier, leaving room for other positions. Three recorded divergences: (1) it is a **step, not an interpolation** — its smooth
/// `UnivariateLinearPiecewiseFunction` path is **commented out**, and what actually runs is the segment lookup in `invoke(zfw)`; C# copies what runs. (2) Segment order is
/// validated in the constructor — its `assert` is off by default in Kotlin while C#'s `ArgumentOutOfRangeException` fires in Release too. (3) `mlw` is the maximum
/// across segments, and segments are sorted by `minZfw`, both as in the code.
/// </remarks>
public sealed class MaxLoadWeightLimit
{
    /// <summary>最大装载重量 / The maximum load weight</summary>
    /// <remarks>
    /// 有分段时是各段的**最大值**，不是某个具体零油重下的值——要后者请用 <see cref="At"/>。
    ///
    /// With segments this is the **maximum** across them, not the value at a particular zero-fuel weight; use <see cref="At"/> for that.
    /// </remarks>
    public double Mlw { get; }

    /// <summary>分段线性段（按最小零油重升序）/ The piecewise segments, ordered by minimum zero-fuel weight</summary>
    public IReadOnlyList<Segment> Segments { get; }

    /// <summary>
    /// 分段线性段 / A piecewise-linear segment.
    /// </summary>
    /// <param name="Mlw">此段的最大装载重量 / The maximum load weight for this segment.</param>
    /// <param name="MinZfw">此段的最小零油重 / The segment's minimum zero-fuel weight.</param>
    /// <param name="MaxZfw">此段的最大零油重；无上界为 `null` / The segment's maximum zero-fuel weight, `null` when unbounded.</param>
    public sealed record Segment(double Mlw, double MinZfw, double? MaxZfw);

    /// <summary>
    /// 由单一常数构造 / Construct from a single constant.
    /// </summary>
    /// <param name="mlw">最大装载重量 / The maximum load weight.</param>
    public MaxLoadWeightLimit(double mlw) {
        Mlw = mlw;
        Segments = Array.Empty<Segment>();
    }

    /// <summary>
    /// 由分段构造 / Construct from segments.
    /// </summary>
    /// <param name="segments">分段 / The segments.</param>
    public MaxLoadWeightLimit(IReadOnlyList<Segment> segments) {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0) {
            throw new ArgumentOutOfRangeException(
                nameof(segments),
                "分段线性规格至少要有一段 / a piecewise specification needs at least one segment");
        }
        Segments = segments.OrderBy(segment => segment.MinZfw).ToList();
        double maximum = Segments[0].Mlw;
        foreach (Segment segment in Segments) {
            if (segment.Mlw > maximum) {
                maximum = segment.Mlw;
            }
        }
        Mlw = maximum;
    }

    /// <summary>
    /// 取给定零油重下的最大装载重量 / The maximum load weight at a given zero-fuel weight.
    /// </summary>
    /// <param name="zeroFuelWeight">零油重 / The zero-fuel weight.</param>
    /// <returns>该零油重下的最大装载重量 / The maximum load weight there.</returns>
    /// <remarks>
    /// 照做 `invoke(zfw)` 的三条分支：无分段用常数；低于第一段的下界用第一段；落在某段内用该段；都不落则用最后一段。
    /// **"都不落则用最后一段"不是兜底而是规格**：零油重超过最后一段的上界说明这架飞机已经超重，那时允许的装载量应当是最小的那个，
    /// 而不是"没有限制"。
    ///
    /// Copies its three branches: no segments means the constant; below the first segment's lower bound means the first segment; inside a segment means that segment;
    /// nothing matching means the last segment. **"Nothing matching means the last segment" is specification, not a fallback**: a zero-fuel weight past the last segment means the
    /// aircraft is already heavy, and the allowed load should be the smallest one — not "no limit".
    /// </remarks>
    public double At(double zeroFuelWeight) {
        if (Segments.Count == 0) {
            return Mlw;
        }
        if (zeroFuelWeight < Segments[0].MinZfw) {
            return Segments[0].Mlw;
        }
        foreach (Segment segment in Segments) {
            bool aboveLower = zeroFuelWeight >= segment.MinZfw;
            bool belowUpper = segment.MaxZfw is null || zeroFuelWeight < segment.MaxZfw;
            if (aboveLower && belowUpper) {
                return segment.Mlw;
            }
        }
        return Segments[Segments.Count - 1].Mlw;
    }

    /// <summary>是否分段 / Whether it is piecewise</summary>
    public bool Piecewise => Segments.Count > 0;
}

/// <summary>
/// 谓词装载重量规格 / Predicate load weight specification.
/// </summary>
/// <param name="Plw">谓词装载重量 / The predicate load weight.</param>
/// <param name="Min">允许的最小值 / The allowed minimum.</param>
/// <param name="Max">允许的最大值 / The allowed maximum.</param>
/// <remarks>
/// 逐条对齐基线 `PositionPredicateLoadWeight` 的**三元组**。C# 之前把它压成一个 `double?`，于是两处用量都丢了：
/// 一处要 `max`（`y` 的上界，见 `PredicateLoadWeightLimit`），一处要 `min`（谓词重量松弛的右端）。
/// 压成单个值时那个数字到底代表哪一端，读代码看不出来——补成三元组就是为了让"是哪一端"成为类型的一部分。
///
/// 's **triple**. C# had flattened it into a single `double?`, losing both uses in the code: one needs `max` (the upper bound on `y`, in
/// `PredicateLoadWeightLimit`) and one needs `min` (the right-hand side of the predicate weight slack). A single number does not say which end it is — the triple makes "which end"
/// part of the type.
/// </remarks>
public sealed record PositionPredicateLoadWeight(double Plw, double Min, double Max);

/// <summary>
/// 装载位置能力。Position capability check result.
/// </summary>
public sealed class PositionCapability
{
    /// <summary>是否通过 / Whether OK</summary>
    public bool Ok { get; }

    /// <summary>不允许的原因 / Why it is not allowed</summary>
    /// <remarks>
    /// 带上原因不是装饰：`Enabled` 有十几条规则，判据必须能说清**是哪一条**拒绝的。只返回 `false` 时，
    /// 一条写错的规则和一条正确拒绝的规则长得一模一样。
    ///
    /// The reason is not decoration: `Enabled` has a dozen rules and a criterion must be able to say **which one** refused. With a bare `false`, a wrongly written rule and a
    /// correctly refusing one look identical.
    /// </remarks>
    public string? Reason { get; }

    public PositionCapability(bool ok, string? reason = null) {
        Ok = ok;
        Reason = reason;
    }
}

/// <summary>
/// 配载位置。Stowage-specific position with status and loaded items.
/// </summary>
public sealed class StowagePosition
{
    /// <summary>飞机位置引用 / Aircraft position reference</summary>
    public Position AircraftPosition { get; }
    /// <summary>状态 / Status</summary>
    public PositionStatus Status { get; }
    /// <summary>最大装载重量限制 / Maximum load weight limit</summary>
    public MaxLoadWeightLimit Mlw { get; }
    /// <summary>最大装载数量 / Maximum load amount</summary>
    public ulong Mla { get; }
    /// <summary>已装载货物 / Loaded items</summary>
    public IReadOnlyList<Item> LoadedItems { get; }
    /// <summary>谓词装载重量规格 / The predicate load weight specification</summary>
    public PositionPredicateLoadWeight? Plw { get; }

    /// <summary>装载禁忌限制 / The stowage taboo restrictions</summary>
    public PositionStowageTaboo Taboo { get; }

    /// <summary>
    /// 建议装载数量 / The advised load amount.
    /// </summary>
    /// <remarks>
    /// **可空**：为 `null` 表示该舱位没有建议数量，`AdviceLoading` 会给恒零松弛、`AdviceLoadAmountLimit` 会跳过它。
    /// 与`Position.ala` 一致。
    ///
    /// **Nullable**: null means the position carries no advised amount, so `AdviceLoading` yields a constant-zero slack and `AdviceLoadAmountLimit` skips it, as in its
    /// `Position.ala`.
    /// </remarks>
    public double? Ala { get; init; }

    /// <summary>
    /// 建议装载重量 / The advised load weight.
    /// </summary>
    /// <remarks>
    /// **可空**，语义同 <see cref="Ala"/>。
    ///
    /// **Nullable**, with the same semantics as <see cref="Ala"/>.
    /// </remarks>
    public double? Alw { get; init; }

    public StowagePosition(
        Position aircraftPosition,
        PositionStatus status,
        MaxLoadWeightLimit mlw,
        ulong mla,
        IReadOnlyList<Item>? loadedItems = null,
        PositionPredicateLoadWeight? plw = null,
        PositionType? type = null,
        PositionStowageTaboo? taboo = null)
    {
        AircraftPosition = aircraftPosition;
        Status = status;
        Mlw = mlw;
        Mla = mla;
        LoadedItems = loadedItems ?? new List<Item>();
        Plw = plw;
        Type = type ?? PositionType.Empty;
        Taboo = taboo ?? PositionStowageTaboo.Empty;
    }

    /// <summary>舱位类型 / Position type</summary>
    /// <remarks>
    /// `enabled` 的判断大半落在"这个码在不在集合里"，所以类型必须在**舱位**上，而不是在规则里写死。
    ///
    /// Most of `enabled`'s checks are "is this code in the set", so the type belongs on the **position**, not hard-coded in the rules.
    /// </remarks>
    public PositionType Type { get; }

    /// <summary>位置名称 / Position name</summary>
    public string Name => AircraftPosition.SpaceName;
    /// <summary>位置位置 / Position location</summary>
    public PositionLocation Location => AircraftPosition.Location;

    /// <summary>检查货物是否启用 / Check if item is enabled at this position.</summary>
    /// <param name="item">货物 / The cargo item.</param>
    /// <returns>能力与原因 / Capability and reason.</returns>
    /// <remarks>
    /// 逐条对齐的 `enabled`：**货位互斥**（主/下、散/非散、头/尾）、
    /// **货类互斥**（虚拟装载物不进下舱；舱位类型码禁止刚硬/易碎/空集装器/放空；航材指定舱位只装航材；
    /// 普散货指定舱位不装航材；已装航材的散舱不装普通散货）、**散货同目的地**。
    ///
    /// 两条集装器规则也已补上：`item.Uld.Code` 不在 `enabledULDs` 里就拒，命中 `Taboo[item]` 的编号区间也拒。
    ///
    /// Copied rule by rule from its `enabled`: **location exclusions** (main/lower, bulk/non-bulk, head/tail), **cargo-class exclusions**
    /// (virtual cargo does not enter the lower deck; position type codes forbid stiff/crush/empty/virtual; AOG-MAT appointed positions take only AOG/MAT; normal-bulk appointed
    /// positions take no AOG/MAT; a bulk hold already holding AOG/MAT takes no ordinary bulk), and **same-destination bulk**.
    /// The two ULD rules are covered too : a `Uld.Code` outside `enabledULDs` is refused, and so is a hit on a `Taboo[item]` number range.
    /// </remarks>
    public PositionCapability Enabled(Item item) {
        ArgumentNullException.ThrowIfNull(item);

        // 货位互斥 / Location exclusions.
        if (Location.Main && item.Location.IsLow) {
            return new PositionCapability(false, "主舱禁止装载下货舱装载物");
        }
        if (Location.Low && (item.Location.IsHead || item.Location.IsTail)) {
            return new PositionCapability(false, "下货舱禁止装载头尾舱装载物");
        }
        if (Location.Bulk && !item.Location.IsBulk) {
            return new PositionCapability(false, "散货舱禁止装载非散货装载物");
        }
        if (!Location.Bulk && item.Location.IsBulk) {
            return new PositionCapability(false, "非散货舱禁止装载散货装载物");
        }
        if (Location.Head && !item.Location.IsHead) {
            return new PositionCapability(false, "头舱禁止装载非头舱装载物");
        }
        if (!Location.Head && item.Location.IsHead) {
            return new PositionCapability(false, "非头舱禁止装载头舱装载物");
        }
        if (Location.Tail && !item.Location.IsTail) {
            return new PositionCapability(false, "尾舱禁止装载非尾舱装载物");
        }
        if (!Location.Tail && item.Location.IsTail) {
            return new PositionCapability(false, "非尾舱禁止装载尾舱装载物");
        }
        if (Location.Low && item.Contains(CargoCode.Virtual)) {
            return new PositionCapability(false, "下货舱禁止装载虚拟装载物");
        }

        // 舱位集装箱限制 / Position ULD restrictions.
        if (item.Uld?.Code is { } uldCode && !AircraftPosition.EnabledULDs.Contains(uldCode)) {
            return new PositionCapability(false, "不支持该集装器类型");
        }
        if (Taboo[item] is { } taboo) {
            return new PositionCapability(
                false,
                "禁止装载 " + taboo.Uld + " 的 " + taboo.NoRange.LowerBound + " ~ " + taboo.NoRange.UpperBound + " 集装器");
        }

        // 舱位类型限制 / Position type restrictions.
        if (Type.Contains(PositionTypeCode.StiffCargoForbidden) && item.Contains(CargoCode.Stiff)) {
            return new PositionCapability(false, "厌恶刚硬性装载物的舱位禁止装载刚硬性装载物");
        }
        if (Type.Contains(PositionTypeCode.CrushItemForbidden) && item.Contains(CargoCode.Crush)) {
            return new PositionCapability(false, "厌恶易碎装载物的舱位禁止装载易碎装载物");
        }
        if (Type.Contains(PositionTypeCode.EmptyItemForbidden) && item.Contains(CargoCode.Empty)) {
            return new PositionCapability(false, "厌恶空集装器的舱位禁止装载空集装器");
        }
        if (Type.Contains(PositionTypeCode.EmptyForbidden) && item.Contains(CargoCode.Virtual)) {
            return new PositionCapability(false, "不可放空舱位禁止装载虚拟装载物");
        }
        if (Type.Contains(PositionTypeCode.AOGMATAppointed)
            && !item.Contains(CargoCode.AOG) && !item.Contains(CargoCode.MAT)) {
            return new PositionCapability(false, "航材指定舱位只能装载航材");
        }

        bool itemIsAogOrMat = item.Contains(CargoCode.AOG) || item.Contains(CargoCode.MAT);
        bool loadedHasAogOrMat = LoadedItems.Any(loaded => loaded.Contains(CargoCode.AOG) || loaded.Contains(CargoCode.MAT));

        if (Type.Contains(PositionTypeCode.NormalBulkAppointed) && !loadedHasAogOrMat && itemIsAogOrMat) {
            return new PositionCapability(false, "普散货指定舱位只能装载普通散货");
        }
        if (loadedHasAogOrMat && !itemIsAogOrMat) {
            return new PositionCapability(false, "已经装载航材的散舱不能装载普通散货");
        }

        // 已经装了普通散货的散舱 / A bulk hold already loaded with ordinary bulk.
        bool loadedOrdinaryBulk = LoadedItems.Any(
            loaded => loaded.Location.IsBulk && !loaded.Contains(CargoCode.AOG) && !loaded.Contains(CargoCode.MAT));
        if (loadedOrdinaryBulk) {
            if (itemIsAogOrMat) {
                return new PositionCapability(false, "已经装载普通散货的散舱不能装载航材");
            }
            var destinations = LoadedItems
                .Where(loaded => loaded.Location.IsBulk && !loaded.Contains(CargoCode.AOG) && !loaded.Contains(CargoCode.MAT))
                .Select(loaded => loaded.Destination)
                .Distinct()
                .ToList();
            if (!destinations.Contains(item.Destination)) {
                return new PositionCapability(false, "散货舱只能装载同一目的地的散货");
            }
        }

        return new PositionCapability(true);
    }

    /// <summary>
    /// 名字 / The name.
    /// </summary>
    /// <remarks>
    /// **必须覆写**，理由同 `Item.ToString`：中间符号名拼了舱位，不覆写会让每个舱位得到同一串类型全名，
    /// 模型注册报 `Repeated "…"`（实测）。
    ///
    /// **Must override**, for the same reason as `Item.ToString`: symbol names concatenate positions, and without this every position renders as the same type name, so model
    /// registration fails with `Repeated "…"` (measured).
    /// </remarks>
    /// <returns>舱位名 / The position name.</returns>
    public override string ToString() => Name;
}
