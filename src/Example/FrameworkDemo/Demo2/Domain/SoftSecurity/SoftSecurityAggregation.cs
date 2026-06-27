#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity;

/// <summary>
/// 软性安全聚合根。Aggregates soft security constraints.
/// </summary>
public sealed class SoftSecurityAggregation
{
    // TODO: Port from Kotlin soft_security Aggregation
    // Properties: aircraftModel, mainDeck, items, positions, stowage, load, ballast, divideEmptyLoading

    /// <summary>
    /// 注册软性安全约束到模型。Register soft security constraints with the model.
    /// </summary>
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Register(
        Infrastructure.StowageMode stowageMode, dynamic model)
    {
        // TODO: Port from Kotlin soft_security Aggregation.register
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
