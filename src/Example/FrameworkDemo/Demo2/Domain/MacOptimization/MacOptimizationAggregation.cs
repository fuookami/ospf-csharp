#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization;

/// <summary>
/// MAC 优化聚合根。Aggregates MAC optimization models for longitudinal and lateral balance.
/// </summary>
public sealed class MacOptimizationAggregation
{
    // TODO: Port from Kotlin mac_optimization Aggregation
    // Properties: aircraftModel, macRange, longitudinalBalance, lateralBalance, horizontalStabilizers

    /// <summary>
    /// 注册 MAC 优化约束到模型。Register MAC optimization constraints with the model.
    /// </summary>
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Register(
        Infrastructure.StowageMode stowageMode, dynamic model)
    {
        // TODO: Port from Kotlin mac_optimization Aggregation.register
        return Utils.Functional.Results.Ok<Utils.Functional.Success>(Utils.Functional.Results.SuccessInstance);
    }

    /// <summary>
    /// 注册 Benders 主问题约束。Register constraints for Benders master problem.
    /// </summary>
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> RegisterForBendersMP(dynamic model)
    {
        return Register(Infrastructure.StowageMode.FullLoad, model);
    }

    /// <summary>
    /// 注册 Benders 子问题约束。Register constraints for Benders sub problem.
    /// </summary>
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> RegisterForBendersSP(
        dynamic model, IReadOnlyList<double> solution)
    {
        return Utils.Functional.Results.Ok<Utils.Functional.Success>(Utils.Functional.Results.SuccessInstance);
    }
}
