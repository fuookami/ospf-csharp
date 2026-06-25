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
/// 可扩展素数缓存 / Extensible prime cache.
/// <para>埃拉托斯特尼筛法，动态扩展；超出范围用试除法。通过 lock 保证线程安全。</para>
/// <para>Sieve of Eratosthenes with dynamic expansion; trial division beyond range; lock-based thread safety.</para>
/// </summary>
public sealed class PrimeCache {
    private UInt64 _current = UInt64Constants.Instance.Zero;
    private bool[] _isPrime = null!;
    private readonly List<UInt64> _primes = new();
    private readonly object _lock = new();

    public PrimeCache() { Sieve(new UInt64(1000UL)); }

    private void ExtendSieve(UInt64 @new) {
        lock (_lock) {
            if (@new.PartialOrd(_current) is not Order.Greater) {
                return;
            }

            UInt64 old = _current;
            UInt64 oldSize = _current.Eq(UInt64Constants.Instance.Zero)
                ? UInt64Constants.Instance.Zero
                : _current.Increment();
            int newSize = (int)@new.Value + 1;
            bool[] newIsPrime = Enumerable.Repeat(true, newSize).ToArray();
            if (_current.PartialOrd(UInt64Constants.Instance.Zero) is Order.Greater) {
                Array.Copy(_isPrime, 0, newIsPrime, 0, (int)oldSize.Value);
            }
            else {
                if (@new.PartialOrd(UInt64Constants.Instance.Zero) is Order.Greater or Order.Equal) {
                    newIsPrime[0] = false;
                }

                if (@new.PartialOrd(UInt64Constants.Instance.One) is Order.Greater or Order.Equal) {
                    newIsPrime[1] = false;
                }
            }
            _isPrime = newIsPrime;
            _current = @new;
            var sqrtLimit = @new.ToFlt64().Sqrt().Floor().ToUInt64();
            foreach (UInt64 p in _primes) {
                if (p.PartialOrd(sqrtLimit) is Order.Greater) {
                    break;
                }

                UInt64 start = p.Times(p).PartialOrd(old.Increment()) is Order.Greater
                    ? p.Times(p)
                    : old.Increment().Plus(p).Minus(UInt64Constants.Instance.One).Div(p).Times(p);
                for (UInt64 j = start; j.PartialOrd(@new) is Order.Less or Order.Equal; j = j.Plus(p)) {
                    newIsPrime[(int)j.Value] = false;
                }
            }
            UInt64 begin = old.PartialOrd(UInt64Constants.Instance.Two) is Order.Less
                ? UInt64Constants.Instance.Two
                : old.Increment();
            for (UInt64 i = begin; i.PartialOrd(@new) is Order.Less or Order.Equal; i = i.Increment()) {
                if (newIsPrime[(int)i.Value]) {
                    _primes.Add(i);
                    if (i.PartialOrd(sqrtLimit) is Order.Less or Order.Equal) {
                        UInt64 startMultiple = i.Times(i).PartialOrd(begin) is Order.Greater
                            ? i.Times(i) : begin;
                        for (UInt64 j = startMultiple; j.PartialOrd(@new) is Order.Less or Order.Equal; j = j.Plus(i)) {
                            newIsPrime[(int)j.Value] = false;
                        }
                    }
                }
            }
        }
    }

    /// <summary>获取不超过 limit 的所有素数 / Get all primes up to limit.</summary>
    public IReadOnlyList<UInt64> GetPrimes(UInt64 limit) {
        lock (_lock) {
            if (limit.PartialOrd(_current) is Order.Greater) {
                ExtendSieve(limit);
            }

            return _primes.Where(p => p.PartialOrd(limit) is Order.Less or Order.Equal).ToList();
        }
    }

    /// <summary>判断是否为素数 / Check whether a number is prime.</summary>
    public bool IsPrime(UInt64 num) {
        if (num.PartialOrd(UInt64Constants.Instance.One) is Order.Less or Order.Equal) {
            return false;
        }

        lock (_lock) {
            if (num.PartialOrd(_current) is Order.Greater) {
                if (num.PartialOrd(new UInt64(1000000UL)) is Order.Less or Order.Equal) { ExtendSieve(num); return _isPrime[(int)num.Value]; }
                return IsPrimeQuickCheck(num);
            }
            return _isPrime[(int)num.Value];
        }
    }

    private bool IsPrimeQuickCheck(UInt64 n) {
        if (n.PartialOrd(UInt64Constants.Instance.One) is Order.Less or Order.Equal) {
            return false;
        }

        if (n.PartialOrd(UInt64Constants.Instance.Three) is Order.Less or Order.Equal) {
            return true;
        }

        if (n.Rem(UInt64Constants.Instance.Two).Eq(UInt64Constants.Instance.Zero)
            || n.Rem(UInt64Constants.Instance.Three).Eq(UInt64Constants.Instance.Zero)) {
            return false;
        }

        lock (_lock) {
            foreach (UInt64 p in _primes) {
                if (p.Times(p).PartialOrd(n) is Order.Greater) {
                    break;
                }

                if (n.Rem(p).Eq(UInt64Constants.Instance.Zero)) {
                    return false;
                }
            }
        }

        for (UInt64 i = UInt64Constants.Instance.Five; i.Times(i).PartialOrd(n) is Order.Less or Order.Equal; i = i.Plus(new UInt64(6UL))) {
            if (n.Rem(i).Eq(UInt64Constants.Instance.Zero) || n.Rem(i.Plus(UInt64Constants.Instance.Two)).Eq(UInt64Constants.Instance.Zero)) {
                return false;
            }
        }

        return true;
    }

    private void Sieve(UInt64 limit) {
        if (limit.PartialOrd(_current) is not Order.Greater) {
            return;
        }

        _isPrime = Enumerable.Repeat(true, (int)limit.Value + 1).ToArray();
        _isPrime[0] = false; _isPrime[1] = false;
        var sqrtLimit = limit.ToFlt64().Sqrt().Floor().ToUInt64();
        for (UInt64 i = UInt64Constants.Instance.Two; i.PartialOrd(sqrtLimit) is Order.Less or Order.Equal; i = i.Increment()) {
            if (_isPrime[(int)i.Value]) {
                for (UInt64 j = i.Times(i); j.PartialOrd(limit) is Order.Less or Order.Equal; j = j.Plus(i)) {
                    _isPrime[(int)j.Value] = false;
                }
            }
        }

        _primes.Clear();
        for (UInt64 i = UInt64Constants.Instance.Two; i.PartialOrd(limit) is Order.Less or Order.Equal; i = i.Increment()) {
            if (_isPrime[(int)i.Value]) {
                _primes.Add(i);
            }
        }

        _current = limit;
    }
}

/// <summary>
/// 素数算法 / Prime Number Algorithm.
/// <para>提供素数判定和素数表生成功能。</para>
/// <para>Provides prime detection and prime table generation.</para>
/// </summary>
public static class Prime {
    internal static readonly PrimeCache Cache = new();

    /// <summary>获取不超过 limit 的所有素数（UInt64 专用）/ Get all primes up to limit (UInt64).</summary>
    public static IReadOnlyList<UInt64> GetPrimesUpTo(UInt64 limit) => Cache.GetPrimes(limit);

    /// <summary>判断整数是否为素数 / Check whether an integer is prime.</summary>
    public static bool IsPrime<T>(T num)
        where T : struct, IInteger<T>
        => Cache.IsPrime(new UInt64((ulong)num.ToFlt64().Value));

    /// <summary>获取不超过 num 的所有素数（内部）/ Get primes up to num (internal).</summary>
    internal static IReadOnlyList<T> GetPrimesImpl<T>(T num, INumericConstants<T> constants)
        where T : struct, IInteger<T> {
        T current = constants.One;
        var primes = new List<T>();
        while (current.PartialOrd(num) is Order.Less or Order.Equal) {
            if (IsPrime(current)) {
                primes.Add(current);
            }

            current = current.Increment();
        }
        return primes;
    }

    /// <summary>获取不超过 num 的所有素数 / Get primes up to num.</summary>
    public static IReadOnlyList<T> GetPrimes<T>(T num, INumericConstants<T> constants)
        where T : struct, IInteger<T>
        => GetPrimesImpl(num, constants);

    /// <summary>获取不超过 num 的所有素数（注册表解析）/ Get primes (registry-resolved).</summary>
    public static Result<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>> GetPrimes<T>(T num)
        where T : struct, IInteger<T> {
        INumericConstants<T>? c = NumericConstantsRegistry.ForOrNull<T>();
        return c is null
            ? new Failed<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                $"未注册 INumericConstants<{typeof(T).Name}> / No INumericConstants<{typeof(T).Name}> registered.")
            : Results.Ok<IReadOnlyList<T>>(GetPrimes(num, c));
    }
}
