using System.Collections.Generic;
using System.Linq;
#nullable enable

using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Math.Fractal;

namespace Fuookami.Ospf.Math.Tests.Fractal
{
    public class MandelbrotSetTest
    {
        [Fact]
        public void MandelbrotStep_ShouldMatchDefinition()
        {
            var set = MandelbrotSet<Flt64>.Create(Flt64.Zero, Flt64.Zero);
            var next = set.Invoke(PointFactory.point2(Flt64.One, Flt64.One));

            Assert.True(next[0].Eq(Flt64.Zero));
            Assert.True(next[1].Eq(new Flt64(2.0)));
        }

        [Fact]
        public void Constructor_ShouldKeepCPoint()
        {
            var set = MandelbrotSet<Flt64>.Create(new Flt64(0.3), new Flt64(-0.5));
            Assert.True(set.C[0].Eq(new Flt64(0.3)));
            Assert.True(set.C[1].Eq(new Flt64(-0.5)));
        }

        [Fact]
        public void Generator_ShouldReturnCurrentThenAdvance()
        {
            var initial = PointFactory.point2(Flt64.One, Flt64.One);
            var generator = MandelbrotSetGenerator.Create(Flt64.Zero, Flt64.Zero, initial);

            var current = generator.Invoke();
            Assert.True(current[0].Eq(initial[0]));
            Assert.True(current[1].Eq(initial[1]));
            Assert.True(generator.Z[0].Eq(Flt64.Zero));
            Assert.True(generator.Z[1].Eq(new Flt64(2.0)));
        }
    }
}
