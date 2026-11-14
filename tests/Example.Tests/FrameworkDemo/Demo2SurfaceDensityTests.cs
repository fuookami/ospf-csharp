// 表面密度的判据 / Criteria for surface density.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **表面密度（B589）** / Surface density (B589).
/// </summary>
/// <remarks>
/// 表面密度 = 舱位装载重量 / 舱位面积，是**按面积算重量**的规格。三处容易做错：
///
/// | 处 | 做错了会怎样 |
/// | --- | --- |
/// | 面积来源 | 用长×宽代替申报面积：可用面积与"长×宽"经常差很多（切角、导轨、锁扣占位），面积错一点重量错一片 |
/// | 只注册限区内舱位 | 不在任何限区的舱位也得到一个"表面密度"符号——没有规格可对照，白白进模型 |
/// | "有交集"不是"完整落在区间内" | 用 `Between` 会漏掉所有压在区域边界上的舱位，而那恰恰是最该查的一批 |
///
/// Surface density is **weight per area**, a specification about how much weight sits on a unit of deck area. The three mistakes are listed above: the area source (a declared area
/// often differs a lot from length × width), registering positions outside every zone (a symbol with no specification to compare against), and "intersects" versus "lies entirely
/// within" (the latter drops every position straddling a zone boundary — exactly the ones that most need checking).
/// </remarks>
public class Demo2SurfaceDensityTests {
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

    private static Position MakePosition(
        string name,
        ulong id,
        double frontArm,
        double backArm,
        double length,
        double width,
        double? declaredArea = null) =>
        new(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(frontArm, backArm, 0.0, 100.0),
            shape: new PositionShape(length, width, length * width * 60.0, declaredArea),
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

    private static (SurfaceDensity SurfaceDensity, Load Load, LinearMetaModel<Flt64> Model, List<Position> Positions) Prepare(
        IReadOnlyList<SurfaceDensityLimitZone> zones,
        IReadOnlyList<Position>? positions = null) {
        List<Position> aircraftPositions = positions?.ToList() ?? new List<Position> {
            MakePosition("P1", 1UL, 0.0, 100.0, 50.0, 40.0),
            MakePosition("P2", 2UL, 200.0, 300.0, 50.0, 40.0),
        };
        var items = new List<Item> { MakeItem("I1") };
        var stowagePositions = aircraftPositions
            .Select(position => new StowagePosition(
                aircraftPosition: position,
                status: new PositionStatus(true, false, true, true),
                mlw: new MaxLoadWeightLimit(1000.0),
                mla: 3UL,
                plw: new PositionPredicateLoadWeight(350.0, 300.0, 400.0)))
            .ToList();
        var stowage = new Stowage(items, stowagePositions) { LoadingContext = MakeKernel(items, stowagePositions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, stowagePositions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        var surfaceDensity = new SurfaceDensity(zones, aircraftPositions, load);

        var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();
        return (surfaceDensity, load, model, aircraftPositions);
    }

    /// <summary>
    /// **系数是 1 / 面积** / The coefficient is 1 / area.
    /// </summary>
    /// <remarks>
    /// P1 面积 50 × 40 = 2000，所以系数是 0.0005。断言每个单项式系数都是对应推荐重量系数的 0.0005 倍，且常数项同样缩放——
    /// 只缩放单项式会漏掉推荐重量里的常数部分。
    ///
    /// P1 measures 50 × 40 = 2000, so the coefficient is 0.0005. Every monomial coefficient must be 0.0005 times the matching recommended-weight coefficient, and the constant must
    /// scale too — scaling only the monomials drops the constant part of the recommended weight.
    /// </remarks>
    [Fact]
    public void Register_ShouldScaleByTheInverseOfTheArea() {
        SurfaceDensityLimitZone[] zones = new[] { new SurfaceDensityLimitZone("Z1", 0.0, 100.0, 500.0) };
        (SurfaceDensity surfaceDensity, Load load, LinearMetaModel<Flt64> model, List<Position> _) = Prepare(zones);
        surfaceDensity.Register(model).IsOk.Should().BeTrue();

        LinearExpressionSymbol symbol = surfaceDensity.SurfaceDensitySymbol(0)!;
        symbol.Name.Should().Be("surface_density_P1");
        LinearExpressionSymbol source = load.EstimateLoadWeightSymbol(0)!;
        double coefficient = 1.0 / 2000.0;
        symbol.Polynomial.Monomials.Should().HaveCount(source.Polynomial.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in symbol.Polynomial.Monomials) {
            LinearMonomial<Flt64> matching = source.Polynomial.Monomials
                .Single(candidate => ReferenceEquals(candidate.Symbol, monomial.Symbol));
            monomial.Coefficient.ToDouble().Should().BeApproximately(coefficient * matching.Coefficient.ToDouble(), 1e-12);
        }
        symbol.Polynomial.Constant.ToDouble().Should().BeApproximately(
            coefficient * source.Polynomial.Constant.ToDouble(), 1e-12,
            "the constant part of the recommended weight scales as well");
    }

    /// <summary>
    /// **申报面积优先于长×宽** / A declared area wins over length × width.
    /// </summary>
    /// <remarks>
    /// 同一舱位长×宽 2000，但申报面积 500（可用面积只有四分之一）。系数必须是 1/500 而不是 1/2000，两者相差四倍——这不是舍入误差。
    /// 货站可用面积会因切角、导轨、锁扣占位而显著小于"长×宽"。
    ///
    /// The same position measures 2000 but declares 500 (a quarter usable). The coefficient must be 1/500, not 1/2000 — a factor of four, not rounding. Usable deck area is often
    /// much smaller than length × width because of cut corners, rails and lock space.
    /// </remarks>
    [Fact]
    public void Register_ShouldPreferTheDeclaredArea() {
        var declared = new List<Position> { MakePosition("P1", 1UL, 0.0, 100.0, 50.0, 40.0, declaredArea: 500.0) };
        SurfaceDensityLimitZone[] zones = new[] { new SurfaceDensityLimitZone("Z1", 0.0, 100.0, 500.0) };
        (SurfaceDensity surfaceDensity, Load load, LinearMetaModel<Flt64> model, List<Position> _) = Prepare(zones, declared);
        surfaceDensity.Register(model).IsOk.Should().BeTrue();

        LinearMonomial<Flt64> first = surfaceDensity.SurfaceDensitySymbol(0)!.Polynomial.Monomials[0];
        LinearMonomial<Flt64> source = load.EstimateLoadWeightSymbol(0)!.Polynomial.Monomials
            .Single(candidate => ReferenceEquals(candidate.Symbol, first.Symbol));
        double ratio = first.Coefficient.ToDouble() / source.Coefficient.ToDouble();
        ratio.Should().BeApproximately(1.0 / 500.0, 1e-12, "the declared area wins");
        ratio.Should().NotBeApproximately(1.0 / 2000.0, 1e-12, "not length × width");
    }

    /// <summary>
    /// **只注册落在限区内的舱位** / Only positions inside a zone are registered.
    /// </summary>
    /// <remarks>
    /// 成对：限区只覆盖 0—100 力臂，P1 在内、P2 在 200—300 不在。P1 有符号、P2 为 `null`，且模型里只有 P1 那条。
    /// 都注册时 P2 会得到一个没有规格可对照的数字——白白进模型，还会让"表面密度约束"莫名其妙地活了。
    ///
    /// Paired: the zone covers arms 0—100, so P1 (0—100) is inside and P2 (200—300) is not. P1 has a symbol, P2 is `null`, and the model holds only P1's row. Registering both
    /// would give P2 a number with no specification to compare against — cluttering the model and making "the surface density constraint" come alive for no reason.
    /// </remarks>
    [Fact]
    public void Register_ShouldSkipPositionsOutsideEveryZone() {
        SurfaceDensityLimitZone[] zones = new[] { new SurfaceDensityLimitZone("Z1", 0.0, 100.0, 500.0) };
        (SurfaceDensity surfaceDensity, Load _, LinearMetaModel<Flt64> model, List<Position> _) = Prepare(zones);
        surfaceDensity.Register(model).IsOk.Should().BeTrue();

        surfaceDensity.InAnyZone(0).Should().BeTrue();
        surfaceDensity.InAnyZone(1).Should().BeFalse();
        surfaceDensity.SurfaceDensitySymbol(0).Should().NotBeNull();
        surfaceDensity.SurfaceDensitySymbol(1).Should().BeNull("P2 lies outside every limit zone");
        // 中间符号没有公开的枚举入口（`MetaSubObjects` 只放目标对象），所以这里钉的是**访问器**：
        // 它只在 `model.Add` 成功后才被赋值，因此"P1 有、P2 没有"同时也证明了"只有 P1 进了模型"。
        // The intermediate-symbol registry has no public enumeration (`MetaSubObjects` only holds objectives), so this pins the **accessor**: it is only assigned after a
        // successful `model.Add`, so "P1 has one, P2 does not" also means "only P1 entered the model".
    }

    /// <summary>
    /// **边界上算在内（有交集，不是完整落在内）** / Touching a boundary counts (intersects, not within).
    /// </summary>
    /// <remarks>
    /// 三个舱位分别：完全在内、**跨界**（-50—150 跨 0—100）、完全在外。-50—100 的力臂区间与跨界那个有交集但与"完整落在内"不成立，
    /// 所以这条能区分两种判断。用 `Between` 会把跨界那个漏掉——而压在区域边界上的舱位恰恰是最该检查的。
    ///
    /// Three positions: fully inside, **straddling** (-50—150 across 0—100), fully outside. The arm range intersects the straddling one without containing it, so this
    /// distinguishes the two predicates. Using `Between` would drop the straddling position — and a position sitting on a zone boundary is exactly what most needs checking.
    /// </remarks>
    [Fact]
    public void InAnyZone_ShouldCountStraddlingPositions() {
        var positions = new List<Position> {
            MakePosition("Inside", 1UL, 10.0, 90.0, 50.0, 40.0),
            MakePosition("Straddle", 2UL, -50.0, 150.0, 50.0, 40.0),
            MakePosition("Outside", 3UL, 200.0, 300.0, 50.0, 40.0),
        };
        SurfaceDensityLimitZone[] zones = new[] { new SurfaceDensityLimitZone("Z1", 0.0, 100.0, 500.0) };
        (SurfaceDensity surfaceDensity, Load _, LinearMetaModel<Flt64> _, List<Position> _) = Prepare(zones, positions);

        surfaceDensity.InAnyZone(0).Should().BeTrue("fully inside");
        surfaceDensity.InAnyZone(1).Should().BeTrue("straddling the boundary still intersects");
        surfaceDensity.InAnyZone(2).Should().BeFalse("fully outside");
        positions[1].Coordinate.Between(0.0, 100.0).Should().BeFalse(
            "sanity: the straddling position is NOT fully within, so this test really distinguishes the two predicates");
    }

    /// <summary>
    /// **力臂两端不保证先后** / The two arm ends are not ordered.
    /// </summary>
    /// <remarks>
    /// 战区里"前力臂"未必数值更小，所以区间判断必须先归一化。限区写成 `front=100, back=0`（反向）时结论必须与 `front=0, back=100` 相同。
    /// 不归一化的话，一条写反的手臂范围会静默匹配不到任何舱位。
    ///
    /// In station numbers "front" is not necessarily the smaller value, so the range must be normalised. A zone written as `front=100, back=0` must give the same answer as
    /// `front=0, back=100`. Without normalisation a reversed range silently matches nothing.
    /// </remarks>
    [Fact]
    public void InAnyZone_ShouldNormaliseTheArmRange() {
        SurfaceDensityLimitZone[] forward = new[] { new SurfaceDensityLimitZone("Z1", 0.0, 100.0, 500.0) };
        SurfaceDensityLimitZone[] reversed = new[] { new SurfaceDensityLimitZone("Z1", 100.0, 0.0, 500.0) };
        // 三个舱位：首端 0—100、**中段 40—60**、尾端 200—300。中段是必须的：贴着下端的位置在未归一化的反向区间下也恰好为真
        // （`FrontArm <= 0` 成立），所以只用它会放过"没归一化"的写法。
        // Three positions: 0—100, the **middle 40—60**, and 200—300. The middle is essential: a position sitting on the lower bound is also true under an un-normalised reversed
        // range (`FrontArm <= 0` holds), so using only that would let a non-normalising implementation pass.
        var positions = new List<Position> {
            MakePosition("Head", 1UL, 0.0, 100.0, 50.0, 40.0),
            MakePosition("Middle", 2UL, 40.0, 60.0, 50.0, 40.0),
            MakePosition("Tail", 3UL, 200.0, 300.0, 50.0, 40.0),
        };
        (SurfaceDensity a, Load _, LinearMetaModel<Flt64> _, List<Position> _) = Prepare(forward, positions);
        (SurfaceDensity b, Load _, LinearMetaModel<Flt64> _, List<Position> _) = Prepare(reversed, positions);

        for (int j = 0; j < 3; ++j) {
            b.InAnyZone(j).Should().Be(a.InAnyZone(j), "a reversed arm range must give the same answer");
        }
        b.InAnyZone(0).Should().BeTrue("the head still intersects the reversed range");
        b.InAnyZone(1).Should().BeTrue("the middle is inside 0—100 whichever way the zone is written");
        b.InAnyZone(2).Should().BeFalse();
    }

    /// <summary>
    /// **零面积是数据错误** / A zero area is a data error.
    /// </summary>
    /// <remarks>
    /// 面积为零会让系数变成 `Infinity` 并污染整个模型（求解器会返回一个莫名其妙的数值错误，而不是"这个舱位面积没填"）。当场报出来。
    ///
    /// A zero area makes the coefficient `Infinity` and poisons the model — the solver reports a baffling numeric error instead of "this position has no area". Report it here.
    /// </remarks>
    [Fact]
    public void Register_ShouldRejectAZeroAreaPosition() {
        var positions = new List<Position> { MakePosition("Zero", 1UL, 0.0, 100.0, 0.0, 40.0) };
        SurfaceDensityLimitZone[] zones = new[] { new SurfaceDensityLimitZone("Z1", 0.0, 100.0, 500.0) };
        (SurfaceDensity surfaceDensity, Load _, LinearMetaModel<Flt64> model, List<Position> _) = Prepare(zones, positions);

        surfaceDensity.Register(model).IsOk.Should().BeFalse();
        surfaceDensity.SurfaceDensitySymbol(0).Should().BeNull();
    }

    /// <summary>
    /// **没有推荐重量符号时明确失败** / Missing recommended weight fails loudly.
    /// </summary>
    /// <remarks>
    /// 空壳版本对任何输入都返回 `Ok`。缺符号时报成功会让调用方以为表面密度算出来了，而模型里一条都没有。
    ///
    /// The shell returned `Ok` for any input. Reporting success while a symbol is missing tells the caller the surface densities exist when the model holds none.
    /// </remarks>
    [Fact]
    public void Register_ShouldFailWithoutTheRecommendedWeight() {
        var positions = new List<Position> { MakePosition("P1", 1UL, 0.0, 100.0, 50.0, 40.0) };
        SurfaceDensityLimitZone[] zones = new[] { new SurfaceDensityLimitZone("Z1", 0.0, 100.0, 500.0) };
        var items = new List<Item> { MakeItem("I1") };
        var stowagePositions = new List<StowagePosition> {
            new(
                aircraftPosition: positions[0],
                status: new PositionStatus(true, false, true, true),
                mlw: new MaxLoadWeightLimit(1000.0),
                mla: 3UL,
                plw: new PositionPredicateLoadWeight(350.0, 300.0, 400.0)),
        };
        var stowage = new Stowage(items, stowagePositions) { LoadingContext = MakeKernel(items, stowagePositions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var load = new Load(items, stowagePositions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();

        var surfaceDensity = new SurfaceDensity(zones, positions, load);
        surfaceDensity.Register(model).IsOk.Should().BeFalse("the load model was never registered");
    }
}
