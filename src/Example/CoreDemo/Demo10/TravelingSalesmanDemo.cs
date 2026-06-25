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

namespace Fuookami.Ospf.Example.CoreDemo.Demo10;
/// <summary>
/// 旅行商问题演示（MTZ 消圈约束）：在 5 个城市间寻找最短哈密顿回路。
/// Traveling Salesman Problem demo (MTZ subtour elimination): find the shortest Hamiltonian cycle among 5 cities.
/// </summary>
public sealed class TravelingSalesmanDemo {
    private static readonly int NumCities = 5;

    /// <summary>
    /// 城市间距离矩阵 / Distance matrix between cities.
    /// Distance[i][j] = distance from city i to city j.
    /// </summary>
    private static readonly List<List<Flt64>> Distance = new()
    {
        new() { new Flt64(0), new Flt64(3), new Flt64(4), new Flt64(2), new Flt64(7) },
        new() { new Flt64(3), new Flt64(0), new Flt64(4), new Flt64(6), new Flt64(3) },
        new() { new Flt64(4), new Flt64(4), new Flt64(0), new Flt64(5), new Flt64(8) },
        new() { new Flt64(2), new Flt64(6), new Flt64(5), new Flt64(0), new Flt64(6) },
        new() { new Flt64(7), new Flt64(3), new Flt64(8), new Flt64(6), new Flt64(0) }
    };

    /// <summary>MTZ 大 M 常数 / Big-M constant for MTZ constraints.</summary>
    private static readonly Flt64 BigN = new(NumCities);

    private readonly List<List<BinVar>> _x = new();
    private readonly List<IntVar> _u = new();
    private LinearExpressionSymbol? _totalDistance;
    private readonly LinearMetaModel<Flt64> _metaModel = new("demo10-tsp", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建 TSP 模型（不求解，用于测试验证）。
    /// Build the TSP model (without solving, for test verification).
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
        // Binary edge variables x[i][j]
        for (int i = 0; i < NumCities; i++) {
            var cityVars = new List<BinVar>();
            for (int j = 0; j < NumCities; j++) {
                var x = new BinVar($"x_{i}_{j}");
                cityVars.Add(x);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(x);
                if (result.IsFailed) {
                    return result;
                }
            }
            _x.Add(cityVars);
        }

        // MTZ position variables u[i] (for subtour elimination)
        for (int i = 0; i < NumCities; i++) {
            var u = new IntVar($"u_{i}");
            _u.Add(u);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.Add(u);
            if (result.IsFailed) {
                return result;
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // totalDistance = sum(distance[i][j] * x[i][j])
        var distPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        for (int i = 0; i < NumCities; i++) {
            for (int j = 0; j < NumCities; j++) {
                if (i == j) {
                    continue;
                }

                distPoly.AddMonomial(new LinearMonomial<Flt64>(Distance[i][j], _x[i][j]));
            }
        }
        _totalDistance = new LinearExpressionSymbol(distPoly, name: "totalDistance");
        _metaModel.Add(_totalDistance);

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // minimize total distance
        return _metaModel.AddObject(
            ObjectCategory.Minimum,
            _totalDistance!.Polynomial,
            "totalDistance",
            "Total Travel Distance");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        // Each city is departed from exactly once: sum_j(x[i][j]) = 1 for all i
        for (int i = 0; i < NumCities; i++) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            for (int j = 0; j < NumCities; j++) {
                if (i == j) {
                    continue;
                }

                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _x[i][j]));
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Eq(Flt64.One);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"depart_{i}");
            if (result.IsFailed) {
                return result;
            }
        }

        // Each city is reached exactly once: sum_i(x[i][j]) = 1 for all j
        for (int j = 0; j < NumCities; j++) {
            var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
            for (int i = 0; i < NumCities; i++) {
                if (i == j) {
                    continue;
                }

                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _x[i][j]));
            }
            LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Eq(Flt64.One);
            Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"arrive_{j}");
            if (result.IsFailed) {
                return result;
            }
        }

        // MTZ subtour elimination: u[i] - u[j] + n*x[i][j] <= n - 1, for i != j, i,j >= 1
        for (int i = 1; i < NumCities; i++) {
            for (int j = 1; j < NumCities; j++) {
                if (i == j) {
                    continue;
                }
                // u[i] - u[j] + n*x[i][j] <= n - 1
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _u[i]));
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One.Negate(), _u[j]));
                poly.AddMonomial(new LinearMonomial<Flt64>(BigN, _x[i][j]));
                Flt64 rhs = BigN.Minus(Flt64.One);
                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Le(rhs);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"mtz_{i}_{j}");
                if (result.IsFailed) {
                    return result;
                }
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
