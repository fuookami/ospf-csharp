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

namespace Fuookami.Ospf.Example.CoreDemo.Demo17;
/// <summary>
/// 客户节点：具有位置、时间窗和需求量。Customer node with position, time window, and demand.
/// </summary>
public sealed class CustomerNode {
    public int Index { get; }
    public string Name { get; }
    public Flt64 PosX { get; }
    public Flt64 PosY { get; }
    public Flt64 TimeWindowStart { get; }
    public Flt64 TimeWindowEnd { get; }
    public int Demand { get; }

    public CustomerNode(int index, string name,
        Flt64 posX, Flt64 posY,
        Flt64 timeWindowStart, Flt64 timeWindowEnd,
        int demand) {
        Index = index;
        Name = name;
        PosX = posX;
        PosY = posY;
        TimeWindowStart = timeWindowStart;
        TimeWindowEnd = timeWindowEnd;
        Demand = demand;
    }
}

/// <summary>
/// 车辆信息。Vehicle information.
/// </summary>
public sealed class Vehicle {
    public int Index { get; }
    public string Name { get; }
    public int Capacity { get; }
    public Flt64 FixedCost { get; }

    public Vehicle(int index, string name, int capacity, Flt64 fixedCost) {
        Index = index;
        Name = name;
        Capacity = capacity;
        FixedCost = fixedCost;
    }
}

/// <summary>
/// 带时间窗的车辆路径规划演示（简化版）：最小化固定成本和行驶成本。
/// Vehicle Routing Problem with Time Windows demo (simplified):
/// minimize fixed cost and travel cost.
///
/// 简化实例：1 个起点、1 个终点、5 个需求节点、3 辆车。
/// Simplified instance: 1 origin, 1 end, 5 demand nodes, 3 vehicles.
/// </summary>
public sealed class VrptwDemo {
    // Node 0 = origin (depot start), Node 6 = end (depot end)
    // Nodes 1-5 = demand nodes
    private static readonly CustomerNode Origin = new(0, "Origin",
        new Flt64(0), new Flt64(0), new Flt64(0), new Flt64(100), 0);

    private static readonly CustomerNode End = new(6, "End",
        new Flt64(0), new Flt64(0), new Flt64(0), new Flt64(100), 0);

    private static readonly List<CustomerNode> DemandNodes = new()
    {
        new CustomerNode(1, "C1", new Flt64(2), new Flt64(3), new Flt64(5), new Flt64(30), 50),
        new CustomerNode(2, "C2", new Flt64(5), new Flt64(1), new Flt64(10), new Flt64(40), 60),
        new CustomerNode(3, "C3", new Flt64(3), new Flt64(5), new Flt64(8), new Flt64(35), 40),
        new CustomerNode(4, "C4", new Flt64(6), new Flt64(4), new Flt64(15), new Flt64(50), 70),
        new CustomerNode(5, "C5", new Flt64(1), new Flt64(6), new Flt64(3), new Flt64(25), 30)
    };

    private static readonly List<Vehicle> Vehicles = new()
    {
        new Vehicle(0, "V1", 200, new Flt64(500)),
        new Vehicle(1, "V2", 200, new Flt64(500)),
        new Vehicle(2, "V3", 200, new Flt64(500))
    };

    /// <summary>所有节点（含起终点）。All nodes including origin and end.</summary>
    private static List<CustomerNode> AllNodes {
        get {
            var nodes = new List<CustomerNode> { Origin };
            nodes.AddRange(DemandNodes);
            nodes.Add(End);
            return nodes;
        }
    }

    private static Flt64 Distance(CustomerNode a, CustomerNode b) {
        Flt64 dx = a.PosX.Minus(b.PosX);
        Flt64 dy = a.PosY.Minus(b.PosY);
        // Euclidean distance approximation: |dx| + |dy| (Manhattan)
        return (dx.Geq(Flt64.Zero) ? dx : dx.Negate())
            .Plus(dy.Geq(Flt64.Zero) ? dy : dy.Negate());
    }

    /// <summary>x[i,j,v] = 1 if vehicle v travels from node i to node j.</summary>
    private readonly List<List<List<BinVar>>> _x = new();
    /// <summary>s[n,v] = service start time of node n by vehicle v.</summary>
    private readonly List<List<URealVar>> _s = new();
    private LinearExpressionSymbol? _totalCost;
    private readonly LinearMetaModel<Flt64> _metaModel = new("coredemo17-vrptw", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建 VRPTW 模型（不求解，用于测试验证）。
    /// Build the VRPTW model (without solving, for test verification).
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
        List<CustomerNode> allNodes = AllNodes;
        int nodeCount = allNodes.Count;

        // x[i,j,v]
        foreach (CustomerNode vi in allNodes) {
            var vjSlices = new List<List<BinVar>>();
            foreach (CustomerNode vj in allNodes) {
                var vehVars = new List<BinVar>();
                foreach (Vehicle veh in Vehicles) {
                    var xv = new BinVar($"x_{vi.Index}_{vj.Index}_{veh.Index}");
                    vehVars.Add(xv);
                    // Skip self-loops (i==j) from model but keep in list for indexing
                    if (vi.Index != vj.Index) {
                        Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(xv);
                        if (result.IsFailed) {
                            return result;
                        }
                    }
                }
                vjSlices.Add(vehVars);
            }
            _x.Add(vjSlices);
        }

        // s[n,v]
        foreach (CustomerNode node in allNodes) {
            var vehVars = new List<URealVar>();
            foreach (Vehicle veh in Vehicles) {
                var s = new URealVar($"s_{node.Index}_{veh.Index}");
                vehVars.Add(s);
                // Only add demand node service times to model (skip origin/end for simplicity)
                if (node.Index != 0 && node.Index != 6) {
                    Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(s);
                    if (result.IsFailed) {
                        return result;
                    }
                }
            }
            _s.Add(vehVars);
        }

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        List<CustomerNode> allNodes = AllNodes;

        // totalCost = fixed cost * sum(x[0, j, v]) + sum(distance[i,j] * x[i,j,v])
        var costPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);

        // Fixed cost: each vehicle used departs from origin (node 0)
        foreach (CustomerNode dest in allNodes) {
            foreach (Vehicle veh in Vehicles) {
                if (dest.Index != 0) {
                    costPoly.AddMonomial(new LinearMonomial<Flt64>(
                        veh.FixedCost, _x[0][dest.Index][veh.Index]));
                }
            }
        }

        // Travel cost
        foreach (CustomerNode from in allNodes) {
            foreach (CustomerNode to in allNodes) {
                if (from.Index == to.Index) {
                    continue;
                }

                Flt64 dist = Distance(from, to);
                foreach (Vehicle veh in Vehicles) {
                    costPoly.AddMonomial(new LinearMonomial<Flt64>(
                        dist, _x[from.Index][to.Index][veh.Index]));
                }
            }
        }

        _totalCost = new LinearExpressionSymbol(costPoly, name: "totalCost");
        _metaModel.Add(_totalCost);

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // minimize total cost (fixed + travel)
        return _metaModel.AddObject(
            ObjectCategory.Minimum,
            _totalCost!.Polynomial,
            "totalCost",
            "Total Fixed + Travel Cost");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        List<CustomerNode> allNodes = AllNodes;

        // 1. Flow balance: each demand node visited exactly once by exactly one vehicle
        // sum(x[*, n, v]) = 1 for each demand node n, summed over all vehicles
        foreach (CustomerNode node in DemandNodes) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (CustomerNode from in allNodes) {
                if (from.Index == node.Index) {
                    continue;
                }

                foreach (Vehicle veh in Vehicles) {
                    poly.AddMonomial(new LinearMonomial<Flt64>(
                        Flt64.One, _x[from.Index][node.Index][veh.Index]));
                }
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Eq(Flt64.One);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"visit_{node.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        // 2. Each vehicle leaves origin at most once
        foreach (Vehicle veh in Vehicles) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (CustomerNode to in allNodes) {
                if (to.Index == 0) {
                    continue;
                }

                poly.AddMonomial(new LinearMonomial<Flt64>(
                    Flt64.One, _x[0][to.Index][veh.Index]));
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(Flt64.One);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"depart_{veh.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        // 3. Each vehicle enters end at most once
        foreach (Vehicle veh in Vehicles) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (CustomerNode from in allNodes) {
                if (from.Index == 6) {
                    continue;
                }

                poly.AddMonomial(new LinearMonomial<Flt64>(
                    Flt64.One, _x[from.Index][6][veh.Index]));
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(Flt64.One);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"arrive_{veh.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        // 4. Capacity: sum(demand[n] * visited(n, v)) <= capacity[v]
        // Approximate: for each vehicle, sum of demands along its route <= capacity
        // visited(n, v) = sum(x[*, n, v]) = 1 (from constraint 1, this is tight)
        // Simplified: total demand <= total capacity (relaxed aggregate)
        int totalDemand = DemandNodes.Sum(n => n.Demand);
        var totalCapPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (CustomerNode node in DemandNodes) {
            foreach (CustomerNode from in allNodes) {
                if (from.Index == node.Index) {
                    continue;
                }

                totalCapPoly.AddMonomial(new LinearMonomial<Flt64>(
                    new Flt64(node.Demand),
                    _x[from.Index][node.Index][0]));
            }
        }
        // Per-vehicle capacity constraint
        foreach (Vehicle veh in Vehicles) {
            var capPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            foreach (CustomerNode node in DemandNodes) {
                foreach (CustomerNode from in allNodes) {
                    if (from.Index == node.Index) {
                        continue;
                    }

                    capPoly.AddMonomial(new LinearMonomial<Flt64>(
                        new Flt64(node.Demand),
                        _x[from.Index][node.Index][veh.Index]));
                }
            }
            LinearInequality<Flt64> constraint = capPoly.ToLinearPolynomial().Le(new Flt64(veh.Capacity));
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                name: $"capacity_{veh.Index}");
            if (result.IsFailed) {
                return result;
            }
        }

        // 5. Time window: s[n,v] >= tw_start[n] (lower bound)
        foreach (CustomerNode node in DemandNodes) {
            foreach (Vehicle veh in Vehicles) {
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                poly.AddMonomial(new LinearMonomial<Flt64>(
                    Flt64.One, _s[node.Index][veh.Index]));
                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Ge(node.TimeWindowStart);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                    name: $"tw_lb_{node.Index}_{veh.Index}");
                if (result.IsFailed) {
                    return result;
                }
            }
        }

        // 6. Time window: s[n,v] <= tw_end[n] (upper bound)
        foreach (CustomerNode node in DemandNodes) {
            foreach (Vehicle veh in Vehicles) {
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                poly.AddMonomial(new LinearMonomial<Flt64>(
                    Flt64.One, _s[node.Index][veh.Index]));
                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(node.TimeWindowEnd);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null,
                    name: $"tw_ub_{node.Index}_{veh.Index}");
                if (result.IsFailed) {
                    return result;
                }
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
