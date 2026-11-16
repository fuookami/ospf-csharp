// 相邻同来源/同目的地的判据 / Criteria for same-source and same-destination adjacency.

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
/// **相邻同来源 / 同目的地** / Same-source and same-destination adjacency.
/// </summary>
/// <remarks>
/// 判据的重点：`IfFunction` 分支**只看第一个舱位**、退化分支**不是一律给零**、以及两个目标都是**最大化**。
///
/// The criteria focus on the `IfFunction` branch **testing only the first position**, the degenerate branches **not always yielding zero**, and both objectives being **maximisations**.
/// </remarks>
public class Demo2TransferAdjacentTests {
    private static Item MakeItem(string name, string source, string destination) =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority("P1", new Fuookami.Ospf.Math.Algebra.Number.UInt64(1UL), CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: source,
            destination: destination,
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);

    private static StowagePosition MakePosition(
        string name, ulong id, byte order, bool stowageNeeded = true) {
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
            status: new PositionStatus(stowageNeeded, false, false, false),
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
    /// **第一个舱位需配载时用真指示；不需配载时退化分支不是一律给零** / The indicator is real when the first position needs stowage, and the degenerate branches are not always zero.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. **第一个舱位需要配载** → 指示符号是 `LinearFunctionSymbolAdapter`（真指示），不是恒零。
    /// 2. **第一个舱位不需要配载** → 走退化分支，符号是 `LinearExpressionSymbol`。
    /// 3. 退化分支的**多项式就是第一个舱位的装载量**，不是零——所以它有单项式。
    /// 4. 矩阵是（来源 × 相邻舱位对）：2 个来源 × 1 个舱位对 = **2 个**同来源指示，目的地同理。
    ///
    /// Four paired points: **when the first position needs stowage** the indicator is a `LinearFunctionSymbolAdapter` (a real indicator), not constant zero; **when it does not** the
    /// degenerate branch yields a `LinearExpressionSymbol`; that branch's **polynomial is the first position's load**, not zero, so it has monomials; and the matrices are
    /// (source × adjacent pair) — two sources times one pair gives **two** same-source indicators, and likewise for destinations.
    /// </remarks>
    [Fact]
    public void TransferAdjacentLoading_ShouldBranchOnTheFirstPositionOnly() {
        var items = new List<Item> { MakeItem("A", "PEK", "SHA"), MakeItem("B", "CTU", "CAN") };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var adjacent = new List<(StowagePosition, StowagePosition)> { (positions[0], positions[1]) };
        StowageModel stowage = MakeStowage(items, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();

        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true);
        var loading = new TransferAdjacentLoading(
            adjacent, new[] { "PEK", "CTU" }, new[] { "SHA", "CAN" }, load);
        loading.Register(model, positions).IsOk.Should().BeTrue();

        loading.SameSource(0, 0).Should().BeOfType<Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>>(
            "the first position needs stowage, so this is a real indicator");
        loading.SameDestination(0, 0).Should().NotBeNull();
        loading.SameSource(1, 0).Should().NotBeNull("two sources give two indicators");

        // **第一个舱位不需要配载** → 走退化分支，符号是普通线性表达式**且多项式不是零**。
        // 只看第二个舱位、或退化分支一律给零、或一律用 `IfFunction`，这三种变异都会被这三条断言杀掉。
        //
        // **The first position needs no stowage**, so the degenerate branch runs and the symbol is a plain linear expression **whose polynomial is not zero**. These three assertions kill the
        // mutants that test the second position, always yield zero, or always use `IfFunction`.
        var relaxedPositions = new List<StowagePosition> {
            MakePosition("P1", 1UL, 1, stowageNeeded: false), MakePosition("P2", 2UL, 2),
        };
        var relaxedAdjacent = new List<(StowagePosition, StowagePosition)> {
            (relaxedPositions[0], relaxedPositions[1]),
        };
        StowageModel relaxedStowage = MakeStowage(items, relaxedPositions);
        using var relaxedModel = new LinearMetaModel<Flt64>();
        relaxedStowage.Register(relaxedModel).IsOk.Should().BeTrue();
        var relaxedLoad = new Load(items, relaxedPositions, relaxedStowage, withMultiLoadingSchema: true);
        var relaxed = new TransferAdjacentLoading(relaxedAdjacent, new[] { "PEK" }, new[] { "SHA" }, relaxedLoad);
        relaxed.Register(relaxedModel, relaxedPositions).IsOk.Should().BeTrue();
        relaxed.SameSource(0, 0).Should().BeOfType<Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol>(
            "the first position needs no stowage, so no indicator is created; the second position's need must not matter");
        ((Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)relaxed.SameSource(0, 0)!).Polynomial.Monomials
            .Should().NotBeEmpty("the degenerate branch carries the first position's load, not zero");
    }

    /// <summary>
    /// **两个目标都是最大化，且名字带空格** / Both objectives are maximisations with spaced names.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 同来源目标类别是 `Maximum`，名字 `"same source adjacent"`。
    /// 2. 同目的地目标类别是 `Maximum`，名字 `"same destination adjacent"`。
    /// 3. **两个名字都用空格，不用下划线**——用下划线会和管线名撞车。
    /// 4. 两者都有单项式（至少来源/目的地非空且舱位相邻）。
    ///
    /// Four paired points: the same-source objective is `Maximum` named `"same source adjacent"`; the same-destination one is `Maximum` named `"same destination adjacent"`; **both names use
    /// spaces**, since underscores would collide with the pipeline names; and both carry monomials.
    /// </remarks>
    [Fact]
    public void BothAdacencyObjectives_ShouldMaximiseWithSpacedNames() {
        var items = new List<Item> { MakeItem("A", "PEK", "SHA") };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        var adjacent = new List<(StowagePosition, StowagePosition)> { (positions[0], positions[1]) };
        StowageModel stowage = MakeStowage(items, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true);
        var loading = new TransferAdjacentLoading(adjacent, new[] { "PEK" }, new[] { "SHA" }, load);
        loading.Register(model, positions).IsOk.Should().BeTrue();

        new SameSourceAdjacentLimit(adjacent, new[] { "PEK" }, loading).Invoke(model).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> source = model.MetaSubObjects[^1];
        source.Name.Should().Be("same source adjacent");
        source.Category.Should().Be(ObjectCategory.Maximum);
        source.Polynomial.Monomials.Should().NotBeEmpty();

        new SameDestinationAdjacent(adjacent, new[] { "SHA" }, loading).Invoke(model).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> destination = model.MetaSubObjects[^1];
        destination.Name.Should().Be("same destination adjacent");
        destination.Category.Should().Be(ObjectCategory.Maximum);
        destination.Polynomial.Monomials.Should().NotBeEmpty();
    }

    /// <summary>
    /// **目标条数 = 来源数 × 相邻舱位对数** / The objective covers sources times adjacent pairs.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. 2 个来源 × 2 个相邻舱位对（3 个舱位）→ 指示矩阵 4 格，目标里每个指示都有单项式。
    /// 2. 系数按 `(来源, 第一个舱位, 第二个舱位)` 取：给一个依赖舱位的系数时，不同相邻对的系数不同。
    ///
    /// Two paired points: two sources times two adjacent pairs (three positions) fill four indicator slots and each contributes monomials; and the coefficient comes per (source, first
    /// position, second position), so a position-dependent coefficient differs across adjacent pairs.
    /// </remarks>
    [Fact]
    public void SameSourceAdjacentLimit_ShouldCoverEveryCombination() {
        var items = new List<Item> { MakeItem("A", "PEK", "SHA") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2), MakePosition("P3", 3UL, 3),
        };
        var adjacent = new List<(StowagePosition, StowagePosition)> {
            (positions[0], positions[1]), (positions[1], positions[2]),
        };
        StowageModel stowage = MakeStowage(items, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true);
        var loading = new TransferAdjacentLoading(adjacent, new[] { "PEK", "CTU" }, new[] { "SHA" }, load);
        loading.Register(model, positions).IsOk.Should().BeTrue();

        for (int s = 0; s < 2; ++s) {
            for (int p = 0; p < 2; ++p) {
                loading.SameSource(s, p).Should().NotBeNull("two sources times two pairs");
            }
        }

        new SameSourceAdjacentLimit(adjacent, new[] { "PEK", "CTU" }, loading,
                (_, first, _) => new Flt64(first.AircraftPosition.LinearLoadingOrder))
            .Invoke(model).IsOk.Should().BeTrue();
        var coefficients = model.MetaSubObjects[^1].Polynomial.Monomials
            .Select(monomial => monomial.Coefficient.ToDouble()).Distinct().OrderBy(value => value).ToList();
        coefficients.Should().HaveCount(2, "the two adjacent pairs start at orders 1 and 2");
    }
}
