// 顺序装载反转的判据 / Criteria for sequential-loading reversal.

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
/// **顺序装载反转** / Sequential-loading reversal.
/// </summary>
/// <remarks>
/// `SequentialLoading` 与 `RelativeOrder` 结构相同，但排序键换成 `order.sequence`，且**没有装载顺序的货物被排除**。
/// 判据的重点：排除规则、全组合谓词的方向、以及函数符号能进线性目标。
///
/// `SequentialLoading` mirrors `RelativeOrder` but sorts on `order.sequence` and **excludes items without one**. The criteria focus on the exclusion rule, the full-combination predicate's
/// direction, and function symbols entering a linear objective.
/// </remarks>
public class Demo2SequentialLoadingTests {
    private static Item MakeItem(string name, byte? sequence, double weight = 100.0) =>
        new(
            name: name,
            weight: weight,
            priority: new CargoPriority("P1", new Fuookami.Ospf.Math.Algebra.Number.UInt64(1UL), CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck,
            order: sequence is null ? null : new ItemOrder { Sequence = sequence });

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

    private static List<(StowagePosition First, StowagePosition Second)> Ordered(
        IReadOnlyList<StowagePosition> positions) {
        var sorted = positions
            .OrderBy(position => position.AircraftPosition.LinearLoadingOrder).ToList();
        var pairs = new List<(StowagePosition, StowagePosition)>();
        for (int i = 0; i < sorted.Count; ++i) {
            for (int j = i + 1; j < sorted.Count; ++j) {
                pairs.Add((sorted[i], sorted[j]));
            }
        }
        return pairs;
    }

    /// <summary>
    /// **只收有装载顺序的货物；有序对是全组合且方向是"顺序小在前"** / Only items with a sequence; pairs are full combinations in ascending order.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 三件有顺序（1、2、3）+ **一件没有顺序** → 有序对是 **3 个**（1→2、1→3、2→3），那件没有顺序的**完全不参与**。
    /// 2. **方向是"顺序小在前"**，不是反过来。
    /// 3. **顺序相同不成对**（谓词严格小于）。
    /// 4. 有序舱位对**由调用方传入**，本类不重算。
    ///
    /// Four paired points: three items with sequences 1, 2, 3 plus **one without** give **three** ordered pairs (1→2, 1→3, 2→3), with the sequence-less item taking **no part**; **the
    /// direction is ascending**; **equal sequences form no pair** (strict less-than); and the ordered position pairs **come from the caller**.
    /// </remarks>
    [Fact]
    public void SequentialLoading_ShouldExcludeItemsWithoutSequence() {
        var items = new List<Item> {
            MakeItem("A", 1), MakeItem("B", 2), MakeItem("C", 3), MakeItem("D", null),
        };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        StowageModel stowage = MakeStowage(items, positions);
        var loading = new SequentialLoading(items, positions, Ordered(positions), stowage);

        loading.OrderedItems.Should().HaveCount(3, "three items carry a sequence, so 3+0 pairs");
        loading.OrderedItems.Should().Contain((items[0], items[1]));
        loading.OrderedItems.Should().Contain((items[0], items[2]));
        loading.OrderedItems.Should().Contain((items[1], items[2]));
        loading.OrderedItems.Should().NotContain(pair => pair.First == items[3] || pair.Second == items[3],
            "the sequence-less item never participates");
        loading.OrderedItems.Should().NotContain((items[1], items[0]), "the direction is ascending");
        loading.OrderedPositions.Should().HaveCount(1, "the caller supplied one pair for two positions");

        var equal = new List<Item> { MakeItem("E", 4), MakeItem("F", 4) };
        StowageModel equalStowage = MakeStowage(equal, positions);
        new SequentialLoading(equal, positions, Ordered(positions), equalStowage)
            .OrderedItems.Should().BeEmpty("equal sequences form no ordered pair");
    }

    /// <summary>
    /// **符号矩阵铺满，且函数符号能进线性目标** / The matrix is filled and function symbols can enter a linear objective.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 注册成功后符号矩阵是 **3 × 1**，每格都有符号。
    /// 2. **符号名互不相同**（依赖 `Item`/`StowagePosition` 的 `ToString` 覆写）。
    /// 3. **可放置组合是真函数符号**（`LinearFunctionSymbolAdapter`），不是恒零——它的 `Polynomial` 在自己的
    ///    `ResultVar` 上是线性的，所以能展开进目标。
    /// 4. `ItemOrderReverseLimit` 因此**正常产出目标**，名为 `"item order reverse"`、类别 `Minimum`。
    ///
    /// Four paired points: after registration the matrix is **3 × 1** with a symbol in every slot; **the names all differ** (relies on the `ToString` overrides); **placeable combinations
    /// are real function symbols**, not constant zero, and their `Polynomial` is linear over their own `ResultVar`, so they expand into the objective; and `ItemOrderReverseLimit` therefore
    /// **produces an objective normally**, named `"item order reverse"` with category `Minimum`.
    /// </remarks>
    [Fact]
    public void ItemOrderReverseLimit_ShouldProduceAMinimisation() {
        var items = new List<Item> { MakeItem("A", 1), MakeItem("B", 2), MakeItem("C", 3) };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        StowageModel stowage = MakeStowage(items, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();

        var loading = new SequentialLoading(items, positions, Ordered(positions), stowage);
        loading.Register(model).IsOk.Should().BeTrue("the boundary is NONZERO_TOLERANCE, which SafeLattice accepts");

        var names = new List<string>();
        for (int p1 = 0; p1 < loading.OrderedItems.Count; ++p1) {
            for (int p2 = 0; p2 < loading.OrderedPositions.Count; ++p2) {
                loading.Reversal(p1, p2).Should().NotBeNull();
                names.Add(loading.Reversal(p1, p2)!.Name);
            }
        }
        names.Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(name => !name.Contains("Fuookami.Ospf", StringComparison.Ordinal),
            "names carry item and position names, never a type dump");
        loading.Reversal(0, 0).Should().BeOfType<
            Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>>(
            "both items fit both positions, so this is a real indicator");

        // **系数必须依赖舱位**才能观测交叉顺序：第一个元组是 `(position2, item1)`，第二个是 `(position1, item2)`。
        // 只依赖货物的系数在两种写法下相同，会让"系数不交叉"的变异存活（实测存活过一次）。
        // **The coefficient must depend on the position** for the crossed ordering to be observable: the first tuple is `(position2, item1)` and the second `(position1, item2)`. A
        // coefficient depending only on the items is identical either way, letting an uncrossed mutant survive (measured once).
        new ItemOrderReverseLimit(loading,
                (left, _) => new Flt64(left.Item1.AircraftPosition.LinearLoadingOrder * 10.0))
            .Invoke(model).IsOk.Should().BeTrue("a function symbol expands through its own ResultVar polynomial");
        MetaSubObject<Flt64> objective = model.MetaSubObjects[^1];
        objective.Name.Should().Be("item order reverse");
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Polynomial.Monomials.Should().NotBeEmpty();
        objective.Polynomial.Monomials.Should().OnlyContain(
            monomial => monomial.Coefficient.ToDouble() > 0.0, "a positive coefficient times the reversal indicator");
        // 舱位顺序 1 与 2：交叉写法读的是**第二个**舱位（顺序 2），得到 20；不交叉会读到第一个（顺序 1），得到 10。
        // Position orders 1 and 2: the crossed form reads the **second** position (order 2), giving 20, while the uncrossed form reads the first (order 1), giving 10.
        objective.Polynomial.Monomials.Should().OnlyContain(
            monomial => System.Math.Abs(monomial.Coefficient.ToDouble() - 20.0) < 1e-9,
            "the coefficient is taken on the second position of the pair");
    }

    /// <summary>
    /// **没有有序对时直接返回成功、不加目标** / With no ordered pairs it returns success and adds nothing.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. 只有一件有顺序的货物 → 没有有序对 → **不加目标**，返回成功。
    /// 2. 目标位数量不变——`if (monomials.isEmpty) return ok` 不能省。
    ///
    /// Two paired points: one item with a sequence gives no ordered pair, so **no objective** is added and success is returned; and the objective count is unchanged, since the
    /// baseline's early return must not be dropped.
    /// </remarks>
    [Fact]
    public void ItemOrderReverseLimit_ShouldSkipEmptySets() {
        var items = new List<Item> { MakeItem("A", 1) };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        StowageModel stowage = MakeStowage(items, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        var loading = new SequentialLoading(items, positions, Ordered(positions), stowage);
        loading.OrderedItems.Should().BeEmpty();
        loading.Register(model).IsOk.Should().BeTrue();
        int before = model.MetaSubObjects.Count;
        new ItemOrderReverseLimit(loading).Invoke(model).IsOk.Should().BeTrue();
        model.MetaSubObjects.Count.Should().Be(before, "an empty monomial set adds no objective");
    }
}
