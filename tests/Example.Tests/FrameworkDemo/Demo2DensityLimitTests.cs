// 表面密度上限与线性密度的判据 / Criteria for the surface density cap and linear density.

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
/// **表面密度上限与线性密度（B590）** / The surface density cap and linear density (B590).
/// </summary>
/// <remarks>
/// 表面密度和线性密度是同一件事的两个方向：`重量 / 面积` 与 `重量 / 长度`。两处口径**不同**，都是基线的选择：
///
/// | | 建符号的舱位 | 系数 |
/// | --- | --- | --- |
/// | 表面密度 | **只在限区内**（没规格就没必要算） | `1 / 面积`（申报面积优先） |
/// | 线性密度 | **所有舱位**（符号不依赖区域，限制线后面再切） | `1 / 长度` |
///
/// 线性密度还有一半是**限制线**：沿机身按力臂切横截面，每个切面覆盖"压在这一刀上"的舱位。三步容易错——候选力臂去重、区域外的切面丢弃、
/// 空线丢弃。
///
/// Surface and linear density are the same idea along two axes: weight per area and weight per length. Their conventions **differ**, and both are the baseline's: surface density
/// builds symbols **only inside a zone**, linear density for **every** position. Linear density also has a second half — **limit lines** cutting cross-sections along the fuselage,
/// each covering the positions lying on that cut. Three easy mistakes: de-duplicating the candidate arms, dropping cuts outside the zone, and dropping empty lines.
/// </remarks>
public class Demo2DensityLimitTests {
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
        double length = 50.0,
        double width = 40.0,
        PositionLocation? location = null) =>
        new(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(frontArm, backArm, 0.0, 100.0),
            shape: new PositionShape(length, width, length * width * 60.0),
            location: location ?? PositionLocation.NormalMainLocation);

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

    private static List<StowagePosition> MakeStowagePositions(IReadOnlyList<Position> positions, bool available = true) =>
        positions
            .Select(position => new StowagePosition(
                aircraftPosition: position,
                status: new PositionStatus(available, false, true, true),
                mlw: new MaxLoadWeightLimit(1000.0),
                mla: 3UL,
                plw: new PositionPredicateLoadWeight(350.0, 300.0, 400.0)))
            .ToList();

    private static Load MakeLoad(IReadOnlyList<StowagePosition> stowagePositions, LinearMetaModel<Flt64> model) {
        var items = new List<Item> { MakeItem("I1") };
        var stowage = new Stowage(items, stowagePositions) { LoadingContext = MakeKernel(items, stowagePositions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, stowagePositions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();
        return load;
    }

    /// <summary>
    /// **线性密度给所有舱位建符号，表面密度只给限区内** / Linear density covers every position; surface density only covers zones.
    /// </summary>
    /// <remarks>
    /// 成对的两个口径。两个舱位、限区只覆盖第一个：`linear_density_*` 两个都在，`surface_density_*` 只有一个。
    /// 两者取同一个数会让"没规格就不算"变成"全都算"，模型里多出一批没有约束的符号。
    ///
    /// Two paired conventions. With two positions and a zone covering only the first, both `linear_density_*` symbols exist while only one `surface_density_*` does. Treating
    /// them alike turns "no specification, no computation" into "compute everything", leaving a batch of symbols no constraint ever refers to.
    /// </remarks>
    [Fact]
    public void LinearDensity_ShouldCoverEveryPositionWhileSurfaceDensityDoesNot() {
        var positions = new List<Position> {
            MakePosition("P1", 1UL, 0.0, 100.0),
            MakePosition("P2", 2UL, 200.0, 300.0),
        };
        List<StowagePosition> stowagePositions = MakeStowagePositions(positions);
        using var model = new LinearMetaModel<Flt64>();
        Load load = MakeLoad(stowagePositions, model);

        var linear = new LinearDensity(
            new[] { new LinearDensityLimitZone("Z1", new HashSet<DeckLocation> { DeckLocation.Main }, 0.0, 100.0, 300.0) },
            new[] { new LinearDensityLimitLine(new LinearDensityLimitZone("Z1", new HashSet<DeckLocation> { DeckLocation.Main }, 0.0, 100.0, 300.0), 0.0, new[] { 0 }) },
            positions,
            load);
        linear.Register(model).IsOk.Should().BeTrue();
        linear.LinearDensitySymbol(0).Should().NotBeNull();
        linear.LinearDensitySymbol(1).Should().NotBeNull("linear density does not depend on zones");

        var surface = new SurfaceDensity(
            new[] { new SurfaceDensityLimitZone("Z1", 0.0, 100.0, 500.0) },
            positions,
            load);
        surface.Register(model).IsOk.Should().BeTrue();
        surface.SurfaceDensitySymbol(0).Should().NotBeNull();
        surface.SurfaceDensitySymbol(1).Should().BeNull("no specification, nothing to compute");
    }

    /// <summary>
    /// **线性密度的系数用长度，不是面积** / Linear density divides by length, not area.
    /// </summary>
    /// <remarks>
    /// P1 长 50、宽 40。系数必须是 1/50，不是 1/2000（面积）也不是 1/40（宽度）。线性密度是"沿机身方向单位长度上的重量"，与宽度无关。
    ///
    /// P1 measures 50 by 40. The coefficient must be 1/50 — neither 1/2000 (area) nor 1/40 (width). Linear density is weight per unit length along the fuselage, independent of
    /// width.
    /// </remarks>
    [Fact]
    public void LinearDensity_ShouldDivideByTheLength() {
        var positions = new List<Position> { MakePosition("P1", 1UL, 0.0, 100.0, length: 50.0, width: 40.0) };
        List<StowagePosition> stowagePositions = MakeStowagePositions(positions);
        using var model = new LinearMetaModel<Flt64>();
        Load load = MakeLoad(stowagePositions, model);
        var linear = new LinearDensity(
            new LinearDensityLimitZone[0],
            new LinearDensityLimitLine[0],
            positions,
            load);
        linear.Register(model).IsOk.Should().BeTrue();

        LinearExpressionSymbol symbol = linear.LinearDensitySymbol(0)!;
        symbol.Name.Should().Be("linear_density_P1");
        LinearMonomial<Flt64> first = symbol.Polynomial.Monomials[0];
        LinearMonomial<Flt64> source = load.EstimateLoadWeightSymbol(0)!.Polynomial.Monomials
            .Single(candidate => ReferenceEquals(candidate.Symbol, first.Symbol));
        double ratio = first.Coefficient.ToDouble() / source.Coefficient.ToDouble();
        ratio.Should().BeApproximately(1.0 / 50.0, 1e-12);
        ratio.Should().NotBeApproximately(1.0 / 2000.0, 1e-12, "not the area");
        ratio.Should().NotBeApproximately(1.0 / 40.0, 1e-12, "not the width");
    }

    /// <summary>
    /// **限制线只留区域内的切面，且丢掉空线** / Only cuts inside the zone survive, and empty lines are dropped.
    /// </summary>
    /// <remarks>
    /// 一个舱位 P1 跨 0—100，区域也是 0—100。候选力臂去重后是 {0, 100}（P1 的前后臂与区域两端重合）。两条线都非空。
    /// 再加一个区域外的舱位 P2（200—300），它的前后臂**不得**成为候选——否则会切出两条位于区域之外、覆盖不到任何区域内舱位的空线。
    /// 空线必须丢掉：一条没有舱位的横截面没有任何重量可检查，留着只会让约束生成器加一行恒真的废约束。
    ///
    /// One position P1 spans 0—100, and so does the zone. The de-duplicated candidates are {0, 100} (P1's arms coincide with the zone ends) and both lines are non-empty. Adding a
    /// position P2 outside the zone (200—300), its arms must **not** become candidates — otherwise two cuts outside the zone appear, covering no in-zone position. Empty lines must
    /// be dropped: a cross-section with no position has no weight to check, and keeping it makes the constraint generator emit a vacuous always-true row.
    /// </remarks>
    [Fact]
    public void BuildLimitLines_ShouldKeepOnlyInZoneCutsAndDropEmptyOnes() {
        var positions = new List<Position> {
            MakePosition("P1", 1UL, 0.0, 100.0),
            MakePosition("P2", 2UL, 200.0, 300.0),
        };
        LinearDensityLimitZone[] zones = new[] { new LinearDensityLimitZone("Z1", new HashSet<DeckLocation> { DeckLocation.Main }, 0.0, 100.0, 300.0) };

        IReadOnlyList<LinearDensityLimitLine> lines = LinearDensity.BuildLimitLines(zones, positions);

        lines.Should().HaveCount(2, "the candidates are exactly the two zone ends");
        lines.Select(line => line.Arm).Should().BeEquivalentTo(new[] { 0.0, 100.0 });
        lines.SelectMany(line => line.PositionIndices).Distinct().Should().BeEquivalentTo(new[] { 0 },
            "P2 lies outside the zone and never contributes");
        lines.Should().OnlyContain(line => line.PositionIndices.Count > 0, "empty lines are dropped");
    }

    /// <summary>
    /// **一个跨多个切面的舱位出现在多条线上** / A position spanning several cuts appears on several lines.
    /// </summary>
    /// <remarks>
    /// 三个首尾相接的舱位 0—100 / 100—200 / 200—300，区域 0—300。候选力臂 {0,100,200,300}；中间两条线（100 和 200）**各覆盖两个舱位**，
    /// 因为边界处两个舱位都"压在"那一刀上。只取"起点等于该力臂"的舱位会让每个切面只剩一个舱位——那会漏掉边界处的另一半重量。
    ///
    /// Three abutting positions 0—100 / 100—200 / 200—300 with a zone 0—300. Candidates {0,100,200,300}; the two interior cuts (100 and 200) **each cover two positions**,
    /// because both neighbours sit on that cut. Taking only positions whose front arm equals the cut would leave one position per cross-section — dropping half the weight at each
    /// boundary.
    /// </remarks>
    [Fact]
    public void BuildLimitLines_ShouldCoverBothSidesOfAnInteriorCut() {
        var positions = new List<Position> {
            MakePosition("A", 1UL, 0.0, 100.0),
            MakePosition("B", 2UL, 100.0, 200.0),
            MakePosition("C", 3UL, 200.0, 300.0),
        };
        LinearDensityLimitZone[] zones = new[] { new LinearDensityLimitZone("Z1", new HashSet<DeckLocation> { DeckLocation.Main }, 0.0, 300.0, 300.0) };

        IReadOnlyList<LinearDensityLimitLine> lines = LinearDensity.BuildLimitLines(zones, positions);

        lines.Select(line => line.Arm).Should().BeEquivalentTo(new[] { 0.0, 100.0, 200.0, 300.0 });
        lines.Single(line => line.Arm == 100.0).PositionIndices.Should().BeEquivalentTo(new[] { 0, 1 },
            "the cut at 100 touches both A and B");
        lines.Single(line => line.Arm == 200.0).PositionIndices.Should().BeEquivalentTo(new[] { 1, 2 },
            "the cut at 200 touches both B and C");
        lines.Single(line => line.Arm == 0.0).PositionIndices.Should().BeEquivalentTo(new[] { 0 });
        lines.Single(line => line.Arm == 300.0).PositionIndices.Should().BeEquivalentTo(new[] { 2 });
    }

    /// <summary>
    /// **跨界舱位的臂落在区域外，不得成为切面** / A straddling position's arms fall outside the zone and must not become cuts.
    /// </summary>
    /// <remarks>
    /// 一个舱位 -50—150 跨过区域 0—100。它的前后臂都与区域**有交集**（所以它算区域内舱位），但 -50 与 150 都在区域力臂区间之外，
    /// 不能当切面用。少了"只留区域内候选"这一步，会切出两条位于区域之外、且各自只覆盖那一个跨界舱位的线——检查了规格范围以外的地方。
    ///
    /// A position -50—150 straddles the zone 0—100: its arms **intersect** the zone (so it counts as an in-zone position) but -50 and 150 lie outside the zone's arm range and
    /// cannot serve as cuts. Without the "keep only in-range candidates" step, two lines appear outside the zone, each covering that one straddling position — inspecting places
    /// the specification never covered.
    /// </remarks>
    [Fact]
    public void BuildLimitLines_ShouldDropStraddlingArmsOutsideTheZone() {
        var positions = new List<Position> { MakePosition("Wide", 1UL, -50.0, 150.0) };
        LinearDensityLimitZone[] zones = new[] { new LinearDensityLimitZone("Z1", new HashSet<DeckLocation> { DeckLocation.Main }, 0.0, 100.0, 300.0) };

        IReadOnlyList<LinearDensityLimitLine> lines = LinearDensity.BuildLimitLines(zones, positions);

        lines.Select(line => line.Arm).Should().BeEquivalentTo(new[] { 0.0, 100.0 },
            "the straddling position's own arms lie outside the zone");
        lines.Should().NotContain(line => line.Arm < 0.0 || line.Arm > 100.0);
    }

    /// <summary>
    /// **区域端点没有舱位时那条线要丢掉** / A zone end with no position on it yields no line.
    /// </summary>
    /// <remarks>
    /// 区域 0—300，唯一的舱位在 100—200。候选力臂是 {0, 300, 100, 200}，其中 0 与 300 在区域区间内但**没有舱位压在上面**。
    /// 不丢空线会得到两条覆盖不到任何舱位的横截面，让约束生成器为它们各加一行恒真的废约束。
    ///
    /// Zone 0—300 with the only position at 100—200. The candidates are {0, 300, 100, 200}; 0 and 300 are inside the zone range but **no position sits on them**. Keeping empty
    /// lines yields two cross-sections covering nothing and makes the constraint generator emit a vacuous always-true row for each.
    /// </remarks>
    [Fact]
    public void BuildLimitLines_ShouldDropZoneEndsWithNoPosition() {
        var positions = new List<Position> { MakePosition("Middle", 1UL, 100.0, 200.0) };
        LinearDensityLimitZone[] zones = new[] { new LinearDensityLimitZone("Z1", new HashSet<DeckLocation> { DeckLocation.Main }, 0.0, 300.0, 300.0) };

        IReadOnlyList<LinearDensityLimitLine> lines = LinearDensity.BuildLimitLines(zones, positions);

        lines.Select(line => line.Arm).Should().BeEquivalentTo(new[] { 100.0, 200.0 },
            "the zone ends carry no position and are dropped");
        lines.Should().OnlyContain(line => line.PositionIndices.Count > 0);
    }

    /// <summary>
    /// **区域只覆盖它声明的甲板位置** / A zone covers only the deck locations it declares.
    /// </summary>
    /// <remarks>
    /// 力臂完全重合但甲板位置不同的两个舱位：区域只声明主甲板，下舱那个不得进入任何限制线。少了这道判断，一条"前下舱线性密度"的规格会被
    /// 主甲板的舱位满足——力臂对得上，甲板完全错。
    ///
    /// Two positions with identical arms but different deck locations: the zone declares the main deck only, so the lower-hold one must not appear on any line. Without that check,
    /// a "lower forward hold linear density" specification would be satisfied by main-deck positions — arms match, deck is entirely wrong.
    /// </remarks>
    [Fact]
    public void BuildLimitLines_ShouldRespectTheZoneDeckLocations() {
        var positions = new List<Position> {
            MakePosition("Main", 1UL, 0.0, 100.0),
            MakePosition("Low", 2UL, 0.0, 100.0, location: PositionLocation.LowForwardNotBulkLocation),
        };
        LinearDensityLimitZone[] zones = new[] { new LinearDensityLimitZone("Z1", new HashSet<DeckLocation> { DeckLocation.Main }, 0.0, 100.0, 300.0) };

        IReadOnlyList<LinearDensityLimitLine> lines = LinearDensity.BuildLimitLines(zones, positions);

        lines.SelectMany(line => line.PositionIndices).Distinct().Should().BeEquivalentTo(new[] { 0 },
            "the low-deck position is outside the zone's declared locations");
    }

    /// <summary>
    /// **表面密度上限：每个限区 × 每个可用舱位一行** / One row per zone per available position.
    /// </summary>
    /// <remarks>
    /// 断言三条：比较符是 `LE`、右端是该限区的上限、名字是**三段**（`surface_density_limit_Z1_P1`）。名字只用前缀的话所有行同名，
    /// 求解器报"第 3 条约束不可行"时无法定位到区域与舱位。
    ///
    /// Three assertions: the operator is `LE`, the right side is that zone's limit, and the name has **three segments** (`surface_density_limit_Z1_P1`). A bare prefix would give
    /// every row the same name and make "constraint 3 is infeasible" untraceable.
    /// </remarks>
    [Fact]
    public void SurfaceDensityLimit_ShouldCapEachZonePositionPair() {
        var positions = new List<Position> { MakePosition("P1", 1UL, 0.0, 100.0) };
        List<StowagePosition> stowagePositions = MakeStowagePositions(positions);
        using var model = new LinearMetaModel<Flt64>();
        Load load = MakeLoad(stowagePositions, model);
        var surface = new SurfaceDensity(
            new[] { new SurfaceDensityLimitZone("Z1", 0.0, 100.0, 500.0) },
            positions,
            load);
        surface.Register(model).IsOk.Should().BeTrue();

        var limit = new SurfaceDensityLimit(surface, stowagePositions);
        limit.Invoke(model).IsOk.Should().BeTrue();

        LinearInequalityConstraint<Flt64> row = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "surface_density_limit_Z1_P1");
        row.Inequality.Comparison.Should().Be(Comparison.LE);
        row.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(500.0, 1e-9);
        row.Inequality.Lhs.Monomials.Select(monomial => monomial.Symbol)
            .Should().BeEquivalentTo(surface.SurfaceDensitySymbol(0)!.Polynomial.Monomials.Select(monomial => monomial.Symbol),
                "the left side is this position's surface density");
        limit.LastAddedNames.Should().ContainSingle();
    }

    /// <summary>
    /// **不可用舱位不加约束** / Unavailable positions get no row.
    /// </summary>
    /// <remarks>
    /// 与"可用舱位加约束"成对。给一个已不可用的舱位加密度上限毫无意义，而且它的符号本就不该承载价值——留着会让求解器在一个"不参与装载"的
    /// 变量上浪费约束。
    ///
    /// Paired with "available positions get a row". Capping the density of a position that cannot take cargo is meaningless and its symbol should carry no value — keeping the row
    /// only makes the solver spend a constraint on a variable that never takes part.
    /// </remarks>
    [Fact]
    public void SurfaceDensityLimit_ShouldSkipUnavailablePositions() {
        var positions = new List<Position> { MakePosition("P1", 1UL, 0.0, 100.0) };
        List<StowagePosition> stowagePositions = MakeStowagePositions(positions, available: false);
        using var model = new LinearMetaModel<Flt64>();
        Load load = MakeLoad(stowagePositions, model);
        var surface = new SurfaceDensity(
            new[] { new SurfaceDensityLimitZone("Z1", 0.0, 100.0, 500.0) },
            positions,
            load);
        surface.Register(model).IsOk.Should().BeTrue();

        var limit = new SurfaceDensityLimit(surface, stowagePositions);
        limit.Invoke(model).IsOk.Should().BeTrue();
        limit.LastAddedNames.Should().BeEmpty("the position is unavailable");
        model.Constraints.Select(constraint => constraint.Name).Should().NotContain("surface_density_limit_Z1_P1");
    }

    /// <summary>
    /// **限区内舱位缺符号时明确失败** / A missing symbol inside a zone fails loudly.
    /// </summary>
    /// <remarks>
    /// 静默跳过会让整条密度约束凭空消失——模型照样求解，"表面密度合规"是一个没人验过的结论。空壳版本对任何输入都返回 `Ok`。
    ///
    /// Skipping silently makes the whole density constraint vanish: the model still solves and "surface density is compliant" is a conclusion nobody checked. The shell returned
    /// `Ok` for any input.
    /// </remarks>
    [Fact]
    public void SurfaceDensityLimit_ShouldFailWhenTheSymbolIsMissing() {
        var positions = new List<Position> { MakePosition("P1", 1UL, 0.0, 100.0) };
        List<StowagePosition> stowagePositions = MakeStowagePositions(positions);
        using var model = new LinearMetaModel<Flt64>();
        Load load = MakeLoad(stowagePositions, model);
        var surface = new SurfaceDensity(
            new[] { new SurfaceDensityLimitZone("Z1", 0.0, 100.0, 500.0) },
            positions,
            load);

        var limit = new SurfaceDensityLimit(surface, stowagePositions);
        limit.Invoke(model).IsOk.Should().BeFalse("the surface density symbols were never registered");
        limit.LastAddedNames.Should().BeEmpty();
    }
}
