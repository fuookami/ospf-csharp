// 网络调度数值适配 / Network-scheduling numeric adapter.
// 与固定 Kotlin 基线 `NetworkSchedulingSolverValueAdapter.kt`（86 行）对应。
// Mirrors the pinned baseline's `NetworkSchedulingSolverValueAdapter.kt`.
//
// 这一层的职责是**把"领域数值"与"求解器数值"的边界收在一处**：领域侧用泛型 `V` 保持精度，求解器侧固定为 `Flt64`。
// 基线注释明确要求"只在进入 solver 边界时才调用 normalize"——本移植保留这条约束并写进接口文档。
// This layer's job is to **funnel the boundary between domain numbers and solver numbers into one place**: the domain side
// keeps precision via generic `V`, the solver side is fixed at `Flt64`. The baseline explicitly requires calling
// `normalize` **only at the solver boundary**; this port keeps that constraint and documents it on the interface.

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;

/// <summary>
/// 网络调度领域值与 solver <see cref="Flt64"/> 的集中转换边界 / Central conversion boundary between network values and
/// solver <see cref="Flt64"/>.
/// </summary>
/// <typeparam name="V">领域数值类型 / Domain numeric type.</typeparam>
public interface INetworkSchedulingSolverValueAdapter<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 将领域值转换为 solver 值 / Convert a domain value to a solver value.
    /// </summary>
    /// <param name="value">领域值 / Domain value.</param>
    /// <returns>solver 值或转换失败 / The solver value, or a conversion failure.</returns>
    Result<Flt64, ErrorCode, Error<ErrorCode>> ToSolverValue(V value);

    /// <summary>
    /// 将 solver 值转换为领域值 / Convert a solver value to a domain value.
    /// </summary>
    /// <param name="value">solver 值 / Solver value.</param>
    /// <returns>领域值或转换失败 / The domain value, or a conversion failure.</returns>
    Result<V, ErrorCode, Error<ErrorCode>> FromSolverValue(Flt64 value);

    /// <summary>
    /// 将物理量归一化到目标单位并保留领域数值类型 / Normalize a quantity to the target unit, preserving the domain type.
    /// </summary>
    /// <remarks>
    /// 该入口用于 ESPPRC 等领域计算；**只有进入 solver 边界时才应调用** <see cref="Normalize"/>。
    /// This entry point is for domain calculations such as ESPPRC; call <see cref="Normalize"/> **only at the solver boundary**.
    /// </remarks>
    /// <param name="quantity">原始物理量 / Source quantity.</param>
    /// <param name="targetUnit">目标单位 / Target unit.</param>
    /// <returns>保留 V 的归一化数值或转换失败 / The normalized value, or a conversion failure.</returns>
    Result<V, ErrorCode, Error<ErrorCode>> NormalizeValue(Quantity<V> quantity, PhysicalUnit targetUnit) {
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(targetUnit);

        if (!quantity.Unit.SameDimension(targetUnit)) {
            return NetworkSchedulingErrors.Failure<V>(
                $"物理量转换失败：{quantity.Unit.Symbol} 与 {targetUnit.Symbol} 量纲不兼容 / Quantity conversion failed: "
                + $"{quantity.Unit.Symbol} and {targetUnit.Symbol} have incompatible dimensions");
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> converted = quantity.ConvertTo(targetUnit);
        if (!converted.IsOk) {
            return NetworkSchedulingErrors.Failure<V>(
                $"物理量转换失败：无法转换到 {targetUnit.Symbol} / Quantity conversion failed: cannot convert to "
                + $"{targetUnit.Symbol}");
        }
        return Results.Ok(converted.Value.Value);
    }

    /// <summary>
    /// 将物理量归一化到目标单位并转换为 solver 值 / Normalize a quantity to the target unit and convert to a solver value.
    /// </summary>
    /// <param name="quantity">原始物理量 / Source quantity.</param>
    /// <param name="targetUnit">目标单位 / Target unit.</param>
    /// <returns>归一化 solver 值或转换失败 / The normalized solver value, or a conversion failure.</returns>
    Result<Flt64, ErrorCode, Error<ErrorCode>> Normalize(Quantity<V> quantity, PhysicalUnit targetUnit) {
        Result<V, ErrorCode, Error<ErrorCode>> converted = NormalizeValue(quantity, targetUnit);
        if (converted is Failed<V, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Flt64>(failed.Error);
        }
        return ToSolverValue(converted.Value);
    }

    /// <summary>
    /// 校验 solver 值有限 / Validate that a solver value is finite.
    /// </summary>
    /// <remarks>
    /// **必须挡住非有限值**：`NaN`／`±∞` 进入求解器会污染整个目标函数，而结果往往表现为"求解成功但方案荒谬"，比直接报错
    /// 难查得多。因此在边界处一次挡住，而不是在每个使用点各判断一次。
    /// **Non-finite values must be blocked**: `NaN` / `±∞` entering a solver poisons the whole objective, and the result usually
    /// looks like "the solve succeeded but the plan is absurd", far harder to diagnose than a straight failure. Blocking
    /// once at the boundary beats testing at every use site.
    /// </remarks>
    /// <param name="value">solver 值 / Solver value.</param>
    /// <returns>原值或失败 / The value, or a failure.</returns>
    Result<Flt64, ErrorCode, Error<ErrorCode>> Finite(Flt64 value) =>
        double.IsFinite(value.ToDouble())
            ? Results.Ok(value)
            : NetworkSchedulingErrors.Failure<Flt64>(
                "数值转换失败：solver 值必须有限 / Numeric conversion failed: solver value must be finite");
}

/// <summary>
/// <see cref="Flt64"/> solver 数值适配器 / <see cref="Flt64"/> solver value adapter.
/// </summary>
public sealed class Flt64NetworkSchedulingSolverValueAdapter : INetworkSchedulingSolverValueAdapter<Flt64> {
    /// <summary>共享实例 / Shared instance.</summary>
    public static Flt64NetworkSchedulingSolverValueAdapter Instance { get; } =
        new Flt64NetworkSchedulingSolverValueAdapter();

    /// <inheritdoc/>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> ToSolverValue(Flt64 value) =>
        double.IsFinite(value.ToDouble())
            ? Results.Ok(value)
            : NetworkSchedulingErrors.Failure<Flt64>(
                "数值转换失败：solver 值必须有限 / Numeric conversion failed: solver value must be finite");

    /// <inheritdoc/>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> FromSolverValue(Flt64 value) =>
        double.IsFinite(value.ToDouble())
            ? Results.Ok(value)
            : NetworkSchedulingErrors.Failure<Flt64>(
                "数值转换失败：solver 值必须有限 / Numeric conversion failed: solver value must be finite");
}

/// <summary>
/// <see cref="FltX"/> solver 数值适配器 / <see cref="FltX"/> solver value adapter.
/// </summary>
public sealed class FltXNetworkSchedulingSolverValueAdapter : INetworkSchedulingSolverValueAdapter<FltX> {
    /// <summary>共享实例 / Shared instance.</summary>
    public static FltXNetworkSchedulingSolverValueAdapter Instance { get; } =
        new FltXNetworkSchedulingSolverValueAdapter();

    /// <inheritdoc/>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> ToSolverValue(FltX value) =>
        double.IsFinite(value.ToFlt64().ToDouble())
            ? Results.Ok(value.ToFlt64())
            : NetworkSchedulingErrors.Failure<Flt64>(
                "数值转换失败：solver 值必须有限 / Numeric conversion failed: solver value must be finite");

    /// <inheritdoc/>
    public Result<FltX, ErrorCode, Error<ErrorCode>> FromSolverValue(Flt64 value) =>
        double.IsFinite(value.ToDouble())
            ? Results.Ok(value.ToFltX())
            : NetworkSchedulingErrors.Failure<FltX>(
                "数值转换失败：solver 值必须有限 / Numeric conversion failed: solver value must be finite");
}
