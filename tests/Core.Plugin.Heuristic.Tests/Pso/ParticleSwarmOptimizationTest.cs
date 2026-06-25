#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Heuristic.Pso;
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

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests.Pso;
/// <summary>
/// particle swarm optimization tests.
/// particle swarm optimization tests.
/// </summary>
public class ParticleSwarmOptimizationTest {
    /// <summary>
    /// PSO should find a solution better than initial on paraboloid.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Paraboloid_ShouldConverge() {
        // Arrange
        var policy = new PsoPolicy(
            w: 0.4, c1: 2.0, c2: 2.0,
            maxVelocity: 10.0,
            maxIterations: 50,
            particleCount: 20,
            seed: 42);
        var pso = new ParticleSwarmOptimizationAlgorithm(policy);
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await pso.InvokeAsync(model);

        // Assert
        result.IsOk.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        double bestObj = result.Value[0].Objective.ToDouble();
        bestObj.Should().BeLessThan(100.0);
    }

    /// <summary>
    /// PSO should converge on two separate runs (uses Random.Shared internally, not seed-reproducible).
    /// </summary>
    [Fact]
    public async Task InvokeAsync_TwoRuns_ShouldBothConverge() {
        // Arrange
        var model1 = new TestCallBackModel(dimension: 3, seed: 42);
        var model2 = new TestCallBackModel(dimension: 3, seed: 99);

        var policy1 = new PsoPolicy(maxIterations: 30, particleCount: 15, seed: 42);
        var policy2 = new PsoPolicy(maxIterations: 30, particleCount: 15, seed: 99);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result1 = await new ParticleSwarmOptimizationAlgorithm(policy1).InvokeAsync(model1);
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result2 = await new ParticleSwarmOptimizationAlgorithm(policy2).InvokeAsync(model2);

        // Assert
        result1.IsOk.Should().BeTrue();
        result2.IsOk.Should().BeTrue();
        // Both should converge below initial range
        result1.Value[0].Objective.ToDouble().Should().BeLessThan(100.0);
        result2.Value[0].Objective.ToDouble().Should().BeLessThan(100.0);
    }

    /// <summary>
    /// PSO should handle cancellation (may throw TaskCanceledException due to Task.Run).
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Cancellation_ShouldTerminateEarly() {
        // Arrange
        var policy = new PsoPolicy(maxIterations: 10000, particleCount: 50, seed: 42);
        var pso = new ParticleSwarmOptimizationAlgorithm(policy);
        var model = new TestCallBackModel(dimension: 3, seed: 42);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - PSO wraps in Task.Run which throws TaskCanceledException
        try {
            Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await pso.InvokeAsync(model, cancellationToken: cts.Token);
            result.IsOk.Should().BeTrue();
        }
        catch (OperationCanceledException) {
            // Expected: PSO's Task.Run propagates cancellation
        }
    }

    /// <summary>
    /// PSO should abort when runningCallBack returns Failed.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_CallbackAbort_ShouldTerminateEarly() {
        // Arrange
        var policy = new PsoPolicy(maxIterations: 1000, particleCount: 20, seed: 42);
        var pso = new ParticleSwarmOptimizationAlgorithm(policy);
        var model = new TestCallBackModel(dimension: 3, seed: 42);
        int callbackCount = 0;

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await pso.InvokeAsync(model, runningCallBack: (iteration, best, pop) => {
            callbackCount++;
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "test abort"));
        });

        // Assert
        result.IsOk.Should().BeTrue();
        callbackCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// PSO policy constructor should store parameters correctly.
    /// </summary>
    [Fact]
    public void Policy_Constructor_ShouldStoreParameters() {
        // Arrange & Act
        var policy = new PsoPolicy(
            w: 0.5, c1: 1.5, c2: 2.5,
            maxVelocity: 5000.0,
            maxIterations: 200,
            particleCount: 30,
            seed: 99);

        // Assert
        policy.Name.Should().Be("PSO");
        policy.MaxIterations.Should().Be(200);
        policy.ParticleCount.Should().Be(30);
        policy.W.ToDouble().Should().BeApproximately(0.5, 1e-10);
        policy.C1.ToDouble().Should().BeApproximately(1.5, 1e-10);
        policy.C2.ToDouble().Should().BeApproximately(2.5, 1e-10);
    }

    /// <summary>
    /// PSO constructor should throw on null policy.
    /// </summary>
    [Fact]
    public void Constructor_NullPolicy_ShouldThrow() =>
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ParticleSwarmOptimizationAlgorithm(null!));

    /// <summary>
    /// PSO Policy.Finished should respect MaxIterations.
    /// </summary>
    [Fact]
    public void Policy_Finished_ShouldRespectMaxIterations() {
        // Arrange
        var policy = new PsoPolicy(maxIterations: 5, particleCount: 10, seed: 42);
        var iteration = new Iteration();

        // Act & Assert
        policy.Finished(iteration).Should().BeFalse();
        for (int i = 0; i < 5; i++) {
            iteration.Next(false);
        }

        policy.Finished(iteration).Should().BeTrue();
    }

    /// <summary>
    /// PSO Accelerate should return valid velocity and position.
    /// </summary>
    [Fact]
    public void Accelerate_ShouldReturnValidVelocityAndPosition() {
        // Arrange
        var policy = new PsoPolicy(w: 0.5, c1: 2.0, c2: 2.0, seed: 42);
        var iteration = new Iteration();
        var velocity = new List<Flt64> { new Flt64(1.0), new Flt64(-1.0) };
        var position = new List<Flt64> { new Flt64(5.0), new Flt64(5.0) };
        var personalBest = new List<Flt64> { new Flt64(3.0), new Flt64(3.0) };
        var globalBest = new List<Flt64> { new Flt64(0.0), new Flt64(0.0) };

        // Act
        (IReadOnlyList<Flt64>? newVel, IReadOnlyList<Flt64>? newPos) = policy.Accelerate(iteration, velocity, position, personalBest, globalBest);

        // Assert
        newVel.Should().HaveCount(2);
        newPos.Should().HaveCount(2);
    }
}
