// 韧性域：辅助连通流 / Resilience domain: the auxiliary connectivity flow.
// 与固定 Kotlin 基线 `resilience/model/ConnectivityFlow.kt`（4582 字节）对应。
// Mirrors the pinned baseline's `resilience/model/ConnectivityFlow.kt`.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Resilience.Model;

/// <summary>
/// 独立辅助连通流组件，不承诺故障后需求容量 / An auxiliary connectivity-flow component, not a demand-capacity assurance.
/// </summary>
/// <remarks>
/// **这是"结构上还有路"的见证，不是容量保障**。它只证明存在若干条（不）相交路径；故障后能否真正承载需求由场景流决定。把它的存在当成
/// "韧性已达标"会让模型在没有余量的情况下宣称可恢复。
/// **This witnesses "a path still exists structurally", not capacity assurance.** It only proves that several (disjoint) paths
/// exist; whether demand actually flows after a failure is decided by the scenario flows. Treating its presence as "resilience has
/// been achieved" would let the model claim recoverability with no spare capacity.
/// **门控必须同时乘可用性与两端设施可用性**。只乘弧可用性会让服务"建成了但端点没开"的情形也算通；只乘一端设施会让另一端漏网。
/// **The gate must multiply availability with both endpoint facility availabilities.** Multiplying only arc availability would count
/// a service that is built while an endpoint is closed; multiplying only one endpoint would let the other slip through.
/// **中间节点吞吐至多一是节点拆分的等价形式**。不加这条约束，两条"点不相交"的路径可以挤过同一个中间点，约束形同虚设。
/// **Unit internal-node throughput is the equivalent of node splitting.** Without it, two "node-disjoint" paths can squeeze through
/// the same intermediate node and the requirement means nothing.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class ConnectivityFlow<V> where V : struct, IRealNumber<V> {
    private readonly Dictionary<NetworkArcId, Core.Variable.URealVar> _variables = new();
    private bool _attempted;

    /// <summary>
    /// 构造连通流组件 / Construct a connectivity-flow component.
    /// </summary>
    /// <param name="design">共享结构决策 / The shared structural decisions.</param>
    /// <param name="requirement">连通要求 / The connectivity requirement.</param>
    /// <param name="index">要求索引 / The requirement index.</param>
    /// <param name="name">命名空间 / The namespace.</param>
    public ConnectivityFlow(
        NetworkDesignContext<V> design,
        ConnectivityRequirement requirement,
        int index,
        string name) {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(name);
        Design = design;
        Requirement = requirement;
        Index = index;
        Name = name;
    }

    /// <summary>共享结构决策 / The shared structural decisions.</summary>
    public NetworkDesignContext<V> Design { get; }

    /// <summary>连通要求 / The connectivity requirement.</summary>
    public ConnectivityRequirement Requirement { get; }

    /// <summary>要求索引 / The requirement index.</summary>
    public int Index { get; }

    /// <summary>命名空间 / The namespace.</summary>
    public string Name { get; }

    /// <summary>已注册的弧连通变量 / The registered arc connectivity variables.</summary>
    public IReadOnlyDictionary<NetworkArcId, Core.Variable.URealVar> Variables => _variables;

    /// <summary>
    /// 注册单位容量连通流与内部节点吞吐 / Register unit-capacity connectivity and internal-node throughput.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_attempted) {
            return Failure("连通流组件不可复用 / Connectivity component cannot be reused");
        }
        _attempted = true;
        NetworkGraph<NetworkNode<V>, NetworkArc<V>> graph = Design.Problem.Flow.Graph;
        bool sourceKnown = graph.Nodes.Any(node => node.Id.Equals(Requirement.Source));
        bool targetKnown = graph.Nodes.Any(node => node.Id.Equals(Requirement.Target));
        if (Requirement.Source.Equals(Requirement.Target)
            || Requirement.Paths < 1
            || !sourceKnown
            || !targetKnown
            || (Requirement.Kind == ConnectivityKind.Reachable && Requirement.Paths != 1)) {
            return Failure("连通要求无效 / Invalid connectivity requirement");
        }
        var amount = new Flt64(Requirement.Paths);
        foreach (NetworkArc<V> arc in graph.Arcs) {
            var variable = new Core.Variable.URealVar(
                $"{Name}_connect_{Index}_{arc.Id.Value}");
            _variables[arc.Id] = variable;
            Try added = model.Add(variable);
            if (added is Failed<Success, ErrorCode, Error<ErrorCode>> addFailure) {
                return Results.Failed<Success>(addFailure.Error);
            }
            // 可达性要求需要 `paths` 条单位流，不相交要求每条路径各自单位容量。 / Reachability needs `paths` unit flows; disjointness
            // gives each path unit capacity.
            Flt64 bound = Requirement.Kind == ConnectivityKind.Reachable ? amount : Flt64.One;
            var gates = new List<LinearPolynomial<Flt64>> {
                Design.Aggregation.Availability(arc.Id),
                Design.Aggregation.FacilityAvailability(arc.From),
                Design.Aggregation.FacilityAvailability(arc.To),
            };
            foreach (LinearPolynomial<Flt64> gate in gates) {
                var gatedExpression = new LinearPolynomial<Flt64>(
                    new[] { new Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, variable) },
                    Flt64.Zero);
                LinearPolynomial<Flt64> gatedLhs = gatedExpression - (bound * gate);
                Try gated = model.AddConstraint(
                    gatedLhs.Le(Flt64.Zero),
                    null,
                    name: $"{Name}_connect_{Index}_{arc.Id.Value}_gate");
                if (gated is Failed<Success, ErrorCode, Error<ErrorCode>> gateFailure) {
                    return Results.Failed<Success>(gateFailure.Error);
                }
            }
        }
        foreach (NetworkNode<V> node in graph.Nodes) {
            var outgoing = new LinearPolynomial<Flt64>(
                Array.Empty<Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.Zero);
            var incoming = new LinearPolynomial<Flt64>(
                Array.Empty<Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.Zero);
            foreach (NetworkArc<V> arc in graph.Outgoing(node.Id)) {
                outgoing = outgoing + Term(_variables[arc.Id]);
            }
            foreach (NetworkArc<V> arc in graph.Incoming(node.Id)) {
                incoming = incoming + Term(_variables[arc.Id]);
            }
            Flt64 balance = Flt64.Zero;
            if (node.Id.Equals(Requirement.Source)) {
                balance = amount;
            } else if (node.Id.Equals(Requirement.Target)) {
                balance = new Flt64(-Requirement.Paths);
            }
            Try balanced = model.AddConstraint(
                (outgoing - incoming).Eq(balance),
                null,
                name: $"{Name}_connect_{Index}_{node.Id.Value}_balance");
            if (balanced is Failed<Success, ErrorCode, Error<ErrorCode>> balanceFailure) {
                return Results.Failed<Success>(balanceFailure.Error);
            }
            if (Requirement.Kind == ConnectivityKind.NodeDisjoint
                && !node.Id.Equals(Requirement.Source)
                && !node.Id.Equals(Requirement.Target)) {
                Try throughput = model.AddConstraint(
                    outgoing.Le(Flt64.One),
                    null,
                    name: $"{Name}_connect_{Index}_{node.Id.Value}_throughput");
                if (throughput is Failed<Success, ErrorCode, Error<ErrorCode>> throughputFailure) {
                    return Results.Failed<Success>(throughputFailure.Error);
                }
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 把单位变量包成一次多项式 / Wrap a unit variable as a first-degree polynomial.
    /// </summary>
    /// <param name="variable">变量 / The variable.</param>
    /// <returns>多项式 / The polynomial.</returns>
    private static LinearPolynomial<Flt64> Term(Core.Variable.URealVar variable) =>
        new(new[] { new Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, variable) }, Flt64.Zero);

    /// <summary>构造失败结果 / Build a failure result.</summary>
    /// <param name="message">错误信息 / The error message.</param>
    /// <returns>失败结果 / The failure result.</returns>
    private static Try Failure(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
