#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// **端到端：一张 ONNX 图真的被求解了** / End to end: a graph is actually solved.
/// </summary>
/// <remarks>
/// 这是本插件第一份"能力可用"的硬证据：**图 → 仿射链 → 线性模型 → Gurobi → 与手算对照。**
/// **覆盖范围明确有限——只有仿射链，激活与形状算子不在其中。**
///
/// This is the plugin's first hard evidence that the capability works: **graph → affine chain → linear model → Gurobi → compared with hand arithmetic.** **Coverage is
/// deliberately narrow — affine chains only; activations and shape operators are not in it.**
/// </remarks>
public class OnnxAffineEndpointGurobiIntegrationTest
{
    /// <summary>**两输入仿射目标的最小化与手算一致** / Minimising a two-input affine objective matches hand arithmetic.</summary>
    [Fact]
    public async Task ATwoInputAffineGraph_ShouldSolveToTheHandComputedOptimum() {
        var x = new RealVar("x");
        var y = new RealVar("y");

        // 图：out = (x + y) - x  →  应当恰好等于 y。
        var graph = new OnnxGraph(
            "g",
            new[] {
                new OnnxNode("Add", "n1", new[] { "x", "y" }, new[] { "s" }, Array.Empty<OnnxAttribute>()),
                new OnnxNode("Sub", "n2", new[] { "s", "x" }, new[] { "out" }, Array.Empty<OnnxAttribute>()),
            },
            Array.Empty<OnnxTensor>(),
            Array.Empty<OnnxValueInfo>(),
            new[] { new OnnxValueInfo("out", 1, Array.Empty<OnnxDimension>()) },
            Array.Empty<OnnxValueInfo>());

        OnnxAffineChain.Result chain = OnnxAffineChain.Evaluate(
            graph,
            new Dictionary<string, RealVar> { ["x"] = x, ["y"] = y });
        chain.Failure.Should().BeNull(chain.Failure);

        var bounds = new Dictionary<string, OnnxBounds> {
            ["x"] = new(0.0, 10.0),
            ["y"] = new(-3.0, 4.0),
        };
        OnnxAffineModelBuilder.Built built = OnnxAffineModelBuilder.Build(chain.Polynomial!, bounds, ObjectCategory.Minimum);
        built.Failure.Should().BeNull(built.Failure);

        using LinearTriadModel model = built.Model!;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)model);

        result.IsOk.Should().BeTrue(result.ToString());
        double optimum = result.Value.Obj.ToDouble();
        optimum.Should().BeApproximately(-3.0, 1e-6,
            "the expression reduces to y, whose minimum over [-3, 4] is -3 — and the coefficient of x has to cancel exactly");
    }

    /// <summary>**缺区间的变量让组装失败，而不是解出一个无界极值** / A missing range fails the assembly rather than yielding an unbounded extreme.</summary>
    [Fact]
    public void AMissingRange_ShouldFailTheAssembly() {
        var x = new RealVar("x");
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, x) },
            Flt64.Zero);

        OnnxAffineModelBuilder.Built built = OnnxAffineModelBuilder.Build(
            polynomial,
            new Dictionary<string, OnnxBounds>());

        built.Model.Should().BeNull("without a range the variable would run off to infinity and the answer would be an extreme nobody asked for");
        built.Failure.Should().Contain("x");
    }
}
#endif
