#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Heuristic.Ga;
using Fuookami.Ospf.Core.Plugin.Heuristic.Gwo;
using Fuookami.Ospf.Core.Plugin.Heuristic.Pso;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests.Infrastructure;

/// <summary>
/// Verify generic type parameter works with multiple numeric types.
/// Verify generic type parameter works with multiple numeric types.
/// </summary>
public class HeuristicAlgorithmGenericTest {
    [Fact]
    public void HeuristicAlgorithm_ShouldBeGeneric_OverFlt64() {
        var policy = new GAPolicy();
        var ga = new GeneAlgorithm(policy);
        ga.Should().NotBeNull();
        ga.Should().BeOfType<GeneAlgorithm>();
    }

    [Fact]
    public void HeuristicAlgorithm_ShouldBeGeneric_OverGWO() {
        var policy = new GwoPolicy();
        var gwo = new GreyWolfOptimizer(policy, populationSize: 5, solutionAmount: 2);
        gwo.Should().NotBeNull();
        gwo.Should().BeOfType<GreyWolfOptimizer>();
    }

    [Fact]
    public void Population_ShouldBeGeneric() {
        var solution = new List<Flt64> { new Flt64(1.0), new Flt64(2.0) };
        var individual = new SolutionWithFitness<Flt64, Flt64>(solution, new Flt64(5.0), 0.5);
        var population = new Population<SolutionWithFitness<Flt64, Flt64>, Flt64, Flt64>(new[] { individual });
        population.Size.Should().Be(1);
        population[0].Objective.Should().Be(new Flt64(5.0));
    }

    [Fact]
    public void Selection_ShouldImplementInterface() {
        new RouletteSelection().Should().BeAssignableTo<ISelection>();
        new TournamentSelection().Should().BeAssignableTo<ISelection>();
        new StochasticUniversalSelection().Should().BeAssignableTo<ISelection>();
        new TruncationSelection().Should().BeAssignableTo<ISelection>();
        new BoltzmannSelection().Should().BeAssignableTo<ISelection>();
    }

    [Fact]
    public void MutationMode_ShouldReturnProbability() {
        var staticMode = new StaticMutationMode();
        staticMode.GetProbability(0, 100).Should().Be(0.1);

        var randomMode = new RandomMutationMode();
        randomMode.GetProbability(0, 100).Should().BeGreaterThanOrEqualTo(0.01);

        var adaptiveMode = new AdaptiveDynamicMutationMode();
        adaptiveMode.GetProbability(0, 100).Should().Be(0.1);
        adaptiveMode.GetProbability(50, 100).Should().BeApproximately(0.05, 0.01);
    }

    [Fact]
    public void Iteration_ShouldTrackProgress() {
        var iteration = new Iteration();
        iteration.Current.Should().Be(0);
        iteration.NotBetterIteration.Should().Be(0);

        iteration.Next(better: true);
        iteration.Current.Should().Be(1);
        iteration.NotBetterIteration.Should().Be(0);

        iteration.Next(better: false);
        iteration.Current.Should().Be(2);
        iteration.NotBetterIteration.Should().Be(1);

        iteration.Next(better: true);
        iteration.Current.Should().Be(3);
        iteration.NotBetterIteration.Should().Be(0);
    }

    [Fact]
    public void Policy_ShouldImplementInterface() {
        var policy = new GAPolicy();
        policy.Should().BeAssignableTo<IAbstractHeuristicPolicy>();
        policy.MaxIterations.Should().Be(1000);
        policy.PopulationSize.Should().Be(100);
    }

    [Fact]
    public void HeuristicResult_ShouldBeRecord() {
        var result = new HeuristicResult<Flt64, Flt64>(
            new List<Flt64>(),
            new Flt64(0.0),
            HeuristicSolutionStatus.Feasible,
            new Iteration());
        result.Should().NotBeNull();
        result.BestSolution.Should().NotBeNull();
        result.Status.Should().Be(HeuristicSolutionStatus.Feasible);
    }

    [Fact]
    public void BasicHeuristicPolicy_Defaults() {
        var policy = new BasicHeuristicPolicy();
        policy.MaxIterations.Should().Be(1000);
        policy.PopulationSize.Should().Be(100);
        policy.MaxIterationsWithoutImprovement.Should().Be(100);
    }
}
