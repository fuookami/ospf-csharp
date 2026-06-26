#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 区间交换变换 / Interval Exchange Transformation.
/// </summary>
public sealed record IntervalExchangeTransformation<V>(IReadOnlyList<V> Lambda, IReadOnlyList<int> Pi) : IExtractor<V, V>
    where V : struct, IFloatingNumber<V> {
    public IntervalExchangeTransformation() : this(Array.Empty<V>(), Array.Empty<int>()) { }

    public V Invoke(V x) {
        int n = Lambda.Count;
        V sum = x.Constants.Zero;
        int intervalIndex = 0;
        for (int i = 0; i < n; i++) {
            if (x.Ls(sum.Plus(Lambda[i]))) {
                intervalIndex = i; break;
            }
            sum = sum.Plus(Lambda[i]);
            if (i == n - 1) intervalIndex = i;
        }
        V relativePos = x.Minus(sum).Div(Lambda[intervalIndex]);
        int targetInterval = Pi[intervalIndex];
        V targetStart = x.Constants.Zero;
        for (int i = 0; i < targetInterval; i++) {
            targetStart = targetStart.Plus(Lambda[i]);
        }
        return targetStart.Plus(relativePos.Times(Lambda[targetInterval]));
    }

    public static IntervalExchangeTransformation<Flt64> Create(Flt64[]? lambda = null, int[]? pi = null)
        => new(lambda ?? new Flt64[] { new Flt64(0.3), new Flt64(0.7) },
               pi ?? new int[] { 1, 0 });
}

public sealed class IntervalExchangeTransformationGenerator : IGenerator<Flt64> {
    public IntervalExchangeTransformation<Flt64> Map { get; }
    public Flt64 X { get; private set; }

    public IntervalExchangeTransformationGenerator(IntervalExchangeTransformation<Flt64>? map = null, Flt64? x = null) {
        Map = map ?? IntervalExchangeTransformation<Flt64>.Create();
        X = x ?? new Flt64(0.5);
    }

    public Flt64 Invoke() {
        Flt64 cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
