#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// &lt;summary>
/// MAX 的一个仿射输入 / An affine input to MAX.
/// &lt;/summary>
/// &lt;remarks>
/// 对齐的 `NativeMaxInputData`。
/// **`Terms` 是按变量键合并后的非零系数。**
///
/// **Following its `NativeMaxInputData`.** **`Terms` holds nonzero coefficients combined by variable
/// key.**
/// &lt;/remarks>
/// &lt;param name="Terms">按变量键合并的系数 / Coefficients combined by variable key.&lt;/param>
/// &lt;param name="Constant">常数项 / Constant term.&lt;/param>
/// &lt;param name="LowerBound">当前输入下界 / Current input lower bound.&lt;/param>
/// &lt;param name="UpperBound">当前输入上界 / Current input upper bound.&lt;/param>
public sealed record NativeMaxInputData(
    IReadOnlyDictionary<VariableItemKey, double> Terms,
    double Constant,
    double LowerBound,
    double UpperBound);

/// &lt;summary>
/// 已校验的 MAX 原语数据 / Validated MAX primitive data.
/// &lt;/summary>
/// &lt;remarks>按的 `NativeMaxData` 补。 / Adds its `NativeMaxData`.&lt;/remarks>
public sealed record NativeMaxData(
    IReadOnlyList<NativeMaxInputData> Inputs,
    VariableItemKey ResultKey,
    IReadOnlyList<VariableItemKey> SelectorKeys,
    IReadOnlyList<double> BigMValues,
    string Name,
    bool Minimum = false);

/// &lt;summary>
/// 校验 MAX 数据及原 Big-M 对输入域的覆盖 / Validate MAX data and the original Big-Ms' coverage of the input domain.
/// &lt;/summary>
/// &lt;remarks>
/// 对齐的 `prepareNativeMax`。
///
/// **这一层最要紧的一条是 Big-M 的<b>覆盖</b>检查：**
///
/// ```
/// required = max(所有输入的 upperBound) - 本输入的 lowerBound
/// ```
///
/// **也就是"本输入可以比全局最大值低多少"。**
/// **未选中的那支要求 `result &lt;= input + M`，而 `result` 至少要够到全局最大值 `maxUpper`，
/// 所以 `M` 必须补上 `maxUpper - lower` 这段距离。**
///
/// **`bigM &lt; required` 即拒——所以 `bigM == required` <b>刚好</b>通过。**
/// **这个边界与 `NativeAbs` 的严格上界不同，方向也不一样：那里"恰好等于上限"没有余量，
/// 这里"恰好等于需求量"是够用的**（**没有别的项会再吃掉这段余量**）。
///
/// **Big-M 还必须 `> 0`**：**`0` 在这里没有意义——一个恒定的输入，其 `required` 为
/// `maxUpper - lower`，只在自己就是最大值时才是 0，而那时它不需要 fallback。**
///
/// **输入里<b>不能出现自己的结果列或选择列</b>：** **一个"最大值函数引用了自己的结果"的结构
/// 会写出 `result >= result` 这样的行，形式上满足、什么都不约束。**
///
/// **同一个变量的多个单项会被<b>合并</b>，然后<b>丢掉零系数项</b>。**
/// **所以 `x - x` 合并成零之后既不进 `Terms`，也不参与上下界的累加——
/// 它不会因为 `x` 缺界而报错，因为它根本不贡献。**
///
/// **The key check here is Big-M <b>coverage</b>:** `required = max(upper bounds) - this input's lower bound`, i.e. "how far
/// below the global maximum this input can be".
///
/// **An unselected branch requires `result &lt;= input + M`, and `result` must reach the global maximum `maxUpper`, so `M`
/// must cover the distance `maxUpper - lower`.**
///
/// **`bigM &lt; required` is rejected, so `bigM == required` passes <b>exactly</b>.** **This boundary differs from
/// `NativeAbs`'s strict upper bound and points the other way: there "exactly at the limit" has no headroom, here "exactly
/// the required amount" is sufficient** (**nothing else eats into it**).
///
/// **Big-Ms must also be `> 0`**: **zero is meaningless here — a constant input's `required` is `maxUpper - lower`, which
/// is zero only when it is itself the maximum, and then it needs no fallback.**
///
/// **An input must not reference its own result or selector columns**: **a max-function referencing its own result writes
/// rows like `result >= result`, formally satisfied and constraining nothing.**
///
/// **Repeated monomials of one variable are <b>combined</b> and then <b>zero coefficients are dropped</b>.** **So `x - x`
/// becomes zero, enters neither `Terms` nor the bound accumulation, and cannot fail for `x` lacking bounds because it
/// contributes nothing.**
/// &lt;/remarks>
public static class NativeMax
{
    /// &lt;summary>校验并转换 / Validate and convert.&lt;/summary>
    public static Result<NativeMaxData, ErrorCode, Error<ErrorCode>> Prepare(
        MaxStructure structure,
        double maximumMagnitude = double.PositiveInfinity) {
        ArgumentNullException.ThrowIfNull(structure);

        if (double.IsNaN(maximumMagnitude) || maximumMagnitude <= 0.0) {
            return Invalid("maximum magnitude must be positive");
        }

        int count = structure.Inputs.Count;
        if (count == 0 || structure.SelectorVariables.Count != count ||
            structure.BigMValues.Count != count) {
            return Invalid("input, selector, and Big-M counts must match and be nonempty");
        }

        var selectorKeys = new List<VariableItemKey>(structure.SelectorVariables.Count);
        foreach (IVariableItem selector in structure.SelectorVariables) {
            selectorKeys.Add(selector.Key);
        }

        var declarationKeys = new List<VariableItemKey>(selectorKeys) {
            structure.ResultVariable.Key,
        };

        if (new HashSet<VariableItemKey>(declarationKeys).Count != declarationKeys.Count) {
            return Invalid("result and selector keys must be distinct");
        }

        var declared = new HashSet<VariableItemKey>(declarationKeys);

        try {
            var inputs = new List<NativeMaxInputData>(count);

            for (int index = 0; index < structure.Inputs.Count; ++index) {
                LinearPolynomial<Flt64> polynomial = structure.Inputs[index];
                double constant =
                    structure.Converter.FromValue(polynomial.Constant).ToSolverDouble();

                if (!Usable(constant, maximumMagnitude)) {
                    return Invalid("input constant is not finite or representable");
                }

                var terms = new Dictionary<VariableItemKey, double>();
                var variables = new Dictionary<VariableItemKey, IVariableItem>();

                foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial
                    in polynomial.Monomials) {
                    if (monomial.Symbol is not IVariableItem variable) {
                        return Invalid("input contains an unexpanded symbol");
                    }

                    if (declared.Contains(variable.Key)) {
                        return Invalid("input references its result or selector");
                    }

                    double coefficient =
                        structure.Converter.FromValue(monomial.Coefficient).ToSolverDouble();
                    double existing = terms.TryGetValue(variable.Key, out double prior) ? prior : 0.0;
                    double combined = existing + coefficient;

                    if (!Usable(coefficient, maximumMagnitude) ||
                        !Usable(combined, maximumMagnitude)) {
                        return Invalid("input coefficient is not finite or representable");
                    }

                    terms[variable.Key] = combined;
                    variables[variable.Key] = variable;
                }

                // **丢掉合并后为零的项：它不贡献任何东西，也就不该要求它有界。**
                // **Drop combined-zero terms: they contribute nothing and must not be required to have bounds.**
                var nonzero = new Dictionary<VariableItemKey, double>();
                foreach (KeyValuePair<VariableItemKey, double> pair in terms) {
                    if (pair.Value != 0.0) {
                        nonzero[pair.Key] = pair.Value;
                    }
                }

                double lowerBound = constant;
                double upperBound = constant;

                foreach (KeyValuePair<VariableItemKey, double> pair in nonzero) {
                    IVariableItem variable = variables[pair.Key];
                    double coefficient = pair.Value;

                    if (variable.LowerBound is null || variable.UpperBound is null) {
                        return Invalid("input variable bounds are missing");
                    }

                    double lower = variable.LowerBound.Value.ToFlt64().ToSolverDouble();
                    double upper = variable.UpperBound.Value.ToFlt64().ToSolverDouble();

                    if (!Usable(lower, maximumMagnitude) || !Usable(upper, maximumMagnitude) ||
                        lower > upper) {
                        return Invalid("input variable bounds are invalid");
                    }

                    // **系数为正时下界取变量的下界、上界取变量的上界；为负时反过来。**
                    // **A positive coefficient takes the variable's lower bound for the minimum and its upper for the
                    // maximum; a negative one swaps them.**
                    lowerBound += coefficient * (coefficient >= 0.0 ? lower : upper);
                    upperBound += coefficient * (coefficient >= 0.0 ? upper : lower);

                    if (!Usable(lowerBound, maximumMagnitude) ||
                        !Usable(upperBound, maximumMagnitude)) {
                        return Invalid("affine input bounds are not finite or representable");
                    }
                }

                inputs.Add(new NativeMaxInputData(
                    Terms: nonzero,
                    Constant: constant,
                    LowerBound: lowerBound,
                    UpperBound: upperBound));
            }

            double maximumUpper = double.NegativeInfinity;
            foreach (NativeMaxInputData input in inputs) {
                if (input.UpperBound > maximumUpper) {
                    maximumUpper = input.UpperBound;
                }
            }

            double[] bigMValues = new double[structure.BigMValues.Count];
            for (int index = 0; index < bigMValues.Length; ++index) {
                bigMValues[index] =
                    structure.Converter.FromValue(structure.BigMValues[index]).ToSolverDouble();
            }

            for (int index = 0; index < bigMValues.Length; ++index) {
                double bigM = bigMValues[index];
                double required = maximumUpper - inputs[index].LowerBound;

                // **`bigM < required` 才拒：恰好等于需求量是够用的。**
                // **Only `bigM < required` is rejected: exactly the required amount suffices.**
                if (!Usable(bigM, maximumMagnitude) || bigM <= 0.0 ||
                    !double.IsFinite(required) || bigM < required) {
                    return Invalid("fallback Big-M does not cover the current input domain");
                }
            }

            return new Ok<NativeMaxData, ErrorCode, Error<ErrorCode>>(
                new NativeMaxData(
                    Inputs: inputs,
                    ResultKey: structure.ResultVariable.Key,
                    SelectorKeys: selectorKeys,
                    BigMValues: bigMValues,
                    Name: structure.Name,
                    Minimum: structure.Minimum));
        }
        catch (Exception error) {
            return Invalid($"numeric conversion failed: {error.Message}");
        }
    }

    private static bool Usable(double value, double maximumMagnitude) =>
        double.IsFinite(value) && global::System.Math.Abs(value) < maximumMagnitude;

    private static Result<NativeMaxData, ErrorCode, Error<ErrorCode>> Invalid(string reason) =>
        new Failed<NativeMaxData, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, $"invalid native MAX data: {reason}");
}
