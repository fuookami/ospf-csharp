#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 一条严格正条件 <c>polynomial &gt; 0</c> / One strictly positive condition.
/// </summary>
/// <remarks>
/// B1168 按基线 `DiscreteCondition.kt` 的 `StrictPositiveCondition` 补。
///
/// **它只是一个包装**：**`polynomial &gt; 0` 这件事全靠调用方按约定解释**，
/// **记录本身不做任何检查。** **这正是它的用处——把一个"约定"变成类型。**
///
/// B1168, following the baseline's `StrictPositiveCondition`.
///
/// **It is only a wrapper**: **the meaning `polynomial &gt; 0` is a convention the caller must honour**, and the record
/// itself checks nothing. **That is exactly its point — turning a convention into a type.**
/// </remarks>
/// <param name="Polynomial">必须严格为正的多项式 / The polynomial that must be strictly positive.</param>
public sealed record StrictPositiveCondition(LinearPolynomial<Flt64> Polynomial);

/// <summary>
/// 把离散条件转成严格正形式 <c>q &gt; 0</c> / Turn a discrete condition into a strictly positive one.
/// </summary>
/// <remarks>
/// B1168 按基线 `DiscreteCondition.kt` 的 `toStrictPositiveCondition` 两个重载补。
///
/// **变换规则**：
///
/// | 关系 | 变换 | 严格正形式 |
/// | --- | --- | --- |
/// | `GT`（`p &gt; 0`） | 不变 | `p &gt; 0` |
/// | `GE`（`p ≥ 0`） | `p + δ` | `p + δ &gt; 0` |
/// | `LT`（`p &lt; 0`） | `−p` | `−p &gt; 0` |
/// | `LE`（`p ≤ 0`） | `−p + δ` | `−p + δ &gt; 0` |
///
/// **`GE` 那一行是本批唯一真正需要解释的**：**`p ≥ 0` 与 `p &gt; 0` 不是同一个条件**，
/// **而 `p + δ &gt; 0` 等价于 `p &gt; −δ`。**
/// **这两者只在 <b>`p` 的取值落在步长为 `δ` 的格点上</b>时相同**——
/// **因为没有落在 `(−δ, 0)` 里的格点。**
///
/// **所以当 `δ ≠ 1` 时，基线要求"证明 `δ` 与 `p` 的格点对齐"**，
/// **证不出来就拒绝**。**`δ = 1` 时不需要证：整数变量的步长本来就是 1。**
///
/// **这个拒绝很重要**：**`δ = 0.5` 而变量是整数的情形下，
/// `p + 0.5 &gt; 0` 与 `p ≥ 0` 恰好等价；换成步长是 1 的变量就仍然等价；
/// 但如果变量实际能取到 `−0.25`，等价性就没了，而模型仍然可行。**
///
/// B1168, following the baseline's two `toStrictPositiveCondition` overloads.
///
/// **The rules**:
///
/// | Relation | Transform | Strictly positive form |
/// | --- | --- | --- |
/// | `GT` (`p &gt; 0`) | none | `p &gt; 0` |
/// | `GE` (`p ≥ 0`) | `p + δ` | `p + δ &gt; 0` |
/// | `LT` (`p &lt; 0`) | `−p` | `−p &gt; 0` |
/// | `LE` (`p ≤ 0`) | `−p + δ` | `−p + δ &gt; 0` |
///
/// **The `GE` row is the only one needing explanation**: **`p ≥ 0` and `p &gt; 0` are not the same condition**, and
/// **`p + δ &gt; 0` is equivalent to `p &gt; −δ`.**
/// **The two agree only when <b>`p` takes values on a lattice of spacing `δ`</b>** — **because then no lattice point
/// lies in `(−δ, 0)`.**
///
/// **So for `δ ≠ 1` the baseline demands a proof that `δ` aligns with the lattice**, and **rejects when it cannot be
/// shown.** **At `δ = 1` no proof is needed: an integer variable's spacing is one by definition.**
///
/// **That rejection matters**: **if the variables could actually reach `−0.25`, the equivalence disappears while the
/// model stays feasible.**
/// </remarks>
public static class StrictPositiveTransformation
{
    /// <summary>把多项式按关系转成严格正形式 / Convert a polynomial under a relation.</summary>
    public static Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> ToStrictPositive(
        LinearPolynomial<Flt64> polynomial,
        Comparison relation,
        Flt64 delta) {
        ArgumentNullException.ThrowIfNull(polynomial);

        // **`EQ`/`NE` 没有严格正形式**：**`p = 0` 既不是 `p &gt; 0` 也不是 `p &lt; 0`，
        // 而 `p ≠ 0` 是"或者严格正或者严格负"，一个严格正条件表达不了。**
        // **返回失败而不是挑一个方向**——**挑方向会让一半的解消失，而模型仍然可行。**
        //
        // **`EQ`/`NE` have no strictly positive form**: **`p = 0` is neither `p &gt; 0` nor `p &lt; 0`, and `p ≠ 0` is
        // "strictly positive or strictly negative", which one strictly positive condition cannot express.**
        // **Return a failure rather than picking a side** — **picking would delete half the solutions while the model
        // stays feasible.**
        if (relation is not (Comparison.GT or Comparison.GE or Comparison.LT or Comparison.LE)) {
            return Fail($"relation {relation} has no strictly positive form");
        }

        if (!ConditionPolynomials.IsUsable(polynomial)) {
            return Fail("the polynomial must have finite, encodable coefficients");
        }

        if (!BigMValues.IsUsable(delta) || delta.CompareTo(Flt64.Zero) <= 0) {
            return Fail("delta must be finite and greater than zero");
        }

        Result<Success, ErrorCode, Error<ErrorCode>> proof = ProveShift(polynomial, relation, delta);
        if (proof is Failed<Success, ErrorCode, Error<ErrorCode>> proofFailure) {
            return new Failed<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>(proofFailure.Error);
        }

        bool negate = relation is Comparison.LT or Comparison.LE;
        LinearPolynomial<Flt64> transformed = negate ? Negate(polynomial) : polynomial;

        if (relation is Comparison.GE or Comparison.LE) {
            transformed = Shift(transformed, delta);
        }

        if (!ConditionPolynomials.IsUsable(transformed)) {
            return Fail("the transformed polynomial must have finite, encodable coefficients");
        }

        return new Ok<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>(transformed);
    }

    /// <summary>把线性不等式转成严格正形式 / Convert an inequality.</summary>
    /// <remarks>
    /// **先算 `lhs − rhs`，再按 `comparison` 走上面那条路径。**
    /// **`lhs − rhs` 的减法不能只减 monomial 而漏掉常数项。**
    ///
    /// **Computes `lhs − rhs` first, then follows the same path.** **The subtraction must flip the constant term too.**
    /// </remarks>
    public static Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> ToStrictPositive(
        LinearInequality<Flt64> inequality,
        Flt64 delta) {
        ArgumentNullException.ThrowIfNull(inequality);

        var monomials = new List<LinearMonomial<Flt64>>(
            inequality.Lhs.Monomials.Count + inequality.Rhs.Monomials.Count);

        foreach (LinearMonomial<Flt64> monomial in inequality.Lhs.Monomials) {
            monomials.Add(monomial);
        }

        foreach (LinearMonomial<Flt64> monomial in inequality.Rhs.Monomials) {
            monomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol));
        }

        return ToStrictPositive(
            new LinearPolynomial<Flt64>(monomials, inequality.Lhs.Constant.Minus(inequality.Rhs.Constant)),
            inequality.Comparison,
            delta);
    }

    /// <summary>把多项式按关系直接转成 <see cref="StrictPositiveCondition"/> / Produce the wrapper.</summary>
    public static Result<StrictPositiveCondition, ErrorCode, Error<ErrorCode>> ToCondition(
        LinearPolynomial<Flt64> polynomial,
        Comparison relation,
        Flt64 delta) {
        Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> transformed =
            ToStrictPositive(polynomial, relation, delta);

        return transformed is Ok<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> ok
            ? new Ok<StrictPositiveCondition, ErrorCode, Error<ErrorCode>>(
                new StrictPositiveCondition(ok.Value))
            : transformed.Map<StrictPositiveCondition>(_ => null!);
    }

    /// <summary>
    /// 证明 <c>δ</c> 与多项式的格点对齐 / Prove that `δ` aligns with the polynomial's lattice.
    /// </summary>
    /// <remarks>
    /// **`GT`/`LT` 不需要证明**（**它们不平移**），**直接通过。**
    ///
    /// **`GE`/`LE` 需要**：
    /// **① 所有非零系数都必须是整数值，且它们乘的变量是整数型——
    /// 这时格点步长是"这些系数的最大公约数"；**
    /// **② `δ` 不得超过那个步长。**
    ///
    /// **条件 ① 不成立时（有非整数系数、或变量不是整数型），退回一个更弱的检查**：
    /// **常数项必须与 0 同侧或离得够远**（`GE` 时 `c ≥ 0` 或 `c ≤ −δ`）。
    /// **这个退化检查较弱，但它是基线给出的全部保证**——
    /// **也就是说这种情形下"等价性"并没有被真正证明，只是没有被证伪。**
    ///
    /// **`GT`/`LT` need no proof** (they do not shift) and **pass immediately.**
    ///
    /// **`GE`/`LE` do**: **① every non-zero coefficient must be integer-valued and multiply an integer-typed variable —
    /// then the lattice spacing is their greatest common divisor; ② `δ` must not exceed that spacing.**
    ///
    /// **When ① fails, a weaker check applies**: **the constant must sit on the same side of zero or far enough away**
    /// (`c ≥ 0` or `c ≤ −δ` for `GE`). **That check is weak, but it is all the baseline guarantees** —
    /// **the equivalence is not proved there, merely not disproved.**
    /// </remarks>
    private static Result<Success, ErrorCode, Error<ErrorCode>> ProveShift(
        LinearPolynomial<Flt64> polynomial,
        Comparison relation,
        Flt64 delta) {
        if (relation is Comparison.GT or Comparison.LT) {
            return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
        }

        double spacing = 0.0;
        bool latticeKnown = true;

        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            double coefficient = monomial.Coefficient.ToDouble();

            // **零系数不参与格点**：**它对可取值没有约束。**
            // A zero coefficient says nothing about the lattice.
            if (coefficient == 0.0) {
                continue;
            }

            if (!IsIntegerValued(coefficient) || monomial.Symbol is not IVariableItem variable ||
                !variable.TypeKind.IsIntegerType) {
                latticeKnown = false;
                break;
            }

            double magnitude = global::System.Math.Abs(coefficient);
            spacing = spacing == 0.0 ? magnitude : GreatestCommonDivisor(spacing, magnitude);
        }

        double deltaValue = delta.ToDouble();

        if (!latticeKnown) {
            // **退化检查**：**常数项与 0 同侧，或离 0 至少 δ。**
            // The degenerate check: the constant is on the same side of zero, or at least delta away.
            double constant = polynomial.Constant.ToDouble();
            bool safe = relation is Comparison.GE
                ? constant >= 0.0 || constant <= -deltaValue
                : constant <= 0.0 || constant >= deltaValue;

            return safe
                ? new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success())
                : FailProof("GE/LE shifting needs a provable lattice alignment; this polynomial has none");
        }

        // **没有非零系数时格点退化**：**多项式是常数，`p ≥ 0` 与 `p + δ &gt; 0` 是否等价只取决于那个常数。**
        // With no non-zero coefficients the lattice degenerates: the polynomial is a constant.
        if (spacing == 0.0) {
            double constant = polynomial.Constant.ToDouble();
            bool safe = relation is Comparison.GE
                ? constant >= 0.0 || constant <= -deltaValue
                : constant <= 0.0 || constant >= deltaValue;

            return safe
                ? new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success())
                : FailProof("a constant condition needs its constant on the same side of zero, or at least delta away");
        }

        if (spacing < deltaValue) {
            return FailProof(
                $"delta ({deltaValue}) exceeds the condition lattice spacing ({spacing}), so the shift is not provably safe");
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }

    /// <summary>是否为整数值 / Whether the value is integral.</summary>
    private static bool IsIntegerValued(double value) =>
        double.IsFinite(value) && global::System.Math.Floor(value) == value;

    /// <summary>两个正数的最大公约数 / The greatest common divisor of two positive values.</summary>
    /// <remarks>
    /// **用刻度化避免浮点余数误差**：**直接取模做欧几里得会在 `0.1` 这类值上停在误差里。**
    ///
    /// **Scaling avoids the floating-point remainder error**: **a direct Euclidean loop stalls in the error for values
    /// like `0.1`.**
    /// </remarks>
    private static double GreatestCommonDivisor(double left, double right) {
        double a = left;
        double b = right;

        for (int guard = 0; guard < 64 && b > 1e-12; ++guard) {
            double remainder = a - b * global::System.Math.Floor(a / b);
            if (remainder < 1e-12) {
                return b;
            }

            a = b;
            b = remainder;
        }

        return b > 1e-12 ? b : a;
    }

    /// <summary>对多项式取负 / Negate a polynomial.</summary>
    private static LinearPolynomial<Flt64> Negate(LinearPolynomial<Flt64> polynomial) {
        var monomials = new List<LinearMonomial<Flt64>>(polynomial.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            monomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol));
        }

        return new LinearPolynomial<Flt64>(monomials, polynomial.Constant.Negate());
    }

    /// <summary>平移常数项 / Shift the constant term.</summary>
    private static LinearPolynomial<Flt64> Shift(LinearPolynomial<Flt64> polynomial, Flt64 delta) =>
        new(polynomial.Monomials, polynomial.Constant.Plus(delta));

    private static Result<Success, ErrorCode, Error<ErrorCode>> FailProof(string message) =>
        new Failed<Success, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);

    private static Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        new Failed<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);
}
