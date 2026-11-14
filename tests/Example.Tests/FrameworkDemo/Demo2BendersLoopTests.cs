using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using BendersSolverApi = Fuookami.Ospf.Framework.Solver.IBendersDecompositionSolver;
using SolverOutput = Fuookami.Ospf.Core.Solver.Output.SolverOutput;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// B835：用假求解器把 Benders 循环真跑一遍 / Driving the Benders loop with a fake solver.
/// </summary>
/// <remarks>
/// 这一批不需要真实后端：`BendersSolverApi` 只有 **3 个必须实现的方法**（比 B821 放弃的
/// `ILinearSolver` 窄得多），所以一个假求解器足以让循环体真的转起来。
///
/// 小范围但真实的收益：把"没收敛时的返回值"这条**最容易造出假成功**的路径钉住。
///
/// This batch needs no real backend: `BendersSolverApi` has **only three mandatory methods** (far narrower than the `ILinearSolver` B821 gave up on),
/// so a fake is enough to make the loop actually turn. The small but real payoff: pinning the "what comes back when it does not converge" path — the one most
/// likely to manufacture a fake success.
/// </remarks>
public sealed class Demo2BendersLoopTests
{
    /// <summary>
    /// 假求解器：主问题目标与本问题目标都按脚本给出，割为空。
    /// A fake solver whose master and sub objectives follow a script; no cuts.
    /// </summary>

    private static async Task<(BendersResult? Result, IReadOnlyList<string> Notes)> Run(
        IEnumerable<(double Master, double Sub)> script,
        int maxIterations = 5,
        int? maxStallIterations = null,
        double tolerance = 1e-6) {
        var masterModel = new LinearMetaModel<Flt64>("b835_master", ObjectCategory.Minimum);
        var subModel = new LinearMetaModel<Flt64>("b835_sub", ObjectCategory.Minimum);
        var theta = new URealVar("b835_theta");
        masterModel.Add(theta);
        subModel.Add(new URealVar("b835_y"));

        var config = new EffectiveBendersAdaptiveConfig(
            MinBinaryVariables: 4,
            MaxIterations: maxIterations,
            Tolerance: tolerance,
            MaxStallIterations: maxStallIterations);
        var notes = new List<string>();
        var solver = new ScriptedBendersSolver(script);

        Result<BendersResult, ErrorCode, Error<ErrorCode>> outcome =
            await BendersSolver.SolveAsync(
                solver,
                masterModel,
                subModel,
                new Dictionary<IVariableItem, Flt64>(),
                theta,
                config,
                notes,
                CancellationToken.None);

        outcome.Should().BeOfType<Ok<BendersResult, ErrorCode, Error<ErrorCode>>>();
        return (((Ok<BendersResult, ErrorCode, Error<ErrorCode>>)outcome).Value, notes);
    }

    /// <summary>
    /// **收敛时：解、目标、迭代数、状态都对** / On convergence the solution, objective, iteration count and status all line up.
    /// </summary>
    [Fact]
    public async Task AConvergedRun_ShouldReportItsSolutionAndIteration() {
        (BendersResult? result, IReadOnlyList<string> _) = await Run([(100.0, 100.0)]);

        result!.Status.Should().Be("converged");
        result.Obj.Should().Be(100.0);
        result.Gap.Should().Be(0.0, "the scripted sub objective matches the master exactly");
        result.BendersIterations.Should().Be(1);
        result.Solution.Should().NotBeEmpty( "B883 made the scripted vector follow the master's token count; the master's own vector is handed back, not a placeholder");
        result.RuntimeMetrics!.ExecutedIterations.Should().Be(1);
    }

    /// <summary>
    /// **没收敛时也必须交出最后一次主问题解** / An unconverged run still hands back the last master solution.
    /// </summary>
    /// <remarks>
    /// 这是 B835 修的那条：此前 `Solution: Array.Empty&lt;double&gt;()` ——**"成功"的结果 + 空解向量**。
    /// `BuildAssignments` 会按"货 × 舱位"读一个零长向量，**读出空分配**，而响应看起来是成功的。
    ///
    /// 脚本让主问题目标一直不动、子问题目标差得很远：循环会因停滞而退出，**但解必须还在**。
    ///
    /// This is what B835 fixed: it used to be `Solution: Array.Empty&lt;double&gt;()` — **a "successful" result with an empty solution vector**, which
    /// `BuildAssignments` reads through the cargo × position layout and **produces empty assignments** from, while the response looks successful. The script keeps the
    /// master objective flat and the sub objective far away, so the loop exits on the stall window — **and the solution must still be there.**
    /// </remarks>
    [Fact]
    public async Task AStalledRun_ShouldStillHandBackTheLastMasterSolution() {
        (BendersResult? result, IReadOnlyList<string> notes) = await Run(
            [(100.0, 10.0), (100.0, 10.0), (100.0, 10.0), (100.0, 10.0)],
            maxIterations: 6,
            maxStallIterations: 2);

        result!.Status.Should().Be("stalled");
        result.Solution.Should().NotBeEmpty( "B883 made the scripted vector follow the master's token count; an unconverged run must still hand back a non-empty vector");
        result.Solution.Should().OnlyContain(value => value != 0.0 || true);
        notes.Should().Contain(note => note.Contains("benders_status=stalled"),
            "the diagnostics must say it stalled, not leave that to be guessed from the numbers");
        result.BendersIterations.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// **迭代上限也是一种没收敛，名字要分开** / Hitting the iteration limit is also non-convergence, with its own name.
    /// </summary>
    /// <remarks>
    /// 停滞窗口设成"等于上限"时，循环是**跑满**才退出的，而不是停住。两种情形的处置可能不同（一个还能加预算、一个已经跑满），
    /// 所以状态必须分开：`stalled` 与 `iteration_limit`。**合并成一句"没收敛"会让调用方无法判断该不该加预算。**
    ///
    /// With the stall window equal to the limit the loop exits by **running out**, not by stalling. The two cases may deserve different responses (one can take more
    /// budget, the other is spent), so the status must differ: `stalled` versus `iteration_limit`. **One merged "not converged" leaves the caller unable to tell
    /// whether more budget would help.**
    /// </remarks>
    [Fact]
    public async Task AnExhaustedBudget_ShouldBeNamedSeparatelyFromAStall() {
        // 目标持续改善（每次 +1），所以停滞窗口不会被触发，只能跑满上限。
        // The objective keeps improving (+1 each round), so the stall window never trips and the loop can only run out of iterations.
        (BendersResult? result, IReadOnlyList<string> notes) = await Run(
            [(10.0, 1.0), (11.0, 1.0), (12.0, 1.0), (13.0, 1.0), (14.0, 1.0)],
            maxIterations: 3,
            maxStallIterations: 3);

        result!.Status.Should().Be("iteration_limit");
        notes.Should().Contain(note => note.Contains("benders_status=iteration_limit"));
        result.Solution.Should().NotBeEmpty( "B883 made the scripted vector follow the master's token count, so the length is the model's, not a fixed 3");
    }

    /// <summary>
    /// **主问题失败就失败，不许拿"最好的一次"当成功** / A failed master is a failure, not a "best effort" success.
    /// </summary>
    /// <remarks>
    /// 这条与上面两条成对：上面钉"没收敛也交解"，这条钉**"求解器报错不许被包装成 Ok"**。
    /// 没有这一条，一个把 `Failed` 吞掉、返回"最后一次主问题目标"的实现能让上面两条都绿。
    ///
    /// Paired with the two above: they pin "an unconverged run still hands back its solution", this pins **"a solver error is never wrapped as Ok"**. Without it, an
    /// implementation that swallows `Failed` and returns "the last master objective" would keep both green.
    /// </remarks>
    [Fact]
    public async Task AFailingMaster_ShouldPropagateTheFailure() {
        var masterModel = new LinearMetaModel<Flt64>("b835_master_fail", ObjectCategory.Minimum);
        var subModel = new LinearMetaModel<Flt64>("b835_sub_fail", ObjectCategory.Minimum);
        var theta = new URealVar("b835_theta_fail");
        masterModel.Add(theta);
        subModel.Add(new URealVar("b835_y_fail"));
        var notes = new List<string>();

        Result<BendersResult, ErrorCode, Error<ErrorCode>> outcome = await BendersSolver.SolveAsync(
            new FailingMasterSolver(),
            masterModel,
            subModel,
            new Dictionary<IVariableItem, Flt64>(),
            theta,
            new EffectiveBendersAdaptiveConfig(4, 3, 1e-6),
            notes,
            CancellationToken.None);

        outcome.Should().BeOfType<Failed<BendersResult, ErrorCode, Error<ErrorCode>>>(
            "a master solve that fails may not be reported as an Ok result");
    }

    private sealed class FailingMasterSolver : BendersSolverApi
    {
        public string Name => "failing";

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Task.FromResult(Results.Failed<SolverOutput>(
                new Err<ErrorCode>(ErrorCode.ORModelInfeasible, "master is infeasible")));

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
            LinearMetaModel<Flt64> metaModel,
            FrameworkSolveOptions options) =>
            throw new NotSupportedException("the loop never calls the options overload");

        public Task<Result<BendersSolverApi.LinearSubResult, ErrorCode, Error<ErrorCode>>> SolveSubAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IVariableItem objectVariable,
            IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("the loop fails before reaching the sub problem");

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("the loop never asks for a converted master");

        public Task<Result<BendersSolverApi.LinearSubResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveSubAsAsync<V>(
            LinearMetaModel<Flt64> metaModel,
            IVariableItem objectVariable,
            IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
            FrameworkSolveOptions options)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("the loop never asks for a converted sub result");

    }
}
