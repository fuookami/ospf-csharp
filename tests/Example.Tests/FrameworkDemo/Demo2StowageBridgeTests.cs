// 装载矩阵桥接的判据 / Criteria for the stowage-matrix bridge.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **装载矩阵要把业务合法性和内核决策对上（B576）** / The stowage matrix must line up business legality with kernel decisions (B576).
/// </summary>
/// <remarks>
/// 基线 `Stowage.kt` 只做两件事：判合法性（`StowageNeeded`/`AdjustmentNeeded`）、把内核的稀疏决策翻译成 `stowage_{i}_{j}` 与 `loaded_{i}`。
/// 这两件事都有一种安静的错法：
///
/// - 合法性判错 → 不该有的组合有了决策变量，模型能解、解出来装了不该装的东西；
/// - 翻译顺序错（先翻译后注册内核）→ 每个 `stowage` 都是**空多项式**（恒零），模型照样能解，只是什么都不装。
///
/// 所以判据成对：合法组合有变量、不合法组合**没有**；`stowage` 的系数指着那个变量；`loaded` 的项数等于该货的合法舱位数。
///
/// The baseline `Stowage.kt` does two things: decide legality (`StowageNeeded`/`AdjustmentNeeded`) and translate the kernel's sparse decisions into `stowage_{i}_{j}` and
/// `loaded_{i}`. Both have a quiet failure mode: wrong legality produces decision variables for forbidden pairs (the model solves, and loads what it should not), and a wrong
/// translation order (translate before registering the kernel) makes every `stowage` an **empty polynomial** — identically zero, still solvable, loading nothing.
/// </remarks>
public class Demo2StowageBridgeTests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static Item MakeItem(string name, ItemStatus status, ItemLocation location, params CargoCode[] codes) {
        var cargos = codes.Select(CargoType.FromCode).ToHashSet();
        return new Item(
            name: name,
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: cargos.First(),
            source: "PEK",
            destination: "SHA",
            status: status,
            location: location,
            cargos: cargos);
    }

    private static StowagePosition MakePosition(
        string name,
        ulong id,
        PositionLocation location,
        bool stowageNeeded = true) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(0.0, 1.0, -0.5, 0.5),
            shape: new PositionShape(1.0, 1.0, 1.0),
            location: location);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(
                stowageNeeded: stowageNeeded,
                adjustmentNeeded: false,
                predicateWeightNeeded: true,
                recommendedWeightNeeded: false),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 1UL,
            // B582 起"需要谓词重量"就必须带三元组规格：`Load` 的松弛右端取 `min`，缺规格是**错误而不是跳过**
            // （跳过会让 `y` 无下界参照，模型照解）。这批既有 fixture 默认给了 `mlw` 那一口径的值。
            // Since B582 "needs the predicate weight" requires the triple: `Load`'s slack takes `min` and a missing specification is an **error, not a skip** (skipping leaves
            // `y` with no reference bound and the model still solves). These pre-existing fixtures default the bounds to the `mlw` figure.
            plw: new PositionPredicateLoadWeight(Plw: 1000.0, Min: 0.0, Max: 1000.0));
    }

    private static DiscreteLoadingContext<Flt64> MakeKernel(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions) {
        var loadingItems = items
            .Select(item => new LoadingItem<Flt64>(item.Name, Grams(item.Weight)))
            .ToList();
        var loadingPositions = positions
            .Select(position => new LoadingPosition<Flt64>(position.Name, position.Mla, Grams(position.Mlw.Mlw)))
            .ToList();

        var allowed = new HashSet<AssignmentKey>();
        for (int i = 0; i < items.Count; ++i) {
            for (int j = 0; j < positions.Count; ++j) {
                if (Stowage.StowageNeeded(items[i], positions[j])) {
                    allowed.Add(new AssignmentKey(items[i].Name, positions[j].Name));
                }
            }
        }

        var problem = new DiscreteLoadingProblem<Flt64>(
            Items: loadingItems,
            Positions: loadingPositions,
            AllowedAssignments: allowed,
            Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
            LayoutGroups: new List<LayoutSelectionGroup>(),
            WeightUnit: MassUnits.Gram);
        return new DiscreteLoadingContext<Flt64>(problem, new Flt64PackingValueAdapter());
    }

    private static (List<Item> Items, List<StowagePosition> Positions, DiscreteLoadingContext<Flt64> Kernel) Prepare() {
        var items = new List<Item> {
            MakeItem("I1", ItemStatus.Optional, ItemLocation.MainDeck, CargoCode.BAL),
            MakeItem("I2", ItemStatus.Optional, ItemLocation.LowNotBulk, CargoCode.BAL),
        };
        // P1 在低舱、P2 在主舱。基线 `enabled()` 只查"主舱位拒下舱货"，**不查**"下舱位拒主舱货"——
        // 所以 (I1, P1) 是合法的、(I2, P2) 不合法。这条单向性是基线原样，见下一条判据。
        // P1 is lower, P2 is main. The baseline `enabled()` only checks "a main position refuses lower cargo" and **not** "a lower position refuses main cargo" —
        // so (I1, P1) is legal and (I2, P2) is not. That one-directionality is the baseline as written; see the next criterion.
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, PositionLocation.LowForwardNotBulkLocation),
            MakePosition("P2", 2UL, PositionLocation.NormalMainLocation),
        };
        return (items, positions, MakeKernel(items, positions));
    }

    /// <summary>
    /// **合法性成对** / Legality is paired.
    /// </summary>
    /// <remarks>
    /// `I1`（主舱货）进 `P1`（低舱位）非法；`I1` 进 `P2`（主舱位）合法。同时钉 `AdjustmentNeeded`：只有
    /// `AdjustmentNeeded` 状态的货才可能需要调整，`Optional` 永远不需要。
    ///
    /// `I1` (main-deck cargo) into `P1` (a lower position) is illegal; `I1` into `P2` (a main position) is legal. `AdjustmentNeeded` is pinned too: only items in the
    /// `AdjustmentNeeded` status can need adjustment, `Optional` never can.
    /// </remarks>
    [Fact]
    public void StowageNeeded_ShouldPairLegalAndIllegalPairs() {
        (List<Item> items, List<StowagePosition> positions, DiscreteLoadingContext<Flt64> _) = Prepare();

        Stowage.StowageNeeded(items[0], positions[1]).Should().BeTrue("main cargo fits a main position");
        Stowage.StowageNeeded(items[1], positions[1]).Should().BeFalse("lower cargo does not fit a main position");
        Stowage.StowageNeeded(items[1], positions[0]).Should().BeTrue("lower cargo fits the lower hold");

        Stowage.AdjustmentNeeded(items[0], positions[1]).Should().BeFalse("an optional item needs no adjustment");
        Item adjusting = MakeItem("I3", ItemStatus.AdjustmentNeeded, ItemLocation.MainDeck, CargoCode.BAL);
        Stowage.AdjustmentNeeded(adjusting, positions[1]).Should().BeTrue("the adjustment status opens the adjustment path");

        ItemStatuses.StowageNeeded(ItemStatus.Loaded).Should().BeFalse("an already-loaded item needs no stowage decision");
        ItemStatuses.Available(ItemStatus.Reserved).Should().BeFalse();
        ItemStatuses.Available(ItemStatus.AdjustmentNeeded).Should().BeTrue();
    }


    /// <summary>
    /// **"下舱位拒主舱货"这条基线没查** / The baseline does not check "a lower position refuses main cargo".
    /// </summary>
    /// <remarks>
    /// 基线 `enabled()` 只写了一个方向：`this.location.main && item.location.low`（主舱位拒下舱货）。
    /// 反方向 `this.location.low && item.location.main` 只存在于 `ItemLocation.enabledIn(PositionLocation)`，
    /// 而那个方法在 Demo2 里**没有调用方**（本批逐文件搜过）。
    ///
    /// 结果：主舱货能装进下舱位。这是**基线原样**，不是本批引入的偏差；写在这里是为了让它留档——
    /// 否则下一个人会以为是移植漏了，然后"顺手修好"，把跨语言行为改掉。
    ///
    /// The baseline `enabled()` writes only one direction: `this.location.main && item.location.low` (a main position refuses lower cargo). The reverse
    /// `this.location.low && item.location.main` exists only in `ItemLocation.enabledIn(PositionLocation)`, and that method has **no caller** anywhere in Demo2 (searched file by
    /// file in this batch). Consequence: main-deck cargo can go into a lower position. That is **the baseline as written**, not a divergence introduced here — recorded so the
    /// next reader does not take it for a porting slip and "fix" the cross-language behaviour.
    /// </remarks>
    [Fact]
    public void StowageNeeded_ShouldRecordTheOneDirectionalBaselineRule() {
        (List<Item> items, List<StowagePosition> positions, DiscreteLoadingContext<Flt64> _) = Prepare();

        Stowage.StowageNeeded(items[0], positions[0]).Should().BeTrue(
            "the baseline checks only 'main position refuses lower cargo', so main cargo into the lower hold passes");
        Stowage.StowageNeeded(items[1], positions[1]).Should().BeFalse(
            "the direction the baseline does check: a main position refuses lower cargo");
    }

    /// <summary>
    /// **非法组合没有决策变量** / Illegal pairs get no decision variable.
    /// </summary>
    /// <remarks>
    /// 与"有变量"成对：`I1`-`P1` 非法 → `null`；`I1`-`P2` 合法 → 非 `null`。只测前者会让"全都没有变量"通过。
    ///
    /// Paired with "has a variable": `I1`-`P1` illegal → `null`; `I1`-`P2` legal → non-null. Testing only the first would pass if nothing had a variable.
    /// </remarks>
    [Fact]
    public void AssignmentVariable_ShouldExistOnlyForLegalPairs() {
        (List<Item> items, List<StowagePosition> positions, DiscreteLoadingContext<Flt64> kernel) = Prepare();
        var stowage = new Stowage(items, positions) { LoadingContext = kernel };
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();

        stowage.AssignmentVariable(1, 1).Should().BeNull("I2 (lower cargo) must not be stowable into the main position P2");
        stowage.AssignmentVariable(0, 1).Should().NotBeNull("I1 into the main position P2 is legal");
        stowage.AssignmentVariable(1, 0).Should().NotBeNull("I2 into the lower hold P1 is legal");
    }

    /// <summary>
    /// **`stowage_{i}_{j}` 的系数必须指着那个变量** / The `stowage_{i}_{j}` coefficient must point at that variable.
    /// </summary>
    /// <remarks>
    /// 只断言"符号存在"会漏掉最坏的情况：符号存在、多项式为空（恒零）。所以成对检查——合法组合恰好一个单项式、
    /// 系数为 1、符号就是决策变量；非法组合的多项式**必须为空**（不是零常数加一个假项）。
    ///
    /// Asserting "the symbol exists" misses the worst case: the symbol exists and the polynomial is empty (identically zero). So the check is paired — a legal pair has exactly one
    /// monomial with coefficient 1 on that very variable; an illegal pair's polynomial **must be empty** (not a zero constant with a fake term).
    /// </remarks>
    [Fact]
    public void StowageSymbol_ShouldCarryTheDecisionVariable() {
        (List<Item> items, List<StowagePosition> positions, DiscreteLoadingContext<Flt64> kernel) = Prepare();
        var stowage = new Stowage(items, positions) { LoadingContext = kernel };
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();

        LinearExpressionSymbol legal = stowage.StowageSymbol(0, 1)!;
        legal.Name.Should().Be("stowage_0_1");
        legal.Polynomial.Monomials.Should().ContainSingle();
        legal.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(1.0, 1e-9);
        legal.Polynomial.Monomials[0].Symbol.Should().BeSameAs(stowage.AssignmentVariable(0, 1));
        legal.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);

        LinearExpressionSymbol illegal = stowage.StowageSymbol(1, 1)!;
        illegal.Polynomial.Monomials.Should().BeEmpty("an illegal pair carries no decision term at all");
        illegal.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>
    /// **`loaded_{i}` 的项数等于该货的合法舱位数** / `loaded_{i}` has one term per legal position of the item.
    /// </summary>
    /// <remarks>
    /// 这条钉住"从内核聚合表翻译过来"这件事：自己按合法组合重算一遍也能得到相同项数，但拿到的会是**另一批变量**，
    /// 与内核的主问题脱钩——解出来的装载量与报告不一致。所以同时断言符号身份。
    ///
    /// This pins "translated from the kernel's aggregation": recomputing from the legal pairs would give the same count but **different variables**, decoupled from the kernel's
    /// master problem — the solved quantities then disagree with the report. So the symbol identity is asserted too.
    /// </remarks>
    [Fact]
    public void LoadedSymbol_ShouldMirrorTheKernelAggregation() {
        (List<Item> items, List<StowagePosition> positions, DiscreteLoadingContext<Flt64> kernel) = Prepare();
        var stowage = new Stowage(items, positions) { LoadingContext = kernel };
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();

        LinearExpressionSymbol loaded1 = stowage.LoadedSymbol(0)!;
        loaded1.Name.Should().Be("loaded_I1");
        loaded1.Polynomial.Monomials.Should().HaveCount(2, "I1 is legal in both P1 and P2 under the baseline's rules");
        loaded1.Polynomial.Monomials.Select(monomial => monomial.Symbol)
            .Should().Contain(new[] { stowage.AssignmentVariable(0, 0)!, stowage.AssignmentVariable(0, 1)! });

        LinearExpressionSymbol loaded2 = stowage.LoadedSymbol(1)!;
        loaded2.Polynomial.Monomials.Should().HaveCount(1, "I2 is legal only in the lower hold P1");
        loaded2.Polynomial.Monomials[0].Symbol.Should().BeSameAs(stowage.AssignmentVariable(1, 0));
    }

    /// <summary>
    /// **没有内核就明确失败** / Without a kernel, fail loudly.
    /// </summary>
    /// <remarks>
    /// 基线是 `if (!::loadingContext.isInitialized) return Failed(...)`。返回 `Ok` 会让调用方以为矩阵建好了，
    /// 而实际上一列都没有——这正是本仓反复修的那类壳行为。
    ///
    /// The baseline is `if (!::loadingContext.isInitialized) return Failed(...)`. Returning `Ok` would let the caller believe the matrix exists when there is not one column —
    /// exactly the shell behaviour this repo keeps removing.
    /// </remarks>
    [Fact]
    public void Register_ShouldFailWithoutALoadingContext() {
        (List<Item> items, List<StowagePosition> positions, DiscreteLoadingContext<Flt64> _) = Prepare();
        var stowage = new Stowage(items, positions);
        using var model = new LinearMetaModel<Flt64>();

        stowage.Register(model).IsOk.Should().BeFalse("an uninitialized loading context is an error, not an empty matrix");
        stowage.AssignmentVariable(0, 1).Should().BeNull("no context means no decisions to look up");
    }
}
