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

namespace Fuookami.Ospf.Example.CoreDemo.Demo14;
/// <summary>
/// 节点类型枚举。Node type enumeration.
/// </summary>
public enum NodeType {
    /// <summary>生产节点。Production node.</summary>
    Production,
    /// <summary>中转节点。Transshipment node.</summary>
    Transshipment,
    /// <summary>销售节点。Sales node.</summary>
    Sales
}

/// <summary>
/// 物流节点：具有类型、库存/需求量。Logistics node with type, storage/demand quantity.
/// </summary>
public sealed class LogisticsNode {
    public int Index { get; }
    public string Name { get; }
    public NodeType Type { get; }
    /// <summary>生产节点的库存量或销售节点的需求量。Storage for production or demand for sales.</summary>
    public int Quantity { get; }

    public LogisticsNode(int index, string name, NodeType type, int quantity) {
        Index = index;
        Name = name;
        Type = type;
        Quantity = quantity;
    }
}

/// <summary>
/// 多商品物流分配演示：最小化运输成本。
/// Multi-commodity distribution demo: minimize shipping cost.
/// </summary>
public sealed class MultiCommodityDistributionDemo {
    private static readonly List<LogisticsNode> Nodes = new()
    {
        // Production nodes
        new LogisticsNode(0, "Guangzhou", NodeType.Production, 600),
        new LogisticsNode(1, "Dalian", NodeType.Production, 400),
        // Transshipment nodes
        new LogisticsNode(2, "Shanghai", NodeType.Transshipment, 0),
        new LogisticsNode(3, "Tianjin", NodeType.Transshipment, 0),
        // Sales nodes
        new LogisticsNode(4, "Nanjing", NodeType.Sales, 200),
        new LogisticsNode(5, "Jinan", NodeType.Sales, 150),
        new LogisticsNode(6, "Nanchang", NodeType.Sales, 350),
        new LogisticsNode(7, "Qingdao", NodeType.Sales, 300)
    };

    /// <summary>
    /// 弧的单位运输成本。从 Node1 到 Node2 的运输成本，-1 表示不可直达。
    /// Arc unit shipping cost. Cost from Node1 to Node2; -1 means not directly connected.
    /// </summary>
    private static readonly Dictionary<(int From, int To), Flt64> UnitCost = new()
    {
        // Guangzhou -> Shanghai, Tianjin, Nanjing, Jinan, Nanchang, Qingdao
        { (0, 2), new Flt64(2) }, { (0, 3), new Flt64(6) },
        { (0, 4), new Flt64(4) }, { (0, 5), new Flt64(5) },
        { (0, 6), new Flt64(2) }, { (0, 7), new Flt64(6) },
        // Dalian -> Shanghai, Tianjin, Nanjing, Jinan, Qingdao
        { (1, 2), new Flt64(6) }, { (1, 3), new Flt64(1) },
        { (1, 4), new Flt64(5) }, { (1, 5), new Flt64(3) },
        { (1, 7), new Flt64(2) },
        // Shanghai -> Nanjing, Jinan, Nanchang, Qingdao
        { (2, 4), new Flt64(1) }, { (2, 5), new Flt64(3) },
        { (2, 6), new Flt64(2) }, { (2, 7), new Flt64(4) },
        // Tianjin -> Nanjing, Jinan, Qingdao
        { (3, 4), new Flt64(4) }, { (3, 5), new Flt64(1) },
        { (3, 7), new Flt64(1) }
    };

    /// <summary>从节点 i 到节点 j 的运输量。Shipment quantity from node i to node j.</summary>
    private readonly List<List<UIntVar>> _x = new();
    private LinearExpressionSymbol? _totalCost;
    private readonly LinearMetaModel<Flt64> _metaModel = new("coredemo14-multi-commodity-distribution", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建多商品物流分配模型（不求解，用于测试验证）。
    /// Build the multi-commodity distribution model (without solving, for test verification).
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
        for (int i = 0; i < Nodes.Count; i++) {
            var row = new List<UIntVar>();
            for (int j = 0; j < Nodes.Count; j++) {
                var x = new UIntVar($"x_{i}_{j}");
                row.Add(x);
                // Only add to model if arc exists
                if (UnitCost.ContainsKey((i, j))) {
                    Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
                    if (result.IsFailed) {
                        return result;
                    }
                }
            }
            _x.Add(row);
        }
        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // totalCost = sum(cost[i,j] * x[i,j]) for all arcs
        var costPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (((int From, int To) arc, Flt64 cost) in UnitCost) {
            costPoly.AddMonomial(new LinearMonomial<Flt64>(cost, _x[arc.From][arc.To]));
        }
        _totalCost = new LinearExpressionSymbol(costPoly, name: "totalCost");
        _metaModel.Add(_totalCost);

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // minimize total shipping cost
        return _metaModel.AddObject(
            ObjectCategory.Minimum,
            _totalCost!.Polynomial,
            "totalCost",
            "Total Shipping Cost");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // Production nodes: outgoing <= storage
        foreach (LogisticsNode? node in Nodes.Where(n => n.Type == NodeType.Production)) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            for (int j = 0; j < Nodes.Count; j++) {
                if (UnitCost.ContainsKey((node.Index, j))) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(
                        Flt64.One, _x[node.Index][j]));
                }
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(new Flt64(node.Quantity));
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"production_{node.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        // Sales nodes: incoming >= demand
        foreach (LogisticsNode? node in Nodes.Where(n => n.Type == NodeType.Sales)) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            for (int i = 0; i < Nodes.Count; i++) {
                if (UnitCost.ContainsKey((i, node.Index))) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(
                        Flt64.One, _x[i][node.Index]));
                }
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Ge(new Flt64(node.Quantity));
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"demand_{node.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        // Transshipment nodes: incoming = outgoing (flow conservation)
        foreach (LogisticsNode? node in Nodes.Where(n => n.Type == NodeType.Transshipment)) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            // Incoming
            for (int i = 0; i < Nodes.Count; i++) {
                if (UnitCost.ContainsKey((i, node.Index))) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(
                        Flt64.One, _x[i][node.Index]));
                }
            }
            // Outgoing (subtract)
            for (int j = 0; j < Nodes.Count; j++) {
                if (UnitCost.ContainsKey((node.Index, j))) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(
                        Flt64.One.Negate(), _x[node.Index][j]));
                }
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Eq(Flt64.Zero);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"transship_{node.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
