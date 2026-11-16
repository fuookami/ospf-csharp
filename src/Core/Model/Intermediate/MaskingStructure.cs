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
/// 由现有二值 mask 门控的数值结果 / A numeric result gated by an existing binary mask.
/// </summary>
/// <remarks>
/// 对齐的 `MaskingStructure`。
///
/// **mask 列可能是<b>共享</b>的，而共享决定了谁保留它。**
///
/// | `MaskDefinition` | 保留 mask 吗 | 理由 |
/// | --- | --- | --- |
/// | 非空 | **保留** | **本结构定义它，所以本结构是它的所有者** |
/// | 空 | **不保留** | **它由别的结构定义；这里再登记一次会让同一个列被登记两遍** |
///
/// **"这个列我用到了"与"这个列归我"是两件事。** **`RetainedResultVariables` 回答的是后者。**
///
/// **定义行是 `mask − definition = 0`，而且它排在条件值的行<b>之前</b>。** **顺序本身不是语义
/// （等式在哪都一样），但如果有人按下标切行、或者按"前 N 行是本结构的定义"做假设，顺序就成了契约。**
/// **这里保留顺序，并把这件事写下来。**
///
/// **差值取 `mask − definition` 而不是 `definition − mask`**：**等式两边取反等价，
/// 所以这一处不影响正确性。但若哪天改成不等式，方向就成了语义——
/// 现在写成减数在前是有意的，不是随手。**
///
/// Following its `MaskingStructure`.
///
/// **The mask column may be <b>shared</b>, and sharing decides who retains it.**
///
/// | `MaskDefinition` | Retains the mask | Why |
/// | --- | --- | --- |
/// | non-null | **yes** | **this structure defines it, so this structure owns it** |
/// | null | **no** | **another structure defines it; registering it here would register one column twice** |
///
/// **"I use this column" and "this column is mine" are two things.** **`RetainedResultVariables` answers the second.**
///
/// **The definition row is `mask − definition = 0` and it sits <b>before</b> the conditional value's rows.** **The order
/// is not itself semantics (an equality is an equality anywhere), but if anyone slices by index or assumes "the first N
/// rows are this structure's definition", the order becomes a contract.** **order is kept, and this is
/// recorded.**
///
/// **The difference is `mask − definition` rather than the reverse**: **negating an equality is equivalent, so it does
/// not affect correctness here. But if it ever became an inequality the direction would be semantics — the subtrahend
/// ordering is deliberate, not incidental.**
/// </remarks>
public sealed record MaskingStructure(
    ConditionalValue Value,
    IVariableItem Mask,
    IIntoValue<Flt64> Converter) : DeferredFunctionStructure
{
    /// <summary>使用语境 / Usage context.</summary>
    public FunctionUsageSummary Usage { get; init; } = new();

    /// <summary>可选的 mask 定义，强制 mask 等于该表达式 / Optional definition forcing the mask's value.</summary>
    public LinearPolynomial<Flt64>? MaskDefinition { get; init; }

    /// <summary>条件数值结果列 / The conditional numeric result column.</summary>
    public IVariableItem ResultVariable => Value.ResultVariable;

    /// <summary>
    /// 本结构定义并必须保留的公开列 / Public columns this structure defines and retains.
    /// </summary>
    public IReadOnlyList<IVariableItem> RetainedResultVariables =>
        MaskDefinition is null
            ? new[] { ResultVariable }
            : new[] { ResultVariable, Mask };

    /// <summary>代表 mask 列的线性多项式 / A linear polynomial standing for the mask column.</summary>
    /// <remarks>
    /// **`1·mask + 0`，系数来自转换器而不是字面 `1`**：**别的数值类型可能不把"一"表示成 `Flt64.One` 的等值。**
    ///
    /// **`1·mask + 0`, with the coefficient from the converter rather than a literal one**: **another numeric type may not
    /// spell "one" as the value of `Flt64.One`.**
    /// </remarks>
    public LinearPolynomial<Flt64> MaskPolynomial =>
        new(new[] { new LinearMonomial<Flt64>(Converter.IntoValue(Flt64.One), Mask) }, Flt64.Zero);

    /// <summary>生成 mask 定义行与条件值的行 / Generate the mask definition and the conditional value's rows.</summary>
    public Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        GenerateConstraints() {
        try {
            Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> generated =
                Value.GenerateConstraints(Mask, Converter);
            if (generated is Failed<
                    IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> failure) {
                return Fail(failure.Error.Message);
            }

            IReadOnlyList<LinearInequality<Flt64>> values =
                ((Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)generated).Value;

            // **共享的 mask 由别的结构定义，本结构不加行、也不保留它。**
            // **A shared mask is defined elsewhere; this structure adds no row and does not retain it.**
            if (MaskDefinition is null) {
                return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
                    values);
            }

            var monomials = new List<LinearMonomial<Flt64>>(MaskDefinition.Monomials.Count + 1);
            foreach (LinearMonomial<Flt64> monomial in MaskDefinition.Monomials) {
                monomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient.Negate(), monomial.Symbol));
            }

            monomials.Add(new LinearMonomial<Flt64>(Converter.IntoValue(Flt64.One), Mask));

            var difference = new LinearPolynomial<Flt64>(
                monomials, MaskDefinition.Constant.Negate());

            if (!ConditionPolynomials.IsUsable(difference)) {
                return Fail("the mask definition produced a non-finite or unrepresentable difference");
            }

            var rows = new List<LinearInequality<Flt64>>(values.Count + 1) {
                new(
                    Lhs: difference,
                    Rhs: new LinearPolynomial<Flt64>(
                        Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: Comparison.EQ,
                    Name: $"{Value.Name}_mask_eq"),
            };
            rows.AddRange(values);

            return new Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(rows);
        }
        catch (Exception) {
            return Fail("failed to generate the mask definition");
        }
    }

    private static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
        Fail(string message) =>
            new Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument, message);

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
}
