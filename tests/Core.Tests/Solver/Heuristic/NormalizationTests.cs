#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Heuristic;

public class NormalizationTests {
    private static readonly Func<double, double, int> CompareMinimize = (a, b) => a.CompareTo(b);
    private static readonly Func<double, double, int> CompareMaximize = (a, b) => b.CompareTo(a);

    [Fact]
    public void MinMaxNormalization_NormalizesToRange() {
        var normalization = new MinMaxNormalization<double, int>();
        var objectives = new List<double> { 10.0, 20.0, 30.0, 40.0, 50.0 };

        IReadOnlyList<double> weights = normalization.Normalize(CompareMinimize, objectives);

        weights.Should().HaveCount(5);
        // For minimization, lowest value (10) should have highest weight (1.0)
        weights[0].Should().BeApproximately(1.0, 1e-10);
        // Highest value (50) should have lowest weight (0.0)
        weights[4].Should().BeApproximately(0.0, 1e-10);
        // Middle value should be around 0.5
        weights[2].Should().BeApproximately(0.5, 1e-10);
    }

    [Fact]
    public void MinMaxNormalization_EmptyList_ReturnsEmpty() {
        var normalization = new MinMaxNormalization<double, int>();
        var objectives = new List<double>();

        IReadOnlyList<double> weights = normalization.Normalize(CompareMinimize, objectives);

        weights.Should().BeEmpty();
    }

    [Fact]
    public void MinMaxNormalization_SingleValue_ReturnsOne() {
        var normalization = new MinMaxNormalization<double, int>();
        var objectives = new List<double> { 42.0 };

        IReadOnlyList<double> weights = normalization.Normalize(CompareMinimize, objectives);

        weights.Should().HaveCount(1);
        weights[0].Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void MinMaxNormalization_AllEqualValues_ReturnsUniformWeights() {
        var normalization = new MinMaxNormalization<double, int>();
        var objectives = new List<double> { 5.0, 5.0, 5.0, 5.0 };

        IReadOnlyList<double> weights = normalization.Normalize(CompareMinimize, objectives);

        weights.Should().HaveCount(4);
        foreach (double w in weights) {
            w.Should().BeApproximately(0.25, 1e-10);
        }
    }

    [Fact]
    public void MinMaxNormalization_Maximization_HighestGetsHighestWeight() {
        var normalization = new MinMaxNormalization<double, int>();
        var objectives = new List<double> { 10.0, 20.0, 30.0, 40.0, 50.0 };

        IReadOnlyList<double> weights = normalization.Normalize(CompareMaximize, objectives);

        // For maximization, highest value (50) should have highest weight (1.0)
        weights[4].Should().BeApproximately(1.0, 1e-10);
        weights[0].Should().BeApproximately(0.0, 1e-10);
    }

    [Fact]
    public void SumNormalization_ProducesWeights() {
        var normalization = new SumNormalization<double, int>();
        var objectives = new List<double> { 10.0, 20.0, 30.0 };

        IReadOnlyList<double> weights = normalization.Normalize(CompareMinimize, objectives);

        weights.Should().HaveCount(3);
        // All weights should be non-negative
        foreach (double w in weights) {
            w.Should().BeGreaterThanOrEqualTo(0.0);
        }
    }

    [Fact]
    public void SumNormalization_EmptyList_ReturnsEmpty() {
        var normalization = new SumNormalization<double, int>();
        var objectives = new List<double>();

        IReadOnlyList<double> weights = normalization.Normalize(CompareMinimize, objectives);

        weights.Should().BeEmpty();
    }

    [Fact]
    public void SumNormalization_SingleValue_ReturnsOne() {
        var normalization = new SumNormalization<double, int>();
        var objectives = new List<double> { 42.0 };

        IReadOnlyList<double> weights = normalization.Normalize(CompareMinimize, objectives);

        weights.Should().HaveCount(1);
        weights[0].Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void SumNormalization_AllEqualValues_ReturnsUniformWeights() {
        var normalization = new SumNormalization<double, int>();
        var objectives = new List<double> { 7.0, 7.0, 7.0 };

        IReadOnlyList<double> weights = normalization.Normalize(CompareMinimize, objectives);

        weights.Should().HaveCount(3);
        foreach (double w in weights) {
            w.Should().BeApproximately(1.0 / 3.0, 1e-10);
        }
    }
}
