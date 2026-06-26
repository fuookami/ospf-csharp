#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Heuristic;

public class SelectionModeTests {
    private static readonly Func<double, double, int> CompareMinimize = (a, b) => a.CompareTo(b);

    private static Population<TestIndividual, double, int> MakePopulation(params double[] objectives) {
        var individuals = objectives.Select((obj, i) =>
            new TestIndividual(new[] { i }, obj)).ToList();
        return new Population<TestIndividual, double, int>(individuals);
    }

    [Fact]
    public void StaticSelectionMode_ReturnsFixedCount() {
        var mode = new StaticSelectionMode<double, int> { SelectionCount = 5 };
        Population<TestIndividual, double, int> pop = MakePopulation(1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0);
        var iteration = new Iteration();

        int count = mode.GetSelectionCount(iteration, pop, CompareMinimize);

        count.Should().Be(5);
    }

    [Fact]
    public void StaticSelectionMode_CountExceedsPopulation_ReturnsPopulationSize() {
        var mode = new StaticSelectionMode<double, int> { SelectionCount = 20 };
        Population<TestIndividual, double, int> pop = MakePopulation(1.0, 2.0, 3.0);
        var iteration = new Iteration();

        int count = mode.GetSelectionCount(iteration, pop, CompareMinimize);

        count.Should().Be(3); // capped at population size
    }

    [Fact]
    public void StaticSelectionMode_DefaultCount_Is10() {
        var mode = new StaticSelectionMode<double, int>();
        mode.SelectionCount.Should().Be(10);
    }

    [Fact]
    public void AdaptiveDynamicSelectionMode_AdjustsBasedOnFitness() {
        var mode = new AdaptiveDynamicSelectionMode<double, int>();
        Population<TestIndividual, double, int> pop = MakePopulation(1.0, 5.0, 10.0, 50.0, 100.0, 200.0, 500.0, 1000.0, 2000.0, 5000.0);
        var iteration = new Iteration();

        int count = mode.GetSelectionCount(iteration, pop, CompareMinimize);

        // Should be at least MinSelectionCount (default 2) and at most MaxSelectionRatio * size
        count.Should().BeGreaterThanOrEqualTo(2);
        int maxCount = (int)System.Math.Round(pop.Size * mode.MaxSelectionRatio);
        count.Should().BeLessThanOrEqualTo(maxCount);
    }

    [Fact]
    public void AdaptiveDynamicSelectionMode_SmallPopulation_ReturnsPopulationSize() {
        var mode = new AdaptiveDynamicSelectionMode<double, int> { MinSelectionCount = 5 };
        Population<TestIndividual, double, int> pop = MakePopulation(1.0, 2.0, 3.0);
        var iteration = new Iteration();

        int count = mode.GetSelectionCount(iteration, pop, CompareMinimize);

        // When population size <= MinSelectionCount, returns population size
        count.Should().Be(3);
    }

    [Fact]
    public void AdaptiveDynamicSelectionMode_EqualFitness_ReturnsMinSelection() {
        var mode = new AdaptiveDynamicSelectionMode<double, int>();
        Population<TestIndividual, double, int> pop = MakePopulation(5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0);
        var iteration = new Iteration();

        int count = mode.GetSelectionCount(iteration, pop, CompareMinimize);

        count.Should().BeGreaterThanOrEqualTo(mode.MinSelectionCount);
    }

    [Fact]
    public void AdaptiveDynamicSelectionMode_DefaultMinSelectionCount_Is2() {
        var mode = new AdaptiveDynamicSelectionMode<double, int>();
        mode.MinSelectionCount.Should().Be(2);
    }

    [Fact]
    public void AdaptiveDynamicSelectionMode_DefaultMaxSelectionRatio_Is0Point5() {
        var mode = new AdaptiveDynamicSelectionMode<double, int>();
        mode.MaxSelectionRatio.Should().Be(0.5);
    }
}
