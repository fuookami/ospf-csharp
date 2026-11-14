#nullable enable

#if SCIP_INTEGRATION

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

public class ScipConstraintProgrammingIntegrationTest {
    [Fact]
    public async Task ExactIntegerModelShouldReturnOptimalSolutionAndExactObjective() {
        using var model = new ConstraintProgrammingModel(
            name: "scip-cp-optimal",
            objectCategory: ObjectCategory.Minimum);
        (VariableId xId, ConstraintProgrammingExpression x) = Register(
            model: model,
            name: "x",
            lowerBound: 0,
            upperBound: 5);
        (VariableId yId, ConstraintProgrammingExpression y) = Register(
            model: model,
            name: "y",
            lowerBound: 5,
            upperBound: 5);
        Assert.True(model.AddConstraint(
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(
                y,
                new Int64(5)).Value).IsOk);
        Assert.True(model.Minimize(x).IsOk);

        Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> result = await new ScipConstraintProgrammingSolver()
            .SolveAsync(model, Options());
        ConstraintProgrammingFeasibleOutput output = Feasible(result);

        Assert.Equal(Int64.Zero, output.Solution.Value(xId).Value);
        Assert.Equal(new Int64(5), output.Solution.Value(yId).Value);
        Assert.Equal(Int64.Zero, output.ExactObjective);
        Assert.Equal(SolverStatus.Optimal, output.Status);
        Assert.Equal(SolutionPresence.Optimal, output.Report!.SolutionPresence);
        Assert.Equal(ProblemStatus.Feasible, output.Report.ProblemStatus);
        Assert.Equal(ProofStatus.Claimed, output.ProofStatus);
        Assert.Equal("scip-cp", output.Report.Proof.Kind);
    }

    [Fact]
    public async Task FixedValuesAndFalseAssumptionShouldRemainSessionLocal() {
        using var model = new ConstraintProgrammingModel(
            name: "scip-cp-fixed",
            objectCategory: ObjectCategory.Minimum);
        (VariableId xId, ConstraintProgrammingExpression x) = Register(
            model: model,
            name: "fixed-x",
            lowerBound: 0,
            upperBound: 3);
        Assert.True(model.Minimize(x).IsOk);
        using IConstraintProgrammingSession session = Assert.IsType<
            Ok<
                IConstraintProgrammingSession,
                ErrorCode,
                Error<ErrorCode>>>(
            new ScipConstraintProgrammingSolver().CreateSession(model, Options())).Value;

        Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> fixedResult = await session.SolveAsync(
            fixedValues: new Dictionary<VariableId, Int64> {
                [xId] = new Int64(3),
            });
        ConstraintProgrammingFeasibleOutput fixedOutput = Feasible(fixedResult);
        Assert.Equal(new Int64(3), fixedOutput.ExactObjective);

        Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> assumptionResult = await session.SolveAsync(
            assumptions: new[] { BooleanLiteral.False });
        ConstraintProgrammingInfeasibleOutput infeasible = Assert.IsType<
            ConstraintProgrammingInfeasibleOutput>(
            Assert.IsType<
                Ok<
                    ConstraintProgrammingSolverOutput,
                    ErrorCode,
                    Error<ErrorCode>>>(assumptionResult).Value);
        Assert.Equal(ProblemStatus.Infeasible, infeasible.Report!.ProblemStatus);
        Assert.Equal(TerminationReason.Completed, infeasible.Report.TerminationReason);

        Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> resetResult = await session.SolveAsync();
        Assert.Equal(Int64.Zero, Feasible(resetResult).ExactObjective);
    }

    [Fact]
    public async Task ScalarAndIntervalHintsShouldBeAcceptedWithoutChangingOptimality() {
        using var scalarModel = new ConstraintProgrammingModel(
            name: "scip-cp-scalar-hint",
            objectCategory: ObjectCategory.Minimum);
        (VariableId scalarId, ConstraintProgrammingExpression scalar) = Register(
            model: scalarModel,
            name: "hinted",
            lowerBound: 0,
            upperBound: 5);
        Assert.True(scalarModel.Minimize(scalar).IsOk);
        using IConstraintProgrammingSession scalarSession = Assert.IsType<
            Ok<
                IConstraintProgrammingSession,
                ErrorCode,
                Error<ErrorCode>>>(
            new ScipConstraintProgrammingSolver().CreateSession(
                scalarModel,
                Options())).Value;
        ConstraintProgrammingFeasibleOutput scalarOutput = Feasible(
            await scalarSession.SolveAsync(
                hints: new ConstraintProgrammingSolution(
                    values: new Dictionary<VariableId, Int64> {
                        [scalarId] = new Int64(3),
                    })));
        Assert.Equal(Int64.Zero, scalarOutput.ExactObjective);

        using var intervalModel = new ConstraintProgrammingModel(
            name: "scip-cp-interval-hint",
            objectCategory: ObjectCategory.Minimum);
        (VariableId startId, ConstraintProgrammingExpression start) = Register(
            model: intervalModel,
            name: "start",
            lowerBound: 0,
            upperBound: 5);
        (VariableId endId, ConstraintProgrammingExpression end) = Register(
            model: intervalModel,
            name: "end",
            lowerBound: 2,
            upperBound: 7);
        IntervalVariable interval = IntervalVariable.CreateFixed(
            id: "window",
            start: start,
            size: new Int64(2),
            end: end).Value;
        Assert.True(intervalModel.RegisterInterval(interval).IsOk);
        Assert.True(intervalModel.Minimize(start).IsOk);
        using IConstraintProgrammingSession intervalSession = Assert.IsType<
            Ok<
                IConstraintProgrammingSession,
                ErrorCode,
                Error<ErrorCode>>>(
            new ScipConstraintProgrammingSolver().CreateSession(
                intervalModel,
                Options())).Value;
        ConstraintProgrammingFeasibleOutput intervalOutput = Feasible(
            await intervalSession.SolveAsync(
                hints: new ConstraintProgrammingSolution(
                    values: new Dictionary<VariableId, Int64> {
                        [startId] = new Int64(2),
                        [endId] = new Int64(4),
                    },
                    intervals: new Dictionary<IntervalId, IntervalValue> {
                        [interval.Id] = new IntervalValue(
                            Start: new Int64(2),
                            Size: new Int64(2),
                            End: new Int64(4)),
                    })));
        IntervalValue solvedInterval = intervalOutput.Solution.Interval(interval.Id).Value;
        Assert.Equal(Int64.Zero, solvedInterval.Start);
        Assert.Equal(new Int64(2), solvedInterval.End);
        Assert.Equal(Int64.Zero, intervalOutput.ExactObjective);
    }

    [Fact]
    public async Task AllDifferentAndTableShouldBeEnforcedByTheExactLowering() {
        using var model = new ConstraintProgrammingModel(
            name: "scip-cp-global",
            objectCategory: ObjectCategory.Minimum);
        (VariableId xId, ConstraintProgrammingExpression x) = Register(
            model: model,
            name: "table-x",
            lowerBound: 0,
            upperBound: 2);
        (VariableId yId, ConstraintProgrammingExpression y) = Register(
            model: model,
            name: "table-y",
            lowerBound: 0,
            upperBound: 2);
        Assert.True(model.AddConstraint(
            ConstraintProgrammingConstraint.CreateAllDifferent(
                new[] { x, y }).Value).IsOk);
        Assert.True(model.AddConstraint(
            ConstraintProgrammingConstraint.CreateAllowedAssignments(
                expressions: new[] { x, y },
                tuples: new[] {
                    new[] { Int64.Zero, Int64.One },
                    new[] { Int64.One, new Int64(2) },
                    new[] { new Int64(2), Int64.Zero },
                }).Value).IsOk);
        Assert.True(model.Minimize(x).IsOk);

        ConstraintProgrammingFeasibleOutput output = Feasible(
            await new ScipConstraintProgrammingSolver().SolveAsync(
                model,
                Options()));
        Int64 solvedX = output.Solution.Value(xId).Value;
        Int64 solvedY = output.Solution.Value(yId).Value;
        Assert.Equal(Int64.Zero, solvedX);
        Assert.Equal(Int64.One, solvedY);
        Assert.NotEqual(solvedX, solvedY);
    }

    private static ConstraintProgrammingSolveOptions Options() => new(
        TimeLimit: TimeSpan.FromSeconds(20),
        ThreadCount: 1,
        Deterministic: true);

    private static (VariableId Id, ConstraintProgrammingExpression Expression) Register(
        ConstraintProgrammingModel model,
        string name,
        long lowerBound,
        long upperBound) {
        var variable = new IntVar(name);
        VariableId id = Assert.IsType<
            Ok<
                VariableId,
                ErrorCode,
                Error<ErrorCode>>>(
            model.RegisterVariable(
                variable: variable,
                domain: IntegerDomain.CreateInterval(
                    lowerBound: new Int64(lowerBound),
                    upperBound: new Int64(upperBound)).Value)).Value;
        return (
            id,
            ConstraintProgrammingExpression.CreateVariable(variable).Value);
    }

    private static ConstraintProgrammingFeasibleOutput Feasible(
        Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> result) =>
        Assert.IsType<ConstraintProgrammingFeasibleOutput>(
            Assert.IsType<
                Ok<
                    ConstraintProgrammingSolverOutput,
                    ErrorCode,
                    Error<ErrorCode>>>(result).Value);
}

#endif
