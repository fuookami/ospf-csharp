#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;

/// <summary>
/// 不对称线性密度限制。Constrains unsymmetrical linear density between left and right sides.
/// </summary>
internal sealed class UnsymmetricalLinearDensityLimit
{
    /// <summary>
    /// 执行限制。Invoke the limit.
    /// </summary>
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Invoke(dynamic model)
    {
        // TODO: Port from Kotlin UnsymmetricalLinearDensityLimit
        return Utils.Functional.Results.Ok<Utils.Functional.Success>(Utils.Functional.Results.SuccessInstance);
    }
}
