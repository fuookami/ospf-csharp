// VRP 定价对偶快照与列系数 / VRP pricing duals and column coefficients.
// 与固定 Kotlin 基线 `PricingDuals.kt`（43 行）、`RouteArcCoefficientProvider.kt`（37 行）对应。
// Mirrors the pinned baseline's `PricingDuals.kt` and `RouteArcCoefficientProvider.kt`.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;

/// <summary>
/// 定价阶段 / Pricing phase.
/// </summary>
public enum PricingPhase {
    /// <summary>第一阶段 / Phase one.</summary>
    PhaseOne,

    /// <summary>第二阶段 / Phase two.</summary>
    PhaseTwo,
}

/// <summary>
/// 与 compilation 解耦的不可变定价对偶快照 / Immutable pricing-dual snapshot decoupled from compilation.
/// </summary>
/// <remarks>
/// **符号约定**（与 Gantt 模块的 `reducedCost` 一致，即 `cost -= dual`）：
/// - `Customer`：等式覆盖约束 `= 1` 的标准 LP 对偶，符号自由；在 reduced cost 中以 `-CustomerDual` 出现，
///   正值降低 reduced cost（奖励覆盖客户）。
/// - `Fleet`：`&lt;=` 车队约束的框架有符号 LP 对偶；当前最小化实现与测试取非正值，直接以 `-FleetDual` 使用，
///   负值增加 reduced cost（惩罚使用车辆）。
/// - `Additional`：扩展主问题行的有符号对偶，键为稳定行标识。
///
/// 把对偶单独抽成值对象（而不是挂在 compilation 上）让定价可以在不重建编译产物的前提下换一组对偶重算，也让"对偶"这一
/// 易错状态的符号约定只写一处。
/// **Sign convention** (consistent with the Gantt module's `reducedCost`, i.e. `cost -= dual`):
/// - `Customer`: standard LP duals for equality coverage constraints `= 1`, sign-free; they appear as `-CustomerDual`, so a
///   positive value lowers reduced cost (rewards covering customers).
/// - `Fleet`: framework-signed LP duals for `&lt;=` fleet constraints; the minimization implementation and tests use
///   non-positive values consumed directly as `-FleetDual`, so a negative value raises reduced cost (penalizes vehicle use).
/// - `Additional`: signed duals for extra master rows, keyed by stable row identity.
///
/// Extracting the duals into a value object (rather than hanging them off compilation) lets pricing recompute against a new
/// dual set without rebuilding the compilation products, and keeps this error-prone sign convention stated in one place.
/// </remarks>
/// <param name="Phase">当前阶段 / Current phase.</param>
/// <param name="CustomerDuals">客户覆盖对偶 / Customer-coverage duals.</param>
/// <param name="FleetDuals">车队数量对偶 / Fleet-size duals.</param>
/// <param name="AdditionalDuals">扩展主问题行的有符号对偶 / Signed duals for extra master rows.</param>
public sealed class PricingDuals(
    PricingPhase Phase,
    IReadOnlyDictionary<CustomerId, Flt64> CustomerDuals,
    IReadOnlyDictionary<VehicleTypeId, Flt64> FleetDuals,
    IReadOnlyDictionary<string, Flt64>? AdditionalDuals = null) {
    /// <summary>当前阶段 / Current phase.</summary>
    public PricingPhase Phase { get; } = Phase;

    /// <summary>客户覆盖对偶 / Customer-coverage duals.</summary>
    public IReadOnlyDictionary<CustomerId, Flt64> Customer { get; } =
        CustomerDuals.ToDictionary(pair => pair.Key, pair => pair.Value);

    /// <summary>车队数量对偶 / Fleet-size duals.</summary>
    public IReadOnlyDictionary<VehicleTypeId, Flt64> Fleet { get; } =
        FleetDuals.ToDictionary(pair => pair.Key, pair => pair.Value);

    /// <summary>扩展主问题行的有符号对偶 / Signed duals for extra master rows.</summary>
    public IReadOnlyDictionary<string, Flt64> Additional { get; } =
        AdditionalDuals is null
            ? new Dictionary<string, Flt64>()
            : AdditionalDuals.ToDictionary(pair => pair.Key, pair => pair.Value);
}

/// <summary>
/// 提供与路线弧可加的扩展主问题列系数 / Provides additive extra-master coefficients for route arcs.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface IRouteArcCoefficientProvider<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 返回该弧在各扩展主问题行中的系数 / Return the coefficients of this arc in extra master rows.
    /// </summary>
    /// <param name="arc">资源弧 / Resource arc.</param>
    /// <returns>按稳定行键索引的领域数值系数 / Domain-valued coefficients keyed by stable row identity.</returns>
    IReadOnlyDictionary<string, V> Coefficients(ResourceArc<VehicleTypeId> arc);

    /// <summary>
    /// 汇总路线全部弧系数 / Sum the arc coefficients of a route.
    /// </summary>
    /// <remarks>
    /// 存在这条默认实现，是为了**确保 RMP 新列与定价使用同一份系数定义**：两边各写一遍求和逻辑时，一旦某处漏算一条弧，
    /// 主问题与定价就会对同一条路线给出不同的 reduced cost，而这种不一致极难从结果里看出来。
    /// **This default exists to keep RMP columns and pricing on one coefficient definition**: when each side sums on its own and
    /// one of them misses an arc, the master and pricing disagree on that route's reduced cost — a mismatch that is very hard
    /// to see in the results.
    /// </remarks>
    /// <param name="route">路线 / Route.</param>
    /// <returns>稳定行键到路线系数的映射 / Route coefficients keyed by stable row identity.</returns>
    IReadOnlyDictionary<string, V> RouteCoefficients(Route<V> route) {
        ArgumentNullException.ThrowIfNull(route);

        var result = new Dictionary<string, V>();
        V zero = route.Cost.Value.Constants.Zero;
        for (int index = 0; index < route.ArcIds.Count; index++) {
            var arc = new ResourceArc<VehicleTypeId>(
                route.VehicleTypeId,
                route.Stops[index].NodeId,
                route.Stops[index + 1].NodeId,
                route.ArcIds[index]);
            foreach (KeyValuePair<string, V> pair in Coefficients(arc)) {
                result[pair.Key] = result.TryGetValue(pair.Key, out V accumulated)
                    ? accumulated.Plus(pair.Value)
                    : zero.Plus(pair.Value);
            }
        }
        return result;
    }
}
