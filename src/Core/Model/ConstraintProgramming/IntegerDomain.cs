#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Model.ConstraintProgramming;

/// <summary>
/// 只允许精确有符号 64 位整数的 CP 值域 / CP domain containing only exact signed 64-bit integers.
/// </summary>
/// <remarks>
/// 建模代码应使用工厂，以便非法输入通过 Result 返回。
/// Model-building code should use the factories so invalid input is returned through Result.
/// </remarks>
public abstract record IntegerDomain {
    /// <summary>默认枚举上限 / Default enumeration limit.</summary>
    public const int DefaultEnumerationLimit = 10_000;

    /// <summary>最小值 / Lower bound.</summary>
    public abstract Int64 LowerBound { get; }

    /// <summary>最大值 / Upper bound.</summary>
    public abstract Int64 UpperBound { get; }

    /// <summary>是否为单点值域 / Whether the domain is a singleton.</summary>
    public bool Singleton => LowerBound == UpperBound;

    /// <summary>可表示数量；超出 UInt64 时为空 / Cardinality, or null when it exceeds UInt64.</summary>
    public abstract ulong? Cardinality { get; }

    /// <summary>判断值是否属于值域 / Check whether a value belongs to the domain.</summary>
    /// <param name="value">待检查整数 / Integer to check.</param>
    /// <returns>是否属于值域 / Whether the value belongs.</returns>
    public abstract bool Contains(Int64 value);

    /// <summary>在规模受限时枚举值域 / Enumerate the domain when its size is bounded.</summary>
    /// <param name="maximumAmount">最大枚举数量 / Maximum amount to enumerate.</param>
    /// <returns>有限枚举结果；超限时为空 / Values, or null when over the limit.</returns>
    public IReadOnlyList<Int64>? Enumerate(int maximumAmount = DefaultEnumerationLimit) {
        if (maximumAmount < 0) {
            return null;
        }
        ulong? amount = Cardinality;
        if (amount is null || amount > (ulong)maximumAmount) {
            return null;
        }
        if (this is Values values) {
            return values.Items;
        }

        var result = new List<Int64>((int)amount.Value);
        long current = LowerBound.ToLong();
        long end = UpperBound.ToLong();
        while (current <= end) {
            result.Add(new Int64(current));
            if (current == long.MaxValue) {
                break;
            }
            ++current;
        }
        return result;
    }

    /// <summary>连续整数区间 / Contiguous integer interval.</summary>
    public sealed record Interval : IntegerDomain {
        /// <summary>创建连续整数区间 / Create a contiguous integer interval.</summary>
        /// <param name="lowerBound">下界 / Lower bound.</param>
        /// <param name="upperBound">上界 / Upper bound.</param>
        public Interval(Int64 lowerBound, Int64 upperBound) {
            LowerBound = lowerBound;
            UpperBound = upperBound;
        }

        /// <inheritdoc/>
        public override Int64 LowerBound { get; }

        /// <inheritdoc/>
        public override Int64 UpperBound { get; }

        /// <inheritdoc/>
        public override ulong? Cardinality =>
            InclusiveCardinality(LowerBound.ToLong(), UpperBound.ToLong());

        /// <inheritdoc/>
        public override bool Contains(Int64 value) =>
            value >= LowerBound && value <= UpperBound;
    }

    /// <summary>排序去重的离散整数值域 / Sorted, de-duplicated sparse integer domain.</summary>
    public sealed record Values : IntegerDomain {
        internal Values(Int64[] items) {
            Items = Array.AsReadOnly(items);
        }

        /// <summary>排序去重后的值 / Sorted, de-duplicated values.</summary>
        public IReadOnlyList<Int64> Items { get; }

        /// <inheritdoc/>
        public override Int64 LowerBound => Items[0];

        /// <inheritdoc/>
        public override Int64 UpperBound => Items[^1];

        /// <inheritdoc/>
        public override ulong? Cardinality => (ulong)Items.Count;

        /// <inheritdoc/>
        public override bool Contains(Int64 value) {
            int lower = 0;
            int upper = Items.Count - 1;
            while (lower <= upper) {
                int middle = lower + ((upper - lower) / 2);
                int comparison = Items[middle].CompareTo(value);
                if (comparison == 0) {
                    return true;
                }
                if (comparison < 0) {
                    lower = middle + 1;
                }
                else {
                    upper = middle - 1;
                }
            }
            return false;
        }
    }

    /// <summary>默认布尔值域 `{0, 1}` / Default Boolean domain `{0, 1}`.</summary>
    public static Values Boolean { get; } = new(new[] { new Int64(0), new Int64(1) });

    /// <summary>创建连续整数区间 / Create a contiguous integer interval.</summary>
    /// <param name="lowerBound">下界 / Lower bound.</param>
    /// <param name="upperBound">上界 / Upper bound.</param>
    /// <returns>值域或非法参数错误 / Domain or illegal-argument error.</returns>
    public static Result<Interval, ErrorCode, Error<ErrorCode>> CreateInterval(
        Int64 lowerBound,
        Int64 upperBound) {
        return lowerBound <= upperBound
            ? Results.Ok(new Interval(lowerBound, upperBound))
            : Failure<Interval>(
                "整数值域下界不得大于上界 / Integer domain lower bound must not exceed upper bound");
    }

    /// <summary>创建离散整数集合 / Create a sparse integer set.</summary>
    /// <param name="values">离散值；输入会被排序、去重并复制 / Sparse values, sorted, de-duplicated, and copied.</param>
    /// <returns>值域或非法参数错误 / Domain or illegal-argument error.</returns>
    public static Result<Values, ErrorCode, Error<ErrorCode>> CreateValues(
        IEnumerable<Int64> values) {
        ArgumentNullException.ThrowIfNull(values);
        Int64[] canonical = values
            .Distinct()
            .OrderBy(value => value.ToLong())
            .ToArray();
        return canonical.Length > 0
            ? Results.Ok(new Values(canonical))
            : Failure<Values>("整数离散值域不能为空 / Integer sparse domain must not be empty");
    }

    /// <summary>创建稀疏值域的语义别名 / Create a sparse domain with the semantic alias.</summary>
    /// <param name="values">离散值 / Sparse values.</param>
    /// <returns>值域或非法参数错误 / Domain or illegal-argument error.</returns>
    public static Result<Values, ErrorCode, Error<ErrorCode>> Sparse(IEnumerable<Int64> values) =>
        CreateValues(values);

    /// <summary>创建单点值域 / Create a singleton domain.</summary>
    /// <param name="value">单点值 / Singleton value.</param>
    /// <returns>单点值域 / Singleton domain.</returns>
    public static Values CreateSingleton(Int64 value) => new(new[] { value });

    private static ulong? InclusiveCardinality(long lowerBound, long upperBound) {
        if (lowerBound > upperBound) {
            return null;
        }
        BigInteger cardinality =
            new BigInteger(upperBound) - new BigInteger(lowerBound) + BigInteger.One;
        return cardinality <= ulong.MaxValue ? (ulong)cardinality : null;
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
