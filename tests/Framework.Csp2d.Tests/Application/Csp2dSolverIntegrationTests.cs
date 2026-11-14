// 真实 SCIP 端到端判据 / Real-SCIP end-to-end criteria.

#if SCIP_INTEGRATION
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Framework.Csp2d.Application.Model;
using Fuookami.Ospf.Framework.Csp2d.Application.Service;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Application;

/// <summary>
/// **真实 SCIP 端到端**：桩只证明接线，这里证明能解 / Real SCIP end to end: the stub proved the wiring, this proves it solves.
/// </summary>
/// <remarks>
/// 只在 `-p:RunScipIntegration=true` 时编译并运行（常量在 **build** 阶段定义）。默认全量测试里这些用例**不存在**，
/// 因此默认通过数不含它们——能力文档里两个数分开报。
///
/// Compiled and run only with `-p:RunScipIntegration=true` (the constant is defined at **build** time). In the default suite these cases **do not exist**, so the two
/// pass counts are reported separately in the capability document.
/// </remarks>
public class Csp2dSolverIntegrationTests {
    /// **MILP 路径在真实 SCIP 上覆盖需求** / The MILP path covers the demand under real SCIP.
    [Fact]
    public async Task Milp_ShouldCoverTheDemandWithRealScip() {
        Result<Csp2dSolution, ErrorCode, Error<ErrorCode>> result =
            await new Csp2dMilp(new ScipColumnGenerationSolver()).SolveAsync(ProblemOf());

        result.IsOk.Should().BeTrue("a 3-piece demand on a 4 by 2 sheet is trivially feasible");
        Csp2dSolution solution = result.ValueOrThrow();
        solution.Covers(ProblemOf()).Should().BeTrue("the solution must actually cover the demand");
        solution.SheetCount.Should().BeGreaterThan(0, "something must be consumed");
        solution.Utilization.Should().BeGreaterThan(0.0);
        solution.Kpi.Should().ContainKey("sheet_count");
    }

    /// **列生成路径在真实 SCIP 上同样覆盖需求** / The column generation path also covers the demand under real SCIP.
    [Fact]
    public async Task ColumnGeneration_ShouldCoverTheDemandWithRealScip() {
        Result<Csp2dSolution, ErrorCode, Error<ErrorCode>> result =
            await new Csp2dColumnGeneration(new ScipColumnGenerationSolver()).SolveAsync(ProblemOf());

        result.IsOk.Should().BeTrue();
        Csp2dSolution solution = result.ValueOrThrow();
        solution.Covers(ProblemOf()).Should().BeTrue("the final integer master must still cover the demand");
        solution.Iterations.Should().BeGreaterThan(0);
    }

    /// **卷材模式真的产出一段消耗长度** / Coil mode really produces a consumed segment length.
    [Fact]
    public async Task Milp_ShouldConsumeCoilLengthWithRealScip() {
        Csp2dProblem problem = ProblemOf(coil: true);

        Result<Csp2dSolution, ErrorCode, Error<ErrorCode>> result =
            await new Csp2dMilp(new ScipColumnGenerationSolver()).SolveAsync(problem);

        result.IsOk.Should().BeTrue();
        Csp2dSolution solution = result.ValueOrThrow();
        solution.ConsumedLength.Should().BeGreaterThan(0.0,
            "a coil solution must report the segment height it consumed");
        solution.Covers(problem).Should().BeTrue();
    }

    /// <summary>构造问题 / Build a problem.</summary>
    /// <param name="coil">是否为卷材 / Whether it is a coil material.</param>
    /// <returns>问题 / The problem.</returns>
    private static Csp2dProblem ProblemOf(bool coil = false) =>
        new(new List<Material2<Flt64>> {
                new(Csp2dIds.MaterialId2Of("MAT1"), "mat", Metres(4.0), coil ? null : Metres(2.0)),
            },
            new List<Product2<Flt64>> { ProductOf() },
            new List<ProductDemand2<Flt64>> { new(ProductOf(), new DemandValue2<Flt64>.Amount(3UL)) },
            Metres(0.0),
            Config: new Csp2dConfiguration());

    /// <summary>构造产品 / Build a product.</summary>
    /// <returns>产品 / The product.</returns>
    private static Product2<Flt64> ProductOf() =>
        new(Csp2dIds.ProductId2Of("P1"), "part", Metres(1.0), Metres(1.0));

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
#endif
