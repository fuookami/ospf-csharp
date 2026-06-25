#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using System;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests.Infrastructure;
/// <summary>
/// iteration counter tests.
/// iteration counter tests.
/// </summary>
public class IterationTest {
    /// <summary>
    /// Iteration should start at zero.
    /// </summary>
    [Fact]
    public void Constructor_ShouldStartAtZero() {
        // Act
        var iteration = new Iteration();

        // Assert
        iteration.Current.Should().Be(0);
        iteration.NotBetterIteration.Should().Be(0);
    }

    /// <summary>
    /// Iteration.Next with improvement should reset NotBetterIteration.
    /// </summary>
    [Fact]
    public void Next_WithImprovement_ShouldResetNotBetterIteration() {
        // Arrange
        var iteration = new Iteration();
        iteration.Next(false); // not better
        iteration.Next(false); // not better

        // Act
        iteration.Next(true); // better

        // Assert
        iteration.Current.Should().Be(3);
        iteration.NotBetterIteration.Should().Be(0);
    }

    /// <summary>
    /// Iteration.Next without improvement should increment NotBetterIteration.
    /// </summary>
    [Fact]
    public void Next_WithoutImprovement_ShouldIncrementNotBetterIteration() {
        // Arrange
        var iteration = new Iteration();

        // Act
        iteration.Next(false);
        iteration.Next(false);
        iteration.Next(false);

        // Assert
        iteration.Current.Should().Be(3);
        iteration.NotBetterIteration.Should().Be(3);
    }

    /// <summary>
    /// Iteration.Next should track mixed improvement pattern.
    /// </summary>
    [Fact]
    public void Next_MixedPattern_ShouldTrackCorrectly() {
        // Arrange
        var iteration = new Iteration();

        // Act
        iteration.Next(false); // Current=1, NotBetter=1
        iteration.Next(false); // Current=2, NotBetter=2
        iteration.Next(true);  // Current=3, NotBetter=0
        iteration.Next(false); // Current=4, NotBetter=1

        // Assert
        iteration.Current.Should().Be(4);
        iteration.NotBetterIteration.Should().Be(1);
    }

    /// <summary>
    /// Iteration.Reset should reset all counters.
    /// </summary>
    [Fact]
    public void Reset_ShouldResetAllCounters() {
        // Arrange
        var iteration = new Iteration();
        iteration.Next(false);
        iteration.Next(true);
        iteration.Next(false);

        // Act
        iteration.Reset();

        // Assert
        iteration.Current.Should().Be(0);
        iteration.NotBetterIteration.Should().Be(0);
    }

    /// <summary>
    /// Iteration.Time should be non-negative after construction.
    /// </summary>
    [Fact]
    public void Time_ShouldBeNonNegative() {
        // Act
        var iteration = new Iteration();

        // Assert
        iteration.Time.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }
}
