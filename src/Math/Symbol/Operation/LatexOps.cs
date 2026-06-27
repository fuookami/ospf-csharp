#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Text;

namespace Fuookami.Ospf.Math.Symbol.Operation;

/// <summary>
/// LaTeX 运算（规范多项式支持）/ LaTeX Operations (Canonical Polynomial Support).
/// 提供规范单项式和规范多项式的 LaTeX 渲染。
/// Kotlin LatexOps.kt 的对应实现。
/// Provides LaTeX rendering for canonical monomials and canonical polynomials.
/// </summary>
public static class CanonicalLatexOps {
    // ===== CanonicalMonomial -> LaTeX =====

    /// <summary>
    /// 将规范单项式渲染为 LaTeX / Render canonical monomial as LaTeX.
    /// </summary>
    /// <param name="m">规范单项式 / Canonical monomial.</param>
    /// <param name="numberOps">数值格式化接口 / Number formatting ops.</param>
    /// <param name="options">LaTeX 选项 / LaTeX options.</param>
    /// <returns>LaTeX 格式字符串 / LaTeX format string.</returns>
    public static string ToLatexString<T>(
        this CanonicalMonomial<T> m,
        LatexNumberOps<T> numberOps,
        LatexOptions? options = null)
        where T : struct, IRing<T>, IRealNumber<T> {
        options ??= LatexOptions.Default;
        if (m.Powers.Count == 0) {
            return numberOps.Format(m.Coefficient);
        }

        string multiply = options.UseTimesSymbol ? (options.ShowOneCoefficient ? " \\times " : " \\cdot ") : "";
        var sb = new StringBuilder();
        bool first = true;
        foreach ((ISymbol? symbol, int power) in m.Powers) {
            if (!first && multiply.Length > 0) {
                sb.Append(multiply);
            }

            if (power == 1) {
                sb.Append(symbol.Name);
            }
            else {
                sb.Append($"{symbol.Name}^{{{power}}}");
            }

            first = false;
        }

        string variable = sb.ToString();
        string coeff = numberOps.Format(m.Coefficient);

        if (!options.ShowOneCoefficient && IsLikelyOne(m.Coefficient)) {
            return variable;
        }

        if (options.UseTimesSymbol) {
            return $"{coeff} \\times {variable}";
        }

        return $"{coeff}{variable}";
    }

    // ===== CanonicalPolynomial -> LaTeX =====

    /// <summary>
    /// 将规范多项式渲染为 LaTeX / Render canonical polynomial as LaTeX.
    /// </summary>
    /// <param name="p">规范多项式 / Canonical polynomial.</param>
    /// <param name="numberOps">数值格式化接口 / Number formatting ops.</param>
    /// <param name="options">LaTeX 选项 / LaTeX options.</param>
    /// <returns>LaTeX 格式字符串 / LaTeX format string.</returns>
    public static string ToLatexString<T>(
        this CanonicalPolynomial<T> p,
        LatexNumberOps<T> numberOps,
        LatexOptions? options = null)
        where T : struct, IRing<T>, IRealNumber<T> {
        options ??= LatexOptions.Default;
        var sb = new StringBuilder();
        bool first = true;

        foreach (CanonicalMonomial<T> m in p.Monomials) {
            string term = m.ToLatexString(numberOps, options);
            if (!first && !term.StartsWith('-')) {
                sb.Append(" + ");
            }

            sb.Append(term);
            first = false;
        }

        // Constant
        T zero = p.Constant.Minus(p.Constant);
        if (!p.Constant.Eq(zero)) {
            string constStr = numberOps.Format(p.Constant);
            if (!first && !constStr.StartsWith('-')) {
                sb.Append(" + ");
            }

            sb.Append(constStr);
        }

        return sb.Length == 0 ? "0" : sb.ToString();
    }

    // ===== Flt64 convenience =====

    /// <summary>将 Flt64 规范单项式渲染为 LaTeX / Render Flt64 canonical monomial as LaTeX.</summary>
    public static string ToLatex(this CanonicalMonomial<Flt64> m, LatexOptions? options = null)
        => m.ToLatexString(LatexNumberOps<Flt64>.Flt64Default, options);

    /// <summary>将 Flt64 规范多项式渲染为 LaTeX / Render Flt64 canonical polynomial as LaTeX.</summary>
    public static string ToLatex(this CanonicalPolynomial<Flt64> p, LatexOptions? options = null)
        => p.ToLatexString(LatexNumberOps<Flt64>.Flt64Default, options);

    // ===== Helpers =====

    /// <summary>
    /// 判断系数是否为 1 / Check if coefficient is one.
    /// </summary>
    private static bool IsLikelyOne<T>(T value) where T : struct, IRing<T>, IRealNumber<T> {
        INumericConstants<T> constants = NumericConstantsRegistry.For<T>();
        T one = constants.One;
        return value.Eq(one);
    }
}
