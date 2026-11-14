// 迭代上下文两阶段注册的判据 / Criteria for two-phase registration.

using System;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **第一步失败时第二步根本不执行** / Step two never runs when step one fails.
/// </summary>
/// <remarks>
/// 管线会在模型上追加约束。若第一步失败还继续跑第二步，约束会加到**一个只注册了一半的模型**上，
/// 而错误最终会以"模型不可解"的形式出现，**与真正的原因隔了好几层**。
///
/// 判据不能只断言"返回了失败"——那两种实现都满足。必须断言**第二步一次都没被调用**。
///
/// The pipelines append constraints to the model. Continuing after step one fails attaches those constraints to **a half-registered model**, and the error surfaces later as
/// "the model is infeasible", **several layers away from its actual cause**. The criterion cannot merely assert "a failure came back" — both implementations satisfy that. It
/// must assert **step two was never called at all**.
/// </remarks>
public class IterativeContextTests {
    /// **第一步失败，第二步不执行** / Step one fails and step two does not run.
    [Fact]
    public void Register_ShouldNotRunPipelinesWhenRegistrationFails() {
        int pipelineCalls = 0;

        Try result = IterativeContext.Register(
            Failed,
            () => {
                pipelineCalls++;
                return Ok();
            });

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        pipelineCalls.Should().Be(0, "the pipelines would constrain a half-registered model");
    }

    /// <summary>
    /// **第一步致命失败也原样返回且不执行第二步** / A fatal step one is returned unchanged and step two still does not run.
    /// </summary>
    /// <remarks>
    /// `Fatal` 与 `Failed` 是两种不同的失败：前者表示不可继续，后者表示这一步没成。
    /// 只处理 `Failed` 的实现会把 `Fatal` 当成成功继续跑管线——判据必须让第一步返回 `Fatal`。
    ///
    /// `Fatal` and `Failed` are different failures: the former means cannot continue, the latter means this step did not succeed. An implementation handling only `Failed`
    /// would treat `Fatal` as success and keep going — so the fixture must return `Fatal` from step one.
    /// </remarks>
    [Fact]
    public void Register_ShouldPassAFatalThroughWithoutRunningPipelines() {
        int pipelineCalls = 0;

        Try result = IterativeContext.Register(
            () => new Fatal<Success, ErrorCode, Error<ErrorCode>>(ErrorCode.ApplicationError, "fatal"),
            () => {
                pipelineCalls++;
                return Ok();
            });

        result.Should().BeOfType<Fatal<Success, ErrorCode, Error<ErrorCode>>>();
        pipelineCalls.Should().Be(0);
    }

    /// **第一步成功后第二步失败被传播** / A step-two failure is propagated after step one succeeds.
    [Fact]
    public void Register_ShouldPropagateAStepTwoFailure() {
        int pipelineCalls = 0;

        Try result = IterativeContext.Register(
            Ok,
            () => {
                pipelineCalls++;
                return Failed();
            });

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        pipelineCalls.Should().Be(1, "step two does run when step one succeeded");
    }

    /// **两步都成功才算成功** / Success requires both steps.
    [Fact]
    public void Register_ShouldSucceedWhenBothStepsSucceed() {
        Try result = IterativeContext.Register(Ok, Ok);

        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// **列数由任务数导出** / The column count is derived from the task count.
    [Theory]
    [InlineData(0, 0UL)]
    [InlineData(3, 3UL)]
    [InlineData(158, 158UL)]
    public void ColumnAmount_ShouldMirrorTheTaskCount(int taskCount, ulong expected) =>
        IterativeContext.ColumnAmount(taskCount).Should().Be(expected);

    /// **负任务数是缺陷而不是零** / A negative task count is a defect, not zero.
    [Fact]
    public void ColumnAmount_ShouldRejectANegativeTaskCount() {
        Action act = () => IterativeContext.ColumnAmount(-1);

        act.Should().Throw<ArgumentOutOfRangeException>("a negative count means the caller is already broken");
    }

    /// <summary>构造一个成功 / Build a success.</summary>
    /// <returns>成功 / Success.</returns>
    private static Try Ok() => Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>构造一个失败 / Build a failure.</summary>
    /// <returns>失败 / Failure.</returns>
    private static Try Failed() => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationError, "step failed"));
}
