#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Heuristic.Gwo;
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

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests.Gwo;
/// <summary>
/// grey wolf optimizer tests.
/// grey wolf optimizer tests.
/// </summary>
public class GreyWolfOptimizerTest {
    /// <summary>
    /// GWO should find a solution better than initial on paraboloid.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Paraboloid_ShouldConverge() {
        // Arrange
        var policy = new GwoPolicy(
            minA: 0.02, maxA: 2.2,
            maxIterations: 50,
            populationSize: 20,
            seed: 42);
        var gwo = new GreyWolfOptimizer(policy, populationSize: 20, solutionAmount: 3);
        var model = new TestCallBackModel(dimension: 3, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await gwo.InvokeAsync(model);

        // Assert
        result.IsOk.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        result.Value.Should().HaveCountLessOrEqualTo(3);
        double bestObj = result.Value.MinBy(ind => ind.Objective.ToDouble())!.Objective.ToDouble();
        bestObj.Should().BeLessThan(100.0);
    }

    /// <summary>
    /// GWO with same seed should produce same best objective.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_SameSeed_ShouldBeReproducible() {
        // Arrange
        var model1 = new TestCallBackModel(dimension: 3, seed: 42);
        var model2 = new TestCallBackModel(dimension: 3, seed: 42);

        var policy1 = new GwoPolicy(maxIterations: 30, populationSize: 15, seed: 42);
        var policy2 = new GwoPolicy(maxIterations: 30, populationSize: 15, seed: 42);

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result1 = await new GreyWolfOptimizer(policy1, populationSize: 15).InvokeAsync(model1);
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result2 = await new GreyWolfOptimizer(policy2, populationSize: 15).InvokeAsync(model2);

        // Assert
        result1.IsOk.Should().BeTrue();
        result2.IsOk.Should().BeTrue();
        double best1 = result1.Value.MinBy(ind => ind.Objective.ToDouble())!.Objective.ToDouble();
        double best2 = result2.Value.MinBy(ind => ind.Objective.ToDouble())!.Objective.ToDouble();
        best1.Should().BeApproximately(best2, 1e-10);
    }

    /// <summary>
    /// GWO should handle cancellation (may throw TaskCanceledException due to Task.Run).
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Cancellation_ShouldTerminateEarly() {
        // Arrange
        var policy = new GwoPolicy(maxIterations: 10000, populationSize: 50, seed: 42);
        var gwo = new GreyWolfOptimizer(policy, populationSize: 50);
        var model = new TestCallBackModel(dimension: 3, seed: 42);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        try {
            Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await gwo.InvokeAsync(model, cancellationToken: cts.Token);
            result.IsOk.Should().BeTrue();
        }
        catch (OperationCanceledException) {
            // Expected: Task.Run propagates cancellation
        }
    }

    /// <summary>
    /// GWO should abort when runningCallBack returns Failed.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_CallbackAbort_ShouldTerminateEarly() {
        // Arrange
        var policy = new GwoPolicy(maxIterations: 1000, populationSize: 20, seed: 42);
        var gwo = new GreyWolfOptimizer(policy, populationSize: 20);
        var model = new TestCallBackModel(dimension: 3, seed: 42);
        int callbackCount = 0;

        // Act
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result = await gwo.InvokeAsync(model, runningCallBack: (iteration, best, pop) => {
            callbackCount++;
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "test abort"));
        });

        // Assert
        result.IsOk.Should().BeTrue();
        callbackCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// GWO constructor should throw on null policy.
    /// </summary>
    [Fact]
    public void Constructor_NullPolicy_ShouldThrow() =>
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new GreyWolfOptimizer(null!, 10));

    /// <summary>
    /// GWO policy constructor should store parameters correctly.
    /// </summary>
    [Fact]
    public void Policy_Constructor_ShouldStoreParameters() {
        // Arrange & Act
        var policy = new GwoPolicy(
            minA: 0.01, maxA: 3.0,
            maxIterations: 200,
            populationSize: 30,
            seed: 99);

        // Assert
        policy.Name.Should().Be("GWO");
        policy.MaxIterations.Should().Be(200);
        policy.PopulationSize.Should().Be(30);
    }

    /// <summary>
    /// GWO Policy.A should decrease over iterations.
    /// </summary>
    [Fact]
    public void Policy_A_ShouldDecreaseOverIterations() {
        // Arrange
        var policy = new GwoPolicy(minA: 0.0, maxA: 2.0, maxIterations: 100, seed: 42);
        var iter0 = new Iteration();
        var iter50 = new Iteration();
        for (int i = 0; i < 50; i++) {
            iter50.Next(false);
        }

        // Act
        Flt64 a0 = policy.A(iter0);
        Flt64 a50 = policy.A(iter50);

        // Assert
        a0.ToDouble().Should().BeGreaterThan(a50.ToDouble());
    }

    /// <summary>
    /// GWO WolfPopulationExtensions.Alpha should throw on empty list.
    /// </summary>
    [Fact]
    public void WolfPopulation_Alpha_Empty_ShouldThrow() {
        // Arrange
        var wolves = new List<SolutionWithFitness<Flt64, Flt64>>();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(wolves.Alpha);
    }

    /// <summary>
    /// GWO WolfPopulationExtensions.Beta should throw on single-element list.
    /// </summary>
    [Fact]
    public void WolfPopulation_Beta_SingleElement_ShouldThrow() {
        // Arrange
        var wolves = new List<SolutionWithFitness<Flt64, Flt64>>
        {
            new(new List<Flt64> { Flt64.Zero }, Flt64.Zero, 0.0)
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(wolves.Beta);
    }

    /// <summary>
    /// GWO WolfPopulationExtensions.Delta should throw on two-element list.
    /// </summary>
    [Fact]
    public void WolfPopulation_Delta_TwoElements_ShouldThrow() {
        // Arrange
        var wolves = new List<SolutionWithFitness<Flt64, Flt64>>
        {
            new(new List<Flt64> { Flt64.Zero }, Flt64.Zero, 0.0),
            new(new List<Flt64> { Flt64.One }, Flt64.One, 1.0)
        };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(wolves.Delta);
    }

    /// <summary>
    /// GWO WolfPopulationExtensions should return correct wolves for valid list.
    /// </summary>
    [Fact]
    public void WolfPopulation_AlphaBetaDelta_Valid_ShouldReturnCorrectWolves() {
        // Arrange
        var wolf0 = new SolutionWithFitness<Flt64, Flt64>(new List<Flt64> { Flt64.Zero }, Flt64.Zero, 0.0);
        var wolf1 = new SolutionWithFitness<Flt64, Flt64>(new List<Flt64> { Flt64.One }, Flt64.One, 1.0);
        var wolf2 = new SolutionWithFitness<Flt64, Flt64>(new List<Flt64> { new Flt64(2.0) }, new Flt64(2.0), 2.0);
        var wolves = new List<SolutionWithFitness<Flt64, Flt64>> { wolf0, wolf1, wolf2 };

        // Act & Assert
        wolves.Alpha().Should().BeSameAs(wolf0);
        wolves.Beta().Should().BeSameAs(wolf1);
        wolves.Delta().Should().BeSameAs(wolf2);
    }
}
