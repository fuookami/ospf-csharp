#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// <summary>已验证的条件数值 / Validated conditional numeric value.</summary>
/// <param name="Terms">仿射系数 / Affine coefficients.</param>
/// <param name="Constant">仿射常数 / Affine constant.</param>
/// <param name="ResultKey">结果列键 / Result column key.</param>
/// <param name="LowerBound">原真分支下界 / Original true-branch lower bound.</param>
/// <param name="UpperBound">原真分支上界 / Original true-branch upper bound.</param>
/// <param name="Name">约束名前缀 / Constraint name prefix.</param>
public sealed record NativeConditionalValueData(
    IReadOnlyDictionary<VariableItemKey, double> Terms,
    double Constant,
    VariableItemKey ResultKey,
    double LowerBound,
    double UpperBound,
    string Name);

/// <summary>mask 的仿射定义 / Affine mask definition.</summary>
/// <param name="Terms">仿射系数 / Affine coefficients.</param>
/// <param name="Constant">常数项 / Constant term.</param>
public sealed record NativeMaskDefinitionData(
    IReadOnlyDictionary<VariableItemKey, double> Terms,
    double Constant);

/// <summary>已验证的二值门控 / Validated binary masking.</summary>
/// <param name="MaskKey">输入 mask 键 / Input mask key.</param>
/// <param name="Value">数值结果及范围证明 / Numeric result and bounds proof.</param>
/// <param name="Definition">可选的 mask 定义等式 / Optional mask definition equality.</param>
public sealed record NativeMaskingData(
    VariableItemKey MaskKey,
    NativeConditionalValueData Value,
    NativeMaskDefinitionData? Definition = null);

/// <summary>
/// 条件数值与二值门控的准备 / Preparation for conditional values and binary masking.
/// </summary>
/// <remarks>
/// 对齐基线 `NativeIndicator.kt` 的 `prepareConditionalValue` 与
/// `NativeMasking.kt` 的 `prepareNativeMasking` / `prepareMasking` 补。
///
/// **范围证明是这两格的实质：** **不能说"这个表达式在 `[lower, upper]` 内"，
/// 除非把每个变量的真实上下界代进去算出一个包络。**
///
/// **所以算法是"按系数符号取对应的那一端"：**
///
/// ```
/// actualLower = constant + Σ (coefficient > 0 ? coefficient × lb : coefficient × ub)
/// actualUpper = constant + Σ (coefficient > 0 ? coefficient × ub : coefficient × lb)
/// ```
///
/// **负系数要把上下界对调——否则一个负系数会把"最坏情形"算成"最好情形"。**
///
/// **这个包络是<b>保守的</b>：** **它假设所有变量可以同时取到各自的极值，
/// 而真实可行域里它们可能被别的约束连在一起。**
/// **所以"包络在范围内"是充分不必要条件；包络越界时会误拒。**
/// **这是可接受的：拒绝一个能用的模型比接受一个不能用的模型安全。**
///
/// **没有上下界的变量直接拒：** **一个无界变量让包络无从计算，
/// 而"跳过它"会让范围证明变成一句空话。**
///
/// **`lower > upper` 被拒**——否则这个区间本身是空的。
///
/// **`filterValues { it != 0.0 }` 在返回之前丢零系数：** **它们不影响包络
/// （已经算过了），但会让指纹与渲染带上无意义的项。**
///
/// **The range proof is the substance of both cells: you cannot say "this expression stays within
/// `[lower, upper]`" without plugging every variable's real bounds into an envelope.**
///
/// **So the algorithm takes the corresponding end by coefficient sign** (see the formula above): **a negative coefficient
/// swaps the bounds, otherwise a negative coefficient would compute the best case where the worst belongs.**
///
/// **The envelope is <b>conservative</b>: it assumes all variables reach their extremes simultaneously, while the real
/// feasible region may couple them.** **So "the envelope is in range" is sufficient but not necessary; an out-of-range
/// envelope over-rejects.** **Acceptable: refusing a usable model beats accepting an unusable one.**
///
/// **An unbounded variable is refused outright: the envelope is uncomputable, and skipping it would make the proof
/// vacuous.**
///
/// **`lower > upper` is refused — that interval is empty.**
///
/// **Zero coefficients are dropped before returning: they no longer affect the envelope (already computed) but would put
/// meaningless terms into fingerprints and rendering.**
/// </remarks>
public static class NativeMasking
{
    /// <summary>准备一个条件数值 / Prepare one conditional value.</summary>
    public static Result<NativeConditionalValueData, ErrorCode, Error<ErrorCode>> PrepareConditionalValue(
        ConditionalValue value,
        IIntoValue<Flt64> converter,
        double maximumMagnitude = double.PositiveInfinity) {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(converter);

        double lower = ToSolverDouble(converter.FromValue(value.Bounds.Lower));
        double upper = ToSolverDouble(converter.FromValue(value.Bounds.Upper));
        double constant = ToSolverDouble(converter.FromValue(value.Input.Constant));

        // **`lower > upper`：区间本身是空的。**
        // **lower > upper: the interval is empty.**
        if (!Usable(lower, maximumMagnitude) || !Usable(upper, maximumMagnitude) ||
            !Usable(constant, maximumMagnitude) || lower > upper) {
            return Fail();
        }

        var terms = new Dictionary<VariableItemKey, double>();
        var variables = new Dictionary<VariableItemKey, IVariableItem>();

        foreach (LinearMonomial<Flt64> monomial in value.Input.Monomials) {
            if (monomial.Symbol is not IVariableItem variable) {
                return Fail();
            }

            // **结果变量不能在它自己的输入里：`result = result + 1` 无法用包络证明。**
            // **The result must not appear in its own input: `result = result + 1` cannot be proven by an envelope.**
            if (variable.Key == value.ResultVariable.Key) {
                return Fail();
            }

            double coefficient = ToSolverDouble(converter.FromValue(monomial.Coefficient));
            double combined = (terms.TryGetValue(variable.Key, out double existing) ? existing : 0.0)
                + coefficient;

            if (!Usable(coefficient, maximumMagnitude) || !Usable(combined, maximumMagnitude)) {
                return Fail();
            }

            terms[variable.Key] = combined;
            variables[variable.Key] = variable;
        }

        double actualLower = constant;
        double actualUpper = constant;

        foreach (KeyValuePair<VariableItemKey, double> term in terms) {
            if (term.Value == 0.0) {
                continue;
            }

            IVariableItem variable = variables[term.Key];

            if (variable.LowerBound is null || variable.UpperBound is null) {
                return Fail();
            }

            double variableLower = ToSolverDouble(variable.LowerBound.Value.ToFlt64());
            double variableUpper = ToSolverDouble(variable.UpperBound.Value.ToFlt64());

            if (!Usable(variableLower, maximumMagnitude) ||
                !Usable(variableUpper, maximumMagnitude) ||
                variableLower > variableUpper) {
                return Fail();
            }

            // **负系数对调上下界。**
            // **A negative coefficient swaps the bounds.**
            actualLower += term.Value * (term.Value > 0 ? variableLower : variableUpper);
            actualUpper += term.Value * (term.Value > 0 ? variableUpper : variableLower);

            if (!Usable(actualLower, maximumMagnitude) || !Usable(actualUpper, maximumMagnitude)) {
                return Fail();
            }
        }

        if (actualLower < lower || actualUpper > upper) {
            return Fail();
        }

        return new Ok<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>(
            new NativeConditionalValueData(
                Terms: NonZero(terms),
                Constant: constant,
                ResultKey: value.ResultVariable.Key,
                LowerBound: lower,
                UpperBound: upper,
                Name: value.Name));
    }

    /// <summary>准备一个二值门控 / Prepare one binary masking.</summary>
    public static Result<NativeMaskingData, ErrorCode, Error<ErrorCode>> Prepare(
        MaskingStructure structure,
        double maximumMagnitude = double.PositiveInfinity) {
        ArgumentNullException.ThrowIfNull(structure);

        // **先让结构自己生成约束：一个连约束都生成不出来的结构，谈范围证明没有意义。**
        // **Let the structure generate its own rows first: proving bounds for a structure that cannot even emit rows is
        // pointless.**
        Result<IReadOnlyList<Math.Symbol.Inequality.LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>
            constraints = structure.GenerateConstraints();
        if (constraints is Failed<
                IReadOnlyList<Math.Symbol.Inequality.LinearInequality<Flt64>>,
                ErrorCode, Error<ErrorCode>> bad) {
            return new Failed<NativeMaskingData, ErrorCode, Error<ErrorCode>>(
                bad.Error.Code, bad.Error.Message);
        }

        NativeMaskDefinitionData? definition = null;

        if (structure.MaskDefinition is not null) {
            double constant = ToSolverDouble(
                structure.Converter.FromValue(structure.MaskDefinition.Constant));
            if (!Usable(constant, maximumMagnitude)) {
                return FailMasking("invalid mask definition constant");
            }

            var terms = new Dictionary<VariableItemKey, double>();

            foreach (LinearMonomial<Flt64> monomial in structure.MaskDefinition.Monomials) {
                if (monomial.Symbol is not IVariableItem variable) {
                    return FailMasking("mask definition is not flattened");
                }

                // **定义不能引用 mask 自己或结果列：那是一个循环，写成等式会直接不可行。**
                // **The definition must not reference the mask or the result: that is a cycle, infeasible as an equality.**
                if (variable.Key == structure.Mask.Key ||
                    variable.Key == structure.ResultVariable.Key) {
                    return FailMasking("mask definition references its own result");
                }

                double coefficient = ToSolverDouble(
                    structure.Converter.FromValue(monomial.Coefficient));
                double combined = (terms.TryGetValue(variable.Key, out double existing)
                    ? existing
                    : 0.0) + coefficient;

                if (!Usable(coefficient, maximumMagnitude) ||
                    !Usable(combined, maximumMagnitude)) {
                    return FailMasking("invalid mask definition coefficient");
                }

                terms[variable.Key] = combined;
            }

            definition = new NativeMaskDefinitionData(NonZero(terms), constant);
        }

        Result<NativeConditionalValueData, ErrorCode, Error<ErrorCode>> value =
            PrepareConditionalValue(structure.Value, structure.Converter, maximumMagnitude);
        if (value is Failed<NativeConditionalValueData, ErrorCode, Error<ErrorCode>> badValue) {
            return new Failed<NativeMaskingData, ErrorCode, Error<ErrorCode>>(
                badValue.Error.Code, badValue.Error.Message);
        }

        return new Ok<NativeMaskingData, ErrorCode, Error<ErrorCode>>(
            new NativeMaskingData(
                MaskKey: structure.Mask.Key,
                Value: ((Ok<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>)value).Value,
                Definition: definition));
    }

    private static double ToSolverDouble(Flt64 value) => value.ToSolverDouble();

    private static bool Usable(double value, double maximumMagnitude) =>
        double.IsFinite(value) &&
        global::System.Math.Abs(value) <
            global::System.Math.Min(maximumMagnitude, double.MaxValue);

    private static IReadOnlyDictionary<VariableItemKey, double> NonZero(
        Dictionary<VariableItemKey, double> terms) {
        var kept = new Dictionary<VariableItemKey, double>(terms.Count);
        foreach (KeyValuePair<VariableItemKey, double> term in terms) {
            if (term.Value != 0.0) {
                kept[term.Key] = term.Value;
            }
        }

        return kept;
    }

    private static Result<NativeConditionalValueData, ErrorCode, Error<ErrorCode>> Fail() =>
        new Failed<NativeConditionalValueData, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument,
            "invalid native conditional value bounds proof");

    private static Result<NativeMaskingData, ErrorCode, Error<ErrorCode>> FailMasking(
        string message) =>
        new Failed<NativeMaskingData, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, message);
}
