#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// <summary>
/// 按列号把解向量摊回变量键 / Spread a solution vector back onto variable keys.
/// </summary>
/// <remarks>
/// 对齐基线 `NativePiecewiseReport.kt` 的 `valuesByKey` 与 `finiteSolverDouble`。
/// **`nativeColumns` 依赖 `LinearTriadModel` 的 `origin` 与 token 概念，
/// 而 C# 侧的 `ModelViewVariable` 没有 `Origin`，所以那一格留到后续批次。**
///
/// **这个函数最重要的一点：数值检查是一道<b>闸门</b>，不是一次<b>转换</b>。**
///
/// **实现里调用了 `finiteSolverDouble(value, ...)`，但<b>丢掉了它的返回值</b>，
/// 存进结果的是<b>原来那个 `Flt64`</b>。**
///
/// **所以这条链上发生了两件不同的事：**
///
/// | 步骤 | 做了什么 |
/// | --- | --- |
/// | `finiteSolverDouble` | **把值转成 `double` 并检查有限——只为了回答"能不能用"** |
/// | 存入 | **原始的 `Flt64`，一个 bit 都没动** |
///
/// **这个区别在 `SolveValueConversionPolicy.AllowRounding` 下会显形：**
/// **转换会四舍五入到 15 位，而存下来的是没被四舍五入的那个。**
///
/// **这不是一个 bug——报告层本来就该报求解器给的数，而不是把它磨一遍再报。**
/// **但它确实意味着"检查通过"与"结果被转换过"是两句话，
/// 把它们混成一句会让"报告值 == 求解器值"这个不变量悄悄消失。**
///
/// **The most important point: the numeric check is a <b>gate</b>, not a <b>transform</b>.**
///
/// **The implementation calls `finiteSolverDouble(value, ...)` and <b>discards its result</b>, storing the <b>original
/// `Flt64`</b>.** **So two different things happen:** the conversion answers "is this usable", and the store keeps the
/// value bit-for-bit.
///
/// **The difference shows under `SolveValueConversionPolicy.AllowRounding`**: the conversion rounds to 15 digits while
/// what is stored was never rounded.
///
/// **That is not a bug — a report should show the number the solver produced, not a polished one.** **But it does mean
/// "the check passed" and "the value was converted" are two statements, and merging them silently loses the invariant
/// "the reported value is the solver's value".**
/// </remarks>
public static class NativeSolutionProjection
{
    /// <summary>按列号把解向量摊回变量键 / Spread a solution vector back onto variable keys.</summary>
    public static Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>
        ValuesByKey(
            IReadOnlyList<Flt64> values,
            IReadOnlyDictionary<VariableItemKey, int> columns) {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(columns);

        if (values.Count != columns.Count) {
            return Failure("native solution length does not match columns");
        }

        // **基线用 `LinkedHashMap` 保留插入顺序，C# 侧用普通字典。**
        // **下游全部按键查值，没有一处依赖顺序，所以这个差异没有行为后果——但它是一处<b>有意的</b>差异，
        // 而不是"忘了"。**
        //
        // **The baseline uses a `LinkedHashMap` to keep insertion order; this uses a plain dictionary.**
        // **Every downstream use looks values up by key and none depends on order, so the difference has no behavioural
        // consequence — but it is a <b>deliberate</b> one, not an oversight.**
        var result = new Dictionary<VariableItemKey, Flt64>(columns.Count);

        foreach (KeyValuePair<VariableItemKey, int> pair in columns) {
            int column = pair.Value;
            if (column < 0 || column >= values.Count) {
                return Failure($"native solution is missing column {column}");
            }

            Flt64 value = values[column];

            // **只为"能不能用"而转换，转换结果丢掉。**
            // **Converted only to answer "is this usable"; the converted number is dropped.**
            Result<double, ErrorCode, Error<ErrorCode>> checked_ =
                FiniteSolverDouble(value, $"native.solution[{column}]");
            if (checked_ is Failed<double, ErrorCode, Error<ErrorCode>> bad) {
                return new Failed<
                    IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>(
                    bad.Error.Code, bad.Error.Message);
            }

            // **存原始的 `Flt64`，不是转换后的 `double`。**
            // **Stores the original `Flt64`, not the converted `double`.**
            result[pair.Key] = value;
        }

        return new Ok<
            IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>(result);
    }

    private static Result<double, ErrorCode, Error<ErrorCode>> FiniteSolverDouble(
        Flt64 value,
        string fieldName) {
        try {
            double converted = value.ToSolverDouble();
            if (!double.IsFinite(converted)) {
                return new Failed<double, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument,
                    $"numeric value must be finite: {fieldName}");
            }

            return new Ok<double, ErrorCode, Error<ErrorCode>>(converted);
        }
        catch (Exception error) {
            return new Failed<double, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"numeric conversion failed at {fieldName}: {error.Message}");
        }
    }

    private static Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>
        Failure(string message) =>
            new Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}
