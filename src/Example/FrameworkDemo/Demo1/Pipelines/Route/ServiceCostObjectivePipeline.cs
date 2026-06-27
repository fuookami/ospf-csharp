#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Route;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Pipelines.Route;

/// <summary>
/// 通过求和按分配变量加权的每服务成本来最小化总服务成本。Minimizes the total service cost by summing per-service cost weighted by assignment variables.
/// </summary>
public sealed class ServiceCostObjectivePipeline : IPipeline<LinearMetaModel<Flt64>> {
    private readonly IReadOnlyList<SspService> _services;
    private readonly Assignment _assignment;

    public ServiceCostObjectivePipeline(IReadOnlyList<SspService> services, Assignment assignment) {
        _services = services;
        _assignment = assignment;
    }

    string IMetaConstraintGroup.Name => "service_cost";

    public Try Invoke(LinearMetaModel<Flt64> model) {
        // minimize sum_s (service.cost * serviceAssignment[s])
        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (SspService service in _services) {
            LinearExpressionSymbol svcAssignSym = _assignment.ServiceAssignment[service.Index];
            LinearPolynomial<Flt64> svcPoly = svcAssignSym.Polynomial;
            Flt64 cost = service.Cost.ToFlt64();
            foreach (LinearMonomial<Flt64> m in svcPoly.Monomials) {
                monomials.Add(new LinearMonomial<Flt64>(cost * m.Coefficient, m.Symbol));
            }
            constant += cost * svcPoly.Constant;
        }
        var poly = new MutableLinearPolynomial<Flt64>(monomials, constant);
        model.AddObject(
            ObjectCategory.Minimum,
            poly.ToLinearPolynomial(),
            "service_cost",
            "service cost");
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
