#nullable enable
#if CPLEX_INTEGRATION

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Plugin.Cplex;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Cplex.Tests;

/// <summary>
/// Opt-in CPLEX native integration tests.
/// / 需要显式启用的 CPLEX 真机集成测试。
/// </summary>
public class CplexIntegrationTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Solver", "Cplex")]
    public async Task SolvesContinuousAndIntegerModelsWithObjectiveOffset(bool integer) {
        using LinearTriadModel model = LinearModel(
            variables: [
                Variable(0, integer ? Integer.Instance : Continuous.Instance, 0, 10, "x"),
                Variable(1, integer ? Integer.Instance : Continuous.Instance, 0, 10, "y"),
            ],
            rows: [
                Row((0, 1), (1, 1)),
                Row((0, 1)),
            ],
            relations: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            rhs: [4, 3],
            objective: [new LinearObjectiveCell(0, new Flt64(2)), new LinearObjectiveCell(1, new Flt64(3))],
            category: ObjectCategory.Minimum,
            constant: 5);

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new CplexLinearSolver().InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        SolveReport<Flt64> report = result.Value;
        Assert.Equal(ProblemStatus.Feasible, report.ProblemStatus);
        Assert.Equal(TerminationReason.Completed, report.TerminationReason);
        Assert.Equal(SolutionPresence.Optimal, report.SolutionPresence);
        Assert.Equal(ProofStatus.Claimed, report.Proof.Status);
        Assert.Equal(14, report.Solution!.Objective!.Value.ToDouble(), 5);
        Assert.Equal(3, report.Solution.Values[0].ToDouble(), 5);
        Assert.Equal(1, report.Solution.Values[1].ToDouble(), 5);
        Assert.Equal("cplex", report.Provenance!.Descriptor.SolverId);
        Assert.StartsWith("12.", report.Provenance.NativeVersion);
        Assert.True(report.Provenance.Descriptor.Capabilities.WarmStart);
        Assert.True(report.Provenance.Descriptor.Capabilities.SolutionPool);
        Assert.True(report.Provenance.Descriptor.Capabilities.Interrupt);
        Assert.False(report.Provenance.Descriptor.Capabilities.Callback);

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy =
            await new CplexLinearSolver().InvokeAsync((ILinearTriadModelView)model);
        Assert.True(legacy.IsOk, legacy.ToString());
        Assert.Equal(14, legacy.Value.Obj.ToDouble(), 5);
    }

    [Fact]
    [Trait("Solver", "Cplex")]
    public async Task MaximizesBinaryModelAndAddsRepeatedCoefficients() {
        using LinearTriadModel model = LinearModel(
            variables: [
                Variable(0, Binary.Instance, 0, 1, "x"),
                Variable(1, Binary.Instance, 0, 1, "y"),
            ],
            rows: [Row((0, 1), (1, 1))],
            relations: [ConstraintRelation.LessEqual],
            rhs: [1],
            objective: [
                new LinearObjectiveCell(0, Flt64.One),
                new LinearObjectiveCell(1, Flt64.One),
                new LinearObjectiveCell(1, Flt64.One),
            ],
            category: ObjectCategory.Maximum,
            constant: 7);

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new CplexLinearSolver().InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(9, result.Value.Solution!.Objective!.Value.ToDouble(), 5);
        Assert.Equal(Flt64.Zero, result.Value.Solution.Values[0]);
        Assert.Equal(Flt64.One, result.Value.Solution.Values[1]);
        Assert.Equal(9, result.Value.Statistics.BestBound!.Value.ToDouble(), 5);
    }

    [Fact]
    [Trait("Solver", "Cplex")]
    public async Task ReportsInfeasibleAndLegacyError() {
        using LinearTriadModel model = LinearModel(
            variables: [Variable(0, Continuous.Instance, 0, 1, "x")],
            rows: [Row((0, 1))],
            relations: [ConstraintRelation.GreaterEqual],
            rhs: [2],
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);

        var solver = new CplexLinearSolver();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(ProblemStatus.Infeasible, result.Value.ProblemStatus);
        Assert.Equal(ProofStatus.Claimed, result.Value.Proof.Status);
        Assert.Null(result.Value.Solution);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy =
            await solver.InvokeAsync((ILinearTriadModelView)model);
        Assert.Equal(
            ErrorCode.ORModelInfeasible,
            Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(legacy).Code);
    }

    [Fact]
    [Trait("Solver", "Cplex")]
    public async Task SolvesConvexQpWithObjectiveOffsetAndCapabilities() {
        using QuadraticTetradModel model = QuadraticModel(
            variables: [
                Variable(0, Continuous.Instance, 0, 10, "x"),
                Variable(1, Continuous.Instance, 0, 10, "y"),
            ],
            rows: [QuadraticRow((0, 1), (1, 1))],
            relations: [ConstraintRelation.GreaterEqual],
            rhs: [1],
            objective: [
                new QuadraticObjectiveCell(0, 0, Flt64.One),
                new QuadraticObjectiveCell(1, 1, Flt64.One),
            ],
            category: ObjectCategory.Minimum,
            constant: 5);

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new CplexQuadraticSolver().InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        SolveReport<Flt64> report = result.Value;
        Assert.Equal(5.5, report.Solution!.Objective!.Value.ToDouble(), 4);
        Assert.Equal(0.5, report.Solution.Values[0].ToDouble(), 4);
        Assert.Equal(0.5, report.Solution.Values[1].ToDouble(), 4);
        Assert.Contains(SolverModelType.Qp, report.Provenance!.Descriptor.Capabilities.ModelTypes);
        Assert.Contains(SolverModelType.Qcp, report.Provenance.Descriptor.Capabilities.ModelTypes);
    }

    [Fact]
    [Trait("Solver", "Cplex")]
    public async Task SolvesQuadraticallyConstrainedProgram() {
        using QuadraticTetradModel model = QuadraticModel(
            variables: [
                Variable(0, Continuous.Instance, 0, 10, "x"),
                Variable(1, Continuous.Instance, 0, 10, "y"),
            ],
            rows: [QuadraticRow((0, 0, 1), (1, 1, 1))],
            relations: [ConstraintRelation.LessEqual],
            rhs: [1],
            objective: [
                new QuadraticObjectiveCell(0, -1, Flt64.One),
                new QuadraticObjectiveCell(1, -1, Flt64.One),
            ],
            category: ObjectCategory.Maximum,
            constant: 0);

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new CplexQuadraticSolver().InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        double rootTwo = System.Math.Sqrt(2);
        Assert.Equal(rootTwo, result.Value.Solution!.Objective!.Value.ToDouble(), 4);
        Assert.Equal(rootTwo / 2, result.Value.Solution.Values[0].ToDouble(), 4);
        Assert.Equal(rootTwo / 2, result.Value.Solution.Values[1].ToDouble(), 4);
    }

    [Fact]
    [Trait("Solver", "Cplex")]
    public async Task SolutionPoolIsDeduplicatedAndIndependent() {
        using LinearTriadModel model = LinearModel(
            variables: [
                Variable(0, Binary.Instance, 0, 1, "x", initial: 0),
                Variable(1, Binary.Instance, 0, 1, "y", initial: 0),
            ],
            rows: [],
            relations: [],
            rhs: [],
            objective: [new LinearObjectiveCell(0, Flt64.One), new LinearObjectiveCell(1, new Flt64(2))],
            category: ObjectCategory.Maximum,
            constant: 0);

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new CplexLinearSolver().InvokeReportAsync(model, 8);

        Assert.True(result.IsOk, result.ToString());
        SolveSolution<Flt64> solution = Assert.IsType<SolveSolution<Flt64>>(result.Value.Solution);
        Assert.Equal([Flt64.One, Flt64.One], solution.Values.Values.ToArray());
        Assert.Equal(
            solution.Pool.Count,
            solution.Pool.Select(item => string.Join(",", item.Values)).Distinct().Count());
        Assert.DoesNotContain(solution.Pool, item => item.Values.SequenceEqual(solution.Values.Values));
        if (solution.Pool.Count > 0) {
            model.Dispose();
            Assert.All(solution.Pool, item => Assert.Equal(2, item.Values.Count));
        }
    }

    [Fact]
    [Trait("Solver", "Cplex")]
    public async Task CancellationInsideNativeCallbackTerminatesSolve() {
        using var cancellation = new CancellationTokenSource();
        int called = 0;
        using LinearTriadModel model = LimitLinearModel();
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromSeconds(20),
            ThreadCount = 1,
        };

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new CplexLinearSolver(config).InvokeReportAsync(
                model: model,
                solvingStatusCallBack: status => {
                    Interlocked.Increment(ref called);
                    cancellation.Cancel();
                    return Results.OkInstance;
                },
                cancellationToken: cancellation.Token);

        Assert.True(result.IsOk, result.ToString());
        Assert.True(called > 0);
        Assert.Equal(TerminationReason.Cancelled, result.Value.TerminationReason);
        Assert.Equal(ProofStatus.None, result.Value.Proof.Status);
    }

    [Fact]
    [Trait("Solver", "Cplex")]
    public async Task StatusCallbackSeesModelAndProgress() {
        using LinearTriadModel model = LimitLinearModel();
        var observations = new List<SolvingStatus>();
        Flt64 initialBestObj = default;
        bool hasInitial = false;

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new CplexLinearSolver().InvokeReportAsync(
                model: model,
                solvingStatusCallBack: status => {
                    if (!hasInitial) {
                        initialBestObj = status.InitialBestObj;
                        hasInitial = true;
                    }
                    observations.Add(status);
                    return Results.OkInstance;
                });

        Assert.True(result.IsOk, result.ToString());
        Assert.NotEmpty(observations);
        Assert.All(observations, status => {
            Assert.Equal("cplex", status.Solver);
            Assert.Same(model, status.IntermediateModel);
            Assert.Equal(ObjectCategory.Minimum, status.ObjectCategory);
            Assert.True(status.Time >= TimeSpan.Zero);
            Assert.Equal(initialBestObj, status.InitialBestObj);
            Assert.True(double.IsFinite(status.Obj.ToDouble()));
        });
    }

    [Fact]
    [Trait("Solver", "Cplex")]
    public async Task ReportsUnboundedModelWithoutPretendingOptimal() {
        using LinearTriadModel model = LinearModel(
            variables: [Variable(0, Continuous.Instance, 0, double.PositiveInfinity, "x")],
            rows: [],
            relations: [],
            rhs: [],
            objective: [new LinearObjectiveCell(0, Flt64.One)],
            category: ObjectCategory.Maximum,
            constant: 0);

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new CplexLinearSolver().InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        Assert.Contains(
            result.Value.ProblemStatus,
            new[] {
                ProblemStatus.Unbounded,
                ProblemStatus.InfeasibleOrUnbounded,
            });
        Assert.Equal(TerminationReason.Completed, result.Value.TerminationReason);
        Assert.Equal(SolutionPresence.None, result.Value.SolutionPresence);
        Assert.Equal(ProofStatus.Claimed, result.Value.Proof.Status);
        Assert.Null(result.Value.Solution);
    }

    [Fact]
    [Trait("Solver", "Cplex")]
    public async Task SolvesZeroColumnModelWithObjectiveConstant() {
        using LinearTriadModel model = LinearModel(
            variables: [],
            rows: [],
            relations: [],
            rhs: [],
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 7);

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new CplexLinearSolver().InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(ProblemStatus.Feasible, result.Value.ProblemStatus);
        Assert.Equal(TerminationReason.Completed, result.Value.TerminationReason);
        Assert.Equal(SolutionPresence.Optimal, result.Value.SolutionPresence);
        Assert.Empty(result.Value.Solution!.Values.Values);
        Assert.Equal(7, result.Value.Solution.Objective!.Value.ToDouble(), 8);
    }

    [Fact]
    [Trait("Solver", "Cplex")]
    public async Task TinyTimeLimitDoesNotFabricateAProof() {
        using LinearTriadModel model = LimitLinearModel();
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromMilliseconds(1),
            ThreadCount = 1,
        };

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new CplexLinearSolver(config).InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal("0.001", result.Value.Provenance!.EffectiveParameters["time"]);
        if (result.Value.TerminationReason == TerminationReason.TimeLimit) {
            Assert.Equal(ProofStatus.None, result.Value.Proof.Status);
            Assert.NotEqual(SolutionPresence.Optimal, result.Value.SolutionPresence);
        }
    }

    [Fact]
    [Trait("Solver", "Cplex")]
    public async Task RejectsForeignBackendConfiguration() {
        using LinearTriadModel linear = LinearModel(
            variables: [],
            rows: [],
            relations: [],
            rhs: [],
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        using QuadraticTetradModel quadratic = QuadraticModel(
            variables: [],
            rows: [],
            relations: [],
            rhs: [],
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        var config = new SolverConfig {
            BackendConfiguration = new GurobiSolverConfig(),
        };

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> linearResult =
            await new CplexLinearSolver(config).InvokeReportAsync(linear);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> quadraticResult =
            await new CplexQuadraticSolver(config).InvokeReportAsync(quadratic);

        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(linearResult).Code);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(quadraticResult).Code);
    }

    [Fact]
    [Trait("Solver", "Cplex")]
    public async Task FeasibleInitialMipStartProducesExpectedSolution() {
        const int count = 80;
        var variables = new List<ModelViewVariable>(count);
        var objective = new LinearObjectiveCell[count];
        var row = new List<(int, double)>(count);
        for (int index = 0; index < count; index++) {
            bool selected = index < count / 2;
            variables.Add(Variable(index, Binary.Instance, 0, 1, $"x{index}", initial: selected ? 1 : 0));
            objective[index] = new LinearObjectiveCell(index, new Flt64(index + 1));
            row.Add((index, 1));
        }
        using LinearTriadModel model = LinearModel(
            variables: variables,
            rows: [Row(row.ToArray())],
            relations: [ConstraintRelation.Equal],
            rhs: [count / 2],
            objective: objective,
            category: ObjectCategory.Minimum,
            constant: 0);
        IReadOnlyList<Flt64> expected = Enumerable.Range(0, count)
            .Select(index => index < count / 2 ? Flt64.One : Flt64.Zero)
            .ToArray();
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromSeconds(20),
            ThreadCount = 1,
        };

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new CplexLinearSolver(config).InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(expected, result.Value.Solution!.Values.Values);
    }

    [Fact]
    [Trait("Solver", "Cplex")]
    public void NativeIterationLimitMapsToUnifiedTermination() {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = CplexReportSupport.Create(
            status: "Unknown",
            detailedStatus: "AbortItLim",
            solution: null,
            statistics: new SolveStatistics(SolveTime: TimeSpan.FromSeconds(1)),
            cancelledDuringSolve: false,
            interruptedDuringSolve: false,
            config: new SolverConfig(),
            quadratic: false,
            nativeVersion: "12.10.0");

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(TerminationReason.IterationLimit, result.Value.TerminationReason);
        Assert.Equal(ProblemStatus.Unknown, result.Value.ProblemStatus);
        Assert.Equal(SolutionPresence.None, result.Value.SolutionPresence);
        Assert.Equal(ProofStatus.None, result.Value.Proof.Status);
    }

    private static ModelViewVariable Variable(
        int index,
        IVariableTypeKind type,
        double lower,
        double upper,
        string name,
        double? initial = null) =>
        new(
            index: index,
            lowerBound: new Flt64(lower),
            upperBound: new Flt64(upper),
            type: type,
            name: name,
            initialResult: initial is { } value ? new Flt64(value) : null);

    private static SparseVector Row(params (int Column, double Coefficient)[] entries) =>
        new(entries.Select(entry => new SparseVectorEntry(entry.Column, new Flt64(entry.Coefficient))));

    private static SparseQuadraticVector QuadraticRow(params (int Column, double Coefficient)[] entries) =>
        new(entries.Select(entry => new SparseQuadraticEntry(
            Col1: entry.Column,
            Col2: -1,
            Value: new Flt64(entry.Coefficient))));

    private static SparseQuadraticVector QuadraticRow(params (int First, int Second, double Coefficient)[] entries) =>
        new(entries.Select(entry => new SparseQuadraticEntry(
            Col1: entry.First,
            Col2: entry.Second,
            Value: new Flt64(entry.Coefficient))));

    private static LinearTriadModel LinearModel(
        IReadOnlyList<ModelViewVariable> variables,
        IReadOnlyList<SparseVector> rows,
        IReadOnlyList<ConstraintRelation> relations,
        IReadOnlyList<double> rhs,
        IReadOnlyList<LinearObjectiveCell> objective,
        ObjectCategory category,
        double constant) =>
        new(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(rows),
                signs: relations,
                rhs: rhs.Select(value => new Flt64(value)).ToArray(),
                names: rows.Select((_, index) => $"r{index}").ToArray(),
                sources: rows.Select(_ => ConstraintSource.Origin).ToArray()),
            objective: new Objective<LinearObjectiveCell>(
                category,
                objective,
                new Flt64(constant)),
            name: "Cplex-linear-integration");

    private static QuadraticTetradModel QuadraticModel(
        IReadOnlyList<ModelViewVariable> variables,
        IReadOnlyList<SparseQuadraticVector> rows,
        IReadOnlyList<ConstraintRelation> relations,
        IReadOnlyList<double> rhs,
        IReadOnlyList<QuadraticObjectiveCell> objective,
        ObjectCategory category,
        double constant) =>
        new(
            variables: variables,
            constraints: new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix(rows),
                signs: relations,
                rhs: rhs.Select(value => new Flt64(value)).ToArray(),
                names: rows.Select((_, index) => $"r{index}").ToArray(),
                sources: rows.Select(_ => ConstraintSource.Origin).ToArray()),
            objective: new Objective<QuadraticObjectiveCell>(
                category,
                objective,
                new Flt64(constant)),
            name: "Cplex-quadratic-integration");

    private static LinearTriadModel LimitLinearModel() {
        const int count = 1000;
        var random = new Random(17);
        var variables = new List<ModelViewVariable>(count);
        var objective = new LinearObjectiveCell[count];
        var row = new List<(int, double)>(count);
        for (int index = 0; index < count; index++) {
            variables.Add(Variable(index, Binary.Instance, 0, 1, $"x{index}", initial: 0));
            objective[index] = new LinearObjectiveCell(
                index,
                new Flt64(-random.Next(1, 100)));
            row.Add((index, random.Next(1, 100)));
        }
        return LinearModel(
            variables: variables,
            rows: [Row(row.ToArray())],
            relations: [ConstraintRelation.LessEqual],
            rhs: [25000],
            objective: objective,
            category: ObjectCategory.Minimum,
            constant: 0);
    }
}
#endif
