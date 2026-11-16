// 货物时效域的判据 / Criteria for the express-effectiveness domain.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using ExpressGenerator = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Service.PipelineListGenerator;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **货物时效域** / The express-effectiveness domain.
/// </summary>
/// <remarks>
/// 三个模型（绝对顺序、相对顺序）与三条管线。判据的重点：**装载模式二选一**、绝对系数公式的两处加一、
/// `IfFunction` 条件的默认 `GT` 关系、以及"空集合不加目标"。
///
/// Two models (absolute and relative order) plus three pipelines. The criteria focus on **one model chosen by stowage mode**, the two plus-ones in the absolute coefficient formula,
/// `IfFunction`'s default `GT` relation, and "an empty set adds no objective".
/// </remarks>
public class Demo2ExpressEffectivenessTests {
    private static Item MakeItem(string name, int priority, double weight = 100.0) =>
        new(
            name: name,
            weight: weight,
            priority: new CargoPriority("P" + priority, new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)priority), CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);

    private static StowagePosition MakePosition(string name, ulong id, byte order) {
        var position = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.Position(
            id: id,
            spaceName: name,
            sizeCode: "N",
            linearLoadingOrder: order,
            coordinate: new PositionCoordinate(100.0 * id, 100.0 * id + 100.0, -60.0, -10.0),
            shape: new PositionShape(100.0, 50.0, 5000.0),
            location: PositionLocation.NormalMainLocation);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(true, false, false, false),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 3UL);
    }

    private sealed class Rig
    {
        public Rig(IReadOnlyList<Item> items, IReadOnlyList<StowagePosition> positions) {
            Items = items;
            Positions = positions;
            var allowed = new HashSet<AssignmentKey>();
            foreach (Item item in Items) {
                foreach (StowagePosition position in Positions) {
                    if (StowageModel.StowageNeeded(item, position)) {
                        allowed.Add(new AssignmentKey(item.Id, position.Name));
                    }
                }
            }
            var problem = new DiscreteLoadingProblem<Flt64>(
                Items: Items.Select(item => new LoadingItem<Flt64>(
                    item.Id, new Quantity<Flt64>(new Flt64(item.Weight), MassUnits.Gram))).ToList(),
                Positions: Positions.Select(position => new LoadingPosition<Flt64>(
                    position.Name, position.Mla,
                    new Quantity<Flt64>(new Flt64(position.Mlw.Mlw), MassUnits.Gram))).ToList(),
                AllowedAssignments: allowed,
                Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
                LayoutGroups: new List<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.LayoutSelectionGroup>(),
                WeightUnit: MassUnits.Gram);
            Stowage = new StowageModel(Items, Positions) {
                LoadingContext = new DiscreteLoadingContext<Flt64>(problem, new Flt64PackingValueAdapter()),
            };
        }

        public IReadOnlyList<Item> Items { get; }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public StowageModel Stowage { get; }

        public LinearMetaModel<Flt64> Register() {
            var model = new LinearMetaModel<Flt64>();
            Stowage.Register(model).IsOk.Should().BeTrue();
            return model;
        }
    }

    /// <summary>
    /// **浮点余量等于`NONZERO_TOLERANCE`** / The floating-point margin equals its `NONZERO_TOLERANCE`.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. 相对顺序的条件常数与 `strictBoundary` 都是 **1e-10**，即基线 `NONZERO_TOLERANCE` 的值。
    /// 2. **1e-9 会改变行为**：条件常数与边界同时放大时 `SafeLattice` 仍能通过（因为两者同步走），
    /// 所以只有把这个常量本身钉住才能发现偏差。它是一个**校准旋钮**，不是可以随手改的实现细节。
    ///
    /// Two paired points: the relative order's condition constant and `strictBoundary` are both **1e-10**, its `NONZERO_TOLERANCE`; and **1e-9 changes behaviour**, yet
    /// `SafeLattice` still passes because the constant and the boundary scale together, so only pinning the constant itself catches the drift. It is a **calibration knob**, not an
    /// implementation detail.
    /// </remarks>
    [Fact]
    public void RelativeOrder_ShouldUseTheBaselineTolerance() {
        typeof(RelativeOrder).GetField("NonzeroTolerance",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null).Should().Be(1e-10,
                "the baseline's NONZERO_TOLERANCE is 1e-10 and the margin must not drift");
    }

    /// <summary>
    /// **装载模式二选一：预配载只建绝对顺序，全载只建相对顺序，建议打板两个都不建** / One model by mode.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 预配载 → `AbsoluteOrder` 非空、`RelativeOrder` 为 `null`。
    /// 2. 全载 → 反过来。
    /// 3. 建议打板 → **两个都 `null`**。写成"两个都建"会让预配载多出一族反转符号。
    ///
    /// Three paired points: predistribution gives a non-null `AbsoluteOrder` and a null `RelativeOrder`; full load the reverse; and weight recommendation gives **both null**, since
    /// building both would give predistribution a family of reversal symbols it should not have.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldBuildOneModelByMode() {
        var rig = new Rig(new List<Item> { MakeItem("I1", 1) }, new List<StowagePosition> { MakePosition("P1", 1UL, 1) });

        var pre = new ExpressEffectivenessAggregation(StowageMode.Predistribution, rig.Items, rig.Positions, rig.Stowage);
        pre.AbsoluteOrder.Should().NotBeNull();
        pre.RelativeOrder.Should().BeNull();

        var full = new ExpressEffectivenessAggregation(StowageMode.FullLoad, rig.Items, rig.Positions, rig.Stowage);
        full.AbsoluteOrder.Should().BeNull();
        full.RelativeOrder.Should().NotBeNull();

        var recommend = new ExpressEffectivenessAggregation(
            StowageMode.WeightRecommendation, rig.Items, rig.Positions, rig.Stowage);
        recommend.AbsoluteOrder.Should().BeNull();
        recommend.RelativeOrder.Should().BeNull();
    }

    /// <summary>
    /// **绝对系数：`(优先级 + 1) × (最大顺序 − 该舱位顺序 + 1)`** / The absolute coefficient has a plus one in both terms.
    /// </summary>
    /// <remarks>
    /// 四点成对，用优先级 0 与 2、装载顺序 1 与 3（最大 3）：
    ///
    /// 1. **优先级 0** 的系数是 `1 × (3 − order + 1)`——**不是 0**。少第一个加一会让优先级 0 的货物完全没有权重。
    /// 2. **最大顺序的舱位** 系数是 `(优先级 + 1) × 1`——**不是 0**。少第二个加一会让最后一个舱位没有权重。
    /// 3. 优先级 2 在顺序 1 上是 `3 × 3 = 9`。
    /// 4. **未登记的优先级回退 1**。
    ///
    /// Four paired points with priorities 0 and 2 and loading orders 1 and 3 (max 3): **priority 0** gets `1 × (3 − order + 1)`, **not 0** — dropping the first plus one leaves
    /// priority-0 cargo weightless; **the highest-order position** gets `(priority + 1) × 1`, **not 0** — dropping the second plus one leaves the last position weightless; priority 2 at
    /// order 1 is `3 × 3 = 9`; and **an unregistered priority falls back to 1**.
    /// </remarks>
    [Fact]
    public void AbsoluteOrder_ShouldAddOneInBothTerms() {
        Item low = MakeItem("L", 0);
        Item high = MakeItem("H", 2);
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P3", 3UL, 3) };
        var order = AbsoluteOrder.From(new List<Item> { low, high }, positions);

        order.Invoke(low.Priority, positions[0]).ToDouble().Should().BeApproximately(1.0 * 3.0, 1e-9,
            "priority 0 still weighs (0 + 1), and the first position weighs (3 - 1 + 1)");
        order.Invoke(low.Priority, positions[1]).ToDouble().Should().BeApproximately(1.0 * 1.0, 1e-9,
            "the highest-order position still weighs 1, not 0");
        order.Invoke(high.Priority, positions[0]).ToDouble().Should().BeApproximately(3.0 * 3.0, 1e-9);
        order.Invoke(new CargoPriority("X", new Fuookami.Ospf.Math.Algebra.Number.UInt64(9UL), CargoPriorityCategory.Normal), positions[0])
            .ToDouble().Should().BeApproximately(1.0, 1e-9, "an unregistered priority falls back to 1");
    }

    /// <summary>
    /// **相对顺序：有序对是全组合，符号矩阵按 (对, 对) 铺满，且可放置组合能构造** / Full combinations, a filled pair matrix, and placeable combinations that build.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 三个不同优先级 → 3 个有序货物对（全组合，不是相邻 2 对）。
    /// 2. 两个舱位 → 1 个有序舱位对。
    /// 3. **两个货物都能放到两个舱位时，注册成功**——`IfFunction` 的 `strictBoundary` 取 `NONZERO_TOLERANCE = 1e-10`
    /// 就能通过 `SafeLattice`（此前取 0.5 被拒，那是本仓的调用错误，不是框架限制）。
    /// 4. 符号矩阵是 **3 × 1**，每个位置都有符号。
    ///
    /// Four paired points: three distinct priorities give 3 ordered item pairs (all combinations, not 2 adjacent); two positions give 1 ordered position pair; **registration succeeds when
    /// both items fit both positions**, since `IfFunction`'s `strictBoundary` at `NONZERO_TOLERANCE = 1e-10` passes `SafeLattice` (the earlier 0.5 was this implementation's calling error, not a
    /// framework limit); and the matrix is **3 × 1** with a symbol in every slot.
    /// </remarks>
    [Fact]
    public void RelativeOrder_ShouldFillEverySlot() {
        var items = new List<Item> { MakeItem("A", 3), MakeItem("B", 2), MakeItem("C", 1) };
        // **三个舱位**，才能让同一货物对配上多个舱位对：位置 `ToString` 没覆写时这些符号会重名。
        // **Three positions**, so one item pair maps onto several position pairs; without a `ToString` override those symbols collide.
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2), MakePosition("P3", 3UL, 3),
        };
        var rig = new Rig(items, positions);
        var relative = new RelativeOrder(rig.Items, rig.Positions, rig.Stowage);

        relative.OrderedItems.Should().HaveCount(3, "three distinct priorities give 3+0 pairs");
        relative.OrderedPositions.Should().HaveCount(3, "three positions give three ascending pairs");

        using LinearMetaModel<Flt64> model = rig.Register();
        relative.Register(model).IsOk.Should().BeTrue(
            "the boundary is the baseline's NONZERO_TOLERANCE, which SafeLattice accepts");
        for (int p1 = 0; p1 < relative.OrderedItems.Count; ++p1) {
            for (int p2 = 0; p2 < relative.OrderedPositions.Count; ++p2) {
                relative.Reversal(p1, p2).Should().NotBeNull("every slot carries a symbol, zero or not");
            }
        }
        // **符号名必须互不相同**：`Item` 或 `StowagePosition` 少了 `ToString` 覆写就会全部退化成类型全名，
        // 注册时报 `Repeated "…"`（实测）。这一条同时钉住两个覆写。
        // **Every symbol name must differ**: without a `ToString` override on either `Item` or `StowagePosition` they all degrade to the full type name and registration fails with
        // `Repeated "…"` (measured). This single assertion pins both overrides.
        var names = new List<string>();
        for (int p1 = 0; p1 < relative.OrderedItems.Count; ++p1) {
            for (int p2 = 0; p2 < relative.OrderedPositions.Count; ++p2) {
                names.Add(relative.Reversal(p1, p2)!.Name);
            }
        }
        names.Should().OnlyHaveUniqueItems("symbol names carry the item and both positions");
        names.Should().OnlyContain(name => !name.Contains("Fuookami.Ospf", StringComparison.Ordinal),
            "a name must carry the item and position names, never a type dump");
    }

    /// <summary>
    /// **优先级目标是最大化、名带空格、每个组合都进目标** / The priority objective is a maximisation named with a space, covering every combination.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 类别是 `Maximum`，名字是 `"item priority"`（带空格），**不是**管线名 `item_priority_limit`。
    /// 2. **每个（货物, 舱位）组合都贡献单项式**：2 货物 × 2 舱位 = 4 个变量。
    /// 3. 系数取 `parameter.priority`：给 5 时单项式系数是给 1 时的 5 倍。
    ///
    /// Three paired points: the category is `Maximum` and the name is `"item priority"` (with a space), **not** the pipeline name `item_priority_limit`; **every (item, position)
    /// combination contributes a monomial**, so 2 items × 2 positions gives 4 variables; and the coefficient comes from `parameter.priority`, so 5 gives five times the coefficient of 1.
    /// </remarks>
    [Fact]
    public void ItemPriorityLimit_ShouldMaximiseEveryCombination() {
        var items = new List<Item> { MakeItem("A", 2), MakeItem("B", 1) };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var rig = new Rig(items, positions);
        using LinearMetaModel<Flt64> model = rig.Register();
        var absolute = AbsoluteOrder.From(rig.Items, rig.Positions);

        new ItemPriorityLimit(rig.Items, rig.Positions, absolute, rig.Stowage, _ => new Flt64(5.0))
            .Invoke(model).IsOk.Should().BeTrue();

        MetaSubObject<Flt64> objective = model.MetaSubObjects[^1];
        objective.Name.Should().Be("item priority");
        objective.Category.Should().Be(ObjectCategory.Maximum);
        objective.Polynomial.Monomials.Should().HaveCount(4, "two items times two positions");
        objective.Polynomial.Monomials.Should().NotContain(monomial => monomial.Coefficient.ToDouble() == 0.0,
            "the coefficient is five, so nothing collapses to zero");

        // 系数 1 时是 5 的 1/5。**必须换一个 `Rig`**：`Stowage.Register` 对同一实例不可重复调用。
        // With coefficient 1 the values are a fifth of the 5 case. **A fresh `Rig` is required**: `Stowage.Register` cannot run twice on one instance.
        var oneRig = new Rig(new List<Item> { MakeItem("A", 2), MakeItem("B", 1) },
            new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) });
        using LinearMetaModel<Flt64> oneModel = oneRig.Register();
        var oneAbsolute = AbsoluteOrder.From(oneRig.Items, oneRig.Positions);
        new ItemPriorityLimit(oneRig.Items, oneRig.Positions, oneAbsolute, oneRig.Stowage, _ => Flt64.One)
            .Invoke(oneModel).IsOk.Should().BeTrue();
        oneModel.MetaSubObjects[^1].Polynomial.Monomials[0].Coefficient.ToDouble().Should()
            .BeApproximately(objective.Polynomial.Monomials[0].Coefficient.ToDouble() / 5.0, 1e-9);
    }

    /// <summary>
    /// **必须发货：等式、右端 1、覆盖全部舱位** / Must-ship is an equality with right-hand side 1 over all positions.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 一个必须发货的货物 → **恰好 1 条**约束。
    /// 2. 关系是 **`EQ`**，右端是 **1**。
    /// 3. 左端覆盖**全部舱位**的变量（这里是 2 个）。
    ///
    /// Three paired points: one must-ship item gives **exactly one** constraint; the relation is **`EQ`** with right-hand side **1**; and the left side covers **every position's**
    /// variable (two here).
    /// </remarks>
    [Fact]
    public void MustShipLimit_ShouldAddOneEqualityPerItem() {
        var items = new List<Item> { MakeItem("A", 9), MakeItem("B", 1) };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var rig = new Rig(items, positions);
        using LinearMetaModel<Flt64> model = rig.Register();
        int before = model.Constraints.Count;

        new MustShipLimit(rig.Items, rig.Positions, rig.Stowage, new[] { 0 }).Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(before + 1);
        Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64> row =
            ((LinearInequalityConstraint<Flt64>)model.Constraints[^1]).Inequality;
        row.Comparison.Should().Be(Fuookami.Ospf.Math.Symbol.Inequality.Comparison.EQ);
        row.Rhs.Constant.ToDouble().Should().Be(1.0);
        row.Lhs.Monomials.Should().HaveCount(2, "both positions enter the sum");
    }

    /// <summary>
    /// **反转目标：空集合不加目标** / The reversal objective adds nothing for an empty set.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. **单个货物没有有序对** → `ItemPriorityReverseLimit` 直接返回成功且**不加目标**（`if (monomials.isEmpty) return ok`）。
    /// 2. 该路径**不需要 `IfFunction`**，因为循环体一次都不执行——所以它能落地，而可放置组合那条路径不能。
    ///
    /// Two paired points: **one item has no ordered pair**, so `ItemPriorityReverseLimit` returns success and **adds no objective** (its early return); and this path **does
    /// not need `IfFunction`**, since the loop body never runs — which is why it lands while the placeable-combination path does not.
    /// </remarks>
    [Fact]
    public void ItemPriorityReverseLimit_ShouldSkipEmptySets() {
        var single = new List<Item> { MakeItem("A", 1) };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var singleRig = new Rig(single, positions);
        using LinearMetaModel<Flt64> singleModel = singleRig.Register();
        var singleRelative = new RelativeOrder(singleRig.Items, singleRig.Positions, singleRig.Stowage);
        singleRelative.Register(singleModel).IsOk.Should().BeTrue("no ordered pairs means nothing to register");
        int objectivesBefore = singleModel.MetaSubObjects.Count;
        new ItemPriorityReverseLimit(singleRelative).Invoke(singleModel).IsOk.Should().BeTrue();
        singleModel.MetaSubObjects.Count.Should().Be(objectivesBefore, "an empty monomial set adds no objective");
    }

    /// <summary>
    /// **生成器：三条条件管线，必须发货只判"非预配载"** / The generator has three conditional pipelines, with must-ship testing only "not predistribution".
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. **预配载**：绝对顺序存在 → 1 条（优先级）。`MustShipIndices` 非空也**不加**必须发货（条件排除预配载）。
    /// 2. **全载**：相对顺序存在 → 2 条（反转 + 必须发货）。
    /// 3. **建议打板**：两个模型都空，但必须发货**照加**（条件只判非预配载）→ 1 条。
    /// 4. 建议打板下必须发货下标为空 → 0 条。
    ///
    /// Four paired points: **predistribution** has the absolute order so one pipeline (priority), and a non-empty must-ship list **does not** add must-ship because the condition
    /// excludes predistribution; **full load** has the relative order so two pipelines (reversal plus must-ship); **weight recommendation** has neither model but still adds must-ship
    /// (the condition only excludes predistribution), giving one; and weight recommendation with an empty must-ship list gives zero.
    /// </remarks>
    [Fact]
    public void PipelineListGenerator_ShouldApplyThreeConditions() {
        var items = new List<Item> { MakeItem("A", 9), MakeItem("B", 1) };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var rig = new Rig(items, positions);
        int[] mustShip = { 0 };

        static int Count(ExpressEffectivenessAggregation aggregation, StowageMode mode) {
            Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> generated =
                new ExpressGenerator(aggregation).Invoke(mode, new Parameter(Priority: new Flt64(1.0)));
            generated.IsOk.Should().BeTrue();
            return ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)generated).Value!.Count;
        }

        Count(new ExpressEffectivenessAggregation(StowageMode.Predistribution, rig.Items, rig.Positions, rig.Stowage, mustShip),
            StowageMode.Predistribution).Should().Be(1, "priority only; must-ship excludes predistribution");
        // **只数管线，不执行**：全载的反转管线在可放置组合上会明确失败（见 `RelativeOrder` 的说明）。
        // **Count only, do not execute**: the full-load reversal pipeline fails explicitly on placeable combinations (see `RelativeOrder`).
        Count(new ExpressEffectivenessAggregation(StowageMode.FullLoad, rig.Items, rig.Positions, rig.Stowage, mustShip),
            StowageMode.FullLoad).Should().Be(2, "reversal plus must-ship");
        Count(new ExpressEffectivenessAggregation(StowageMode.WeightRecommendation, rig.Items, rig.Positions, rig.Stowage, mustShip),
            StowageMode.WeightRecommendation).Should().Be(1, "must-ship still applies");
        Count(new ExpressEffectivenessAggregation(StowageMode.WeightRecommendation, rig.Items, rig.Positions, rig.Stowage),
            StowageMode.WeightRecommendation).Should().Be(0);
    }

    /// <summary>
    /// **初始化器：优先级 >= 8 的货物下标被选中** / The initializer picks the indices of items with priority 8 or more.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 优先级 7 与 8 相邻 → **只有 8 入选**，阈值是 `>= 8`。
    /// 2. 下标是**请求里的位置**，不是货物名。
    /// 3. 配载聚合为 `null` → 明确失败。
    ///
    /// Three paired points: priorities 7 and 8 straddle the threshold, so **only 8 is selected** under `>= 8`; the indices are the **request positions**, not names; and a null stowage
    /// aggregation fails explicitly.
    /// </remarks>
    [Fact]
    public void AggregationInitializer_ShouldPickPrioritiesAtLeastEight() {
        var request = new RequestDTO(
            "REQ",
            new List<CargoInput> {
                new("LOW", 10.0, 7, "PEK", "SHA"),
                new("HIGH", 10.0, 8, "PEK", "SHA"),
                new("MID", 10.0, 5, "PEK", "SHA"),
            },
            new List<PositionInput> { new("P1", 1000.0, 100.0, 0.0) });

        Result<ExpressEffectivenessAggregation, ErrorCode, Error<ErrorCode>> missing =
            Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Service.AggregationInitializer.Initialize(null, request, StowageMode.FullLoad);
        missing.IsOk.Should().BeFalse("the stowage aggregation is uninitialised");
        (missing as Failed<ExpressEffectivenessAggregation, ErrorCode, Error<ErrorCode>>)!.Error.Message
            .Should().Contain("配载聚合未初始化");

        // **调用被测函数本身**，不要自己重算期望值——上一版正是后者，让"阈值改成 7"的变异存活。
        // **Call the code under test**, rather than recomputing the expectation — the previous version did the latter and a threshold-to-7 mutant survived.
        Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.ExpressEffectiveness.Service.AggregationInitializer
            .MustShipIndices(request).Should().Equal(new[] { 1 }, "priority 8 qualifies and 7 does not");
    }
}
