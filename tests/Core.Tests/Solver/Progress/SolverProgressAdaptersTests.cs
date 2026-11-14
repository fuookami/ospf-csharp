#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Progress;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Progress;

/// <summary>
/// 进度适配器必须**保留原回调**，且**上报失败时不得继续调用原回调**。
/// The progress adapters must **preserve the original callback**, and **must not invoke it when reporting fails**.
/// </summary>
/// <remarks>
/// B324 移植了核心类型（"怎么上报"），这批移植适配器（"从哪里接"）。**没有适配器，进度上报存在但无人调用**——
/// 与 B313"可达 ≠ 在用"是同一类问题。
///
/// 两条最要紧的语义：
/// 1. **保留原回调**：接入进度上报**不得让既有回调失效**。
/// 2. **`Try.then` 的短路**：基线的 `Try.then` 只在 `Ok` 时执行 `next`，
///    所以**上报失败时原回调不会被调用**——否则一次上报失败会让下游回调在错误的时机触发。
///
/// B324 ported the core types ("how to report"); this batch ports the adapters ("where to hook in"). **Without adapters progress reporting exists but nothing calls it** —
/// the same class of problem as B313's "reachable ≠ in use".
///
/// Two critical semantics: (1) **preserve the original callback** — wiring in progress reporting **must not disable existing callbacks**; (2) **`Try.then`'s
/// short-circuit** — the baseline's `Try.then` only runs `next` on `Ok`, so **when reporting fails the original callback is not invoked**; otherwise a failed
/// report would fire downstream callbacks at the wrong moment.
/// </remarks>
public class SolverProgressAdaptersTests {
    private sealed class RecordingReporter : IProgressReporter {
        public List<SolverProgressSnapshot> Snapshots { get; } = new();

        public bool Fail { get; init; }

        public Result<Success, ErrorCode, Error<ErrorCode>> Report(SolverProgressSnapshot snapshot) {
            Snapshots.Add(snapshot);
            return Fail
                ? Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "reporter failed"))
                : Results.Ok(Results.SuccessInstance);
        }
    }

    private static SolvingStatus MakeSolvingStatus(double gap) =>
        new(
            Solver: "test-solver",
            SolverIndex: 0,
            SolverConfig: new SolverConfig(),
            Gap: new Flt64(gap)
        );

    /// <summary>**求解回调适配后必须仍然调用原回调** / The solving adapter must still invoke the original callback.</summary>
    [Fact]
    public void SolvingCallback_ShouldPreserveTheDelegate() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);
        int delegateCalls = 0;

        SolvingStatusCallBack adapted = context.SolvingCallback(
            SolverStages.Milp,
            _ => {
                ++delegateCalls;
                return Results.Ok(Results.SuccessInstance);
            });
        adapted(MakeSolvingStatus(0.1));

        delegateCalls.Should().Be(1, "wiring progress reporting must not disable the original callback");
        reporter.Snapshots.Should().ContainSingle();
    }

    /// <summary>**上报失败时不得调用原回调** / When reporting fails, the delegate must not be invoked.</summary>
    /// <remarks>
    /// 这是 `Try.then` 短路的直接后果。若实现无条件调用 `delegate`，
    /// **一次上报失败会让下游回调在错误的时机触发**——而"回调被调用了"这条单边判据不会发现。
    ///
    /// This follows directly from `Try.then`'s short-circuit. If the implementation called `delegate` unconditionally, **a failed report would fire downstream
    /// callbacks at the wrong moment** — and a one-sided "the callback was called" criterion would never notice.
    /// </remarks>
    [Fact]
    public void SolvingCallback_ShouldNotInvokeTheDelegateWhenReportingFails() {
        var reporter = new RecordingReporter { Fail = true };
        var context = new SolverProgressContext(reporter);
        int delegateCalls = 0;

        SolvingStatusCallBack adapted = context.SolvingCallback(
            SolverStages.Milp,
            _ => {
                ++delegateCalls;
                return Results.Ok(Results.SuccessInstance);
            });
        Result<Success, ErrorCode, Error<ErrorCode>> result = adapted(MakeSolvingStatus(0.1));

        result.IsFailed.Should().BeTrue();
        delegateCalls.Should().Be(0, "a failed report must short-circuit before the delegate");
    }

    /// <summary>**gap 为 0 时阶段内进度取 99** / With a zero gap, in-stage progress is 99.</summary>
    [Fact]
    public void SolvingCallback_ShouldUseNinetyNineWhenTheGapIsZero() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);

        context.SolvingCallback(SolverStages.Milp)(MakeSolvingStatus(0.0));

        reporter.Snapshots[0].ProgressInStage.Should().Be(99);
    }

    /// <summary>**总体进度按阶段分四档** / Overall progress has four bands by stage kind.</summary>
    /// <remarks>
    /// 基线四档系数：`MasterLP → 30 + p*4/10`、`ColumnGeneration → 60 + p*2/10`、
    /// `BranchAndPrice → 70 + p*2/10`、其余 `30 + p*7/10`。
    /// **四档必须逐个验证**——只验一档时，其余三档写错不会被发现。
    ///
    /// The baseline's four coefficients are `MasterLP → 30 + p*4/10`, `ColumnGeneration → 60 + p*2/10`, `BranchAndPrice → 70 + p*2/10`, and everything else
    /// `30 + p*7/10`. **All four must be verified** — checking only one leaves the other three free to be wrong.
    /// </remarks>
    [Fact]
    public void SolvingCallback_ShouldUseTheCorrectOverallBandPerStageKind() {
        var cases = new (SolverStage Stage, int Expected)[] {
            (SolverStages.MasterLp, 30 + (99 * 4 / 10)),
            (SolverStages.ColumnGeneration, 60 + (99 * 2 / 10)),
            (SolverStages.BranchAndPrice, 70 + (99 * 2 / 10)),
            (SolverStages.Milp, 30 + (99 * 7 / 10)),
        };

        foreach ((SolverStage stage, int expected) in cases) {
            var reporter = new RecordingReporter();
            var context = new SolverProgressContext(reporter);
            context.SolvingCallback(stage)(MakeSolvingStatus(0.0));
            reporter.Snapshots[0].OverallProgress.Should().Be(expected, "stage {0}", stage.Id.Value);
        }
    }

    /// <summary>**总量为 0 时百分比取 100** / With a zero total, the percentage is 100.</summary>
    /// <remarks>
    /// 基线 `percentage` 在 `totalValue == 0.0` 时返回 100，而不是 0——**总量为零意味着"已经全部完成"**。
    /// 这条在初始化瞬间触发，写成 0 会让进度条从 0 开始跳。
    ///
    /// The baseline's `percentage` returns 100 when `totalValue == 0.0`, not 0 — **a zero total means "everything is done"**. This fires at the first instant of
    /// initialisation; writing 0 makes the progress bar jump from zero.
    /// </remarks>
    [Fact]
    public void RegistrationCallback_ShouldReportFullProgressWhenTheTotalIsZero() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);

        context.RegistrationCallback()(
            new RegistrationStatus(
                ReadySymbolAmount: default,
                TotalSymbolAmount: default,
                EmptySymbolAmount: default
            ));

        reporter.Snapshots[0].ProgressInStage.Should().Be(100);
    }
}
