// 舱位装载重量上限 / The per-position load weight cap.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
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
/// 舱位装载重量上限 / The per-position load weight cap.
/// </summary>
/// <remarks>
/// 对齐基线 `stowage/service/limits/LoadWeightLimit.kt`：对每个**可用**舱位加一行 `estimate_load_weight_j ≤ max_load_weight_j`。
///
/// 右边取的是 `MaxLoadWeight` 的**符号**，不是 `EffectiveMlw` 的标量。两者在分段线性时不同：符号是按零油重分段的阶跃，
/// 标量是当前零油重下的一个取值。用标量会让整架飞机在重载下被同一个上限卡住。
///
/// Aligned with the baseline: one row `estimate_load_weight_j ≤ max_load_weight_j` per **available** position. The right-hand side is `MaxLoadWeight`'s **symbol**, not the
/// `EffectiveMlw` scalar. They differ under piecewise linearity: the symbol is a step keyed by zero-fuel weight while the scalar is one value at the current zero-fuel weight;
/// using the scalar pins the whole aircraft to one cap regardless of loading.
/// </remarks>
public sealed class LoadWeightLimit
{
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Load _load;
    private readonly MaxLoadWeight _maxLoadWeight;

    /// <summary>
    /// 构造重量上限 / Construct the load weight cap.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="maxLoadWeight">最大装载重量 / The maximum load weight model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public LoadWeightLimit(
        IReadOnlyList<StowagePosition> positions,
        Load load,
        MaxLoadWeight maxLoadWeight,
        string name = "load_weight_limit") {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(maxLoadWeight);
        _positions = positions;
        _load = load;
        _maxLoadWeight = maxLoadWeight;
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
            if (_load.EstimateLoadWeightSymbol(j) is not { } weight) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "舱位 " + position.Name + " 的估计装载重量符号未注册 / the estimate load weight symbol of position " + position.Name + " is not registered"));
            }
            if (_maxLoadWeight.MaxLoadWeightSymbol(j) is not { } limit) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "舱位 " + position.Name + " 的最大装载重量符号未注册 / the maximum load weight symbol of position " + position.Name + " is not registered"));
            }
            string rowName = Name + "_" + position.Name;
            var row = new LinearInequality<Flt64>(weight.Polynomial, limit.Polynomial, Comparison.LE, rowName);
            Try added = linear.AddConstraint(row, group: null, name: rowName);
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
