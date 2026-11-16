// 拖车换装/绕行的判据 / Criteria for trailer change and circling.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
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
/// **拖车换装 / 绕行** / Trailer change and circling.
/// </summary>
/// <remarks>
/// 判据的重点：拖车对只收"有货在本次装载里"的、拖车内货物对**不排序**、换装条件的**交叉**、以及两个目标都是最小化。
///
/// The criteria focus on trailer pairs keeping only trailers with cargo in this load, item pairs within a trailer **not** being sorted, the **crossed** change condition, and both
/// objectives being minimisations.
/// </remarks>
public class Demo2TrailerLoadingTests {
    private static Item MakeItem(string name) =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority("P1", new Fuookami.Ospf.Math.Algebra.Number.UInt64(1UL), CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: "PEK",
            destination: "SHA",
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
    /// **拖车对只收有货的拖车，按顺序后按名字；拖车内货物对不排序** / Trailer pairs keep only loaded trailers, ordered by sequence then name; item pairs stay unsorted.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. **三辆拖车，其中一辆没有本次装载的货** → 只剩 2 辆 → **1 个**相邻拖车对。
    /// 2. 排序是**顺序升序**，再按**名字**——所以给顺序相反的名字时仍按顺序排。
    /// 3. **拖车内货物对是全组合**：一辆车装 3 件 → **3 个**对。
    /// 4. **不按货物顺序过滤**：拖车里的货物没有 `order` 也照样成对（与 `SequentialLoading` 不同）。
    ///
    /// Four paired points: three trailers with **one carrying no cargo in this load** leave two and hence **one** adjacent trailer pair; the sort is by sequence then name, so reversed
    /// names still sort by sequence; **item pairs within a trailer are full combinations**, three items giving **three** pairs; and **there is no sequence filter**, so items without an
    /// `order` still pair up (unlike `SequentialLoading`).
    /// </remarks>
    [Fact]
    public void TrailerLoading_ShouldKeepOnlyLoadedTrailers() {
        // **四件货**：`AT` 装一件、`ZT` 装三件，这样既有相邻拖车对，又有三组拖车内货物对。
        // **Four items**: `AT` carries one and `ZT` three, giving both an adjacent trailer pair and three item pairs.
        var items = new List<Item> { MakeItem("A"), MakeItem("B"), MakeItem("C"), MakeItem("D") };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var adjacent = new List<(StowagePosition, StowagePosition)> { (positions[0], positions[1]) };
        var trailers = new List<Trailer> {
            // **名字的字母序与装载顺序相反**：`ZT` 是顺序 1，`AT` 是 2。按名字排会得到 `[AT, ZT]`，与按顺序排相反。
            // **The alphabetical order is the reverse of the loading order**: `ZT` has order 1 and `AT` order 2, so a name sort yields `[AT, ZT]`, the opposite.
            new(TrailerType.Transit, 2, "AT", new List<Item> { items[1], items[2], items[3] }),
            new(TrailerType.Hardstand, 1, "ZT", new List<Item> { items[0] }),
            new(TrailerType.Warehouse, 9, "UNUSED", new List<Item> { MakeItem("X") }),
        };
        StowageModel stowage = MakeStowage(items, positions);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true);
        var loading = new TrailerLoading(items, positions, adjacent, trailers, stowage, load);

        loading.OrderedTrailers.Should().HaveCount(1, "only ZT and AT carry items in this load");
        loading.OrderedTrailers[0].First.Name.Should().Be("ZT", "sequence 1 sorts first, despite sorting last by name");
        loading.OrderedTrailers[0].Second.Name.Should().Be("AT");
        loading.OrderedItemsInTrailers.Should().HaveCount(3, "ZT's three items give 3+0 pairs; AT's single item gives none");
        loading.OrderedItemsInTrailers.Should().Contain((items[1], items[2]));
        loading.OrderedItemsInTrailers.Should().Contain((items[1], items[3]));
        loading.OrderedItemsInTrailers.Should().Contain((items[2], items[3]));
        loading.OrderedItemsInTrailers.Should().NotContain(pair => pair.First == items[0] || pair.Second == items[0],
            "AT carries only one item, so it contributes no pair");
    }

    /// <summary>
    /// **两个目标都是最小化、名字带空格、且都有单项式** / Both objectives minimise, with spaced names, and both carry monomials.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 换装目标类别 `Minimum`，名字 `"trailer change"`。
    /// 2. 绕行目标类别 `Minimum`，名字 `"trailer circling"`。
    /// 3. **两个名字都用空格**。
    /// 4. 两者都有单项式——绕行指示来自配载变量，换装指示来自装载量，都能展开。
    ///
    /// Four paired points: the change objective is `Minimum` named `"trailer change"`; the circling one is `Minimum` named `"trailer circling"`; **both names use spaces**; and both carry
    /// monomials — circling from stowage variables and change from loads, both expandable.
    /// </remarks>
    [Fact]
    public void TrailerObjectives_ShouldMinimiseWithSpacedNames() {
        var items = new List<Item> { MakeItem("A"), MakeItem("B"), MakeItem("C") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2), MakePosition("P3", 3UL, 3),
        };
        var adjacent = new List<(StowagePosition, StowagePosition)> {
            (positions[0], positions[1]), (positions[1], positions[2]),
        };
        var trailers = new List<Trailer> {
            new(TrailerType.Transit, 1, "T1", new List<Item> { items[0], items[1] }),
            new(TrailerType.Hardstand, 2, "T2", new List<Item> { items[2] }),
        };
        StowageModel stowage = MakeStowage(items, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true);
        var loading = new TrailerLoading(items, positions, adjacent, trailers, stowage, load);
        loading.Register(model).IsOk.Should().BeTrue();

        new TrailerChangeLimit(loading).Invoke(model).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> change = model.MetaSubObjects[^1];
        change.Name.Should().Be("trailer change");
        change.Category.Should().Be(ObjectCategory.Minimum);
        change.Polynomial.Monomials.Should().NotBeEmpty();

        // **系数必须依赖舱位**才能观测交叉顺序；常量系数会让"系数不交叉"的变异存活（实测存活过一次）。
        // **The coefficient must depend on the position** for the crossed ordering to be observable; a constant one lets the uncrossed mutant survive (measured once).
        new TrailerCirclingLimit(loading,
                (left, _) => new Flt64(left.Item1.AircraftPosition.LinearLoadingOrder * 10.0))
            .Invoke(model).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> circling = model.MetaSubObjects[^1];
        circling.Name.Should().Be("trailer circling");
        circling.Category.Should().Be(ObjectCategory.Minimum);
        circling.Polynomial.Monomials.Should().NotBeEmpty();
        // 两个相邻对分别以顺序 1 与 2 开头，交叉写法读第二个舱位 → 系数 20 与 30；不交叉会读到 10 与 20。
        // The two adjacent pairs start at orders 1 and 2; the crossed form reads the second position, giving 20 and 30, while the uncrossed form gives 10 and 20.
        circling.Polynomial.Monomials.Select(monomial => monomial.Coefficient.ToDouble())
            .Distinct().OrderBy(value => value).Should().Equal(new[] { 20.0, 30.0 });
    }

    /// <summary>
    /// **没有相邻拖车对时不加目标** / No adjacent trailer pair means no objective.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. 只有一辆拖车 → 没有相邻对 → 换装目标**不加**、返回成功。
    /// 2. 目标位数量不变——基线的 `if (monomials.isEmpty) return ok`。
    ///
    /// Two paired points: a single trailer gives no adjacent pair, so **no** change objective is added and success is returned; and the objective count is unchanged, per the baseline's
    /// early return.
    /// </remarks>
    [Fact]
    public void TrailerChangeLimit_ShouldSkipEmptySets() {
        var items = new List<Item> { MakeItem("A"), MakeItem("B") };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var adjacent = new List<(StowagePosition, StowagePosition)> { (positions[0], positions[1]) };
        var trailers = new List<Trailer> {
            new(TrailerType.Transit, 1, "T1", new List<Item> { items[0], items[1] }),
        };
        StowageModel stowage = MakeStowage(items, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true);
        var loading = new TrailerLoading(items, positions, adjacent, trailers, stowage, load);
        loading.OrderedTrailers.Should().BeEmpty();
        loading.Register(model).IsOk.Should().BeTrue();

        int before = model.MetaSubObjects.Count;
        new TrailerChangeLimit(loading).Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Count.Should().Be(before, "one trailer gives no adjacent pair");

        // 绕行这条**不**早退：一辆车里的两件货仍构成货物对。
        // The circling one does **not** early-return: two items in one trailer still form a pair.
        loading.OrderedItemsInTrailers.Should().HaveCount(1);
        new TrailerCirclingLimit(loading).Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Count.Should().Be(before + 1, "an item pair exists, so circling does add an objective");
    }
}
