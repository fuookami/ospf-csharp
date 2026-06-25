#nullable enable

using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Error;
/// <summary>
/// CSP1D 生命周期错误 / CSP1D lifecycle error
///
/// 用于 CSP1D 应用生命周期相关的错误（注册、恢复、上下文等）。
/// Used for errors related to the CSP1D application lifecycle (registration, recovery, context, etc.).
/// </summary>
/// <param name="Detail">错误详情 / Error detail.</param>
public sealed record Csp1dLifecycleError(string? Detail = null)
    : Err<ErrorCode>(ErrorCode.ApplicationError, Detail ?? "CSP1D lifecycle error.");

/// <summary>
/// CSP1D 类型错误 / CSP1D type error
///
/// 用于 CSP1D 领域中不支持的类型错误。
/// Used for unsupported type errors in the CSP1D domain.
/// </summary>
/// <param name="Type">不支持的类型 / The unsupported type.</param>
public sealed record Csp1dTypeError(string? Type = null)
    : Err<ErrorCode>(ErrorCode.IllegalArgument, Type != null ? $"Unsupported type: {Type}" : "Unsupported type.");

/// <summary>
/// CSP1D 求解错误 / CSP1D solving error
///
/// 用于 CSP1D 领域中求解相关的错误。
/// Used for solving-related errors in the CSP1D domain.
/// </summary>
/// <param name="Detail">错误详情 / Error detail.</param>
public sealed record Csp1dSolvingError(string? Detail = null)
    : Err<ErrorCode>(ErrorCode.ApplicationFailed, Detail ?? "CSP1D solving error.");

/// <summary>
/// CSP1D 能力不支持错误 / CSP1D capability not supported error
///
/// 用于 CSP1D 领域中不支持的能力错误。
/// Used for capability-not-supported errors in the CSP1D domain.
/// </summary>
/// <param name="Capability">不支持的能力 / The unsupported capability.</param>
public sealed record Csp1dCapabilityError(string? Capability = null)
    : Err<ErrorCode>(ErrorCode.IllegalArgument, Capability != null ? $"Capability not supported: {Capability}" : "Capability not supported.");
