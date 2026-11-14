#nullable enable

using System;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Model;

/// <summary>
/// 横向平衡松弛 / The lateral-balance slack.
/// </summary>
/// <remarks>
/// 与基线 `mac_optimization/model/LateralBalance.kt` 对齐：**一个恒等符号**，名为 `lateral_balance_slack`，注册进模型后由
/// `LateralBalanceLimit` 作为最小化目标。
///
/// 三处必须照抄：
///
/// 1. **符号的初值是 0，不是变量**。基线 `LinearExpressionSymbol(Flt64, name = …)` 造的是"零多项式 + 名字"，
///    符号本身由 `model.add` 变成变量。手写成 `new RealVar(...)` 会多一个基线没有的界。
/// 2. **只注册一次**。基线用 `if (!::slack.isInitialized)`；重复注册会往模型里塞两个同名符号。
/// 3. **注册失败要原样返回**，不能吞掉。上游 `Aggregation.Register` 靠这个短路。
///
/// Aligned with the baseline: **one identity symbol** named `lateral_balance_slack`, registered into the model and later minimised by `LateralBalanceLimit`. Three things must be
/// copied: **the symbol's initial value is zero, not a variable** — the baseline builds "zero polynomial plus a name" and `model.add` turns it into a variable, so hand-writing a
/// `RealVar` would add a bound the baseline does not have; **register once**, as the baseline's `if (!::slack.isInitialized)` does, since registering twice puts two same-named symbols
/// in the model; and **a failed registration is returned as-is**, never swallowed, because the upstream `Aggregation.Register` short-circuits on it.
/// </remarks>
public sealed class LateralBalance
{
    private readonly Torque _torque;

    /// <summary>
    /// 构造横向平衡松弛 / Construct the lateral-balance slack.
    /// </summary>
    /// <param name="torque">扭矩模型 / The torque model.</param>
    public LateralBalance(Torque torque)
    {
        ArgumentNullException.ThrowIfNull(torque);
        _torque = torque;
    }

    /// <summary>横向平衡松弛符号 / The lateral-balance slack symbol</summary>
    public LinearExpressionSymbol? Slack { get; private set; }

    /// <summary>扭矩模型 / The torque model</summary>
    public Torque Torque => _torque;

    /// <summary>
    /// 注册到模型 / Register with the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (Slack is null)
        {
            Slack = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                name: "lateral_balance_slack");
        }
        return model.Add(Slack);
    }
}
