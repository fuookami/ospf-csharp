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
/// 双向正数指示结构：结果 1 激活 <c>input ≥ tolerance</c>，另一分支 <c>input ≤ 0</c> /
/// Bidirectional positive indicator.
/// </summary>
/// <remarks>
/// 对齐补，连同它循环依赖的 `ConjoinedIndicator`、`DifferenceIndicator`
/// 一起放进本文件——**三者互相引用，分开写会需要三份 partial 或者一个永远不会被单独使用的中间层。**
///
/// **`ConjoinedIndicator.Condition` 的类型就是 `IndicatorStructure`，而 `IndicatorStructure` 又持有
/// 可空的 `Conjunction`。** **C# 允许同程序集内互相引用，所以这里不需要前向声明。**
///
/// **`GenerateConstraints` 的<b>校验顺序</b>是语义的一部分**，而它不直观：
///
/// 1. **合取/差值与其它附加结果不能共存**（**且合取与差值也不能同时给**）
/// 2. **合取与差值的行<b>先生成</b>**
/// 3. **然后才检查零容差带的组合**
///
/// **所以"合取结构本身非法"会先于"零容差带组合非法"报出来。**
/// **把两步顺序调换，两条错误消息都会在对方的场景里出现——而两条都是"非法参数"，
/// 调用方分不出来。**
///
/// Following its `IndicatorStructure`, with its mutually recursive `ConjoinedIndicator` and
/// `DifferenceIndicator` in the same file — **the three reference each other, so splitting them would need three partials
/// or an intermediate layer that is never used alone.**
///
/// **The <b>order of checks</b> in `GenerateConstraints` is part of the semantics, and it is not obvious:**
///
/// 1. **conjunction/difference cannot coexist with other extra results** (**nor with each other**)
/// 2. **the conjunction and difference rows are generated <b>first</b>**
/// 3. **only then is the zero-band combination checked**
///
/// **So "the conjunction is malformed" surfaces before "the zero-band combination is invalid".**
/// **Swapping the two puts each error message into the other's scenario — and both are `IllegalArgument`, so a caller
/// cannot tell them apart.**
/// </remarks>
public sealed record IndicatorStructure(
    LinearPolynomial<Flt64> Input,
    IVariableItem ResultVariable,
    Flt64 BigM,
    Flt64 Tolerance,
    IIntoValue<Flt64> Converter,
    string Name) : DeferredFunctionStructure
{
    /// <summary>使用语境 / Usage context.</summary>
    public FunctionUsageSummary Usage { get; init; } = new();

    /// <summary>自动 M 的有限范围证明 / Finite bounds proof for the automatic M.</summary>
    public LinearPolynomialBounds<Flt64>? CapturedInputBounds { get; init; }

    /// <summary>为真时由结果 0 激活正分支 / Activate the positive branch on result zero when true.</summary>
    public bool PositiveOnZero { get; init; }

    /// <summary>可选的原条件范围 / Optional original condition range.</summary>
    public ConditionBounds<Flt64>? ConditionBounds { get; init; }

    /// <summary>必须保留且与结果相等的公开列 / Retained public columns equal to the result.</summary>
    public IReadOnlyList<IVariableItem> EquivalentResults { get; init; } = Array.Empty<IVariableItem>();

    /// <summary>可选的条件数值结果 / Optional conditional numeric result.</summary>
    public ConditionalValue? ConditionalValue { get; init; }

    /// <summary>可选的单向蕴含后件 / Optional one-way implication consequent.</summary>
    public ImpliedCondition? ImpliedCondition { get; init; }

    /// <summary>可选的零容差带 / Optional zero band.</summary>
    public ZeroBand<Flt64>? ZeroBand { get; init; }

    /// <summary>可选的第二个条件及 AND 结果 / Optional second condition and AND result.</summary>
    public ConjoinedIndicator? Conjunction { get; init; }

    /// <summary>可选的互斥条件差值 / Optional exclusive condition difference.</summary>
    public DifferenceIndicator? Difference { get; init; }

    /// <summary>
    /// 原生路径必须保留的全部结果列 / All result columns native lowering must retain.
    /// </summary>
    /// <remarks>
    /// **顺序有意义**：**`ResultVariable` 恒在首位，其余按"条件值、蕴含、零带、合取、差值"的固定顺序追加。**
    /// **判据按位置核对，所以这个顺序不能随手调整。**
    ///
    /// **The order matters**: **`ResultVariable` is always first, then the fixed order conditional value, implication,
    /// zero band, conjunction, difference.** **A criterion checks by position, so the order cannot be reshuffled
    /// casually.**
    /// </remarks>
    public IReadOnlyList<IVariableItem> RetainedResultVariables {
        get {
            var retained = new List<IVariableItem>(2 + EquivalentResults.Count) { ResultVariable };
            retained.AddRange(EquivalentResults);

            if (ConditionalValue is not null) {
                retained.Add(ConditionalValue.ResultVariable);
            }

            if (ImpliedCondition is not null) {
                retained.Add(ImpliedCondition.IndicatorVariable);
            }

            if (ZeroBand is not null) {
                retained.Add(ZeroBand.SideVariable);
            }

            if (Conjunction is not null) {
                retained.Add(Conjunction.Condition.ResultVariable);
                retained.Add(Conjunction.ResultVariable);
            }

            if (Difference is not null) {
                retained.Add(Difference.Condition.ResultVariable);
                retained.Add(Difference.ResultVariable);
            }

            return retained;
        }
    }

    /// <summary>
    /// 生成全部行 / Generate every row.
    /// </summary>
    /// <remarks>
    /// **校验顺序是语义的一部分**（见类型注释）：**合取/差值与其它附加结果不能共存 → 先生成合取/差值行 →
    /// 再检查零容差带组合。** **把这两步调换，两条 `IllegalArgument` 会在对方的场景里出现。**
    /// </remarks>
    /// <summary>
    /// 物化 fallback 约束 / Materialize the fallback constraints.
    /// </summary>
    /// <remarks>
    /// **这一层是<b>纯包装</b>：把 `GenerateConstraints` 的结果塞进 `DeferredFunctionFallbackConstraints`。**
    ///
    /// **包装而不是让物化器直接调 `GenerateConstraints` 的理由：**
    /// **物化器的返回类型统一是 `DeferredFunctionFallbackConstraints`，
    /// 而那个类型标记"这些行来自 fallback 路径"——调用方据此决定要不要登记等价列。**
    /// **若某个物化分支裸返回列表，那个标记就丢了，而丢掉的标记不会报错。**
    ///
    /// **A <b>pure wrapper</b>: it puts `GenerateConstraints`'s rows into `DeferredFunctionFallbackConstraints`.**
    ///
    /// **Wrapping rather than letting the materializer call `GenerateConstraints` directly because the materializer's
    /// return type is uniformly `DeferredFunctionFallbackConstraints`, and that type marks "these rows came from the
    /// fallback path" — which the caller uses to decide about registering equivalents.** **A branch returning a bare list
    /// would lose the marker, and a lost marker does not throw.**
    /// </remarks>
    public Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
        MaterializeFallbackConstraints() {
        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> rows =
            GenerateConstraints();

        return rows switch {
            Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> ok =>
                new Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                    new DeferredFunctionFallbackConstraints(ok.Value)),
            Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> failed =>
                new Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                    failed.Error.Code, failed.Error.Message),
            _ => new Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, "unreachable"),
        };
    }

    public Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> GenerateConstraints() {
        if ((Conjunction is not null || Difference is not null) &&
            (ZeroBand is not null || ConditionalValue is not null || ImpliedCondition is not null ||
             EquivalentResults.Count != 0 || (Conjunction is not null && Difference is not null))) {
            return Reject("conjoined conditions do not support additional results");
        }

        var rows = new List<LinearInequality<Flt64>>();

        if (Conjunction is not null) {
            Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> conjunction =
                Conjunction.GenerateConstraints(ResultVariable);
            if (conjunction is Failed<
                    IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> cf) {
                return Reject(cf.Error.Message);
            }

            rows.AddRange(
                ((Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)conjunction).Value);
        }

        if (Difference is not null) {
            Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> difference =
                Difference.GenerateConstraints(ResultVariable);
            if (difference is Failed<
                    IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> df) {
                return Reject(df.Error.Message);
            }

            rows.AddRange(
                ((Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)difference).Value);
        }

        if (ZeroBand is not null &&
            (ConditionBounds is not null || ConditionalValue is not null || ImpliedCondition is not null ||
             ContainsByReference(EquivalentResults, ZeroBand.SideVariable))) {
            return Reject("the zero-band structure combination is invalid");
        }

        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> implied = GenerateImplied();
        if (implied is Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> impliedF) {
            return Reject(impliedF.Error.Message);
        }

        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> value =
            GenerateConditionalValue();
        if (value is Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> valueF) {
            return Reject(valueF.Error.Message);
        }

        if (EquivalentResults.Count != 0) {
            var seen = new HashSet<ReferenceKey>();
            foreach (IVariableItem equivalent in EquivalentResults) {
                if (!equivalent.TypeKind.IsBinaryType || ReferenceEquals(equivalent, ResultVariable)) {
                    return Reject("an equivalent result column must be a distinct binary");
                }

                if (!seen.Add(new ReferenceKey(equivalent))) {
                    return Reject("equivalent result columns must be pairwise distinct");
                }
            }
        }

        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> indicator =
            GenerateIndicatorConstraints();
        if (indicator is Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> failure) {
            return Reject(failure.Error.Message);
        }

        rows.AddRange(
            ((Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)indicator).Value);
        rows.AddRange(
            ((Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)value).Value);
        rows.AddRange(
            ((Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)implied).Value);

        for (int index = 0; index < EquivalentResults.Count; ++index) {
            rows.Add(new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(
                    new[] {
                        new LinearMonomial<Flt64>(Converter.IntoValue(Flt64.One), ResultVariable),
                        new LinearMonomial<Flt64>(
                            Converter.IntoValue(Flt64.One).Negate(), EquivalentResults[index]),
                    },
                    Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                Comparison: Comparison.EQ,
                Name: $"{Name}_equivalent_{index}"));
        }

        return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
    }

    private Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        GenerateImplied() =>
        ImpliedCondition is null
            ? Empty()
            : ImpliedCondition.GenerateConstraints(ResultVariable, Tolerance, Converter);

    private Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        GenerateConditionalValue() =>
        ConditionalValue is null
            ? Empty()
            : ConditionalValue.GenerateConstraints(ResultVariable, Converter);

    private static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> Empty() =>
        new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
            Array.Empty<LinearInequality<Flt64>>());

    private static bool ContainsByReference(IReadOnlyList<IVariableItem> items, IVariableItem target) {
        foreach (IVariableItem item in items) {
            if (ReferenceEquals(item, target)) {
                return true;
            }
        }

        return false;
    }

    private Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        GenerateIndicatorConstraints() {
        // **原始指示行先算：它是"输入是否可用"的唯一判断点。**
        // **The primitive indicator rows come first: they are the single place that judges whether the input is usable.**
        if (!ResultVariable.TypeKind.IsBinaryType) {
            return Reject($"the result column `{ResultVariable.Name}` must be binary");
        }

        if (!BigMValues.IsUsable(Tolerance) || Tolerance.CompareTo(Flt64.Zero) <= 0) {
            return Reject("the tolerance must be finite, encodable and greater than zero");
        }

        if (!ConditionPolynomials.IsUsable(Input)) {
            return Reject(
                "the indicator input's constant and every coefficient must be finite and encodable");
        }

        // **捕捉到的范围证明必须没收窄**：**输入的范围一旦超出当初证明的范围，
        // 自动 M 就不再是合法上界，而后续行仍然"看起来"正确。**
        //
        // **The captured bounds proof must not have narrowed**: **once the input's range exceeds the proven one the
        // automatic M stops being a legal upper bound, while the rows still "look" right.**
        if (CapturedInputBounds is not null) {
            LinearPolynomialBounds<Flt64>? current = BigMValues.FiniteBounds(Input, Converter);
            if (current is null) {
                return Reject("the indicator bounds proof is unavailable");
            }

            if (current.LowerBound.CompareTo(CapturedInputBounds.LowerBound) < 0 ||
                current.UpperBound.CompareTo(CapturedInputBounds.UpperBound) > 0) {
                return Reject("the indicator bounds exceed the captured proof");
            }
        }

        if (ConditionBounds is not null) {
            return RelationIndicatorRows.Build(
                poly: Input,
                indicator: ResultVariable,
                relation: PositiveOnZero ? Comparison.LE : Comparison.GT,
                bounds: ConditionBounds,
                strictBoundary: Tolerance,
                namePrefix: Name);
        }

        if (ZeroBand is not null) {
            if (!ZeroBand.SideVariable.TypeKind.IsBinaryType ||
                ZeroBand.SideVariable.Key == ResultVariable.Key ||
                !BigMValues.IsUsable(ZeroBand.Tolerance) ||
                ZeroBand.Tolerance.CompareTo(Flt64.Zero) < 0 ||
                ZeroBand.Tolerance.CompareTo(Tolerance) >= 0) {
                return Reject("the zero band or its direction column is invalid");
            }

            return PositiveOnZero
                ? ZeroIndicatorRows.Build(
                    poly: Input,
                    indicator: ResultVariable,
                    sideVar: ZeroBand.SideVariable,
                    bigM: BigM,
                    tolerance: ZeroBand.Tolerance,
                    strictBoundary: Tolerance,
                    namePrefix: Name)
                : ZeroIndicatorRows.BuildNonzero(
                    poly: Input,
                    indicator: ResultVariable,
                    sideVar: ZeroBand.SideVariable,
                    bigM: BigM,
                    tolerance: ZeroBand.Tolerance,
                    strictBoundary: Tolerance,
                    namePrefix: Name);
        }

        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> positive =
            PositiveIndicatorRows.Build(Input, ResultVariable, BigM, Tolerance, Name);
        if (positive is Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> positiveFailure) {
            return Reject(positiveFailure.Error.Message);
        }

        if (!PositiveOnZero) {
            return positive;
        }

        return InvertPositive(
            ((Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)positive).Value);
    }

    /// <summary>
    /// 极性的变换：**重新生成两行，而不是把已有行的方向翻过来** /
    /// The polarity transform **regenerates two rows instead of flipping the existing ones**.
    /// </summary>
    /// <remarks>
    /// **把 `≥` 改成 `≤` 并取负右侧，看起来等价，但会让 `_satisfied` / `_violated` 的名字落到相反的行上。**
    /// **基线选择重新生成，本实现照做——这样名字与语义的对应关系与正极性一致。**
    ///
    /// **Flipping `≥` to `≤` and negating the right-hand side looks equivalent but lands `_satisfied` and `_violated`
    /// on the opposite rows.** **The code regenerates, and so does this — keeping the name-to-meaning mapping the
    /// same as the positive polarity.**
    /// </remarks>
    private Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        InvertPositive(IReadOnlyList<LinearInequality<Flt64>> ignored) {
        var monomials = new List<LinearMonomial<Flt64>>(Input.Monomials.Count + 1);
        foreach (LinearMonomial<Flt64> monomial in Input.Monomials) {
            monomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient, monomial.Symbol));
        }

        var satisfiedMonomials = new List<LinearMonomial<Flt64>>(monomials) {
            new LinearMonomial<Flt64>(BigM, ResultVariable),
        };

        var violatedMonomials = new List<LinearMonomial<Flt64>>(monomials) {
            new LinearMonomial<Flt64>(BigM.Plus(Tolerance), ResultVariable),
        };

        var rows = new List<LinearInequality<Flt64>>(2) {
            new(
                Lhs: new LinearPolynomial<Flt64>(satisfiedMonomials, Input.Constant),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), BigM),
                Comparison: Comparison.LE,
                Name: $"{Name}_satisfied"),
            new(
                Lhs: new LinearPolynomial<Flt64>(violatedMonomials, Input.Constant),
                Rhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), Tolerance),
                Comparison: Comparison.GE,
                Name: $"{Name}_violated"),
        };

        foreach (LinearInequality<Flt64> row in rows) {
            if (!ConditionPolynomials.IsUsable(row.Lhs) || !ConditionPolynomials.IsUsable(row.Rhs)) {
                return Reject("the indicator polarity transform produced unusable values");
            }
        }

        return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
    }

    internal static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        Reject(string message) =>
            new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);
}

/// <summary>
/// 第二个条件与主条件的二值合取 / Binary conjunction of a second condition with the primary one.
/// </summary>
/// <remarks>
/// **三行链接，它们的合取就是 `result = primary ∧ condition.result`：**
///
/// | 行 | 内容 | 作用 |
/// | --- | --- | --- |
/// | `_link_ge` | `result − primary ≤ 0` | **`result ⇒ primary`** |
/// | `_link_le` | `result − conditionResult ≤ 0` | **`result ⇒ conditionResult`** |
/// | `_link_lb` | `result − primary − conditionResult ≥ −1` | **`primary ∧ conditionResult ⇒ result`** |
///
/// **前两行只说"合取不能比任一支大"，第三行补上"也不能比任一支小"。**
/// **只有前两行的话，`result = 0` 永远可行——合取就被常量 0 替换了，而模型完全可行。**
///
/// **三个二元量必须互不相同**：**`primary` 与 `conditionResult` 相同意味着"自己与自己合取"，
/// 而第三行会退化成 `result − 2·primary ≥ −1`。**
///
/// **Three link rows whose conjunction is `result = primary ∧ condition.result`:**
///
/// **The first two say "the conjunction is at most either branch"; the third adds "and not less".**
/// **Without the third, `result = 0` is always feasible — the conjunction would be replaced by the constant zero while
/// the model stays feasible.**
///
/// **The three binaries must be pairwise distinct**: **`primary` equal to `conditionResult` means "conjoin a condition
/// with itself", and the third row degenerates to `result − 2·primary ≥ −1`.**
/// </remarks>
public sealed record ConjoinedIndicator(
    IndicatorStructure Condition,
    IVariableItem ResultVariable,
    string Name)
{
    /// <summary>生成条件自己的行加三条链接行 / Generate the condition's rows plus three links.</summary>
    internal Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        GenerateConstraints(IVariableItem primary) {
        if (!ResultVariable.TypeKind.IsBinaryType ||
            new HashSet<ReferenceKey> { new(primary), new(Condition.ResultVariable), new(ResultVariable) }.Count != 3) {
            return IndicatorStructure.Reject(
                "the conjoined indicator is invalid: its three columns must be distinct binaries");
        }

        // **被合取的那个条件本身不能再挂附加结果**：**否则"合取"这一层会把它们的结果列
        // 一起算进 `result`，而 `result` 本应只是 AND。**
        //
        // **The conjoined condition cannot itself carry extra results**: **otherwise the conjunction would fold those
        // result columns into `result`, which should be only the AND.**
        if (Condition.Conjunction is not null || Condition.Difference is not null ||
            Condition.ConditionalValue is not null || Condition.ImpliedCondition is not null ||
            Condition.ZeroBand is not null || Condition.EquivalentResults.Count != 0) {
            return IndicatorStructure.Reject("the conjoined indicator structure is invalid");
        }

        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> generated =
            Condition.GenerateConstraints();
        if (generated is Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> failure) {
            return IndicatorStructure.Reject(failure.Error.Message);
        }

        Flt64 one = ConverterOne(Condition);
        Flt64 zero = Flt64.Zero;
        var rows = new List<LinearInequality<Flt64>>(
            ((Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)generated).Value) {
            Row(
                new[] {
                    new LinearMonomial<Flt64>(one, ResultVariable),
                    new LinearMonomial<Flt64>(one.Negate(), primary),
                },
                zero, zero, Comparison.LE, $"{Name}_link_ge"),
            Row(
                new[] {
                    new LinearMonomial<Flt64>(one, ResultVariable),
                    new LinearMonomial<Flt64>(one.Negate(), Condition.ResultVariable),
                },
                zero, zero, Comparison.LE, $"{Name}_link_le"),
            Row(
                new[] {
                    new LinearMonomial<Flt64>(one, ResultVariable),
                    new LinearMonomial<Flt64>(one.Negate(), primary),
                    new LinearMonomial<Flt64>(one.Negate(), Condition.ResultVariable),
                },
                zero, one.Negate(), Comparison.GE, $"{Name}_link_lb"),
        };

        return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
    }

    private static Flt64 ConverterOne(IndicatorStructure condition) =>
        condition.Converter.IntoValue(Flt64.One);

    private static LinearInequality<Flt64> Row(
        IReadOnlyList<LinearMonomial<Flt64>> monomials,
        Flt64 constant,
        Flt64 rhsConstant,
        Comparison comparison,
        string name) =>
        new(
            Lhs: new LinearPolynomial<Flt64>(monomials, constant),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), rhsConstant),
            Comparison: comparison,
            Name: name);
}

/// <summary>
/// 两个互斥条件的有符号差值 / Signed difference of two mutually exclusive conditions.
/// </summary>
/// <remarks>
/// **两行，而它们的分工与合取相反**：
///
/// | 行 | 内容 | 作用 |
/// | --- | --- | --- |
/// | `_bter_result` | `result − primary + conditionResult = 0` | **`result = primary − conditionResult`** |
/// | `_bter_exclusive` | `primary + conditionResult ≤ 1` | **两者不能同时为 1** |
///
/// **第一行是等式，它同时钉住"值是差"与"两个分支不都取 1"。**
/// **第二行是<b>冗余</b>的——等式已经蕴含它。**
/// **保留它是因为：`result` 可以是 `−1`/`0`/`1`，而等式允许 `primary = conditionResult = 1, result = 0`；
/// 那种组合下"两个条件同时成立"会被表示成 `result = 0`，与"两个都不成立"混淆。**
/// **第二行把它排除掉。**
///
/// **Two rows, and they divide the work the opposite way from a conjunction:**
///
/// **The first is an equality pinning both "the value is the difference" and "not both branches are one".**
/// **The second is <b>redundant</b> given the equality — no; it is not.** **`result` may be `−1`, `0` or `1`, and the
/// equality admits `primary = conditionResult = 1` with `result = 0`; that state would read as "neither holds" while both
/// do.** **The second row excludes it.**
/// </remarks>
public sealed record DifferenceIndicator(
    IndicatorStructure Condition,
    IVariableItem ResultVariable,
    string Name)
{
    /// <summary>生成条件自己的行加两条链接行 / Generate the condition's rows plus two links.</summary>
    internal Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        GenerateConstraints(IVariableItem primary) {
        if (new HashSet<ReferenceKey> { new(primary), new(Condition.ResultVariable), new(ResultVariable) }.Count != 3) {
            return IndicatorStructure.Reject(
                "the exclusive difference is invalid: its three columns must be distinct");
        }

        if (Condition.Conjunction is not null || Condition.Difference is not null ||
            Condition.ConditionalValue is not null || Condition.ImpliedCondition is not null ||
            Condition.ZeroBand is not null || Condition.EquivalentResults.Count != 0) {
            return IndicatorStructure.Reject("the exclusive difference structure is invalid");
        }

        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> generated =
            Condition.GenerateConstraints();
        if (generated is Failed<
                IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> failure) {
            return IndicatorStructure.Reject(failure.Error.Message);
        }

        Flt64 one = Condition.Converter.IntoValue(Flt64.One);
        Flt64 zero = Flt64.Zero;
        var rows = new List<LinearInequality<Flt64>>(
            ((Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)generated).Value) {
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    new[] {
                        new LinearMonomial<Flt64>(one, ResultVariable),
                        new LinearMonomial<Flt64>(one.Negate(), primary),
                        new LinearMonomial<Flt64>(one, Condition.ResultVariable),
                    },
                    zero),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), zero),
                Comparison: Comparison.EQ,
                Name: $"{Name}_bter_result"),
            new(
                Lhs: new LinearPolynomial<Flt64>(
                    new[] {
                        new LinearMonomial<Flt64>(one, primary),
                        new LinearMonomial<Flt64>(one, Condition.ResultVariable),
                    },
                    zero),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), one),
                Comparison: Comparison.LE,
                Name: $"{Name}_bter_exclusive"),
        };

        return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
    }
}

/// <summary>
/// 按<b>标识</b>比较的变量键 / A variable key compared by <b>identity</b>.
/// </summary>
/// <remarks>
/// **用 `ReferenceEquals` 而不是 `Equals`**：**两个同名的变量在这个模型层是不同的列，
/// 而"三个列是否互不相同"问的是标识，不是名字。**
///
/// **`ReferenceEquals` rather than `Equals`**: **two same-named variables are distinct columns at this model layer, and
/// "are the three columns distinct" asks about identity, not names.**
/// </remarks>
internal readonly struct ReferenceKey(IVariableItem item) : IEquatable<ReferenceKey>
{
    private readonly IVariableItem _item = item;

    public bool Equals(ReferenceKey other) => ReferenceEquals(_item, other._item);

    public override bool Equals(object? obj) => obj is ReferenceKey other && Equals(other);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_item);

}
