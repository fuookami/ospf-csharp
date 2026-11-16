#nullable enable

using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Inequality;
using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>
/// 结构节点拥有的连续约束区域 / A contiguous constraint region owned by a structure node.
/// </summary>
/// <remarks>
/// 对齐基线 `FunctionExpansion.kt` 的 `DeferredFunctionConstraintRegion`。
///
/// **`LastConstraintIndex` 是<b>排他</b>索引，而这一点决定了所有用它做的算术。**
///
/// **对一个 `[2, 5)` 的区域：**
///
/// | 量 | 值 |
/// | --- | --- |
/// | `FirstConstraintIndex` | `2` |
/// | `ConstraintCount` | `3` |
/// | `LastConstraintIndex` | `5`（**排他**） |
/// | 最后一行的索引 | `LastConstraintIndex − 1 = 4` |
///
/// **写成 `First + Count − 1` 就成了"结尾的闭区间"，而它的名字里没有 `inclusive`。**
/// **两者差 1，而差 1 在"往下标里塞行、再按下标切回来"的代码里不会报错，只会错一位。**
///
/// **判据钉住"`Last − First == Count`"，而不是钉住某个具体数值——
/// 后者在改变 `First` 时会一起变，于是不变量看起来仍然成立。**
///
/// Following the baseline's `DeferredFunctionConstraintRegion`.
///
/// **`LastConstraintIndex` is an <b>exclusive</b> index, and that decides every arithmetic on it.**
///
/// For a `[2, 5)` region the first index is `2`, the count is `3` and the last index is `5` (**exclusive**); the final
/// row sits at `LastConstraintIndex − 1 = 4`.
///
/// **`First + Count − 1` would make it an inclusive end, and the name does not say `inclusive`.**
/// **The two differ by one, and off-by-one in "insert rows by index, then slice back by index" does not throw — it
/// shifts by one.**
///
/// **The criterion pins `Last − First == Count` rather than a concrete number**: **a concrete number moves with
/// `First`, so the invariant would appear to hold while the formula was wrong.**
/// </remarks>
/// <param name="Structure">所属节点 / Owning structure.</param>
/// <param name="FirstConstraintIndex">本模型层的起始行号 / First row index in this model layer.</param>
/// <param name="ConstraintCount">区域行数 / Number of rows.</param>
public sealed record DeferredFunctionConstraintRegion(
    DeferredFunctionStructure Structure,
    int FirstConstraintIndex,
    int ConstraintCount)
{
    /// <summary>区域末端的排他索引 / The region's exclusive end index.</summary>
    public int LastConstraintIndex => FirstConstraintIndex + ConstraintCount;
}

/// <summary>
/// 为延迟自动 Big-M 快照捕获有限输入范围 / Capture a finite input range for a deferred automatic Big-M snapshot.
/// </summary>
/// <remarks>
/// 对齐基线 `DeferredFunctionFallback.kt` 的 `capturePwlInputBounds`。
///
/// **三层校验，顺序不能换：**
///
/// 1. **范围<b>存在</b>**（`FiniteBounds` 返回 `null` 表示推导不出有限范围）
/// 2. **范围<b>可用</b>**（两端都必须是有限且可编码的 solver 值）
/// 3. **异常也要归成同一条失败**（推导过程中可能抛）
///
/// **第 1 步与第 2 步的差别不是形式主义**：**`FiniteBounds` 可能返回一个由
/// 哨兵值（如 `1e30`）构成的"有限"范围——它在 `double` 意义下有限，
/// 但不是真正可用的界。** **只做第 1 步会把这个哨兵当成证明。**
///
/// **Three checks, and the order matters:**
///
/// 1. **the range <b>exists</b>** (`FiniteBounds` returns `null` when no finite range can be derived)
/// 2. **the range is <b>usable</b>** (both ends finite and encodable)
/// 3. **exceptions land on the same failure** (the derivation may throw)
///
/// **Steps 1 and 2 are not formalism**: **`FiniteBounds` may return a "finite" range built from sentinels such as
/// `1e30` — finite as a `double`, but not a usable bound.** **Step 1 alone would treat that sentinel as a proof.**
/// </remarks>
public static class PiecewiseInputBounds
{
    /// <summary>捕获输入范围 / Capture the input range.</summary>
    public static Result<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> Capture(
        LinearPolynomial<Flt64> input,
        IIntoValue<Flt64> converter) {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(converter);

        try {
            LinearPolynomialBounds<Flt64>? bounds = BigMValues.FiniteBounds(input, converter);
            if (bounds is null) {
                return Fail(
                    "no provable finite input range was available when creating the piecewise snapshot");
            }

            if (!IsUsable(bounds)) {
                return Fail(
                    "the input range was non-finite or a solver sentinel when creating the piecewise snapshot");
            }

            return new Ok<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>(bounds);
        }
        catch (Exception error) {
            return Fail($"failed to capture piecewise input bounds: {error.Message}");
        }
    }

    /// <summary>两端都必须是可用值 / Both ends must be usable values.</summary>
    public static bool IsUsable(LinearPolynomialBounds<Flt64> bounds) =>
        BigMValues.IsUsable(bounds.LowerBound) && BigMValues.IsUsable(bounds.UpperBound);

    private static Result<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>
        Fail(string message) =>
            new Failed<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}

/// <summary>
/// 分段线性函数的结构与 fallback 证明快照 / Piecewise structure and fallback proof snapshot.
/// </summary>
/// <remarks>
/// 对齐基线 `FunctionExpansion.kt` 的 `UnivariateLinearPiecewiseStructure` 补其
/// `ValidateFallbackInputBounds` 部分；**行生成与选择列约束留给后续批次。**
///
/// **这个校验的<b>第一件事</b>是看 `ExplicitM`：**
///
/// **显式给了 Big-M 时，整套范围证明都不需要——直接通过。**
///
/// **理由：** **自动 M 是从输入范围推出来的，所以它必须证明"范围没有被后续约束放宽"。
/// 而显式 M 是调用方给的数，它与输入范围无关，也就不需要这份证明。**
///
/// **"不需要"不等于"跳过检查"**：**它意味着后面三步的前提都不成立，
/// 所以那个分岔必须在最前面。** **把它放到后面，一个带显式 M 而缺转换器的快照会被判为非法——
/// 而它其实是合法的。**
///
/// Adds **row generation and selector constraints are left to later batches.**
///
/// **The first thing the check does is look at `ExplicitM`:**
///
/// **With an explicit Big-M the whole bounds proof is unnecessary — it passes immediately.**
///
/// **Because the automatic M is derived from the input range and must prove that later constraints did not widen it,
/// while an explicit M is a number the caller supplied, independent of the input range, and needs no such proof.**
///
/// **"Not needed" is not "skipped"**: **it means the premises of the next three steps do not hold, so the fork has to
/// come first.** **Put it later and a snapshot with an explicit M but no converter is rejected — although it is
/// legal.**
/// </remarks>
public sealed record UnivariateLinearPiecewiseStructure(
    LinearPolynomial<Flt64> Input,
    IReadOnlyList<Flt64> Breakpoints,
    IReadOnlyList<Flt64> Slopes,
    IReadOnlyList<Flt64> Intercepts,
    IVariableItem ResultVariable,
    IReadOnlyList<IVariableItem> SelectorVariables) : DeferredFunctionStructure
{
    /// <summary>使用语境 / Usage context.</summary>
    public FunctionUsageSummary Usage { get; init; } = new();

    /// <summary>可选的显式 Big-M / Optional explicit Big-M.</summary>
    public Flt64? ExplicitM { get; init; }

    /// <summary>求解器边界值转换；可空，因为显式 M 时不需要 / Value conversion; nullable because an explicit M does not need it.</summary>
    public IIntoValue<Flt64>? Converter { get; init; }

    /// <summary>函数名称 / Function name.</summary>
    public string Name { get; init; } = "piecewise";

    /// <summary>创建快照时捕获的输入范围证明 / The input-range proof captured when the snapshot was created.</summary>
    public Result<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>? CapturedInputBounds {
        get;
        init;
    }

    /// <summary>fallback 路径需要保留的选择变量 / Selector variables retained by the fallback path.</summary>
    public IReadOnlyList<IVariableItem> HelperVariables => SelectorVariables;

    /// <summary>校验捕获的输入范围证明是否仍然成立 / Validate that the captured range proof still holds.</summary>
    public Result<Success, ErrorCode, Error<ErrorCode>> ValidateFallbackInputBounds() {
        // **显式 M 不需要范围证明——这个分岔必须最先。**
        // **An explicit M needs no range proof — this fork comes first.**
        if (ExplicitM is not null) {
            return Ok();
        }

        IIntoValue<Flt64>? converter = Converter;
        if (converter is null) {
            return Fail($"the piecewise snapshot `{Name}` has no value converter");
        }

        Result<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>? captured =
            CapturedInputBounds;
        if (captured is null) {
            return Fail($"the piecewise snapshot `{Name}` has no input bounds proof");
        }

        // **捕获本身可能是失败的，那种失败要原样传出去，不能折成"缺失"。**
        // **The capture may itself be a failure, and that failure is forwarded rather than folded into "absent".**
        if (captured is Failed<
                LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> capturedFailure) {
            return Fail(capturedFailure.Error.Message);
        }

        LinearPolynomialBounds<Flt64> original =
            ((Ok<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>)captured).Value;

        Result<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> current =
            PiecewiseInputBounds.Capture(Input, converter);
        if (current is Failed<
                LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> currentFailure) {
            return Fail(currentFailure.Error.Message);
        }

        LinearPolynomialBounds<Flt64> now =
            ((Ok<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>)current).Value;

        if (now.LowerBound.CompareTo(original.LowerBound) < 0 ||
            now.UpperBound.CompareTo(original.UpperBound) > 0) {
            return Fail($"input bounds for `{Name}` exceed the captured Big-M proof");
        }

        return Ok();
    }

    private static Result<Success, ErrorCode, Error<ErrorCode>> Ok() =>
        new Ok<Success, ErrorCode, Error<ErrorCode>>(new Success());

    private static Result<Success, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        new Failed<Success, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);

    /// <summary>
    /// 生成分段线性约束行 / Generate the piecewise rows.
    /// </summary>
    /// <remarks>
    /// **`1 + 4n` 行（`n` 段）：一行选择等式，每段四条。**
    ///
    /// | 行 | 内容 | 被选中时 |
    /// | --- | --- | --- |
    /// | `_select_one` | `Σsel = 1` | **恰好一段被选中** |
    /// | `_seg_i_lb` | `input + M(1 − sel_i) ≥ bp_i` | **`input ≥ bp_i`** |
    /// | `_seg_i_ub` | `input ≤ bp_{i+1} + M(1 − sel_i)` | **`input ≤ bp_{i+1}`** |
    /// | `_seg_i_eq_ub` | `result ≤ slope·input + intercept + M(1 − sel_i)` | **结果 = 该段直线** |
    /// | `_seg_i_eq_lb` | `result ≥ slope·input + intercept − M(1 − sel_i)` | **同上** |
    ///
    /// **未选中时每一条都松弛 `M`。** **所以 `M` 必须覆盖"未选中那段的解离这条行有多远"，
    /// 而<b>每一段需要的松弛量各不相同</b>——这就是逐段算 `M` 的理由。**
    ///
    /// **自动 `M` 取五个量的最大值：**
    ///
    /// | 量 | 含义 |
    /// | --- | --- |
    /// | `bp_i − xLower`（若正） | **输入域在本段下断点之下伸出多远** |
    /// | `xUpper − bp_{i+1}`（若正） | **输入域在上断点之上伸出多远** |
    /// | `|outputUpper − lineLower|` | **本段最低输出离全局输出上界多远** |
    /// | `|outputLower − lineUpper|` | **本段最高输出离全局输出下界多远** |
    /// | **`fallbackM`** | **`|xBounds|` 的绝对值最大者，规范化后作为<b>下界</b>** |
    ///
    /// **最后那一项不是一个"松弛量"，而是一个<b>地板</b>**：**它保证 `M` 不小于输入域的量级。**
    /// **前四项有可能都恒等于 0（例如输入域恰好在段内、输出界恰好贴着直线端点），
    /// 那时 `M` 会是 0，而 `M = 0` 的"松弛"其实不松弛——未选中段的约束会照常生效，
    /// 于是模型变得不可行。地板挡住的就是这件事。**
    ///
    /// **`1 + 4n` rows (`n` segments): one select equality and four per segment.**
    ///
    /// **Every row relaxes by `M` when unselected.** **So `M` has to cover "how far the solution for an unselected segment
    /// sits from this row", and <b>each segment needs a different amount</b> — which is why `M` is derived per
    /// segment.**
    ///
    /// **The automatic `M` takes the maximum of five quantities:** **how far the input domain extends below this
    /// segment's lower breakpoint, how far above the upper one, the distance from this segment's lowest output to the
    /// global output upper bound, the distance from its highest output to the global output lower bound, and
    /// <b>`fallbackM`</b> — the largest absolute input bound, normalized, serving as a <b>floor</b>.**
    ///
    /// **That last one is not a "relaxation" but a <b>floor</b>**: **it keeps `M` at least the magnitude of the input
    /// domain.** **The first four can all be zero (the domain sits inside the segment and the output bounds touch the
    /// line's endpoints), and then `M = 0` — but a relaxation of zero does not relax, the unselected segments' rows
    /// bind, and the model becomes infeasible.** **The floor is what stops that.**
    /// </remarks>
    public Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        GenerateConstraints() {
        try {
            int segments = Breakpoints.Count - 1;
            if (Breakpoints.Count < 2 ||
                Slopes.Count != segments ||
                Intercepts.Count != segments ||
                SelectorVariables.Count != segments) {
                return FailRows("piecewise snapshot dimensions are inconsistent");
            }

            IIntoValue<Flt64>? converter = Converter;
            if (converter is null) {
                return FailRows($"the piecewise snapshot `{Name}` has no value converter");
            }

            if (!ConditionPolynomials.IsUsable(Input)) {
                return FailRows(
                    "the piecewise input polynomial contains a non-finite or unrepresentable value");
            }

            Result<PwlOutputBounds, ErrorCode, Error<ErrorCode>> outputBounds =
                PwlOutputBoundsCalculator.Calculate(Breakpoints, Slopes, Intercepts, converter);
            if (outputBounds is Failed<PwlOutputBounds, ErrorCode, Error<ErrorCode>> badBounds) {
                return FailRows(badBounds.Error.Message);
            }

            PwlOutputBounds output =
                ((Ok<PwlOutputBounds, ErrorCode, Error<ErrorCode>>)outputBounds).Value;

            // **显式 M 走严格正的校验；自动 M 走"可修复"的那条。**
            // **An explicit M takes the strict-positivity check; the automatic one takes the repairable path.**
            Flt64? explicitBigM = null;
            if (ExplicitM is not null) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> resolved =
                    PiecewiseBigM.Resolve(ExplicitM.Value, @explicit: true, converter);
                if (resolved is Failed<Flt64, ErrorCode, Error<ErrorCode>> badM) {
                    return FailRows(badM.Error.Message);
                }

                explicitBigM = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)resolved).Value;
            }

            LinearPolynomialBounds<Flt64>? xBounds = null;
            Flt64? fallbackM = null;

            if (explicitBigM is null) {
                if (CapturedInputBounds is not null) {
                    if (CapturedInputBounds is Failed<
                            LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> badCapture) {
                        return FailRows(badCapture.Error.Message);
                    }

                    xBounds =
                        ((Ok<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>)
                        CapturedInputBounds).Value;
                }
                else {
                    Result<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> captured =
                        PiecewiseInputBounds.Capture(Input, converter);
                    if (captured is Failed<
                            LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>> badCapture) {
                        return FailRows(badCapture.Error.Message);
                    }

                    xBounds =
                        ((Ok<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>)captured)
                        .Value;
                }

                if (xBounds is null) {
                    return FailRows(
                        "the piecewise function has no provable finite input range for automatic Big-M inference");
                }

                // **地板：输入域两端绝对值的较大者。**
                // **The floor: the larger absolute value of the two input bounds.**
                Flt64 magnitudeLower = xBounds.LowerBound.Abs();
                Flt64 magnitudeUpper = xBounds.UpperBound.Abs();
                Flt64 magnitude = magnitudeLower.CompareTo(magnitudeUpper) > 0
                    ? magnitudeLower
                    : magnitudeUpper;

                Result<Flt64, ErrorCode, Error<ErrorCode>> floor =
                    PiecewiseBigM.Resolve(magnitude, @explicit: false, converter);
                if (floor is Failed<Flt64, ErrorCode, Error<ErrorCode>> badFloor) {
                    return FailRows(badFloor.Error.Message);
                }

                fallbackM = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)floor).Value;
            }

            Flt64 zero = Flt64.Zero;
            Flt64 one = converter.IntoValue(Flt64.One);
            LinearMonomial<Flt64>[] empty = Array.Empty<LinearMonomial<Flt64>>();
            var rows = new List<LinearInequality<Flt64>>(1 + segments * 4);

            var selectors = new List<LinearMonomial<Flt64>>(SelectorVariables.Count);
            foreach (IVariableItem selector in SelectorVariables) {
                selectors.Add(new LinearMonomial<Flt64>(one, selector));
            }

            rows.Add(new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(selectors, zero),
                Rhs: new LinearPolynomial<Flt64>(empty, one),
                Comparison: Comparison.EQ,
                Name: $"{Name}_select_one"));

            for (int index = 0; index < segments; ++index) {
                IVariableItem selector = SelectorVariables[index];
                Flt64 breakpointLower = Breakpoints[index];
                Flt64 breakpointUpper = Breakpoints[index + 1];
                Flt64 slope = Slopes[index];
                Flt64 intercept = Intercepts[index];

                Flt64 bigM;
                if (explicitBigM is not null) {
                    bigM = explicitBigM.Value;
                }
                else {
                    // **逐段算四个松弛量，再与地板取最大值。**
                    // **Four per-segment relaxations, then the max with the floor.**
                    Flt64 lineAtLower = slope.Times(xBounds!.LowerBound).Plus(intercept);
                    Flt64 lineAtUpper = slope.Times(xBounds.UpperBound).Plus(intercept);
                    Flt64 lineLower = lineAtLower.CompareTo(lineAtUpper) < 0
                        ? lineAtLower
                        : lineAtUpper;
                    Flt64 lineUpper = lineAtLower.CompareTo(lineAtUpper) > 0
                        ? lineAtLower
                        : lineAtUpper;

                    Flt64 xLowerRelax = breakpointLower.CompareTo(xBounds.LowerBound) > 0
                        ? breakpointLower.Minus(xBounds.LowerBound)
                        : zero;
                    Flt64 xUpperRelax = xBounds.UpperBound.CompareTo(breakpointUpper) > 0
                        ? xBounds.UpperBound.Minus(breakpointUpper)
                        : zero;
                    Flt64 eqUpperRelax = output.Upper.Minus(lineLower).Abs();
                    Flt64 eqLowerRelax = output.Lower.Minus(lineUpper).Abs();

                    Flt64 candidate = xLowerRelax;
                    foreach (Flt64 value in new[] {
                        xUpperRelax, eqUpperRelax, eqLowerRelax, fallbackM!.Value,
                    }) {
                        if (value.CompareTo(candidate) > 0) {
                            candidate = value;
                        }
                    }

                    Result<Flt64, ErrorCode, Error<ErrorCode>> resolved =
                        PiecewiseBigM.Resolve(candidate, @explicit: false, converter);
                    if (resolved is Failed<Flt64, ErrorCode, Error<ErrorCode>> badM) {
                        return FailRows(badM.Error.Message);
                    }

                    bigM = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)resolved).Value;
                }

                var inputMonomials = new List<LinearMonomial<Flt64>>(Input.Monomials.Count);
                foreach (LinearMonomial<Flt64> monomial in Input.Monomials) {
                    inputMonomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
                }

                // **`input + M(1 − sel) ≥ bp_i`，即 `input − M·sel + M ≥ bp_i`。**
                var lower = new List<LinearMonomial<Flt64>>(inputMonomials.Count + 1);
                lower.AddRange(inputMonomials);
                lower.Add(new LinearMonomial<Flt64>(bigM.Negate(), selector));

                rows.Add(new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(lower, Input.Constant.Plus(bigM)),
                    Rhs: new LinearPolynomial<Flt64>(empty, breakpointLower),
                    Comparison: Comparison.GE,
                    Name: $"{Name}_seg_{index}_lb"));

                // **`input ≤ bp_{i+1} + M(1 − sel)`，即 `input + M·sel ≤ bp_{i+1} + M`。**
                var upper = new List<LinearMonomial<Flt64>>(inputMonomials.Count + 1);
                upper.AddRange(inputMonomials);
                upper.Add(new LinearMonomial<Flt64>(bigM, selector));

                rows.Add(new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(upper, Input.Constant),
                    Rhs: new LinearPolynomial<Flt64>(empty, breakpointUpper.Plus(bigM)),
                    Comparison: Comparison.LE,
                    Name: $"{Name}_seg_{index}_ub"));

                // **`result − slope·input + M·sel + (−slope·c − intercept) ≤ M`。**
                var negatedInput = new List<LinearMonomial<Flt64>>(Input.Monomials.Count);
                foreach (LinearMonomial<Flt64> monomial in Input.Monomials) {
                    negatedInput.Add(new LinearMonomial<Flt64>(
                        monomial.Coefficient.Times(slope).Negate(), monomial.Symbol));
                }

                Flt64 equationConstant = slope.Times(Input.Constant).Plus(intercept).Negate();
                var resultMonomial = new LinearMonomial<Flt64>(one, ResultVariable);

                var equationUpper = new List<LinearMonomial<Flt64>>(negatedInput.Count + 2) {
                    resultMonomial,
                };
                equationUpper.AddRange(negatedInput);
                equationUpper.Add(new LinearMonomial<Flt64>(bigM, selector));

                rows.Add(new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(equationUpper, equationConstant),
                    Rhs: new LinearPolynomial<Flt64>(empty, bigM),
                    Comparison: Comparison.LE,
                    Name: $"{Name}_seg_{index}_eq_ub"));

                var equationLower = new List<LinearMonomial<Flt64>>(negatedInput.Count + 2) {
                    resultMonomial,
                };
                equationLower.AddRange(negatedInput);
                equationLower.Add(new LinearMonomial<Flt64>(bigM.Negate(), selector));

                rows.Add(new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(equationLower, equationConstant),
                    Rhs: new LinearPolynomial<Flt64>(empty, bigM.Negate()),
                    Comparison: Comparison.GE,
                    Name: $"{Name}_seg_{index}_eq_lb"));
            }

            foreach (LinearInequality<Flt64> row in rows) {
                if (!ConditionPolynomials.IsUsable(row.Lhs) || !ConditionPolynomials.IsUsable(row.Rhs)) {
                    return FailRows(
                        "the piecewise function generated a non-finite or unrepresentable constraint");
                }
            }

            return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
        }
        catch (Exception error) {
            return FailRows($"failed to generate piecewise constraints: {error.Message}");
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
        FailRows(string message) =>
            new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}
