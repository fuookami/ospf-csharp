#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.CoreDemo.Demo11;
/// <summary>
/// 边定义：记录有向边的起点、终点和容量。
/// Edge definition: records source, target and capacity of a directed edge.
/// </summary>
public sealed class Edge {
    public int From { get; }
    public int To { get; }
    public Flt64 Capacity { get; }

    public Edge(int from, int to, Flt64 capacity) {
        From = from;
        To = to;
        Capacity = capacity;
    }
}

/// <summary>
/// 最大流演示：在网络流图中求解从源到汇的最大流量。
/// Maximum flow demo: compute maximum flow from source to sink in a network flow graph.
/// </summary>
public sealed class MaximumFlowDemo {
    private static readonly int NumNodes = 9;
    private static readonly int SourceNode = 0;
    private static readonly int SinkNode = 8;

    /// <summary>有向边列表（含容量）/ Directed edges with capacities.</summary>
    private static readonly List<Edge> Edges = new()
    {
        new Edge(0, 1, new Flt64(10)),
        new Edge(0, 2, new Flt64(15)),
        new Edge(0, 3, new Flt64(12)),
        new Edge(1, 4, new Flt64(8)),
        new Edge(1, 5, new Flt64(6)),
        new Edge(2, 4, new Flt64(5)),
        new Edge(2, 5, new Flt64(7)),
        new Edge(2, 6, new Flt64(10)),
        new Edge(3, 5, new Flt64(9)),
        new Edge(3, 6, new Flt64(14)),
        new Edge(4, 7, new Flt64(11)),
        new Edge(5, 7, new Flt64(13)),
        new Edge(5, 8, new Flt64(9)),
        new Edge(6, 7, new Flt64(8)),
        new Edge(6, 8, new Flt64(16)),
        new Edge(7, 8, new Flt64(20))
    };

    /// <summary>邻接表：每个节点的出边索引列表 / Adjacency list: outgoing edge indices per node.</summary>
    private static readonly List<List<int>> OutEdges;
    /// <summary>邻接表：每个节点的入边索引列表 / Adjacency list: incoming edge indices per node.</summary>
    private static readonly List<List<int>> InEdges;

    static MaximumFlowDemo() {
        OutEdges = new List<List<int>>();
        InEdges = new List<List<int>>();
        for (int n = 0; n < NumNodes; n++) {
            OutEdges.Add(new List<int>());
            InEdges.Add(new List<int>());
        }
        for (int e = 0; e < Edges.Count; e++) {
            OutEdges[Edges[e].From].Add(e);
            InEdges[Edges[e].To].Add(e);
        }
    }

    private readonly List<UIntVar> _edgeFlow = new();
    private UIntVar? _totalFlow;
    private LinearExpressionSymbol? _flowSymbol;
    private readonly LinearMetaModel<Flt64> _metaModel = new("demo11-maximum-flow", ObjectCategory.Maximum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建最大流模型（不求解，用于测试验证）。
    /// Build the maximum flow model (without solving, for test verification).
    /// </summary>
    public Result<Success, ErrorCode, Error<ErrorCode>> BuildModel() {
        Result<Success, ErrorCode, Error<ErrorCode>> r1 = InitVariables();
        if (r1.IsFailed) {
            return r1;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> r2 = InitSymbols();
        if (r2.IsFailed) {
            return r2;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> r3 = InitObjective();
        if (r3.IsFailed) {
            return r3;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> r4 = InitConstraints();
        if (r4.IsFailed) {
            return r4;
        }

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitVariables() {
        // Edge flow variables
        for (int e = 0; e < Edges.Count; e++) {
            var x = new UIntVar($"x_{Edges[e].From}_{Edges[e].To}");
            _edgeFlow.Add(x);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
            if (result.IsFailed) {
                return result;
            }
        }

        // Total flow variable
        _totalFlow = new UIntVar("flow");
        Result<Success, ErrorCode, Error<ErrorCode>> r = _metaModel.Add(_totalFlow);
        if (r.IsFailed) {
            return r;
        }

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // flow symbol = totalFlow variable
        var flowPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        flowPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _totalFlow!));
        _flowSymbol = new LinearExpressionSymbol(flowPoly, name: "flow");
        _metaModel.Add(_flowSymbol);

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // maximize total flow
        return _metaModel.AddObject(
            ObjectCategory.Maximum,
            _flowSymbol!.Polynomial,
            "flow",
            "Maximum Flow");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // Edge capacity constraints: x[e] <= capacity[e]
        for (int e = 0; e < Edges.Count; e++) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _edgeFlow[e]));
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(Edges[e].Capacity);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"capacity_{Edges[e].From}_{Edges[e].To}");
            if (result.IsFailed) {
                return result;
            }
        }

        // Flow conservation for each node
        for (int n = 0; n < NumNodes; n++) {
            if (n == SourceNode) {
                // Source: out - in = flow
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                foreach (int e in OutEdges[n]) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _edgeFlow[e]));
                }

                foreach (int e in InEdges[n]) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One.Negate(), _edgeFlow[e]));
                }

                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One.Negate(), _totalFlow!));
                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Eq(Flt64.Zero);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"flow_source");
                if (result.IsFailed) {
                    return result;
                }
            }
            else if (n == SinkNode) {
                // Sink: in - out = flow
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                foreach (int e in InEdges[n]) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _edgeFlow[e]));
                }

                foreach (int e in OutEdges[n]) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One.Negate(), _edgeFlow[e]));
                }

                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One.Negate(), _totalFlow!));
                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Eq(Flt64.Zero);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"flow_sink");
                if (result.IsFailed) {
                    return result;
                }
            }
            else {
                // Intermediate: in = out
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                foreach (int e in InEdges[n]) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _edgeFlow[e]));
                }

                foreach (int e in OutEdges[n]) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One.Negate(), _edgeFlow[e]));
                }

                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Eq(Flt64.Zero);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"flow_node_{n}");
                if (result.IsFailed) {
                    return result;
                }
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
