// 求解器数值适配 / Solver value adapter.
// 与固定 Kotlin 对应。
//
// `Bpp2dSolverAmount` 已在 earlier 随 `LayerAssignmentMath` 落地，这里补齐其余三项并把四个函数归到同一个静态类，
// 便于调用方按心智模型找到它们。
// `Bpp2dSolverAmount` landed with `LayerAssignmentMath` in earlier; the remaining three and groups all
// four in one static class so callers find them the way its mental model expects.

using System;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 二维装箱的求解器数值适配 / Solver value adaptation for 2D packing.
/// </summary>
public static class SolverValueAdapter {
    /// <summary>
    /// 把无符号整数转换为 <see cref="FltX"/> / Convert an unsigned integer to <see cref="FltX"/>.
    /// </summary>
    /// <param name="value">无符号整数 / Unsigned integer.</param>
    /// <returns>FltX 数值 / FltX value.</returns>
    public static FltX Bpp2dSolverAmount(ulong value) => new((decimal)value);

    /// <summary>
    /// 把 <see cref="FltX"/> 量值取出为求解器数值 / Extract an <see cref="FltX"/> quantity as a solver number.
    /// </summary>
    /// <remarks>
    /// 只取数值、**丢弃单位**。这是刻意的：求解器模型本身无量纲概念，量纲由调用方在建模前保证一致；若在这里做换算，
    /// 每个调用点都会各自猜一个目标单位，不一致的结果比没有换算更危险。
    /// Takes the value and **drops the unit**. That is deliberate: the solver model has no notion of dimension and the caller
    /// guarantees dimensional consistency before modelling; converting here would make every call site guess a target unit,
    /// and inconsistent guesses are more dangerous than no conversion.
    /// </remarks>
    /// <param name="value">FltX 量值 / FltX quantity.</param>
    /// <returns>求解器数值 / Solver number.</returns>
    public static FltX Bpp2dSolverQuantity(Quantity<FltX> value) {
        ArgumentNullException.ThrowIfNull(value);
        return value.Value;
    }

    /// <summary>
    /// 计算两个 <see cref="FltX"/> 量值的比值，分母非正时返回零 / Compute the ratio, zero when the denominator is
    /// non-positive.
    /// </summary>
    /// <remarks>
    /// 用 `&lt;=` 而不是 `==` 判断分母：负的分母同样会让比值失去意义，而"零或负"这一个条件就能同时挡住除零与符号翻转。
    /// Uses `&lt;=` rather than `==`: a negative denominator is equally meaningless, and one "zero or negative" test stops
    /// both division by zero and a sign flip.
    /// </remarks>
    /// <param name="numerator">分子量值 / Numerator quantity.</param>
    /// <param name="denominator">分母量值 / Denominator quantity.</param>
    /// <returns>比值 / The ratio.</returns>
    public static FltX Bpp2dSolverRatio(Quantity<FltX> numerator, Quantity<FltX> denominator) {
        ArgumentNullException.ThrowIfNull(numerator);
        ArgumentNullException.ThrowIfNull(denominator);

        if (denominator.Value.CompareTo(new FltX(0)) <= 0) {
            return new FltX(0);
        }
        return numerator.Value.Div(denominator.Value);
    }

    /// <summary>
    /// 把 <see cref="FltX"/> 量值按标量缩放 / Scale an <see cref="FltX"/> quantity by a scalar.
    /// </summary>
    /// <param name="value">FltX 量值 / FltX quantity.</param>
    /// <param name="scaler">缩放标量 / Scaling scalar.</param>
    /// <returns>缩放后的量值 / The scaled quantity.</returns>
    public static Quantity<FltX> Bpp2dSolverScale(Quantity<FltX> value, FltX scaler) {
        ArgumentNullException.ThrowIfNull(value);
        return new Quantity<FltX>(value.Value.Times(scaler), value.Unit);
    }
}
