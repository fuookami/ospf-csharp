#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Sinus 映射 / Sinus Map.
/// x_{n+1} = 2.3 * x^(2*sin(pi*x)).
/// </summary>
public sealed record SinusMap<V>(V C23, V C2) : IExtractor<V, V>
    where V : struct, IFloatingNumber<V> {
    public V Invoke(V x) {
        double pi = ((Flt64)(object)x.Constants.Pi).Value;
        double xVal = ((Flt64)(object)x).Value;
        double sinVal = global::System.Math.Sin(pi * xVal);
        double exponent = 2.0 * sinVal;
        double c23 = ((Flt64)(object)C23).Value;
        return (V)(object)new Flt64(c23 * global::System.Math.Pow(xVal, exponent));
    }

    public static SinusMap<Flt64> Create()
        => new(new Flt64(2.3), new Flt64(2.0));
}

public sealed class SinusMapGenerator : IGenerator<Flt64> {
    public SinusMap<Flt64> Map { get; }
    public Flt64 X { get; private set; }

    public SinusMapGenerator(SinusMap<Flt64>? map = null, Flt64? x = null) {
        Map = map ?? SinusMap<Flt64>.Create();
        X = x ?? new Flt64(0.5);
    }

    public Flt64 Invoke() {
        Flt64 cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
