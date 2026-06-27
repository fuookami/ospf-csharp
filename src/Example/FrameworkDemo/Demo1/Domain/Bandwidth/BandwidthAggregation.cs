#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Bandwidth;

/// <summary>
/// 聚合边、服务和节点带宽模型，并将其注册到优化模型。Aggregates edge, service, and node bandwidth models and registers them with the optimization model.
/// </summary>
public sealed class BandwidthAggregation {
    /// <summary>边带宽模型 / Edge bandwidth model</summary>
    public EdgeBandwidth EdgeBandwidth { get; }
    /// <summary>服务带宽模型 / Service bandwidth model</summary>
    public ServiceBandwidth ServiceBandwidth { get; }
    /// <summary>节点带宽模型 / Node bandwidth model</summary>
    public NodeBandwidth NodeBandwidth { get; }

    public BandwidthAggregation(EdgeBandwidth edgeBandwidth, ServiceBandwidth serviceBandwidth, NodeBandwidth nodeBandwidth) {
        EdgeBandwidth = edgeBandwidth;
        ServiceBandwidth = serviceBandwidth;
        NodeBandwidth = nodeBandwidth;
    }

    /// <summary>
    /// 注册带宽聚合到模型。Register bandwidth aggregation to the model.
    /// </summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        Try r1 = EdgeBandwidth.Register(model);
        if (r1.IsFailed) {
            return r1;
        }

        Try r2 = ServiceBandwidth.Register(model);
        if (r2.IsFailed) {
            return r2;
        }

        Try r3 = NodeBandwidth.Register(model);
        if (r3.IsFailed) {
            return r3;
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
