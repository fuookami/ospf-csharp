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

namespace Fuookami.Ospf.Example.CoreDemo.Demo9;
/// <summary>
/// 设施选址演示：在曼哈顿距离度量下确定设施最优位置。
/// Facility location demo: determine optimal facility position minimizing total Manhattan distance.
/// </summary>
public sealed class FacilityLocationDemo {
    /// <summary>
    /// 居民点坐标 / Settlement coordinates (x, y).
    /// </summary>
    private static readonly List<(Flt64 X, Flt64 Y)> Settlements = new()
    {
        (new Flt64(2), new Flt64(5)),
        (new Flt64(3), new Flt64(8)),
        (new Flt64(5), new Flt64(3)),
        (new Flt64(7), new Flt64(6)),
        (new Flt64(9), new Flt64(2)),
        (new Flt64(4), new Flt64(9))
    };

    private IntVar? _facilityX;
    private IntVar? _facilityY;
    private readonly List<UIntVar> _dx = new();
    private readonly List<UIntVar> _dy = new();
    private LinearExpressionSymbol? _totalDistance;
    private readonly LinearMetaModel<Flt64> _metaModel = new("demo9-facility-location", ObjectCategory.Minimum);

    public LinearMetaModel<Flt64> MetaModel => _metaModel;

    /// <summary>
    /// 构建设施选址模型（不求解，用于测试验证）。
    /// Build the facility location model (without solving, for test verification).
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
        // Facility position variables
        _facilityX = new IntVar("facility_x");
        Result<Success, ErrorCode, Error<ErrorCode>> r1 = _metaModel.Add(_facilityX);
        if (r1.IsFailed) {
            return r1;
        }

        _facilityY = new IntVar("facility_y");
        Result<Success, ErrorCode, Error<ErrorCode>> r2 = _metaModel.Add(_facilityY);
        if (r2.IsFailed) {
            return r2;
        }

        // Absolute distance slack variables for each settlement
        for (int i = 0; i < Settlements.Count; i++) {
            var dx = new UIntVar($"dx_{i}");
            _dx.Add(dx);
            Result<Success, ErrorCode, Error<ErrorCode>> rd = _metaModel.Add(dx);
            if (rd.IsFailed) {
                return rd;
            }

            var dy = new UIntVar($"dy_{i}");
            _dy.Add(dy);
            Result<Success, ErrorCode, Error<ErrorCode>> ry = _metaModel.Add(dy);
            if (ry.IsFailed) {
                return ry;
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitSymbols() {
        // totalDistance = sum(dx[i] + dy[i])
        var distPoly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        for (int i = 0; i < Settlements.Count; i++) {
            distPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _dx[i]));
            distPoly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _dy[i]));
        }
        _totalDistance = new LinearExpressionSymbol(distPoly, name: "totalDistance");
        _metaModel.Add(_totalDistance);

        return Results.Ok(Results.SuccessInstance);
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitObjective() {
        // minimize total Manhattan distance
        return _metaModel.AddObject(
            ObjectCategory.Minimum,
            _totalDistance!.Polynomial,
            "totalDistance",
            "Total Manhattan Distance");
    }

    private Result<Success, ErrorCode, Error<ErrorCode>> InitConstraints() {
        for (int i = 0; i < Settlements.Count; i++) {
            Flt64 sx = Settlements[i].X;
            Flt64 sy = Settlements[i].Y;

            // dx[i] >= facilityX - sx[i]  =>  dx[i] - facilityX >= -sx[i]
            // Rewrite: -facilityX + dx[i] >= -sx[i]
            {
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One.Negate(), _facilityX!));
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _dx[i]));
                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Ge(sx.Negate());
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"dx_{i}_ge_pos");
                if (result.IsFailed) {
                    return result;
                }
            }

            // dx[i] >= sx[i] - facilityX  =>  facilityX + dx[i] >= sx[i]
            {
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _facilityX!));
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _dx[i]));
                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Ge(sx);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"dx_{i}_ge_neg");
                if (result.IsFailed) {
                    return result;
                }
            }

            // dy[i] >= facilityY - sy[i]  =>  dy[i] - facilityY >= -sy[i]
            // Rewrite: -facilityY + dy[i] >= -sy[i]
            {
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One.Negate(), _facilityY!));
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _dy[i]));
                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Ge(sy.Negate());
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"dy_{i}_ge_pos");
                if (result.IsFailed) {
                    return result;
                }
            }

            // dy[i] >= sy[i] - facilityY  =>  facilityY + dy[i] >= sy[i]
            {
                var poly = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _facilityY!));
                poly.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, _dy[i]));
                LinearInequality<Flt64> constraint = poly.ToLinearPolynomial().Ge(sy);
                Result<Success, ErrorCode, Error<ErrorCode>> result = _metaModel.AddConstraint(constraint, group: null, name: $"dy_{i}_ge_neg");
                if (result.IsFailed) {
                    return result;
                }
            }
        }

        return Results.Ok(Results.SuccessInstance);
    }
}
