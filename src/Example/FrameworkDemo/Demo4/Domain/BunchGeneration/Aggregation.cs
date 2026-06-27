#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration;

/// <summary>
/// 批次生成域对象聚合。
/// Aggregation of bunch generation domain objects.
/// </summary>
/// <param name="Graphs">路线图映射 / Route graphs.</param>
/// <param name="Reverse">可反转任务对 / Reversible task pairs.</param>
/// <param name="InitialFlightBunches">初始航班束 / Initial flight bunches.</param>
public sealed class Aggregation(
    IReadOnlyDictionary<Aircraft, Graph> graphs,
    FlightTaskReverse reverse,
    IReadOnlyList<FlightTaskBunch> initialFlightBunches
) {
    /// <summary>路线图映射 / Route graphs.</summary>
    public IReadOnlyDictionary<Aircraft, Graph> Graphs { get; } = graphs;

    /// <summary>可反转任务对 / Reversible task pairs.</summary>
    public FlightTaskReverse Reverse { get; } = reverse;

    /// <summary>初始航班束 / Initial flight bunches.</summary>
    public IReadOnlyList<FlightTaskBunch> InitialFlightBunches { get; } = initialFlightBunches;
}

/// <summary>
/// 批次生成上下文（管理聚合和生成器）。
/// Context for bunch generation, managing aggregation and generators.
/// </summary>
public sealed class BunchGenerationContext {
    private Aggregation? _aggregation;
    private FlightTaskFeasibilityJudger? _feasibilityJudger;
    private IReadOnlyDictionary<Aircraft, FlightTaskBunchGenerator>? _generators;
    private TotalCostCalculator? _totalCostCalculator;

    /// <summary>获取初始航班束 / Gets the initial flight bunches.</summary>
    public IReadOnlyList<FlightTaskBunch> InitialFlightBunches => _aggregation?.InitialFlightBunches ?? Array.Empty<FlightTaskBunch>();

    /// <summary>
    /// 初始化批次生成上下文。
    /// Initializes the bunch generation context.
    /// </summary>
    public void Init(
        IReadOnlyList<Aircraft> aircrafts,
        IReadOnlyDictionary<Aircraft, AircraftUsability> aircraftUsability,
        IReadOnlyList<FlightTask> flightTasks,
        IReadOnlyList<FlightTaskBunch> originBunches,
        Lock lockObj,
        ConnectionTimeCalculator connectionTimeCalculator,
        MinimumDepartureTimeCalculator minimumDepartureTimeCalculator,
        RuleChecker ruleChecker,
        BunchCostCalculator costCalculator,
        TotalCostCalculator totalCostCalculator,
        bool withOrderChange = false) {
        _totalCostCalculator = totalCostCalculator;

        _feasibilityJudger = new FlightTaskFeasibilityJudger(aircraftUsability, connectionTimeCalculator, ruleChecker);

        var initialGenerator = new InitialFlightTaskBunchGenerator(_feasibilityJudger, connectionTimeCalculator, minimumDepartureTimeCalculator, totalCostCalculator);
        var initializer = new AggregationInitializer();
        var result = initializer.Invoke(aircrafts, aircraftUsability, flightTasks, originBunches, lockObj, _feasibilityJudger, initialGenerator, withOrderChange);
        if (result is Ok<Aggregation, ErrorCode, Error<ErrorCode>> ok) {
            _aggregation = ok.Value;
        } else {
            throw new InvalidOperationException("Failed to initialize bunch generation aggregation.");
        }

        var generators = new Dictionary<Aircraft, FlightTaskBunchGenerator>();
        foreach (var aircraft in aircrafts) {
            generators[aircraft] = new FlightTaskBunchGenerator(
                aircraft, aircraftUsability[aircraft], _aggregation.Graphs[aircraft],
                connectionTimeCalculator, minimumDepartureTimeCalculator, costCalculator, totalCostCalculator,
                new BunchGenerationConfiguration(withOrderChange));
        }
        _generators = generators;
    }

    /// <summary>
    /// 为给定的飞机和影子价格映射生成航班任务束。
    /// Generates flight task bunches for the given aircrafts and shadow price map.
    /// </summary>
    public IReadOnlyList<FlightTaskBunch> GenerateFlightTaskBunch(
        IReadOnlyList<Aircraft> aircrafts, long iteration, FlightShadowPriceMap shadowPriceMap) {
        var bunches = new List<FlightTaskBunch>();
        foreach (var aircraft in aircrafts) {
            if (_generators!.TryGetValue(aircraft, out var generator)) {
                bunches.AddRange(generator.Invoke(iteration, shadowPriceMap));
            }
        }
        return bunches;
    }
}
