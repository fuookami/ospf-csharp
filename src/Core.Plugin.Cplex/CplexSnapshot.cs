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

namespace Fuookami.Ospf.Core.Plugin.Cplex;

internal sealed record CplexColumn(
    double Lower,
    double Upper,
    char Type,
    double? Initial);

internal sealed record CplexRow(
    int[] Columns,
    double[] Coefficients,
    ConstraintRelation Relation,
    double Right);

internal sealed record CplexLinearSnapshot(
    CplexColumn[] Columns,
    CplexRow[] Rows,
    double[] Objective,
    double Constant,
    ObjectCategory Category) {
    /// <summary>
    /// 捕获线性模型快照 / Capture an immutable linear-model snapshot.
    /// </summary>
    /// <param name="model">线性模型视图 / Linear model view.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>模型快照或错误 / Model snapshot or an error.</returns>
    internal static Result<CplexLinearSnapshot, ErrorCode, Error<ErrorCode>> Capture(
        ILinearTriadModelView model,
        CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled();
        }
        Result<Success, ErrorCode, Error<ErrorCode>> identity = model.ValidateIdentity();
        if (!identity.IsOk) {
            return identity.Bind<CplexLinearSnapshot>(_ => Failure(
                "模型身份无效 / Invalid model identity"));
        }
        try {
            Result<CplexColumn[], ErrorCode, Error<ErrorCode>> columns =
                CplexSnapshotSupport.CaptureColumns(
                    variables: model.Variables,
                    cancellationToken: cancellationToken);
            if (columns is not Ok<CplexColumn[], ErrorCode, Error<ErrorCode>> captured) {
                return columns.Bind<CplexLinearSnapshot>(_ => Failure(
                    "无法读取变量定义 / Cannot read variable definitions"));
            }
            if (model.Objective.Category is not ObjectCategory.Minimum and not ObjectCategory.Maximum
                || !double.IsFinite(model.Objective.Constant.ToDouble())) {
                return Failure("目标方向或常数无效 / Invalid objective direction or constant");
            }

            double[] objective = new double[captured.Value.Length];
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
                    "暂不支持 Cplex 约束优先级 / Cplex constraint priorities are not yet supported");
            }
            var rows = new CplexRow[constraints.Size];
            for (int row = 0; row < rows.Length; row++) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled();
                }
                ConstraintRelation relation = constraints.Signs[row];
                double right = constraints.Rhs[row].ToDouble();
                if (!CplexSnapshotSupport.ValidRelation(relation) || !double.IsFinite(right)) {
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
                    if (!CplexSnapshotSupport.Add(
                            values: coefficients,
                            key: cell.ColIndex,
                            value: cell.Coefficient.ToDouble())) {
                        return Failure("约束系数溢出 / Constraint coefficient overflow");
                    }
                }
                rows[row] = CplexSnapshotSupport.Row(
                    linear: coefficients,
                    relation: relation,
                    right: right);
            }
            return Results.Ok(new CplexLinearSnapshot(
                Columns: captured.Value,
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

    private static Result<CplexLinearSnapshot, ErrorCode, Error<ErrorCode>> Cancelled() =>
        Results.Failed<CplexLinearSnapshot>(new Err<ErrorCode>(
            ErrorCode.ApplicationStopped,
            "模型快照已取消 / Model snapshot was cancelled"));

    private static Result<CplexLinearSnapshot, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Results.Failed<CplexLinearSnapshot>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private static Result<CplexLinearSnapshot, ErrorCode, Error<ErrorCode>> Unsupported(string message) =>
        Results.Failed<CplexLinearSnapshot>(new Err<ErrorCode>(ErrorCode.Other, message));
}

internal sealed record CplexQuadraticTerm(
    int Col1,
    int Col2,
    double Coefficient);

internal sealed record CplexQuadraticRow(
    int[] LinearColumns,
    double[] LinearCoefficients,
    CplexQuadraticTerm[] QuadraticTerms,
    ConstraintRelation Relation,
    double Right);

internal sealed record CplexQuadraticSnapshot(
    CplexColumn[] Columns,
    CplexQuadraticRow[] Rows,
    double[] Objective,
    CplexQuadraticTerm[] QuadraticObjective,
    double Constant,
    ObjectCategory Category) {
    /// <summary>
    /// 捕获二次模型快照 / Capture an immutable quadratic-model snapshot.
    /// </summary>
    /// <param name="model">二次模型视图 / Quadratic model view.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>模型快照或错误 / Model snapshot or an error.</returns>
    internal static Result<CplexQuadraticSnapshot, ErrorCode, Error<ErrorCode>> Capture(
        IQuadraticTetradModelView model,
        CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled();
        }
        Result<Success, ErrorCode, Error<ErrorCode>> identity = model.ValidateIdentity();
        if (!identity.IsOk) {
            return identity.Bind<CplexQuadraticSnapshot>(_ => Failure(
                "模型身份无效 / Invalid model identity"));
        }
        try {
            Result<CplexColumn[], ErrorCode, Error<ErrorCode>> columns =
                CplexSnapshotSupport.CaptureColumns(
                    variables: model.Variables,
                    cancellationToken: cancellationToken);
            if (columns is not Ok<CplexColumn[], ErrorCode, Error<ErrorCode>> captured) {
                return columns.Bind<CplexQuadraticSnapshot>(_ => Failure(
                    "无法读取变量定义 / Cannot read variable definitions"));
            }
            if (model.Objective.Category is not ObjectCategory.Minimum and not ObjectCategory.Maximum
                || !double.IsFinite(model.Objective.Constant.ToDouble())) {
                return Failure("目标方向或常数无效 / Invalid objective direction or constant");
            }

            double[] objective = new double[captured.Value.Length];
            var quadraticObjective = new Dictionary<(int First, int Second), double>();
            foreach (QuadraticObjectiveCell cell in model.Objective.Cells) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled();
                }
                Result<Success, ErrorCode, Error<ErrorCode>> valid = CplexSnapshotSupport.ValidateCell(
                    first: cell?.Col1 ?? -1,
                    second: cell?.Col2 ?? -2,
                    coefficient: cell?.Coefficient ?? default,
                    columnCount: objective.Length);
                if (!valid.IsOk) {
                    return Failure("目标单元格无效 / Invalid objective cell");
                }
                double coefficient = cell!.Coefficient.ToDouble();
                if (cell.Col2 < 0) {
                    objective[cell.Col1] += coefficient;
                    if (!double.IsFinite(objective[cell.Col1])) {
                        return Failure("目标系数溢出 / Objective coefficient overflow");
                    }
                    continue;
                }
                (int first, int second) = CplexSnapshotSupport.Normalize(cell.Col1, cell.Col2);
                if (!CplexSnapshotSupport.Add(
                        values: quadraticObjective,
                        key: (first, second),
                        value: coefficient)) {
                    return Failure("二次目标系数溢出 / Quadratic objective coefficient overflow");
                }
            }

            ModelConstraint<QuadraticConstraintCell> constraints = model.Constraints;
            if (constraints.Priorities is not null) {
                return Unsupported(
                    "暂不支持 Cplex 约束优先级 / Cplex constraint priorities are not yet supported");
            }
            var rows = new CplexQuadraticRow[constraints.Size];
            for (int row = 0; row < rows.Length; row++) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled();
                }
                ConstraintRelation relation = constraints.Signs[row];
                double right = constraints.Rhs[row].ToDouble();
                if (!CplexSnapshotSupport.ValidRelation(relation) || !double.IsFinite(right)) {
                    return Failure(
                        "约束关系或右端值无效 / Invalid constraint relation or right-hand side");
                }
                var linear = new Dictionary<int, double>();
                var quadratic = new Dictionary<(int First, int Second), double>();
                foreach (QuadraticConstraintCell cell in constraints.Lhs[row]) {
                    if (cancellationToken.IsCancellationRequested) {
                        return Cancelled();
                    }
                    Result<Success, ErrorCode, Error<ErrorCode>> valid = CplexSnapshotSupport.ValidateCell(
                        first: cell?.Col1 ?? -1,
                        second: cell?.Col2 ?? -2,
                        coefficient: cell?.Coefficient ?? default,
                        columnCount: objective.Length);
                    if (!valid.IsOk || cell!.RowIndex != row) {
                        return Failure("约束单元格无效 / Invalid constraint cell");
                    }
                    double coefficient = cell.Coefficient.ToDouble();
                    if (cell.Col2 < 0) {
                        if (!CplexSnapshotSupport.Add(
                                values: linear,
                                key: cell.Col1,
                                value: coefficient)) {
                            return Failure("约束系数溢出 / Constraint coefficient overflow");
                        }
                        continue;
                    }
                    (int first, int second) = CplexSnapshotSupport.Normalize(cell.Col1, cell.Col2);
                    if (!CplexSnapshotSupport.Add(
                            values: quadratic,
                            key: (first, second),
                            value: coefficient)) {
                        return Failure(
                            "二次约束系数溢出 / Quadratic constraint coefficient overflow");
                    }
                }
                KeyValuePair<int, double>[] linearEntries = linear
                    .Where(entry => entry.Value != 0)
                    .OrderBy(entry => entry.Key)
                    .ToArray();
                CplexQuadraticTerm[] terms = quadratic
                    .Where(entry => entry.Value != 0)
                    .OrderBy(entry => entry.Key.First)
                    .ThenBy(entry => entry.Key.Second)
                    .Select(entry => new CplexQuadraticTerm(
                        Col1: entry.Key.First,
                        Col2: entry.Key.Second,
                        Coefficient: entry.Value))
                    .ToArray();
                rows[row] = new CplexQuadraticRow(
                    LinearColumns: linearEntries.Select(entry => entry.Key).ToArray(),
                    LinearCoefficients: linearEntries.Select(entry => entry.Value).ToArray(),
                    QuadraticTerms: terms,
                    Relation: relation,
                    Right: right);
            }
            CplexQuadraticTerm[] objectiveTerms = quadraticObjective
                .Where(entry => entry.Value != 0)
                .OrderBy(entry => entry.Key.First)
                .ThenBy(entry => entry.Key.Second)
                .Select(entry => new CplexQuadraticTerm(
                    Col1: entry.Key.First,
                    Col2: entry.Key.Second,
                    Coefficient: entry.Value))
                .ToArray();
            return Results.Ok(new CplexQuadraticSnapshot(
                Columns: captured.Value,
                Rows: rows,
                Objective: objective,
                QuadraticObjective: objectiveTerms,
                Constant: model.Objective.Constant.ToDouble(),
                Category: model.Objective.Category));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(
                $"无法读取二次模型：{error.Message} / Cannot read the quadratic model: {error.Message}");
        }
    }

    private static Result<CplexQuadraticSnapshot, ErrorCode, Error<ErrorCode>> Cancelled() =>
        Results.Failed<CplexQuadraticSnapshot>(new Err<ErrorCode>(
            ErrorCode.ApplicationStopped,
            "模型快照已取消 / Model snapshot was cancelled"));

    private static Result<CplexQuadraticSnapshot, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Results.Failed<CplexQuadraticSnapshot>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private static Result<CplexQuadraticSnapshot, ErrorCode, Error<ErrorCode>> Unsupported(string message) =>
        Results.Failed<CplexQuadraticSnapshot>(new Err<ErrorCode>(ErrorCode.Other, message));
}

internal static class CplexSnapshotSupport {
    internal static Result<CplexColumn[], ErrorCode, Error<ErrorCode>> CaptureColumns(
        IReadOnlyList<ModelViewVariable> variables,
        CancellationToken cancellationToken) {
        var columns = new CplexColumn[variables.Count];
        for (int index = 0; index < columns.Length; index++) {
            if (cancellationToken.IsCancellationRequested) {
                return Results.Failed<CplexColumn[]>(new Err<ErrorCode>(
                    ErrorCode.ApplicationStopped,
                    "模型快照已取消 / Model snapshot was cancelled"));
            }
            ModelViewVariable variable = variables[index];
            if (variable is null || variable.Index != index) {
                return Results.Failed<CplexColumn[]>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "变量索引无效 / Invalid variable index"));
            }
            double lower = variable.LowerBound.ToDouble();
            double upper = variable.UpperBound.ToDouble();
            double? initial = variable.InitialResult?.ToDouble();
            if (lower > upper || double.IsNaN(lower) || double.IsNaN(upper)
                || double.IsNaN(initial ?? 0)) {
                return Results.Failed<CplexColumn[]>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "变量边界或初始解无效 / Invalid variable bounds or initial solution"));
            }
            columns[index] = new CplexColumn(
                Lower: lower,
                Upper: upper,
                Type: VariableType(variable),
                Initial: initial);
        }
        return Results.Ok(columns);
    }

    internal static bool ValidRelation(ConstraintRelation relation) =>
        relation == ConstraintRelation.LessEqual
        || relation == ConstraintRelation.Equal
        || relation == ConstraintRelation.GreaterEqual;

    internal static (int First, int Second) Normalize(int first, int second) =>
        first <= second ? (first, second) : (second, first);

    internal static Result<Success, ErrorCode, Error<ErrorCode>> ValidateCell(
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

    internal static bool Add<TKey>(
        IDictionary<TKey, double> values,
        TKey key,
        double value)
        where TKey : notnull {
        values.TryGetValue(key, out double previous);
        double sum = previous + value;
        if (!double.IsFinite(sum)) {
            return false;
        }
        values[key] = sum;
        return true;
    }

    internal static CplexRow Row(
        IReadOnlyDictionary<int, double> linear,
        ConstraintRelation relation,
        double right) {
        KeyValuePair<int, double>[] entries = linear
            .Where(entry => entry.Value != 0)
            .OrderBy(entry => entry.Key)
            .ToArray();
        return new CplexRow(
            Columns: entries.Select(entry => entry.Key).ToArray(),
            Coefficients: entries.Select(entry => entry.Value).ToArray(),
            Relation: relation,
            Right: right);
    }

    private static char VariableType(ModelViewVariable variable) {
        if (variable.Type.IsBinaryType) {
            return 'B';
        }
        return variable.Type.IsIntegerType ? 'I' : 'C';
    }
}
