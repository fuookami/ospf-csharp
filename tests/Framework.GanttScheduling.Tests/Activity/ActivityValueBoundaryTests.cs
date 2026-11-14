// 活动数值转换边界的判据 / Criteria for the activity value boundary.

using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Activity;

/// <summary>
/// **下溢、仿射单位、非有限值三类都必须被拒，而三类转换后都"看着正常"** / Underflow, affine units and non-finite values must all be rejected, and all three look fine after conversion.
/// </summary>
/// <remarks>
/// 下溢让一个本该消耗物料的作业消耗了零；仿射单位的线性缩放没有物理意义；`NaN` 进入模型后
/// **所有约束比较都为假，于是所有约束都被静默满足**。三者都不留痕迹。
///
/// Underflow makes an operation that should consume material consume nothing; linear scaling of affine units has no physical meaning; and once `NaN` enters the model
/// **every constraint comparison is false, so every constraint is silently satisfied**. None of the three leaves a trace.
/// </remarks>
public class ActivityValueBoundaryTests {
    /// **同量纲的正常换算通过** / A normal same-dimension conversion passes.
    [Fact]
    public void Value_ShouldConvertWithinTheSameDimension() {
        var quantity = new Quantity<Flt64>(new Flt64(1.5), LengthUnits.Kilometer);

        Result<Flt64, ErrorCode, Error<ErrorCode>> result =
            ActivityValueBoundary.Value(quantity, SIBaseUnits.Meter, "length");
        result.Should().BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>();
        Flt64 value = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)result).Value;

        value.ToDouble().Should().BeApproximately(1500.0, 1e-9);
    }

    /// <summary>
    /// **换算下溢被拒** / Conversion underflow is rejected.
    /// </summary>
    /// <remarks>
    /// 判据必须构造一个**原值非零、换算后精确为零**的输入。`1e-320 米` 转成千米会落到次正规数以下变成零；
    /// 用一个"看着很小但不为零"的值（如 `1e-9`）是测不出下溢的。
    ///
    /// The fixture must make the original **non-zero while the converted value is exactly zero**. `1e-320 metres` converted to kilometres falls below the subnormal range and
    /// becomes zero; a merely "very small but non-zero" value such as `1e-9` cannot exercise underflow.
    /// </remarks>
    [Fact]
    public void Value_ShouldRejectUnderflowToZero() {
        var tiny = new Quantity<Flt64>(new Flt64(1e-320), LengthUnits.Nanometer);

        ActivityValueBoundary.Value(tiny, LengthUnits.Kilometer, "tiny")
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>(
                "a value that converts to zero has silently lost the whole quantity");
    }

    /// <summary>
    /// **本来就是零的量不算下溢** / A quantity that is already zero is not underflow.
    /// </summary>
    /// <remarks>
    /// 这条补的是另一端：守卫写成 `normalized == 0` 时，一个**合法的零距离**（里程碑、零时长作业）会被误报成下溢。
    /// 两个方向都要有判据——只测"下溢必报"的话，一个把全部零值都拒掉的实现照样通过。
    ///
    /// This covers the other side: with the guard written as `normalized == 0`, a **legitimate zero distance** (a milestone, a zero-duration operation) is misreported as
    /// underflow. Both directions need criteria — testing only "underflow must be reported" would let an implementation that rejects every zero pass.
    /// </remarks>
    [Fact]
    public void Value_ShouldAcceptAGenuineZero() {
        var zero = new Quantity<Flt64>(new Flt64(0.0), LengthUnits.Kilometer);

        ActivityValueBoundary.Value(zero, SIBaseUnits.Meter, "zero")
            .Should().BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>(
                "a milestone has zero length and that is not an underflow");
    }

    /// **仿射单位在入口被拒** / Affine units are rejected at the door.
    [Fact]
    public void Value_ShouldRejectAffineUnits() {
        PhysicalUnit affine = new TestAffineUnit();
        var quantity = new Quantity<Flt64>(new Flt64(20.0), SIBaseUnits.Meter);

        ActivityValueBoundary.Value(quantity, affine, "temperature")
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
    }

    /// **量纲不兼容被拒** / An incompatible dimension is rejected.
    [Fact]
    public void Value_ShouldRejectAnIncompatibleDimension() {
        var length = new Quantity<Flt64>(new Flt64(1.0), SIBaseUnits.Meter);

        ActivityValueBoundary.Value(length, SIBaseUnits.Kilogram, "mass")
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **非有限值在回程被拒** / A non-finite value is rejected on the return trip.
    /// </summary>
    /// <remarks>
    /// `Flt64` 以 `decimal` 存储，`new Flt64(double.NaN)` 会直接抛 `OverflowException`——**它本身就不可能非有限**。
    /// 所以非有限值的守卫只能在回程（求解器数值 → 业务量）有意义：那里收到的可能来自别的数值后端。
    /// 在入参侧写这条判据是测不到的，写了也只是摆设。
    ///
    /// `Flt64` stores a `decimal`, so `new Flt64(double.NaN)` throws `OverflowException` — **it cannot even be non-finite**. The guard therefore only means something on the
    /// return trip (solver value → business quantity), where the input may come from another numeric backend. A criterion on the inbound side cannot be exercised at all.
    /// </remarks>
    [Fact]
    public void QuantityOf_ShouldRejectANonFiniteSolverValue() {
        ActivityValueBoundary.QuantityOf(new Flt64(double.PositiveInfinity), SIBaseUnits.Meter)
            .Should().BeOfType<Failed<Quantity<Flt64>, ErrorCode, Error<ErrorCode>>>();

        ActivityValueBoundary.QuantityOf(new Flt64(2.5), SIBaseUnits.Meter)
            .Should().BeOfType<Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>测试用仿射单位 / An affine unit for tests.</summary>
    private sealed class TestAffineUnit : PhysicalUnit {
        /// <inheritdoc/>
        public override string Name => "affine test unit";

        /// <inheritdoc/>
        public override string Symbol => "af";

        /// <inheritdoc/>
        public override Fuookami.Ospf.Quantities.Dimension.DerivedQuantity Quantity =>
            Fuookami.Ospf.Quantities.Dimension.Dimensions.Length;

        /// <inheritdoc/>
        public override UnitConversionRule ConversionRule =>
            new UnitConversionRule.Affine(Fuookami.Ospf.Math.Scale.Invoke(1.0), FltX.Zero);
    }
}
