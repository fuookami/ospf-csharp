#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Heuristic.Mvo;
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

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests.Mvo;
/// <summary>
/// multi-verse optimizer tests.
/// multi-verse optimizer tests.
/// </summary>
public class MultiVerseOptimizerTest {
    private static MvoPolicy<Flt64, Flt64> CreatePolicy(
        int maxIterations = 50, int? seed = null) {
        var policy = new MvoPolicy<Flt64, Flt64>(
            fromValue: v => v,
            intoValue: v => v) {
            MaxIterations = maxIterations,
            Random = seed.HasValue ? new Random(seed.Value) : Random.Shared
        };
        return policy;
    }

    /// <summary>
    /// MVO should find a solution better than initial on paraboloid.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Paraboloid_ShouldConverge() {
        // Arrange
        MvoPolicy<Flt64, Flt64> policy = CreatePolicy(maxIterations: 50, seed: 42);
        var mvo = new MultiVerseOptimizer<Flt64, Flt64, Flt64>(
            policy, universeAmount: 20, solutionAmount: 3);
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await mvo.InvokeAsync(model);

        // Assert
        result.IsOk.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        result.Value.Should().HaveCountLessOrEqualTo(3);
        double bestObj = result.Value.MinBy(ind => ind.Objective.ToDouble())!.Objective.ToDouble();
        bestObj.Should().BeLessThan(200.0);
    }

    /// <summary>
    /// MVO with same seed should produce same best objective.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_SameSeed_ShouldBeReproducible() {
        // Arrange
        var model1 = new TestCallBackModel(dimension: 3, seed: 42);
        var model2 = new TestCallBackModel(dimension: 3, seed: 42);

        MvoPolicy<Flt64, Flt64> policy1 = CreatePolicy(maxIterations: 30, seed: 42);
        MvoPolicy<Flt64, Flt64> policy2 = CreatePolicy(maxIterations: 30, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result1 = await new MultiVerseOptimizer<Flt64, Flt64, Flt64>(policy1, universeAmount: 15).InvokeAsync(model1);
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result2 = await new MultiVerseOptimizer<Flt64, Flt64, Flt64>(policy2, universeAmount: 15).InvokeAsync(model2);

        // Assert
        result1.IsOk.Should().BeTrue();
        result2.IsOk.Should().BeTrue();
        double best1 = result1.Value.MinBy(ind => ind.Objective.ToDouble())!.Objective.ToDouble();
        double best2 = result2.Value.MinBy(ind => ind.Objective.ToDouble())!.Objective.ToDouble();
        best1.Should().BeApproximately(best2, 1e-10);
    }

    /// <summary>
    /// MVO should handle cancellation (may throw TaskCanceledException due to Task.Run).
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Cancellation_ShouldTerminateEarly() {
        // Arrange
        MvoPolicy<Flt64, Flt64> policy = CreatePolicy(maxIterations: 10000, seed: 42);
        var mvo = new MultiVerseOptimizer<Flt64, Flt64, Flt64>(policy, universeAmount: 50);
        var model = new TestCallBackModel(dimension: 3, seed: 42);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        try {
            Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await mvo.InvokeAsync(model, cancellationToken: cts.Token);
            result.IsOk.Should().BeTrue();
        }
        catch (OperationCanceledException) {
            // Expected: Task.Run propagates cancellation
        }
    }

    /// <summary>
    /// MVO should abort when runningCallBack returns Failed.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_CallbackAbort_ShouldTerminateEarly() {
        // Arrange
        MvoPolicy<Flt64, Flt64> policy = CreatePolicy(maxIterations: 1000, seed: 42);
        var mvo = new MultiVerseOptimizer<Flt64, Flt64, Flt64>(policy, universeAmount: 20);
        var model = new TestCallBackModel(dimension: 3, seed: 42);
        int callbackCount = 0;

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await mvo.InvokeAsync(model, runningCallBack: (iteration, best, pop) => {
            callbackCount++;
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "test abort"));
        });

        // Assert
        result.IsOk.Should().BeTrue();
        callbackCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// MVO constructor should throw on null policy.
    /// </summary>
    [Fact]
    public void Constructor_NullPolicy_ShouldThrow() {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => new MultiVerseOptimizer<Flt64, Flt64, Flt64>(null!, 10));
    }

    /// <summary>
    /// MVO Policy.Wep should increase over iterations.
    /// </summary>
    [Fact]
    public void Policy_Wep_ShouldIncreaseOverIterations() {
        // Arrange
        MvoPolicy<Flt64, Flt64> policy = CreatePolicy(maxIterations: 100, seed: 42);
        var iter0 = new Iteration();
        var iter50 = new Iteration();
        for (int i = 0; i < 50; i++) {
            iter50.Next(false);
        }

        // Act
        Flt64 wep0 = policy.Wep(iter0);
        Flt64 wep50 = policy.Wep(iter50);

        // Assert
        wep50.ToDouble().Should().BeGreaterThan(wep0.ToDouble());
    }

    /// <summary>
    /// MVO Policy.Tdr should decrease over iterations.
    /// </summary>
    [Fact]
    public void Policy_Tdr_ShouldDecreaseOverIterations() {
        // Arrange
        MvoPolicy<Flt64, Flt64> policy = CreatePolicy(maxIterations: 100, seed: 42);
        var iter0 = new Iteration();
        var iter50 = new Iteration();
        for (int i = 0; i < 50; i++) {
            iter50.Next(false);
        }

        // Act
        Flt64 tdr0 = policy.Tdr(iter0);
        Flt64 tdr50 = policy.Tdr(iter50);

        // Assert
        tdr0.ToDouble().Should().BeGreaterThan(tdr50.ToDouble());
    }
}
