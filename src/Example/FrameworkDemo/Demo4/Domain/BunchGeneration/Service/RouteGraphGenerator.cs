#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Model;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Service;

/// <summary>
/// 路线图生成配置。
/// Configuration for route graph generation.
/// </summary>
/// <param name="WithOrderChange">是否启用顺序变更 / Whether to enable order change.</param>
public sealed record RouteGraphConfiguration(bool WithOrderChange = false);

/// <summary>
/// 为批次生成生成路线图。
/// Generates route graphs for bunch generation.
/// </summary>
public sealed class RouteGraphGenerator {
    private readonly FlightTaskReverse _reverse;
    private readonly RouteGraphConfiguration _configuration;
    private readonly Func<Aircraft, FlightTask?, FlightTask, bool> _feasibilityJudger;

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    public RouteGraphGenerator(
        FlightTaskReverse reverse,
        RouteGraphConfiguration configuration,
        Func<Aircraft, FlightTask?, FlightTask, bool> feasibilityJudger) {
        _reverse = reverse;
        _configuration = configuration;
        _feasibilityJudger = feasibilityJudger;
    }

    /// <summary>
    /// 为给定飞机生成路线图。
    /// Generates a route graph for the given aircraft.
    /// </summary>
    public Graph Invoke(Aircraft aircraft, AircraftUsability aircraftUsability, IReadOnlyDictionary<Airport, IReadOnlyList<FlightTask>> flightTasks) {
        var graph = new Graph();
        graph.Put(RootNode.Instance);
        graph.Put(EndNode.Instance);
        var location = aircraftUsability.Location;
        var nodes = new List<(Airport, Node)> { (location, RootNode.Instance) };
        var nodeMap = new Dictionary<FlightTask, Node>();

        while (nodes.Count > 0) {
            var (airport, node) = nodes[0];
            nodes.RemoveAt(0);
            if (!flightTasks.ContainsKey(airport)) {
                graph.Put(node, EndNode.Instance);
                continue;
            }
            SearchAndInsertFlightTasks(graph, nodes, nodeMap, node, aircraft, flightTasks[airport]);
        }
        return graph;
    }

    private void SearchAndInsertFlightTasks(
        Graph graph, List<(Airport, Node)> nodes, Dictionary<FlightTask, Node> nodeMap,
        Node node, Aircraft aircraft, IReadOnlyList<FlightTask> flightTasks) {
        if (node is RootNode) {
            bool flag = false;
            foreach (var flightTask in flightTasks) {
                if (_feasibilityJudger(aircraft, null, flightTask)) {
                    flag = true;
                    InsertFlightTask(graph, nodes, nodeMap, node, flightTask);
                }
            }
            if (!flag) graph.Put(node, EndNode.Instance);
        } else if (_configuration.WithOrderChange) {
            var taskNode = (TaskNode<FlightTask, Aircraft, FlightTaskAssignment>)node;
            var prevFlightTask = taskNode.Task;
            foreach (var flightTask in flightTasks) {
                if (_feasibilityJudger(aircraft, prevFlightTask, flightTask)) {
                    InsertFlightTask(graph, nodes, nodeMap, node, flightTask);
                }
                if (_reverse.Contains(flightTask, prevFlightTask) && _feasibilityJudger(aircraft, flightTask, prevFlightTask)) {
                    InsertFlightTask(graph, nodes, nodeMap, node, flightTask);
                }
            }
            graph.Put(node, EndNode.Instance);
        } else {
            var taskNode = (TaskNode<FlightTask, Aircraft, FlightTaskAssignment>)node;
            var prevFlightTask = taskNode.Task;
            foreach (var flightTask in flightTasks) {
                if (_feasibilityJudger(aircraft, prevFlightTask, flightTask)) {
                    InsertFlightTask(graph, nodes, nodeMap, node, flightTask);
                }
            }
            graph.Put(node, EndNode.Instance);
        }
    }

    private static void InsertFlightTask(
        Graph graph, List<(Airport, Node)> nodes, Dictionary<FlightTask, Node> nodeMap,
        Node prevNode, FlightTask flightTask) {
        if (!nodeMap.ContainsKey(flightTask)) {
            var index = (ulong)graph.Nodes.Count;
            var depTime = flightTask.Time?.Start ?? flightTask.TimeWindow!.Start;
            var taskNode = new TaskNode<FlightTask, Aircraft, FlightTaskAssignment>(flightTask, depTime, index);
            graph.Put(taskNode);
            graph.Put(prevNode, taskNode);
            nodeMap[flightTask] = taskNode;
            nodes.Add((flightTask.Arr, taskNode));
            foreach (var dep in flightTask.DepBackup) {
                var arr = flightTask.ActualArr(dep);
                if (arr is not null) nodes.Add((arr, taskNode));
            }
        } else {
            graph.Put(prevNode, nodeMap[flightTask]);
        }
    }
}
