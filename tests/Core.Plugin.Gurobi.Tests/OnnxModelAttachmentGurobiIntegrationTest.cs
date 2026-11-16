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
/// **挂进真实机制模型并求解** / Attach into a real mechanism model and solve.
/// </summary>
/// <remarks>
/// 这是"插件能往真实模型里加东西"的实证：**变量进令牌表、界以约束形式落地、目标设上，然后整条模型由求解器解出。**
/// **与端到端那批的区别：那批组装的是一个特制的 `LinearTriadModel`，这批走的是框架自己的 `LinearMetaModel` → `LinearMechanismModel` → 求解器。**
///
/// This is the evidence that the plugin can put something into a real model: **variables enter the token table, bounds land as constraints, the objective is set, and
/// the solver resolves the whole model.** **Unlike the earlier end-to-end batch, which assembled a purpose-built `LinearTriadModel`, this one goes through the
/// framework's own `LinearMetaModel` → `LinearMechanismModel` → solver.**
/// </remarks>
public class OnnxModelAttachmentGurobiIntegrationTest
{
    /// <summary>**最小化仿射目标，最优值与手算一致** / Minimising an affine objective matches hand arithmetic.</summary>
    [Fact]
    public async Task AnAffineObjective_ShouldSolveThroughTheFramework() {
        using var meta = new LinearMetaModel<Flt64>(configuration: new MetaModelConfiguration());
        var x = new RealVar("x");
        var y = new RealVar("y");
        var polynomial = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(new Flt64(1.0), x),
                new LinearMonomial<Flt64>(new Flt64(2.0), y),
            },
            new Flt64(5.0));

        var bounds = new Dictionary<string, OnnxBounds> {
            ["x"] = new(0.0, 10.0),
            ["y"] = new(-3.0, 4.0),
        };

        OnnxModelAttachment.Attached attached = OnnxModelAttachment.Attach(meta, polynomial, bounds);
        attached.Failure.Should().BeNull(attached.Failure);

        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(meta)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);

        solved.IsOk.Should().BeTrue(solved.ToString());
        // x 与 y 都取到下界：0 + 2·(-3) + 5 = -1。
        solved.Value.Obj.ToDouble().Should().BeApproximately(-1.0, 1e-6,
            "both variables sit at their lower bounds, and the constant does not move");
    }

    /// <summary>**界真的生效：把下界抬高，最优值随之变化** / The bounds take effect: raising one changes the optimum.</summary>
    /// <remarks>
    /// **这是"约束落地了"唯一的实证：** **若界被静默丢掉，模型照常求解，最优值仍会落在一个端点——** **只是那个端点是我们没要求的。**
    ///
    /// **This is the only evidence a bound actually landed: if it were silently dropped the model still solves and the optimum still sits at a vertex — just at a vertex
    /// nobody asked for.**
    /// </remarks>
    [Fact]
    public async Task TheBounds_ShouldActuallyBind() {
        using var meta = new LinearMetaModel<Flt64>(configuration: new MetaModelConfiguration());
        var x = new RealVar("x");
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
            Flt64.Zero);

        OnnxModelAttachment.Attached attached = OnnxModelAttachment.Attach(
            meta,
            polynomial,
            new Dictionary<string, OnnxBounds> { ["x"] = new(7.0, 10.0) });
        attached.Failure.Should().BeNull(attached.Failure);

        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(meta)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);

        solved.IsOk.Should().BeTrue(solved.ToString());
        solved.Value.Obj.ToDouble().Should().BeApproximately(7.0, 1e-6,
            "the minimum sits on the lower bound, so a dropped bound would show up as roughly -double.MaxValue or a solver complaint");
    }
}
#endif
