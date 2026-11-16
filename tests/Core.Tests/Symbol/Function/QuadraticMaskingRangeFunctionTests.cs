#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 第 3、4 行才是"掩码关掉"的关键 / Rows 3 and 4 are what make "the mask is off" mean anything.
/// </summary>
/// <remarks>
/// 对齐基线 `QuadraticMaskingRange.kt` 把原本的空壳换成真实编码。
///
/// **五条约束里，1、2 在 `z = 0` 时给 `y = p`，而 `y` 本该是 0——**
/// **所以 3、4（`y ≤ M·z`、`y ≥ −M·z`）才是这里的关键。**
///
/// Replaces a stub with the real encoding, following the baseline.
///
/// **Of the five rows, 1 and 2 give `y = p` at `z = 0` while `y` should be zero —**
/// **so rows 3 and 4 (`y ≤ M·z`, `y ≥ −M·z`) are what turns on.**
/// </remarks>
public class QuadraticMaskingRangeFunctionTests
{
    /// <summary>**`z = 1` 时给多项式值、`z = 0` 时给 0** / `z = 1` gives the polynomial, `z = 0` gives zero.</summary>
    /// <remarks>
    /// **两个分支都测**：**只测一个时，把 `z` 的分支写反的实现会通过。**
    ///
    /// **Both branches**: **with only one, an implementation whose branches are swapped passes.**
    /// </remarks>
    [Fact]
    public void TheEvaluate_ShouldMaskOrPassThrough()
    {
        QuadraticMaskingRangeFunction function = Build();
        RealVar x = InputVariable(function);

        function.Evaluate(Values(function, x, 3.0, 1.0)).Should().Be(
            new Flt64(3.0), "the mask is on, so the polynomial passes through");
        function.Evaluate(Values(function, x, 3.0, 0.0)).Should().Be(
            Flt64.Zero, "the mask is off, so the result is zero");
    }

    /// <summary>**两步多项式的值也算对** / A quadratic polynomial evaluates correctly.</summary>
    [Fact]
    public void TheEvaluate_ShouldHandleAQuadraticInput()
    {
        QuadraticMaskingRangeFunction function = BuildSquared();
        RealVar x = inputOfSquared;

        function.Evaluate(Values(function, x, 3.0, 1.0)).Should().Be(
            new Flt64(9.0), "x squared at three is nine");
        function.Evaluate(Values(function, x, 3.0, 0.0)).Should().Be(
            Flt64.Zero, "masked off");
    }

    /// <summary>**`z` 非 0 非 1 时给 `null`** / A mask that is neither zero nor one gives `null`.</summary>
    /// <remarks>
    /// **五条约束在 `z = 0.5` 时仍然可满足，给出一个看起来像插值的数。**
    /// **而语义上这个值不存在**——**所以语义路径必须显式拒绝。**
    ///
    /// **The five rows stay satisfiable at `z = 0.5`, yielding a number that looks like an interpolation.**
    /// **No such value exists semantically, so the semantic path must reject it explicitly.**
    /// </remarks>
    [Fact]
    public void TheEvaluate_ShouldRejectANonBinaryMask()
    {
        QuadraticMaskingRangeFunction function = Build();
        RealVar x = InputVariable(function);

        function.Evaluate(Values(function, x, 3.0, 0.5)).Should().BeNull(
            "half a mask is an illegal input, not an interpolation");
    }

    /// <summary>
    /// **第 3、4 行真的被注册** / Rows 3 and 4 really reach the model.
    /// </summary>
    /// <remarks>
    /// **这条是这里的核心**：**几行里 1、2 在 `z = 0` 时给 `y = p`，而 `y` 本该是 0——
    /// 只有 3、4 把它钉住。**
 /// **而"构造出第 3、4 行"与"它们被交给模型"是两件事**。
    ///
    /// **This is the point of the batch**: **rows 1 and 2 give `y = p` at `z = 0` while `y` should be zero — only 3
    /// and 4 pin it.**
 /// **And "rows 3 and 4 are built" is not "they reach the model".**
    /// </remarks>
    [Fact]
    public async Task TheRegisterConstraints_ShouldWriteAllFourRows()
    {
        QuadraticMaskingRangeFunction function = Build();
        RealVar x = InputVariable(function);

        var parent = new QuadraticMetaModel<Flt64>();
        var declared = new List<IVariableItem> { x, function.Z };
        declared.AddRange(function.HelperVariables);
        parent.Add(declared).IsOk.Should().BeTrue();

        QuadraticMechanismModel<Flt64> model =
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        int before = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Count;
        function.RegisterConstraints(model).IsOk.Should().BeTrue();

        int after = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Count;
        (after - before).Should().Be(
            4, "two through-rows and two zero-rows must all reach the model");
    }

    /// <summary>**四条行的名字与方向固定** / The four rows keep their names and directions.</summary>
    [Fact]
    public void TheRows_ShouldKeepTheirNamesAndDirections()
    {
        QuadraticMaskingRangeFunction function = Build();
        IReadOnlyList<QuadraticInequalityOf<Flt64>> rows = function.Rows();

        rows.Should().HaveCount(4);
        rows[0].Name.Should().EndWith("_upper");
        rows[1].Name.Should().EndWith("_lower");
        rows[2].Name.Should().EndWith("_zero_upper");
        rows[3].Name.Should().EndWith("_zero_lower");

        rows[0].Comparison.Should().Be(Comparison.LE);
        rows[1].Comparison.Should().Be(Comparison.GE);
        rows[2].Comparison.Should().Be(Comparison.LE);
        rows[3].Comparison.Should().Be(Comparison.GE);
    }

    /// <summary>**`z = 0` 时 3、4 行把 `y` 夹成 0** / At `z = 0` rows 3 and 4 pin `y` to zero.</summary>
    /// <remarks>
    /// **这是"掩码关掉"的定义性质**：**`y ≤ M·0 = 0` 与 `y ≥ −M·0 = 0` 合起来给 `y = 0`。**
    /// **如果判据只验证"行存在"，一条把右端写成常数的实现也能通过。**
    ///
    /// **This is the defining property of "the mask is off"**: **`y ≤ 0` and `y ≥ 0` together give `y = 0`.**
    /// **A criterion that only checks the rows exist lets an implementation with a constant right-hand side pass.**
    /// </remarks>
    [Fact]
    public void TheZeroRows_ShouldPinYToZeroAtMaskZero()
    {
        QuadraticMaskingRangeFunction function = Build();
        IReadOnlyList<QuadraticInequalityOf<Flt64>> rows = function.Rows();

        // row2 是 y ≤ M·z：z 的系数是 +M。
        // row2 is y <= M*z: the coefficient of z is +M.
        BigMCoefficient(rows[2], function).Should().BeApproximately(
            function.BigM.ToDouble(), 1e-12,
            "the zero_upper row scales the mask by plus M");

        // row3 是 y ≥ −M·z：z 的系数是 −M。
        // row3 is y >= -M*z: the coefficient of z is -M.
        BigMCoefficient(rows[3], function).Should().BeApproximately(
            -function.BigM.ToDouble(), 1e-12,
            "the zero_lower row scales the mask by minus M");
    }

    /// <summary>**大 M 必须为正** / The big-M must be positive.</summary>
    /// <remarks>
    /// **`M = 0` 时第 3、4 行变成 `y ≤ 0` 与 `y ≥ 0`，于是 `y` 恒为 0——
    /// 掩码"永远关掉"，而第 1、2 行在 `z = 1` 时又要求 `y = p`，两者冲突。**
    ///
    /// **At `M = 0` rows 3 and 4 become `y ≤ 0` and `y ≥ 0`, so `y` is always zero — the mask is "always off" —
    /// while rows 1 and 2 demand `y = p` at `z = 1`. The two conflict.**
    /// </remarks>
    [Fact]
    public void TheBigM_ShouldBePositive()
    {
        Action zero = () => _ = Build(bigM: 0.0);
        zero.Should().Throw<ArgumentException>("a zero big-M pins the result to zero");

        Action negative = () => _ = Build(bigM: -5.0);
        negative.Should().Throw<ArgumentException>("a negative big-M is not a big-M");
    }

    private static RealVar inputOfSquared = new RealVar("qmask-sq-x");

    /// <summary>`y = z · x`，`x ∈ [0, 20]` / Build one.</summary>
    private static QuadraticMaskingRangeFunction Build(double? bigM = null) {
        var x = new RealVar("qmask-x");
        x.Range.Geq(Flt64.Zero);
        x.Range.Leq(new Flt64(20.0));

        return new QuadraticMaskingRangeFunction(
            polynomial: new QuadraticPolynomial<Flt64>(
                new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, x) }, Flt64.Zero),
            z: new BinVar("qmask-z"),
            converter: IIntoValue<Flt64>.Identity,
            bigM: bigM is { } value ? new Flt64(value) : null,
            name: "qmask");
    }

    /// <summary>`y = z · x²` / Build a squared input.</summary>
    private static QuadraticMaskingRangeFunction BuildSquared() {
        inputOfSquared.Range.Geq(Flt64.Zero);
        inputOfSquared.Range.Leq(new Flt64(20.0));

        return new QuadraticMaskingRangeFunction(
            polynomial: new QuadraticPolynomial<Flt64>(
                new[] { new QuadraticMonomial<Flt64>(Flt64.One, inputOfSquared, inputOfSquared) },
                Flt64.Zero),
            z: new BinVar("qmask-sq-z"),
            converter: IIntoValue<Flt64>.Identity,
            name: "qmask_sq");
    }

    private static RealVar InputVariable(QuadraticMaskingRangeFunction function) =>
        (RealVar)function.Input.Monomials[0].Symbol1;

    private static double BigMCoefficient(
        QuadraticInequalityOf<Flt64> row, QuadraticMaskingRangeFunction function) {
        foreach (QuadraticMonomial<Flt64> monomial in row.Rhs.Monomials) {
            if (ReferenceEquals(monomial.Symbol1, function.Z) && monomial.Symbol2 is null) {
                return monomial.Coefficient.ToDouble();
            }
        }

        throw new InvalidOperationException($"row {row.Name} has no mask monomial");
    }

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(
        QuadraticMaskingRangeFunction function, RealVar variable, double value, double mask) =>
        new Dictionary<ISymbol, Flt64> {
            [variable] = new Flt64(value),
            [function.Z] = new Flt64(mask),
        };
}
