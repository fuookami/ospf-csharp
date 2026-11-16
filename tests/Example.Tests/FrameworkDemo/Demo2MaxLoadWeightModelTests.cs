// 最大装载重量模型本体的判据 / Criteria for the maximum load weight model itself.

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
/// **最大装载重量模型本体** / The maximum load weight model itself.
/// </summary>
/// <remarks>
/// 的三支判断是这里的焦点：
///
/// | 分段规格 | 已知零油重 | 取值 |
/// | --- | --- | --- |
/// | 无 | — | 常数 `mlw` |
/// | 有 | 有 | `mlw(zfw)`——零油重落在哪段取哪段 |
/// | 有 | **无** | 各段**最大值** `mlw` |
///
/// 第三支最容易被"顺手合并"进第二支（"没有零油重就当作第一段"）。那等于凭空假设一个装载状态；而"取各段最大值"至少
/// 在零油重真值偏轻时是对的——它是**最宽松**的规格。
///
/// The three branches are the focus: no segments → the constant; segments with a known zero-fuel weight → `mlw(zfw)`; segments with **no** known weight → the maximum across
/// segments. The third invites merging into the second ("no weight, so assume the first segment"), which invents a loading state; the maximum is at least correct when the true
/// weight is light, since it is the **loosest** specification.
/// </remarks>
public class Demo2MaxLoadWeightModelTests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static Item MakeItem(string name) =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromCode(CargoCode.BAL),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);

    private static StowagePosition MakePosition(string name, ulong id, MaxLoadWeightLimit mlw) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(500.0, 500.0, 60.0, 60.0),
            shape: new PositionShape(1.0, 1.0, 1.0),
            location: PositionLocation.NormalMainLocation);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(true, false, true, true),
            mlw: mlw,
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

    private static MaxLoadWeightLimit Piecewise(params (double Mlw, double MinZfw, double? MaxZfw)[] segments) {
        var list = new List<MaxLoadWeightLimit.Segment>();
        foreach ((double mlw, double minZfw, double? maxZfw) in segments) {
            list.Add(new MaxLoadWeightLimit.Segment(mlw, minZfw, maxZfw));
        }
        return new MaxLoadWeightLimit(list);
    }

    private static (MaxLoadWeight Model, LinearMetaModel<Flt64> Meta, TotalWeight TotalWeight) Register(
        MaxLoadWeightLimit limit,
        IReadOnlyDictionary<FlightPhase, double>? computedTotalWeight) {
        var items = new List<Item> { MakeItem("I1") };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, limit) };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        var payload = new Payload(positions, load, 5000.0, 20000.0);
        var fuselage = new Fuselage(null, 40000.0, 10.0, 900.0);
        var totalWeight = new TotalWeight(
            fuselage,
            new Dictionary<FlightPhase, FuelConstant>(),
            payload,
            computedTotalWeight: computedTotalWeight);

        var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();
        payload.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
        totalWeight.Register(model).IsOk.Should().BeTrue();

        var maxLoadWeight = new MaxLoadWeight(positions, totalWeight);
        maxLoadWeight.Register(model).IsOk.Should().BeTrue();
        return (maxLoadWeight, model, totalWeight);
    }

    /// <summary>
    /// **已知零油重时取那一段** / A known zero-fuel weight selects its segment.
    /// </summary>
    /// <remarks>
    /// 成对：同一个分段规格，零油重 60 落在第二段（800），零油重 450 落在第三段（400）。只测一个点会让"永远取第一段"
    /// 或"永远取最大值"通过。
    ///
    /// Paired: the same specification gives 800 at a zero-fuel weight of 60 and 400 at 450. Testing one point would pass "always the first segment" or "always the maximum".
    /// </remarks>
    [Fact]
    public void MaxLoadWeight_ShouldSelectTheSegmentByZeroFuelWeight() {
        MaxLoadWeightLimit limit = Piecewise(
            (600.0, 0.0, 50.0),
            (800.0, 50.0, 100.0),
            (400.0, 100.0, 500.0));

        (MaxLoadWeight light, LinearMetaModel<Flt64> _, TotalWeight _) = Register(
            limit, new Dictionary<FlightPhase, double> { [FlightPhase.ZeroFuel] = 60.0 });
        light.MaxLoadWeightSymbol(0)!.Polynomial.Constant.ToDouble().Should().BeApproximately(800.0, 1e-9);

        (MaxLoadWeight heavy, LinearMetaModel<Flt64> _, TotalWeight _) = Register(
            limit, new Dictionary<FlightPhase, double> { [FlightPhase.ZeroFuel] = 450.0 });
        heavy.MaxLoadWeightSymbol(0)!.Polynomial.Constant.ToDouble().Should().BeApproximately(400.0, 1e-9,
            "the heavier zero-fuel weight selects the tighter segment");
    }

    /// <summary>
    /// **没有零油重时取各段最大值，不是第一段** / Without a zero-fuel weight it takes the maximum, not the first segment.
    /// </summary>
    /// <remarks>
    /// 这是这里最容易写错的一支。成对断言：最大值在**中间**（800），第一段是 600、最后一段是 400，三个数互不相同，
    /// 所以"取最大值""取第一段""取最后一段"三种写法能被区分开。
    ///
    /// This is the branch most likely to be written wrong. Paired: the maximum sits **in the middle** (800) while the first segment is 600 and the last is 400 — three distinct
    /// numbers, so "take the maximum", "take the first" and "take the last" are distinguishable.
    /// </remarks>
    [Fact]
    public void MaxLoadWeight_ShouldFallBackToTheMaximumWithoutAZeroFuelWeight() {
        MaxLoadWeightLimit limit = Piecewise(
            (600.0, 0.0, 50.0),
            (800.0, 50.0, 100.0),
            (400.0, 100.0, 500.0));

        (MaxLoadWeight model, LinearMetaModel<Flt64> _, TotalWeight _) = Register(limit, computedTotalWeight: null);

        double effective = model.MaxLoadWeightSymbol(0)!.Polynomial.Constant.ToDouble();
        effective.Should().BeApproximately(800.0, 1e-9, "the fallback is the maximum across segments");
        effective.Should().NotBeApproximately(600.0, 1e-9, "not the first segment");
        effective.Should().NotBeApproximately(400.0, 1e-9, "not the last segment");
        effective.Should().BeApproximately(model.EffectiveMlw(0), 1e-9,
            "the registered symbol carries exactly the effective value");
    }

    /// <summary>
    /// **没有分段规格时就是那个常数** / With no segments it is the constant.
    /// </summary>
    /// <remarks>
    /// 与"有分段但没有零油重"成对，两者都退到 `limit.Mlw`，但原因不同：这一支本来就只有一个数，那一支是**选不出来**才退到最大值。
    /// 把两支写成一支时，`limit.Mlw` 恰好也是"各段最大值"，所以数值上无法区分——但语义与将来的改动路径不同，注释里已分开写。
    ///
    /// Paired with the "segments but no zero-fuel weight" case: both fall back to `limit.Mlw`, for different reasons — this one only ever had one number, the other could not
    /// choose. Collapsing them is numerically invisible (the maximum across segments *is* `limit.Mlw`), so the comments keep the two reasons apart.
    /// </remarks>
    [Fact]
    public void MaxLoadWeight_ShouldUseTheConstantWithoutSegments() {
        (MaxLoadWeight model, LinearMetaModel<Flt64> _, TotalWeight _) = Register(
            new MaxLoadWeightLimit(750.0),
            new Dictionary<FlightPhase, double> { [FlightPhase.ZeroFuel] = 60.0 });

        model.MaxLoadWeightSymbol(0)!.Polynomial.Constant.ToDouble().Should().BeApproximately(750.0, 1e-9,
            "a zero-fuel weight does not change a constant specification");
        model.EffectiveMlw(0).Should().BeApproximately(750.0, 1e-9);
    }

    /// <summary>
    /// **符号是常数，没有单项式** / The symbol is a constant with no monomials.
    /// </summary>
    /// <remarks>
    /// 三种取值都是**数值**（`position.mlw(zfw)` 返回 `Quantity`），不是关于决策变量的表达式。把它写成含单项式的
    /// 符号会让"最大装载重量"变成可优化的量——那与"规格"是两件事，而求解器会很乐意把规格调到对自己有利的位置。
    ///
    /// All three values are **numbers** (`position.mlw(zfw)` returns a `Quantity`), not expressions over decision variables. Making it carry monomials would turn a
    /// specification into something the solver may optimise, and the solver would happily move the spec wherever it likes.
    /// </remarks>
    [Fact]
    public void MaxLoadWeight_ShouldBeAConstantSymbol() {
        (MaxLoadWeight model, LinearMetaModel<Flt64> _, TotalWeight _) = Register(
            Piecewise((600.0, 0.0, 50.0), (800.0, 50.0, null)),
            new Dictionary<FlightPhase, double> { [FlightPhase.ZeroFuel] = 60.0 });

        LinearExpressionSymbol symbol = model.MaxLoadWeightSymbol(0)!;
        symbol.Name.Should().Be("max_load_weight_P1");
        symbol.Polynomial.Monomials.Should().BeEmpty("the maximum load weight is a specification, not a decision");
    }

    /// <summary>
    /// **零油重只认已计算总重，不算符号** / The zero-fuel weight comes only from the computed total, not from a symbolic one.
    /// </summary>
    /// <remarks>
    /// 分段的取值是**查表**，需要的是一个数；`estimate_total_weight_zerofuel` 是决策变量的表达式，代入分段规格会得到一个非线性表达式。
    /// 的是 `totalWeight.computedTotalWeight[FlightPhase.ZeroFuel]`——一个 `Quantity?`，不是符号。这条判据钉住"用的是哪个来源"：
    /// 把 `ComputedTotalWeight` 换成"从符号里取常数"就会在这一支里拿到零，从而永远走"取最大值"。
    ///
    /// Segment lookup needs a **number**; `estimate_total_weight_zerofuel` is an expression over decisions, and substituting it into the piecewise spec yields a nonlinear
 /// expression. This implementation reads `totalWeight.computedTotalWeight[FlightPhase.ZeroFuel]` — a `Quantity?`, not a symbol. This criterion pins the source: swapping it for
    /// "the constant of the symbol" would read zero here and always take the maximum.
    /// </remarks>
    [Fact]
    public void MaxLoadWeight_ShouldReadTheComputedZeroFuelWeightOnly() {
        MaxLoadWeightLimit limit = Piecewise(
            (600.0, 0.0, 50.0),
            (800.0, 50.0, 100.0),
            (400.0, 100.0, 500.0));

        (MaxLoadWeight model, LinearMetaModel<Flt64> _, TotalWeight totalWeight) = Register(
            limit, new Dictionary<FlightPhase, double> { [FlightPhase.ZeroFuel] = 450.0 });

        totalWeight.ComputedTotalWeight.Should().ContainKey(FlightPhase.ZeroFuel);
        totalWeight.EstimateTotalWeight(FlightPhase.ZeroFuel)!.Polynomial.Constant.ToDouble()
            .Should().BeApproximately(450.0, 1e-9);
        model.EffectiveMlw(0).Should().BeApproximately(400.0, 1e-9,
            "450 falls in the third segment, so the answer differs from the fallback 800");
    }
}
