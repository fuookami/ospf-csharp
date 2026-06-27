#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
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
/// 跨所有服务的每个节点聚合入度、出度和流出的中间符号。Intermediate symbols for aggregated in-degree, out-degree, and out-flow at each node across all services.
/// </summary>
public sealed class NodeBandwidth {
    private readonly IReadOnlyList<Node> _nodes;
    private readonly IReadOnlyList<SspService> _services;
    private readonly ServiceBandwidth _serviceBandwidth;

    /// <summary>节点聚合入度中间符号 / Node aggregated in-degree intermediate symbols</summary>
    public SymbolCombination<LinearExpressionSymbol, Shape1> InDegree { get; private set; } = null!;
    /// <summary>节点聚合出度中间符号 / Node aggregated out-degree intermediate symbols</summary>
    public SymbolCombination<LinearExpressionSymbol, Shape1> OutDegree { get; private set; } = null!;
    /// <summary>节点聚合流出中间符号 / Node aggregated out-flow intermediate symbols</summary>
    public SymbolCombination<LinearExpressionSymbol, Shape1> OutFlow { get; private set; } = null!;

    public NodeBandwidth(IReadOnlyList<Node> nodes, IReadOnlyList<SspService> services, ServiceBandwidth serviceBandwidth) {
        _nodes = nodes;
        _services = services;
        _serviceBandwidth = serviceBandwidth;
    }

    /// <summary>
    /// 注册节点带宽中间符号到模型。Register node bandwidth intermediate symbols to the model.
    /// </summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        // inDegree[n] = sum_s serviceBandwidth.inDegree[n,s]
        InDegree = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            "bandwidth_indegree_node",
            Shape1.Invoke(_nodes.Count),
            (i, _) => {
                Node node = _nodes[i];
                var monomials = new List<LinearMonomial<Flt64>>();
                Flt64 constant = Flt64.Zero;
                foreach (SspService service in _services) {
                    LinearExpressionSymbol svcSym = _serviceBandwidth.InDegree[new int[] { node.Index, service.Index }];
                    foreach (LinearMonomial<Flt64> m in svcSym.Polynomial.Monomials) {
                        monomials.Add(m);
                    }
                    constant += svcSym.Polynomial.Constant;
                }
                var poly = new MutableLinearPolynomial<Flt64>(monomials, constant);
                return new LinearExpressionSymbol(poly, name: $"bandwidth_indegree_node_{node}");
            });
        model.Add(InDegree);

        // outDegree[n] = sum_s serviceBandwidth.outDegree[n,s] for normal nodes
        OutDegree = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            "bandwidth_outdegree_node",
            Shape1.Invoke(_nodes.Count),
            (i, _) => {
                Node node = _nodes[i];
                if (node is NormalNode) {
                    var monomials = new List<LinearMonomial<Flt64>>();
                    Flt64 constant = Flt64.Zero;
                    foreach (SspService service in _services) {
                        LinearExpressionSymbol svcSym = _serviceBandwidth.OutDegree[new int[] { node.Index, service.Index }];
                        foreach (LinearMonomial<Flt64> m in svcSym.Polynomial.Monomials) {
                            monomials.Add(m);
                        }
                        constant += svcSym.Polynomial.Constant;
                    }
                    var poly = new MutableLinearPolynomial<Flt64>(monomials, constant);
                    return new LinearExpressionSymbol(poly, name: $"bandwidth_outdegree_node_{node}");
                }
                return new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                    name: $"bandwidth_outdegree_node_{node}");
            });
        model.Add(OutDegree);

        // outFlow[n] = sum_s serviceBandwidth.outFlow[n,s] for normal nodes
        OutFlow = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            "bandwidth_outflow_node",
            Shape1.Invoke(_nodes.Count),
            (i, _) => {
                Node node = _nodes[i];
                if (node is NormalNode) {
                    var monomials = new List<LinearMonomial<Flt64>>();
                    Flt64 constant = Flt64.Zero;
                    foreach (SspService service in _services) {
                        LinearExpressionSymbol svcSym = _serviceBandwidth.OutFlow[new int[] { node.Index, service.Index }];
                        foreach (LinearMonomial<Flt64> m in svcSym.Polynomial.Monomials) {
                            monomials.Add(m);
                        }
                        constant += svcSym.Polynomial.Constant;
                    }
                    var poly = new MutableLinearPolynomial<Flt64>(monomials, constant);
                    return new LinearExpressionSymbol(poly, name: $"bandwidth_outflow_node_{node}");
                }
                return new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                    name: $"bandwidth_outflow_node_{node}");
            });
        model.Add(OutFlow);

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
