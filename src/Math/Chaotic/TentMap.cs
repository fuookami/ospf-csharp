#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 帐篷映射 / Tent Map.
/// x_{n+1} = mu*x if x &lt; 0.5, mu*(1-x) if x &gt;= 0.5.
/// </summary>
public sealed record TentMap<V>(V Mu) : IExtractor<V, V>
    where V : struct, IFloatingNumber<V> {
    public V Invoke(V x) {
        V half = x.Constants.Half;
        return x.Ls(half)
            ? Mu.Times(x)
            : Mu.Times(x.Constants.One.Minus(x));
    }

    public static TentMap<Flt64> Create(Flt64? mu = null)
        => new(mu ?? new Flt64(1.5));
}

public sealed class TentMapGenerator : IGenerator<Flt64> {
    public TentMap<Flt64> Map { get; }
    public Flt64 X { get; private set; }

    public TentMapGenerator(TentMap<Flt64>? map = null, Flt64? x = null) {
        Map = map ?? TentMap<Flt64>.Create();
        X = x ?? new Flt64(0.3);
    }

    public Flt64 Invoke() {
        Flt64 cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
