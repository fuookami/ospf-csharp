#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Route;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Bandwidth;

/// <summary>
/// 每服务每个节点入度、出度和流出的中间符号。Intermediate symbols for per-service in-degree, out-degree, and out-flow at each node.
/// </summary>
public sealed class ServiceBandwidth {
    private readonly SspGraph _graph;
    private readonly IReadOnlyList<SspService> _services;
    private readonly EdgeBandwidth _edgeBandwidth;

    /// <summary>每服务每节点入度中间符号 / Per-service per-node in-degree intermediate symbols</summary>
    public SymbolCombination<LinearExpressionSymbol, Shape2> InDegree { get; private set; } = null!;
    /// <summary>每服务每节点出度中间符号 / Per-service per-node out-degree intermediate symbols</summary>
    public SymbolCombination<LinearExpressionSymbol, Shape2> OutDegree { get; private set; } = null!;
    /// <summary>每服务每节点流出中间符号 / Per-service per-node out-flow intermediate symbols</summary>
    public SymbolCombination<LinearExpressionSymbol, Shape2> OutFlow { get; private set; } = null!;

    public ServiceBandwidth(SspGraph graph, IReadOnlyList<SspService> services, EdgeBandwidth edgeBandwidth) {
        _graph = graph;
        _services = services;
        _edgeBandwidth = edgeBandwidth;
    }

    /// <summary>
    /// 注册服务带宽中间符号到模型。Register service bandwidth intermediate symbols to the model.
    /// </summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        UIntVar2 y = _edgeBandwidth.Y;

        // inDegree[n,s] = sum of y[e,s] for edges e where e.To == n
        InDegree = new SymbolCombination<LinearExpressionSymbol, Shape2>(
            "bandwidth_indegree_service",
            Shape2.Invoke(_graph.Nodes.Count, _services.Count),
            (i, vec) => {
                Node node = _graph.Nodes[vec[0]];
                SspService service = _services[vec[1]];
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                foreach (Edge edge in _graph.Edges.Where(e => e.To == node)) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, y[edge.Index, service.Index]));
                }
                return new LinearExpressionSymbol(poly, name: $"bandwidth_indegree_service_{node}_{service}");
            });
        model.Add(InDegree);

        // outDegree[n,s] = sum of y[e,s] for edges e where e.From == n (normal nodes only)
        OutDegree = new SymbolCombination<LinearExpressionSymbol, Shape2>(
            "bandwidth_outdegree_service",
            Shape2.Invoke(_graph.Nodes.Count, _services.Count),
            (i, vec) => {
                Node node = _graph.Nodes[vec[0]];
                SspService service = _services[vec[1]];
                if (node is NormalNode) {
                    var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                    foreach (Edge edge in _graph.Edges.Where(e => e.From == node)) {
                        poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, y[edge.Index, service.Index]));
                    }
                    return new LinearExpressionSymbol(poly, name: $"bandwidth_outdegree_service_{node}_{service}");
                }
                return new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                    name: $"bandwidth_outdegree_service_{node}_{service}");
            });
        model.Add(OutDegree);

        // outFlow[n,s] = outDegree[n,s] - inDegree[n,s] for normal nodes
        OutFlow = new SymbolCombination<LinearExpressionSymbol, Shape2>(
            "bandwidth_outflow_service",
            Shape2.Invoke(_graph.Nodes.Count, _services.Count),
            (i, vec) => {
                Node node = _graph.Nodes[vec[0]];
                SspService service = _services[vec[1]];
                if (node is NormalNode) {
                    // outFlow = outDegree - inDegree
                    var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                    foreach (Edge edge in _graph.Edges.Where(e => e.From == node)) {
                        poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, y[edge.Index, service.Index]));
                    }
                    foreach (Edge edge in _graph.Edges.Where(e => e.To == node)) {
                        poly.AddMonomial(new LinearMonomial<Flt64>(-Flt64.One, y[edge.Index, service.Index]));
                    }
                    return new LinearExpressionSymbol(poly, name: $"bandwidth_outflow_service_{node}_{service}");
                }
                return new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                    name: $"bandwidth_outflow_service_{node}_{service}");
            });
        model.Add(OutFlow);

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
