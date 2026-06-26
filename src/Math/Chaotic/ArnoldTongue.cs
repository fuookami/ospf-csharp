#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// Arnold Tongue 映射 / Arnold Tongue Map.
/// 一维标量映射。/ 1D scalar map. x_{n+1} = x + omega - kappa/(2*pi) * sin(2*pi*x).
/// Flt64-only (uses trig functions).
/// </summary>
public sealed record ArnoldTongue(Flt64 Omega, Flt64 Kappa) : IExtractor<Flt64, Flt64> {
    public Flt64 Invoke(Flt64 x) {
        double pi2 = global::System.Math.PI * 2.0;
        double sinVal = global::System.Math.Sin(pi2 * x.Value);
        return new Flt64(x.Value + Omega.Value - Kappa.Value / pi2 * sinVal);
    }

    public static ArnoldTongue Create(Flt64? omega = null, Flt64? kappa = null)
        => new(omega ?? new Flt64(0.5), kappa ?? new Flt64(1.0));
}

/// <summary>
/// Arnold Tongue 生成器 / Arnold Tongue Generator.
/// </summary>
public sealed class ArnoldTongueGenerator : IGenerator<Flt64> {
    public ArnoldTongue Map { get; }
    public Flt64 X { get; private set; }

    public ArnoldTongueGenerator(ArnoldTongue? map = null, Flt64? x = null) {
        Map = map ?? ArnoldTongue.Create();
        X = x ?? new Flt64(0.1);
    }

    public Flt64 Invoke() {
        Flt64 cur = X;
        X = Map.Invoke(X);
        return cur;
    }
}
