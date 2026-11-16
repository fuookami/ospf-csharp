#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// &lt;summary>
/// 全种类结构的审计指纹 / Audit fingerprint across every structure kind.
/// &lt;/summary>
/// &lt;remarks>
/// 对齐的 `nativeFingerprint` 补，
/// **覆盖快路径与 PWL / ABS / MAX / 半连续 / 二值逻辑五类结构行**。
///
/// **缺口（明确记录，不静默丢弃）：** **这个函数还接受 `indicators` 与 `maskings` 两个列表，
/// 而它们对应的 `NativeIndicatorData` 与 `NativeMaskingData` 在 C# 侧<b>还不存在</b>。**
///
/// **那两个类型各自带着一整族嵌套语义**（**`zeroBand` 的侧列、`conjunction`、`difference`、
/// `impliedCondition`、`conditionalValue`、`equivalentResultKeys`，以及 `definition`**），
/// **不是"补两个 record"就够的。**
///
/// **所以这里<b>不接受</b>这两个参数，而不是接受之后忽略它们。**
/// **"接受但忽略"会让指纹看起来覆盖了指示器，而实际上没有——
/// 那是最坏的一种：一个安静的谎。**
///
/// **同理，schema 阶梯里 `-functions-indicator-2..8` 与 `-functions-masking-1/2` 七个分支
/// <b>这里不存在</b>：它们的前提（`indicators` / `maskings` 非空）在本签名下不可达。**
/// **七个分支是<b>缺的</b>，不是被合并成别的分支。**
///
/// **快路径：所有非 PWL 结构都为空时，直接交给 `piecewiseFingerprint`。**
/// **注意这条路径的 schema 是 `{backend}-pwl-1`，而慢路径最低是 `{backend}-abs-1` 或
/// `{backend}-pwl-abs-1`——所以同一份 PWL 数据走两条路会得到<b>不同</b>的 schema 版本。**
/// **这不是笔误：快路径说的是"只有 PWL"，慢路径说的是"PWL 加上别的东西"。**
///
/// **慢路径的换行是<b>无条件</b>的** **（`append('\n')` 之后直接 join），
/// 与 `piecewiseFingerprint` 里那个条件换行相反。**
/// **两条路径在同一件事上有不同写法，而它们各自自洽。**
///
/// **Covers the fast path and the PWL / ABS / MAX / semi-continuous / binary-logic structure lines.**
///
/// **Gap, recorded rather than silently dropped:** **the code also accepts `indicators` and `maskings`, whose
/// `NativeIndicatorData` and `NativeMaskingData` do <b>not</b> exist on the C# side yet.**
///
/// **Those two types each carry a whole family of nested semantics** (**the `zeroBand` side column, `conjunction`,
/// `difference`, `impliedCondition`, `conditionalValue`, `equivalentResultKeys`, and `definition`**) — far more than "two
/// records".**
///
/// **So those parameters are <b>not accepted</b> at all, rather than accepted and ignored.** **"Accepted but
/// ignored" would make the fingerprint look like it covers indicators while it does not — the worst kind: a quiet
/// lie.**
///
/// **Likewise the seven schema-ladder branches `-functions-indicator-2..8` and `-functions-masking-1/2` <b>do not
/// exist</b> here: their precondition (`indicators` / `maskings` non-empty) is unreachable under this signature.** **The
/// seven branches are <b>missing</b>, not merged into another.**
///
/// **Fast path: when every non-PWL structure is empty, delegate to `piecewiseFingerprint`.** **Note its schema is
/// `{backend}-pwl-1` while the slow path's lowest is `{backend}-abs-1` or `{backend}-pwl-abs-1` — so the same PWL data
/// gets a <b>different</b> schema version depending on the path.** **Not a typo: the fast path says "PWL only", the slow
/// path says "PWL plus something else".**
///
/// **The slow path's newline is <b>unconditional</b> (append then join), the opposite of `piecewiseFingerprint`'s
/// conditional one.** **The two paths write the same thing differently, and each is self-consistent.**
/// &lt;/remarks>
public static class NativeFingerprint
{
    /// &lt;summary>算全种类指纹 / Compute the cross-kind fingerprint.&lt;/summary>
    public static Result<AuditFingerprint, ErrorCode, Error<ErrorCode>> Compute(
        AuditFingerprint fingerprint,
        IReadOnlyDictionary<VariableItemKey, int> columns,
        IReadOnlyList<NormalizedPiecewise> piecewise,
        IReadOnlyList<NormalizedAbs> abs,
        IReadOnlyList<NativeMaxData> maxima,
        IReadOnlyList<SemiStructure> semis,
        IReadOnlyList<BinaryLogicStructure> binaryLogic,
        string backendName,
        IReadOnlyList<NativeIndicatorData>? indicators = null,
        IReadOnlyList<NativeMaskingData>? maskings = null) {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(piecewise);
        ArgumentNullException.ThrowIfNull(abs);
        ArgumentNullException.ThrowIfNull(maxima);
        ArgumentNullException.ThrowIfNull(semis);
        ArgumentNullException.ThrowIfNull(binaryLogic);
        ArgumentNullException.ThrowIfNull(backendName);

        // **指示器与遮盖是可选参数：当时它们还不存在，这里接进来但不破坏既有调用点。**
        // **空集合与 `null` 等价，两种写法都表示"没有这一类结构"。**
        //
        // **Indicators and maskings are optional: they did not exist, and them in without
        // breaking existing call sites.** **An empty collection and `null` are equivalent.**
        IReadOnlyList<NativeIndicatorData> indicatorList =
            indicators ?? Array.Empty<NativeIndicatorData>();
        IReadOnlyList<NativeMaskingData> maskingList =
            maskings ?? Array.Empty<NativeMaskingData>();

        // **快路径。**
        // **The fast path.**
        if (abs.Count == 0 && maxima.Count == 0 && semis.Count == 0 && binaryLogic.Count == 0 &&
            indicatorList.Count == 0 && maskingList.Count == 0) {
            return NativePiecewiseFingerprint.Compute(fingerprint, columns, piecewise, backendName);
        }

        var lines = new List<string>(piecewise.Count + abs.Count + maxima.Count
            + semis.Count + binaryLogic.Count + indicatorList.Count + maskingList.Count);

        foreach (NormalizedPiecewise structure in piecewise) {
            if (!columns.TryGetValue(structure.InputKey, out int inputColumn)) {
                return Fail("PWL input column is missing");
            }

            if (!columns.TryGetValue(structure.ResultKey, out int resultColumn)) {
                return Fail("PWL result column is missing");
            }

            Result<string, ErrorCode, Error<ErrorCode>> breakpoints =
                NativePiecewiseFingerprint.NumberListText(
                    structure.Breakpoints, "native.pwl.fingerprint.breakpoints");
            if (breakpoints is Failed<string, ErrorCode, Error<ErrorCode>> badBreakpoints) {
                return Fail(badBreakpoints.Error.Message);
            }

            Result<string, ErrorCode, Error<ErrorCode>> values =
                NativePiecewiseFingerprint.NumberListText(
                    structure.Values, "native.pwl.fingerprint.values");
            if (values is Failed<string, ErrorCode, Error<ErrorCode>> badValues) {
                return Fail(badValues.Error.Message);
            }

            // **慢路径的 PWL 行比快路径多一个 `kind=pwl|` 前缀——所以同一份数据在两条路径上
            // 连行内容都不同，不只是 schema 不同。**
            //
            // **The slow path's PWL line carries an extra `kind=pwl|` prefix, so the same data differs in line content too,
            // not only in schema.**
            lines.Add(string.Join("|",
                "kind=pwl",
                $"input-column={inputColumn}",
                $"result-column={resultColumn}",
                $"breakpoints={((Ok<string, ErrorCode, Error<ErrorCode>>)breakpoints).Value}",
                $"values={((Ok<string, ErrorCode, Error<ErrorCode>>)values).Value}"));
        }

        foreach (NormalizedAbs structure in abs) {
            if (!columns.TryGetValue(structure.InputKey, out int inputColumn)) {
                return Fail("ABS input column is missing");
            }

            if (!columns.TryGetValue(structure.ResultKey, out int resultColumn)) {
                return Fail("ABS result column is missing");
            }

            lines.Add(string.Join("|",
                "kind=abs",
                $"input-column={inputColumn}",
                $"result-column={resultColumn}"));
        }

        foreach (NativeMaxData structure in maxima) {
            if (!columns.TryGetValue(structure.ResultKey, out int resultColumn)) {
                return Fail("MAX result column is missing");
            }

            var inputs = new List<string>(structure.Inputs.Count);
            foreach (NativeMaxInputData input in structure.Inputs) {
                var terms = new List<KeyValuePair<int, double>>(input.Terms.Count);
                foreach (KeyValuePair<VariableItemKey, double> term in input.Terms) {
                    if (!columns.TryGetValue(term.Key, out int column)) {
                        return Fail("MAX input column is missing");
                    }

                    terms.Add(new KeyValuePair<int, double>(column, term.Value));
                }

                terms.Sort(static (left, right) => left.Key.CompareTo(right.Key));
                inputs.Add(
                    $"constant={NumberText(input.Constant)};terms="
                    + string.Join(",", terms.ConvertAll(
                        static pair => $"{pair.Key}:{NumberText(pair.Value)}")));
            }

            inputs.Sort(StringComparer.Ordinal);
            lines.Add(
                $"kind=max|result-column={resultColumn}|inputs={string.Join(";", inputs)}");
        }

        foreach (SemiStructure structure in semis) {
            if (!columns.TryGetValue(structure.ResultVariable.Key, out int resultColumn)) {
                return Fail("Semi-continuous result column is missing");
            }

            if (!columns.TryGetValue(structure.IndicatorVariable.Key, out int indicatorColumn)) {
                return Fail("Semi-continuous indicator column is missing");
            }

            double lower = structure.Converter.FromValue(structure.LowerBound).ToSolverDouble();
            double upper = structure.Converter.FromValue(structure.UpperBound).ToSolverDouble();

            // **`lower <= 0` 被拒，不是 `< 0`：半连续的下界必须为正，
            // 否则 0 与"未启用"分不开。**
            //
            // **`lower <= 0` is rejected, not `< 0`: a semi-continuous lower bound must be positive, otherwise zero is
            // indistinguishable from "not enabled".**
            if (!double.IsFinite(lower) || !double.IsFinite(upper) || lower <= 0.0 || lower > upper) {
                return Fail("invalid semi-continuous bounds");
            }

            lines.Add(
                $"kind=semi|result-column={resultColumn}|indicator-column={indicatorColumn}"
                + $"|lower={NumberText(lower)}|upper={NumberText(upper)}");
        }

        foreach (BinaryLogicStructure structure in binaryLogic) {
            if (!columns.TryGetValue(structure.ResultVariable.Key, out int resultColumn)) {
                return Fail("Binary logic result column is missing");
            }

            var inputColumns = new List<int>(structure.Inputs.Count);
            foreach (IVariableItem input in structure.Inputs) {
                if (!columns.TryGetValue(input.Key, out int column)) {
                    return Fail("Binary logic input column is missing");
                }

                inputColumns.Add(column);
            }

            inputColumns.Sort();
            lines.Add(
                $"kind=binary-logic|operation={structure.Operation}|result-column={resultColumn}"
                + $"|inputs={string.Join(",", inputColumns)}");
        }

        foreach (NativeIndicatorData structure in indicatorList) {
            if (!columns.TryGetValue(structure.ResultKey, out int resultColumn)) {
                return Fail("Indicator result column missing");
            }

            string? maybeTerms = TermsText(
                structure.Terms, columns, "Indicator input column missing");
            if (maybeTerms is null) {
                return Fail("Indicator input column missing");
            }

            string terms = maybeTerms;

            if (structure.Difference is not null) {
                if (!columns.TryGetValue(
                        structure.Difference.Condition.ResultKey, out int secondColumn) ||
                    !columns.TryGetValue(structure.Difference.ResultKey, out int outputColumn)) {
                    return Fail("Difference condition column missing");
                }

                lines.Add(
                    $"kind=exclusive-difference|first={resultColumn}"
                    + $"|second={secondColumn}|result={outputColumn}");
            }

            if (structure.Conjunction is not null) {
                if (!columns.TryGetValue(
                        structure.Conjunction.Condition.ResultKey, out int secondColumn) ||
                    !columns.TryGetValue(structure.Conjunction.ResultKey, out int outputColumn)) {
                    return Fail("Conjoined condition column missing");
                }

                lines.Add(
                    $"kind=conjunction|first={resultColumn}"
                    + $"|second={secondColumn}|result={outputColumn}");
            }

            if (structure.ZeroBand is null) {
                lines.Add(
                    $"kind=positive-indicator|result={resultColumn}"
                    + $"|constant={NumberText(structure.Constant)}"
                    + $"|gap={NumberText(structure.Tolerance)}"
                    + $"|positive-on-zero={BoolText(structure.PositiveOnZero)}|terms={terms}");
            }
            else {
                if (!columns.TryGetValue(structure.ZeroBand.SideKey, out int sideColumn)) {
                    return Fail("Zero-band side column missing");
                }

                lines.Add(
                    $"kind=zero-band|result={resultColumn}|side={sideColumn}"
                    + $"|constant={NumberText(structure.Constant)}"
                    + $"|inside={NumberText(structure.ZeroBand.Tolerance)}"
                    + $"|outside={NumberText(structure.Tolerance)}"
                    + $"|outside-on-zero={BoolText(structure.PositiveOnZero)}|terms={terms}");
            }

            if (structure.ImpliedCondition is not null) {
                if (!columns.TryGetValue(
                        structure.ImpliedCondition.ResultKey, out int consequentColumn)) {
                    return Fail("Implied indicator column missing");
                }

                string? maybeConsequent = TermsText(
                    structure.ImpliedCondition.Terms, columns, "Implied input column missing");
                if (maybeConsequent is null) {
                    return Fail("Implied input column missing");
                }

                string consequentTerms = maybeConsequent;

                lines.Add(
                    $"kind=implied-condition|antecedent={resultColumn}"
                    + $"|consequent={consequentColumn}"
                    + $"|constant={NumberText(structure.ImpliedCondition.Constant)}"
                    + $"|gap={NumberText(structure.Tolerance)}|terms={consequentTerms}");
            }

            if (structure.ConditionalValue is not null) {
                if (!columns.TryGetValue(
                        structure.ConditionalValue.ResultKey, out int outputColumn)) {
                    return Fail("Conditional value result column missing");
                }

                string? maybeValue = TermsText(
                    structure.ConditionalValue.Terms, columns,
                    "Conditional value input column missing");
                if (maybeValue is null) {
                    return Fail("Conditional value input column missing");
                }

                string valueTerms = maybeValue;

                lines.Add(
                    $"kind=conditional-value|indicator={resultColumn}|result={outputColumn}"
                    + $"|constant={NumberText(structure.ConditionalValue.Constant)}"
                    + $"|terms={valueTerms}");
            }

            if (structure.EquivalentResultKeys is not null) {
                foreach (VariableItemKey key in structure.EquivalentResultKeys) {
                    if (!columns.TryGetValue(key, out int equivalentColumn)) {
                        return Fail("Indicator equivalent result column missing");
                    }

                    lines.Add(
                        $"kind=indicator-equivalence|result={resultColumn}"
                        + $"|equivalent={equivalentColumn}");
                }
            }
        }

        foreach (NativeMaskingData structure in maskingList) {
            if (!columns.TryGetValue(structure.MaskKey, out int maskColumn)) {
                return Fail("Mask column missing");
            }

            if (!columns.TryGetValue(structure.Value.ResultKey, out int resultColumn)) {
                return Fail("Masking result column missing");
            }

            string? maybeMaskTerms = TermsText(
                structure.Value.Terms, columns, "Masking input column missing");
            if (maybeMaskTerms is null) {
                return Fail("Masking input column missing");
            }

            string terms = maybeMaskTerms;

            lines.Add(
                $"kind=masking|mask={maskColumn}|result={resultColumn}"
                + $"|constant={NumberText(structure.Value.Constant)}|terms={terms}");

            if (structure.Definition is not null) {
                string? maybeDefinition = TermsText(
                    structure.Definition.Terms, columns, "Mask definition column missing");
                if (maybeDefinition is null) {
                    return Fail("Mask definition column missing");
                }

                string definitionTerms = maybeDefinition;

                lines.Add(
                    $"kind=mask-definition|mask={maskColumn}"
                    + $"|constant={NumberText(structure.Definition.Constant)}"
                    + $"|terms={definitionTerms}");
            }
        }

        string schema = Schema(
            piecewise, maxima, semis, binaryLogic, backendName, indicatorList, maskingList);

        var content = new System.Text.StringBuilder(fingerprint.Value);
        content.Append('\n');
        content.Append(string.Join("\n", Sorted(lines)));

        return new Ok<AuditFingerprint, ErrorCode, Error<ErrorCode>>(
            SolveFingerprinting.Sha256(content.ToString(), schema));
    }

    /// &lt;summary>阶梯选 schema 版本 / Pick the schema version following the code ladder.&lt;/summary>
    /// &lt;remarks>
    /// **阶梯有十三个分支，这里只有<b>五个</b>可达：**
    /// **`binary-logic-1`、`semi-1`、`max-1`、`abs-1`、`pwl-abs-1`。**
    ///
    /// **另外八个（`masking-1`、`masking-2`、`indicator-2..8`）的前提是指示器或遮盖非空，
    /// 而这两个列表在本签名下不存在——所以那八个分支是<b>缺的</b>，不是被并进别的分支。**
    ///
    /// **阶梯的顺序本身就是语义：** **遮盖压过二值逻辑，二值逻辑压过半连续，半连续压过 MAX，
    /// MAX 压过"只有 ABS"或"PWL+ABS"。**
    ///
    /// **所以要测的不是"某一个分支对不对"，而是<b>相邻两个同时非空时谁赢</b>。**
    ///
    /// **The code ladder has thirteen branches; <b>five</b> are reachable here:**
    /// **`binary-logic-1`, `semi-1`, `max-1`, `abs-1`, `pwl-abs-1`.**
    ///
    /// **The other eight (`masking-1/2`, `indicator-2..8`) presuppose indicators or maskings, absent from this signature —
    /// so those eight are <b>missing</b>, not folded into another branch.**
    ///
    /// **The order is the semantics: masking over binary logic, binary logic over semi-continuous, semi-continuous over
    /// MAX, MAX over "ABS only" or "PWL + ABS".**
    ///
    /// **So what needs testing is not "is one branch right" but <b>who wins when two neighbours are both
    /// non-empty</b>.**
    /// &lt;/remarks>
    public static string Schema(
        IReadOnlyList<NormalizedPiecewise> piecewise,
        IReadOnlyList<NativeMaxData> maxima,
        IReadOnlyList<SemiStructure> semis,
        IReadOnlyList<BinaryLogicStructure> binaryLogic,
        string backendName,
        IReadOnlyList<NativeIndicatorData>? indicators = null,
        IReadOnlyList<NativeMaskingData>? maskings = null) {
        IReadOnlyList<NativeIndicatorData> indicatorList =
            indicators ?? Array.Empty<NativeIndicatorData>();
        IReadOnlyList<NativeMaskingData> maskingList =
            maskings ?? Array.Empty<NativeMaskingData>();

        if (maskingList.Count > 0 && HasDefinition(maskingList)) {
            return $"{backendName}-functions-masking-2";
        }

        if (maskingList.Count > 0) {
            return $"{backendName}-functions-masking-1";
        }

        if (binaryLogic.Count > 0) {
            return $"{backendName}-functions-binary-logic-1";
        }

        if (semis.Count > 0) {
            return $"{backendName}-functions-semi-1";
        }

        if (maxima.Count > 0 && indicatorList.Count == 0) {
            return $"{backendName}-functions-max-1";
        }

        // **指示器的阶梯<b>在 MAX 之上</b>：顺序是 masking → binary-logic → semi → max →
        // indicator-8..2 → max-1 → abs-1 / pwl-abs-1。**
        // **而 `max` 那档在位于 indicator 之后，所以两者同时非空时应由 indicator 胜出。**
        //
        // **The indicator ladder sits <b>above</b> MAX: the code order is masking, binary-logic, semi, max,
        // indicator-8..2, max-1, abs-1/pwl-abs-1.** **`max` comes after the indicator rungs, so when both are non-empty
        // the indicator wins.**
        if (indicatorList.Count > 0) {
            if (Any(indicatorList, static s => s.Difference is not null)) {
                return $"{backendName}-functions-indicator-8";
            }

            if (Any(indicatorList, static s => s.Conjunction is not null)) {
                return $"{backendName}-functions-indicator-7";
            }

            if (Any(indicatorList, static s => s.ZeroBand is not null)) {
                return $"{backendName}-functions-indicator-6";
            }

            if (Any(indicatorList, static s => s.ImpliedCondition is not null)) {
                return $"{backendName}-functions-indicator-5";
            }

            if (Any(indicatorList, static s => s.ConditionalValue is not null)) {
                return $"{backendName}-functions-indicator-4";
            }

            if (Any(indicatorList, static s =>
                    s.EquivalentResultKeys is not null && s.EquivalentResultKeys.Count > 0)) {
                return $"{backendName}-functions-indicator-3";
            }

            return $"{backendName}-functions-indicator-2";
        }

        if (maxima.Count > 0) {
            return $"{backendName}-functions-max-1";
        }

        return piecewise.Count == 0
            ? $"{backendName}-abs-1"
            : $"{backendName}-pwl-abs-1";
    }

    private static bool HasDefinition(IReadOnlyList<NativeMaskingData> maskings) {
        foreach (NativeMaskingData structure in maskings) {
            if (structure.Definition is not null) {
                return true;
            }
        }

        return false;
    }

    private static bool Any(
        IReadOnlyList<NativeIndicatorData> indicators,
        Func<NativeIndicatorData, bool> predicate) {
        foreach (NativeIndicatorData structure in indicators) {
            if (predicate(structure)) {
                return true;
            }
        }

        return false;
    }

    /// &lt;summary>按键的列号排序后渲染仿射项 / Render affine terms ordered by column.&lt;/summary>
    /// &lt;remarks>
    /// **按<b>列号</b>排序而不是按键排序：** **指纹描述的是"这个模型"，而列号是模型内部的编号。
    /// 按键排序会让同一个模型在不同列的排布下给出不同指纹。**
    ///
    /// **`sortedBy { it.first }` 排的是 `Pair&lt;Int, Double>` 的第一项，也就是列号。**
    ///
    /// **Ordered by <b>column</b>, not by key: the fingerprint describes "this model", and the column index is that model's
    /// internal numbering.** **Sorting by key would make the same model fingerprint differently under a different column
    /// arrangement.**
    ///
    /// **The code's `sortedBy { it.first }` sorts on the first element of `Pair&lt;Int, Double&gt;`, the column.**
    /// &lt;/remarks>
    public static string? TermsText(
        IReadOnlyDictionary<VariableItemKey, double>? terms,
        IReadOnlyDictionary<VariableItemKey, int> columns,
        string missingMessage) {
        if (terms is null) {
            return string.Empty;
        }

        var ordered = new List<KeyValuePair<int, double>>(terms.Count);
        foreach (KeyValuePair<VariableItemKey, double> term in terms) {
            if (!columns.TryGetValue(term.Key, out int column)) {
                _ = missingMessage;
                return null;
            }

            ordered.Add(new KeyValuePair<int, double>(column, term.Value));
        }

        ordered.Sort(static (left, right) => left.Key.CompareTo(right.Key));

        var parts = new List<string>(ordered.Count);
        foreach (KeyValuePair<int, double> term in ordered) {
            parts.Add($"{term.Key}:{NumberText(term.Value)}");
        }

        return string.Join(",", parts);
    }

    /// &lt;summary>渲染一个布尔值 / Render a boolean.&lt;/summary>
    /// &lt;remarks>
    /// **字符串插值给出 Kotlin 的 `true` / `false`，与 .NET 的 `True` / `False` <b>大小写不同</b>。**
    /// **这里用<b>小写</b>以贴合文本形状——但因为指纹文本本就不保证跨语言一致
 ///，这一处选择只影响本侧的自洽性。**
    ///
    /// **The code's interpolation yields Kotlin's `true`/`false`, differing in <b>case</b> from .NET's
    /// `True`/`False`.** **Lowercase is used here to match its text shape — but since the fingerprint text is
    /// not guaranteed to match across languages (recorded), this choice only affects self-consistency.**
    /// &lt;/remarks>
    public static string BoolText(bool value) => value ? "true" : "false";

    /// &lt;summary>渲染一个数 / Render a number.&lt;/summary>
    public static string NumberText(double value) =>
        value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static List<string> Sorted(List<string> lines) {
        var copy = new List<string>(lines);
        copy.Sort(StringComparer.Ordinal);
        return copy;
    }

    private static Result<AuditFingerprint, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        new Failed<AuditFingerprint, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, message);
}
