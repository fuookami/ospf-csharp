// CSV 数据集读取与渲染适配判据 / Criteria for CSV dataset loading and the render adapter.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp1d.Application;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Packing;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Application;

/// <summary>
/// **CSV 读取与渲染适配** / CSV loading and the render adapter.
/// </summary>
/// <remarks>
/// 两者都**不需要求解器**，所以判据全都进默认全量测试——这是本会话少有的、覆盖不靠 `RunScipIntegration` 的一批。
/// Both need **no solver**, so every criterion here lands in the default full suite — a rarity in this session.
/// </remarks>
public class Bpp1dCsvAndRenderTests {
    // ===== CSV 读取 =====

    /// <summary>**好数据读得出来，空库存读成"未声明"** / Good data reads, and an empty inventory cell means "not declared".</summary>
    /// <remarks>
    /// **空串不是 0。** 未声明库存 = 箱型不限量；库存 0 = 一件也不能用。读成 0 会**静默禁掉一个箱型**。
    ///
    /// **An empty cell is not zero.** Undeclared inventory is unlimited while zero forbids the bin type; reading it as 0 would **silently ban a bin type**.
    /// </remarks>
    [Fact]
    public void Load_ShouldReadRowsAndKeepAnEmptyInventoryUndeclared() {
        Result<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>> result = Loader().Load(
            "id,length,weight,amount\nP1,100,2,3\n",
            "id,length,max_weight,cost,inventory\nB1,500,10,1,\n",
            LengthUnits.Millimeter,
            SIBaseUnits.Kilogram,
            Millimetres(0.0));

        result.IsOk.Should().BeTrue(result.ToString());
        PackingProblem<Flt64> problem = ((Ok<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        problem.Items.Should().HaveCount(1);
        problem.Items[0].Amount.Should().Be(3UL);
        problem.BinTypes.Should().HaveCount(1);
        problem.BinTypes[0].Inventory.Should().BeNull("an empty inventory cell means no declared stock");
    }

    /// <summary>**声明了库存就是那个数** / A declared inventory is that number.</summary>
    [Fact]
    public void Load_ShouldKeepADeclaredInventory() {
        Result<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>> result = Loader().Load(
            "id,length,weight,amount\nP1,100,2,3\n",
            "id,length,max_weight,cost,inventory\nB1,500,10,1,7\n",
            LengthUnits.Millimeter,
            SIBaseUnits.Kilogram,
            Millimetres(0.0));

        result.IsOk.Should().BeTrue(result.ToString());
        ((Ok<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value.BinTypes[0].Inventory
            .Should().Be(7UL);
    }

    /// <summary>**表头必须完全匹配，多一列就拒** / The header must match exactly; an extra column is rejected.</summary>
    /// <remarks>
    /// 少一列 / 多一列能过的话，后面的取值拿到空串就**静默变成 0**——数据错而**不报错**。
    ///
    /// If the schema were loose, a missing or extra column would leave lookups with empty strings that **silently become zero**.
    /// </remarks>
    /// <summary>**缺一列就拒绝** / A missing column is refused.</summary>
    /// <remarks>
    /// **另有 `Load_ShouldRejectAnExtraHeaderColumn` 钉多一列，这条钉少一列。**
    /// **两个方向都得钉：按列名取值时，缺列会在取值那一行抛，而多列只是没人读它。**
    ///
    /// **`Load_ShouldRejectAnExtraHeaderColumn` pins one column too many; this pins one too few.**
    /// **Both directions matter: columns are read by name, so a missing one throws at the lookup, while an extra one is simply never read.**
    /// </remarks>
    [Fact]
    public void Load_ShouldRejectAMissingColumn() {
        Result<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>> result = Loader().Load(
            "id,length,weight\nP1,100,2\n",
            "id,length,max_weight,cost,inventory\nB1,500,10,1,\n",
            LengthUnits.Millimeter,
            SIBaseUnits.Kilogram,
            Millimetres(0.0));

        result.IsOk.Should().BeFalse("the amount column is required and must not default to zero");
    }

    [Fact]
    public void Load_ShouldRejectAnExtraHeaderColumn() {
        Result<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>> result = Loader().Load(
            "id,length,weight,amount,extra\nP1,100,2,3,x\n",
            "id,length,max_weight,cost,inventory\nB1,500,10,1,\n",
            LengthUnits.Millimeter,
            SIBaseUnits.Kilogram,
            Millimetres(0.0));

        result.IsFailed.Should().BeTrue("the closed schema admits exactly four item columns");
        ((Failed<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>**引号里的逗号不拆字段** / A comma inside quotes does not split the field.</summary>
    [Fact]
    public void Load_ShouldKeepAQuotedCommaInsideOneField() {
        Result<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>> result = Loader().Load(
            "id,length,weight,amount\n\"a,b\",100,2,3\n",
            "id,length,max_weight,cost,inventory\nB1,500,10,1,\n",
            LengthUnits.Millimeter,
            SIBaseUnits.Kilogram,
            Millimetres(0.0));

        ((Ok<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value.Items[0].Id
            .Should().Be("a,b");
    }

    /// <summary>**`""` 转义出单个引号** / A doubled quote inside a quoted field yields one quote.</summary>
    [Fact]
    public void Load_ShouldUnescapeADoubledQuote() {
        Result<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>> result = Loader().Load(
            "id,length,weight,amount\n\"a\"\"b\",100,2,3\n",
            "id,length,max_weight,cost,inventory\nB1,500,10,1,\n",
            LengthUnits.Millimeter,
            SIBaseUnits.Kilogram,
            Millimetres(0.0));

        ((Ok<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value.Items[0].Id
            .Should().Be("a\"b");
    }

    /// <summary>**行宽不一致必须拒** / A row of the wrong width must be rejected.</summary>
    /// <remarks>
    /// 短行放过会让字段**错位对齐到别的列**——数据全错而**不报错**。
    ///
    /// A short row that were accepted would **shift fields onto other columns** — everything wrong, nothing reported.
    /// </remarks>
    [Fact]
    public void Load_ShouldRejectARowOfTheWrongWidth() {
        Result<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>> result = Loader().Load(
            "id,length,weight,amount\nP1,100,2\n",
            "id,length,max_weight,cost,inventory\nB1,500,10,1,\n",
            LengthUnits.Millimeter,
            SIBaseUnits.Kilogram,
            Millimetres(0.0));

        result.IsFailed.Should().BeTrue("the item row has three fields where four are declared");
    }

    /// <summary>**未闭合的引号必须拒** / An unclosed quote must be rejected.</summary>
    [Fact]
    public void Load_ShouldRejectAnUnclosedQuote() {
        Result<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>> result = Loader().Load(
            "id,length,weight,amount\n\"P1,100,2,3\n",
            "id,length,max_weight,cost,inventory\nB1,500,10,1,\n",
            LengthUnits.Millimeter,
            SIBaseUnits.Kilogram,
            Millimetres(0.0));

        result.IsFailed.Should().BeTrue("the quote is never closed");
    }

    /// <summary>**非数字的数量必须拒** / A non-numeric amount must be rejected.</summary>
    [Fact]
    public void Load_ShouldRejectANonNumericAmount() {
        Result<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>> result = Loader().Load(
            "id,length,weight,amount\nP1,100,2,x\n",
            "id,length,max_weight,cost,inventory\nB1,500,10,1,\n",
            LengthUnits.Millimeter,
            SIBaseUnits.Kilogram,
            Millimetres(0.0));

        result.IsFailed.Should().BeTrue("'x' is not an amount");
    }

    /// <summary>**数字解析失败原样透出** / A parse failure passes through untouched.</summary>
    [Fact]
    public void Load_ShouldPropagateAParserFailure() {
        var failing = new Bpp1dCsvDatasetLoader<Flt64>(
            _ => Results.Failed<Flt64>(new Err<ErrorCode>(ErrorCode.DataNotFound, "no number")));

        Result<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>> result = failing.Load(
            "id,length,weight,amount\nP1,100,2,3\n",
            "id,length,max_weight,cost,inventory\nB1,500,10,1,\n",
            LengthUnits.Millimeter,
            SIBaseUnits.Kilogram,
            Millimetres(0.0));

        result.IsFailed.Should().BeTrue();
        ((Failed<PackingProblem<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.DataNotFound, "the parser's own code must survive the loader");
    }

    // ===== 渲染适配 =====

    /// <summary>**渲染契约按显式几何生成** / The render contract follows the explicit geometry.</summary>
    [Fact]
    public void ToSchema_ShouldRenderTheExplicitGeometry() {
        Result<RenderSchema1Dto, ErrorCode, Error<ErrorCode>> result = Renderer().ToSchema(
            MakeResult(
                new Placement<Flt64>("I1", Millimetres(0.0), Millimetres(100.0)),
                new Placement<Flt64>("I2", Millimetres(100.0), Millimetres(200.0))),
            LengthUnits.Millimeter);

        result.IsOk.Should().BeTrue(result.ToString());
        RenderSchema1Dto schema = ((Ok<RenderSchema1Dto, ErrorCode, Error<ErrorCode>>)result).Value;
        schema.Bins.Should().HaveCount(1);
        schema.Bins[0].Length.Should().BeApproximately(500.0, 1e-9);
        schema.Bins[0].Items.Should().HaveCount(2);
        schema.Bins[0].Items[1].Start.Should().BeApproximately(100.0, 1e-9);
        schema.Bins[0].Items[1].Length.Should().BeApproximately(200.0, 1e-9);
    }

    /// <summary>**向前重叠必须拒** / A backward overlap must be rejected.</summary>
    /// <remarks>
    /// **本批最要紧的一条渲染判据。** 两件物品压在一起，在图上只是"挤"，**不会报错**。
    ///
    /// **The most important render criterion here.** Two overlapping items merely **look cramped** on a chart and raise nothing.
    /// </remarks>
    [Fact]
    public void ToSchema_ShouldRejectABackwardOverlap() {
        Result<RenderSchema1Dto, ErrorCode, Error<ErrorCode>> result = Renderer().ToSchema(
            MakeResult(
                new Placement<Flt64>("I1", Millimetres(0.0), Millimetres(200.0)),
                new Placement<Flt64>("I2", Millimetres(100.0), Millimetres(100.0))),
            LengthUnits.Millimeter);

        result.IsFailed.Should().BeTrue("the second item starts before the first one ends");
        ((Failed<RenderSchema1Dto, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>**零长度放置必须拒** / A zero-length placement must be rejected.</summary>
    [Fact]
    public void ToSchema_ShouldRejectAZeroLengthPlacement() {
        Result<RenderSchema1Dto, ErrorCode, Error<ErrorCode>> result = Renderer().ToSchema(
            MakeResult(new Placement<Flt64>("I1", Millimetres(0.0), Millimetres(0.0))),
            LengthUnits.Millimeter);

        result.IsFailed.Should().BeTrue("a zero-length item occupies space invisibly");
    }

    /// <summary>**伸出箱外必须拒** / Overflowing the bin must be rejected.</summary>
    [Fact]
    public void ToSchema_ShouldRejectAnOverflowBeyondTheTolerance() {
        Result<RenderSchema1Dto, ErrorCode, Error<ErrorCode>> result = Renderer().ToSchema(
            MakeResult(new Placement<Flt64>("I1", Millimetres(0.0), Millimetres(500.5))),
            LengthUnits.Millimeter);

        result.IsFailed.Should().BeTrue("500.5 mm does not fit a 500 mm bin");
    }

    /// <summary>**容差内的噪声不算越界** / Noise inside the tolerance is not an overflow.</summary>
    /// <remarks>
    /// 反向对照：容差写死成 0 时这条会红。**`1e-8` 是给浮点噪声的，不是给"稍微超一点"的。**
    ///
    /// Reverse control: a zero tolerance turns this red. **`1e-8` is slack for floating noise**, not for a slightly-over placement.
    /// </remarks>
    [Fact]
    public void ToSchema_ShouldTolerateOverflowWithinTheFloatingNoise() {
        Result<RenderSchema1Dto, ErrorCode, Error<ErrorCode>> result = Renderer().ToSchema(
            MakeResult(new Placement<Flt64>("I1", Millimetres(0.0), Millimetres(500.000000004))),
            LengthUnits.Millimeter);

        result.IsOk.Should().BeTrue("4e-9 mm beyond the bin length is floating noise: " + result.ToString());
    }

    /// <summary>构造读取器 / Build a loader.</summary>
    /// <returns>读取器 / The loader.</returns>
    private static Bpp1dCsvDatasetLoader<Flt64> Loader() =>
        new(text => double.TryParse(text, out double value)
            ? Results.Ok(new Flt64(value))
            : Results.Failed<Flt64>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "not a number")));

    /// <summary>构造渲染器 / Build the renderer.</summary>
    /// <returns>渲染器 / The renderer.</returns>
    private static PackingRendererAdapter<Flt64> Renderer() =>
        new(Flt64PackingValueAdapter.Instance);

    /// <summary>构造一次只含一个箱的结果 / Build a result with exactly one bin.</summary>
    /// <param name="placements">放置 / The placements.</param>
    /// <returns>结果 / The result.</returns>
    private static PackingResult<Flt64> MakeResult(params Placement<Flt64>[] placements) =>
        new(
            Plans: new List<PackingPlan<Flt64>> {
                new(
                    new Bin<Flt64>("bin_0", new BinType<Flt64>(
                        "B1", Millimetres(500.0), Kilograms(10.0), Flt64.One, null)),
                    placements),
            },
            Remnants: new List<Remnant<Flt64>>(),
            Materials: new Dictionary<string, ulong>(),
            Utilization: new Flt64(0.6));

    /// <summary>构造毫米量 / Build a millimetre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    /// <summary>构造千克量 / Build a kilogram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) =>
        new(new Flt64(value), SIBaseUnits.Kilogram);
}
