#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.service;

/// <summary>
/// `MaxRepeatCount` 不得**静默截断**合法结果，且**零宽不得死循环**。
/// `MaxRepeatCount` must not **silently truncate** legitimate results, and **a zero width must not loop forever**.
/// </summary>
/// <remarks>
/// 旧实现默认 `limit = 4096`，把手写的"防御性上限"当成了真实语义：
/// 1mm 宽、10000mm 可用时正确答案是 **10000**，旧实现返回 **4096**，**不报任何错**。
///
/// `computeMaxRepeatCount` **没有上限**，所以 4096 是一个真实的偏离。
/// 但**直接删掉上限会继承死循环**（宽度为零时逐次相加永不推进），所以改成显式零宽守卫。
///
/// 判据分两组：**不截断**（合法大数要算对）与**不死循环**（零宽要立即返回）。
///
/// The old implementation defaulted to `limit = 4096`, treating a hand-written "defensive cap" as real semantics: with a 1mm width and 10000mm available the correct
/// answer is **10000**, but it returned **4096** **without reporting anything**.
///
/// Its `computeMaxRepeatCount` has **no cap**, so 4096 was a real divergence. But **deleting the cap outright would inherit its infinite loop**
/// (with a zero width, repeated addition never advances), hence the explicit zero-width guard.
///
/// The criteria come in two groups: **no truncation** (large legitimate values must be exact) and **no infinite loop** (zero width must return immediately).
/// </remarks>
public class MaxRepeatCountBoundaryTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static IQuantityArithmetic<Flt64> Arithmetic() =>
        GenerationQuantityMath.Resolve(new Flt64(1.0))!;

    /// <summary>**合法大数不得被截断** / A large legitimate value must not be truncated.</summary>
    /// <remarks>
    /// 1mm 宽、10000mm 可用 → **10000**。旧实现返回 4096。
    /// **判据取 10000 而不是"大于 4096"**：后者对"返回 5000 也过"这种实现无效。
    ///
    /// A 1mm width with 10000mm available → **10000**. The old implementation returned 4096.
    /// **The criterion demands exactly 10000, not "greater than 4096"** — the latter would accept an implementation returning 5000.
    /// </remarks>
    [Fact]
    public void MaxRepeatCount_ShouldNotTruncateALegitimateLargeValue() {
        ulong count = GenerationQuantityMath.MaxRepeatCount(
            Millimetres(1.0), Millimetres(10000.0), Arithmetic());

        count.Should().Be(10000UL, "the old 4096 cap silently truncated this");
    }

    /// <summary>**零宽必须立即返回 0** / A zero width must return 0 immediately.</summary>
    /// <remarks>
    /// 零宽时逐次相加永不推进——**循环在这里会挂死**。C# 用显式守卫避开，
    /// 这是**有意改进**的一处：死循环不是需要保持的语义。
    ///
    /// 判据若挂住，说明守卫失效（xUnit 会超时失败，等价于检出）。
    ///
    /// With a zero width, repeated addition never advances — **a loop there would hang**. C# avoids it with an explicit guard, which is **deliberately
    /// better**: an infinite loop is not semantics worth preserving.
    ///
    /// If the criterion hangs, the guard is broken (xUnit fails on timeout, which counts as detected).
    /// </remarks>
    [Fact]
    public void MaxRepeatCount_ShouldReturnZeroForAZeroWidth() {
        ulong count = GenerationQuantityMath.MaxRepeatCount(
            Millimetres(0.0), Millimetres(100.0), Arithmetic());

        count.Should().Be(0UL);
    }

    /// <summary>**负宽必须返回 0** / A negative width must return 0.</summary>
    [Fact]
    public void MaxRepeatCount_ShouldReturnZeroForANegativeWidth() {
        ulong count = GenerationQuantityMath.MaxRepeatCount(
            Millimetres(-5.0), Millimetres(100.0), Arithmetic());

        count.Should().Be(0UL);
    }

    /// <summary>**恰好整除与不足一次两种边界** / The exact-division and fits-zero-times boundaries.</summary>
    [Fact]
    public void MaxRepeatCount_ShouldHandleTheExactAndInsufficientBoundaries() {
        GenerationQuantityMath.MaxRepeatCount(Millimetres(50.0), Millimetres(100.0), Arithmetic())
            .Should().Be(2UL, "100 / 50 is exactly 2");
        GenerationQuantityMath.MaxRepeatCount(Millimetres(50.0), Millimetres(49.0), Arithmetic())
            .Should().Be(0UL, "49 cannot fit a 50mm width");
        GenerationQuantityMath.MaxRepeatCount(Millimetres(50.0), Millimetres(50.0), Arithmetic())
            .Should().Be(1UL, "50 fits a 50mm width exactly once");
    }

    /// <summary>**显式上限仍然生效** / An explicit limit still takes effect.</summary>
    /// <remarks>
    /// 默认不截断**不等于取消上限**——调用方显式传 `limit` 时仍应被尊重。
    /// 这条钉住"参数还在"这一事实，防止实现把参数删掉。
    ///
    /// "No truncation by default" **does not mean the cap is gone** — an explicit `limit` from a caller must still be honoured. This pins the fact that the parameter
    /// still exists, preventing an implementation from deleting it.
    /// </remarks>
    [Fact]
    public void MaxRepeatCount_ShouldHonourAnExplicitLimit() {
        ulong count = GenerationQuantityMath.MaxRepeatCount(
            Millimetres(1.0), Millimetres(10000.0), Arithmetic(), limit: 7UL);

        count.Should().Be(7UL);
    }
}
