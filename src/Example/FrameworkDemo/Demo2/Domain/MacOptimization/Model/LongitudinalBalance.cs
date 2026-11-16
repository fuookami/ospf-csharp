#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Model;

/// <summary>
/// 纵向平衡松弛 / The longitudinal-balance slacks.
/// </summary>
/// <remarks>
/// **按装载模式注册不同的松弛集合**。
///
/// | 装载模式 | 注册哪些松弛 |
/// | --- | --- |
/// | 全载 | `A`、`B` 两点（**不含 `OPT` 与 `C`**） |
/// | 预配载 | 仅 `OPT` |
/// | 建议打板 | **一个都不注册** |
///
/// 三处必须沿用：
///
/// 1. **`OPT` 与 `C` 在全载下被排除**。它们不在 `MACRange` 的两侧，给它们松弛等于允许模型把最优点推走。
/// 2. **建议打板下是空集合**，不是"默认集合"。写成"没配置就全注册"会让建议打板多出两个没有系数的目标项。
/// 3. **符号名带类型后缀**：`longitudinal_balance_slack_A` / `_B` / `_opt`。`type.name`，`OPT` 在预配载下变成小写 `opt`——
///    这是不一致（`Type.OPT.name` 是 `"OPT"`，而它写死了 `"_opt"`）。
///
/// **which slacks register depends on the stowage mode**, tabulated above. Three things must be copied: **`OPT` and `C` are excluded under full load** —
/// they are not on either side of the `MACRange`, and giving them slack lets the model push the optimum away; **weight recommendation registers an empty set**, not "the default set",
/// since falling back to "register everything when unconfigured" would add two coefficient-less objectives there; and **the symbol names carry a type suffix**
/// (`longitudinal_balance_slack_A` / `_B` / `_opt`) — this implementation uses `type.name` but hard-codes `"_opt"` for `OPT`, so the suffix is lower-case only in the predistribution case.
/// That inconsistency is its and is copied.
/// </remarks>
public sealed class LongitudinalBalance
{
    private readonly MACRange _macRange;
    private readonly Torque _torque;

    /// <summary>
    /// 构造纵向平衡松弛 / Construct the longitudinal-balance slacks.
    /// </summary>
    /// <param name="macRange">MAC 范围 / The MAC range.</param>
    /// <param name="torque">扭矩模型 / The torque model.</param>
    public LongitudinalBalance(MACRange macRange, Torque torque)
    {
        ArgumentNullException.ThrowIfNull(macRange);
        ArgumentNullException.ThrowIfNull(torque);
        _macRange = macRange;
        _torque = torque;
    }

    /// <summary>纵向平衡松弛符号 / The longitudinal-balance slack symbols</summary>
    public IReadOnlyDictionary<MACRangeType, LinearExpressionSymbol>? Slack { get; private set; }

    /// <summary>MAC 范围 / The MAC range</summary>
    public MACRange MacRange => _macRange;

    /// <summary>
    /// 注册到模型 / Register with the model.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(StowageMode stowageMode, LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (Slack is null)
        {
            var slack = new Dictionary<MACRangeType, LinearExpressionSymbol>();
            switch (stowageMode)
            {
                case StowageMode.FullLoad:
                    // **`OPT` 与 `C` 排除**：它们不在两侧。
                    // **`OPT` and `C` excluded**: they are not on either side.
                    slack[MACRangeType.A] = MakeSlack("longitudinal_balance_slack_A");
                    slack[MACRangeType.B] = MakeSlack("longitudinal_balance_slack_B");
                    break;
                case StowageMode.Predistribution:
                    slack[MACRangeType.OPT] = MakeSlack("longitudinal_balance_slack_opt");
                    break;
                default:
                    // **建议打板下空集合**。
                    // **An empty set under weight recommendation**.
                    break;
            }
            Slack = slack;
        }

        foreach (LinearExpressionSymbol symbol in Slack.Values)
        {
            Try added = model.Add(symbol);
            if (!added.IsOk)
            {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 造一个松弛符号 / Build one slack symbol.
    /// </summary>
    /// <param name="name">符号名 / The symbol name.</param>
    /// <returns>松弛符号 / The slack symbol.</returns>
    private static LinearExpressionSymbol MakeSlack(string name) =>
        new(new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero), name: name);
}
