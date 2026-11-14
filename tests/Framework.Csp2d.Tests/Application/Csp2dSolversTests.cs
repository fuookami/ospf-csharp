// 求解入口判据（桩求解器驱动）/ Criteria for the solver entries, driven by a stub solver.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp2d.Application.Model;
using Fuookami.Ospf.Framework.Csp2d.Application.Service;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using IColumnGenerationSolver = Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Application;

/// <summary>
/// **两条求解入口的装配与收敛** / Assembly and convergence of the two solver entries.
/// </summary>
/// <remarks>
/// 用**桩求解器**驱动：判据关心的是"装配→求解→读解→恢复"这条链是否接对，而不是求解质量。
/// 真实 SCIP 的端到端判据留到带集成的批次——**桩能证明接线，不能证明求解**。
///
/// Driven by a **stub solver**: the criteria check that "assemble, solve, read back, recover" is wired, not the solve quality. A real-SCIP end-to-end criterion
/// belongs to an integration batch — **a stub proves the wiring, not the solving**.
/// </remarks>
public class Csp2dSolversTests {
    /// <summary>**MILP 路径一次求解并读回使用次数** / The MILP path solves once and reads the usages back.</summary>
    [Fact]
    public async Task Milp_ShouldSolveOnceAndReadBackUsages() {
        Result<Csp2dSolution, ErrorCode, Error<ErrorCode>> result =
            await new Csp2dMilp(new StubSolver()).SolveAsync(ProblemOf());

        result.IsOk.Should().BeTrue();
        Csp2dSolution solution = result.ValueOrThrow();
        solution.Iterations.Should().Be(1, "the MILP path is a single solve");
        solution.SheetCount.Should().Be(solution.Usages.Count,
            "every usage read back as one sheet");
        solution.Coverage.Should().NotBeEmpty();
    }

    /// <summary>**问题不合法时先校验再求解** / An invalid problem is validated before any solve.</summary>
    [Fact]
    public async Task Milp_ShouldValidateTheProblemFirst() {
        Csp2dProblem invalid = ProblemOf() with { Demands = new List<ProductDemand2<Flt64>>() };

        Result<Csp2dSolution, ErrorCode, Error<ErrorCode>> result =
            await new Csp2dMilp(new StubSolver()).SolveAsync(invalid);

        result.IsFailed.Should().BeTrue("an empty demand set is not a solvable problem");
        ((Failed<Csp2dSolution, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain("没有需求");
    }

    /// <summary>**求解器失败时原样向上传递** / A solver failure is propagated unchanged.</summary>
    [Fact]
    public async Task Milp_ShouldPropagateASolverFailure() {
        Result<Csp2dSolution, ErrorCode, Error<ErrorCode>> result =
            await new Csp2dMilp(new StubSolver(fail: true)).SolveAsync(ProblemOf());

        result.IsFailed.Should().BeTrue("swallowing a solver failure would hide an infeasible model");
        ((Failed<Csp2dSolution, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain("桩求解器失败");
    }

    /// <summary>**迭代上限为 1 时列生成只解最终整数主问题** / With a limit of one the column generation path solves the final master only.</summary>
    [Fact]
    public async Task ColumnGeneration_ShouldSkipTheLoopWhenTheLimitIsOne() {
        Csp2dProblem problem = ProblemOf() with { Config = new Csp2dConfiguration(IterationLimit: 1) };

        StubSolver solver = new();
        Result<Csp2dSolution, ErrorCode, Error<ErrorCode>> result =
            await new Csp2dColumnGeneration(solver).SolveAsync(problem);

        result.IsOk.Should().BeTrue();
        result.ValueOrThrow().Iterations.Should().Be(1, "the loop never ran");
        // **"没进循环"要用"LP 一次都没调用"来验**：只看 `Iterations` 会被循环里的
        // 「需求已覆盖」与「没有新列」两条 break 挡住，看不见上限判断本身。
        // **"The loop never ran" must be checked by "the LP was never called"**: `Iterations` alone is masked by the two breaks inside the loop, hiding the limit check.
        solver.LpCalls.Should().Be(0, "a loop that enters would solve the relaxation at least once");
    }

    /// <summary>**需求已被覆盖时列生成在松弛轮次后收敛** / Column generation converges after the relaxed round once the demand is covered.</summary>
    [Fact]
    public async Task ColumnGeneration_ShouldBreakWhenTheDemandIsCovered() {
        Csp2dProblem problem = ProblemOf() with { Config = new Csp2dConfiguration(IterationLimit: 5) };

        Result<Csp2dSolution, ErrorCode, Error<ErrorCode>> result =
            await new Csp2dColumnGeneration(new StubSolver()).SolveAsync(problem);

        result.IsOk.Should().BeTrue();
        result.ValueOrThrow().Iterations.Should().Be(1,
            "the first relaxed round already covers the demand, so no column is appended");
    }

    /// <summary>**使用次数向下取整且负数归零** / Usages are floored and negatives clamp to zero.</summary>
    [Fact]
    public void ReadUsages_ShouldFloorAndClampAtZero() {
        Csp2dProblem problem = ProblemOf();
        IReadOnlyList<CuttingPlan2<Flt64>> plans = Csp2dSolverSupport
            .BuildPlans(problem, new List<(Product2<Flt64>, ulong)> { (ProductOf(), 3UL) }).ValueOrThrow();
        (LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment) assignmentTuple =
            Csp2dSolverSupport.AssembleMaster("read", problem, plans).ValueOrThrow();
        LinearMetaModel<Flt64> model = assignmentTuple.Model;
        Csp2dPatternAssignment assignment = assignmentTuple.Assignment;
        try {
            int count = model.Tokens.TokensInSolver.Count;
            var values = new List<Flt64>();
            for (int index = 0; index < count; index++) {
                values.Add(index == 0 ? new Flt64(2.9) : new Flt64(-1.0));
            }
            model.SetSolution(values);

            IReadOnlyList<int> usages = Csp2dSolverReading.ReadUsages(model, assignment);

            usages[0].Should().Be(2, "2.9 floors to 2, because rounding up would open an extra plan");
        } finally {
            model.Dispose();
        }

        // **负数单独验一次**：同一模型里只有一个方案时，塞两个值就把两件事混在一起了。
        // **Negatives get their own fixture**: with a single plan in the model, stuffing two values would mix two claims into one.
        (LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment) negativeTuple =
            Csp2dSolverSupport.AssembleMaster("neg", problem, plans).ValueOrThrow();
        try {
            var negative = new List<Flt64>();
            for (int index = 0; index < negativeTuple.Model.Tokens.TokensInSolver.Count; index++) {
                negative.Add(new Flt64(-1.0));
            }
            negativeTuple.Model.SetSolution(negative);
            Csp2dSolverReading.ReadUsages(negativeTuple.Model, negativeTuple.Assignment)[0]
                .Should().Be(0, "a negative usage does not exist physically");
        } finally {
            negativeTuple.Model.Dispose();
        }
    }

    /// <summary>按"解值 1 给使用量变量"的桩求解器 / A stub solver returning one for every usage token.</summary>
    private sealed class StubSolver : IColumnGenerationSolver {
        private readonly bool _fail;

        public StubSolver(bool fail = false) => _fail = fail;

        public string Name => "stub";

        /// <summary>LP 被调用的次数 / How many times the LP was solved.</summary>
        public int LpCalls { get; private set; }

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) {
            if (_fail) {
                return Task.FromResult<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(
                    Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(
                        ErrorCode.OREngineSolvingException, "桩求解器失败 / the stub solver failed")));
            }
            return Task.FromResult<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(
                Results.Ok(OutputOf(metaModel)));
        }

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode,
            Error<ErrorCode>>> SolveMILPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            ulong amount,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Task.FromResult<Result<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>>(
                Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed, "桩求解器不提供解池 / the stub solver has no solution pool")));

        public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("桩求解器不支持带转换的求解 / the stub solver has no converter path");

        public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) {
            LpCalls++;
            FeasibleSolverOutput<Flt64> output = OutputOf(metaModel);
            return Task.FromResult<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>>(
                Results.Ok(new IColumnGenerationSolver.LpResult(
                    output, new Dictionary<MathConstraint, Flt64>())));
        }

        public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("桩求解器不支持带转换的求解 / the stub solver has no converter path");

        private static FeasibleSolverOutput<Flt64> OutputOf(LinearMetaModel<Flt64> model) {
            var values = new List<Flt64>();
            foreach (Core.Token.Token<Flt64> token in model.Tokens.TokensInSolver) {
                string? variableName = token.Variable.Name;
                values.Add(variableName is not null && variableName.Contains("usage", StringComparison.Ordinal)
                    ? Flt64.One
                    : Flt64.Zero);
            }
            return new FeasibleSolverOutput<Flt64>(
                Obj: Flt64.Zero,
                Solution: new Solution<Flt64>(values),
                Time: TimeSpan.Zero,
                PossibleBestObj: Flt64.Zero,
                Gap: Flt64.Zero);
        }
    }

    /// <summary>构造问题 / Build a problem.</summary>
    /// <returns>问题 / The problem.</returns>
    private static Csp2dProblem ProblemOf() =>
        new(new List<Material2<Flt64>> {
                new(Csp2dIds.MaterialId2Of("MAT1"), "mat", Metres(4.0), Metres(2.0)),
            },
            new List<Product2<Flt64>> { ProductOf() },
            new List<ProductDemand2<Flt64>> { new(ProductOf(), new DemandValue2<Flt64>.Amount(3UL)) },
            Metres(0.0),
            Config: new Csp2dConfiguration());

    /// <summary>构造产品 / Build a product.</summary>
    /// <returns>产品 / The product.</returns>
    private static Product2<Flt64> ProductOf() =>
        new(Csp2dIds.ProductId2Of("P1"), "part", Metres(1.0), Metres(1.0));

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);
}
