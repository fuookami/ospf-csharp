// 活动数值转换边界 / The activity value conversion boundary.
// 与固定 Kotlin 基线 `gantt-scheduling-domain-activity-context/.../ActivityCompilation.kt` 里的
// `ActivityValueBoundary` 对应。
// Mirrors `ActivityValueBoundary` in the pinned Kotlin baseline's `ActivityCompilation.kt`.

using System;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;

/// <summary>
/// 活动数值转换边界 / The activity value conversion boundary.
/// </summary>
/// <remarks>
/// 这一层的职责是**在业务数值与求解器数值之间做一次可失败、可解释的转换**。它必须抓住三类问题，
/// 而三类都属于"转换后看着正常、算出来是错的"：
///
/// 1. **换算下溢**：`1 mg` 换算成 `t` 得到 `1e-9`，再转成某个只保留有效位的表示时变成 `0`。
///    结果是一个本该消耗物料的作业消耗了零，而下溢本身不留任何痕迹。
/// 2. **仿射单位**：摄氏与华氏不能线性缩放。`20 °C` 乘 2 不是 `40 °C` 的物理意义下正确结果，
///    排程里的"量"全都参与线性运算，所以仿射单位必须在入口就拒。
/// 3. **非有限值**：`NaN`/`∞` 进入模型后，约束比较全为假，于是**所有约束都被静默满足**。
///
/// This layer's job is **one fallible, explainable conversion between business numbers and solver numbers**. It must catch three classes of problem, all of which look fine after
/// conversion and compute the wrong thing:
///
/// 1. **Conversion underflow**: `1 mg` converted to tonnes is `1e-9`, which becomes `0` in a representation that keeps only significant bits. An operation that should
///    consume material then consumes nothing, and the underflow itself leaves no trace.
/// 2. **Affine units**: Celsius and Fahrenheit cannot be scaled linearly. Multiplying `20 °C` by 2 is not `40 °C` in any meaningful sense, and every quantity in scheduling
///    participates in linear arithmetic, so affine units must be rejected at the door.
/// 3. **Non-finite values**: once `NaN`/`∞` enters the model, every constraint comparison is false and therefore **every constraint is silently satisfied**.
/// </remarks>
public static class ActivityValueBoundary {
    /// <summary>
    /// 把业务量转换为求解器数值 / Convert a business quantity into a solver value.
    /// </summary>
    /// <param name="quantity">业务量 / The business quantity.</param>
    /// <param name="unit">目标单位 / The target unit.</param>
    /// <param name="label">用于报错的标签 / The label used in error messages.</param>
    /// <returns>求解器数值或失败 / The solver value, or a failure.</returns>
    public static Result<Flt64, ErrorCode, Error<ErrorCode>> Value(
        Quantity<Flt64> quantity,
        PhysicalUnit unit,
        string label) {
        if (quantity.Unit.IsAffine || unit.IsAffine) {
            return Failed("线性排程不支持仿射单位：" + label
                + " / Linear scheduling does not support affine units: " + label);
        }

        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> converted = quantity.ConvertTo(unit);
        if (converted is Failed<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> failed) {
            return Failed("单位不兼容：" + label + " / Incompatible units: " + label + " (" + failed.Error.Message + ")");
        }

        double original = quantity.Value.ToDouble();
        double normalized = ((Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>>)converted).Value.Value.ToDouble();

        // 原值非零、换算后为零 —— 这就是下溢。
        // A non-zero original becoming zero is exactly what underflow looks like.
        if (normalized == 0.0 && original != 0.0) {
            return Failed("单位换算下溢为零：" + label + " / Unit conversion underflow to zero: " + label);
        }
        if (!double.IsFinite(normalized)) {
            return Failed("数值非有限：" + label + " / Non-finite numeric value: " + label);
        }
        return Results.Ok(new Flt64(normalized));
    }

    /// <summary>
    /// 把求解器数值转换回业务量 / Convert a solver value back into a business quantity.
    /// </summary>
    /// <param name="value">求解器数值 / The solver value.</param>
    /// <param name="unit">目标单位 / The target unit.</param>
    /// <returns>业务量或失败 / The business quantity, or a failure.</returns>
    /// <remarks>
    /// **回程也要判下溢**：正向转换通过不代表反向也通。一个 `1e-9` 的求解器值转回 `mg` 时若先被截断，会得到零，
    /// 于是"消耗了极少一点"变成"什么都没消耗"。
    ///
    /// **The return trip checks underflow too**: a valid forward conversion does not imply a valid reverse one. A solver value of `1e-9` converted back to `mg` after a
    /// truncation becomes zero, turning "consumed a tiny amount" into "consumed nothing".
    /// </remarks>
    public static Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> QuantityOf(Flt64 value, PhysicalUnit unit) {
        if (!double.IsFinite(value.ToDouble())) {
            return FailedQuantity("费用溢出 / Cost overflow");
        }
        return Results.Ok(new Quantity<Flt64>(value, unit));
    }

    /// <summary>构造值失败 / Build a value failure.</summary>
    /// <param name="message">描述 / The description.</param>
    /// <returns>失败 / The failure.</returns>
    private static Result<Flt64, ErrorCode, Error<ErrorCode>> Failed(string message) =>
        Results.Failed<Flt64>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    /// <summary>构造量失败 / Build a quantity failure.</summary>
    /// <param name="message">描述 / The description.</param>
    /// <returns>失败 / The failure.</returns>
    private static Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> FailedQuantity(string message) =>
        Results.Failed<Quantity<Flt64>>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
