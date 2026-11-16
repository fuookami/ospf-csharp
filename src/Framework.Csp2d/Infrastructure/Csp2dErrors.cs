// 二维套裁错误类型 / 2D cutting stock error types.

using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Framework.Csp2d.Infrastructure;

/// <summary>
/// 二维套裁校验错误：输入数据、配置、几何或切分树不合法 / 2D cutting stock validation error: invalid input data, configuration, geometry or cut tree.
/// </summary>
/// <param name="Detail">错误详情，为空时使用默认消息 / The error detail, defaulting when null.</param>
/// <remarks>
/// **用语义化子类型替代裸 `ErrorCode + message`**，与 BPP2D/BPP3D 一致：调用方可以按类型分支，
/// 而不必比对一个字符串。**比对字符串在文案改动时会静默失效。**
///
/// **Semantic subtypes replace bare `ErrorCode + message`**, consistent with BPP2D/BPP3D: callers can branch on the type instead of comparing a string. **String
/// comparison silently breaks when the wording changes.**
/// </remarks>
public sealed record Csp2dValidationError(string? Detail = null)
    : Err<ErrorCode>(ErrorCode.IllegalArgument, Detail ?? "CSP2D validation error.");

/// <summary>二维套裁求解错误：模型注册、求解或结果提取失败 / 2D cutting stock solving error: registration, solving or extraction failed.</summary>
/// <param name="Detail">错误详情，为空时使用默认消息 / The error detail, defaulting when null.</param>
public sealed record Csp2dSolvingError(string? Detail = null)
    : Err<ErrorCode>(ErrorCode.ApplicationFailed, Detail ?? "CSP2D solving error.");

/// <summary>二维套裁能力不支持错误：请求了当前路径未覆盖的工艺、模式或形状 / 2D cutting stock capability error: an unsupported process, mode or shape was requested.</summary>
/// <param name="Capability">不支持的能力 / The unsupported capability.</param>
public sealed record Csp2dCapabilityError(string? Capability = null)
    : Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        Capability is null ? "Capability not supported." : "Capability not supported: " + Capability);

/// <summary>二维套裁内部错误：不应到达的分支或状态 / 2D cutting stock internal error: an unreachable branch or state.</summary>
/// <param name="Detail">错误详情，为空时使用默认消息 / The error detail, defaulting when null.</param>
public sealed record Csp2dInternalError(string? Detail = null)
    : Err<ErrorCode>(ErrorCode.ApplicationError, Detail ?? "CSP2D internal error.");
