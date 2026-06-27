#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;

/// <summary>
/// 区域载荷重量限制。Constrains load weight per fuselage zone.
/// </summary>
internal sealed class ZoneLoadWeightLimit
{
    /// <summary>
    /// 执行限制。Invoke the limit.
    /// </summary>
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Invoke(dynamic model)
    {
        // TODO: Port from Kotlin ZoneLoadWeightLimit
        return Utils.Functional.Results.Ok<Utils.Functional.Success>(Utils.Functional.Results.SuccessInstance);
    }
}
