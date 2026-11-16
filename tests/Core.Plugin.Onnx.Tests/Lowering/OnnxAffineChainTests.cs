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
/// **纯仿射链：唯一能端到端验证的那部分降阶** / The affine chain: the only part of the lowering verifiable end to end.
/// </summary>
/// <remarks>
/// 降阶层里逐元素线性算子那一部分。**整条链都是仿射时结果就是一条多项式，可以直接与手算对照。**
/// **其余算子都要产生函数符号或改变形状，那一步需要模型机制**——**把两者混在一批里，会让"能不能算"永远得不到单独的证据。**
///
/// Syncs the elementwise-linear part of the lowering. **When the whole chain is affine the result is one polynomial, directly comparable with hand arithmetic.** **Every
/// other operator needs a function symbol or a shape change and therefore model machinery — mixing the two would leave "can it compute" without evidence of its own.**
/// </remarks>
public class OnnxAffineChainTests
{
    /// <summary>**加法与缩放合成一条多项式** / Addition and scaling compose into one polynomial.</summary>
    [Fact]
    public void AddAndScale_ShouldCompose() {
        var x = new RealVar("x");
        var y = new RealVar("y");
        var variables = new Dictionary<string, RealVar> { ["x"] = x, ["y"] = y };

        // x + x + (-1)·y  →  2x - y
        OnnxAffineChain.Result result = OnnxAffineChain.Evaluate(
            Graph(
                new[] { Node("Add", "x", "x", "a"), Node("Neg", "y", "b"), Node("Add", "a", "b", "out") },
                Array.Empty<OnnxTensor>()),
            variables);

        result.Failure.Should().BeNull();
        LinearPolynomial<Flt64> polynomial = result.Polynomial!;
        polynomial.Monomials.Should().HaveCount(2);
        CoefficientOf(polynomial, x).Should().Be(new Flt64(2.0));
        CoefficientOf(polynomial, y).Should().Be(new Flt64(-1.0));
        polynomial.Constant.Should().Be(Flt64.Zero);
    }

    /// <summary>**常量参与运算并落进常数项** / Constants take part and land in the constant term.</summary>
    [Fact]
    public void AConstant_ShouldLandInTheConstantTerm() {
        var x = new RealVar("x");
        var variables = new Dictionary<string, RealVar> { ["x"] = x };

        // (x + c) · k，c = 3，k = 2  →  2x + 6
        OnnxAffineChain.Result result = OnnxAffineChain.Evaluate(
            Graph(
                new[] { Node("Add", "x", "c", "s"), Node("Mul", "s", "k", "out") },
                new[] { Scalar("c", 3.0), Scalar("k", 2.0) }),
            variables);

        result.Failure.Should().BeNull();
        LinearPolynomial<Flt64> polynomial = result.Polynomial!;
        CoefficientOf(polynomial, x).Should().Be(new Flt64(2.0));
        polynomial.Constant.Should().Be(new Flt64(6.0), "two times three is six and it belongs to the constant term");
    }

    /// <summary>**两个非常量相乘被拒绝，而不是丢掉二次项** / Two non-constant factors are refused rather than dropped.</summary>
    /// <remarks>
    /// **两个非常量相乘就是二次，而这条链只表达仿射：** **"尽量算一下"会让二次项被静默丢掉，** **而模型对着一条少了一项的表达式优化出"最优解"。**
    ///
    /// **Two non-constant factors are a quadratic, and this chain only expresses affine forms: trying anyway drops the quadratic terms silently, and the model optimises to
    /// an "optimum" of an expression missing them.**
    /// </remarks>
    [Fact]
    public void MultiplyingTwoVariables_ShouldBeRefused() {
        var x = new RealVar("x");
        var y = new RealVar("y");

        OnnxAffineChain.Result result = OnnxAffineChain.Evaluate(
            Graph(new[] { Node("Mul", "x", "y", "out") }, Array.Empty<OnnxTensor>()),
            new Dictionary<string, RealVar> { ["x"] = x, ["y"] = y });

        result.Polynomial.Should().BeNull("an affine chain cannot express a product of two variables");
        result.Failure.Should().NotBeNull();
    }

    /// <summary>**未知输入被拒绝，不当成零** / An unknown input is refused, not read as zero.</summary>
    /// <remarks>
    /// **把未知输入当成零会让整条链静默少一项，** **而少掉的那一项在解里看起来完全正常。**
    ///
    /// **Treating an unknown input as zero silently drops a term, and the answer without it looks perfectly normal.**
    /// </remarks>
    [Fact]
    public void AnUnknownInput_ShouldBeRefused() {
        var x = new RealVar("x");

        OnnxAffineChain.Result result = OnnxAffineChain.Evaluate(
            Graph(new[] { Node("Add", "x", "ghost", "out") }, Array.Empty<OnnxTensor>()),
            new Dictionary<string, RealVar> { ["x"] = x });

        result.Failure.Should().Contain("ghost");
        result.Polynomial.Should().BeNull();
    }

    /// <summary>**非线性算子在这条链上被拒绝** / A non-linear operator is refused on this chain.</summary>
    [Fact]
    public void ANonLinearOperator_ShouldBeRefused() {
        var x = new RealVar("x");
        OnnxAffineChain.Result result = OnnxAffineChain.Evaluate(
            Graph(new[] { Node("Relu", "x", "out") }, Array.Empty<OnnxTensor>()),
            new Dictionary<string, RealVar> { ["x"] = x });

        result.Polynomial.Should().BeNull("Relu bends the value and cannot be part of an affine chain");
        result.Failure.Should().Contain("Relu");
    }

    /// <summary>**非标量初始化器被拒绝** / A non-scalar initialiser is refused.</summary>
    /// <remarks>
    /// **一个形状为 `[2]` 的常量会引入两个不同的值，** **而仿射链只能表达一个：** **静默取第一个会让模型少一项，而且看不出少了哪一项。**
    ///
    /// **A constant shaped `[2]` carries two different values while an affine chain expresses one: silently taking the first drops a term, with no way to see which.**
    /// </remarks>
    [Fact]
    public void ANonScalarInitialiser_ShouldBeRefused() {
        var x = new RealVar("x");
        OnnxAffineChain.Result result = OnnxAffineChain.Evaluate(
            Graph(
                new[] { Node("Add", "x", "c", "out") },
                new[] { new OnnxTensor("c", 1, new long[] { 2 }, new[] { 1.0f, 2.0f }, Array.Empty<long>(), Array.Empty<double>(), null) }),
            new Dictionary<string, RealVar> { ["x"] = x });

        result.Failure.Should().NotBeNull();
    }

    /// <summary>**多个图输出被拒绝** / More than one graph output is refused.</summary>
    [Fact]
    public void MultipleGraphOutputs_ShouldBeRefused() {
        var x = new RealVar("x");
        OnnxAffineChain.Result result = OnnxAffineChain.Evaluate(
            new OnnxGraph(
                "g",
                new[] { Node("Identity", "x", "out") },
                Array.Empty<OnnxTensor>(),
                Array.Empty<OnnxValueInfo>(),
                new[] { ValueInfo("out"), ValueInfo("second") },
                Array.Empty<OnnxValueInfo>()),
            new Dictionary<string, RealVar> { ["x"] = x });

        result.Failure.Should().NotBeNull("an affine chain produces one expression, so it cannot answer for two outputs");
    }

    private static Flt64 CoefficientOf(LinearPolynomial<Flt64> polynomial, RealVar variable) {
        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            if (ReferenceEquals(monomial.Symbol, variable)) {
                return monomial.Coefficient;
            }
        }
        return Flt64.Zero;
    }

    private static OnnxNode Node(string opType, params string[] names) {
        var inputs = new List<string>();
        for (int index = 0; index < names.Length - 1; ++index) {
            inputs.Add(names[index]);
        }
        return new OnnxNode(opType, "n_" + opType + "_" + names[^1], inputs, new[] { names[^1] }, Array.Empty<OnnxAttribute>());
    }

    private static OnnxGraph Graph(OnnxNode[] nodes, OnnxTensor[] initializers) =>
        new("g", nodes, initializers, Array.Empty<OnnxValueInfo>(), new[] { ValueInfo("out") }, Array.Empty<OnnxValueInfo>());

    private static OnnxTensor Scalar(string name, double value) =>
        new(name, 1, Array.Empty<long>(), new[] { (float)value }, Array.Empty<long>(), Array.Empty<double>(), null);

    private static OnnxValueInfo ValueInfo(string name) => new(name, 1, Array.Empty<OnnxDimension>());
}
