#nullable enable

using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Heuristic.Saa;
/// <summary>
/// 模拟退火策略实现 / Simulated Annealing policy implementation.
/// </summary>
public class SaaPolicy : HeuristicPolicy, ISaaPolicy {
    private readonly double _initialTemperature;
    private readonly double _finalTemperature;
    private readonly double _temperatureGradient;
    private readonly Random _random;

    public override string Name => "SAA";
    public int MarkovLength { get; }

    public SaaPolicy(
        double initialTemperature = 100.0,
        double finalTemperature = 1.0,
        double temperatureGradient = 0.98,
        int markovLength = 100,
        int maxIterations = 1000,
        int? seed = null) {
        _initialTemperature = initialTemperature;
        _finalTemperature = finalTemperature;
        _temperatureGradient = temperatureGradient;
        MarkovLength = markovLength;
        MaxIterations = maxIterations;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <inheritdoc/>
    public Flt64 Temperature(Iteration iteration) => new Flt64(_initialTemperature * System.Math.Pow(_temperatureGradient, iteration.Current));

    /// <inheritdoc/>
    public bool Accept(Iteration iteration, Flt64 currentObjective, Flt64 newObjective) {
        Flt64 delta = (newObjective - currentObjective).Abs();
        Flt64 temp = Temperature(iteration);
        if (temp.ToDouble() <= 0) {
            return false;
        }

        double acceptProb = System.Math.Exp(-delta.ToDouble() / temp.ToDouble());
        return _random.NextDouble() < acceptProb;
    }

    /// <inheritdoc/>
    public override bool Finished(Iteration iteration) => base.Finished(iteration) || Temperature(iteration).ToDouble() <= _finalTemperature;
}
