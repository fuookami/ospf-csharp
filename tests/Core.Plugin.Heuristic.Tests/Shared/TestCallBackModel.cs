#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Tests.Shared;
/// <summary>
/// deterministic test objective model for heuristic algorithm tests.
/// deterministic test objective model for heuristic algorithm tests.
/// Minimizes f(x) = sum(x_i^2) with known optimum at origin.
/// </summary>
public sealed class TestCallBackModel : ICallBackModelInterface<Flt64> {
    private readonly int _dimension;
    private readonly Random _random;
    private readonly double _initialRangeMin;
    private readonly double _initialRangeMax;

    public ObjectCategory ObjectCategory => ObjectCategory.Minimum;
    public IAbstractMutableTokenTable<Flt64> Tokens { get; }
    public IReadOnlyList<(Func<IReadOnlyList<Flt64>, bool?> Extractor, string Name)> Constraints { get; } =
        Array.Empty<(Func<IReadOnlyList<Flt64>, bool?>, string)>();
    public IReadOnlyList<(Func<IReadOnlyList<Flt64>, Flt64> Extractor, string Name)> ObjectiveFunctions { get; } =
        Array.Empty<(Func<IReadOnlyList<Flt64>, Flt64>, string)>();

    public Flt64 DefaultObjective => NegativeInfinity();

    /// <summary>
    /// construct test model.
    /// construct test model.
    /// </summary>
    /// <param name="dimension">decision variable dimension</param>
    /// <param name="seed">RNG seed for reproducibility</param>
    /// <param name="initialRangeMin">initial solution lower bound</param>
    /// <param name="initialRangeMax">initial solution upper bound</param>
    public TestCallBackModel(
        int dimension = 3,
        int seed = 42,
        double initialRangeMin = 2.0,
        double initialRangeMax = 8.0) {
        _dimension = dimension;
        _random = new Random(seed);
        _initialRangeMin = initialRangeMin;
        _initialRangeMax = initialRangeMax;
        Tokens = new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
    }

    /// <inheritdoc/>
    public IReadOnlyList<IReadOnlyList<Flt64>> InitialSolutions(ulong initialSolutionAmount = 1) {
        var solutions = new List<IReadOnlyList<Flt64>>();
        for (ulong s = 0; s < initialSolutionAmount; s++) {
            var solution = new List<Flt64>(_dimension);
            for (int d = 0; d < _dimension; d++) {
                double val = _initialRangeMin + _random.NextDouble() * (_initialRangeMax - _initialRangeMin);
                solution.Add(new Flt64(val));
            }
            solutions.Add(solution);
        }
        return solutions;
    }

    /// <inheritdoc/>
    public Flt64 Objective(IReadOnlyList<Flt64> solution) {
        // f(x) = sum(x_i^2)
        double sum = 0;
        foreach (Flt64 x in solution) {
            double v = x.ToDouble();
            sum += v * v;
        }
        return new Flt64(sum);
    }

    /// <inheritdoc/>
    public Order? CompareObjective(Flt64 lhs, Flt64 rhs) {
        double l = lhs.ToDouble();
        double r = rhs.ToDouble();
        if (l < r) {
            return new Order.Less();
        }

        if (l > r) {
            return new Order.Greater();
        }

        return new Order.Equal();
    }

    /// <inheritdoc/>
    public Flt64 Operation(Flt64 lhs, Flt64 rhs) => lhs.Plus(rhs);

    /// <inheritdoc/>
    public Flt64 ObjectiveValue() => Flt64.Zero;

    /// <inheritdoc/>
    public Flt64 ObjectiveValue(Flt64 obj) => obj;

    /// <inheritdoc/>
    public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => true;

    /// <inheritdoc/>
    public void Flush() { /* no-op for test */ }

    /// <inheritdoc/>
    public IFlt64ValueConverter<Flt64> Converter() => new IdentityFlt64Converter();

    /// <inheritdoc/>
    public Flt64 NegativeInfinity() => new Flt64(double.NegativeInfinity);

    /// <inheritdoc/>
    public Flt64 Infinity() => new Flt64(double.PositiveInfinity);

    /// <inheritdoc/>
    public void Dispose() { /* no-op for test */ }

    private sealed class IdentityFlt64Converter : IFlt64ValueConverter<Flt64> {
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => value;
    }
}
