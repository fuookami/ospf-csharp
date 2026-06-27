#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Bandwidth;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Route;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Pipelines.Bandwidth;

/// <summary>
/// 通过求和边容量计算节点的最大出带宽容量。Computes the maximum outgoing bandwidth capacity of a node by summing its edge capacities.
/// </summary>
internal static class NodeBandwidthExtensions {
    /// <summary>
    /// 计算节点的最大出带宽容量。Compute the maximum outgoing bandwidth capacity of a node.
    /// </summary>
    /// <param name="node">网络节点 / Network node</param>
    /// <returns>最大出带宽容量 / Maximum outgoing bandwidth capacity</returns>
    public static UInt64 MaxOutDegree(this Node node) {
        UInt64 bandwidth = UInt64.Zero;
        foreach (Edge edge in node.Edges) {
            bandwidth += edge.MaxBandwidth;
        }
        return bandwidth;
    }
}

/// <summary>
/// 约束总节点流出到节点容量（当节点被分配时）。Constrains total node out-flow to the node's capacity when the node is assigned.
/// </summary>
public sealed class TransferNodeBandwidthConstraintPipeline : IPipeline<LinearMetaModel<Flt64>> {
    private readonly IReadOnlyList<Node> _nodes;
    private readonly Assignment _assignment;
    private readonly NodeBandwidth _nodeBandwidth;

    public TransferNodeBandwidthConstraintPipeline(
        IReadOnlyList<Node> nodes,
        Assignment assignment,
        NodeBandwidth nodeBandwidth) {
        _nodes = nodes;
        _assignment = assignment;
        _nodeBandwidth = nodeBandwidth;
    }

    string IMetaConstraintGroup.Name => "transfer_node_bandwidth_constraint";

    public Try Invoke(LinearMetaModel<Flt64> model) {
        SymbolCombination<LinearExpressionSymbol, Shape1> nodeAssign = _assignment.NodeAssignment;
        SymbolCombination<LinearExpressionSymbol, Shape1> outFlow = _nodeBandwidth.OutFlow;

        foreach (Node node in _nodes.Where(n => n is NormalNode)) {
            // node.maxOutDegree() * (1 - nodeAssignment[node]) + outFlow[node] <= node.maxOutDegree()
            Flt64 maxOut = node.MaxOutDegree().ToFlt64();
            LinearExpressionSymbol nodeAssignSym = nodeAssign[node.Index];
            LinearPolynomial<Flt64> nodeAssignPoly = nodeAssignSym.Polynomial;
            LinearExpressionSymbol outFlowSym = outFlow[node.Index];
            LinearPolynomial<Flt64> outFlowPoly = outFlowSym.Polynomial;

            // Build polynomial: constant = maxOut - maxOut * nodeAssignPoly.Constant + outFlowPoly.Constant
            Flt64 constant = maxOut - maxOut * nodeAssignPoly.Constant + outFlowPoly.Constant;
            var monomials = new List<LinearMonomial<Flt64>>();
            // - maxOut * nodeAssignment[node] terms
            foreach (LinearMonomial<Flt64> m in nodeAssignPoly.Monomials) {
                monomials.Add(new LinearMonomial<Flt64>(-maxOut * m.Coefficient, m.Symbol));
            }
            // + outFlow terms
            foreach (LinearMonomial<Flt64> m in outFlowPoly.Monomials) {
                monomials.Add(new LinearMonomial<Flt64>(Flt64.One, m.Symbol));
            }

            var poly = new MutableLinearPolynomial<Flt64>(monomials, constant);
            model.AddConstraint(
                poly.ToLinearPolynomial().Le(maxOut),
                this,
                name: $"transfer_node_bandwidth_constraint_{node}");
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
