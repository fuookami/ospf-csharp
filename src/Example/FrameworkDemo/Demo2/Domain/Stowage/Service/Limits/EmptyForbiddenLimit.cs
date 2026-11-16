// 禁止放空舱位 / The "no empty position" rows.

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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service.Limits;

/// <summary>
/// 禁止放空舱位 / The "no empty position" rows.
/// </summary>
/// <remarks>
/// 对齐：对每个**可用且带 `EmptyForbidden` 类型码**的舱位加一行 `estimate_loaded_j = 1`。
///
/// 两个条件都要查。少查"可用"会给本来就不用的舱位加强制装载，直接让模型不可行；少查类型码会给所有舱位都强制装载。
/// 后者在舱位比货物多时立刻不可行，是这两条里更常见的那种错。
///
/// One row `estimate_loaded_j = 1` per position that is **available and carries the `EmptyForbidden` code**. Both conditions matter. Dropping
/// "available" forces loading into positions nobody uses and makes the model infeasible outright; dropping the code forces loading everywhere. The latter turns infeasible as soon
/// as there are more positions than items and is the more common of the two mistakes.
/// </remarks>
public sealed class EmptyForbiddenLimit
{
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Load _load;

    /// <summary>
    /// 构造禁止放空 / Construct the no-empty rows.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public EmptyForbiddenLimit(
        IReadOnlyList<StowagePosition> positions,
        Load load,
        string name = "empty_forbidden_limit") {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(load);
        _positions = positions;
        _load = load;
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加约束 / Apply the rows.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> linear) {
        ArgumentNullException.ThrowIfNull(linear);
        for (int j = 0; j < _positions.Count; ++j) {
            StowagePosition position = _positions[j];
            if (!position.Status.Available || !position.Type.Contains(PositionTypeCode.EmptyForbidden)) {
                continue;
            }
            if (_load.EstimateLoadedSymbol(j) is not { } loaded) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "舱位 " + position.Name + " 的估计已装载符号未注册 / the estimate loaded symbol of position " + position.Name + " is not registered"));
            }
            string rowName = Name + "_" + position.Name;
            var row = new LinearInequality<Flt64>(
                loaded.Polynomial,
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                Comparison.EQ,
                rowName);
            Try added = linear.AddConstraint(row, group: null, name: rowName);
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
