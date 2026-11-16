// 混合板材目标的判据 / Criteria for the mixed sheet objectives.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// **余料率按板材面积归一，缺省系数是一不是零，未绑定板材可互换** / The waste ratio divides by the sheet area, the default coefficient is one, and unbound sheets are interchangeable.
/// </summary>
/// <remarks>
/// 三处都能静默给出一个"合理但错"的数：改用层的外接矩形当分母会让余料率偏小，缺省系数给零会让没配系数的板材免费，
/// 把"两侧都未绑定"判成不等会让本该合并的两列分家。
///
/// All three can silently produce a "plausible but wrong" number: dividing by the layer's bounding rectangle understates waste, a zero default coefficient makes
/// unpriced sheet types free, and treating "both unbound" as unequal splits columns that should merge.
/// </remarks>
public class MixedSheetObjectivesTests {
    /// **余料面积 = 板材面积 − 占用面积** / Waste area is the sheet area minus the occupied area.
    [Fact]
    public void LayerWasteArea_ShouldBeTheUnoccupiedSheetArea() {
        Layer<FltX> layer = LayerOf(occupiedArea: 40, sheetArea: 100);

        MixedSheetObjectives.LayerWasteArea(layer).ToFlt64().ToDouble()
            .Should().BeApproximately(60.0, 1e-9);
    }

    /// <summary>
    /// **占用超过板材面积时余料为零，不是负数** / Over-capacity waste is zero, not negative.
    /// </summary>
    /// <remarks>
    /// 这条是补上的：第一版判据里所有输入都是"占用小于板材"，于是"去掉下限钳制"的变异体存活——
    /// 守卫写了却从没被执行到。负余料会在多列求和里**减掉**别的列的余料，而总数看起来依然合理。
    ///
    /// This one was added late: every input in the first version had occupancy below the sheet, so the mutant removing the lower clamp survived — the guard existed but was
    /// never exercised. Negative waste would **subtract** from another column's waste in a sum, and the total would still look plausible.
    /// </remarks>
    [Fact]
    public void LayerWasteArea_ShouldNeverGoNegativeOnOverCapacity() {
        Layer<FltX> layer = LayerOf(occupiedArea: 200, sheetArea: 100);

        MixedSheetObjectives.LayerWasteArea(layer).ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
        MixedSheetObjectives.LayerWasteRatio(layer).ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// **未绑定板材类型时余料为零** / A layer without a sheet type has no waste.
    [Fact]
    public void LayerWasteArea_ShouldBeZeroWithoutASheetType() {
        Layer<FltX> layer = LayerOf(occupiedArea: 40, sheetArea: null);

        MixedSheetObjectives.LayerWasteArea(layer).ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
        MixedSheetObjectives.LayerWasteRatio(layer).ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>
    /// **余料率的分母是板材面积，不是层的外接矩形** / The ratio divides by the sheet area, not the layer's bounding box.
    /// </summary>
    /// <remarks>
    /// 这一层只占用了 `1×1`，但它的板材是 `10×10`：按板材算余料率是 `0.99`，按它自己的包围盒算则是 `0`。
    /// 判据取前者——两个数差了整整一个量级，而**两个看起来都像是"计算正确"**。
    ///
    /// The layer occupies `1×1` on a `10×10` sheet: against the sheet the waste ratio is `0.99`, against its own bounding box it is `0`. The criterion takes the
    /// former — the two differ by a whole order of magnitude while **both look like a correct computation**.
    /// </remarks>
    [Fact]
    public void LayerWasteRatio_ShouldDivideByTheSheetArea() {
        Layer<FltX> layer = LayerOf(occupiedArea: 1, sheetArea: 100);

        MixedSheetObjectives.LayerWasteRatio(layer).ToFlt64().ToDouble()
            .Should().BeApproximately(0.99, 1e-9);
        layer.Utilization().ToFlt64().ToDouble()
            .Should().BeApproximately(1.0, 1e-9, "the layer fills its own bounding box, which is a different question");
    }

    /// <summary>
    /// **未配价的板材类型取缺省值，未绑定板材也取缺省值** / An unconfigured type and an unbound layer both take the default.
    /// </summary>
    /// <remarks>
    /// 两种情形合成一条判据是有意的：基线的 `?.let {... } ?: default` 就是把这个合并的，
    /// 而分开实现的人很容易只处理其中一种，另一种静默变成零。
    ///
    /// Combining the two cases is deliberate: the baseline's `?.let {... } ?: default` merges them, and a split implementation easily handles one and lets the other
    /// silently become zero.
    /// </remarks>
    [Fact]
    public void PerTypeCost_ShouldFallBackToTheDefaultForBothMissingCases() {
        var prices = new Dictionary<SheetTypeId, FltX> { [new SheetTypeId("A")] = new(7.0) };
        System.Func<Layer<FltX>, FltX> cost = MixedSheetObjectives.PerTypeCost(prices, new FltX(9.0));

        cost(LayerOf(occupiedArea: 1, sheetArea: 100, typeCode: "A")).ToFlt64().ToDouble()
            .Should().BeApproximately(7.0, 1e-9);
        cost(LayerOf(occupiedArea: 1, sheetArea: 100, typeCode: "B")).ToFlt64().ToDouble()
            .Should().BeApproximately(9.0, 1e-9, "an unpriced type must not look free");
        cost(LayerOf(occupiedArea: 1, sheetArea: null)).ToFlt64().ToDouble()
            .Should().BeApproximately(9.0, 1e-9, "an unbound layer follows the same fallback");
    }

    /// <summary>
    /// **缺省数量系数是一，不是零** / The default amount coefficient is one, not zero.
    /// </summary>
    /// <remarks>
    /// 系数乘的是"用了几块这种板"。缺省给零意味着"没配系数的板材不计入用量"，
    /// 于是求解器可以把任意多块板塞进那个类型，而目标值纹丝不动。
    ///
    /// The coefficient multiplies "how many sheets of this type are used". A default of zero means "unconfigured types do not count towards usage", so the solver could pack
    /// arbitrarily many sheets of that type without moving the objective at all.
    /// </remarks>
    [Fact]
    public void PerTypeAmountCoefficient_ShouldDefaultToOne() {
        var coefficients = new Dictionary<SheetTypeId, FltX> { [new SheetTypeId("A")] = new(2.0) };
        System.Func<Layer<FltX>, FltX> coefficient = MixedSheetObjectives.PerTypeAmountCoefficient(coefficients);

        coefficient(LayerOf(occupiedArea: 1, sheetArea: 100, typeCode: "A")).ToFlt64().ToDouble()
            .Should().BeApproximately(2.0, 1e-9);
        coefficient(LayerOf(occupiedArea: 1, sheetArea: 100, typeCode: "B")).ToFlt64().ToDouble()
            .Should().BeApproximately(1.0, 1e-9, "an unconfigured type must still count as one sheet");
        coefficient(LayerOf(occupiedArea: 1, sheetArea: null)).ToFlt64().ToDouble()
            .Should().BeApproximately(1.0, 1e-9);
    }

    /// **只比类型编码；两侧都未绑定时等价** / Only the type code is compared, and two unbound types are equivalent.
    [Fact]
    public void SameSheetType_ShouldCompareCodesOnly() {
        SheetType<FltX> small = SheetTypeOf("A", 10);
        SheetType<FltX> large = SheetTypeOf("A", 100);
        SheetType<FltX> other = SheetTypeOf("B", 10);

        MixedSheetObjectives.SameSheetType(small, large).Should().BeTrue("same code, different dimensions");
        MixedSheetObjectives.SameSheetType(small, other).Should().BeFalse();
        MixedSheetObjectives.SameSheetType(null, null).Should().BeTrue("two unbound columns are interchangeable");
        MixedSheetObjectives.SameSheetType(small, null).Should().BeFalse();
        MixedSheetObjectives.SameSheetType(null, small).Should().BeFalse();
    }

    /// <summary>构造一个层 / Build a layer.</summary>
    /// <param name="occupiedArea">占用面积 / The occupied area.</param>
    /// <param name="sheetArea">板材面积，null 表示未绑定 / The sheet area, null when unbound.</param>
    /// <param name="typeCode">板材类型编码 / The sheet type code.</param>
    /// <returns>层 / The layer.</returns>
    private static Layer<FltX> LayerOf(double occupiedArea, double? sheetArea, string typeCode = "A") {
        var units = new List<Placement2<FltX>> {
            new(new RectanglePackingShape2<FltX>(Meters(occupiedArea), Meters(1)),
                new QuantityPoint2<FltX>(Meters(0), Meters(0)),
                false,
                "A"),
        };
        return new Layer<FltX>(
            0,
            null,
            sheetArea is null ? null : SheetTypeOf(typeCode, System.Math.Sqrt(sheetArea.Value)),
            new Sheet2Shape<FltX>(Meters(occupiedArea), Meters(1)),
            units);
    }

    /// <summary>
    /// 构造板材类型 / Build a sheet type.
    /// </summary>
    /// <param name="code">类型编码 / The type code.</param>
    /// <param name="side">边长（面积 = 边长的平方）/ The side length (area = side squared).</param>
    /// <returns>板材类型 / The sheet type.</returns>
    private static SheetType<FltX> SheetTypeOf(string code, double side) =>
        new(Meters(side), Meters(side), Meters(0), new SheetTypeId(code));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
