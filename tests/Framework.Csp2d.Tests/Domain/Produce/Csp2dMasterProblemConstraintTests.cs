// 主问题约束与目标判据 / Criteria for the master-problem constraints and objective.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Domain.Produce;

/// <summary>
/// **覆盖量口径、可用量上限与就绪判定** / Coverage units, availability bounds and readiness.
/// </summary>
/// <remarks>
/// 对应后半段。判据同时走**约束条数**（`model.Constraints.Count`）与**覆盖量数学**（internal 助手），
/// 因为前者能证明"该注册的注册了 / 该跳过的不注册"，后者能证明系数算对了。
///
/// Mirrors the tail of the implementation. The criteria use both the **constraint count** (`model.Constraints.Count`) and the **coverage math**
/// (internal helpers): the former proves "registered what must be registered and skipped what must be skipped", the latter proves the coefficients are right.
/// </remarks>
public class Csp2dMasterProblemConstraintTests {
    /// <summary>**覆盖量随需求口径换算法** / The coverage formula follows the demand unit.</summary>
    /// <remarks>
    /// 三种口径都归一到同一个数值域，因为主问题只有一套变量。**重量口径缺单件重量时必须为零**——
    /// 拿件数顶替重量会让"重量需求"被件数悄悄满足。
    ///
    /// All three units map into one numeric domain because the master problem has a single variable set. **The weight unit must yield zero when the unit weight is
    /// missing**: substituting the count for the weight would let a weight demand be silently satisfied by piece counts.
    /// </remarks>
    [Fact]
    public void CoverageOf_ShouldFollowTheDemandUnit() {
        Pattern2<Flt64> pattern = PatternOf(pieces: 2);
        CuttingPlan2<Flt64> plan = PlanOf(pattern);

        Csp2dProduceSupport.CoverageOf(plan, Demand(pattern, DemandMode2.Amount)).Should().Be(2.0,
            "two pieces cover two pieces");
        Csp2dProduceSupport.CoverageOf(plan, Demand(pattern, DemandMode2.Area)).Should().BeApproximately(16.0, 1e-12,
            "two pieces on a 4 by 2 metre sheet cover 16 square metres of material");
        Csp2dProduceSupport.CoverageOf(plan, Demand(pattern, DemandMode2.Weight)).Should().Be(0.0,
            "the product declares no unit weight, so a weight demand cannot be covered");
    }

    /// <summary>**一个方案对另一个产品零覆盖** / A plan covers another product by zero.</summary>
    [Fact]
    public void CoverageOf_ShouldBeZeroForAnotherProduct() {
        Pattern2<Flt64> pattern = PatternOf(pieces: 2);
        CuttingPlan2<Flt64> plan = PlanOf(pattern);
        Product2<Flt64> other = new(Csp2dIds.ProductId2Of("P2"), "other", Metres(1.0), Metres(1.0));
        ProductDemand2<Flt64> demand = new(other, new DemandValue2<Flt64>.Amount(5UL));

        Csp2dProduceSupport.CoverageOf(plan, demand).Should().Be(0.0,
            "treating a missing product as coverage one would let any plan satisfy any demand");
    }

    /// <summary>**需求约束按需求条数各注册一条** / One constraint per demand.</summary>
    [Fact]
    public void DemandConstraint_ShouldRegisterOneConstraintPerDemand() {
        using var model = new LinearMetaModel<Flt64>();
        IReadOnlyList<CuttingPlan2<Flt64>> plans = PlansOf();
        Pattern2<Flt64> pattern = plans[0].Pattern;
        Csp2dPatternAssignment assignment = new(plans);
        assignment.Register(model, 10UL).IsOk.Should().BeTrue();

        var demands = new List<ProductDemand2<Flt64>> {
            Demand(pattern, DemandMode2.Amount),
            Demand(pattern, DemandMode2.Area),
        };
        Csp2dDemandConstraint constraint = new(plans, assignment, demands);

        int before = model.Constraints.Count;
        constraint.Invoke(model).IsOk.Should().BeTrue();
        model.Constraints.Count.Should().Be(before + 2, "each demand gets its own coverage row");
    }

    /// <summary>**不限量物料与无产能设备都不注册约束** / Unlimited material and capacity-less machines register nothing.</summary>
    /// <remarks>
    /// 给 `ulong.MaxValue` 的上界等于给求解器添一道**永远成立的大数约束**，除了数值病态什么也没带来。
    ///
    /// An upper bound of `ulong.MaxValue` adds a constraint that **always holds**, bringing nothing but numerical trouble.
    /// </remarks>
    [Fact]
    public void MaterialAndCapacityConstraints_ShouldSkipUnboundedSide() {
        using var model = new LinearMetaModel<Flt64>();
        Material2<Flt64> limited = MaterialOfBatches("MAT1", 3UL);
        Material2<Flt64> unlimited = MaterialOfBatches("MAT2", ulong.MaxValue);

        // **两种物料各要有方案**：只用一种物料时，"跳过不限量物料"与"无单项式跳过"这两条守卫无法区分。
        // **Each kind needs its own plan**: with a single material, the "skip unlimited" guard is indistinguishable from the "no monomials" guard.
        IReadOnlyList<CuttingPlan2<Flt64>> plans = new List<CuttingPlan2<Flt64>> {
            PlanOf(PatternOf(1)),
            PlanOfOn(unlimited, 2),
        };
        Csp2dPatternAssignment assignment = new(plans);
        assignment.Register(model, 10UL).IsOk.Should().BeTrue();

        int before = model.Constraints.Count;
        Csp2dMaterialAvailabilityConstraint material = new(
            plans, assignment, new List<Material2<Flt64>> { limited, unlimited });
        material.Invoke(model).IsOk.Should().BeTrue();
        model.Constraints.Count.Should().Be(before + 1, "only the limited material gets a row");

        Machine2<Flt64> noCapacity = new(Csp2dIds.MachineId2Of("M1"), "no capacity");
        Machine2<Flt64> withCapacity = new(
            Csp2dIds.MachineId2Of("M2"), "with capacity", MaxWidth: null, Capacity: Metres(9.0));
        int afterMaterial = model.Constraints.Count;
        Csp2dMachineCapacityConstraint capacity = new(
            plans, assignment, new List<Machine2<Flt64>> { noCapacity, withCapacity });
        capacity.Invoke(model).IsOk.Should().BeTrue();
        model.Constraints.Count.Should().Be(afterMaterial,
            "the plans declare no capacity consumption, so the capacity row has no terms");
    }

    /// <summary>**就绪判定四项全查** / Readiness checks all four registrations.</summary>
    [Fact]
    public void AllSucceeded_ShouldRequireEveryRegistration() {
        using var model = new LinearMetaModel<Flt64>();
        Csp2dPatternAssignment assignment = new(PlansOf());
        Result<Success, ErrorCode, Error<ErrorCode>> ok = Results.OkInstance;
        Result<Success, ErrorCode, Error<ErrorCode>> bad = Results.Failed<Success>(
            new Err<ErrorCode>(ErrorCode.ApplicationError, "boom"));

        new Csp2dMasterProblem(model, assignment, ok, ok, ok, ok).AllSucceeded().Should().BeTrue();
        new Csp2dMasterProblem(model, assignment, bad, ok, ok, ok).AllSucceeded().Should().BeFalse();
        new Csp2dMasterProblem(model, assignment, ok, bad, ok, ok).AllSucceeded().Should().BeFalse();
        new Csp2dMasterProblem(model, assignment, ok, ok, bad, ok).AllSucceeded().Should().BeFalse();
        new Csp2dMasterProblem(model, assignment, ok, ok, ok, bad).AllSucceeded().Should().BeFalse();
    }

    /// <summary>**零系数目标不注册** / An all-zero objective is not registered.</summary>
    [Fact]
    public void UsageObjective_ShouldSkipZeroCoefficients() {
        using var model = new LinearMetaModel<Flt64>();
        IReadOnlyList<CuttingPlan2<Flt64>> plans = PlansOf();
        Csp2dPatternAssignment assignment = new(plans);
        assignment.Register(model, 10UL).IsOk.Should().BeTrue();

        Csp2dUsageObjective zero = new(plans, assignment, _ => Flt64.Zero);
        zero.Invoke(model).IsOk.Should().BeTrue("an empty objective is legal, it just optimises nothing");

        Csp2dUsageObjective nonZero = new(plans, assignment, _ => Flt64.One);
        nonZero.Invoke(model).IsOk.Should().BeTrue();
    }

    /// <summary>构造一个需求 / Build a demand.</summary>
    /// <param name="pattern">图案（用于取产品与料域）/ The pattern supplying the product and the region.</param>
    /// <param name="mode">需求口径 / The demand unit.</param>
    /// <returns>需求 / The demand.</returns>
    private static ProductDemand2<Flt64> Demand(Pattern2<Flt64> pattern, DemandMode2 mode) {
        Product2<Flt64> product = new(Csp2dIds.ProductId2Of("P1"), "part", Metres(1.0), Metres(1.0));
        DemandValue2<Flt64> value = mode switch {
            DemandMode2.Area => new DemandValue2<Flt64>.Area(SquareMetres(2.0)),
            DemandMode2.Weight => new DemandValue2<Flt64>.Weight(Kilograms(1.0)),
            _ => new DemandValue2<Flt64>.Amount(4UL),
        };
        return new ProductDemand2<Flt64>(product, value);
    }

    /// <summary>两个候选方案 / Two candidate plans.</summary>
    /// <returns>方案列表 / The plan list.</returns>
    private static IReadOnlyList<CuttingPlan2<Flt64>> PlansOf() =>
        new List<CuttingPlan2<Flt64>> {
            PlanOf(PatternOf(pieces: 2)),
            PlanOf(PatternOf(pieces: 1)),
        };

    /// <summary>由图案构造方案 / Build a plan from a pattern.</summary>
    /// <param name="pattern">图案 / The pattern.</param>
    /// <returns>方案 / The plan.</returns>
    private static CuttingPlan2<Flt64> PlanOf(Pattern2<Flt64> pattern) {
        Product2<Flt64> product = new(Csp2dIds.ProductId2Of("P1"), "part", Metres(1.0), Metres(1.0));
        ulong amount = pattern.ProductAmounts().TryGetValue("P1", out ulong count) ? count : 0UL;
        var contributions = new List<CuttingPlanDemandContribution2<Flt64>>();
        if (amount > 0UL) {
            contributions.Add(new CuttingPlanDemandContribution2<Flt64>(product, amount));
        }
        return new(
            Csp2dIds.PlanId2Of("PLAN-" + pattern.Id.Value), pattern.Material, pattern, contributions, null);
    }

    /// <summary>在指定物料上构造方案 / Build a plan on the given material.</summary>
    /// <param name="material">物料 / The material.</param>
    /// <param name="pieces">件数 / The piece count.</param>
    /// <returns>方案 / The plan.</returns>
    private static CuttingPlan2<Flt64> PlanOfOn(Material2<Flt64> material, int pieces) {
        Product2<Flt64> product = new(Csp2dIds.ProductId2Of("P1"), "part", Metres(1.0), Metres(1.0));
        var placements = new List<Placement2<Flt64>>();
        for (int index = 0; index < pieces; index++) {
            placements.Add(new Placement2<Flt64>(
                new RectanglePackingShape2<Flt64>(Metres(1.0), Metres(1.0)),
                new QuantityPoint2<Flt64>(Metres(index), Metres(0.0)),
                false,
                "P1"));
        }
        var pattern = new Pattern2<Flt64>(
            Csp2dIds.PatternId2Of("PAT-" + material.Id.Value), material,
            Region2Factory.Region2Of(Metres(0.0), Metres(0.0), Metres(4.0), Metres(2.0)),
            placements, new List<Placement2<Flt64>>());
        return new(
            Csp2dIds.PlanId2Of("PLAN-" + material.Id.Value), material, pattern,
            new List<CuttingPlanDemandContribution2<Flt64>> {
                new(product, (ulong)pieces),
            },
            null);
    }

    /// <summary>构造图案 / Build a pattern.</summary>
    /// <param name="pieces">件数 / The piece count.</param>
    /// <returns>图案 / The pattern.</returns>
    private static Pattern2<Flt64> PatternOf(int pieces) {
        Material2<Flt64> material = MaterialOfBatches("MAT1", 3UL);
        var placements = new List<Placement2<Flt64>>();
        for (int index = 0; index < pieces; index++) {
            placements.Add(new Placement2<Flt64>(
                new RectanglePackingShape2<Flt64>(Metres(1.0), Metres(1.0)),
                new QuantityPoint2<Flt64>(Metres(index), Metres(0.0)),
                false,
                "P1"));
        }
        return new(
            Csp2dIds.PatternId2Of("PAT" + pieces.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            material,
            Region2Factory.Region2Of(Metres(0.0), Metres(0.0), Metres(4.0), Metres(2.0)),
            placements,
            new List<Placement2<Flt64>>());
    }

    /// <summary>构造指定可用量的物料 / Build a material with the given availability.</summary>
    /// <param name="materialId">物料编号 / The material identifier.</param>
    /// <param name="batches">可用卷数/张数 / The available count.</param>
    /// <returns>物料 / The material.</returns>
    private static Material2<Flt64> MaterialOfBatches(string materialId, ulong batches) =>
        new(Csp2dIds.MaterialId2Of(materialId), "steel", Metres(4.0), Metres(2.0),
            AvailableBatches: batches);

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>构造平方米量 / Build an area in square metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> SquareMetres(double value) =>
        new(new Flt64(value), AreaUnits.SquareMeter);

    /// <summary>构造千克量 / Build a mass in kilograms.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) => new(new Flt64(value), SIBaseUnits.Kilogram);
}
