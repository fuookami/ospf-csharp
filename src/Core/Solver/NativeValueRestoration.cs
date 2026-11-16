#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// <summary>
/// 把 native 解还原成原始 token 顺序的值 / Restore a native solution into the original token order.
/// </summary>
/// <remarks>
/// 对齐基线 `NativePiecewiseReport.kt` 的 `restoreValues` 与 `maxInputValues`。
///
/// **五路查找的<b>顺序就是语义</b>：**
///
/// ```
/// nativeValues[key] ?? selectors[key] ?? absHelpers[key] ?? maxHelpers[key] ?? semiHelpers[key]
/// ```
///
/// **`??` 在 Kotlin 里是"这个键<b>存在</b>吗"，不是"它的值是真值吗"——
/// 所以一个值为 `Flt64.zero` 的键照样命中。**
///
/// **而"第一个命中就赢"意味着：同一个键出现在两路里时，<b>前一路静默胜出</b>。**
///
/// **这不是理论问题：** **发现 `absHelperValues` <b>不</b>拒绝"解里含有辅助列"，
/// 所以一个解里带着陈旧的 `positiveKey`，`nativeValues` 会赢——
/// 那个陈旧的值会原样进到还原结果里，而重建出来的正确值被丢掉。**
///
/// **选择列没有这个问题，因为 `selectorValues` 会先把"解里含选择列"拒掉。**
/// **所以同一份代码里，一类辅助列是安全的，另一类不是，差别来自上一层的一道检查。**
///
/// **还原结果按 `originalTokens` 的顺序排列**，所以它可以逐位贴回调用方的 token 列表。
/// **拼错的顺序不会报错，只会让每个变量的值贴到别的变量上——所以这一步必须按 token 走，
/// 而不是按字典的迭代顺序。**
///
/// **The <b>order</b> of the five-way lookup <b>is</b> the semantics.**
///
/// **Kotlin's `??` asks "does this key <b>exist</b>", not "is its value truthy" — so a key holding `Flt64.zero` still
/// matches.**
///
/// **And "first hit wins" means a key present in two sources has the <b>earlier one silently win</b>.**
///
/// **This is not theoretical: measurement shows that `absHelperValues` does <b>not</b> reject a solution containing helpers, so a
/// solution carrying a stale `positiveKey` lets `nativeValues` win — the stale value reaches the restored result and the
/// correctly reconstructed one is dropped.**
///
/// **Selectors do not have this problem because `selectorValues` rejects "the solution contains selectors" first.** **So in
/// one piece of code one kind of helper is safe and the other is not, and the difference comes from a check one layer
/// up.**
///
/// **The restored list follows the `originalTokens` order so it can be zipped back position by position.** **Getting the
/// order wrong raises nothing; it just attaches each value to another variable — so this step must walk the tokens, not a
/// dictionary's iteration order.**
/// </remarks>
public static class NativeValueRestoration
{
    /// <summary>算一个 MAX 结构各输入在解处的取值 / Evaluate a MAX structure's inputs at the solution.</summary>
    public static Result<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>> MaxInputValues(
        IReadOnlyDictionary<VariableItemKey, Flt64> values,
        NativeMaxData structure) {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(structure);

        var inputs = new List<double>(structure.Inputs.Count);

        foreach (NativeMaxInputData input in structure.Inputs) {
            double value = input.Constant;

            foreach (KeyValuePair<VariableItemKey, double> term in input.Terms) {
                if (!values.TryGetValue(term.Key, out Flt64 variable)) {
                    return FailInputs("MAX input token is missing");
                }

                double converted;
                try {
                    converted = variable.ToSolverDouble();
                }
                catch (Exception error) {
                    return FailInputs($"numeric conversion failed at native.max.input: {error.Message}");
                }

                if (!double.IsFinite(converted)) {
                    return FailInputs("numeric value must be finite: native.max.input");
                }

                value += term.Value * converted;

                if (!double.IsFinite(value)) {
                    return FailInputs("MAX input is not finite");
                }
            }

            inputs.Add(value);
        }

        return new Ok<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>>(inputs);
    }

    /// <summary>把 native 解还原成原始 token 顺序 / Restore into the original token order.</summary>
    public static Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> Restore(
        IReadOnlyList<Flt64> values,
        IReadOnlyDictionary<VariableItemKey, int> columns,
        IReadOnlyList<Token<Flt64>> originalTokens,
        IReadOnlyList<NormalizedPiecewise> structures,
        IReadOnlyList<NormalizedAbs> absStructures,
        IReadOnlyList<NativeMaxData> maxStructures,
        IReadOnlyList<SemiStructure> semiStructures) {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(originalTokens);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(absStructures);
        ArgumentNullException.ThrowIfNull(maxStructures);
        ArgumentNullException.ThrowIfNull(semiStructures);

        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>
            projected = NativeSolutionProjection.ValuesByKey(values, columns);
        if (projected is Failed<
                IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> badValues) {
            return Fail(badValues.Error.Message);
        }

        IReadOnlyDictionary<VariableItemKey, Flt64> nativeValues =
            ((Ok<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)projected)
            .Value;

        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> selected =
            NativeHelperReconstruction.SelectorValues(nativeValues, structures);
        if (selected is Failed<
                IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> badSelectors) {
            return Fail(badSelectors.Error.Message);
        }

        IReadOnlyDictionary<VariableItemKey, Flt64> selectors =
            ((Ok<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)selected)
            .Value;

        Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> absHelpers =
            NativeHelperReconstruction.AbsHelperValues(nativeValues, absStructures);
        if (absHelpers is Failed<
                IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>> badAbs) {
            return Fail(badAbs.Error.Message);
        }

        IReadOnlyDictionary<VariableItemKey, Flt64> abss =
            ((Ok<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>)absHelpers)
            .Value;

        var maxHelpers = new Dictionary<VariableItemKey, Flt64>();
        foreach (NativeMaxData structure in maxStructures) {
            Result<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>> evaluated =
                MaxInputValues(nativeValues, structure);
            if (evaluated is Failed<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>> badMax) {
                return Fail(badMax.Error.Message);
            }

            IReadOnlyList<double> inputs =
                ((Ok<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>>)evaluated).Value;

            // **`indexOf(maxOrNull)` 取的是第一个极值：并列时先出现的那支胜出。**
            // **`indexOf(maxOrNull)` takes the first extreme: on a tie the earlier input wins.**
            int chosen = 0;
            for (int index = 1; index < inputs.Count; ++index) {
                bool better = structure.Minimum
                    ? inputs[index] < inputs[chosen]
                    : inputs[index] > inputs[chosen];
                if (better) {
                    chosen = index;
                }
            }

            for (int index = 0; index < structure.SelectorKeys.Count; ++index) {
                maxHelpers[structure.SelectorKeys[index]] =
                    index == chosen ? Flt64.One : Flt64.Zero;
            }
        }

        var semiHelpers = new Dictionary<VariableItemKey, Flt64>();
        foreach (SemiStructure structure in semiStructures) {
            if (!nativeValues.TryGetValue(structure.ResultVariable.Key, out Flt64 result)) {
                return Fail("semi-continuous result token is missing");
            }

            double lower = structure.Converter.FromValue(structure.LowerBound).ToSolverDouble();
            semiHelpers[structure.IndicatorVariable.Key] =
                result.ToSolverDouble() >= lower ? Flt64.One : Flt64.Zero;
        }

        // **按 token 走，不按字典的迭代顺序走。**
        // **Walk the tokens, not a dictionary's iteration order.**
        var restored = new List<Flt64>(originalTokens.Count);
        foreach (Token<Flt64> token in originalTokens) {
            if (nativeValues.TryGetValue(token.Key, out Flt64 value)) {
                restored.Add(value);
                continue;
            }

            if (selectors.TryGetValue(token.Key, out value)) {
                restored.Add(value);
                continue;
            }

            if (abss.TryGetValue(token.Key, out value)) {
                restored.Add(value);
                continue;
            }

            if (maxHelpers.TryGetValue(token.Key, out value)) {
                restored.Add(value);
                continue;
            }

            if (semiHelpers.TryGetValue(token.Key, out value)) {
                restored.Add(value);
                continue;
            }

            return Fail(
                $"original token has no native value: {NativeCombinedKeyValidator.KeyText(token.Key)}");
        }

        return new Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>(restored);
    }

    private static Result<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>> FailInputs(
        string message) =>
        new Failed<IReadOnlyList<double>, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, message);

    private static Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        new Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, message);
}
