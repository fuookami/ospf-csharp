// 求解器数值适配测试 / Tests for the solver value adapter.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 求解器数值适配 / Solver value adapter.
/// </summary>
public sealed class SolverValueAdapterTest {
    /// <summary>件数换算为求解器数值 / An amount converts to a solver number.</summary>
    [Fact]
    public void Amount_ShouldConvert() =>
        SolverValueAdapter.Bpp2dSolverAmount(7).ToFlt64().ToDouble().Should().BeApproximately(7.0, 1e-9);

    /// <summary>零件数换算为零 / A zero amount converts to zero.</summary>
    [Fact]
    public void Amount_ShouldHandleZero() =>
        SolverValueAdapter.Bpp2dSolverAmount(0).ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);

    /// <summary>
    /// **大件数不丢精度**：`FltX` 是十进制类型，经 `double` 中转的大整数会失真。
    /// **Large amounts keep precision**: `FltX` is a decimal type and routing large integers through `double` loses accuracy.
    /// </summary>
    [Fact]
    public void Amount_ShouldPreserveLargeValues() =>
        SolverValueAdapter.Bpp2dSolverAmount(123456789UL).ToFlt64().ToDouble()
            .Should().BeApproximately(123456789.0, 1e-3);

    /// <summary>量值取数丢弃单位 / Taking the value drops the unit.</summary>
    [Fact]
    public void Quantity_ShouldDropUnit() =>
        SolverValueAdapter.Bpp2dSolverQuantity(Meters(3.5)).ToFlt64().ToDouble()
            .Should().BeApproximately(3.5, 1e-9);

    /// <summary>比值按分子除以分母 / The ratio divides numerator by denominator.</summary>
    [Fact]
    public void Ratio_ShouldDivide() =>
        SolverValueAdapter.Bpp2dSolverRatio(Meters(6), Meters(3)).ToFlt64().ToDouble()
            .Should().BeApproximately(2.0, 1e-9);

    /// <summary>
    /// **分母为零返回零而不是抛出或得到无穷**：求解器里出现无穷会污染整个目标函数，而零只是一个可解释的退化结果。
    /// **A zero denominator returns zero rather than throwing or yielding infinity**: infinity in a solver poisons the whole
    /// objective, whereas zero is an explainable degenerate result.
    /// </summary>
    [Fact]
    public void Ratio_ShouldReturnZeroForZeroDenominator() =>
        SolverValueAdapter.Bpp2dSolverRatio(Meters(6), Meters(0)).ToFlt64().ToDouble()
            .Should().BeApproximately(0.0, 1e-9);

    /// <summary>
    /// **负分母同样返回零**：负分母会让比值符号翻转，而"面积比"这类量不该为负。
    /// **A negative denominator also returns zero**: it would flip the ratio's sign, and a quantity like an area ratio should
    /// never be negative.
    /// </summary>
    [Fact]
    public void Ratio_ShouldReturnZeroForNegativeDenominator() =>
        SolverValueAdapter.Bpp2dSolverRatio(Meters(6), Meters(-3)).ToFlt64().ToDouble()
            .Should().BeApproximately(0.0, 1e-9);

    /// <summary>缩放保留单位 / Scaling keeps the unit.</summary>
    [Fact]
    public void Scale_ShouldKeepUnit() {
        Quantity<FltX> scaled = SolverValueAdapter.Bpp2dSolverScale(Meters(3), new FltX(2m));

        scaled.Value.ToFlt64().ToDouble().Should().BeApproximately(6.0, 1e-9);
        scaled.Unit.Should().Be(SIBaseUnits.Meter);
    }

    /// <summary>零缩放得到零 / A zero scalar yields zero.</summary>
    [Fact]
    public void Scale_ShouldHandleZero() =>
        SolverValueAdapter.Bpp2dSolverScale(Meters(3), new FltX(0m)).Value.ToFlt64().ToDouble()
            .Should().BeApproximately(0.0, 1e-9);

    /// <summary>负缩放翻转符号 / A negative scalar flips the sign.</summary>
    [Fact]
    public void Scale_ShouldAllowNegative() =>
        SolverValueAdapter.Bpp2dSolverScale(Meters(3), new FltX(-1m)).Value.ToFlt64().ToDouble()
            .Should().BeApproximately(-3.0, 1e-9);

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
