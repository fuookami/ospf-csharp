#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Service;

/// <summary>
/// 批次生成配置。
/// Configuration for bunch generation.
/// </summary>
/// <param name="WithOrderChange">是否启用顺序变更 / Whether to enable order change.</param>
/// <param name="MaximumLabelPerNode">每节点最大标签数 / Maximum labels per node.</param>
/// <param name="MaximumColumnGeneratedPerAircraft">每飞机最大生成列数 / Maximum columns generated per aircraft.</param>
public sealed record BunchGenerationConfiguration(
    bool WithOrderChange = false,
    ulong MaximumLabelPerNode = 100UL,
    ulong MaximumColumnGeneratedPerAircraft = 10UL
);

/// <summary>
/// 使用标号设置算法生成航班任务束。
/// Generates flight task bunches using Label Setting algorithm.
/// </summary>
public sealed class FlightTaskBunchGenerator {
    private readonly Aircraft _aircraft;
    private readonly AircraftUsability _aircraftUsability;
    private readonly Graph _graph;
    private readonly ConnectionTimeCalculator _connectionTimeCalculator;
    private readonly MinimumDepartureTimeCalculator _minimumDepartureTimeCalculator;
    private readonly BunchCostCalculator _costCalculator;
    private readonly TotalCostCalculator _totalCostCalculator;
    private readonly BunchGenerationConfiguration _configuration;
    private readonly DateTimeOffset _enabledTime;
    private readonly IReadOnlyList<Node> _nodes;

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    public FlightTaskBunchGenerator(
        Aircraft aircraft,
        AircraftUsability aircraftUsability,
        Graph graph,
        ConnectionTimeCalculator connectionTimeCalculator,
        MinimumDepartureTimeCalculator minimumDepartureTimeCalculator,
        BunchCostCalculator costCalculator,
        TotalCostCalculator totalCostCalculator,
        BunchGenerationConfiguration configuration) {
        _aircraft = aircraft;
        _aircraftUsability = aircraftUsability;
        _graph = graph;
        _connectionTimeCalculator = connectionTimeCalculator;
        _minimumDepartureTimeCalculator = minimumDepartureTimeCalculator;
        _costCalculator = costCalculator;
        _totalCostCalculator = totalCostCalculator;
        _configuration = configuration;
        _enabledTime = aircraftUsability.EnabledTime;
        _nodes = configuration.WithOrderChange ? Array.Empty<Node>() : TopologicalSort(graph);
    }

    /// <summary>
    /// 为给定的迭代和影子价格映射生成束。
    /// Generates bunches for the given iteration and shadow price map.
    /// </summary>
    public IReadOnlyList<FlightTaskBunch> Invoke(long iteration, FlightShadowPriceMap shadowPriceMap) {
        var labels = new Dictionary<Node, List<Label<FlightTask, Aircraft, FlightTaskAssignment>>>();
        InitRootLabel(labels, shadowPriceMap);

        if (_configuration.WithOrderChange) {
            var deque = new List<Label<FlightTask, Aircraft, FlightTaskAssignment>>(labels[RootNode.Instance]);
            while (deque.Count > 0) {
                var prevLabel = deque[0];
                deque.RemoveAt(0);
                var prevNode = prevLabel.Node!;
                var edges = _graph.GetEdges(prevNode).OrderBy(e => e.To.Time).ToList();
                foreach (var edge in edges) {
                    var succNode = edge.To;
                    var succLabels = GetLabels(labels, succNode);
                    if (succNode is EndNode) {
                        if (prevNode is not RootNode) {
                            var shadowPrice = shadowPriceMap.Invoke(prevLabel.Task!, (FlightTask?)null);
                            succLabels.Add(new Label<FlightTask, Aircraft, FlightTaskAssignment>(
                                prevLabel.Cost.Copy(), prevLabel.ShadowPrice + shadowPrice, prevLabel, succNode, null));
                        }
                    } else if (!prevLabel.Visited(succNode)) {
                        var succLabel = GenerateFlightTaskLabel(prevLabel, succNode, shadowPriceMap);
                        if (succLabel is not null) {
                            succLabels.Add(succLabel);
                            deque.Add(succLabel);
                        }
                    }
                }
            }
        } else {
            foreach (var prevNode in _nodes) {
                foreach (var prevLabel in GetLabels(labels, prevNode)) {
                    foreach (var edge in _graph.GetEdges(prevNode)) {
                        var succNode = edge.To;
                        var succLabels = GetLabels(labels, succNode);
                        if (succNode is EndNode) {
                            if (prevNode is not RootNode) {
                                var shadowPrice = shadowPriceMap.Invoke(prevLabel.Task!, (FlightTask?)null);
                                succLabels.Add(new Label<FlightTask, Aircraft, FlightTaskAssignment>(
                                    prevLabel.Cost.Copy(), prevLabel.ShadowPrice + shadowPrice, prevLabel, succNode, null));
                            }
                        } else if (!prevLabel.Visited(succNode)) {
                            var succLabel = GenerateFlightTaskLabel(prevLabel, succNode, shadowPriceMap);
                            if (succLabel is not null) succLabels.Add(succLabel);
                        }
                    }
                }
            }
        }

        return SelectBunches(iteration, labels.TryGetValue(EndNode.Instance, out var endLabels) ? endLabels : new());
    }

    private void InitRootLabel(Dictionary<Node, List<Label<FlightTask, Aircraft, FlightTaskAssignment>>> labels, FlightShadowPriceMap shadowPriceMap) {
        var rootNode = RootNode.Instance;
        var cost = new ImmutableCost<Flt64>(Array.Empty<CostItem<Flt64>>());
        var shadowPrice = shadowPriceMap.Invoke(_aircraft);
        labels[rootNode] = new() { new Label<FlightTask, Aircraft, FlightTaskAssignment>(cost, shadowPrice, null, rootNode, null) };
    }

    private List<Label<FlightTask, Aircraft, FlightTaskAssignment>> GetLabels(
        Dictionary<Node, List<Label<FlightTask, Aircraft, FlightTaskAssignment>>> labels, Node node) {
        if (!labels.TryGetValue(node, out var list)) { list = new(); labels[node] = list; }
        return list;
    }

    private Label<FlightTask, Aircraft, FlightTaskAssignment>? GenerateFlightTaskLabel(
        Label<FlightTask, Aircraft, FlightTaskAssignment> prevLabel,
        Node succNode,
        FlightShadowPriceMap shadowPriceMap) {
        if (succNode is not TaskNode<FlightTask, Aircraft, FlightTaskAssignment> taskNode) return null;
        var succTask = taskNode.Task;
        var minDepTime = GetMinDepartureTime(prevLabel, succNode);
        var duration = succTask.DurationFor(_aircraft);
        var time = succTask.ScheduledTime is not null
            ? new TimeRange(minDepTime, minDepTime + duration)
            : (minDepTime + duration) > succTask.TimeWindow!.End ? null : new TimeRange(minDepTime, minDepTime + duration);
        if (time is null) return null;

        var prevArr = prevLabel.Node is RootNode ? succTask.Dep : prevLabel.Task!.Arr;
        var recoveryTask = GenerateRecoveryFlightTask(prevArr, succTask, time);
        if (recoveryTask is null) return null;

        var cost = prevLabel.Node is RootNode
            ? _costCalculator(_aircraft, _aircraftUsability.LastTask as FlightTask, recoveryTask, prevLabel.Task?.FlightHour ?? FlightHour.Zero, prevLabel.Task?.FlightCycle ?? FlightCycle.Zero)
            : _costCalculator(_aircraft, prevLabel.Task!, recoveryTask, prevLabel.Task!.FlightHour ?? FlightHour.Zero, prevLabel.Task.FlightCycle);
        if (cost is null) return null;

        var shadowPrice = prevLabel.Node is RootNode
            ? shadowPriceMap.Invoke(_aircraftUsability.LastTask as FlightTask, recoveryTask)
            : shadowPriceMap.Invoke(prevLabel.Task!, recoveryTask);

        var newCost = prevLabel.Cost.Copy();
        if (newCost is MutableCost<Flt64> mc) mc.Add(new CostItem<Flt64>("bunch", cost.CostSum));
        return new Label<FlightTask, Aircraft, FlightTaskAssignment>(newCost, prevLabel.ShadowPrice + shadowPrice, prevLabel, succNode, recoveryTask);
    }

    private FlightTask? GenerateRecoveryFlightTask(Airport dep, FlightTask succTask, TimeRange time) {
        Aircraft? aircraft = succTask.Aircraft is null || succTask.Aircraft != _aircraft ? _aircraft : null;
        var arr = succTask.ActualArr(dep);
        if (arr is null) return null;
        var policy = new FlightTaskAssignment(aircraft, time, new Route(dep, arr));
        return policy.Empty ? succTask : succTask.RecoveryEnabled(policy) ? succTask.Recovery(policy) : null;
    }

    private DateTimeOffset GetMinDepartureTime(Label<FlightTask, Aircraft, FlightTaskAssignment> prevLabel, Node succNode) {
        var thisFlightTask = ((TaskNode<FlightTask, Aircraft, FlightTaskAssignment>)succNode).Task;
        if (prevLabel.Node is RootNode && _aircraftUsability.LastTask is null) {
            return _minimumDepartureTimeCalculator(prevLabel.Task?.Time?.End ?? DateTimeOffset.MinValue, _aircraft, thisFlightTask, TimeSpan.Zero);
        }
        var prevFlightTask = prevLabel.Node is RootNode ? (FlightTask)_aircraftUsability.LastTask! : prevLabel.Task!;
        var connTime = _connectionTimeCalculator(_aircraft, prevFlightTask, thisFlightTask);
        return _minimumDepartureTimeCalculator(prevLabel.Task?.Time?.End ?? DateTimeOffset.MinValue, _aircraft, thisFlightTask, connTime);
    }

    private IReadOnlyList<FlightTaskBunch> SelectBunches(long iteration, List<Label<FlightTask, Aircraft, FlightTaskAssignment>> labels) {
        var bunches = new List<FlightTaskBunch>();
        var sorted = labels.Where(l => l.IsBetterBunch).OrderBy(l => l.ReducedCost).ToList();
        var usability = new ExecutorInitialUsability<FlightTask, Aircraft, FlightTaskAssignment>(_aircraftUsability.LastTask as FlightTask, _aircraftUsability.EnabledTime);
        foreach (var label in sorted) {
            var abstractBunch = label.GenerateBunch(iteration, _aircraft, usability,
                (e, last, tasks) => _totalCostCalculator(e, tasks));
            if (abstractBunch is not null) {
                var bunch = new FlightTaskBunch(abstractBunch.Executor, abstractBunch.Tasks, iteration, abstractBunch.Cost);
                bunches.Add(bunch);
            }
            if (bunches.Count >= (int)_configuration.MaximumColumnGeneratedPerAircraft) break;
        }
        return bunches;
    }

    private static IReadOnlyList<Node> TopologicalSort(Graph graph) {
        var inDegree = new Dictionary<Node, ulong>();
        foreach (var kv in graph.Nodes) inDegree[kv.Value] = 0;
        foreach (var kv in graph.Nodes) {
            foreach (var edge in graph.GetEdges(kv.Value)) {
                inDegree[edge.To] = inDegree.GetValueOrDefault(edge.To, 0UL) + 1;
            }
        }
        var nodes = new List<Node>();
        while (inDegree.Count > 0) {
            var zeroNodes = inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList();
            if (zeroNodes.Count > 0) {
                foreach (var n in zeroNodes) {
                    foreach (var edge in graph.GetEdges(n)) inDegree[edge.To]--;
                    nodes.Add(n);
                    inDegree.Remove(n);
                }
            } else {
                var min = inDegree.Values.Min();
                var minNodes = inDegree.Where(kv => kv.Value == min).Select(kv => kv.Key).OrderBy(n => n.Time).ToList();
                foreach (var n in minNodes) {
                    foreach (var edge in graph.GetEdges(n)) inDegree[edge.To]--;
                    nodes.Add(n);
                    inDegree.Remove(n);
                }
            }
        }
        return nodes;
    }
}

/// <summary>
/// Label 的 ArrivalTime 扩展。
/// Extension for Label ArrivalTime.
/// </summary>
internal static class LabelExtensions {
    /// <summary>
    /// 获取标签的到达时间。
    /// Gets the arrival time of the label.
    /// </summary>
    public static DateTimeOffset ArrivalTime<T, E, A>(this Label<T, E, A> label)
        where T : class, IAbstractTask<E, A>
        where E : Executor
        where A : IAssignmentPolicy<E>
        => label.Task?.Time?.End ?? DateTimeOffset.MinValue;
}
