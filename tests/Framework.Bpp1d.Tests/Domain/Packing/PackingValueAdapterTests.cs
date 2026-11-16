// 求解器数值边界判据 / Criteria for the solver numeric boundary.

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.Packing;

/// <summary>
/// **求解器数值边界** / The solver numeric boundary.
/// </summary>
/// <remarks>
/// 对应基线的 `PackingValueAdapter`（在 `bpp/infrastructure/`，是 `bpp1d` 的前置依赖）。
/// 判据钉住三件事：
///
/// | 点 | 漏掉的后果 |
/// | --- | --- |
/// | **量纲不兼容返回 `Failed`** | 单位写错看起来像"系数为零"，静默污染模型 |
/// | **非有限值被拒** | `NaN`/`Inf` 进入求解器，结果是未定义行为 |
/// | **单位换算真的发生** | 1000 毫米当成 1000 米，模型量级错三个数量级 |
///
/// Mirrors the baseline's `PackingValueAdapter` (in `bpp/infrastructure/`, a prerequisite of `bpp1d`); the table lists what the criteria pin.
/// </remarks>
public class PackingValueAdapterTests {
    /// <summary>**单位换算真的发生** / The unit conversion really happens.</summary>
    /// <remarks>
    /// 1000 毫米归一化到米应是 **1**，不是 1000。若实现跳过换算，模型量级会错三个数量级——
    /// 而这种错误**不会让求解器报错**，只会给出一个"最优"的错解。
    ///
    /// 1000 mm normalised to metres must be **1**, not 1000. Skipping the conversion would put the model three orders of magnitude off, and **the solver would not
    /// complain** — it would just return a confidently wrong optimum.
    /// </remarks>
    [Fact]
    public void Quantity_ShouldConvertToTheRequestedUnit() {
        Result<Flt64, ErrorCode, Error<ErrorCode>> result =
            Flt64Adapter().Quantity(Millimetres(1000.0), SIBaseUnits.Meter);

        result.IsOk.Should().BeTrue(result.ToString());
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)result).Value.ToDouble().Should().BeApproximately(
            1.0, 1e-12, "1000 mm is 1 m; skipping the conversion would leave 1000");
    }

    /// <summary>**量纲不兼容必须失败，不返回零** / An incompatible dimension must fail, not return zero.</summary>
    /// <remarks>
    /// 把质量归一化到米：必须 `Failed`。**返回零是最坏的选择**——
    /// 那会让"单位写错"看起来像"这个系数本来就是零"，模型少一项约束而无人察觉。
    ///
    /// Normalising a mass to metres must fail. **Returning zero is the worst option**: it would make a wrong unit look like a genuinely zero coefficient, dropping a
    /// term from the model with nobody noticing.
    /// </remarks>
    [Fact]
    public void Quantity_ShouldFailOnAnIncompatibleDimension() {
        Result<Flt64, ErrorCode, Error<ErrorCode>> result =
            Flt64Adapter().Quantity(Grams(1000.0), SIBaseUnits.Meter);

        result.IsFailed.Should().BeTrue("a mass cannot be normalised to a length");
    }

    /// <summary>**非有限值必须被拒** / Non-finite values must be rejected.</summary>
    /// <param name="value">非有限值 / A non-finite value.</param>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Quantity_ShouldRejectNonFiniteValues(double value) {
        Result<Flt64, ErrorCode, Error<ErrorCode>> result =
            Flt64Adapter().Quantity(Millimetres(value), SIBaseUnits.Meter);

        result.IsFailed.Should().BeTrue("NaN and the infinities must never reach the solver");
    }

    /// <summary>**`FltX` 适配器往返保精度** / The `FltX` adapter round-trips.</summary>
    [Fact]
    public void FltXAdapter_ShouldRoundTrip() {
        IPackingValueAdapter<FltX> adapter = FltXPackingValueAdapter.Instance;
        var original = new FltX(2.5);

        Flt64 solver = adapter.ToSolver(original);
        FltX back = adapter.FromSolver(solver);

        back.Should().Be(original, "a lossless round-trip keeps the domain value unchanged");
    }

    /// <summary>
    /// 以接口类型取适配器 / Get the adapter through its interface type.
    /// </summary>
    /// <returns>适配器 / The adapter.</returns>
    /// <remarks>
    /// **必须走接口类型。** `Quantity` 是**默认接口方法**，在具体类上调用**不编译**——
    /// 与 的 `DiagnosticAnalyzers` 是同一类问题。
    ///
    /// **The interface type is required.** `Quantity` is a **default interface method** and calling it on the concrete class **does not compile** — the same problem as
    /// the earlier `DiagnosticAnalyzers`.
    /// </remarks>
    private static IPackingValueAdapter<Flt64> Flt64Adapter() => Flt64PackingValueAdapter.Instance;

    /// <summary>构造毫米量 / Build a millimetre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    /// <summary>构造克量 / Build a gram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Grams(double value) =>
        new(new Flt64(value), MassUnits.Gram);
}
