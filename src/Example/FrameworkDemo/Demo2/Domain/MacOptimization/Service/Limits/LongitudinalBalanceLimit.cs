#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Service.Limits;

/// <summary>
/// 纵向平衡目标 / The longitudinal-balance objective.
/// </summary>
/// <remarks>
/// 与基线 `mac_optimization/service/limits/LongitudinalBalanceLimit.kt` 对齐：把**所有已注册的松弛按各自系数加权求和**，
/// 作为**一个**最小化目标，名为 `longitudinal balance`（带空格，与 `Name` 属性 `longitudinal_balance_limit` **不同**）。
///
/// 四处必须照抄：
///
/// 1. **一个目标，不是每个松弛一个**。逐条 `AddObject` 会变成多目标优化，而求解器只认第一个。
/// 2. **目标名带空格**：`"longitudinal balance"`。基线的 `name` 属性是下划线式但 `minimize` 传的是空格式，两者都被使用。
/// 3. **系数按类型逐项取**：`C` 点用 `macRangeC`，用 ` LongitudinalBalance`，其余用 `longitudinalBalance`。
/// 4. **空松弛集合仍然注册一个零目标**。基线不判空——`sum(emptyList)` 是零多项式，`minimize` 照样加一项。
///
/// Aligned with the baseline: **all registered slacks weighted by their coefficients are summed** into **one** minimisation objective named `longitudinal balance` (with a space, which
/// differs from the `Name` property `longitudinal_balance_limit`). Four things must be copied: **one objective, not one per slack** — adding one each turns it into multi-objective
/// optimisation while the solver only honours the first; **the objective name has a space**, since the baseline's `name` property is underscore-style but the `minimize` call passes
/// the spaced form and both are used; **the coefficient is taken per type** — `C` uses `macRangeC`, uses ` LongitudinalBalance`, everything else `longitudinalBalance`; and
/// **an empty slack set still registers a zero objective**, because the baseline does not test for emptiness — `sum(emptyList)` is the zero polynomial and `minimize` adds it anyway.
/// </remarks>
internal sealed class LongitudinalBalanceLimit
{
    private readonly LongitudinalBalance _longitudinalBalance;
    private readonly Func<MACRangeType, Flt64> _coefficient;

    /// <summary>
    /// 构造纵向平衡目标 / Construct the longitudinal-balance objective.
    /// </summary>
    /// <param name="longitudinalBalance">纵向平衡 / The longitudinal balance.</param>
    /// <param name="coefficient">按类型的系数 / The per-type coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public LongitudinalBalanceLimit(
        LongitudinalBalance longitudinalBalance,
        Func<MACRangeType, Flt64>? coefficient = null,
        string name = "longitudinal_balance_limit")
    {
        ArgumentNullException.ThrowIfNull(longitudinalBalance);
        _longitudinalBalance = longitudinalBalance;
        _coefficient = coefficient ?? (_ => Flt64.One);
        Name = name;
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加目标 / Apply the objective.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_longitudinalBalance.Slack is not { } slack)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "纵向平衡松弛未注册 / the longitudinal-balance slacks are not registered"));
        }

        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (KeyValuePair<MACRangeType, Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol> entry in slack)
        {
            Flt64 coefficient = _coefficient(entry.Key);
            foreach (LinearMonomial<Flt64> monomial in entry.Value.Polynomial.Monomials)
            {
                monomials.Add(new LinearMonomial<Flt64>(coefficient * monomial.Coefficient, monomial.Symbol));
            }
            constant += coefficient * entry.Value.Polynomial.Constant;
        }
        var objective = new LinearPolynomial<Flt64>(monomials, constant);
        return model.AddObject(ObjectCategory.Minimum, objective, "longitudinal balance", displayName: null);
    }
}
