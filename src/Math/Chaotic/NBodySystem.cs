#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// N 体系统 / N-Body System.
/// 多体引力相互作用模型。/ Multi-body gravitational interaction model.
/// </summary>
public sealed record NBodySystem(IReadOnlyList<Flt64> M, Flt64 G, Flt64 H) {
    public IReadOnlyList<(Point<Dim3, Flt64> Pos, Point<Dim3, Flt64> Vel)> Invoke(
        IReadOnlyList<(Point<Dim3, Flt64> Pos, Point<Dim3, Flt64> Vel)> state) {
        int n = state.Count;
        var result = new (Point<Dim3, Flt64> Pos, Point<Dim3, Flt64> Vel)[n];
        for (int i = 0; i < n; i++) {
            (Point<Dim3, Flt64>? posI, Point<Dim3, Flt64>? velI) = state[i];
            double ax = 0, ay = 0, az = 0;
            for (int j = 0; j < n; j++) {
                if (i != j) {
                    (Point<Dim3, Flt64>? posJ, Point<Dim3, Flt64> _) = state[j];
                    double dx = posJ[0].Value - posI[0].Value;
                    double dy = posJ[1].Value - posI[1].Value;
                    double dz = posJ[2].Value - posI[2].Value;
                    double distSq = dx * dx + dy * dy + dz * dz;
                    double dist = global::System.Math.Sqrt(distSq);
                    double distCubed = distSq * dist;
                    if (distCubed > 0) {
                        double force = G.Value * M[j].Value / distCubed;
                        ax += force * dx;
                        ay += force * dy;
                        az += force * dz;
                    }
                }
            }
            Point<Dim3, Flt64> newPos = PointFactory.point3(
                posI[0] + H * velI[0],
                posI[1] + H * velI[1],
                posI[2] + H * velI[2]);
            Point<Dim3, Flt64> newVel = PointFactory.point3(
                velI[0] + H * new Flt64(ax),
                velI[1] + H * new Flt64(ay),
                velI[2] + H * new Flt64(az));
            result[i] = (newPos, newVel);
        }
        return result;
    }

    public static NBodySystem Create(Flt64[]? m = null, Flt64? g = null, Flt64? h = null)
        => new(m ?? new Flt64[] { Flt64.One, Flt64.One, Flt64.One },
               g ?? new Flt64(6.67e-11), h ?? new Flt64(0.001));
}

/// <summary>
/// 二维 N 体系统 / 2D N-Body System.
/// </summary>
public sealed record NBodySystemPlane(IReadOnlyList<Flt64> M, Flt64 G, Flt64 H) {
    public IReadOnlyList<(Point<Dim2, Flt64> Pos, Point<Dim2, Flt64> Vel)> Invoke(
        IReadOnlyList<(Point<Dim2, Flt64> Pos, Point<Dim2, Flt64> Vel)> state) {
        int n = state.Count;
        var result = new (Point<Dim2, Flt64> Pos, Point<Dim2, Flt64> Vel)[n];
        for (int i = 0; i < n; i++) {
            (Point<Dim2, Flt64>? posI, Point<Dim2, Flt64>? velI) = state[i];
            double ax = 0, ay = 0;
            for (int j = 0; j < n; j++) {
                if (i != j) {
                    (Point<Dim2, Flt64>? posJ, Point<Dim2, Flt64> _) = state[j];
                    double dx = posJ[0].Value - posI[0].Value;
                    double dy = posJ[1].Value - posI[1].Value;
                    double distSq = dx * dx + dy * dy;
                    double dist = global::System.Math.Sqrt(distSq);
                    double distCubed = distSq * dist;
                    if (distCubed > 0) {
                        double force = G.Value * M[j].Value / distCubed;
                        ax += force * dx;
                        ay += force * dy;
                    }
                }
            }
            Point<Dim2, Flt64> newPos = PointFactory.point2(posI[0] + H * velI[0], posI[1] + H * velI[1]);
            Point<Dim2, Flt64> newVel = PointFactory.point2(velI[0] + H * new Flt64(ax), velI[1] + H * new Flt64(ay));
            result[i] = (newPos, newVel);
        }
        return result;
    }

    public static NBodySystemPlane CreatePlane(Flt64[]? m = null, Flt64? g = null, Flt64? h = null)
        => new(m ?? new Flt64[] { Flt64.One, Flt64.One, Flt64.One },
               g ?? new Flt64(6.67e-11), h ?? new Flt64(0.001));
}

public sealed class NBodySystemGenerator {
    public NBodySystem System { get; }
    public IReadOnlyList<(Point<Dim3, Flt64> Pos, Point<Dim3, Flt64> Vel)> State { get; private set; }

    public NBodySystemGenerator(NBodySystem? system = null,
        IReadOnlyList<(Point<Dim3, Flt64> Pos, Point<Dim3, Flt64> Vel)>? state = null) {
        System = system ?? NBodySystem.Create();
        State = state ?? System.M.Select(_ => (
            PointFactory.point3(new Flt64(0.1), new Flt64(0.1), new Flt64(0.1)),
            PointFactory.point3(Flt64.Zero, Flt64.Zero, Flt64.Zero)
        )).ToArray();
    }

    public IReadOnlyList<(Point<Dim3, Flt64> Pos, Point<Dim3, Flt64> Vel)> Invoke() {
        IReadOnlyList<(Point<Dim3, Flt64> Pos, Point<Dim3, Flt64> Vel)> cur = State;
        State = System.Invoke(State);
        return cur;
    }
}
