#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Bandwidth;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Route;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Infrastructure;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Pipelines.Bandwidth;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Pipelines.Route;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Application;

/// <summary>
/// 最短服务路径（SSP）演示：将路由分配和带宽分配建模为网络图上的线性优化问题。
/// Shortest Service Path (SSP) demo: models route assignment and bandwidth allocation
/// as a linear optimization problem on a network graph.
///
/// Port of Kotlin framework_demo/demo1 SSP solver.
/// </summary>
public sealed class SspDemo {
    private const string Data = """
        28 45 12
        100
        0 16 8 2
        0 26 13 2
        0 9 14 2
        0 8 36 2
        0 7 25 2
        0 6 13 2
        0 1 20 1
        0 2 16 1
        0 3 13 1
        1 19 26 2
        1 18 31 2
        1 16 24 2
        1 15 16 2
        1 2 4 1
        1 3 11 1
        2 4 37 2
        2 25 24 2
        2 21 5 2
        2 20 2 2
        2 3 7 1
        3 19 24 2
        3 24 17 2
        3 27 26 2
        4 5 26 1
        4 6 12 1
        5 6 14 1
        8 21 36 5
        9 10 6 1
        9 11 14 1
        10 26 11 5
        10 11 9 1
        12 13 15 1
        12 14 9 1
        12 15 12 1
        13 14 11 1
        13 15 27 1
        14 15 19 1
        17 18 22 1
        21 22 22 1
        21 23 18 1
        21 24 14 1
        22 23 23 1
        22 24 11 1
        23 24 23 1
        26 27 19 1
        0 8 40
        1 11 13
        2 22 28
        3 3 45
        4 17 11
        5 19 26
        6 16 15
        7 13 13
        8 5 18
        9 25 15
        10 7 10
        11 24 23
        """;

    private readonly LinearMetaModel<Flt64> _metaModel = new("demo1-ssp", ObjectCategory.Minimum);

    private SspGraph _graph = null!;
    private IReadOnlyList<SspService> _services = null!;
    private RouteAggregation _routeAggregation = null!;
    private BandwidthAggregation _bandwidthAggregation = null!;

    /// <summary>元模型 / Meta model</summary>
    public LinearMetaModel<Flt64> MetaModel => _metaModel;
    /// <summary>网络图 / Network graph</summary>
    public SspGraph Graph => _graph;
    /// <summary>服务列表 / Service list</summary>
    public IReadOnlyList<SspService> Services => _services;

    /// <summary>
    /// 解析输入数据并构建 SSP 优化模型。Parse input data and build the SSP optimization model.
    /// </summary>
    /// <returns>执行结果 / Execution result</returns>
    public Result<SspInput, ErrorCode, Error<ErrorCode>> ParseInput() {
        return Parse(Data);
    }

    /// <summary>
    /// 构建 SSP 优化模型（不求解，用于测试验证）。Build the SSP optimization model (without solving, for test verification).
    /// </summary>
    /// <returns>执行结果 / Execution result</returns>
    public Try BuildModel() {
        Result<SspInput, ErrorCode, Error<ErrorCode>> parseResult = ParseInput();
        if (parseResult is not Ok<SspInput, ErrorCode, Error<ErrorCode>> parseOk) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationFailed, "Failed to parse input data");
        }
        return BuildModel(parseOk.Value);
    }

    /// <summary>
    /// 使用给定输入构建 SSP 优化模型。Build the SSP optimization model with the given input.
    /// </summary>
    /// <param name="input">输入数据 / Input data</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try BuildModel(SspInput input) {
        // 1. Build graph and services
        Try r1 = InitDomain(input);
        if (r1.IsFailed) {
            return r1;
        }

        // 2. Register route context
        Try r2 = _routeAggregation.Register(_metaModel);
        if (r2.IsFailed) {
            return r2;
        }

        // 3. Register bandwidth context
        Try r3 = _bandwidthAggregation.Register(_metaModel);
        if (r3.IsFailed) {
            return r3;
        }

        // 4. Add route constraints and objectives
        Try r4 = AddRoutePipelines();
        if (r4.IsFailed) {
            return r4;
        }

        // 5. Add bandwidth constraints and objectives
        Try r5 = AddBandwidthPipelines();
        if (r5.IsFailed) {
            return r5;
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private Try InitDomain(SspInput input) {
        UInt64 totalDemand = UInt64.Zero;
        foreach (ClientNodeDTO cn in input.ClientNodes) {
            totalDemand += cn.Demand;
        }

        // Create services
        int serviceCount = (int)(input.NormalNodeAmount.ToULong()) / 2;
        var services = new List<SspService>(serviceCount);
        for (int i = 0; i < serviceCount; i++) {
            services.Add(new SspService(new UInt64((ulong)i), i, totalDemand, input.ServiceCost));
        }
        _services = services;

        // Create nodes
        int normalNodeCount = (int)(input.NormalNodeAmount.ToULong());
        var nodes = new List<Node>(normalNodeCount + input.ClientNodes.Count);
        for (int i = 0; i < normalNodeCount; i++) {
            nodes.Add(new NormalNode(new UInt64((ulong)i), i));
        }
        for (int i = 0; i < input.ClientNodes.Count; i++) {
            ClientNodeDTO cn = input.ClientNodes[i];
            nodes.Add(new ClientNode(cn.Id, normalNodeCount + i, cn.Demand));
        }

        // Create edges (bidirectional for each input edge)
        var edges = new List<Edge>(input.Edges.Count * 2 + input.ClientNodes.Count);
        for (int i = 0; i < input.Edges.Count; i++) {
            EdgeDTO edgeDto = input.Edges[i];
            Node fromNode = nodes[(int)(edgeDto.FromNodeId.ToULong())];
            Node toNode = nodes[(int)(edgeDto.ToNodeId.ToULong())];
            edges.Add(new Edge(fromNode, toNode, edgeDto.MaxBandwidth, edgeDto.CostPerBandwidth, edges.Count));
            edges.Add(new Edge(toNode, fromNode, edgeDto.MaxBandwidth, edgeDto.CostPerBandwidth, edges.Count));
            fromNode.Add(edges[edges.Count - 2]);
            toNode.Add(edges[edges.Count - 1]);
        }

        // Create edges from normal nodes to client nodes
        for (int i = 0; i < input.ClientNodes.Count; i++) {
            ClientNodeDTO cn = input.ClientNodes[i];
            Node normalNode = nodes[(int)(cn.NormalNodeId.ToULong())];
            Node clientNode = nodes[normalNodeCount + i];
            edges.Add(new Edge(normalNode, clientNode, cn.Demand, UInt64.Zero, edges.Count));
            normalNode.Add(edges[edges.Count - 1]);
        }

        _graph = new SspGraph(nodes, edges);

        // Create route aggregation
        var assignment = new Assignment(_graph.Nodes, _services);
        _routeAggregation = new RouteAggregation(_graph, _services, assignment);

        // Create bandwidth aggregation
        var edgeBandwidth = new EdgeBandwidth(_graph.Edges, _services);
        var serviceBandwidth = new ServiceBandwidth(_graph, _services, edgeBandwidth);
        var nodeBandwidth = new NodeBandwidth(_graph.Nodes, _services, serviceBandwidth);
        _bandwidthAggregation = new BandwidthAggregation(edgeBandwidth, serviceBandwidth, nodeBandwidth);

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private Try AddRoutePipelines() {
        var pipelines = new List<IPipeline<LinearMetaModel<Flt64>>> {
            new NodeAssignmentConstraintPipeline(_graph.Nodes, _routeAggregation.Assignment),
            new ServiceAssignmentConstraintPipeline(_services, _routeAggregation.Assignment),
            new ServiceCostObjectivePipeline(_services, _routeAggregation.Assignment)
        };
        return PipelineListInvoker.Invoke(pipelines, _metaModel);
    }

    private Try AddBandwidthPipelines() {
        var pipelines = new List<IPipeline<LinearMetaModel<Flt64>>> {
            new EdgeBandwidthConstraintPipeline(
                _graph.Edges, _services,
                _routeAggregation.Assignment, _bandwidthAggregation.EdgeBandwidth),
            new DemandConstraintPipeline(_graph.Nodes, _bandwidthAggregation.NodeBandwidth),
            new ServiceCapacityConstraintPipeline(
                _graph.Nodes, _services,
                _routeAggregation.Assignment, _bandwidthAggregation.ServiceBandwidth),
            new BandwidthCostObjectivePipeline(_graph.Edges, _bandwidthAggregation.EdgeBandwidth)
        };
        return PipelineListInvoker.Invoke(pipelines, _metaModel);
    }

    internal static Result<SspInput, ErrorCode, Error<ErrorCode>> Parse(string data) {
        try {
            string[] lines = data.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string firstLine = lines[0].Trim();
            string[] firstParts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            ulong normalNodeAmount = ulong.Parse(firstParts[0]);
            ulong edgeAmount = ulong.Parse(firstParts[1]);
            ulong clientNodeAmount = ulong.Parse(firstParts[2]);
            ulong serviceCost = ulong.Parse(lines[1].Trim());

            var edges = new List<EdgeDTO>((int)edgeAmount);
            for (int i = 2; i < (int)edgeAmount + 2; i++) {
                string[] parts = lines[i].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                edges.Add(new EdgeDTO(
                    new UInt64(ulong.Parse(parts[0])),
                    new UInt64(ulong.Parse(parts[1])),
                    new UInt64(ulong.Parse(parts[2])),
                    new UInt64(ulong.Parse(parts[3]))));
            }

            var clientNodes = new List<ClientNodeDTO>((int)clientNodeAmount);
            for (int i = (int)edgeAmount + 2; i < (int)edgeAmount + (int)clientNodeAmount + 2; i++) {
                string[] parts = lines[i].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                clientNodes.Add(new ClientNodeDTO(
                    new UInt64(ulong.Parse(parts[0])),
                    new UInt64(ulong.Parse(parts[1])),
                    new UInt64(ulong.Parse(parts[2]))));
            }

            return new Ok<SspInput, ErrorCode, Error<ErrorCode>>(
                new SspInput(new UInt64(serviceCost), new UInt64(normalNodeAmount), edges, clientNodes));
        }
        catch (Exception ex) {
            return new Failed<SspInput, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationFailed, $"Failed to parse input: {ex.Message}");
        }
    }
}
