#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
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
/// 约束每个普通节点的流出到服务容量（当服务被分配时）。Constrains the out-flow at each normal node to the service capacity when the service is assigned.
/// </summary>
public sealed class ServiceCapacityConstraintPipeline : IPipeline<LinearMetaModel<Flt64>> {
    private readonly IReadOnlyList<Node> _nodes;
    private readonly IReadOnlyList<SspService> _services;
    private readonly Assignment _assignment;
    private readonly ServiceBandwidth _serviceBandwidth;

    public ServiceCapacityConstraintPipeline(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<SspService> services,
        Assignment assignment,
        ServiceBandwidth serviceBandwidth) {
        _nodes = nodes;
        _services = services;
        _assignment = assignment;
        _serviceBandwidth = serviceBandwidth;
    }

    string IMetaConstraintGroup.Name => "service_capacity_constraint";

    public Try Invoke(LinearMetaModel<Flt64> model) {
        BinVariable2 x = _assignment.X;
        SymbolCombination<LinearExpressionSymbol, Shape2> outFlow = _serviceBandwidth.OutFlow;

        foreach (Node node in _nodes.Where(n => n is NormalNode)) {
            foreach (SspService service in _services) {
                // service.capacity * (1 - x[node, service]) + outFlow[node, service] <= service.capacity
                Flt64 capacity = service.Capacity.ToFlt64();
                LinearExpressionSymbol outFlowSym = outFlow[new int[] { node.Index, service.Index }];
                LinearPolynomial<Flt64> outFlowPoly = outFlowSym.Polynomial;

                // Build polynomial: constant = capacity + outFlowPoly.Constant
                Flt64 constant = capacity + outFlowPoly.Constant;
                var monomials = new List<LinearMonomial<Flt64>>();
                // - capacity * x[node, service]
                monomials.Add(new LinearMonomial<Flt64>(-capacity, x[node.Index, service.Index]));
                // + outFlow terms
                foreach (LinearMonomial<Flt64> m in outFlowPoly.Monomials) {
                    monomials.Add(new LinearMonomial<Flt64>(Flt64.One, m.Symbol));
                }

                var poly = new MutableLinearPolynomial<Flt64>(monomials, constant);
                model.AddConstraint(
                    poly.ToLinearPolynomial().Le(capacity),
                    this,
                    name: $"service_capacity_constraint_({node},{service})");
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
