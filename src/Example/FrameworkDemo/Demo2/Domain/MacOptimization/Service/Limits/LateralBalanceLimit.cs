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
/// 横向平衡目标 / The lateral-balance objective.
/// </summary>
/// <remarks>
/// 与基线 `mac_optimization/service/limits/LateralBalanceLimit.kt` 对齐：把横向松弛乘系数后作为**最小化**目标，名为 `lateral balance`。
///
/// 三处必须照抄：
///
/// 1. **目标是 `Minimum` 类别**，且**只对宽体机存在**（生成器负责跳过）。
/// 2. **目标名带空格**：`"lateral balance"`，与 `Name` 属性 `lateral_balance_limit` 不同。
/// 3. **系数同时乘单项式与常数**。基线写 `LinearPolynomial(coefficient * slack.value)`，即整个多项式乘系数。
///
/// Aligned with the baseline: the lateral slack scaled by a coefficient becomes a **minimisation** objective named `lateral balance`. Three things must be copied: **the objective is
/// `Minimum` and exists only for wide-body aircraft** (the generator skips it otherwise); **the objective name has a space**, unlike the `Name` property `lateral_balance_limit`; and
/// **the coefficient scales both the monomials and the constant**, since the baseline writes `LinearPolynomial(coefficient * slack.value)`, multiplying the whole polynomial.
/// </remarks>
internal sealed class LateralBalanceLimit
{
    private readonly LateralBalance _lateralBalance;
    private readonly Func<Flt64> _coefficient;

    /// <summary>
    /// 构造横向平衡目标 / Construct the lateral-balance objective.
    /// </summary>
    /// <param name="lateralBalance">横向平衡 / The lateral balance.</param>
    /// <param name="coefficient">系数 / The coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public LateralBalanceLimit(
        LateralBalance lateralBalance,
        Func<Flt64>? coefficient = null,
        string name = "lateral_balance_limit")
    {
        ArgumentNullException.ThrowIfNull(lateralBalance);
        _lateralBalance = lateralBalance;
        _coefficient = coefficient ?? (() => Flt64.One);
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
        if (_lateralBalance.Slack is not { } slack)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "横向平衡松弛未注册 / the lateral-balance slack is not registered"));
        }

        Flt64 coefficient = _coefficient();
        var monomials = new List<LinearMonomial<Flt64>>(slack.Polynomial.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in slack.Polynomial.Monomials)
        {
            monomials.Add(new LinearMonomial<Flt64>(coefficient * monomial.Coefficient, monomial.Symbol));
        }
        var objective = new LinearPolynomial<Flt64>(monomials, coefficient * slack.Polynomial.Constant);
        return model.AddObject(ObjectCategory.Minimum, objective, "lateral balance", displayName: null);
    }
}
