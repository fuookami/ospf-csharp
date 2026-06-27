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
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Pipelines.Bandwidth;

/// <summary>
/// 确保每个客户端节点至少接收其所需的带宽需求。Ensures each client node receives at least its required bandwidth demand.
/// </summary>
public sealed class DemandConstraintPipeline : IPipeline<LinearMetaModel<Flt64>> {
    private readonly IReadOnlyList<Node> _nodes;
    private readonly NodeBandwidth _nodeBandwidth;

    public DemandConstraintPipeline(IReadOnlyList<Node> nodes, NodeBandwidth nodeBandwidth) {
        _nodes = nodes;
        _nodeBandwidth = nodeBandwidth;
    }

    string IMetaConstraintGroup.Name => "demand_constraint";

    public Try Invoke(LinearMetaModel<Flt64> model) {
        foreach (Node node in _nodes.Where(n => n is ClientNode)) {
            ClientNode clientNode = (ClientNode)node;
            LinearExpressionSymbol inDegreeSym = _nodeBandwidth.InDegree[node.Index];
            Flt64 demand = clientNode.Demand.ToFlt64();
            model.AddConstraint(
                inDegreeSym.Polynomial.Ge(demand),
                this,
                name: $"demand_constraint_{node}");
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
