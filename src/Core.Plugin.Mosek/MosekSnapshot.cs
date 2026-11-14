#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Plugin.Mosek;

internal sealed record MosekColumn(
    double Lower,
    double Upper,
    bool Integer,
    double? Initial);

internal sealed record MosekRow(
    int[] Columns,
    double[] Coefficients,
    ConstraintRelation Relation,
    double Right);

internal sealed record MosekLinearSnapshot(
    MosekColumn[] Columns,
    MosekRow[] Rows,
    double[] Objective,
    double Constant,
    ObjectCategory Category) {
    /// <summary>
    /// 捕获线性模型快照 / Capture an immutable linear-model snapshot.
    /// </summary>
    /// <param name="model">线性模型视图 / Linear model view.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>模型快照或错误 / Model snapshot or an error.</returns>
    internal static Result<MosekLinearSnapshot, ErrorCode, Error<ErrorCode>> Capture(
        ILinearTriadModelView model,
        CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled();
        }
        Result<Success, ErrorCode, Error<ErrorCode>> identity = model.ValidateIdentity();
        if (!identity.IsOk) {
            return identity.Bind<MosekLinearSnapshot>(_ => Failure(
                "模型身份无效 / Invalid model identity"));
        }
        try {
            var columns = new MosekColumn[model.Variables.Count];
            for (int index = 0; index < columns.Length; index++) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled();
                }
                ModelViewVariable variable = model.Variables[index];
                if (variable is null || variable.Index != index) {
                    return Failure("变量索引无效 / Invalid variable index");
                }
                double lower = variable.LowerBound.ToDouble();
                double upper = variable.UpperBound.ToDouble();
                double? initial = variable.InitialResult?.ToDouble();
                if (lower > upper
                    || double.IsNaN(lower)
                    || double.IsNaN(upper)
                    || double.IsNaN(initial ?? 0)) {
                    return Failure(
                        "变量边界或初始解无效 / Invalid variable bounds or initial solution");
                }
                columns[index] = new MosekColumn(
                    Lower: lower,
                    Upper: upper,
                    Integer: !variable.Type.IsContinuousType,
                    Initial: initial);
            }

            if (model.Objective.Category is not ObjectCategory.Minimum and not ObjectCategory.Maximum
                || !double.IsFinite(model.Objective.Constant.ToDouble())) {
                return Failure("目标方向或常数无效 / Invalid objective direction or constant");
            }

            double[] objective = new double[columns.Length];
            foreach (LinearObjectiveCell cell in model.Objective.Cells) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled();
                }
                if (cell is null
                    || cell.ColIndex < 0
                    || cell.ColIndex >= objective.Length
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
                return Unsupported(
                    "暂不支持 MOSEK 约束优先级 / MOSEK constraint priorities are not yet supported");
            }
            var rows = new MosekRow[constraints.Size];
            for (int row = 0; row < rows.Length; row++) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled();
                }
                ConstraintRelation relation = constraints.Signs[row];
                double right = constraints.Rhs[row].ToDouble();
                if (!ValidRelation(relation) || !double.IsFinite(right)) {
                    return Failure(
                        "约束关系或右端值无效 / Invalid constraint relation or right-hand side");
                }
                var coefficients = new Dictionary<int, double>();
                foreach (LinearConstraintCell cell in constraints.Lhs[row]) {
                    if (cancellationToken.IsCancellationRequested) {
                        return Cancelled();
                    }
                    if (cell is null
                        || cell.RowIndex != row
                        || cell.ColIndex < 0
                        || cell.ColIndex >= objective.Length
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
                KeyValuePair<int, double>[] entries = coefficients
                    .Where(entry => entry.Value != 0)
                    .OrderBy(entry => entry.Key)
                    .ToArray();
                rows[row] = new MosekRow(
                    Columns: entries.Select(entry => entry.Key).ToArray(),
                    Coefficients: entries.Select(entry => entry.Value).ToArray(),
                    Relation: relation,
                    Right: right);
            }
            return Results.Ok(new MosekLinearSnapshot(
                Columns: columns,
                Rows: rows,
                Objective: objective,
                Constant: model.Objective.Constant.ToDouble(),
                Category: model.Objective.Category));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(
                $"无法读取线性模型：{error.Message} / Cannot read the linear model: {error.Message}");
        }
    }

    private static bool ValidRelation(ConstraintRelation relation) =>
        relation == ConstraintRelation.LessEqual
        || relation == ConstraintRelation.Equal
        || relation == ConstraintRelation.GreaterEqual;

    private static Result<MosekLinearSnapshot, ErrorCode, Error<ErrorCode>> Cancelled() =>
        Results.Failed<MosekLinearSnapshot>(new Err<ErrorCode>(
            ErrorCode.ApplicationStopped,
            "模型快照已取消 / Model snapshot was cancelled"));

    private static Result<MosekLinearSnapshot, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Results.Failed<MosekLinearSnapshot>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private static Result<MosekLinearSnapshot, ErrorCode, Error<ErrorCode>> Unsupported(string message) =>
        Results.Failed<MosekLinearSnapshot>(new Err<ErrorCode>(ErrorCode.Other, message));
}
