#nullable enable

using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Algebra.Concept;
/// <summary>
/// 求和扩展方法 / Sum extension methods
/// </summary>
public static class SumExtensions {
    /// <summary>
    /// 安全求和 / Safe sum (returns Ret)
    /// </summary>
    public static Result<T, ErrorCode, Error<ErrorCode>> SumSafe<T>(this IReadOnlyList<T> values)
        where T : struct, IPlus<T, T>, IArithmetic<T> {
        if (values.Count == 0) {
            return Results.Failed<T>(new Err<ErrorCode>(ErrorCode.DataEmpty, "Cannot sum empty collection"));
        }
        T result = values[0];
        for (int i = 1; i < values.Count; i++) {
            result = result.Plus(values[i]);
        }
        return Results.Ok(result);
    }

    /// <summary>
    /// 求和 / Sum
    /// </summary>
    public static T Sum<T>(this IEnumerable<T> values)
        where T : struct, IPlus<T, T>, IArithmetic<T> {
        using IEnumerator<T> enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext()) {
            return default;
        }
        T result = enumerator.Current;
        while (enumerator.MoveNext()) {
            result = result.Plus(enumerator.Current);
        }
        return result;
    }

    /// <summary>
    /// 求和（可能为 null）/ Sum (nullable)
    /// </summary>
    public static T? SumOrNull<T>(this IEnumerable<T> values)
        where T : struct, IPlus<T, T>, IArithmetic<T> {
        using IEnumerator<T> enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext()) {
            return null;
        }
        T result = enumerator.Current;
        while (enumerator.MoveNext()) {
            result = result.Plus(enumerator.Current);
        }
        return result;
    }

    /// <summary>
    /// 带零值求和 / Sum with explicit zero
    /// </summary>
    public static T SumWithZero<T>(this IEnumerable<T> values, T zero)
        where T : struct, IPlus<T, T> {
        T result = zero;
        foreach (T v in values) {
            result = result.Plus(v);
        }
        return result;
    }
}
