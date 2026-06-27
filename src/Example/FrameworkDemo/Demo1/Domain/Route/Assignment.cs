#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
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
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Route;

/// <summary>
/// 用于将服务分配到网络中节点的决策变量和中间符号。Decision variables and intermediate symbols for assigning services to nodes in the network.
/// </summary>
public sealed class Assignment {
    private readonly IReadOnlyList<Node> _nodes;
    private readonly IReadOnlyList<SspService> _services;

    /// <summary>二值决策变量 x[n,s]：节点 n 是否分配给服务 s / Binary decision variable x[n,s]: whether node n is assigned to service s</summary>
    public BinVariable2 X { get; private set; } = null!;
    /// <summary>节点分配中间符号：每个普通节点分配的服务数之和 / Node assignment: sum of service assignments per normal node</summary>
    public SymbolCombination<LinearExpressionSymbol, Shape1> NodeAssignment { get; private set; } = null!;
    /// <summary>服务分配中间符号：每个服务分配的节点数之和 / Service assignment: sum of node assignments per service</summary>
    public SymbolCombination<LinearExpressionSymbol, Shape1> ServiceAssignment { get; private set; } = null!;

    public Assignment(IReadOnlyList<Node> nodes, IReadOnlyList<SspService> services) {
        _nodes = nodes;
        _services = services;
    }

    /// <summary>
    /// 注册决策变量和中间符号到模型。Register decision variables and intermediate symbols to the model.
    /// </summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        // x[n,s]: binary variable for node-service assignment
        X = new BinVariable2("x", _nodes.Count, _services.Count);
        foreach (SspService service in _services) {
            foreach (Node node in _nodes.Where(n => n is NormalNode)) {
                X[node.Index, service.Index].Name = $"x_{node}_{service}";
            }
            foreach (Node node in _nodes.Where(n => n is ClientNode)) {
                CombinationVariableItem<UInt8, Binary> variable = X[node.Index, service.Index];
                variable.Name = $"x_{node}_{service}";
                variable.Range.Eq(false);
            }
        }
        model.Add(X.Items);

        // nodeAssignment[n] = sum_s x[n,s] for normal nodes, 0 for client nodes
        NodeAssignment = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            "node_assignment",
            Shape1.Invoke(_nodes.Count),
            (i, _) => {
                Node node = _nodes[i];
                if (node is NormalNode) {
                    var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                    foreach (SspService service in _services) {
                        poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, X[node.Index, service.Index]));
                    }
                    return new LinearExpressionSymbol(poly, name: $"node_assignment_{node}");
                }
                return new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                    name: $"node_assignment_{node}");
            });
        model.Add(NodeAssignment);

        // serviceAssignment[s] = sum_n x[n,s] for normal nodes
        ServiceAssignment = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            "service_assignment",
            Shape1.Invoke(_services.Count),
            (j, _) => {
                SspService service = _services[j];
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                foreach (Node node in _nodes.Where(n => n is NormalNode)) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, X[node.Index, service.Index]));
                }
                return new LinearExpressionSymbol(poly, name: $"service_assignment_{service}");
            });
        model.Add(ServiceAssignment);

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
