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

namespace Fuookami.Ospf.Core.Plugin.Hexaly;

internal enum HexalyColumnKind {
    Binary,
    Integer,
    Continuous,
}

internal sealed record HexalyColumn(
    HexalyColumnKind Kind,
    double Lower,
    double Upper,
    long IntegerLower,
    long IntegerUpper,
    double? Initial);

internal sealed record HexalyTerm(
    int First,
    int? Second,
    double Coefficient);

internal sealed record HexalyRow(
    HexalyTerm[] Terms,
    ConstraintRelation Relation,
    double Right);

internal sealed record HexalySnapshot(
    HexalyColumn[] Columns,
    HexalyRow[] Rows,
    HexalyTerm[] Objective,
    double Constant,
    ObjectCategory Category,
    bool IsQuadratic) {
    internal static Result<HexalySnapshot, ErrorCode, Error<ErrorCode>> CaptureLinear(
        ILinearTriadModelView model,
        CancellationToken cancellationToken = default) {
        if (model is null) {
            return Failure("线性模型为空 / Linear model is null");
        }
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled();
        }
        Result<Success, ErrorCode, Error<ErrorCode>> identity = model.ValidateIdentity();
        if (!identity.IsOk) {
            return identity.Bind<HexalySnapshot>(_ =>
                Failure("模型身份无效 / Invalid model identity"));
        }

        try {
            Result<HexalyColumn[], ErrorCode, Error<ErrorCode>> columns =
                CaptureColumns(model.Variables, cancellationToken);
            if (columns is not Ok<HexalyColumn[], ErrorCode, Error<ErrorCode>> columnsOk) {
                return Propagate<HexalySnapshot, HexalyColumn[]>(columns);
            }
            if (model.Objective.Category is not ObjectCategory.Minimum
                and not ObjectCategory.Maximum
                || !double.IsFinite(model.Objective.Constant.ToDouble())) {
                return Failure("目标方向或常数无效 / Invalid objective direction or constant");
            }

            double[] objective = new double[columnsOk.Value.Length];
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

            Result<HexalyRow[], ErrorCode, Error<ErrorCode>> rows = CaptureLinearRows(
                model.Constraints,
                columnsOk.Value.Length,
                cancellationToken);
            if (rows is not Ok<HexalyRow[], ErrorCode, Error<ErrorCode>> rowsOk) {
                return Propagate<HexalySnapshot, HexalyRow[]>(rows);
            }
            return Results.Ok(new HexalySnapshot(
                Columns: columnsOk.Value,
                Rows: rowsOk.Value,
                Objective: objective
                    .Select((coefficient, column) => (coefficient, column))
                    .Where(entry => entry.coefficient != 0)
                    .Select(entry => new HexalyTerm(
                        First: entry.column,
                        Second: null,
                        Coefficient: entry.coefficient))
                    .ToArray(),
                Constant: model.Objective.Constant.ToDouble(),
                Category: model.Objective.Category,
                IsQuadratic: false));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(
                $"无法读取线性模型：{error.Message} / Cannot read the linear model: {error.Message}");
        }
    }

    internal static Result<HexalySnapshot, ErrorCode, Error<ErrorCode>> CaptureQuadratic(
        IQuadraticTetradModelView model,
        CancellationToken cancellationToken = default) {
        if (model is null) {
            return Failure("二次模型为空 / Quadratic model is null");
        }
        if (cancellationToken.IsCancellationRequested) {
            return Cancelled();
        }
        Result<Success, ErrorCode, Error<ErrorCode>> identity = model.ValidateIdentity();
        if (!identity.IsOk) {
            return identity.Bind<HexalySnapshot>(_ =>
                Failure("模型身份无效 / Invalid model identity"));
        }

        try {
            Result<HexalyColumn[], ErrorCode, Error<ErrorCode>> columns =
                CaptureColumns(model.Variables, cancellationToken);
            if (columns is not Ok<HexalyColumn[], ErrorCode, Error<ErrorCode>> columnsOk) {
                return Propagate<HexalySnapshot, HexalyColumn[]>(columns);
            }
            if (model.Objective.Category is not ObjectCategory.Minimum
                and not ObjectCategory.Maximum
                || !double.IsFinite(model.Objective.Constant.ToDouble())) {
                return Failure("目标方向或常数无效 / Invalid objective direction or constant");
            }

            var objective = new Dictionary<(int First, int Second), double>();
            foreach (QuadraticObjectiveCell cell in model.Objective.Cells) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled();
                }
                if (cell is null
                    || cell.Col1 < 0
                    || cell.Col1 >= columnsOk.Value.Length
                    || cell.Col2 < -1
                    || cell.Col2 >= columnsOk.Value.Length
                    || !double.IsFinite(cell.Coefficient.ToDouble())) {
                    return Failure("目标单元格无效 / Invalid objective cell");
                }
                Accumulate(
                    objective,
                    First: cell.Col1,
                    Second: cell.Col2 < 0 ? null : cell.Col2,
                    Coefficient: cell.Coefficient.ToDouble());
            }

            Result<HexalyRow[], ErrorCode, Error<ErrorCode>> rows = CaptureQuadraticRows(
                model.Constraints,
                columnsOk.Value.Length,
                cancellationToken);
            if (rows is not Ok<HexalyRow[], ErrorCode, Error<ErrorCode>> rowsOk) {
                return Propagate<HexalySnapshot, HexalyRow[]>(rows);
            }
            return Results.Ok(new HexalySnapshot(
                Columns: columnsOk.Value,
                Rows: rowsOk.Value,
                Objective: objective
                    .Where(entry => entry.Value != 0)
                    .OrderBy(entry => entry.Key.First)
                    .ThenBy(entry => entry.Key.Second)
                    .Select(entry => new HexalyTerm(
                        First: entry.Key.First,
                        Second: entry.Key.Second < 0 ? null : entry.Key.Second,
                        Coefficient: entry.Value))
                    .ToArray(),
                Constant: model.Objective.Constant.ToDouble(),
                Category: model.Objective.Category,
                IsQuadratic: true));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure(
                $"无法读取二次模型：{error.Message} / Cannot read the quadratic model: {error.Message}");
        }
    }

    private static Result<HexalyColumn[], ErrorCode, Error<ErrorCode>> CaptureColumns(
        IReadOnlyList<ModelViewVariable> variables,
        CancellationToken cancellationToken) {
        var columns = new HexalyColumn[variables.Count];
        for (int column = 0; column < columns.Length; column++) {
            if (cancellationToken.IsCancellationRequested) {
                return Cancelled<HexalyColumn[]>();
            }
            ModelViewVariable? variable = variables[column];
            if (variable is null || variable.Index != column) {
                return Failure<HexalyColumn[]>("变量索引无效 / Invalid variable index");
            }
            double lower = variable.LowerBound.ToDouble();
            double upper = variable.UpperBound.ToDouble();
            double? initial = variable.InitialResult?.ToDouble();
            if (!ValidLowerBound(lower)
                || !ValidUpperBound(upper)
                || lower > upper
                || initial is { } initialValue && !double.IsFinite(initialValue)) {
                return Failure<HexalyColumn[]>(
                    "变量边界或初始解无效 / Invalid variable bounds or initial solution");
            }

            HexalyColumnKind kind = HexalyVariableExtensions.From(variable.Type) switch {
                HexalyVariableType.Binary => HexalyColumnKind.Binary,
                HexalyVariableType.Integer => HexalyColumnKind.Integer,
                _ => HexalyColumnKind.Continuous,
            };
            if (kind == HexalyColumnKind.Binary) {
                if (lower > 0 || upper < 1 || initial is { } binaryInitial
                    && binaryInitial is not 0 and not 1) {
                    return Failure<HexalyColumn[]>(
                        "二进制变量边界或初始解无效 / Invalid binary bounds or initial solution");
                }
                columns[column] = new HexalyColumn(
                    Kind: kind,
                    Lower: 0,
                    Upper: 1,
                    IntegerLower: 0,
                    IntegerUpper: 1,
                    Initial: initial);
                continue;
            }
            if (kind == HexalyColumnKind.Integer) {
                if (!TryIntegerBound(value: lower, isLower: true, out long integerLower)
                    || !TryIntegerBound(value: upper, isLower: false, out long integerUpper)
                    || integerLower > integerUpper
                    || initial is { } integerInitial
                    && (!double.IsFinite(integerInitial)
                        || System.Math.Truncate(integerInitial) != integerInitial
                        || integerInitial < integerLower
                        || integerInitial > integerUpper)) {
                    return Failure<HexalyColumn[]>(
                        "整数变量边界或初始解无效 / Invalid integer bounds or initial solution");
                }
                columns[column] = new HexalyColumn(
                    Kind: kind,
                    Lower: integerLower,
                    Upper: integerUpper,
                    IntegerLower: integerLower,
                    IntegerUpper: integerUpper,
                    Initial: initial);
                continue;
            }
            if (initial is { } continuousInitial
                && (continuousInitial < lower || continuousInitial > upper)) {
                return Failure<HexalyColumn[]>(
                    "连续变量初始解越界 / Continuous initial solution is outside its bounds");
            }
            columns[column] = new HexalyColumn(
                Kind: kind,
                Lower: lower,
                Upper: upper,
                IntegerLower: 0,
                IntegerUpper: 0,
                Initial: initial);
        }
        return Results.Ok(columns);
    }

    private static Result<HexalyRow[], ErrorCode, Error<ErrorCode>> CaptureLinearRows(
        ModelConstraint<LinearConstraintCell> constraints,
        int columnCount,
        CancellationToken cancellationToken) {
        if (constraints.Priorities is not null) {
            return Unsupported<HexalyRow[]>(
                "Hexaly 暂不支持字段约束优先级 / Hexaly constraint priorities are not yet supported");
        }
        var rows = new HexalyRow[constraints.Size];
        for (int row = 0; row < rows.Length; row++) {
            if (cancellationToken.IsCancellationRequested) {
                return Cancelled<HexalyRow[]>();
            }
            ConstraintRelation relation = constraints.Signs[row];
            double right = constraints.Rhs[row].ToDouble();
            if (!ValidRelation(relation) || !double.IsFinite(right)) {
                return Failure<HexalyRow[]>(
                    "约束关系或右端值无效 / Invalid constraint relation or right-hand side");
            }
            var terms = new Dictionary<int, double>();
            foreach (LinearConstraintCell cell in constraints.Lhs[row]) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled<HexalyRow[]>();
                }
                if (cell is null
                    || cell.RowIndex != row
                    || cell.ColIndex < 0
                    || cell.ColIndex >= columnCount
                    || !double.IsFinite(cell.Coefficient.ToDouble())) {
                    return Failure<HexalyRow[]>("约束单元格无效 / Invalid constraint cell");
                }
                terms.TryGetValue(cell.ColIndex, out double previous);
                double sum = previous + cell.Coefficient.ToDouble();
                if (!double.IsFinite(sum)) {
                    return Failure<HexalyRow[]>("约束系数溢出 / Constraint coefficient overflow");
                }
                terms[cell.ColIndex] = sum;
            }
            rows[row] = new HexalyRow(
                Terms: terms
                    .Where(entry => entry.Value != 0)
                    .OrderBy(entry => entry.Key)
                    .Select(entry => new HexalyTerm(
                        First: entry.Key,
                        Second: null,
                        Coefficient: entry.Value))
                    .ToArray(),
                Relation: relation,
                Right: right);
        }
        return Results.Ok(rows);
    }

    private static Result<HexalyRow[], ErrorCode, Error<ErrorCode>> CaptureQuadraticRows(
        ModelConstraint<QuadraticConstraintCell> constraints,
        int columnCount,
        CancellationToken cancellationToken) {
        if (constraints.Priorities is not null) {
            return Unsupported<HexalyRow[]>(
                "Hexaly 暂不支持字段约束优先级 / Hexaly constraint priorities are not yet supported");
        }
        var rows = new HexalyRow[constraints.Size];
        for (int row = 0; row < rows.Length; row++) {
            if (cancellationToken.IsCancellationRequested) {
                return Cancelled<HexalyRow[]>();
            }
            ConstraintRelation relation = constraints.Signs[row];
            double right = constraints.Rhs[row].ToDouble();
            if (!ValidRelation(relation) || !double.IsFinite(right)) {
                return Failure<HexalyRow[]>(
                    "约束关系或右端值无效 / Invalid constraint relation or right-hand side");
            }
            var terms = new Dictionary<(int First, int Second), double>();
            foreach (QuadraticConstraintCell cell in constraints.Lhs[row]) {
                if (cancellationToken.IsCancellationRequested) {
                    return Cancelled<HexalyRow[]>();
                }
                if (cell is null
                    || cell.RowIndex != row
                    || cell.Col1 < 0
                    || cell.Col1 >= columnCount
                    || cell.Col2 < -1
                    || cell.Col2 >= columnCount
                    || !double.IsFinite(cell.Coefficient.ToDouble())) {
                    return Failure<HexalyRow[]>("约束单元格无效 / Invalid constraint cell");
                }
                Accumulate(
                    terms,
                    First: cell.Col1,
                    Second: cell.Col2 < 0 ? null : cell.Col2,
                    Coefficient: cell.Coefficient.ToDouble());
            }
            rows[row] = new HexalyRow(
                Terms: terms
                    .Where(entry => entry.Value != 0)
                    .OrderBy(entry => entry.Key.First)
                    .ThenBy(entry => entry.Key.Second)
                    .Select(entry => new HexalyTerm(
                        First: entry.Key.First,
                        Second: entry.Key.Second < 0 ? null : entry.Key.Second,
                        Coefficient: entry.Value))
                    .ToArray(),
                Relation: relation,
                Right: right);
        }
        return Results.Ok(rows);
    }

    private static void Accumulate(
        Dictionary<(int First, int Second), double> terms,
        int First,
        int? Second,
        double Coefficient) {
        int second = Second ?? -1;
        if (second >= 0 && First > second) {
            (First, second) = (second, First);
        }
        (int First, int Second) key = (First, second);
        terms.TryGetValue(key, out double previous);
        double sum = previous + Coefficient;
        if (!double.IsFinite(sum)) {
            throw new OverflowException("Coefficient overflow");
        }
        terms[key] = sum;
    }

    private static bool TryIntegerBound(double value, bool isLower, out long bound) {
        if (double.IsPositiveInfinity(value)) {
            bound = isLower ? 0 : long.MaxValue;
            return !isLower;
        }
        if (double.IsNegativeInfinity(value)) {
            bound = isLower ? long.MinValue : 0;
            return isLower;
        }
        double rounded = isLower ? System.Math.Ceiling(value) : System.Math.Floor(value);
        if (!double.IsFinite(rounded)
            || rounded < long.MinValue
            || rounded > long.MaxValue) {
            bound = 0;
            return false;
        }
        bound = (long)rounded;
        return true;
    }

    private static bool ValidLowerBound(double value) =>
        double.IsFinite(value) || double.IsNegativeInfinity(value);

    private static bool ValidUpperBound(double value) =>
        double.IsFinite(value) || double.IsPositiveInfinity(value);

    private static bool ValidRelation(ConstraintRelation relation) =>
        relation == ConstraintRelation.LessEqual
        || relation == ConstraintRelation.Equal
        || relation == ConstraintRelation.GreaterEqual;

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Failure<T>("模型快照结果无效 / Invalid model snapshot result"),
        };

    private static Result<HexalySnapshot, ErrorCode, Error<ErrorCode>> Cancelled() =>
        Cancelled<HexalySnapshot>();

    private static Result<T, ErrorCode, Error<ErrorCode>> Cancelled<T>() =>
        Results.Failed<T>(new Err<ErrorCode>(
            ErrorCode.ApplicationStopped,
            "模型快照已取消 / Model snapshot was cancelled"));

    private static Result<HexalySnapshot, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Failure<HexalySnapshot>(message);

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private static Result<T, ErrorCode, Error<ErrorCode>> Unsupported<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.Other, message));
}
