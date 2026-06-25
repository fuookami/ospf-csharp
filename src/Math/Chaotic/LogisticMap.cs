#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 逻辑斯蒂映射 / Logistic Map. x_{n+1} = a*x*(1-x).
/// </summary>
public sealed record LogisticMap<V>(V A) : IExtractor<V, V>
    where V : struct, IFloatingNumber<V> {
    public V Invoke(V x) => A.Times(x).Times(x.Constants.One.Minus(x));

    public static LogisticMap<Flt64> Create(Flt64? a = null)
        => new(a ?? new Flt64(3.9));
}

public sealed class LogisticMapGenerator : IGenerator<Flt64> {
    public LogisticMap<Flt64> Map { get; }
    public Flt64 X { get; private set; }

    public LogisticMapGenerator(LogisticMap<Flt64>? map = null, Flt64? x = null) {
        Map = map ?? LogisticMap<Flt64>.Create();
        X = x ?? new Flt64(0.5);
    }

    public Flt64 Invoke() {
        Flt64 cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
