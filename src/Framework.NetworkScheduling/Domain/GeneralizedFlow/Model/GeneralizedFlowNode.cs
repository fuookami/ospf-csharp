// 广义流节点与问题 / Generalized-flow node and problem.
// And.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Model;

/// <summary>
/// 带计量单位和硬净供需的广义流节点 / Generalized-flow node with a measurement unit and hard net supply.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class GeneralizedFlowNode<V> where V : struct, IRealNumber<V> {
    private GeneralizedFlowNode(NetworkNode<V> networkNode, PhysicalUnit unit, Quantity<V> netSupply) {
        NetworkNode = networkNode;
        Unit = unit;
        NetSupply = netSupply;
    }

    /// <summary>通用网络节点 / Generic network node.</summary>
    public NetworkNode<V> NetworkNode { get; }

    /// <summary>节点投入／产出计量单位 / Measurement unit for node inputs and outputs.</summary>
    public PhysicalUnit Unit { get; }

    /// <summary>正值供给、负值需求 / Positive supply and negative demand.</summary>
    public Quantity<V> NetSupply { get; }

    /// <summary>稳定节点标识 / Stable node identity.</summary>
    public NetworkNodeId Id => NetworkNode.Id;

    /// <summary>
    /// 创建广义流节点 / Create a generalized-flow node.
    /// </summary>
    /// <remarks>
    /// **净供需的量纲必须与节点计量单位一致**。不检查就能构造出一个"以吨计量、以桶守恒"的节点，节点平衡约束两边量纲不同，求解器
    /// 只会看到一串数字并给出一个量纲上毫无意义的解。
    /// **The net supply's dimension must match the node's measurement unit.** Without the check one can build a node measured in
    /// tonnes but balanced in barrels; the balance constraint then compares different dimensions, and the solver sees only numbers
    /// and returns a dimensionally meaningless answer.
    /// </remarks>
    /// <param name="networkNode">网络节点 / Network node.</param>
    /// <param name="unit">节点计量单位 / Node measurement unit.</param>
    /// <param name="netSupply">净供需量，正值供给、负值需求 / Net balance; positive is supply, negative is demand.</param>
    /// <returns>节点或量纲错误 / The node, or a dimension error.</returns>
    public static Result<GeneralizedFlowNode<V>, ErrorCode, Error<ErrorCode>> Invoke(
        NetworkNode<V> networkNode,
        PhysicalUnit unit,
        Quantity<V> netSupply) {
        ArgumentNullException.ThrowIfNull(networkNode);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(netSupply);

        if (!netSupply.Unit.SameDimension(unit)) {
            return NetworkSchedulingErrors.Failure<GeneralizedFlowNode<V>>(
                "广义流节点净供需与计量单位量纲不一致 / Generalized-flow node balance dimension does not match its "
                + "measurement unit");
        }
        return Results.Ok(new GeneralizedFlowNode<V>(networkNode, unit, netSupply));
    }
}

/// <summary>
/// 不含 solver 注册态的单商品广义流问题 / Single-commodity generalized-flow problem without solver registration state.
/// </summary>
/// <remarks>
/// **问题本身是不可变数据，注册态不在这里**。把变量、约束、目标这些 solver 侧状态放进来，问题对象就会随每次求解而变化，
/// 同一份问题无法被并行求解或重放——而重放正是定位数值问题的主要手段。
/// **The problem is immutable data; registration state lives elsewhere.** Putting variables, constraints and objectives here would
/// make the object mutate with every solve, so one problem could not be solved in parallel or replayed — and replaying is the main
/// tool for diagnosing numerical trouble.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class GeneralizedFlowProblem<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造广义流问题 / Construct a generalized-flow problem.
    /// </summary>
    /// <param name="graph">通用有向网络图 / Generic directed network graph.</param>
    /// <param name="nodes">节点单位与硬供需 / Node units and hard balances.</param>
    /// <param name="arcs">单输入服务弧 / Single-input service arcs.</param>
    /// <param name="zero">领域数值零 / Domain numeric zero.</param>
    /// <param name="costUnit">总费用单位 / Total-cost unit.</param>
    public GeneralizedFlowProblem(
        NetworkGraph<NetworkNode<V>, NetworkArc<V>> graph,
        IReadOnlyList<GeneralizedFlowNode<V>> nodes,
        IReadOnlyList<GeneralizedFlowArc<V>> arcs,
        V zero,
        PhysicalUnit? costUnit = null) {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(arcs);
        Graph = graph;
        Nodes = nodes.ToList();
        Arcs = arcs.ToList();
        Zero = zero;
        CostUnit = costUnit ?? NoneUnit.Instance;
    }

    /// <summary>通用有向网络图 / Generic directed network graph.</summary>
    public NetworkGraph<NetworkNode<V>, NetworkArc<V>> Graph { get; }

    /// <summary>节点单位与硬供需 / Node units and hard balances.</summary>
    public IReadOnlyList<GeneralizedFlowNode<V>> Nodes { get; }

    /// <summary>单输入服务弧 / Single-input service arcs.</summary>
    public IReadOnlyList<GeneralizedFlowArc<V>> Arcs { get; }

    /// <summary>领域数值零 / Domain numeric zero.</summary>
    public V Zero { get; }

    /// <summary>总费用单位 / Total-cost unit.</summary>
    public PhysicalUnit CostUnit { get; }
}
