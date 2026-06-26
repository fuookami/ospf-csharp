#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Heuristic;

public class ParticleSwarmSolverTests {
    [Fact]
    public void BasicHeuristicPolicy_HasCorrectDefaults() {
        var policy = new BasicHeuristicPolicy();

        policy.Name.Should().Be("basic");
        policy.MaxIterations.Should().Be(1000);
        policy.PopulationSize.Should().Be(100);
        policy.MaxIterationsWithoutImprovement.Should().Be(100);
    }

    [Fact]
    public void BasicHeuristicPolicy_FinishedAfterMaxIterations() {
        var policy = new BasicHeuristicPolicy { MaxIterations = 5 };
        var iteration = new Iteration();

        for (int i = 0; i < 5; i++) {
            policy.Finished(iteration).Should().BeFalse();
            iteration.Next(true);
        }

        policy.Finished(iteration).Should().BeTrue();
    }

    [Fact]
    public void BasicHeuristicPolicy_FinishedAfterNoImprovement() {
        var policy = new BasicHeuristicPolicy { MaxIterationsWithoutImprovement = 3 };
        var iteration = new Iteration();

        for (int i = 0; i < 3; i++) {
            policy.Finished(iteration).Should().BeFalse();
            iteration.Next(false); // no improvement
        }

        policy.Finished(iteration).Should().BeTrue();
    }

    [Fact]
    public void HeuristicResult_HasCorrectProperties() {
        var iteration = new Iteration();
        var result = new HeuristicResult<double, int>(
            BestSolution: new[] { 1, 2, 3 },
            BestObjective: 42.0,
            Status: HeuristicSolutionStatus.Feasible,
            Iteration: iteration);

        result.BestSolution.Should().BeEquivalentTo(new[] { 1, 2, 3 });
        result.BestObjective.Should().Be(42.0);
        result.Status.Should().Be(HeuristicSolutionStatus.Feasible);
        result.Iteration.Should().BeSameAs(iteration);
    }

    [Fact]
    public void HeuristicResult_NullSolution_IsAllowed() {
        var iteration = new Iteration();
        var result = new HeuristicResult<double, int>(
            BestSolution: null,
            BestObjective: default,
            Status: HeuristicSolutionStatus.Infeasible,
            Iteration: iteration);

        result.BestSolution.Should().BeNull();
        result.Status.Should().Be(HeuristicSolutionStatus.Infeasible);
    }

    [Fact]
    public void HeuristicSolutionStatus_HasCorrectValues() {
        // Verify the enum values exist
        HeuristicSolutionStatus.Feasible.Should().Be(HeuristicSolutionStatus.Feasible);
        HeuristicSolutionStatus.Infeasible.Should().Be(HeuristicSolutionStatus.Infeasible);

        // Verify they are distinct
        ((int)HeuristicSolutionStatus.Feasible).Should().NotBe((int)HeuristicSolutionStatus.Infeasible);
    }

    [Fact]
    public void Iteration_TracksCurrentAndNotBetter() {
        var iteration = new Iteration();

        iteration.Current.Should().Be(0);
        iteration.NotBetterIteration.Should().Be(0);

        iteration.Next(true);
        iteration.Current.Should().Be(1);
        iteration.NotBetterIteration.Should().Be(0);

        iteration.Next(false);
        iteration.Current.Should().Be(2);
        iteration.NotBetterIteration.Should().Be(1);

        iteration.Next(false);
        iteration.Current.Should().Be(3);
        iteration.NotBetterIteration.Should().Be(2);

        iteration.Next(true);
        iteration.Current.Should().Be(4);
        iteration.NotBetterIteration.Should().Be(0); // reset on improvement
    }

    [Fact]
    public void Iteration_Reset_ClearsCounters() {
        var iteration = new Iteration();
        iteration.Next(true);
        iteration.Next(false);

        iteration.Reset();

        iteration.Current.Should().Be(0);
        iteration.NotBetterIteration.Should().Be(0);
    }

    [Fact]
    public void Population_Best_ReturnsIndividualWithLowestObjective() {
        TestIndividual[] individuals = new[] {
            new TestIndividual(new[] { 0 }, 5.0),
            new TestIndividual(new[] { 1 }, 1.0),
            new TestIndividual(new[] { 2 }, 3.0),
        };
        var population = new Population<TestIndividual, double, int>(individuals);

        TestIndividual? best = population.Best((a, b) => a.CompareTo(b));

        best.Should().NotBeNull();
        best!.Objective.Should().Be(1.0);
    }

    [Fact]
    public void Population_Empty_Best_ReturnsDefault() {
        var population = new Population<TestIndividual, double, int>(
            System.Array.Empty<TestIndividual>());

        TestIndividual? best = population.Best((a, b) => a.CompareTo(b));

        best.Should().BeNull();
    }

    [Fact]
    public void Population_Size_ReturnsCount() {
        TestIndividual[] individuals = new[] {
            new TestIndividual(new[] { 0 }, 1.0),
            new TestIndividual(new[] { 1 }, 2.0),
        };
        var population = new Population<TestIndividual, double, int>(individuals);

        population.Size.Should().Be(2);
    }

    [Fact]
    public void Population_Indexer_ReturnsCorrectIndividual() {
        TestIndividual[] individuals = new[] {
            new TestIndividual(new[] { 0 }, 10.0),
            new TestIndividual(new[] { 1 }, 20.0),
        };
        var population = new Population<TestIndividual, double, int>(individuals);

        population[0].Objective.Should().Be(10.0);
        population[1].Objective.Should().Be(20.0);
    }
}
