// 求解器数值边界 / Solver numeric boundary.
// 与固定 Kotlin 对应。
// The pinned Kotlin: .

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp.Infrastructure;

/// <summary>
/// 求解器数值边界；所有有量纲值先归一到显式单位 / Solver boundary with explicit unit normalization.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
public interface IPackingValueAdapter<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 转为求解器数值 / Convert to solver value.
    /// </summary>
    /// <param name="value">领域值 / Domain value.</param>
    /// <returns>求解器值 / Solver value.</returns>
    Flt64 ToSolver(V value);

    /// <summary>
    /// 转回领域数值 / Convert to domain value.
    /// </summary>
    /// <param name="value">求解器值 / Solver value.</param>
    /// <returns>领域值 / Domain value.</returns>
    V FromSolver(Flt64 value);

    /// <summary>
    /// 归一量值 / Normalize a quantity.
    /// </summary>
    /// <param name="value">量值 / Quantity.</param>
    /// <param name="unit">目标单位 / Target unit.</param>
    /// <returns>有限数值或失败 / Finite value or failure.</returns>
    /// <remarks>
    /// **先换成 `Flt64` 再换算单位**，而不是在领域数值里换算：求解器只认 `Flt64`，
    /// 在 `FltX` 里换算再转过来会多一次舍入。**量纲不兼容返回 `Failed`，不返回零**——
    /// 返回零会让"单位写错"看起来像"系数为零"，是个会静默污染模型的错误。
    ///
    /// **Convert to `Flt64` first, then convert units**, rather than converting in the domain type: the solver only accepts `Flt64`, and converting in `FltX` then
    /// casting adds a rounding step. **An incompatible dimension returns `Failed`, not zero** — zero would make a wrong unit look like a zero coefficient, silently
    /// corrupting the model.
    /// </remarks>
    Result<Flt64, ErrorCode, Error<ErrorCode>> Quantity(Quantity<V> value, PhysicalUnit unit) {
        // **有限性必须在校验换算之前。** 实测：`Inf` 进入 `To(unit)` 会在内部转 `Decimal` 时抛
        // `OverflowException`——**抛异常而不是返回 `Failed`**，调用方拿不到结果类型。
        // 所以先挡非有限值，再做换算。
        //
        // **Finiteness must be checked before the conversion.** Measured: `Inf` entering `To(unit)` throws `OverflowException` while converting to `Decimal` internally —
        // **it throws instead of returning `Failed`**, so callers never get a result type. Non-finite values are therefore rejected first.
        Flt64 solverValue = ToSolver(value.Value);
        if (!double.IsFinite(solverValue.ToDouble())) {
            return Finite(solverValue);
        }
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> converted =
            new Quantity<Flt64>(solverValue, value.Unit).To(unit);
        if (converted is not Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> convertedOk) {
            return Results.Failed<Flt64>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "量纲不兼容 / Incompatible quantity dimensions"));
        }
        return Finite(convertedOk.Value.Value);
    }

    /// <summary>
    /// 验证有限值 / Validate a finite value.
    /// </summary>
    /// <param name="value">待验证值 / Value to validate.</param>
    /// <returns>有限值或失败 / Finite value or failure.</returns>
    static Result<Flt64, ErrorCode, Error<ErrorCode>> Finite(Flt64 value) =>
        double.IsFinite(value.ToDouble())
            ? Results.Ok(value)
            : Results.Failed<Flt64>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "数值必须有限 / Value must be finite"));
}

/// <summary>
/// `Flt64` 适配器 / `Flt64` adapter.
/// </summary>
public sealed class Flt64PackingValueAdapter : IPackingValueAdapter<Flt64> {
    /// <summary>共享实例 / Shared instance.</summary>
    public static readonly Flt64PackingValueAdapter Instance = new();

    /// <inheritdoc/>
    public Flt64 ToSolver(Flt64 value) => value;

    /// <inheritdoc/>
    public Flt64 FromSolver(Flt64 value) => value;
}

/// <summary>
/// `FltX` 适配器 / `FltX` adapter.
/// </summary>
public sealed class FltXPackingValueAdapter : IPackingValueAdapter<FltX> {
    /// <summary>共享实例 / Shared instance.</summary>
    public static readonly FltXPackingValueAdapter Instance = new();

    /// <inheritdoc/>
    public Flt64 ToSolver(FltX value) => value.ToFlt64();

    /// <inheritdoc/>
    public FltX FromSolver(Flt64 value) => new(value.ToDouble());
}
