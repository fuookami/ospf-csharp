// 最大装载重量模型：按零油重取分段规格 / Maximum load weight model: the piecewise specification evaluated at zero-fuel weight.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 最大装载重量模型 / The maximum load weight model.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `demo2/domain/stowage/model/MaxLoadWeight.kt`：每个舱位产一个 `max_load_weight_{舱位}` 符号，取值三支：
///
/// 1. 有分段规格**且**已知零油重 → `mlw(zfw)`，即零油重落在哪一段就取那段；
/// 2. 有分段规格**但**不知零油重 → 退回各段最大值 `mlw`；
/// 3. 没有分段规格 → 常数 `mlw`。
///
/// 第 2 支容易被"顺手合并"掉：既然分段要零油重，那没有零油重就当作第一段——**错**。分段的意义是"飞机越重允许装得越少"，
/// 零油重未知时取任何一段都等于凭空假设一个装载状态；退回各段最大值则是"按最宽松的规格",至少在零油重真值偏轻时是对的。
/// 这条与 的"超出最后一段取最后一段"是不同的两件事：那一条里的零油重**是已知的**。
///
/// Mirrors the baseline: one `max_load_weight_{position}` symbol per position, with three branches — (1) piecewise **and** a known zero-fuel weight takes `mlw(zfw)`;
/// (2) piecewise but **no** known zero-fuel weight falls back to the maximum across segments; (3) no segments is the constant `mlw`. Branch 2 invites merging into branch 1
/// ("no zero-fuel weight, so take the first segment") — that is wrong: segments mean "heavier aircraft, less cargo", so with the weight unknown any single segment is an invented
/// loading state, while the maximum is the loosest specification and is at least correct when the true weight is light. This is a different thing from the earlier "past the last segment
/// takes the last segment": there the zero-fuel weight **is** known.
/// </remarks>
public sealed class MaxLoadWeight
{
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly TotalWeight _totalWeight;

    private readonly Dictionary<int, LinearExpressionSymbol> _symbols = new();

    /// <summary>
    /// 构造最大装载重量模型 / Construct the maximum load weight model.
    /// </summary>
    /// <param name="positions">舱位 / The stowage positions.</param>
    /// <param name="totalWeight">总重模型（提供零油重）/ The total weight model, which supplies the zero-fuel weight.</param>
    public MaxLoadWeight(
        IReadOnlyList<StowagePosition> positions,
        TotalWeight totalWeight) {
        _positions = positions;
        _totalWeight = totalWeight;
    }

    /// <summary>
    /// 取某舱位的最大装载重量符号 / The maximum load weight symbol of a position.
    /// </summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>最大装载重量符号 / The maximum load weight symbol.</returns>
    public LinearExpressionSymbol? MaxLoadWeightSymbol(int positionIndex) =>
        _symbols.TryGetValue(positionIndex, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>
    /// 取某舱位生效的最大装载重量 / The effective maximum load weight of a position.
    /// </summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>最大装载重量 / The maximum load weight.</returns>
    /// <remarks>
    /// 三支的判断集中在这一处，`Register` 只负责把结果包成符号。分开写会让三支各出现一次，而它们必须**同时**改。
    ///
    /// The three branches live here and `Register` only wraps the result; splitting them would put each branch in its own place while they must change together.
    /// </remarks>
    public double EffectiveMlw(int positionIndex) {
        MaxLoadWeightLimit limit = _positions[positionIndex].Mlw;
        if (!limit.Piecewise) {
            return limit.Mlw;
        }
        double? zeroFuelWeight = _totalWeight.ComputedTotalWeight.TryGetValue(FlightPhase.ZeroFuel, out double computed)
            ? computed
            : null;
        return zeroFuelWeight is { } zfw ? limit.At(zfw) : limit.Mlw;
    }

    /// <summary>
    /// 注册最大装载重量符号 / Register the maximum load weight symbols.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        _symbols.Clear();
        for (int j = 0; j < _positions.Count; ++j) {
            var symbol = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: new Flt64(EffectiveMlw(j))),
                name: "max_load_weight_" + _positions[j].Name);
            Try added = model.Add(symbol);
            if (!added.IsOk) {
                return added;
            }
            _symbols[j] = symbol;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
