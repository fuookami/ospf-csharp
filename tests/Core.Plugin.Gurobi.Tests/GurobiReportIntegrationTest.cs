#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gurobi;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class GurobiReportIntegrationTest {
    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(false, 1, false)]
    [InlineData(false, 2, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, false)]
    [InlineData(true, 3, false)]
    [InlineData(false, 0, true)]
    [InlineData(false, 1, true)]
    [InlineData(false, 2, true)]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, true)]
    [InlineData(true, 2, true)]
    [InlineData(true, 3, true)]
    public async Task SerialLegacyEntriesUseRealGurobiBackends(
        bool quadratic,
        int entry,
        bool infeasible) {
        int unused = 0;
        Result<(SolverOutput Output, List<List<Flt64>>? Pool), ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel(infeasible);
            var factories = new List<Func<IQuadraticSolver>>();
            if (!infeasible) { factories.Add(() => throw new InvalidOperationException("factory failure")); }
            factories.Add(() => new GurobiQuadraticSolver());
            factories.Add(() => { unused++; return new GurobiQuadraticSolver(); });
            var solver = new SerialCombinatorialQuadraticSolver(factories);
            result = entry switch {
                0 => (await solver.InvokeAsync((IQuadraticTetradModelView)model)).Map(output => ((SolverOutput)output, (List<List<Flt64>>?)null)),
                1 => (await solver.InvokeAsync(model, 2)).Map(value => ((SolverOutput)value.Output, (List<List<Flt64>>?)value.Solutions)),
                2 => (await solver.InvokeAsync(
                    model: (IQuadraticTetradModelView)model,
                    solvingStatusCallBack: null,
                    iisConfig: new IisConfig())).Map(output => (output, (List<List<Flt64>>?)null)),
                _ => (await solver.InvokeAsync(
                    model: model,
                    solvingStatusCallBack: null,
                    iisConfig: new IisConfig())).Map(output => (output, (List<List<Flt64>>?)null)),
            };
        }
        else {
            using LinearTriadModel model = LinearModel(infeasible);
            var factories = new List<Func<ILinearSolver>>();
            if (!infeasible) { factories.Add(() => throw new InvalidOperationException("factory failure")); }
            factories.Add(() => new GurobiLinearSolver());
            factories.Add(() => { unused++; return new GurobiLinearSolver(); });
            var solver = new SerialCombinatorialLinearSolver(factories);
            result = entry switch {
                0 => (await solver.InvokeAsync((ILinearTriadModelView)model)).Map(output => ((SolverOutput)output, (List<List<Flt64>>?)null)),
                1 => (await solver.InvokeAsync(model, 2)).Map(value => ((SolverOutput)value.Output, (List<List<Flt64>>?)value.Solutions)),
                _ => (await solver.InvokeAsync(
                    model: model,
                    solvingStatusCallBack: null,
                    iisConfig: new IisConfig())).Map(output => (output, (List<List<Flt64>>?)null)),
            };
        }
        Assert.Equal(0, unused);
        if (infeasible) {
            Assert.Equal(ErrorCode.ORModelInfeasible,
                Assert.IsType<Failed<(SolverOutput Output, List<List<Flt64>>? Pool), ErrorCode, Error<ErrorCode>>>(result).Code);
        }
        else {
            Assert.True(result.IsOk, result.ToString());
            FeasibleSolverOutput<Flt64> output = Assert.IsType<FeasibleSolverOutput<Flt64>>(result.Value.Output);
            Assert.Equal(new Flt64(-1), output.Obj);
            Assert.Equal(Flt64.One, Assert.Single(output.Solution.Values));
            if (entry == 1) { Assert.Equal(2, Assert.IsType<List<List<Flt64>>>(result.Value.Pool).Count); }
        }
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(false, 1, false)]
    [InlineData(false, 2, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, false)]
    [InlineData(false, 0, true)]
    [InlineData(false, 1, true)]
    [InlineData(false, 2, true)]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, true)]
    [InlineData(true, 2, true)]
    public async Task ParallelLegacyEntriesUseRealGurobiBackends(
        bool quadratic,
        int entry,
        bool infeasible) {
        Result<(SolverOutput Output, List<List<Flt64>>? Pool), ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel(infeasible);
            var factories = new List<Func<IQuadraticSolver>>();
            if (!infeasible) { factories.Add(() => throw new InvalidOperationException("factory failure")); }
            factories.Add(() => new GurobiQuadraticSolver());
            factories.Add(() => new GurobiQuadraticSolver());
            IAbstractQuadraticSolver solver = new Fuookami.Ospf.Framework.Solver.ParallelCombinatorialQuadraticSolver(factories);
            result = entry switch {
                0 => (await solver.InvokeAsync((IQuadraticTetradModelView)model)).Map(output => ((SolverOutput)output, (List<List<Flt64>>?)null)),
                1 => (await solver.InvokeAsync(model, 2)).Map(value => ((SolverOutput)value.Output, (List<List<Flt64>>?)value.Solutions)),
                _ => (await solver.InvokeAsync(
                    model: model,
                    solvingStatusCallBack: null,
                    iisConfig: new IisConfig())).Map(output => (output, (List<List<Flt64>>?)null)),
            };
        }
        else {
            using LinearTriadModel model = LinearModel(infeasible);
            var factories = new List<Func<ILinearSolver>>();
            if (!infeasible) { factories.Add(() => throw new InvalidOperationException("factory failure")); }
            factories.Add(() => new GurobiLinearSolver());
            factories.Add(() => new GurobiLinearSolver());
            IAbstractLinearSolver solver = new Fuookami.Ospf.Framework.Solver.ParallelCombinatorialLinearSolver(factories);
            result = entry switch {
                0 => (await solver.InvokeAsync((ILinearTriadModelView)model)).Map(output => ((SolverOutput)output, (List<List<Flt64>>?)null)),
                1 => (await solver.InvokeAsync(model, 2)).Map(value => ((SolverOutput)value.Output, (List<List<Flt64>>?)value.Solutions)),
                _ => (await solver.InvokeAsync(
                    model: model,
                    solvingStatusCallBack: null,
                    iisConfig: new IisConfig())).Map(output => (output, (List<List<Flt64>>?)null)),
            };
        }
        if (infeasible) {
            Assert.Equal(ErrorCode.ORModelInfeasible,
                Assert.IsType<Failed<(SolverOutput Output, List<List<Flt64>>? Pool), ErrorCode, Error<ErrorCode>>>(result).Code);
        }
        else {
            Assert.True(result.IsOk, result.ToString());
            FeasibleSolverOutput<Flt64> output = Assert.IsType<FeasibleSolverOutput<Flt64>>(result.Value.Output);
            Assert.Equal(new Flt64(-1), output.Obj);
            Assert.Equal(Flt64.One, Assert.Single(output.Solution.Values));
            if (entry == 1) { Assert.Equal(2, Assert.IsType<List<List<Flt64>>>(result.Value.Pool).Count); }
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task ParallelCombinationPreservesBothRealGurobiReports(bool quadratic, bool infeasible, bool pool) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel(infeasible);
            IAbstractQuadraticSolver solver = new Fuookami.Ospf.Framework.Solver.ParallelCombinatorialQuadraticSolver(new Func<IQuadraticSolver>[] {
                () => throw new InvalidOperationException("factory failure"),
                () => new GurobiQuadraticSolver(),
                () => new GurobiQuadraticSolver(),
            });
            result = pool ? await solver.InvokeReportAsync(model, 2) : await solver.InvokeReportAsync(model);
        }
        else {
            using LinearTriadModel model = LinearModel(infeasible);
            IAbstractLinearSolver solver = new Fuookami.Ospf.Framework.Solver.ParallelCombinatorialLinearSolver(new Func<ILinearSolver>[] {
                () => throw new InvalidOperationException("factory failure"),
                () => new GurobiLinearSolver(),
                () => new GurobiLinearSolver(),
            });
            result = pool ? await solver.InvokeReportAsync(model, 2) : await solver.InvokeReportAsync(model);
        }
        Assert.True(result.IsOk, result.ToString());
        SolveReport<Flt64> report = result.Value;
        Assert.Equal(infeasible ? ProblemStatus.Infeasible : ProblemStatus.Feasible, report.ProblemStatus);
        Assert.Equal(TerminationReason.Completed, report.TerminationReason);
        Assert.Equal(ProofStatus.Claimed, report.Proof.Status);
        Assert.Equal("gurobi", report.Provenance!.Descriptor.SolverId);
        Assert.Equal(3, report.Attempts.Count);
        Assert.Equal("OREngineSolvingException", Assert.Single(report.Diagnostics.Errors).Code);
        Assert.Equal("uninitialized-0", report.Attempts[0].BackendId);
        Assert.Equal("gurobi", report.Attempts[1].BackendId);
        Assert.Equal("gurobi", report.Attempts[2].BackendId);
        Assert.Equal(infeasible ? null : new SolveAttemptId("parallel-1"), report.SelectedAttemptId);
        Assert.Equal(infeasible ? SolveSelectionReason.NoSuccessfulAttempt : SolveSelectionReason.BestObjective, report.SelectionReason);
        Assert.All(report.Attempts.Skip(1), attempt => {
            Assert.Equal(infeasible ? ProblemStatus.Infeasible : ProblemStatus.Feasible, attempt.Report!.ProblemStatus);
            Assert.Equal(ProofStatus.Claimed, attempt.Report.Proof.Status);
        });
        if (infeasible) {
            Assert.Null(report.Solution);
        }
        else {
            Assert.Equal(Flt64.One, Assert.Single(report.Solution!.Values.Values));
            if (pool) {
                Assert.All(report.Attempts.Skip(1), attempt => Assert.Equal(2, attempt.Report!.Solution!.Pool.Count));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParallelCombinationPreservesRealGurobiCancellation(bool quadratic) {
        using var cancellation = new CancellationTokenSource();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            GurobiQuadraticSolverCallBack callback = new GurobiQuadraticSolverCallBack().Configuration((_, _, _, _) => {
                cancellation.Cancel();
                return Task.FromResult(Results.OkInstance);
            });
            IAbstractQuadraticSolver solver = new Fuookami.Ospf.Framework.Solver.ParallelCombinatorialQuadraticSolver(new IQuadraticSolver[] {
                new GurobiQuadraticSolver(callBack: callback),
            });
            result = await solver.InvokeReportAsync(model: model, cancellationToken: cancellation.Token);
        }
        else {
            using LinearTriadModel model = LinearModel();
            GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, _, _, _) => {
                cancellation.Cancel();
                return Task.FromResult(Results.OkInstance);
            });
            IAbstractLinearSolver solver = new Fuookami.Ospf.Framework.Solver.ParallelCombinatorialLinearSolver(new ILinearSolver[] {
                new GurobiLinearSolver(callBack: callback),
            });
            result = await solver.InvokeReportAsync(model: model, cancellationToken: cancellation.Token);
        }
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(TerminationReason.Cancelled, result.Value.TerminationReason);
        Assert.Null(result.Value.SelectedAttemptId);
        Assert.Equal("cancellation requested", Assert.Single(result.Value.Attempts).CancellationReason);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SerialCombinationPreservesRealGurobiReports(bool quadratic, bool infeasible, bool pool) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel(infeasible);
            IAbstractQuadraticSolver solver = new SerialCombinatorialQuadraticSolver(new Func<IQuadraticSolver>[] {
                () => throw new InvalidOperationException("factory failure"),
                () => new GurobiQuadraticSolver(),
            });
            result = pool ? await solver.InvokeReportAsync(model, 2) : await solver.InvokeReportAsync(model);
        }
        else {
            using LinearTriadModel model = LinearModel(infeasible);
            IAbstractLinearSolver solver = new SerialCombinatorialLinearSolver(new Func<ILinearSolver>[] {
                () => throw new InvalidOperationException("factory failure"),
                () => new GurobiLinearSolver(),
            });
            result = pool ? await solver.InvokeReportAsync(model, 2) : await solver.InvokeReportAsync(model);
        }
        Assert.True(result.IsOk, result.ToString());
        SolveReport<Flt64> report = result.Value;
        Assert.Equal(infeasible ? ProblemStatus.Infeasible : ProblemStatus.Feasible, report.ProblemStatus);
        Assert.Equal(TerminationReason.Completed, report.TerminationReason);
        Assert.Equal(ProofStatus.Claimed, report.Proof.Status);
        Assert.Equal("gurobi", report.Provenance!.Descriptor.SolverId);
        Assert.Equal(2, report.Attempts.Count);
        Assert.Equal("OREngineSolvingException", Assert.Single(report.Diagnostics.Errors).Code);
        Assert.Equal("uninitialized-0", report.Attempts[0].BackendId);
        Assert.Equal("gurobi", report.Attempts[1].BackendId);
        Assert.Equal(infeasible ? null : new SolveAttemptId("serial-1"), report.SelectedAttemptId);
        if (infeasible) {
            Assert.Null(report.Solution);
        }
        else {
            Assert.Equal(Flt64.One, Assert.Single(report.Solution!.Values.Values));
            if (pool) {
                Assert.Equal(2, report.Solution.Pool.Count);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SerialCombinationPreservesRealGurobiCancellation(bool quadratic) {
        using var cancellation = new CancellationTokenSource();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            GurobiQuadraticSolverCallBack callback = new GurobiQuadraticSolverCallBack().Configuration((_, _, _, _) => {
                cancellation.Cancel();
                return Task.FromResult(Results.OkInstance);
            });
            IAbstractQuadraticSolver solver = new SerialCombinatorialQuadraticSolver(new IQuadraticSolver[] {
                new GurobiQuadraticSolver(callBack: callback),
            });
            result = await solver.InvokeReportAsync(model: model, cancellationToken: cancellation.Token);
        }
        else {
            using LinearTriadModel model = LinearModel();
            GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, _, _, _) => {
                cancellation.Cancel();
                return Task.FromResult(Results.OkInstance);
            });
            IAbstractLinearSolver solver = new SerialCombinatorialLinearSolver(new ILinearSolver[] {
                new GurobiLinearSolver(callBack: callback),
            });
            result = await solver.InvokeReportAsync(model: model, cancellationToken: cancellation.Token);
        }
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(TerminationReason.Cancelled, result.Value.TerminationReason);
        Assert.Null(result.Value.SelectedAttemptId);
        Assert.Equal("cancellation requested", Assert.Single(result.Value.Attempts).CancellationReason);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReportsRealOptimalAndInfeasibleStates(bool quadratic, bool infeasible) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel(infeasible);
            IAbstractQuadraticSolver solver = new GurobiQuadraticSolver();
            result = await solver.SolveReportAsync(model, IIntoValue<Flt64>.Identity);
        }
        else {
            using LinearTriadModel model = LinearModel(infeasible);
            IAbstractLinearSolver solver = new GurobiLinearSolver();
            result = await solver.SolveReportAsync(model, IIntoValue<Flt64>.Identity);
        }
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(infeasible ? ProblemStatus.Infeasible : ProblemStatus.Feasible, result.Value.ProblemStatus);
        Assert.Equal(TerminationReason.Completed, result.Value.TerminationReason);
        Assert.Equal(infeasible ? SolutionPresence.None : SolutionPresence.Optimal, result.Value.SolutionPresence);
        Assert.Equal(ProofStatus.Claimed, result.Value.Proof.Status);
        Assert.NotNull(result.Value.Statistics.SolveTime);
        Assert.NotNull(result.Value.Provenance);
        Assert.Equal("gurobi", result.Value.Provenance.Descriptor.SolverId);
        Assert.True(result.Value.Provenance.Descriptor.Capabilities.Interrupt);
        if (infeasible) {
            Assert.Null(result.Value.Solution);
        }
        else {
            Assert.NotNull(result.Value.Solution);
            Assert.Equal(
                expected: 1.0,
                actual: Assert.Single(result.Value.Solution.Values.Values).ToDouble(),
                precision: 8);
            Assert.Equal(
                expected: -1.0,
                actual: result.Value.Solution.Objective!.Value.ToDouble(),
                precision: 8);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenericSolveRejectsInvalidConverter(bool quadratic) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            IAbstractQuadraticSolver solver = new GurobiQuadraticSolver();
            result = await solver.SolveReportAsync(model, new ZeroConverter());
        }
        else {
            using LinearTriadModel model = LinearModel();
            IAbstractLinearSolver solver = new GurobiLinearSolver();
            result = await solver.SolveReportAsync(model, new ZeroConverter());
        }
        Assert.Equal(ErrorCode.IllegalArgument,
            Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(result).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptimalEmptyModelHasAnEmptySolution(bool quadratic) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using var model = new QuadraticTetradModel(
                variables: [],
                constraints: new QuadraticConstraintBatch(
                    lhs: new SparseQuadraticMatrix([]),
                    signs: [],
                    rhs: [],
                    names: [],
                    sources: []),
                objective: new Objective<QuadraticObjectiveCell>(
                    category: ObjectCategory.Minimum,
                    cells: [],
                    constant: new Flt64(5)),
                name: "empty-quadratic");
            result = await new GurobiQuadraticSolver().InvokeReportAsync(model);
        }
        else {
            using var model = new LinearTriadModel(
                variables: [],
                constraints: new LinearConstraintBatch(
                    lhs: new SparseMatrix([]),
                    signs: [],
                    rhs: [],
                    names: [],
                    sources: []),
                objective: new Objective<LinearObjectiveCell>(
                    category: ObjectCategory.Minimum,
                    cells: [],
                    constant: new Flt64(5)),
                name: "empty-linear");
            result = await new GurobiLinearSolver().InvokeReportAsync(model);
        }
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(ProblemStatus.Feasible, result.Value.ProblemStatus);
        Assert.Equal(SolutionPresence.Optimal, result.Value.SolutionPresence);
        Assert.NotNull(result.Value.Solution);
        Assert.Empty(result.Value.Solution.Values.Values);
        Assert.Equal(new Flt64(5), result.Value.Solution.Objective);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnboundedStatusIsNotReportedAsInfeasible(bool quadratic) {
        IReadOnlyList<ModelViewVariable> variables = [new ModelViewVariable(
            index: 0,
            lowerBound: Flt64.Zero,
            upperBound: new Flt64(double.PositiveInfinity),
            type: Continuous.Instance,
            name: "unbounded-x")];
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using var model = new QuadraticTetradModel(
                variables: variables,
                constraints: new QuadraticConstraintBatch(
                    lhs: new SparseQuadraticMatrix([]),
                    signs: [],
                    rhs: [],
                    names: [],
                    sources: []),
                objective: new Objective<QuadraticObjectiveCell>(ObjectCategory.Minimum, [new QuadraticObjectiveCell(
                    Col1: 0,
                    Col2: -1,
                    Coefficient: -Flt64.One)]),
                name: "quadratic-unbounded");
            result = await new GurobiQuadraticSolver().InvokeReportAsync(model);
        }
        else {
            using var model = new LinearTriadModel(
                variables: variables,
                constraints: new LinearConstraintBatch(
                    lhs: new SparseMatrix([]),
                    signs: [],
                    rhs: [],
                    names: [],
                    sources: []),
                objective: new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, [new LinearObjectiveCell(0, -Flt64.One)]),
                name: "linear-unbounded");
            result = await new GurobiLinearSolver().InvokeReportAsync(model);
        }
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(ProblemStatus.Unbounded, result.Value.ProblemStatus);
        Assert.Equal(TerminationReason.Completed, result.Value.TerminationReason);
        Assert.Equal(ProofStatus.Claimed, result.Value.Proof.Status);
    }

    [Fact]
    public async Task TimeLimitRetainsAnInitialIncumbent() {
        using LinearTriadModel model = LargeLimitModel();
        var config = new Fuookami.Ospf.Core.Solver.Config.SolverConfig { TimeLimit = TimeSpan.FromMilliseconds(500) };
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, native, _, _) => {
            native.Set(GRB.IntParam.Presolve, 0);
            native.Set(GRB.DoubleParam.Heuristics, 0.0);
            native.Set(GRB.IntParam.Cuts, 0);
            return Task.FromResult(Results.OkInstance);
        });
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new GurobiLinearSolver(config, callback).InvokeReportAsync(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(TerminationReason.TimeLimit, result.Value.TerminationReason);
        Assert.Equal(ProblemStatus.Feasible, result.Value.ProblemStatus);
        Assert.Equal(SolutionPresence.Incumbent, result.Value.SolutionPresence);
        Assert.Equal(ProofStatus.None, result.Value.Proof.Status);
        Assert.NotNull(result.Value.Solution);
        Assert.Equal(1000, result.Value.Solution.Values.Length);
        Assert.NotNull(result.Value.Statistics.BestBound);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NodeLimitRetainsAnInitialIncumbent(bool quadratic) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticLimitModel(withStart: true);
            GurobiQuadraticSolverCallBack callback = new GurobiQuadraticSolverCallBack().Configuration((_, native, _, _) => {
                native.Set(GRB.IntParam.Presolve, 0);
                native.Set(GRB.DoubleParam.Heuristics, 0.0);
                native.Set(GRB.DoubleParam.NodeLimit, 0.0);
                return Task.FromResult(Results.OkInstance);
            });
            result = await new GurobiQuadraticSolver(callBack: callback).InvokeReportAsync(model);
        }
        else {
            using LinearTriadModel model = LinearLimitModel(withStart: true);
            GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, native, _, _) => {
                native.Set(GRB.IntParam.Presolve, 0);
                native.Set(GRB.DoubleParam.Heuristics, 0.0);
                native.Set(GRB.DoubleParam.NodeLimit, 0.0);
                return Task.FromResult(Results.OkInstance);
            });
            result = await new GurobiLinearSolver(callBack: callback).InvokeReportAsync(model);
        }
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(TerminationReason.NodeLimit, result.Value.TerminationReason);
        Assert.Equal(ProblemStatus.Feasible, result.Value.ProblemStatus);
        Assert.Equal(SolutionPresence.Incumbent, result.Value.SolutionPresence);
        Assert.Equal(ProofStatus.None, result.Value.Proof.Status);
        Assert.NotNull(result.Value.Solution);
        Assert.Equal(120, result.Value.Solution.Values.Length);
        Assert.Equal(Flt64.Zero, result.Value.Solution.Objective);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZeroTimeLimitWithoutIncumbentPreservesNativeConclusion(bool quadratic) {
        var config = new Fuookami.Ospf.Core.Solver.Config.SolverConfig { TimeLimit = TimeSpan.Zero };
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticLimitModel(requireHalf: true);
            GurobiQuadraticSolverCallBack callback = new GurobiQuadraticSolverCallBack().Configuration((_, native, _, _) => {
                native.Set(GRB.IntParam.Presolve, 0);
                native.Set(GRB.DoubleParam.Heuristics, 0.0);
                return Task.FromResult(Results.OkInstance);
            });
            result = await new GurobiQuadraticSolver(config, callback).InvokeReportAsync(model);
        }
        else {
            using LinearTriadModel model = LinearLimitModel(requireHalf: true);
            GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, native, _, _) => {
                native.Set(GRB.IntParam.Presolve, 0);
                native.Set(GRB.DoubleParam.Heuristics, 0.0);
                return Task.FromResult(Results.OkInstance);
            });
            result = await new GurobiLinearSolver(config, callback).InvokeReportAsync(model);
        }
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(SolutionPresence.None, result.Value.SolutionPresence);
        Assert.Null(result.Value.Solution);
        if (result.Value.TerminationReason == TerminationReason.TimeLimit) {
            Assert.Equal(ProblemStatus.Unknown, result.Value.ProblemStatus);
            Assert.Equal(ProofStatus.None, result.Value.Proof.Status);
        }
        else {
            Assert.Equal(TerminationReason.Completed, result.Value.TerminationReason);
            Assert.Equal(ProblemStatus.Infeasible, result.Value.ProblemStatus);
            Assert.Equal(ProofStatus.Claimed, result.Value.Proof.Status);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportPoolPreservesSolutions(bool quadratic) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            result = await new GurobiQuadraticSolver().InvokeReportAsync(model, 2);
        }
        else {
            using LinearTriadModel model = LinearModel();
            result = await new GurobiLinearSolver().InvokeReportAsync(model, 2);
        }
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(SolutionPresence.Optimal, result.Value.SolutionPresence);
        Assert.NotNull(result.Value.Solution);
        Assert.Equal(2, result.Value.Solution.Pool.Count);
        Assert.Contains(result.Value.Solution.Pool, item => item[0] == Flt64.Zero);
        Assert.Contains(result.Value.Solution.Pool, item => item[0] == Flt64.One);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationInsideNativeCallbackTerminatesSolve(bool quadratic) {
        using var cancellation = new CancellationTokenSource();
        int called = 0;
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            var callback = new GurobiQuadraticSolverCallBack(nativeCallback: _ => {
                Interlocked.Increment(ref called);
                cancellation.Cancel();
            });
            result = await new GurobiQuadraticSolver(callBack: callback).InvokeReportAsync(
                model: model,
                cancellationToken: cancellation.Token);
        }
        else {
            using LinearTriadModel model = LinearModel();
            var callback = new GurobiLinearSolverCallBack(nativeCallback: _ => {
                Interlocked.Increment(ref called);
                cancellation.Cancel();
            });
            result = await new GurobiLinearSolver(callBack: callback).InvokeReportAsync(
                model: model,
                cancellationToken: cancellation.Token);
        }
        Assert.True(result.IsOk, result.ToString());
        Assert.True(called > 0);
        Assert.Equal(TerminationReason.Cancelled, result.Value.TerminationReason);
        Assert.Equal(ProofStatus.None, result.Value.Proof.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringConfigurationDoesNotOptimize(bool quadratic) {
        using var cancellation = new CancellationTokenSource();
        int nativeCalls = 0;
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            GurobiQuadraticSolverCallBack callback = new GurobiQuadraticSolverCallBack(nativeCallback: _ => nativeCalls++)
                .Configuration((_, _, _, _) => {
                    cancellation.Cancel();
                    return Task.FromResult(Results.OkInstance);
                });
            result = await new GurobiQuadraticSolver(callBack: callback).InvokeReportAsync(
                model: model,
                cancellationToken: cancellation.Token);
        }
        else {
            using LinearTriadModel model = LinearModel();
            GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack(nativeCallback: _ => nativeCalls++)
                .Configuration((_, _, _, _) => {
                    cancellation.Cancel();
                    return Task.FromResult(Results.OkInstance);
                });
            result = await new GurobiLinearSolver(callBack: callback).InvokeReportAsync(
                model: model,
                cancellationToken: cancellation.Token);
        }
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(TerminationReason.Cancelled, result.Value.TerminationReason);
        Assert.Equal(SolutionPresence.None, result.Value.SolutionPresence);
        Assert.Equal(0, nativeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCancellationCannotOverwriteCompletedStatus(bool quadratic) {
        using var cancellation = new CancellationTokenSource();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            GurobiQuadraticSolverCallBack callback = new GurobiQuadraticSolverCallBack().AnalyzingSolution((_, _, _, _) => {
                cancellation.Cancel();
                return Task.FromResult(Results.OkInstance);
            });
            result = await new GurobiQuadraticSolver(callBack: callback).InvokeReportAsync(
                model: model,
                cancellationToken: cancellation.Token);
        }
        else {
            using LinearTriadModel model = LinearModel();
            GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AnalyzingSolution((_, _, _, _) => {
                cancellation.Cancel();
                return Task.FromResult(Results.OkInstance);
            });
            result = await new GurobiLinearSolver(callBack: callback).InvokeReportAsync(
                model: model,
                cancellationToken: cancellation.Token);
        }
        Assert.True(result.IsOk, result.ToString());
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(TerminationReason.Completed, result.Value.TerminationReason);
        Assert.Equal(SolutionPresence.Optimal, result.Value.SolutionPresence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterSolveDoesNotTouchDisposedNativeModel(bool quadratic) {
        using var cancellation = new CancellationTokenSource();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            result = await new GurobiQuadraticSolver().InvokeReportAsync(
                model: model,
                cancellationToken: cancellation.Token);
        }
        else {
            using LinearTriadModel model = LinearModel();
            result = await new GurobiLinearSolver().InvokeReportAsync(
                model: model,
                cancellationToken: cancellation.Token);
        }
        Assert.True(result.IsOk, result.ToString());
        cancellation.Cancel();
        Assert.Equal(TerminationReason.Completed, result.Value.TerminationReason);
        Assert.Equal(SolutionPresence.Optimal, result.Value.SolutionPresence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedPoolIsRejectedBeforeCreatingEnvironment(bool quadratic) {
        int environments = 0;
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (quadratic) {
            using QuadraticTetradModel model = QuadraticModel();
            var callback = new GurobiQuadraticSolverCallBack(creatingEnvironmentFunction: _ => {
                environments++;
                return Results.OkInstance;
            });
            result = await new GurobiQuadraticSolver(callBack: callback).InvokeReportAsync(
                model: model,
                solutionAmount: (ulong)int.MaxValue + 1);
        }
        else {
            using LinearTriadModel model = LinearModel();
            var callback = new GurobiLinearSolverCallBack(creatingEnvironmentFunction: _ => {
                environments++;
                return Results.OkInstance;
            });
            result = await new GurobiLinearSolver(callBack: callback).InvokeReportAsync(
                model: model,
                solutionAmount: (ulong)int.MaxValue + 1);
        }
        Assert.Equal(ErrorCode.IllegalArgument,
            Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(result).Code);
        Assert.Equal(0, environments);
    }

    [Fact]
    public async Task ConcurrentCancellationIsDetachedBeforeNativeDisposal() {
        using var cancellation = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using LinearTriadModel model = LinearModel();
        var callback = new GurobiLinearSolverCallBack(nativeCallback: _ => {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        });
        Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> solve = Task.Run(() =>
            new GurobiLinearSolver(callBack: callback).InvokeReportAsync(
                model: model,
                cancellationToken: cancellation.Token));
        try {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(10))));
            var cancel = Task.Run(cancellation.Cancel);
            await Task.WhenAny(cancel, Task.Delay(TimeSpan.FromSeconds(1)));
            release.Set();
            await cancel.WaitAsync(TimeSpan.FromSeconds(10));
            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await solve.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.IsOk, result.ToString());
            Assert.Equal(TerminationReason.Cancelled, result.Value.TerminationReason);
        }
        finally {
            release.Set();
        }
    }

    private static LinearTriadModel LinearModel(bool infeasible = false) => new(
        variables: Variables(),
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix(infeasible ? [new SparseVector([new SparseVectorEntry(0, Flt64.One)])] : []),
            signs: infeasible ? [ConstraintRelation.GreaterEqual] : [],
            rhs: infeasible ? [new Flt64(2)] : [],
            names: infeasible ? ["impossible"] : [],
            sources: infeasible ? [ConstraintSource.Origin] : []),
        objective: new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, [new LinearObjectiveCell(0, -Flt64.One)]),
        name: "linear-report");

    private static QuadraticTetradModel QuadraticModel(bool infeasible = false) => new(
        variables: Variables(),
        constraints: new QuadraticConstraintBatch(
            lhs: new SparseQuadraticMatrix(infeasible ? [new SparseQuadraticVector([new SparseQuadraticEntry(
                Col1: 0,
                Col2: -1,
                Value: Flt64.One)])] : []),
            signs: infeasible ? [ConstraintRelation.GreaterEqual] : [],
            rhs: infeasible ? [new Flt64(2)] : [],
            names: infeasible ? ["impossible"] : [],
            sources: infeasible ? [ConstraintSource.Origin] : []),
        objective: new Objective<QuadraticObjectiveCell>(ObjectCategory.Minimum, [new QuadraticObjectiveCell(
            Col1: 0,
            Col2: -1,
            Coefficient: -Flt64.One)]),
        name: "quadratic-report");

    private static IReadOnlyList<ModelViewVariable> Variables() => [new ModelViewVariable(
        index: 0,
        lowerBound: Flt64.Zero,
        upperBound: Flt64.One,
        type: Binary.Instance,
        name: "x")];

    private static IReadOnlyList<ModelViewVariable> LimitVariables(bool withStart) {
        var variables = new List<ModelViewVariable>();
        for (int i = 0; i < 120; i++) {
            variables.Add(new ModelViewVariable(
                index: i,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: $"x-{i}",
                initialResult: withStart ? Flt64.Zero : null));
        }
        return variables;
    }

    private static LinearTriadModel LinearLimitModel(bool withStart = false, bool requireHalf = false) {
        var row = new SparseVector();
        var objective = new List<LinearObjectiveCell>();
        for (int i = 0; i < 120; i++) {
            row.Entries.Add(new SparseVectorEntry(i, Flt64.One));
            objective.Add(new LinearObjectiveCell(i, new Flt64(-i - 1)));
        }
        return new LinearTriadModel(
            variables: LimitVariables(withStart),
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix([row]),
                signs: [requireHalf ? ConstraintRelation.Equal : ConstraintRelation.LessEqual],
                rhs: [new Flt64(requireHalf ? 0.5 : 60)],
                names: ["capacity"],
                sources: [ConstraintSource.Origin]),
            objective: new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, objective),
            name: "linear-limit");
    }

    private static QuadraticTetradModel QuadraticLimitModel(bool withStart = false, bool requireHalf = false) {
        var row = new SparseQuadraticVector();
        var objective = new List<QuadraticObjectiveCell>();
        for (int i = 0; i < 120; i++) {
            row.Entries.Add(new SparseQuadraticEntry(
                Col1: i,
                Col2: -1,
                Value: Flt64.One));
            objective.Add(new QuadraticObjectiveCell(
                Col1: i,
                Col2: -1,
                Coefficient: new Flt64(-i - 1)));
        }
        return new QuadraticTetradModel(
            variables: LimitVariables(withStart),
            constraints: new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix([row]),
                signs: [requireHalf ? ConstraintRelation.Equal : ConstraintRelation.LessEqual],
                rhs: [new Flt64(requireHalf ? 0.5 : 60)],
                names: ["capacity"],
                sources: [ConstraintSource.Origin]),
            objective: new Objective<QuadraticObjectiveCell>(ObjectCategory.Minimum, objective),
            name: "quadratic-limit");
    }

    private static LinearTriadModel LargeLimitModel() {
        var variables = new List<ModelViewVariable>();
        var objective = new List<LinearObjectiveCell>();
        for (int i = 0; i < 1000; i++) {
            variables.Add(new ModelViewVariable(
                index: i,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: $"x-{i}",
                initialResult: Flt64.Zero));
            objective.Add(new LinearObjectiveCell(i, new Flt64(-i - 1)));
        }
        var rows = new List<SparseVector>();
        var signs = new List<ConstraintRelation>();
        var rhs = new List<Flt64>();
        var names = new List<string>();
        var sources = new List<ConstraintSource>();
        for (int rowIndex = 0; rowIndex < 120; rowIndex++) {
            var row = new SparseVector();
            for (int i = 0; i < variables.Count; i++) {
                if ((i * 37 + rowIndex * 101 + i * rowIndex * 13) % 1009 < 60) {
                    row.Entries.Add(new SparseVectorEntry(i, Flt64.One));
                }
            }
            rows.Add(row);
            signs.Add(ConstraintRelation.LessEqual);
            rhs.Add(new Flt64(18));
            names.Add($"capacity-{rowIndex}");
            sources.Add(ConstraintSource.Origin);
        }
        return new LinearTriadModel(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(rows),
                signs: signs,
                rhs: rhs,
                names: names,
                sources: sources),
            objective: new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, objective),
            name: "linear-large-limit");
    }

    private sealed class ZeroConverter : IIntoValue<Flt64> {
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => Flt64.Zero;
        public Flt64 FromValue(Flt64 value) => value;
    }
}
#endif
