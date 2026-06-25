#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Math.Ordinary;
/// <summary>
/// 最大公约数 / Greatest Common Divisor (GCD).
/// <para>提供整数、浮点数、有理数 GCD 计算；常量通过 NumericConstantsRegistry.For&lt;T&gt;() 解析。</para>
/// <para>Provides GCD for integers, floats, rationals; constants via NumericConstantsRegistry.For&lt;T&gt;().</para>
/// </summary>
public static class Gcd {
    /// <summary>减法 GCD（内部）/ Subtraction-based GCD (internal).</summary>
    internal static T GcdImpl<T>(T x, T y)
        where T : struct, IInteger<T>, IMinus<T, T> {
        T zero = x.Constants.Zero;
        System.Diagnostics.Debug.Assert(x.PartialOrd(zero) is Order.Greater or Order.Equal);
        System.Diagnostics.Debug.Assert(y.PartialOrd(zero) is Order.Greater or Order.Equal);
        if (x.Eq(zero)) {
            return y;
        }

        if (y.Eq(zero)) {
            return x;
        }

        T a = x; T b = y;
        while (!b.Eq(zero)) {
            if (a.PartialOrd(b) is Order.Greater) { (a, b) = (b, a); }
            b = b.Minus(a);
        }
        return a;
    }

    /// <summary>取模 GCD（内部，欧几里得）/ Modulo (Euclidean) GCD (internal).</summary>
    internal static T GcdModImpl<T>(T x, T y)
        where T : struct, IInteger<T>, IRem<T, T> {
        T zero = x.Constants.Zero;
        T a = x.Abs(); T b = y.Abs();
        while (!b.Eq(zero)) { T r = a.Rem(b); a = b; b = r; }
        return a;
    }

    /// <summary>多整数 GCD（内部）/ Multi-integer GCD (internal).</summary>
    internal static T GcdImpl<T>(IEnumerable<T> numbers, INumericConstants<T> constants)
        where T : struct, IInteger<T>, IRem<T, T> {
        using IEnumerator<T> e = numbers.GetEnumerator();
        if (!e.MoveNext()) {
            return constants.One;
        }

        T acc = e.Current;
        while (e.MoveNext()) {
            acc = GcdModImpl(acc, e.Current);
        }

        return acc;
    }

    /// <summary>计算两个整数的最大公约数 / Compute GCD of two integers.</summary>
    public static T Of<T>(T x, T y)
        where T : struct, IInteger<T>, IMinus<T, T>
        => GcdImpl(x, y);

    /// <summary>计算多个整数的最大公约数 / Compute GCD of multiple integers.</summary>
    public static T Of<T>(IEnumerable<T> numbers, INumericConstants<T> constants)
        where T : struct, IInteger<T>, IRem<T, T>
        => GcdImpl(numbers.Select(n => n.Abs()).OrderByDescending(n => n), constants);

    /// <summary>多整数 GCD（注册表解析常量）/ Multi-integer GCD (registry-resolved).</summary>
    public static Result<T, ErrorCode, Error<ErrorCode>> Of<T>(IEnumerable<T> numbers)
        where T : struct, IInteger<T>, IRem<T, T> {
        INumericConstants<T>? c = NumericConstantsRegistry.ForOrNull<T>();
        return c is null
            ? new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                $"未注册 INumericConstants<{typeof(T).Name}> / No INumericConstants<{typeof(T).Name}> registered.")
            : Results.Ok(Of(numbers, c));
    }

    /// <summary>计算多个整数的 GCD（可变参数）/ Compute GCD of multiple integers (vararg).</summary>
    public static T Of<T>(T x, T y, T z, INumericConstants<T> constants, params T[] rest)
        where T : struct, IInteger<T>, IRem<T, T>
        => Of(new[] { x, y, z }.Concat(rest), constants);

    /// <summary>使用取模算法计算两个整数的 GCD / Compute GCD of two integers using modulo.</summary>
    public static T GcdMod<T>(T x, T y)
        where T : struct, IInteger<T>, IRem<T, T>
        => GcdModImpl(x, y);

    /// <summary>使用取模算法计算多个整数的 GCD / Compute GCD of multiple integers using modulo.</summary>
    public static T GcdMod<T>(IEnumerable<T> numbers, INumericConstants<T> constants)
        where T : struct, IInteger<T>, IRem<T, T>
        => Of(numbers, constants);

    /// <summary>使用取模算法计算多个整数的 GCD（注册表解析）/ GCD modulo (registry-resolved).</summary>
    public static Result<T, ErrorCode, Error<ErrorCode>> GcdMod<T>(IEnumerable<T> numbers)
        where T : struct, IInteger<T>, IRem<T, T>
        => Of(numbers);

    /// <summary>
    /// 扩展欧几里得结果 / Extended Euclidean result (gcd, x, y of Bezout identity).
    /// </summary>
    public readonly record struct ExtendedGcdResult<T>(T Gcd, T X, T Y)
        where T : struct, IIntegerNumber<T>;

    /// <summary>扩展欧几里得算法，求解 gcd(a,b)=ax+by / Extended Euclidean: gcd(a,b)=ax+by.</summary>
    public static ExtendedGcdResult<T> ExtendedGcd<T>(T a, T b)
        where T : struct, IIntegerNumber<T>, IDiv<T, T>, IRem<T, T>, IMinus<T, T> {
        T zero = a.Constants.Zero;
        T one = a.Constants.One;
        T oldR = a; T r = b;
        T oldS = one; T s = zero;
        T oldT = zero; T t = one;
        while (!r.Eq(zero)) {
            T q = oldR.Div(r);
            T nextR = oldR.Minus(q.Times(r)); oldR = r; r = nextR;
            T nextS = oldS.Minus(q.Times(s)); oldS = s; s = nextS;
            T nextT = oldT.Minus(q.Times(t)); oldT = t; t = nextT;
        }
        return oldR.PartialOrd(zero) is Order.Less
            ? new ExtendedGcdResult<T>(oldR.Negate(), oldS.Negate(), oldT.Negate())
            : new ExtendedGcdResult<T>(oldR, oldS, oldT);
    }

    // —— FltX 特化 / FltX specialization ——

    /// <summary>计算两个浮点数的 GCD / Compute GCD of two FltX values.</summary>
    public static FltX Of(FltX x, FltX y) => Of(new[] { x, y });

    /// <summary>计算多个浮点数的 GCD / Compute GCD of multiple FltX values.</summary>
    public static FltX Of(IEnumerable<FltX> numbers) {
        (List<IntX>? integerNumbers, int factor) = FltXHelper.ScaleToIntegers(numbers);
        var integerGcd = GcdImpl(integerNumbers.OrderByDescending(n => n), IntXConstants.Instance).ToFltX();
        return integerGcd / FltXConstants.Instance.Ten.Pow(factor);
    }

    /// <summary>计算多个浮点数的 GCD（可变参数）/ Compute GCD of FltX values (vararg).</summary>
    public static FltX Of(FltX x, FltX y, FltX z, params FltX[] rest) => Of(new[] { x, y, z }.Concat(rest));

    // —— RtnX 特化 / RtnX specialization ——
    // Note: RtnX in C# uses double Numerator/Denominator (simplified).
    // Gcd(RtnX) / Lcm(RtnX) specializations are deferred to future IntX-backed RtnX port.
}
