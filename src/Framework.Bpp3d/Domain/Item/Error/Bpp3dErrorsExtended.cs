#nullable enable

using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Error;

/// <summary>
/// BPP3D 能力错误 / BPP3D capability error.
/// 不支持的功能。
/// </summary>
public sealed record Bpp3dCapabilityError(string? Capability = null, string? Message = null)
    : Err<ErrorCode>(ErrorCode.IllegalArgument, Message ?? $"Capability not supported: {Capability}");

/// <summary>
/// BPP3D 求解错误 / BPP3D solving error.
/// 求解器相关错误。
/// </summary>
public sealed record Bpp3dSolvingError(string? Detail = null, string? Message = null)
    : Err<ErrorCode>(ErrorCode.ApplicationFailed, Message ?? $"Solving error: {Detail}");

/// <summary>
/// BPP3D 内部错误 / BPP3D internal error.
/// 内部逻辑错误。
/// </summary>
public sealed record Bpp3dInternalError(string? Detail = null, string? Message = null)
    : Err<ErrorCode>(ErrorCode.ApplicationError, Message ?? $"Internal error: {Detail}");

/// <summary>
/// BPP3D 验证错误 / BPP3D validation error.
/// 参数验证错误。
/// </summary>
public sealed record Bpp3dValidationError(string? Detail = null, string? Message = null)
    : Err<ErrorCode>(ErrorCode.IllegalArgument, Message ?? $"Validation error: {Detail}");
