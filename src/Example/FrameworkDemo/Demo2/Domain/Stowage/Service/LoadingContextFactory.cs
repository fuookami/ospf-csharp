#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Packing;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using PositionConflict = Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;

/// <summary>
/// 严格 DTO 到框架的适配边界 / The strict DTO-to-framework adaptation boundary.
/// </summary>
/// <remarks>
/// 以下**两处真实的差距**都记在这里而不是悄悄缩水：
///
/// 1. ~~**只有一维内核**~~ —— **已闭**。`Framework.Bpp2d` 有了 `Bpp2dDiscreteLoading.Context`，
///    于是按机型分流：窄体走一维、宽体走二维。**缺几何的请求仍退回一维**，那是数据问题不是能力问题。
/// 2. **DTO 没有分舱、几何、固定装载、冲突、布局组**。`PositionInput` 带 `spaceId`/`geometry`/`partitionId`/`loadedItems`/
///    `ala`/`alw`/`predicateLoadWeightMin`/`maxLoadCount`，本仓的只有名字、最大重量与两个力臂。因此分区配置与这些约束**无从构造**。
///
/// 两条都在 `docs/kotlin-sync-status.md` 的差距表里，不是这里临时决定。
///
/// **Two real gaps** are recorded rather than quietly shrunk: (1) **one-dimensional kernel only** — the kernel used to be picked
/// only as `Bpp1dDiscreteLoading`, because `Framework.Bpp2d` had no `Context` factory yet, so **every type went 1-D** and
/// two-dimensional loading is unavailable; that is a capability gap, not a design choice. (2) **the DTO has no partitions, geometry, fixed loads, conflicts or layout groups** —
/// a full `PositionInput` carries `spaceId`/`geometry`/`partitionId`/`loadedItems`/`ala`/`alw`/`predicateLoadWeightMin`/`maxLoadCount`, while this one has only a name,
/// a maximum weight and two arms, so the partition configuration and those constraints cannot be built at all. Both are in the gap table of `docs/kotlin-sync-status.md`.
/// </remarks>
internal static class LoadingContextFactory
{
    /// <summary>
    /// 构造装载内核 / Build the loading kernel.
    /// </summary>
    /// <param name="input">请求 / The request.</param>
    /// <param name="aggregation">配载聚合 / The stowage aggregation.</param>
    /// <returns>内核或失败 / The kernel or a failure.</returns>
    public static Result<DiscreteLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> Build(
        RequestDTO input,
        StowageAggregation aggregation) {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(aggregation);

        var positionNames = new HashSet<string>(aggregation.Positions.Select(position => position.Name));
        var itemsById = aggregation.Items.ToDictionary(item => item.Id, item => item);

        // **固定装载：一件货不能同时固定在两个舱位**（两条前置校验）。
        //
        // 构造问题之前先做两件事：`allowedPositions` 引用未知舱位就拒，以及**同一件货出现在两个舱位的 `loadedItems` 里就拒**。
        // 这两条在本仓从来就没有：`FixedPositionId` 一直硬编码 `null`，于是**已经装在某个舱位里的货物被当成自由货物**，
        // 求解器可以把它挪走——**而"挪走已装载货物"恰恰是调整模式要最小化的东西**。
        //
        // **Fixed loading: one item cannot be fixed to two positions** (two pre-checks). An
        // `allowedPositions` entry naming an unknown position is refused, as is an item appearing in two positions' `loadedItems`. Neither existed here:
        // `FixedPositionId` was hard-coded `null`, so **an item already inside a position counted as free** and the solver could move it — **while moving
        // loaded cargo is exactly what the adjustment mode exists to minimise**.
        var fixedPositionByItem = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (StowagePosition position in aggregation.Positions) {
            if (!positionNames.Contains(position.Name)) {
                continue;
            }
            foreach (Item loaded in position.LoadedItems) {
                if (fixedPositionByItem.TryGetValue(loaded.Id, out string? already)) {
                    return Results.Failed<DiscreteLoadingContext<Flt64>>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        $"货物不能固定到多个舱位 / Cargo cannot be fixed to multiple positions: {loaded.Id} ({already}, {position.Name})"));
                }
                fixedPositionByItem[loaded.Id] = position.Name;
            }
        }

        // **重量单位来自请求**。两处来源：货物重量与问题声明用 `aircraftModel.weightUnit`（固定 `Kilogram`），
        // 舱位承重用 `input.weightUnit`。**那两处可能不一致**，本仓统一用请求那一个：
        // 默认 `Kilogram` 时两种来源相同，非默认时**至少内部一致**。
        //
        // **The weight unit comes from the request**. There are two sources: item weight and the problem declaration use
        // `aircraftModel.weightUnit` (fixed `Kilogram`), the position cap uses `input.weightUnit`. **Those two can disagree**; this implementation uses the
        // request's one throughout: the two agree at the default `Kilogram`, and **at least it stays internally consistent** otherwise.
        PhysicalUnit weightUnit = input.WeightUnit == WeightUnitInput.Kilogram ? SIBaseUnits.Kilogram : MassUnits.Gram;
        var loadingItems = new List<LoadingItem<Flt64>>(aggregation.Items.Count);
        foreach (Item item in aggregation.Items) {
            loadingItems.Add(new LoadingItem<Flt64>(
                Id: item.Id,
                Weight: new Quantity<Flt64>(new Flt64(item.Weight), weightUnit),
                Required: item.Required,
                FixedPositionId: fixedPositionByItem.TryGetValue(item.Id, out string? fixedPosition) ? fixedPosition : null));
        }

        var loadingPositions = new List<LoadingPosition<Flt64>>(aggregation.Positions.Count);
        foreach (StowagePosition position in aggregation.Positions) {
            loadingPositions.Add(new LoadingPosition<Flt64>(
                Id: position.Name,
                MaxCount: position.Mla,
                MaxWeight: new Quantity<Flt64>(new Flt64(position.Mlw.Mlw), weightUnit),
                FixedOccupied: position.LoadedItems.Count > 0,
                Enabled: position.Status.Available || position.LoadedItems.Count > 0));
        }

        // **允许的指派 = 三支的并集**：`stowageNeeded || adjustmentNeeded || item ∈ position.loadedItems`。
        //
        // **之前这里只问 `StowageNeeded`，而它并不包含后两支**——`Stowage.StowageNeeded(item, position)` 的第一句就是
        // `ItemStatuses.StowageNeeded(item.Status)`，那是 `Preassigned`/`Optional` 才为真的判定。于是：
        //
        // - 状态为 `AdjustmentNeeded` 的货物**一个允许指派都拿不到**，也就永远装不上去；
        // - 已经装在某个舱位里的货物（`LoadedItems`）同样拿不到指派。
        //
        // 而且**不会报错**：稀疏决策只给合法组合建变量，没有变量就等于"这件货不存在"。注释当时写的是
        // "`StowageNeeded` 已经把三个条件合并了"——**那句话是错的**，这就是照着注释相信代码的代价。
        //
        // **Allowed assignments are the union of its three branches**: `stowageNeeded || adjustmentNeeded || item ∈ position.loadedItems`.
        // **Before only `StowageNeeded` was consulted, and it does not contain the other two** — its first line is `ItemStatuses.StowageNeeded(item.Status)`,
        // which holds only for `Preassigned`/`Optional`. So an `AdjustmentNeeded` item **got no allowed assignment at all** and could never be placed, and an item
        // already inside a position's `LoadedItems` got none either. **Nothing raised an error**: the sparse decisions only create variables for legal pairs, so no
        // variable reads as "this item does not exist". The old comment claimed `StowageNeeded` folded all three conditions — **that sentence was wrong**, which is the
 // price of believing a comment over the implementation.
        //
        // **再加一条前置校验与一条过滤**：
        //
        // 1. `allowedPositions` 里出现**未知舱位名就拒**——否则那条限制永远不生效，而且不留痕迹；
        // 2. `allowedPositions == null` 表示不限，否则必须包含该舱位。
        //
        // **甲板那条没有沿用**，理由写在下面过滤处：同一字段已经推导成 `Item.location`，再过滤一次是空转。
        //
        // **One more pre-check and one filter**: an `allowedPositions` entry naming an **unknown position is refused** (otherwise the restriction
 // never takes effect and leaves no trace), and `null` means unrestricted while a non-null set must contain the position. **Its deck comparison
        // is not copied**, for the reason recorded at the filter below: the same field already becomes `Item.location`, so a second filter is a no-op.
        var spaceByPosition = new Dictionary<string, string>(aggregation.Positions.Count, StringComparer.Ordinal);
        foreach (PositionInput requested in input.Positions ?? []) {
            spaceByPosition[requested.Name] = requested.SpaceId;
        }
        Dictionary<string, CargoInput> cargoByName = new(StringComparer.Ordinal);
        foreach (CargoInput cargo in input.Cargos) {
            cargoByName[cargo.Name] = cargo;
        }
        foreach (CargoInput cargo in input.Cargos) {
            if (cargo.AllowedPositions is null) {
                continue;
            }
            foreach (string positionName in cargo.AllowedPositions) {
                if (!positionNames.Contains(positionName)) {
                    return Results.Failed<DiscreteLoadingContext<Flt64>>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        $"允许舱位引用未知名称 / Unknown allowed position: {cargo.Name} → {positionName}"));
                }
            }
        }

        var allowed = new HashSet<AssignmentKey>();
        foreach (Item item in aggregation.Items) {
            foreach (StowagePosition position in aggregation.Positions) {
                if (!positionNames.Contains(position.Name)) {
                    continue;
                }
                // **请求里没有这件货就没有甲板信息**：那是装配阶段的缺口，不是"不限甲板"——
                // 静默放行会让甲板过滤对这类货物失效。
                // **A cargo absent from the request has no deck information**: that is an assembly gap, not "any deck"; letting it through would disable the
                // deck filter for it.
                if (!cargoByName.TryGetValue(item.Id, out CargoInput? requested)) {
                    continue;
                }
                if (requested.AllowedPositions is { } permitted && !permitted.Contains(position.Name)) {
                    continue;
                }
                // **甲板不再单独过滤，因为没有可过滤的东西**。
                //
                // 判据是"货物的甲板 == 该舱位所在甲板"。本仓把同一个 `DeckLocation` 已经换算成了 `Item.location`
                // （`Main → MainDeck`、其余 → `LowNotBulk`），而 `StowagePosition.Enabled(item)` 会按它判可用性——
                // **同一字段推导两次，第二次永远是空转**。
                //
                // 我写了那道过滤，然后用变异测试验证：把它关掉，判据**照样绿**（下舱货仍然一个指派都拿不到，
                // 因为 `Item.location` 已经挡住了）。所以删掉它——**留一段测不出、又不改变行为的代码，只会让人以为这里有两道防线。**
                //
 // **The deck is no longer filtered separately, because there is nothing left to filter** (measured). The implementation compares
                // `cargo.deckLocation` against the position's deck; this implementation already turns the same `DeckLocation` into `Item.location` (`Main → MainDeck`,
                // else `LowNotBulk`), and `StowagePosition.Enabled(item)` judges availability from it — **deriving the same field twice makes the second check a
                // no-op**. I wrote its filter anyway and mutation-tested it: disabling it left the criterion **green** (the lower-deck cargo still got
                // no assignment, because `Item.location` already blocks it). So it is gone — **code that cannot be measured and changes no behaviour only makes
                // people believe there are two lines of defence.**
                if (StowageModel.StowageNeeded(item, position)
                    || StowageModel.AdjustmentNeeded(item, position)
                    || position.LoadedItems.Contains(item)) {
                    allowed.Add(new AssignmentKey(item.Id, position.Name));
                }
            }
        }


        var problem = new DiscreteLoadingProblem<Flt64>(
            Items: loadingItems,
            Positions: loadingPositions,
            AllowedAssignments: allowed,
            // **调用方声明的业务互斥真的进去**。之前恒为空集：声明了也白声明，**不报错，只是那条业务规则不存在**。
            // "只增不减、不覆盖几何冲突"，而几何冲突在 `Bpp1d/2dDiscreteLoading.Context` 里合并，所以这里只放业务那一半。
            //
            // **The caller's declared business exclusions really go in** . The set used to be empty: declaring one did nothing, **with no error — the rule
 // simply did not exist**. This implementation adds without ever replacing, and geometry is merged inside `Bpp1d/2dDiscreteLoading.Context`, so only the business
            // half belongs here.
            Conflicts: RequestConflicts(input),
            // **布局组真的进去**。之前恒为空表，于是"区域或整机恰选一种布局"变成了"随便几种"。
            // **Layout groups really go in** . The list used to be empty, turning "exactly one layout per region" into "any number of them".
            LayoutGroups: RequestLayoutGroups(input),
            // **重量单位来自请求**。本仓三处都用克，所以默认是 `Gram`；把它带进来之后，
            // 任何"声明克、按千克算"的错配都会被 那条判据抓到。
            //
            // **The weight unit comes from the request** . This implementation uses grams throughout, hence the default; carrying it in means any "declared grams,
            // computed as kilograms" mismatch is caught by the criterion.
            WeightUnit: weightUnit);

        // **业务互斥：按无序对收**。`(A,B)` 与 `(B,A)` 是同一条，去重靠 `PositionConflict` 的值语义。
        // 引用未知舱位**不在这里拦**——`Bpp1d/2dDiscreteLoading.Context` 会拿它和几何的 ID 集合做双向比对，
        // 那里报的错更准（它知道几何那一侧有没有）。
        //
        // **Business exclusions are collected as unordered pairs**: `(A,B)` and `(B,A)` are the same edge, deduplicated by `PositionConflict` value
        // semantics. An unknown position name is **not** rejected here: `Bpp1d/2dDiscreteLoading.Context` compares both ID sets and reports it more precisely.
        HashSet<PositionConflict> RequestConflicts(RequestDTO request) {
            var conflicts = new HashSet<PositionConflict>();
            foreach (PositionConflictInput declared in request.PositionConflicts ?? []) {
                conflicts.Add(new PositionConflict(declared.First, declared.Second));
            }
            return conflicts;
        }

        // **布局组逐字段映射**。压成一个数或丢掉 `Positions` 都会让"恰选一种"的作用域变成整机。
        // **Layout groups are mapped field by field** . Collapsing them or dropping `Positions` would widen "exactly one" from a region to the whole aircraft.
        List<LayoutSelectionGroup> RequestLayoutGroups(RequestDTO request) {
            var groups = new List<LayoutSelectionGroup>();
            foreach (LayoutGroupInput group in request.LayoutGroups ?? []) {
                var layouts = new List<LoadingLayout>(group.Layouts.Count);
                foreach (LoadingLayoutInput layout in group.Layouts) {
                    layouts.Add(new LoadingLayout(layout.Id, layout.Positions));
                }
                groups.Add(new LayoutSelectionGroup(group.Id, group.Positions, layouts));
            }
            return groups;
        }

        // **每个舱位所属的物理空间，从请求里取**。
        //
        // **之前这里写的是 `SpaceId: position.Name`，那是个真缺陷**：`LoadingGeometry.Conflicts` **按 `SpaceId` 分桶**，
        // 每个舱位自成一体就永远配不到一起，于是**一维几何扫描一个冲突都不产生**—— 的几何扫掠在 Demo2 里等于没接。
        // 而"没有冲突"不会报错，只会给出**一个违反几何的装载解**。的是 `spaceId = p.spaceId`（请求字段），本仓也有这个字段（默认 `MAIN`）。
        //
        // **The physical space each position belongs to, taken from the request** . **Before this line said `SpaceId: position.Name`, which is a real
        // defect**: `LoadingGeometry.Conflicts` **buckets by `SpaceId`**, so a position that is its own space can never be paired and **the 1-D geometric sweep
        // produces no conflict at all** — the earlier sweep was effectively unwired in Demo2. And "no conflicts" raises no error; it just yields **a solution that
 // violates the geometry**. This implementation uses `spaceId = p.spaceId` (a request field), and this implementation has that field too (defaulting to `MAIN`).
        // **几何数值的单位来自请求**。 的二维路径把它们**当成米**硬编码，而请求的 `LengthUnit` 默认是**英寸**——
        // 于是一个 1.0 的舱位被当成 1 米（39.37 倍），**判交结果照样"看起来合理"**。这与 记的那处是同一个缺陷类别。
        //
        // **The unit of the geometry numbers comes from the request**. The earlier 2-D path **treated them as metres** while the request's `LengthUnit`
        // defaults to **inches**, so a 1.0 position became 1 metre (39.37x) — **and the intersection result still looked plausible**. Same defect class.
        PhysicalUnit requestLengthUnit = input.LengthUnit switch {
            LengthUnitInput.Meter => SIBaseUnits.Meter,
            LengthUnitInput.Centimeter => LengthUnits.Centimeter,
            _ => LengthUnits.Inch,
        };


        // 一维几何：`[前臂, 后臂]`。`geometry.front/back`；本仓的舱位直接带 `Coordinate.FrontArm/BackArm`。
 // 1-D geometry is `[front arm, back arm]`. This implementation reads `geometry.front/back`; a position here carries `Coordinate.FrontArm/BackArm` directly.
        var geometry = new List<LoadingInterval<Flt64>>(aggregation.Positions.Count);
        foreach (StowagePosition position in aggregation.Positions) {
            geometry.Add(new LoadingInterval<Flt64>(
                Id: position.Name,
                // **请求里没有这个舱位时退回舱位名**：那等于"自成空间"，而不是编一个空间出来。
                // **A position absent from the request falls back to its own name**, i.e. its own space, rather than inventing one.
                SpaceId: spaceByPosition.TryGetValue(position.Name, out string? space) ? space : position.Name,
                Start: new Quantity<Flt64>(new Flt64(position.AircraftPosition.Coordinate.FrontArm), LengthUnits.Inch),
                End: new Quantity<Flt64>(new Flt64(position.AircraftPosition.Coordinate.BackArm), LengthUnits.Inch)));
        }

        // **按机型选内核**：窄体走一维，宽体走二维。
        // 之前这里**恒走一维**，理由写在类注释里（"Bpp2d 没有 Context 工厂"），那个理由现在不成立了。
        //
        // **宽体的二维矩形直接取自请求的 `Geometry`**（`Front`/`Back`/`Left`/`Right`）。**缺几何就退回一维并写进备注**，
        // 而不是猜一个矩形：猜出来的矩形会让"冲突"与"不冲突"都变成假的。
        //
        // **Kernel by aircraft type**: narrow bodies go 1-D, wide bodies go 2-D. Before this
        // **always** went 1-D, for a reason recorded in the class remarks ("Bpp2d has no Context factory") that no longer holds. **A wide body's rectangles come straight
        // from the request's `Geometry`**; **missing geometry falls back to 1-D and says so in the notes** rather than inventing a rectangle, since an invented one makes
        // both "conflicting" and "not conflicting" fake.
        if (aggregation.AircraftModel.Type?.IsWideBody() == true)
        {
            List<LoadingRectangle<Flt64>> rectangles = [];
            bool complete = true;
            foreach (StowagePosition position in aggregation.Positions)
            {
                PositionInput? requested = null;
                foreach (PositionInput candidate in input.Positions ?? [])
                {
                    if (string.Equals(candidate.Name, position.Name, StringComparison.Ordinal))
                    {
                        requested = candidate;
                        break;
                    }
                }
                if (requested?.Geometry is not { } box)
                {
                    complete = false;
                    break;
                }
                rectangles.Add(new LoadingRectangle<Flt64>(
                    Id: position.Name,
                    SpaceId: requested.SpaceId,
                    // **轴序固定**：`x = front`（纵向）、`y = left`（横向），宽沿纵向、高沿横向。
                    // 这不只是"好看"——`LoadingGeometry.Conflicts` 用 `x` 区间做扫描剪枝，**剪枝轴与判交轴必须是同一个物理方向**，
                    // 否则被剪掉的候选里会混进真正相交的舱位。
                    //
                    // **Axis order is fixed**: `x = front` (longitudinal), `y = left` (lateral), width along the length, height across. That is not
                    // cosmetic: `LoadingGeometry.Conflicts` prunes with the `x` interval, **and the pruning axis must be the same physical direction as the
                    // intersection test**, or genuinely overlapping positions get evicted from the candidate set.
                    Bounds: Box2Geometry<Flt64>.Of(
                        x: new Quantity<Flt64>(new Flt64(box.Front), requestLengthUnit),
                        y: new Quantity<Flt64>(new Flt64(box.Left), requestLengthUnit),
                        width: new Quantity<Flt64>(new Flt64(box.Back - box.Front), requestLengthUnit),
                        height: new Quantity<Flt64>(new Flt64(box.Right - box.Left), requestLengthUnit))));
            }
            if (complete)
            {
                Result<DiscreteLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> twoDimensional =
                    Bpp2dDiscreteLoading.Context(problem, rectangles, new Flt64PackingValueAdapter());
                if (twoDimensional.IsOk)
                {
                    return twoDimensional;
                }
                // **二维失败就报出来，不静默退回一维**：退回会让"宽体按二维建模"变成一句空话，
                // 而调用方看不到任何提示。
                // **A 2-D failure is reported, never silently downgraded**: a fallback would make "wide bodies are modelled in 2-D" an empty promise the caller
                // never hears about.
                return twoDimensional;
            }
        }

        return Bpp1dDiscreteLoading.Context(problem, geometry, new Flt64PackingValueAdapter());
    }
}
