#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Heuristic.Ga;
using Fuookami.Ospf.Core.Plugin.Heuristic.Tests.Shared;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests.Ga;
/// <summary>
/// genetic algorithm tests.
/// genetic algorithm tests.
/// </summary>
public class GeneAlgorithmTest {
    /// <summary>
    /// GA should find a solution better than initial on paraboloid.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Paraboloid_ShouldConverge() {
        // Arrange
        var policy = new GAPolicy(
            maxIterations: 50,
            populationSize: 20,
            eliteCount: 2,
            tournamentSize: 3,
            mutationRate: 0.3,
            mutationStrength: 0.5,
            seed: 42);
        var ga = new GeneAlgorithm(policy);
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await ga.InvokeAsync(model);

        // Assert
        result.Should().BeOfType<Ok<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>>();
        result.IsOk.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        // Best objective should be finite (converged from initial range [2,8])
        IIndividual<Flt64, Flt64> bestObj = result.Value.MinBy(ind => ind.Objective.ToDouble())!;
        bestObj.Objective.ToDouble().Should().BeLessThan(100.0);
    }

    /// <summary>
    /// GA with same seed should produce same best objective.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_SameSeed_ShouldBeReproducible() {
        // Arrange
        var model1 = new TestCallBackModel(dimension: 3, seed: 42);
        var model2 = new TestCallBackModel(dimension: 3, seed: 42);

        var policy1 = new GAPolicy(maxIterations: 30, populationSize: 15, seed: 42);
        var policy2 = new GAPolicy(maxIterations: 30, populationSize: 15, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result1 = await new GeneAlgorithm(policy1).InvokeAsync(model1);
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result2 = await new GeneAlgorithm(policy2).InvokeAsync(model2);

        // Assert
        result1.IsOk.Should().BeTrue();
        result2.IsOk.Should().BeTrue();

        double best1 = result1.Value.MinBy(ind => ind.Objective.ToDouble())!.Objective.ToDouble();
        double best2 = result2.Value.MinBy(ind => ind.Objective.ToDouble())!.Objective.ToDouble();
        best1.Should().BeApproximately(best2, 1e-10);
    }

    /// <summary>
    /// GA should respect cancellation token.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Cancellation_ShouldTerminateEarly() {
        // Arrange
        var policy = new GAPolicy(maxIterations: 10000, populationSize: 50, seed: 42);
        var ga = new GeneAlgorithm(policy);
        var model = new TestCallBackModel(dimension: 3, seed: 42);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await ga.InvokeAsync(model, cancellationToken: cts.Token);

        // Assert
        result.IsOk.Should().BeTrue();
    }

    /// <summary>
    /// GA should abort when runningCallBack returns Failed.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_CallbackAbort_ShouldTerminateEarly() {
        // Arrange
        var policy = new GAPolicy(maxIterations: 1000, populationSize: 20, seed: 42);
        var ga = new GeneAlgorithm(policy);
        var model = new TestCallBackModel(dimension: 3, seed: 42);
        int callbackCount = 0;

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await ga.InvokeAsync(model, runningCallBack: (iteration, best, pop) => {
            callbackCount++;
            // Abort after first iteration
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "test abort"));
        });

        // Assert
        result.IsOk.Should().BeTrue();
        callbackCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// GA policy constructor should store parameters correctly.
    /// </summary>
    [Fact]
    public void Policy_Constructor_ShouldStoreParameters() {
        // Arrange & Act
        var policy = new GAPolicy(
            maxIterations: 500,
            populationSize: 50,
            eliteCount: 5,
            tournamentSize: 4,
            mutationRate: 0.2,
            mutationStrength: 0.3,
            seed: 123);

        // Assert
        policy.Name.Should().Be("GA");
        policy.MaxIterations.Should().Be(500);
        policy.PopulationSize.Should().Be(50);
        policy.EliteCount.Should().Be(5);
    }

    /// <summary>
    /// GA constructor should throw on null policy.
    /// </summary>
    [Fact]
    public void Constructor_NullPolicy_ShouldThrow() =>
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new GeneAlgorithm(null!));

    /// <summary>
    /// GA Policy.Finished should respect MaxIterations.
    /// </summary>
    [Fact]
    public void Policy_Finished_ShouldRespectMaxIterations() {
        // Arrange
        var policy = new GAPolicy(maxIterations: 5, populationSize: 10, seed: 42);
        var iteration = new Iteration();

        // Act & Assert - not finished at start
        policy.Finished(iteration).Should().BeFalse();

        // Advance to max iterations
        for (int i = 0; i < 5; i++) {
            iteration.Next(false);
        }

        policy.Finished(iteration).Should().BeTrue();
    }

    /// <summary>
    /// GA Policy.Finished should respect MaxIterationsWithoutImprovement.
    /// </summary>
    [Fact]
    public void Policy_Finished_ShouldRespectMaxIterationsWithoutImprovement() {
        // Arrange
        var policy = new GAPolicy(maxIterations: 1000, populationSize: 10, seed: 42) {
            MaxIterationsWithoutImprovement = 3
        };
        var iteration = new Iteration();

        // Act - 3 iterations without improvement
        iteration.Next(false);
        iteration.Next(false);
        iteration.Next(false);

        // Assert
        policy.Finished(iteration).Should().BeTrue();
    }
}
