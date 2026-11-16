// 渲染输出 DTO / Render output DTOs.

using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// 渲染输出 / Rendering output.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `demo2/infrastructure/dto/RenderDTO.kt` 的三个记录：**只带渲染需要的字段**，
/// 不把求解过程的内部状态暴露出来。
///
/// Mirrors the three records in the baseline `RenderDTO.kt`: **only the fields rendering needs**, without exposing the solve's internal state.
/// </remarks>
/// <param name="Positions">按装载顺序排列的舱位渲染行 / Position render rows in loading order</param>
public sealed record RenderDTO(IReadOnlyList<RenderPositionDTO> Positions);

/// <summary>
/// 单个舱位的可渲染配载明细 / Renderable stowage details for one position.
/// </summary>
/// <remarks>
/// **两个重量都是可空的，而且可空是有意义的**：`null` 表示该舱位不使用这个模式，
/// 而 `0` 是一个真实的重量值。基线用可空表达这件事，这里照抄——压成 `0` 会让渲染侧分不清"没启用"和"启用但是零"。
///
/// **Both weights are nullable, and the nullability carries meaning**: `null` means the position does not use that mode, while `0` is a real weight. The baseline
/// expresses it with nullability and so does this port — collapsing to `0` would leave the rendering side unable to tell "not enabled" from "enabled but zero".
/// </remarks>
/// <param name="PositionId">舱位稳定标识 / Stable position identifier</param>
/// <param name="PositionName">舱位显示名称 / Position display name</param>
/// <param name="ItemIds">该舱位中的货物标识 / Item identifiers assigned to this position</param>
/// <param name="PredicateLoadWeight">谓词装载重量，不适用时为 <c>null</c> / The predicate load weight, or <c>null</c> when not applicable</param>
/// <param name="RecommendedLoadWeight">推荐装载重量，不适用时为 <c>null</c> / The recommended load weight, or <c>null</c> when not applicable</param>
public sealed record RenderPositionDTO(
    string PositionId,
    string PositionName,
    IReadOnlyList<string> ItemIds,
    RenderWeightDTO? PredicateLoadWeight = null,
    RenderWeightDTO? RecommendedLoadWeight = null);

/// <summary>
/// 用于渲染的重量值 / A weight value for rendering.
/// </summary>
/// <remarks>
/// **单位是字符串而不是枚举**：渲染侧要的是"能直接打出来的符号"，而单位枚举的 `ToString` 跟基线的 `unit.symbol ?: unit.name` 不是一个东西。
/// **`unit` 可空**——单位缺失时宁可留空，也不要编一个默认单位出来。
///
/// **The unit is a string, not an enum**: the rendering side wants a symbol it can print, and an enum's `ToString` is not the baseline's `unit.symbol ?: unit.name`.
/// **`unit` is nullable** — when the unit is missing, leaving it blank beats inventing a default.
/// </remarks>
/// <param name="Value">重量数值 / The weight value</param>
/// <param name="Unit">重量单位符号或名称，不可用时为 <c>null</c> / The weight unit symbol or name, or <c>null</c> when unavailable</param>
public sealed record RenderWeightDTO(double Value, string? Unit);
