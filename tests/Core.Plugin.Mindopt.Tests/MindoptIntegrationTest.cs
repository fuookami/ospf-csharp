#nullable enable
#if MINDOPT_INTEGRATION

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Plugin.Mindopt;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Mindopt.Tests;

/// <summary>
/// Opt-in MindOPT native integration tests.
/// / 需要显式启用的 MindOPT 真机集成测试。
/// </summary>
public class MindoptIntegrationTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Solver", "MindOPT")]
    public async Task SolvesContinuousAndIntegerModelsWithObjectiveOffset(bool integer) {
        using LinearTriadModel model = LinearModel(
            variables: [
                Variable(
                    index: 0,
                    type: integer ? Integer.Instance : Continuous.Instance,
                    name: "x"),
                Variable(
                    index: 1,
                    type: integer ? Integer.Instance : Continuous.Instance,
                    name: "y"),
            ],
            rows: [
                Row((0, 1), (1, 1)),
                Row((0, 1)),
            ],
            relations: [
                ConstraintRelation.GreaterEqual,
                ConstraintRelation.LessEqual,
            ],
            rhs: [4, 3],
            objective: [
                new LinearObjectiveCell(0, new Flt64(2)),
                new LinearObjectiveCell(1, new Flt64(3)),
            ],
            category: ObjectCategory.Minimum,
            constant: 5);

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new MindoptLinearSolver().InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        SolveReport<Flt64> report = result.Value;
        Assert.Equal(ProblemStatus.Feasible, report.ProblemStatus);
        Assert.Equal(TerminationReason.Completed, report.TerminationReason);
        Assert.Equal(SolutionPresence.Optimal, report.SolutionPresence);
        Assert.Equal(ProofStatus.Claimed, report.Proof.Status);
        Assert.Equal(14, report.Solution!.Objective!.Value.ToDouble(), 5);
        Assert.Equal(3, report.Solution.Values[0].ToDouble(), 5);
        Assert.Equal(1, report.Solution.Values[1].ToDouble(), 5);
        Assert.Equal("mindopt", report.Provenance!.Descriptor.SolverId);
        Assert.StartsWith("2.3", report.Provenance.NativeVersion);

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy =
            await new MindoptLinearSolver().InvokeAsync((ILinearTriadModelView)model);
        Assert.True(legacy.IsOk, legacy.ToString());
        Assert.Equal(14, legacy.Value.Obj.ToDouble(), 5);
    }

    [Fact]
    [Trait("Solver", "MindOPT")]
    public async Task SolvesConvexQpWithObjectiveOffset() {
        using QuadraticTetradModel model = QuadraticModel(
            variables: [
                Variable(index: 0, type: Continuous.Instance, name: "x"),
                Variable(index: 1, type: Continuous.Instance, name: "y"),
            ],
            rows: [QuadraticRow((0, -1, 1), (1, -1, 1))],
            relations: [ConstraintRelation.GreaterEqual],
            rhs: [1],
            objective: [
                new QuadraticObjectiveCell(0, 0, Flt64.One),
                new QuadraticObjectiveCell(1, 1, Flt64.One),
            ],
            category: ObjectCategory.Minimum,
            constant: 5);

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new MindoptQuadraticSolver().InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(5.5, result.Value.Solution!.Objective!.Value.ToDouble(), 4);
        Assert.Equal(0.5, result.Value.Solution.Values[0].ToDouble(), 4);
        Assert.Equal(0.5, result.Value.Solution.Values[1].ToDouble(), 4);
        Assert.Contains(SolverModelType.Qp, result.Value.Provenance!.Descriptor.Capabilities.ModelTypes);
    }

    [Fact]
    [Trait("Solver", "MindOPT")]
    public async Task SolvesQuadraticallyConstrainedProgram() {
        using QuadraticTetradModel model = QuadraticModel(
            variables: [
                Variable(index: 0, type: Continuous.Instance, name: "x"),
                Variable(index: 1, type: Continuous.Instance, name: "y"),
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
            await new MindoptQuadraticSolver().InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        double rootTwo = System.Math.Sqrt(2);
        Assert.Equal(rootTwo, result.Value.Solution!.Objective!.Value.ToDouble(), 4);
        Assert.Equal(rootTwo / 2, result.Value.Solution.Values[0].ToDouble(), 4);
        Assert.Equal(rootTwo / 2, result.Value.Solution.Values[1].ToDouble(), 4);
    }

    [Fact]
    [Trait("Solver", "MindOPT")]
    public async Task ReturnsRowDualsForPricing() {
        using LinearTriadModel model = LinearModel(
            variables: [
                Variable(index: 0, type: Continuous.Instance, name: "x"),
                Variable(index: 1, type: Continuous.Instance, name: "y"),
            ],
            rows: [
                Row((0, 1), (1, 1)),
                Row((0, 1)),
            ],
            relations: [
                ConstraintRelation.GreaterEqual,
                ConstraintRelation.LessEqual,
            ],
            rhs: [4, 3],
            objective: [
                new LinearObjectiveCell(0, new Flt64(2)),
                new LinearObjectiveCell(1, new Flt64(3)),
            ],
            category: ObjectCategory.Minimum,
            constant: 5);

        Result<MindoptLinearOutcome, ErrorCode, Error<ErrorCode>> result =
            await new MindoptLinearSolver().InvokeReportWithDualsAsync(model);

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(ProblemStatus.Feasible, result.Value.Report.ProblemStatus);
        Assert.NotNull(result.Value.RowDuals);
        Assert.Equal(2, result.Value.RowDuals!.Count);
        Assert.All(
            result.Value.RowDuals,
            value => Assert.True(double.IsFinite(value.ToDouble())));
    }

    [Fact]
    [Trait("Solver", "MindOPT")]
    public async Task ReturnsFarkasEvidenceForInfeasibleSubproblem() {
        using LinearTriadModel model = LinearModel(
            variables: [
                Variable(index: 0, type: Continuous.Instance, name: "x"),
            ],
            rows: [
                Row((0, 1)),
                Row((0, 1)),
            ],
            relations: [
                ConstraintRelation.GreaterEqual,
                ConstraintRelation.LessEqual,
            ],
            rhs: [2, 1],
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);

        Result<MindoptLinearOutcome, ErrorCode, Error<ErrorCode>> result =
            await new MindoptLinearSolver().InvokeReportWithDualsAsync(
                model: model,
                requireFarkas: true);

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(ProblemStatus.Infeasible, result.Value.Report.ProblemStatus);
        Assert.NotNull(result.Value.FarkasDuals);
        Assert.Equal(2, result.Value.FarkasDuals!.Count);
        Assert.Contains(result.Value.FarkasDuals, value => value.ToDouble() != 0);
    }

    [Fact]
    [Trait("Solver", "MindOPT")]
    public async Task SolutionPoolIsDeduplicatedAndExcludesTheMainSolution() {
        using LinearTriadModel model = LinearModel(
            variables: [
                Variable(index: 0, type: Binary.Instance, name: "x"),
                Variable(index: 1, type: Binary.Instance, name: "y"),
            ],
            rows: [],
            relations: [],
            rhs: [],
            objective: [
                new LinearObjectiveCell(0, Flt64.One),
                new LinearObjectiveCell(1, new Flt64(2)),
            ],
            category: ObjectCategory.Maximum,
            constant: 0);

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new MindoptLinearSolver().InvokeReportAsync(
                model: model,
                solutionAmount: 8);

        Assert.True(result.IsOk, result.ToString());
        SolveSolution<Flt64> solution = Assert.IsType<SolveSolution<Flt64>>(result.Value.Solution);
        Assert.Equal([Flt64.One, Flt64.One], solution.Values.Values.ToArray());
        Assert.Equal(
            solution.Pool.Count,
            solution.Pool.Select(item => string.Join(",", item.Values)).Distinct().Count());
        Assert.DoesNotContain(solution.Pool, item => item.Values.SequenceEqual(solution.Values.Values));
    }

    [Fact]
    [Trait("Solver", "MindOPT")]
    public async Task CancellationInsideNativeCallbackTerminatesSolve() {
        using var cancellation = new CancellationTokenSource();
        int called = 0;
        using LinearTriadModel model = LimitLinearModel();
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromSeconds(20),
            ThreadCount = 1,
        };

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new MindoptLinearSolver(config).InvokeReportAsync(
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

    private static ModelViewVariable Variable(
        int index,
        IVariableTypeKind type,
        string name) => new(
            index: index,
            lowerBound: Flt64.Zero,
            upperBound: type.IsBinaryType ? Flt64.One : new Flt64(10),
            type: type,
            name: name);

    private static SparseVector Row(params (int Column, double Coefficient)[] entries) =>
        new(entries.Select(entry => new SparseVectorEntry(
            Index: entry.Column,
            Value: new Flt64(entry.Coefficient))));

    private static SparseQuadraticVector QuadraticRow(
        params (int First, int Second, double Coefficient)[] entries) =>
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
                category: category,
                cells: objective,
                constant: new Flt64(constant)),
            name: "MindOPT-linear-integration");

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
                category: category,
                cells: objective,
                constant: new Flt64(constant)),
            name: "MindOPT-quadratic-integration");

    private static LinearTriadModel LimitLinearModel() {
        const int count = 500;
        var random = new Random(17);
        var variables = new List<ModelViewVariable>(count);
        var objective = new LinearObjectiveCell[count];
        var row = new List<(int Column, double Coefficient)>(count);
        for (int index = 0; index < count; index++) {
            variables.Add(new ModelViewVariable(
                index: index,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: $"x{index}"));
            objective[index] = new LinearObjectiveCell(
                ColIndex: index,
                Coefficient: new Flt64(-random.Next(1, 100)));
            row.Add((index, random.Next(1, 100)));
        }
        return LinearModel(
            variables: variables,
            rows: [Row(row.ToArray())],
            relations: [ConstraintRelation.LessEqual],
            rhs: [12_500],
            objective: objective,
            category: ObjectCategory.Minimum,
            constant: 0);
    }
}
#endif
