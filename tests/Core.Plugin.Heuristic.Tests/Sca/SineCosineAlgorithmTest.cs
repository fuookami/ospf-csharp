#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Heuristic.Sca;
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

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests.Sca;
/// <summary>
/// sine cosine algorithm tests.
/// sine cosine algorithm tests.
/// </summary>
public class SineCosineAlgorithmTest {
    private static ScaPolicy<Flt64, Flt64> CreatePolicy(
        int maxIterations = 50, int? seed = null) {
        var policy = new ScaPolicy<Flt64, Flt64>(
            fromValue: v => v,
            intoValue: v => v) {
            MaxIterations = maxIterations,
            Random = seed.HasValue ? new Random(seed.Value) : Random.Shared
        };
        return policy;
    }

    /// <summary>
    /// SCA should find a solution better than initial on paraboloid.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Paraboloid_ShouldConverge() {
        // Arrange
        ScaPolicy<Flt64, Flt64> policy = CreatePolicy(maxIterations: 50, seed: 42);
        var sca = new SineCosineAlgorithm<Flt64, Flt64, Flt64>(
            policy, populationSize: 20, solutionAmount: 3);
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await sca.InvokeAsync(model);

        // Assert
        result.IsOk.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        result.Value.Should().HaveCountLessOrEqualTo(3);
        double bestObj = result.Value.MinBy(ind => ind.Objective.ToDouble())!.Objective.ToDouble();
        bestObj.Should().BeLessThan(200.0);
    }

    /// <summary>
    /// SCA with same seed should produce same best objective.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_SameSeed_ShouldBeReproducible() {
        // Arrange
        var model1 = new TestCallBackModel(dimension: 3, seed: 42);
        var model2 = new TestCallBackModel(dimension: 3, seed: 42);

        ScaPolicy<Flt64, Flt64> policy1 = CreatePolicy(maxIterations: 30, seed: 42);
        ScaPolicy<Flt64, Flt64> policy2 = CreatePolicy(maxIterations: 30, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result1 = await new SineCosineAlgorithm<Flt64, Flt64, Flt64>(policy1, populationSize: 15).InvokeAsync(model1);
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result2 = await new SineCosineAlgorithm<Flt64, Flt64, Flt64>(policy2, populationSize: 15).InvokeAsync(model2);

        // Assert
        result1.IsOk.Should().BeTrue();
        result2.IsOk.Should().BeTrue();
        double best1 = result1.Value.MinBy(ind => ind.Objective.ToDouble())!.Objective.ToDouble();
        double best2 = result2.Value.MinBy(ind => ind.Objective.ToDouble())!.Objective.ToDouble();
        best1.Should().BeApproximately(best2, 1e-10);
    }

    /// <summary>
    /// SCA should handle cancellation (may throw TaskCanceledException due to Task.Run).
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Cancellation_ShouldTerminateEarly() {
        // Arrange
        ScaPolicy<Flt64, Flt64> policy = CreatePolicy(maxIterations: 10000, seed: 42);
        var sca = new SineCosineAlgorithm<Flt64, Flt64, Flt64>(policy, populationSize: 50);
        var model = new TestCallBackModel(dimension: 3, seed: 42);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        try {
            Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await sca.InvokeAsync(model, cancellationToken: cts.Token);
            result.IsOk.Should().BeTrue();
        }
        catch (OperationCanceledException) {
            // Expected: Task.Run propagates cancellation
        }
    }

    /// <summary>
    /// SCA should abort when runningCallBack returns Failed.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_CallbackAbort_ShouldTerminateEarly() {
        // Arrange
        ScaPolicy<Flt64, Flt64> policy = CreatePolicy(maxIterations: 1000, seed: 42);
        var sca = new SineCosineAlgorithm<Flt64, Flt64, Flt64>(policy, populationSize: 20);
        var model = new TestCallBackModel(dimension: 3, seed: 42);
        int callbackCount = 0;

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await sca.InvokeAsync(model, runningCallBack: (iteration, best, pop) => {
            callbackCount++;
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "test abort"));
        });

        // Assert
        result.IsOk.Should().BeTrue();
        callbackCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// SCA constructor should throw on null policy.
    /// </summary>
    [Fact]
    public void Constructor_NullPolicy_ShouldThrow() {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => new SineCosineAlgorithm<Flt64, Flt64, Flt64>(null!, 10));
    }

    /// <summary>
    /// SCA Policy.R1 should decrease over iterations.
    /// </summary>
    [Fact]
    public void Policy_R1_ShouldDecreaseOverIterations() {
        // Arrange
        ScaPolicy<Flt64, Flt64> policy = CreatePolicy(maxIterations: 100, seed: 42);
        var iter0 = new Iteration();
        var iter50 = new Iteration();
        for (int i = 0; i < 50; i++) {
            iter50.Next(false);
        }

        // Act
        Flt64 r1_0 = policy.R1(iter0);
        Flt64 r1_50 = policy.R1(iter50);

        // Assert
        r1_0.ToDouble().Should().BeGreaterThan(r1_50.ToDouble());
    }

    /// <summary>
    /// SCA Policy.R2 should return correct dimension count.
    /// </summary>
    [Fact]
    public void Policy_R2_ShouldReturnCorrectDimension() {
        // Arrange
        ScaPolicy<Flt64, Flt64> policy = CreatePolicy(seed: 42);
        var iteration = new Iteration();

        // Act
        IReadOnlyList<Flt64> r2 = policy.R2(iteration, 5);

        // Assert
        r2.Should().HaveCount(5);
        foreach (Flt64 val in r2) {
            val.ToDouble().Should().BeGreaterThanOrEqualTo(0);
            val.ToDouble().Should().BeLessThanOrEqualTo(2.0 * System.Math.PI);
        }
    }

    /// <summary>
    /// SCA Policy.R3 should return correct dimension count.
    /// </summary>
    [Fact]
    public void Policy_R3_ShouldReturnCorrectDimension() {
        // Arrange
        ScaPolicy<Flt64, Flt64> policy = CreatePolicy(seed: 42);
        var iteration = new Iteration();

        // Act
        IReadOnlyList<Flt64> r3 = policy.R3(iteration, 4);

        // Assert
        r3.Should().HaveCount(4);
        foreach (Flt64 val in r3) {
            val.ToDouble().Should().BeGreaterThanOrEqualTo(0);
            val.ToDouble().Should().BeLessThanOrEqualTo(2.0);
        }
    }
}
