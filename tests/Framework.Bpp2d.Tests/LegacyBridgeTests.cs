// 既有模型桥接的判据 / Criteria for the legacy bridge.

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Application;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// **量纲不匹配返回失败，不在桥接处抛异常** / A dimension mismatch returns a failure instead of throwing at the bridge.
/// </summary>
/// <remarks>
/// C# 的 `QuantityArithmetic.Minus` 在量纲不匹配时抛 `ArgumentException`。在桥接处这是**输入数据的问题**，
/// 应该作为失败返回给调用方报告，而不是让异常穿过铸造边界。
///
/// C#'s `QuantityArithmetic.Minus` throws `ArgumentException` on a dimension mismatch. At the bridge that is **an input-data problem**, which should be returned as a
/// failure for the caller to report rather than letting an exception cross the casting boundary.
/// </remarks>
public class LegacyBridgeTests {
    /// **正常盒体转换出正确的原点与尺寸** / A well-formed box need converts to the right origin and size.
    [Fact]
    public void ToBox2Geometry_ShouldCarryTheOriginAndTheSize() {
        var need = new Box2Need<FltX>(Metres(2), Metres(3), Metres(12), Metres(13));

        Box2Geometry<FltX> box = RetAssertions.ValueOrFail(need.ToBox2Geometry());

        box.X.Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-9);
        box.Y.Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9);
        box.Width.Value.ToFlt64().ToDouble().Should().BeApproximately(10.0, 1e-9);
        box.Height.Value.ToFlt64().ToDouble().Should().BeApproximately(10.0, 1e-9);
    }

    /// <summary>
    /// **y 轴量纲错时返回失败，不抛异常** / A dimension mismatch on y returns a failure rather than throwing.
    /// </summary>
    /// <remarks>
    /// 这条专门打**第二个分量**：只校验 `x` 的实现会在 `x` 合法时直接继续，于是 `y` 的异常照样抛出。
    /// 判据用 `Assert` 之外的方式确认"没有异常"——`Unwrap` 若抛就说明守卫没生效。
    ///
    /// This targets the **second** component: an implementation validating only `x` would continue once `x` is fine and the `y` exception would still escape. The criterion
    /// checks "no exception" through the returned result, so a throwing guard surfaces as a test failure.
    /// </remarks>
    [Fact]
    public void ToBox2Geometry_ShouldReturnFailureOnAMismatchedHeight() {
        var need = new Box2Need<FltX>(Metres(2), Kilograms(3), Metres(12), Kilograms(13));
        var mixed = new Box2Need<FltX>(Metres(2), Metres(3), Metres(12), Kilograms(13));

        // x 合法（米-米），y 不合法（米-千克）。
        // x is fine (metre minus metre) while y is not (metre minus kilogram).
        Result<Box2Geometry<FltX>, ErrorCode, Error<ErrorCode>> result = mixed.ToBox2Geometry();

        result.Should().BeOfType<Failed<Box2Geometry<FltX>, ErrorCode, Error<ErrorCode>>>(
            "a dimension mismatch must surface as a failure, not an exception");
        ((Failed<Box2Geometry<FltX>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);

        RetAssertions.ValueOrFail(need.ToBox2Geometry()).Width.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(10.0, 1e-9, "a consistent input still converts");
    }

    /// <summary>
    /// **x 轴量纲错时同样返回失败** / A dimension mismatch on x also returns a failure.
    /// </summary>
    /// <remarks>
    /// 这条是补上的：第一条判据只让 **y** 不合法，于是"宽度不做量纲校验"的变异体存活——
    /// **一个输入同时触发两个守卫时，去掉任意一个它仍被拒**。两个分量必须各自有只让它非法的输入。
    ///
    /// This one was added late: the first criterion only made **y** invalid, so the mutant dropping the width check survived — **an input that trips two guards still gets
    /// rejected when either one is removed**. Each component needs an input that invalidates only that one.
    /// </remarks>
    [Fact]
    public void ToBox2Geometry_ShouldReturnFailureOnAMismatchedWidth() {
        // x：米 与 千克 混配；y：米-米，合法。
        // x mixes metre with kilogram while y is metre-minus-metre and therefore legal.
        var mixedWidth = new Box2Need<FltX>(Metres(2), Metres(3), Kilograms(12), Metres(13));

        mixedWidth.ToBox2Geometry()
            .Should().BeOfType<Failed<Box2Geometry<FltX>, ErrorCode, Error<ErrorCode>>>(
                "the width guard must fire on its own, not only when the height guard already caught the input");
    }

    /// **板材类型沿用板材 id 并标为主板** / The sheet type takes the sheet id and is marked main.
    [Fact]
    public void ToSheetType_ShouldReuseTheIdAndBeMain() {
        var sheet = new Sheet2<FltX>("S1", Metres(100), Metres(80));

        SheetType<FltX> type = RetAssertions.ValueOrFail(sheet.ToSheetType());

        type.TypeCode.Value.Should().Be("S1");
        type.IsMain.Should().BeTrue("the bridge's input is a sheet already decided upon");
        type.Width.Value.ToFlt64().ToDouble().Should().BeApproximately(100.0, 1e-9);
        type.Height.Value.ToFlt64().ToDouble().Should().BeApproximately(80.0, 1e-9);
    }

    /// **最大承重缺省为零，而不是"不限额"** / The maximum load defaults to zero, not "unlimited".
    [Fact]
    public void ToSheetType_ShouldDefaultTheMaxLoadToZero() {
        var sheet = new Sheet2<FltX>("S1", Metres(100), Metres(80));

        SheetType<FltX> type = RetAssertions.ValueOrFail(sheet.ToSheetType());

        type.MaxLoad.Value.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
        type.MaxLoad.Unit.Should().Be(SIBaseUnits.Meter, "the zero carries the sheet's unit, not a dimensionless default");
    }

    /// **非正尺寸的板材被拒** / A non-positive sheet size is rejected.
    [Fact]
    public void ToSheetType_ShouldRejectNonPositiveDimensions() {
        var zeroWidth = new Sheet2<FltX>("S1", Metres(0), Metres(80));

        zeroWidth.ToSheetType()
            .Should().BeOfType<Failed<SheetType<FltX>, ErrorCode, Error<ErrorCode>>>(
                "a zero-width sheet cannot hold anything, and accepting it defers the failure");
    }

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<FltX> Metres(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>构造千克量 / Build a mass in kilograms.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<FltX> Kilograms(double value) => new(new FltX(value), SIBaseUnits.Kilogram);
}
