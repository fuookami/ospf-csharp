// 广义流聚合与上下文 / Generalized-flow aggregation and context.
// 与固定 Kotlin （25651 字节）、（3510 字节）对应。
// And.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Service.Limits;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
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

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Model;

/// <summary>
/// 使用单次状态完成校验、变量、广义平衡与管线注册的聚合 / Per-registration aggregate for validation, variables, generalized
/// balances and pipelines.
/// </summary>
/// <typeparam name="V">领域数值类型 / Domain numeric type.</typeparam>
public sealed class GeneralizedFlowAggregation<V> where V : struct, IRealNumber<V> {
    private readonly IReadOnlyList<Func<GeneralizedFlowExpressions, IPipeline<LinearMetaModel<Flt64>>>>
        _extensionFactories;
    private readonly GeneralizedFlowProblem<V> _problem;
    private readonly INetworkSchedulingSolverValueAdapter<V> _adapter;
    private readonly string _name;
    private readonly GeneralizedFlowSolverTolerance _solverTolerance;
    private bool _attempted;
    private LinearMetaModel<Flt64>? _registeredModel;
    private bool? _registeredMinimizeCost;
    private IReadOnlyList<PreparedGeneralizedFlowArc<V>> _preparedArcs = new List<PreparedGeneralizedFlowArc<V>>();
    private IReadOnlyDictionary<NetworkArcId, URealVar> _inputVariables = new Dictionary<NetworkArcId, URealVar>();
    private GeneralizedFlowExpressions? _registeredExpressions;
    private IReadOnlyDictionary<NetworkNodeId, Flt64> _nodeSupplyValues = new Dictionary<NetworkNodeId, Flt64>();
    private LinearPolynomial<Flt64> _cost = Zero();

    /// <summary>
    /// 构造广义流聚合 / Construct the generalized-flow aggregate.
    /// </summary>
    /// <param name="problem">不可变业务问题 / Immutable business problem.</param>
    /// <param name="adapter">solver 数值转换边界 / Solver numeric-conversion boundary.</param>
    /// <param name="name">模型命名空间 / Model namespace.</param>
    /// <param name="extensionFactories">接收已注册弧表达式的业务扩展管线工厂 / Factories receiving registered arc expressions.</param>
    /// <param name="solverTolerance">solver 单位可行性容差 / Solver-unit feasibility tolerance.</param>
    public GeneralizedFlowAggregation(
        GeneralizedFlowProblem<V> problem,
        INetworkSchedulingSolverValueAdapter<V> adapter,
        string name = "generalized_flow",
        IReadOnlyList<Func<GeneralizedFlowExpressions, IPipeline<LinearMetaModel<Flt64>>>>? extensionFactories = null,
        GeneralizedFlowSolverTolerance? solverTolerance = null) {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(name);
        _problem = problem;
        _adapter = adapter;
        _name = name;
        _extensionFactories = extensionFactories ?? Array.Empty<
            Func<GeneralizedFlowExpressions, IPipeline<LinearMetaModel<Flt64>>>>();
        _solverTolerance = solverTolerance ?? new GeneralizedFlowSolverTolerance();
    }

    /// <summary>已注册的总费用表达式 / The registered total-cost expression.</summary>
    public LinearPolynomial<Flt64> Cost => _cost;

    /// <summary>
    /// 注册每条弧的投入变量、有限容量、广义平衡与扩展管线 / Register arc inputs, finite capacities, generalized balances and
    /// extension pipelines.
    /// </summary>
    /// <remarks>
    /// **跨量纲弧必须显式声明转换系数**。缺少它时基线直接失败，而不是默认按 1 处理：把"立方米"当成"吨"会给出一个单位上完全错误的
    /// 平衡，而模型照样解得出可行解。
    /// **A cross-dimensional arc must declare its conversion factor.** Without it the code fails instead of defaulting to one:
    /// treating cubic metres as tonnes yields a dimensionally wrong balance that still solves to a feasible-looking answer.
    /// </remarks>
    /// <param name="model">元模型 / Meta model.</param>
    /// <param name="minimizeCost">是否注册内置总费用目标 / Whether to register the built-in total-cost objective.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model, bool minimizeCost = true) {
        ArgumentNullException.ThrowIfNull(model);
        if (ReferenceEquals(_registeredModel, model)) {
            return _registeredMinimizeCost == minimizeCost
                ? Results.Ok(Results.SuccessInstance)
                : NetworkSchedulingErrors.TryFailure(
                    "重复注册的目标选项不一致 / Repeated registration has different objective options");
        }
        if (_attempted) {
            return NetworkSchedulingErrors.TryFailure(
                "广义流聚合不可跨模型或注册失败后复用 / Generalized-flow aggregation cannot be reused across models or "
                + "after a failed registration");
        }
        _attempted = true;

        Result<(IReadOnlyDictionary<NetworkNodeId, Flt64> NetSupplies,
            IReadOnlyList<PreparedGeneralizedFlowArc<V>> Arcs), ErrorCode, Error<ErrorCode>> prepared = Prepare();
        if (prepared is Failed<(IReadOnlyDictionary<NetworkNodeId, Flt64>, IReadOnlyList<PreparedGeneralizedFlowArc<V>>),
                ErrorCode, Error<ErrorCode>> prepareFailure) {
            return Results.Failed<Success>(prepareFailure.Error);
        }
        (IReadOnlyDictionary<NetworkNodeId, Flt64> netSupplies, IReadOnlyList<PreparedGeneralizedFlowArc<V>> arcs) =
            prepared.Value;

        var component = new GeneralizedFlowArcComponent<V>(arcs, _name);
        if (component.Register(model) is Failed<Success, ErrorCode, Error<ErrorCode>> componentFailure) {
            return Results.Failed<Success>(componentFailure.Error);
        }
        var expressions = new GeneralizedFlowExpressions(component.Inputs, component.Outputs);
        var capacityBounds = arcs.ToDictionary(
            item => item.Arc.NetworkArc.Id,
            item => (Lower: item.LowerInput, Upper: item.UpperInput));
        Try capacity = RegisterPipeline(
            new GeneralizedFlowInputCapacityPipeline(component.Inputs, capacityBounds, $"{_name}_capacity"), model);
        if (capacity is Failed<Success, ErrorCode, Error<ErrorCode>> capacityFailure) {
            return Results.Failed<Success>(capacityFailure.Error);
        }

        var balances = _problem.Nodes.ToDictionary(node => node.Id, _ => Zero());
        foreach (PreparedGeneralizedFlowArc<V> item in arcs) {
            NetworkArcId arcId = item.Arc.NetworkArc.Id;
            NetworkNodeId from = item.Arc.NetworkArc.From;
            NetworkNodeId to = item.Arc.NetworkArc.To;
            LinearPolynomial<Flt64> input = component.Inputs[arcId];
            LinearPolynomial<Flt64> output = component.Outputs[arcId];
            balances[from] = Add(balances[from], input);
            balances[to] = Subtract(balances[to], output);
        }
        Try balance = RegisterPipeline(
            new GeneralizedFlowNodeBalancePipeline(balances, netSupplies, $"{_name}_generalized"), model);
        if (balance is Failed<Success, ErrorCode, Error<ErrorCode>> balanceFailure) {
            return Results.Failed<Success>(balanceFailure.Error);
        }

        LinearPolynomial<Flt64> totalCost = Zero();
        foreach (PreparedGeneralizedFlowArc<V> item in arcs) {
            totalCost = Add(
                totalCost,
                Scale(item.CostCoefficient, component.Inputs[item.Arc.NetworkArc.Id]));
        }
        foreach (Func<GeneralizedFlowExpressions, IPipeline<LinearMetaModel<Flt64>>> factory in _extensionFactories) {
            Try extension = RegisterPipeline(factory(expressions), model);
            if (extension is Failed<Success, ErrorCode, Error<ErrorCode>> extensionFailure) {
                return Results.Failed<Success>(extensionFailure.Error);
            }
        }
        if (minimizeCost) {
            Try objective = RegisterPipeline(
                new GeneralizedFlowCostObjective(totalCost, $"{_name}_cost"), model);
            if (objective is Failed<Success, ErrorCode, Error<ErrorCode>> objectiveFailure) {
                return Results.Failed<Success>(objectiveFailure.Error);
            }
        }

        _cost = totalCost;
        _preparedArcs = arcs;
        _inputVariables = component.Variables;
        _registeredExpressions = expressions;
        _nodeSupplyValues = netSupplies;
        _registeredMinimizeCost = minimizeCost;
        _registeredModel = model;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 访问弧的投入表达式 / Get an arc input expression.
    /// </summary>
    /// <param name="arcId">稳定弧标识 / Stable arc identity.</param>
    /// <returns>起点单位投入表达式或注册错误 / The origin-unit input expression, or a registration error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> Input(NetworkArcId arcId) =>
        _registeredExpressions?.Input(arcId)
        ?? NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
            "广义流尚未成功注册 / Generalized flow has not been registered successfully");

    /// <summary>
    /// 访问弧的到达产出表达式 / Get an arc arrival-output expression.
    /// </summary>
    /// <param name="arcId">稳定弧标识 / Stable arc identity.</param>
    /// <returns>终点单位产出表达式或注册错误 / The destination-unit output expression, or a registration error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> Output(NetworkArcId arcId) =>
        _registeredExpressions?.Output(arcId)
        ?? NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
            "广义流尚未成功注册 / Generalized flow has not been registered successfully");

    /// <summary>
    /// 从已回填 solver 解中提取弧结果和节点广义守恒校验 / Extract arc results and generalized node-balance checks from a populated
    /// solution.
    /// </summary>
    /// <remarks>
    /// **节点平衡残差超容差时直接失败，而不是把残差记下来继续走**。残差意味着解与声明的净供需不符，此时弧结果与总费用同样不可信；
    /// 把它们当作有效结果返回，会让上游基于一个并不成立的守恒关系做后续决策。
    /// **A node-balance residual beyond tolerance fails outright instead of being recorded and passed along.** A residual means the
    /// solution disagrees with the declared net supply, so the arc results and total cost are equally untrustworthy; returning them as
    /// valid would have the caller decide on a conservation law that does not hold.
    /// </remarks>
    /// <param name="model">已注册并回填解的模型 / Registered model with a populated solution.</param>
    /// <returns>领域结果或模型／数值转换错误 / The domain solution, or a model/numeric-conversion error.</returns>
    public Result<GeneralizedFlowSolution<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (!ReferenceEquals(_registeredModel, model)) {
            return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                "广义流模型未注册或模型身份不匹配 / Generalized-flow model is unregistered or has a different identity");
        }

        var nodeTotals = _problem.Nodes.ToDictionary(
            node => node.Id, _ => new NodeTotals());
        var arcResults = new Dictionary<NetworkArcId, GeneralizedFlowArcResult<V>>();
        Flt64 totalCost = Flt64.Zero;

        foreach (PreparedGeneralizedFlowArc<V> item in _preparedArcs) {
            GeneralizedFlowArc<V> arc = item.Arc;
            NetworkArcId arcId = arc.NetworkArc.Id;
            if (!_inputVariables.TryGetValue(arcId, out URealVar? variable)) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流投入变量缺失：{arcId.Value} / Generalized-flow input variable is missing: {arcId.Value}");
            }
            Token<Flt64>? token = model.Tokens.Find(variable);
            if (token?.Result is not { } inputValue) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"缺失广义流投入结果：{arcId.Value} / Missing generalized-flow input result: {arcId.Value}");
            }
            if (!Finite(inputValue)) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流投入结果必须有限：{arcId.Value} / Generalized-flow input result must be finite: {arcId.Value}");
            }
            if (inputValue.CompareTo(Flt64.Zero) < 0) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流投入不能为负：{arcId.Value} / Generalized-flow input cannot be negative: {arcId.Value}");
            }
            Flt64? capacityTolerance = _solverTolerance.ForValues(
                new[] { inputValue, item.LowerInput, item.UpperInput });
            if (capacityTolerance is null) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流容量容差计算必须有限：{arcId.Value} / Generalized-flow capacity tolerance must be finite: "
                    + $"{arcId.Value}");
            }
            if ((inputValue.CompareTo(item.LowerInput) < 0
                    && item.LowerInput.Minus(inputValue).CompareTo(capacityTolerance.Value) > 0)
                || (inputValue.CompareTo(item.UpperInput) > 0
                    && inputValue.Minus(item.UpperInput).CompareTo(capacityTolerance.Value) > 0)) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流投入违反容量界：{arcId.Value}，投入={inputValue.ToDouble()}，范围=[{item.LowerInput.ToDouble()}, "
                    + $"{item.UpperInput.ToDouble()}] / Generalized-flow input violates capacity bounds: {arcId.Value}, "
                    + $"input={inputValue.ToDouble()}, bounds=[{item.LowerInput.ToDouble()}, {item.UpperInput.ToDouble()}]");
            }

            Flt64 convertedOutput = inputValue.Times(item.UnitConversionCoefficient);
            Flt64 outputValue = inputValue.Times(item.OutputCoefficient);
            Flt64 lossOrGain = convertedOutput.Minus(outputValue);
            Flt64 arcCost = inputValue.Times(item.CostCoefficient);
            if (!Finite(convertedOutput) || !Finite(outputValue) || !Finite(lossOrGain) || !Finite(arcCost)) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流弧结果必须有限：{arcId.Value} / Generalized-flow arc results must be finite: {arcId.Value}");
            }
            totalCost = totalCost.Plus(arcCost);
            if (!Finite(totalCost)) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    "广义流总费用结果必须有限 / Generalized-flow total-cost result must be finite");
            }
            if (!nodeTotals.TryGetValue(arc.NetworkArc.From, out NodeTotals? fromTotals)) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流起点统计缺失：{arcId.Value} / Generalized-flow origin totals are missing: {arcId.Value}");
            }
            if (!nodeTotals.TryGetValue(arc.NetworkArc.To, out NodeTotals? toTotals)) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流终点统计缺失：{arcId.Value} / Generalized-flow destination totals are missing: {arcId.Value}");
            }
            fromTotals.OutgoingInput = fromTotals.OutgoingInput.Plus(inputValue);
            toTotals.IncomingOutput = toTotals.IncomingOutput.Plus(outputValue);
            if (!Finite(fromTotals.OutgoingInput) || !Finite(toTotals.IncomingOutput)) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流节点累计结果必须有限：{arcId.Value} / Generalized-flow node totals must be finite: {arcId.Value}");
            }

            GeneralizedFlowNode<V>? fromNode = _problem.Nodes.FirstOrDefault(
                node => node.Id.Equals(arc.NetworkArc.From));
            GeneralizedFlowNode<V>? toNode = _problem.Nodes.FirstOrDefault(
                node => node.Id.Equals(arc.NetworkArc.To));
            if (fromNode is null) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流起点不存在：{arcId.Value} / Generalized-flow origin node is missing: {arcId.Value}");
            }
            if (toNode is null) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流终点不存在：{arcId.Value} / Generalized-flow destination node is missing: {arcId.Value}");
            }

            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> input = QuantityOf(inputValue, fromNode.Unit);
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> converted =
                QuantityOf(convertedOutput, toNode.Unit);
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> output = QuantityOf(outputValue, toNode.Unit);
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> loss = QuantityOf(lossOrGain, toNode.Unit);
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> cost = QuantityOf(arcCost, _problem.CostUnit);
            foreach (Result<Quantity<V>, ErrorCode, Error<ErrorCode>> candidate in
                     new[] { input, converted, output, loss, cost }) {
                if (candidate is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> candidateFailure) {
                    return Results.Failed<GeneralizedFlowSolution<V>>(candidateFailure.Error);
                }
            }
            arcResults[arcId] = new GeneralizedFlowArcResult<V>(
                input.Value, converted.Value, output.Value, loss.Value, cost.Value);
        }

        var nodeResults = new Dictionary<NetworkNodeId, GeneralizedFlowNodeResult<V>>();
        foreach (GeneralizedFlowNode<V> node in _problem.Nodes) {
            if (!nodeTotals.TryGetValue(node.Id, out NodeTotals? totals)) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流节点统计缺失：{node.Id.Value} / Generalized-flow node totals are missing: {node.Id.Value}");
            }
            if (!_nodeSupplyValues.TryGetValue(node.Id, out Flt64 netSupply)) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流节点净供需缺失：{node.Id.Value} / Generalized-flow node balance is missing: {node.Id.Value}");
            }
            Flt64 realized = totals.OutgoingInput.Minus(totals.IncomingOutput);
            Flt64 residual = realized.Minus(netSupply);
            if (!Finite(realized) || !Finite(residual)) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流节点平衡结果必须有限：{node.Id.Value} / Generalized-flow node-balance results must be finite: "
                    + $"{node.Id.Value}");
            }
            Flt64? balanceTolerance = _solverTolerance.ForValues(new[] { realized, netSupply });
            if (balanceTolerance is null) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流节点平衡容差计算必须有限：{node.Id.Value} / Generalized-flow node-balance tolerance must be "
                    + $"finite: {node.Id.Value}");
            }
            if (residual.Abs().CompareTo(balanceTolerance.Value) > 0) {
                return NetworkSchedulingErrors.Failure<GeneralizedFlowSolution<V>>(
                    $"广义流节点平衡不满足：{node.Id.Value}，残差={residual.ToDouble()} / Generalized-flow node balance is "
                    + $"violated: {node.Id.Value}, residual={residual.ToDouble()}");
            }
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> declared = QuantityOf(netSupply, node.Unit);
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> outgoing =
                QuantityOf(totals.OutgoingInput, node.Unit);
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> incoming =
                QuantityOf(totals.IncomingOutput, node.Unit);
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> realizedQuantity = QuantityOf(realized, node.Unit);
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> residualQuantity = QuantityOf(residual, node.Unit);
            foreach (Result<Quantity<V>, ErrorCode, Error<ErrorCode>> candidate in
                     new[] { declared, outgoing, incoming, realizedQuantity, residualQuantity }) {
                if (candidate is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> candidateFailure) {
                    return Results.Failed<GeneralizedFlowSolution<V>>(candidateFailure.Error);
                }
            }
            nodeResults[node.Id] = new GeneralizedFlowNodeResult<V>(
                declared.Value, outgoing.Value, incoming.Value, realizedQuantity.Value, residualQuantity.Value);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> totalCostQuantity =
            QuantityOf(totalCost, _problem.CostUnit);
        if (totalCostQuantity is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> costFailure) {
            return Results.Failed<GeneralizedFlowSolution<V>>(costFailure.Error);
        }
        return Results.Ok(new GeneralizedFlowSolution<V>(
            arcResults, nodeResults, totalCostQuantity.Value));
    }

    /// <summary>
    /// 校验并归一化全部弧与节点数据 / Validate and normalise every arc and node.
    /// </summary>
    /// <returns>净供需与已准备弧，或校验失败 / Net supplies and prepared arcs, or a validation failure.</returns>
    private Result<(IReadOnlyDictionary<NetworkNodeId, Flt64> NetSupplies,
        IReadOnlyList<PreparedGeneralizedFlowArc<V>> Arcs), ErrorCode, Error<ErrorCode>> Prepare() {
        if (string.IsNullOrWhiteSpace(_name)) {
            return Fail("广义流模型名称不能为空 / Generalized-flow model name cannot be blank");
        }
        if (!_solverTolerance.IsValid()) {
            return Fail("广义流 solver 容差必须是有限非负值 / Generalized-flow solver tolerances must be finite and "
                + "nonnegative");
        }
        var nodeById = new Dictionary<NetworkNodeId, GeneralizedFlowNode<V>>();
        foreach (GeneralizedFlowNode<V> node in _problem.Nodes) {
            nodeById[node.Id] = node;
        }
        var arcIds = _problem.Arcs.Select(arc => arc.NetworkArc.Id).ToList();
        var arcIdSet = new HashSet<NetworkArcId>(arcIds);
        if (nodeById.Count != _problem.Nodes.Count
            || arcIdSet.Count != arcIds.Count
            || !new HashSet<NetworkNodeId>(_problem.Graph.NodeIds).SetEquals(nodeById.Keys)
            || !new HashSet<NetworkArcId>(_problem.Graph.ArcIds).SetEquals(arcIdSet)
            || _problem.Arcs.Any(arc =>
                !_problem.Graph.From(arc.NetworkArc).Equals(arc.NetworkArc.From)
                || !_problem.Graph.To(arc.NetworkArc).Equals(arc.NetworkArc.To)
                || !nodeById.ContainsKey(arc.NetworkArc.From)
                || !nodeById.ContainsKey(arc.NetworkArc.To))) {
            return Fail("广义流图、节点或弧引用不一致 / Generalized-flow graph, nodes, or arc references are inconsistent");
        }
        if (_problem.Nodes.Any(node => node.Unit.IsAffine) || _problem.CostUnit.IsAffine) {
            return Fail("广义流只支持线性单位，不支持仿射单位 / Generalized flow supports linear units, not affine units");
        }

        Result<Flt64, ErrorCode, Error<ErrorCode>> solverZero = _adapter.ToSolverValue(_problem.Zero);
        if (solverZero is Failed<Flt64, ErrorCode, Error<ErrorCode>>
            || !Finite(solverZero.Value)
            || solverZero.Value.CompareTo(Flt64.Zero) != 0) {
            return Fail("广义流 solver 零值转换无效 / Generalized-flow solver zero conversion is invalid");
        }

        var netSupplies = new Dictionary<NetworkNodeId, Flt64>();
        foreach (GeneralizedFlowNode<V> node in _problem.Nodes) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> net = _adapter.Normalize(node.NetSupply, node.Unit);
            if (net is Failed<Flt64, ErrorCode, Error<ErrorCode>> netFailure) {
                return Fail(netFailure.Error);
            }
            if (!Finite(net.Value)) {
                return Fail($"节点净供需必须有限：{node.Id.Value} / Node balance must be finite: {node.Id.Value}");
            }
            netSupplies[node.Id] = net.Value;
        }

        var prepared = new List<PreparedGeneralizedFlowArc<V>>();
        foreach (GeneralizedFlowArc<V> arc in _problem.Arcs) {
            GeneralizedFlowNode<V> fromNode = nodeById[arc.NetworkArc.From];
            GeneralizedFlowNode<V> toNode = nodeById[arc.NetworkArc.To];
            Result<Flt64, ErrorCode, Error<ErrorCode>> lower =
                _adapter.Normalize(arc.InputCapacity.Lower, fromNode.Unit);
            Result<Flt64, ErrorCode, Error<ErrorCode>> upper =
                _adapter.Normalize(arc.InputCapacity.Upper, fromNode.Unit);
            if (lower is Failed<Flt64, ErrorCode, Error<ErrorCode>>
                || upper is Failed<Flt64, ErrorCode, Error<ErrorCode>>) {
                return Fail($"弧投入容量单位无效：{arc.NetworkArc.Id.Value} / Invalid arc input capacity unit: "
                    + arc.NetworkArc.Id.Value);
            }
            if (!Finite(lower.Value) || !Finite(upper.Value)
                || lower.Value.CompareTo(Flt64.Zero) < 0
                || upper.Value.CompareTo(lower.Value) < 0) {
                return Fail($"弧投入容量必须是有限非负上下界：{arc.NetworkArc.Id.Value} / Arc input capacity must have finite "
                    + $"nonnegative bounds: {arc.NetworkArc.Id.Value}");
            }

            Result<Flt64, ErrorCode, Error<ErrorCode>> yield =
                _adapter.Normalize(arc.ProductionYield, NoneUnit.Instance);
            if (yield is Failed<Flt64, ErrorCode, Error<ErrorCode>> yieldFailure) {
                return Fail(yieldFailure.Error);
            }
            if (!Finite(yield.Value) || yield.Value.CompareTo(Flt64.Zero) < 0) {
                return Fail($"弧业务产率必须有限且非负：{arc.NetworkArc.Id.Value} / Arc business yield must be finite and "
                    + $"nonnegative: {arc.NetworkArc.Id.Value}");
            }

            Flt64 unitConversion;
            if (arc.InputToOutputFactor is { } factor) {
                if (factor.Unit.IsAffine) {
                    return Fail($"弧单位转换系数不能使用仿射单位：{arc.NetworkArc.Id.Value} / Arc unit-conversion factor "
                        + $"cannot use an affine unit: {arc.NetworkArc.Id.Value}");
                }
                PhysicalUnit ratioUnit = toNode.Unit.Divide(fromNode.Unit);
                Result<Flt64, ErrorCode, Error<ErrorCode>> conversion = _adapter.Normalize(factor, ratioUnit);
                if (conversion is Failed<Flt64, ErrorCode, Error<ErrorCode>> conversionFailure) {
                    return Fail(conversionFailure.Error);
                }
                unitConversion = conversion.Value;
            } else {
                if (!fromNode.Unit.SameDimension(toNode.Unit)) {
                    return Fail($"跨量纲广义流弧必须显式声明投入到产出转换系数：{arc.NetworkArc.Id.Value} / "
                        + $"Cross-dimensional generalized-flow arc requires an explicit input-to-output conversion factor: "
                        + arc.NetworkArc.Id.Value);
                }
                Result<Flt64, ErrorCode, Error<ErrorCode>> conversion = _adapter.Normalize(
                    new Quantity<V>(_problem.Zero.Constants.One, fromNode.Unit), toNode.Unit);
                if (conversion is Failed<Flt64, ErrorCode, Error<ErrorCode>> conversionFailure) {
                    return Fail(conversionFailure.Error);
                }
                unitConversion = conversion.Value;
            }
            if (!Finite(unitConversion) || unitConversion.CompareTo(Flt64.Zero) < 0) {
                return Fail($"单位转换系数必须有限且非负：{arc.NetworkArc.Id.Value} / Unit-conversion factor must be finite "
                    + $"and nonnegative: {arc.NetworkArc.Id.Value}");
            }
            Flt64 outputCoefficient = unitConversion.Times(yield.Value);
            if (!Finite(outputCoefficient)) {
                return Fail($"弧投入到达产出系数必须有限：{arc.NetworkArc.Id.Value} / Arc input-to-output coefficient must be "
                    + $"finite: {arc.NetworkArc.Id.Value}");
            }

            PhysicalUnit costUnitPerInput = _problem.CostUnit.Divide(fromNode.Unit);
            Result<Flt64, ErrorCode, Error<ErrorCode>> cost = _adapter.Normalize(arc.NetworkArc.Cost, costUnitPerInput);
            if (cost is Failed<Flt64, ErrorCode, Error<ErrorCode>> costFailure) {
                return Fail(costFailure.Error);
            }
            if (!Finite(cost.Value)) {
                return Fail($"弧投入资费必须有限：{arc.NetworkArc.Id.Value} / Arc input tariff must be finite: "
                    + arc.NetworkArc.Id.Value);
            }
            prepared.Add(new PreparedGeneralizedFlowArc<V>(
                arc, lower.Value, upper.Value, unitConversion, outputCoefficient, cost.Value));
        }
        return Results.Ok<(IReadOnlyDictionary<NetworkNodeId, Flt64>, IReadOnlyList<PreparedGeneralizedFlowArc<V>>)>(
            (netSupplies, prepared));
    }

    /// <summary>把管线注册进模型并立即执行 / Register a pipeline into the model and invoke it.</summary>
    /// <param name="pipeline">管线 / The pipeline.</param>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>执行结果 / The invocation result.</returns>
    private static Try RegisterPipeline(IPipeline<LinearMetaModel<Flt64>> pipeline, LinearMetaModel<Flt64> model) {
        pipeline.Register(model);
        return pipeline.Invoke(model);
    }

    /// <summary>把 solver 值包成物理量 / Wrap a solver value into a physical quantity.</summary>
    /// <param name="value">solver 值 / The solver value.</param>
    /// <param name="unit">单位 / The unit.</param>
    /// <returns>物理量或转换失败 / The quantity, or a conversion failure.</returns>
    private Result<Quantity<V>, ErrorCode, Error<ErrorCode>> QuantityOf(Flt64 value, PhysicalUnit unit) {
        Result<V, ErrorCode, Error<ErrorCode>> converted = _adapter.FromSolverValue(value);
        return converted is Failed<V, ErrorCode, Error<ErrorCode>> failure
            ? Results.Failed<Quantity<V>>(failure.Error)
            : Results.Ok(new Quantity<V>(converted.Value, unit));
    }

    /// <summary>构造失败结果 / Build a failure result.</summary>
    /// <param name="message">失败信息 / The failure message.</param>
    /// <returns>失败结果 / The failure result.</returns>
    private static Result<(IReadOnlyDictionary<NetworkNodeId, Flt64> NetSupplies,
        IReadOnlyList<PreparedGeneralizedFlowArc<V>> Arcs), ErrorCode, Error<ErrorCode>> Fail(string message) =>
        NetworkSchedulingErrors.Failure<(IReadOnlyDictionary<NetworkNodeId, Flt64>,
            IReadOnlyList<PreparedGeneralizedFlowArc<V>>)>(message);

    /// <summary>
    /// 透传一个已有的失败 / Forward an existing failure.
    /// </summary>
    /// <param name="error">失败信息 / The failure.</param>
    /// <returns>失败结果 / The failure result.</returns>
    private static Result<(IReadOnlyDictionary<NetworkNodeId, Flt64> NetSupplies,
        IReadOnlyList<PreparedGeneralizedFlowArc<V>> Arcs), ErrorCode, Error<ErrorCode>> Fail(
        Error<ErrorCode> error) =>
        Results.Failed<(IReadOnlyDictionary<NetworkNodeId, Flt64>,
            IReadOnlyList<PreparedGeneralizedFlowArc<V>>)>(error);

    /// <summary>构造零多项式 / Build the zero polynomial.</summary>
    /// <returns>零多项式 / The zero polynomial.</returns>
    private static LinearPolynomial<Flt64> Zero() =>
        new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);

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

    /// <summary>判断 Flt64 是否有限 / Whether an Flt64 is finite.</summary>
    /// <param name="value">待判断的值 / The value to check.</param>
    /// <returns>是否有限 / Whether it is finite.</returns>
    private static bool Finite(Flt64 value) => ((IRealNumber<Flt64>)value).IsFinite();

    /// <summary>节点累计量 / Per-node running totals.</summary>
    private sealed class NodeTotals {
        /// <summary>流出投入量之和 / Sum of outgoing arc inputs.</summary>
        public Flt64 OutgoingInput { get; set; } = Flt64.Zero;

        /// <summary>流入到达产出之和 / Sum of incoming arc outputs.</summary>
        public Flt64 IncomingOutput { get; set; } = Flt64.Zero;
    }
}

/// <summary>
/// 面向模型装配和业务结果提取的广义流上下文 / Generalized-flow context for model assembly and domain-result extraction.
/// </summary>
/// <typeparam name="V">领域数值类型 / Domain numeric type.</typeparam>
public sealed class GeneralizedFlowContext<V> where V : struct, IRealNumber<V> {
    private readonly GeneralizedFlowAggregation<V> _aggregation;

    /// <summary>
    /// 构造广义流上下文 / Construct the generalized-flow context.
    /// </summary>
    /// <param name="problem">不可变广义流问题 / Immutable generalized-flow problem.</param>
    /// <param name="adapter">solver 数值转换边界 / Solver numeric-conversion boundary.</param>
    /// <param name="name">模型命名空间 / Model namespace.</param>
    /// <param name="extraPipelineFactories">业务扩展管线工厂 / Business extension pipeline factories.</param>
    /// <param name="solverTolerance">solver 单位可行性容差 / Solver-unit feasibility tolerance.</param>
    public GeneralizedFlowContext(
        GeneralizedFlowProblem<V> problem,
        INetworkSchedulingSolverValueAdapter<V> adapter,
        string name = "generalized_flow",
        IReadOnlyList<Func<GeneralizedFlowExpressions, IPipeline<LinearMetaModel<Flt64>>>>? extraPipelineFactories = null,
        GeneralizedFlowSolverTolerance? solverTolerance = null) {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(adapter);
        Problem = problem;
        Adapter = adapter;
        Name = name;
        SolverTolerance = solverTolerance ?? new GeneralizedFlowSolverTolerance();
        _aggregation = new GeneralizedFlowAggregation<V>(
            problem, adapter, name, extraPipelineFactories, SolverTolerance);
    }

    /// <summary>不可变广义流问题 / The immutable generalized-flow problem.</summary>
    public GeneralizedFlowProblem<V> Problem { get; }

    /// <summary>solver 数值转换边界 / The solver numeric-conversion boundary.</summary>
    public INetworkSchedulingSolverValueAdapter<V> Adapter { get; }

    /// <summary>模型命名空间 / The model namespace.</summary>
    public string Name { get; }

    /// <summary>solver 单位可行性容差 / The solver-unit feasibility tolerance.</summary>
    public GeneralizedFlowSolverTolerance SolverTolerance { get; }

    /// <summary>每模型独立聚合与注册态 / The per-model aggregate and registration state.</summary>
    public GeneralizedFlowAggregation<V> Aggregation => _aggregation;

    /// <summary>已注册的总费用表达式 / The registered total-cost expression.</summary>
    public LinearPolynomial<Flt64> Cost => _aggregation.Cost;

    /// <summary>
    /// 注册广义流模型 / Register the generalized-flow model.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <param name="minimizeCost">是否注册内置费用目标 / Whether to register the built-in cost objective.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model, bool minimizeCost = true) =>
        _aggregation.Register(model, minimizeCost);

    /// <summary>
    /// 获取弧投入表达式 / Get an arc input expression.
    /// </summary>
    /// <param name="arcId">稳定弧标识 / Stable arc identity.</param>
    /// <returns>起点单位投入表达式 / The origin-unit input expression.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> Input(NetworkArcId arcId) =>
        _aggregation.Input(arcId);

    /// <summary>
    /// 获取弧到达产出表达式 / Get an arc arrival-output expression.
    /// </summary>
    /// <param name="arcId">稳定弧标识 / Stable arc identity.</param>
    /// <returns>终点单位产出表达式 / The destination-unit output expression.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> Output(NetworkArcId arcId) =>
        _aggregation.Output(arcId);

    /// <summary>
    /// 提取领域结果与节点守恒核验 / Extract domain results and node-balance checks.
    /// </summary>
    /// <param name="model">已注册并回填求解结果的模型 / Registered model populated with a solver result.</param>
    /// <returns>广义流领域解 / The generalized-flow domain solution.</returns>
    public Result<GeneralizedFlowSolution<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model) =>
        _aggregation.ExtractSolution(model);
}
