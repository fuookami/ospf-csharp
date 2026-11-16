#nullable enable

using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Symbol.Inequality;
using System.Linq;
using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>
/// ABS 辅助变量在后续模型限制之前捕获的范围 / An ABS helper's range captured before later model restrictions.
/// </summary>
/// <remarks>
/// 对齐基线 `FunctionExpansion.kt` 的 `AbsHelperBoundsSnapshot`。
///
/// **两个字段都可空，而"可空"是这里的全部信息量**：
/// **`null` 表示"当时没有已知界"，不是"界是零"。**
/// **把它写成非空的 `Flt64` 会让"没有界"变成"界为零"，
/// 而后者会让 fallback 证明认为辅助变量只可能在 0 上——那是错的，且不会报错。**
///
/// **Both fields are nullable, and the nullability is the whole information content**:
/// **`null` means "no known bound at the time", not "the bound is zero".**
/// **Making them non-nullable turns "no bound" into "bound zero", and the fallback proof then believes a helper can only
/// be zero — wrong, and silent.**
/// </remarks>
/// <param name="Lower">辅助变量下界 / Helper lower bound.</param>
/// <param name="Upper">辅助变量上界 / Helper upper bound.</param>
public sealed record AbsHelperBoundsSnapshot(Flt64? Lower, Flt64? Upper);

/// <summary>
/// ABS 函数的结构与 fallback 证明快照 / Absolute-value structure and fallback proof snapshot.
/// </summary>
/// <remarks>
/// 对齐基线 `FunctionExpansion.kt` 的 `AbsStructure`。
///
/// **`|x|` 需要三个辅助列，而不是一个**：
///
/// | 列 | 作用 |
/// | --- | --- |
/// | `PositiveVariable` | **正部**（`max(x, 0)`） |
/// | `NegativeVariable` | **负部**（`max(−x, 0)`） |
/// | `SignVariable` | **正负部选择** |
///
/// **"与结果变量相等的那个列"是 `PositiveVariable + NegativeVariable`，
/// 而 `SignVariable` 决定当前走哪一支。**
/// **只有正部与负部两列也能表达结果，但无法表达"这个点落在哪一支"——
/// 而那正是后续需要按分支处理的地方。**
///
/// **`InputVariableKeyOrNull` 要求输入是一个<b>裸变量</b>**：
/// **恰好一个 monomial、系数为 1、常数为 0。**
/// **三条中少任何一条，返回 `null` 而不是"尽力而为"。**
///
/// **为什么必须这么严**：**这个方法的结果被用来"按输入变量索引 helper"。
/// 如果系数是 2，那个 helper 属于 `2x` 而不是 `x`，
/// 而两个不同的结构会被索引到同一个键上——后写入的那个悄悄覆盖前一个。**
///
/// Following the baseline's `AbsStructure`.
///
/// **`|x|` needs three helper columns, not one:**
///
/// | Column | Role |
/// | --- | --- |
/// | `PositiveVariable` | **positive part** (`max(x, 0)`) |
/// | `NegativeVariable` | **negative part** (`max(−x, 0)`) |
/// | `SignVariable` | **which part is active** |
///
/// **The result equals `PositiveVariable + NegativeVariable` while `SignVariable` says which branch is live.**
/// **Two columns could express the value but not which branch a point falls on — and that is exactly what later
/// branch-dependent handling needs.**
///
/// **`InputVariableKeyOrNull` demands a <b>bare variable</b>: exactly one monomial, coefficient one, constant zero.**
/// **Missing any of the three yields `null` rather than a best effort.**
///
/// **Why so strict**: **the result indexes helpers <b>by input variable</b>. With coefficient two the helper belongs to
/// `2x` rather than `x`, two different structures get the same key, and the later write silently overwrites the
/// earlier one.**
/// </remarks>
public sealed record AbsStructure(
    LinearPolynomial<Flt64> Input,
    IVariableItem ResultVariable,
    IVariableItem PositiveVariable,
    IVariableItem NegativeVariable,
    IVariableItem SignVariable,
    Flt64 PositiveBigM,
    Flt64 NegativeBigM,
    IIntoValue<Flt64> Converter,
    string Name = "abs") : DeferredFunctionStructure
{
    /// <summary>使用语境 / Usage context.</summary>
    public FunctionUsageSummary Usage { get; init; } = new();

    /// <summary>创建快照时捕获的辅助变量范围 / Helper bounds captured when the snapshot was created.</summary>
    public IReadOnlyDictionary<VariableItemKey, AbsHelperBoundsSnapshot> CapturedHelperBounds {
        get;
        init;
    } = new Dictionary<VariableItemKey, AbsHelperBoundsSnapshot>();

    /// <summary>
    /// fallback 路径需要保留的辅助变量 / Helper variables retained by the fallback path.
    /// </summary>
    /// <remarks>
    /// **顺序固定为"正部、负部、选择"，且判据按位置核对。**
    /// **这三列都不包含 `ResultVariable`——那是"保留结果列"，与"辅助列"是两份清单，
    /// 混在一起会让调用方重复登记。**
    ///
    /// **The order is fixed as positive, negative, selector, and a criterion checks by position.**
    /// **None of the three is `ResultVariable` — that belongs to the retained-result list, a separate list; mixing them
    /// makes callers register a column twice.**
    /// </remarks>
    public IReadOnlyList<IVariableItem> HelperVariables =>
        new[] { PositiveVariable, NegativeVariable, SignVariable };

    /// <summary>
    /// 输入的裸变量键；输入不是裸变量时 <c>null</c> /
    /// The bare-variable key of the input, or <c>null</c> when the input is not one.
    /// </summary>
    public VariableItemKey? InputVariableKeyOrNull() {
        try {
            if (Input.Monomials.Count != 1) {
                return null;
            }

            if (Converter.FromValue(Input.Monomials[0].Coefficient).CompareTo(Flt64.One) != 0 ||
                Converter.FromValue(Input.Constant).CompareTo(Flt64.Zero) != 0) {
                return null;
            }

            return Input.Monomials[0].Symbol is IVariableItem variable ? variable.Key : null;
        }
        catch (Exception) {
            // **转换器可能抛（比如值超出可表示范围）。**
            // **这里捕获它并返回 `null`，而不是让"取不到键"变成整个建模过程失败——
            // 因为取键失败<b>本来就是一个可预期的结果</b>（输入不是裸变量）。**
            //
            // **The converter may throw (a value out of representable range).**
            // **Catching it and returning `null` keeps "no key" from failing the whole build, because failing to get a
            // key <b>is itself an expected outcome</b> (the input is not a bare variable).**
            return null;
        }
    }

    /// <summary>
    /// 两个 Big-M 的 solver 值；转换失败时 <c>null</c> /
    /// Both Big-M values in solver form, or <c>null</c> when conversion fails.
    /// </summary>
    public (Flt64 Positive, Flt64 Negative)? SolverBigMOrNull() {
        try {
            return (Converter.FromValue(PositiveBigM), Converter.FromValue(NegativeBigM));
        }
        catch (Exception) {
            return null;
        }
    }

    /// <summary>
    /// 生成 ABS 约束行 / Generate the ABS rows.
    /// </summary>
    /// <remarks>
    /// **四行，两两一组：**
    ///
    /// | 行 | 内容 | 作用 |
    /// | --- | --- | --- |
    /// | `_abs_result` | `result − pos − neg = 0` | **结果等于两侧之和** |
    /// | `_abs_decompose` | `input − pos + neg = 0` | **输入按符号拆成两侧之差** |
    /// | `_abs_pos_ub` | `pos − posM·sign ≤ 0` | **`sign = 0` 时 `pos ≤ 0`** |
    /// | `_abs_neg_ub` | `neg + negM·sign ≤ negM` | **`sign = 1` 时 `neg ≤ 0`** |
    ///
    /// **两条上界是互补的：`sign = 1` 时 `pos` 只受 `posM` 约束、`neg` 被压到 `≤ 0` 且无下界；
    /// `sign = 0` 时反过来。** **于是"哪一侧可以非零"由 `sign` 决定，
    /// 而"两侧都非负"是<b>调用方的声明带来的前提</b>，不是这四行给的。**
    ///
    /// **这个前提必须写下来**：**若 `neg` 可以取负，`sign = 1` 时
    /// `input = pos − neg` 可以大于 `pos`，而 `result = pos + neg` 同时变小——
    /// 于是 `result ≠ |input|`，而四行全部满足。**
    /// **`CapturedHelperBounds` 记录辅助变量的范围，正是为了这件事；
    /// 本方法<b>不</b>替调用方检查它，因为那一层拿不到变量的域。**
    ///
    /// **两个 Big-M 只要求非负，不要求严格正**：**`posM = 0` 意味着"输入不可能为正"，
    /// 那是对输入的一个声明，不是一个错误。** **这与 `PiecewiseBigM` 要求显式 M 为正不同——
    /// 那里的 M 是一个松弛量，为零就没有松弛；这里的 M 是一个域的上界，为零是合法的。**
    ///
    /// **Four rows, in two pairs:**
    ///
    /// | Row | Content | Role |
    /// | --- | --- | --- |
    /// | `_abs_result` | `result − pos − neg = 0` | **the result is the sum of both sides** |
    /// | `_abs_decompose` | `input − pos + neg = 0` | **the input splits into their difference** |
    /// | `_abs_pos_ub` | `pos − posM·sign ≤ 0` | **`pos ≤ 0` when `sign = 0`** |
    /// | `_abs_neg_ub` | `neg + negM·sign ≤ negM` | **`neg ≤ 0` when `sign = 1`** |
    ///
    /// **The two upper bounds are complementary: at `sign = 1` `pos` is capped by `posM` while `neg` is only pushed to
    /// `≤ 0` with no lower bound, and at `sign = 0` the reverse.** **Which side may be nonzero is decided by `sign`,
    /// while "both sides are nonnegative" is <b>a premise the caller's declaration supplies</b>, not something these four
    /// rows give.**
    ///
    /// **That premise must be written down**: **if `neg` may go negative, at `sign = 1` `input = pos − neg` can exceed
    /// `pos` while `result = pos + neg` shrinks — so `result ≠ |input|` and all four rows still hold.**
    /// **`CapturedHelperBounds` records the helper ranges precisely for this; this method does <b>not</b> check it for the
    /// caller, because that layer cannot see the variables' domains.**
    ///
    /// **Both Big-Ms need only be nonnegative, not strictly positive**: **`posM = 0` says "the input cannot be positive",
    /// a statement about the input rather than an error.** **Different from `PiecewiseBigM`, which demands a positive
    /// explicit M — there the M is slack, and zero means no slack; here it is a domain cap, and zero is legal.**
    /// </remarks>
    public Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        GenerateConstraints() {
        try {
            // **四个键必须互不相同；否则同一个列会同时扮演两个角色，
            // 而方程看起来仍然成立。**
            //
            // **The four keys must be distinct; otherwise one column plays two roles while the equations still look
            // fine.**
            VariableItemKey[] keys = {
                ResultVariable.Key, PositiveVariable.Key,
                NegativeVariable.Key, SignVariable.Key,
            };

            if (new HashSet<VariableItemKey>(keys).Count != keys.Length) {
                return Fail("ABS result and helper variables must have distinct keys");
            }

            if (!BigMValues.IsUsable(Input.Constant)) {
                return Fail("ABS input or Big-M contains a non-finite or unrepresentable value");
            }

            foreach (LinearMonomial<Flt64> monomial in Input.Monomials) {
                if (!BigMValues.IsUsable(monomial.Coefficient)) {
                    return Fail("ABS input or Big-M contains a non-finite or unrepresentable value");
                }
            }

            if (!BigMValues.IsUsable(PositiveBigM) || !BigMValues.IsUsable(NegativeBigM)) {
                return Fail("ABS input or Big-M contains a non-finite or unrepresentable value");
            }

            if (PositiveBigM.CompareTo(Flt64.Zero) < 0 || NegativeBigM.CompareTo(Flt64.Zero) < 0) {
                return Fail("ABS Big-M values must be nonnegative");
            }

            Flt64 zero = Flt64.Zero;
            Flt64 one = Converter.IntoValue(Flt64.One);
            LinearMonomial<Flt64>[] empty = Array.Empty<LinearMonomial<Flt64>>();
            var rows = new List<LinearInequality<Flt64>>(4) {
                new(
                    Lhs: new LinearPolynomial<Flt64>(new[] {
                        new LinearMonomial<Flt64>(one, ResultVariable),
                        new LinearMonomial<Flt64>(one.Negate(), PositiveVariable),
                        new LinearMonomial<Flt64>(one.Negate(), NegativeVariable),
                    }, zero),
                    Rhs: new LinearPolynomial<Flt64>(empty, zero),
                    Comparison: Comparison.EQ,
                    Name: $"{Name}_abs_result"),
            };

            var decompose = new List<LinearMonomial<Flt64>>(Input.Monomials.Count + 2);
            foreach (LinearMonomial<Flt64> monomial in Input.Monomials) {
                decompose.Add(new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
            }

            decompose.Add(new LinearMonomial<Flt64>(one.Negate(), PositiveVariable));
            decompose.Add(new LinearMonomial<Flt64>(one, NegativeVariable));

            rows.Add(new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(decompose, Input.Constant),
                Rhs: new LinearPolynomial<Flt64>(empty, zero),
                Comparison: Comparison.EQ,
                Name: $"{Name}_abs_decompose"));

            rows.Add(new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(new[] {
                    new LinearMonomial<Flt64>(one, PositiveVariable),
                    new LinearMonomial<Flt64>(PositiveBigM.Negate(), SignVariable),
                }, zero),
                Rhs: new LinearPolynomial<Flt64>(empty, zero),
                Comparison: Comparison.LE,
                Name: $"{Name}_abs_pos_ub"));

            rows.Add(new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(new[] {
                    new LinearMonomial<Flt64>(one, NegativeVariable),
                    new LinearMonomial<Flt64>(NegativeBigM, SignVariable),
                }, zero),
                Rhs: new LinearPolynomial<Flt64>(empty, NegativeBigM),
                Comparison: Comparison.LE,
                Name: $"{Name}_abs_neg_ub"));

            return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
        }
        catch (Exception error) {
            return Fail($"failed to generate ABS constraints: {error.Message}");
        }
    }

    /// <summary>物化 fallback 约束 / Materialize the fallback constraints.</summary>
    public Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
        MaterializeFallbackConstraints() {
        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> rows =
            GenerateConstraints();

        if (rows is Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> failed) {
            return new Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                failed.Error.Code, failed.Error.Message);
        }

        return new Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
            new DeferredFunctionFallbackConstraints(
                ((Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)rows)
                .Value));
    }

    private static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        Fail(string message) =>
            new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}
