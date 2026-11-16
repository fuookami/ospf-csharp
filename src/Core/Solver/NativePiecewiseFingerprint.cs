#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// <summary>
/// PWL 结构的审计指纹 / Audit fingerprint over the PWL structures.
/// </summary>
/// <remarks>
/// 对齐的 `piecewiseFingerprint` 与 `numberListText`。
/// **`nativeFingerprint` 还需要 `NativeIndicatorData` 与 `NativeMaskingData`，留到后续批次。**
///
/// **两处行为值得单独点出来，因为它们都<b>不是</b>"把所有的东西拼起来再哈希"那么简单：**
///
/// **第一处：行被<b>排序</b>之后才拼。**
///
/// **`structureLines.sorted.joinToString("\n")`。**
///
/// **所以同一个结构集合，无论以什么顺序给出，指纹相同。**
/// **这不是"顺手排一下"——它是"指纹描述的是<b>集合</b>而不是<b>列表</b>"这个语义的直接体现。**
///
/// **如果不排序，一个仅仅在顺序上不同的模型会得到不同的指纹，
/// 而审计指纹的用途恰恰是"这两个模型是不是同一个"。**
///
/// **第二处：空列表时<b>不加换行</b>。**
///
/// **`append(fingerprint.value)` 之后是 `if (structureLines.isNotEmpty) { append('\n'); ... }`。**
///
/// **所以"没有 PWL 结构"时的内容就是上一阶段的指纹本身，
/// 而不是"上一阶段指纹 + 一个换行"。**
///
/// **这个差别只影响一个字符，但它决定了"无结构"与"结构列表为空"是不是同一个状态——
/// 而在这个函数里它们确实是同一个状态，所以<b>不该</b>多出一个换行。**
///
/// **Two behaviours deserve to be called out, because neither is simply "concatenate everything and hash":**
///
/// **First, the lines are <b>sorted</b> before joining.**
///
/// **So the same set of structures yields the same fingerprint in any order.** **That is not an incidental sort — it is
/// the semantics "the fingerprint describes a <b>set</b>, not a <b>list</b>".**
///
/// **Without sorting, a model differing only in order would fingerprint differently, and the whole point of an audit
/// fingerprint is "are these two models the same one".**
///
/// **Second, an empty list adds <b>no newline</b>.**
///
/// **So "there are no PWL structures" hashes to the previous stage's fingerprint itself, not "that fingerprint plus a
/// newline".**
///
/// **The difference is one character, but it decides whether "no structures" and "an empty list of structures" are the
/// same state — and in this function they are, so the extra newline must <b>not</b> appear.**
/// </remarks>
public static class NativePiecewiseFingerprint
{
    /// <summary>算 PWL 指纹 / Compute the PWL fingerprint.</summary>
    public static Result<AuditFingerprint, ErrorCode, Error<ErrorCode>> Compute(
        AuditFingerprint fingerprint,
        IReadOnlyDictionary<VariableItemKey, int> columns,
        IReadOnlyList<NormalizedPiecewise> structures,
        string backendName) {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(backendName);

        var lines = new List<string>(structures.Count);

        foreach (NormalizedPiecewise structure in structures) {
            if (!columns.TryGetValue(structure.InputKey, out int inputColumn)) {
                return Fail("PWL input column is missing");
            }

            if (!columns.TryGetValue(structure.ResultKey, out int resultColumn)) {
                return Fail("PWL result column is missing");
            }

            Result<string, ErrorCode, Error<ErrorCode>> breakpoints =
                NumberListText(structure.Breakpoints, "native.pwl.fingerprint.breakpoints");
            if (breakpoints is Failed<string, ErrorCode, Error<ErrorCode>> badBreakpoints) {
                return Fail(badBreakpoints.Error.Message);
            }

            Result<string, ErrorCode, Error<ErrorCode>> values =
                NumberListText(structure.Values, "native.pwl.fingerprint.values");
            if (values is Failed<string, ErrorCode, Error<ErrorCode>> badValues) {
                return Fail(badValues.Error.Message);
            }

            lines.Add(string.Join("|",
                $"input-column={inputColumn}",
                $"result-column={resultColumn}",
                $"breakpoints={((Ok<string, ErrorCode, Error<ErrorCode>>)breakpoints).Value}",
                $"values={((Ok<string, ErrorCode, Error<ErrorCode>>)values).Value}"));
        }

        var content = new System.Text.StringBuilder(fingerprint.Value);

        // **空列表不加换行：见上面的注释。**
        // **No newline for an empty list; see the remarks above.**
        if (lines.Count > 0) {
            lines.Sort(StringComparer.Ordinal);
            content.Append('\n');
            content.Append(string.Join("\n", lines));
        }

        return new Ok<AuditFingerprint, ErrorCode, Error<ErrorCode>>(
            SolveFingerprinting.Sha256(content.ToString(), $"{backendName}-pwl-1"));
    }

    /// <summary>把一串数渲染成文本，顺带校验有限 / Render a number list, validating finiteness.</summary>
    /// <remarks>
    /// **渲染进指纹之前必须逐个检查有限：** **一个 `NaN` 或无穷会让"同一个模型"的指纹
    /// 取决于它被创建时的具体数值，而那是不可复现的——而指纹的全部意义就是可复现。**
    ///
    /// **Finiteness is checked before rendering**: **a `NaN` or infinity would make the fingerprint of "the same model"
    /// depend on a specific value at creation time, which is unreproducible — and reproducibility is the entire point of a
    /// fingerprint.**
    /// </remarks>
    public static Result<string, ErrorCode, Error<ErrorCode>> NumberListText(
        IReadOnlyList<Flt64> values,
        string fieldName) {
        ArgumentNullException.ThrowIfNull(values);

        var parts = new List<string>(values.Count);
        for (int index = 0; index < values.Count; ++index) {
            double converted;
            try {
                converted = values[index].ToSolverDouble();
            }
            catch (Exception error) {
                return new Failed<string, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument,
                    $"numeric conversion failed at {fieldName}[{index}]: {error.Message}");
            }

            if (!double.IsFinite(converted)) {
                return new Failed<string, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.IllegalArgument,
                    $"numeric value must be finite: {fieldName}[{index}]");
            }

            parts.Add(
                converted.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        return new Ok<string, ErrorCode, Error<ErrorCode>>(string.Join(",", parts));
    }

    private static Result<AuditFingerprint, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        new Failed<AuditFingerprint, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, message);
}
