// 网络流聚合与上下文 / Network-flow aggregation and context.
// 与固定 Kotlin 基线 `FlowContext.kt` 的 `FlowContext` 类、`FlowAggregation.kt`（19647 字节）对应。
// Mirrors the `FlowContext` class from `FlowContext.kt` and the whole of `FlowAggregation.kt`.
//
// 聚合持有**每模型独立的注册态**（变量、表达式、松弛、行表），问题本身仍是不可变数据。生命周期被三条规则钉死：
// 同一模型重复注册必须选项一致；不可跨模型复用；失败后不可复用。
// The aggregation owns **per-model registration state** (variables, expressions, slacks, rows) while the problem stays immutable data.
// Its lifecycle is pinned by three rules: repeated registration on the same model must use identical options; reuse across models is
// forbidden; reuse after a failure is forbidden.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow.Service.Limits;
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

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;

/// <summary>
/// 每模型独立的流聚合及注册入口 / Per-model flow aggregate and registration entry.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class FlowAggregation<V> where V : struct, IRealNumber<V> {
    private readonly FlowProblem<V> _problem;
    private readonly INetworkSchedulingSolverValueAdapter<V> _adapter;
    private readonly string _name;
    private readonly Dictionary<FlowKey, IVariableItem> _variables = new();
    private readonly Dictionary<string, Flt64> _stepValues = new();
    private readonly Dictionary<FlowKey, LinearPolynomial<Flt64>> _expressions = new();
    private readonly Dictionary<(string Commodity, NetworkNodeId Node), (URealVar Unused, URealVar Unmet)> _slackVariables
        = new();
    private readonly Dictionary<string, LinearPolynomial<Flt64>> _loads = new();
    private readonly Dictionary<string, LinearPolynomial<Flt64>> _productCosts = new();
    private List<FlowCapacity<V>> _rows = new();
    private LinearPolynomial<Flt64> _relaxationCost = Zero();
    private LinearPolynomial<Flt64> _cost = Zero();
    private (HashSet<string> OwnedRows, bool MinimizeCost)? _registeredOptions;
    private bool _attempted;
    private LinearMetaModel<Flt64>? _registeredModel;

    /// <summary>
    /// 构造流聚合 / Construct the flow aggregate.
    /// </summary>
    /// <param name="problem">不可变业务问题 / Immutable business problem.</param>
    /// <param name="adapter">数值边界 / Numeric boundary.</param>
    /// <param name="name">模型命名空间 / Model namespace.</param>
    public FlowAggregation(
        FlowProblem<V> problem,
        INetworkSchedulingSolverValueAdapter<V> adapter,
        string name = "network_flow") {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(name);
        _problem = problem;
        _adapter = adapter;
        _name = name;
    }

    /// <summary>已归一化费用表达式 / The normalised cost expression.</summary>
    public LinearPolynomial<Flt64> Cost => _cost;

    /// <summary>
    /// 商品单位下的流表达式 / The flow expression in the commodity unit.
    /// </summary>
    /// <param name="key">商品服务键 / Commodity-service key.</param>
    /// <returns>表达式或错误 / The expression, or an error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> Flow(FlowKey key) {
        ArgumentNullException.ThrowIfNull(key);
        return _expressions.TryGetValue(key, out LinearPolynomial<Flt64>? expression)
            ? Results.Ok(expression)
            : NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                $"流变量不存在：{key.Commodity}/{key.Arc.Value} / Flow variable does not exist: {key.Commodity}/{key.Arc.Value}");
    }

    /// <summary>
    /// 资源单位下的占用表达式 / The resource load expression.
    /// </summary>
    /// <param name="id">资源标识 / Resource identity.</param>
    /// <returns>表达式或错误 / The expression, or an error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> Load(string id) {
        ArgumentNullException.ThrowIfNull(id);
        return _loads.TryGetValue(id, out LinearPolynomial<Flt64>? expression)
            ? Results.Ok(expression)
            : NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                $"资源未注册：{id} / Resource not registered: {id}");
    }

    /// <summary>
    /// 注册变量、守恒、资源和费用 / Register flows, conservation, resources and cost.
    /// </summary>
    /// <remarks>
    /// **同一模型重复注册必须选项一致**：第二次用另一组 `ownedCapacityRows` 注册会让同一批容量行既被本组件约束、又被上层结构决策
    /// 约束，两条上界约束叠在一起相当于取较紧者——数值上不报错，但可行域被悄悄收窄，解因此变差。
    /// 另外三条校验（零值、商品唯一、弧与图一致）拦住的都是"模型能解但意义错误"的输入。
    /// **Repeated registration on the same model must use identical options**: registering a second time with a different
    /// `ownedCapacityRows` makes those capacity rows constrained both here and by the structural layer; the two upper bounds stack as
    /// the tighter one — no error, but the feasible region silently shrinks and the solution degrades. The other three checks (zero
    /// value, unique commodities, arcs matching the graph) all catch inputs that "solve fine but mean something wrong".
    /// </remarks>
    /// <param name="model">元模型 / Meta model.</param>
    /// <param name="ownedCapacityRows">被上层结构决策接管的资源行 / Resource rows owned by structural decisions.</param>
    /// <param name="minimizeCost">是否注册费用目标 / Whether to register a cost objective.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(
        LinearMetaModel<Flt64> model,
        IReadOnlySet<string>? ownedCapacityRows = null,
        bool minimizeCost = true) {
        ArgumentNullException.ThrowIfNull(model);
        IReadOnlySet<string> owned = ownedCapacityRows ?? new HashSet<string>();

        if (ReferenceEquals(_registeredModel, model)) {
            return _registeredOptions is { } options && options.MinimizeCost == minimizeCost
                && options.OwnedRows.SetEquals(owned)
                ? Results.Ok(Results.SuccessInstance)
                : NetworkSchedulingErrors.TryFailure("重复注册选项不一致 / Inconsistent repeated registration options");
        }
        if (_attempted) {
            return NetworkSchedulingErrors.TryFailure(
                "流上下文不可跨模型或失败后复用 / Flow context cannot be reused across models or after failure");
        }
        _attempted = true;

        IReadOnlyList<CommodityFlow<V>> products = _problem.Commodities;
        var arcIds = new HashSet<NetworkArcId>(_problem.Arcs.Select(arc => arc.NetworkArc.Id));
        Result<Flt64, ErrorCode, Error<ErrorCode>> zero = _adapter.ToSolverValue(_problem.Zero);
        if (zero is Failed<Flt64, ErrorCode, Error<ErrorCode>>
            || zero.Value.CompareTo(Flt64.Zero) != 0
            || products.Select(product => product.Commodity.Id).ToHashSet(StringComparer.Ordinal).Count
                != products.Count
            || arcIds.Count != _problem.Arcs.Count
            || !arcIds.SetEquals(_problem.Graph.Arcs.Select(arc => arc.Id))
            || _problem.Arcs.Any(arc =>
                !_problem.Graph.From(arc.NetworkArc).Equals(arc.From)
                || !_problem.Graph.To(arc.NetworkArc).Equals(arc.To))
            || _problem.CapacityUsage.Keys.Any(key =>
                !arcIds.Contains(key.Arc)
                || products.All(product => !string.Equals(product.Commodity.Id, key.Commodity, StringComparison.Ordinal)))) {
            return NetworkSchedulingErrors.TryFailure(
                "商品或服务弧定义无效 / Invalid commodities or services");
        }

        Result<IReadOnlyList<FlowCapacity<V>>, ErrorCode, Error<ErrorCode>> capacities = _problem.Capacities();
        if (capacities is Failed<IReadOnlyList<FlowCapacity<V>>, ErrorCode, Error<ErrorCode>> capacityFailure) {
            return Results.Failed<Success>(capacityFailure.Error);
        }
        _rows = capacities.Value.ToList();
        var rowIds = new HashSet<string>(_rows.Select(row => row.Id), StringComparer.Ordinal);
        if (rowIds.Count != _rows.Count || !owned.All(rowIds.Contains)) {
            return NetworkSchedulingErrors.TryFailure(
                "资源行重复或缺失 / Duplicate or missing capacity row");
        }

        foreach (CommodityFlow<V> product in products) {
            LinearPolynomial<Flt64> productCost = Zero();
            Flt64 step = Flt64.One;
            if (product.IntegerStep is { } integerStep) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> normalized =
                    _adapter.Normalize(integerStep, product.Unit);
                if (normalized is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                    return NetworkSchedulingErrors.TryFailure(
                        "商品单位、步长或引用无效 / Invalid commodity unit, step or reference");
                }
                step = normalized.Value;
            }
            if (!(Finite(step) && step.CompareTo(Flt64.Zero) > 0)
                || product.Commodity.SupplyDemands.Any(balance =>
                    _problem.Graph.Node(balance.NodeId) is null || !balance.Net.ConvertTo(product.Unit).IsOk)
                || (product.AllowedArcs is { } allowed && allowed.Any(id => !arcIds.Contains(id)))
                || product.Costs.Keys.Any(id => !arcIds.Contains(id))) {
                return NetworkSchedulingErrors.TryFailure(
                    "商品单位、步长或引用无效 / Invalid commodity unit, step or reference");
            }
            _stepValues[product.Commodity.Id] = step;

            foreach (Model.FlowArc<V> arc in _problem.Arcs) {
                if (product.AllowedArcs is { } allowedArcs && !allowedArcs.Contains(arc.NetworkArc.Id)) {
                    continue;
                }
                var key = new FlowKey(product.Commodity.Id, arc.NetworkArc.Id);
                IVariableItem variable = product.IntegerStep is null
                    ? new URealVar($"{_name}_{product.Commodity.Id}_{arc.NetworkArc.Id.Value}")
                    : new UIntVar($"{_name}_{product.Commodity.Id}_{arc.NetworkArc.Id.Value}");
                if (model.Add(variable) is Failed<Success, ErrorCode, Error<ErrorCode>> addFailure) {
                    return Results.Failed<Success>(addFailure.Error);
                }
                _variables[key] = variable;
                LinearPolynomial<Flt64> expression = Expression(variable, step);
                _expressions[key] = expression;

                Quantity<V> arcCost = product.Costs.TryGetValue(arc.NetworkArc.Id, out Quantity<V>? overridden)
                    ? overridden
                    : arc.Cost;
                Result<Flt64, ErrorCode, Error<ErrorCode>> coefficient =
                    _adapter.Normalize(arcCost, _problem.CostUnit);
                if (coefficient is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                    return NetworkSchedulingErrors.TryFailure(
                        "商品费用单位不兼容 / Incompatible commodity cost unit");
                }
                LinearPolynomial<Flt64> scaled = Scale(coefficient.Value, expression);
                _cost = Add(_cost, scaled);
                productCost = Add(productCost, scaled);
            }
            _productCosts[product.Commodity.Id] = productCost;
        }

        foreach (FlowCapacity<V> row in _rows) {
            LinearPolynomial<Flt64> expression = Zero();
            foreach ((FlowKey key, Quantity<V> usage) in row.Usage) {
                if (!_expressions.TryGetValue(key, out LinearPolynomial<Flt64>? flow)) {
                    return NetworkSchedulingErrors.TryFailure(
                        $"资源引用不可用服务：{key.Commodity}/{key.Arc.Value} / Resource references unavailable service: "
                        + $"{key.Commodity}/{key.Arc.Value}");
                }
                Result<Flt64, ErrorCode, Error<ErrorCode>> coefficient =
                    _adapter.Normalize(usage, row.Bounds.Upper.Unit);
                if (coefficient is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                    return NetworkSchedulingErrors.TryFailure(
                        "资源消耗单位不兼容 / Incompatible resource usage unit");
                }
                if (coefficient.Value.CompareTo(Flt64.Zero) < 0) {
                    return NetworkSchedulingErrors.TryFailure(
                        "资源消耗不能为负 / Resource usage must be nonnegative");
                }
                expression = Add(expression, Scale(coefficient.Value, flow));
            }
            _loads[row.Id] = expression;
            if (owned.Contains(row.Id)) {
                continue;
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> lower =
                _adapter.Normalize(row.Bounds.Lower, row.Bounds.Upper.Unit);
            if (lower is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.TryFailure("资源下界单位无效 / Invalid capacity lower unit");
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> upper =
                _adapter.Normalize(row.Bounds.Upper, row.Bounds.Upper.Unit);
            if (upper is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.TryFailure("资源上界无效 / Invalid capacity upper bound");
            }
            Try result = new CapacityBoundConstraint(expression, lower.Value, upper.Value, $"{_name}_{row.Id}")
                .Invoke(model);
            if (result is Failed<Success, ErrorCode, Error<ErrorCode>> boundFailure) {
                return Results.Failed<Success>(boundFailure.Error);
            }
        }

        foreach (FlowSlack<V> slack in _problem.Slacks) {
            (string Commodity, NetworkNodeId Node) key = (slack.Commodity, slack.Node);
            CommodityFlow<V>? product = products.FirstOrDefault(
                candidate => string.Equals(candidate.Commodity.Id, slack.Commodity, StringComparison.Ordinal));
            if (product is null) {
                return NetworkSchedulingErrors.TryFailure(
                    "松弛引用缺失商品 / Relaxation references missing commodity");
            }
            if (_slackVariables.ContainsKey(key) || _problem.Graph.Node(slack.Node) is null) {
                return NetworkSchedulingErrors.TryFailure(
                    "松弛节点重复或缺失 / Duplicate or missing relaxation node");
            }
            var unused = new URealVar($"{_name}_unused_{slack.Commodity}_{slack.Node.Value}");
            var unmet = new URealVar($"{_name}_unmet_{slack.Commodity}_{slack.Node.Value}");
            Result<Flt64, ErrorCode, Error<ErrorCode>> unusedBound =
                _adapter.Normalize(slack.UnusedSupply, product.Unit);
            Result<Flt64, ErrorCode, Error<ErrorCode>> unmetBound =
                _adapter.Normalize(slack.UnmetDemand, product.Unit);
            Result<Flt64, ErrorCode, Error<ErrorCode>> penalty =
                _adapter.Normalize(slack.Penalty, _problem.CostUnit);
            if (unusedBound is Failed<Flt64, ErrorCode, Error<ErrorCode>>
                || unmetBound is Failed<Flt64, ErrorCode, Error<ErrorCode>>
                || penalty is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.TryFailure(
                    "松弛上界和罚成本单位无效 / Invalid relaxation bound or penalty unit");
            }
            if (unusedBound.Value.CompareTo(Flt64.Zero) < 0
                || unmetBound.Value.CompareTo(Flt64.Zero) < 0
                || penalty.Value.CompareTo(Flt64.Zero) < 0) {
                return NetworkSchedulingErrors.TryFailure(
                    "松弛上界和罚成本不能为负 / Relaxation bounds and penalty must be nonnegative");
            }
            SupplyDemand<V>? balance = product.Commodity.SupplyDemands.FirstOrDefault(
                candidate => candidate.NodeId.Equals(slack.Node));
            Flt64 net = Flt64.Zero;
            if (balance is not null) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> normalized =
                    _adapter.Normalize(balance.Net, product.Unit);
                if (normalized is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                    return NetworkSchedulingErrors.TryFailure(
                        "松弛供需单位无效 / Invalid relaxation balance unit");
                }
                net = normalized.Value;
            }
            if (unusedBound.Value.CompareTo(MaxOf(net, Flt64.Zero)) > 0
                || unmetBound.Value.CompareTo(MaxOf(Flt64.Zero.Minus(net), Flt64.Zero)) > 0) {
                return NetworkSchedulingErrors.TryFailure(
                    "松弛上界不能超过对应方向的供给或需求 / Relaxation bounds cannot exceed supply or demand in the "
                    + "corresponding direction");
            }
            foreach (IVariableItem variable in new IVariableItem[] { unused, unmet }) {
                if (model.Add(variable) is Failed<Success, ErrorCode, Error<ErrorCode>> addFailure) {
                    return Results.Failed<Success>(addFailure.Error);
                }
            }
            foreach (Try bound in new[] {
                UpperBound(model, unused, unusedBound.Value, $"{_name}_unused_bound_{slack.Commodity}_{slack.Node.Value}"),
                UpperBound(model, unmet, unmetBound.Value, $"{_name}_unmet_bound_{slack.Commodity}_{slack.Node.Value}"),
            }) {
                if (bound is Failed<Success, ErrorCode, Error<ErrorCode>> boundFailure) {
                    return Results.Failed<Success>(boundFailure.Error);
                }
            }
            LinearPolynomial<Flt64> penaltyTerm = Add(
                Scale(penalty.Value, Expression(unused, Flt64.One)),
                Scale(penalty.Value, Expression(unmet, Flt64.One)));
            _cost = Add(_cost, penaltyTerm);
            _relaxationCost = Add(_relaxationCost, penaltyTerm);
            _slackVariables[key] = (unused, unmet);
        }

        // 保留原始弧顺序及自环的正负两项，只跳过与节点无关的弧。
        // Preserve arc order and both self-loop terms, skipping only non-incident arcs.
        var incidentArcs = new Dictionary<NetworkNodeId, List<Model.FlowArc<V>>>();
        foreach (Model.FlowArc<V> arc in _problem.Arcs) {
            if (!incidentArcs.TryGetValue(arc.From, out List<Model.FlowArc<V>>? fromList)) {
                fromList = new List<Model.FlowArc<V>>();
                incidentArcs[arc.From] = fromList;
            }
            fromList.Add(arc);
            if (!arc.To.Equals(arc.From)) {
                if (!incidentArcs.TryGetValue(arc.To, out List<Model.FlowArc<V>>? toList)) {
                    toList = new List<Model.FlowArc<V>>();
                    incidentArcs[arc.To] = toList;
                }
                toList.Add(arc);
            }
        }

        foreach (CommodityFlow<V> product in products) {
            foreach (NetworkNodeId node in _problem.Graph.NodeIds) {
                LinearPolynomial<Flt64> balanceExpression = Zero();
                if (incidentArcs.TryGetValue(node, out List<Model.FlowArc<V>>? incident)) {
                    foreach (Model.FlowArc<V> arc in incident) {
                        if (!_expressions.TryGetValue(
                                new FlowKey(product.Commodity.Id, arc.NetworkArc.Id), out LinearPolynomial<Flt64>? flow)) {
                            continue;
                        }
                        if (arc.From.Equals(node)) {
                            balanceExpression = Add(balanceExpression, flow);
                        }
                        if (arc.To.Equals(node)) {
                            balanceExpression = Subtract(balanceExpression, flow);
                        }
                    }
                }
                if (_slackVariables.TryGetValue((product.Commodity.Id, node), out (URealVar Unused, URealVar Unmet) slackPair)) {
                    balanceExpression = Add(balanceExpression, Expression(slackPair.Unused, Flt64.One));
                    balanceExpression = Subtract(balanceExpression, Expression(slackPair.Unmet, Flt64.One));
                }
                SupplyDemand<V>? demand = product.Commodity.SupplyDemands.FirstOrDefault(
                    candidate => candidate.NodeId.Equals(node));
                Flt64 net = Flt64.Zero;
                if (demand is not null) {
                    Result<Flt64, ErrorCode, Error<ErrorCode>> normalized =
                        _adapter.Normalize(demand.Net, product.Unit);
                    if (normalized is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                        return NetworkSchedulingErrors.TryFailure(
                            "供需归一化失败 / Balance normalization failed");
                    }
                    net = normalized.Value;
                }
                Try result = new FlowConservationConstraint(
                    balanceExpression,
                    net,
                    $"{_name}_balance_{product.Commodity.Id}_{node.Value}").Invoke(model);
                if (result is Failed<Success, ErrorCode, Error<ErrorCode>> conservationFailure) {
                    return Results.Failed<Success>(conservationFailure.Error);
                }
            }
        }

        if (minimizeCost) {
            Try objective = new MinCostFlowObjective(_cost, $"{_name}_cost").Invoke(model);
            if (objective is Failed<Success, ErrorCode, Error<ErrorCode>> objectiveFailure) {
                return Results.Failed<Success>(objectiveFailure.Error);
            }
        }

        _registeredOptions = (new HashSet<string>(owned, StringComparer.Ordinal), minimizeCost);
        _registeredModel = model;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 从已回填的模型提取业务量 / Extract business quantities from a populated model.
    /// </summary>
    /// <remarks>
    /// **任何一处取不到结果都直接失败**，绝不用 0 顶替。缺结果意味着模型没解、或者解没回填；用 0 顶替会产出一份"看起来完整"的业务
    /// 报表，而其中某些流量、费用或满足量其实是编出来的。
    /// **Any missing value fails outright** and is never replaced by zero. A missing result means the model was not solved or the
    /// solution was not populated; substituting zero yields a "complete-looking" business report in which some flows, costs or
    /// fulfilments were invented.
    /// </remarks>
    /// <param name="model">已求解模型 / Solved model.</param>
    /// <returns>流结果或缺失结果错误 / The flow solution, or a missing-value error.</returns>
    public Result<FlowSolution<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (!ReferenceEquals(_registeredModel, model)) {
            return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                "流模型未注册 / Flow model is not registered");
        }

        var commodityById = new Dictionary<string, CommodityFlow<V>>(StringComparer.Ordinal);
        foreach (CommodityFlow<V> product in _problem.Commodities) {
            commodityById.TryAdd(product.Commodity.Id, product);
        }

        Flt64? Evaluate(LinearPolynomial<Flt64> polynomial) {
            Flt64 value = polynomial.Constant;
            foreach (LinearMonomial<Flt64> term in polynomial.Monomials) {
                if (term.Symbol is not IVariableItem item) {
                    return null;
                }
                Token<Flt64>? token = model.Tokens.Find(item);
                if (token?.Result is not { } result) {
                    return null;
                }
                value = value.Plus(term.Coefficient.Times(result));
            }
            return value;
        }

        var flows = new Dictionary<FlowKey, Quantity<V>>();
        foreach ((FlowKey key, IVariableItem variable) in _variables) {
            Token<Flt64>? token = model.Tokens.Find(variable);
            if (token?.Result is not { } solverValue) {
                return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                    $"缺失流量结果：{key.Commodity}/{key.Arc.Value} / Missing flow result: {key.Commodity}/{key.Arc.Value}");
            }
            Result<V, ErrorCode, Error<ErrorCode>> converted =
                _adapter.FromSolverValue(solverValue.Times(_stepValues[key.Commodity]));
            if (converted is Failed<V, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                    "流量转换失败 / Flow conversion failed");
            }
            flows[key] = new Quantity<V>(converted.Value, commodityById[key.Commodity].Unit);
        }

        var utilization = new Dictionary<string, Quantity<V>>(StringComparer.Ordinal);
        foreach (FlowCapacity<V> row in _rows) {
            if (Evaluate(_loads[row.Id]) is not { } scalar) {
                return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                    $"缺失资源结果：{row.Id} / Missing resource result: {row.Id}");
            }
            Result<V, ErrorCode, Error<ErrorCode>> converted = _adapter.FromSolverValue(scalar);
            if (converted is Failed<V, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                    "资源结果转换失败 / Resource result conversion failed");
            }
            utilization[row.Id] = new Quantity<V>(converted.Value, row.Bounds.Upper.Unit);
        }

        var unusedSupply = new Dictionary<(string Commodity, NetworkNodeId Node), Quantity<V>>();
        var unmetDemand = new Dictionary<(string Commodity, NetworkNodeId Node), Quantity<V>>();
        foreach (((string Commodity, NetworkNodeId Node) key, (URealVar Unused, URealVar Unmet) pair) in _slackVariables) {
            PhysicalUnit unit = commodityById[key.Commodity].Unit;
            if (model.Tokens.Find(pair.Unused)?.Result is not { } rawUnused
                || model.Tokens.Find(pair.Unmet)?.Result is not { } rawUnmet) {
                return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                    "缺失弃供或缺货结果 / Missing unused-supply or unmet-demand result");
            }
            Result<V, ErrorCode, Error<ErrorCode>> unusedValue = _adapter.FromSolverValue(rawUnused);
            Result<V, ErrorCode, Error<ErrorCode>> unmetValue = _adapter.FromSolverValue(rawUnmet);
            if (unusedValue is Failed<V, ErrorCode, Error<ErrorCode>>
                || unmetValue is Failed<V, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                    "松弛结果转换失败 / Relaxation result conversion failed");
            }
            unusedSupply[key] = new Quantity<V>(unusedValue.Value, unit);
            unmetDemand[key] = new Quantity<V>(unmetValue.Value, unit);
        }

        if (Evaluate(_cost) is not { } rawCost) {
            return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                "缺失费用结果 / Missing cost result");
        }
        Result<V, ErrorCode, Error<ErrorCode>> totalCost = _adapter.FromSolverValue(rawCost);
        if (totalCost is Failed<V, ErrorCode, Error<ErrorCode>>) {
            return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                "费用结果转换失败 / Cost result conversion failed");
        }

        var commodityCosts = new Dictionary<string, Quantity<V>>(StringComparer.Ordinal);
        foreach ((string id, LinearPolynomial<Flt64> expression) in _productCosts) {
            if (Evaluate(expression) is not { } scalar) {
                return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                    $"商品费用提取失败：{id} / Commodity cost extraction failed: {id}");
            }
            Result<V, ErrorCode, Error<ErrorCode>> converted = _adapter.FromSolverValue(scalar);
            if (converted is Failed<V, ErrorCode, Error<ErrorCode>>) {
                return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                    "商品费用转换失败 / Commodity cost conversion failed");
            }
            commodityCosts[id] = new Quantity<V>(converted.Value, _problem.CostUnit);
        }

        if (Evaluate(_relaxationCost) is not { } rawPenalty) {
            return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                "罚费用提取失败 / Penalty extraction failed");
        }
        Result<V, ErrorCode, Error<ErrorCode>> relaxation = _adapter.FromSolverValue(rawPenalty);
        if (relaxation is Failed<V, ErrorCode, Error<ErrorCode>>) {
            return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                "罚费用转换失败 / Penalty conversion failed");
        }

        var balances = new Dictionary<(string Commodity, NetworkNodeId Node), Quantity<V>>();
        foreach (CommodityFlow<V> product in _problem.Commodities) {
            foreach (NetworkNodeId node in _problem.Graph.NodeIds) {
                SupplyDemand<V>? balance = product.Commodity.SupplyDemands.FirstOrDefault(
                    candidate => candidate.NodeId.Equals(node));
                Flt64 net = Flt64.Zero;
                if (balance is not null) {
                    Result<Flt64, ErrorCode, Error<ErrorCode>> normalized =
                        _adapter.Normalize(balance.Net, product.Unit);
                    if (normalized is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                        return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                            "供需结果换算失败 / Balance result conversion failed");
                    }
                    net = normalized.Value;
                }
                if (_slackVariables.TryGetValue((product.Commodity.Id, node), out (URealVar Unused, URealVar Unmet) slackPair)) {
                    if (model.Tokens.Find(slackPair.Unused)?.Result is not { } rawUnused
                        || model.Tokens.Find(slackPair.Unmet)?.Result is not { } rawUnmet) {
                        return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                            "缺失弃供或缺货结果 / Missing unused-supply or unmet-demand result");
                    }
                    net = net.Minus(rawUnused).Plus(rawUnmet);
                }
                Result<V, ErrorCode, Error<ErrorCode>> converted = _adapter.FromSolverValue(net);
                if (converted is Failed<V, ErrorCode, Error<ErrorCode>>) {
                    return NetworkSchedulingErrors.Failure<FlowSolution<V>>(
                        "满足量结果转换失败 / Fulfillment result conversion failed");
                }
                balances[(product.Commodity.Id, node)] = new Quantity<V>(converted.Value, product.Unit);
            }
        }

        return Results.Ok(new FlowSolution<V>(
            flows, new Quantity<V>(totalCost.Value, _problem.CostUnit), utilization,
            unusedSupply, unmetDemand, commodityCosts,
            new Quantity<V>(relaxation.Value, _problem.CostUnit), balances));
    }

    /// <summary>构造零多项式 / Build the zero polynomial.</summary>
    /// <returns>零多项式 / The zero polynomial.</returns>
    private static LinearPolynomial<Flt64> Zero() =>
        new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);

    /// <summary>构造"步长 × 变量"的表达式 / Build a "step × variable" expression.</summary>
    /// <param name="variable">变量 / The variable.</param>
    /// <param name="step">步长 / The step.</param>
    /// <returns>线性表达式 / The linear expression.</returns>
    private static LinearPolynomial<Flt64> Expression(IVariableItem variable, Flt64 step) =>
        new(new[] { new LinearMonomial<Flt64>(step, variable) }, Flt64.Zero);

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

    /// <summary>相减两个表达式 / Subtract one expression from another.</summary>
    /// <param name="left">左式 / Left operand.</param>
    /// <param name="right">右式 / Right operand.</param>
    /// <returns>差 / The difference.</returns>
    private static LinearPolynomial<Flt64> Subtract(LinearPolynomial<Flt64> left, LinearPolynomial<Flt64> right) =>
        new(
            left.Monomials
                .Concat(right.Monomials.Select(term =>
                    new LinearMonomial<Flt64>(Flt64.Zero.Minus(term.Coefficient), term.Symbol)))
                .ToList(),
            left.Constant.Minus(right.Constant));

    /// <summary>
    /// 为变量注册上界约束 / Register an upper bound on a variable.
    /// </summary>
    /// <remarks>
    /// **松弛变量必须显式带上界**。没有上界时，罚函数只会把它推向 0，看起来无碍；但一旦主问题因为数值问题给它一个非零值，
    /// 它就会把"弃供/缺货"放大到任意规模，而目标函数里的罚成本照样把它压到最小——解看起来正常，实际不可行。
    /// **A slack variable must carry an explicit upper bound.** Without one the penalty only pushes it towards zero, which looks
    /// harmless; but once the master hands it a non-zero value for numerical reasons it can balloon "unused supply / unmet demand" to
    /// any size while the penalty still minimises it — the solution looks fine and is actually infeasible.
    /// </remarks>
    /// <param name="model">元模型 / Meta model.</param>
    /// <param name="variable">变量 / The variable.</param>
    /// <param name="bound">上界 / The upper bound.</param>
    /// <param name="name">约束名称 / Constraint name.</param>
    /// <returns>注册结果 / The registration result.</returns>
    private static Try UpperBound(
        LinearMetaModel<Flt64> model,
        URealVar variable,
        Flt64 bound,
        string name) =>
        model.AddConstraint(
            Expression(variable, Flt64.One).Le(new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), bound)),
            null,
            name: name);

    /// <summary>判断 Flt64 是否有限 / Whether an Flt64 is finite.</summary>
    /// <param name="value">待判断的值 / The value to check.</param>
    /// <returns>是否有限 / Whether it is finite.</returns>
    private static bool Finite(Flt64 value) => ((IRealNumber<Flt64>)value).IsFinite();

    /// <summary>取两个值的较大者 / Take the larger of two values.</summary>
    /// <param name="left">左值 / Left value.</param>
    /// <param name="right">右值 / Right value.</param>
    /// <returns>较大者 / The larger value.</returns>
    private static Flt64 MaxOf(Flt64 left, Flt64 right) => left.CompareTo(right) >= 0 ? left : right;

}

/// <summary>
/// 每次模型注册使用独立聚合 / Independent per-model aggregation.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class FlowContext<V> where V : struct, IRealNumber<V> {
    private readonly FlowAggregation<V> _aggregation;

    /// <summary>
    /// 构造流上下文 / Construct the flow context.
    /// </summary>
    /// <param name="problem">不可变问题 / Immutable problem.</param>
    /// <param name="adapter">数值边界 / Numeric boundary.</param>
    /// <param name="name">模型命名空间 / Model namespace.</param>
    public FlowContext(
        FlowProblem<V> problem,
        INetworkSchedulingSolverValueAdapter<V> adapter,
        string name = "network_flow") {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(adapter);
        Problem = problem;
        Adapter = adapter;
        Name = name;
        _aggregation = new FlowAggregation<V>(problem, adapter, name);
    }

    /// <summary>不可变问题 / The immutable problem.</summary>
    public FlowProblem<V> Problem { get; }

    /// <summary>数值边界 / The numeric boundary.</summary>
    public INetworkSchedulingSolverValueAdapter<V> Adapter { get; }

    /// <summary>模型命名空间 / The model namespace.</summary>
    public string Name { get; }

    /// <summary>注册态 / The registration state.</summary>
    public FlowAggregation<V> Aggregation => _aggregation;

    /// <summary>已注册的运行费用表达式 / The registered operating-cost expression.</summary>
    public LinearPolynomial<Flt64> Cost => _aggregation.Cost;

    /// <summary>
    /// 访问商品流表达式 / Access a commodity flow expression.
    /// </summary>
    /// <param name="key">商品弧键 / Commodity-arc key.</param>
    /// <returns>表达式或错误 / The expression, or an error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> Flow(FlowKey key) =>
        _aggregation.Flow(key);

    /// <summary>
    /// 访问资源占用表达式 / Access a resource load expression.
    /// </summary>
    /// <param name="id">资源标识 / Resource identity.</param>
    /// <returns>表达式或错误 / The expression, or an error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> Load(string id) =>
        _aggregation.Load(id);

    /// <summary>
    /// 注册流聚合 / Register the flow aggregation.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <param name="ownedCapacityRows">外部决策管理的容量行 / Capacity rows managed by external decisions.</param>
    /// <param name="minimizeCost">是否添加目标 / Whether to add the objective.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(
        LinearMetaModel<Flt64> model,
        IReadOnlySet<string>? ownedCapacityRows = null,
        bool minimizeCost = true) =>
        _aggregation.Register(model, ownedCapacityRows, minimizeCost);

    /// <summary>
    /// 提取已求解结果 / Extract the solved values.
    /// </summary>
    /// <param name="model">已求解模型 / Solved model.</param>
    /// <returns>业务结果 / The business solution.</returns>
    public Result<FlowSolution<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model) =>
        _aggregation.ExtractSolution(model);
}
