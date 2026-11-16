#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **逐节点求值：图第一次被"走过去"** / Node-by-node evaluation: the graph is walked for the first time.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 子图体递归降阶的第一刀。**这一批引入了前面所有批次都没有的东西：** **图被逐节点走过，中间结果留在上下文里。**
/// **那就是子图展开能成立的形式——`If` 选中分支体后，把外层可见张量灌进上下文，再对分支体调用这里。**
///
/// Syncs the first cut of recursive body lowering. **It brings what no earlier batch had: the graph is walked node by node and intermediates stay in the context.** **That is
/// the shape subgraph unrolling needs.**
/// </remarks>
public class OnnxGraphEvaluatorTests
{
    /// <summary>**一条链走完，输出可取** / A chain is walked and the output is available.</summary>
    [Fact]
    public void AChain_ShouldEvaluateToAnOutput() {
        var context = new OnnxLoweringContext();
        context.TryBind("x", Expression("x"), new[] { 3 }, out _);
        context.TryBind("y", Expression("y"), new[] { 3 }, out _);

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Add", "x", "y", "s"), Node("Neg", "s", "out") }, "out"),
            context);

        result.IsOk.Should().BeTrue(result.Failure);
        result.Outputs!["out"].Shape.Should().Equal(new[] { 3 }, "the shape propagates through the broadcast");
        result.Outputs["out"].Expression.Monomials.Should().HaveCount(2, "both variables survive the chain");
    }

    /// <summary>**中间结果留在上下文里** / Intermediates stay in the context.</summary>
    [Fact]
    public void Intermediates_ShouldStayInTheContext() {
        var context = new OnnxLoweringContext();
        context.TryBind("x", Expression("x"), new[] { 2 }, out _);

        OnnxGraphEvaluator.Evaluate(Graph(new[] { Node("Identity", "x", "mid"), Node("Neg", "mid", "out") }, "out"), context)
            .IsOk.Should().BeTrue();

        context.TryGet("mid", out _).Should().BeTrue("the intermediate is what a subgraph body leaves behind for its own later nodes");
    }

    /// <summary>**未知输入被拒绝并点出节点** / An unknown input is refused and the node named.</summary>
    [Fact]
    public void AnUnknownInput_ShouldBeRefused() {
        // x 必须真的绑上：否则先报的是 x 找不到，而这条判据要验的是 ghost。
        // x has to be genuinely bound, or the missing-input report names x while this criterion is about ghost.
        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Add", "x", "ghost", "out") }, "out"),
            ContextWith("x"));

        result.IsOk.Should().BeFalse();
        result.Failure.Should().Contain("ghost");
        result.NodeName.Should().NotBeNull("in a body of a few hundred nodes the failing node has to be named");
    }

    /// <summary>**未接入的算子被拒绝，不静默当恒等** / An unsupported operator is refused rather than treated as identity.</summary>
    /// <remarks>
    /// **静默当恒等会让激活、归约这些真正改变数值的算子全部消失，** **而模型照常求解、照常给出"最优解"。**
    ///
    /// **Treating them as identity would erase activations and reductions — everything that actually changes the value — and the model still solves and still returns an
    /// "optimum".**
    /// </remarks>
    [Fact]
    public void AnUnsupportedOperator_ShouldBeRefused() {
        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Conv", "x", "out") }, "out"),
            ContextWith("x"));

        result.IsOk.Should().BeFalse();
        result.Failure.Should().Contain("Conv");
    }

    /// <summary>**形状不兼容的广播被拒绝** / Incompatible broadcast is refused.</summary>
    [Fact]
    public void IncompatibleBroadcast_ShouldBeRefused() {
        var context = new OnnxLoweringContext();
        context.TryBind("x", Expression("x"), new[] { 3 }, out _);
        context.TryBind("y", Expression("y"), new[] { 4 }, out _);

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Add", "x", "y", "out") }, "out"),
            context);

        result.IsOk.Should().BeFalse("three and four cannot broadcast");
    }

    /// <summary>**两个非常量相乘被拒绝** / Multiplying two non-constants is refused.</summary>
    [Fact]
    public void MultiplyingTwoVariables_ShouldBeRefused() {
        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Mul", "x", "y", "out") }, "out"),
            ContextWith("x", "y"));

        result.IsOk.Should().BeFalse("this evaluation only expresses affine forms; a product would be quadratic");
    }

    /// <summary>**常量参与乘法可以** / A constant factor is fine.</summary>
    [Fact]
    public void AConstantFactor_ShouldScale() {
        var context = new OnnxLoweringContext();
        context.TryBind("x", Expression("x"), new[] { 2 }, out _);

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Mul", "x", "k", "out") }, "out"),
            context,
            new Dictionary<string, double> { ["k"] = 2.5 });

        result.IsOk.Should().BeTrue(result.Failure);
        result.Outputs!["out"].Expression.Monomials[0].Coefficient.ToDouble().Should().Be(2.5);
    }

    /// <summary>**图输出没有被产出时被拒绝** / An unproduced graph output is refused.</summary>
    [Fact]
    public void AnUnproducedOutput_ShouldBeRefused() {
        var context = new OnnxLoweringContext();
        context.TryBind("x", Expression("x"), new[] { 1 }, out _);

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Identity", "x", "mid") }, "out"),
            context);

        result.IsOk.Should().BeFalse();
        result.Failure.Should().Contain("out");
    }

    /// <summary>**常量输入的激活被折成一个数** / An activation over a constant folds into a number.</summary>
    [Fact]
    public void AConstantActivation_ShouldFold() {
        var context = new OnnxLoweringContext();

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Identity", "k", "out") }, "out"),
            context,
            new Dictionary<string, double> { ["k"] = -3.0 });

        result.IsOk.Should().BeTrue(result.Failure);
        result.Outputs!["out"].Expression.Constant.ToDouble().Should().Be(-3.0, "identity is the baseline for the activation cases");
    }

    /// <summary>**`Relu`／`Sigmoid`／`Clip` 在常量上算出正确的数** / Relu, Sigmoid and Clip compute the right numbers on constants.</summary>
    /// <remarks>
    /// **这是"能折叠"与"折对了"的区别：** **一条 `Relu` 折成输入本身也能跑通，** **只是负半轴没被截断——** **而那种错在数值上就是"结果偏小"。**
    ///
    /// **This is the difference between "it folds" and "it folds right": folding Relu into the input also runs, the negative half just never gets clipped — and that error
    /// shows up numerically as "the result is too small".**
    /// </remarks>
    [Fact]
    public void TheActivationValues_ShouldBeCorrect() {
        ConstantFold("Relu", 3.0).Should().Be(3.0);
        ConstantFold("Relu", -3.0).Should().Be(0.0, "the negative half is clipped, not passed through");
        ConstantFold("Sigmoid", 0.0).Should().Be(0.5);
        ConstantFold("Tanh", 0.0).Should().Be(0.0);
        ConstantFold("Softplus", 0.0).Should().BeApproximately(System.Math.Log(2.0), 1e-12);
        ConstantFold("Elu", -1.0).Should().BeApproximately(System.Math.Exp(-1.0) - 1.0, 1e-12);
    }

    /// <summary>**符号输入的激活被明确拒绝，而不是原样返回** / A symbolic activation is refused rather than passed through.</summary>
    /// <remarks>
    /// **原样返回会让激活从模型里消失，** **而模型照常求解、照常给出"最优解"——** **只是它优化的是一个没有激活的网络。** 判据钉住这条边界的措辞，'
    /// **因为"为什么不能用"和"哪里能用"一样重要。**
    ///
    /// **Passing it through erases the activation, and the model still solves and still returns an "optimum" — it just optimises a network without that activation.** The
    /// criterion pins the wording of the boundary, because "why it cannot" matters as much as "where it can".
    /// </remarks>
    [Fact]
    public void ASymbolicActivation_ShouldBeRefused() {
        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Sigmoid", "x", "out") }, "out"),
            ContextWith("x"));

        result.IsOk.Should().BeFalse("a variable input needs a function symbol, which belongs to the model layer");
        result.Failure.Should().Contain("函数符号");
    }

    private static double ConstantFold(string opType, double value) {
        OnnxLoweringContext.Binding? produced = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node(opType, "k", "out") }, "out"),
            new OnnxLoweringContext(),
            new Dictionary<string, double> { ["k"] = value }).Outputs?["out"];
        produced.Should().NotBeNull("folding a constant activation has to produce a binding");
        return produced!.Expression.Constant.ToDouble();
    }

    private static OnnxLoweringContext ContextWith(params string[] names) {
        var context = new OnnxLoweringContext();
        foreach (string name in names) {
            context.TryBind(name, Expression(name), new[] { 1 }, out _);
        }
        return context;
    }

    private static OnnxNode Node(string opType, string a, string b, string output) =>
        new(opType, "n_" + opType, new[] { a, b }, new[] { output }, Array.Empty<OnnxAttribute>());

    private static OnnxNode Node(string opType, string a, string output) =>
        new(opType, "n_" + opType, new[] { a }, new[] { output }, Array.Empty<OnnxAttribute>());

    private static OnnxGraph Graph(OnnxNode[] nodes, string output) =>
        new("g", nodes, Array.Empty<OnnxTensor>(), Array.Empty<OnnxValueInfo>(),
            new[] { new OnnxValueInfo(output, 1, Array.Empty<OnnxDimension>()) }, Array.Empty<OnnxValueInfo>());

    private static LinearPolynomial<Flt64> Expression(string name) {
        var variable = new RealVar(name);
        return new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
            Flt64.Zero);
    }
}
