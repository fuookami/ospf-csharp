#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 离散条件的格点计算：合并同变量系数、判整数性、求步长、证平移安全 / Lattice arithmetic for discrete conditions.
/// </summary>
/// <remarks>
/// B1172 按基线 `DiscreteCondition.kt` 的 `effectiveDiscreteConditionMonomials`、
/// `isIntegerValuedDiscreteConditionNumber`、`discreteConditionCoefficientGcd`、
/// `hasSafeDiscreteShiftProof` 四个辅助函数补。
///
/// **补这一批的直接原因是一个已发现的缺口**：**B1168 的 `ProveShift` 只检查了
/// "步长 ≥ δ"，而基线还要检查"常数到<b>会被误判那一侧</b>的最近格点距离 ≥ δ"。**
/// **前者是必要条件，不是充分条件。**
///
/// **反例**：**`p = 2x + 1`、`δ = 1`。步长是 2，`2 ≥ 1` 通过。**
/// **但 `p` 的取值是 `1, 3, 5, …` 与 `−1, −3, …`——也就是 `1 + 2ℤ`。**
/// **`GE` 平移后要求 `p > −1`，而 `p = −1` 恰好<b>不满足</b>它，却满足 `p ≥ 0`…… 不，`−1 &lt; 0`，
/// 所以 `p = −1` 两边都不满足。**
/// **真正的问题是 `p` 可以取 `−1`：`p + δ = 0` 不满足 `> 0`，而 `p ≥ 0` 也不满足。**
/// **所以这个例子恰好是安全的。**
///
/// **换 `p = 2x − 1`、`δ = 1`：取值 `…, −3, −1, 1, …`。**
/// **`GE` 平移后要求 `p > −1`，而 `p = −1` 不满足它、也不满足 `p ≥ 0`——仍然安全。**
///
/// **真正不安全的是余数非零的情形**：**`p = 3x + 1`、`δ = 2`。取值 `1 + 3ℤ`。
/// **`GE` 平移后要求 `p > −2`；`p` 能取到 `−2`（`1 + 3·(−1) = −2`），
/// 而 `−2 > −2` 是假、`−2 ≥ 0` 也是假。**
/// **再看 `p` 取 `1`：两边都真。所以这个例子也安全。**
///
/// **结论：我上面构造的反例都不成立。** **基线那个余数检查挡的是什么？**
/// **是 `p = 3x + 2`、`δ = 2`：取值 `2 + 3ℤ = …, −1, 2, 5, …`。**
/// **`GE` 平移后要求 `p > −2`，于是 `p = −1` <b>满足</b>`p > −2`；**
/// **而 `p ≥ 0` 对 `p = −1` 是<b>假</b>。**
/// **这就是不等价——而 `spacing = 3 ≥ δ = 2` 通过，只有余数检查能挡住它。**
///
/// **所以两处检查都必需：`spacing ≥ δ` 挡"格点太密"，余数距离挡"格点位置不对齐"。**
///
/// B1172 adds the baseline's four lattice helpers.
///
/// **The immediate reason for this batch is a gap already found**: **B1168's `ProveShift` only checked
/// "spacing ≥ δ", while the baseline also checks "the distance from the constant to the nearest lattice point
/// <b>on the side that would be misclassified</b> is at least δ".** **The former is necessary, not sufficient.**
///
/// **The counterexample that shows why**: **`p = 3x + 2`, `δ = 2`. The values are `2 + 3ℤ = …, −1, 2, 5, …`.**
/// **After the `GE` shift the requirement is `p > −2`, so `p = −1` <b>satisfies</b> it; while `p ≥ 0` is
/// <b>false</b> at `p = −1`.** **That is the inequivalence — and `spacing = 3 ≥ δ = 2` passes, so only the
/// remainder check catches it.**
///
/// **Both checks are necessary: `spacing ≥ δ` blocks "the lattice is too dense", the remainder distance blocks "the
/// lattice is misaligned".**
/// </remarks>
internal static class DiscreteConditionLattice
{
    /// <summary>
    /// 合并同变量系数并丢掉零项 / Merge coefficients per symbol and drop zeros.
    /// </summary>
    /// <remarks>
    /// **合并是必需的**：**`2x` 与 `−2x` 分两个 monomial 写时，
    /// 逐个看系数都是整数，而它们的和是 0——格点信息完全不同。**
    ///
    /// **Merging is required**: **`2x` and `−2x` written as two monomials each have integer coefficients, but their
    /// sum is zero — completely different lattice information.**
    /// </remarks>
    public static IReadOnlyList<LinearMonomial<Flt64>> EffectiveMonomials(
        LinearPolynomial<Flt64> polynomial) {
        var coefficients = new List<KeyValuePair<ISymbol, Flt64>>();
        var index = new Dictionary<ISymbol, int>();

        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            if (monomial.Symbol is not { } symbol) {
                continue;
            }

            if (index.TryGetValue(symbol, out int position)) {
                coefficients[position] = new KeyValuePair<ISymbol, Flt64>(
                    symbol, coefficients[position].Value.Plus(monomial.Coefficient));
            } else {
                index[symbol] = coefficients.Count;
                coefficients.Add(new KeyValuePair<ISymbol, Flt64>(symbol, monomial.Coefficient));
            }
        }

        var effective = new List<LinearMonomial<Flt64>>(coefficients.Count);
        foreach (KeyValuePair<ISymbol, Flt64> entry in coefficients) {
            if (entry.Value.CompareTo(Flt64.Zero) != 0) {
                effective.Add(new LinearMonomial<Flt64>(entry.Value, entry.Key));
            }
        }

        return effective;
    }

    /// <summary>是否为整数值 / Whether the value is integral.</summary>
    /// <remarks>
    /// **`NaN` 必须先排除**：**`Floor(NaN) == NaN` 是 `false`，但 `NaN == NaN` 也是 `false`，
    /// 所以少了这一步，`NaN` 会走完整条路径再"恰好"返回 `false`——依赖巧合而不是依赖检查。**
    ///
    /// **`NaN` must be excluded first**: **`Floor(NaN) == NaN` is `false` and so is `NaN == NaN`, so without this step
    /// `NaN` would travel the whole path and "happen" to return `false` — by coincidence, not by check.**
    /// </remarks>
    public static bool IsIntegerValued(Flt64 value) {
        double number = value.ToDouble();
        return double.IsFinite(number) && global::System.Math.Floor(number) == number;
    }

    /// <summary>
    /// 所有非零系数的最大公约数 / The greatest common divisor of the non-zero coefficients.
    /// </summary>
    /// <remarks>
    /// **返回 `null` 表示"没有非零系数"**——**多项式是常数，格点退化成一个点。**
    /// **这与"步长是 0"不同，调用方必须分开处理。**
    ///
    /// **`null` means "there are no non-zero coefficients"** — **the polynomial is constant and the lattice degenerates
    /// to a point.** **That differs from "spacing is zero"; callers must handle them separately.**
    /// </remarks>
    public static double? CoefficientGcd(LinearPolynomial<Flt64> polynomial) {
        double? spacing = null;

        foreach (LinearMonomial<Flt64> monomial in EffectiveMonomials(polynomial)) {
            double magnitude = global::System.Math.Abs(monomial.Coefficient.ToDouble());
            if (magnitude == 0.0) {
                continue;
            }

            spacing = spacing is null ? magnitude : GreatestCommonDivisor(spacing.Value, magnitude);
        }

        return spacing;
    }

    /// <summary>
    /// 证明 `GE`/`LE` 的 `δ` 平移安全 / Prove that a `GE`/`LE` shift by `δ` is safe.
    /// </summary>
    /// <remarks>
    /// **三件事依次检查，缺一不可**：
    ///
    /// **① 多项式必须是"整系数乘整数变量"**——**否则格点无从谈起。**
    /// **② 步长必须 ≥ δ**——**挡"格点太密"。**
    /// **③ 常数到会被误判那一侧最近格点的距离必须 ≥ δ**——**挡"格点位置不对齐"。**
    ///
    /// **③ 的"会被误判那一侧"**：**`GE` 平移后要求 `p > −δ`，
    /// 所以危险的是 `p` 落在 `(−δ, 0)` 里——那是常数<b>下方</b>最近的格点；
    /// `LE` 对称，危险的是<b>上方</b>最近的格点。**
    ///
    /// **Three checks in order, none optional**:
    ///
    /// **① the polynomial must be "integer coefficients times integer variables" — otherwise there is no lattice;**
    /// **② the spacing must be at least δ — this blocks "the lattice is too dense";**
    /// **③ the distance from the constant to the nearest lattice point on the side that would be misclassified must be
    /// at least δ — this blocks "the lattice is misaligned".**
    ///
    /// **The "misclassified side"**: **after the `GE` shift the requirement is `p > −δ`, so the danger is `p` falling in
    /// `(−δ, 0)` — the nearest lattice point <b>below</b> the constant; `LE` is symmetric with <b>above</b>.**
    /// </remarks>
    public static bool HasSafeShiftProof(
        LinearPolynomial<Flt64> polynomial,
        Comparison relation,
        Flt64 delta) {
        if (relation is not (Comparison.GE or Comparison.LE) || !IsIntegerLatticePolynomial(polynomial)) {
            return false;
        }

        double deltaValue = delta.ToDouble();
        if (!double.IsFinite(deltaValue) || deltaValue <= 0.0) {
            return false;
        }

        double constant = polynomial.Constant.ToDouble();
        if (!double.IsFinite(constant)) {
            return false;
        }

        double? spacing = CoefficientGcd(polynomial);

        // **没有非零系数：格点退化，只能看常数本身。**
        // No non-zero coefficients: the lattice degenerates and only the constant matters.
        if (spacing is null) {
            return relation is Comparison.GE
                ? constant >= 0.0 || constant <= -deltaValue
                : constant <= 0.0 || constant >= deltaValue;
        }

        if (!double.IsFinite(spacing.Value) || spacing.Value <= 0.0 || spacing.Value < deltaValue) {
            return false;
        }

        double rawRemainder = constant - spacing.Value * global::System.Math.Floor(constant / spacing.Value);
        double remainder = rawRemainder < 0.0 ? rawRemainder + spacing.Value : rawRemainder;

        // **余数为 0 时，常数本身就是格点，而最近的另一侧格点在 `spacing` 之外。**
        // With a zero remainder the constant is itself a lattice point, and the nearest one on the other side is a
        // whole spacing away.
        double distance = relation switch {
            Comparison.GE => remainder == 0.0 ? spacing.Value : spacing.Value - remainder,
            Comparison.LE => remainder == 0.0 ? spacing.Value : remainder,
            _ => 0.0,
        };

        return distance >= deltaValue;
    }

    /// <summary>
    /// 证明 <c>GT</c>/<c>LT</c> 的严格边界不落在格点间隙里 / Prove a strict boundary clears the lattice gap.
    /// </summary>
    /// <remarks>
    /// **与 `HasSafeShiftProof` 不是同一件事**：
    /// **`GE`/`LE` 要证的是"平移后仍然等价"，看的是常数<b>下方</b>最近的格点；**
    /// **`GT`/`LT` 要证的是"严格边界 `δ` 不落在 `q = 0` 与最近的<b>正</b>格点之间"，
    /// 看的是常数<b>上方</b>最近的格点。**
    ///
    /// **`LT` 先归一化**：**取 `q = −p`，于是两种关系都用 `q > 0`。**
    /// **不做这一步会让 `LT` 的常数符号反掉，而判据只会看到"某个分支被拒绝"。**
    ///
    /// **为什么需要它**：**`p = 3x + 1`、`δ = 2` 时，`p` 的正侧最近格点是 `1`。
    /// `1 &lt; 2`，所以 `p > 0` 与 `p ≥ δ` 之间<b>存在一个格点</b>——
    /// 那个格点满足前者而不满足后者，于是"严格正"的条件在这里与"至少 δ"不等价。**
    ///
    /// **Not the same as `HasSafeShiftProof`**: **`GE`/`LE` prove "the shift stays equivalent", looking at
    /// the nearest lattice point <b>below</b> the constant; `GT`/`LT` prove "the strict boundary δ does not fall
    /// between `q = 0` and the nearest <b>positive</b> lattice point", looking <b>above</b>.**
    ///
    /// **`LT` normalises first**: **take `q = −p` so both relations use `q > 0`.**
    /// **Skipping that flips the sign of `LT`'s constant, and a criterion would only see "some branch was rejected".**
    ///
    /// **Why it is needed**: **with `p = 3x + 1` and `δ = 2` the nearest positive lattice point is `1`, and `1 &lt; 2`, so
    /// a lattice point sits <b>between</b> `p > 0` and `p ≥ δ` — it satisfies the former and not the latter, so "strictly
    /// positive" is not equivalent to "at least δ" here.**
    /// </remarks>
    public static bool HasSafeStrictPositiveProof(
        LinearPolynomial<Flt64> polynomial,
        Comparison relation,
        Flt64 strictBoundary) {
        if (relation is not (Comparison.GT or Comparison.LT) || !IsIntegerLatticePolynomial(polynomial)) {
            return false;
        }

        double boundary = strictBoundary.ToDouble();
        if (!double.IsFinite(boundary) || boundary <= 0.0) {
            return false;
        }

        // `LT` 归一化为 `q = −p`。
        // LT normalises to q = -p.
        double constant = polynomial.Constant.ToDouble();
        double normalizedConstant = relation is Comparison.LT ? -constant : constant;
        if (!double.IsFinite(normalizedConstant)) {
            return false;
        }

        double? spacing = CoefficientGcd(polynomial);
        if (spacing is null) {
            return normalizedConstant <= 0.0 || normalizedConstant >= boundary;
        }

        if (!double.IsFinite(spacing.Value) || spacing.Value <= 0.0) {
            return false;
        }

        double rawRemainder = normalizedConstant -
            spacing.Value * global::System.Math.Floor(normalizedConstant / spacing.Value);
        double remainder = rawRemainder < 0.0 ? rawRemainder + spacing.Value : rawRemainder;

        // **余数为 0 时常数本身是格点，最近的正格点在一整个 `spacing` 之外。**
        // With a zero remainder the constant is a lattice point and the nearest positive one is a spacing away.
        double nearestPositive = remainder == 0.0 ? spacing.Value : remainder;

        return nearestPositive >= boundary;
    }

    /// <summary>
    /// 多项式是否为"整系数乘整数变量" / Whether the polynomial is integer coefficients over integer variables.
    /// </summary>
    /// <remarks>
    /// **零系数的项直接通过**：**它对可取值没有约束。**
    /// **变量必须是 `IVariableItem` 且整数型**——**中间符号没有"整数型"这个概念，
    /// 所以它们一律不通过。**
    ///
    /// **Zero-coefficient terms pass immediately**: **they constrain nothing.**
    /// **The symbol must be an `IVariableItem` of integer type** — **intermediate symbols have no "integer type", so
    /// they never pass.**
    /// </remarks>
    public static bool IsIntegerLatticePolynomial(LinearPolynomial<Flt64> polynomial) {
        if (!double.IsFinite(polynomial.Constant.ToDouble())) {
            return false;
        }

        foreach (LinearMonomial<Flt64> monomial in EffectiveMonomials(polynomial)) {
            if (!IsIntegerValued(monomial.Coefficient)) {
                return false;
            }

            if (monomial.Symbol is not Variable.IVariableItem variable || !variable.TypeKind.IsIntegerType) {
                return false;
            }
        }

        return true;
    }

    /// <summary>两个正数的最大公约数 / The greatest common divisor of two positive values.</summary>
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
}
