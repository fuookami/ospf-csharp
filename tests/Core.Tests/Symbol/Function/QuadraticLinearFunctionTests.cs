#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 线性输入什么都不做 / A linear input does nothing.
/// </summary>
/// <remarks>
/// 对齐把空壳换成真实编码。
///
/// **线性输入时不创建 `y`、不写约束、辅助变量为空**——
/// **一条 `y = p` 的等式对本来就是线性的 `p` 只是把 `p` 抄了一遍。**
///
/// Replaces a stub with the real encoding.
///
/// **A linear input creates no `y`, writes no row and has no helper** —
/// **a `y = p` equality over an already-linear `p` only copies it.**
/// </remarks>
public class QuadraticLinearFunctionTests
{
    /// <summary>**线性输入不产生桥接变量** / A linear input produces no bridge.</summary>
    /// <remarks>
    /// **这条判据必须同时看"辅助变量为空"与"注册不写行"**：**只看"注册成功"是不够的，
    /// 因为"多写了一条等式"也注册成功。**
    ///
    /// **This criterion must look at both "no helper" and "no row written"**: **"registration succeeds" alone is not
    /// enough, since an extra row also succeeds.**
    /// </remarks>
    [Fact]
    public async Task TheLinearInput_ShouldNeedNoBridgeAndWriteNoRow()
    {
        QuadraticLinearFunction function = BuildLikelyLinear();

        function.IsLinear.Should().BeTrue("the polynomial has no quadratic monomial");
        function.Bridge.Should().BeNull("a bridge over a linear form is redundant");
        function.HelperVariables.Should().BeEmpty();
        function.BridgeRow().Should().BeNull("there is no equality to write");

        var parent = new QuadraticMetaModel<Flt64>();
        parent.Add(new IVariableItem[] { InputVariable(function) }).IsOk.Should().BeTrue();
        QuadraticMechanismModel<Flt64> model =
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        int before = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Count;
        function.RegisterConstraints(model).IsOk.Should().BeTrue();
        int after = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Count;

        (after - before).Should().Be(0, "a linear input adds nothing to the model");
    }

    /// <summary>**非线性输入产生桥接变量与一条等式** / A quadratic input gets a bridge and one equality.</summary>
    [Fact]
    public async Task TheQuadraticInput_ShouldBridgeWithOneEquality()
    {
        QuadraticLinearFunction function = BuildSquared();

        function.IsLinear.Should().BeFalse("the polynomial has a quadratic monomial");
        function.Bridge.Should().NotBeNull();
        function.HelperVariables.Should().HaveCount(1, "only the bridge is auxiliary");

        function.BridgeRow().Should().NotBeNull();
        function.BridgeRow()!.Comparison.Should().Be(
            Fuookami.Ospf.Math.Symbol.Inequality.Comparison.EQ,
            "the bridge is an equality, not an inequality");

        var parent = new QuadraticMetaModel<Flt64>();
        var declared = new List<IVariableItem> { InputVariable(function) };
        declared.AddRange(function.HelperVariables);
        parent.Add(declared).IsOk.Should().BeTrue();
        QuadraticMechanismModel<Flt64> model =
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        int before = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Count;
        function.RegisterConstraints(model).IsOk.Should().BeTrue();
        int after = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Count;

        (after - before).Should().Be(1, "exactly one bridge equality");
    }

    /// <summary>
    /// **负值不被截断** / Negative values are not clipped.
    /// </summary>
    /// <remarks>
    /// **注释明确记了这件事**：**早先的 `URealVar` 会在求解器边界把负值静默截断成 0**——
    /// **于是同一个多项式在负半轴上的结果全错，而模型照样求解成功。**
    /// **判据直接求一个负值，验证它原样返回。**
    ///
    /// **The code's comment records this**: **an earlier `URealVar` silently clipped negatives to zero at the
    /// solver boundary** — **so the polynomial was wrong on its whole negative half while the model still solved
    /// fine.** **The criterion evaluates a negative value and checks it comes back unchanged.**
    /// </remarks>
    [Fact]
    public void TheEvaluate_ShouldNotClipNegativeValues()
    {
        QuadraticLinearFunction function = BuildSquared();
        RealVar x = InputVariable(function);

        // x² 在 x = −3 时是 9；用线性多项式更直接：−3 → −3。
        // x^2 at -3 is 9; a linear form is more direct: -3 stays -3.
        QuadraticLinearFunction signed = BuildSigned();
        RealVar signedVariable = InputVariable(signed);

        signed.Evaluate(SignedValues(signedVariable, -3.0)).Should().Be(
            new Flt64(-3.0), "a signed polynomial must keep its sign");
        function.Evaluate(Values(x, -3.0)).Should().Be(
            new Flt64(9.0), "the square of minus three is nine, not zero");
    }

    /// <summary>**求值就是多项式本身** / Evaluation is the polynomial itself.</summary>
    [Fact]
    public void TheEvaluate_ShouldBeThePolynomial()
    {
        QuadraticLinearFunction function = BuildSquared();
        RealVar x = InputVariable(function);

        function.Evaluate(Values(x, 3.0)).Should().Be(new Flt64(9.0));
        function.Evaluate(Values(x, 0.0)).Should().Be(Flt64.Zero);
    }

    /// <summary>`y = x`：线性输入 / `y = x`: a linear input.</summary>
    private static QuadraticLinearFunction BuildLikelyLinear() =>
        new(
            polynomial: Linear(),
            converter: IIntoValue<Flt64>.Identity,
            name: "qlinear");

    /// <summary>`y = x²`：非线性输入 / `y = x^2`: a quadratic input.</summary>
    private static QuadraticLinearFunction BuildSquared() {
        var x = new RealVar("qlinear-sq-x");
        return new QuadraticLinearFunction(
            polynomial: new QuadraticPolynomial<Flt64>(
                new[] { new QuadraticMonomial<Flt64>(Flt64.One, x, x) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            name: "qlinear_sq");
    }

    /// <summary>`y = x`，用于测负值 / `y = x`, for the sign check.</summary>
    private static QuadraticLinearFunction BuildSigned() =>
        new(
            polynomial: Linear(),
            converter: IIntoValue<Flt64>.Identity,
            name: "qlinear_signed");

    private static QuadraticPolynomial<Flt64> Linear() =>
        new(new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, new RealVar("qlinear-x")) }, Flt64.Zero);

    private static RealVar InputVariable(QuadraticLinearFunction function) =>
        (RealVar)function.Polynomial.Monomials[0].Symbol1;

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(RealVar variable, double value) =>
        new Dictionary<ISymbol, Flt64> { [variable] = new Flt64(value) };

    private static IReadOnlyDictionary<ISymbol, Flt64> SignedValues(RealVar variable, double value) =>
        new Dictionary<ISymbol, Flt64> { [variable] = new Flt64(value) };
}
