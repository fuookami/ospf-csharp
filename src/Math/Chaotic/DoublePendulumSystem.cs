#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Functional;
using Fuookami.Ospf.Math.Geometry;
using System;

namespace Fuookami.Ospf.Math.Chaotic;
/// <summary>
/// 双摆系统 / Double Pendulum System.
/// 非线性动力学系统，Euler 单步迭代。/ Nonlinear dynamical system, one Euler step.
/// </summary>
public sealed record DoublePendulumSystem(Flt64 M, Flt64 L, Flt64 G, Flt64 H)
    : IExtractor<(Point<Dim2, Flt64>, Point<Dim2, Flt64>), (Point<Dim2, Flt64>, Point<Dim2, Flt64>)> {
    public (Point<Dim2, Flt64>, Point<Dim2, Flt64>) Invoke((Point<Dim2, Flt64>, Point<Dim2, Flt64>) input) {
        (Point<Dim2, Flt64> x, Point<Dim2, Flt64> y) = input;
        double theta1 = x[0].Value;
        double omega1 = x[1].Value;
        double theta2 = y[0].Value;
        double omega2 = y[1].Value;
        double m = M.Value;
        double l = L.Value;
        double g = G.Value;
        double h = H.Value;

        double sinTemp = global::System.Math.Sin(theta1 - theta2);
        double cosTemp = global::System.Math.Cos(theta1 - theta2);
        double dThetaDenominator = (m * l * l) * (16.0 - 9.0 * cosTemp * cosTemp);
        double dTheta1 = 6.0 * (2.0 * omega1 - 3.0 * cosTemp * omega2) / dThetaDenominator;
        double dTheta2 = 6.0 * (8.0 * omega2 - 3.0 * cosTemp * omega1) / dThetaDenominator;
        double dOmegaCoefficient = -m * l * l / 2.0;
        double dOmega1 = dOmegaCoefficient * (dTheta1 * dTheta2 * sinTemp + 3.0 * g / l * global::System.Math.Sin(theta1));
        double dOmega2 = dOmegaCoefficient * (-dTheta1 * dTheta2 * sinTemp + g / l * global::System.Math.Sin(theta2));

        var newX = new Point<Dim2, Flt64>(new Flt64[] {
            new Flt64(theta1 + h * dTheta1),
            new Flt64(omega1 + h * dOmega1)
        }, Dim2.Instance);
        var newY = new Point<Dim2, Flt64>(new Flt64[] {
            new Flt64(omega2 + h * dTheta2),
            new Flt64(omega2 + h * dOmega2)
        }, Dim2.Instance);
        return (newX, newY);
    }

    public static DoublePendulumSystem Create(
        Flt64? m = null, Flt64? l = null, Flt64? g = null, Flt64? h = null)
        => new(m ?? new Flt64(10.0), l ?? new Flt64(1.0), g ?? new Flt64(9.80665), h ?? new Flt64(0.01));
}

/// <summary>
/// 双摆系统生成器 / Double Pendulum System Generator.
/// </summary>
public sealed class DoublePendulumSystemGenerator : IGenerator<(Point<Dim2, Flt64>, Point<Dim2, Flt64>)> {
    public DoublePendulumSystem System { get; }
    public (Point<Dim2, Flt64>, Point<Dim2, Flt64>) State { get; private set; }

    public DoublePendulumSystemGenerator(DoublePendulumSystem? system = null, (Point<Dim2, Flt64>, Point<Dim2, Flt64>)? state = null) {
        System = system ?? DoublePendulumSystem.Create();
        State = state ?? (
            PointFactory.point2(new Flt64(0.1), new Flt64(0.1)),
            PointFactory.point2(new Flt64(0.1), new Flt64(0.1)));
    }

    public (Point<Dim2, Flt64>, Point<Dim2, Flt64>) Invoke() {
        (Point<Dim2, Flt64>, Point<Dim2, Flt64>) cur = State;
        State = System.Invoke(State);
        return cur;
    }
}
