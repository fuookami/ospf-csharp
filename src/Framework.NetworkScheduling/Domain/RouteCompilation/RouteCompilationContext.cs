// 路线编译上下文 / Route compilation context.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Service.Limits;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Shadow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;
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
/// 路线编译上下文 / Route compilation context.
/// </summary>
/// <remarks>
/// 显式覆盖完整列生成生命周期：`Register` → `AddColumns` → `RemoveColumns` → `RefreshShadowPrice` /
/// `ExtractShadowPrice` → `SwitchToPhaseTwo` → `ExtractRouteValues` → `ExtractSolution`。
/// Explicitly covers the whole column-generation lifecycle: `Register` → `AddColumns` → `RemoveColumns` →
/// `RefreshShadowPrice` / `ExtractShadowPrice` → `SwitchToPhaseTwo` → `ExtractRouteValues` → `ExtractSolution`.
/// **扩展贡献的生命周期边界是明确的**：
/// - 扩展通过管线列表参与 `Register` 与影子价格（`Refresh` + `Extractor`）；
/// - `AddColumns` / `RemoveColumns` 只改路线变量与相关中间符号表达式，不动管线里已注册的约束结构；
/// - `ExtractRouteValues` / `ExtractSolution` 只从路线列池提解，扩展的约束值由模型自身管理。
/// **The lifecycle boundary of extra contributions is explicit**.
/// - contributions take part in `Register` and shadow pricing (`Refresh` + `Extractor`) via the pipeline list;
/// - `AddColumns` / `RemoveColumns` only touch route variables and related intermediate-symbol expressions, never the already
///   registered constraint structure in the pipelines;
/// - `ExtractRouteValues` / `ExtractSolution` only extract from the route column pool; contribution constraint values are managed by
///   the model itself.
/// **阶段切换是单向的**：`SwitchToPhaseTwo` 一旦进入 Phase II 就不再回头。回头需要重建模型，因为 Phase I 的人工变量此时已固定在 0，
/// 把它们放开会让已收敛的解失去可行性。
/// **The phase switch is one-way**: once `SwitchToPhaseTwo` enters Phase II it does not go back. Going back would require rebuilding
/// the model, because the Phase-I artificial variables are pinned to zero by then and releasing them would make the converged
/// solution infeasible.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class RouteCompilationContext<V> where V : struct, IRealNumber<V> {
    private readonly IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>
        _extraPipelines;
    private readonly CustomerCoverageConstraint<V> _customerCoverageConstraint;
    private readonly FleetSizeConstraint<V> _fleetSizeConstraint;
    private IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>> _pipelineList;
    private LinearMetaModel<Flt64>? _registeredModel;

    /// <summary>
    /// 构造路线编译上下文 / Construct the route compilation context.
    /// </summary>
    /// <param name="instance">VRPTW 实例 / The VRPTW instance.</param>
    /// <param name="valueAdapter">数值边界适配器 / The numeric-boundary adapter.</param>
    /// <param name="extraPipelines">额外的业务管线 / Extra business pipelines.</param>
    /// <param name="extraContributions">额外的贡献扩展 / Extra contribution extensions.</param>
    public RouteCompilationContext(
        VrptwInstance<V> instance,
        INetworkSchedulingSolverValueAdapter<V> valueAdapter,
        IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>? extraPipelines = null,
        IReadOnlyList<IRouteCompilationContribution<V>>? extraContributions = null) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(valueAdapter);
        Instance = instance;
        Aggregation = new RouteCompilationAggregation<V>(instance, valueAdapter);
        ExtraContributions = extraContributions?.ToList() ?? new List<IRouteCompilationContribution<V>>();
        _extraPipelines = extraPipelines?.ToList()
            ?? new List<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>>();
        _customerCoverageConstraint = new CustomerCoverageConstraint<V>(instance.Customers, Aggregation.Compilation);
        _fleetSizeConstraint = new FleetSizeConstraint<V>(instance.VehicleTypes, Aggregation.Compilation);
        _pipelineList = PipelineListGenerator.PhaseOne(
            instance,
            Aggregation.Compilation,
            _extraPipelines,
            _customerCoverageConstraint,
            _fleetSizeConstraint);
        Phase = PricingPhase.PhaseOne;
        ExtractedContributions = new Dictionary<string, object?>();
    }

    /// <summary>VRPTW 实例 / The VRPTW instance.</summary>
    public VrptwInstance<V> Instance { get; }

    /// <summary>聚合 / The aggregation.</summary>
    public RouteCompilationAggregation<V> Aggregation { get; }

    /// <summary>编译模型 / The compilation model.</summary>
    public RouteCompilation<V> Compilation => Aggregation.Compilation;

    /// <summary>模型专属的扩展贡献快照 / The model-local extra-contribution snapshot.</summary>
    public IReadOnlyList<IRouteCompilationContribution<V>> ExtraContributions { get; }

    /// <summary>当前管线列表 / The current pipeline list.</summary>
    public IReadOnlyList<ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap>> PipelineList =>
        _pipelineList;

    /// <summary>当前阶段 / The current phase.</summary>
    public PricingPhase Phase { get; private set; }

    /// <summary>最近一次解提取得到的额外贡献结果 / Extra-contribution values from the latest extraction.</summary>
    public IReadOnlyDictionary<string, object?> ExtractedContributions { get; private set; }

    /// <summary>
    /// 注册到模型 / Register to the model.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        // 重复注册必须整体幂等。聚合层本身已幂等（`RouteCompilation.Register` 内部有守卫），但管线调用没有：第二次调用会把覆盖行、
        // 车队行和目标再插一遍，模型里出现重复约束行，而聚合层却什么都没做——整个调用变成"半应用"状态。
        // Repeat registration must be idempotent as a whole. The aggregation layer already is (`RouteCompilation.Register` guards
        // internally), but pipeline invocation is not: a second call would insert the coverage rows, fleet rows and objective again,
        // leaving duplicate constraint rows in the model while the aggregation layer did nothing — the call ends up half-applied.
        if (ReferenceEquals(_registeredModel, model)) {
            return Results.Ok(Results.SuccessInstance);
        }
        Try aggregated = Aggregation.Register(model);
        if (aggregated is Failed<Success, ErrorCode, Error<ErrorCode>> aggregationFailure) {
            return Results.Failed<Success>(aggregationFailure.Error);
        }
        Try pipelines = InvokePipelines(model);
        if (pipelines is Failed<Success, ErrorCode, Error<ErrorCode>> pipelineFailure) {
            return Results.Failed<Success>(pipelineFailure.Error);
        }
        foreach (IRouteCompilationContribution<V> contribution in ExtraContributions) {
            Try result = contribution.Register(Compilation, model);
            if (result is Failed<Success, ErrorCode, Error<ErrorCode>> contributionFailure) {
                return Results.Failed<Success>(contributionFailure.Error);
            }
        }
        _registeredModel = model;
        return Results.Ok(Results.SuccessInstance);
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
        Result<IReadOnlyList<Route<V>>, ErrorCode, Error<ErrorCode>> added =
            Aggregation.AddColumns(iteration, newRoutes, model);
        if (added is Failed<IReadOnlyList<Route<V>>, ErrorCode, Error<ErrorCode>>) {
            return added;
        }
        IReadOnlyList<Route<V>> addedRoutes =
            ((Ok<IReadOnlyList<Route<V>>, ErrorCode, Error<ErrorCode>>)added).Value;
        if (Phase == PricingPhase.PhaseTwo && addedRoutes.Count > 0) {
            // Phase II 每加一批列都要为新列补目标系数：注册过的目标只是快照，不会自己长大。
            // In Phase II every batch of new columns needs its objective coefficients: the registered objective is only a
            // snapshot and never grows by itself.
            Try objective = Compilation.RegisterRouteCostObjective(
                addedRoutes,
                model,
                $"route_cost_minimization_{iteration}");
            if (objective is Failed<Success, ErrorCode, Error<ErrorCode>> objectiveFailure) {
                return Results.Failed<IReadOnlyList<Route<V>>>(objectiveFailure.Error);
            }
        }
        foreach (IRouteCompilationContribution<V> contribution in ExtraContributions) {
            Result<IReadOnlyList<RouteCompilationColumn<V>>, ErrorCode, Error<ErrorCode>> columns =
                ColumnsFor(contribution, addedRoutes);
            if (columns is Failed<IReadOnlyList<RouteCompilationColumn<V>>, ErrorCode, Error<ErrorCode>> columnFailure) {
                return Results.Failed<IReadOnlyList<Route<V>>>(columnFailure.Error);
            }
            Try result = contribution.AddColumns(
                ((Ok<IReadOnlyList<RouteCompilationColumn<V>>, ErrorCode, Error<ErrorCode>>)columns).Value, model);
            if (result is Failed<Success, ErrorCode, Error<ErrorCode>> contributionFailure) {
                return Results.Failed<IReadOnlyList<Route<V>>>(contributionFailure.Error);
            }
        }
        return added;
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
        var bySignature = new Dictionary<string, Route<V>>(StringComparer.Ordinal);
        foreach (Route<V> route in Compilation.ColumnPool.Routes) {
            bySignature[route.Signature] = route;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var existing = new List<Route<V>>();
        foreach (Route<V> route in routesToRemove) {
            if (!bySignature.TryGetValue(route.Signature, out Route<V>? match) || !seen.Add(match.Signature)) {
                continue;
            }
            existing.Add(match);
        }
        foreach (IRouteCompilationContribution<V> contribution in ExtraContributions) {
            Result<IReadOnlyList<RouteCompilationColumn<V>>, ErrorCode, Error<ErrorCode>> columns =
                ColumnsFor(contribution, existing);
            if (columns is Failed<IReadOnlyList<RouteCompilationColumn<V>>, ErrorCode, Error<ErrorCode>> columnFailure) {
                return Results.Failed<Success>(columnFailure.Error);
            }
            Try result = contribution.RemoveColumns(
                ((Ok<IReadOnlyList<RouteCompilationColumn<V>>, ErrorCode, Error<ErrorCode>>)columns).Value, model);
            if (result is Failed<Success, ErrorCode, Error<ErrorCode>> contributionFailure) {
                return Results.Failed<Success>(contributionFailure.Error);
            }
        }
        return Aggregation.RemoveColumns(existing, model);
    }

    /// <summary>
    /// 提取影子价格 / Extract shadow prices.
    /// </summary>
    /// <param name="shadowPriceMap">影子价格映射 / The shadow-price map.</param>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="shadowPrices">对偶解 / The dual solution.</param>
    /// <returns>操作结果 / The operation result.</returns>
    public Try ExtractShadowPrice(
        VrpShadowPriceMap shadowPriceMap,
        LinearMetaModel<Flt64> model,
        MetaDualSolution shadowPrices) {
        ArgumentNullException.ThrowIfNull(shadowPriceMap);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(shadowPrices);
        foreach (ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap> pipeline in
                 _pipelineList) {
            Try refreshed = pipeline.Refresh(shadowPriceMap, model, shadowPrices);
            if (refreshed is Failed<Success, ErrorCode, Error<ErrorCode>> refreshFailure) {
                return Results.Failed<Success>(refreshFailure.Error);
            }
            if (pipeline.Extractor() is { } extractor) {
                shadowPriceMap.Put(extractor);
            }
        }
        var additionalRows = new HashSet<string>(StringComparer.Ordinal);
        foreach (IRouteCompilationContribution<V> contribution in ExtraContributions) {
            Result<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>> additional =
                contribution.RefreshShadowPrices(model, shadowPrices);
            if (additional is Failed<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>> additionalFailure) {
                return Results.Failed<Success>(additionalFailure.Error);
            }
            foreach ((string key, Flt64 value) in
                     ((Ok<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>>)additional).Value) {
                if (!additionalRows.Add(key)) {
                    return NetworkSchedulingErrors.TryFailure(
                        $"刷新扩展对偶失败：扩展行键 {key} 重复 / Failed to refresh extra duals: duplicate extra-row key {key}");
                }
                shadowPriceMap.PutAdditionalDual(key, value);
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 提取影子价格并输出 <see cref="PricingDuals"/> 快照 / Extract shadow prices and produce a <see cref="PricingDuals"/>
    /// snapshot.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="shadowPrices">对偶解 / The dual solution.</param>
    /// <returns>定价对偶快照 / The pricing-dual snapshot.</returns>
    public Result<PricingDuals, ErrorCode, Error<ErrorCode>> ExtractPricingDuals(
        LinearMetaModel<Flt64> model,
        MetaDualSolution shadowPrices) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(shadowPrices);
        var shadowPriceMap = new VrpShadowPriceMap();
        Try extracted = ExtractShadowPrice(shadowPriceMap, model, shadowPrices);
        if (extracted is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<PricingDuals>(failure.Error);
        }
        return Results.Ok(shadowPriceMap.ToPricingDuals(Phase));
    }

    /// <summary>
    /// 切换到 Phase II / Switch to Phase II.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <returns>操作结果 / The operation result.</returns>
    public Try SwitchToPhaseTwo(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (Phase == PricingPhase.PhaseTwo) {
            return Results.Ok(Results.SuccessInstance);
        }
        Try switched = Aggregation.SwitchToPhaseTwo(model);
        if (switched is Failed<Success, ErrorCode, Error<ErrorCode>> switchFailure) {
            return Results.Failed<Success>(switchFailure.Error);
        }
        Try objective = new RouteCostMinimization<V>(Compilation).Invoke(model);
        if (objective is Failed<Success, ErrorCode, Error<ErrorCode>> objectiveFailure) {
            return Results.Failed<Success>(objectiveFailure.Error);
        }
        _pipelineList = PipelineListGenerator.PhaseTwo(
            Instance,
            Compilation,
            _extraPipelines,
            _customerCoverageConstraint,
            _fleetSizeConstraint);
        Phase = PricingPhase.PhaseTwo;
        return Results.Ok(Results.SuccessInstance);
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
        return Aggregation.IsPhaseOneConverged(model, feasibilityTolerance);
    }

    /// <summary>
    /// 提取路线变量值 / Extract route variable values.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="integralityTolerance">整数容差；缺省取实例值 / The integrality tolerance; defaults to the instance value.</param>
    /// <returns>路线及其解值 / The routes and their solution values.</returns>
    public Result<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>> ExtractRouteValues(
        LinearMetaModel<Flt64> model,
        Flt64? integralityTolerance = null) {
        ArgumentNullException.ThrowIfNull(model);
        Result<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>> routeValues =
            Aggregation.ExtractRouteValues(model, integralityTolerance);
        if (routeValues is Failed<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>>) {
            return routeValues;
        }
        IReadOnlyList<(Route<V> Route, Flt64 Value)> extracted =
            ((Ok<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>>)routeValues).Value;
        Result<IReadOnlyDictionary<string, object?>, ErrorCode, Error<ErrorCode>> contributions =
            ExtractContributionValues(model, extracted);
        if (contributions is Failed<IReadOnlyDictionary<string, object?>, ErrorCode, Error<ErrorCode>> contributionFailure) {
            return Results.Failed<IReadOnlyList<(Route<V> Route, Flt64 Value)>>(contributionFailure.Error);
        }
        ExtractedContributions =
            ((Ok<IReadOnlyDictionary<string, object?>, ErrorCode, Error<ErrorCode>>)contributions).Value;
        return routeValues;
    }

    /// <summary>
    /// 提取解中的路线 / Extract the routes in the solution.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="integralityTolerance">整数容差；缺省取实例值 / The integrality tolerance; defaults to the instance value.</param>
    /// <returns>选中的路线 / The selected routes.</returns>
    public Result<IReadOnlyList<Route<V>>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model,
        Flt64? integralityTolerance = null) {
        ArgumentNullException.ThrowIfNull(model);
        Flt64 tolerance = integralityTolerance ?? Instance.Tolerances.Integrality;
        Result<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>> routeValues =
            ExtractRouteValues(model, tolerance);
        if (routeValues is Failed<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>> routeFailure) {
            return Results.Failed<IReadOnlyList<Route<V>>>(routeFailure.Error);
        }
        var selected = new List<Route<V>>();
        Flt64 threshold = Flt64.One.Minus(tolerance);
        foreach ((Route<V> route, Flt64 value) in
                 ((Ok<IReadOnlyList<(Route<V> Route, Flt64 Value)>, ErrorCode, Error<ErrorCode>>)routeValues).Value) {
            if (value.CompareTo(threshold) >= 0) {
                selected.Add(route);
            }
        }
        return Results.Ok<IReadOnlyList<Route<V>>>(selected);
    }

    /// <summary>
    /// 执行额外贡献的最终结果提取 / Extract the final results from every extra contribution.
    /// </summary>
    /// <param name="model">最终线性主问题 / The final linear master problem.</param>
    /// <param name="routeValues">路线列及其解值 / The route columns and their solution values.</param>
    /// <returns>按扩展名称索引的异构结果 / Heterogeneous results keyed by contribution name.</returns>
    public Result<IReadOnlyDictionary<string, object?>, ErrorCode, Error<ErrorCode>> ExtractContributionValues(
        LinearMetaModel<Flt64> model,
        IReadOnlyList<(Route<V> Route, Flt64 Value)> routeValues) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(routeValues);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (IRouteCompilationContribution<V> contribution in ExtraContributions) {
            // 名称为空或重复会让两个扩展的结果互相覆盖，而调用方只看得到其中一个。
            // A blank or duplicate name would make two extensions overwrite each other's results and the caller would only ever see
            // one of them.
            if (string.IsNullOrWhiteSpace(contribution.Name) || values.ContainsKey(contribution.Name)) {
                return Results.Failed<IReadOnlyDictionary<string, object?>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "提取扩展结果失败：扩展名称为空或重复 / Failed to extract extra results: blank or duplicate contribution "
                    + "name"));
            }
            Result<object?, ErrorCode, Error<ErrorCode>> extracted = contribution.Extract(model, routeValues);
            if (extracted is Failed<object?, ErrorCode, Error<ErrorCode>> extractionFailure) {
                return Results.Failed<IReadOnlyDictionary<string, object?>>(extractionFailure.Error);
            }
            values[contribution.Name] = ((Ok<object?, ErrorCode, Error<ErrorCode>>)extracted).Value;
        }
        return Results.Ok<IReadOnlyDictionary<string, object?>>(values);
    }

    /// <summary>
    /// 为额外贡献解析路线变量与同源可加弧系数 / Resolve route variables and shared additive arc coefficients for an extension.
    /// </summary>
    /// <param name="contribution">目标扩展贡献 / The target contribution.</param>
    /// <param name="routes">路线列 / The route columns.</param>
    /// <returns>扩展列快照，或系数转换失败 / The extension-column snapshot, or a coefficient-conversion failure.</returns>
    private Result<IReadOnlyList<RouteCompilationColumn<V>>, ErrorCode, Error<ErrorCode>> ColumnsFor(
        IRouteCompilationContribution<V> contribution,
        IReadOnlyList<Route<V>> routes) {
        var columns = new List<RouteCompilationColumn<V>>();
        foreach (Route<V> route in routes) {
            Result<Math.Symbol.Polynomial.LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> variable =
                Compilation.ColumnVariable(route);
            if (variable is Failed<Math.Symbol.Polynomial.LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> variableFailure) {
                return Results.Failed<IReadOnlyList<RouteCompilationColumn<V>>>(variableFailure.Error);
            }
            Math.Symbol.Polynomial.LinearPolynomial<Flt64> variableValue =
                ((Ok<Math.Symbol.Polynomial.LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>)variable).Value;
            IReadOnlyDictionary<string, V> coefficients = contribution.RouteCoefficients(route);
            var solverCoefficients = new Dictionary<string, Flt64>(StringComparer.Ordinal);
            foreach ((string key, V value) in coefficients) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> solverValue = Aggregation.ValueAdapter.ToSolverValue(value);
                if (solverValue is Failed<Flt64, ErrorCode, Error<ErrorCode>> solverFailure) {
                    return Results.Failed<IReadOnlyList<RouteCompilationColumn<V>>>(solverFailure.Error);
                }
                solverCoefficients[key] = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)solverValue).Value;
            }
            columns.Add(new RouteCompilationColumn<V>(route, variableValue, coefficients, solverCoefficients));
        }
        return Results.Ok<IReadOnlyList<RouteCompilationColumn<V>>>(columns);
    }

    /// <summary>
    /// 逐条调用管线 / Invoke every pipeline.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <returns>调用结果 / The invocation result.</returns>
    private Try InvokePipelines(LinearMetaModel<Flt64> model) {
        foreach (ICGPipeline<VrpShadowPriceArguments, LinearMetaModel<Flt64>, VrpShadowPriceMap> pipeline in
                 _pipelineList) {
            Try result = pipeline.Invoke(model);
            if (result is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<Success>(failure.Error);
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }
}
