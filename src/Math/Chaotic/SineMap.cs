#nullable enable

using System;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math.Chaotic
{
    /// <summary>
    /// 正弦映射 / Sine Map. x_{n+1} = mu * sin(pi * x).
    /// Flt64-only (uses trig functions).
    /// </summary>
    public sealed record SineMap<V>(V Mu) : IExtractor<V, V>
        where V : struct, IFloatingNumber<V>
    {
        public V Invoke(V x)
        {
            var piDbl = ((Flt64)(object)x.Constants.Pi).Value;
            var xDbl = ((Flt64)(object)x).Value;
            var muDbl = ((Flt64)(object)Mu).Value;
            return (V)(object)new Flt64(muDbl * global::System.Math.Sin(piDbl * xDbl));
        }

        public static SineMap<Flt64> Create(Flt64? mu = null)
            => new(mu ?? new Flt64(1.0));
    }

    public sealed class SineMapGenerator : IGenerator<Flt64>
    {
        public SineMap<Flt64> Map { get; }
        public Flt64 X { get; private set; }

        public SineMapGenerator(SineMap<Flt64>? map = null, Flt64? x = null)
        {
            Map = map ?? SineMap<Flt64>.Create();
            X = x ?? new Flt64(0.5);
        }

        public Flt64 Invoke()
        {
            var cur = X;
            X = Map.Invoke(X);
            return cur;
        }
    }
}
