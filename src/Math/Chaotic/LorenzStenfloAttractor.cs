#nullable enable

using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic
{
    /// <summary>
    /// Lorenz-Stenflo 吸引子（四维）/ Lorenz-Stenflo Attractor (4D).
    /// </summary>
    public sealed record LorenzStenfloAttractor<V>(V Alpha, V Beta, V Delta, V Zeta, V H)
        : IExtractor<Point<Dim4, V>, Point<Dim4, V>>
        where V : struct, IFloatingNumber<V>
    {
        public Point<Dim4, V> Invoke(Point<Dim4, V> p)
        {
            var x = p[0]; var y = p[1]; var z = p[2]; var u = p[3];
            var dx = Alpha.Times(y.Minus(x)).Plus(Delta.Times(u));
            var dy = x.Times(Zeta.Minus(z)).Minus(y);
            var dz = x.Times(y).Minus(Beta.Times(z));
            var du = x.Negate().Minus(Alpha.Times(u));
            return new Point<Dim4, V>(new V[] {
                x.Plus(H.Times(dx)),
                y.Plus(H.Times(dy)),
                z.Plus(H.Times(dz)),
                u.Plus(H.Times(du))
            }, Dim4.Instance);
        }

        public static LorenzStenfloAttractor<Flt64> Create(
            Flt64? alpha = null, Flt64? beta = null, Flt64? delta = null, Flt64? zeta = null, Flt64? h = null)
            => new(alpha ?? new Flt64(2.0), beta ?? new Flt64(0.7), delta ?? new Flt64(1.5),
                   zeta ?? new Flt64(26.0), h ?? new Flt64(0.01));
    }

    public sealed class LorenzStenfloAttractorGenerator : IGenerator<Point<Dim4, Flt64>>
    {
        public LorenzStenfloAttractor<Flt64> System { get; }
        public Point<Dim4, Flt64> X { get; private set; }

        public LorenzStenfloAttractorGenerator(LorenzStenfloAttractor<Flt64>? system = null, Point<Dim4, Flt64>? x = null)
        {
            System = system ?? LorenzStenfloAttractor<Flt64>.Create();
            X = x ?? PointFactory.point4(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
        }

        public Point<Dim4, Flt64> Invoke()
        {
            var cur = X;
            X = System.Invoke(X);
            return cur;
        }
    }
}
