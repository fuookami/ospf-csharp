#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Text;

namespace Fuookami.Ospf.Math.Symbol.Operation;
/// <summary>
/// LaTeX 渲染选项 / LaTeX rendering options.
/// </summary>
public sealed record LatexOptions {
    /// <summary>是否显示系数为 1 的项 / Whether to show coefficient when it is one.</summary>
    public bool ShowOneCoefficient { get; init; } = false;

    /// <summary>是否使用乘号 / Whether to use multiplication sign.</summary>
    public bool UseTimesSymbol { get; init; } = false;

    /// <summary>是否简化分数 / Whether to simplify fractions.</summary>
    public bool SimplifyFractions { get; init; } = true;

    /// <summary>默认选项 / Default options.</summary>
    public static readonly LatexOptions Default = new();
}

/// <summary>
/// LaTeX 数值格式化策略 / LaTeX number formatting policy.
/// </summary>
/// <typeparam name="T">数值类型 / Number type.</typeparam>
public sealed record LatexNumberOps<T> where T : struct, IRing<T>, IRealNumber<T> {
    /// <summary>将数值格式化为 LaTeX 字符串 / Format number as LaTeX string.</summary>
    public required System.Func<T, string> Format { get; init; }

    /// <summary>Flt64 默认格式化 / Flt64 default formatting.</summary>
    public static readonly LatexNumberOps<Flt64> Flt64Default = new() {
        Format = v => {
            double d = v.Value;
            if (d == System.Math.Floor(d) && System.Math.Abs(d) < 1e15) {
                return ((long)d).ToString();
            }

            return d.ToString("G6");
        }
    };
}

/// <summary>
/// LaTeX 运算 / LaTeX operations.
/// 将符号多项式和不等式渲染为 LaTeX 字符串。
/// Renders symbolic polynomials and inequalities as LaTeX strings.
/// </summary>
public static class LatexOps {
    // ===== LinearMonomial -> LaTeX =====

    /// <summary>将线性单项式渲染为 LaTeX / Render linear monomial as LaTeX.</summary>
    public static string ToLatexString<T>(
        this LinearMonomial<T> m,
        LatexNumberOps<T> numberOps,
        LatexOptions? options = null)
        where T : struct, IRing<T>, IRealNumber<T> {
        options ??= LatexOptions.Default;
        string coeff = numberOps.Format(m.Coefficient);
        string sym = m.Symbol.Name;

        if (options.ShowOneCoefficient || !m.Coefficient.Eq(m.Coefficient.Minus(m.Coefficient).Plus(m.Coefficient))) {
            // Non-trivial coefficient
            if (options.UseTimesSymbol) {
                return $"{coeff} \\times {sym}";
            }

            return $"{coeff}{sym}";
        }
        return sym;
    }

    // ===== QuadraticMonomial -> LaTeX =====

    /// <summary>将二次单项式渲染为 LaTeX / Render quadratic monomial as LaTeX.</summary>
    public static string ToLatexString<T>(
        this QuadraticMonomial<T> m,
        LatexNumberOps<T> numberOps,
        LatexOptions? options = null)
        where T : struct, IRing<T>, IRealNumber<T> {
        options ??= LatexOptions.Default;
        string coeff = numberOps.Format(m.Coefficient);
        string sym1 = m.Symbol1.Name;

        if (m.IsQuadratic) {
            string sym2 = m.Symbol2!.Name;
            string body = sym1 == sym2 ? $"{sym1}^{{2}}" : $"{sym1}{sym2}";
            if (options.UseTimesSymbol) {
                return $"{coeff} \\times {body}";
            }

            return $"{coeff}{body}";
        }

        if (options.UseTimesSymbol) {
            return $"{coeff} \\times {sym1}";
        }

        return $"{coeff}{sym1}";
    }

    // ===== LinearPolynomial -> LaTeX =====

    /// <summary>将线性多项式渲染为 LaTeX / Render linear polynomial as LaTeX.</summary>
    public static string ToLatexString<T>(
        this LinearPolynomial<T> p,
        LatexNumberOps<T> numberOps,
        LatexOptions? options = null)
        where T : struct, IRing<T>, IRealNumber<T> {
        options ??= LatexOptions.Default;
        var sb = new StringBuilder();
        bool first = true;

        foreach (LinearMonomial<T> m in p.Monomials) {
            string term = m.ToLatexString(numberOps, options);
            if (!first && !term.StartsWith('-')) {
                sb.Append(" + ");
            }

            sb.Append(term);
            first = false;
        }

        // Constant
        IRealNumberConstants<T> constants = p.Constant.Constants;
        if (!p.Constant.Eq(p.Constant.Minus(p.Constant))) {
            string constStr = numberOps.Format(p.Constant);
            if (!first && !constStr.StartsWith('-')) {
                sb.Append(" + ");
            }

            sb.Append(constStr);
        }

        return sb.Length == 0 ? "0" : sb.ToString();
    }

    // ===== QuadraticPolynomial -> LaTeX =====

    /// <summary>将二次多项式渲染为 LaTeX / Render quadratic polynomial as LaTeX.</summary>
    public static string ToLatexString<T>(
        this QuadraticPolynomial<T> p,
        LatexNumberOps<T> numberOps,
        LatexOptions? options = null)
        where T : struct, IRing<T>, IRealNumber<T> {
        options ??= LatexOptions.Default;
        var sb = new StringBuilder();
        bool first = true;

        foreach (QuadraticMonomial<T> m in p.Monomials) {
            string term = m.ToLatexString(numberOps, options);
            if (!first && !term.StartsWith('-')) {
                sb.Append(" + ");
            }

            sb.Append(term);
            first = false;
        }

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

    // ===== Inequality -> LaTeX =====

    /// <summary>将线性不等式渲染为 LaTeX / Render linear inequality as LaTeX.</summary>
    public static string ToLatexString<T>(
        this LinearInequality<T> ineq,
        LatexNumberOps<T> numberOps,
        LatexOptions? options = null)
        where T : struct, IRing<T>, IRealNumber<T> {
        string lhs = ineq.Lhs.ToLatexString(numberOps, options);
        string rhs = ineq.Rhs.ToLatexString(numberOps, options);
        string op = ineq.Comparison switch {
            Comparison.LT => " < ",
            Comparison.LE => " \\leq ",
            Comparison.EQ => " = ",
            Comparison.NE => " \\neq ",
            Comparison.GE => " \\geq ",
            Comparison.GT => " > ",
            _ => " ? "
        };
        return $"{lhs}{op}{rhs}";
    }

    // ===== Flt64 convenience =====

    /// <summary>将 Flt64 线性单项式渲染为 LaTeX / Render Flt64 linear monomial as LaTeX.</summary>
    public static string ToLatex(this LinearMonomial<Flt64> m, LatexOptions? options = null)
        => m.ToLatexString(LatexNumberOps<Flt64>.Flt64Default, options);

    /// <summary>将 Flt64 二次单项式渲染为 LaTeX / Render Flt64 quadratic monomial as LaTeX.</summary>
    public static string ToLatex(this QuadraticMonomial<Flt64> m, LatexOptions? options = null)
        => m.ToLatexString(LatexNumberOps<Flt64>.Flt64Default, options);

    /// <summary>将 Flt64 线性多项式渲染为 LaTeX / Render Flt64 linear polynomial as LaTeX.</summary>
    public static string ToLatex(this LinearPolynomial<Flt64> p, LatexOptions? options = null)
        => p.ToLatexString(LatexNumberOps<Flt64>.Flt64Default, options);

    /// <summary>将 Flt64 二次多项式渲染为 LaTeX / Render Flt64 quadratic polynomial as LaTeX.</summary>
    public static string ToLatex(this QuadraticPolynomial<Flt64> p, LatexOptions? options = null)
        => p.ToLatexString(LatexNumberOps<Flt64>.Flt64Default, options);

    /// <summary>将 Flt64 线性不等式渲染为 LaTeX / Render Flt64 linear inequality as LaTeX.</summary>
    public static string ToLatex(this LinearInequality<Flt64> ineq, LatexOptions? options = null)
        => ineq.ToLatexString(LatexNumberOps<Flt64>.Flt64Default, options);
}
