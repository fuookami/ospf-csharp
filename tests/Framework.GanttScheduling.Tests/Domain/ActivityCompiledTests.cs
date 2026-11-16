// 编译后的活动模型的判据 / Criteria for the compiled activity model.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Domain;

/// <summary>
/// **编译后的活动模型** / The compiled activity model.
/// </summary>
/// <remarks>
/// 判据的重点：**变量索引就是选择顺序且成本同源**、**产能优先级不可换**
/// （不可用 → 0，其次分时段，最后总产能）。
///
/// The criteria focus on **the variable index being the choice order with the cost from the same source**, and on **the capacity precedence being fixed**: unavailable yields zero, then the
/// per-period value, then the overall one.
/// </remarks>
public class ActivityCompiledTests {
    private static TimeRange Range(int startHour, int endHour) =>
        new(new DateTimeOffset(2026, 11, 9, startHour, 0, 0, TimeSpan.FromHours(8.0)),
            new DateTimeOffset(2026, 11, 9, endHour, 0, 0, TimeSpan.FromHours(8.0)));
    private static DateTimeOffset At(int hour) =>
        new(2026, 11, 9, hour, 0, 0, TimeSpan.FromHours(8.0));
    private static double D(Flt64 value) => value.ToFlt64().ToDouble();
    private static Quantity<Flt64> Q(double value) => new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>
    /// **变量索引就是选择顺序，成本同源** / The variable index is the choice order, with costs from the same source.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 三个选择 → **三个变量**，名字带命名空间与序号（`ns_choice_0/1/2`）。
    /// 2. 成本多项式的三个单项式系数就是 `5 / 7 / 9`，常数项为 0。
    /// 3. 第 `i` 个单项式的变量**就是第 `i` 个变量**（引用相等）——索引一处定，两处用。
    /// 4. 成本数与选择数不符 → 抛（不能在编译期悄悄错位）。
    ///
    /// Four paired points: three choices give **three variables** named with the namespace and ordinal (`ns_choice_0/1/2`); the cost polynomial's three coefficients are `5`, `7` and `9` with a zero
    /// constant; the i-th monomial's variable **is the i-th variable** (reference-equal), one index fixed and used twice; and a cost count not matching the choice count throws rather than letting
    /// a silent misalignment through.
    /// </remarks>
    [Fact]
    public void Compilation_ShouldKeepIndexAndCostInStep() {
        var compilation = new ActivityCompilation(3, new[] { new Flt64(5.0), new Flt64(7.0), new Flt64(9.0) }, "ns");

        compilation.Variables.Should().HaveCount(3);
        compilation.Variables[0].Name.Should().Contain("ns_choice_0");
        compilation.Variables[2].Name.Should().Contain("ns_choice_2");

        compilation.Cost.Monomials.Should().HaveCount(3);
        D(compilation.Cost.Constant).Should().BeApproximately(0.0, 1e-12);
        D(compilation.Cost.Monomials[0].Coefficient).Should().BeApproximately(5.0, 1e-12);
        D(compilation.Cost.Monomials[1].Coefficient).Should().BeApproximately(7.0, 1e-12);
        D(compilation.Cost.Monomials[2].Coefficient).Should().BeApproximately(9.0, 1e-12);
        ReferenceEquals(compilation.Cost.Monomials[1].Symbol, compilation.Variables[1])
            .Should().BeTrue("the i-th monomial must use the i-th variable, not a copy");

        // **必须钉住是哪条守卫拒的**：数组越界也是 `ArgumentException` 的子类，
        // 只断言"抛了 ArgumentException"会被"越界"这个副作用蒙混过去。
        // **Pin which guard rejects it**: an array bounds error is also an `ArgumentException` subclass, so merely asserting "an ArgumentException was thrown" would be satisfied by that side effect.
        Action mismatch = () => new ActivityCompilation(2, new[] { new Flt64(1.0) }, "ns");
        mismatch.Should().Throw<ArgumentException>()
            .Which.Message.Should().Contain("one entry per choice", "the count guard must fire, not the array bound");
    }

    /// <summary>
    /// **产能优先级不可换：不可用 → 分时段 → 总产能** / The capacity precedence is fixed: unavailable, then per-period, then overall.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 某时刻落在**不可用时段**内 → `0`，即使同一时刻也有分时段产能（不可用优先）。
    /// 2. 落在**分时段产能**内 → 该值（`4`）。
    /// 3. 都不覆盖 → 回落到**总产能**（`8`）。
    /// 4. 时段是**左闭右开**：`10` 点整仍在 `[8,10)` 之外，因此拿到总产能。
    ///
    /// Four paired points: an instant inside an **unavailable range** yields `0` even when a per-period capacity also covers it, since unavailability wins; an instant inside a **per-period
    /// capacity** yields that value (`4`); one covered by neither falls back to the **overall capacity** (`8`); and ranges are **half-open**, so exactly `10` lies outside `[8,10)` and takes the
    /// overall capacity.
    /// </remarks>
    [Fact]
    public void CapacityAt_ShouldRespectThePrecedence() {
        var source = new ActivityResource<Flt64>(
            Id: "m1",
            Capacity: Q(8.0),
            UnavailableTimes: new[] { Range(8, 9) },
            Capacities: new[] { new ActivityCapacity<Flt64>(Range(8, 10), Q(4.0)) });
        var compiled = new CompiledActivityResource<Flt64>(
            Source: source,
            Capacity: new Flt64(8.0),
            Capacities: new[] { new KeyValuePair<TimeRange, Flt64>(Range(8, 10), new Flt64(4.0)) });

        D(compiled.CapacityAt(At(8))).Should().BeApproximately(0.0, 1e-12,
            "unavailability beats a per-period capacity covering the same instant");
        D(compiled.CapacityAt(At(9))).Should().BeApproximately(4.0, 1e-12,
            "9 lies in the per-period window and after the unavailable one");
        D(compiled.CapacityAt(At(10))).Should().BeApproximately(8.0, 1e-12,
            "10 is outside [8,10), so the overall capacity applies");
        D(compiled.CapacityAt(At(6))).Should().BeApproximately(8.0, 1e-12);
    }
}
