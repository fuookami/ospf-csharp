#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
namespace Fuookami.Ospf.Core.Model.Intermediate;
// ===== Sparse Vector =====

/// <summary>稀疏向量条目 / Sparse vector entry</summary>
/// <param name="Index">列索引 / Column index</param>
/// <param name="Value">值 / Value</param>
public sealed record SparseVectorEntry(int Index, Flt64 Value);

/// <summary>稀疏向量（有序条目列表）/ Sparse vector (ordered entry list)</summary>
public sealed class SparseVector {
    private readonly RevisionList<SparseVectorEntry> _entries;

    /// <summary>条目列表（按索引排序）/ Entry list (sorted by index)</summary>
    public IList<SparseVectorEntry> Entries => _entries;
    internal object Revision => _entries.Revision;

    /// <summary>复制传入的条目列表 / Copy the supplied entry list.</summary>
    /// <param name="entries">初始条目 / Initial entries.</param>
    public SparseVector(IEnumerable<SparseVectorEntry>? entries = null) {
        _entries = new RevisionList<SparseVectorEntry>(entries);
    }

    /// <summary>条目数量 / Entry count</summary>
    public int Count => Entries.Count;

    /// <summary>添加条目 / Add entry</summary>
    public void Add(int index, Flt64 value) => Entries.Add(new SparseVectorEntry(index, value));

    /// <summary>创建取反副本 / Create negated copy</summary>
    public SparseVector Negate() =>
        new(Entries.Select(e => new SparseVectorEntry(e.Index, -e.Value)).ToList());

    /// <summary>就地标量乘法 / In-place scalar multiplication</summary>
    public SparseVector ScaleInPlace(Flt64 factor) {
        for (int i = 0; i < Entries.Count; i++) {
            Entries[i] = new SparseVectorEntry(Entries[i].Index, Entries[i].Value * factor);
        }
        return this;
    }

    /// <inheritdoc/>
    public override string ToString() => $"SparseVector[{Count}]";
}

// ===== Sparse Matrix =====

/// <summary>稀疏矩阵（行列表）/ Sparse matrix (row list)</summary>
public sealed class SparseMatrix {
    private readonly RevisionList<SparseVector> _rows;

    /// <summary>行向量列表 / Row vector list</summary>
    public IList<SparseVector> Rows => _rows;
    internal object Revision => _rows.Revision;

    /// <summary>复制行列表，保留向量引用 / Copy the row list, retaining vector references.</summary>
    /// <param name="rows">初始行 / Initial rows.</param>
    public SparseMatrix(IEnumerable<SparseVector>? rows = null) {
        _rows = new RevisionList<SparseVector>(rows);
    }

    /// <summary>行数 / Row count</summary>
    public int RowCount => Rows.Count;

    /// <summary>添加行 / Add row</summary>
    public void AddRow(SparseVector row) => Rows.Add(row);

    /// <summary>创建取反副本 / Create negated copy</summary>
    public SparseMatrix Negate() =>
        new(Rows.Select(r => r.Negate()).ToList());

    /// <summary>就地标量乘法 / In-place scalar multiplication</summary>
    public SparseMatrix ScaleInPlace(Flt64 factor) {
        foreach (SparseVector row in Rows) {
            row.ScaleInPlace(factor);
        }
        return this;
    }

    /// <inheritdoc/>
    public override string ToString() => $"SparseMatrix[{RowCount}]";
}

// ===== Sparse Quadratic =====

/// <summary>稀疏二次条目 / Sparse quadratic entry</summary>
/// <param name="Col1">第一个列索引 / First column index</param>
/// <param name="Col2">第二个列索引 / Second column index</param>
/// <param name="Value">值 / Value</param>
public sealed record SparseQuadraticEntry(int Col1, int Col2, Flt64 Value);

/// <summary>稀疏二次向量 / Sparse quadratic vector</summary>
public sealed class SparseQuadraticVector {
    private readonly RevisionList<SparseQuadraticEntry> _entries;

    /// <summary>条目列表 / Entry list</summary>
    public IList<SparseQuadraticEntry> Entries => _entries;
    internal object Revision => _entries.Revision;

    /// <summary>复制传入的条目列表 / Copy the supplied entry list.</summary>
    /// <param name="entries">初始条目 / Initial entries.</param>
    public SparseQuadraticVector(IEnumerable<SparseQuadraticEntry>? entries = null) {
        _entries = new RevisionList<SparseQuadraticEntry>(entries);
    }

    /// <summary>条目数量 / Entry count</summary>
    public int Count => Entries.Count;

    /// <summary>添加条目 / Add entry</summary>
    public void Add(int col1, int col2, Flt64 value) =>
        Entries.Add(new SparseQuadraticEntry(
            Col1: col1,
            Col2: col2,
            Value: value
        ));

    /// <summary>创建取反副本 / Create negated copy</summary>
    public SparseQuadraticVector Negate() =>
        new(Entries.Select(e => new SparseQuadraticEntry(e.Col1, e.Col2, -e.Value)).ToList());

    /// <summary>就地标量乘法 / In-place scalar multiplication</summary>
    public SparseQuadraticVector ScaleInPlace(Flt64 factor) {
        for (int i = 0; i < Entries.Count; i++) {
            Entries[i] = new SparseQuadraticEntry(Entries[i].Col1, Entries[i].Col2, Entries[i].Value * factor);
        }
        return this;
    }

    /// <inheritdoc/>
    public override string ToString() => $"SparseQuadraticVector[{Count}]";
}

/// <summary>稀疏二次矩阵 / Sparse quadratic matrix</summary>
public sealed class SparseQuadraticMatrix {
    private readonly RevisionList<SparseQuadraticVector> _rows;

    /// <summary>行向量列表 / Row vector list</summary>
    public IList<SparseQuadraticVector> Rows => _rows;
    internal object Revision => _rows.Revision;

    /// <summary>复制行列表，保留向量引用 / Copy the row list, retaining vector references.</summary>
    /// <param name="rows">初始行 / Initial rows.</param>
    public SparseQuadraticMatrix(IEnumerable<SparseQuadraticVector>? rows = null) {
        _rows = new RevisionList<SparseQuadraticVector>(rows);
    }

    /// <summary>行数 / Row count</summary>
    public int RowCount => Rows.Count;

    /// <summary>添加行 / Add row</summary>
    public void AddRow(SparseQuadraticVector row) => Rows.Add(row);

    /// <summary>创建取反副本 / Create negated copy</summary>
    public SparseQuadraticMatrix Negate() =>
        new(Rows.Select(r => r.Negate()).ToList());

    /// <summary>就地标量乘法 / In-place scalar multiplication</summary>
    public SparseQuadraticMatrix ScaleInPlace(Flt64 factor) {
        foreach (SparseQuadraticVector row in Rows) {
            row.ScaleInPlace(factor);
        }
        return this;
    }

    /// <inheritdoc/>
    public override string ToString() => $"SparseQuadraticMatrix[{RowCount}]";
}

internal sealed class RevisionList<T> : Collection<T> {
    internal object Revision { get; private set; } = new();

    internal RevisionList(IEnumerable<T>? items) : base(items is null ? new List<T>() : new List<T>(items)) {
    }

    protected override void InsertItem(int index, T item) {
        object revision = new();
        base.InsertItem(index, item);
        Revision = revision;
    }

    protected override void SetItem(int index, T item) {
        object revision = new();
        base.SetItem(index, item);
        Revision = revision;
    }

    protected override void RemoveItem(int index) {
        object revision = new();
        base.RemoveItem(index);
        Revision = revision;
    }

    protected override void ClearItems() {
        object revision = new();
        base.ClearItems();
        Revision = revision;
    }
}

// ===== Extension methods =====

/// <summary>稀疏结构扩展方法 / Sparse structure extension methods</summary>
public static class SparseExtensions {
    /// <summary>取反（非破坏性）/ Negate (non-destructive)</summary>
    public static SparseVector Negated(this SparseVector v) => v.Negate();

    /// <summary>取反（非破坏性）/ Negate (non-destructive)</summary>
    public static SparseMatrix Negated(this SparseMatrix m) => m.Negate();

    /// <summary>取反（非破坏性）/ Negate (non-destructive)</summary>
    public static SparseQuadraticVector Negated(this SparseQuadraticVector v) => v.Negate();

    /// <summary>取反（非破坏性）/ Negate (non-destructive)</summary>
    public static SparseQuadraticMatrix Negated(this SparseQuadraticMatrix m) => m.Negate();
}
