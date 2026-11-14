#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
namespace Fuookami.Ospf.Core.Model.Intermediate;

internal sealed class SparseConstraintRows<TRow, TCell> : IReadOnlyList<IReadOnlyList<TCell>> {
    private readonly IList<TRow> _rows;
    private readonly Func<TRow, int, IReadOnlyList<TCell>> _project;

    internal SparseConstraintRows(IList<TRow> rows, Func<TRow, int, IReadOnlyList<TCell>> project) {
        _rows = rows;
        _project = project;
    }

    public int Count => _rows.Count;
    public IReadOnlyList<TCell> this[int index] => _project(_rows[index], index);

    public IEnumerator<IReadOnlyList<TCell>> GetEnumerator() {
        for (int i = 0; i < _rows.Count; i++) {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
