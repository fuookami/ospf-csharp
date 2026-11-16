// 既有模型到新模型的桥接 / Bridge from the legacy models to the current ones.
// 与固定 Kotlin 的 `toSheetType` / `toBox2Geometry` 对应。
// .
//
// `toActualItem` / `toLayerPlacement` / `toLayer` / `toLegacyScene` 需要 `ActualItem` 的完整形状体系，
// 见文末说明。The remaining conversions need the full `ActualItem` shape system — see the tail note.

using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Application;

/// <summary>
/// 既有模型到新模型的桥接 / Bridge from the legacy models to the current ones.
/// </summary>
/// <remarks>
/// **这一层的每个转换都返回 `Result`，不抛异常**。理由不是风格：C# 的 `QuantityArithmetic.Minus` 在量纲不匹配时**抛**
/// `ArgumentException`，而量纲不匹配在这里是**输入数据的问题**，不是编码错误——它应该作为失败返回给调用方去报告，
/// 而不是在铸造边界上炸开。
///
/// **Every conversion here returns `Result` instead of throwing.** The reason is not stylistic: C#'s `QuantityArithmetic.Minus` **throws** `ArgumentException` on a
/// dimension mismatch, yet a dimension mismatch here is **an input-data problem**, not a coding error — it should be returned as a failure for the caller to report, not
/// detonate at the casting boundary.
/// </remarks>
public static class LegacyBridge {
    /// <summary>
    /// 把既有板材转换为板材类型 / Convert a legacy sheet into a sheet type.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="sheet">既有板材 / The legacy sheet.</param>
    /// <param name="typeCode">类型编码，缺省用板材 id / The type code, defaulting to the sheet id.</param>
    /// <param name="maxLoad">最大承重，缺省为零 / The maximum load, defaulting to zero.</param>
    /// <returns>板材类型或失败 / The sheet type or a failure.</returns>
    /// <remarks>
    /// **缺省 `IsMain = true`**：这一层的输入是"已经决定要用的板材"，把一个实际使用的板材标为非主板材会让下游把它当成备选。
    /// 最大承重缺省用零而不是"不限额"——限额是个需要显式声明的约束，静默不限额会让重量校验整体失效。
    ///
    /// **`IsMain = true` by default**: this layer's input is "a sheet already decided upon", and marking a sheet in use as non-main would make downstream treat it as an
    /// alternative. The maximum load defaults to zero rather than "unlimited" — a limit is a constraint that must be stated explicitly, and a silent "unlimited" would
    /// disable weight checking across the board.
    /// </remarks>
    public static Result<SheetType<V>, ErrorCode, Error<ErrorCode>> ToSheetType<V>(
        this Sheet2<V> sheet,
        string? typeCode = null,
        Quantity<V>? maxLoad = null) where V : struct, IFloatingNumber<V> {
        System.ArgumentNullException.ThrowIfNull(sheet);

        if (sheet.Width.Value.CompareTo(default) <= 0 || sheet.Height.Value.CompareTo(default) <= 0) {
            return Failed<SheetType<V>>("板材尺寸必须为正 / A sheet's dimensions must be positive");
        }
        return Results.Ok(new SheetType<V>(
            width: sheet.Width,
            height: sheet.Height,
            maxLoad: maxLoad ?? new Quantity<V>(default, sheet.Width.Unit),
            typeCode: new SheetTypeId(typeCode ?? sheet.Id),
            isMain: true));
    }

    /// <summary>
    /// 把既有盒体需求转换为包围盒 / Convert a legacy box need into a bounding box.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="need">既有盒体需求 / The legacy box need.</param>
    /// <returns>包围盒或失败 / The bounding box or a failure.</returns>
    /// <remarks>
    /// 宽高由 `max - min` 得出，**差值必须先量纲校验再构造包围盒**：直接调会抛异常的 `Minus` 会让一个 `米/千克` 混配的
    /// 需求在桥接处炸开，而正确行为是把它作为"这条输入不合法"返回。
    ///
    /// The width and height come from `max - min`, and **the differences must pass the dimension check before the box is built**: calling the throwing `Minus` directly
    /// would make a metre/kilogram mix detonate at the bridge, when the correct behaviour is to return "this input is invalid".
    /// </remarks>
    public static Result<Box2Geometry<V>, ErrorCode, Error<ErrorCode>> ToBox2Geometry<V>(
        this Box2Need<V> need) where V : struct, IFloatingNumber<V> {
        System.ArgumentNullException.ThrowIfNull(need);

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> width = need.MaxX.Subtract(need.MinX);
        if (width is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> widthFailed) {
            return Results.Failed<Box2Geometry<V>>(widthFailed.Error);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> height = need.MaxY.Subtract(need.MinY);
        if (height is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> heightFailed) {
            return Results.Failed<Box2Geometry<V>>(heightFailed.Error);
        }
        return Results.Ok(Box2Geometry<V>.Of(
            need.MinX,
            need.MinY,
            ((Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>)width).Value,
            ((Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>)height).Value));
    }

    /// <summary>构造失败结果 / Build a failure.</summary>
    /// <param name="message">描述 / The description.</param>
    /// <returns>失败 / The failure.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Failed<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

}
