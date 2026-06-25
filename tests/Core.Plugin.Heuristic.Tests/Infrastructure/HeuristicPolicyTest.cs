#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests.Infrastructure;
/// <summary>
/// heuristic policy base class tests.
/// heuristic policy base class tests.
/// </summary>
public class HeuristicPolicyTest {
    private sealed class TestPolicy : HeuristicPolicy {
        public override string Name => "Test";
    }

    /// <summary>
    /// HeuristicPolicy default values should be set correctly.
    /// </summary>
    [Fact]
    public void DefaultValues_ShouldBeCorrect() {
        // Act
        var policy = new TestPolicy();

        // Assert
        policy.Name.Should().Be("Test");
        policy.MaxIterations.Should().Be(1000);
        policy.PopulationSize.Should().Be(100);
        policy.MaxIterationsWithoutImprovement.Should().Be(100);
    }

    /// <summary>
    /// HeuristicPolicy.Finished should return false when under limits.
    /// </summary>
    [Fact]
    public void Finished_UnderLimits_ShouldReturnFalse() {
        // Arrange
        var policy = new TestPolicy();
        var iteration = new Iteration();
        iteration.Next(true);

        // Act
        bool result = policy.Finished(iteration);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// HeuristicPolicy.Finished should return true at MaxIterations.
    /// </summary>
    [Fact]
    public void Finished_AtMaxIterations_ShouldReturnTrue() {
        // Arrange
        var policy = new TestPolicy { MaxIterations = 5 };
        var iteration = new Iteration();
        for (int i = 0; i < 5; i++) {
            iteration.Next(true);
        }

        // Act
        bool result = policy.Finished(iteration);

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// HeuristicPolicy.Finished should return true at MaxIterationsWithoutImprovement.
    /// </summary>
    [Fact]
    public void Finished_AtMaxIterationsWithoutImprovement_ShouldReturnTrue() {
        // Arrange
        var policy = new TestPolicy { MaxIterationsWithoutImprovement = 3 };
        var iteration = new Iteration();
        iteration.Next(false);
        iteration.Next(false);
        iteration.Next(false);

        // Act
        bool result = policy.Finished(iteration);

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// HeuristicPolicy init properties should be settable.
    /// </summary>
    [Fact]
    public void InitProperties_ShouldBeSettable() {
        // Act
        var policy = new TestPolicy {
            MaxIterations = 500,
            PopulationSize = 50,
            MaxIterationsWithoutImprovement = 25
        };

        // Assert
        policy.MaxIterations.Should().Be(500);
        policy.PopulationSize.Should().Be(50);
        policy.MaxIterationsWithoutImprovement.Should().Be(25);
    }
}
