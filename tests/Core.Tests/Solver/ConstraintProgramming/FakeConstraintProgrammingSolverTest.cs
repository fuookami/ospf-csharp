#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Solver.ConstraintProgramming;

public class FakeConstraintProgrammingSolverTest {
    [Fact]
    public async Task ShouldEnumerateModelAndReturnExactOptimalSolution() {
        using var model = new ConstraintProgrammingModel(
            name: "fake-optimal",
            objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar("x");
        IntegerDomain domain = IntegerDomain.CreateInterval(
            lowerBound: Int64.Zero,
            upperBound: new Int64(3)).Value;
        VariableId id = model.RegisterVariable(variable, domain).Value;
        ConstraintProgrammingExpression expression =
            ConstraintProgrammingExpression.CreateVariable(variable).Value;
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(
                expression,
                Int64.One).Value).IsOk.Should().BeTrue();
        model.Minimize(expression).IsOk.Should().BeTrue();

        var solver = new FakeConstraintProgrammingSolver();
        Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> result = await solver.SolveAsync(model);

        result.Should().BeOfType<
            Ok<
                ConstraintProgrammingSolverOutput,
                ErrorCode,
                Error<ErrorCode>>>();
        var output = (ConstraintProgrammingFeasibleOutput)result.Value;
        output.Solution.Value(id).Value.Should().Be(Int64.One);
        output.ExactObjective.Should().Be(Int64.One);
        output.Status.Should().Be(SolverStatus.Optimal);
        output.ProofStatus.Should().Be(ProofStatus.Verified);
        output.Report!.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        output.Report.TerminationReason.Should().Be(TerminationReason.Completed);
        output.Report.SolutionPresence.Should().Be(SolutionPresence.Optimal);
        solver.Descriptor.Capabilities.ModelTypes.Should().Contain(SolverModelType.Cp);
    }

    [Fact]
    public async Task ShouldRespectMaximumObjectiveDirection() {
        using var model = new ConstraintProgrammingModel(
            name: "fake-maximum",
            objectCategory: ObjectCategory.Maximum);
        var variable = new IntVar("maximum-value");
        IntegerDomain domain = IntegerDomain.CreateInterval(
            lowerBound: Int64.Zero,
            upperBound: new Int64(3)).Value;
        VariableId id = model.RegisterVariable(variable, domain).Value;
        ConstraintProgrammingExpression expression =
            ConstraintProgrammingExpression.CreateVariable(variable).Value;
        model.Maximize(expression).IsOk.Should().BeTrue();

        var output = (ConstraintProgrammingFeasibleOutput)(await new FakeConstraintProgrammingSolver()
            .SolveAsync(model)).Value;

        output.Solution.Value(id).Value.Should().Be(new Int64(3));
        output.ExactObjective.Should().Be(new Int64(3));
        output.Status.Should().Be(SolverStatus.Optimal);
    }

    [Fact]
    public async Task ShouldReturnInfeasibleAndUnknownWithoutThrowing() {
        using var infeasibleModel = new ConstraintProgrammingModel(name: "fake-infeasible");
        var variable = new IntVar("x");
        infeasibleModel.RegisterVariable(
            variable,
            IntegerDomain.CreateSingleton(Int64.Zero)).IsOk.Should().BeTrue();
        ConstraintProgrammingExpression expression =
            ConstraintProgrammingExpression.CreateVariable(variable).Value;
        infeasibleModel.AddConstraint(
            ConstraintProgrammingConstraint.CreateEqual(
                expression,
                Int64.One).Value).IsOk.Should().BeTrue();

        var infeasible = (ConstraintProgrammingInfeasibleOutput)(
            await new FakeConstraintProgrammingSolver().SolveAsync(infeasibleModel)).Value;
        infeasible.Conflict!.ConstraintIds.Should().ContainSingle();
        infeasible.ProofStatus.Should().Be(ProofStatus.Verified);
        infeasible.Report!.TerminationReason.Should().Be(TerminationReason.Completed);

        using var largeModel = new ConstraintProgrammingModel(name: "fake-unknown");
        largeModel.RegisterVariable(
            new IntVar("large"),
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(10)).Value).IsOk.Should().BeTrue();
        var unknown = (ConstraintProgrammingUnknownOutput)(
            await new FakeConstraintProgrammingSolver(enumerationLimit: 1)
                .SolveAsync(largeModel)).Value;
        unknown.TerminationReason.Should().Be(TerminationReason.NodeLimit);
        unknown.Report!.SolutionPresence.Should().Be(SolutionPresence.None);
    }

    [Fact]
    public async Task SessionShouldReuseAndCloseIdempotently() {
        using var model = new ConstraintProgrammingModel(name: "fake-session");
        var variable = new IntVar("session-value");
        IntegerDomain domain = IntegerDomain.CreateInterval(
            lowerBound: Int64.Zero,
            upperBound: new Int64(2)).Value;
        VariableId id = model.RegisterVariable(variable, domain).Value;
        var solver = new FakeConstraintProgrammingSolver();
        using IConstraintProgrammingSession session = solver.CreateSession(model).Value;

        var first = (ConstraintProgrammingFeasibleOutput)(await session.SolveAsync()).Value;
        var second = (ConstraintProgrammingFeasibleOutput)(await session.SolveAsync()).Value;

        first.Solution.Value(id).Value.Should().Be(Int64.Zero);
        second.Solution.Value(id).Value.Should().Be(Int64.Zero);
        session.Dispose();
        session.IsClosed.Should().BeTrue();
        session.Dispose();
        (await session.SolveAsync()).Should().BeOfType<
            Failed<
                ConstraintProgrammingSolverOutput,
                ErrorCode,
                Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    [Fact]
    public async Task FixedValuesAssumptionsAndHintsShouldRemainExactAndBoundaryChecked() {
        using var model = new ConstraintProgrammingModel(name: "fake-boundaries");
        var value = new IntVar("value");
        var gate = new BinVar("gate");
        IntegerDomain domain = IntegerDomain.CreateInterval(
            lowerBound: Int64.Zero,
            upperBound: new Int64(3)).Value;
        VariableId valueId = model.RegisterVariable(value, domain).Value;
        VariableId gateId = model.RegisterVariable(gate, IntegerDomain.Boolean).Value;
        ConstraintProgrammingExpression expression =
            ConstraintProgrammingExpression.CreateVariable(value).Value;
        BooleanLiteral literal = new BooleanLiteral.VariableLiteral(gate);
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(
                expression,
                new Int64(2)).Value).IsOk.Should().BeTrue();
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateLiteral(literal).Value).IsOk.Should().BeTrue();
        using IConstraintProgrammingSession session =
            new FakeConstraintProgrammingSolver().CreateSession(model).Value;

        var output = (ConstraintProgrammingFeasibleOutput)(
            await session.SolveAsync(
                assumptions: new[] { literal },
                fixedValues: new Dictionary<VariableId, Int64> {
                    [valueId] = new Int64(3),
                },
                hints: new ConstraintProgrammingSolution(
                    values: new Dictionary<VariableId, Int64> {
                        [valueId] = new Int64(3),
                        [gateId] = Int64.One,
                    }))).Value;

        output.Solution.Value(valueId).Value.Should().Be(new Int64(3));
        output.Solution.Boolean(gate).Value.Should().BeTrue();

        (await session.SolveAsync(
            fixedValues: new Dictionary<VariableId, Int64> {
                [valueId] = new Int64(4),
            })).Should().BeOfType<
                Failed<
                    ConstraintProgrammingSolverOutput,
                    ErrorCode,
                    Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ORSolutionInvalid);
        (await session.SolveAsync(
            assumptions: new[] {
                new BooleanLiteral.VariableLiteral(new BinVar("missing")),
            })).Should().BeOfType<
                Failed<
                    ConstraintProgrammingSolverOutput,
                    ErrorCode,
                    Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        (await session.SolveAsync(
            hints: new ConstraintProgrammingSolution(
                values: new Dictionary<VariableId, Int64> {
                    [valueId] = new Int64(99),
                }))).Should().BeOfType<
                    Failed<
                        ConstraintProgrammingSolverOutput,
                        ErrorCode,
                        Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ORSolutionInvalid);
    }

    [Fact]
    public async Task CompleteHintShouldSeedIncumbentWhenEnumerationIsTruncated() {
        using var model = new ConstraintProgrammingModel(
            name: "fake-hint-incumbent",
            objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar("hinted");
        VariableId id = model.RegisterVariable(
            variable,
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(10)).Value).Value;
        ConstraintProgrammingExpression expression =
            ConstraintProgrammingExpression.CreateVariable(variable).Value;
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(
                expression,
                new Int64(5)).Value).IsOk.Should().BeTrue();
        model.Minimize(expression).IsOk.Should().BeTrue();
        using IConstraintProgrammingSession session =
            new FakeConstraintProgrammingSolver().CreateSession(
                model,
                new ConstraintProgrammingSolveOptions(NodeLimit: 1)).Value;

        var output = (ConstraintProgrammingFeasibleOutput)(await session.SolveAsync(
            hints: new ConstraintProgrammingSolution(
                values: new Dictionary<VariableId, Int64> {
                    [id] = new Int64(7),
                }))).Value;

        output.Solution.Value(id).Value.Should().Be(new Int64(7));
        output.ExactObjective.Should().Be(new Int64(7));
        output.Status.Should().Be(SolverStatus.Feasible);
        output.ProofStatus.Should().Be(ProofStatus.None);
        output.Report!.TerminationReason.Should().Be(TerminationReason.NodeLimit);
    }

    [Fact]
    public async Task CancellationAndSolutionLimitShouldBeReportedAsStoppedStates() {
        using var model = new ConstraintProgrammingModel(
            name: "fake-stop",
            objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar("stop-value");
        VariableId id = model.RegisterVariable(
            variable,
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(3)).Value).Value;
        model.Minimize(
            ConstraintProgrammingExpression.CreateVariable(variable).Value).IsOk.Should().BeTrue();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = (ConstraintProgrammingUnknownOutput)(await new FakeConstraintProgrammingSolver()
            .SolveAsync(model, cancellationToken: cancellation.Token)).Value;
        cancelled.TerminationReason.Should().Be(TerminationReason.Cancelled);
        cancelled.Report!.SolutionPresence.Should().Be(SolutionPresence.None);

        using IConstraintProgrammingSession session =
            new FakeConstraintProgrammingSolver().CreateSession(
                model,
                new ConstraintProgrammingSolveOptions(SolutionLimit: 1)).Value;
        var limited = (ConstraintProgrammingFeasibleOutput)(await session.SolveAsync()).Value;
        limited.Solution.Value(id).Value.Should().Be(Int64.Zero);
        limited.Status.Should().Be(SolverStatus.Feasible);
        limited.ProofStatus.Should().Be(ProofStatus.None);
        limited.Report!.TerminationReason.Should().Be(TerminationReason.SolutionLimit);
    }

    [Fact]
    public async Task FalseAssumptionAndInvalidIntervalHintShouldFailStructurally() {
        using var assumedModel = new ConstraintProgrammingModel(name: "fake-false-assumption");
        assumedModel.RegisterVariable(
            new IntVar("value"),
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: Int64.One).Value).IsOk.Should().BeTrue();
        using IConstraintProgrammingSession assumedSession =
            new FakeConstraintProgrammingSolver().CreateSession(assumedModel).Value;
        var assumed = (ConstraintProgrammingInfeasibleOutput)(await assumedSession.SolveAsync(
            assumptions: new[] { BooleanLiteral.False })).Value;
        assumed.ProofStatus.Should().Be(ProofStatus.Verified);
        assumed.Report!.ProblemStatus.Should().Be(ProblemStatus.Infeasible);

        using var intervalModel = new ConstraintProgrammingModel(name: "fake-invalid-interval-hint");
        var start = new IntVar("start");
        var end = new IntVar("end");
        ConstraintProgrammingExpression startExpression =
            ConstraintProgrammingExpression.CreateVariable(start).Value;
        ConstraintProgrammingExpression endExpression =
            ConstraintProgrammingExpression.CreateVariable(end).Value;
        intervalModel.RegisterVariable(
            start,
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(3)).Value).IsOk.Should().BeTrue();
        intervalModel.RegisterVariable(
            end,
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(4)).Value).IsOk.Should().BeTrue();
        IntervalVariable interval = IntervalVariable.CreateFixed(
            id: "interval",
            start: startExpression,
            size: new Int64(2),
            end: endExpression).Value;
        intervalModel.RegisterInterval(interval).IsOk.Should().BeTrue();

        using IConstraintProgrammingSession intervalSession =
            new FakeConstraintProgrammingSolver().CreateSession(intervalModel).Value;
        Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> result = await intervalSession.SolveAsync(
            hints: new ConstraintProgrammingSolution(
                values: new Dictionary<VariableId, Int64>(),
                intervals: new Dictionary<IntervalId, IntervalValue> {
                    [interval.Id] = new IntervalValue(
                        Start: Int64.Zero,
                        Size: new Int64(2),
                        End: new Int64(3)),
                }));

        result.Should().BeOfType<
            Failed<
                ConstraintProgrammingSolverOutput,
                ErrorCode,
                Error<ErrorCode>>>();
    }
}
