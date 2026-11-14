// Phase-I 与 Phase-II 最小化目标管线 / Phase-I and Phase-II minimisation objectives.
// 与固定 Kotlin 基线 `route_compilation/service/limits/PhaseOneMinimization.kt`（1696 字节）、
// `route_compilation/service/limits/RouteCostMinimization.kt`（1685 字节）对应。
// Mirrors the pinned baseline's `PhaseOneMinimization.kt` and `RouteCostMinimization.kt`.

using System;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Shadow;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Service.Limits;

/// <summary>
/// Phase-I 最小化管线 / Phase-I minimisation pipeline.
/// </summary>
/// <remarks>
/// Phase I 最小化人工变量总和：`min artificialCost`。真实路线列的 Phase-I 目标系数为 0。
/// Phase I minimises the sum of artificial variables: `min artificialCost`. The Phase-I objective coefficient for real route columns
/// is zero.
/// **人工成本只在 `Register` 时建一次，之后不再变动**，因此这里注册的是它当前的多项式快照。若哪天人工成本也要增量更新，
/// 这条管线就必须改成按批重新注册，否则快照会与符号脱钩。
/// **The artificial cost is built once at `Register` and never changes afterwards**, so what this pipeline registers is a snapshot of
/// its current polynomial. If the artificial cost ever needs incremental updates, this pipeline must re-register per batch or the
/// snapshot will drift from the symbol.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class PhaseOneMinimization<V>
    : ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>
    where V : struct, IRealNumber<V> {
    private readonly RouteCompilation<V> _compilation;

    /// <summary>
    /// 构造 Phase-I 最小化管线 / Construct the Phase-I minimisation pipeline.
    /// </summary>
    /// <param name="compilation">路线编译模型 / The route compilation model.</param>
    /// <param name="name">目标名称 / Objective name.</param>
    public PhaseOneMinimization(RouteCompilation<V> compilation, string name = "phase_one_minimization") {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(name);
        _compilation = compilation;
        Name = name;
    }

    /// <summary>管线名称 / Pipeline name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => Name;

    /// <summary>
    /// 注册 Phase-I 人工成本最小化 / Register the Phase-I artificial-cost minimisation.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_compilation.ArtificialCoverage?.Cost is not { } cost) {
            return NetworkSchedulingErrors.TryFailure(
                "Phase-I 最小化失败：人工成本符号不存在 / Phase-I minimisation failed: the artificial-cost symbol does not exist");
        }
        model.AddObject(ObjectCategory.Minimum, cost.Polynomial, Name, Name);
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>Phase-I 目标不需要影子价格提取 / The Phase-I objective needs no shadow-price extraction.</summary>
    /// <returns>始终为 null / Always null.</returns>
    public ShadowPriceExtractor<VrpShadowPriceArguments, VrpShadowPriceMap>? Extractor() => null;
}

/// <summary>
/// 路线成本最小化管线 / Route-cost minimisation pipeline.
/// </summary>
/// <remarks>
/// Phase II 最小化路线成本：`min routeCost`。Phase I 时此管线不激活（由 <see cref="PhaseOneMinimization{V}"/> 代替）。
/// Phase II minimises route cost: `min routeCost`. In Phase I this pipeline is inactive (replaced by
/// <see cref="PhaseOneMinimization{V}"/>).
/// **C# 侧注册的是 `routeCost` 此刻的多项式快照，不是符号本身**：`AddObject` 只接受展平数据。因此 Phase II 在注册过这条目标之后，
/// 每加入一批新列都必须调用 `RouteCompilation.RegisterRouteCostObjective` 为新列补目标系数。漏掉的话新列在目标里系数为 0——
/// 它们对求解器是完全免费的，模型会大量选用，总成本却看起来正常。
/// **The C# side registers a snapshot of `routeCost` at this moment, not the symbol itself**: `AddObject` only accepts flatten data.
/// So once Phase II has registered this objective, every batch of new columns must be followed by
/// `RouteCompilation.RegisterRouteCostObjective` to add their objective coefficients. Skip that and the new columns carry coefficient
/// zero — they are entirely free to the solver, it will use many of them, and the reported total cost still looks plausible.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class RouteCostMinimization<V>
    : ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>
    where V : struct, IRealNumber<V> {
    private readonly RouteCompilation<V> _compilation;

    /// <summary>
    /// 构造路线成本最小化管线 / Construct the route-cost minimisation pipeline.
    /// </summary>
    /// <param name="compilation">路线编译模型 / The route compilation model.</param>
    /// <param name="name">目标名称 / Objective name.</param>
    public RouteCostMinimization(RouteCompilation<V> compilation, string name = "route_cost_minimization") {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(name);
        _compilation = compilation;
        Name = name;
    }

    /// <summary>管线名称 / Pipeline name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => Name;

    /// <summary>
    /// 注册 Phase-II 路线成本最小化 / Register the Phase-II route-cost minimisation.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_compilation.RouteCost is not { } routeCost) {
            return NetworkSchedulingErrors.TryFailure(
                "路线成本最小化失败：路线成本符号不存在 / Route-cost minimisation failed: the route-cost symbol does not exist");
        }
        model.AddObject(ObjectCategory.Minimum, routeCost.Polynomial, Name, Name);
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>Phase-II 目标不需要影子价格提取 / The Phase-II objective needs no shadow-price extraction.</summary>
    /// <returns>始终为 null / Always null.</returns>
    public ShadowPriceExtractor<VrpShadowPriceArguments, VrpShadowPriceMap>? Extractor() => null;
}
