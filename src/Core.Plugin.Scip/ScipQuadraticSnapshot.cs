#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Plugin.Scip;

internal sealed record ScipQuadraticTerm(int Col1, int Col2, double Coefficient);

internal sealed record ScipQuadraticRow(
    int[] LinearColumns,
    double[] LinearCoefficients,
    ScipQuadraticTerm[] QuadraticTerms,
    ConstraintRelation Relation,
    double Right);

internal sealed record ScipQuadraticSnapshot(
    ScipColumn[] Columns,
    ScipQuadraticRow[] Rows,
    double[] Objective,
    ScipQuadraticTerm[] QuadraticObjective,
    double Constant,
    ObjectCategory Category) {
    internal static Result<ScipQuadraticSnapshot, ErrorCode, Error<ErrorCode>> Capture(
        IQuadraticTetradModelView model,
        CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled();
        }
        Result<Success, ErrorCode, Error<ErrorCode>> identity = model.ValidateIdentity();
        if (!identity.IsOk) {
            return identity.Bind<ScipQuadraticSnapshot>(_ => Failure(
                "模型身份无效 / Invalid model identity"));
        }
        try {
            Result<ScipColumn[], ErrorCode, Error<ErrorCode>> captured =
                ScipSnapshotSupport.CaptureColumns(model.Variables, cancellationToken);
            if (captured is not Ok<ScipColumn[], ErrorCode, Error<ErrorCode>> capturedColumns) {
                return captured.Bind<ScipQuadraticSnapshot>(_ => Failure(
                    "无法读取变量定义 / Cannot read variable definitions"));
            }
            ScipColumn[] columns = capturedColumns.Value;
            if (model.Objective.Category is not ObjectCategory.Minimum and not ObjectCategory.Maximum
                || !double.IsFinite(model.Objective.Constant.ToDouble())) {
                return Failure("目标方向或常数无效 / Invalid objective direction or constant");
            }

            double[] objective = new double[columns.Length];
            var quadraticObjective = new Dictionary<(int First, int Second), double>();
            foreach (QuadraticObjectiveCell? cell in model.Objective.Cells) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled();
                }
                Result<Success, ErrorCode, Error<ErrorCode>> valid = ValidateCell(
                    cell?.Col1 ?? -1,
                    cell?.Col2 ?? -2,
                    cell?.Coefficient ?? Flt64.Zero,
                    columns.Length);
                if (!valid.IsOk) {
                    return valid.Bind<ScipQuadraticSnapshot>(_ => Failure(
                        "目标单元格无效 / Invalid objective cell"));
                }
                double coefficient = cell!.Coefficient.ToDouble();
                if (cell.Col2 < 0) {
                    objective[cell.Col1] += coefficient;
                    if (!double.IsFinite(objective[cell.Col1])) {
                        return Failure("目标系数溢出 / Objective coefficient overflow");
                    }
                }
                else {
                    (int first, int second) = Normalize(cell.Col1, cell.Col2);
                    quadraticObjective.TryGetValue((first, second), out double previous);
                    double sum = previous + coefficient;
                    if (!double.IsFinite(sum)) {
                        return Failure("二次目标系数溢出 / Quadratic objective coefficient overflow");
                    }
                    quadraticObjective[(first, second)] = sum;
                }
            }

            ModelConstraint<QuadraticConstraintCell> constraints = model.Constraints;
            if (constraints.Priorities is not null) {
                return Unsupported("暂不支持约束优先级 / Constraint priorities are not yet supported");
            }
            var rows = new ScipQuadraticRow[constraints.Size];
            for (int row = 0; row < rows.Length; row++) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled();
                }
                ConstraintRelation relation = constraints.Signs[row];
                double right = constraints.Rhs[row].ToDouble();
                if ((relation != ConstraintRelation.LessEqual && relation != ConstraintRelation.Equal
                        && relation != ConstraintRelation.GreaterEqual) || !double.IsFinite(right)) {
                    return Failure("约束关系或右端值无效 / Invalid constraint relation or right-hand side");
                }
                var linear = new Dictionary<int, double>();
                var quadratic = new Dictionary<(int First, int Second), double>();
                foreach (QuadraticConstraintCell? cell in constraints.Lhs[row]) {
                    if (cancellationToken.IsCancellationRequested) {
                        return Cancelled();
                    }
                    Result<Success, ErrorCode, Error<ErrorCode>> valid = ValidateCell(
                        cell?.Col1 ?? -1,
                        cell?.Col2 ?? -2,
                        cell?.Coefficient ?? Flt64.Zero,
                        columns.Length);
                    if (!valid.IsOk || cell!.RowIndex != row) {
                        return Failure("约束单元格无效 / Invalid constraint cell");
                    }
                    double coefficient = cell.Coefficient.ToDouble();
                    if (cell.Col2 < 0) {
                        linear.TryGetValue(cell.Col1, out double previous);
                        double sum = previous + coefficient;
                        if (!double.IsFinite(sum)) {
                            return Failure("约束系数溢出 / Constraint coefficient overflow");
                        }
                        linear[cell.Col1] = sum;
                    }
                    else {
                        (int first, int second) = Normalize(cell.Col1, cell.Col2);
                        quadratic.TryGetValue((first, second), out double previous);
                        double sum = previous + coefficient;
                        if (!double.IsFinite(sum)) {
                            return Failure("二次约束系数溢出 / Quadratic constraint coefficient overflow");
                        }
                        quadratic[(first, second)] = sum;
                    }
                }
                KeyValuePair<int, double>[] linearEntries = linear
                    .Where(entry => entry.Value != 0)
                    .OrderBy(entry => entry.Key)
                    .ToArray();
                ScipQuadraticTerm[] quadraticTerms = quadratic
                    .Where(entry => entry.Value != 0)
                    .OrderBy(entry => entry.Key.First)
                    .ThenBy(entry => entry.Key.Second)
                    .Select(entry => new ScipQuadraticTerm(
                        entry.Key.First,
                        entry.Key.Second,
                        entry.Value))
                    .ToArray();
                rows[row] = new ScipQuadraticRow(
                    LinearColumns: linearEntries.Select(entry => entry.Key).ToArray(),
                    LinearCoefficients: linearEntries.Select(entry => entry.Value).ToArray(),
                    QuadraticTerms: quadraticTerms,
                    Relation: relation,
                    Right: right);
            }
            return Results.Ok(new ScipQuadraticSnapshot(
                Columns: columns,
                Rows: rows,
                Objective: objective,
                QuadraticObjective: quadraticObjective
                    .Where(entry => entry.Value != 0)
                    .OrderBy(entry => entry.Key.First)
                    .ThenBy(entry => entry.Key.Second)
                    .Select(entry => new ScipQuadraticTerm(
                        entry.Key.First,
                        entry.Key.Second,
                        entry.Value))
                    .ToArray(),
                Constant: model.Objective.Constant.ToDouble(),
                Category: model.Objective.Category));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure($"无法读取二次模型：{error.Message} / Cannot read the quadratic model: {error.Message}");
        }
    }

    internal bool Fits(double infinity) => double.IsFinite(infinity) && infinity > 0
        && System.Math.Abs(Constant) < infinity
        && Objective.All(value => System.Math.Abs(value) < infinity)
        && QuadraticObjective.All(term => System.Math.Abs(term.Coefficient) < infinity)
        && Rows.All(row => System.Math.Abs(row.Right) < infinity
            && row.LinearCoefficients.All(value => System.Math.Abs(value) < infinity)
            && row.QuadraticTerms.All(term => System.Math.Abs(term.Coefficient) < infinity))
        && Columns.All(column => (double.IsNegativeInfinity(column.Lower) || System.Math.Abs(column.Lower) < infinity)
            && (double.IsPositiveInfinity(column.Upper) || System.Math.Abs(column.Upper) < infinity)
            && (column.Initial is null || System.Math.Abs(column.Initial.Value) < infinity));

    private static Result<Success, ErrorCode, Error<ErrorCode>> ValidateCell(
        int first,
        int second,
        Flt64 coefficient,
        int columnCount) {
        int[] columns = second < 0 ? [first] : [first, second];
        if (columns.Any(column => column < 0 || column >= columnCount)
            || !double.IsFinite(coefficient.ToDouble())) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "二次单元格索引或系数无效 / Invalid quadratic cell index or coefficient"));
        }
        return Results.OkInstance;
    }

    private static (int First, int Second) Normalize(int first, int second) =>
        first <= second ? (first, second) : (second, first);

    private static Result<ScipQuadraticSnapshot, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Results.Failed<ScipQuadraticSnapshot>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private static Result<ScipQuadraticSnapshot, ErrorCode, Error<ErrorCode>> Unsupported(string message) =>
        Results.Failed<ScipQuadraticSnapshot>(new Err<ErrorCode>(ErrorCode.Other, message));

    private static Result<ScipQuadraticSnapshot, ErrorCode, Error<ErrorCode>> Cancelled() =>
        Results.Failed<ScipQuadraticSnapshot>(new Err<ErrorCode>(
            ErrorCode.ApplicationStopped,
            "模型快照已取消 / Model snapshot was cancelled"));
}
