#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Pipelines.Route;

/// <summary>
/// 约束每个普通节点最多分配一个服务。Constrains each normal node to be assigned to at most one service.
/// </summary>
public sealed class NodeAssignmentConstraintPipeline : IPipeline<LinearMetaModel<Flt64>> {
    private readonly IReadOnlyList<Node> _nodes;
    private readonly Assignment _assignment;

    public NodeAssignmentConstraintPipeline(IReadOnlyList<Node> nodes, Assignment assignment) {
        _nodes = nodes;
        _assignment = assignment;
    }

    string IMetaConstraintGroup.Name => "node_assignment";

    public Try Invoke(LinearMetaModel<Flt64> model) {
        foreach (Node node in _nodes.Where(n => n is NormalNode)) {
            LinearExpressionSymbol nodeAssignSym = _assignment.NodeAssignment[node.Index];
            model.AddConstraint(
                nodeAssignSym.Polynomial.Le(Flt64.One),
                this,
                name: $"node_assignment_{node}");
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
