#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Math.Ordinary;
/// <summary>
/// FltX 级数计算结果 / FltX series computation result.
/// </summary>
public readonly record struct FltXSeriesResult(FltX Value, int Iterations, bool Converged);

/// <summary>
/// FltX 幂运算策略 / FltX power strategy.
/// <para>提供 FltX 高精度幂、指数、对数计算（泰勒级数）。</para>
/// <para>Provides high-precision power/exp/log for FltX via Taylor series.</para>
/// </summary>
public static class FltXPowerStrategy {
    /// <summary>根据精度位数计算默认精度阈值 / Compute default precision threshold from digit count.</summary>
    public static FltX DefaultPrecision(int digits) {
        int normalizedDigits = digits <= 0 ? 1 : digits;
        FltX threshold = Pow.Of(FltXConstants.Instance.Ten, -normalizedDigits, FltXConstants.Instance, normalizedDigits + 1);
        return MinMax.Min(threshold, FltXConstants.Instance.Epsilon);
    }

    /// <summary>计算 FltX 自然对数 / Compute FltX natural logarithm.</summary>
    public static FltX? Ln(FltX x, int digits, FltX? precision = default, int maxIterations = 8192)
        => LnWithStats(x, digits, precision, maxIterations)?.Value;

    /// <summary>计算 FltX 自然对数，返回迭代统计 / Compute FltX ln with iteration stats.</summary>
    public static FltXSeriesResult? LnWithStats(FltX x, int digits, FltX? precision = default, int maxIterations = 8192) {
        if (x.PartialOrd(FltXConstants.Instance.Zero) is Order.Less or Order.Equal) {
            return null;
        }

        int normalizedDigits = digits <= 0 ? 1 : digits;
        int scale = normalizedDigits + 1;
        FltX m = x.WithScale(scale, MidpointRounding.AwayFromZero);
        FltX k = FltXConstants.Instance.Zero;
        while (m.PartialOrd(FltXConstants.Instance.Two) is Order.Greater or Order.Equal) { m = m.Div(FltXConstants.Instance.Two).WithScale(scale, MidpointRounding.AwayFromZero); k = k.Increment(); }
        while (m.PartialOrd(FltXConstants.Instance.One) is Order.Less) { m = m.Times(FltXConstants.Instance.Two).WithScale(scale, MidpointRounding.AwayFromZero); k = k.Minus(FltXConstants.Instance.One); }
        FltXSeriesResult core = LnUnitInterval(m, scale, precision ?? DefaultPrecision(digits), maxIterations);
        FltX value = core.Value.Plus(k.Times(FltXConstants.Instance.Lg2)).WithScale(scale, MidpointRounding.AwayFromZero);
        return new FltXSeriesResult(value, core.Iterations, core.Converged);
    }

    private static FltXSeriesResult LnUnitInterval(FltX x, int scale, FltX precision, int maxIterations) {
        FltX two = FltXConstants.Instance.Two; FltX one = FltXConstants.Instance.One;
        FltX y = x.Minus(one).Div(x.Plus(one)).WithScale(scale, MidpointRounding.AwayFromZero);
        FltX yPow = y; FltX value = y; FltX i = one; int iterations = 0;
        while (iterations < maxIterations) {
            yPow = yPow.Times(y).Times(y).WithScale(scale, MidpointRounding.AwayFromZero);
            FltX denominator = two.Times(i).Plus(one);
            FltX term = yPow.Div(denominator).WithScale(scale, MidpointRounding.AwayFromZero);
            value = value.Plus(term).WithScale(scale, MidpointRounding.AwayFromZero);
            iterations++;
            if (term.Abs().PartialOrd(precision) is Order.Less or Order.Equal) {
                return new FltXSeriesResult(value.Times(two).WithScale(scale, MidpointRounding.AwayFromZero), iterations, true);
            }

            i = i.Increment();
        }
        return new FltXSeriesResult(value.Times(two).WithScale(scale, MidpointRounding.AwayFromZero), iterations, false);
    }

    /// <summary>计算 FltX 指数函数 / Compute FltX exponential.</summary>
    public static FltX Exp(FltX index, int digits, FltX? precision = default, int maxIterations = 8192)
        => ExpWithStats(index, digits, precision, maxIterations).Value;

    /// <summary>计算 FltX 指数函数，返回迭代统计 / Compute FltX exp with iteration stats.</summary>
    public static FltXSeriesResult ExpWithStats(FltX index, int digits, FltX? precision = default, int maxIterations = 8192) {
        int normalizedDigits = digits <= 0 ? 1 : digits;
        int scale = normalizedDigits + 1;
        FltX one = FltXConstants.Instance.One;
        FltX value = one.WithScale(scale, MidpointRounding.AwayFromZero);
        FltX term = one.WithScale(scale, MidpointRounding.AwayFromZero);
        FltX i = one; int iterations = 0;
        while (iterations < maxIterations) {
            FltX next = term.Times(index.WithScale(scale, MidpointRounding.AwayFromZero)).Div(i).WithScale(scale, MidpointRounding.AwayFromZero);
            value = value.Plus(next).WithScale(scale, MidpointRounding.AwayFromZero);
            iterations++;
            if (next.Abs().PartialOrd(precision ?? DefaultPrecision(digits)) is Order.Less or Order.Equal) {
                return new FltXSeriesResult(value, iterations, true);
            }

            term = next; i = i.Increment();
        }
        return new FltXSeriesResult(value, iterations, false);
    }

    /// <summary>计算 FltX 幂函数 / Compute FltX power.</summary>
    public static Result<FltX, ErrorCode, Error<ErrorCode>> Of(FltX @base, FltX index, int digits, FltX? precision = default, int maxIterations = 8192)
        => PowSafe(@base, index, digits, precision, maxIterations);

    /// <summary>安全计算 FltX 幂函数 / Safely compute FltX power.</summary>
    public static Result<FltX, ErrorCode, Error<ErrorCode>> PowSafe(FltX @base, FltX index, int digits, FltX? precision = default, int maxIterations = 8192)
        => PowOrNull(@base, index, digits, precision, maxIterations) is { } v
            ? Results.Ok(v)
            : new Failed<FltX, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                $"FltX 幂计算失败：底数的自然对数未定义，base={@base} / FltX power failed: ln(base) is undefined, base={@base}.");

    /// <summary>尝试计算 FltX 幂函数，未定义返回 null / Try FltX power; null if undefined.</summary>
    public static FltX? PowOrNull(FltX @base, FltX index, int digits, FltX? precision = default, int maxIterations = 8192) {
        if (index.StripTrailingZeros().Eq(index.Round())) {
            return Pow.Of(@base, index.Round().ToInt32().Value, FltXConstants.Instance, digits, precision);
        }

        FltX? lnBase = Ln(@base, digits, precision, maxIterations);
        if (lnBase is null) {
            return null;
        }

        return Exp(index.Times(lnBase.Value), digits, precision, maxIterations);
    }
}
