#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Singer 映射 / Singer Map.
/// 一维混沌映射。/ 1D chaotic map.
/// </summary>
public sealed record SingerMap<V>(V Mu, V C786, V C2323, V C2875, V C1330) : IExtractor<V, V>
    where V : struct, IFloatingNumber<V> {
    public V Invoke(V x) {
        V x2 = x.Times(x);
        V x3 = x2.Times(x);
        V x4 = x3.Times(x);
        return Mu.Times(C786.Times(x).Minus(C2323.Times(x2)).Plus(C2875.Times(x3)).Minus(C1330.Times(x4)));
    }

    public static SingerMap<Flt64> Create(Flt64? mu = null)
        => new(mu ?? Flt64.One, new Flt64(7.86), new Flt64(23.23), new Flt64(28.75), new Flt64(13.30));
}

public sealed class SingerMapGenerator : IGenerator<Flt64> {
    public SingerMap<Flt64> Map { get; }
    public Flt64 X { get; private set; }

    public SingerMapGenerator(SingerMap<Flt64>? map = null, Flt64? x = null) {
        Map = map ?? SingerMap<Flt64>.Create();
        X = x ?? new Flt64(0.5);
    }

    public Flt64 Invoke() {
        Flt64 cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
