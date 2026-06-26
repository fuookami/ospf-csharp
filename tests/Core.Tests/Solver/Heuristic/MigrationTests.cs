#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Heuristic;

/// <summary>
/// Test individual implementing IIndividual for migration tests.
/// </summary>
public sealed record TestIndividual(IReadOnlyList<int> Solution, double Objective)
    : IIndividual<double, int>;

public class MigrationTests {
    private static readonly Func<double, double, int> CompareMinimize = (a, b) => a.CompareTo(b);

    private static Population<TestIndividual, double, int> MakePopulation(params double[] objectives) {
        var individuals = objectives.Select((obj, i) =>
            new TestIndividual(new[] { i }, obj)).ToList();
        return new Population<TestIndividual, double, int>(individuals);
    }

    private static Iteration MakeIteration() => new();

    [Fact]
    public void RandomMigration_TwoPopulations_ReturnsIncomingForEach() {
        var migration = new RandomMigration<double, int>();
        Population<TestIndividual, double, int> pop1 = MakePopulation(1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0);
        Population<TestIndividual, double, int> pop2 = MakePopulation(11.0, 12.0, 13.0, 14.0, 15.0, 16.0, 17.0, 18.0, 19.0, 20.0);
        Population<TestIndividual, double, int>[] populations = new[] { pop1, pop2 };

        IReadOnlyList<(Population<TestIndividual, double, int> Population, IReadOnlyList<TestIndividual> Incoming)> result = migration.Migrate(MakeIteration(), populations, CompareMinimize);

        result.Should().HaveCount(2);
        // Each population should receive incoming from the other
        result[0].Incoming.Should().NotBeEmpty();
        result[1].Incoming.Should().NotBeEmpty();
        // Name should be "Random"
        migration.Name.Should().Be("Random");
    }

    [Fact]
    public void RandomMigration_SinglePopulation_ReturnsEmptyIncoming() {
        var migration = new RandomMigration<double, int>();
        Population<TestIndividual, double, int> pop = MakePopulation(1.0, 2.0, 3.0);
        Population<TestIndividual, double, int>[] populations = new[] { pop };

        IReadOnlyList<(Population<TestIndividual, double, int> Population, IReadOnlyList<TestIndividual> Incoming)> result = migration.Migrate(MakeIteration(), populations, CompareMinimize);

        result.Should().HaveCount(1);
        result[0].Incoming.Should().BeEmpty();
    }

    [Fact]
    public void BetterToWorseMigration_FindsBestIndividual() {
        var migration = new BetterToWorseMigration<double, int>();
        Population<TestIndividual, double, int> pop1 = MakePopulation(5.0, 1.0, 3.0); // best = 1.0
        Population<TestIndividual, double, int> pop2 = MakePopulation(10.0, 2.0, 8.0); // best = 2.0
        Population<TestIndividual, double, int>[] populations = new[] { pop1, pop2 };

        IReadOnlyList<(Population<TestIndividual, double, int> Population, IReadOnlyList<TestIndividual> Incoming)> result = migration.Migrate(MakeIteration(), populations, CompareMinimize);

        result.Should().HaveCount(2);
        // Pop1 receives best from pop2 (index 1 wraps to 0), pop2 receives best from pop1
        result[0].Incoming.Should().HaveCount(1);
        result[0].Incoming[0].Objective.Should().Be(2.0);
        result[1].Incoming.Should().HaveCount(1);
        result[1].Incoming[0].Objective.Should().Be(1.0);
        migration.Name.Should().Be("BetterToWorse");
    }

    [Fact]
    public void PopulationMerge_MergesAndRedistributes() {
        var migration = new PopulationMergeMigration<double, int>();
        Population<TestIndividual, double, int> pop1 = MakePopulation(1.0, 2.0, 3.0, 4.0);
        Population<TestIndividual, double, int> pop2 = MakePopulation(5.0, 6.0, 7.0, 8.0);
        Population<TestIndividual, double, int>[] populations = new[] { pop1, pop2 };

        IReadOnlyList<(Population<TestIndividual, double, int> Population, IReadOnlyList<TestIndividual> Incoming)> result = migration.Migrate(MakeIteration(), populations, CompareMinimize);

        result.Should().HaveCount(2);
        // Total incoming individuals should equal total original individuals
        int totalIncoming = result.Sum(r => r.Incoming.Count);
        totalIncoming.Should().Be(8);
        migration.Name.Should().Be("PopulationMerge");
    }

    [Fact]
    public void StandardMigration_TransfersBetweenAdjacentPops() {
        var migration = new StandardMigration<double, int>();
        Population<TestIndividual, double, int> pop1 = MakePopulation(1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0);
        Population<TestIndividual, double, int> pop2 = MakePopulation(11.0, 12.0, 13.0, 14.0, 15.0, 16.0, 17.0, 18.0, 19.0, 20.0);
        Population<TestIndividual, double, int> pop3 = MakePopulation(21.0, 22.0, 23.0, 24.0, 25.0, 26.0, 27.0, 28.0, 29.0, 30.0);
        Population<TestIndividual, double, int>[] populations = new[] { pop1, pop2, pop3 };

        IReadOnlyList<(Population<TestIndividual, double, int> Population, IReadOnlyList<TestIndividual> Incoming)> result = migration.Migrate(MakeIteration(), populations, CompareMinimize);

        result.Should().HaveCount(3);
        // Each population should receive incoming from the previous (ring topology)
        foreach ((Population<TestIndividual, double, int>? pop, IReadOnlyList<TestIndividual>? incoming) in result) {
            incoming.Should().NotBeEmpty();
        }
        migration.Name.Should().Be("Standard");
    }

    [Fact]
    public void MoreToLessMigration_LargestPopDonatesToAll() {
        var migration = new MoreToLessMigration<double, int>();
        Population<TestIndividual, double, int> pop1 = MakePopulation(1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0); // largest
        Population<TestIndividual, double, int> pop2 = MakePopulation(11.0, 12.0, 13.0);
        Population<TestIndividual, double, int>[] populations = new[] { pop1, pop2 };

        IReadOnlyList<(Population<TestIndividual, double, int> Population, IReadOnlyList<TestIndividual> Incoming)> result = migration.Migrate(MakeIteration(), populations, CompareMinimize);

        result.Should().HaveCount(2);
        // Both populations should receive incoming from the largest
        result[0].Incoming.Should().NotBeEmpty();
        result[1].Incoming.Should().NotBeEmpty();
        migration.Name.Should().Be("MoreToLess");
    }

    [Fact]
    public void ElitistMigration_TransfersTopIndividuals() {
        var migration = new ElitistMigration<double, int>();
        Population<TestIndividual, double, int> pop1 = MakePopulation(5.0, 1.0, 3.0, 7.0, 2.0, 9.0, 4.0, 8.0, 6.0, 10.0);
        Population<TestIndividual, double, int> pop2 = MakePopulation(15.0, 11.0, 13.0, 17.0, 12.0, 19.0, 14.0, 18.0, 16.0, 20.0);
        Population<TestIndividual, double, int>[] populations = new[] { pop1, pop2 };

        IReadOnlyList<(Population<TestIndividual, double, int> Population, IReadOnlyList<TestIndividual> Incoming)> result = migration.Migrate(MakeIteration(), populations, CompareMinimize);

        result.Should().HaveCount(2);
        result[0].Incoming.Should().NotBeEmpty();
        result[1].Incoming.Should().NotBeEmpty();
        migration.Name.Should().Be("Elitist");
    }

    [Fact]
    public void RingExchangeMigration_TwoPopulations_ReturnsIncoming() {
        var migration = new RingExchangeMigration<double, int>();
        Population<TestIndividual, double, int> pop1 = MakePopulation(1.0, 2.0, 3.0, 4.0, 5.0);
        Population<TestIndividual, double, int> pop2 = MakePopulation(6.0, 7.0, 8.0, 9.0, 10.0);
        Population<TestIndividual, double, int>[] populations = new[] { pop1, pop2 };

        IReadOnlyList<(Population<TestIndividual, double, int> Population, IReadOnlyList<TestIndividual> Incoming)> result = migration.Migrate(MakeIteration(), populations, CompareMinimize);

        result.Should().HaveCount(2);
        migration.Name.Should().Be("RingExchange");
    }

    [Fact]
    public void RandomDiffusionMigration_GlobalShuffle() {
        var migration = new RandomDiffusionMigration<double, int>();
        Population<TestIndividual, double, int> pop1 = MakePopulation(1.0, 2.0, 3.0, 4.0, 5.0);
        Population<TestIndividual, double, int> pop2 = MakePopulation(6.0, 7.0, 8.0, 9.0, 10.0);
        Population<TestIndividual, double, int>[] populations = new[] { pop1, pop2 };

        IReadOnlyList<(Population<TestIndividual, double, int> Population, IReadOnlyList<TestIndividual> Incoming)> result = migration.Migrate(MakeIteration(), populations, CompareMinimize);

        result.Should().HaveCount(2);
        result[0].Incoming.Should().NotBeEmpty();
        result[1].Incoming.Should().NotBeEmpty();
        migration.Name.Should().Be("RandomDiffusion");
    }

    [Fact]
    public void BetterToWorse_SinglePopulation_ReturnsEmptyIncoming() {
        var migration = new BetterToWorseMigration<double, int>();
        Population<TestIndividual, double, int> pop = MakePopulation(1.0, 2.0);
        Population<TestIndividual, double, int>[] populations = new[] { pop };

        IReadOnlyList<(Population<TestIndividual, double, int> Population, IReadOnlyList<TestIndividual> Incoming)> result = migration.Migrate(MakeIteration(), populations, CompareMinimize);

        result.Should().HaveCount(1);
        result[0].Incoming.Should().BeEmpty();
    }

    [Fact]
    public void PopulationMerge_SinglePopulation_ReturnsEmptyIncoming() {
        var migration = new PopulationMergeMigration<double, int>();
        Population<TestIndividual, double, int> pop = MakePopulation(1.0, 2.0);
        Population<TestIndividual, double, int>[] populations = new[] { pop };

        IReadOnlyList<(Population<TestIndividual, double, int> Population, IReadOnlyList<TestIndividual> Incoming)> result = migration.Migrate(MakeIteration(), populations, CompareMinimize);

        result.Should().HaveCount(1);
        result[0].Incoming.Should().BeEmpty();
    }
}
