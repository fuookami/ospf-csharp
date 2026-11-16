#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service.Limits;

/// <summary>
/// 建议装载数量目标 / The advised-load-amount objective.
/// </summary>
/// <remarks>
/// 把**有建议数量**的舱位的数量松弛
/// 加权求和，作为**最小化**目标，名为 `"advice load amount"`（带空格）。
///
/// 四点必须沿用：
///
/// 1. **是 `Minimum`**。
/// 2. **目标名带空格**：`"advice load amount"`。
/// 3. **只收 `Ala` 非 `null` 的舱位**，且**一条都没有时不加目标**（`if (monomials.isEmpty) return ok`）。
///    注意这里"空"指**没有任何舱位带建议值**，不是"没有舱位"。
/// 4. **系数按舱位取**，不是按舱位下标。
///
/// The amount slacks of positions **carrying an advised amount** are weighted and summed into a **minimisation** named `"advice load amount"` (with a space).
/// Four things are carried over: **it is `Minimum`**; **the name has a space**; **only positions with a non-null `Ala` are collected**, and **an empty collection adds no objective** —
/// "empty" meaning no position carries an advice, not that there are no positions; and **the coefficient is taken per position**, not per index.
/// </remarks>
internal sealed class AdviceLoadAmountLimit
{
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly AdviceLoading _loading;
    private readonly Func<StowagePosition, Flt64> _coefficient;

    /// <summary>
    /// 构造建议数量目标 / Construct the advised-amount objective.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="loading">建议装载松弛 / The advised-load slack.</param>
    /// <param name="coefficient">按舱位的系数 / The per-position coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public AdviceLoadAmountLimit(
        IReadOnlyList<StowagePosition> positions,
        AdviceLoading loading,
        Func<StowagePosition, Flt64>? coefficient = null,
        string name = "advice_load_amount_limit")
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(loading);
        _positions = positions;
        _loading = loading;
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

        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        for (int j = 0; j < _positions.Count; ++j)
        {
            // **只收有建议数量的舱位**。
            // **Only positions carrying an advised amount.**
            if (_positions[j].Ala is null)
            {
                continue;
            }
            if (_loading.AmountSlack(j) is not { } slack)
            {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "数量松弛未注册 / the amount slack is not registered at " + j));
            }
            SameSourceAdjacentLimit.Accumulate(monomials, ref constant, slack, _coefficient(_positions[j]));
        }
        if (monomials.Count == 0)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        return model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(monomials, constant),
            "advice load amount",
            displayName: null);
    }
}
