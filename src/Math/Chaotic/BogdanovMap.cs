#nullable enable

using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic
{
    /// <summary>
    /// Bogdanov 映射 / Bogdanov Map.
    /// </summary>
    public sealed record BogdanovMap<V>(V Epsilon, V Kappa, V Mu)
        : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
        where V : struct, IFloatingNumber<V>
    {
        public Point<Dim2, V> Invoke(Point<Dim2, V> x)
        {
            var one = x[0].Constants.One;
            var temp = x[1].Plus(Epsilon.Times(x[1])).Plus(Kappa.Times(x[0]).Times(one.Minus(x[0]))).Plus(Mu.Times(x[0]).Times(x[1]));
            return new Point<Dim2, V>(new V[] { x[0].Plus(temp), temp }, Dim2.Instance);
        }

        public static BogdanovMap<Flt64> Create(Flt64? epsilon = null, Flt64? kappa = null, Flt64? mu = null)
            => new(epsilon ?? new Flt64(0.01), kappa ?? new Flt64(0.1), mu ?? new Flt64(0.01));
    }

    public sealed class BogdanovMapGenerator : IGenerator<Point<Dim2, Flt64>>
    {
        public BogdanovMap<Flt64> Map { get; }
        public Point<Dim2, Flt64> X { get; private set; }

        public BogdanovMapGenerator(BogdanovMap<Flt64>? map = null, Point<Dim2, Flt64>? x = null)
        {
            Map = map ?? BogdanovMap<Flt64>.Create();
            X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
        }

        public Point<Dim2, Flt64> Invoke()
        {
            var cur = X;
            X = Map.Invoke(X);
            return cur;
        }
    }
}
