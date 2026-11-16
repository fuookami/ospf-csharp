// 冗余与纵向平衡的两条目标管线 / The two objective pipelines for redundancy and longitudinal balance.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy.Service.Limits;

/// <summary>
/// 最小化按系数加权的冗余松弛 / Minimise the coefficient-weighted redundancy slack.
/// </summary>
/// <remarks>
/// 对齐：`minimize(coefficient × redundancySlack)`。
/// 系数是**调用时求值**的（`coefficient:  -> Flt64`），因为参数对象在管线构建之后还会被改。
///
/// `minimize(coefficient × redundancySlack)`. The coefficient is **evaluated at invoke time** (the code takes `coefficient:  -> Flt64`) because the
/// parameter object keeps being updated after the pipeline list is built.
/// </remarks>
public sealed class RedundancyLimit
{
    private readonly Model.Redundancy _redundancy;
    private readonly Func<Flt64> _coefficient;

    /// <summary>
    /// 构造冗余目标 / Construct the redundancy objective.
    /// </summary>
    /// <param name="redundancy">冗余模型 / The redundancy model.</param>
    /// <param name="coefficient">权重系数 / The weight coefficient.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public RedundancyLimit(
        Model.Redundancy redundancy,
        Func<Flt64> coefficient,
        string name = "redundancy") {
        _redundancy = redundancy;
        _coefficient = coefficient;
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加目标 / Apply the objective.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> linear) {
        ArgumentNullException.ThrowIfNull(linear);
        if (_redundancy.RedundancySlackSymbol is not { } slack) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "冗余松弛未注册 / the redundancy slack is not registered"));
        }

        Flt64 coefficient = _coefficient();
        var monomials = new List<LinearMonomial<Flt64>>();
        foreach (LinearMonomial<Flt64> monomial in slack.Polynomial.Monomials) {
            monomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient.Times(coefficient), monomial.Symbol));
        }
        return linear.AddObject(
            ObjectCategory.Minimum,
            new SymbolFlatten.LinearFlattenData<Flt64>(monomials, slack.Polynomial.Constant.Times(coefficient)),
            "redundancy",
            displayName: null);
    }
}

/// <summary>
/// 最小化按系数加权的实验纵向平衡松弛 / Minimise the coefficient-weighted experimental longitudinal balance slack.
/// </summary>
/// <remarks>
/// 对齐。与冗余目标的形状完全一样，只是取的松弛不同——
/// 所以"两条管线其实是一条"这种错能编译、能跑，只是纵向平衡再也不被优化。
///
/// The shape is identical to the redundancy objective with a different slack, so collapsing the two into one compiles and runs — it just stops
/// optimising the longitudinal balance.
/// </remarks>
public sealed class ExperimentalLongitudinalBalanceLimit
{
    private readonly Model.ExperimentalLongitudinalBalance _balance;
    private readonly Func<Flt64> _coefficient;

    /// <summary>
    /// 构造纵向平衡目标 / Construct the longitudinal balance objective.
    /// </summary>
    /// <param name="balance">纵向平衡模型 / The longitudinal balance model.</param>
    /// <param name="coefficient">权重系数 / The weight coefficient.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public ExperimentalLongitudinalBalanceLimit(
        Model.ExperimentalLongitudinalBalance balance,
        Func<Flt64> coefficient,
        string name = "experimental_longitudinal_balance_limit") {
        _balance = balance;
        _coefficient = coefficient;
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加目标 / Apply the objective.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> linear) {
        ArgumentNullException.ThrowIfNull(linear);
        if (_balance.LongitudinalTorqueSlackSymbol is not { } slack) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "纵向平衡松弛未注册 / the longitudinal torque slack is not registered"));
        }

        Flt64 coefficient = _coefficient();
        var monomials = new List<LinearMonomial<Flt64>>();
        foreach (LinearMonomial<Flt64> monomial in slack.Polynomial.Monomials) {
            monomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient.Times(coefficient), monomial.Symbol));
        }
        return linear.AddObject(
            ObjectCategory.Minimum,
            new SymbolFlatten.LinearFlattenData<Flt64>(monomials, slack.Polynomial.Constant.Times(coefficient)),
            "experimental longitudinal balance",
            displayName: null);
    }
}
