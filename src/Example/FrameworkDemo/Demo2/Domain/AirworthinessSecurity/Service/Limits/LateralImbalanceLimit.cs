// 横向不平衡对称限制 / The symmetric lateral imbalance constraint.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;

/// <summary>
/// 用对称上下界约束横向力矩 / Constrain lateral torque with symmetric upper and lower bounds.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `service/limits/LateralImbalanceLimit.kt`：两行，`lateral_torque ≤ max`（名字 `{name}_maximum`）与
/// `−lateral_torque ≤ max`（名字 `{name}_minimum`）。**下限写成对负数取上限**，不是 `lateral_torque ≥ −max`；
/// 两种写法代数等价，但符号排列与报告里的系数不同，照抄基线的形状。
///
/// 两处容易做错：
///
/// 1. **`maximum` 在前、`minimum` 在后**（与 `LongitudinalMomentLimit` 一致，与 `PayloadLimit` 相反）。
/// 2. **`−lateral_torque` 是把整个多项式的系数取反**，常数项也要反。只翻系数、不翻常数会让带常数项的横向力矩偏一个常数。
///
/// Mirrors the baseline: two rows, `lateral_torque ≤ max` (named `{name}_maximum`) and `−lateral_torque ≤ max` (named `{name}_minimum`). **The lower bound is written as an upper
/// bound on the negation**, not as `lateral_torque ≥ −max`; the two are algebraically equal but the monomial layout and the reported coefficients differ, so the baseline's shape is
/// copied. Two easy mistakes: (1) **`maximum` first, `minimum` second** (matching `LongitudinalMomentLimit` and opposite to `PayloadLimit`); (2) **`−lateral_torque` negates the
/// whole polynomial**, constant term included — flipping only the coefficients leaves a constant offset.
/// </remarks>
internal sealed class LateralImbalanceLimit
{
    private readonly Torque _torque;
    private readonly double _maxLateralImbalance;

    /// <summary>
    /// 构造横向不平衡限制 / Construct the lateral imbalance constraint.
    /// </summary>
    /// <param name="torque">横向力矩模型 / The lateral torque model.</param>
    /// <param name="maxLateralImbalance">最大允许绝对横向力矩 / The maximum allowed absolute lateral torque.</param>
    /// <param name="name">约束名前缀 / The constraint name prefix.</param>
    public LateralImbalanceLimit(Torque torque, double maxLateralImbalance, string name = "lateral_imbalance_limit") {
        ArgumentNullException.ThrowIfNull(torque);
        _torque = torque;
        _maxLateralImbalance = maxLateralImbalance;
        Name = name;
    }

    /// <summary>约束名前缀 / The constraint name prefix</summary>
    public string Name { get; }

    /// <summary>上一次执行加上的约束名 / The constraint names added by the last invocation</summary>
    public IReadOnlyList<string> LastAddedNames { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// 执行限制 / Invoke the limit.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_torque.LateralTorqueSymbol is not { } lateral) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "横向力矩符号未注册 / the lateral torque symbol is not registered"));
        }

        string maximumName = Name + "_maximum";
        Try upper = Add(model, lateral.Polynomial, _maxLateralImbalance, maximumName);
        if (!upper.IsOk) {
            return upper;
        }
        // `-lateral_torque` 用多项式的一元取负：系数与**常数项**一起翻。只翻系数会让带常数项的横向力矩偏一个常数。
        // `-lateral_torque` uses the polynomial's unary negation: coefficients **and the constant** flip together. Flipping only the coefficients leaves an offset.
        string minimumName = Name + "_minimum";
        Try lower = Add(model, -lateral.Polynomial, _maxLateralImbalance, minimumName);
        if (!lower.IsOk) {
            return lower;
        }
        LastAddedNames = new[] { maximumName, minimumName };
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private static Try Add(
        LinearMetaModel<Flt64> model,
        LinearPolynomial<Flt64> polynomial,
        double bound,
        string name) {
        var row = new LinearInequality<Flt64>(
            new LinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant),
            new LinearPolynomial<Flt64>(new LinearMonomial<Flt64>[0], new Flt64(bound)),
            Comparison.LE,
            name);
        return model.AddConstraint(row, group: null, name: name);
    }
}
