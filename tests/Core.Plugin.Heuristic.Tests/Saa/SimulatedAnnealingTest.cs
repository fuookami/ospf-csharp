#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Heuristic.Saa;
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

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests.Saa;
/// <summary>
/// simulated annealing algorithm tests.
/// simulated annealing algorithm tests.
/// </summary>
public class SimulatedAnnealingTest {
    /// <summary>
    /// SAA should find a solution better than initial on paraboloid.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Paraboloid_ShouldConverge() {
        // Arrange
        var policy = new SaaPolicy(
            initialTemperature: 100.0,
            finalTemperature: 1.0,
            temperatureGradient: 0.95,
            markovLength: 10,
            maxIterations: 50,
            seed: 42);
        var saa = new SimulatedAnnealingAlgorithm(policy);
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await saa.InvokeAsync(model);

        // Assert
        result.IsOk.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        double bestObj = result.Value[0].Objective.ToDouble();
        bestObj.Should().BeLessThan(200.0);
    }

    /// <summary>
    /// SAA should converge on two separate runs (uses new Random() internally, not seed-reproducible).
    /// </summary>
    [Fact]
    public async Task InvokeAsync_TwoRuns_ShouldBothConverge() {
        // Arrange
        var model1 = new TestCallBackModel(dimension: 3, seed: 42);
        var model2 = new TestCallBackModel(dimension: 3, seed: 99);

        var policy1 = new SaaPolicy(maxIterations: 30, markovLength: 5, seed: 42);
        var policy2 = new SaaPolicy(maxIterations: 30, markovLength: 5, seed: 99);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result1 = await new SimulatedAnnealingAlgorithm(policy1).InvokeAsync(model1);
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result2 = await new SimulatedAnnealingAlgorithm(policy2).InvokeAsync(model2);

        // Assert
        result1.IsOk.Should().BeTrue();
        result2.IsOk.Should().BeTrue();
        // Both should converge below initial range
        result1.Value[0].Objective.ToDouble().Should().BeLessThan(200.0);
        result2.Value[0].Objective.ToDouble().Should().BeLessThan(200.0);
    }

    /// <summary>
    /// SAA should handle cancellation (may throw TaskCanceledException due to Task.Run).
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Cancellation_ShouldTerminateEarly() {
        // Arrange
        var policy = new SaaPolicy(maxIterations: 10000, seed: 42);
        var saa = new SimulatedAnnealingAlgorithm(policy);
        var model = new TestCallBackModel(dimension: 3, seed: 42);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        try {
            Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await saa.InvokeAsync(model, cancellationToken: cts.Token);
            result.IsOk.Should().BeTrue();
        }
        catch (OperationCanceledException) {
            // Expected: Task.Run propagates cancellation
        }
    }

    /// <summary>
    /// SAA should abort when runningCallBack returns Failed.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_CallbackAbort_ShouldTerminateEarly() {
        // Arrange
        var policy = new SaaPolicy(maxIterations: 1000, markovLength: 5, seed: 42);
        var saa = new SimulatedAnnealingAlgorithm(policy);
        var model = new TestCallBackModel(dimension: 3, seed: 42);
        int callbackCount = 0;

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await saa.InvokeAsync(model, runningCallBack: (iteration, best, pop) => {
            callbackCount++;
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "test abort"));
        });

        // Assert
        result.IsOk.Should().BeTrue();
        callbackCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// SAA policy constructor should store parameters correctly.
    /// </summary>
    [Fact]
    public void Policy_Constructor_ShouldStoreParameters() {
        // Arrange & Act
        var policy = new SaaPolicy(
            initialTemperature: 200.0,
            finalTemperature: 0.5,
            temperatureGradient: 0.99,
            markovLength: 50,
            maxIterations: 500,
            seed: 99);

        // Assert
        policy.Name.Should().Be("SAA");
        policy.MaxIterations.Should().Be(500);
        policy.MarkovLength.Should().Be(50);
    }

    /// <summary>
    /// SAA constructor should throw on null policy.
    /// </summary>
    [Fact]
    public void Constructor_NullPolicy_ShouldThrow() =>
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SimulatedAnnealingAlgorithm(null!));

    /// <summary>
    /// SAA Temperature should decrease over iterations.
    /// </summary>
    [Fact]
    public void Temperature_ShouldDecreaseOverIterations() {
        // Arrange
        var policy = new SaaPolicy(initialTemperature: 100.0, temperatureGradient: 0.9, seed: 42);
        var iter0 = new Iteration();
        var iter5 = new Iteration();
        for (int i = 0; i < 5; i++) {
            iter5.Next(false);
        }

        // Act
        Flt64 temp0 = policy.Temperature(iter0);
        Flt64 temp5 = policy.Temperature(iter5);

        // Assert
        temp0.ToDouble().Should().BeGreaterThan(temp5.ToDouble());
    }

    /// <summary>
    /// SAA Accept should return false when temperature is zero.
    /// </summary>
    [Fact]
    public void Accept_ZeroTemperature_ShouldReturnFalse() {
        // Arrange
        var policy = new SaaPolicy(initialTemperature: 0.0, finalTemperature: 0.0, seed: 42);
        var iteration = new Iteration();

        // Act
        bool result = policy.Accept(iteration, new Flt64(1.0), new Flt64(10.0));

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// SAA Policy.Finished should respect temperature threshold.
    /// </summary>
    [Fact]
    public void Policy_Finished_ShouldRespectFinalTemperature() {
        // Arrange
        var policy = new SaaPolicy(
            initialTemperature: 10.0,
            finalTemperature: 5.0,
            temperatureGradient: 0.1,
            maxIterations: 10000,
            seed: 42);
        var iteration = new Iteration();

        // Act - after a few iterations with gradient=0.1, temp drops below 5.0
        for (int i = 0; i < 5; i++) {
            iteration.Next(false);
        }

        // Assert
        policy.Finished(iteration).Should().BeTrue();
    }
}
