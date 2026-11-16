#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Xunit;

namespace Fuookami.Ospf.Quantities.Tests.Unit;

/// <summary>
/// 整数时长截断是对的，负数时长截断到 0 是错的 / Truncating integer seconds is right; truncating negatives is not.
/// </summary>
/// <remarks>
/// B1144 补 4 个面积单位与 2 个 Duration 转换器（基线 `Area.kt` / `DurationExtensions.kt`）。
///
/// **两个类型对"负时长"的处理必须不同**：
/// **`IntX` 能表示负数，所以截断即可；`UIntX` 不能，所以负数必须拒绝。**
/// **把 -5 秒变成 0 秒会让"早于基准点"的信息悄悄消失，而调用方看到的是一个合法的 0。**
///
/// adds
///
/// **The two types must treat a negative duration differently**:
/// **`IntX` can hold negatives, so truncation suffices; `UIntX` cannot, so negatives must be rejected.**
/// **Turning -5 s into 0 s silently erases "before the reference point" while handing back a legal zero.**
/// </remarks>
public class AreaAndDurationConverterTests
{
    /// <summary>**平方链恰好是十分之一英亩** / A square chain is exactly a tenth of an acre.</summary>
    /// <remarks>
    /// **这是英国土地测量的基础关系（10 平方链 = 1 英亩），而它是一条恒等式**：
    /// **两个值各自算错都会让这条断，而"平方链比英亩小"这种定性检查看不出来。**
    ///
    /// **This is the relation British land survey rests on (ten square chains to an acre), and it is an identity**:
    /// **either value going wrong breaks it, while a qualitative "a square chain is smaller than an acre" cannot see it.**
    /// </remarks>
    [Fact]
    public void TheSquareChain_ShouldBeATenthOfAnAcre()
    {
        (FactorOf(AreaUnits.Acre) / FactorOf(AreaUnits.SquareChain)).Should().BeApproximately(
            10.0, 1e-9,
            "ten square chains make an acre");
    }

    /// <summary>**平方杆是 1 rod 见方（≈25.29 m²）** / A square rod is one rod on a side.</summary>
    [Fact]
    public void TheSquareRod_ShouldBeOneRodOnASide()
    {
        // 1 rod = 5.0292 m，平方得 25.29285264 m²。
        // One rod is 5.0292 m, squared gives 25.29285264 m2.
        FactorOf(AreaUnits.SquareRod).Should().BeApproximately(
            5.0292 * 5.0292, 1e-9,
            "a square rod is the area of a square five point zero two nine two metres on a side");
    }

    /// <summary>**面积阶梯的十进制部分每级 100 倍** / The decimal area ladder steps by a hundred.</summary>
    /// <remarks>
    /// **面积是长度平方，所以小数点移动两位对应 100 倍，不是 10 倍。**
    /// **把它当作"像长度那样每级 10 倍"会让每级差一个数量级。**
    ///
    /// **Area is length squared, so two decimal places mean a factor of 100, not 10.**
    /// **Treating it "like length, ten per step" is off by a decade at every level.**
    /// </remarks>
    [Fact]
    public void TheDecimalAreaLadder_ShouldStepByAHundred()
    {
        (FactorOf(AreaUnits.SquareDecimeter) / FactorOf(AreaUnits.SquareCentimeter)).Should().BeApproximately(100.0, 1e-9);
        (FactorOf(AreaUnits.SquareMeter) / FactorOf(AreaUnits.SquareDecimeter)).Should().BeApproximately(100.0, 1e-9);
        (FactorOf(AreaUnits.SquareMillimeter) * 100).Should().BeApproximately(
            FactorOf(AreaUnits.SquareCentimeter), 1e-12);
    }

    /// <summary>**IntX 转换器截断到整秒，不带小数** / The IntX converter truncates to whole seconds.</summary>
    [Fact]
    public void TheIntXConverter_ShouldTruncateToWholeSeconds()
    {
        IntXDurationConverter converter = IntXDurationConverter.Instance;
        Result<Quantity<IntX>, ErrorCode, Error<ErrorCode>> quantity =
            converter.ToQuantity(TimeSpan.FromSeconds(90.7), SIBaseUnits.Second);

        quantity.IsOk.Should().BeTrue("a positive duration is representable");
        quantity.Value.Value.Should().Be(new IntX(90),
            "an integral type carries no fraction, and the baseline truncates the same way");
    }

    /// <summary>**UIntX 转换器必须拒绝负时长，而不是截断到 0** / A negative duration is rejected, not zeroed.</summary>
    /// <remarks>
    /// **这是本批最重要的一条判据。** **截断到 0 会让 -5 秒变成"合法的 0 秒"，
    /// 而调用方无法区分"正好在基准点"与"早于基准点 5 秒"。**
    ///
    /// **The most important criterion here.** **Zeroing turns -5 s into a legal 0 s,
    /// and the caller cannot tell "exactly at the reference point" from "five seconds before it".**
    /// </remarks>
    [Fact]
    public void TheUIntXConverter_ShouldRejectNegativeDurations()
    {
        UIntXDurationConverter converter = UIntXDurationConverter.Instance;
        Result<Quantity<UIntX>, ErrorCode, Error<ErrorCode>> negative =
            converter.ToQuantity(TimeSpan.FromSeconds(-5), SIBaseUnits.Second);

        negative.IsFailed.Should().BeTrue("a negative duration has no unsigned representation");
        negative.IsOk.Should().BeFalse();

        Result<Quantity<UIntX>, ErrorCode, Error<ErrorCode>> zero =
            converter.ToQuantity(TimeSpan.Zero, SIBaseUnits.Second);
        zero.IsOk.Should().BeTrue("zero itself is representable");
        zero.Value.Value.Should().Be(UIntX.Zero);
    }

    /// <summary>**IntX 接受负时长** / The IntX converter accepts negatives.</summary>
    /// <remarks>
    /// **与上一条成对：** **如果两个转换器都把负数拒掉，那"IntX 能表示负数"这件事就没被用上；
    /// 如果两个都接受，UIntX 就会溢出或静默取值。**
    ///
    /// **Paired with the previous one**: **rejecting negatives in both would leave "IntX holds negatives" unused;
    /// accepting in both would overflow or silently wrap UIntX.**
    /// </remarks>
    [Fact]
    public void TheIntXConverter_ShouldAcceptNegativeDurations()
    {
        IntXDurationConverter converter = IntXDurationConverter.Instance;
        Result<Quantity<IntX>, ErrorCode, Error<ErrorCode>> negative =
            converter.ToQuantity(TimeSpan.FromSeconds(-5), SIBaseUnits.Second);

        negative.IsOk.Should().BeTrue("a signed integral type can hold five seconds before the reference point");
        negative.Value.Value.Should().Be(new IntX(-5));
    }

    /// <summary>**往返一次不丢整秒** / A round trip loses no whole second.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3600)]
    [InlineData(-86400)]
    public void TheRoundTrip_ShouldPreserveWholeSeconds(int seconds)
    {
        IntXDurationConverter converter = IntXDurationConverter.Instance;
        Result<Quantity<IntX>, ErrorCode, Error<ErrorCode>> quantity =
            converter.ToQuantity(TimeSpan.FromSeconds(seconds), SIBaseUnits.Second);
        quantity.IsOk.Should().BeTrue();

        Result<TimeSpan, ErrorCode, Error<ErrorCode>> back = converter.ToDuration(quantity.Value);
        back.IsOk.Should().BeTrue("a whole-second duration converts back");
        back.Value.TotalSeconds.Should().Be(seconds);
    }

    /// <summary>取一个单位相对其基准的换算因子 / Read a unit's factor against its own base.</summary>
    private static double FactorOf(PhysicalUnit unit) =>
        unit.ConversionRule
            .Should().BeAssignableTo<UnitConversionRule.Linear>().Subject
            .Scale.ValueOrNull!.Value.ToFlt64().ToDouble();
}
