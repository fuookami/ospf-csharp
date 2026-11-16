// 条件包络的判据/ Criteria for the conditional envelope.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;
using ErrorCode = Fuookami.Ospf.Utils.Error.ErrorCode;
using Fuookami.Ospf.Utils.Error;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **条件包络** / The conditional envelope.
/// </summary>
/// <remarks>
/// 同一相位下两组边界，`valueCondition` 决定用哪一组：`true` → 第一组，`false` → 第二组，`null` → 条件是决策变量（需要 `IfThen` 开关）。
///
/// **两组写反不会报错**：两支都是合法包络，只是边界值不同，求解照样成功——所以判据必须让两组的边界值**明显不同**，
/// 才能分辨"取了第一组"与"取了第二组"。
///
/// Two pairs of bounds per phase with `valueCondition` picking one: `true` → the first, `false` → the second, `null` → the condition is a decision variable (needs the `IfThen` switch).
///
/// **Swapping the two pairs raises nothing**: both are legal envelopes with different values and the solve still succeeds — so the criteria must make the two pairs' bounds clearly
/// different, otherwise "took the first pair" and "took the second pair" are indistinguishable.
/// </remarks>
public class Demo2ConditionalEnvelopeTests {
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

    private static StowagePosition MakePosition(string name, ulong id, PositionLocation location) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(500.0, 500.0, 60.0, 60.0),
            shape: new PositionShape(50.0, 40.0, 50.0 * 40.0 * 60.0),
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

    /// <summary>
    /// 装配一次：两组边界值相差 100 以上，便于分辨取的是哪一组。
    /// </summary>
    /// <param name="computedTotalWeight">该相位已算出的总重，null 表示未算出 / The computed weight, null when unknown.</param>
    /// <returns>装配结果 / The fixture.</returns>
    private static (TotalWeight TotalWeight, LinearMetaModel<Flt64> Model) Prepare(
        IReadOnlyDictionary<FlightPhase, double>? computedTotalWeight) {
        var items = new List<Item> { MakeItem("I1") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, PositionLocation.NormalMainLocation),
            MakePosition("P2", 2UL, PositionLocation.LowForwardNotBulkLocation),
        };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        var payload = new Payload(positions, load, plannedPayload: 5000.0, maxPayload: 20000.0);
        var fuselage = new Fuselage(new Liferaft(50.0, 1.0), Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0);
        var fuel = new Dictionary<FlightPhase, FuelConstant> {
            [FlightPhase.TakeOff] = new FuelConstant(0.8, 8000.0, 5.0),
        };
        var totalWeight = new TotalWeight(fuselage, fuel, payload, computedTotalWeight: computedTotalWeight);

        var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();
        payload.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
        totalWeight.Register(model).IsOk.Should().BeTrue();

        // 第一组与第二组的边界值**故意拉开**：取错组时判据才有东西可比。
        // The two pairs' bounds are **deliberately far apart**, so a wrong branch has something to compare against.
        var lhs1 = new EnvelopeSide("lhs1", EnvelopeSideType.Left, new[] {
            new EnvelopePoint(40000.0, 10.0),
            new EnvelopePoint(60000.0, 20.0),
        });
        var rhs1 = new EnvelopeSide("rhs1", EnvelopeSideType.Right, new[] {
            new EnvelopePoint(40000.0, 50.0),
            new EnvelopePoint(60000.0, 70.0),
        });
        var lhs2 = new EnvelopeSide("lhs2", EnvelopeSideType.Left, new[] {
            new EnvelopePoint(40000.0, 110.0),
            new EnvelopePoint(60000.0, 120.0),
        });
        var rhs2 = new EnvelopeSide("rhs2", EnvelopeSideType.Right, new[] {
            new EnvelopePoint(40000.0, 150.0),
            new EnvelopePoint(60000.0, 170.0),
        });
        _ = lhs1; _ = rhs1; _ = lhs2; _ = rhs2;
        return (totalWeight, model);
    }

    /// <remarks>
    /// 两组边界的边界值**故意拉开**（第一组 10/50，第二组 110/150），取错组时判据才有东西可比。
    ///
    /// The two pairs' bounds are **deliberately far apart** (first pair 10/50, second 110/150) so a wrong branch has something to compare against.
    /// </remarks>
    private static ConditionalEnvelope Build(
        TotalWeight totalWeight,
        Func<bool?> valueCondition,
        Func<string, ILinearIntermediateSymbol<Flt64>>? symbolCondition = null) {
        var lhs1 = new EnvelopeSide("lhs1", EnvelopeSideType.Left, new[] {
            new EnvelopePoint(40000.0, 10.0),
            new EnvelopePoint(60000.0, 20.0),
        });
        var rhs1 = new EnvelopeSide("rhs1", EnvelopeSideType.Right, new[] {
            new EnvelopePoint(40000.0, 50.0),
            new EnvelopePoint(60000.0, 70.0),
        });
        var lhs2 = new EnvelopeSide("lhs2", EnvelopeSideType.Left, new[] {
            new EnvelopePoint(40000.0, 110.0),
            new EnvelopePoint(60000.0, 120.0),
        });
        var rhs2 = new EnvelopeSide("rhs2", EnvelopeSideType.Right, new[] {
            new EnvelopePoint(40000.0, 150.0),
            new EnvelopePoint(60000.0, 170.0),
        });
        return new ConditionalEnvelope(
            FlightPhase.TakeOff, "conditional", lhs1, rhs1, lhs2, rhs2,
            valueCondition: valueCondition, totalWeight: totalWeight, symbolCondition: symbolCondition);
    }

    /// <summary>
    /// **两组的差异被认出来：真取第一组、假取第二组** / The two pairs are told apart: true takes the first, false the second.
    /// </summary>
    /// <remarks>
    /// 四点成对：`true` → 最小值是 **10**、最大值是 **50** / `false` → 最小值是 **110**、最大值是 **150** /
    /// 两支的名字都带 `min_index_` / `max_index_` 前缀。
    /// 只测一支会让"永远取第一组"的实现通过——**那正好是这一层最容易写错的地方**。
    ///
    /// Four paired points: `true` gives **10** and **50**; `false` gives **110** and **150**; and both branches name their symbols with the `min_index_` / `max_index_` prefixes.
    /// Testing only one branch would let "always takes the first pair" pass — **the easiest mistake in this layer**.
    /// </remarks>
    [Fact]
    public void Register_ShouldFollowTheDecidedCondition() {
        var computed = new Dictionary<FlightPhase, double> { [FlightPhase.TakeOff] = 50000.0 };

        foreach ((bool condition, double minimum, double maximum) in new[] { (true, 15.0, 60.0), (false, 115.0, 160.0) }) {
            (TotalWeight totalWeight, LinearMetaModel<Flt64> model) = Prepare(computed);
            using LinearMetaModel<Flt64> unused = model;
            ConditionalEnvelope envelope = Build(totalWeight, () => condition);

            envelope.Register(model).IsOk.Should().BeTrue();

            envelope.MinIndexSymbol.Should().NotBeNull();
            envelope.MaxIndexSymbol.Should().NotBeNull();
            envelope.MinIndexSymbol!.Name.Should().StartWith("min_index_");
            envelope.MaxIndexSymbol!.Name.Should().StartWith("max_index_");
            ((LinearExpressionSymbol)envelope.MinIndexSymbol!).Polynomial.Constant.ToDouble().Should().Be(minimum,
                condition ? "the first left bound at 50000 kg is 15" : "the second left bound at 50000 kg is 115");
            ((LinearExpressionSymbol)envelope.MaxIndexSymbol!).Polynomial.Constant.ToDouble().Should().Be(maximum,
                condition ? "the first right bound at 50000 kg is 60" : "the second right bound at 50000 kg is 160");
        }
    }

    /// <summary>
    /// **条件未定：开关拼出来，两支都在模型里** / An undecided condition synthesises the switch and both pairs reach the model.
    /// </summary>
    /// <remarks>
    /// 四点成对：注册成功 / 两个开关符号 `_min_switch` / `_max_switch` 都在 / **`min = 第二组 + 开关·(第一组 − 第二组)`**
    /// （常数项含第二组的值，系数里既有一也能找到第一组与第二组的差）/ `min_index_` / `max_index_` 名字照旧。
    /// 开关写反（`第二组 − 第一组`）不会报错，**只有"第二组是基准、第一组是差值"这个结构能分辨**。
    ///
    /// Four paired points: registration succeeds; both switch symbols (`_min_switch`, `_max_switch`) are present; **`min = second + switch·(first − second)`** (the constant carries the
    /// second pair's value and the coefficients hold both the switch and the difference); and the `min_index_` / `max_index_` names are unchanged. Reversing the difference raises nothing —
    /// **only the shape "the second pair is the base and the first is the difference" tells**.
    /// </remarks>
    [Fact]
    public void Register_ShouldSynthesiseTheSwitchWhenTheConditionIsUndecided() {
        var computed = new Dictionary<FlightPhase, double> { [FlightPhase.TakeOff] = 50000.0 };
        (TotalWeight totalWeight, LinearMetaModel<Flt64> model) = Prepare(computed);
        using LinearMetaModel<Flt64> unused = model;
        ConditionalEnvelope envelope = Build(
            totalWeight,
            valueCondition: () => null,
            symbolCondition: name => new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: Flt64.One),
                name: name));

        envelope.Register(model).IsOk.Should().BeTrue();

        envelope.MinIndexSymbol.Should().NotBeNull();
        envelope.MaxIndexSymbol.Should().NotBeNull();
        envelope.MinIndexSymbol!.Name.Should().Be("min_index_conditional_takeoff");
        envelope.MaxIndexSymbol!.Name.Should().Be("max_index_conditional_takeoff");
        string symbolNames = string.Join(",", model.Tokens.Symbols.Select(symbol => symbol.Name));
        symbolNames.Should().Contain("conditional_takeoff_min_switch");
        symbolNames.Should().Contain("conditional_takeoff_max_switch");

        var minimum = (LinearExpressionSymbol)envelope.MinIndexSymbol!;
        minimum.Polynomial.Constant.ToDouble().Should().Be(115.0 + 0.0, "the base is the second pair's left bound at 50000 kg");
        minimum.Polynomial.Monomials.Should().NotBeEmpty("the switch and the difference have to show up as coefficients");
        // **开关的"真"分支必须是 `第一组 − 第二组`**：常数项 −100（= 15 − 115）。写反成 +100 不影响可解性，
        // 只在取到第一组时把边界值翻倍——**只有这一项能分辨**。
        // **The switch's "true" branch must be `first − second`**: constant −100 (= 15 − 115). Reversing it does not hurt solvability and merely doubles the bound when the first pair is
        // taken — **only this term tells**.
        IfThenFunction<Flt64>? minSwitch = model.Tokens.Symbols
            .OfType<LinearFunctionSymbolAdapter<Flt64>>()
            .Select(adapter => adapter.Function)
            .OfType<IfThenFunction<Flt64>>()
            .FirstOrDefault(function => (function.Name ?? "").EndsWith("_min_switch"));
        minSwitch.Should().NotBeNull("the min switch has to be in the model");
        minSwitch!.Then.Constant.ToDouble().Should().Be(-100.0, "the true branch is the difference between the first and second pairs");
        minSwitch.ConditionBounds.LowerBound.ToDouble().Should().Be(0.0);
        minSwitch.ConditionBounds.UpperBound.ToDouble().Should().Be(1.0);
        var maximum = (LinearExpressionSymbol)envelope.MaxIndexSymbol!;
        maximum.Polynomial.Constant.ToDouble().Should().Be(160.0 + 0.0, "the base is the second pair's right bound at 50000 kg");
    }

    /// <summary>
    /// **条件未定又没给条件符号时明确失败** / An undecided condition with no symbol fails loudly.
    /// </summary>
    /// <remarks>
    /// 两点成对：返回失败 / **消息点名"没给条件符号"**（说明缺的是输入而不是实现）。
    /// 悄悄退回某一组会让"条件没接上"看起来像"条件取了这个值"——**后者是业务结论，前者是漏接**。
    ///
    /// Two paired points: it returns a failure; and the message names the missing condition symbol (the input is missing, not the implementation). A quiet fallback to one pair would make
    /// "the condition was never wired" look like "the condition took this value" — **the latter is a business conclusion, the former a missing wire**.
    /// </remarks>
    [Fact]
    public void Register_ShouldFailWhenNoConditionSymbolIsGiven() {
        var computed = new Dictionary<FlightPhase, double> { [FlightPhase.TakeOff] = 50000.0 };
        (TotalWeight totalWeight, LinearMetaModel<Flt64> model) = Prepare(computed);
        using LinearMetaModel<Flt64> unused = model;
        ConditionalEnvelope envelope = Build(totalWeight, valueCondition: () => null);

        Try result = envelope.Register(model);

        result.IsOk.Should().BeFalse("an undecided condition without a condition symbol cannot be modelled");
        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Message.Should().Contain("条件符号");
        envelope.MinIndexSymbol.Should().BeNull();
        envelope.MaxIndexSymbol.Should().BeNull();
    }

    /// <summary>
    /// **总重未算出时两支都是分段符号** / Without a computed weight both pairs become piecewise symbols.
    /// </summary>
    /// <remarks>
    /// 三点成对：`true` 与 `false` 都成功 / 两个符号都是 `LinearFunctionSymbolAdapter` / **名字被换成 `min_index_` / `max_index_`**（分段符号自带的是 `名字_方向`）。
    /// 名字不换的话，下游按 `min_index_` 取符号全部落空——**而那只会在报告里以"没有边界"的形式出现**。
    ///
    /// Three paired points: both `true` and `false` succeed; both symbols are `LinearFunctionSymbolAdapter`s; and **the names are replaced by `min_index_` / `max_index_`** (a piecewise
    /// symbol carries `name_side` of its own). Without the rename, downstream lookups by `min_index_` all miss — **and that only shows up in the report as "no bound"**.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildPiecewiseSymbolsWhenTheWeightIsUnknown() {
        foreach (bool condition in new[] { true, false }) {
            (TotalWeight totalWeight, LinearMetaModel<Flt64> model) = Prepare(computedTotalWeight: null);
            using LinearMetaModel<Flt64> unused = model;
            ConditionalEnvelope envelope = Build(totalWeight, () => condition);

            envelope.Register(model).IsOk.Should().BeTrue();

            envelope.MinIndexSymbol.Should().BeOfType<LinearFunctionSymbolAdapter<Flt64>>();
            envelope.MaxIndexSymbol.Should().BeOfType<LinearFunctionSymbolAdapter<Flt64>>();
            envelope.MinIndexSymbol!.Name.Should().Be("min_index_conditional_takeoff");
            envelope.MaxIndexSymbol!.Name.Should().Be("max_index_conditional_takeoff");
        }
    }

}
