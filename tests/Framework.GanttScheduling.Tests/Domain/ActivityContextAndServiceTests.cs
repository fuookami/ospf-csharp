// 活动排程上下文与应用服务（B691）的判据 / Criteria for the activity context and application service (B691).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Framework.GanttScheduling.Application.Service.Activity;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Domain;

/// <summary>
/// **活动排程上下文与应用服务（B691）** / The activity context and application service (B691).
/// </summary>
/// <remarks>
/// 判据的重点：**命名空间非法给出具体错误**、**`Choices` 只含带 placement 的项**、
/// **无解不是失败**、**报告说有解却无数值是失败**。
///
/// The criteria focus on **an illegal namespace producing a concrete error**, on **`Choices` holding only entries that carry a placement**, on **no solution not being a failure**, and on **a report
/// claiming an incumbent without values being one**.
/// </remarks>
public class ActivityContextAndServiceTests {
    private static Quantity<Flt64> Q(double value) => new(new Flt64(value), SIBaseUnits.Meter);
    private static DateTimeOffset At(int hour) =>
        new(2026, 11, 9, hour, 0, 0, TimeSpan.FromHours(8.0));
    private static TimeRange Range(int startHour, int endHour) => new(At(startHour), At(endHour));

    private static ActivitySchedulingProblem<Flt64> Problem(bool withCancellation = true) {
        var use = new ActivityResourceUse<Flt64>("m1", Q(1.0));
        var mode = new ActivityMode<Flt64>("m", TimeSpan.FromHours(2.0),
            new List<DateTimeOffset> { At(8) }, new List<ActivityResourceUse<Flt64>> { use }, Q(10.0));
        return new ActivitySchedulingProblem<Flt64>(
            Horizon: Range(0, 23),
            Activities: new List<Activity<Flt64>> {
                new("a1", new List<ActivityMode<Flt64>> { mode },
                    CancellationCost: withCancellation ? Q(3.0) : null),
            },
            Resources: new List<ActivityResource<Flt64>> { new("m1", Q(8.0)) },
            Dependencies: null, Baselines: null, Executions: null, Rows: null,
            CostUnit: SIBaseUnits.Meter);
    }

    /// <summary>
    /// **命名空间非法给具体错误；`Choices` 只含带 placement 的项** / An illegal namespace gives a concrete error, and `Choices` holds only entries with a placement.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 命名空间以数字开头 → 失败，消息点名首字符。
    /// 2. 命名空间含 `-` → 失败，消息点名允许的字符。
    /// 3. 合法命名空间 → 成功，且 `Choices` **只有 1 项**（模式那一个），取消项不在其中。
    /// 4. 不给取消成本时 `Choices` 仍只有 1 项——取消项本来就不进候选。
    ///
    /// Four paired points: a namespace starting with a digit fails naming the first character; one containing `-` fails naming the allowed characters; a legal namespace succeeds with **exactly one**
    /// entry in `Choices` (the mode), the cancellation not among them; and without a cancellation cost there is still exactly one, since cancellations never enter the candidate list.
    /// </remarks>
    [Fact]
    public void Create_ShouldCheckTheNamespaceAndKeepOnlyPlaceableCandidates() {
        Result<ActivitySchedulingContext<Flt64>, ErrorCode, Error<ErrorCode>> digit =
            ActivitySchedulingContext<Flt64>.Create(Problem(), "0bad");
        digit.IsOk.Should().BeFalse();
        Message(digit).Should().Contain("first character");

        Result<ActivitySchedulingContext<Flt64>, ErrorCode, Error<ErrorCode>> dash =
            ActivitySchedulingContext<Flt64>.Create(Problem(), "bad-name");
        dash.IsOk.Should().BeFalse();
        Message(dash).Should().Contain("letters, digits and underscores");

        Result<ActivitySchedulingContext<Flt64>, ErrorCode, Error<ErrorCode>> ok =
            ActivitySchedulingContext<Flt64>.Create(Problem(), "good_name");
        ok.IsOk.Should().BeTrue();
        ok.Value.Choices.Should().ContainSingle("the cancellation is not a schedulable candidate");
        ok.Value.Choices[0].Key.Mode.Should().Be("m");

        Result<ActivitySchedulingContext<Flt64>, ErrorCode, Error<ErrorCode>> noCancel =
            ActivitySchedulingContext<Flt64>.Create(Problem(withCancellation: false), "good_name");
        noCancel.IsOk.Should().BeTrue();
        noCancel.Value.Choices.Should().ContainSingle();
    }

    /// <summary>
    /// **无解不是失败；有解无数值才是失败** / No solution is not a failure, but an incumbent without values is.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 求解器报不可行 → `SolveAsync` **成功**，`Schedule` 为 null。
    /// 2. 求解器报可行但解向量长度不符 → 失败，消息点名维度。
    /// 3. 求解器报可行且数值正确 → 成功，`Schedule` 非空。
    /// 4. `extra` 在注册**之后**调用：回调里能看到已注册的选择变量。
    ///
    /// Four paired points: an infeasible verdict makes `SolveAsync` **succeed** with a null `Schedule`; a feasible verdict whose vector has the wrong length fails with a message naming the
    /// dimension; a feasible verdict with correct values succeeds with a non-null `Schedule`; and `extra` runs **after** registration, so the callback can see the registered choice variables.
    /// </remarks>
    [Fact]
    public async global::System.Threading.Tasks.Task Solve_ShouldTreatNoSolutionAsSuccess() {
        var infeasible = new FakeSolver(FakeSolver.Mode.Infeasible);
        var service = new ActivitySchedulingApplicationService(infeasible);
        Result<ActivitySchedulingResult, ErrorCode, Error<ErrorCode>> none =
            await service.SolveAsync(Problem());
        none.IsOk.Should().BeTrue("an infeasible problem is an answer, not an error");
        none.Value.Schedule.Should().BeNull();
        none.Value.Report.SolutionPresence.Should().Be(SolutionPresence.None);

        var wrongLength = new FakeSolver(FakeSolver.Mode.WrongLength);
        Result<ActivitySchedulingResult, ErrorCode, Error<ErrorCode>> bad =
            await new ActivitySchedulingApplicationService(wrongLength).SolveAsync(Problem());
        bad.IsOk.Should().BeFalse();
        Message(bad).Should().Contain("dimension");

        var feasible = new FakeSolver(FakeSolver.Mode.Feasible);
        var service2 = new ActivitySchedulingApplicationService(feasible);
        int variablesSeen = -1;
        Result<ActivitySchedulingResult, ErrorCode, Error<ErrorCode>> good = await service2.SolveAsync(
            Problem(), (context, model) => {
                variablesSeen = model.Tokens.TokensInSolver.Count;
                return Results.Ok<Success>(Results.SuccessInstance);
            });
        good.IsOk.Should().BeTrue();
        good.Value.Schedule.Should().NotBeNull();
        variablesSeen.Should().Be(2,
            "the two choice columns are registered before the caller's extra modelling runs");
    }

    private static string Message<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result is Failed<T, ErrorCode, Error<ErrorCode>> failed ? failed.Error.Message : string.Empty;

    private sealed class FakeSolver : IColumnGenerationSolver {
        public enum Mode { Infeasible, WrongLength, Feasible }

        private readonly Mode _mode;

        public FakeSolver(Mode mode) => _mode = mode;

        public string Name => "fake";

        public global::System.Threading.Tasks.Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("the service goes through SolveMILPWithStatusAsync");

        public global::System.Threading.Tasks.Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            ulong amount,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("the service goes through SolveMILPWithStatusAsync");

        public global::System.Threading.Tasks.Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("the service goes through SolveMILPWithStatusAsync");

        public global::System.Threading.Tasks.Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("the service goes through SolveMILPWithStatusAsync");

        public global::System.Threading.Tasks.Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("the service goes through SolveMILPWithStatusAsync");

        public global::System.Threading.Tasks.Task<Result<IColumnGenerationSolver.MilpSolveResult, ErrorCode, Error<ErrorCode>>> SolveMILPWithStatusAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) {
            if (_mode == Mode.Infeasible) {
                return global::System.Threading.Tasks.Task.FromResult(Results.Ok<IColumnGenerationSolver.MilpSolveResult>(
                    new IColumnGenerationSolver.MilpSolveResult.Infeasible(
                        new LinearInfeasibleSolverOutput(SolverStatus.Infeasible))));
            }
            int count = metaModel.Tokens.TokensInSolver.Count;
            var values = new List<Flt64>();
            if (_mode == Mode.WrongLength) {
                values.Add(new Flt64(1.0));
            } else {
                for (int index = 0; index < count; index++) {
                    values.Add(new Flt64(index == 0 ? 1.0 : 0.0));
                }
            }
            return global::System.Threading.Tasks.Task.FromResult(Results.Ok<IColumnGenerationSolver.MilpSolveResult>(
                new IColumnGenerationSolver.MilpSolveResult.Feasible(new FeasibleSolverOutput<Flt64>(
                    Obj: new Flt64(10.0),
                    Solution: new Solution<Flt64>(values),
                    Time: TimeSpan.Zero,
                    PossibleBestObj: new Flt64(10.0),
                    Gap: new Flt64(0.0)))));
        }
    }
}
