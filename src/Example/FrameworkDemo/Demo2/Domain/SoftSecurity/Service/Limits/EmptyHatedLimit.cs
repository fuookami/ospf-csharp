#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Service.Limits;

/// <summary>
/// 空位厌恶限制。Empty hated limit for soft security.
/// </summary>
internal sealed class EmptyHatedLimit
{
    /// <summary>
    /// 执行限制。Invoke the limit.
    /// </summary>
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Invoke(dynamic model)
    {
        // TODO: Port from Kotlin EmptyHatedLimit
        return Utils.Functional.Results.Ok<Utils.Functional.Success>(Utils.Functional.Results.SuccessInstance);
    }
}
