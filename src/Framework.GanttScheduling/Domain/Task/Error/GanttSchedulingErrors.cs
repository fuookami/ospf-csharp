#nullable enable

using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Error;
/// <summary>
/// Gantt Scheduling 能力不支持错误 / Gantt Scheduling capability not supported error.
/// </summary>
/// <param name="Capability">不支持的能力 / The unsupported capability.</param>
public sealed record GanttSchedulingCapabilityError(string? Capability = null)
    : Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        Capability is not null ? $"Capability not supported: {Capability}" : "Capability not supported.") {
    /// <summary>创建能力不支持错误 / Create capability not supported error.</summary>
    public static GanttSchedulingCapabilityError Of(string capability) => new(capability);

    /// <summary>创建通用能力不支持错误 / Create generic capability not supported error.</summary>
    public static GanttSchedulingCapabilityError Unsupported() => new();
}

/// <summary>
/// Gantt Scheduling 生命周期错误 / Gantt Scheduling lifecycle error.
/// </summary>
/// <param name="Detail">错误详情 / Error detail.</param>
public sealed record GanttSchedulingLifecycleError(string? Detail = null)
    : Err<ErrorCode>(
        ErrorCode.ApplicationError,
        Detail ?? "Gantt Scheduling lifecycle error.");

/// <summary>
/// Gantt Scheduling 求解错误 / Gantt Scheduling solving error.
/// </summary>
/// <param name="Detail">错误详情 / Error detail.</param>
public sealed record GanttSchedulingSolvingError(string? Detail = null)
    : Err<ErrorCode>(
        ErrorCode.ApplicationFailed,
        Detail ?? "Gantt Scheduling solving error.");

/// <summary>
/// Gantt Scheduling 参数验证错误 / Gantt Scheduling parameter validation error.
/// </summary>
/// <param name="Detail">错误详情 / Error detail.</param>
public sealed record GanttSchedulingValidationError(string? Detail = null)
    : Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        Detail ?? "Gantt Scheduling validation error.");
