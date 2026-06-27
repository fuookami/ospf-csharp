#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Route;

/// <summary>
/// 聚合路由图、服务和分配变量，并将其注册到模型。Aggregates the route graph, services, and assignment variables, and registers them with the model.
/// </summary>
public sealed class RouteAggregation {
    /// <summary>网络图 / Network graph</summary>
    public SspGraph Graph { get; }
    /// <summary>服务列表 / Service list</summary>
    public IReadOnlyList<SspService> Services { get; }
    /// <summary>分配模型 / Assignment model</summary>
    public Assignment Assignment { get; }

    public RouteAggregation(SspGraph graph, IReadOnlyList<SspService> services, Assignment assignment) {
        Graph = graph;
        Services = services;
        Assignment = assignment;
    }

    /// <summary>
    /// 注册路由聚合到模型。Register route aggregation to the model.
    /// </summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        return Assignment.Register(model);
    }
}
