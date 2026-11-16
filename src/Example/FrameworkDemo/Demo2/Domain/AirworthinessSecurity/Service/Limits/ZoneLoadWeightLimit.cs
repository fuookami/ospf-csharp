// 区域载荷重量上限 / The upper bound on load weight per fuselage zone.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;

/// <summary>
/// 区域载荷重量上限 / The upper bound on load weight per fuselage zone.
/// </summary>
/// <remarks>
/// 逐条对齐：每个限区一行
/// `Σ (系数 × 该舱位估计装载重量) + 救生筏重量 ≤ 区域最大载荷`，名字 `{name}_{区域名}`。
///
/// 四处容易做错：
///
/// 1. **系数不是 1**。舱位可以跨两个限区，每个区按重叠比例分它的一部分。当成 1 会让每个区都算整个舱位。
/// 2. **救生筏重量是常数项**，不进单项式。它不随决策变化。
/// 3. **整区没有可用舱位就跳过**，不是失败。这是合法配置（该区本就不装货）。
/// 4. **名字带区域名**。多个区域同名时"第 2 条约束不可行"不知道是哪个区。
///
/// one row per limit zone, `Σ (coefficient × estimated load weight) + liferaft weight ≤ zone maximum`, named `{name}_{zone}`. Four easy mistakes: (1) **the
/// coefficient is not 1** — a position can straddle two zones and each takes a share in proportion to the overlap; (2) **the liferaft weight is a constant term**, not a monomial,
/// since it does not vary with decisions; (3) **a zone with no available position is skipped**, not failed — that is a legitimate configuration; (4) **the name carries the zone**,
/// or "constraint 2 is infeasible" does not say which zone.
/// </remarks>
internal sealed class ZoneLoadWeightLimit
{
    private readonly MaxZoneLoadWeight _maxZoneLoadWeight;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Load _load;
    private readonly string _name;

    /// <summary>
    /// 构造区域载荷上限 / Construct the zone load bounds.
    /// </summary>
    /// <param name="maxZoneLoadWeight">区域载荷限制 / The zone load limits.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="name">约束名前缀 / The constraint name prefix.</param>
    public ZoneLoadWeightLimit(
        MaxZoneLoadWeight maxZoneLoadWeight,
        IReadOnlyList<StowagePosition> positions,
        Load load,
        string name = "zone_load_weight_limit") {
        _maxZoneLoadWeight = maxZoneLoadWeight;
        _positions = positions;
        _load = load;
        _name = name;
    }

    /// <summary>上一次执行加上的约束名 / The constraint names added by the last invocation</summary>
    public IReadOnlyList<string> LastAddedNames { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// 执行限制 / Invoke the limit.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        var added = new List<string>();
        foreach (ZoneLoadLimitZone zone in _maxZoneLoadWeight.LimitZones) {
            bool anyAvailable = false;
            foreach (ZoneLoadLimitPart part in zone.Parts) {
                if (_positions[part.PositionIndex].Status.Available) {
                    anyAvailable = true;
                    break;
                }
            }
            if (!anyAvailable) {
                continue;
            }

            var polynomial = new MutableLinearPolynomial<Flt64>();
            foreach (ZoneLoadLimitPart part in zone.Parts) {
                StowagePosition position = _positions[part.PositionIndex];
                if (_load.EstimateLoadWeightSymbol(part.PositionIndex) is not { } estimate) {
                    // 区域里有可用舱位却没有估计重量符号，说明 `Load.Register` 没跑或跑失败。静默跳过会让整条区域约束消失。
                    // An available position inside the zone without an estimated weight symbol means `Load.Register` never ran or failed. Skipping silently drops the whole zone
                    // constraint.
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "区域 " + zone.Name + " 内的舱位缺少估计装载重量符号 / a position in zone " + zone.Name
                        + " has no estimated load weight symbol: " + position.Name));
                }
                var coefficient = new Flt64(part.Weight);
                foreach (LinearMonomial<Flt64> monomial in estimate.Polynomial.Monomials) {
                    polynomial.AddMonomial(new LinearMonomial<Flt64>(
                        monomial.Coefficient.Times(coefficient),
                        monomial.Symbol));
                }
                polynomial.AddConstant(estimate.Polynomial.Constant.Times(coefficient));
            }
            if (zone.LiferaftWeight is { } liferaftWeight) {
                polynomial.AddConstant(new Flt64(liferaftWeight));
            }

            string name = _name + "_" + zone.Name;
            var row = new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant),
                new LinearPolynomial<Flt64>(new LinearMonomial<Flt64>[0], new Flt64(zone.MaxLoadWeight)),
                Comparison.LE,
                name);
            Try result = model.AddConstraint(row, group: null, name: name);
            if (!result.IsOk) {
                return result;
            }
            added.Add(name);
        }
        LastAddedNames = added;
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
