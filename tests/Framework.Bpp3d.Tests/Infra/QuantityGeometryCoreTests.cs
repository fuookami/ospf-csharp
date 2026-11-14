// 量纲几何核心的判据 / Criteria for the quantity geometry core.

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.Bpp3d.Tests.Application;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Infra;

/// <summary>
/// **向量的分量整体一致，取整用自身单位** / A vector fails as a whole, and rounding stays in the quantity's own unit.
/// </summary>
/// <remarks>
/// 两处都容易出"部分正确"的结果：漏检一个分量的量纲会得到一个半对半错的向量；
/// 取整前换算到基准单位会把 `150 厘米` 压成 `1 米`——两个数字在计数里都看起来正常。
///
/// Both spots easily produce a "partly correct" answer: missing one component's dimension check yields a half-right vector, and rounding after converting to the base unit
/// collapses `150 cm` into `1 m` — both numbers look normal in a count.
/// </remarks>
public class QuantityGeometryCoreTests {
    /// **向量加法逐分量相加** / Vector addition adds component-wise.
    [Fact]
    public void QuantityVector3_ShouldAddComponentWise() {
        var left = new QuantityVector3<Flt64>(Metres(1.0), Metres(2.0), Metres(3.0));
        var right = new QuantityVector3<Flt64>(Metres(4.0), Metres(5.0), Metres(6.0));

        QuantityVector3<Flt64> sum = RetAssertions.Unwrap(left.Add(right));

        sum.X.Value.ToDouble().Should().BeApproximately(5.0, 1e-12);
        sum.Y.Value.ToDouble().Should().BeApproximately(7.0, 1e-12);
        sum.Z.Value.ToDouble().Should().BeApproximately(9.0, 1e-12);
        sum.X.Unit.Should().Be(SIBaseUnits.Meter);
    }

    /// **向量减法逐分量相减** / Vector subtraction subtracts component-wise.
    [Fact]
    public void QuantityVector3_ShouldSubtractComponentWise() {
        var left = new QuantityVector3<Flt64>(Metres(5.0), Metres(7.0), Metres(9.0));
        var right = new QuantityVector3<Flt64>(Metres(1.0), Metres(2.0), Metres(3.0));

        QuantityVector3<Flt64> difference = RetAssertions.Unwrap(left.Subtract(right));

        difference.X.Value.ToDouble().Should().BeApproximately(4.0, 1e-12);
        difference.Y.Value.ToDouble().Should().BeApproximately(5.0, 1e-12);
        difference.Z.Value.ToDouble().Should().BeApproximately(6.0, 1e-12);
    }

    /// <summary>
    /// **z 分量量纲不符时整体失败** / A dimension mismatch on z fails the whole vector.
    /// </summary>
    /// <remarks>
    /// 这条专门打**第三个分量**：只检查前两个分量的实现会在 `x`、`y` 都合法时直接返回结果，
    /// 于是 `z` 的错误被完全跳过。用前两个分量合法的输入，才能确认检查覆盖到了最后一个。
    ///
    /// This targets the **third** component on purpose: an implementation that checks only the first two returns a result whenever `x` and `y` are fine, so the `z`
    /// error is skipped entirely. Only an input with valid `x` and `y` proves the check reaches the last one.
    /// </remarks>
    [Fact]
    public void QuantityVector3_ShouldFailWhenTheThirdComponentMismatches() {
        var metres = new QuantityVector3<Flt64>(Metres(1.0), Metres(2.0), Metres(3.0));
        var mixedLast = new QuantityVector3<Flt64>(Metres(4.0), Metres(5.0), Kilograms(6.0));

        Result<QuantityVector3<Flt64>, ErrorCode, Error<ErrorCode>> sum = metres.Add(mixedLast);

        sum.Should().BeOfType<Failed<QuantityVector3<Flt64>, ErrorCode, Error<ErrorCode>>>(
            "kg cannot join m; a partly-added vector would look legal downstream");
    }

    /// **首个分量量纲不符同样整体失败** / A mismatch on the first component also fails the whole vector.
    [Fact]
    public void QuantityVector3_ShouldFailWhenTheFirstComponentMismatches() {
        var metres = new QuantityVector3<Flt64>(Metres(1.0), Metres(2.0), Metres(3.0));
        var mixedFirst = new QuantityVector3<Flt64>(Kilograms(4.0), Metres(5.0), Metres(6.0));

        metres.Subtract(mixedFirst)
            .Should().BeOfType<Failed<QuantityVector3<Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **取整用该量自身的单位** / Rounding stays in the quantity's own unit.
    /// </summary>
    /// <remarks>
    /// 用厘米做输入：`150 cm` 向下取整必须是 `150`。若实现先换算到米再取整，会得到 `1`——
    /// 而 `1` 在"这条带能切几段"的计数里完全不显眼，却整体错了一个量级。
    ///
    /// Uses centimetres: flooring `150 cm` must give `150`. An implementation that converts to metres first returns `1` — and `1` is utterly unremarkable when counting
    /// how many pieces fit on a strip, while being off by two orders of magnitude.
    /// </remarks>
    [Fact]
    public void Rounding_ShouldStayInTheQuantitysOwnUnit() {
        var centimetres = new Quantity<FltX>(new FltX(150.0), LengthUnits.Centimeter);

        centimetres.Floor().Value.ToFlt64().ToDouble().Should().BeApproximately(150.0, 1e-12);
        centimetres.Ceil().Value.ToFlt64().ToDouble().Should().BeApproximately(150.0, 1e-12);
        centimetres.Floor().Unit.Should().Be(LengthUnits.Centimeter,
            "the unit must survive; dropping it would turn the result into a bare number");
    }

    /// <summary>
    /// **向下与向上取整在非整数上分开，四舍五入远离零** / Floor and ceil diverge on fractions, and rounding goes away from zero.
    /// </summary>
    /// <remarks>
    /// `.5` 是唯一能区分"远离零"与银行家舍入的输入：`2.5` 远离零得 `3`，银行家舍入得 `2`。
    /// 负数 `-2.5` 同理：远离零得 `-3`。
    ///
    /// `.5` is the only input that separates "away from zero" from banker's rounding: `2.5` gives `3` away from zero and `2` under banker's rounding. The negative case
    /// `-2.5` likewise gives `-3`.
    /// </remarks>
    [Fact]
    public void Rounding_ShouldDivergeCorrectlyOnFractions() {
        var fraction = new Quantity<FltX>(new FltX(2.5), SIBaseUnits.Meter);
        var negative = new Quantity<FltX>(new FltX(-2.5), SIBaseUnits.Meter);
        var small = new Quantity<FltX>(new FltX(2.25), SIBaseUnits.Meter);

        fraction.Floor().Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-12);
        fraction.Ceil().Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-12);
        fraction.Round().Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-12, "away from zero");
        negative.Round().Value.ToFlt64().ToDouble().Should().BeApproximately(-3.0, 1e-12, "away from zero");
        small.Round().Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-12);
    }

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>构造千克量 / Build a mass in kilograms.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) => new(new Flt64(value), SIBaseUnits.Kilogram);
}
