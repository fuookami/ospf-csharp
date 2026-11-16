#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// <summary>
/// 跨三类 native 结构的键冲突检查 / Cross-structure key-conflict checking over the three native kinds.
/// </summary>
/// <remarks>
/// 对齐基线 `NativePiecewiseReport.kt` 的 `validateCombinedStructureKeys` 与 `keyText`。
///
/// **这是唯一一处同时看 PWL、ABS、MAX 三类结构的检查，而它做的是<b>两件不同的事</b>：**
///
/// | 阶段 | 问的问题 |
/// | --- | --- |
/// | **登记** | **哪些列被"输出或辅助"占用了，且只能被占用一次** |
/// | **引用** | **有没有哪个结构的<b>输入</b>读了那些会被消掉的辅助列** |
///
/// **登记的范围是"结果列 + 辅助列"，<b>不含输入列</b>。**
/// **所以三个结构共用同一个输入列是合法的——那正是"同一个量的三个不同函数"这个正常情形。**
///
/// **引用检查的范围是"被省略的辅助列"：PWL 选择列、MAX 选择列、ABS 的正部/负部/符号列。**
/// **这些列在建模后<b>会被消掉</b>（它们的值由其他列决定），所以任何拿它们当输入的结构
/// 都会在消元之后留下一个悬空引用。**
///
/// **注意 PWL 与 ABS 的<b>结果</b>列不在这个集合里**：**结果列是真实输出，不会被消掉，
/// 所以拿别人的结果当自己的输入是合法的。**
///
/// **失败消息只给出<b>重复的那个键</b>，不给出<b>谁先占用了它</b>。**
/// **这是一个真实的诊断缺口：看到 `key is duplicated` 之后，调用方还得自己去找是哪两个结构。**
/// **这里照抄基线不改消息格式，但把它记下来。**
///
/// **This is the only check that looks at all three kinds at once, and it does <b>two different things</b>:**
///
/// | Phase | The question |
/// | --- | --- |
/// | **claim** | **which columns are taken as output-or-helper, and can be taken only once** |
/// | **reference** | **whether any structure's <b>input</b> reads a helper column that will be eliminated** |
///
/// **The claim range is "results plus helpers" and <b>excludes input columns</b>.** **So three structures sharing one
/// input column is legal — that is the perfectly normal case of "three different functions of one quantity".**
///
/// **The reference check covers "omitted helpers": PWL selectors, MAX selectors and ABS positive/negative/sign.** **These
/// are <b>eliminated</b> after modelling (their values follow from other columns), so any structure taking one as an input
/// leaves a dangling reference once elimination runs.**
///
/// **Note that PWL and ABS <b>result</b> columns are not in that set**: **a result is a real output and is not eliminated,
/// so taking someone else's result as your input is legal.**
///
/// **The failure message names only the <b>duplicated key</b>, never <b>who claimed it first</b>.** **That is a real
/// diagnostic gap: after `key is duplicated` the caller still has to find the two structures itself.** ** copies
/// the baseline's message format and records the gap.**
/// </remarks>
public static class NativeCombinedKeyValidator
{
    /// <summary>校验跨结构键 / Validate cross-structure keys.</summary>
    public static Result<Success, ErrorCode, Error<ErrorCode>> Validate(
        IReadOnlyList<NormalizedPiecewise> piecewise,
        IReadOnlyList<NormalizedAbs> abs,
        IReadOnlyList<NativeMaxData> maxima) {
        ArgumentNullException.ThrowIfNull(piecewise);
        ArgumentNullException.ThrowIfNull(abs);
        ArgumentNullException.ThrowIfNull(maxima);

        var owners = new Dictionary<VariableItemKey, string>();

        Result<Success, ErrorCode, Error<ErrorCode>>? claimFailure = null;

        for (int index = 0; index < piecewise.Count && claimFailure is null; ++index) {
            NormalizedPiecewise structure = piecewise[index];

            claimFailure = Claim(owners, structure.ResultKey, $"pwl[{index}].result");
            if (claimFailure is not null) {
                break;
            }

            for (int selector = 0; selector < structure.SelectorKeys.Count; ++selector) {
                claimFailure = Claim(
                    owners, structure.SelectorKeys[selector], $"pwl[{index}].selector[{selector}]");
                if (claimFailure is not null) {
                    break;
                }
            }
        }

        for (int index = 0; index < abs.Count && claimFailure is null; ++index) {
            NormalizedAbs structure = abs[index];
            (VariableItemKey Key, string Label)[] claims = {
                (structure.ResultKey, $"abs[{index}].result"),
                (structure.PositiveKey, $"abs[{index}].positive"),
                (structure.NegativeKey, $"abs[{index}].negative"),
                (structure.SignKey, $"abs[{index}].sign"),
            };

            foreach ((VariableItemKey key, string label) in claims) {
                claimFailure = Claim(owners, key, label);
                if (claimFailure is not null) {
                    break;
                }
            }
        }

        for (int index = 0; index < maxima.Count && claimFailure is null; ++index) {
            NativeMaxData structure = maxima[index];

            claimFailure = Claim(owners, structure.ResultKey, $"max[{index}]");
            if (claimFailure is not null) {
                break;
            }

            foreach (VariableItemKey key in structure.SelectorKeys) {
                claimFailure = Claim(owners, key, $"max[{index}]");
                if (claimFailure is not null) {
                    break;
                }
            }
        }

        if (claimFailure is not null) {
            return claimFailure;
        }

        // **被省略的辅助列：这些在建模后会被消掉。**
        // **Omitted helper columns: these are eliminated after modelling.**
        var omittedHelpers = new HashSet<VariableItemKey>();

        foreach (NormalizedPiecewise structure in piecewise) {
            foreach (VariableItemKey key in structure.SelectorKeys) {
                omittedHelpers.Add(key);
            }
        }

        foreach (NativeMaxData structure in maxima) {
            foreach (VariableItemKey key in structure.SelectorKeys) {
                omittedHelpers.Add(key);
            }
        }

        foreach (NormalizedAbs structure in abs) {
            omittedHelpers.Add(structure.PositiveKey);
            omittedHelpers.Add(structure.NegativeKey);
            omittedHelpers.Add(structure.SignKey);
        }

        foreach (NormalizedPiecewise structure in piecewise) {
            if (omittedHelpers.Contains(structure.InputKey)) {
                return Failure("PWL input references an omitted helper");
            }
        }

        foreach (NormalizedAbs structure in abs) {
            if (omittedHelpers.Contains(structure.InputKey)) {
                return Failure("ABS input references an omitted helper");
            }
        }

        foreach (NativeMaxData structure in maxima) {
            foreach (NativeMaxInputData input in structure.Inputs) {
                foreach (VariableItemKey key in input.Terms.Keys) {
                    if (omittedHelpers.Contains(key)) {
                        return Failure("MAX input references an omitted helper");
                    }
                }
            }
        }

        return new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());
    }

    /// <summary>把键与列文本化 / Render a key as text.</summary>
    /// <remarks>
    /// **`identifier:index` 两段都要——只给 `identifier` 的话，一个组合变量的多个维度会打印成同一个字符串。**
    ///
    /// **Both parts are needed: with `identifier` alone, several dimensions of one combination variable print
    /// identically.**
    /// </remarks>
    public static string KeyText(VariableItemKey key) =>
        $"{key.Identifier}:{key.Index}";

    private static Result<Success, ErrorCode, Error<ErrorCode>>? Claim(
        Dictionary<VariableItemKey, string> owners,
        VariableItemKey key,
        string owner) {
        if (owners.TryAdd(key, owner)) {
            return null;
        }

        return Failure(
            $"native function key is duplicated: {KeyText(key)}");
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        new Failed<Success, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);
}
