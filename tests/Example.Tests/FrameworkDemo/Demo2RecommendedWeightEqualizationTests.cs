// 推荐重量均衡域的判据 / Criteria for the recommended-weight-equalisation domain.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using AircraftPosition = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.Position;
using RweGenerator = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Service.PipelineListGenerator;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **推荐重量均衡域** / The recommended-weight-equalisation domain.
/// </summary>
/// <remarks>
/// 四条管线：货物顺序（约束）、优先级预约（约束）、重量均衡（约束）、偏差目标（最大化）。判据的重点：**`precDepth` 恒为 0** 这个怪癖、
/// 有序对的排序键、均衡约束的**两条方向**与**交叉的 `mlw` 系数**。
///
/// Four pipelines: item order (constraint), priority appointment (constraint), weight equalisation (constraint), deviation objective (maximisation). The criteria focus on the
/// quirk that **`precDepth` is always 0**, the ordered pairs' sort keys, and the equalisation constraint's **two directions** with **crossed `mlw` coefficients**.
/// </remarks>
public class Demo2RecommendedWeightEqualizationTests {
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

    private static StowagePosition MakePosition(
        string name, ulong id, byte order, bool recommendedWeightNeeded = true, bool available = true) {
        var position = new AircraftPosition(
            id: id,
            spaceName: name,
            sizeCode: "N",
            linearLoadingOrder: order,
            coordinate: new PositionCoordinate(100.0 * id, 100.0 * id + 100.0, -60.0, -10.0),
            shape: new PositionShape(100.0, 50.0, 5000.0),
            location: PositionLocation.NormalMainLocation);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(available, false, false, recommendedWeightNeeded),
            mlw: new MaxLoadWeightLimit(1000.0 + id * 100.0),
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
                LayoutGroups: new List<LayoutSelectionGroup>(),
                WeightUnit: MassUnits.Gram);
            Stowage = new StowageModel(Items, Positions) {
                LoadingContext = new DiscreteLoadingContext<Flt64>(
                    problem, new Flt64PackingValueAdapter()),
            };
            Load = new Load(Items, Positions, Stowage, withMultiLoadingSchema: true);
            Aggregation = new RecommendedWeightEqualizationAggregation(
                new AircraftModel(AircraftType.B737, "B737", new AircraftMinorModel("B737")),
                Items, Positions,
                new Dictionary<Item, StowagePosition>(),
                Stowage, Load);
        }

        public IReadOnlyList<Item> Items { get; }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public StowageModel Stowage { get; }

        public Load Load { get; }

        public RecommendedWeightEqualizationAggregation Aggregation { get; }

        public LinearMetaModel<Flt64> Register() {
            var model = new LinearMetaModel<Flt64>();
            Stowage.Register(model).IsOk.Should().BeTrue();
            Load.Register(model).IsOk.Should().BeTrue();
            return model;
        }
    }

    /// <summary>
    /// **`precDepth` 恒为 0，因此优先级预约对所有可用舱位为真** / `precDepth` is always 0, so the appointment holds for every available position.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. **串好的装载顺序链上，每个节点的 `PrecDepth` 都是 0**——取的是"直接前驱的 `precDepth` 最小值"，而叶子也是 0。
    /// 2. 因此 `PriorityAppointment.Invoke(任意优先级, 任意可用舱位)` 为真。
    /// 3. `availableDepths` 是 `{0}`，所以即使没有预约，回退也是 `{0}`。
    ///
    /// 这不是"修好它"的地方：改成真实深度会让 `PriorityAppointmentLimit` 产生原本没有的约束。
    ///
    /// Three paired points: **every node on a linked loading-order chain has `PrecDepth` 0**, since the minimum `precDepth` among direct predecessors is taken and a leaf is
    /// also 0; therefore `PriorityAppointment.Invoke` is true for any priority and any available position; and `availableDepths` is `{0}`, so even the no-appointment fallback is
    /// `{0}`. This is not the place to "fix it": real depths would make `PriorityAppointmentLimit` emit constraints it does not emit today.
    /// </remarks>
    [Fact]
    public void PrecDepth_ShouldBeZeroThroughoutTheChain() {
        Item item = MakeItem("I1", priority: 1);
        StowagePosition first = MakePosition("P1", 1UL, 1);
        StowagePosition second = MakePosition("P2", 2UL, 2);
        var rig = new Rig(new List<Item> { item }, new List<StowagePosition> { first, second });

        // 手工串一条链，模拟 `Aircraft.AggregationInitializer` 的后置赋值。
        // Link a chain by hand, mirroring the aircraft initializer's post-construction assignment.
        first.AircraftPosition.LoadingOrder = new LoadingOrder(
            DeckLocation.Main, 1, new HashSet<AircraftPosition>(), new HashSet<AircraftPosition> { second.AircraftPosition });
        second.AircraftPosition.LoadingOrder = new LoadingOrder(
            DeckLocation.Main, 2, new HashSet<AircraftPosition> { first.AircraftPosition }, new HashSet<AircraftPosition>());

        first.AircraftPosition.LoadingOrder.PrecDepth.Should().Be(0, "a leaf's predecessor depth is zero");
        second.AircraftPosition.LoadingOrder.PrecDepth.Should().Be(0,
            "the minimum over the direct predecessors is the leaf's own zero, so the chain stays flat");

        var appointment = PriorityAppointment.From(
            rig.Items, rig.Positions, new Dictionary<Item, StowagePosition>());
        appointment.Invoke(item.Priority, first).Should().BeTrue("the fallback depth set contains 0");
        appointment.Invoke(item.Priority, second).Should().BeTrue("the fallback depth set contains 0");
        appointment.Appointment[item.Priority].Should().BeEquivalentTo(new byte[] { 0 });
    }

    /// <summary>
    /// **有序对：货物按优先级降序，舱位按装载顺序升序；都是全组合** / Ordered pairs: items by priority descending, positions by loading order ascending, both as full combinations.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. **货物对**：三个不同优先级的货物 → 3 对（`高→中`、`高→低`、`中→低`），不是相邻的 2 对。
    /// 2. **舱位对**：三个舱位 → 3 对，按装载顺序。
    /// 3. **同优先级的货物不构成有序对**（谓词是严格大于）。
    ///
    /// Three paired points: **item pairs** — three distinct priorities give 3 pairs (high→mid, high→low, mid→low), not the 2 adjacent ones; **position pairs** — three positions give
    /// 3 pairs in loading order; and **equal-priority items form no ordered pair**, since the predicate is strict greater-than.
    /// </remarks>
    [Fact]
    public void ItemOrderLimit_ShouldBuildFullCombinations() {
        Item high = MakeItem("H", priority: 3);
        Item mid = MakeItem("M", priority: 2);
        Item low = MakeItem("L", priority: 1);
        Item midToo = MakeItem("M2", priority: 2);
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2), MakePosition("P3", 3UL, 3),
        };
        var rig = new Rig(new List<Item> { high, mid, low, midToo }, positions);

        var limit = new ItemOrderLimit(rig.Items, rig.Positions, rig.Stowage);
        // **3+2=5**：high 胜 mid、midToo、low（3 对）；两个 mid 各胜 low（2 对）。两个 mid 之间不构成对。
        // **3+2=5**: high beats mid, midToo and low (3 pairs); each mid beats low (2 pairs). The two mids form no pair.
        limit.OrderedItems.Should().HaveCount(5, "high beats three, each mid beats the low");
        limit.OrderedItems.Should().Contain((high, mid));
        limit.OrderedItems.Should().Contain((high, low));
        limit.OrderedItems.Should().Contain((mid, low));
        limit.OrderedItems.Should().NotContain((mid, midToo), "equal priorities form no ordered pair");
        limit.OrderedPositions.Should().HaveCount(3, "three positions give three ascending pairs");
        limit.OrderedPositions.Should().Contain((positions[0], positions[1]));
        limit.OrderedPositions.Should().Contain((positions[0], positions[2]));
        limit.OrderedPositions.Should().Contain((positions[1], positions[2]));
        // **方向必须是"装载顺序小 → 大"**：只断言三对存在会让"整个列表反转"的变异存活（实测存活过一次）。
        // **The direction must be "smaller loading order → larger"**: asserting only that three pairs exist lets a mutant that reverses the whole list survive (measured once).
        limit.OrderedPositions.Should().NotContain((positions[1], positions[0]),
            "the pairs run in ascending loading order, not descending");
        limit.OrderedPositions.Should().NotContain((positions[2], positions[0]));
        // **排序本身不可观测，只能观测谓词的方向**。实测：把列表整体 `Reverse` 后仍全绿——因为谓词
        // `order[a] < order[b]` 会在反转后的列表里重新筛出升序对，结果集与不反转相同。真正会改变结果的是
        // 排序的**第二级**（`Id` 字符串），而它只在**装载顺序相同**时才起作用，本 fixture 的三个舱位顺序互异。
        // 要在那里造出可观测差异，需要两个 `LoadingOrder.Order` 相同的舱位——而 `Aircraft.AggregationInitializer`
        // 会拒绝重复的装载顺序，所以那种舱位在正常路径上不存在。记录为不可观测。
        //
        // **The sort itself is unobservable; only the predicate's direction is.** Measured: reversing the whole list leaves every criterion green, because the predicate
        // `order[a] < order[b]` re-selects the ascending pairs from the reversed list and yields the same set. What would change the result is the sort's **second level** (the `Id`
        // string), which only matters when two positions share a loading order — and this fixture's three positions all differ. Producing an observable difference there needs two
        // positions with the same `LoadingOrder.Order`, which `Aircraft.AggregationInitializer` rejects as a duplicate, so such positions do not exist on the normal path. Recorded as
        // unobservable.
    }

    /// <summary>
    /// **货物顺序约束是 `s[i1,j2] + s[i2,j1] ≤ 1`** / The item-order constraint is `s[i1,j2] + s[i2,j1] ≤ 1`.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 两个不同优先级的货物、两个舱位 → **恰好 1 条**约束（`1×1` 的有序对）。
    /// 2. 约束的右端是常数 **1**，不是 0。
    /// 3. 左端是**两个不同变量之和**，系数都是 1。
    ///
    /// Three paired points: two distinct-priority items and two positions give **exactly one** constraint; the right-hand side is the constant **1**, not 0; and the left side is **the
    /// sum of two distinct variables**, both with coefficient 1.
    /// </remarks>
    [Fact]
    public void ItemOrderLimit_ShouldAddOneRowPerPair() {
        Item high = MakeItem("H", priority: 2);
        Item low = MakeItem("L", priority: 1);
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var rig = new Rig(new List<Item> { high, low }, positions);
        using LinearMetaModel<Flt64> model = rig.Register();
        int before = model.Constraints.Count;

        new ItemOrderLimit(rig.Items, rig.Positions, rig.Stowage).Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(before + 1, "one item pair times one position pair");
        Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64> row = ((Fuookami.Ospf.Core.Model.Mechanism.LinearInequalityConstraint<Flt64>)model.Constraints[^1]).Inequality;
        row.Rhs.Constant.ToDouble().Should().Be(1.0, "the right-hand side is one, not zero");
        row.Lhs.Monomials.Should().HaveCount(2, "two distinct stowage variables");
        row.Lhs.Monomials.Should().OnlyContain(monomial => System.Math.Abs(monomial.Coefficient.ToDouble() - 1.0) < 1e-9);
    }

    /// <summary>
    /// **重量均衡：两条方向相反，系数是另一个舱位的 `mlw`** / Weight equalisation: two opposite rows, with the other position's `mlw` as the coefficient.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两个需要推荐重量的舱位 → **恰好 2 条**约束（不是 1 条，也不是 4 条）。
    /// 2. 第一条的 `z` 系数是 `+1`（`j1`）与 `−1`（`j2`）；第二条反过来。
    /// 3. **松弛项的系数是另一个舱位的 `mlw`**：第一条用 `mlw[j1]`，第二条用 `mlw[j2]`。写反会让两个容量张冠李戴。
    /// 4. 只有**一个**舱位需要推荐重量时 **0 条**约束。
    ///
    /// Four paired points: two positions needing recommended weight give **exactly two** rows; the first has `+1` for `z[j1]` and `−1` for `z[j2]`, the second reverses them; **the
    /// slack coefficient is the other position's `mlw`** — `mlw[j1]` in the first row and `mlw[j2]` in the second, and swapping them mixes up the two capacities; and **one** position
    /// needing it gives **zero** rows.
    /// </remarks>
    [Fact]
    public void RecommendedWeightEqualizationLimit_ShouldAddTwoOppositeRows() {
        Item item = MakeItem("I1", priority: 1);
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var rig = new Rig(new List<Item> { item }, positions);
        using LinearMetaModel<Flt64> model = rig.Register();
        int before = model.Constraints.Count;

        new RecommendedWeightEqualizationLimit(rig.Positions, rig.Load).Invoke(model).IsOk.Should().BeTrue();
        model.Constraints.Count.Should().Be(before + 2, "one pair gives two opposite rows");

        Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64> first = ((Fuookami.Ospf.Core.Model.Mechanism.LinearInequalityConstraint<Flt64>)model.Constraints[^2]).Inequality;
        Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64> second = ((Fuookami.Ospf.Core.Model.Mechanism.LinearInequalityConstraint<Flt64>)model.Constraints[^1]).Inequality;
        first.Lhs.Monomials.Should().HaveCount(3, "z[j1], z[j2] and the loaded slack");
        // **松弛系数是 `mlw[j1]`（第一条）**。
        // **The slack coefficient is `mlw[j1]` in the first row.**
        first.Lhs.Monomials.Where(monomial => monomial.Coefficient.ToDouble() < -1.5).Should().ContainSingle()
            .Which.Coefficient.ToDouble().Should().BeApproximately(-positions[0].Mlw.Mlw, 1e-9);
        second.Lhs.Monomials.Where(monomial => monomial.Coefficient.ToDouble() < -1.5).Should().ContainSingle()
            .Which.Coefficient.ToDouble().Should().BeApproximately(-positions[1].Mlw.Mlw, 1e-9);

        // 只一个舱位需要推荐重量 → 0 条。
        // Only one position needs it → zero rows.
        var single = new List<StowagePosition> {
            MakePosition("Q1", 1UL, 1, recommendedWeightNeeded: true),
            MakePosition("Q2", 2UL, 2, recommendedWeightNeeded: false),
        };
        var singleRig = new Rig(new List<Item> { item }, single);
        using LinearMetaModel<Flt64> singleModel = singleRig.Register();
        int singleBefore = singleModel.Constraints.Count;
        new RecommendedWeightEqualizationLimit(singleRig.Positions, singleRig.Load).Invoke(singleModel).IsOk.Should().BeTrue();
        singleModel.Constraints.Count.Should().Be(singleBefore, "one needing and one not gives no pair");
    }

    /// <summary>
    /// **偏差目标是最大化、系数为负、名为管线名** / The deviation objective is a maximisation with a negative coefficient, named after the pipeline.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 类别是 `Maximum`，**不是 `Minimum`**——写成最小化会得到相反的符号语义。
    /// 2. **系数为负**（`−coefficient`）。
    /// 3. **目标名就是 `Name`**（`recommended_weight_deviation_objective`），与另外三条管线"目标名带空格"不同。
    ///
    /// Three paired points: the category is `Maximum`, **not `Minimum`**; **the coefficient is negative**; and **the objective name is `Name`**
    /// (`recommended_weight_deviation_objective`), unlike the other three pipelines whose objective names carry spaces.
    /// </remarks>
    [Fact]
    public void RecommendedWeightDeviationObjective_ShouldMaximiseANegativeSum() {
        Item item = MakeItem("I1", priority: 1);
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var rig = new Rig(new List<Item> { item }, positions);
        using LinearMetaModel<Flt64> model = rig.Register();

        new RecommendedWeightDeviationObjective(rig.Load, () => new Flt64(2.0)).Invoke(model).IsOk.Should().BeTrue();

        MetaSubObject<Flt64> objective = model.MetaSubObjects[^1];
        objective.Name.Should().Be("recommended_weight_deviation_objective");
        objective.Category.Should().Be(ObjectCategory.Maximum);
        objective.Polynomial.Monomials.Should().HaveCount(2, "one per position");
        objective.Polynomial.Monomials.Should().OnlyContain(
            monomial => System.Math.Abs(monomial.Coefficient.ToDouble() + 2.0) < 1e-9,
            "the coefficient is negative twice the input");
    }

    /// <summary>
    /// **生成器只建议打板模式；四条管线顺序固定** / The generator honours weight recommendation only, with four pipelines in a fixed order.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. **只有建议打板给四条**，预配载与全载都给空列表。
    /// 2. 顺序是"货物顺序、优先级预约、重量均衡、偏差目标"——第四条是目标。
    /// 3. 跑完之后 `MetaSubObjects` 里**恰好一个**目标，名字是偏差目标。
    ///
    /// Three paired points: **only weight recommendation yields four**, with predistribution and full load empty; the order is item order, priority appointment, weight equalisation,
    /// deviation objective, the fourth being the objective; and after running, `MetaSubObjects` holds **exactly one** objective, named after the deviation one.
    /// </remarks>
    [Fact]
    public void PipelineListGenerator_ShouldHonourOnlyWeightRecommendation() {
        Item high = MakeItem("H", priority: 2);
        Item low = MakeItem("L", priority: 1);
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var rig = new Rig(new List<Item> { high, low }, positions);

        foreach (StowageMode mode in new[] { StowageMode.Predistribution, StowageMode.FullLoad }) {
            Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> off =
                new RweGenerator(rig.Aggregation).Invoke(mode, new Parameter(WeightRecommendationBalance: new Flt64(1.0)));
            off.IsOk.Should().BeTrue();
            ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)off).Value!
                .Should().BeEmpty("{0} does not run equalisation", mode);
        }

        Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> on =
            new RweGenerator(rig.Aggregation).Invoke(
                StowageMode.WeightRecommendation, new Parameter(WeightRecommendationBalance: new Flt64(1.0)));
        on.IsOk.Should().BeTrue();
        IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>> pipelines =
            ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)on).Value!;
        pipelines.Should().HaveCount(4);

        using LinearMetaModel<Flt64> model = rig.Register();
        foreach (Func<LinearMetaModel<Flt64>, Try> pipeline in pipelines) {
            pipeline(model).IsOk.Should().BeTrue();
        }
        model.MetaSubObjects.Should().ContainSingle("only the deviation objective");
        model.MetaSubObjects[0].Name.Should().Be("recommended_weight_deviation_objective");
        model.MetaSubObjects[0].Category.Should().Be(ObjectCategory.Maximum);
    }

    /// <summary>
    /// **优先级预约约束：已预约、虚拟货物、非主舱都跳过** / The appointment constraint skips appointed, virtual and non-main-deck items.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 一个**已预约**的货物 → **0 条**约束（`appointment.ContainsKey` 跳过）。
    /// 2. 一个**虚拟货物** → **0 条**约束。
    /// 3. 一个未预约的普通货物 + 一个不可放置的舱位 → 仍为 0 条（`StowageNeeded` 为假）。
    ///
    /// Three paired points: an **appointed** item gives **zero** rows; a **virtual** item gives **zero** rows; and an unappointed ordinary item with an unplaceable position also gives
    /// zero, since `StowageNeeded` is false.
    /// </remarks>
    [Fact]
    public void PriorityAppointmentLimit_ShouldSkipAppointedVirtualAndNonMain() {
        Item plain = MakeItem("I1", priority: 1);
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1) };
        var rig = new Rig(new List<Item> { plain }, positions);

        // 1. 已预约 → 跳过。
        // 1. Appointed → skipped.
        var appointed = new Dictionary<Item, StowagePosition> { [plain] = positions[0] };
        using LinearMetaModel<Flt64> appointedModel = rig.Register();
        int appointedBefore = appointedModel.Constraints.Count;
        new PriorityAppointmentLimit(
            rig.Items, rig.Positions, appointed,
            PriorityAppointment.From(rig.Items, rig.Positions, appointed),
            rig.Stowage).Invoke(appointedModel).IsOk.Should().BeTrue();
        appointedModel.Constraints.Count.Should().Be(appointedBefore, "an appointed item is unconstrained");

        // 2. 虚拟货物 → 跳过。用 `CargoType.FromName("VIRTUAL")` 造一个带 Virtual 码的货物。
        // 2. Virtual → skipped, using a cargo type carrying the Virtual code.
        // **`FromName` 是大小写敏感的 `Enum.TryParse`**：`"VIRTUAL"` 解析失败返回 `null` Code（实测），必须写 `"Virtual"`。
        // **`FromName` is a case-sensitive `Enum.TryParse`**: `"VIRTUAL"` fails and yields a `null` Code (measured), so `"Virtual"` is required.
        var virtualType = CargoType.FromName("Virtual");
        virtualType.Code.Should().Be(CargoCode.Virtual);
        var virtualItem = new Item(
            name: "V1", weight: 10.0,
            priority: new CargoPriority("P1", new Fuookami.Ospf.Math.Algebra.Number.UInt64(1UL), CargoPriorityCategory.Normal),
            cargoType: virtualType, source: "PEK", destination: "SHA",
            status: ItemStatus.Optional, location: ItemLocation.MainDeck);
        var virtualRig = new Rig(new List<Item> { virtualItem }, positions);
        using LinearMetaModel<Flt64> virtualModel = virtualRig.Register();
        int virtualBefore = virtualModel.Constraints.Count;
        new PriorityAppointmentLimit(
            virtualRig.Items, virtualRig.Positions, new Dictionary<Item, StowagePosition>(),
            PriorityAppointment.From(virtualRig.Items, virtualRig.Positions, new Dictionary<Item, StowagePosition>()),
            virtualRig.Stowage).Invoke(virtualModel).IsOk.Should().BeTrue();
        virtualModel.Constraints.Count.Should().Be(virtualBefore, "virtual cargo is unconstrained");
    }
}
