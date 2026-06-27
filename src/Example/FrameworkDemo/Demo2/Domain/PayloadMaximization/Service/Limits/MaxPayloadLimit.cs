#nullable enable

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.Service.Limits;

/// <summary>
/// 最大业载限制。Maximizes the estimated payload as an objective function with an optional reward coefficient.
/// </summary>
internal sealed class MaxPayloadLimit
{
    /// <summary>
    /// 执行限制。Invoke the limit.
    /// </summary>
    public Utils.Functional.Result<Utils.Functional.Success, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> Invoke(dynamic model)
    {
        // TODO: Port from Kotlin MaxPayloadLimit
        return Utils.Functional.Results.Ok<Utils.Functional.Success>(Utils.Functional.Results.SuccessInstance);
    }
}
