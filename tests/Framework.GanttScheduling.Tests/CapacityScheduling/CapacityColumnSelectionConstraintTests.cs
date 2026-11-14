// 产能列选择约束的判据 / Criteria for the capacity column selection constraint.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.CapacityScheduling;

/// <summary>
/// **组内缺表达式失败，组外缺表达式正常；同一键的影子价格累加** / In-group gaps fail, out-of-group gaps are fine, and prices accumulate per key.
/// </summary>
/// <remarks>
/// 两条规则方向相反，而它们的作用对象只差一个集合：
/// 把组当成全积会**强制**求解器给本该空着的组合塞一列，凭空占用产能；
/// 把组外也检查会把正常状态报成缺陷。
///
/// Two rules point opposite ways and differ only by one set:
/// treating the group as the full product **forces** the solver to fill combinations that should stay empty, consuming capacity out of thin air; checking outside the group
/// reports a normal state as a defect.
/// </remarks>
public class CapacityColumnSelectionConstraintTests {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// **组内全部有表达式时成功** / Success when every in-group pair has an expression.
    [Fact]
    public void Invoke_ShouldSucceedWhenTheGroupIsCovered() {
        var executors = new List<TestExecutor> { Executor("E1"), Executor("E2") };
        var slots = new List<TimeRange> { Slot(0, 8), Slot(8, 16) };
        var constraint = new CapacityColumnSelectionConstraint<TestExecutor>(
            executors, slots, Expressions(executors, slots));

        constraint.Invoke().Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        constraint.MissingExpressions().Should().BeEmpty();
        constraint.Groups.Should().HaveCount(4, "an explicit null group set means the full product");
    }

    /// **组内缺一个即失败** / One missing in-group pair fails.
    [Fact]
    public void Invoke_ShouldFailOnAnInGroupGap() {
        var executors = new List<TestExecutor> { Executor("E1") };
        var slots = new List<TimeRange> { Slot(0, 8), Slot(8, 16) };
        var groups = new HashSet<(TestExecutor, TimeRange)> { (executors[0], slots[0]), (executors[0], slots[1]) };

        var constraint = new CapacityColumnSelectionConstraint<TestExecutor>(
            executors,
            slots,
            new Dictionary<(TestExecutor, TimeRange), object> { [(executors[0], slots[0])] = new object() },
            groups);

        Try result = constraint.Invoke();

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Code.Should().Be(ErrorCode.ApplicationError);
        constraint.MissingExpressions().Should().ContainSingle();
    }

    /// <summary>
    /// **组外的组合缺表达式必须被跳过，不能报错** / An out-of-group pair without an expression is skipped, not reported.
    /// </summary>
    /// <remarks>
    /// 这条与上一条只差一个集合，结论却相反。判据必须让**组内全有、组外全无**：
    /// 若实现把组判成全积，这里会因为组外缺表达式而失败；若实现把"跳过组外"也写成"检查组外"，同样失败。
    ///
    /// This differs from the previous case by one set with the opposite conclusion. The fixture must have **everything inside the group and nothing outside**:
    /// an implementation treating the group as the full product fails here on the out-of-group gap, and so does one that "checks outside" instead of "skips outside".
    /// </remarks>
    [Fact]
    public void Invoke_ShouldSkipOutOfGroupPairsEntirely() {
        var executors = new List<TestExecutor> { Executor("E1"), Executor("E2") };
        var slots = new List<TimeRange> { Slot(0, 8) };
        var groups = new HashSet<(TestExecutor, TimeRange)> { (executors[0], slots[0]) };

        var constraint = new CapacityColumnSelectionConstraint<TestExecutor>(
            executors,
            slots,
            new Dictionary<(TestExecutor, TimeRange), object> { [(executors[0], slots[0])] = new object() },
            groups);

        constraint.Invoke().Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        constraint.MissingExpressions().Should().BeEmpty("E2 in that slot is not required to pick a column");
        constraint.Groups.Should().ContainSingle("the explicit group replaces the full product rather than extending it");
    }

    /// <summary>
    /// **同一键的影子价格必须累加，不能覆盖** / Prices for one key must accumulate, never overwrite.
    /// </summary>
    /// <remarks>
    /// 同一组合可能被多条约束共享。覆盖只保留**最后一条**的价格，而那条往往最不重要。
    /// 下游用这个价格做列定价，少算的价格会让本该入列的候选被跳过——**产出变差，但没有任何错误信息**。
    ///
    /// One pair can be shared by several constraints. Overwriting keeps only the **last** price, usually the least significant one. Downstream uses it for column pricing, so an
    /// under-counted price skips candidates that should enter the basis — **worse output, with no error message at all**.
    /// </remarks>
    [Fact]
    public void ExtractShadowPrices_ShouldAccumulateForTheSameKey() {
        TestExecutor executor = Executor("E1");
        TimeRange slot = Slot(0, 8);
        var key = new CapacityColumnSelectionShadowPriceKey<TestExecutor>(executor, slot);
        var other = new CapacityColumnSelectionShadowPriceKey<TestExecutor>(Executor("E2"), slot);

        IReadOnlyDictionary<CapacityColumnSelectionShadowPriceKey<TestExecutor>, Flt64> prices =
            CapacityColumnSelectionConstraint<TestExecutor>.ExtractShadowPrices(new List<(CapacityColumnSelectionShadowPriceKey<TestExecutor>, Flt64)> {
                (key, new Flt64(1.5)),
                (other, new Flt64(2.0)),
                (key, new Flt64(3.5)),
            });

        prices.Should().HaveCount(2);
        prices[key].ToDouble().Should().BeApproximately(5.0, 1e-12, "1.5 and 3.5 share the key and must sum");
        prices[other].ToDouble().Should().BeApproximately(2.0, 1e-12);
    }

    /// **影子价格键区分执行器与时隙** / The shadow price key distinguishes executor and slot.
    [Fact]
    public void ShadowPriceKey_ShouldDistinguishBothComponents() {
        TestExecutor first = Executor("E1");
        TestExecutor second = Executor("E2");
        TimeRange morning = Slot(0, 8);
        TimeRange evening = Slot(8, 16);

        var a = new CapacityColumnSelectionShadowPriceKey<TestExecutor>(first, morning);

        a.Should().NotBe(new CapacityColumnSelectionShadowPriceKey<TestExecutor>(first, evening));
        a.Should().NotBe(new CapacityColumnSelectionShadowPriceKey<TestExecutor>(second, morning));
        a.Should().Be(new CapacityColumnSelectionShadowPriceKey<TestExecutor>(first, morning));
    }

    /// <summary>构造执行器 / Build an executor.</summary>
    /// <param name="id">身份 / The identity.</param>
    /// <returns>执行器 / The executor.</returns>
    private static TestExecutor Executor(string id) => new(id, id);

    /// <summary>构造时隙 / Build a time slot.</summary>
    /// <param name="startHour">开始小时 / The start hour.</param>
    /// <param name="endHour">结束小时 / The end hour.</param>
    /// <returns>时隙 / The time slot.</returns>
    private static TimeRange Slot(int startHour, int endHour) =>
        new(Origin.AddHours(startHour), Origin.AddHours(endHour));

    /// <summary>构造覆盖全积的表达式映射 / Build an expression map covering the full product.</summary>
    /// <param name="executors">执行器 / The executors.</param>
    /// <param name="slots">时隙 / The slots.</param>
    /// <returns>映射 / The map.</returns>
    private static Dictionary<(TestExecutor, TimeRange), object> Expressions(
        IReadOnlyList<TestExecutor> executors,
        IReadOnlyList<TimeRange> slots) {
        var map = new Dictionary<(TestExecutor, TimeRange), object>();
        foreach (TestExecutor executor in executors) {
            foreach (TimeRange slot in slots) {
                map[(executor, slot)] = new object();
            }
        }
        return map;
    }
}
