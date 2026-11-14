#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Solver;

/// <summary>
/// 带原生变量边界贡献的线性 Benders 割构造。
/// Linear Benders cut construction including native variable-bound contributions.
/// </summary>
/// <remarks>
/// 割只使用调用方提供的当前主问题值和同一子问题的原始行对偶；不会翻转无效证书的符号。
/// Cuts use only the supplied current master values and original-row duals from the same
/// subproblem; invalid certificates are never repaired by reversing their signs.
/// </remarks>
public static class LinearBendersCuts {
    /// <summary>
    /// 构造锚定当前最优追索值的最优性割。
    /// Build an optimality cut anchored at the current optimal recourse value.
    /// </summary>
    /// <param name="theta">追索目标变量 / Recourse projection variable.</param>
    /// <param name="fixedVariables">当前主问题变量值 / Current master-variable values.</param>
    /// <param name="dual">原始约束对偶价格 / Original-row dual prices.</param>
    /// <param name="objective">子问题完整目标 / Complete subproblem objective.</param>
    /// <param name="value">当前最优追索值 / Current optimal recourse value.</param>
    /// <returns>最优性割或结构化错误 / Optimality cut or structured error.</returns>
    public static Result<LinearInequality<Flt64>, ErrorCode, Error<ErrorCode>> Optimal(
        IVariableItem theta,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        IReadOnlyDictionary<IConstraint<Flt64, LinearCategory>, Flt64> dual,
        LinearPolynomial<Flt64> objective,
        Flt64 value) {
        if (!IsFinite(value) ||
            fixedVariables.Values.Any(number => !IsFinite(number)) ||
            dual.Values.Any(number => !IsFinite(number)) ||
            objective.Monomials.Any(monomial => !IsFinite(monomial.Coefficient)) ||
            !IsFinite(objective.Constant)) {
            return Invalid("当前值、目标或对偶包含非有限数值 / Current values, objective, or duals are nonfinite");
        }

        var gradient = fixedVariables.Keys.ToDictionary(
            variable => variable,
            _ => Flt64.Zero
        );
        foreach (LinearMonomial<Flt64> monomial in objective.Monomials) {
            if (monomial.Symbol is IVariableItem variable &&
                gradient.ContainsKey(variable)) {
                gradient[variable] = gradient[variable] + monomial.Coefficient;
            }
        }

        foreach ((IConstraint<Flt64, LinearCategory> row, Flt64 price) in dual) {
            foreach (ICell<Flt64> cell in row.Lhs) {
                if (cell is not ILinearCell<Flt64> linear) {
                    return Invalid("对偶行包含非线性格 / Dual row contains a nonlinear cell");
                }
                IVariableItem variable = linear.Token.Variable;
                if (gradient.ContainsKey(variable)) {
                    gradient[variable] = gradient[variable] - price * linear.Coefficient;
                }
            }
        }

        Flt64 intercept = value;
        foreach ((IVariableItem variable, Flt64 coefficient) in gradient) {
            if (!IsFinite(coefficient)) {
                return Invalid("最优性割梯度溢出 / Optimality-cut gradient overflow");
            }
            intercept -= coefficient * fixedVariables[variable];
        }
        if (!IsFinite(intercept)) {
            return Invalid("最优性割截距溢出 / Optimality-cut intercept overflow");
        }

        var thetaPolynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, theta) },
            Flt64.Zero
        );
        var rhs = new LinearPolynomial<Flt64>(
            gradient
                .Where(pair => !pair.Value.Eq(Flt64.Zero))
                .Select(pair => new LinearMonomial<Flt64>(pair.Value, pair.Key))
                .ToList(),
            intercept
        );
        return Results.Ok(thetaPolynomial.Ge(rhs));
    }

    /// <summary>
    /// 从显式 Farkas 行乘子构造排除当前主问题点的可行性割。
    /// Build a feasibility cut separating the current master point from explicit Farkas row multipliers.
    /// </summary>
    /// <param name="fixedVariables">当前主问题变量值 / Current master-variable values.</param>
    /// <param name="dual">Farkas 行乘子 / Farkas row multipliers.</param>
    /// <returns>有效割或结构化错误 / Valid cut or structured error.</returns>
    public static Result<LinearInequality<Flt64>, ErrorCode, Error<ErrorCode>> Feasible(
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        IReadOnlyDictionary<IConstraint<Flt64, LinearCategory>, Flt64> dual) {
        if (fixedVariables.Values.Any(number => !IsFinite(number))) {
            return Invalid("主问题值包含非有限数值 / Master values are nonfinite");
        }

        var coefficients = new Dictionary<IVariableItem, Flt64>();
        Flt64 rhs = Flt64.Zero;
        foreach ((IConstraint<Flt64, LinearCategory> row, Flt64 price) in dual) {
            if (!IsFinite(price) ||
                (row.Sign == ConstraintRelation.LessEqual && price < Flt64.Zero) ||
                (row.Sign == ConstraintRelation.GreaterEqual && price > Flt64.Zero)) {
                return Invalid($"行符号或乘子无效 / Invalid row sign or multiplier: {row.Name}");
            }

            rhs += price * row.Rhs;
            foreach (ICell<Flt64> cell in row.Lhs) {
                if (cell is not ILinearCell<Flt64> linear) {
                    return Invalid($"对偶行包含非线性格 / Dual row contains a nonlinear cell: {row.Name}");
                }
                IVariableItem variable = linear.Token.Variable;
                coefficients[variable] = coefficients.TryGetValue(variable, out Flt64 coefficient)
                    ? coefficient + price * linear.Coefficient
                    : price * linear.Coefficient;
            }
        }

        Flt64 minimum = Flt64.Zero;
        foreach ((IVariableItem variable, Flt64 coefficient) in coefficients) {
            if (fixedVariables.ContainsKey(variable) || coefficient.Eq(Flt64.Zero)) {
                continue;
            }
            Flt64? bound = coefficient > Flt64.Zero
                ? variable.LowerBound?.Value.UnwrapOrNull()
                : variable.UpperBound?.Value.UnwrapOrNull();
            if (bound is null || !IsFinite(bound.Value)) {
                return Invalid($"缺少支撑变量边界 / Missing supporting bound: {variable.Name}");
            }
            minimum += coefficient * bound.Value;
        }

        var fixedCoefficients = coefficients
            .Where(pair => fixedVariables.ContainsKey(pair.Key) && !pair.Value.Eq(Flt64.Zero))
            .ToList();
        Flt64 current = minimum;
        foreach ((IVariableItem variable, Flt64 coefficient) in fixedCoefficients) {
            current += coefficient * fixedVariables[variable];
        }
        if (!IsFinite(minimum) || !IsFinite(rhs) || !IsFinite(current) || current <= rhs) {
            return Invalid("Farkas 证书不能分离当前主问题点 / Farkas certificate does not separate the incumbent");
        }

        var lhs = new LinearPolynomial<Flt64>(
            fixedCoefficients
                .Select(pair => new LinearMonomial<Flt64>(pair.Value, pair.Key))
                .ToList(),
            minimum
        );
        return Results.Ok(lhs.Le(rhs));
    }

    private static bool IsFinite(Flt64 value) => double.IsFinite(value.ToDouble());

    private static Result<LinearInequality<Flt64>, ErrorCode, Error<ErrorCode>> Invalid(string reason) =>
        Results.Failed<LinearInequality<Flt64>>(new Err<ErrorCode>(
            ErrorCode.OREngineSolvingException,
            $"无效 Benders 割 / Invalid Benders cut: {reason}"
        ));
}
