#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
namespace Fuookami.Ospf.Core.Model.Intermediate;

// shortcut: 非稀疏字段修改后还原不可见；支持完整矩阵版本后替换 / Restored non-sparse changes are invisible; replace with complete matrix revisions.
internal sealed class MechanismMatrixSnapshot {
    private readonly ConstraintRelation[] _signs;
    private readonly long[] _rhs;
    private readonly string[] _names;
    private readonly ConstraintSource[] _sources;
    private readonly (int, int, int, long)[][] _lhs;
    private readonly (int, int, int, long)[][] _sparse;
    private readonly object _sparseRevision;
    private readonly object[] _sparseRowRevisions;
    private readonly ObjectCategory _category;
    private readonly long _constant;
    private readonly (int, int, long)[] _objective;

    private MechanismMatrixSnapshot(
        ConstraintRelation[] signs,
        long[] rhs,
        string[] names,
        ConstraintSource[] sources,
        (int, int, int, long)[][] lhs,
        (int, int, int, long)[][] sparse,
        object sparseRevision,
        object[] sparseRowRevisions,
        ObjectCategory category,
        long constant,
        (int, int, long)[] cells) {
        _signs = signs;
        _rhs = rhs;
        _names = names;
        _sources = sources;
        _lhs = lhs;
        _sparse = sparse;
        _sparseRevision = sparseRevision;
        _sparseRowRevisions = sparseRowRevisions;
        _category = category;
        _constant = constant;
        _objective = cells;
    }

    internal static MechanismMatrixSnapshot Capture(LinearConstraintBatch constraints, Objective<LinearObjectiveCell> objective) =>
        Capture(
            constraints: constraints,
            objective: objective,
            lhs: LinearLhs(constraints),
            sparse: LinearSparse(constraints),
            sparseRevision: constraints.SparseLhs.Revision,
            sparseRowRevisions: constraints.SparseLhs.Rows.Select(row => row.Revision).ToArray(),
            cells: LinearObjective(objective)
        );

    internal static MechanismMatrixSnapshot Capture(QuadraticConstraintBatch constraints, Objective<QuadraticObjectiveCell> objective) =>
        Capture(
            constraints: constraints,
            objective: objective,
            lhs: QuadraticLhs(constraints),
            sparse: QuadraticSparse(constraints),
            sparseRevision: constraints.SparseLhs.Revision,
            sparseRowRevisions: constraints.SparseLhs.Rows.Select(row => row.Revision).ToArray(),
            cells: QuadraticObjective(objective)
        );

    private static MechanismMatrixSnapshot Capture<C, O>(
        ModelConstraint<C> constraints,
        Objective<O> objective,
        (int, int, int, long)[][] lhs,
        (int, int, int, long)[][] sparse,
        object sparseRevision,
        object[] sparseRowRevisions,
        (int, int, long)[] cells)
        where C : IConstraintCell<C>, ICopyable<C>
        where O : ICopyable<O> => new(
            signs: constraints.Signs.ToArray(),
            rhs: constraints.Rhs.Select(Bits).ToArray(),
            names: constraints.Names.ToArray(),
            sources: constraints.Sources.ToArray(),
            lhs: lhs,
            sparse: sparse,
            sparseRevision: sparseRevision,
            sparseRowRevisions: sparseRowRevisions,
            category: objective.Category,
            constant: Bits(objective.Constant),
            cells: cells
        );

    internal bool Matches(LinearTriadModel model) => !model.IsDisposed
        && model.Constraints is LinearConstraintBatch constraints
        && Matches(
            constraints: constraints,
            objective: model.Objective
        )
        && SameRows(
            rows: constraints.Lhs,
            expected: _lhs,
            project: cell => (cell.RowIndex, cell.ColIndex, -1, Bits(cell.Coefficient))
        )
        && ReferenceEquals(_sparseRevision, constraints.SparseLhs.Revision)
        && constraints.SparseLhs.Rows.Select(row => row.Revision).SequenceEqual(_sparseRowRevisions)
        && SameRows(
            rows: constraints.SparseLhs.Rows.Select(row => row.Entries),
            expected: _sparse,
            project: cell => (0, cell.Index, -1, Bits(cell.Value))
        )
        && SameObjective(model.Objective.Cells, cell => (cell.ColIndex, -1, Bits(cell.Coefficient)));

    internal bool Matches(QuadraticTetradModel model) => !model.IsDisposed
        && model.Constraints is QuadraticConstraintBatch constraints
        && Matches(
            constraints: constraints,
            objective: model.Objective
        )
        && SameRows(
            rows: constraints.Lhs,
            expected: _lhs,
            project: cell => (cell.RowIndex, cell.Col1, cell.Col2, Bits(cell.Coefficient))
        )
        && ReferenceEquals(_sparseRevision, constraints.SparseLhs.Revision)
        && constraints.SparseLhs.Rows.Select(row => row.Revision).SequenceEqual(_sparseRowRevisions)
        && SameRows(
            rows: constraints.SparseLhs.Rows.Select(row => row.Entries),
            expected: _sparse,
            project: cell => (0, cell.Col1, cell.Col2, Bits(cell.Value))
        )
        && SameObjective(model.Objective.Cells, cell => (cell.Col1, cell.Col2, Bits(cell.Coefficient)));

    private bool Matches<C, O>(
        ModelConstraint<C> constraints,
        Objective<O> objective)
        where C : IConstraintCell<C>, ICopyable<C>
        where O : ICopyable<O> =>
        !constraints.IsDisposed
        && constraints.Signs.SequenceEqual(_signs)
        && constraints.Rhs.Select(Bits).SequenceEqual(_rhs)
        && constraints.Names.SequenceEqual(_names)
        && constraints.Sources.SequenceEqual(_sources)
        && objective.Category == _category && Bits(objective.Constant) == _constant;

    private static bool SameRows<T>(
        IEnumerable<IEnumerable<T>> rows,
        (int, int, int, long)[][] expected,
        Func<T, (int, int, int, long)> project) {
        int rowIndex = 0;
        foreach (IEnumerable<T> row in rows) {
            if (rowIndex >= expected.Length) {
                return false;
            }
            int columnIndex = 0;
            foreach (T cell in row) {
                if (columnIndex >= expected[rowIndex].Length || project(cell) != expected[rowIndex][columnIndex++]) {
                    return false;
                }
            }
            if (columnIndex != expected[rowIndex++].Length) {
                return false;
            }
        }
        return rowIndex == expected.Length;
    }

    private bool SameObjective<T>(IReadOnlyList<T> cells, Func<T, (int, int, long)> project) {
        if (cells.Count != _objective.Length) {
            return false;
        }
        for (int i = 0; i < cells.Count; i++) {
            if (project(cells[i]) != _objective[i]) {
                return false;
            }
        }
        return true;
    }

    private static (int, int, int, long)[][] LinearLhs(LinearConstraintBatch constraints) =>
        constraints.Lhs.Select(row => row.Select(cell => (cell.RowIndex, cell.ColIndex, -1, Bits(cell.Coefficient))).ToArray()).ToArray();

    private static (int, int, int, long)[][] LinearSparse(LinearConstraintBatch constraints) =>
        constraints.SparseLhs.Rows.Select(row => row.Entries.Select(cell => (0, cell.Index, -1, Bits(cell.Value))).ToArray()).ToArray();

    private static (int, int, int, long)[][] QuadraticLhs(QuadraticConstraintBatch constraints) =>
        constraints.Lhs.Select(row => row.Select(cell => (cell.RowIndex, cell.Col1, cell.Col2, Bits(cell.Coefficient))).ToArray()).ToArray();

    private static (int, int, int, long)[][] QuadraticSparse(QuadraticConstraintBatch constraints) =>
        constraints.SparseLhs.Rows.Select(row => row.Entries.Select(cell => (0, cell.Col1, cell.Col2, Bits(cell.Value))).ToArray()).ToArray();

    private static (int, int, long)[] LinearObjective(Objective<LinearObjectiveCell> objective) =>
        objective.Cells.Select(cell => (cell.ColIndex, -1, Bits(cell.Coefficient))).ToArray();

    private static (int, int, long)[] QuadraticObjective(Objective<QuadraticObjectiveCell> objective) =>
        objective.Cells.Select(cell => (cell.Col1, cell.Col2, Bits(cell.Coefficient))).ToArray();

    private static long Bits(Flt64 value) => BitConverter.DoubleToInt64Bits(value.ToDouble());
}
