// 执行器编译约束的判据 / Criteria for the executor compilation constraint.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **缺编译表达式必须失败，不能让它在该执行器上一条要求都没有** / A missing compiled expression must fail rather than leave that executor with no requirement at all.
/// </summary>
/// <remarks>
/// 缺一个执行器就意味着该执行器在模型里**没有任何编译要求**，求解器可以让它一列都不选，
/// 而排程结果看起来完全正常：所有任务都排上了，只是某个班次一个人都没上。
/// **这种缺陷不在目标值上留痕迹，只让实际产出低于计划。**
///
/// A missing executor means it has **no compilation requirement** in the model, so the solver may select no column for it while the schedule looks entirely normal: every task
/// assigned, one shift simply using nobody. **The defect leaves no trace in the objective; only output falls short of plan.**
/// </remarks>
public class ExecutorCompilationConstraintTests {
    /// **全部执行器都有表达式时成功** / Success when every executor has an expression.
    [Fact]
    public void Invoke_ShouldSucceedWhenEveryExecutorHasAnExpression() {
        var executors = new List<TestExecutor> { Executor("E1"), Executor("E2") };
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new ExecutorCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, Stub(), Expressions(executors));

        constraint.Invoke(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        constraint.MissingExpressions().Should().BeEmpty();
    }

    /// **缺一个即失败并指明是哪个** / One missing executor fails and names it.
    [Fact]
    public void Invoke_ShouldFailAndNameTheMissingExecutor() {
        var executors = new List<TestExecutor> { Executor("E1"), Executor("E2") };
        Dictionary<TestExecutor, object> expressions = Expressions(executors);
        expressions.Remove(executors[1]);

        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new ExecutorCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, Stub(), expressions);
        Try result = constraint.Invoke(model);

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Code.Should().Be(ErrorCode.ApplicationError);
        constraint.MissingExpressions().Should().ContainSingle().Which.Id.Should().Be("E2");
    }

    /// <summary>
    /// **部分缺失时报告的是第一个，而不是最后一个** / A partial gap reports the first missing executor, not the last.
    /// </summary>
    /// <remarks>
    /// 这条把"第一次缺失就失败"钉死：若实现改成"收集全部缺失再一起报"，在只有一处缺失的判据上完全看不出差别。
    /// 判据要求同时缺 `E2` 与 `E4`，并断言消息里**有 `E2` 且没有 `E4`**。
    ///
    /// This pins "fail on the first gap": an implementation that collects every gap and reports them together is indistinguishable on a fixture with only one gap. The
    /// criterion leaves both `E2` and `E4` missing and asserts the message **contains `E2` and not `E4`**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldReportTheFirstGapOnly() {
        var executors = new List<TestExecutor> { Executor("E1"), Executor("E2"), Executor("E3"), Executor("E4") };
        Dictionary<TestExecutor, object> expressions = Expressions(executors);
        expressions.Remove(executors[1]);
        expressions.Remove(executors[3]);

        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new ExecutorCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, Stub(), expressions);
        Try result = constraint.Invoke(model);

        string message = ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message;
        message.Should().Contain("E2");
        message.Should().NotContain("E4", "reporting every gap at once would hide which one the iteration stops at");
    }

    /// **空执行器列表成功且无需任何表达式** / An empty executor list succeeds without any expressions.
    [Fact]
    public void Invoke_ShouldSucceedVacuouslyOnAnEmptyExecutorList() {
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new ExecutorCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            new List<TestExecutor>(), Stub(), new Dictionary<TestExecutor, object>());

        constraint.Invoke(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        constraint.MissingExpressions().Should().BeEmpty();
    }

    /// <summary>
    /// **表未接线时不检查** / No check is made while the table is not wired.
    /// </summary>
    /// <remarks>
    /// 表达式表由 `ICompilation` 提供，而 C# 的 `ICompilation` **还没有暴露这张表**——所以真实调用方一律传 `null`。
    /// 这里如实记录：`null` 时无从检查，也就没有守卫。
    /// 判据的作用是**把这条边界钉住**，避免日后有人把 `null` 当成"空表"从而让真实流程开始失败。
    ///
    /// The expression table comes from `ICompilation`, and C#'s `ICompilation` **does not expose it yet** — so every real caller passes `null`. This records that honestly:
    /// with `null` there is nothing to check, hence no guard. The criterion **pins this boundary** so nobody later treats `null` as "an empty table" and breaks real flows.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldSkipTheCheckWhileTheTableIsNotWired() {
        var executors = new List<TestExecutor> { Executor("E1") };
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new ExecutorCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, Stub());

        constraint.Invoke(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        constraint.MissingExpressions().Should().BeEmpty();
    }

    /// **空表（非 `null`）意味着每个执行器都缺表达式** / An empty non-null table means every executor is missing.
    [Fact]
    public void Invoke_ShouldFailOnAnEmptyButWiredTable() {
        var executors = new List<TestExecutor> { Executor("E1") };
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new ExecutorCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, Stub(), new Dictionary<TestExecutor, object>());

        constraint.Invoke(model).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        constraint.MissingExpressions().Should().ContainSingle().Which.Id.Should().Be("E1");
    }

    /// **缺失列表按执行器顺序** / The missing list follows the executor order.
    [Fact]
    public void MissingExpressions_ShouldFollowTheExecutorOrder() {
        var executors = new List<TestExecutor> { Executor("E3"), Executor("E1"), Executor("E2") };
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new ExecutorCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, Stub(), new Dictionary<TestExecutor, object>());

        IReadOnlyList<TestExecutor> missing = constraint.MissingExpressions();
        missing.Should().HaveCount(3);
        missing[0].Id.Should().Be("E3", "a reordered report would make the first-gap message depend on map iteration order");
        missing[1].Id.Should().Be("E1");
        missing[2].Id.Should().Be("E2");
    }

    /// <summary>
    /// **表未显式给出时从编译结果取** / The table is read from the compilation when not given explicitly.
    /// </summary>
    /// <remarks>
    /// 这张表的正当来源就是编译结果。若约束只认构造参数，每个调用方都得自己复制一份，
    /// 而复制漏了执行器的调用方**不会报错**——它只是少了一条约束。
    ///
    /// The table's proper source is the compilation. If the constraint accepted only the constructor argument, every caller would have to duplicate it, and a caller that
    /// misses an executor **gets no error** — just one fewer constraint.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldReadTheTableFromTheCompilation() {
        var executors = new List<TestExecutor> { Executor("E1"), Executor("E2") };

        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new ExecutorCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, FullCompilation(executors));

        constraint.Invoke(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        constraint.MissingExpressions().Should().BeEmpty();
    }

    /// **编译结果的表缺项时同样失败** / A gap in the compilation's own table fails too.
    [Fact]
    public void Invoke_ShouldFailWhenTheCompilationTableHasAGap() {
        var executors = new List<TestExecutor> { Executor("E1"), Executor("E2") };
        StubCompilation<TestExecutor> partial = FullCompilation(new List<TestExecutor> { executors[0] });

        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new ExecutorCompilationConstraint<TestExecutor, TestAssignmentPolicy>(executors, partial);

        constraint.Invoke(model).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        constraint.MissingExpressions().Should().ContainSingle().Which.Id.Should().Be("E2");
    }

    /// <summary>
    /// **表在 `Invoke` 时才读，构造时快照会永久冻结"还没接线"** / The table is read at `Invoke`; a construction-time snapshot freezes "not wired yet" forever.
    /// </summary>
    /// <remarks>
    /// 真实流程里约束**先构造、后注册**：管线列表在聚合注册之前生成，那时 `executor_compilation` 表还是空的。
    /// 构造时快照下来，等于把"还没接线"冻结成"一个执行器都没有"，于是每个执行器都报"编译表达式缺失"——
    /// 报错指向编译层，而真正的毛病是读表太早。这条判据把"延迟读"钉住。
    ///
    /// In the real flow the constraint is **constructed before registration**: the pipeline list is generated ahead of the aggregation's registration, when the
    /// `executor_compilation` table is still empty. Snapshotting at construction freezes "not wired yet" into "no executor at all", so every executor reports a missing compilation
    /// expression — the error points at the compilation layer while the actual fault is reading the table too early. This criterion pins the lazy read.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldReadTheTableAtInvokeTimeNotAtConstruction() {
        var executors = new List<TestExecutor> { Executor("E1") };
        var compilation = new LateWiredCompilation<TestExecutor>();
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Fuookami.Ospf.Math.Algebra.Number.Flt64>();
        var constraint = new ExecutorCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, compilation);

        // 构造之后才接线：快照实现会把这一步永久错过。
        // Wired only after construction: a snapshotting implementation misses it forever.
        constraint.Invoke(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>(
            "an unwired table means there is nothing to check yet");
        compilation.CompilationTable = new Dictionary<TestExecutor, object>();
        constraint.Invoke(model).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
            "once wired, the gap must fail");
        compilation.CompilationTable = Expressions(executors);
        constraint.Invoke(model).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>构造执行器 / Build an executor.</summary>
    /// <param name="id">身份 / The identity.</param>
    /// <returns>执行器 / The executor.</returns>
    private static TestExecutor Executor(string id) => new(id, id);

    /// <summary>构造一个占位编译结果 / Build a placeholder compilation.</summary>
    /// <returns>编译结果 / The compilation.</returns>
    private static StubCompilation Stub() => new();

    /// <summary>构造带表的编译结果 / Build a compilation carrying a table.</summary>
    /// <param name="executors">填进表的执行器 / The executors placed in the table.</param>
    /// <returns>编译结果 / The compilation.</returns>
    private static StubCompilation<TestExecutor> FullCompilation(IReadOnlyList<TestExecutor> executors) => new(executors);

    /// <summary>构造覆盖全部执行器的表达式映射 / Build an expression map covering every executor.</summary>
    /// <param name="executors">执行器 / The executors.</param>
    /// <returns>映射 / The map.</returns>
    private static Dictionary<TestExecutor, object> Expressions(IReadOnlyList<TestExecutor> executors) {
        var map = new Dictionary<TestExecutor, object>();
        foreach (TestExecutor executor in executors) {
            map[executor] = new object();
        }
        return map;
    }
}

/// <summary>
/// 可在构造之后接线的编译结果桩 / A compilation double whose table can be wired after construction.
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type.</typeparam>
public sealed class LateWiredCompilation<E> : Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model.ICompilation,
    Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model.IExecutorCompilation<E>
    where E : Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.Executor {
    /// <summary>执行器编译表；`null` 表示还没接线 / The executor-compilation table; null means not wired yet.</summary>
    public IReadOnlyDictionary<E, object>? CompilationTable { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// 还没接线时返回 `null`——**那正是"表不存在"这个语义**，约束据此跳过检查。返回空表会让"还没接线"变成"一个执行器都没有"，
    /// 两种状态在检查逻辑里必须分开。
    ///
    /// Returns `null` while unwired — that **is** the "no table" semantic, and the constraint skips the check on it. Returning an empty table would turn "not wired yet" into
    /// "no executor at all"; the two states must stay distinct in the checking logic.
    /// </remarks>
    public IReadOnlyDictionary<E, object> ExecutorCompilation => CompilationTable!;

    /// <inheritdoc/>
    public bool TaskCancelEnabled => false;

    /// <inheritdoc/>
    public bool WithExecutorLeisure => false;

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Register(object model) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
