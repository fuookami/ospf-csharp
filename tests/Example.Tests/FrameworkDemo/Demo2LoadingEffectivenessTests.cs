// 装载效能域（基础部分）的判据 / Criteria for the foundation of the loading-effectiveness domain.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **装载效能域基础** / The loading-effectiveness foundation.
/// </summary>
/// <remarks>
/// 只覆盖不依赖 `IfFunction` 的部分：拖车模型、相邻与有序舱位对、优先级顺序限制、同来源早装限制。
///
/// Covers only the `IfFunction`-free part: the trailer model, adjacent and ordered position pairs, the priority-order constraint and the same-source early-loading constraint.
/// </remarks>
public class Demo2LoadingEffectivenessTests {
    private static Item MakeItem(string name, int priority, string source, string destination, double weight = 100.0) =>
        new(
            name: name,
            weight: weight,
            priority: new CargoPriority("P" + priority, new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)priority), CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: source,
            destination: destination,
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);

    private static StowagePosition MakePosition(string name, ulong id, byte order) {
        var position = new Position(
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

    private static StowageModel MakeStowage(
        IReadOnlyList<Item> items, IReadOnlyList<StowagePosition> positions) {
        var allowed = new HashSet<AssignmentKey>();
        foreach (Item item in items) {
            foreach (StowagePosition position in positions) {
                if (StowageModel.StowageNeeded(item, position)) {
                    allowed.Add(new AssignmentKey(item.Id, position.Name));
                }
            }
        }
        var problem = new DiscreteLoadingProblem<Flt64>(
            Items: items.Select(item => new LoadingItem<Flt64>(
                item.Id, new Quantity<Flt64>(new Flt64(item.Weight), MassUnits.Gram))).ToList(),
            Positions: positions.Select(position => new LoadingPosition<Flt64>(
                position.Name, position.Mla,
                new Quantity<Flt64>(new Flt64(position.Mlw.Mlw), MassUnits.Gram))).ToList(),
            AllowedAssignments: allowed,
            Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
            LayoutGroups: new List<LayoutSelectionGroup>(),
            WeightUnit: MassUnits.Gram);
        return new StowageModel(items, positions) {
            LoadingContext = new DiscreteLoadingContext<Flt64>(problem, new Flt64PackingValueAdapter()),
        };
    }

    /// <summary>
    /// **`Trailer.ToString` 只返回名字** / `Trailer.ToString` returns the name alone.
    /// </summary>
    /// <remarks>
    /// 两点成对：名字是 `"T1"` 时 `ToString` 就是 `"T1"`；而**默认记录输出会带上类型名与全部成员**，长度远超名字。
    /// 这个 `ToString` 直接进约束名，泄漏会让名字不可读且不可比对。
    ///
    /// Two paired points: a trailer named `"T1"` stringifies to `"T1"`; and **the default record output carries the type name and every member**, far longer than the name. This
    /// `ToString` goes straight into constraint names, and leaking would make them unreadable and incomparable.
    /// </remarks>
    [Fact]
    public void Trailer_ShouldStringifyToItsName() {
        var trailer = new Trailer(
            TrailerType.Transit, 3, "T1", new List<Item> { MakeItem("A", 1, "PEK", "SHA") });
        trailer.ToString().Should().Be("T1");
        trailer.ToString().Length.Should().BeLessThan(20, "the default record output is a long type dump");
        trailer.Order.Should().Be(3);
        trailer.Type.Should().Be(TrailerType.Transit);
    }

    /// <summary>
    /// **相邻舱位对按装载顺序，且是 `n − 1` 个** / Adjacent position pairs follow loading order and number `n − 1`.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 三个舱位 → **2 个**相邻对（不是 3 个，也不是全组合的 3 个）。
    /// 2. **有序对是全组合**：三个舱位 → **3 个**。
    /// 3. 相邻对的方向按**装载顺序升序**，与输入列表顺序无关（这里故意乱序传入）。
    ///
    /// Three paired points: three positions give **2** adjacent pairs (not 3, and not the 3 of a full combination); **ordered pairs are full combinations**, so three positions give
    /// **3**; and the adjacency direction follows **ascending loading order**, independent of the input order, which is deliberately shuffled here.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldPairPositionsByLoadingOrder() {
        var positions = new List<StowagePosition> {
            MakePosition("P3", 3UL, 3), MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2),
        };
        var items = new List<Item> { MakeItem("A", 1, "PEK", "SHA") };
        var aggregation = new LoadingEffectivenessAggregation(
            StowageMode.FullLoad, items, positions, MakeStowage(items, positions));

        aggregation.AdjacentPositions.Should().HaveCount(2, "three positions give two adjacencies");
        aggregation.AdjacentPositions[0].First.Name.Should().Be("P1", "the input order is shuffled, loading order decides");
        aggregation.AdjacentPositions[0].Second.Name.Should().Be("P2");
        aggregation.AdjacentPositions[1].First.Name.Should().Be("P2");
        aggregation.AdjacentPositions[1].Second.Name.Should().Be("P3");
        aggregation.OrderedPositions.Should().HaveCount(3, "ordered pairs are full combinations");
        aggregation.EarlyEnd.Should().Be(1, "(3 - 1) / 2 = 1 by integer division");
    }

    /// <summary>
    /// **优先级顺序限制：只对严格更高优先级加，右端是 `2M`** / The priority-order constraint applies only to strictly higher priorities, with right-hand side `2M`.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两个**同优先级**货物 → **0 条**约束（谓词是严格大于）。
    /// 2. 一个高一个低 → **1 条**。
    /// 3. 右端是 **`2M`**（这里 `M = 2`，所以是 4），不是 `M` 也不是 0。
    /// 4. 约束名带优先级值。
    ///
    /// Four paired points: two items of **equal priority** give **zero** constraints (the predicate is strictly greater); one higher and one lower gives **one**; the right-hand side is
    /// **`2M`** (4 here, since `M = 2`), not `M` and not 0; and the name carries the priority value.
    /// </remarks>
    [Fact]
    public void PriorityOrderLimit_ShouldCoverStrictlyHigherPrioritiesOnly() {
        // **名字的降序与装载顺序相反**：`AA` 是装载顺序 1，`ZA` 是 2。按名字降序排会得到 `[ZA, AA]`，与装载顺序相反。
        // **Descending name order is the reverse of the loading order**: `AA` has order 1 and `ZA` has order 2, so a descending name sort yields `[ZA, AA]`, the opposite.
        var positions = new List<StowagePosition> { MakePosition("AA", 1UL, 1), MakePosition("ZA", 2UL, 2) };

        var equal = new List<Item> { MakeItem("A", 2, "PEK", "SHA"), MakeItem("B", 2, "PEK", "SHA") };
        StowageModel equalStowage = MakeStowage(equal, positions);
        using var equalModel = new LinearMetaModel<Flt64>();
        equalStowage.Register(equalModel).IsOk.Should().BeTrue();
        int equalBefore = equalModel.Constraints.Count;
        new PriorityOrderLimit(equal, positions, equalStowage, 2.0).Invoke(equalModel).IsOk.Should().BeTrue();
        equalModel.Constraints.Count.Should().Be(equalBefore, "equal priorities are skipped");

        var distinct = new List<Item> { MakeItem("H", 3, "PEK", "SHA"), MakeItem("L", 1, "PEK", "SHA") };
        StowageModel stowage = MakeStowage(distinct, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        int before = model.Constraints.Count;
        new PriorityOrderLimit(distinct, positions, stowage, 2.0).Invoke(model).IsOk.Should().BeTrue();
        model.Constraints.Count.Should().Be(before + 1, "one ordered priority pair");

        Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64> row =
            ((LinearInequalityConstraint<Flt64>)model.Constraints[^1]).Inequality;
        row.Rhs.Constant.ToDouble().Should().BeApproximately(4.0, 1e-9, "the right-hand side is 2M with M = 2");
        row.Name.Should().Contain("3", "the name carries the higher priority");
        // **两个 `M` 都加**：`M = 2` 大于任一排名（0、1），所以四个系数全是正的。把第二个 `M` 改成减会让它们变负。
        // **Both `M`s are added**: `M = 2` exceeds every rank (0 and 1), so all four coefficients are positive. Negating the second `M` would make them negative.
        row.Lhs.Monomials.Should().HaveCount(4, "two positions times two items");
        row.Lhs.Monomials.Should().OnlyContain(monomial => monomial.Coefficient.ToDouble() > 0.0,
            "the Big-M term is added, not subtracted");

        // **排名由 `linearLoadingOrder` 决定，不是名字**。装载顺序 1 的 `ZA` 排 0，顺序 2 的 `AA` 排 1：
        // 高优先级货物在 `ZA` 上的系数是 `rank + M = 2`，在 `AA` 上是 `rank + M = 3`；低优先级货物反过来是 `M − rank`。
        // **The rank follows `linearLoadingOrder`, not the name.** `ZA` (order 1) ranks 0 and `AA` (order 2) ranks 1, so the high-priority item's coefficient is `2` at `ZA` and `3` at
        // `AA`, with the low-priority item taking `M − rank`.
        // **必须按符号名断言**：只断言系数集合 `{2, 3, 2, 1}` 会让"改按名字排序"的变异存活——两种排序给出的集合相同，
        // 只有系数与舱位的对应关系不同（实测存活过一次）。
        // **Assert per symbol name**: checking only the coefficient multiset `{2, 3, 2, 1}` lets a name-sort mutant survive, since both sorts yield the same multiset and differ only in
        // which position each coefficient attaches to (measured to survive once).
        var bySymbol = row.Lhs.Monomials.ToDictionary(
            monomial => monomial.Symbol.Name, monomial => monomial.Coefficient.ToDouble());
        bySymbol["assignment_H_AA"].Should().BeApproximately(2.0, 1e-9, "rank 0 plus M = 2");
        bySymbol["assignment_H_ZA"].Should().BeApproximately(3.0, 1e-9, "rank 1 plus M = 3");
        bySymbol["assignment_L_AA"].Should().BeApproximately(2.0, 1e-9, "M minus rank 0");
        bySymbol["assignment_L_ZA"].Should().BeApproximately(1.0, 1e-9, "M minus rank 1");
    }

    /// <summary>
    /// **同来源早装：只有多于一件货物的来源才加，且是 `GE 1`** / Same-source early loading applies only to sources with more than one item, as `GE 1`.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 一个来源只有 **1 件**货物 → **0 条**约束。
    /// 2. 一个来源有 **2 件**货物 → **1 条**。
    /// 3. 关系是 **`GE`**、右端 **1**（至少一件），不是等式。
    /// 4. 前段舱位数是 `earlyEnd + 1` 且被夹到 `[0, 舱位数]`——`earlyEnd = 10` 时不会越界。
    ///
    /// Four paired points: a source with **one** item gives **zero** constraints; a source with **two** gives **one**; the relation is **`GE`** with right-hand side **1** (at least one),
    /// not an equality; and the early-position count is `earlyEnd + 1` clamped to `[0, positionCount]`, so `earlyEnd = 10` cannot overrun.
    /// </remarks>
    [Fact]
    public void SourceEarlyLimit_ShouldSkipSingleItemSources() {
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2), MakePosition("P3", 3UL, 3),
        };

        var single = new List<Item> { MakeItem("A", 1, "PEK", "SHA") };
        StowageModel singleStowage = MakeStowage(single, positions);
        using var singleModel = new LinearMetaModel<Flt64>();
        singleStowage.Register(singleModel).IsOk.Should().BeTrue();
        int singleBefore = singleModel.Constraints.Count;
        new SourceEarlyLimit(single, positions, singleStowage,
            new Dictionary<string, IReadOnlyList<int>> { ["PEK"] = new[] { 0 } }, 1)
            .Invoke(singleModel).IsOk.Should().BeTrue();
        singleModel.Constraints.Count.Should().Be(singleBefore, "one item cannot be split across positions");

        var pair = new List<Item> { MakeItem("A", 1, "PEK", "SHA"), MakeItem("B", 1, "PEK", "CAN") };
        StowageModel stowage = MakeStowage(pair, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        int before = model.Constraints.Count;
        var limit = new SourceEarlyLimit(pair, positions, stowage,
            new Dictionary<string, IReadOnlyList<int>> { ["PEK"] = new[] { 0, 1 } }, 1);
        limit.Invoke(model).IsOk.Should().BeTrue();
        model.Constraints.Count.Should().Be(before + 1, "a two-item source gets one constraint");

        Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64> row =
            ((LinearInequalityConstraint<Flt64>)model.Constraints[^1]).Inequality;
        row.Comparison.Should().Be(Fuookami.Ospf.Math.Symbol.Inequality.Comparison.GE);
        row.Rhs.Constant.ToDouble().Should().Be(1.0, "at least one item, not an equality");
        row.Name.Should().Contain("PEK", "the name carries the source");

        // `earlyEnd` 远大于舱位数也不越界。
        // A far-too-large `earlyEnd` does not overrun.
        // **取 `earlyEnd + 1` 个**：`earlyEnd` 是末位下标，不是个数。
        // **Takes `earlyEnd + 1` positions**: `earlyEnd` is the last index, not a count.
        limit.EarlyPositions().Should().HaveCount(2, "earlyEnd 1 includes indices 0 and 1");
        new SourceEarlyLimit(pair, positions, stowage,
            new Dictionary<string, IReadOnlyList<int>>(), earlyEnd: 10).EarlyPositions()
            .Should().HaveCount(3, "ten clamps to the three available positions");
    }

    /// <summary>
    /// **目的地集合排除航班到达站** / The destination set excludes the flight's arrival.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 两件去 `SHA`、一件去 `CAN`，航班到 `SHA` → 目的地只有 `CAN`。
    /// 2. 没有航班时**不排除任何目的地**。
    /// 3. 来源集合去重且保序。
    ///
    /// Three paired points: two items to `SHA` and one to `CAN` with a flight arriving at `SHA` leave only `CAN`; without a flight **nothing is excluded**; and the source set is
    /// deduplicated and order-preserving.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldExcludeArrivalFromDestinations() {
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1) };
        var items = new List<Item> {
            MakeItem("A", 1, "PEK", "SHA"), MakeItem("B", 1, "PEK", "SHA"), MakeItem("C", 1, "CTU", "CAN"),
        };
        StowageModel stowage = MakeStowage(items, positions);
        var flight = new Flight(new FlightNo("CA1"), new IATACode("PEK"), new IATACode("SHA"));

        var withFlight = new LoadingEffectivenessAggregation(
            StowageMode.FullLoad, items, positions, stowage, flight: flight);
        withFlight.Destinations.Should().Equal(new[] { "CAN" }, "SHA is the arrival and is excluded");
        withFlight.Sources.Should().Equal(new[] { "PEK", "CTU" }, "sources are deduplicated in order");

        var withoutFlight = new LoadingEffectivenessAggregation(
            StowageMode.FullLoad, items, positions, stowage);
        withoutFlight.Destinations.Should().Equal(new[] { "SHA", "CAN" }, "no flight means no exclusion");
    }
}
