#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 切比雪夫映射 / Chebyshev Map. x_{n+1} = cos(a * acos(x)).
/// Flt64-only (uses trig functions).
/// </summary>
public sealed record ChebyshevMap<V>(V A) : IExtractor<V, V>
    where V : struct, IFloatingNumber<V> {
    public V Invoke(V x) {
        // For generic V, this is a placeholder. Concrete Flt64 usage via factory.
        V zero = x.Constants.Zero;
        V one = x.Constants.One;
        if (x.Geq(one.Negate()) && x.Leq(one)) {
            // Generic: use acos/cos through the value's methods if available
            // For Flt64-specific, see ChebyshevMapFlt64Helper
            return ChebyshevMapFlt64Helper.Invoke(A, x);
        }
        return zero;
    }

    public static ChebyshevMap<Flt64> Create(Flt64? a = null)
        => new(a ?? new Flt64(6.0));
}

internal static class ChebyshevMapFlt64Helper {
    public static V Invoke<V>(V a, V x) where V : struct, IFloatingNumber<V> {
        // Use double math for trig operations
        double xDbl = ((Flt64)(object)x).Value;
        double aDbl = ((Flt64)(object)a).Value;
        double acosVal = global::System.Math.Acos(xDbl);
        double result = global::System.Math.Cos(aDbl * acosVal);
        return (V)(object)new Flt64(result);
    }
}

public sealed class ChebyshevMapGenerator : IGenerator<Flt64> {
    public ChebyshevMap<Flt64> Map { get; }
    public Flt64 X { get; private set; }

    public ChebyshevMapGenerator(ChebyshevMap<Flt64>? map = null, Flt64? x = null) {
        Map = map ?? ChebyshevMap<Flt64>.Create();
        X = x ?? new Flt64(0.5);
    }

    public Flt64 Invoke() {
        Flt64 cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
