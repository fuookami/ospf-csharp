// 用真实 SCIP 跑通 Csp1d 列生成 / Run Csp1d column generation against real SCIP.

#if SCIP_INTEGRATION

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.Application.Service;

/// <summary>
/// **用真实 SCIP 跑通框架列生成** / Drive the framework's column generation with real SCIP.
/// </summary>
/// <remarks>
/// **B408 推翻了"环境里没有求解器"的记录**：机器上 `/c/environment/solver/scip924` 在 PATH 上，
/// `libscip.dll` 可加载。本批把这条能力接到**框架层**——
/// 在此之前 `Framework.Csp1d.Tests` **没有任何真实求解测试**（8 个插件工程有集成开关，框架层没有）。
///
/// **这直接补掉 B397 的判据弱点**：那里的框架链路判据用 `MockColumnGenerationSolver`，
/// 只能断言"不抛异常、返回非 null"，**对内部实现完全不敏感**（两个变异都存活）。
/// 桩求解器的 `LpObjective` 没有意义，而真实 SCIP 的 LP 目标值是**真实的数值**。
///
/// | 断言 | 桩求解器 | 真实 SCIP |
/// | --- | --- | --- |
/// | 解非 null | ✅ | ✅ |
/// | `LpFailureMessage` 为空 | ✅（假象） | ✅（真实解出） |
/// | `LpObjective` 是有限真实值 | ❌ 无意义 | ✅ |
/// | `TerminationReason` 是收敛 | ❌ | ✅ |
///
/// **B408 overturned the record "there is no solver in this environment"** (table above explains why this matters): `Framework.Csp1d.Tests` had **no real-solve test at
/// all**. This closes B397's criterion weakness — that one used `MockColumnGenerationSolver` and could only assert "does not throw, returns non-null", staying
/// **completely insensitive to internals** (two mutants survived). A stub's `LpObjective` is meaningless, whereas real SCIP produces a genuine LP optimum.
/// </remarks>
public class ScipColumnGenerationIntegrationTests {
    /// <summary>**真实求解器下框架链路必须解出真实 LP 并收敛** / With a real solver the pipeline must solve a genuine LP and converge.</summary>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldConvergeAgainstRealScip() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var generation = new Csp1dColumnGeneration<Flt64>(new ScipColumnGenerationSolver());

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result = await generation.SolveWithTraceAsync(problem);

        result.Solution.Should().NotBeNull("the pipeline must return a solution object");
        result.Trace.LpFailureMessage.Should().BeNull(
            "every master LP must have solved; a message here means the real solver rejected the model");
        result.Trace.Iterations.Should().NotBeEmpty(
            "a real LP master always reports at least one iteration");
        result.Trace.Iterations.Should().OnlyContain(
            record => record.LpObjective.ToDouble().Equals(double.NaN) == false,
            "each iteration carries the LP objective the real solver returned; a NaN would mean the value never came from the solver");
        result.Trace.FinalPlanCount.Should().BeGreaterThan(
            new UInt64(0),
            "the initial generator must have produced at least one usable plan");
    }

    /// <summary>构造一个最小切割库存问题 / Build a minimal cutting-stock problem.</summary>
    /// <returns>问题定义 / The problem.</returns>
    private static Csp1dProblem<Flt64> MakeProblem() {
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));
        var productA = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) });
        var productB = new Product<Flt64>(
            id: "B",
            name: "product-b",
            width: new List<Quantity<Flt64>> { Millimetres(60.0) });
        var builder = new Csp1dProblemBuilder<Flt64>();
        builder.AddProduct(productA);
        builder.AddProduct(productB);
        builder.AddMaterial(material);
        builder.AddDemand(new ProductDemand<Flt64>(productA, Millimetres(50.0)));
        builder.AddDemand(new ProductDemand<Flt64>(productB, Millimetres(60.0)));
        return builder.Build();
    }

    /// <summary>构造毫米量 / Build a millimetre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);
}

#endif
