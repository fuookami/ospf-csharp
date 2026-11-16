#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **两个一次式相乘：进入二次域** / Two first-degree forms multiplied: entering the quadratic domain.
/// </summary>
/// <remarks>
/// `Mul`／`MatMul` 在 `Quadratic` 导入模式下的那一半。**一次域里 `Mul` 只允许"变量乘常数"，'
/// **而两个含变量的式子相乘不再是多项式一次**——**硬塞进一次域只能得到一个错误的结果，而它照常可解。**
///
/// **合并同类项在这里必须做对：** **`(x+y)(x−y)` 展开出 `x²`、`−y²` 与 `xy`、`−xy` 四项，后两项必须抵消。**
/// **若不抵消，模型里就多出一项 `xy`——** **而求解器会用它去凑一个"更优"的点，那个点根本不满足原来的问题。**
///
/// Syncs the `Quadratic`-mode half of `Mul` and `MatMul`. **In the first-degree domain Mul only allows a variable times a constant, while two forms that both carry variables
/// are no longer first degree — forcing them into the first-degree domain yields a wrong result that still solves.**
///
/// **Merging like terms has to be right here: (x+y)(x−y) expands to x², −y² and xy, −xy, and the last two must cancel.** **If they do not, the model gains an xy term, and
/// the solver uses it to reach a "better" point that does not satisfy the original problem at all.**
/// </remarks>
public static class OnnxQuadraticProduct
{
    /// <summary>
    /// 相乘结果 / The product result.
    /// </summary>
    /// <param name="Polynomial">二次多项式 / The quadratic polynomial.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Result(QuadraticPolynomial<Flt64>? Polynomial, string? Failure)
    {
        /// <summary>是否成功 / Whether it succeeded.</summary>
        public bool IsOk => Failure is null;
    }

    /// <summary>
    /// 两个一次式相乘 / Multiply two first-degree forms.
    /// </summary>
    /// <param name="left">左乘数 / The left factor.</param>
    /// <param name="right">右乘数 / The right factor.</param>
    /// <returns>二次多项式 / The quadratic polynomial.</returns>
    public static Result Multiply(LinearPolynomial<Flt64> left, LinearPolynomial<Flt64> right) {
        if (left?.Monomials is null || right?.Monomials is null) {
            return new Result(null, "相乘的一方为空 / one of the factors is null");
        }

        var monomials = new List<QuadraticMonomial<Flt64>>();
        // 变量 × 变量 → 二次项；变量 × 常数 → 一次项（用 Symbol2 = null 表示）。
        // Variable × variable gives a quadratic term; variable × constant gives a linear one, written with Symbol2 = null.
        foreach (LinearMonomial<Flt64> a in left.Monomials) {
            foreach (LinearMonomial<Flt64> b in right.Monomials) {
                monomials.Add(new QuadraticMonomial<Flt64>(a.Coefficient * b.Coefficient, a.Symbol, b.Symbol));
            }
            if (!right.Constant.ToDouble().Equals(0.0)) {
                monomials.Add(new QuadraticMonomial<Flt64>(a.Coefficient * right.Constant, a.Symbol));
            }
        }
        foreach (LinearMonomial<Flt64> b in right.Monomials) {
            if (!left.Constant.ToDouble().Equals(0.0)) {
                monomials.Add(new QuadraticMonomial<Flt64>(left.Constant * b.Coefficient, b.Symbol));
            }
        }

        Flt64 constant = left.Constant * right.Constant;
        return new Result(new QuadraticPolynomial<Flt64>(Normalise(monomials), constant), null);
    }

    /// <summary>
    /// 判断一个式子是否能在一次域里相乘 / Whether a factor stays expressible in the first-degree domain.
    /// </summary>
    /// <param name="polynomial">式子 / The form.</param>
    /// <returns>是否为常数 / Whether it is a constant.</returns>
    public static bool IsConstant(LinearPolynomial<Flt64> polynomial) => polynomial.Monomials.Count == 0;

    /// <summary>
    /// 合并同类项 / Merge like terms.
    /// </summary>
    /// <remarks>
    /// **键是"两个符号的无序对"：** **`xy` 与 `yx` 必须落进同一格，否则 `(x+y)(x−y)` 里的两项不会抵消。** '
    /// **按有序对做键，展开结果里就会留下一项 `xy`，而模型照常求解。**
    ///
    /// **The key is the unordered pair of symbols: xy and yx must land in the same cell, or the two terms in `(x+y)(x−y)` do not cancel.** **Keyed by an ordered pair, the
    /// expansion keeps an xy term and the model still solves.**
    /// </remarks>
    private static IReadOnlyList<QuadraticMonomial<Flt64>> Normalise(IReadOnlyList<QuadraticMonomial<Flt64>> monomials) {
        var order = new List<string>();
        var table = new Dictionary<string, (Flt64 Coefficient, ISymbol First, ISymbol? Second)>(StringComparer.Ordinal);
        foreach (QuadraticMonomial<Flt64> monomial in monomials) {
            string? second = monomial.Symbol2?.Name;
            bool swap = second is not null && string.CompareOrdinal(monomial.Symbol1.Name, second) > 0;
            string key = swap
                ? second + "\u0000" + monomial.Symbol1.Name
                : monomial.Symbol1.Name + (second is null ? string.Empty : "\u0000" + second);
            ISymbol first = swap ? monomial.Symbol2! : monomial.Symbol1;
            ISymbol? keptSecond = monomial.Symbol2 is null ? null : (swap ? monomial.Symbol1 : monomial.Symbol2);
            if (table.TryGetValue(key, out (Flt64 Coefficient, ISymbol First, ISymbol? Second) existing)) {
                table[key] = (existing.Coefficient + monomial.Coefficient, existing.First, existing.Second);
            }
            else {
                table[key] = (monomial.Coefficient, first, keptSecond);
                order.Add(key);
            }
        }

        var merged = new List<QuadraticMonomial<Flt64>>(order.Count);
        foreach (string key in order) {
            (Flt64 coefficient, ISymbol first, ISymbol? second) = table[key];
            if (coefficient.ToDouble() == 0.0) {
                continue;
            }
            merged.Add(new QuadraticMonomial<Flt64>(coefficient, first, second));
        }
        return merged;
    }
}