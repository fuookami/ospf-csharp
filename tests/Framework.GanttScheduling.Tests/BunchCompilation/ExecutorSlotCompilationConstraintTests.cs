// 执行器-时隙编译约束的判据 / Criteria for the executor-slot compilation constraint.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;

/// <summary>
/// **表达式缺失必须报错，不能跳过** / A missing expression must be an error, never skipped.
/// </summary>
/// <remarks>
/// 这条约束要求"每个执行器在每个时隙恰好选一列"。某组合缺表达式时若静默跳过，
/// 那个执行器在该时隙就**没有任何覆盖要求**，求解器可以让它空着，而排程结果看起来完全正常：
/// 所有任务都排上了，只是某个班次少用了一个人。**这种缺陷不在目标值上留痕迹，只让实际产出低于计划**。
///
/// This constraint requires "exactly one column per executor and slot". Silently skipping a pair that lacks an expression removes every coverage requirement for that executor
/// in that slot, so the solver may leave it idle while the schedule looks entirely normal: every task assigned, one shift simply using one fewer person. **The defect leaves no
/// trace in the objective; it only makes actual output fall short of plan.**
/// </remarks>
public class ExecutorSlotCompilationConstraintTests {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// **全部组合都有表达式时成功** / Success when every pair has an expression.
    [Fact]
    public void Invoke_ShouldSucceedWhenEveryPairHasAnExpression() {
        var executors = new List<TestExecutor> { Executor("E1"), Executor("E2") };
        var slots = new List<TimeRange> { Slot(0, 8), Slot(8, 16) };
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new ExecutorSlotCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, slots, Full(executors, slots));

        constraint.Invoke(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        constraint.MissingExpressions().Should().BeEmpty();
    }

    /// **缺一个组合即失败,并指明缺的是哪一个** / One missing pair fails and names which.
    [Fact]
    public void Invoke_ShouldFailAndNameTheMissingPair() {
        var executors = new List<TestExecutor> { Executor("E1"), Executor("E2") };
        var slots = new List<TimeRange> { Slot(0, 8), Slot(8, 16) };
        Dictionary<(TestExecutor, TimeRange), object> partial = Full(executors, slots);
        partial.Remove((executors[1], slots[1]));

        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new ExecutorSlotCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, slots, partial);
        Try result = constraint.Invoke(model);

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.ApplicationError);
        constraint.MissingExpressions().Should().ContainSingle();
        constraint.MissingExpressions()[0].Executor.Id.Should().Be("E2");
    }

    /// <summary>
    /// **不静默跳过缺失项** / Missing pairs are not silently skipped.
    /// </summary>
    /// <remarks>
    /// 判据的核心不是"返回了失败"，而是**没有任何组合被放过**。一个"跳过缺失继续做"的实现会返回成功，
    /// 于是这条判据必须断言结果类型是 `Failed`——而它的价值在于说明"部分施加约束"是更危险的失败模式。
    ///
    /// What matters is not "a failure was returned" but that **no pair was let through**. A "skip and continue" implementation returns success, so the criterion must assert
    /// the `Failed` type — and its value is in naming "partially applied constraints" as the more dangerous failure mode.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldNeverApplyAPartialConstraintSet() {
        var executors = new List<TestExecutor> { Executor("E1") };
        var slots = new List<TimeRange> { Slot(0, 8), Slot(8, 16) };
        Dictionary<(TestExecutor, TimeRange), object> partial = Full(executors, slots);
        partial.Remove((executors[0], slots[0]));

        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new ExecutorSlotCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, slots, partial);

        constraint.Invoke(model)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
                "leaving the model partially constrained is worse than failing outright");
    }

    /// **空执行器或空时隙时无需任何表达式** / No expressions are required with an empty executor or slot list.
    [Fact]
    public void Invoke_ShouldSucceedVacuouslyOnAnEmptyAxis() {
        var executors = new List<TestExecutor> { Executor("E1") };

        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var noSlots = new ExecutorSlotCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, new List<TimeRange>(), new Dictionary<(TestExecutor, TimeRange), object>());
        noSlots.Invoke(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        var noExecutors = new ExecutorSlotCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            new List<TestExecutor>(),
            new List<TimeRange> { Slot(0, 8) },
            new Dictionary<(TestExecutor, TimeRange), object>());
        noExecutors.Invoke(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// **影子价格键区分执行器与时隙** / The shadow price key distinguishes executor and slot.
    [Fact]
    public void ShadowPriceKey_ShouldDistinguishBothComponents() {
        TestExecutor first = Executor("E1");
        TestExecutor second = Executor("E2");
        TimeRange morning = Slot(0, 8);
        TimeRange evening = Slot(8, 16);

        var a = new ExecutorSlotCompilationShadowPriceKey<TestExecutor>(first, morning);
        var b = new ExecutorSlotCompilationShadowPriceKey<TestExecutor>(first, evening);
        var c = new ExecutorSlotCompilationShadowPriceKey<TestExecutor>(second, morning);

        a.Should().NotBe(b, "the same executor in a different slot is a different key");
        a.Should().NotBe(c, "a different executor in the same slot is a different key");
        a.Should().Be(new ExecutorSlotCompilationShadowPriceKey<TestExecutor>(first, morning));
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

    /// <summary>构造覆盖全部组合的表达式映射 / Build an expression map covering every pair.</summary>
    /// <param name="executors">执行器 / The executors.</param>
    /// <param name="slots">时隙 / The slots.</param>
    /// <returns>映射 / The map.</returns>
    private static Dictionary<(TestExecutor, TimeRange), object> Full(
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
