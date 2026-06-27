#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Bandwidth;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Route;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
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
/// 最小化所有普通边的总带宽成本。Minimizes the total bandwidth cost across all normal edges.
/// </summary>
public sealed class BandwidthCostObjectivePipeline : IPipeline<LinearMetaModel<Flt64>> {
    private readonly IReadOnlyList<Edge> _edges;
    private readonly EdgeBandwidth _edgeBandwidth;

    public BandwidthCostObjectivePipeline(IReadOnlyList<Edge> edges, EdgeBandwidth edgeBandwidth) {
        _edges = edges;
        _edgeBandwidth = edgeBandwidth;
    }

    string IMetaConstraintGroup.Name => "bandwidth_cost";

    public Try Invoke(LinearMetaModel<Flt64> model) {
        // minimize sum_{e from normal} (edge.costPerBandwidth * bandwidth[e])
        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (Edge edge in _edges.Where(e => e.From is NormalNode)) {
            LinearExpressionSymbol bwSym = _edgeBandwidth.Bandwidth[edge.Index];
            Flt64 cost = edge.CostPerBandwidth.ToFlt64();
            foreach (LinearMonomial<Flt64> m in bwSym.Polynomial.Monomials) {
                monomials.Add(new LinearMonomial<Flt64>(cost * m.Coefficient, m.Symbol));
            }
            constant += cost * bwSym.Polynomial.Constant;
        }
        var poly = new MutableLinearPolynomial<Flt64>(monomials, constant);
        model.AddObject(
            ObjectCategory.Minimum,
            poly.ToLinearPolynomial(),
            "bandwidth_cost",
            "bandwidth cost");
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
