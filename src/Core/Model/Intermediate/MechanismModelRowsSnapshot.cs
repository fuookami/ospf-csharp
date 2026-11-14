#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Mechanism;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Model.Intermediate;

// shortcut: 无法识别修改后还原；支持单调结构版本后替换 / Restored changes are invisible; replace with monotonic structural revisions.
internal sealed class MechanismModelRowsSnapshot<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly WeakReference<object>[] _rows;
    private readonly CellSnapshot[][] _cells;

    private MechanismModelRowsSnapshot(WeakReference<object>[] rows, CellSnapshot[][] cells) {
        _rows = rows;
        _cells = cells;
    }

    internal static Result<MechanismModelRowsSnapshot<V>, ErrorCode, Error<ErrorCode>> Capture<P>(
        IReadOnlyList<IConstraint<V, P>> rows) {
        try {
            return Results.Ok(new MechanismModelRowsSnapshot<V>(
                rows.Select(row => new WeakReference<object>(row)).ToArray(),
                rows.Select(row => row.Lhs.Select(cell => new CellSnapshot(cell)).ToArray()).ToArray()
            ));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Results.Failed<MechanismModelRowsSnapshot<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"无法读取机制约束：{error.Message} / Cannot inspect mechanism constraints: {error.Message}"));
        }
    }

    internal Try Validate<P>(IReadOnlyList<IConstraint<V, P>> rows) {
        try {
            if (rows.Count == _rows.Length) {
                bool matches = true;
                for (int i = 0; i < rows.Count && matches; i++) {
                    matches = _rows[i].TryGetTarget(out object? row) && ReferenceEquals(row, rows[i])
                        && rows[i].Lhs.Count == _cells[i].Length;
                    for (int j = 0; j < _cells[i].Length && matches; j++) {
                        matches = _cells[i][j].Matches(rows[i].Lhs[j]);
                    }
                }
                if (matches) {
                    return Results.OkInstance;
                }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"无法检查机制约束：{error.Message} / Cannot inspect mechanism constraints: {error.Message}"));
        }
        return Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "机制约束已变更，请重新转储和求解 / Mechanism constraints changed; dump and solve again"));
    }

    private sealed class CellSnapshot {
        private readonly WeakReference<object> _cell;
        private readonly int _kind;
        private readonly V _coefficient;
        private readonly object? _first;
        private readonly object? _second;

        internal CellSnapshot(ICell<V> cell) {
            _cell = new WeakReference<object>(cell);
            if (cell is ILinearCell<V> linear) {
                _kind = 1;
                _coefficient = linear.Coefficient;
                _first = linear.Token.Identity;
            }
            else if (cell is IQuadraticCell<V> quadratic) {
                _kind = 2;
                _coefficient = quadratic.Coefficient;
                _first = quadratic.Token1.Identity;
                _second = quadratic.Token2?.Identity;
            }
        }

        internal bool Matches(ICell<V> cell) =>
            _cell.TryGetTarget(out object? expected) && ReferenceEquals(expected, cell) && (_kind switch {
                1 when cell is ILinearCell<V> linear =>
                    Same(_coefficient, linear.Coefficient) && ReferenceEquals(_first, linear.Token.Identity),
                2 when cell is IQuadraticCell<V> quadratic =>
                    Same(_coefficient, quadratic.Coefficient) && ReferenceEquals(_first, quadratic.Token1.Identity)
                        && ReferenceEquals(_second, quadratic.Token2?.Identity),
                0 => true,
                _ => false,
            });
    }

    private static bool Same(V first, V second) => typeof(V) == typeof(Flt64)
        ? BitConverter.DoubleToInt64Bits(((Flt64)(object)first).ToDouble()) ==
            BitConverter.DoubleToInt64Bits(((Flt64)(object)second).ToDouble())
        : StructuralValueEquality<V>.Same(first, second);
}
