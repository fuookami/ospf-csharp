// 路线编译聚合 / Route compilation aggregation.
// 与固定 Kotlin （3748 字节）对应。

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Shadow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation;

/// <summary>
/// 路线编译聚合 / Route compilation aggregation.
/// </summary>
/// <remarks>
/// 包装 <see cref="RouteCompilation{V}"/>，提供 `Register`、`AddColumns`、`RemoveColumns`、`ExtractRouteValues`、
/// `ExtractSolution`、`SwitchToPhaseTwo` 等高层操作，并持有一份可变的管线列表。
/// Wraps <see cref="RouteCompilation{V}"/> with the high-level operations `Register`, `AddColumns`, `RemoveColumns`,
/// `ExtractRouteValues`, `ExtractSolution` and `SwitchToPhaseTwo`, and holds a mutable pipeline list.
/// **管线列表是可变的、且与编译模型的生命周期绑定**：`AddPipeline` 只登记，真正生效要等到下一次 `Register`（或调用方显式逐条
/// `Invoke`）。这不是缺陷，而是让"装配阶段"与"求解阶段"分开——求解中途加管线会让已注册的约束结构失效，而模型不会察觉。
/// **The pipeline list is mutable and tied to the compilation model's lifecycle**: `AddPipeline` only records; a pipeline takes effect
/// at the next `Register` (or when the caller explicitly invokes it). That is not a flaw: it keeps assembly and solving separate —
/// adding a pipeline mid-solve invalidates already-registered constraint structure and the model would never notice.
/// **容差从实例取默认值**，而不是硬编码常量：可行性容差与整数性容差是实例的属性，不同实例可以有不同取值。
/// **Tolerances default from the instance** rather than from hard-coded constants: feasibility and integrality tolerances are instance
/// properties, and different instances may carry different values.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class RouteCompilationAggregation<V> where V : struct, IRealNumber<V> {
    private readonly List<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>> _pipelines = new();

    /// <summary>
    /// 构造路线编译聚合 / Construct the route compilation aggregation.
    /// </summary>
    /// <param name="instance">VRPTW 实例 / The VRPTW instance.</param>
    /// <param name="valueAdapter">数值边界适配器 / The numeric-boundary adapter.</param>
    public RouteCompilationAggregation(
        VrptwInstance<V> instance,
        INetworkSchedulingSolverValueAdapter<V> valueAdapter) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(valueAdapter);
        Instance = instance;
        ValueAdapter = valueAdapter;
        Compilation = new RouteCompilation<V>(instance, valueAdapter);
    }

    /// <summary>VRPTW 实例 / The VRPTW instance.</summary>
    public VrptwInstance<V> Instance { get; }

    /// <summary>数值边界适配器 / The numeric-boundary adapter.</summary>
    public INetworkSchedulingSolverValueAdapter<V> ValueAdapter { get; }

    /// <summary>路线编译模型 / The route compilation model.</summary>
    public RouteCompilation<V> Compilation { get; }

    /// <summary>管线列表快照 / A snapshot of the pipeline list.</summary>
    public IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>> PipelineList =>
        _pipelines.ToList();

    /// <summary>
    /// 登记一条管线 / Record a pipeline.
    /// </summary>
    /// <param name="pipeline">管线 / The pipeline.</param>
    public void AddPipeline(ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap> pipeline) {
        ArgumentNullException.ThrowIfNull(pipeline);
        _pipelines.Add(pipeline);
    }

    /// <summary>
    /// 注册到模型 / Register to the model.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return Compilation.Register(model);
    }

    /// <summary>
    /// 添加路线列 / Add route columns.
    /// </summary>
    /// <param name="iteration">迭代序号 / The iteration index.</param>
    /// <param name="newRoutes">新路线 / The new routes.</param>
    /// <param name="model">元模型 / The meta model.</param>
    /// <returns>实际加入的路线 / The routes actually added.</returns>
    public Result<IReadOnlyList<Route<V>>, ErrorCode, Error<ErrorCode>> AddColumns(
        ulong iteration,
        IReadOnlyList<Route<V>> newRoutes,
        LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(newRoutes);
        ArgumentNullException.ThrowIfNull(model);
        return Compilation.AddColumns(iteration, newRoutes, model);
    }

    /// <summary>
    /// 移除路线列 / Remove route columns.
    /// </summary>
    /// <param name="routesToRemove">待移除的路线 / The routes to remove.</param>
    /// <param name="model">元模型 / The meta model.</param>
    /// <returns>操作结果 / The operation result.</returns>
    public Try RemoveColumns(IReadOnlyList<Route<V>> routesToRemove, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(routesToRemove);
        ArgumentNullException.ThrowIfNull(model);
        return Compilation.RemoveColumns(routesToRemove, model);
    }

    /// <summary>
    /// 切换到 Phase II / Switch to Phase II.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <returns>操作结果 / The operation result.</returns>
    public Try SwitchToPhaseTwo(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return Compilation.SwitchToPhaseTwo(model);
    }

    /// <summary>
    /// 检查 Phase I 是否收敛 / Check whether Phase I has converged.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="feasibilityTolerance">可行性容差；缺省取实例值 / The feasibility tolerance; defaults to the instance value.</param>
    /// <returns>是否收敛 / Whether it converged.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> IsPhaseOneConverged(
        LinearMetaModel<Flt64> model,
        Flt64? feasibilityTolerance = null) {
        ArgumentNullException.ThrowIfNull(model);
        // 未注册时 ArtificialCoverage 尚不存在，直接解引用会抛 NullReferenceException 而不是返回失败结果。
        // Before registration ArtificialCoverage does not exist yet; dereferencing it would throw NullReferenceException
        // instead of returning a failure result.
        if (Compilation.ArtificialCoverage is not { } coverage) {
            return Results.Failed<bool>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "Phase-I 收敛检查失败：路线编译模型尚未注册 / Phase-I convergence check failed: the "
                + "route compilation model is not registered"));
        }
        return coverage.IsPhaseOneConverged(
            model,
            feasibilityTolerance ?? Instance.Tolerances.Feasibility);
    }

    /// <summary>
    /// 提取路线变量值 / Extract route variable values.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="integralityTolerance">整数性容差；缺省取实例值 / The integrality tolerance; defaults to the instance value.</param>
    /// <returns>路线及其解值 / The routes and their solution values.</returns>
    public Result<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>> ExtractRouteValues(
        LinearMetaModel<Flt64> model,
        Flt64? integralityTolerance = null) {
        ArgumentNullException.ThrowIfNull(model);
        return Compilation.ExtractRouteValues(
            model,
            integralityTolerance ?? Instance.Tolerances.Integrality);
    }

    /// <summary>
    /// 提取解中的路线 / Extract the routes in the solution.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="integralityTolerance">整数性容差；缺省取实例值 / The integrality tolerance; defaults to the instance value.</param>
    /// <returns>解中的路线 / The routes in the solution.</returns>
    public Result<IReadOnlyList<Route<V>>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model,
        Flt64? integralityTolerance = null) {
        ArgumentNullException.ThrowIfNull(model);
        return Compilation.ExtractSolution(
            model,
            integralityTolerance ?? Instance.Tolerances.Integrality);
    }
}
