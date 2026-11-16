// 舱位装载件数上限 / The per-position load count cap.

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
/// 舱位装载件数上限 / The per-position load count cap.
/// </summary>
/// <remarks>
/// 对齐：对每个**可用**舱位加一行 `load_amount_j ≤ mla_j`。
///
/// 判据要盯住"可用"这个过滤：不可用舱位的装载量恒为零，多这一行不会错，但会让约束表多出一批废行。
///
/// One row `load_amount_j ≤ mla_j` per **available** position. The criteria watch the "available" filter: an unavailable position's load amount is
/// identically zero, so the extra row is harmless but swells the table with dead rows.
/// </remarks>
public sealed class LoadAmountLimit
{
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Load _load;

    /// <summary>
    /// 构造件数上限 / Construct the load count cap.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public LoadAmountLimit(
        IReadOnlyList<StowagePosition> positions,
        Load load,
        string name = "load_amount_limit") {
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
            if (!position.Status.Available) {
                continue;
            }
            if (_load.LoadAmountSymbol(j) is not { } amount) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "舱位 " + position.Name + " 的装载量符号未注册 / the load amount symbol of position " + position.Name + " is not registered"));
            }
            string rowName = Name + "_" + position.Name;
            var row = new LinearInequality<Flt64>(
                amount.Polynomial,
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(position.Mla)),
                Comparison.LE,
                rowName);
            Try added = linear.AddConstraint(row, group: null, name: rowName);
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
