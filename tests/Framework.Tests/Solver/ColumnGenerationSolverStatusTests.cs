// 带状态的求解结果的判据 / Criteria for the status-carrying solve results.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Solver;

/// <summary>
/// **带状态的求解结果** / The status-carrying solve results.
/// </summary>
/// <remarks>
/// 判据的重点：**可行与不可行是两个分支**、**失败不是 `Infeasible`**、以及**转换真的发生**
/// （只构造结果类型测不到转换逻辑，这是 第一版判据的错误）。
///
/// The criteria focus on **feasible and infeasible being two branches**, on **a failure not being `Infeasible`**, and on **the conversion actually happening**: constructing the result types alone
/// does not exercise it, which was the flaw's first criterion.
/// </remarks>
public class ColumnGenerationSolverStatusTests {
    private static FeasibleSolverOutput<Flt64> Output() =>
        new(
            Obj: new Flt64(1.0),
            Solution: new Solution<Flt64>(new[] { new Flt64(2.0), new Flt64(3.0) }),
            Time: TimeSpan.Zero,
            PossibleBestObj: new Flt64(1.0),
            Gap: Flt64.Zero);

    /// <summary>
    /// 只实现这里要测的两个方法的最小求解器 / A minimal solver implementing only the two methods under test.
    /// </summary>
    /// <remarks>
    /// **其余成员抛 `NotSupportedException`**：将来某个测试误用它们会立刻炸，而不是拿到假数据。
    /// 为满足接口写一堆"返回空"的桩，正是最该避免的——空壳会让人以为接口已完整。
    ///
    /// **Every other member throws `NotSupportedException`**, so a future test that reaches one fails loudly instead of receiving fake data. Filling the interface with "return empty" stubs is
    /// exactly what to avoid, since a hollow shell reads as a complete interface.
    /// </remarks>
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

        public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Task.FromResult(_succeeds
                ? Results.Ok<IColumnGenerationSolver.LpResult>(
                    new IColumnGenerationSolver.LpResult(Output(), new Dictionary<MathConstraint, Flt64>()))
                : Results.Failed<IColumnGenerationSolver.LpResult>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, "solver crashed")));

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

        public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("not under test");
    }

    /// <summary>
    /// **可行被包成 `Feasible`，失败原样透传** / Success is wrapped as `Feasible` and failures pass through.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 成功的 MILP → `Feasible` 分支，**解向量读得到**。
    /// 2. 成功的 LP → `Feasible` 分支，**结果读得到**。
    /// 3. 失败的 MILP → **`Result` 失败，不是 `Infeasible` 分支**。
    /// 4. 失败的 LP → 同样。
    ///
    /// Four paired points: a successful MILP yields the `Feasible` branch **with a readable solution**; a successful LP likewise **with a readable result**; a failed MILP yields **a failed `Result`
    /// rather than the `Infeasible` branch**; and a failed LP likewise.
    /// </remarks>
    [Fact]
    public async Task WithStatus_ShouldWrapFeasibleAndPassFailuresThrough() {
        using var model = new LinearMetaModel<Flt64>("m");
        var succeeding = new MinimalSolver(succeeds: true);

        Result<IColumnGenerationSolver.MilpSolveResult, ErrorCode, Error<ErrorCode>> milp =
            await ((IColumnGenerationSolver)succeeding).SolveMILPWithStatusAsync("m", model);
        milp.IsOk.Should().BeTrue();
        IColumnGenerationSolver.MilpSolveResult milpValue = ((Ok<IColumnGenerationSolver.MilpSolveResult, ErrorCode, Error<ErrorCode>>)milp).Value;
        milpValue.Should().BeOfType<IColumnGenerationSolver.MilpSolveResult.Feasible>();
        ((IColumnGenerationSolver.MilpSolveResult.Feasible)milpValue).Output.Solution.Values
            .Should().HaveCount(2, "the feasible branch carries the solution");

        Result<IColumnGenerationSolver.LpResultWithStatus, ErrorCode, Error<ErrorCode>> lp =
            await ((IColumnGenerationSolver)succeeding).SolveLPWithStatusAsync("m", model);
        lp.IsOk.Should().BeTrue();
        IColumnGenerationSolver.LpResultWithStatus lpValue = ((Ok<IColumnGenerationSolver.LpResultWithStatus, ErrorCode, Error<ErrorCode>>)lp).Value;
        lpValue.Should().BeOfType<IColumnGenerationSolver.LpResultWithStatus.Feasible>();
        ((IColumnGenerationSolver.LpResultWithStatus.Feasible)lpValue).Result.Solution
            .Should().HaveCount(2);

        var failing = new MinimalSolver(succeeds: false);
        Result<IColumnGenerationSolver.MilpSolveResult, ErrorCode, Error<ErrorCode>> failedMilp =
            await ((IColumnGenerationSolver)failing).SolveMILPWithStatusAsync("m", model);
        failedMilp.IsOk.Should().BeFalse("a solve error is not the infeasible branch");
        ((Failed<IColumnGenerationSolver.MilpSolveResult, ErrorCode, Error<ErrorCode>>)failedMilp)
            .Error.Message.Should().Be("solver crashed");

        Result<IColumnGenerationSolver.LpResultWithStatus, ErrorCode, Error<ErrorCode>> failedLp =
            await ((IColumnGenerationSolver)failing).SolveLPWithStatusAsync("m", model);
        failedLp.IsOk.Should().BeFalse();
        ((Failed<IColumnGenerationSolver.LpResultWithStatus, ErrorCode, Error<ErrorCode>>)failedLp)
            .Error.Message.Should().Be("solver crashed");
    }

    /// <summary>
    /// **`Infeasible` 分支没有解可读** / The `Infeasible` branch has no solution to read.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. `Infeasible` 的载荷是不可行输出，**没有解向量**——不可行时解不存在，用空解顶替会被当成零解读。
    /// 2. 两个分支运行时类型不同，`is` 能分开。
    /// 3. 两个分支载荷类型不同，编译器就挡住误用。
    ///
    /// Three paired points: `Infeasible` carries the infeasible output and **no solution vector**, since an infeasible model has no solution and an empty stand-in would read as all zeros; the
    /// runtime types differ so an `is` check separates them; and the payload types differ so the compiler blocks misuse.
    /// </remarks>
    [Fact]
    public void Infeasible_ShouldCarryNoSolution() {
        var infeasible = new IColumnGenerationSolver.MilpSolveResult.Infeasible(
            new LinearInfeasibleSolverOutput(Status: SolverStatus.Infeasible));
        infeasible.Should().BeAssignableTo<IColumnGenerationSolver.MilpSolveResult>();
        infeasible.Should().NotBeOfType<IColumnGenerationSolver.MilpSolveResult.Feasible>();
        infeasible.Output.Status.Should().Be(SolverStatus.Infeasible);

        var lpInfeasible = new IColumnGenerationSolver.LpResultWithStatus.Infeasible(
            new LinearInfeasibleSolverOutput(Status: SolverStatus.Infeasible));
        lpInfeasible.Should().NotBeOfType<IColumnGenerationSolver.LpResultWithStatus.Feasible>();
    }
}
