#nullable enable

using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic
{
    /// <summary>
    /// 埃农映射 / Henon Map. x_{n+1} = 1 - a*x^2 + y, y_{n+1} = b*x.
    /// </summary>
    public sealed record HenonMap<V>(V A, V B) : IExtractor<Point<Dim2, V>, Point<Dim2, V>>
        where V : struct, IFloatingNumber<V>
    {
        public Point<Dim2, V> Invoke(Point<Dim2, V> p)
        {
            var x = p[0];
            var y = p[1];
            return new Point<Dim2, V>(new V[] {
                x.Constants.One.Minus(A.Times(x).Times(x)).Plus(y),
                B.Times(x)
            }, Dim2.Instance);
        }

        public static HenonMap<Flt64> Create(Flt64? a = null, Flt64? b = null)
            => new(a ?? new Flt64(1.4), b ?? new Flt64(0.3));
    }

    public sealed class HenonMapGenerator : IGenerator<Point<Dim2, Flt64>>
    {
        public HenonMap<Flt64> Map { get; }
        public Point<Dim2, Flt64> X { get; private set; }

        public HenonMapGenerator(HenonMap<Flt64>? map = null, Point<Dim2, Flt64>? x = null)
        {
            Map = map ?? HenonMap<Flt64>.Create();
            X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
        }

        public static HenonMapGenerator Create(Flt64 a, Flt64 b, Point<Dim2, Flt64>? x = null)
            => new(HenonMap<Flt64>.Create(a, b), x);

        public Point<Dim2, Flt64> Invoke()
        {
            var cur = X;
            X = Map.Invoke(X);
            return cur;
        }
    }
}
