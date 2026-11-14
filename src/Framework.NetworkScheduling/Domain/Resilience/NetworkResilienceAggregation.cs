// 韧性域：场景与连通聚合 / Resilience domain: the scenario and connectivity aggregation.
// 与固定 Kotlin 基线 `resilience/NetworkResilienceAggregation.kt`（15146 字节）对应。
// Mirrors the pinned baseline's `resilience/NetworkResilienceAggregation.kt`.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Resilience.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Resilience;

/// <summary>
/// 共享建设与显式场景的聚合 / The aggregation over shared construction and explicit scenarios.
/// </summary>
/// <remarks>
/// **结论只对显式场景集合成立**。场景没写进去的故障就是没被考虑；把结果当成"任意故障都可恢复"是这类模型最危险的误读。
/// **The conclusion holds only for the explicit scenario set.** A failure that was never written down was never considered; reading
/// the result as "any failure is recoverable" is the most dangerous misreading of this model.
/// **失效节点的供需默认仍然存在**。自动免除会让每个场景都自动可行——需求随节点一起消失，"韧性"成了同义反复。
/// **Demand at a failed node still exists by default.** Waiving it automatically would make every scenario trivially feasible — the
/// demand disappears with the node, and "resilience" becomes a tautology.
/// **降级容量与硬下界冲突时按可行域处理**。当场景上界低于名义下界，名义下界会打开一条"绕过故障"的通道；此时必须补一条按可用性缩放的
/// 硬下界行，否则场景"看起来可行"但实际在走已失效的服务。
/// **A degraded capacity below the nominal minimum is handled as a feasibility issue.** When the scenario upper bound sits below the
/// nominal lower bound, the nominal minimum would open a channel that bypasses the failure; a hard-minimum row scaled by
/// availability must be added, otherwise the scenario "looks feasible" while routing over a dead service.
/// **有效容量要同时满足四件事**：场景里该服务还在、两端设施都已开设、服务等级已被选、建成容量不超过声明容量。任一条不满足都应为零。
/// **An effective capacity must satisfy four conditions at once**: the service survives in the scenario, both endpoint facilities are
/// open, a service level was selected, and the built capacity does not exceed the declared one. Failing any of them yields zero.
/// **注册态与装配态分离**。`_registeredModel` 让同一模型的重复注册幂等；`_attempted` 一旦置位就拒绝复用，避免半装配模型被继续求解。
/// **Registration state is separated from assembly state.** `_registeredModel` makes repeated registration on the same model
/// idempotent; once `_attempted` is set, reuse is refused so a half-assembled model is never solved.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkResilienceAggregation<V> where V : struct, IRealNumber<V> {
    private readonly Dictionary<string, FlowContext<V>> _scenarioFlows = new(StringComparer.Ordinal);
    private readonly List<ConnectivityFlow<V>> _witnesses = new();
    private readonly LinearPolynomial<Flt64> _worstCost;
    private LinearMetaModel<Flt64>? _registeredModel;
    private bool _attempted;

    /// <summary>
    /// 构造韧性聚合 / Construct the resilience aggregation.
    /// </summary>
    /// <param name="design">共享结构决策上下文 / The shared structural-decision context.</param>
    /// <param name="scenarios">显式故障场景 / The explicit failure scenarios.</param>
    /// <param name="connectivity">连通要求 / The connectivity requirements.</param>
    /// <param name="name">模型命名空间 / The model namespace.</param>
    public NetworkResilienceAggregation(
        NetworkDesignContext<V> design,
        IReadOnlyList<FailureScenario<V>>? scenarios = null,
        IReadOnlyList<ConnectivityRequirement>? connectivity = null,
        string name = "network_resilience") {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(name);
        Design = design;
        Scenarios = scenarios?.ToList() ?? new List<FailureScenario<V>>();
        Connectivity = connectivity?.ToList() ?? new List<ConnectivityRequirement>();
        Name = name;
        // 变量名必须含命名空间，否则多实例会在同一模型里撞名。 / The variable name must embed the namespace, or two instances
        // would collide inside one model.
        _worstCost = new LinearPolynomial<Flt64>(
            new[] {
                new Math.Symbol.Monomial.LinearMonomial<Flt64>(
                    Flt64.One, new Core.Variable.RealVar($"{name}_worst_operation")),
            },
            Flt64.Zero);
    }

    /// <summary>共享结构决策上下文 / The shared structural-decision context.</summary>
    public NetworkDesignContext<V> Design { get; }

    /// <summary>显式故障场景 / The explicit failure scenarios.</summary>
    public IReadOnlyList<FailureScenario<V>> Scenarios { get; }

    /// <summary>连通要求 / The connectivity requirements.</summary>
    public IReadOnlyList<ConnectivityRequirement> Connectivity { get; }

    /// <summary>模型命名空间 / The model namespace.</summary>
    public string Name { get; }

    /// <summary>已注册的场景流 / The registered scenario flows.</summary>
    public IReadOnlyDictionary<string, FlowContext<V>> ScenarioFlows => _scenarioFlows;

    /// <summary>已注册的连通见证 / The registered connectivity witnesses.</summary>
    public IReadOnlyList<ConnectivityFlow<V>> Witnesses => _witnesses;

    /// <summary>
    /// 获取已注册场景流，供业务追加约束 / Access a registered scenario flow for business extensions.
    /// </summary>
    /// <param name="scenario">场景标识，名义场景为 nominal / The scenario identity; nominal for the base case.</param>
    /// <returns>场景上下文或未注册错误 / The scenario context, or a registration error.</returns>
    public Result<FlowContext<V>, ErrorCode, Error<ErrorCode>> Flow(string scenario) {
        ArgumentNullException.ThrowIfNull(scenario);
        if (_registeredModel is null) {
            return Failure<FlowContext<V>>("韧性模型尚未注册 / Resilience model is not registered");
        }
        return _scenarioFlows.TryGetValue(scenario, out FlowContext<V>? context)
            ? Results.Ok(context)
            : Failure<FlowContext<V>>($"场景不存在：{scenario} / Unknown scenario: {scenario}");
    }

    /// <summary>
    /// 注册场景模型、连通辅助流和最坏费用目标 / Register scenarios, connectivity flows and the worst-cost objective.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (ReferenceEquals(_registeredModel, model)) {
            return Results.Ok(Results.SuccessInstance);
        }
        if (_attempted) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "韧性上下文不可复用 / Resilience context cannot be reused"));
        }
        _attempted = true;
        var ids = Scenarios.Select(scenario => scenario.Id).ToList();
        if (ids.Any(id => string.IsNullOrWhiteSpace(id) || id == "nominal")
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "场景标识重复、为空或占用 nominal / Duplicate, blank or reserved scenario identity"));
        }
        Try designRegistration = Design.Register(model, false);
        if (designRegistration is Failed<Success, ErrorCode, Error<ErrorCode>> designFailure) {
            return Results.Failed<Success>(designFailure.Error);
        }
        // 最坏费用变量必须先显式注册，否则下面的 `cost <= worst` 行会引用一个不存在的符号。
        // The worst-cost variable must be registered explicitly, or the `cost <= worst` rows below would reference a missing symbol.
        Try worstAdded = model.Add((Core.Variable.RealVar)_worstCost.Monomials[0].Symbol);
        if (worstAdded is Failed<Success, ErrorCode, Error<ErrorCode>> worstAddFailure) {
            return Results.Failed<Success>(worstAddFailure.Error);
        }
        _scenarioFlows["nominal"] = Design.Flow;
        foreach (FailureScenario<V> scenario in Scenarios) {
            Result<FlowProblem<V>, ErrorCode, Error<ErrorCode>> problem = ScenarioProblem(scenario);
            if (problem is Failed<FlowProblem<V>, ErrorCode, Error<ErrorCode>> problemFailure) {
                return Results.Failed<Success>(problemFailure.Error);
            }
            var context = new FlowContext<V>(
                ((Ok<FlowProblem<V>, ErrorCode, Error<ErrorCode>>)problem).Value,
                Design.Adapter,
                $"{Name}_{scenario.Id}");
            Try attached = Design.AttachFlow(model, context);
            if (attached is Failed<Success, ErrorCode, Error<ErrorCode>> attachFailure) {
                return Results.Failed<Success>(attachFailure.Error);
            }
            Try hardMinimums = ApplyHardMinimums(model, context);
            if (hardMinimums is Failed<Success, ErrorCode, Error<ErrorCode>> minimumFailure) {
                return Results.Failed<Success>(minimumFailure.Error);
            }
            _scenarioFlows[scenario.Id] = context;
        }
        foreach ((string id, FlowContext<V> flow) in _scenarioFlows) {
            Try worst = model.AddConstraint(
                flow.Cost.Le(_worstCost),
                null,
                name: $"{Name}_{id}_worst");
            if (worst is Failed<Success, ErrorCode, Error<ErrorCode>> worstFailure) {
                return Results.Failed<Success>(worstFailure.Error);
            }
        }
        for (int index = 0; index < Connectivity.Count; index++) {
            var witness = new ConnectivityFlow<V>(Design, Connectivity[index], index, Name);
            Try registered = witness.Register(model);
            if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> witnessFailure) {
                return Results.Failed<Success>(witnessFailure.Error);
            }
            _witnesses.Add(witness);
        }
        // 目标最小化建设费用加最坏运行费用。 / Minimise construction cost plus the worst operating cost.
        Try objective = model.AddObject(
            Core.Model.Basic.ObjectCategory.Minimum,
            Design.ConstructionCost + _worstCost,
            $"{Name}_cost",
            $"{Name}_cost");
        if (objective is Failed<Success, ErrorCode, Error<ErrorCode>> objectiveFailure) {
            return Results.Failed<Success>(objectiveFailure.Error);
        }
        _registeredModel = model;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 提取所有场景及辅助连通流 / Extract every scenario and the connectivity witnesses.
    /// </summary>
    /// <param name="model">已求解模型 / The solved model.</param>
    /// <returns>韧性结果 / The resilience solution.</returns>
    public Result<NetworkResilienceSolution<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (!ReferenceEquals(_registeredModel, model)) {
            return Failure<NetworkResilienceSolution<V>>(
                "韧性模型未注册 / Resilience model not registered");
        }
        Result<NetworkDesignSolution<V>, ErrorCode, Error<ErrorCode>> designSolution =
            Design.ExtractSolution(model);
        if (designSolution is Failed<NetworkDesignSolution<V>, ErrorCode, Error<ErrorCode>> designFailure) {
            return Results.Failed<NetworkResilienceSolution<V>>(designFailure.Error);
        }
        NetworkDesignSolution<V> design =
            ((Ok<NetworkDesignSolution<V>, ErrorCode, Error<ErrorCode>>)designSolution).Value;
        var solutions = new Dictionary<string, FlowSolution<V>>(StringComparer.Ordinal);
        foreach ((string id, FlowContext<V> flow) in _scenarioFlows) {
            Result<FlowSolution<V>, ErrorCode, Error<ErrorCode>> extracted = flow.ExtractSolution(model);
            if (extracted is Failed<FlowSolution<V>, ErrorCode, Error<ErrorCode>> flowFailure) {
                return Results.Failed<NetworkResilienceSolution<V>>(flowFailure.Error);
            }
            solutions[id] = ((Ok<FlowSolution<V>, ErrorCode, Error<ErrorCode>>)extracted).Value;
        }
        var connectivity = new List<IReadOnlyDictionary<NetworkArcId, V>>();
        foreach (ConnectivityFlow<V> witness in _witnesses) {
            var values = new Dictionary<NetworkArcId, V>();
            foreach ((NetworkArcId id, Core.Variable.URealVar variable) in witness.Variables) {
                if (!TryReadVariable(model, variable, out V value)) {
                    return Failure<NetworkResilienceSolution<V>>(
                        $"缺失连通见证：{id.Value} / Missing connectivity witness: {id.Value}");
                }
                values[id] = value;
            }
            connectivity.Add(values);
        }
        var capacities = new Dictionary<string, IReadOnlyDictionary<NetworkArcId, Quantity<V>>>(
            StringComparer.Ordinal);
        foreach ((string id, FlowContext<V> context) in _scenarioFlows) {
            Result<IReadOnlyDictionary<NetworkArcId, Quantity<V>>, ErrorCode, Error<ErrorCode>> effective =
                EffectiveCapacities(context, design);
            if (effective is
                Failed<IReadOnlyDictionary<NetworkArcId, Quantity<V>>, ErrorCode, Error<ErrorCode>>
                effectiveFailure) {
                return Results.Failed<NetworkResilienceSolution<V>>(effectiveFailure.Error);
            }
            capacities[id] =
                ((Ok<IReadOnlyDictionary<NetworkArcId, Quantity<V>>, ErrorCode, Error<ErrorCode>>)effective).Value;
        }
        string worstScenario = "nominal";
        foreach ((string id, FlowSolution<V> solution) in solutions) {
            if (solution.Cost.Value.CompareTo(solutions[worstScenario].Cost.Value) > 0) {
                worstScenario = id;
            }
        }
        return Results.Ok(new NetworkResilienceSolution<V>(
            design,
            solutions,
            new Quantity<V>(
                ((Ok<V, ErrorCode, Error<ErrorCode>>)ReadWorstCost(model)).Value,
                Design.Problem.Flow.CostUnit),
            connectivity,
            capacities,
            worstScenario));
    }

    /// <summary>
    /// 为降级容量低于名义下界的场景补一条硬下界行 / Add a hard-minimum row where degradation falls below the nominal minimum.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="context">场景流上下文 / The scenario flow context.</param>
    /// <returns>注册结果 / The registration result.</returns>
    private Try ApplyHardMinimums(LinearMetaModel<Flt64> model, FlowContext<V> context) {
        foreach (FlowArc<V> arc in context.Problem.Arcs) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> lower = Design.Adapter.Normalize(
                NominalCapacity(arc.NetworkArc.Id).Lower, arc.Capacity.Upper.Unit);
            if (lower is Failed<Flt64, ErrorCode, Error<ErrorCode>> lowerFailure) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "场景下界单位不兼容 / Incompatible scenario minimum unit"));
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> upper = Design.Adapter.Normalize(
                arc.Capacity.Upper, arc.Capacity.Upper.Unit);
            if (upper is Failed<Flt64, ErrorCode, Error<ErrorCode>> upperFailure) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "场景上界无效 / Invalid scenario upper capacity"));
            }
            Flt64 lowerValue = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)lower).Value;
            Flt64 upperValue = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)upper).Value;
            if (lowerValue.CompareTo(upperValue) <= 0) {
                continue;
            }
            Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> load =
                context.Load($"arc:{arc.NetworkArc.Id.Value}");
            if (load is Failed<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> loadFailure) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "场景流表达式缺失 / Missing scenario flow expression"));
            }
            LinearPolynomial<Flt64> availability = Design.Aggregation.Availability(arc.NetworkArc.Id);
            LinearPolynomial<Flt64> scaled = lowerValue * availability;
            Try added = model.AddConstraint(
                ((Ok<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>>)load).Value - scaled,
                null,
                name: $"{context.Name}_{arc.NetworkArc.Id.Value}_hard_minimum");
            if (added is Failed<Success, ErrorCode, Error<ErrorCode>> addFailure) {
                return Results.Failed<Success>(addFailure.Error);
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 计算各场景的建成且有效容量 / Compute the built and effective capacities per scenario.
    /// </summary>
    /// <param name="context">场景流上下文 / The scenario flow context.</param>
    /// <param name="design">结构决策结果 / The structural-decision solution.</param>
    /// <returns>有效容量表或错误 / The effective-capacity table, or an error.</returns>
    private Result<IReadOnlyDictionary<NetworkArcId, Quantity<V>>, ErrorCode, Error<ErrorCode>>
        EffectiveCapacities(FlowContext<V> context, NetworkDesignSolution<V> design) {
        var result = new Dictionary<NetworkArcId, Quantity<V>>();
        foreach (FlowArc<V> arc in Design.Problem.Flow.Arcs) {
            NetworkArcId id = arc.NetworkArc.Id;
            PhysicalUnit unit = arc.Capacity.Upper.Unit;
            FlowArc<V>? scenarioArc =
                context.Problem.Arcs.FirstOrDefault(candidate => candidate.NetworkArc.Id.Equals(id));
            NetworkService<V>? service =
                Design.Problem.Services.FirstOrDefault(candidate => candidate.Arc.Equals(id));
            CapacityLevel<V>? selected = null;
            if (service is not null && design.ServiceLevels.TryGetValue(service.Arc, out string? levelId)) {
                selected = service.Levels.FirstOrDefault(level => level.Id == levelId);
            }
            bool facilityClosed = Design.Problem.Facilities.Any(facility =>
                (facility.Node.Equals(arc.NetworkArc.From) || facility.Node.Equals(arc.NetworkArc.To))
                && !design.OpenFacilities.Contains(facility.Node));
            if (scenarioArc is null || facilityClosed || (service is not null && selected is null)) {
                result[id] = new Quantity<V>(Design.Problem.Flow.Zero, unit);
                continue;
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> declared = Design.Adapter.Normalize(
                scenarioArc.Capacity.Upper, unit);
            if (declared is Failed<Flt64, ErrorCode, Error<ErrorCode>> declaredFailure) {
                return Results.Failed<IReadOnlyDictionary<NetworkArcId, Quantity<V>>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "场景容量结果换算失败 / Scenario capacity result conversion failed"));
            }
            Flt64 declaredValue = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)declared).Value;
            Flt64 builtValue = declaredValue;
            if (selected is not null) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> built =
                    Design.Adapter.Normalize(selected.Capacity, unit);
                if (built is Failed<Flt64, ErrorCode, Error<ErrorCode>> builtFailure) {
                    return Results.Failed<IReadOnlyDictionary<NetworkArcId, Quantity<V>>>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        "建成容量结果换算失败 / Built capacity conversion failed"));
                }
                builtValue = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)built).Value;
            }
            Flt64 available = builtValue.CompareTo(declaredValue) < 0 ? builtValue : declaredValue;
            Result<V, ErrorCode, Error<ErrorCode>> restored = Design.Adapter.FromSolverValue(available);
            if (restored is Failed<V, ErrorCode, Error<ErrorCode>> restoreFailure) {
                return Results.Failed<IReadOnlyDictionary<NetworkArcId, Quantity<V>>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "有效容量回写失败 / Effective capacity could not be converted back"));
            }
            result[id] = new Quantity<V>(
                ((Ok<V, ErrorCode, Error<ErrorCode>>)restored).Value, unit);
        }
        return Results.Ok<IReadOnlyDictionary<NetworkArcId, Quantity<V>>>(result);
    }

    /// <summary>
    /// 构造场景流问题 / Build a scenario flow problem.
    /// </summary>
    /// <param name="scenario">故障场景 / The failure scenario.</param>
    /// <returns>场景流问题或校验错误 / The scenario flow problem, or a validation error.</returns>
    private Result<FlowProblem<V>, ErrorCode, Error<ErrorCode>> ScenarioProblem(FailureScenario<V> scenario) {
        FlowProblem<V> nominal = Design.Problem.Flow;
        var arcIds = nominal.Arcs.Select(arc => arc.NetworkArc.Id).ToHashSet();
        var commodityIds = nominal.Commodities
            .Select(commodity => commodity.Commodity.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (scenario.ClosedArcs.Concat(scenario.UpperCapacities.Keys).Any(id => !arcIds.Contains(id))
            || scenario.FailedNodes.Any(node => !nominal.Graph.Nodes.Any(candidate => candidate.Id.Equals(node)))
            || scenario.Balances.Keys.Any(key => !commodityIds.Contains(key))) {
            return Failure<FlowProblem<V>>("故障场景引用不存在 / Failure scenario references unknown assets");
        }
        var arcs = new List<FlowArc<V>>();
        foreach (FlowArc<V> arc in nominal.Arcs) {
            NetworkArcId id = arc.NetworkArc.Id;
            Quantity<V> upper = scenario.UpperCapacities.TryGetValue(id, out Quantity<V>? degraded)
                ? degraded
                : arc.Capacity.Upper;
            Result<Flt64, ErrorCode, Error<ErrorCode>> newValue = Design.Adapter.Normalize(
                upper, arc.Capacity.Upper.Unit);
            if (newValue is Failed<Flt64, ErrorCode, Error<ErrorCode>> newFailure) {
                return Failure<FlowProblem<V>>(
                    "降级容量单位不兼容 / Incompatible degraded capacity unit");
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> oldValue = Design.Adapter.Normalize(
                arc.Capacity.Upper, arc.Capacity.Upper.Unit);
            if (oldValue is Failed<Flt64, ErrorCode, Error<ErrorCode>> oldFailure) {
                return Failure<FlowProblem<V>>("名义容量无效 / Invalid nominal capacity");
            }
            Flt64 degradedValue = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)newValue).Value;
            Flt64 nominalValue = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)oldValue).Value;
            if (degradedValue.CompareTo(Flt64.Zero) < 0 || degradedValue.CompareTo(nominalValue) > 0) {
                return Failure<FlowProblem<V>>(
                    "故障容量必须非负且不高于名义容量 / Failure capacity must be nonnegative and no greater than nominal");
            }
            if (scenario.ClosedArcs.Contains(id)
                || scenario.FailedNodes.Contains(arc.NetworkArc.From)
                || scenario.FailedNodes.Contains(arc.NetworkArc.To)) {
                continue;
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> minimum = Design.Adapter.Normalize(
                arc.Capacity.Lower, arc.Capacity.Upper.Unit);
            if (minimum is Failed<Flt64, ErrorCode, Error<ErrorCode>> minimumFailure) {
                return Failure<FlowProblem<V>>(
                    "名义容量下界无效 / Invalid nominal capacity minimum");
            }
            Quantity<V> lower = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)minimum).Value.CompareTo(degradedValue) > 0
                ? new Quantity<V>(nominal.Zero, upper.Unit)
                : arc.Capacity.Lower;
            Result<CapacityBounds<V>, ErrorCode, Error<ErrorCode>> bounds =
                CapacityBounds<V>.Invoke(lower, upper);
            if (bounds is Failed<CapacityBounds<V>, ErrorCode, Error<ErrorCode>> boundFailure) {
                return Results.Failed<FlowProblem<V>>(boundFailure.Error);
            }
            Result<FlowArc<V>, ErrorCode, Error<ErrorCode>> rebuilt = FlowArc<V>.Invoke(
                arc.NetworkArc, ((Ok<CapacityBounds<V>, ErrorCode, Error<ErrorCode>>)bounds).Value);
            if (rebuilt is Failed<FlowArc<V>, ErrorCode, Error<ErrorCode>> rebuildFailure) {
                return Failure<FlowProblem<V>>("故障服务无效 / Invalid failed service");
            }
            arcs.Add(((Ok<FlowArc<V>, ErrorCode, Error<ErrorCode>>)rebuilt).Value);
        }
        var active = arcs.Select(arc => arc.NetworkArc.Id).ToHashSet();
        NetworkGraph<NetworkNode<V>, NetworkArc<V>> graph =
            nominal.Graph.Filtered(arc => active.Contains(arc.Id));
        var commodities = new List<CommodityFlow<V>>();
        foreach (CommodityFlow<V> product in nominal.Commodities) {
            IReadOnlyList<SupplyDemand<V>> balances =
                scenario.Balances.TryGetValue(product.Commodity.Id, out IReadOnlyList<SupplyDemand<V>>? overridden)
                    ? overridden
                    : product.Commodity.SupplyDemands;
            Result<FlowCommodity<V>, ErrorCode, Error<ErrorCode>> commodity =
                FlowCommodity<V>.Invoke(product.Commodity.Id, balances);
            if (commodity is Failed<FlowCommodity<V>, ErrorCode, Error<ErrorCode>> commodityFailure) {
                return Failure<FlowProblem<V>>("场景供需无效 / Invalid scenario balances");
            }
            commodities.Add(new CommodityFlow<V>(
                ((Ok<FlowCommodity<V>, ErrorCode, Error<ErrorCode>>)commodity).Value,
                product.Unit,
                product.IntegerStep,
                product.Costs.Where(pair => active.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value),
                product.AllowedArcs?.Where(active.Contains).ToHashSet()));
        }
        var capacityUsage = nominal.CapacityUsage
            .Where(pair => active.Contains(pair.Key.Arc))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var resources = nominal.Resources
            .Select(row => new FlowCapacity<V>(
                row.Id,
                row.Bounds,
                row.Usage.Where(pair => active.Contains(pair.Key.Arc))
                    .ToDictionary(pair => pair.Key, pair => pair.Value)))
            .ToList();
        return Results.Ok(new FlowProblem<V>(
            graph,
            arcs,
            commodities,
            nominal.Zero,
            nominal.CostUnit,
            capacityUsage,
            resources,
            scenario.Slacks ?? nominal.Slacks));
    }

    /// <summary>
    /// 读取最坏运行费用 / Read the worst operating cost.
    /// </summary>
    /// <param name="model">已求解模型 / The solved model.</param>
    /// <returns>费用或错误 / The cost, or an error.</returns>
    private Result<V, ErrorCode, Error<ErrorCode>> ReadWorstCost(LinearMetaModel<Flt64> model) {
        var symbol = (Core.Variable.RealVar)_worstCost.Monomials[0].Symbol;
        if (!TryReadVariable(model, symbol, out Flt64 value)) {
            return Failure<V>("缺失最坏费用结果 / Missing worst-cost result");
        }
        return Design.Adapter.FromSolverValue(value);
    }

    /// <summary>
    /// 读取一个变量的求解值 / Read a solved variable value.
    /// </summary>
    /// <param name="model">已求解模型 / The solved model.</param>
    /// <param name="variable">变量 / The variable.</param>
    /// <param name="value">求解值 / The solved value.</param>
    /// <returns>是否读取成功 / Whether the read succeeded.</returns>
    private static bool TryReadVariable(LinearMetaModel<Flt64> model, Core.Variable.RealVar variable, out Flt64 value) {
        value = Flt64.Zero;
        Core.Token.Token<Flt64>? token = model.Tokens.Find(variable);
        if (token?.Result is not { } result) {
            return false;
        }
        value = result;
        return true;
    }

    /// <summary>
    /// 读取一个实数变量的求解值 / Read a solved real-variable value.
    /// </summary>
    /// <param name="model">已求解模型 / The solved model.</param>
    /// <param name="variable">变量 / The variable.</param>
    /// <param name="value">求解值 / The solved value.</param>
    /// <returns>是否读取成功 / Whether the read succeeded.</returns>
    private bool TryReadVariable(
        LinearMetaModel<Flt64> model,
        Core.Variable.URealVar variable,
        out V value) {
        value = default;
        Core.Token.Token<Flt64>? token = model.Tokens.Find(variable);
        if (token?.Result is not { } result) {
            return false;
        }
        // 走适配器而不是强转：`V` 是抽象数值类型，强转只在 `V` 恰好是 `Flt64` 时成立。
        // Go through the adapter rather than a cast: `V` is an abstract numeric type, and a cast only holds when `V` is `Flt64`.
        Result<V, ErrorCode, Error<ErrorCode>> converted = Design.Adapter.FromSolverValue(result);
        if (converted is Failed<V, ErrorCode, Error<ErrorCode>> conversionFailure) {
            return false;
        }
        value = ((Ok<V, ErrorCode, Error<ErrorCode>>)converted).Value;
        return true;
    }

    /// <summary>
    /// 取名义容量 / The nominal capacity of an arc.
    /// </summary>
    /// <param name="id">服务标识 / The service identity.</param>
    /// <returns>名义容量 / The nominal capacity.</returns>
    private CapacityBounds<V> NominalCapacity(NetworkArcId id) =>
        Design.Problem.Flow.Arcs.First(arc => arc.NetworkArc.Id.Equals(id)).Capacity;

    /// <summary>构造失败结果 / Build a failure result.</summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="message">错误信息 / The error message.</param>
    /// <returns>失败结果 / The failure result.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
