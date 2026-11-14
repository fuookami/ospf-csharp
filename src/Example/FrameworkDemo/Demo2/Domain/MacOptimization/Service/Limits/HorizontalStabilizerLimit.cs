#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Service.Limits;

/// <summary>
/// 水平安定面警告松弛目标 / The horizontal-stabilizer warning-slack objectives.
/// </summary>
/// <remarks>
/// 与基线 `mac_optimization/service/limits/HorizontalStabilizerLimit.kt` 对齐：**每个安定面一个目标**（不是合并成一个），
/// 名称为 `"horizontal stabilizer {key}"`。
///
/// 四处必须照抄：
///
/// 1. **每个安定面单独一个目标**。这与 `LongitudinalBalanceLimit` 相反（那里是合并成一个），因为基线就是这么写的——
///    安定面的警告边界各自独立，合并会让模型用一个安定面的松弛去抵消另一个。
/// 2. **目标名带安定面键**。用固定名会让多个目标同名，`MetaSubObjects` 里就分不清哪个是哪个。
/// 3. **系数按安定面键取**：`coefficient(key)`。
/// 4. **任一安定面缺 `WarnSlack` 就明确失败**。恒零符号是基线在"上下界都没有"时的**正常**产物，而 `null` 说明根本没注册。
///
/// Aligned with the baseline: **one objective per stabilizer** (not merged), named `"horizontal stabilizer {key}"`. Four things must be copied: **one objective per stabilizer**, the
/// opposite of `LongitudinalBalanceLimit`'s merge, because the baseline does it this way — stabilizer warning boundaries are independent and merging would let one stabilizer's slack
/// offset another's; **the name carries the stabilizer key**, since a fixed name would make several objectives indistinguishable in `MetaSubObjects`; **the coefficient is taken per
/// key**; and **a stabilizer with no `WarnSlack` is an explicit failure** — a constant-zero symbol is the baseline's *normal* product when neither bound exists, whereas `null` means it
/// was never registered at all.
/// </remarks>
internal sealed class HorizontalStabilizerLimit
{
    private readonly IReadOnlyDictionary<HorizontalStabilizer.Key, HorizontalStabilizer> _horizontalStabilizers;
    private readonly Func<HorizontalStabilizer.Key, Flt64> _coefficient;

    /// <summary>
    /// 构造安定面目标 / Construct the stabilizer objectives.
    /// </summary>
    /// <param name="horizontalStabilizers">水平安定面 / The horizontal stabilizers.</param>
    /// <param name="coefficient">按安定面键的系数 / The per-key coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public HorizontalStabilizerLimit(
        IReadOnlyDictionary<HorizontalStabilizer.Key, HorizontalStabilizer> horizontalStabilizers,
        Func<HorizontalStabilizer.Key, Flt64>? coefficient = null,
        string name = "horizontal_stabilizer_limit")
    {
        ArgumentNullException.ThrowIfNull(horizontalStabilizers);
        _horizontalStabilizers = horizontalStabilizers;
        _coefficient = coefficient ?? (_ => Flt64.One);
        Name = name;
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加目标 / Apply the objectives.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        foreach (KeyValuePair<HorizontalStabilizer.Key, HorizontalStabilizer> entry in _horizontalStabilizers)
        {
            if (entry.Value.WarnSlack is not { } warnSlack)
            {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "安定面警告松弛未注册 / the stabilizer warning slack is not registered: " + entry.Key));
            }
            Flt64 coefficient = _coefficient(entry.Key);
            var monomials = new List<LinearMonomial<Flt64>>();
            Flt64 constant = Flt64.Zero;
            // `WarnSlack` 可能是函数符号（`LinearFunctionSymbolAdapter`）或普通符号（恒零分支），
            // 前者没有 `Polynomial` 属性，所以用 `IIntermediateSymbol` 的通用取值路径。
            // `WarnSlack` is either a function symbol (`LinearFunctionSymbolAdapter`) or a plain one (the constant-zero branch); the former has no `Polynomial`, so the generic
            // `IIntermediateSymbol` value path is used.
            if (warnSlack is Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol plain)
            {
                foreach (LinearMonomial<Flt64> monomial in plain.Polynomial.Monomials)
                {
                    monomials.Add(new LinearMonomial<Flt64>(coefficient * monomial.Coefficient, monomial.Symbol));
                }
                constant += coefficient * plain.Polynomial.Constant;
            }
            else
            {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "安定面警告松弛是函数符号，本仓尚不能把它展成线性目标 / the stabilizer warning slack is a function symbol this port cannot flatten into a linear objective: "
                        + entry.Key));
            }
            var objective = new LinearPolynomial<Flt64>(monomials, constant);
            Try added = model.AddObject(
                ObjectCategory.Minimum, objective, "horizontal stabilizer " + entry.Key, displayName: null);
            if (!added.IsOk)
            {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
