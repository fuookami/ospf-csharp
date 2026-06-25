#nullable enable

using System;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math.Chaotic
{
    /// <summary>
    /// 圆映射 / Circle Map. x_{n+1} = x + alpha - beta*sin(2*pi*x) (mod 1).
    /// Flt64-only (uses trig functions).
    /// </summary>
    public sealed record CircleMap<V>(V Alpha, V Beta) : IExtractor<V, V>
        where V : struct, IFloatingNumber<V>
    {
        public V Invoke(V x)
        {
            var one = x.Constants.One;
            var pi2 = x.Constants.Pi.Times(((IHasTwo<V>)x.Constants).Two);
            var sinVal = new Flt64(global::System.Math.Sin(((Flt64)(object)pi2).Value * ((Flt64)(object)x).Value));
            var rawDbl = ((Flt64)(object)x).Value + ((Flt64)(object)Alpha).Value
                - ((Flt64)(object)Beta).Value * sinVal.Value / ((Flt64)(object)pi2).Value;
            var floored = global::System.Math.Floor(rawDbl);
            return (V)(object)new Flt64(rawDbl - floored);
        }

        public static CircleMap<Flt64> Create(Flt64? alpha = null, Flt64? beta = null)
            => new(alpha ?? new Flt64(0.5), beta ?? new Flt64(0.5));
    }

    public sealed class CircleMapGenerator : IGenerator<Flt64>
    {
        public CircleMap<Flt64> Map { get; }
        public Flt64 X { get; private set; }

        public CircleMapGenerator(CircleMap<Flt64>? map = null, Flt64? x = null)
        {
            Map = map ?? CircleMap<Flt64>.Create();
            X = x ?? new Flt64(0.3);
        }

        public Flt64 Invoke()
        {
            var cur = X;
            X = Map.Invoke(X);
            return cur;
        }
    }
}
