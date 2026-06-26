#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Lorenz 96 模型 / Lorenz 96 Model.
/// N 维混沌系统，周期性边界条件。/ N-dimensional chaotic system with periodic boundary conditions.
/// </summary>
public sealed record Lorenz96Model(Flt64 A, Flt64 H) : IExtractor<IReadOnlyList<Flt64>, IReadOnlyList<Flt64>> {
    public IReadOnlyList<Flt64> Invoke(IReadOnlyList<Flt64> state) {
        int n = state.Count;
        var result = new Flt64[n];
        for (int i = 0; i < n; i++) {
            Flt64 xip1 = state[(i + 1) % n];
            Flt64 xi = state[i];
            Flt64 xim1 = state[(i - 1 + n) % n];
            Flt64 xim2 = state[(i - 2 + n) % n];
            Flt64 dx = (xip1 - xim2) * xim1 - xi + A;
            result[i] = xi + H * dx;
        }
        return result;
    }

    public static Lorenz96Model Create(Flt64? a = null, Flt64? h = null)
        => new(a ?? new Flt64(8.0), h ?? new Flt64(0.01));
}

public sealed class Lorenz96ModelGenerator : IGenerator<IReadOnlyList<Flt64>> {
    public Lorenz96Model Model { get; }
    public IReadOnlyList<Flt64> State { get; private set; }

    public Lorenz96ModelGenerator(Lorenz96Model? model = null, IReadOnlyList<Flt64>? state = null, int n = 40) {
        Model = model ?? Lorenz96Model.Create();
        State = state ?? Enumerable.Range(0, n).Select(_ => new Flt64(0.1)).ToArray();
    }

    public IReadOnlyList<Flt64> Invoke() {
        IReadOnlyList<Flt64> cur = State;
        State = Model.Invoke(State);
        return cur;
    }
}
