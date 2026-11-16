#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// &lt;summary>
/// 重建被省略的辅助列 / Reconstruct the omitted helper columns.
/// &lt;/summary>
/// &lt;remarks>
/// 对齐的 `selectorValues` 与 `absHelperValues`。
///
/// **两个函数做的事看起来一样：从解里算出那些<b>不在解里</b>的列。**
/// **但它们的严格程度差得很远，而那个差别有理由。**
///
/// | | `selectorValues` | `absHelperValues` |
/// | --- | --- | --- |
/// | **解里出现了辅助列** | **拒绝**（`must omit selectors`） | **不检查** |
/// | **重键冲突** | **检查**（`reconstruction conflicts`） | **不检查** |
///
/// **为什么选择列要拒绝"解里含有它"：** **选择列在建模时被消掉了，
/// 所以一个"含选择列的解"意味着调用方给的是<b>消元之前</b>的向量。**
/// **那时的选择列值与这里重建出来的可能不一致，而两个来源的同一个量是最难查的一类错。**
///
/// **为什么 ABS 不检查这一条：** **ABS 的辅助列（正部/负部/符号）同样是消掉的，
/// 但这里没有写这条检查。** **单方面加检查会让两侧行为分岔，所以沿用现状。**
/// **记在这里，作为一处已知的不对称。**
///
/// **为什么 ABS 不需要冲突检查：** **跨结构的辅助键唯一性已由
/// `NativeCombinedKeyValidator` 保证，所以同一个键不会从两个结构出来。**
/// **而 `selectorValues` 的冲突检查在正常流水线里<b>也</b>触发不到——
/// 跨结构的选择键同样被那一层拒掉了。**
/// **它是一道<b>防御性</b>检查，只有在直接调用这个函数时才可能命中。**
///
/// **Both functions look alike: derive the columns that are <b>not</b> in the solution.**
/// **But their strictness differs a lot, and the difference has reasons.**
///
/// | | `selectorValues` | `absHelperValues` |
/// | --- | --- | --- |
/// | **a helper appears in the solution** | **rejected** | **not checked** |
/// | **duplicate-key conflict** | **checked** | **not checked** |
///
/// **Why selectors reject "the solution contains one": a selector is eliminated at model build time, so a solution
/// containing one means the caller passed the <b>pre-elimination</b> vector.** **Its selector values may disagree with
/// what is reconstructed here, and the same quantity from two sources is among the hardest mistakes to find.**
///
/// **Why ABS does not check it: ABS helpers are eliminated too, but no such check exists here.** **This follows
/// the existing behaviour — adding a check it lacks would fork the two sides.** **Recorded as a known
/// asymmetry.**
///
/// **Why ABS needs no conflict check: cross-structure helper-key uniqueness is already guaranteed by
/// `NativeCombinedKeyValidator`, so one key cannot come from two structures.** **And `selectorValues`'s conflict check
/// <b>also</b> cannot fire on the normal pipeline for the same reason — it is <b>defensive</b>, reachable only when the
/// function is called directly.**
/// &lt;/remarks>
public static class NativeHelperReconstruction
{
    /// &lt;summary>重建 PWL 选择列 / Reconstruct the PWL selector columns.&lt;/summary>
    public static Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>
        SelectorValues(
            IReadOnlyDictionary<VariableItemKey, Flt64> nativeValues,
            IReadOnlyList<NormalizedPiecewise> structures) {
        ArgumentNullException.ThrowIfNull(nativeValues);
        ArgumentNullException.ThrowIfNull(structures);

        var result = new Dictionary<VariableItemKey, Flt64>();

        foreach (NormalizedPiecewise structure in structures) {
            if (!nativeValues.TryGetValue(structure.InputKey, out Flt64 input)) {
                return Failure("PWL input token is missing");
            }

            if (!nativeValues.ContainsKey(structure.ResultKey)) {
                return Failure("PWL result token is missing");
            }

            foreach (VariableItemKey key in structure.SelectorKeys) {
                if (nativeValues.ContainsKey(key)) {
                    return Failure("native solution must omit selectors");
                }
            }

            Result<SegmentSelection, ErrorCode, Error<ErrorCode>> selected =
                NativePiecewiseReport.SelectSegment(input, structure.Breakpoints);
            if (selected is Failed<SegmentSelection, ErrorCode, Error<ErrorCode>> bad) {
                return new Failed<
                    IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>(
                    bad.Error.Code, bad.Error.Message);
            }

            SegmentSelection selection =
                ((Ok<SegmentSelection, ErrorCode, Error<ErrorCode>>)selected).Value;

            for (int index = 0; index < structure.SelectorKeys.Count; ++index) {
                Flt64 value = index == selection.Index ? Flt64.One : Flt64.Zero;

                if (result.TryGetValue(structure.SelectorKeys[index], out Flt64 previous) &&
                    previous != value) {
                    return Failure("PWL selector reconstruction conflicts");
                }

                result[structure.SelectorKeys[index]] = value;
            }
        }

        return new Ok<
            IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>(result);
    }

    /// &lt;summary>重建 ABS 的正部、负部与符号列 / Reconstruct ABS positive, negative and sign columns.&lt;/summary>
    /// &lt;remarks>
    /// **符号列的零点取 <b>`1`</b>：** **`input &lt; 0` 取 `0`，否则取 `1`，
    /// 所以 `input == 0` 时符号是 `1`。**
    ///
    /// **这与"正部在零点为 `0`"一起构成一套约定：** **零点落在非负那一侧。**
    /// **约定本身没有对错，但必须确定且与建模侧的 `sign` 列一致——
    /// 否则求解器给的 `sign` 与报告重建的 `sign` 会差一。**
    ///
    /// **The sign column takes <b>`1`</b> at zero: `input &lt; 0` gives `0`, otherwise `1`, so `input == 0` signs as `1`.**
    ///
    /// **Together with "the positive part is zero at zero" this is a convention: <b>zero falls on the nonnegative
    /// side</b>.** **The convention is neither right nor wrong, but it must be fixed and agree with the modelled `sign`
    /// column — otherwise the solver's sign and the reconstructed one differ by one.**
    /// &lt;/remarks>
    public static Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>
        AbsHelperValues(
            IReadOnlyDictionary<VariableItemKey, Flt64> nativeValues,
            IReadOnlyList<NormalizedAbs> structures) {
        ArgumentNullException.ThrowIfNull(nativeValues);
        ArgumentNullException.ThrowIfNull(structures);

        var result = new Dictionary<VariableItemKey, Flt64>(structures.Count * 3);

        foreach (NormalizedAbs structure in structures) {
            if (!nativeValues.TryGetValue(structure.InputKey, out Flt64 input)) {
                return Failure("ABS input token is missing");
            }

            if (!nativeValues.ContainsKey(structure.ResultKey)) {
                return Failure("ABS result token is missing");
            }

            Flt64 zero = Flt64.Zero;
            Flt64 positive = input.CompareTo(zero) > 0 ? input : zero;
            Flt64 negative = input.CompareTo(zero) < 0 ? input.Negate() : zero;
            Flt64 sign = input.CompareTo(zero) < 0 ? Flt64.Zero : Flt64.One;

            result[structure.PositiveKey] = positive;
            result[structure.NegativeKey] = negative;
            result[structure.SignKey] = sign;
        }

        return new Ok<
            IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>(result);
    }

    private static Result<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>
        Failure(string message) =>
            new Failed<IReadOnlyDictionary<VariableItemKey, Flt64>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}
