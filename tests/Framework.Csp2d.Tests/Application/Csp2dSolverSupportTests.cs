// 装配与恢复判据 / Criteria for assembly and recovery.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp2d.Application.Model;
using Fuookami.Ospf.Framework.Csp2d.Application.Service;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp2d.Domain.SheetAssignment;
using Fuookami.Ospf.Framework.Csp2d.Domain.Yield;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Application;

/// <summary>
/// **候选生成、主问题装配与解恢复** / Candidate generation, master assembly and solution recovery.
/// </summary>
/// <remarks>
/// 对应基线 `Csp2dSolvers.kt` 的纯函数部分。求解器入口（`Csp2dMilp` / `Csp2dColumnGeneration`）需要真求解器，另批处理。
///
/// Mirrors the pure-function part of the baseline `Csp2dSolvers.kt`. The solver entries need a real solver and are handled in another batch.
/// </remarks>
public class Csp2dSolverSupportTests {
    /// <summary>**每个物料都产出一批候选，编号带物料下标** / Each material yields its own candidates, with the material index in the identifier.</summary>
    [Fact]
    public void BuildPlans_ShouldEmitOneBatchPerMaterial() {
        Csp2dProblem twoMaterials = ProblemOf(materials: 2);

        IReadOnlyList<CuttingPlan2<Flt64>> plans = Csp2dSolverSupport
            .BuildPlans(twoMaterials, Products()).ValueOrThrow();

        plans.Should().HaveCount(2, "one candidate per material");
        plans[0].Id.Value.Should().Be("P0_0_0");
        plans[1].Id.Value.Should().Be("P1_0_0");
        plans[0].Pattern.Validate(Metres(0.0)).IsOk.Should().BeTrue();
        plans[0].Contributions.Should().NotBeNull();
    }

    /// <summary>**一个候选都没有时明确失败** / No candidate at all is an explicit failure.</summary>
    [Fact]
    public void BuildPlans_ShouldFailWhenNothingIsGenerated() {
        Result<IReadOnlyList<CuttingPlan2<Flt64>>, ErrorCode, Error<ErrorCode>> result =
            Csp2dSolverSupport.BuildPlans(ProblemOf(materials: 0), Products());

        result.IsFailed.Should().BeTrue("an empty plan set would make the master a zero-variable model");
        ((Failed<IReadOnlyList<CuttingPlan2<Flt64>>, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("没有生成任何候选方案");
    }

    /// <summary>**无配置时只注册需求约束与主目标** / Without configuration only the demand row and the primary objective appear.</summary>
    [Fact]
    public void AssembleMaster_ShouldRegisterOnlyTheApplicableRows() {
        Csp2dProblem problem = ProblemOf();
        IReadOnlyList<CuttingPlan2<Flt64>> plans = Csp2dSolverSupport.BuildPlans(problem, Products()).ValueOrThrow();

        (LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment) assembly =
            Csp2dSolverSupport.AssembleMaster("test", problem, plans).ValueOrThrow();

        assembly.Model.RelationConstraints.Should().HaveCount(1,
            "unlimited material, no machine, no yield bound and no sheet limit contribute nothing");
        assembly.Model.MetaSubObjects.Should().NotBeEmpty("the primary objective must still register");
        assembly.Assignment.Usage.Should().HaveCount(plans.Count);
        assembly.Model.Dispose();
    }

    /// <summary>**配了上界才多一行** / A configured bound adds exactly one row.</summary>
    [Fact]
    public void AssembleMaster_ShouldAddRowsForConfiguredBounds() {
        Csp2dProblem problem = ProblemOf() with {
            YieldConfig = new Csp2dYieldConfig(new Dictionary<string, Flt64> { ["P1"] = new Flt64(2.0) }),
        };
        IReadOnlyList<CuttingPlan2<Flt64>> plans = Csp2dSolverSupport.BuildPlans(problem, Products()).ValueOrThrow();

        (LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment) assembly =
            Csp2dSolverSupport.AssembleMaster("test", problem, plans).ValueOrThrow();

        assembly.Model.RelationConstraints.Should().HaveCount(2, "the yield bound adds one row on top of the demand");
        assembly.Model.Dispose();
    }

    /// <summary>**解恢复按使用次数累加覆盖与面积** / Recovery accumulates coverage and area per usage.</summary>
    [Fact]
    public void SolutionOf_ShouldAccumulateCoverageAndAreas() {
        Csp2dProblem problem = ProblemOf(materials: 2);
        IReadOnlyList<CuttingPlan2<Flt64>> plans = Csp2dSolverSupport.BuildPlans(problem, Products()).ValueOrThrow();

        Csp2dSolution solution = Csp2dSolverSupport
            .SolutionOf(problem, plans, new List<int> { 1, 0 }, 1).ValueOrThrow();

        solution.Usages.Should().HaveCount(1, "the zero usage must be dropped");
        solution.SheetCount.Should().Be(1);
        solution.UsedArea.Should().BeApproximately(3.0, 1e-12, "three 1 square metre pieces");
        solution.WasteArea.Should().BeApproximately(5.0, 1e-12, "an 8 square metre sheet with 3 used");
        solution.Utilization.Should().BeApproximately(3.0 / 8.0, 1e-12);
        solution.Coverage.Should().ContainKey("P1").WhoseValue.Should().Be(3);
        solution.Covers(problem).Should().BeTrue("three pieces meet the three-piece demand");
        solution.Kpi.Should().ContainKeys("sheet_count", "utilization", "iteration_count");
    }

    /// <summary>**使用次数与方案数不一致时恢复失败** / A usage count mismatch fails recovery.</summary>
    [Fact]
    public void SolutionOf_ShouldRejectAMismatchedUsageList() {
        Csp2dProblem problem = ProblemOf();
        IReadOnlyList<CuttingPlan2<Flt64>> plans = Csp2dSolverSupport.BuildPlans(problem, Products()).ValueOrThrow();

        Csp2dSolverSupport.SolutionOf(problem, plans, new List<int>(), 1).IsFailed.Should().BeTrue();
    }

    /// <summary>参与生成的产品与件数 / The products and counts taking part.</summary>
    /// <returns>产品与件数 / Products and counts.</returns>
    private static IReadOnlyList<(Product2<Flt64> Product, ulong Amount)> Products() =>
        new List<(Product2<Flt64>, ulong)> { (ProductOf(), 3UL) };

    /// <summary>构造问题 / Build a problem.</summary>
    /// <param name="materials">物料个数 / The material count.</param>
    /// <returns>问题 / The problem.</returns>
    private static Csp2dProblem ProblemOf(int materials = 1) {
        var materialList = new List<Material2<Flt64>>();
        for (int index = 0; index < materials; index++) {
            materialList.Add(new Material2<Flt64>(
                Csp2dIds.MaterialId2Of("MAT" + index), "mat" + index, Metres(4.0), Metres(2.0)));
        }
        return new Csp2dProblem(
            materialList,
            new List<Product2<Flt64>> { ProductOf() },
            new List<ProductDemand2<Flt64>> {
                new(ProductOf(), new DemandValue2<Flt64>.Amount(3UL)),
            },
            Metres(0.0),
            Config: new Csp2dConfiguration(),
            SheetConfig: new Csp2dSheetAssignmentConfig());
    }

    /// <summary>构造产品 / Build a product.</summary>
    /// <returns>产品 / The product.</returns>
    private static Product2<Flt64> ProductOf() =>
        new(Csp2dIds.ProductId2Of("P1"), "part", Metres(1.0), Metres(1.0));

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
