// 管线列表生成器与影子价格键安全转换 / Pipeline-list generator and shadow-price-key safe cast.
//（337 字节）对应。
// And.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Service.Limits;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Shadow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Service;

/// <summary>
/// 管线列表生成器 / Pipeline list generator.
/// </summary>
/// <remarks>
/// 按定价阶段生成不同的管线列表：
/// - Phase I：客户覆盖约束 + 车队约束 + Phase-I 最小化
/// - Phase II：客户覆盖约束 + 车队约束 + 路线成本最小化
/// Generates different pipeline lists by pricing phase:
/// - Phase I: customer-coverage constraint + fleet constraint + Phase-I minimisation
/// - Phase II: customer-coverage constraint + fleet constraint + route-cost minimisation
/// **两条最小化目标互斥，绝不会同时出现在同一份列表里**。若两者都在，目标函数是 `min (人工成本 + 路线成本)`，Phase I 就会为了省钱而
/// 不覆盖客户，第一阶段直接失去意义。列表由这一个入口产出，正是为了让"互斥"成为结构上的保证，而不是调用方的约定。
/// **The two minimisation objectives are mutually exclusive and never appear in the same list.** With both present the objective
/// becomes `min (artificial cost + route cost)` and Phase I would leave customers uncovered to save money, which defeats the whole
/// first phase. Funnelling list construction through this single entry point makes that exclusivity a structural guarantee rather
/// than a caller convention.
/// </remarks>
public static class PipelineListGenerator {
    /// <summary>
    /// 生成 Phase-I 管线列表 / Generate the Phase-I pipeline list.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="instance">VRPTW 实例 / The VRPTW instance.</param>
    /// <param name="compilation">路线编译模型 / The route compilation model.</param>
    /// <param name="extraPipelines">额外的业务管线，追加在末尾 / Extra business pipelines, appended at the end.</param>
    /// <param name="customerCoverageConstraint">客户覆盖约束；缺省新建 / The coverage constraint; created when omitted.</param>
    /// <param name="fleetSizeConstraint">车队约束；缺省新建 / The fleet constraint; created when omitted.</param>
    /// <returns>有序管线列表 / The ordered pipeline list.</returns>
    public static IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>
        PhaseOne<V>(
            VrptwInstance<V> instance,
            RouteCompilation<V> compilation,
            IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>? extraPipelines = null,
            CustomerCoverageConstraint<V>? customerCoverageConstraint = null,
            FleetSizeConstraint<V>? fleetSizeConstraint = null)
        where V : struct, IRealNumber<V> {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(compilation);
        return Compose(
            customerCoverageConstraint ?? new CustomerCoverageConstraint<V>(instance.Customers, compilation),
            fleetSizeConstraint ?? new FleetSizeConstraint<V>(instance.VehicleTypes, compilation),
            new PhaseOneMinimization<V>(compilation),
            extraPipelines);
    }

    /// <summary>
    /// 生成 Phase-II 管线列表 / Generate the Phase-II pipeline list.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="instance">VRPTW 实例 / The VRPTW instance.</param>
    /// <param name="compilation">路线编译模型 / The route compilation model.</param>
    /// <param name="extraPipelines">额外的业务管线，追加在末尾 / Extra business pipelines, appended at the end.</param>
    /// <param name="customerCoverageConstraint">客户覆盖约束；缺省新建 / The coverage constraint; created when omitted.</param>
    /// <param name="fleetSizeConstraint">车队约束；缺省新建 / The fleet constraint; created when omitted.</param>
    /// <returns>有序管线列表 / The ordered pipeline list.</returns>
    public static IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>
        PhaseTwo<V>(
            VrptwInstance<V> instance,
            RouteCompilation<V> compilation,
            IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>? extraPipelines = null,
            CustomerCoverageConstraint<V>? customerCoverageConstraint = null,
            FleetSizeConstraint<V>? fleetSizeConstraint = null)
        where V : struct, IRealNumber<V> {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(compilation);
        return Compose(
            customerCoverageConstraint ?? new CustomerCoverageConstraint<V>(instance.Customers, compilation),
            fleetSizeConstraint ?? new FleetSizeConstraint<V>(instance.VehicleTypes, compilation),
            new RouteCostMinimization<V>(compilation),
            extraPipelines);
    }

    /// <summary>
    /// 按"两条约束 + 一条目标 + 额外管线"的固定顺序组合 / Compose in the fixed order of two constraints, one objective, extras.
    /// </summary>
    /// <param name="coverage">客户覆盖约束 / The coverage constraint.</param>
    /// <param name="fleet">车队约束 / The fleet constraint.</param>
    /// <param name="objective">阶段目标 / The phase objective.</param>
    /// <param name="extra">额外管线 / Extra pipelines.</param>
    /// <returns>有序管线列表 / The ordered pipeline list.</returns>
    private static IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>
        Compose(
            ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap> coverage,
            ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap> fleet,
            ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap> objective,
            IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>? extra) {
        var pipelines = new List<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>> {
            coverage,
            fleet,
            objective,
        };
        if (extra is not null) {
            pipelines.AddRange(extra);
        }
        return pipelines;
    }
}

/// <summary>
/// 影子价格键安全转换工具 / Shadow price key safe cast utility.
/// </summary>
/// <remarks>
/// `shadowPriceKeyOf` 是 `inline fun &lt;reified K : ShadowPriceKey&gt;`。C# 的泛型在运行时保留类型实参，因此直接用 `as` 即可，
/// 不需要 `reified` 的语法糖；返回 `null` 表达"不是这个键"，与`as?` 一致。
/// `shadowPriceKeyOf` is `inline fun &lt;reified K : ShadowPriceKey&gt;`. C# generics keep their type argument at
/// runtime, so a plain `as` does the job without `reified` sugar; returning `null` expresses "not this key", matching its
/// `as?`.
/// </remarks>
public static class ShadowPriceKeyCastSupport {
    /// <summary>
    /// 把任意参数安全转换成指定的影子价格键 / Safely cast arbitrary arguments to the given shadow-price key.
    /// </summary>
    /// <typeparam name="K">影子价格键类型 / Shadow-price key type.</typeparam>
    /// <param name="args">约束携带的参数 / The arguments carried by a constraint.</param>
    /// <returns>键，或 null 表示不是该类型 / The key, or null when it is not that type.</returns>
    public static K? ShadowPriceKeyOf<K>(object? args) where K : ShadowPriceKey => args as K;
}
