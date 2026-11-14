#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
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
/// 同来源相邻目标 / The same-source-adjacency objective.
/// </summary>
/// <remarks>
/// 与基线 `loading_effectiveness/service/limits/SameSourceAdjacentLimit.kt` 对齐：把（来源 × 相邻舱位对）的指示
/// 加权求和，作为**最大化**目标，名为 `"same source adjacent"`（带空格）。
///
/// 四点必须照抄：
///
/// 1. **是 `Maximum`**——相邻同来源是好事，不是惩罚。
/// 2. **目标名带空格**：`"same source adjacent"`，与管线名 `same_source_adjacent_limit` 不同。
/// 3. **两重遍历**：外层来源、内层相邻舱位对，**每个组合都进目标**。
/// 4. **系数按 `(来源, 第一个舱位, 第二个舱位)` 取**，不是按货位下标。
///
/// Aligned with the baseline: the (source × adjacent pair) indicators weighted and summed into a **maximisation** named `"same source adjacent"` (with a space). Four things must be copied:
/// **it is `Maximum`**, since same-source adjacency is desirable rather than penalised; **the name has a space**, unlike the pipeline name; **there are two nested loops** — sources
/// outside, adjacent pairs inside, with **every combination entering the objective**; and **the coefficient is taken per (source, first position, second position)**, not per index.
/// </remarks>
internal sealed class SameSourceAdjacentLimit
{
    private readonly IReadOnlyList<(StowagePosition First, StowagePosition Second)> _adjacentPositions;
    private readonly IReadOnlyList<string> _sources;
    private readonly TransferAdjacentLoading _loading;
    private readonly Func<string, StowagePosition, StowagePosition, Flt64> _coefficient;

    /// <summary>
    /// 构造同来源相邻目标 / Construct the same-source-adjacency objective.
    /// </summary>
    /// <param name="adjacentPositions">相邻舱位对 / The adjacent position pairs.</param>
    /// <param name="sources">来源集合 / The source set.</param>
    /// <param name="loading">相邻装载指示 / The adjacency indicators.</param>
    /// <param name="coefficient">按来源与两舱位的系数 / The per-source, per-pair coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public SameSourceAdjacentLimit(
        IReadOnlyList<(StowagePosition First, StowagePosition Second)> adjacentPositions,
        IReadOnlyList<string> sources,
        TransferAdjacentLoading loading,
        Func<string, StowagePosition, StowagePosition, Flt64>? coefficient = null,
        string name = "same_source_adjacent_limit")
    {
        ArgumentNullException.ThrowIfNull(adjacentPositions);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(loading);
        _adjacentPositions = adjacentPositions;
        _sources = sources;
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
        for (int s = 0; s < _sources.Count; ++s)
        {
            string source = _sources[s];
            for (int p = 0; p < _adjacentPositions.Count; ++p)
            {
                (StowagePosition position1, StowagePosition position2) = _adjacentPositions[p];
                if (_loading.SameSource(s, p) is not { } indicator)
                {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "同来源指示未注册 / the same-source indicator is not registered at " + s + "," + p));
                }
                Accumulate(monomials, ref constant, indicator, _coefficient(source, position1, position2));
            }
        }
        return model.AddObject(
            ObjectCategory.Maximum,
            new LinearPolynomial<Flt64>(monomials, constant),
            "same source adjacent",
            displayName: null);
    }

    /// <summary>
    /// 把一个符号按系数累加进目标 / Accumulate one symbol into the objective at a coefficient.
    /// </summary>
    /// <param name="monomials">单项式表 / The monomial list.</param>
    /// <param name="constant">常数累加器 / The constant accumulator.</param>
    /// <param name="symbol">指示符号 / The indicator symbol.</param>
    /// <param name="coefficient">系数 / The coefficient.</param>
    internal static void Accumulate(
        List<LinearMonomial<Flt64>> monomials,
        ref Flt64 constant,
        ILinearIntermediateSymbol<Flt64> symbol,
        Flt64 coefficient)
    {
        LinearPolynomial<Flt64> polynomial = symbol switch
        {
            LinearExpressionSymbol plain => plain.Polynomial,
            LinearFunctionSymbolAdapter<Flt64> function => function.Polynomial,
            _ => throw new NotSupportedException("unknown symbol kind: " + symbol.Name),
        };
        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials)
        {
            monomials.Add(new LinearMonomial<Flt64>(coefficient * monomial.Coefficient, monomial.Symbol));
        }
        constant += coefficient * polynomial.Constant;
    }
}
