#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Scip;

internal sealed record ScipColumn(double Lower, double Upper, int Type, double? Initial);
internal sealed record ScipRow(int[] Columns, double[] Coefficients, ConstraintRelation Relation, double Right);

internal sealed record ScipLinearSnapshot(
    ScipColumn[] Columns,
    ScipRow[] Rows,
    double[] Objective,
    double Constant,
    ObjectCategory Category) {
    internal static Result<ScipLinearSnapshot, ErrorCode, Error<ErrorCode>> Capture(
        ILinearTriadModelView model,
        CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled();
        }
        Result<Success, ErrorCode, Error<ErrorCode>> identity = model.ValidateIdentity();
        if (!identity.IsOk) {
            return identity.Bind<ScipLinearSnapshot>(_ => Failure("模型身份无效 / Invalid model identity"));
        }
        try {
            if (cancellationToken.IsCancellationRequested) {
                return Cancelled();
            }
            if (model.Objective.Category is not ObjectCategory.Minimum and not ObjectCategory.Maximum
                || !double.IsFinite(model.Objective.Constant.ToDouble())) {
                return Failure("目标方向或常数无效 / Invalid objective direction or constant");
            }
            Result<ScipColumn[], ErrorCode, Error<ErrorCode>> captured =
                ScipSnapshotSupport.CaptureColumns(model.Variables, cancellationToken);
            if (captured is not Ok<ScipColumn[], ErrorCode, Error<ErrorCode>> capturedColumns) {
                return captured.Bind<ScipLinearSnapshot>(_ => Failure(
                    "无法读取变量定义 / Cannot read variable definitions"));
            }
            ScipColumn[] columns = capturedColumns.Value;
            double[] objective = new double[columns.Length];
            foreach (LinearObjectiveCell cell in model.Objective.Cells) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled();
                }
                if (cell is null || cell.ColIndex < 0 || cell.ColIndex >= columns.Length
                    || !double.IsFinite(cell.Coefficient.ToDouble())) {
                    return Failure("目标单元格无效 / Invalid objective cell");
                }
                objective[cell.ColIndex] += cell.Coefficient.ToDouble();
                if (!double.IsFinite(objective[cell.ColIndex])) {
                    return Failure("目标系数溢出 / Objective coefficient overflow");
                }
            }
            ModelConstraint<LinearConstraintCell> constraints = model.Constraints;
            if (constraints.Priorities is not null) {
                return Unsupported("暂不支持约束优先级 / Constraint priorities are not yet supported");
            }
            var rows = new ScipRow[constraints.Size];
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
                var coefficients = new Dictionary<int, double>();
                foreach (LinearConstraintCell cell in constraints.Lhs[row]) {
                    if (cancellationToken.IsCancellationRequested) {
                        return Cancelled();
                    }
                    if (cell is null || cell.RowIndex != row || cell.ColIndex < 0 || cell.ColIndex >= columns.Length
                        || !double.IsFinite(cell.Coefficient.ToDouble())) {
                        return Failure("约束单元格无效 / Invalid constraint cell");
                    }
                    coefficients.TryGetValue(cell.ColIndex, out double previous);
                    double sum = previous + cell.Coefficient.ToDouble();
                    if (!double.IsFinite(sum)) {
                        return Failure("约束系数溢出 / Constraint coefficient overflow");
                    }
                    coefficients[cell.ColIndex] = sum;
                }
                KeyValuePair<int, double>[] entries = coefficients.Where(entry => entry.Value != 0).OrderBy(entry => entry.Key).ToArray();
                rows[row] = new ScipRow(
                    Columns: entries.Select(entry => entry.Key).ToArray(),
                    Coefficients: entries.Select(entry => entry.Value).ToArray(),
                    Relation: relation,
                    Right: right);
            }
            return Results.Ok(new ScipLinearSnapshot(
                Columns: columns,
                Rows: rows,
                Objective: objective,
                Constant: model.Objective.Constant.ToDouble(),
                Category: model.Objective.Category));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure($"无法读取线性模型：{error.Message} / Cannot read the linear model: {error.Message}");
        }
    }

    internal bool Fits(double infinity) => double.IsFinite(infinity) && infinity > 0
        && System.Math.Abs(Constant) < infinity
        && Objective.All(value => System.Math.Abs(value) < infinity)
        && Rows.All(row => System.Math.Abs(row.Right) < infinity && row.Coefficients.All(value => System.Math.Abs(value) < infinity))
        && Columns.All(column => (double.IsNegativeInfinity(column.Lower) || System.Math.Abs(column.Lower) < infinity)
            && (double.IsPositiveInfinity(column.Upper) || System.Math.Abs(column.Upper) < infinity)
            && (column.Initial is null || System.Math.Abs(column.Initial.Value) < infinity));

    private static Result<ScipLinearSnapshot, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Results.Failed<ScipLinearSnapshot>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private static Result<ScipLinearSnapshot, ErrorCode, Error<ErrorCode>> Unsupported(string message) =>
        Results.Failed<ScipLinearSnapshot>(new Err<ErrorCode>(ErrorCode.Other, message));

    private static Result<ScipLinearSnapshot, ErrorCode, Error<ErrorCode>> Cancelled() =>
        Results.Failed<ScipLinearSnapshot>(new Err<ErrorCode>(ErrorCode.ApplicationStopped,
            "模型快照已取消 / Model snapshot was cancelled"));
}
