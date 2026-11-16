// 压舱物重量下限 / The lower bound on ballast weight.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;

/// <summary>
/// 压舱物重量下限 / The lower bound on ballast weight.
/// </summary>
/// <remarks>
/// 逐条对齐：一行 `ballast_weight ≥ min_ballast_weight`，名字固定为 `ballast_weight_limit`
/// （**不**带 `name` 前缀——基线写死了字符串）。
///
/// 三处容易做错：
///
/// 1. **两个前提都满足才加行**：没有可用压舱舱位，或没有最低压舱重量，都直接返回成功。这是合法配置——不装压舱物的航班不该有这条约束。
/// 2. **名字写死**。看起来应当用 `{name}`，但的是字面量 `"ballast_weight_limit"`；改名会让既有报告对不上。
/// 3. **下限用的是 `AdaptiveMinBallastWeightSymbol`**，不是 `MinBallastWeight` 那个裸数字。前者在软安保下已注册进模型，后者只是构造参数。
///
/// one row `ballast_weight ≥ min_ballast_weight`, named the literal `ballast_weight_limit` (**not** prefixed with `name` — the code hard-codes it). Three
/// easy mistakes: (1) **both preconditions must hold** — no available ballast position or no minimum ballast weight both return success, because a flight that carries no ballast
/// should not have this constraint; (2) **the name is hard-coded**; it looks like it should use `{name}`, but the code uses the literal, and renaming breaks existing reports;
/// (3) **the lower bound is `AdaptiveMinBallastWeightSymbol`**, not the raw `MinBallastWeight` number — the former is registered in the model under soft security, the latter is just
/// a constructor argument.
/// </remarks>
internal sealed class BallastWeightLimit
{
    private readonly Ballast _ballast;

    /// <summary>
    /// 构造压舱重量下限 / Construct the ballast weight lower bound.
    /// </summary>
    /// <param name="ballast">压舱物 / The ballast.</param>
    public BallastWeightLimit(Ballast ballast) {
        _ballast = ballast;
    }

    /// <summary>上一次执行加上的约束名 / The constraint names added by the last invocation</summary>
    public IReadOnlyList<string> LastAddedNames { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// 执行限制 / Invoke the limit.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);

        bool anyAvailable = false;
        foreach (StowagePosition position in _ballast.BallastPositions) {
            if (position.Status.Available) {
                anyAvailable = true;
                break;
            }
        }
        if (!anyAvailable || _ballast.MinBallastWeight is null) {
            LastAddedNames = Array.Empty<string>();
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (_ballast.BallastWeightSymbol is not { } weight) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "压舱重量符号未注册 / the ballast weight symbol is not registered"));
        }
        if (_ballast.AdaptiveMinBallastWeightSymbol is not { } minimum) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "最低压舱重量符号未注册 / the adaptive minimum ballast weight symbol is not registered"));
        }

        const string name = "ballast_weight_limit";
        var row = new LinearInequality<Flt64>(
            new LinearPolynomial<Flt64>(weight.Polynomial.Monomials, weight.Polynomial.Constant),
            new LinearPolynomial<Flt64>(minimum.Polynomial.Monomials, minimum.Polynomial.Constant),
            Comparison.GE,
            name);
        Try result = model.AddConstraint(row, group: null, name: name);
        if (!result.IsOk) {
            return result;
        }
        LastAddedNames = new[] { name };
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
