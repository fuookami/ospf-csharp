// 线性密度上限的判据 / Criteria for the linear density cap.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **线性密度上限（B591）** / The linear density cap (B591).
/// </summary>
/// <remarks>
/// 与表面密度上限的区别不只是"面积换长度"，还有**求和的范围**：表面密度是**逐舱位各一行**，线性密度是**一条横截面一行**，
/// 而一条线通常覆盖多个舱位。所以这里的左端是**多舱位之和**——漏掉一个舱位，那条横截面就少算一截重量，而约束看起来依然成立。
///
/// 三处细节：整条线全部不可用才跳过（不是逐舱位过滤）、名字带力臂数值、左端符号与线上舱位一一对应。
///
/// The difference from the surface cap is not just area-versus-length but **what is summed**: surface density is **one row per position**, linear density **one row per
/// cross-section**, and a line usually covers several positions — dropping one understates the weight there while the constraint still looks satisfied. Three details: skip only when
/// the whole line is unavailable (not per position), the name carries the arm value, and the left side matches the line's positions one for one.
/// </remarks>
public class Demo2LinearDensityLimitTests {
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

    private static Position MakePosition(string name, ulong id, double frontArm, double backArm, double length = 50.0) =>
        new(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(frontArm, backArm, 0.0, 100.0),
            shape: new PositionShape(length, 40.0, length * 40.0 * 60.0),
            location: PositionLocation.NormalMainLocation);

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

    private static (LinearDensity LinearDensity, List<StowagePosition> Positions, LinearMetaModel<Flt64> Model)
        Prepare(IReadOnlyList<bool> available, IReadOnlyList<LinearDensityLimitLine> lines, params LinearDensityLimitZone[] zones) {
        var aircraftPositions = new List<Position> {
            MakePosition("A", 1UL, 0.0, 100.0),
            MakePosition("B", 2UL, 100.0, 200.0),
            MakePosition("C", 3UL, 200.0, 300.0),
        };
        var stowagePositions = new List<StowagePosition>();
        for (int j = 0; j < aircraftPositions.Count; ++j) {
            stowagePositions.Add(new StowagePosition(
                aircraftPosition: aircraftPositions[j],
                status: new PositionStatus(available[j], false, true, true),
                mlw: new MaxLoadWeightLimit(1000.0),
                mla: 3UL,
                plw: new PositionPredicateLoadWeight(350.0, 300.0, 400.0)));
        }
        var items = new List<Item> { MakeItem("I1") };
        var stowage = new Stowage(items, stowagePositions) { LoadingContext = MakeKernel(items, stowagePositions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, stowagePositions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        var linearDensity = new LinearDensity(zones, lines, aircraftPositions, load);

        var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();
        linearDensity.Register(model).IsOk.Should().BeTrue();
        return (linearDensity, stowagePositions, model);
    }

    private static LinearDensityLimitZone Zone(string name, double max) =>
        new(name, new HashSet<DeckLocation> { DeckLocation.Main }, 0.0, 300.0, max);

    /// <summary>
    /// **左端是线上所有舱位之和** / The left side sums every position on the line.
    /// </summary>
    /// <remarks>
    /// 一条线覆盖 A、B 两个舱位，左端必须含**两个**密度符号。只取第一个时约束依然成立，只是那条横截面少算了一半重量——
    /// 这正是"一条线一行"这种写法最容易出错的地方。
    ///
    /// A line covers A and B, so the left side must hold **both** density symbols. Taking only the first leaves the constraint satisfiable while that cross-section understates half
    /// its weight — exactly where the one-row-per-line shape goes wrong.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldSumEveryPositionOnTheLine() {
        LinearDensityLimitZone zone = Zone("Z1", 300.0);
        LinearDensityLimitLine[] lines = new[] {
            new LinearDensityLimitLine(zone, 100.0, new[] { 0, 1 }),
        };
        (LinearDensity linearDensity, List<StowagePosition> positions, LinearMetaModel<Flt64> model) =
            Prepare(new[] { true, true, true }, lines, zone);

        var limit = new LinearDensityLimit(linearDensity, positions);
        limit.Invoke(model).IsOk.Should().BeTrue();

        LinearInequalityConstraint<Flt64> row = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "linear_density_limit_Z1_100");
        row.Inequality.Comparison.Should().Be(Comparison.LE);
        row.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(300.0, 1e-9);

        var expected = new List<Fuookami.Ospf.Math.Symbol.ISymbol>();
        expected.AddRange(linearDensity.LinearDensitySymbol(0)!.Polynomial.Monomials.Select(monomial => monomial.Symbol));
        expected.AddRange(linearDensity.LinearDensitySymbol(1)!.Polynomial.Monomials.Select(monomial => monomial.Symbol));
        row.Inequality.Lhs.Monomials.Select(monomial => monomial.Symbol)
            .Should().BeEquivalentTo(expected, "both positions on the line contribute");
        row.Inequality.Lhs.Monomials.Select(monomial => monomial.Symbol)
            .Should().Contain(linearDensity.LinearDensitySymbol(1)!.Polynomial.Monomials[0].Symbol,
                "B is the second position on the line and must not be dropped");
    }

    /// <summary>
    /// **整条线全部不可用才跳过，不是逐舱位过滤** / Skip only when the whole line is unavailable, not per position.
    /// </summary>
    /// <remarks>
    /// 两个成对的例子：一条线 {A,B} 其中 B 不可用 → 约束**照加**，且 A、B 的密度都在左端；另一条线只有一个不可用舱位 → **不加**。
    /// 把判断写成逐舱位过滤会悄悄改变左端的构成；写成"任一不可用就跳过"会把整条横截面一起丢掉。两种都错，方向相反。
    ///
    /// Two paired cases: a line {A, B} where B is unavailable **still gets its row**, with both A's and B's density on the left; a line holding only an unavailable position gets
    /// **no row**. Filtering per position silently changes what the left side sums; skipping as soon as any position is unavailable throws the whole cross-section away. Both wrong,
    /// in opposite directions.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldSkipOnlyWhenTheWholeLineIsUnavailable() {
        LinearDensityLimitZone zone = Zone("Z1", 300.0);
        LinearDensityLimitLine[] lines = new[] {
            new LinearDensityLimitLine(zone, 100.0, new[] { 0, 1 }),
            new LinearDensityLimitLine(zone, 200.0, new[] { 2 }),
        };
        (LinearDensity linearDensity, List<StowagePosition> positions, LinearMetaModel<Flt64> model) =
            Prepare(new[] { true, false, false }, lines, zone);

        var limit = new LinearDensityLimit(linearDensity, positions);
        limit.Invoke(model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().Equal(new[] { "linear_density_limit_Z1_100" },
            "the mixed line stays, the all-unavailable line goes");
        LinearInequalityConstraint<Flt64> row = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "linear_density_limit_Z1_100");
        row.Inequality.Lhs.Monomials.Select(monomial => monomial.Symbol)
            .Should().Contain(linearDensity.LinearDensitySymbol(1)!.Polynomial.Monomials[0].Symbol,
                "the unavailable position's density still joins the sum, as in the baseline");
        model.Constraints.Select(constraint => constraint.Name)
            .Should().NotContain("linear_density_limit_Z1_200");
    }

    /// <summary>
    /// **名字带力臂数值** / The name carries the arm value.
    /// </summary>
    /// <remarks>
    /// 同一区域在多个力臂上各有一条线。名字里没有力臂时三条约束同名，求解器报"第 2 条约束不可行"就无法定位到那条横截面。
    ///
    /// One zone has a line at each of several arms. Without the arm in the name all three rows share one name, and "constraint 2 is infeasible" cannot be traced to a
    /// cross-section.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldPutTheArmInTheConstraintName() {
        LinearDensityLimitZone zone = Zone("Z1", 300.0);
        LinearDensityLimitLine[] lines = new[] {
            new LinearDensityLimitLine(zone, 0.0, new[] { 0 }),
            new LinearDensityLimitLine(zone, 100.0, new[] { 0, 1 }),
            new LinearDensityLimitLine(zone, 200.0, new[] { 1, 2 }),
        };
        (LinearDensity linearDensity, List<StowagePosition> positions, LinearMetaModel<Flt64> model) =
            Prepare(new[] { true, true, true }, lines, zone);

        var limit = new LinearDensityLimit(linearDensity, positions);
        limit.Invoke(model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().BeEquivalentTo(new[] {
            "linear_density_limit_Z1_0",
            "linear_density_limit_Z1_100",
            "linear_density_limit_Z1_200",
        });
        limit.LastAddedNames.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// **线上舱位缺符号时明确失败** / A missing symbol on the line fails loudly.
    /// </summary>
    /// <remarks>
    /// 静默跳过会让那条横截面少算一截重量，而约束照样"成立"——最坏的失败方式。空壳版本对任何输入都返回 `Ok`。
    ///
    /// Skipping silently understates that cross-section while the constraint still "holds" — the worst failure mode. The shell returned `Ok` for any input.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailWhenASymbolIsMissing() {
        LinearDensityLimitZone zone = Zone("Z1", 300.0);
        LinearDensityLimitLine[] lines = new[] {
            new LinearDensityLimitLine(zone, 100.0, new[] { 0, 1 }),
        };
        // 不注册线性密度符号：直接从 `Load` 起一套最小模型。
        var aircraftPositions = new List<Position> {
            MakePosition("A", 1UL, 0.0, 100.0),
            MakePosition("B", 2UL, 100.0, 200.0),
        };
        var stowagePositions = aircraftPositions
            .Select(position => new StowagePosition(
                aircraftPosition: position,
                status: new PositionStatus(true, false, true, true),
                mlw: new MaxLoadWeightLimit(1000.0),
                mla: 3UL,
                plw: new PositionPredicateLoadWeight(350.0, 300.0, 400.0)))
            .ToList();
        var items = new List<Item> { MakeItem("I1") };
        var stowage = new Stowage(items, stowagePositions) { LoadingContext = MakeKernel(items, stowagePositions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, stowagePositions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        var linearDensity = new LinearDensity(new[] { zone }, lines, aircraftPositions, load);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        var limit = new LinearDensityLimit(linearDensity, stowagePositions);
        limit.Invoke(model).IsOk.Should().BeFalse("the linear density symbols were never registered");
        limit.LastAddedNames.Should().BeEmpty();
    }

    /// <summary>
    /// **每条线恰好一行** / Exactly one row per line.
    /// </summary>
    /// <remarks>
    /// 与"左端求和正确"成对：三条线三行，不是"每个舱位一行"也不是"每个区域一行"。行数错了时单看某一行仍然合理，
    /// 只有数一遍才发现。
    ///
    /// Paired with "the left side sums correctly": three lines mean three rows — neither one per position nor one per zone. With the wrong count each individual row still looks
    /// plausible; only counting reveals it.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldEmitOneRowPerLine() {
        LinearDensityLimitZone zone = Zone("Z1", 300.0);
        LinearDensityLimitZone second = new("Z2", new HashSet<DeckLocation> { DeckLocation.Main }, 100.0, 300.0, 250.0);
        LinearDensityLimitLine[] lines = new[] {
            new LinearDensityLimitLine(zone, 0.0, new[] { 0 }),
            new LinearDensityLimitLine(zone, 100.0, new[] { 0, 1 }),
            new LinearDensityLimitLine(second, 200.0, new[] { 1, 2 }),
        };
        (LinearDensity linearDensity, List<StowagePosition> positions, LinearMetaModel<Flt64> model) =
            Prepare(new[] { true, true, true }, lines, zone, second);

        var limit = new LinearDensityLimit(linearDensity, positions);
        limit.Invoke(model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().HaveCount(3);
        model.Constraints
            .Where(constraint => constraint.Name.StartsWith("linear_density_limit_"))
            .Should().HaveCount(3);
        LinearInequalityConstraint<Flt64> secondRow = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "linear_density_limit_Z2_200");
        secondRow.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(250.0, 1e-9,
            "each line uses its own zone's limit");
    }
}
