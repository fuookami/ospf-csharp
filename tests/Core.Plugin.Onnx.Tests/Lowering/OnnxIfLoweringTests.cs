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
/// **`If` 真的展开：三件东西接起来** / The If is actually unrolled, three pieces wired together.
/// </summary>
/// <remarks>
/// `lowerIf` 落地形态：**选分支 → 压作用域 → 求值 → 弹作用域。** '
/// **"子图"在这里不再是一个特殊概念——它只是"在子上下文里运行的一张普通图"。**
///
/// Syncs the concrete `lowerIf`: **choose, push, evaluate, pop.** **A subgraph stops being a special concept: it is an ordinary graph run in a child context.**
/// </remarks>
public class OnnxIfLoweringTests
{
    /// <summary>**条件为真时算出真分支的结果** / A true condition computes the true branch.</summary>
    [Fact]
    public void ATrueCondition_ShouldComputeTheTrueBranch() {
        OnnxLoweringContext context = ContextWith("x", "y");

        OnnxIfLowering.Result result = OnnxIfLowering.Lower(
            Node(condition: 1.0, then: Branch(new[] { Node("Add", "x", "y", "sum") }, "sum"),
                 @else: Branch(new[] { Node("Sub", "x", "y", "diff") }, "diff"), outputs: new[] { "out" }),
            context);

        result.IsOk.Should().BeTrue(result.Failure);
        result.Outputs!["out"].Expression.Monomials.Should().HaveCount(2, "x and y both survive an addition");
        CoefficientOf(result.Outputs["out"].Expression, "x").Should().Be(1.0);
        CoefficientOf(result.Outputs["out"].Expression, "y").Should().Be(1.0, "an addition keeps both signs positive");
    }

    /// <summary>**条件为假时算出假分支的结果** / A false condition computes the else branch.</summary>
    [Fact]
    public void AFalseCondition_ShouldComputeTheElseBranch() {
        OnnxLoweringContext context = ContextWith("x", "y");

        OnnxIfLowering.Result result = OnnxIfLowering.Lower(
            Node(condition: 0.0, then: Branch(new[] { Node("Add", "x", "y", "sum") }, "sum"),
                 @else: Branch(new[] { Node("Sub", "x", "y", "diff") }, "diff"), outputs: new[] { "out" }),
            context);

        result.IsOk.Should().BeTrue(result.Failure);
        CoefficientOf(result.Outputs!["out"].Expression, "y").Should().Be(-1.0,
            "a subtraction flips the second sign; the same coefficient on both branches would mean the condition is ignored");
    }

    /// <summary>**结果挂在节点声明的输出名上，分支体内部名不外泄** / The result lands under the declared name.</summary>
    [Fact]
    public void TheResult_ShouldLandUnderTheDeclaredName() {
        OnnxLoweringContext context = ContextWith("x", "y");

        OnnxIfLowering.Lower(
            Node(condition: 1.0, then: Branch(new[] { Node("Add", "x", "y", "sum") }, "sum"),
                 @else: Branch(new[] { Node("Sub", "x", "y", "diff") }, "diff"), outputs: new[] { "out" }),
            context).IsOk.Should().BeTrue();

        context.TryGet("out", out _).Should().BeTrue("the outer scope sees the node's output");
        context.TryGet("sum", out _).Should().BeFalse("the body's own output name belongs to the body");
        context.Depth.Should().Be(1, "the scope was popped after the pass");
    }

    /// <summary>**分支体失败后作用域仍然弹掉** / The scope is popped even when the body fails.</summary>
    /// <remarks>
    /// **失败之后留着半张表，比失败本身更难查：** **外层会多出半张分支体的表，** **后续节点可能意外命中一个本不属于它们的名字，**
    /// **而那种错在数值上表现为"某一项莫名多出来"。**
    ///
    /// **Half a table left behind after a failure is harder to trace than the failure: the outer scope keeps part of the body's table, later nodes may hit names that never
    /// belonged to them, and that shows up numerically as "one term appeared out of nowhere".**
    /// </remarks>
    [Fact]
    public void AFailingBody_ShouldStillPopTheScope() {
        OnnxLoweringContext context = ContextWith("x");

        OnnxIfLowering.Result result = OnnxIfLowering.Lower(
            Node(condition: 1.0, then: Branch(new[] { Node("Conv", "x", "out") }, "out"),
                 @else: Branch(new[] { Node("Conv", "x", "out") }, "out"), outputs: new[] { "out" }),
            context);

        result.IsOk.Should().BeFalse("Conv is not part of node-by-node evaluation");
        context.Depth.Should().Be(1, "the scope has to be popped either way, or half a body table stays behind");
    }

    /// <summary>**条件不是常量被拒绝，且不动上下文** / A non-constant condition is refused without touching the context.</summary>
    [Fact]
    public void ANonConstantCondition_ShouldBeRefused() {
        OnnxLoweringContext context = ContextWith("x");

        OnnxIfLowering.Result result = OnnxIfLowering.Lower(
            Node(condition: 1.0, then: Branch(new[] { Node("Identity", "x", "o") }, "o"),
                 @else: Branch(new[] { Node("Identity", "x", "o") }, "o"), outputs: new[] { "out" }) with { ConditionIsConstant = false },
            context);

        result.IsOk.Should().BeFalse();
        context.Depth.Should().Be(1);
        context.TryGet("out", out _).Should().BeFalse("a refused If produces nothing");
    }

    /// <summary>**两个分支个数不同被拒绝** / Differing branch arity is refused.</summary>
    [Fact]
    public void MismatchedArity_ShouldBeRefused() {
        OnnxLoweringContext context = ContextWith("x");

        OnnxIfLowering.Result result = OnnxIfLowering.Lower(
            Node(condition: 1.0,
                 then: Branch(new[] { Node("Identity", "x", "a") }, "a"),
                 @else: Branch(new[] { Node("Identity", "x", "a"), Node("Identity", "x", "b") }, "a", "b"),
                 outputs: new[] { "out" }),
            context);

        result.IsOk.Should().BeFalse();
    }

    /// <summary>**展开出来的表达式的形状沿分支体传播** / The shape propagates from the body.</summary>
    [Fact]
    public void TheShape_ShouldComeFromTheBody() {
        var context = new OnnxLoweringContext();
        context.TryBind("x", Expression("x"), new[] { 4 }, out _);

        OnnxIfLowering.Result result = OnnxIfLowering.Lower(
            Node(condition: 1.0, then: Branch(new[] { Node("Identity", "x", "o") }, "o"),
                 @else: Branch(new[] { Node("Identity", "x", "o") }, "o"), outputs: new[] { "out" }),
            context);

        result.IsOk.Should().BeTrue(result.Failure);
        result.Outputs!["out"].Shape.Should().Equal(new[] { 4 });
    }

    private static OnnxLoweringContext ContextWith(params string[] names) {
        var context = new OnnxLoweringContext();
        foreach (string name in names) {
            context.TryBind(name, Expression(name), new[] { 2 }, out _);
        }
        return context;
    }

    private static OnnxIfExpansion.IfNode Node(double condition, OnnxGraph then, OnnxGraph @else, string[] outputs) =>
        new(condition, true, then, @else, outputs);

    private static OnnxNode Node(string opType, string a, string b, string output) =>
        new(opType, "n_" + opType, new[] { a, b }, new[] { output }, Array.Empty<OnnxAttribute>());

    private static OnnxNode Node(string opType, string a, string output) =>
        new(opType, "n_" + opType, new[] { a }, new[] { output }, Array.Empty<OnnxAttribute>());

    private static OnnxGraph Branch(OnnxNode[] nodes, params string[] outputs) {
        var infos = new OnnxValueInfo[outputs.Length];
        for (int index = 0; index < outputs.Length; ++index) {
            infos[index] = new OnnxValueInfo(outputs[index], 1, Array.Empty<OnnxDimension>());
        }
        return new OnnxGraph("branch", nodes, Array.Empty<OnnxTensor>(), Array.Empty<OnnxValueInfo>(), infos, Array.Empty<OnnxValueInfo>());
    }

    private static double CoefficientOf(LinearPolynomial<Flt64> polynomial, string symbolName) {
        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            if (monomial.Symbol.Name == symbolName) {
                return monomial.Coefficient.ToDouble();
            }
        }
        return 0.0;
    }

    private static LinearPolynomial<Flt64> Expression(string name) {
        var variable = new RealVar(name);
        return new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
            Flt64.Zero);
    }
}