#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver
{
    public class GapTests
    {
        [Fact]
        public void Compute_SameValues_ShouldReturnZero()
        {
            var obj = new Flt64(100.0);
            var best = new Flt64(100.0);
            Gap.Compute(obj, best).Should().Be(Flt64.Zero);
        }

        [Fact]
        public void Compute_BothZero_ShouldReturnZero()
        {
            Gap.Compute(Flt64.Zero, Flt64.Zero).Should().Be(Flt64.Zero);
        }

        [Fact]
        public void Compute_DifferentValues_ShouldReturnPositiveGap()
        {
            var obj = new Flt64(100.0);
            var best = new Flt64(90.0);
            var gap = Gap.Compute(obj, best);
            gap.ToDouble().Should().BeGreaterThan(0.0);
        }

        [Fact]
        public void Compute_DifferentValues_ShouldBeSymmetric()
        {
            var a = new Flt64(100.0);
            var b = new Flt64(90.0);
            var gap1 = Gap.Compute(a, b);
            var gap2 = Gap.Compute(b, a);
            gap1.Should().Be(gap2);
        }

        [Fact]
        public void Compute_KnownValues_ShouldBeCorrect()
        {
            // |100 - 90| / max(|100|, |90|) = 10/100 = 0.1
            var obj = new Flt64(100.0);
            var best = new Flt64(90.0);
            var gap = Gap.Compute(obj, best);
            gap.ToDouble().Should().BeApproximately(0.1, 1e-10);
        }

        [Fact]
        public void Compute_NegativeValues_ShouldWork()
        {
            // |-100 - -90| / max(|-100|, |-90|) = 10/100 = 0.1
            var obj = new Flt64(-100.0);
            var best = new Flt64(-90.0);
            var gap = Gap.Compute(obj, best);
            gap.ToDouble().Should().BeApproximately(0.1, 1e-10);
        }

        [Fact]
        public void Compute_MixedSignValues_ShouldWork()
        {
            // |10 - (-10)| / max(|10|, |-10|) = 20/10 = 2.0
            var obj = new Flt64(10.0);
            var best = new Flt64(-10.0);
            var gap = Gap.Compute(obj, best);
            gap.ToDouble().Should().BeApproximately(2.0, 1e-10);
        }

        [Fact]
        public void Compute_OneZero_ShouldWork()
        {
            // |0 - 100| / max(|0|, |100|) = 100/100 = 1.0
            var gap = Gap.Compute(Flt64.Zero, new Flt64(100.0));
            gap.ToDouble().Should().BeApproximately(1.0, 1e-10);
        }

        [Fact]
        public void Compute_SmallDifference_ShouldWork()
        {
            var obj = new Flt64(1000.0);
            var best = new Flt64(999.99);
            var gap = Gap.Compute(obj, best);
            gap.ToDouble().Should().BeGreaterThan(0.0);
            gap.ToDouble().Should().BeLessThan(0.01);
        }
    }
}
