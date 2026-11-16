#nullable enable

#if GUROBI_INTEGRATION
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// **分段近似的 Sigmoid 真的被求解** / A piecewise-approximated Sigmoid is really solved.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxActivation.approximated`。**判据设计成一个"三种错法都能区分开"的形状：** '
/// **正确解在左端点，目标值约 `0.0067`（`sigmoid(-5)`，而且 `-5` 就是第一个断点，插值在那里是精确的）。**
/// **若近似被丢掉当恒等，最小值是 `-5`；若被换成常数零，最小值是 `0`；若分段漏掉负半轴，最小值会停在 0 附近而不是 0.0067。**
///
/// Syncs `approximated`. **The criterion is shaped so that three different mistakes separate: the right answer sits on the left endpoint with an objective of about 0.0067
/// (sigmoid(-5), and −5 is the first breakpoint, where the interpolation is exact). Treating the approximation as identity gives −5; replacing it with constant zero gives 0;
/// a piecewise that misses the negative half stops near 0 instead of 0.0067.**
/// </remarks>
public class OnnxPiecewiseActivationGurobiIntegrationTest
{
    /// <summary>**最小值落在左端点，取值是 `sigmoid(-5)`** / The minimum sits on the left endpoint at sigmoid(-5).</summary>
    [Fact]
    public async Task TheApproximatedSigmoid_ShouldSitOnTheLeftEndpoint() {
        using var meta = new LinearMetaModel<Flt64>();
        var x = new RealVar("x");
        meta.Add(x).IsFailed.Should().BeFalse("the variable has to be in the token table before anything references it");

        var input = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
            Flt64.Zero);
        var bounds = new OnnxBounds(-5.0, 5.0);

        OnnxSymbolicActivation.Result activation = OnnxSymbolicActivation.Piecewise("Sigmoid", input, bounds);
        activation.Symbol.Should().NotBeNull(activation.Failure);

        var objective = new LinearPolynomial<Flt64>(
            new[] { OnnxSymbolicActivation.Monomial(activation.Symbol!) },
            Flt64.Zero);

        OnnxModelAttachment.Attached attached = OnnxModelAttachment.Attach(
            meta,
            objective,
            new Dictionary<string, OnnxBounds> { ["x"] = bounds },
            ObjectCategory.Minimum,
            "sigmoid-objective",
            new Fuookami.Ospf.Core.Symbol.IIntermediateSymbol[] { activation.Symbol! });
        attached.Failure.Should().BeNull(attached.Failure);

        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(meta)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);

        solved.IsOk.Should().BeTrue(solved.ToString());
        solved.Value.Obj.ToDouble().Should().BeApproximately(0.006692850924284855, 1e-4,
            "the minimum of an increasing sigmoid on [-5,5] is sigmoid(-5), which is the first breakpoint and therefore exact in the approximation");
    }
}
#endif