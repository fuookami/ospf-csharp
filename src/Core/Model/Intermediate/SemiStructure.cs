#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>
/// 延迟函数 fallback 生成的求解器无关约束 / Solver-neutral constraints from a deferred-function fallback.
/// </summary>
/// <remarks>
/// 对齐基线 `DeferredFunctionFallback.kt` 的 `DeferredFunctionFallbackConstraints`。
///
/// **一个包装而不是直接返回列表**：**它标记"这些行的来源是 fallback 路径，不是结构的原生展开"。
/// 调用方据此决定要不要做等价列与保留列的登记——两者的处理不同。**
///
/// **A wrapper rather than a bare list**: **it marks "these rows came from the fallback path, not from the structure's
/// own expansion".** **The caller uses that to decide whether to register equivalents and retained columns — the two are
/// handled differently.**
/// </remarks>
/// <param name="Constraints">Flt64 约束列表 / The Flt64 constraint list.</param>
public sealed record DeferredFunctionFallbackConstraints(
    IReadOnlyList<LinearInequality<Flt64>> Constraints);

/// <summary>
/// 半连续变量的结构 / Semi-continuous variable structure.
/// </summary>
/// <remarks>
/// 对齐基线 `FunctionExpansion.kt` 的 `SemiStructure`。
///
/// **半连续的意思是"要么是 0，要么落在 `[L, U]` 里"。**
/// **两条行，而<b>它们只在成对时才强制 0</b>：**
///
/// | 行 | 内容 | 指示 = 0 时 | 指示 = 1 时 |
/// | --- | --- | --- | --- |
/// | `_semi_upper` | `result − U·ind ≤ 0` | `result ≤ 0` | `result ≤ U` |
/// | `_semi_lower` | `result − L·ind ≥ 0` | `result ≥ 0` | `result ≥ L` |
///
/// **指示 = 0 时，上界行只说"不超过 0"，下界行只说"不小于 0"。**
/// **单独任何一行都只是半边；<b>两行一起</b>才把 `result` 钉在 0 上。**
///
/// **这就是半连续的编码方式：不是"加一条 `result = 0` 的分支"，
/// 而是让两条行在指示 = 0 时<b>都收紧到 0</b>、在指示 = 1 时<b>都放宽到一个区间</b>。**
///
/// **没有校验 `L > 0`，而这不是遗漏**：**基线在 `materializeFallbackConstraints` 里也不校验。**
/// **但 `L ≤ 0` 会让"半连续"退化成"普通有界变量"——
/// 因为那时指示 = 1 的区间本身包含 0，"要么 0 要么 ≥ L"就没有区分力。**
/// **本实现不做基线没有的校验，但把这个后果写在这里。**
///
/// Following the baseline's `SemiStructure`.
///
/// **Semi-continuous means "either zero or inside `[L, U]`".**
/// **Two rows, and <b>they force zero only as a pair</b>:**
///
/// **At indicator 0 the upper row says only "≤ 0" and the lower only "≥ 0".**
/// **Either alone is half a constraint; <b>together</b> they pin `result` to zero.**
///
/// **That is the encoding: not "add a `result = 0` branch" but make both rows <b>tighten to zero</b> at indicator 0 and
/// <b>relax to an interval</b> at indicator 1.**
///
/// **`L > 0` is not validated, and that is not an omission**: **neither does the baseline's
/// `materializeFallbackConstraints`.** **But `L ≤ 0` degrades "semi-continuous" into "an ordinary bounded variable",
/// because the indicator-1 interval then contains zero and "either zero or ≥ L" loses its meaning.** **This
/// implementation adds no check the baseline lacks, and records the consequence here.**
/// </remarks>
public sealed record SemiStructure(
    Flt64 LowerBound,
    Flt64 UpperBound,
    IVariableItem ResultVariable,
    IVariableItem IndicatorVariable,
    IIntoValue<Flt64> Converter,
    string Name = "semi") : DeferredFunctionStructure
{
    /// <summary>使用语境 / Usage context.</summary>
    public FunctionUsageSummary Usage { get; init; } = new();

    /// <summary>fallback 路径需要保留的指示列 / The indicator column retained by the fallback path.</summary>
    public IReadOnlyList<IVariableItem> HelperVariables => new[] { IndicatorVariable };

    /// <summary>
    /// 物化 fallback 约束 / Materialize the fallback constraints.
    /// </summary>
    /// <remarks>
    /// **基线返回 `Ret&lt;DeferredFunctionFallbackConstraints&gt;` 并经过 `convertPwlConstraintsToFlt64`，**
    /// **那个转换器存在的理由是 `SemiStructure` 在基线里对 `V` 泛型，而约束列表要落到 `Flt64`。**
    ///
    /// **本实现是 `Flt64` 专用的（与 `IndicatorStructure`、`AbsStructure`、`MaxStructure` 一致），
    /// 所以行<b>本来就是</b> `Flt64`——转换是恒等，这里不做一次假转换。**
    /// **这个差别写在方法注释里，而不是悄悄省掉一个调用。**
    ///
    /// **The baseline returns `Ret&lt;DeferredFunctionFallbackConstraints&gt;` through `convertPwlConstraintsToFlt64`, and
    /// that converter exists because the baseline's `SemiStructure` is generic over `V` while the constraint list must
    /// land on `Flt64`.**
    ///
    /// **This implementation is `Flt64`-only (consistent with `IndicatorStructure`, `AbsStructure` and `MaxStructure`), so
    /// the rows <b>already are</b> `Flt64` — the conversion is the identity, and no pretend conversion is performed.**
    /// **The difference is stated here rather than a call being quietly dropped.**
    /// </remarks>
    public Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
        MaterializeFallbackConstraints() {
        try {
            Flt64 zero = Flt64.Zero;
            Flt64 one = Converter.IntoValue(Flt64.One);

            var rows = new List<LinearInequality<Flt64>>(2) {
                // **上界：`result − U·ind ≤ 0`。** **指示 = 0 时 `result ≤ 0`。**
                // **Upper: `result − U·ind ≤ 0`.** **`result ≤ 0` at indicator 0.**
                new(
                    Lhs: new LinearPolynomial<Flt64>(
                        new[] {
                            new LinearMonomial<Flt64>(one, ResultVariable),
                            new LinearMonomial<Flt64>(UpperBound.Negate(), IndicatorVariable),
                        },
                        zero),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), zero),
                    Comparison: Comparison.LE,
                    Name: $"{Name}_semi_upper"),

                // **下界：`result − L·ind ≥ 0`。** **指示 = 0 时 `result ≥ 0`。**
                // **Lower: `result − L·ind ≥ 0`.** **`result ≥ 0` at indicator 0.**
                new(
                    Lhs: new LinearPolynomial<Flt64>(
                        new[] {
                            new LinearMonomial<Flt64>(one, ResultVariable),
                            new LinearMonomial<Flt64>(LowerBound.Negate(), IndicatorVariable),
                        },
                        zero),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), zero),
                    Comparison: Comparison.GE,
                    Name: $"{Name}_semi_lower"),
            };

            foreach (LinearInequality<Flt64> row in rows) {
                if (!ConditionPolynomials.IsUsable(row.Lhs) || !ConditionPolynomials.IsUsable(row.Rhs)) {
                    return Fail("the semi-continuous fallback produced unusable values");
                }
            }

            return new Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                new DeferredFunctionFallbackConstraints(rows));
        }
        catch (Exception error) {
            return Fail($"failed to generate the semi-continuous fallback: {error.Message}");
        }
    }

    private static Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
        Fail(string message) =>
            new Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}
