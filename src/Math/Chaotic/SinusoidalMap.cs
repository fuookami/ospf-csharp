#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 正弦平方映射 / Sinusoidal Map.
/// x_{n+1} = mu * x^2 * sin(pi*x).
/// </summary>
public sealed record SinusoidalMap<V>(V Mu) : IExtractor<V, V>
    where V : struct, IFloatingNumber<V> {
    public V Invoke(V x) {
        double mu = ((Flt64)(object)Mu).Value;
        double xVal = ((Flt64)(object)x).Value;
        double pi = ((Flt64)(object)x.Constants.Pi).Value;
        return (V)(object)new Flt64(mu * xVal * xVal * global::System.Math.Sin(pi * xVal));
    }

    public static SinusoidalMap<Flt64> Create(Flt64? mu = null)
        => new(mu ?? new Flt64(2.3));
}

public sealed class SinusoidalMapGenerator : IGenerator<Flt64> {
    public SinusoidalMap<Flt64> Map { get; }
    public Flt64 X { get; private set; }

    public SinusoidalMapGenerator(SinusoidalMap<Flt64>? map = null, Flt64? x = null) {
        Map = map ?? SinusoidalMap<Flt64>.Create();
        X = x ?? new Flt64(0.5);
    }

    public Flt64 Invoke() {
        Flt64 cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
