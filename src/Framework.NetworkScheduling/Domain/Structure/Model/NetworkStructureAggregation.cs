// 结构聚合与上下文 / Structure aggregation and context.
// And.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure.Model;

/// <summary>
/// 组合建设、根树和分区归属组件 / Aggregate for construction, rooted tree and partition ownership.
/// </summary>
/// <remarks>
/// **本聚合刻意不注册商品流**。结构决策只回答"建什么、树怎么连、谁管哪块"，商品流由调用方按场景通过 `AttachFlow` 挂上去；把流量
/// 一起注册进来会让结构模型必须同时给出货流方案，而结构决策往往是先定、再按场景反复求解的。
/// **This aggregate deliberately registers no commodity flows.** The structural decision only answers "what to build, how the tree
/// connects, who governs which area"; commodity flows are attached per scenario by the caller through `AttachFlow`. Registering flows
/// here would force the structural model to produce a cargo plan as well, whereas the structure is usually decided once and then
/// re-solved per scenario.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkStructureAggregation<V> where V : struct, IRealNumber<V> {
    private readonly NetworkStructureProblem<V> _problem;
    private readonly string _name;
    private bool _attempted;
    private LinearMetaModel<Flt64>? _registeredModel;
    private bool? _registeredObjective;

    /// <summary>
    /// 构造结构聚合 / Construct the structure aggregation.
    /// </summary>
    /// <param name="problem">结构问题 / Structure problem.</param>
    /// <param name="designContext">独占建设上下文 / Owned construction context.</param>
    /// <param name="name">模型命名空间 / Model namespace.</param>
    public NetworkStructureAggregation(
        NetworkStructureProblem<V> problem,
        NetworkDesignContext<V> designContext,
        string name = "network_structure") {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(designContext);
        ArgumentNullException.ThrowIfNull(name);
        _problem = problem;
        DesignContext = designContext;
        _name = name;
        Tree = new RootedTreeComponent<V>(problem, designContext, name);
        Coverage = new FacilityCoverageComponent<V>(problem, designContext, name);
        ConstructionCostObjective = new NetworkStructureConstructionCostObjective(
            () => ConstructionCost, $"{name}_construction_cost");
    }

    /// <summary>结构问题 / The structure problem.</summary>
    public NetworkStructureProblem<V> Problem => _problem;

    /// <summary>独占建设上下文 / The owned construction context.</summary>
    public NetworkDesignContext<V> DesignContext { get; }

    /// <summary>根树组件 / The rooted-tree component.</summary>
    public RootedTreeComponent<V> Tree { get; }

    /// <summary>设施覆盖组件 / The facility-coverage component.</summary>
    public FacilityCoverageComponent<V> Coverage { get; }

    /// <summary>建设费用表达式 / The construction-cost expression.</summary>
    public LinearPolynomial<Flt64> ConstructionCost => DesignContext.ConstructionCost;

    /// <summary>建设费用目标管线 / The construction-cost objective pipeline.</summary>
    public NetworkStructureConstructionCostObjective ConstructionCostObjective { get; }

    /// <summary>
    /// 注册底层建设变量、结构组件及可选建设费用目标；不注册商品流 / Register construction, structure and an optional
    /// construction-cost objective, without commodity flows.
    /// </summary>
    /// <remarks>
    /// **`validateProblem` 必须在任何变量落库之前跑完**。这些校验拦住的都是"模型能解但规划不可施工"的输入——节点不存在、流弧与图快照
    /// 不一致、设施或服务重复、配额越界。若先建变量再校验，失败时模型里已经留下一堆半成品变量，而调用方通常会把同一个元模型拿去复用。
    /// **`validateProblem` must complete before any variable is added.** These checks all catch inputs that "solve fine but cannot be
    /// built": missing nodes, flow arcs disagreeing with the graph snapshot, duplicate facilities or services, out-of-range quotas. Adding
    /// variables first and validating afterwards leaves half-created variables behind on failure, and callers typically reuse the same
    /// meta model.
    /// </remarks>
    /// <param name="model">目标元模型 / Target meta model.</param>
    /// <param name="minimizeCost">是否最小化建设费用 / Whether to minimise construction cost.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model, bool minimizeCost = true) {
        ArgumentNullException.ThrowIfNull(model);
        if (ReferenceEquals(_registeredModel, model)) {
            return _registeredObjective == minimizeCost
                ? Results.Ok(Results.SuccessInstance)
                : NetworkSchedulingErrors.TryFailure(
                    "重复结构注册的目标选项不一致 / Inconsistent repeated structure-registration objective option");
        }
        if (_attempted) {
            return NetworkSchedulingErrors.TryFailure(
                "结构上下文不可复用 / Structure context cannot be reused");
        }
        _attempted = true;

        Try validation = ValidateProblem();
        if (validation is Failed<Success, ErrorCode, Error<ErrorCode>> validationFailure) {
            return Results.Failed<Success>(validationFailure.Error);
        }
        Try construction = DesignContext.Aggregation.Construction.Register(model);
        if (construction is Failed<Success, ErrorCode, Error<ErrorCode>> constructionFailure) {
            return Results.Failed<Success>(constructionFailure.Error);
        }
        Try tree = Tree.Register(model);
        if (tree is Failed<Success, ErrorCode, Error<ErrorCode>> treeFailure) {
            return Results.Failed<Success>(treeFailure.Error);
        }
        Try coverage = Coverage.Register(model);
        if (coverage is Failed<Success, ErrorCode, Error<ErrorCode>> coverageFailure) {
            return Results.Failed<Success>(coverageFailure.Error);
        }
        if (minimizeCost) {
            Try objective = ConstructionCostObjective.Invoke(model);
            if (objective is Failed<Success, ErrorCode, Error<ErrorCode>> objectiveFailure) {
                return Results.Failed<Success>(objectiveFailure.Error);
            }
        }

        _registeredModel = model;
        _registeredObjective = minimizeCost;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 读取树弧选择表达式 / Read a tree-arc selection expression.
    /// </summary>
    /// <param name="arc">稳定弧标识 / Stable arc identity.</param>
    /// <returns>选择表达式或未知弧错误 / The selection expression, or an unknown-arc error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> SelectedTreeArc(NetworkArcId arc) =>
        Tree.SelectedTreeArc(arc);

    /// <summary>
    /// 读取树节点启用表达式 / Read the tree-node activation expression.
    /// </summary>
    /// <param name="node">节点标识 / Node identity.</param>
    /// <returns>启用表达式或非候选节点错误 / The activation expression, or a non-candidate-node error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> ActiveTreeNode(NetworkNodeId node) =>
        Tree.ActiveTreeNode(node);

    /// <summary>
    /// 读取节点的父弧选择和 / Read the sum of selected parent arcs for a node.
    /// </summary>
    /// <param name="node">节点标识 / Node identity.</param>
    /// <returns>父弧表达式或非候选节点错误 / The parent-arc expression, or a non-candidate-node error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> ParentArc(NetworkNodeId node) =>
        Tree.ParentArc(node);

    /// <summary>
    /// 读取分区归属二元变量 / Read a partition-owner binary variable.
    /// </summary>
    /// <param name="partition">分区标识 / Partition identity.</param>
    /// <param name="facility">设施节点 / Facility node.</param>
    /// <returns>归属表达式或不合格组合错误 / The assignment expression, or an ineligible-pair error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> PartitionAssignment(
        string partition,
        NetworkNodeId facility) =>
        Coverage.Assignment(partition, facility);

    /// <summary>
    /// 提取结构结果并校验其父子关系 / Extract structure and validate parent-child relations.
    /// </summary>
    /// <param name="model">已求解元模型 / Solved meta model.</param>
    /// <returns>结构结果 / The structure solution.</returns>
    public Result<NetworkStructureSolution<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (!ReferenceEquals(_registeredModel, model)) {
            return NetworkSchedulingErrors.Failure<NetworkStructureSolution<V>>(
                "结构模型未注册 / Structure model is not registered");
        }
        Result<NetworkConstructionSolution<V>, ErrorCode, Error<ErrorCode>> construction =
            DesignContext.Aggregation.Construction.ExtractSolution(model);
        if (construction is Failed<NetworkConstructionSolution<V>, ErrorCode, Error<ErrorCode>> constructionFailure) {
            return Results.Failed<NetworkStructureSolution<V>>(constructionFailure.Error);
        }
        Result<RootedTreeSelection, ErrorCode, Error<ErrorCode>> tree = Tree.ExtractSolution(model);
        if (tree is Failed<RootedTreeSelection, ErrorCode, Error<ErrorCode>> treeFailure) {
            return Results.Failed<NetworkStructureSolution<V>>(treeFailure.Error);
        }
        Result<IReadOnlyDictionary<string, NetworkNodeId>, ErrorCode, Error<ErrorCode>> owners =
            Coverage.ExtractSolution(model);
        if (owners is Failed<IReadOnlyDictionary<string, NetworkNodeId>, ErrorCode, Error<ErrorCode>> ownerFailure) {
            return Results.Failed<NetworkStructureSolution<V>>(ownerFailure.Error);
        }
        return Results.Ok(new NetworkStructureSolution<V>(
            construction.Value,
            tree.Value.Arcs,
            tree.Value.Nodes,
            tree.Value.Parents,
            owners.Value));
    }

    /// <summary>
    /// 校验结构问题的全部前提 / Validate every precondition of the structure problem.
    /// </summary>
    /// <returns>校验结果 / The validation result.</returns>
    private Try ValidateProblem() {
        FlowProblem<V> flowProblem = _problem.Design.Flow;
        NetworkGraph<NetworkNode<V>, NetworkArc<V>> graph = flowProblem.Graph;

        if (string.IsNullOrWhiteSpace(_problem.Root.Value)
            || graph.Node(_problem.Root) is null
            || _problem.TreeNodes.Any(node => string.IsNullOrWhiteSpace(node.Value) || graph.Node(node) is null)) {
            return NetworkSchedulingErrors.TryFailure(
                "根、终端或中转节点为空或不存在 / Root, terminal or relay node is blank or missing");
        }

        var flowArcIds = flowProblem.Arcs.Select(arc => arc.NetworkArc.Id).ToList();
        if (new HashSet<NetworkArcId>(flowArcIds).Count != flowArcIds.Count
            || !new HashSet<NetworkArcId>(flowArcIds).SetEquals(graph.ArcIds)
            || flowProblem.Arcs.Any(arc =>
                !graph.From(arc.NetworkArc).Equals(arc.NetworkArc.From)
                || !graph.To(arc.NetworkArc).Equals(arc.NetworkArc.To))) {
            return NetworkSchedulingErrors.TryFailure(
                "流弧与有向图快照不一致 / Flow arcs do not match the directed graph snapshot");
        }

        var facilities = _problem.Design.Facilities
            .Select(facility => facility.Node).ToList();
        if (new HashSet<NetworkNodeId>(facilities).Count != facilities.Count
            || facilities.Any(node => graph.Node(node) is null)) {
            return NetworkSchedulingErrors.TryFailure(
                "候选设施重复或不存在 / Duplicate or missing candidate facility");
        }

        var services = _problem.Design.Services.Select(service => service.Arc).ToList();
        if (new HashSet<NetworkArcId>(services).Count != services.Count
            || services.Any(id => !flowArcIds.Contains(id))) {
            return NetworkSchedulingErrors.TryFailure(
                "候选服务重复或不存在 / Duplicate or missing candidate service");
        }

        if (_problem.Partitions.Any(partition => string.IsNullOrWhiteSpace(partition.Id))
            || _problem.Partitions.Select(partition => partition.Id).Distinct(StringComparer.Ordinal).Count()
                != _problem.Partitions.Count
            || _problem.Partitions.Any(partition =>
                partition.EligibleFacilities.Any(node => graph.Node(node) is null))) {
            return NetworkSchedulingErrors.TryFailure(
                "分区标识重复、为空或引用缺失设施节点 / Duplicate, blank partition identity or missing facility node");
        }

        if (_problem.Quotas.Select(quota => quota.Facility).Distinct().Count() != _problem.Quotas.Count
            || _problem.Quotas.Any(quota =>
                graph.Node(quota.Facility) is null
                || quota.Minimum < 0
                || (quota.Maximum is { } maximum && maximum < quota.Minimum))) {
            return NetworkSchedulingErrors.TryFailure(
                "设施配额重复、越界或引用缺失节点 / Duplicate, invalid or missing facility quota");
        }

        return Results.Ok(Results.SuccessInstance);
    }
}

/// <summary>
/// 结构决策的注册入口与结果提取 / Registration entry and result extraction for structural decisions.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkStructureContext<V> where V : struct, IRealNumber<V> {
    private readonly NetworkStructureAggregation<V> _aggregation;

    /// <summary>
    /// 构造结构上下文 / Construct the structure context.
    /// </summary>
    /// <param name="problem">结构问题 / Structure problem.</param>
    /// <param name="adapter">数值边界 / Numeric boundary.</param>
    /// <param name="name">模型命名空间 / Model namespace.</param>
    public NetworkStructureContext(
        NetworkStructureProblem<V> problem,
        INetworkSchedulingSolverValueAdapter<V> adapter,
        string name = "network_structure") {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(adapter);
        Problem = problem;
        Adapter = adapter;
        Name = name;
        DesignContext = new NetworkDesignContext<V>(problem.Design, adapter, name);
        _aggregation = new NetworkStructureAggregation<V>(problem, DesignContext, name);
    }

    /// <summary>结构问题 / The structure problem.</summary>
    public NetworkStructureProblem<V> Problem { get; }

    /// <summary>数值边界 / The numeric boundary.</summary>
    public INetworkSchedulingSolverValueAdapter<V> Adapter { get; }

    /// <summary>模型命名空间 / The model namespace.</summary>
    public string Name { get; }

    /// <summary>独占建设上下文 / The owned construction context.</summary>
    public NetworkDesignContext<V> DesignContext { get; }

    /// <summary>结构聚合 / The structure aggregation.</summary>
    public NetworkStructureAggregation<V> Aggregation => _aggregation;

    /// <summary>建设费用表达式 / The construction-cost expression.</summary>
    public LinearPolynomial<Flt64> ConstructionCost => _aggregation.ConstructionCost;

    /// <summary>
    /// 注册结构模型 / Register the structure model.
    /// </summary>
    /// <param name="model">目标元模型 / Target meta model.</param>
    /// <param name="minimizeCost">是否注册建设费用目标 / Whether to register the construction-cost objective.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model, bool minimizeCost = true) =>
        _aggregation.Register(model, minimizeCost);

    /// <summary>
    /// 提取结构结果 / Extract the structure solution.
    /// </summary>
    /// <param name="model">已求解元模型 / Solved meta model.</param>
    /// <returns>结构结果 / The structure solution.</returns>
    public Result<NetworkStructureSolution<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model) =>
        _aggregation.ExtractSolution(model);
}
