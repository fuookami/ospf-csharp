#nullable enable
#if SCIP_INTEGRATION

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

public class ScipQuadraticIntegrationTest {
    [Fact]
    public async Task SolvesConvexQpWithObjectiveOffsetAndReportsCapabilities() {
        using QuadraticTetradModel model = MakeModel(
            variables: [
                ContinuousVariable(0, "x"),
                ContinuousVariable(1, "y"),
            ],
            rows: [
                new SparseQuadraticVector([
                    new SparseQuadraticEntry(0, -1, Flt64.One),
                    new SparseQuadraticEntry(1, -1, Flt64.One),
                ]),
            ],
            relations: [ConstraintRelation.GreaterEqual],
            right: [Flt64.One],
            objective: [
                new QuadraticObjectiveCell(0, 0, Flt64.One),
                new QuadraticObjectiveCell(1, 1, Flt64.One),
            ],
            category: ObjectCategory.Minimum,
            constant: 5);

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipQuadraticSolver().InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        SolveReport<Flt64> report = result.Value;
        Assert.Equal(ProblemStatus.Feasible, report.ProblemStatus);
        Assert.Equal(SolutionPresence.Optimal, report.SolutionPresence);
        Assert.Equal(ProofStatus.Claimed, report.Proof.Status);
        Assert.Equal(0.5, report.Solution!.Values[0].ToDouble(), 3);
        Assert.Equal(0.5, report.Solution.Values[1].ToDouble(), 3);
        Assert.Equal(5.5, report.Solution.Objective!.Value.ToDouble(), 3);
        Assert.Contains(SolverModelType.Qp, report.Provenance!.Descriptor.Capabilities.ModelTypes);
        Assert.Contains(SolverModelType.Qcp, report.Provenance.Descriptor.Capabilities.ModelTypes);
        Assert.True(report.Provenance.Descriptor.Capabilities.Callback);
        Assert.True(report.Provenance.Descriptor.Capabilities.Interrupt);
        Assert.Equal("scip", report.Provenance.Descriptor.SolverId);
    }

    [Fact]
    public async Task SolvesQuadraticallyConstrainedProgramAndLegacyEntry() {
        using QuadraticTetradModel model = MakeModel(
            variables: [
                ContinuousVariable(0, "x"),
                ContinuousVariable(1, "y"),
            ],
            rows: [
                new SparseQuadraticVector([
                    new SparseQuadraticEntry(0, 0, Flt64.One),
                    new SparseQuadraticEntry(1, 1, Flt64.One),
                ]),
            ],
            relations: [ConstraintRelation.LessEqual],
            right: [Flt64.One],
            objective: [
                new QuadraticObjectiveCell(0, -1, Flt64.One),
                new QuadraticObjectiveCell(1, -1, Flt64.One),
            ],
            category: ObjectCategory.Maximum,
            constant: 0);

        var solver = new ScipQuadraticSolver();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> reportResult =
            await solver.InvokeReportAsync(model);
        Assert.True(reportResult.IsOk, reportResult.ToString());
        double rootTwo = System.Math.Sqrt(2);
        Assert.Equal(rootTwo, reportResult.Value.Solution!.Objective!.Value.ToDouble(), 5);
        Assert.Equal(rootTwo / 2, reportResult.Value.Solution.Values[0].ToDouble(), 5);
        Assert.Equal(rootTwo / 2, reportResult.Value.Solution.Values[1].ToDouble(), 5);

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy =
            await solver.InvokeAsync((IQuadraticTetradModelView)model);
        Assert.True(legacy.IsOk, legacy.ToString());
        Assert.Equal(rootTwo, legacy.Value.Obj.ToDouble(), 5);
        Assert.Equal(2, legacy.Value.Solution.Values.Count);
    }

    [Fact]
    public async Task StatusCallbackSeesTheQuadraticModel() {
        using QuadraticTetradModel model = MakeModel(
            variables: [ContinuousVariable(0, "x")],
            rows: [],
            relations: [],
            right: [],
            objective: [new QuadraticObjectiveCell(0, 0, Flt64.One)],
            category: ObjectCategory.Minimum,
            constant: 2);
        var observations = new List<SolvingStatus>();

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipQuadraticSolver().InvokeReportAsync(
                model: model,
                solvingStatusCallBack: status => {
                    observations.Add(status);
                    return Results.OkInstance;
                });

        Assert.True(result.IsOk, result.ToString());
        Assert.NotEmpty(observations);
        Assert.All(observations, status => {
            Assert.Equal("scip", status.Solver);
            Assert.Same(model, status.IntermediateModel);
            Assert.Equal(ObjectCategory.Minimum, status.ObjectCategory);
        });
    }

    [Fact]
    public async Task FullInitialHintsAreSubmittedBeforeQuadraticObjectiveAuxiliaries() {
        using QuadraticTetradModel model = MakeModel(
            variables: [
                new ModelViewVariable(
                    index: 0,
                    lowerBound: Flt64.Zero,
                    upperBound: new Flt64(10),
                    type: Continuous.Instance,
                    name: "x",
                    initialResult: new Flt64(2)),
                new ModelViewVariable(
                    index: 1,
                    lowerBound: Flt64.Zero,
                    upperBound: new Flt64(10),
                    type: Continuous.Instance,
                    name: "y",
                    initialResult: new Flt64(3)),
            ],
            rows: [
                new SparseQuadraticVector([
                    new SparseQuadraticEntry(0, -1, Flt64.One),
                    new SparseQuadraticEntry(1, -1, Flt64.One),
                ]),
            ],
            relations: [ConstraintRelation.GreaterEqual],
            right: [new Flt64(5)],
            objective: [
                new QuadraticObjectiveCell(0, 0, Flt64.One),
                new QuadraticObjectiveCell(1, 1, Flt64.One),
            ],
            category: ObjectCategory.Minimum,
            constant: 0);

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipQuadraticSolver().InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(12.5, result.Value.Solution!.Objective!.Value.ToDouble(), 3);
        Assert.Equal(2.5, result.Value.Solution.Values[0].ToDouble(), 3);
        Assert.Equal(2.5, result.Value.Solution.Values[1].ToDouble(), 3);
    }

    [Fact]
    public async Task CancellationBeforeSnapshotReturnsCancelledReport() {
        using QuadraticTetradModel model = MakeModel(
            variables: [ContinuousVariable(0, "x")],
            rows: [],
            relations: [],
            right: [],
            objective: [new QuadraticObjectiveCell(0, 0, Flt64.One)],
            category: ObjectCategory.Minimum,
            constant: 0);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipQuadraticSolver().InvokeReportAsync(
                model: model,
                cancellationToken: cancellation.Token);

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(TerminationReason.Cancelled, result.Value.TerminationReason);
        Assert.Null(result.Value.Solution);
    }

    [Fact]
    public async Task NativeCancellationProducesASolveReport() {
        const int variableCount = 36;
        var variables = new ModelViewVariable[variableCount];
        var linear = new List<SparseQuadraticEntry>(variableCount);
        var objective = new List<QuadraticObjectiveCell>();
        for (int i = 0; i < variableCount; i++) {
            variables[i] = new ModelViewVariable(
                index: i,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: $"x{i}");
            linear.Add(new SparseQuadraticEntry(i, -1, Flt64.One));
            for (int j = i + 1; j < variableCount; j++) {
                objective.Add(new QuadraticObjectiveCell(
                    i,
                    j,
                    new Flt64((i * 37 + j * 17) % 11 + 1)));
            }
        }
        using QuadraticTetradModel model = MakeModel(
            variables: variables,
            rows: [new SparseQuadraticVector(linear)],
            relations: [ConstraintRelation.LessEqual],
            right: [new Flt64(18)],
            objective: objective.ToArray(),
            category: ObjectCategory.Maximum,
            constant: 0);
        using var cancellation = new CancellationTokenSource();
        int calls = 0;

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipQuadraticSolver().InvokeReportAsync(
                model: model,
                solvingStatusCallBack: _ => {
                    calls++;
                    if (calls == 2) {
                        cancellation.Cancel();
                    }
                    return Results.OkInstance;
                },
                cancellationToken: cancellation.Token).WaitAsync(TimeSpan.FromSeconds(25));

        Assert.True(result.IsOk, result.ToString());
        Assert.True(calls >= 2);
        Assert.NotNull(result.Value.Provenance);
        Assert.Equal(TerminationReason.Cancelled, result.Value.TerminationReason);
        Assert.Equal(ProofStatus.None, result.Value.Proof.Status);
    }

    private static ModelViewVariable ContinuousVariable(int index, string name) => new(
        index: index,
        lowerBound: Flt64.Zero,
        upperBound: new Flt64(10),
        type: Continuous.Instance,
        name: name);

    private static QuadraticTetradModel MakeModel(
        ModelViewVariable[] variables,
        SparseQuadraticVector[] rows,
        ConstraintRelation[] relations,
        Flt64[] right,
        QuadraticObjectiveCell[] objective,
        ObjectCategory category,
        double constant) => new(
            variables: variables,
            constraints: new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix(rows),
                signs: relations,
                rhs: right,
                names: Array.ConvertAll(rows, _ => "row"),
                sources: Array.ConvertAll(rows, _ => ConstraintSource.Origin)),
            objective: new Objective<QuadraticObjectiveCell>(
                category: category,
                cells: objective,
                constant: new Flt64(constant)),
            name: "quadratic-test");
}

#endif
