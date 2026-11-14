#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

/// <summary>SCIP 行对偶后处理的契约测试，不依赖原生库。 / Contract tests for SCIP row-dual post-processing without the native library.</summary>
public class ScipLpDualSupportTest {
    [Fact]
    public void ToDualSolution_ShouldCoverEveryRowAndUseAPlaceholderWithoutOrigin() {
        var origin = new NamedConstraint("c1");
        MathConstraint?[] originMap = [null, origin];
        IReadOnlyDictionary<MathConstraint, Flt64> dualSolution = MathConstraintOrigins.ToDualSolution(
            rowDuals: [new Flt64(2), new Flt64(3)],
            originMap: originMap);

        Assert.Equal(2, dualSolution.Count);
        Assert.Equal(2, dualSolution[new FallbackMathConstraint(0)].ToDouble());
        Assert.Equal(3, dualSolution[origin].ToDouble());
        Assert.Equal("__fallback_0", dualSolution.Keys.First().Name);
    }

    [Fact]
    public void ToFarkasSolution_ShouldDropZerosAndRejectUnusableCertificates() {
        MathConstraint?[] originMap = [new NamedConstraint("c0"), new NamedConstraint("c1")];
        IReadOnlyDictionary<MathConstraint, Flt64> farkas = Unwrap(MathConstraintOrigins.ToFarkasSolution(
            farkasDuals: [Flt64.Zero, new Flt64(3)],
            originMap: originMap,
            backendName: "SCIP"));
        Assert.Single(farkas);
        Assert.Equal(3, farkas[originMap[1]!].ToDouble());

        Assert.Equal(
            ErrorCode.OREngineSolvingException,
            Assert.IsType<Failed<IReadOnlyDictionary<MathConstraint, Flt64>, ErrorCode, Error<ErrorCode>>>(
                MathConstraintOrigins.ToFarkasSolution([Flt64.Zero, Flt64.Zero], originMap, "SCIP")).Code);
        Assert.Equal(
            ErrorCode.OREngineSolvingException,
            Assert.IsType<Failed<IReadOnlyDictionary<MathConstraint, Flt64>, ErrorCode, Error<ErrorCode>>>(
                MathConstraintOrigins.ToFarkasSolution([Flt64.Zero, new Flt64(double.NaN)], originMap, "SCIP")).Code);
    }

    [Fact]
    public async Task RowDuals_ShouldKeepNativeValuesWhenStrongDualityHolds() {
        using LinearTriadModel model = Primal();
        var solver = new StubSolver(_ => throw new InvalidOperationException("不应求解对偶模型 / the dual model must stay unused"));
        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> result = await ScipLpDualSupport.RowDualsAsync(
            model: model,
            outcome: Outcome(rowDuals: [new Flt64(3), new Flt64(4)], objective: 11),
            dualSolver: solver,
            cancellationToken: default);

        Assert.Equal([3d, 4d], Unwrap(result).Select(value => value.ToDouble()));
        Assert.Equal(0, solver.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RowDuals_ShouldSolveTheDualModelWhenStrongDualityFails(int caseIndex) {
        using LinearTriadModel model = Primal();
        Flt64[] rowDuals = caseIndex == 0 ? [new Flt64(0), new Flt64(0)] : [new Flt64(9)];
        var solver = new StubSolver(_ => Results.Ok(new FeasibleSolverOutput<Flt64>(
            Obj: Flt64.Zero,
            Solution: new Solution<Flt64>([new Flt64(7), new Flt64(8), new Flt64(9)]),
            Time: TimeSpan.Zero,
            PossibleBestObj: Flt64.Zero,
            Gap: Flt64.Zero)));
        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> result = await ScipLpDualSupport.RowDualsAsync(
            model: model,
            outcome: Outcome(rowDuals: rowDuals, objective: 11),
            dualSolver: solver,
            cancellationToken: default);

        Assert.Equal([7d, 8d], Unwrap(result).Select(value => value.ToDouble()));
        Assert.Equal(1, solver.Calls);
        Assert.Equal("primal-dual", solver.Seen!.Name);
    }

    [Fact]
    public async Task RowDuals_ShouldPropagateDualModelFailure() {
        using LinearTriadModel model = Primal();
        var solver = new StubSolver(_ => Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(
            ErrorCode.ORModelUnbounded,
            "stub / stub")));
        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> result = await ScipLpDualSupport.RowDualsAsync(
            model: model,
            outcome: Outcome(rowDuals: [Flt64.Zero, Flt64.Zero], objective: 11),
            dualSolver: solver,
            cancellationToken: default);

        Assert.Equal(
            ErrorCode.ORModelUnbounded,
            Assert.IsType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>(result).Code);
    }

    /// <summary>两行两列的连续原模型：行右端值为 1 和 2。 / A two-row, two-column continuous primal with right-hand sides 1 and 2.</summary>
    private static LinearTriadModel Primal() => new(
        variables: [
            new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: new Flt64(double.PositiveInfinity),
                type: Continuous.Instance,
                name: "x0"),
            new ModelViewVariable(
                index: 1,
                lowerBound: new Flt64(double.NegativeInfinity),
                upperBound: new Flt64(double.PositiveInfinity),
                type: Continuous.Instance,
                name: "x1"),
        ],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([
                new SparseVector([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)]),
                new SparseVector([new SparseVectorEntry(1, new Flt64(2))]),
            ]),
            signs: [ConstraintRelation.LessEqual, ConstraintRelation.GreaterEqual],
            rhs: [Flt64.One, new Flt64(2)],
            names: ["c0", "c1"],
            sources: [ConstraintSource.Origin, ConstraintSource.Origin]),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: [new LinearObjectiveCell(0, new Flt64(3)), new LinearObjectiveCell(1, Flt64.One)]),
        name: "primal");

    private static ScipLpOutcome Outcome(Flt64[] rowDuals, double objective) => new(
        Report: new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = TerminationReason.Completed,
            SolutionPresence = SolutionPresence.Optimal,
        },
        Output: new FeasibleSolverOutput<Flt64>(
            Obj: new Flt64(objective),
            Solution: new Solution<Flt64>([Flt64.One, Flt64.One]),
            Time: TimeSpan.Zero,
            PossibleBestObj: new Flt64(objective),
            Gap: Flt64.Zero),
        RowDuals: rowDuals);

    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        Assert.IsType<Ok<T, ErrorCode, Error<ErrorCode>>>(result).Value;

    private sealed record NamedConstraint(string RowName) : MathConstraint {
        public IMetaConstraintGroup? Group => null;
        public bool Lazy => false;
        public object? Args => null;
        public int Priority => 0;
        public string Name => RowName;
        public string? DisplayName => null;
    }

    /// <summary>
    /// **后端名真的进了错误消息** / The backend name really reaches the error message.
    /// </summary>
    /// <remarks>
    /// **B1229 把四个后端的 `ToFarkasSolution` 合一，唯一按后端变化的参数是 `backendName`。**
    ///
    /// **这条判据存在的理由是：** **那个参数很容易变成"传了但没用"——
    /// 合并时最常见的退化是把 `$"{backendName} ..."` 写成固定字符串，而编译器不会抱怨，
    /// 因为参数确实被"接收"了。**
    ///
    /// **而后果是可诊断性的丧失：** **用户看到"未提供可用的 Farkas 证书"，却不知道是哪个后端。**
    /// **当四个后端共用一份实现时，这句话必须自己说出是谁。**
    ///
    /// **The only per-backend parameter in the merged `ToFarkasSolution` is `backendName`.**
    ///
    /// **This criterion exists because that parameter easily becomes "passed but unused": the common degradation when
    /// merging is to write the message as a fixed string, and the compiler will not complain, because the parameter is
    /// indeed accepted.**
    ///
    /// **The cost is diagnosability: the user sees "did not provide a usable Farkas certificate" with no idea which
    /// backend.** **When four backends share one implementation, that sentence has to say who.**
    /// </remarks>
    [Fact]
    public void TheFarkasFailureMessage_ShouldNameTheBackend() {
        MathConstraint?[] originMap = [new NamedConstraint("c0")];

        Failed<IReadOnlyDictionary<MathConstraint, Flt64>, ErrorCode, Error<ErrorCode>> failed =
            Assert.IsType<Failed<IReadOnlyDictionary<MathConstraint, Flt64>, ErrorCode, Error<ErrorCode>>>(
                MathConstraintOrigins.ToFarkasSolution(
                    [Flt64.Zero], originMap, backendName: "SomeBackend"));

        Assert.Contains("SomeBackend", failed.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>记录调用次数的对偶模型求解替身 / Dual-model solver stub counting calls.</summary>
    private sealed class StubSolver(
        Func<ILinearTriadModelView, Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> solve)
        : ILinearSolver {
        public string Name => "stub";

        public SolverConfig Config { get; } = new();

        public ILinearTriadModelView? Seen { get; private set; }

        public int Calls { get; private set; }

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) {
            Calls++;
            Seen = model;
            return Task.FromResult(solve(model));
        }

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            LinearTriadModel model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            IisConfig? iisConfig = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model,
            ulong solutionAmount,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
            LinearMechanismModel<Flt64> model,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
            LinearMetaModel<Flt64> model,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            MechanismModelDumpingStatusCallBack? dumpingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");
    }
}
