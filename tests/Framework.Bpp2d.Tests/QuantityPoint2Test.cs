// 二维量纲点与向量测试 / Tests for 2D quantity points and vectors.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 二维量纲点与向量 / 2D quantity points and vectors.
/// </summary>
public sealed class QuantityPoint2Test {
    /// <summary>向量加法逐分量求和 / Vector addition sums component-wise.</summary>
    [Fact]
    public void Vector_ShouldAddComponentWise() {
        QuantityVector2<FltX> sum = Vector(1.0, 2.0).Add(Vector(3.0, 4.0)).Value;

        D(sum.X).Should().BeApproximately(4.0, 1e-9);
        D(sum.Y).Should().BeApproximately(6.0, 1e-9);
    }

    /// <summary>向量减法逐分量求差 / Vector subtraction subtracts component-wise.</summary>
    [Fact]
    public void Vector_ShouldSubtractComponentWise() {
        QuantityVector2<FltX> difference = Vector(5.0, 7.0).Subtract(Vector(3.0, 4.0)).Value;

        D(difference.X).Should().BeApproximately(2.0, 1e-9);
        D(difference.Y).Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>点位加偏移得到新点位 / Adding an offset to a point yields a new point.</summary>
    [Fact]
    public void Point_ShouldOffsetByVector() {
        QuantityPoint2<FltX> moved = Point(1.0, 2.0).Plus(Vector(10.0, 20.0)).Value;

        D(moved.X).Should().BeApproximately(11.0, 1e-9);
        D(moved.Y).Should().BeApproximately(22.0, 1e-9);
    }

    /// <summary>点位减偏移得到新点位 / Subtracting an offset from a point yields a new point.</summary>
    [Fact]
    public void Point_ShouldSubtractVector() {
        QuantityPoint2<FltX> moved = Point(11.0, 22.0).Minus(Vector(10.0, 20.0)).Value;

        D(moved.X).Should().BeApproximately(1.0, 1e-9);
        D(moved.Y).Should().BeApproximately(2.0, 1e-9);
    }

    /// <summary>
    /// **量纲不匹配必须失败而不是抛异常**：几何计算里写错一个装置不该让整个排样崩掉，而应作为可处理的失败返回。
    /// **A dimension mismatch must fail rather than throw**: one wrong unit in a geometry computation should be a
    /// handleable failure, not a crash of the whole layout run.
    /// </summary>
    [Fact]
    public void Vector_ShouldFailOnDimensionMismatch() {
        var length = new QuantityVector2<FltX>(
            new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter),
            new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter));
        var mass = new QuantityVector2<FltX>(
            new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Kilogram),
            new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter));

        Result<QuantityVector2<FltX>, ErrorCode, Error<ErrorCode>> result = length.Add(mass);

        result.IsOk.Should().BeFalse("长度加质量无量纲意义 / length plus mass is meaningless");
        result.Should().BeOfType<Failed<QuantityVector2<FltX>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// 不同单位但同量纲必须**成功**并换算——只测失配会让"拒绝一切"也算实现。
    /// Different units of the same dimension must **succeed** and convert; a mismatch-only suite would let
    /// "reject everything" count as an implementation.
    /// </summary>
    [Fact]
    public void Vector_ShouldConvertCompatibleUnits() {
        var meters = new QuantityVector2<FltX>(
            new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter),
            new Quantity<FltX>(new FltX(1.0), SIBaseUnits.Meter));
        var centimeters = new QuantityVector2<FltX>(
            new Quantity<FltX>(new FltX(100.0), LengthUnits.Centimeter),
            new Quantity<FltX>(new FltX(100.0), LengthUnits.Centimeter));

        Result<QuantityVector2<FltX>, ErrorCode, Error<ErrorCode>> result = meters.Add(centimeters);

        result.IsOk.Should().BeTrue("同量纲不同单位可以换算 / same dimension, convertible units");
        D(result.Value.X).Should().BeApproximately(2.0, 1e-9);
    }

    /// <summary>工厂方法保持分量原样 / The factories keep components verbatim.</summary>
    [Fact]
    public void Factory_ShouldPreserveComponents() {
        QuantityPoint2<FltX> point = QuantityPoint2Factory.Point2Of(
            new Quantity<FltX>(new FltX(3.0), SIBaseUnits.Meter),
            new Quantity<FltX>(new FltX(4.0), SIBaseUnits.Meter));

        D(point.X).Should().BeApproximately(3.0, 1e-9);
        D(point.Y).Should().BeApproximately(4.0, 1e-9);
    }

    /// <summary>空向量偏移不减不增 / A zero offset leaves the point unchanged.</summary>
    [Fact]
    public void Point_ShouldAcceptZeroOffset() {
        QuantityPoint2<FltX> point = Point(1.0, 2.0);
        QuantityPoint2<FltX> moved = point.Plus(Vector(0.0, 0.0)).Value;

        D(moved.X).Should().BeApproximately(1.0, 1e-9);
        D(moved.Y).Should().BeApproximately(2.0, 1e-9);
    }

    /// <summary>取出数值（FltX 以 decimal 存储）/ Read the number (FltX stores a decimal).</summary>
    private static double D(Quantity<FltX> quantity) => quantity.Value.ToFlt64().ToDouble();

    /// <summary>构造一个二维向量 / Build a 2D vector.</summary>
    private static QuantityVector2<FltX> Vector(double x, double y) => new(
        new Quantity<FltX>(new FltX(x), SIBaseUnits.Meter),
        new Quantity<FltX>(new FltX(y), SIBaseUnits.Meter));

    /// <summary>构造一个二维点 / Build a 2D point.</summary>
    private static QuantityPoint2<FltX> Point(double x, double y) => new(
        new Quantity<FltX>(new FltX(x), SIBaseUnits.Meter),
        new Quantity<FltX>(new FltX(y), SIBaseUnits.Meter));
}
