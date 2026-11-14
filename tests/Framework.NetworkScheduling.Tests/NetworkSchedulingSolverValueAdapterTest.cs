// 网络调度数值适配测试 / Tests for the network-scheduling numeric adapter.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Tests;

/// <summary>
/// 网络调度数值适配 / Network-scheduling numeric adapter.
/// </summary>
public sealed class NetworkSchedulingSolverValueAdapterTest {
    /// <summary>Flt64 领域值转 solver 值 / An Flt64 domain value converts to a solver value.</summary>
    [Fact]
    public void Flt64Adapter_ShouldRoundTrip() {
        INetworkSchedulingSolverValueAdapter<Flt64> adapter = Flt64NetworkSchedulingSolverValueAdapter.Instance;

        adapter.ToSolverValue(new Flt64(3.5)).Value.ToDouble().Should().BeApproximately(3.5, 1e-9);
        adapter.FromSolverValue(new Flt64(3.5)).Value.ToDouble().Should().BeApproximately(3.5, 1e-9);
    }

    /// <summary>FltX 领域值转 solver 值 / An FltX domain value converts to a solver value.</summary>
    [Fact]
    public void FltXAdapter_ShouldConvert() =>
        FltXNetworkSchedulingSolverValueAdapter.Instance.ToSolverValue(new FltX(3.5m))
            .Value.ToDouble().Should().BeApproximately(3.5, 1e-9);

    /// <summary>solver 值转 FltX 领域值 / A solver value converts back to FltX.</summary>
    [Fact]
    public void FltXAdapter_ShouldConvertBack() =>
        FltXNetworkSchedulingSolverValueAdapter.Instance.FromSolverValue(new Flt64(3.5))
            .Value.ToFlt64().ToDouble().Should().BeApproximately(3.5, 1e-9);

    /// <summary>
    /// **NaN 被挡住**：`NaN` 进入求解器会污染整个目标函数，结果往往表现为"求解成功但方案荒谬"，比直接报错难查得多。
    /// **NaN is blocked**: it poisons the whole objective, and the result usually looks like "the solve succeeded but the plan
    /// is absurd" — far harder to diagnose than a straight failure.
    /// </summary>
    [Fact]
    public void Adapter_ShouldRejectNaN() {
        var nan = new Flt64(double.NaN);

        Flt64NetworkSchedulingSolverValueAdapter.Instance.ToSolverValue(nan).IsOk.Should().BeFalse();
        FltXNetworkSchedulingSolverValueAdapter.Instance.FromSolverValue(nan).IsOk.Should().BeFalse();
    }

    /// <summary>正无穷被挡住 / Positive infinity is blocked.</summary>
    [Fact]
    public void Adapter_ShouldRejectPositiveInfinity() =>
        Flt64NetworkSchedulingSolverValueAdapter.Instance.ToSolverValue(new Flt64(double.PositiveInfinity))
            .IsOk.Should().BeFalse();

    /// <summary>
    /// 负无穷被挡住。**`FltX` 是十进制类型，无法表示无穷**，因此这条只能用 `Flt64` 适配器验证——这不是遗漏，是类型能力边界。
    /// Negative infinity is blocked. **`FltX` is a decimal type and cannot represent infinity**, so this can only be checked
    /// through the `Flt64` adapter — a type-capability boundary, not an omission.
    /// </summary>
    [Fact]
    public void Adapter_ShouldRejectNegativeInfinity() =>
        Flt64NetworkSchedulingSolverValueAdapter.Instance.FromSolverValue(new Flt64(double.NegativeInfinity))
            .IsOk.Should().BeFalse();

    /// <summary>同单位归一化保持数值 / Normalizing to the same unit keeps the value.</summary>
    [Fact]
    public void Normalize_ShouldKeepSameUnit() {
        INetworkSchedulingSolverValueAdapter<FltX> adapter = FltXNetworkSchedulingSolverValueAdapter.Instance;

        adapter.Normalize(Meters(3), SIBaseUnits.Meter).Value.ToDouble().Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>可换算单位归一化后数值改变 / A convertible unit changes the value after normalization.</summary>
    [Fact]
    public void Normalize_ShouldConvertUnit() {
        INetworkSchedulingSolverValueAdapter<FltX> adapter = FltXNetworkSchedulingSolverValueAdapter.Instance;

        adapter.Normalize(Centimeters(300), SIBaseUnits.Meter).Value.ToDouble()
            .Should().BeApproximately(3.0, 1e-9, "300cm = 3m");
    }

    /// <summary>
    /// **量纲不兼容被拒绝**：米与秒不能换算，静默接受会让求解器拿到一个无意义的数。
    /// **Incompatible dimensions are rejected**: metres and seconds are inconvertible, and accepting silently would hand the
    /// solver a meaningless number.
    /// </summary>
    [Fact]
    public void Normalize_ShouldRejectDimensionMismatch() {
        INetworkSchedulingSolverValueAdapter<FltX> adapter = FltXNetworkSchedulingSolverValueAdapter.Instance;

        adapter.Normalize(Meters(3), SIBaseUnits.Second).IsOk.Should().BeFalse();
    }

    /// <summary>
    /// **`NormalizeValue` 保留领域类型而不进入 solver 精度**：ESPRC 等领域计算需要 V 的精度，过早降到 Flt64 会累积误差。
    /// **`NormalizeValue` keeps the domain type and never drops to solver precision**: domain calculations such as ESPRC need
    /// `V`'s precision, and an early drop to Flt64 accumulates error.
    /// </summary>
    [Fact]
    public void NormalizeValue_ShouldKeepDomainType() {
        INetworkSchedulingSolverValueAdapter<FltX> adapter = FltXNetworkSchedulingSolverValueAdapter.Instance;
        FltX value = adapter.NormalizeValue(Centimeters(300), SIBaseUnits.Meter).Value;

        value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>量纲不兼容时 `NormalizeValue` 也拒绝 / `NormalizeValue` also rejects a dimension mismatch.</summary>
    [Fact]
    public void NormalizeValue_ShouldRejectDimensionMismatch() {
        INetworkSchedulingSolverValueAdapter<FltX> adapter = FltXNetworkSchedulingSolverValueAdapter.Instance;

        adapter.NormalizeValue(Meters(3), SIBaseUnits.Second).IsOk.Should().BeFalse();
    }

    /// <summary>有限值校验通过 / A finite value passes the check.</summary>
    [Fact]
    public void Finite_ShouldAcceptFinite() {
        INetworkSchedulingSolverValueAdapter<Flt64> adapter = Flt64NetworkSchedulingSolverValueAdapter.Instance;

        adapter.Finite(new Flt64(1.0)).IsOk.Should().BeTrue();
    }

    /// <summary>非有限值校验失败 / A non-finite value fails the check.</summary>
    [Fact]
    public void Finite_ShouldRejectNonFinite() {
        INetworkSchedulingSolverValueAdapter<Flt64> adapter = Flt64NetworkSchedulingSolverValueAdapter.Instance;

        adapter.Finite(new Flt64(double.NaN)).IsOk.Should().BeFalse();
    }

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>构造以厘米为单位的量 / Build a quantity in centimetres.</summary>
    private static Quantity<FltX> Centimeters(double value) => new(new FltX(value), LengthUnits.Centimeter);
}
