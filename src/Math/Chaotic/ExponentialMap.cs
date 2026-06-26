#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 指数映射 / Exponential Map.
/// z_{n+1} = exp(z) + c.
/// </summary>
public sealed record ExponentialMap<V>(V C) : IExtractor<V, V>
    where V : struct, IFloatingNumber<V> {
    public V Invoke(V z) {
        double val = ((Flt64)(object)z).Value;
        double c = ((Flt64)(object)C).Value;
        return (V)(object)new Flt64(global::System.Math.Exp(val) + c);
    }

    public static ExponentialMap<Flt64> Create(Flt64? c = null)
        => new(c ?? new Flt64(0.5));
}

public sealed class ExponentialMapGenerator : IGenerator<Flt64> {
    public ExponentialMap<Flt64> Map { get; }
    public Flt64 X { get; private set; }

    public ExponentialMapGenerator(ExponentialMap<Flt64>? map = null, Flt64? x = null) {
        Map = map ?? ExponentialMap<Flt64>.Create();
        X = x ?? new Flt64(0.1);
    }

    public Flt64 Invoke() {
        Flt64 cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
