// 累积载荷重量上限 / The upper bound on cumulative load weight at each checkpoint.

using System;
using System.Collections.Generic;
using System.Globalization;
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
/// 累积载荷重量上限 / The upper bound on cumulative load weight.
/// </summary>
/// <remarks>
/// 逐条对齐：每个检查点一行
/// `Σ (重叠比例 × 该舱位估计装载重量) ≤ 检查点最大累积和`，名字 `{name}_{区域名}_{力臂}`。
///
/// 三处容易做错：
///
/// 1. **检查点没有可用舱位就跳过**，不是失败。与区域载荷上限同形。
/// 2. **力臂用不变文化格式**。`toArm.value` 拼进名字，用当前文化会让小数点随机器变化（`900.5` vs `900,5`），
///    同一份数据在两台机器上产生不同的约束名，报告无法对照。
/// 3. **没有救生筏常数项**。累积载荷只看货，不像区域载荷那样含救生筏——这是分工，不是遗漏。
///
/// one row per checkpoint, `Σ (overlap ratio × estimated load weight) ≤ checkpoint maximum`, named `{name}_{zone}_{arm}`. Three easy mistakes:
/// (1) **a checkpoint with no available position is skipped**, not failed — same shape as the zone bound; (2) **the arm uses the invariant culture** — the code splices
/// `toArm.value` into the name, and the current culture would make the decimal separator machine-dependent (`900.5` vs `900,5`), so the same data yields different constraint names
/// on two machines and reports cannot be compared; (3) **there is no liferaft constant** — cumulative load counts cargo only, unlike the zone bound; that is its
/// division of labour, not an omission.
/// </remarks>
internal sealed class CumulativeLoadWeightLimit
{
    private readonly MaxCumulativeLoadWeight _maxCumulativeLoadWeight;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Load _load;
    private readonly string _name;

    /// <summary>
    /// 构造累积载荷上限 / Construct the cumulative load bounds.
    /// </summary>
    /// <param name="maxCumulativeLoadWeight">累积载荷限制 / The cumulative load limits.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="name">约束名前缀 / The constraint name prefix.</param>
    public CumulativeLoadWeightLimit(
        MaxCumulativeLoadWeight maxCumulativeLoadWeight,
        IReadOnlyList<StowagePosition> positions,
        Load load,
        string name = "cumulative_load_weight_limit") {
        _maxCumulativeLoadWeight = maxCumulativeLoadWeight;
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
        foreach (CumulativeCheckPoint checkPoint in _maxCumulativeLoadWeight.CheckPoints) {
            bool anyAvailable = false;
            foreach (ZoneLoadLimitPart part in checkPoint.Parts) {
                if (_positions[part.PositionIndex].Status.Available) {
                    anyAvailable = true;
                    break;
                }
            }
            if (!anyAvailable) {
                continue;
            }

            var polynomial = new MutableLinearPolynomial<Flt64>();
            foreach (ZoneLoadLimitPart part in checkPoint.Parts) {
                StowagePosition position = _positions[part.PositionIndex];
                if (_load.EstimateLoadWeightSymbol(part.PositionIndex) is not { } estimate) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "检查点 " + checkPoint.Zone.Name + " 内的舱位缺少估计装载重量符号 / a position at checkpoint "
                        + checkPoint.Zone.Name + " has no estimated load weight symbol: " + position.Name));
                }
                var coefficient = new Flt64(part.Weight);
                foreach (LinearMonomial<Flt64> monomial in estimate.Polynomial.Monomials) {
                    polynomial.AddMonomial(new LinearMonomial<Flt64>(
                        monomial.Coefficient.Times(coefficient),
                        monomial.Symbol));
                }
                polynomial.AddConstant(estimate.Polynomial.Constant.Times(coefficient));
            }

            string name = _name + "_" + checkPoint.Zone.Name + "_"
                + checkPoint.ToArm.ToString(CultureInfo.InvariantCulture);
            var row = new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant),
                new LinearPolynomial<Flt64>(new LinearMonomial<Flt64>[0], new Flt64(checkPoint.MaxSum)),
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
