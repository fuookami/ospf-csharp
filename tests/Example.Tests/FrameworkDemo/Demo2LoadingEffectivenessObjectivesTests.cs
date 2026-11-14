// 装载效能域三条货物目标（B618）的判据 / Criteria for the three item objectives of the loading-effectiveness domain (B618).

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
/// **装载效能的三条货物目标（B618）** / The three item objectives of loading effectiveness (B618).
/// </summary>
/// <remarks>
/// `ItemAheadLoadLimit`、`ItemReserveLimit`、`ItemReweighNeededLimit` —— 三条都是**目标**，都是 `Minimum`，都读 `loaded` 符号。
/// 判据的重点是三处容易写反的地方：谓词"取第一个"、预留目标的非零常数、待复重目标的空集合不早退。
///
/// All three are **objectives**, all `Minimum`, all reading the `loaded` symbol. The criteria focus on the three easy inversions: the predicates' "first wins", the reserve objective's
/// non-zero constant, and the reweigh objective's lack of an empty-set early return.
/// </remarks>
public class Demo2LoadingEffectivenessObjectivesTests {
    private static Item MakeItem(string name, ItemOrder? order = null, double weight = 100.0) =>
        new(
            name: name,
            weight: weight,
            priority: new CargoPriority("P1", new Fuookami.Ospf.Math.Algebra.Number.UInt64(1UL), CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck,
            order: order);

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
    /// **提前装载：谓词取第一个成立的；一条都不成立就不加目标** / Item-ahead-load takes the first predicate that holds, and adds nothing when none does.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. **没有 `order` 的货物** → 三个谓词全不成立 → **不加目标**。
    /// 2. **只有 `hardstand`** → 谓词 0 成立 → 加目标。谓词 0 与 1、2 都成立，取**第一个（0）**。
    /// 3. **只有 `carBoard`** → 谓词 0 不成立、1 成立 → 取 **1**。
    /// 4. **只有 `reweighed`** → 取 **2**。
    ///
    /// Four paired points: an item **with no `order`** makes no predicate hold, so **no objective is added**; **only `hardstand`** makes predicate 0 hold, and since 0, 1 and 2 all hold,
    /// the **first (0)** is taken; **only `carBoard`** skips 0 and takes **1**; and **only `reweighed`** takes **2**.
    /// </remarks>
    [Fact]
    public void ItemAheadLoadLimit_ShouldTakeTheFirstHoldingPredicate() {
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1) };

        var none = new List<Item> { MakeItem("A") };
        StowageModel noneStowage = MakeStowage(none, positions);
        using var noneModel = new LinearMetaModel<Flt64>();
        noneStowage.Register(noneModel).IsOk.Should().BeTrue();
        int noneBefore = noneModel.MetaSubObjects.Count;
        var noneLimit = new ItemAheadLoadLimit(none, noneStowage);
        noneLimit.ActivePredicate().Should().BeNull("no operational instant at all");
        noneLimit.Invoke(noneModel).IsOk.Should().BeTrue();
        noneModel.MetaSubObjects.Count.Should().Be(noneBefore, "no predicate holds, so no objective is added");

        var onlyHardstand = new List<Item> { MakeItem("A", new ItemOrder { Hardstand = 5 }) };
        StowageModel hardstandStowage = MakeStowage(onlyHardstand, positions);
        using var hardstandModel = new LinearMetaModel<Flt64>();
        hardstandStowage.Register(hardstandModel).IsOk.Should().BeTrue();
        var hardstandLimit = new ItemAheadLoadLimit(onlyHardstand, hardstandStowage);
        hardstandLimit.ActivePredicate().Should().Be(0, "predicate zero holds and the later, wider ones also hold");
        hardstandLimit.Invoke(hardstandModel).IsOk.Should().BeTrue();
        hardstandModel.MetaSubObjects[^1].Name.Should().Be("item ahead load");
        hardstandModel.MetaSubObjects[^1].Category.Should().Be(ObjectCategory.Minimum);

        var onlyBoard = new List<Item> { MakeItem("A", new ItemOrder { CarBoard = 5 }) };
        StowageModel boardStowage = MakeStowage(onlyBoard, positions);
        new ItemAheadLoadLimit(onlyBoard, boardStowage).ActivePredicate().Should().Be(1, "only the second predicate holds");

        var onlyReweigh = new List<Item> { MakeItem("A", new ItemOrder { Reweighed = 5 }) };
        StowageModel reweighStowage = MakeStowage(onlyReweigh, positions);
        new ItemAheadLoadLimit(onlyReweigh, reweighStowage).ActivePredicate().Should().Be(2, "only the third predicate holds");
    }

    /// <summary>
    /// **预留目标：每件货物带一个常数 `+c`，所以常数项非零** / The reserve objective carries a `+c` per item, so its constant is non-zero.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 三件货物、系数 2 → 常数项是 **6**，不是 0。丢掉常数项会让不同车型的目标不可比。
    /// 2. 类别是 `Minimum`，名字是 `"item reserve"`（带空格）。
    /// 3. 单项式系数是 **负的** `−c`。
    ///
    /// Three paired points: three items at coefficient 2 give a constant of **6**, not 0, since dropping it would make objectives incomparable across aircraft types; the category is
    /// `Minimum` with the spaced name `"item reserve"`; and the monomial coefficients are **negative** `−c`.
    /// </remarks>
    [Fact]
    public void ItemReserveLimit_ShouldCarryAPerItemConstant() {
        var items = new List<Item> { MakeItem("A"), MakeItem("B"), MakeItem("C") };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1), MakePosition("P2", 2UL, 2) };
        StowageModel stowage = MakeStowage(items, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();

        new ItemReserveLimit(items, stowage, _ => new Flt64(2.0)).Invoke(model).IsOk.Should().BeTrue();

        MetaSubObject<Flt64> objective = model.MetaSubObjects[^1];
        objective.Name.Should().Be("item reserve");
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Polynomial.Constant.ToDouble().Should().BeApproximately(6.0, 1e-9, "three items times two");
        objective.Polynomial.Monomials.Should().NotBeEmpty("every item has a loaded variable");
        objective.Polynomial.Monomials.Should().OnlyContain(
            monomial => monomial.Coefficient.ToDouble() < 0.0,
            "the loaded term is subtracted, not added");
    }

    /// <summary>
    /// **待复重目标：没有复重时刻的进目标；没有 `order` 也算没有复重** / The reweigh objective includes items without a reweigh instant, including those with no `order`.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 一件有 `reweighed`、一件没有、一件**根本没有 `order`** → 后两件进目标 → `PendingIndices` 是 `{1, 2}`。
    /// 2. **全部已复重时仍然加目标**（与 `ItemAheadLoadLimit` 的早退不同），只是单项式为空。
    /// 3. 类别 `Minimum`、名字 `"item reweigh needed"`。
    /// 4. 系数是**正的**（最小化未复重货物的装载量）。
    ///
    /// Four paired points: one item reweighed, one not and one **with no `order` at all** leave the last two pending, so `PendingIndices` is `{1, 2}`; **the objective is still added when
    /// everything is reweighed** (unlike `ItemAheadLoadLimit`'s early return), merely with no monomials; the category is `Minimum` named `"item reweigh needed"`; and the coefficients
    /// are **positive**, minimising the loaded amount of unreweighed cargo.
    /// </remarks>
    [Fact]
    public void ItemReweighNeededLimit_ShouldIncludeItemsWithoutOrder() {
        var items = new List<Item> {
            MakeItem("A", new ItemOrder { Hardstand = 1, Reweighed = 3 }),
            MakeItem("B", new ItemOrder { Hardstand = 1 }),
            MakeItem("C"),
        };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1) };
        StowageModel stowage = MakeStowage(items, positions);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();

        var limit = new ItemReweighNeededLimit(items, stowage);
        limit.PendingIndices().Should().Equal(new[] { 1, 2 }, "B has no reweigh instant and C has no order at all");
        limit.Invoke(model).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> objective = model.MetaSubObjects[^1];
        objective.Name.Should().Be("item reweigh needed");
        objective.Category.Should().Be(ObjectCategory.Minimum);
        objective.Polynomial.Monomials.Should().OnlyContain(monomial => monomial.Coefficient.ToDouble() > 0.0);

        // 全部已复重：仍然加目标，只是空的。
        // Everything reweighed: the objective is still added, merely empty.
        var allDone = new List<Item> { MakeItem("A", new ItemOrder { Reweighed = 1 }) };
        StowageModel doneStowage = MakeStowage(allDone, positions);
        using var doneModel = new LinearMetaModel<Flt64>();
        doneStowage.Register(doneModel).IsOk.Should().BeTrue();
        int doneBefore = doneModel.MetaSubObjects.Count;
        var doneLimit = new ItemReweighNeededLimit(allDone, doneStowage);
        doneLimit.PendingIndices().Should().BeEmpty();
        doneLimit.Invoke(doneModel).IsOk.Should().BeTrue();
        doneModel.MetaSubObjects.Count.Should().Be(doneBefore + 1,
            "unlike the ahead-load limit, there is no empty-set early return");
        doneModel.MetaSubObjects[^1].Polynomial.Monomials.Should().BeEmpty("nothing is pending");
    }
}
