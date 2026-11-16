// 网络建设聚合与上下文 / Network-design aggregation and context.
// `NetworkDesignContext` 类（5875 字节）对应。
// And the `NetworkDesignContext` class.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Service.Limits;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;

/// <summary>
/// 共用结构变量的模型上下文；场景可复用其建设决策 / Model context owning structural variables shared by scenarios.
/// </summary>
/// <remarks>
/// **场景流必须挂到同一个模型的同一组建设变量上**。若每个场景各自注册一份建设变量，同一批设施会在模型里出现两次，费用被计两遍，
/// 而两个副本还可以取到互相矛盾的值——建设决策的正确性依赖于"全局只有一份"。
/// **Scenario flows must attach to the same construction variables in the same model.** Registering a second copy of the construction
/// variables per scenario charges every facility twice and lets the two copies disagree; the correctness of the construction decision
/// depends on there being exactly one global copy.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkDesignAggregation<V> where V : struct, IRealNumber<V> {
    private readonly NetworkDesignProblem<V> _problem;
    private readonly INetworkSchedulingSolverValueAdapter<V> _adapter;
    private readonly string _name;
    private readonly HashSet<FlowContext<V>> _attachedFlows = new(ReferenceEqualityComparer.Instance);
    private bool? _registeredObjective;
    private bool _registered;
    private LinearMetaModel<Flt64>? _model;
    private bool _attempted;
    private bool _structureReady;

    /// <summary>
    /// 构造建设聚合 / Construct the design aggregation.
    /// </summary>
    /// <param name="problem">建设问题 / Design problem.</param>
    /// <param name="adapter">数值边界 / Numeric boundary.</param>
    /// <param name="name">模型命名空间 / Model namespace.</param>
    public NetworkDesignAggregation(
        NetworkDesignProblem<V> problem,
        INetworkSchedulingSolverValueAdapter<V> adapter,
        string name = "network_design") {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(name);
        _problem = problem;
        _adapter = adapter;
        _name = name;
        Flow = new FlowContext<V>(problem.Flow, adapter, $"{name}_nominal");
        Construction = new NetworkConstruction<V>(problem, adapter, name);
    }

    /// <summary>名义流上下文 / The nominal flow context.</summary>
    public FlowContext<V> Flow { get; }

    /// <summary>建设变量组件 / The construction-variable component.</summary>
    public NetworkConstruction<V> Construction { get; }

    /// <summary>建设费用表达式 / The construction-cost expression.</summary>
    public LinearPolynomial<Flt64> ConstructionCost => Construction.ConstructionCost;

    /// <summary>服务启用表达式 / Service activation expression.</summary>
    /// <param name="arc">服务弧 / Service arc.</param>
    /// <returns>启用量表达式 / The activation expression.</returns>
    public LinearPolynomial<Flt64> Availability(NetworkArcId arc) => Construction.Availability(arc);

    /// <summary>设施启用表达式 / Facility activation expression.</summary>
    /// <param name="node">设施节点 / Facility node.</param>
    /// <returns>启用量表达式 / The activation expression.</returns>
    public LinearPolynomial<Flt64> FacilityAvailability(NetworkNodeId node) =>
        Construction.FacilityAvailability(node);

    /// <summary>
    /// 注册建设、名义流及可选目标 / Register construction, nominal flows and an optional objective.
    /// </summary>
    /// <param name="target">元模型 / Meta model.</param>
    /// <param name="minimizeCost">是否最小化建设加名义运行费用 / Whether to minimise construction plus nominal operation.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> target, bool minimizeCost = true) {
        ArgumentNullException.ThrowIfNull(target);
        if (_registered && ReferenceEquals(_model, target)) {
            return _registeredObjective == minimizeCost
                ? Results.Ok(Results.SuccessInstance)
                : NetworkSchedulingErrors.TryFailure(
                    "重复建设注册选项不一致 / Inconsistent repeated design registration options");
        }
        if (_attempted) {
            return NetworkSchedulingErrors.TryFailure(
                "建设上下文不可复用 / Design context cannot be reused");
        }
        _attempted = true;

        Try construction = Construction.Register(target);
        if (construction is Failed<Success, ErrorCode, Error<ErrorCode>> constructionFailure) {
            return Results.Failed<Success>(constructionFailure.Error);
        }
        _model = target;
        _structureReady = true;
        Try nominal = AttachFlow(target, Flow);
        if (nominal is Failed<Success, ErrorCode, Error<ErrorCode>> nominalFailure) {
            return Results.Failed<Success>(nominalFailure.Error);
        }
        if (minimizeCost) {
            target.AddObject(
                Core.Model.Basic.ObjectCategory.Minimum,
                Add(ConstructionCost, Flow.Cost),
                $"{_name}_cost",
                $"{_name}_cost");
        }
        _registeredObjective = minimizeCost;
        _registered = true;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 将独立场景流连接到同一组建设变量 / Attach scenario flows to the same construction variables.
    /// </summary>
    /// <remarks>
    /// **受控通道必须有正的容量消耗**。场景弧的上界要靠"容量消耗"与建设等级变量联动才能推出有限的取值；消耗为零时这条联动退化成
    /// `0 ≤ 容量 × 启用量`，对启用量毫无约束，于是"没建设也能满负荷运行"。
    /// **A controlled service must have positive capacity usage.** The scenario arc's bound links capacity usage to the construction level
    /// variables; with zero usage that link degenerates into `0 ≤ capacity × activation`, which constrains the activation not at all, so
    /// an unbuilt service can still run at full load.
    /// </remarks>
    /// <param name="target">元模型 / Meta model.</param>
    /// <param name="context">场景流 / Scenario flow.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try AttachFlow(LinearMetaModel<Flt64> target, FlowContext<V> context) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);
        if (!_structureReady || !ReferenceEquals(_model, target) || (!_registered && !ReferenceEquals(context, Flow))) {
            return NetworkSchedulingErrors.TryFailure(
                "场景必须使用已完成建设注册的同一模型 / Scenarios require completed design registration in the same model");
        }
        if (_attachedFlows.Contains(context)) {
            return Results.Ok(Results.SuccessInstance);
        }

        var facilityRows = new HashSet<string>(StringComparer.Ordinal);
        foreach (NetworkFacility<V> facility in _problem.Facilities) {
            facilityRows.UnionWith(facility.CapacityRows);
        }
        var resourceIds = new HashSet<string>(
            context.Problem.Resources.Select(resource => resource.Id), StringComparer.Ordinal);
        if (facilityRows.Any(id => !resourceIds.Contains(id))) {
            return NetworkSchedulingErrors.TryFailure(
                "设施处理容量行不存在 / Missing facility processing-capacity row");
        }
        Result<IReadOnlyList<FlowCapacity<V>>, ErrorCode, Error<ErrorCode>> capacities =
            context.Problem.Capacities();
        if (capacities is Failed<IReadOnlyList<FlowCapacity<V>>, ErrorCode, Error<ErrorCode>> capacityFailure) {
            return Results.Failed<Success>(capacityFailure.Error);
        }
        var capacityRows = capacities.Value.ToDictionary(row => row.Id, StringComparer.Ordinal);

        var serviceIds = new HashSet<NetworkArcId>(_problem.Services.Select(service => service.Arc));
        foreach (Domain.Flow.Model.FlowArc<V> arc in context.Problem.Arcs) {
            NetworkArcId arcId = arc.NetworkArc.Id;
            if (!serviceIds.Contains(arcId)
                && !Construction.Facilities.ContainsKey(arc.From)
                && !Construction.Facilities.ContainsKey(arc.To)) {
                continue;
            }
            // 容量行由 `Capacities` 按 `arc:<id>` 命名生成；缺失即表示场景弧没有可联动的资源。
            // Rows are named `arc:<id>` by `Capacities`; a miss means the scenario arc has no resource to link against.
            if (!capacityRows.TryGetValue($"arc:{arcId.Value}", out FlowCapacity<V>? row)) {
                return NetworkSchedulingErrors.TryFailure(
                    $"缺失通道容量行：{arcId.Value} / Missing service capacity row: {arcId.Value}");
            }
            foreach ((FlowKey key, Quantity<V> usage) in row.Usage) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> coefficient =
                    _adapter.Normalize(usage, row.Bounds.Upper.Unit);
                if (coefficient is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                    return NetworkSchedulingErrors.TryFailure(
                        $"通道用量单位无效：{key.Commodity}/{key.Arc.Value} / Invalid service usage unit: "
                        + $"{key.Commodity}/{key.Arc.Value}");
                }
                if (coefficient.Value.CompareTo(Flt64.Zero) <= 0) {
                    return NetworkSchedulingErrors.TryFailure(
                        $"受控通道需要正容量消耗以推导有限联动上界：{key.Commodity}/{key.Arc.Value} / Controlled services require "
                        + $"positive capacity usage for a finite linking bound: {key.Commodity}/{key.Arc.Value}");
                }
            }
        }

        var owned = new HashSet<string>(
            context.Problem.Arcs
                .Where(arc => serviceIds.Contains(arc.NetworkArc.Id))
                .Select(arc => $"arc:{arc.NetworkArc.Id.Value}"),
            StringComparer.Ordinal);
        owned.UnionWith(facilityRows);
        Try registered = context.Register(target, owned, minimizeCost: false);
        if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> registerFailure) {
            return Results.Failed<Success>(registerFailure.Error);
        }

        foreach (Domain.Flow.Model.FlowArc<V> arc in context.Problem.Arcs) {
            NetworkArcId arcId = arc.NetworkArc.Id;
            Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> scenarioLoadResult =
                context.Load($"arc:{arcId.Value}");
            if (scenarioLoadResult is Failed<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> loadFailure) {
                return Results.Failed<Success>(loadFailure.Error);
            }
            Domain.Flow.Model.FlowArc<V>? nominal = _problem.Flow.Arcs.FirstOrDefault(
                candidate => candidate.NetworkArc.Id.Equals(arcId));
            if (nominal is null) {
                return NetworkSchedulingErrors.TryFailure(
                    "场景引用未知通道 / Scenario references unknown service");
            }
            if (!nominal.NetworkArc.From.Equals(arc.NetworkArc.From)
                || !nominal.NetworkArc.To.Equals(arc.NetworkArc.To)) {
                return NetworkSchedulingErrors.TryFailure(
                    "场景不能改写稳定通道端点 / Scenarios cannot rewrite stable service endpoints");
            }
            PhysicalUnit unit = nominal.Capacity.Upper.Unit;
            Result<Flt64, ErrorCode, Error<ErrorCode>> loadScale = _adapter.Normalize(
                new Quantity<V>(_problem.Flow.Zero.Constants.One, arc.Capacity.Upper.Unit), unit);
            if (loadScale is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.TryFailure(
                    "场景容量量纲不兼容 / Incompatible scenario capacity dimension");
            }
            LinearPolynomial<Flt64> load = Scale(loadScale.Value, scenarioLoadResult.Value);
            Result<Flt64, ErrorCode, Error<ErrorCode>> nominalUpper =
                _adapter.Normalize(nominal.Capacity.Upper, unit);
            Result<Flt64, ErrorCode, Error<ErrorCode>> scenarioUpper =
                _adapter.Normalize(arc.Capacity.Upper, unit);
            if (nominalUpper is Failed<Flt64, ErrorCode, Error<ErrorCode>>
                || scenarioUpper is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.TryFailure(
                    "名义或场景容量无效 / Invalid nominal or scenario capacity");
            }
            if (scenarioUpper.Value.CompareTo(nominalUpper.Value) > 0) {
                return NetworkSchedulingErrors.TryFailure(
                    "场景容量不能高于名义容量 / Scenario capacity cannot exceed nominal capacity");
            }

            NetworkService<V>? service = _problem.Services.FirstOrDefault(
                candidate => candidate.Arc.Equals(arcId));
            if (service is not null) {
                LinearPolynomial<Flt64> upper = Zero();
                IReadOnlyList<Core.Variable.BinVar> levelVariables = Construction.Services[arcId];
                for (int index = 0; index < service.Levels.Count; index++) {
                    Result<Flt64, ErrorCode, Error<ErrorCode>> capacity =
                        _adapter.Normalize(service.Levels[index].Capacity, unit);
                    if (capacity is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                        return NetworkSchedulingErrors.TryFailure(
                            "等级容量无效 / Invalid level capacity");
                    }
                    upper = Add(
                        upper,
                        Scale(capacity.Value, Variable(levelVariables[index])));
                }
                Result<Flt64, ErrorCode, Error<ErrorCode>> lower =
                    _adapter.Normalize(arc.Capacity.Lower, unit);
                if (lower is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                    return NetworkSchedulingErrors.TryFailure(
                        "容量下界无效 / Invalid lower bound");
                }
                Try capacityConstraint = new NetworkCapacityConstraint(
                    load,
                    Scale(lower.Value, Availability(arcId)),
                    upper,
                    $"{context.Name}_{arcId.Value}_design").Invoke(target);
                if (capacityConstraint is Failed<Success, ErrorCode, Error<ErrorCode>> capacityConstraintFailure) {
                    return Results.Failed<Success>(capacityConstraintFailure.Error);
                }
                Try available = target.AddConstraint(
                    load.Le(Constant(scenarioUpper.Value)), null,
                    name: $"{context.Name}_{arcId.Value}_available_capacity");
                if (available is Failed<Success, ErrorCode, Error<ErrorCode>> availableFailure) {
                    return Results.Failed<Success>(availableFailure.Error);
                }
            }

            Flt64? levelMaximum = null;
            if (service is not null) {
                foreach (CapacityLevel<V> level in service.Levels) {
                    Result<Flt64, ErrorCode, Error<ErrorCode>> capacity =
                        _adapter.Normalize(level.Capacity, unit);
                    if (capacity is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                        continue;
                    }
                    levelMaximum = levelMaximum is null || capacity.Value.CompareTo(levelMaximum.Value) > 0
                        ? capacity.Value
                        : levelMaximum;
                }
            }
            Flt64 maxCapacity = levelMaximum is { } maximum && maximum.CompareTo(scenarioUpper.Value) < 0
                ? maximum
                : scenarioUpper.Value;

            // 端点设施联动：没有设施的地方，通道不能承载流量。
            // Endpoint-facility linkage: without a facility at an endpoint the service carries no flow.
            foreach (NetworkNodeId node in new[] { arc.NetworkArc.From, arc.NetworkArc.To }.Distinct()) {
                if (!Construction.Facilities.ContainsKey(node)) {
                    continue;
                }
                Try linkage = target.AddConstraint(
                    load.Le(Scale(maxCapacity, FacilityAvailability(node))), null,
                    name: $"{context.Name}_{arcId.Value}_{node.Value}_facility");
                if (linkage is Failed<Success, ErrorCode, Error<ErrorCode>> linkageFailure) {
                    return Results.Failed<Success>(linkageFailure.Error);
                }
            }
        }

        foreach (NetworkFacility<V> facility in _problem.Facilities) {
            foreach (string id in facility.CapacityRows) {
                FlowCapacity<V>? row = context.Problem.Resources.FirstOrDefault(
                    resource => string.Equals(resource.Id, id, StringComparison.Ordinal));
                if (row is null) {
                    return NetworkSchedulingErrors.TryFailure(
                        "设施容量行不存在 / Missing facility capacity row");
                }
                Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> loadResult = context.Load(id);
                if (loadResult is Failed<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> facilityLoadFailure) {
                    return Results.Failed<Success>(facilityLoadFailure.Error);
                }
                Result<Flt64, ErrorCode, Error<ErrorCode>> upper =
                    _adapter.Normalize(row.Bounds.Upper, row.Bounds.Upper.Unit);
                Result<Flt64, ErrorCode, Error<ErrorCode>> lower =
                    _adapter.Normalize(row.Bounds.Lower, row.Bounds.Upper.Unit);
                if (upper is Failed<Flt64, ErrorCode, Error<ErrorCode>>
                    || lower is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                    return NetworkSchedulingErrors.TryFailure(
                        "设施容量无效 / Invalid facility capacity");
                }
                LinearPolynomial<Flt64> activation = FacilityAvailability(facility.Node);
                Try facilityConstraint = new NetworkCapacityConstraint(
                    loadResult.Value,
                    Scale(lower.Value, activation),
                    Scale(upper.Value, activation),
                    $"{context.Name}_{id}_facility").Invoke(target);
                if (facilityConstraint is Failed<Success, ErrorCode, Error<ErrorCode>> facilityConstraintFailure) {
                    return Results.Failed<Success>(facilityConstraintFailure.Error);
                }
            }
        }

        _attachedFlows.Add(context);
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 提取建设选择与名义流 / Extract construction choices and nominal flows.
    /// </summary>
    /// <param name="target">已求解模型 / Solved model.</param>
    /// <returns>建设结果 / The design solution.</returns>
    public Result<NetworkDesignSolution<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> target) {
        ArgumentNullException.ThrowIfNull(target);
        if (!_registered || !ReferenceEquals(_model, target)) {
            return NetworkSchedulingErrors.Failure<NetworkDesignSolution<V>>(
                "建设模型未注册 / Design model not registered");
        }
        Result<NetworkConstructionSolution<V>, ErrorCode, Error<ErrorCode>> decisions =
            Construction.ExtractSolution(target);
        if (decisions is Failed<NetworkConstructionSolution<V>, ErrorCode, Error<ErrorCode>> decisionFailure) {
            return Results.Failed<NetworkDesignSolution<V>>(decisionFailure.Error);
        }
        Result<FlowSolution<V>, ErrorCode, Error<ErrorCode>> flowSolution = Flow.ExtractSolution(target);
        if (flowSolution is Failed<FlowSolution<V>, ErrorCode, Error<ErrorCode>> flowFailure) {
            return Results.Failed<NetworkDesignSolution<V>>(flowFailure.Error);
        }
        return Results.Ok(new NetworkDesignSolution<V>(
            decisions.Value.OpenFacilities,
            decisions.Value.ServiceLevels,
            decisions.Value.Cost,
            flowSolution.Value));
    }

    /// <summary>构造常数多项式 / Build a constant polynomial.</summary>
    /// <param name="value">常数值 / The constant.</param>
    /// <returns>常数多项式 / The constant polynomial.</returns>
    private static LinearPolynomial<Flt64> Constant(Flt64 value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), value);

    /// <summary>构造零多项式 / Build the zero polynomial.</summary>
    /// <returns>零多项式 / The zero polynomial.</returns>
    private static LinearPolynomial<Flt64> Zero() => Constant(Flt64.Zero);

    /// <summary>把变量包装成一次多项式 / Wrap a variable as a linear polynomial.</summary>
    /// <param name="variable">变量 / The variable.</param>
    /// <returns>一次多项式 / The linear polynomial.</returns>
    private static LinearPolynomial<Flt64> Variable(Core.Variable.IVariableItem variable) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, variable) }, Flt64.Zero);

    /// <summary>按标量缩放表达式 / Scale an expression by a scalar.</summary>
    /// <param name="factor">标量 / The scalar.</param>
    /// <param name="polynomial">表达式 / The expression.</param>
    /// <returns>缩放后的表达式 / The scaled expression.</returns>
    private static LinearPolynomial<Flt64> Scale(Flt64 factor, LinearPolynomial<Flt64> polynomial) =>
        new(
            polynomial.Monomials
                .Select(term => new LinearMonomial<Flt64>(factor.Times(term.Coefficient), term.Symbol))
                .ToList(),
            factor.Times(polynomial.Constant));

    /// <summary>相加两个表达式 / Add two expressions.</summary>
    /// <param name="left">左式 / Left operand.</param>
    /// <param name="right">右式 / Right operand.</param>
    /// <returns>和 / The sum.</returns>
    private static LinearPolynomial<Flt64> Add(LinearPolynomial<Flt64> left, LinearPolynomial<Flt64> right) =>
        new(
            left.Monomials.Concat(right.Monomials).ToList(),
            left.Constant.Plus(right.Constant));
}

/// <summary>
/// 结构决策注册入口，共享模型聚合可供场景和业务规则使用 / Structural registration entry with a shared model aggregate.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkDesignContext<V> where V : struct, IRealNumber<V> {
    private readonly NetworkDesignAggregation<V> _aggregation;

    /// <summary>
    /// 构造结构决策上下文 / Construct the structural decision context.
    /// </summary>
    /// <param name="problem">建设问题 / Design problem.</param>
    /// <param name="adapter">数值边界 / Numeric boundary.</param>
    /// <param name="name">模型命名空间 / Model namespace.</param>
    public NetworkDesignContext(
        NetworkDesignProblem<V> problem,
        INetworkSchedulingSolverValueAdapter<V> adapter,
        string name = "network_design") {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(adapter);
        Problem = problem;
        Adapter = adapter;
        Name = name;
        _aggregation = new NetworkDesignAggregation<V>(problem, adapter, name);
    }

    /// <summary>建设问题 / The design problem.</summary>
    public NetworkDesignProblem<V> Problem { get; }

    /// <summary>数值边界 / The numeric boundary.</summary>
    public INetworkSchedulingSolverValueAdapter<V> Adapter { get; }

    /// <summary>模型命名空间 / The model namespace.</summary>
    public string Name { get; }

    /// <summary>模型聚合 / The model aggregation.</summary>
    public NetworkDesignAggregation<V> Aggregation => _aggregation;

    /// <summary>名义流上下文 / The nominal flow context.</summary>
    public FlowContext<V> Flow => _aggregation.Flow;

    /// <summary>建设费用表达式 / The construction-cost expression.</summary>
    public LinearPolynomial<Flt64> ConstructionCost => _aggregation.ConstructionCost;

    /// <summary>
    /// 注册建设模型 / Register the design model.
    /// </summary>
    /// <param name="target">元模型 / Meta model.</param>
    /// <param name="minimizeCost">是否注册内置费用目标 / Whether to register the built-in cost objective.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> target, bool minimizeCost = true) =>
        _aggregation.Register(target, minimizeCost);

    /// <summary>
    /// 把场景流挂到同一组建设变量 / Attach a scenario flow to the same construction variables.
    /// </summary>
    /// <param name="target">元模型 / Meta model.</param>
    /// <param name="context">场景流 / Scenario flow.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try AttachFlow(LinearMetaModel<Flt64> target, FlowContext<V> context) =>
        _aggregation.AttachFlow(target, context);

    /// <summary>
    /// 提取建设结果 / Extract the design solution.
    /// </summary>
    /// <param name="target">已求解模型 / Solved model.</param>
    /// <returns>建设结果 / The design solution.</returns>
    public Result<NetworkDesignSolution<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> target) =>
        _aggregation.ExtractSolution(target);
}
