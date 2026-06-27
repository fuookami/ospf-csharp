#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Route;
using Fuookami.Ospf.Math.Algebra.Concept;
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

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo1.Domain.Bandwidth;

/// <summary>
/// 跨服务的每边带宽分配的决策变量和中间符号。Decision variables and intermediate symbols for per-edge bandwidth allocation across services.
/// </summary>
public sealed class EdgeBandwidth {
    private readonly IReadOnlyList<Edge> _edges;
    private readonly IReadOnlyList<SspService> _services;

    /// <summary>无符号整数变量 y[e,s]：边 e 上服务 s 的带宽分配 / Unsigned integer variable y[e,s]: bandwidth of service s on edge e</summary>
    public UIntVar2 Y { get; private set; } = null!;
    /// <summary>带宽中间符号：每条边跨服务的带宽总和 / Bandwidth: total bandwidth per edge across services</summary>
    public SymbolCombination<LinearExpressionSymbol, Shape1> Bandwidth { get; private set; } = null!;

    public EdgeBandwidth(IReadOnlyList<Edge> edges, IReadOnlyList<SspService> services) {
        _edges = edges;
        _services = services;
    }

    /// <summary>
    /// 注册边带宽变量和中间符号到模型。Register edge bandwidth variables and intermediate symbols to the model.
    /// </summary>
    /// <param name="model">元模型 / Meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        // y[e,s]: unsigned integer variable for edge bandwidth allocation
        Y = new UIntVar2("y", _edges.Count, _services.Count);
        foreach (SspService service in _services) {
            foreach (Edge edge in _edges.Where(e => e.From is NormalNode)) {
                Y[edge.Index, service.Index].Name = $"y_{edge}_{service}";
                Y[edge.Index, service.Index].Range.Leq(new InvariantUInt64Wrapper(edge.MaxBandwidth));
            }
            foreach (Edge edge in _edges.Where(e => e.From is not NormalNode)) {
                Y[edge.Index, service.Index].Range.Eq(UInt64.Zero);
            }
        }
        model.Add(Y.Items);

        // bandwidth[e] = sum_s y[e,s] for normal-source edges, 0 otherwise
        Bandwidth = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            "bandwidth",
            Shape1.Invoke(_edges.Count),
            (i, _) => {
                Edge edge = _edges[i];
                if (edge.From is NormalNode) {
                    var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                    foreach (SspService service in _services) {
                        poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, Y[edge.Index, service.Index]));
                    }
                    return new LinearExpressionSymbol(poly, name: $"bandwidth_{edge}");
                }
                return new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                    name: $"bandwidth_{edge}");
            });
        model.Add(Bandwidth);

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}

/// <summary>
/// UInt64 的不变量包装器 / Invariant wrapper for UInt64
/// </summary>
internal readonly struct InvariantUInt64Wrapper : IInvariant<UInt64> {
    private readonly UInt64 _value;
    public InvariantUInt64Wrapper(UInt64 value) => _value = value;
    public UInt64 Value() => _value;
}
