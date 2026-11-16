#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// &lt;summary>
/// 归一化后的分段线性结构 / A normalized piecewise structure.
/// &lt;/summary>
/// &lt;remarks>按基线 `NormalizedPiecewise` 补。 / Adds the baseline's `NormalizedPiecewise`.&lt;/remarks>
public sealed record NormalizedPiecewise(
    VariableItemKey InputKey,
    VariableItemKey ResultKey,
    IReadOnlyList<VariableItemKey> SelectorKeys,
    IReadOnlyList<Flt64> Breakpoints,
    IReadOnlyList<Flt64> Values);

/// &lt;summary>
/// 归一化后的绝对值结构 / A normalized absolute-value structure.
/// &lt;/summary>
/// &lt;remarks>按基线 `NormalizedAbs` 补。 / Adds the baseline's `NormalizedAbs`.&lt;/remarks>
public sealed record NormalizedAbs(
    VariableItemKey InputKey,
    VariableItemKey ResultKey,
    VariableItemKey PositiveKey,
    VariableItemKey NegativeKey,
    VariableItemKey SignKey);

/// &lt;summary>
/// 把结构列表归一化成 primitive 数据 / Normalize structure lists into primitive data.
/// &lt;/summary>
/// &lt;remarks>
/// 对齐基线 `NativePiecewiseReport.kt` 的 `normalizePiecewiseStructures` 与
/// `normalizeAbsStructures` 补。
///
/// **两层唯一性检查，管的不是同一件事：**
///
/// | 层 | 检查 | 范围 |
/// | --- | --- | --- |
/// | **primitive** | **一个结构内部五键（PWL 是结果+选择）互不相同** | **单个结构** |
/// | **归一化**（这里） | **结果键在<b>整个列表</b>里唯一** | **跨结构** |
///
/// **所以两者都不能省：** **一个结构自己没问题，仍然可能和另一个结构抢同一列。**
///
/// **而"输入列"<b>不在</b>跨结构唯一性里。** **PWL 只登记 `resultKey`，ABS 只登记
/// `resultKey`/`positive`/`negative`/`sign` 四个。**
/// **所以两个 ABS 共用同一个输入列是<b>允许</b>的——`abs(x)` 与 `abs(x)` 可以合法地出现两次（结果列不同）。**
///
/// **PWL 的 selector 数量检查是<b>有条件的</b>：`selectorKeys.isNotEmpty &amp;&amp; ...`。**
///
/// **也就是说<b>零个选择变量能通过归一化</b>。**
/// **而行生成里 `SelectorVariables.Count != segments` 直接拒绝。**
///
/// **两层严格程度不同，而宽松的那一层在前。** **这不是矛盾——归一化只保证"数据形状可用"，
/// 行生成才保证"能建出约束"——但它意味着<b>一个零选择的快照会走到很后面才被拒</b>。**
/// **这里照抄基线，并把这件事写成判据，让它成为一个被记录的事实。**
///
/// **Two levels of uniqueness, checking different things:**
///
/// | Level | Check | Scope |
/// | --- | --- | --- |
/// | **primitive** | **five keys of one structure are pairwise distinct** | **a single structure** |
/// | **normalization** (here) | **the result key is unique <b>across the whole list</b>** | **across structures** |
///
/// **Neither can be dropped: a structure that is fine on its own can still fight another one for the same column.**
///
/// **The <b>input</b> column is <b>not</b> part of cross-structure uniqueness.** **PWL registers only `resultKey`; ABS
/// registers `resultKey`/`positive`/`negative`/`sign`.** **So two ABS structures sharing one input column are
/// <b>allowed</b> — `abs(x)` may legitimately appear twice with different result columns.**
///
/// **The PWL selector-count check is <b>conditional</b>: `selectorKeys.isNotEmpty &amp;&amp; ...`.**
///
/// **So <b>zero selectors passes normalization</b>**, while the earlier row generation rejects
/// `SelectorVariables.Count != segments` outright.**
///
/// **The two layers differ in strictness and the lenient one comes first.** **Not a contradiction — normalization only
/// guarantees "the data shape is usable" while row generation guarantees "constraints can be built" — but it does mean
/// a zero-selector snapshot travels a long way before being rejected.** ** copies the baseline and turns that
/// into a criterion so it is a recorded fact.**
/// &lt;/remarks>
public static class NativePrimitiveNormalizer
{
    /// &lt;summary>归一化 PWL 列表 / Normalize a PWL list.&lt;/summary>
    public static Result<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>
        NormalizePiecewise(IReadOnlyList<UnivariateLinearPiecewiseStructure> structures) {
        ArgumentNullException.ThrowIfNull(structures);

        var normalized = new List<NormalizedPiecewise>(structures.Count);
        var resultKeys = new HashSet<VariableItemKey>();

        foreach (UnivariateLinearPiecewiseStructure structure in structures) {
            Result<NativePiecewiseData, ErrorCode, Error<ErrorCode>> prepared =
                NativePiecewise.Prepare(structure);
            if (prepared is Failed<NativePiecewiseData, ErrorCode, Error<ErrorCode>> bad) {
                return Forward(bad.Error);
            }

            NativePiecewiseData data =
                ((Ok<NativePiecewiseData, ErrorCode, Error<ErrorCode>>)prepared).Value;

            var selectorKeys = new List<VariableItemKey>(structure.SelectorVariables.Count);
            foreach (IVariableItem selector in structure.SelectorVariables) {
                selectorKeys.Add(selector.Key);
            }

            // **数量检查是有条件的：零个选择变量放行。**
            // **The count check is conditional: zero selectors pass.**
            if (selectorKeys.Count > 0 && selectorKeys.Count != data.XPoints.Length - 1) {
                return Failure("native PWL selector count is inconsistent");
            }

            if (new HashSet<VariableItemKey>(selectorKeys).Count != selectorKeys.Count) {
                return Failure("native PWL selector keys are duplicated");
            }

            if (!resultKeys.Add(structure.ResultVariable.Key)) {
                return Failure("native PWL result keys are duplicated");
            }

            var breakpoints = new Flt64[data.XPoints.Length];
            for (int index = 0; index < data.XPoints.Length; ++index) {
                breakpoints[index] = new Flt64(data.XPoints[index]);
            }

            var values = new Flt64[data.YPoints.Length];
            for (int index = 0; index < data.YPoints.Length; ++index) {
                values[index] = new Flt64(data.YPoints[index]);
            }

            normalized.Add(new NormalizedPiecewise(
                InputKey: data.InputKey,
                ResultKey: data.ResultKey,
                SelectorKeys: selectorKeys,
                Breakpoints: breakpoints,
                Values: values));
        }

        return new Ok<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>(normalized);
    }

    /// &lt;summary>归一化 ABS 列表 / Normalize an ABS list.&lt;/summary>
    public static Result<IReadOnlyList<NormalizedAbs>, ErrorCode, Error<ErrorCode>>
        NormalizeAbs(IReadOnlyList<AbsStructure> structures) {
        ArgumentNullException.ThrowIfNull(structures);

        var normalized = new List<NormalizedAbs>(structures.Count);
        var declarationKeys = new HashSet<VariableItemKey>();

        foreach (AbsStructure structure in structures) {
            Result<NativeAbsData, ErrorCode, Error<ErrorCode>> prepared = NativeAbs.Prepare(structure);
            if (prepared is Failed<NativeAbsData, ErrorCode, Error<ErrorCode>> bad) {
                return ForwardAbs(bad.Error);
            }

            NativeAbsData data = ((Ok<NativeAbsData, ErrorCode, Error<ErrorCode>>)prepared).Value;

            VariableItemKey positiveKey = structure.PositiveVariable.Key;
            VariableItemKey negativeKey = structure.NegativeVariable.Key;
            VariableItemKey signKey = structure.SignVariable.Key;

            // **结构内部的五键唯一：`NativeAbs.Prepare` 已经查过一遍，这里照抄基线再查一次。**
            // **重复在这里是<b>无害的多余</b>：两道检查用的是同一个判据，谁也拦不住对方拦不住的。**
            // **The in-structure five-key uniqueness was already checked by `NativeAbs.Prepare`; the baseline repeats it
            // and so does this.** **The duplication is <b>harmless but inert</b>: both use the same predicate, so neither
            // catches what the other misses.**
            var keys = new VariableItemKey[] {
                data.InputKey, data.ResultKey, positiveKey, negativeKey, signKey,
            };

            if (new HashSet<VariableItemKey>(keys).Count != keys.Length) {
                return FailureAbs("native ABS keys are duplicated");
            }

            // **跨结构只登记四个——输入列不在其中。**
            // **Only four are registered across structures; the input column is not among them.**
            foreach (VariableItemKey key in new[] {
                data.ResultKey, positiveKey, negativeKey, signKey,
            }) {
                if (!declarationKeys.Add(key)) {
                    return FailureAbs("native ABS keys are duplicated");
                }
            }

            normalized.Add(new NormalizedAbs(
                InputKey: data.InputKey,
                ResultKey: data.ResultKey,
                PositiveKey: positiveKey,
                NegativeKey: negativeKey,
                SignKey: signKey));
        }

        return new Ok<IReadOnlyList<NormalizedAbs>, ErrorCode, Error<ErrorCode>>(normalized);
    }

    private static Result<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>
        Failure(string message) =>
            new Failed<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);

    private static Result<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>
        Forward(Error<ErrorCode> error) =>
            new Failed<IReadOnlyList<NormalizedPiecewise>, ErrorCode, Error<ErrorCode>>(
                error.Code, error.Message);

    private static Result<IReadOnlyList<NormalizedAbs>, ErrorCode, Error<ErrorCode>>
        FailureAbs(string message) =>
            new Failed<IReadOnlyList<NormalizedAbs>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);

    private static Result<IReadOnlyList<NormalizedAbs>, ErrorCode, Error<ErrorCode>>
        ForwardAbs(Error<ErrorCode> error) =>
            new Failed<IReadOnlyList<NormalizedAbs>, ErrorCode, Error<ErrorCode>>(
                error.Code, error.Message);
}
