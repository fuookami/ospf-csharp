#nullable enable

using System;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic
{
    /// <summary>
    /// 面包师映射 / Baker's Map. Flt64-only (singleton, no params).
    /// </summary>
    public sealed record BakersMap : IExtractor<Point<Dim2, Flt64>, Point<Dim2, Flt64>>
    {
        public static readonly BakersMap Instance = new();

        public Point<Dim2, Flt64> Invoke(Point<Dim2, Flt64> x)
        {
            var twoX = new Flt64(2.0 * x[0].Value);
            var newX = new Flt64(twoX.Value - global::System.Math.Floor(twoX.Value)); // mod 1
            var newY = new Flt64((global::System.Math.Floor(twoX.Value) + x[1].Value) / 2.0);
            return PointFactory.point2(newX, newY);
        }
    }

    public sealed class BakersMapGenerator : IGenerator<Point<Dim2, Flt64>>
    {
        public Point<Dim2, Flt64> X { get; private set; }

        public BakersMapGenerator(Point<Dim2, Flt64>? x = null)
        {
            X = x ?? PointFactory.point2(new Flt64(0.1), new Flt64(0.1));
        }

        public Point<Dim2, Flt64> Invoke()
        {
            var cur = X;
            X = BakersMap.Instance.Invoke(X);
            return cur;
        }
    }
}
