#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 二进制变换 / Dyadic Transformation.
/// x_{n+1} = 2*x mod 1.
/// </summary>
public sealed record DyadicTransformation<V>(V Two, V One) : IExtractor<V, V>
    where V : struct, IFloatingNumber<V> {
    public V Invoke(V x) {
        double val = ((Flt64)(object)Two.Times(x)).Value;
        return (V)(object)new Flt64(val - global::System.Math.Floor(val));
    }

    public static DyadicTransformation<Flt64> Create()
        => new(new Flt64(2.0), Flt64.One);
}

public sealed class DyadicTransformationGenerator : IGenerator<Flt64> {
    public DyadicTransformation<Flt64> Map { get; }
    public Flt64 X { get; private set; }

    public DyadicTransformationGenerator(DyadicTransformation<Flt64>? map = null, Flt64? x = null) {
        Map = map ?? DyadicTransformation<Flt64>.Create();
        X = x ?? new Flt64(0.1);
    }

    public Flt64 Invoke() {
        Flt64 cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
