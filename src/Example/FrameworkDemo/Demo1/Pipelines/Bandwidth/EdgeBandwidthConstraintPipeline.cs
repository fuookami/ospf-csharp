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
/// 当关联服务未分配到该边时将边带宽限制为零。Limits edge bandwidth to zero when the associated service is not assigned to that edge.
/// </summary>
public sealed class EdgeBandwidthConstraintPipeline : IPipeline<LinearMetaModel<Flt64>> {
    private readonly IReadOnlyList<Edge> _edges;
    private readonly IReadOnlyList<SspService> _services;
    private readonly Assignment _assignment;
    private readonly EdgeBandwidth _edgeBandwidth;

    public EdgeBandwidthConstraintPipeline(
        IReadOnlyList<Edge> edges,
        IReadOnlyList<SspService> services,
        Assignment assignment,
        EdgeBandwidth edgeBandwidth) {
        _edges = edges;
        _services = services;
        _assignment = assignment;
        _edgeBandwidth = edgeBandwidth;
    }

    string IMetaConstraintGroup.Name => "edge_bandwidth_constraint";

    public Try Invoke(LinearMetaModel<Flt64> model) {
        UIntVar2 y = _edgeBandwidth.Y;
        SymbolCombination<LinearExpressionSymbol, Shape1> assignment = _assignment.ServiceAssignment;

        foreach (Edge edge in _edges.Where(e => e.From is NormalNode)) {
            foreach (SspService service in _services) {
                // (1 - assignment[service]) * edge.maxBandwidth + y[edge, service] <= edge.maxBandwidth
                Flt64 maxBw = edge.MaxBandwidth.ToFlt64();
                LinearExpressionSymbol svcAssignSym = assignment[service.Index];
                LinearPolynomial<Flt64> svcAssignPoly = svcAssignSym.Polynomial;

                // Build polynomial: constant = maxBw - maxBw * svcAssignPoly.Constant
                Flt64 constant = maxBw - maxBw * svcAssignPoly.Constant;
                var monomials = new List<LinearMonomial<Flt64>>();
                // - maxBw * assignment[service] terms
                foreach (LinearMonomial<Flt64> m in svcAssignPoly.Monomials) {
                    monomials.Add(new LinearMonomial<Flt64>(-maxBw * m.Coefficient, m.Symbol));
                }
                // + y[edge, service]
                monomials.Add(new LinearMonomial<Flt64>(Flt64.One, y[edge.Index, service.Index]));

                var poly = new MutableLinearPolynomial<Flt64>(monomials, constant);
                model.AddConstraint(
                    poly.ToLinearPolynomial().Le(maxBw),
                    this,
                    name: $"edge_bandwidth_constraint_({edge},{service})");
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
