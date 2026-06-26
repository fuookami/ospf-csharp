#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 高斯迭代映射 / Gauss Iterated Map.
/// x_{n+1} = exp(-a*x^2) + b.
/// </summary>
public sealed record GaussIteratedMap<V>(V A, V B) : IExtractor<V, V>
    where V : struct, IFloatingNumber<V> {
    public V Invoke(V x) {
        double a = ((Flt64)(object)A).Value;
        double xVal = ((Flt64)(object)x).Value;
        double b = ((Flt64)(object)B).Value;
        return (V)(object)new Flt64(global::System.Math.Exp(-a * xVal * xVal) + b);
    }

    public static GaussIteratedMap<Flt64> Create(Flt64? a = null, Flt64? b = null)
        => new(a ?? new Flt64(4.9), b ?? new Flt64(0.1));
}

public sealed class GaussIteratedMapGenerator : IGenerator<Flt64> {
    public GaussIteratedMap<Flt64> Map { get; }
    public Flt64 X { get; private set; }

    public GaussIteratedMapGenerator(GaussIteratedMap<Flt64>? map = null, Flt64? x = null) {
        Map = map ?? GaussIteratedMap<Flt64>.Create();
        X = x ?? new Flt64(0.1);
    }

    public Flt64 Invoke() {
        Flt64 cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
