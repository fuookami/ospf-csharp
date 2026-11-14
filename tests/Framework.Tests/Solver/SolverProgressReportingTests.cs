// 求解进度上报（B657）的判据 / Criteria for solve-progress reporting (B657).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Progress;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Solver;

/// <summary>
/// **求解进度上报（B657）** / Solve-progress reporting (B657).
/// </summary>
/// <remarks>
/// 判据的重点：**求解前后各报一次**（0% 与 100%）、**失败时不报 100%**（进度停在中途）、
/// 以及**没有进度上下文时行为完全不变**。
///
/// The criteria focus on **one report before and one after the solve** (0% and 100%), on **no 100% report on failure** (the bar stays mid-way), and on **behaviour being unchanged without a
/// progress context**.
/// </remarks>
public class SolverProgressReportingTests {
    private static FeasibleSolverOutput<Flt64> Output() =>
        new(
            Obj: new Flt64(1.0),
            Solution: new Solution<Flt64>(new[] { new Flt64(1.0) }),
            Time: TimeSpan.Zero,
            PossibleBestObj: new Flt64(1.0),
            Gap: Flt64.Zero);

    /// <summary>记录所有上报的快照 / Records every reported snapshot.</summary>
    private sealed class RecordingReporter : IProgressReporter
    {
        public List<SolverProgressSnapshot> Snapshots { get; } = new();

        public Result<Success, ErrorCode, Error<ErrorCode>> Report(SolverProgressSnapshot snapshot) {
            Snapshots.Add(snapshot);
            return Results.Ok<Success>(Results.SuccessInstance);
        }
    }

    /// <summary>只实现被测路径的最小求解器 / A minimal solver implementing only the path under test.</summary>
    private sealed class MinimalSolver : IColumnGenerationSolver
    {
        private readonly bool _succeeds;

        public MinimalSolver(bool succeeds) => _succeeds = succeeds;

        public string Name => "minimal";

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Task.FromResult(_succeeds
                ? Results.Ok<FeasibleSolverOutput<Flt64>>(Output())
                : Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, "solver crashed")));

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name, LinearMetaModel<Flt64> metaModel, ulong amount, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("not under test");

        public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("not under test");

        public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name, LinearMetaModel<Flt64> metaModel, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("not under test");

        public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("not under test");
    }

    /// <summary>
    /// **成功时报 0% 与 100%，诊断带模型名与求解器名** / Success reports 0% and 100%, with the model and solver in the diagnostics.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 成功 → **至少两次上报**，第一次 0%、最后一次 100%。
    /// 2. **诊断里有模型名与求解器名**——多求解器并行时只有百分比无法区分谁在跑。
    /// 3. 上报的阶段是 `Milp`。
    /// 4. **没有进度上下文时结果不变**（同一个求解器，成功仍是成功）。
    ///
    /// Four paired points: success yields **at least two reports**, the first at 0% and the last at 100%; **the diagnostics carry the model and solver names**, since a bare percentage cannot
    /// distinguish concurrent solvers; the stage is `Milp`; and **without a progress context the result is unchanged**, the same solver still succeeding.
    /// </remarks>
    [Fact]
    public async Task Solve_ShouldReportZeroAndHundredOnSuccess() {
        using var model = new LinearMetaModel<Flt64>("myModel");
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);
        var solver = new MinimalSolver(succeeds: true);

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await ((IColumnGenerationSolver)solver).SolveMILPAsync(model, new FrameworkSolveOptions { ProgressContext = context });

        solved.IsOk.Should().BeTrue();
        reporter.Snapshots.Should().HaveCountGreaterThanOrEqualTo(2, "one report before and one after the solve");
        reporter.Snapshots[0].ProgressInStage.Should().Be(0);
        reporter.Snapshots[0].OverallProgress.Should().Be(30, "the pre-solve report is not at the top");
        reporter.Snapshots[^1].ProgressInStage.Should().Be(100);
        reporter.Snapshots[^1].OverallProgress.Should().Be(100);
        reporter.Snapshots[^1].Stage.Should().Be(SolverStages.Milp);
        reporter.Snapshots[^1].Diagnostics.Should().NotBeNull();
        reporter.Snapshots[^1].Diagnostics!["model"].Should().Be("myModel");
        reporter.Snapshots[^1].Diagnostics!["solver"].Should().Be("minimal");

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> withoutContext =
            await ((IColumnGenerationSolver)solver).SolveMILPAsync(model, new FrameworkSolveOptions());
        withoutContext.IsOk.Should().BeTrue("no progress context changes nothing about the result");
    }

    /// <summary>
    /// **失败时不报 100%** / No 100% report on failure.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 求解失败 → **进度停在 0%，没有 100% 上报**。
    /// 2. 调用方能从"进度没走完"判断这次没跑完，而不是看到一个假的完成。
    /// 3. **失败原因不变**：进度上报不影响错误透传。
    ///
    /// Three paired points: a failed solve **leaves the progress at 0% with no 100% report**; a caller reads an incomplete bar as an incomplete solve rather than a false completion; and
    /// **the failure reason is unchanged**, since progress reporting does not affect error propagation.
    /// </remarks>
    [Fact]
    public async Task Solve_ShouldNotReportHundredOnFailure() {
        using var model = new LinearMetaModel<Flt64>("myModel");
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);
        var solver = new MinimalSolver(succeeds: false);

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await ((IColumnGenerationSolver)solver).SolveMILPAsync(model, new FrameworkSolveOptions { ProgressContext = context });

        solved.IsOk.Should().BeFalse();
        ((Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)solved)
            .Error.Message.Should().Be("solver crashed");
        reporter.Snapshots.Should().NotBeEmpty("the pre-solve report still happens");
        reporter.Snapshots.Should().OnlyContain(snapshot => snapshot.ProgressInStage == 0,
            "a failed solve must not claim completion");
    }
}
