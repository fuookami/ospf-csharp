#nullable enable
#if GUROBI_AVAILABLE

using Gurobi;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class GurobiReportStatusTest {
    [Theory]
    [InlineData(GRB.Status.OPTIMAL, true, true, ProblemStatus.Feasible, TerminationReason.Completed, SolutionPresence.Optimal, ProofStatus.Claimed)]
    [InlineData(GRB.Status.INFEASIBLE, false, true, ProblemStatus.Infeasible, TerminationReason.Completed, SolutionPresence.None, ProofStatus.Claimed)]
    [InlineData(GRB.Status.UNBOUNDED, false, true, ProblemStatus.Unbounded, TerminationReason.Completed, SolutionPresence.None, ProofStatus.Claimed)]
    [InlineData(GRB.Status.INF_OR_UNBD, false, true, ProblemStatus.InfeasibleOrUnbounded, TerminationReason.Completed, SolutionPresence.None, ProofStatus.Claimed)]
    [InlineData(GRB.Status.TIME_LIMIT, false, false, ProblemStatus.Unknown, TerminationReason.TimeLimit, SolutionPresence.None, ProofStatus.None)]
    [InlineData(GRB.Status.TIME_LIMIT, true, true, ProblemStatus.Feasible, TerminationReason.TimeLimit, SolutionPresence.Incumbent, ProofStatus.None)]
    [InlineData(GRB.Status.NODE_LIMIT, false, false, ProblemStatus.Unknown, TerminationReason.NodeLimit, SolutionPresence.None, ProofStatus.None)]
    [InlineData(GRB.Status.ITERATION_LIMIT, false, false, ProblemStatus.Unknown, TerminationReason.IterationLimit, SolutionPresence.None, ProofStatus.None)]
    [InlineData(GRB.Status.SOLUTION_LIMIT, true, false, ProblemStatus.Feasible, TerminationReason.SolutionLimit, SolutionPresence.Incumbent, ProofStatus.None)]
    [InlineData(GRB.Status.USER_OBJ_LIMIT, false, false, ProblemStatus.Unknown, TerminationReason.ObjectiveLimit, SolutionPresence.None, ProofStatus.None)]
    [InlineData(GRB.Status.CUTOFF, false, false, ProblemStatus.Unknown, TerminationReason.ObjectiveLimit, SolutionPresence.None, ProofStatus.None)]
    [InlineData(GRB.Status.INTERRUPTED, true, true, ProblemStatus.Feasible, TerminationReason.Cancelled, SolutionPresence.Incumbent, ProofStatus.None)]
    [InlineData(GRB.Status.INTERRUPTED, false, false, ProblemStatus.Unknown, TerminationReason.Interrupted, SolutionPresence.None, ProofStatus.None)]
    [InlineData(GRB.Status.NUMERIC, true, false, ProblemStatus.Feasible, TerminationReason.NumericalFailure, SolutionPresence.Incumbent, ProofStatus.None)]
    [InlineData(GRB.Status.SUBOPTIMAL, false, false, ProblemStatus.Unknown, TerminationReason.NumericalFailure, SolutionPresence.None, ProofStatus.None)]
    [InlineData(-42, false, true, ProblemStatus.Unknown, TerminationReason.BackendFailure, SolutionPresence.None, ProofStatus.None)]
    public void NativeStatusKeepsIndependentDimensions(
        int status,
        bool hasSolution,
        bool cancelled,
        ProblemStatus problem,
        TerminationReason termination,
        SolutionPresence presence,
        ProofStatus proof) {
        SolveSolution<Flt64>? solution = hasSolution
            ? new SolveSolution<Flt64>(new Solution<Flt64>([Flt64.One]), Flt64.One)
            : null;
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = GurobiStatusMapping.Create(
            nativeStatus: status,
            solution: solution,
            statistics: new SolveStatistics(),
            cancelledDuringSolve: cancelled);
        Assert.True(report.IsOk, report.ToString());
        Assert.Equal(problem, report.Value.ProblemStatus);
        Assert.Equal(termination, report.Value.TerminationReason);
        Assert.Equal(presence, report.Value.SolutionPresence);
        Assert.Equal(proof, report.Value.Proof.Status);
        Assert.Equal(hasSolution, report.Value.Solution is not null);
        Assert.Equal(termination == TerminationReason.BackendFailure, report.Value.Diagnostics.Errors.Count > 0);
    }

    [Theory]
    [InlineData(GRB.Status.OPTIMAL, false)]
    [InlineData(GRB.Status.INFEASIBLE, true)]
    [InlineData(GRB.Status.INF_OR_UNBD, true)]
    public void ContradictoryNativeStateFails(int status, bool hasSolution) {
        SolveSolution<Flt64>? solution = hasSolution
            ? new SolveSolution<Flt64>(new Solution<Flt64>([]), Flt64.Zero)
            : null;
        Assert.True(GurobiStatusMapping.Create(
            nativeStatus: status,
            solution: solution,
            statistics: new SolveStatistics(),
            cancelledDuringSolve: false).IsFailed);
    }

    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(1.0, double.NegativeInfinity)]
    public void NonfiniteNativeSolutionFails(double value, double objective) {
        var solution = new SolveSolution<Flt64>(new Solution<Flt64>([new Flt64(value)]), new Flt64(objective));
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = GurobiStatusMapping.Create(
            nativeStatus: GRB.Status.OPTIMAL,
            solution: solution,
            statistics: new SolveStatistics(),
            cancelledDuringSolve: false);
        Assert.Equal(ErrorCode.ORSolutionInvalid,
            Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(result).Code);
    }
}
#endif
