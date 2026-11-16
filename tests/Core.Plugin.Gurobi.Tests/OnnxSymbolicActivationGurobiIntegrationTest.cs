#nullable enable

#if GUROBI_INTEGRATION
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// **符号激活真的挂进模型并求解** / A symbolic activation is attached to a model and solved.
/// </summary>
/// <remarks>
/// 激活降阶在模型层的那一半。**此前 `Relu(x)` 只能拒绝，因为多项式装不下"正部"：**
/// **它是分段线性的，而分段线性不是一次多项式。** **现在它被包成系数为一的单项式，展平器展开成自己的结果多项式。**
///
/// **两条判据成对测，是因为单测一条会说谎：** **只看 `[0,10]` 得 0，一个"总是 0"的实现也通过；'
/// **只看 `[5,10]` 得 2，一个"当恒等"的实现也通过。** **两条一起，才能同时排除这两种。**
///
/// Syncs the model-layer half of the activation lowering. **Until now `Relu(x)` could only be refused because a polynomial cannot hold "positive part": it is piecewise
/// linear and piecewise linear is not a first-degree polynomial.** **It is now wrapped as a monomial of coefficient one and the flattener expands it.**
///
/// **The two criteria come as a pair because either alone would lie: `[0,10]` giving 0 also passes for an implementation that always returns 0, and `[5,10]` giving 2 also
/// passes for one that treats Relu as identity.** **Together they rule both out.**
/// </remarks>
public class OnnxSymbolicActivationGurobiIntegrationTest
{
    /// <summary>**界在零附近时正部截断到 0** / With the bound near zero the positive part clamps to zero.</summary>
    [Fact]
    public async Task OnTheNegativeBranch_ShouldClampToZero() {
        GurobiSolved solved = await Solve(lower: 0.0, upper: 10.0);

        solved.Failure.Should().BeNull(solved.Failure);
        solved.Objective.Should().BeApproximately(0.0, 1e-6,
            "x - 3 is negative for most of [0,10], and the positive part is zero there, so the minimum is zero at x = 3");
    }

    /// <summary>**界全在零之上时正部就是本身** / With the bound entirely above zero the positive part is the input itself.</summary>
    /// <remarks>
    /// **这一条排除"总是 0"：** **若实现把正部当成常数零，`[5,10]` 上也会给 0；** **而正确答案是 2（`x = 5` 处 `x - 3 = 2`）。**
    ///
    /// **This rules out "always zero": were the positive part a constant zero, `[5,10]` would also give 0, while the right answer is 2 (`x - 3 = 2` at `x = 5`).**
    /// </remarks>
    [Fact]
    public async Task OnThePositiveBranch_ShouldEqualTheInput() {
        GurobiSolved solved = await Solve(lower: 5.0, upper: 10.0);

        solved.Failure.Should().BeNull(solved.Failure);
        solved.Objective.Should().BeApproximately(2.0, 1e-6,
            "x - 3 is positive throughout [5,10], so the positive part equals it and the minimum is 2 at x = 5");
    }

    /// <summary>**只有 `Relu` 走正部这条路** / Only Relu goes through the positive part.
    /// </summary>
    /// <remarks>
    /// **`Sigmoid`／`Tanh` 不是分段线性，`Relu` 是。** **把非线性但非分段的函数塞进正部，会得到一个"能求解但算的是别的函数"的模型——'
    /// **而它比拒绝更难发现。** **这条判据钉住那条界线，而不是钉住某个实现细节。**
    ///
    /// **Sigmoid and Tanh are not piecewise linear, Relu is.** **Feeding a non-piecewise function into the positive part yields a model that solves while evaluating a
    /// different function — harder to notice than a refusal.** The criterion pins that line rather than an implementation detail.
    /// </remarks>
    [Fact]
    public void OnlyRelu_ShouldTakeThisRoute() {
        OnnxSymbolicActivation.CanExpress("Relu").Should().BeTrue();
        OnnxSymbolicActivation.CanExpress("Sigmoid").Should().BeFalse("a sigmoid is not piecewise linear");
        OnnxSymbolicActivation.CanExpress("Tanh").Should().BeFalse();
        OnnxSymbolicActivation.CanExpress("Clip").Should().BeFalse("Clip may nest a piecewise shape, but Relu is the case this layer builds");
    }

    private sealed record GurobiSolved(double Objective, string? Failure);

    private static async Task<GurobiSolved> Solve(double lower, double upper) {
        using var meta = new LinearMetaModel<Flt64>();
        var x = new RealVar("x");
        var input = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
            new Flt64(-3.0));

        OnnxSymbolicActivation.Result activation = OnnxSymbolicActivation.PositivePart(input);
        // 直接说值是什么：裸的 `IsOk` 只证明"没出错"，而不证明正部真的建起来了。
        // Say what the value is: a bare IsOk only proves nothing failed, not that the positive part exists.
        activation.Symbol.Should().NotBeNull(activation.Failure);

        var objective = new LinearPolynomial<Flt64>(
            new[] { OnnxSymbolicActivation.Monomial(activation.Symbol!) },
            Flt64.Zero);

        OnnxModelAttachment.Attached attached = OnnxModelAttachment.Attach(
            meta,
            objective,
            new Dictionary<string, OnnxBounds> { ["x"] = new OnnxBounds(lower, upper) },
            ObjectCategory.Minimum,
            "relu-objective",
            new IIntermediateSymbol[] { activation.Symbol! });
        attached.Failure.Should().BeNull(attached.Failure);

        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(meta)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;

        var solved = await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        if (!solved.IsOk) {
            return new GurobiSolved(double.NaN, solved.ToString());
        }
        return new GurobiSolved(solved.Value.Obj.ToDouble(), null);
    }
}
#endif