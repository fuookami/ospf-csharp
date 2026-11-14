#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Distribution;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using DistributionModel = Fuookami.Ospf.Math.Distribution.Distribution;

namespace Fuookami.Ospf.Math.Tests.Distributions;

public class DistributionTest {
    private const int SampleAmount = 20000;

    private sealed class SeededEntropy(ulong seed = 20240521UL) {
        private ulong _state = seed;

        public Flt64? Next() {
            _state = unchecked(_state * 6364136223846793005UL + 1442695040888963407UL);
            return new Flt64((_state >> 11) * (1.0 / (1UL << 53)));
        }
    }

    [Fact]
    public void UniformMatchesTheoreticalMoments() {
        IReadOnlyList<double> samples = Samples(new Uniform(new Flt64(-2.0), new Flt64(4.0)));

        Assert.InRange(samples.Average(), 0.95, 1.05);
        Assert.InRange(Variance(samples), 2.85, 3.15);
        Assert.All(samples, value => Assert.InRange(value, -2.0, 4.0));
    }

    [Fact]
    public void GaussianMatchesStandardNormalMoments() {
        IReadOnlyList<double> samples = Samples(new Gaussian());

        Assert.InRange(samples.Average(), -0.03, 0.03);
        Assert.InRange(Variance(samples), 0.94, 1.06);
    }

    [Fact]
    public void CauchyIsCenteredAndHeavyTailed() {
        IReadOnlyList<double> samples = Samples(new Cauchy());

        Assert.InRange(Median(samples), -0.05, 0.05);
        Assert.InRange((double)samples.Count(value => System.Math.Abs(value) > 5.0) / samples.Count, 0.096, 0.156);
    }

    [Fact]
    public void ExponentialMatchesTheoreticalMean() {
        IReadOnlyList<double> samples = Samples(new Exponential(new Flt64(2.0)));

        Assert.InRange(samples.Average(), 0.48, 0.52);
    }

    [Fact]
    public void GammaMatchesTheoreticalMean() {
        IReadOnlyList<double> samples = Samples(new Gamma(new Flt64(2.0), new Flt64(3.0)));

        Assert.InRange(samples.Average(), 5.85, 6.15);
    }

    [Fact]
    public void BetaStaysInUnitIntervalWithTheoreticalMean() {
        IReadOnlyList<double> samples = Samples(new Beta(new Flt64(2.0), new Flt64(5.0)));

        Assert.All(samples, value => Assert.InRange(value, double.Epsilon, 1.0 - double.Epsilon));
        Assert.InRange(samples.Average(), 2.0 / 7.0 - 0.01, 2.0 / 7.0 + 0.01);
    }

    [Fact]
    public void StudentTIsCentered() {
        IReadOnlyList<double> samples = Samples(new StudentT(new Flt64(5.0)));

        Assert.All(samples, value => Assert.True(double.IsFinite(value)));
        Assert.InRange(samples.Average(), -0.08, 0.08);
    }

    [Fact]
    public void LevyIsSymmetricWithHeavyTails() {
        IReadOnlyList<double> samples = Samples(new Levy(new Flt64(1.5)));

        Assert.All(samples, value => Assert.True(double.IsFinite(value)));
        Assert.InRange(Median(samples), -0.1, 0.1);
        Assert.True(samples.Max(System.Math.Abs) > 10.0);
    }

    [Fact]
    public void TruncatedRespectsBounds() {
        IReadOnlyList<double> samples = Samples(new Truncated(
            Distribution: new Gaussian(StandardDeviation: new Flt64(10.0)),
            LowerBound: new Flt64(-1.0),
            UpperBound: new Flt64(1.0)));

        Assert.All(samples, value => Assert.InRange(value, -1.0, 1.0));
    }

    [Fact]
    public void DirectionSamplerProducesUnitVectors() {
        var entropy = new SeededEntropy();
        var sampler = new DirectionSampler(3);

        for (int iteration = 0; iteration < 200; iteration++) {
            Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> result = sampler.Invoke(entropy.Next);
            Assert.True(result.IsOk);
            double squaredNorm = result.Value.Sum(component => component.ToDouble() * component.ToDouble());
            Assert.InRange(System.Math.Sqrt(squaredNorm), 1.0 - 1.0e-12, 1.0 + 1.0e-12);
        }
    }

    [Fact]
    public void InvalidParametersReturnFailed() {
        var entropy = new SeededEntropy();

        Assert.True(new Uniform(new Flt64(2.0), new Flt64(1.0)).Invoke(entropy.Next).IsFailed);
        Assert.True(new Gaussian(new Flt64(0.0), new Flt64(-1.0)).Invoke(entropy.Next).IsFailed);
        Assert.True(new Cauchy(new Flt64(0.0), Flt64.Zero).Invoke(entropy.Next).IsFailed);
        Assert.True(new Exponential(Flt64.Zero).Invoke(entropy.Next).IsFailed);
        Assert.True(new Gamma(Flt64.Zero, Flt64.One).Invoke(entropy.Next).IsFailed);
        Assert.True(new Beta(Flt64.Zero, Flt64.One).Invoke(entropy.Next).IsFailed);
        Assert.True(new StudentT(Flt64.Zero).Invoke(entropy.Next).IsFailed);
        Assert.True(new Levy(new Flt64(3.0)).Invoke(entropy.Next).IsFailed);
        Assert.True(new Truncated(
            Distribution: new Gaussian(),
            LowerBound: Flt64.One,
            UpperBound: new Flt64(-1.0)).Invoke(entropy.Next).IsFailed);
        Assert.True(new DirectionSampler(0).Invoke(entropy.Next).IsFailed);
    }

    private static IReadOnlyList<double> Samples(DistributionModel distribution, int amount = SampleAmount) {
        var entropy = new SeededEntropy();
        double[] samples = new double[amount];
        for (int index = 0; index < samples.Length; index++) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> result = distribution.Invoke(entropy.Next);
            Assert.True(result.IsOk);
            samples[index] = result.Value.ToDouble();
        }
        return samples;
    }

    private static double Variance(IReadOnlyList<double> samples) {
        double mean = samples.Average();
        double accumulated = 0.0;
        for (int index = 0; index < samples.Count; index++) {
            double difference = samples[index] - mean;
            accumulated += difference * difference;
        }
        return accumulated / samples.Count;
    }

    private static double Median(IReadOnlyList<double> samples) {
        double[] sorted = samples.OrderBy(value => value).ToArray();
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2.0
            : sorted[middle];
    }
}
