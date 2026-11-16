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
/// 同目的地相邻目标 / The same-destination-adjacency objective.
/// </summary>
/// <remarks>
/// 把（目的地 × 相邻舱位对）的指示
/// 加权求和，作为**最大化**目标，名为 `"same destination adjacent"`（带空格）。
///
/// 四点必须沿用：
///
/// 1. **是 `Maximum`**。
/// 2. **目标名带空格**：`"same destination adjacent"`。管线类名是 `SameDestinationAdjacent`（**没有** `Limit`
///    后缀），但目标名与同来源那条**结构完全对称**，写成 `same_destination_adjacent` 会与管线名混淆。
/// 3. **两重遍历**：外层目的地、内层相邻舱位对，**每个组合都进目标**。
/// 4. **系数按 `(目的地, 第一个舱位, 第二个舱位)` 取**。
///
/// The (destination × adjacent pair) indicators weighted and summed into a **maximisation** named `"same destination adjacent"` (with a space). Four things must be
/// copied: **it is `Maximum`**; **the name has a space** — its class is `SameDestinationAdjacent` (**without** a `Limit` suffix), but the objective name is structurally symmetric
/// with the same-source one, and `same_destination_adjacent` would be confused with the pipeline name; **there are two nested loops** with **every combination entering the objective**;
/// and **the coefficient is taken per (destination, first position, second position)**.
/// </remarks>
internal sealed class SameDestinationAdjacent
{
    private readonly IReadOnlyList<(StowagePosition First, StowagePosition Second)> _adjacentPositions;
    private readonly IReadOnlyList<string> _destinations;
    private readonly TransferAdjacentLoading _loading;
    private readonly Func<string, StowagePosition, StowagePosition, Flt64> _coefficient;

    /// <summary>
    /// 构造同目的地相邻目标 / Construct the same-destination-adjacency objective.
    /// </summary>
    /// <param name="adjacentPositions">相邻舱位对 / The adjacent position pairs.</param>
    /// <param name="destinations">目的地集合 / The destination set.</param>
    /// <param name="loading">相邻装载指示 / The adjacency indicators.</param>
    /// <param name="coefficient">按目的地与两舱位的系数 / The per-destination, per-pair coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public SameDestinationAdjacent(
        IReadOnlyList<(StowagePosition First, StowagePosition Second)> adjacentPositions,
        IReadOnlyList<string> destinations,
        TransferAdjacentLoading loading,
        Func<string, StowagePosition, StowagePosition, Flt64>? coefficient = null,
        string name = "same_destination_adjacent_limit")
    {
        ArgumentNullException.ThrowIfNull(adjacentPositions);
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentNullException.ThrowIfNull(loading);
        _adjacentPositions = adjacentPositions;
        _destinations = destinations;
        _loading = loading;
        _coefficient = coefficient ?? ((_, _, _) => Flt64.One);
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
        for (int d = 0; d < _destinations.Count; ++d)
        {
            string destination = _destinations[d];
            for (int p = 0; p < _adjacentPositions.Count; ++p)
            {
                (StowagePosition position1, StowagePosition position2) = _adjacentPositions[p];
                if (_loading.SameDestination(d, p) is not { } indicator)
                {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "同目的地指示未注册 / the same-destination indicator is not registered at " + d + "," + p));
                }
                SameSourceAdjacentLimit.Accumulate(
                    monomials, ref constant, indicator, _coefficient(destination, position1, position2));
            }
        }
        return model.AddObject(
            ObjectCategory.Maximum,
            new LinearPolynomial<Flt64>(monomials, constant),
            "same destination adjacent",
            displayName: null);
    }
}
