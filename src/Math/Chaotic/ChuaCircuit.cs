#nullable enable

using System;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;

namespace Fuookami.Ospf.Math.Chaotic
{
    /// <summary>
    /// 蔡氏电路 / Chua's Circuit.
    /// 三维连续混沌系统，含分段线性非线性。/ 3D continuous chaotic system with piecewise-linear nonlinearity.
    /// </summary>
    public sealed record ChuaCircuit<V>(V A, V B, V C, V D, V H)
        : IExtractor<Point<Dim3, V>, Point<Dim3, V>>
        where V : struct, IFloatingNumber<V>
    {
        public Point<Dim3, V> Invoke(Point<Dim3, V> x)
        {
            var one = x[0].Constants.One;
            var two = ((IHasTwo<V>)x[0].Constants).Two;
            var half = one.Div(two);
            var f = C.Times(x[0]).Plus(half.Times(D.Minus(C)).Times(x[0].Plus(one).Abs().Minus(x[0].Minus(one).Abs())));
            var dx = A.Times(x[1].Minus(x[0]).Minus(f));
            var dy = x[0].Minus(x[1]).Plus(x[2]);
            var dz = B.Negate().Times(x[1]);
            return new Point<Dim3, V>(new V[] {
                x[0].Plus(H.Times(dx)),
                x[1].Plus(H.Times(dy)),
                x[2].Plus(H.Times(dz))
            }, Dim3.Instance);
        }

        public static ChuaCircuit<Flt64> Create(
            Flt64? a = null, Flt64? b = null, Flt64? c = null, Flt64? d = null, Flt64? h = null)
            => new(a ?? new Flt64(15.6), b ?? new Flt64(28.0), c ?? new Flt64(-0.71), d ?? new Flt64(-1.14), h ?? new Flt64(0.01));
    }

    public sealed class ChuaCircuitGenerator : IGenerator<Point<Dim3, Flt64>>
    {
        public ChuaCircuit<Flt64> System { get; }
        public Point<Dim3, Flt64> X { get; private set; }

        public ChuaCircuitGenerator(ChuaCircuit<Flt64>? system = null, Point<Dim3, Flt64>? x = null)
        {
            System = system ?? ChuaCircuit<Flt64>.Create();
            X = x ?? PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1));
        }

        public Point<Dim3, Flt64> Invoke()
        {
            var cur = X;
            X = System.Invoke(X);
            return cur;
        }
    }
}
