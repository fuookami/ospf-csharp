#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
namespace Fuookami.Ospf.Core.Symbol.Function;

internal interface IUnivariatePiecewiseFunction<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    UnivariateLinearPiecewiseFunction<V> Piecewise { get; }
}

internal static class TrigonometricSupport {
    internal static Result<(UnivariateLinearPiecewiseFunction<V> Function, IReadOnlyList<Point<Dim2, Flt64>> Points), ErrorCode, Error<ErrorCode>> Create<V>(
        LinearPolynomial<V>? input,
        IIntoValue<V>? converter,
        IReadOnlyList<Point<Dim2, Flt64>>? samplingPoints,
        bool cosine,
        string name,
        string? displayName)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            if (converter is null) {
                return Invalid<V>();
            }
            samplingPoints ??= DefaultPoints(cosine);
            if (samplingPoints.Count < 2) {
                return Invalid<V>();
            }
            var points = new Point<Dim2, Flt64>[samplingPoints.Count];
            var breakpoints = new V[points.Length];
            var slopes = new V[points.Length - 1];
            var intercepts = new V[slopes.Length];
            for (int i = 0; i < points.Length; ++i) {
                Point<Dim2, Flt64> point = samplingPoints[i];
                if (point is null || point.Count != 2 || !double.IsFinite(point[0].ToDouble()) ||
                    !double.IsFinite(point[1].ToDouble()) || i > 0 && point[0].ToDouble() <= points[i - 1][0].ToDouble()) {
                    return Invalid<V>();
                }
                points[i] = Point<Dim2, Flt64>.Create(point[0], point[1]);
                breakpoints[i] = converter.IntoValue(point[0]);
                if (point[0].ToDouble() != 0 && breakpoints[i].Eq(converter.Zero)) {
                    return Invalid<V>();
                }
            }
            for (int i = 0; i < slopes.Length; ++i) {
                double x0 = points[i][0].ToDouble();
                double y0 = points[i][1].ToDouble();
                double dx = points[i + 1][0].ToDouble() - x0;
                double dy = points[i + 1][1].ToDouble() - y0;
                double slope = dy / dx;
                double intercept = y0 - slope * x0;
                if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(slope) ||
                    !double.IsFinite(intercept) || dy != 0 && slope == 0) {
                    return Invalid<V>();
                }
                slopes[i] = converter.IntoValue(new Flt64(slope));
                intercepts[i] = converter.IntoValue(new Flt64(intercept));
                if (slope != 0 && slopes[i].Eq(converter.Zero) || intercept != 0 && intercepts[i].Eq(converter.Zero)) {
                    return Invalid<V>();
                }
            }
            return UnivariateLinearPiecewiseFunction<V>.Create(
                input: input,
                breakpoints: breakpoints,
                slopes: slopes,
                intercepts: intercepts,
                converter: converter,
                name: name,
                displayName: displayName
            ).Map(function => (function, (IReadOnlyList<Point<Dim2, Flt64>>)Array.AsReadOnly(points)));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<V>();
        }
    }

    private static Point<Dim2, Flt64>[] DefaultPoints(bool cosine) {
        double pi = global::System.Math.PI;
        return new[] {
            Point<Dim2, Flt64>.Create(new Flt64(-pi), new Flt64(cosine ? -1 : 0)),
            Point<Dim2, Flt64>.Create(new Flt64(-pi / 2), new Flt64(cosine ? 0 : -1)),
            Point<Dim2, Flt64>.Create(Flt64.Zero, new Flt64(cosine ? 1 : 0)),
            Point<Dim2, Flt64>.Create(new Flt64(pi / 2), new Flt64(cosine ? 0 : 1)),
            Point<Dim2, Flt64>.Create(new Flt64(pi), new Flt64(cosine ? -1 : 0)),
        };
    }

    private static Result<(UnivariateLinearPiecewiseFunction<V> Function, IReadOnlyList<Point<Dim2, Flt64>> Points), ErrorCode, Error<ErrorCode>> Invalid<V>()
        where V : struct, IRealNumber<V>, INumberField<V> =>
        Results.Failed<(UnivariateLinearPiecewiseFunction<V>, IReadOnlyList<Point<Dim2, Flt64>>)>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "三角函数采样点或数值无效。 / Invalid trigonometric samples or numeric values."
        ));
}
