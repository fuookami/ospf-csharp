// 容器拟合检查的判据 / Criteria for the container fit check.

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Infra;

/// <summary>
/// **量纲不符算"放不下"** / A dimension mismatch means "does not fit".
/// </summary>
/// <remarks>
/// 比较在量纲不同时返回 `null`。把它当"不知道"会让一次单位配错悄悄放行一个更大的货物，
/// 而错误要到后续几何校验才暴露——那时已离配置处很远。所以 `null` 必须归入 `false`。
///
/// The comparison returns `null` when dimensions differ. Treating that as "unknown" would silently let an oversized item through on any unit mix-up, and the error would
/// only surface in a later geometric check, far from where it was configured. So `null` must fold into `false`.
/// </remarks>
public class ContainerGeometryTests {
    /// **完全放得下时通过** / It fits when every extent is within the container.
    [Fact]
    public void Enabled_ShouldAcceptExtentsWithinTheContainer() {
        IContainer3Geometry<Flt64> container = Container(10.0, 10.0, 10.0);

        container.Enabled(Metres(5.0), Metres(5.0), Metres(5.0)).Should().BeTrue();
    }

    /// **等号也算放得下** / Equality also fits.
    [Fact]
    public void Enabled_ShouldAcceptExactFit() {
        IContainer3Geometry<Flt64> container = Container(10.0, 10.0, 10.0);

        container.Enabled(Metres(10.0), Metres(10.0), Metres(10.0))
            .Should().BeTrue("a snug fit is still a fit");
    }

    /// <summary>
    /// **只有第三个维度超出时必须拒绝** / An over-size on the third dimension alone must be rejected.
    /// </summary>
    /// <remarks>
    /// 这条专门打**最后一个分量**：只检查前两个分量的实现会在 `宽度`、`高度` 都合法时直接返回 `true`，
    /// 于是深度超限被完全跳过。
    ///
    /// This targets the **last** component: an implementation checking only the first two would return `true` whenever width and height are fine, skipping an over-size
    /// in depth entirely.
    /// </remarks>
    [Fact]
    public void Enabled_ShouldRejectAnOversizeOnTheThirdDimensionOnly() {
        IContainer3Geometry<Flt64> container = Container(10.0, 10.0, 10.0);

        container.Enabled(Metres(5.0), Metres(5.0), Metres(11.0)).Should().BeFalse();
    }

    /// <summary>
    /// **量纲不符时拒绝而不是放行** / A dimension mismatch is rejected rather than allowed.
    /// </summary>
    /// <remarks>
    /// 输入是"三个数都更小、但其中一个是质量"。数值上一切都"放得下"，只有量纲说着不行。
    /// 若实现把 `null` 当"不知道"→ 放行，这条就会失败。
    ///
    /// The input has three numerically smaller values, one of which is a mass. Numerically everything "fits"; only the dimension says otherwise. An implementation that
    /// treats `null` as "unknown" and allows it would fail here.
    /// </remarks>
    [Fact]
    public void Enabled_ShouldRejectAMassAgainstALength() {
        IContainer3Geometry<Flt64> container = Container(10.0, 10.0, 10.0);

        container.Enabled(Metres(1.0), Metres(1.0), Kilograms(1.0))
            .Should().BeFalse("a mass cannot be compared against a length, so it cannot be shown to fit");
    }

    /// **二维形状没有拟合检查，不臆造一个** / The 2D shape carries no fit check, and none is invented.
    [Fact]
    public void QuantityContainer2Shape_ShouldStayAMeasurementOnly() {
        var shape = new QuantityContainer2Shape<Flt64>(Metres(4.0), Metres(3.0), new ProjectivePlane.Bottom());

        shape.Length.Value.ToDouble().Should().BeApproximately(4.0, 1e-12);
        shape.Width.Value.ToDouble().Should().BeApproximately(3.0, 1e-12);
        typeof(QuantityContainer2Shape<Flt64>).GetMethod("Enabled").Should().BeNull(
            "the baseline's 2D fit check lives in the bpp2d module, not here");
    }

    /// <summary>构造容器 / Build a container.</summary>
    /// <param name="width">宽 / Width.</param>
    /// <param name="height">高 / Height.</param>
    /// <param name="depth">深 / Depth.</param>
    /// <returns>容器 / The container.</returns>
    private static QuantityContainer3Shape<Flt64> Container(double width, double height, double depth) =>
        new(MetresFor(width), MetresFor(height), MetresFor(depth));

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => MetresFor(value);

    /// <summary>构造米量（内部复用）/ Build a length in metres (shared body).</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> MetresFor(double value) => new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>构造千克量 / Build a mass in kilograms.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) => new(new Flt64(value), SIBaseUnits.Kilogram);
}
