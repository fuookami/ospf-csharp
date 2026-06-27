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
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Pipelines.Route;

/// <summary>
/// 约束每个服务最多分配一个节点。Constrains each service to be assigned to at most one node.
/// </summary>
public sealed class ServiceAssignmentConstraintPipeline : IPipeline<LinearMetaModel<Flt64>> {
    private readonly IReadOnlyList<SspService> _services;
    private readonly Assignment _assignment;

    public ServiceAssignmentConstraintPipeline(IReadOnlyList<SspService> services, Assignment assignment) {
        _services = services;
        _assignment = assignment;
    }

    string IMetaConstraintGroup.Name => "service_assignment";

    public Try Invoke(LinearMetaModel<Flt64> model) {
        foreach (SspService service in _services) {
            LinearExpressionSymbol svcAssignSym = _assignment.ServiceAssignment[service.Index];
            model.AddConstraint(
                svcAssignSym.Polynomial.Le(Flt64.One),
                this,
                name: $"service_assignment_{service}");
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
