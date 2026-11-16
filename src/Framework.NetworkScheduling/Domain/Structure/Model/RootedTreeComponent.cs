// 带显式父弧和深度证据的根有向树组件 / Rooted directed tree with explicit parent arcs and depth certificates.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure.Model;

/// <summary>
/// 从 solver 解中恢复出的根树选择 / A rooted-tree selection recovered from a solver solution.
/// </summary>
/// <param name="Arcs">选中的树弧 / Selected tree arcs.</param>
/// <param name="Nodes">活动树节点 / Active tree nodes.</param>
/// <param name="Parents">每个活动非根节点的父弧 / Parent arc for each active non-root node.</param>
internal sealed record RootedTreeSelection(
    IReadOnlySet<NetworkArcId> Arcs,
    IReadOnlySet<NetworkNodeId> Nodes,
    IReadOnlyDictionary<NetworkNodeId, NetworkArcId> Parents);

/// <summary>
/// 带显式父弧和深度证据的根有向树组件 / Rooted directed tree with explicit parent arcs and depth certificates.
/// </summary>
/// <remarks>
/// **无环性靠深度证书保证，不靠"每节点恰好一个父弧"**。父弧唯一只排除"两个父"，环形结构仍然满足它：一个环上每个节点都恰好有一个
/// 父，却谁也到不了根。深度约束 `depth(to) ≥ depth(from) + 1 − M(1 − 选中)` 才对选中的弧强制严格递增，从而排除有向环。
/// **Acyclicity is guaranteed by depth certificates, not by "exactly one parent per node".** Unique parent only rules out "two
/// parents"; a cycle satisfies it — every node on the cycle has exactly one parent and none reach the root. The depth row
/// `depth(to) ≥ depth(from) + 1 − M(1 − selected)` forces strict increase on selected arcs and thereby excludes directed cycles.
/// **深度上界乘在节点启用量上，而不是常数**。中转节点未启用时它的深度必须为 0；若上界写成常数，未启用的中转节点仍可持有非零深度，
/// 于是大 M 松弛后的深度约束可以在不该出现的位置"放行"一条弧。
/// **The depth bound multiplies node activation rather than being constant.** An unused relay node must hold depth 0; with a constant
/// bound an unused relay could still carry a non-zero depth and the big-M-relaxed ordering row would let an arc through where it
/// should not.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class RootedTreeComponent<V> where V : struct, IRealNumber<V> {
    private readonly NetworkStructureProblem<V> _problem;
    private readonly NetworkDesignContext<V> _designContext;
    private readonly string _name;
    private readonly Dictionary<NetworkArcId, BinVar> _arcVariables = new();
    private readonly Dictionary<NetworkNodeId, BinVar> _activeVariables = new();
    private readonly Dictionary<NetworkNodeId, URealVar> _depthVariables = new();
    private readonly Dictionary<NetworkNodeId, IReadOnlyList<NetworkArcId>> _incoming = new();
    private bool _attempted;
    private LinearMetaModel<Flt64>? _registeredModel;

    /// <summary>
    /// 构造根树组件 / Construct the rooted-tree component.
    /// </summary>
    /// <param name="problem">结构问题 / Structure problem.</param>
    /// <param name="designContext">建设上下文 / Construction context.</param>
    /// <param name="name">模型命名空间 / Model namespace.</param>
    public RootedTreeComponent(
        NetworkStructureProblem<V> problem,
        NetworkDesignContext<V> designContext,
        string name) {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(designContext);
        ArgumentNullException.ThrowIfNull(name);
        _problem = problem;
        _designContext = designContext;
        _name = name;
    }

    /// <summary>
    /// 读取树弧选择表达式 / Read a tree-arc selection expression.
    /// </summary>
    /// <param name="arc">稳定弧标识 / Stable arc identity.</param>
    /// <returns>选择表达式或未知弧错误 / The selection expression, or an unknown-arc error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> SelectedTreeArc(NetworkArcId arc) {
        ArgumentNullException.ThrowIfNull(arc);
        if (_registeredModel is null) {
            return NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                "根树尚未注册 / Rooted tree is not registered");
        }
        return _problem.Design.Flow.Graph.Arc(arc) is null
            ? NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                "树弧不存在 / Tree arc does not exist")
            : Results.Ok(ArcExpression(arc));
    }

    /// <summary>
    /// 读取树节点启用表达式 / Read the tree-node activation expression.
    /// </summary>
    /// <param name="node">节点标识 / Node identity.</param>
    /// <returns>启用表达式或非候选节点错误 / The activation expression, or a non-candidate-node error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> ActiveTreeNode(NetworkNodeId node) {
        ArgumentNullException.ThrowIfNull(node);
        if (_registeredModel is null) {
            return NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                "根树尚未注册 / Rooted tree is not registered");
        }
        return TreeNodes.Contains(node)
            ? Results.Ok(ActiveNode(node))
            : NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                "节点不是树候选节点 / Node is not a tree candidate");
    }

    /// <summary>供聚合使用的父弧选择 / Parent-arc selection exposed to the aggregation.</summary>
    /// <param name="node">节点标识 / Node identity.</param>
    /// <returns>父弧表达式或错误 / The parent-arc expression, or an error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> ParentArc(NetworkNodeId node) {
        ArgumentNullException.ThrowIfNull(node);
        if (_registeredModel is null) {
            return NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                "根树尚未注册 / Rooted tree is not registered");
        }
        return TreeNodes.Contains(node)
            ? Results.Ok(ParentArcExpression(node))
            : NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                "节点不是树候选节点 / Node is not a tree candidate");
    }

    /// <summary>
    /// 注册父弧唯一性、建设联动和有界深度无环约束 / Register unique-parent, construction-link and bounded-depth acyclicity constraints.
    /// </summary>
    /// <param name="model">目标元模型 / Target meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_attempted) {
            return NetworkSchedulingErrors.TryFailure(
                "根树组件不可复用 / Rooted-tree component cannot be reused");
        }
        _attempted = true;

        HashSet<NetworkNodeId> treeNodes = TreeNodes;
        var relays = new HashSet<NetworkNodeId>(_problem.SteinerNodes);
        relays.Remove(_problem.Root);
        relays.ExceptWith(_problem.Terminals);

        foreach (NetworkNodeId node in relays) {
            var variable = new BinVar($"{_name}_tree_node_{node.Value}");
            if (model.Add(variable) is Failed<Success, ErrorCode, Error<ErrorCode>> relayFailure) {
                return Results.Failed<Success>(relayFailure.Error);
            }
            _activeVariables[node] = variable;
        }

        int maximumDepth = global::System.Math.Max(treeNodes.Count - 1, 0);
        foreach (NetworkNodeId node in treeNodes) {
            if (node.Equals(_problem.Root)) {
                continue;
            }
            var variable = new URealVar($"{_name}_tree_depth_{node.Value}");
            if (model.Add(variable) is Failed<Success, ErrorCode, Error<ErrorCode>> depthFailure) {
                return Results.Failed<Success>(depthFailure.Error);
            }
            _depthVariables[node] = variable;
            Try bound = model.AddConstraint(
                DepthExpression(node).Le(Scale(new Flt64(maximumDepth), ActiveNode(node))),
                null,
                name: $"{_name}_tree_depth_bound_{node.Value}");
            if (bound is Failed<Success, ErrorCode, Error<ErrorCode>> boundFailure) {
                return Results.Failed<Success>(boundFailure.Error);
            }
        }

        var facilityNodes = new HashSet<NetworkNodeId>(_problem.Design.Facilities.Select(facility => facility.Node));
        foreach (NetworkNodeId node in treeNodes) {
            if (!facilityNodes.Contains(node)) {
                continue;
            }
            Try linking = model.AddConstraint(
                ActiveNode(node).Le(
                    _designContext.Aggregation.Construction.FacilityAvailability(node)),
                null,
                name: $"{_name}_tree_facility_{node.Value}");
            if (linking is Failed<Success, ErrorCode, Error<ErrorCode>> linkingFailure) {
                return Results.Failed<Success>(linkingFailure.Error);
            }
        }

        foreach (Domain.Flow.Model.FlowArc<V> arc in _problem.Design.Flow.Arcs) {
            NetworkArcId id = arc.NetworkArc.Id;
            if (!treeNodes.Contains(arc.NetworkArc.From)
                || !treeNodes.Contains(arc.NetworkArc.To)
                || arc.NetworkArc.From.Equals(arc.NetworkArc.To)) {
                continue;
            }
            var variable = new BinVar($"{_name}_tree_arc_{id.Value}");
            if (model.Add(variable) is Failed<Success, ErrorCode, Error<ErrorCode>> arcFailure) {
                return Results.Failed<Success>(arcFailure.Error);
            }
            _arcVariables[id] = variable;
            foreach ((LinearPolynomial<Flt64> right, string suffix) in new[] {
                (_designContext.Aggregation.Construction.Availability(id), "service"),
                (ActiveNode(arc.NetworkArc.From), "parent_active"),
                (ActiveNode(arc.NetworkArc.To), "child_active"),
            }) {
                Try linkage = model.AddConstraint(
                    ArcExpression(id).Le(right), null, name: $"{_name}_tree_{suffix}_{id.Value}");
                if (linkage is Failed<Success, ErrorCode, Error<ErrorCode>> linkageFailure) {
                    return Results.Failed<Success>(linkageFailure.Error);
                }
            }
        }

        foreach (NetworkNodeId node in treeNodes) {
            _incoming[node] = _problem.Design.Flow.Arcs
                .Where(arc => arc.NetworkArc.To.Equals(node)
                    && treeNodes.Contains(arc.NetworkArc.From)
                    && !arc.NetworkArc.From.Equals(arc.NetworkArc.To))
                .Select(arc => arc.NetworkArc.Id)
                .ToList();
        }

        foreach (NetworkNodeId node in treeNodes) {
            LinearPolynomial<Flt64> parentExpression = ParentArcExpression(node);
            Try relation = model.AddConstraint(
                node.Equals(_problem.Root)
                    ? parentExpression.Eq(Constant(Flt64.Zero))
                    : parentExpression.Eq(ActiveNode(node)),
                null,
                name: $"{_name}_tree_parent_{node.Value}");
            if (relation is Failed<Success, ErrorCode, Error<ErrorCode>> relationFailure) {
                return Results.Failed<Success>(relationFailure.Error);
            }
        }

        var bigM = new Flt64(global::System.Math.Max(treeNodes.Count, 1));
        foreach (Domain.Flow.Model.FlowArc<V> arc in _problem.Design.Flow.Arcs) {
            if (!_arcVariables.ContainsKey(arc.NetworkArc.Id) || arc.NetworkArc.To.Equals(_problem.Root)) {
                continue;
            }
            LinearPolynomial<Flt64> relaxation = Subtract(
                Constant(Flt64.One), ArcExpression(arc.NetworkArc.Id));
            LinearInequality<Flt64> order = DepthExpression(arc.NetworkArc.To).Ge(
                Add(
                    Add(DepthExpression(arc.NetworkArc.From), Constant(Flt64.One)),
                    Scale(Flt64.Zero.Minus(bigM), relaxation)));
            Try orderResult = model.AddConstraint(
                order, null, name: $"{_name}_tree_order_{arc.NetworkArc.Id.Value}");
            if (orderResult is Failed<Success, ErrorCode, Error<ErrorCode>> orderFailure) {
                return Results.Failed<Success>(orderFailure.Error);
            }
        }

        _registeredModel = model;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 从 solver token 提取并防御性校验树结构 / Extract and defensively validate the tree from solver tokens.
    /// </summary>
    /// <remarks>
    /// **父弧数量与有向环都要在提取时再查一遍**。约束已经排除了这两种情形，但提取是模型与上层之间唯一的关口：求解器的容差、松弛变量
    /// 或后续手工修改都可能让一个不成立的树通过，而"树里有个环"在上层看起来只是某条线路多绕了一圈。
    /// **Both the parent count and directed cycles are re-checked during extraction.** The constraints already exclude them, but
    /// extraction is the only gate between the model and the caller: solver tolerances, slack variables or later manual edits can let
    /// an invalid tree through, and "there is a cycle in the tree" just looks like a route that goes around once more.
    /// </remarks>
    /// <param name="model">已求解元模型 / Solved meta model.</param>
    /// <returns>树选择或缺失／无效结果错误 / The tree selection, or a missing/invalid-result error.</returns>
    internal Result<RootedTreeSelection, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (!ReferenceEquals(_registeredModel, model)) {
            return NetworkSchedulingErrors.Failure<RootedTreeSelection>(
                "根树模型未注册 / Rooted-tree model is not registered");
        }
        Flt64 half = new(0.5);
        var selected = new HashSet<NetworkArcId>();
        foreach ((NetworkArcId id, BinVar variable) in _arcVariables) {
            Token<Flt64>? token = model.Tokens.Find(variable);
            if (token?.Result is not { } value) {
                return NetworkSchedulingErrors.Failure<RootedTreeSelection>(
                    "缺失树弧结果 / Missing tree-arc result");
            }
            if (value.CompareTo(half) > 0) {
                selected.Add(id);
            }
        }

        var active = new HashSet<NetworkNodeId>();
        foreach (NetworkNodeId node in TreeNodes) {
            if (!_activeVariables.TryGetValue(node, out BinVar? variable)) {
                active.Add(node);
                continue;
            }
            Token<Flt64>? token = model.Tokens.Find(variable);
            if (token?.Result is not { } value) {
                return NetworkSchedulingErrors.Failure<RootedTreeSelection>(
                    "缺失中转节点结果 / Missing relay-node result");
            }
            if (value.CompareTo(half) > 0) {
                active.Add(node);
            }
        }

        var parents = new Dictionary<NetworkNodeId, NetworkArcId>();
        foreach (NetworkNodeId node in active) {
            if (node.Equals(_problem.Root)) {
                continue;
            }
            var selectedParents = (_incoming.TryGetValue(node, out IReadOnlyList<NetworkArcId>? incoming)
                    ? incoming
                    : Array.Empty<NetworkArcId>())
                .Where(selected.Contains)
                .ToList();
            if (selectedParents.Count != 1) {
                return NetworkSchedulingErrors.Failure<RootedTreeSelection>(
                    "树节点父弧数量无效 / Invalid rooted-tree parent count");
            }
            parents[node] = selectedParents[0];
        }

        foreach (NetworkNodeId node in active) {
            if (node.Equals(_problem.Root)) {
                continue;
            }
            var visited = new HashSet<NetworkNodeId>();
            NetworkNodeId current = node;
            while (!current.Equals(_problem.Root)) {
                if (!visited.Add(current)) {
                    return NetworkSchedulingErrors.Failure<RootedTreeSelection>(
                        "求解结果包含有向环 / Solver result contains a directed cycle");
                }
                if (!parents.TryGetValue(current, out NetworkArcId? parent)) {
                    return NetworkSchedulingErrors.Failure<RootedTreeSelection>(
                        "活动节点未连接到根 / Active node is disconnected from root");
                }
                NetworkArc<V>? parentArc = _problem.Design.Flow.Graph.Arc(parent);
                if (parentArc is null) {
                    return NetworkSchedulingErrors.Failure<RootedTreeSelection>(
                        "树父弧结果不存在 / Parent arc result does not exist");
                }
                current = parentArc.From;
            }
        }

        return Results.Ok(new RootedTreeSelection(selected, active, parents));
    }

    /// <summary>候选树节点 / Candidate tree nodes.</summary>
    private HashSet<NetworkNodeId> TreeNodes => new(_problem.TreeNodes);

    /// <summary>节点启用量表达式 / Node activation expression.</summary>
    /// <param name="node">节点标识 / Node identity.</param>
    /// <returns>启用表达式 / The activation expression.</returns>
    private LinearPolynomial<Flt64> ActiveNode(NetworkNodeId node) {
        if (node.Equals(_problem.Root) || _problem.Terminals.Contains(node)) {
            return Constant(Flt64.One);
        }
        return _activeVariables.TryGetValue(node, out BinVar? variable) ? Variable(variable) : Zero();
    }

    /// <summary>树弧选择表达式 / Tree-arc selection expression.</summary>
    /// <param name="arc">弧标识 / Arc identity.</param>
    /// <returns>选择表达式 / The selection expression.</returns>
    private LinearPolynomial<Flt64> ArcExpression(NetworkArcId arc) =>
        _arcVariables.TryGetValue(arc, out BinVar? variable) ? Variable(variable) : Zero();

    /// <summary>父弧选择之和 / The sum of selected parent arcs.</summary>
    /// <param name="node">节点标识 / Node identity.</param>
    /// <returns>父弧表达式 / The parent-arc expression.</returns>
    private LinearPolynomial<Flt64> ParentArcExpression(NetworkNodeId node) {
        LinearPolynomial<Flt64> expression = Zero();
        if (_incoming.TryGetValue(node, out IReadOnlyList<NetworkArcId>? incoming)) {
            foreach (NetworkArcId arc in incoming) {
                expression = Add(expression, ArcExpression(arc));
            }
        }
        return expression;
    }

    /// <summary>节点深度表达式 / Node depth expression.</summary>
    /// <param name="node">节点标识 / Node identity.</param>
    /// <returns>深度表达式 / The depth expression.</returns>
    private LinearPolynomial<Flt64> DepthExpression(NetworkNodeId node) =>
        _depthVariables.TryGetValue(node, out URealVar? variable) ? Variable(variable) : Zero();

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
    private static LinearPolynomial<Flt64> Variable(IVariableItem variable) =>
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
}
