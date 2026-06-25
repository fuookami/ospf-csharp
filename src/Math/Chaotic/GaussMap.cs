#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 高斯映射 / Gauss Map. x_{n+1} = mu/x (or 0 if x==0).
/// </summary>
public sealed record GaussMap<V>(V Mu) : IExtractor<V, V>
    where V : struct, IFloatingNumber<V> {
    public V Invoke(V x) {
        V zero = x.Constants.Zero;
        return x.Eq(zero) ? zero : Mu.Div(x);
    }

    public static GaussMap<Flt64> Create(Flt64? mu = null)
        => new(mu ?? new Flt64(6.2));
}

public sealed class GaussMapGenerator : IGenerator<Flt64> {
    public GaussMap<Flt64> Map { get; }
    public Flt64 X { get; private set; }

    public GaussMapGenerator(GaussMap<Flt64>? map = null, Flt64? x = null) {
        Map = map ?? GaussMap<Flt64>.Create();
        X = x ?? new Flt64(0.5);
    }

    public Flt64 Invoke() {
        Flt64 cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
