// 业载守恒与按谓词装载量的判据 / Criteria for payload conservation and predicate-scoped amounts.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **业载守恒与按谓词的装载量** / Payload conservation and predicate-scoped amounts.
/// </summary>
/// <remarks>
/// 两件事：
///
/// 1. **预分配模式下业载必须等于目标业载**。这里加的是一行 `eq` 约束，不是目标函数里的软项。少了它，预分配算出来的总量
/// 可以偏离货主要多少，而每个舱位单独看都合法。
/// 2. **按谓词取装载量**（`load_amount_of_{舱位}`）。用途是"只看主舱货"或"只看某类货"装了几件，与全部货物的
///    `load_amount` 是**两个不同的量**——它们共用同一批 `stowage` 符号，所以同一份解里必然自洽。
///
/// Two things: (1) under predistribution the payload must **equal** the target — this implementation adds an `eq` row, not a soft objective term; without it the planned total drifts
/// from the shipper's figure while every position looks legal on its own. (2) **Predicate-scoped amounts** (`load_amount_of_{position}`), which this implementation uses for "main-deck
/// pieces only" or "pieces of one class" — a **different quantity** from the all-items `load_amount`, sharing the same `stowage` symbols so one solution is consistent by
/// construction.
/// </remarks>
public class Demo2PayloadConservationTests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static Item MakeItem(string name, bool aog = false) =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromCode(aog ? CargoCode.AOG : CargoCode.BAL),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);

    private static StowagePosition MakePosition(string name, ulong id, PositionLocation location) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(500.0, 500.0, 60.0, 60.0),
            shape: new PositionShape(1.0, 1.0, 1.0),
            location: location);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(true, false, true, true),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 3UL,
            plw: new PositionPredicateLoadWeight(350.0, 300.0, 400.0));
    }

    private static DiscreteLoadingContext<Flt64> MakeKernel(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions) {
        var loadingItems = items.Select(item => new LoadingItem<Flt64>(item.Name, Grams(item.Weight))).ToList();
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

    private static (Load Load, Stowage Stowage, List<Item> Items, List<StowagePosition> Positions) Prepare() {
        var items = new List<Item> { MakeItem("I1"), MakeItem("I2", aog: true) };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, PositionLocation.NormalMainLocation),
            MakePosition("P2", 2UL, PositionLocation.LowForwardNotBulkLocation),
        };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        return (load, stowage, items, positions);
    }

    private static LinearMetaModel<Flt64> RegisterAll(
        Load load,
        Stowage stowage,
        Payload payload,
        StowageMode mode) {
        var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();
        payload.Register(mode, model).IsOk.Should().BeTrue();
        return model;
    }

    /// <summary>
    /// **预分配模式下有一条业载守恒约束** / Predistribution adds a payload conservation row.
    /// </summary>
    /// <remarks>
    /// 成对：`Predistribution` 下这条约束在场，`FullLoad` 下不在场。两个模式都有它会强制全配载也等于目标值——而全配载的
    /// 意义恰恰是"能装多少装多少"，那会把模型直接判不可行。
    ///
    /// Paired: the row is present under `Predistribution` and absent under `FullLoad`. Present in both would force full load to equal the target too — and full load means "load
    /// as much as fits", which turns the model infeasible outright.
    /// </remarks>
    [Fact]
    public void Payload_ShouldConserveOnlyUnderPredistribution() {
        (Load load, Stowage stowage, List<Item> _, List<StowagePosition> positions) = Prepare();

        var predistributionPayload = new Payload(positions, load, plannedPayload: 5000.0, maxPayload: 20000.0);
        using LinearMetaModel<Flt64> predistributionModel = RegisterAll(load, stowage, predistributionPayload, StowageMode.Predistribution);
        predistributionModel.Constraints.Select(constraint => constraint.Name)
            .Should().Contain("predistribution_payload_conservation");

        (Load fullLoad, Stowage fullStowage, List<Item> _, List<StowagePosition> fullPositions) = Prepare();
        var fullPayload = new Payload(fullPositions, fullLoad, 5000.0, 20000.0);
        using LinearMetaModel<Flt64> fullModel = RegisterAll(fullLoad, fullStowage, fullPayload, StowageMode.FullLoad);
        fullModel.Constraints.Select(constraint => constraint.Name)
            .Should().NotContain("predistribution_payload_conservation",
                "full load means load as much as fits, so pinning the total would make it infeasible");
    }

    /// <summary>
    /// **守恒的右端取已计算业载，没给才用计划业载** / The row's right side prefers the computed payload over the planned one.
    /// </summary>
    /// <remarks>
    /// 成对：`computedPayload` 给出时右端是它（6000），未给出时是 `plannedPayload`（5000）。写成"总是计划业载"会让上游已经算好的
    /// 业载被无声覆盖。
    ///
    /// Paired: with `computedPayload` the right side is 6000, without it 5000. Always using the planned figure would silently override a payload upstream already computed.
    /// </remarks>
    [Fact]
    public void Payload_ShouldPreferTheComputedTargetOverThePlannedOne() {
        (Load load, Stowage stowage, List<Item> _, List<StowagePosition> positions) = Prepare();
        var payload = new Payload(positions, load, plannedPayload: 5000.0, maxPayload: 20000.0, computedPayload: 6000.0);
        using LinearMetaModel<Flt64> model = RegisterAll(load, stowage, payload, StowageMode.Predistribution);

        LinearInequalityConstraint<Flt64> row = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "predistribution_payload_conservation");
        row.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(6000.0, 1e-9);

        (Load plannedLoad, Stowage plannedStowage, List<Item> _, List<StowagePosition> plannedPositions) = Prepare();
        var plannedOnly = new Payload(plannedPositions, plannedLoad, 5000.0, 20000.0);
        using LinearMetaModel<Flt64> plannedModel = RegisterAll(plannedLoad, plannedStowage, plannedOnly, StowageMode.Predistribution);
        LinearInequalityConstraint<Flt64> plannedRow = plannedModel.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "predistribution_payload_conservation");
        plannedRow.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(5000.0, 1e-9);
    }

    /// <summary>
    /// **按谓词取装载量只算满足谓词的货物** / A predicate-scoped amount counts only the matching items.
    /// </summary>
    /// <remarks>
    /// 成对：`AOG` 谓词只含 `I2` 那一项，全部货物含两项。写成"总是全部"会与 <see cref="Load.LoadAmountSymbol"/> 完全一样,
    /// 而两者的名字不同，报告里会出现两个永远相等的数。
    ///
    /// Paired: the `AOG` predicate covers `I2` only while the all-items symbol covers both. Always taking everything makes it identical to
    /// <see cref="Load.LoadAmountSymbol"/> under a different name, and the report shows two numbers that are always equal.
    /// </remarks>
    [Fact]
    public void LoadAmountOf_ShouldCountOnlyMatchingItems() {
        (Load load, Stowage stowage, List<Item> _, List<StowagePosition> _) = Prepare();
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        LinearExpressionSymbol aog = load.LoadAmountOf(0, item => item.Contains(CargoCode.AOG));
        aog.Name.Should().Be("load_amount_of_P1");
        aog.Polynomial.Monomials.Should().ContainSingle();
        aog.Polynomial.Monomials[0].Symbol.Should().BeSameAs(stowage.StowageSymbol(1, 0),
            "I2 is the only AOG item");

        LinearExpressionSymbol all = load.LoadAmountOf(0, _ => true);
        all.Polynomial.Monomials.Should().HaveCount(2);
        all.Polynomial.Monomials.Select(monomial => monomial.Symbol)
            .Should().BeEquivalentTo(new[] { stowage.StowageSymbol(0, 0)!, stowage.StowageSymbol(1, 0)! });
    }

    /// <summary>
    /// **批量接口按舱位去重、顺序保留** / The batch API deduplicates positions and keeps order.
    /// </summary>
    /// <remarks>
    /// 成对：重复的舱位下标只落一个 key（两个符号值相同但对象不同，所以能查到重复是**字典按 key 覆盖**而不是某个守卫的结果），
    /// 且 `ItemIndicesOf` 保留调用方顺序与重复项——签名是 `List<Int>` 而不是 `Set`，转成集合会静默吞掉重复项，
    /// 而重复项在"同一件货算两次"这种用法里是有意的。
    ///
    /// Paired: a repeated position index lands on one key (two symbols would be equal in value but distinct objects, so a duplicate surviving would mean the dictionary does not
 /// replace by key), and `ItemIndicesOf` keeps the caller's order and duplicates — the implementation signature is `List<Int>`, not `Set`, and converting would silently drop
    /// duplicates that are deliberate in "count this item twice" usages.
    /// </remarks>
    [Fact]
    public void LoadAmountsOf_ShouldDeduplicatePositionsAndKeepItemOrder() {
        (Load load, Stowage stowage, List<Item> _, List<StowagePosition> _) = Prepare();
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        IReadOnlyDictionary<int, LinearExpressionSymbol> amounts =
            load.LoadAmountsOf(new[] { 0, 0, 1 }, new[] { 1, 0 });
        amounts.Should().HaveCount(2, "the repeated position index maps to one symbol");
        amounts[0].Name.Should().Be("load_amount_of_P1");
        amounts[1].Name.Should().Be("load_amount_of_P2");
        amounts[0].Polynomial.Monomials.Should().HaveCount(2, "two item indices, in the caller's order");
        amounts[1].Polynomial.Monomials.Should().HaveCount(2);
        // 两个舱位必须用**各自的** `stowage` 符号。都取第一个舱位时数量与名字都对得上，只有把符号本身比一遍才看得出。
        // The two positions must use **their own** `stowage` symbols: taking the first position for both matches in count and in name, and only comparing the symbols shows it.
        amounts[1].Polynomial.Monomials.Select(monomial => monomial.Symbol)
            .Should().NotBeEquivalentTo(amounts[0].Polynomial.Monomials.Select(monomial => monomial.Symbol),
                "each position scopes the amount to its own stowage variables");
        amounts[1].Polynomial.Monomials.Select(monomial => monomial.Symbol)
            .Should().BeEquivalentTo(new[] { stowage.StowageSymbol(1, 1)!, stowage.StowageSymbol(0, 1)! });

        IReadOnlyList<int> indices = load.ItemIndicesOf(item => item.Contains(CargoCode.AOG));
        indices.Should().Equal(new[] { 1 });
        load.ItemIndicesOf(_ => true).Should().Equal(new[] { 0, 1 });
    }

    /// <summary>
    /// **越界下标是错误，不是半份结果** / An out-of-range index is an error, not half a result.
    /// </summary>
    /// <remarks>
    /// 静默返回部分字典更坏：调用方拿到一个少了几个舱位的字典，看起来"这些舱位没货"，而真相是索引错了。
    ///
    /// Silently returning a partial dictionary is worse: the caller gets one missing some positions and reads it as "those positions are empty" when the real story is a wrong
    /// index.
    /// </remarks>
    [Fact]
    public void LoadAmountsOf_ShouldThrowOnAnOutOfRangeItemIndex() {
        (Load load, Stowage stowage, List<Item> _, List<StowagePosition> _) = Prepare();
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        System.Action act = () => load.LoadAmountsOf(new[] { 0 }, new[] { 0, 99 });
        act.Should().Throw<System.ArgumentOutOfRangeException>();
    }
}
