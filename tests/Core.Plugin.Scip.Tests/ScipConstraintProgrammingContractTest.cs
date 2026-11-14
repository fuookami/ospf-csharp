#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

public class ScipConstraintProgrammingContractTest {
    [Fact]
    public void DescriptorShouldExposeOnlyExactLoweringAndNoNativeCpClaims() {
        var solver = new ScipConstraintProgrammingSolver();

        Assert.Equal("scip-cp", solver.Descriptor.SolverId);
        Assert.Equal("SCIP", solver.Descriptor.BackendName);
        Assert.Equal(
            new[] { SolverModelType.Cp },
            solver.Descriptor.Capabilities.ModelTypes);
        Assert.All(
            solver.Descriptor.Capabilities.ConstraintProgrammingFeatures.Values,
            support => Assert.Equal(
                support == ConstraintProgrammingSupportLevel.Unsupported
                    ? ConstraintProgrammingSupportLevel.Unsupported
                    : ConstraintProgrammingSupportLevel.ExactLowering,
                support));
        Assert.True(solver.Descriptor.Capabilities.SupportsConstraintProgramming(
            ConstraintProgrammingFeature.BooleanLogic));
        Assert.True(solver.Descriptor.Capabilities.SupportsConstraintProgramming(
            ConstraintProgrammingFeature.Assumption));
        Assert.True(solver.Descriptor.Capabilities.SupportsConstraintProgramming(
            ConstraintProgrammingFeature.SolutionHint));
        Assert.False(solver.Descriptor.Capabilities.SupportsConstraintProgramming(
            ConstraintProgrammingFeature.Circuit));
        Assert.False(solver.Descriptor.Capabilities.SupportsConstraintProgramming(
            ConstraintProgrammingFeature.Automaton));
        Assert.False(solver.Descriptor.Capabilities.SupportsConstraintProgramming(
            ConstraintProgrammingFeature.Reservoir));
        Assert.False(solver.Descriptor.Capabilities.SupportsConstraintProgramming(
            ConstraintProgrammingFeature.ConflictCore));
        Assert.False(solver.Descriptor.Capabilities.SupportsConstraintProgramming(
            ConstraintProgrammingFeature.Cumulative));
    }

    [Fact]
    public void CumulativeCapabilityShouldFollowTheLoweringPolicy() {
        var strict = new ScipConstraintProgrammingSolver(
            ConstraintProgrammingLoweringPolicy.Strict);
        var cumulative = new ScipConstraintProgrammingSolver(
            new ConstraintProgrammingLoweringPolicy(AllowCumulative: true));

        Assert.Equal(
            ConstraintProgrammingSupportLevel.Unsupported,
            strict.Descriptor.Capabilities.ConstraintProgrammingSupport(
                ConstraintProgrammingFeature.Cumulative));
        Assert.Equal(
            ConstraintProgrammingSupportLevel.ExactLowering,
            cumulative.Descriptor.Capabilities.ConstraintProgrammingSupport(
                ConstraintProgrammingFeature.Cumulative));
    }

    [Fact]
    public async Task UnsupportedConstraintsShouldFailBeforeNativeLoading() {
        var solver = new ScipConstraintProgrammingSolver();

        using var circuitModel = new ConstraintProgrammingModel("scip-cp-circuit");
        ConstraintProgrammingExpression circuitX = Register(
            model: circuitModel,
            variable: new IntVar("circuit-x"),
            lowerBound: 0,
            upperBound: 1);
        ConstraintProgrammingExpression circuitY = Register(
            model: circuitModel,
            variable: new IntVar("circuit-y"),
            lowerBound: 0,
            upperBound: 1);
        Assert.True(circuitModel.AddConstraint(
            ConstraintProgrammingConstraint.CreateCircuit(
                new[] { circuitX, circuitY }).Value).IsOk);

        using var automatonModel = new ConstraintProgrammingModel("scip-cp-automaton");
        ConstraintProgrammingExpression automatonInput = Register(
            model: automatonModel,
            variable: new IntVar("automaton-input"),
            lowerBound: 0,
            upperBound: 1);
        Assert.True(automatonModel.AddConstraint(
            ConstraintProgrammingConstraint.CreateAutomaton(
                expressions: new[] { automatonInput },
                initialState: 0,
                finalStates: new[] { 1 },
                transitions: new[] {
                    new ConstraintProgrammingConstraint.AutomatonTransition(
                        fromState: 0,
                        value: Int64.One,
                        toState: 1),
                }).Value).IsOk);

        using var reservoirModel = new ConstraintProgrammingModel("scip-cp-reservoir");
        Assert.True(reservoirModel.AddConstraint(
            ConstraintProgrammingConstraint.CreateReservoir(
                events: new[] {
                    new ConstraintProgrammingConstraint.Reservoir.Event(
                        Time: ConstraintProgrammingExpression.CreateConstant(1),
                        LevelChange: ConstraintProgrammingExpression.CreateConstant(2)),
                },
                initialLevel: Int64.Zero,
                minimumLevel: Int64.Zero,
                maximumLevel: new Int64(10)).Value).IsOk);

        await AssertUnsupported(solver, circuitModel);
        await AssertUnsupported(solver, automatonModel);
        await AssertUnsupported(solver, reservoirModel);
    }

    [Fact]
    public void InvalidOptionsAndBackendSnapshotsShouldFailBeforeNativeLoading() {
        using ConstraintProgrammingModel model = CreateModel();
        var solver = new ScipConstraintProgrammingSolver();

        AssertFailure(
            solver.CreateSession(
                model,
                new ConstraintProgrammingSolveOptions(CollectConflict: true)),
            ErrorCode.Other);
        AssertFailure(
            solver.CreateSession(
                model,
                new ConstraintProgrammingSolveOptions(LogEnabled: true)),
            ErrorCode.Other);
        AssertFailure(
            solver.CreateSession(
                model,
                new ConstraintProgrammingSolveOptions(NodeLimit: 0)),
            ErrorCode.IllegalArgument);
        AssertFailure(
            solver.CreateSession(
                model,
                new ConstraintProgrammingSolveOptions(
                    BackendConfiguration: new BackendConfigurationSnapshot(
                        Type: "gurobi",
                        Parameters: Array.Empty<BackendParameter>()))),
            ErrorCode.Other);
        AssertFailure(
            solver.CreateSession(
                model,
                new ConstraintProgrammingSolveOptions(
                    BackendConfiguration: new BackendConfigurationSnapshot(
                        Type: "scip",
                        Parameters: new[] {
                            new BackendParameter(
                                Name: "license",
                                Value: new BackendParameterValue.Text("secret"),
                                Sensitive: true),
                        }))),
            ErrorCode.Other);
        AssertFailure(
            solver.CreateSession(
                model,
                new ConstraintProgrammingSolveOptions(
                    BackendConfiguration: new BackendConfigurationSnapshot(
                        Type: "scip",
                        Parameters: new[] {
                            new BackendParameter(
                                Name: "limits/nodes",
                                Value: new BackendParameterValue.Integer(1)),
                        }))),
            ErrorCode.IllegalArgument);
    }

    [Fact]
    public async Task CallerAndOptionCancellationShouldReturnCancelledWithoutNativeLoading() {
        using ConstraintProgrammingModel model = CreateModel();
        var solver = new ScipConstraintProgrammingSolver();

        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();
        Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> callerResult = await solver.SolveAsync(
            model: model,
            cancellationToken: callerCancellation.Token);
        ConstraintProgrammingUnknownOutput callerCancelled = Assert.IsType<
            ConstraintProgrammingUnknownOutput>(
            Assert.IsType<
                Ok<
                    ConstraintProgrammingSolverOutput,
                    ErrorCode,
                    Error<ErrorCode>>>(callerResult).Value);
        Assert.Equal(TerminationReason.Cancelled, callerCancelled.TerminationReason);
        Assert.Equal(SolutionPresence.None, callerCancelled.Report!.SolutionPresence);

        var optionCancellation = new SolveCancellationToken();
        using IConstraintProgrammingSession session = Assert.IsType<
            Ok<
                IConstraintProgrammingSession,
                ErrorCode,
                Error<ErrorCode>>>(
            solver.CreateSession(
                model,
                new ConstraintProgrammingSolveOptions(
                    CancellationToken: optionCancellation))).Value;
        Assert.True(optionCancellation.Request().IsOk);
        Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> optionResult = await session.SolveAsync();
        ConstraintProgrammingUnknownOutput optionCancelled = Assert.IsType<
            ConstraintProgrammingUnknownOutput>(
            Assert.IsType<
                Ok<
                    ConstraintProgrammingSolverOutput,
                    ErrorCode,
                    Error<ErrorCode>>>(optionResult).Value);
        Assert.Equal(TerminationReason.Cancelled, optionCancelled.TerminationReason);
    }

    private static ConstraintProgrammingModel CreateModel() {
        var model = new ConstraintProgrammingModel(
            name: "scip-cp-contract",
            objectCategory: ObjectCategory.Minimum);
        ConstraintProgrammingExpression value = Register(
            model: model,
            variable: new IntVar("contract-value"),
            lowerBound: 0,
            upperBound: 3);
        Assert.True(model.Minimize(value).IsOk);
        return model;
    }

    private static ConstraintProgrammingExpression Register(
        ConstraintProgrammingModel model,
        IVariableItem variable,
        long lowerBound,
        long upperBound) {
        Assert.True(model.RegisterVariable(
            variable: variable,
            domain: IntegerDomain.CreateInterval(
                lowerBound: new Int64(lowerBound),
                upperBound: new Int64(upperBound)).Value).IsOk);
        return ConstraintProgrammingExpression.CreateVariable(variable).Value;
    }

    private static void AssertFailure(
        Result<
            IConstraintProgrammingSession,
            ErrorCode,
            Error<ErrorCode>> result,
        ErrorCode code) {
        Assert.Equal(
            code,
            Assert.IsType<
                Failed<
                    IConstraintProgrammingSession,
                    ErrorCode,
                    Error<ErrorCode>>>(result).Code);
    }

    private static async Task AssertUnsupported(
        ScipConstraintProgrammingSolver solver,
        ConstraintProgrammingModel model) {
        Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>> result = await solver.SolveAsync(model);
        Assert.Equal(
            ErrorCode.Other,
            Assert.IsType<
                Failed<
                    ConstraintProgrammingSolverOutput,
                    ErrorCode,
                    Error<ErrorCode>>>(result).Code);
    }
}
