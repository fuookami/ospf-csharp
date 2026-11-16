// 数值类型到 FltX 的转换的判据 / Criteria for converting numeric types to FltX.

using System;
using FluentAssertions;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Algebra.Number;
using N = Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Math.Tests;

/// <summary>
/// **数值类型到 `FltX` 的转换** / Converting numeric types to `FltX`.
/// </summary>
/// <remarks>
/// 判据的重点：**十二个数值类型全通**、**值真的传过去**、**未知类型抛出而不是返回 0**。
///
/// The criteria focus on **all twelve numeric types working**, on **the value really coming through**, and on **an unknown type throwing rather than returning zero**.
/// </remarks>
public class NumberConversionTests {
    /// <summary>
    /// **十二个数值类型都能转，且值对** / All twelve numeric types convert, with the right value.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 十二个类型**逐个**转到 `FltX` 并等于期望值——不是"挑几个代表性的"。
    /// 少一个就是运行期一处 `NotSupportedException`（`Flt32` / `Int32` / `UInt32` 原本各自缺在不同调用点）。
    /// 2. `FltX` 转自己**值不变**（不能顺手降级成 `double`）。
    /// 3. 负值与小数**符号与量级都对**（负数转无符号类型会另说，这里只走有符号）。
    ///
    /// Three paired points: **every one** of the twelve types converts to `FltX` with the expected value rather than a hand-picked representative few, since one missing type is one run-time
    /// `NotSupportedException` (`Flt32` / `Int32` / `UInt32` were each missing at a different call site); `FltX` to itself **keeps its value** rather than quietly degrading through `double`; and
    /// negative and fractional values keep **both sign and magnitude**.
    /// </remarks>
    [Fact]
    public void ToFltXValue_ShouldCoverEveryNumericType() {
        new N.FltX(1.5m).ToFltXValue().Should().Be(new N.FltX(1.5m));
        new N.Flt64(1.5).ToFltXValue().Should().Be(new N.FltX(1.5m));
        new N.Flt32(1.5f).ToFltXValue().Should().Be(new N.FltX(1.5m));
        new N.Int64(-7L).ToFltXValue().Should().Be(new N.FltX(-7m));
        new N.Int32(-7).ToFltXValue().Should().Be(new N.FltX(-7m));
        new N.Int16(-7).ToFltXValue().Should().Be(new N.FltX(-7m));
        new N.Int8(-7).ToFltXValue().Should().Be(new N.FltX(-7m));
        new N.UInt64(7UL).ToFltXValue().Should().Be(new N.FltX(7m));
        new N.UInt32(7U).ToFltXValue().Should().Be(new N.FltX(7m));
        new N.UInt16(7).ToFltXValue().Should().Be(new N.FltX(7m));
        new N.UInt8(7).ToFltXValue().Should().Be(new N.FltX(7m));
        new N.IntX(new System.Numerics.BigInteger(-7)).ToFltXValue().Should().Be(new N.FltX(-7m));
        new N.UIntX(new System.Numerics.BigInteger(7)).ToFltXValue().Should().Be(new N.FltX(7m));
    }

    /// <summary>
    /// **未知类型抛出，不返回 0** / An unknown type throws instead of returning zero.
    /// </summary>
    /// <remarks>
    /// 两点成对：非数值结构体 **抛 `NotSupportedException`**；抛出的消息**带类型名**，
    /// 否则排障时只知道"转不了"而不知道"转不了的是什么"。
    ///
    /// Two paired points: a non-numeric struct **throws `NotSupportedException`**, and the message **names the type**, otherwise a diagnosis says only "cannot convert" without saying what.
    /// </remarks>
    [Fact]
    public void ToFltXValue_ShouldThrowForAnUnknownType() {
        Func<FltX> act = () => default(Guid).ToFltXValue();

        act.Should().Throw<NotSupportedException>().WithMessage("*Guid*");
    }
}
