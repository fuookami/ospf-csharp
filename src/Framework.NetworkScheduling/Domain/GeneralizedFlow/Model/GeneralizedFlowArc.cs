// 单输入、单输出的广义流服务弧 / Single-input, single-output generalized-flow service arc.
// 与固定 Kotlin 基线 `model/GeneralizedFlowArc.kt`（3326 字节）对应。
// Mirrors the pinned baseline's `GeneralizedFlowArc.kt`.
//
// 容量与资费都按**起点投入量**计；`inputToOutputFactor` 只负责单位／工艺计量换算，`productionYield` 独立表示业务损耗或增益。
// 把这两件事分开是必须的：混成一个"总换算率"后，就再也无法区分"1 吨原料出 0.8 吨成品（工艺损耗）"与"1 短吨折 0.907 公吨
// （纯单位换算）"——而它们在报表与成本归集上是完全不同的两回事。
// Capacity and tariff are charged on the **origin input**; `inputToOutputFactor` only handles unit/process measurement conversion
// while `productionYield` independently expresses business loss or gain. Keeping them apart is mandatory: folded into one
// "overall conversion rate" it becomes impossible to tell "1 t of ore yields 0.8 t of product (process loss)" from "1 short ton is
// 0.907 metric tonnes (pure unit conversion)" — and those are entirely different things in reporting and cost attribution.

using System;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Model;

/// <summary>
/// 单输入、单输出的广义流服务弧 / Single-input, single-output generalized-flow service arc.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class GeneralizedFlowArc<V> where V : struct, IRealNumber<V> {
    private GeneralizedFlowArc(
        NetworkArc<V> networkArc,
        CapacityBounds<V> inputCapacity,
        Quantity<V> productionYield,
        Quantity<V>? inputToOutputFactor) {
        NetworkArc = networkArc;
        InputCapacity = inputCapacity;
        ProductionYield = productionYield;
        InputToOutputFactor = inputToOutputFactor;
    }

    /// <summary>拓扑与每起点单位投入资费 / Topology and tariff per origin-unit input.</summary>
    public NetworkArc<V> NetworkArc { get; }

    /// <summary>按起点单位计的有限投入容量 / Finite input capacity measured in origin units.</summary>
    public CapacityBounds<V> InputCapacity { get; }

    /// <summary>无量纲业务产率 / Dimensionless business yield.</summary>
    public Quantity<V> ProductionYield { get; }

    /// <summary>可选的终点单位／起点单位转换系数 / Optional destination-unit per origin-unit conversion factor.</summary>
    public Quantity<V>? InputToOutputFactor { get; }

    /// <summary>
    /// 创建单输入广义流弧 / Create a single-input generalized-flow arc.
    /// </summary>
    /// <remarks>
    /// **产率必须是非仿射的无量纲量**。仿射单位（如摄氏温度）的乘除没有物理意义；带量纲的"产率"则意味着单位换算被混进了业务
    /// 损耗里，后续任何成本归集都会少算或多算一整个量纲的因子。
    /// **The yield must be a non-affine dimensionless quantity.** Multiplication and division of affine units (Celsius, say) is
    /// physically meaningless, and a dimensional "yield" means unit conversion has been folded into business loss, so any later
    /// cost attribution is off by a whole dimension's worth of factor.
    /// </remarks>
    /// <param name="networkArc">通用网络弧；其 cost 为每起点单位投入资费 / Network arc; its cost is the tariff per origin-unit input.</param>
    /// <param name="inputCapacity">投入容量，单位由起点节点决定 / Input capacity, measured in the origin node's unit.</param>
    /// <param name="productionYield">无量纲业务产率，允许损耗、保持或增益 / Dimensionless business yield; loss, retention or gain.</param>
    /// <param name="inputToOutputFactor">跨量纲时必须显式提供的终点单位／起点单位系数 / Destination-per-origin factor, required for cross-dimensional conversion.</param>
    /// <returns>弧或产率量纲错误 / The arc, or a yield-dimension error.</returns>
    public static Result<GeneralizedFlowArc<V>, ErrorCode, Error<ErrorCode>> Invoke(
        NetworkArc<V> networkArc,
        CapacityBounds<V> inputCapacity,
        Quantity<V>? productionYield = null,
        Quantity<V>? inputToOutputFactor = null) {
        ArgumentNullException.ThrowIfNull(networkArc);
        ArgumentNullException.ThrowIfNull(inputCapacity);

        Quantity<V> yield = productionYield
            ?? new Quantity<V>(networkArc.Cost.Value.Constants.One, NoneUnit.Instance);
        if (yield.Unit.IsAffine || !yield.Unit.SameDimension(NoneUnit.Instance)) {
            return NetworkSchedulingErrors.Failure<GeneralizedFlowArc<V>>(
                "广义流业务产率必须是非仿射无量纲量 / Generalized-flow business yield must be a non-affine dimensionless "
                + "quantity");
        }
        return Results.Ok(new GeneralizedFlowArc<V>(
            networkArc, inputCapacity, yield, inputToOutputFactor));
    }
}
