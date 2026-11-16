// 路线编译影子价格键与映射 / Route-compilation shadow-price keys and map.
// ,,.
//
// 这一层只做「对偶的命名与快照」，不碰主问题结构：上层用稳定键把每行对偶存进来，定价侧按业务含义（客户覆盖 / 车队规模）读出来，
// 再压成不可变的 `PricingDuals`。generation 与 compilation 因此保持单向解耦——定价不需要知道主问题长什么样。
// This layer only names and snapshots duals; it never touches master-problem structure: the layer above stores each row's dual under
// a stable key, the pricing side reads it back by business meaning (customer coverage / fleet size) and flattens it into immutable
// `PricingDuals`. Generation and compilation therefore stay decoupled in one direction — pricing need not know the master's shape.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Shadow;

/// <summary>
/// 车队数量影子价格键 / Fleet-size shadow price key.
/// </summary>
/// <param name="VehicleTypeId">车辆类型标识 / Vehicle type identifier.</param>
public sealed class FleetSizeShadowPriceKey(VehicleTypeId VehicleTypeId)
    : ShadowPriceKey(typeof(FleetSizeShadowPriceKey)) {
    /// <summary>车辆类型标识 / Vehicle type identifier.</summary>
    public VehicleTypeId VehicleTypeId { get; } = VehicleTypeId;

    /// <inheritdoc/>
    public override string ToString() => $"FleetSizeShadowPriceKey({VehicleTypeId.Value})";

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is FleetSizeShadowPriceKey other && other.VehicleTypeId.Equals(VehicleTypeId);

    /// <inheritdoc/>
    public override int GetHashCode() => VehicleTypeId.GetHashCode();
}

/// <summary>
/// 客户覆盖影子价格键 / Customer-coverage shadow price key.
/// </summary>
/// <param name="CustomerId">客户标识 / Customer identifier.</param>
public sealed class CustomerCoverageShadowPriceKey(CustomerId CustomerId)
    : ShadowPriceKey(typeof(CustomerCoverageShadowPriceKey)) {
    /// <summary>客户标识 / Customer identifier.</summary>
    public CustomerId CustomerId { get; } = CustomerId;

    /// <inheritdoc/>
    public override string ToString() => $"CustomerCoverageShadowPriceKey({CustomerId.Value})";

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is CustomerCoverageShadowPriceKey other && other.CustomerId.Equals(CustomerId);

    /// <inheritdoc/>
    public override int GetHashCode() => CustomerId.GetHashCode();
}

/// <summary>
/// VRP 影子价格参数 / VRP shadow price arguments.
/// </summary>
/// <remarks>
/// 用于按「这条路线用了哪种车、访问了哪些客户」求 reduced cost。
/// Used to compute a reduced cost from "which vehicle type and which customers this route uses".
/// </remarks>
/// <param name="VehicleTypeId">车辆类型标识 / Vehicle type identifier.</param>
/// <param name="VisitedCustomerIds">已访问客户 / Visited customers.</param>
public sealed record VrpShadowPriceArguments(
    VehicleTypeId VehicleTypeId,
    IReadOnlySet<CustomerId> VisitedCustomerIds);

/// <summary>
/// VRP 影子价格映射 / VRP shadow price map.
/// </summary>
/// <remarks>
/// **车队对偶保持 solver 的原始符号，不做归一化**。solver 返回的是框架约定的有符号 LP 对偶；reduced cost 公式
/// `reducedCost = objectiveCost - customerDual - fleetDual` 直接消费该有符号值，与 Gantt 模块 `cost -= dual` 的约定一致。
/// 若在这里"顺手取绝对值"或改符号，定价算出的 reduced cost 会系统性偏移，主问题因此收敛到更差的解，且没有报错。
/// **The fleet dual keeps the solver's sign and is never normalised.** The solver returns the framework's signed LP dual; the
/// reduced-cost formula `reducedCost = objectiveCost - customerDual - fleetDual` consumes that signed value directly, matching the
/// Gantt module's `cost -= dual` convention. Taking absolute values or flipping the sign here would systematically shift every
/// priced reduced cost, letting the master converge to a worse solution with nothing reported.
/// </remarks>
public sealed class VrpShadowPriceMap : AbstractShadowPriceMap<VrpShadowPriceArguments, VrpShadowPriceMap> {
    private readonly Dictionary<string, Flt64> _additionalDuals = new();

    /// <summary>扩展主问题行对偶快照 / Snapshot of extra master-row duals.</summary>
    public IReadOnlyDictionary<string, Flt64> AdditionalDuals => _additionalDuals;

    /// <summary>
    /// 写入扩展行的 solver 有符号对偶 / Store a signed solver dual for an extra master row.
    /// </summary>
    /// <param name="key">扩展行稳定键 / Stable extra-row key.</param>
    /// <param name="price">solver 有符号对偶值 / Signed solver dual value.</param>
    public void PutAdditionalDual(string key, Flt64 price) {
        ArgumentNullException.ThrowIfNull(key);
        _additionalDuals[key] = price;
    }

    /// <summary>
    /// 获取客户覆盖对偶 / Get the customer-coverage dual.
    /// </summary>
    /// <param name="customerId">客户标识 / Customer identifier.</param>
    /// <returns>对偶值，不存在时为 0 / The dual value, or zero when absent.</returns>
    public Flt64 CustomerDual(CustomerId customerId) =>
        this[new CustomerCoverageShadowPriceKey(customerId)]?.Price ?? Flt64.Zero;

    /// <summary>
    /// 获取车队数量对偶 / Get the fleet-size dual.
    /// </summary>
    /// <param name="vehicleTypeId">车辆类型标识 / Vehicle type identifier.</param>
    /// <returns>对偶值，不存在时为 0 / The dual value, or zero when absent.</returns>
    public Flt64 FleetDual(VehicleTypeId vehicleTypeId) =>
        this[new FleetSizeShadowPriceKey(vehicleTypeId)]?.Price ?? Flt64.Zero;

    /// <summary>
    /// 输出不可变定价对偶快照 / Produce an immutable pricing-dual snapshot.
    /// </summary>
    /// <param name="phase">当前定价阶段 / Current pricing phase.</param>
    /// <returns>不可变定价对偶 / Immutable pricing duals.</returns>
    public PricingDuals ToPricingDuals(PricingPhase phase) {
        var customerDuals = new Dictionary<CustomerId, Flt64>();
        var fleetDuals = new Dictionary<VehicleTypeId, Flt64>();
        foreach ((ShadowPriceKey key, ShadowPrice shadowPrice) in Map) {
            switch (key) {
                case CustomerCoverageShadowPriceKey coverage:
                    customerDuals[coverage.CustomerId] = shadowPrice.Price;
                    break;
                case FleetSizeShadowPriceKey fleet:
                    fleetDuals[fleet.VehicleTypeId] = shadowPrice.Price;
                    break;
            }
        }
        return new PricingDuals(
            phase,
            customerDuals,
            fleetDuals,
            new Dictionary<string, Flt64>(_additionalDuals));
    }
}
