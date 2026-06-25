#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Model.Intermediate
{
    // ===== Sparse Vector =====

    /// <summary>稀疏向量条目 / Sparse vector entry</summary>
    /// <param name="Index">列索引 / Column index</param>
    /// <param name="Value">值 / Value</param>
    public sealed record SparseVectorEntry(int Index, Flt64 Value);

    /// <summary>稀疏向量（有序条目列表）/ Sparse vector (ordered entry list)</summary>
    public sealed class SparseVector
    {
        /// <summary>条目列表（按索引排序）/ Entry list (sorted by index)</summary>
        public List<SparseVectorEntry> Entries { get; }

        public SparseVector(List<SparseVectorEntry>? entries = null)
        {
            Entries = entries ?? new List<SparseVectorEntry>();
        }

        /// <summary>条目数量 / Entry count</summary>
        public int Count => Entries.Count;

        /// <summary>添加条目 / Add entry</summary>
        public void Add(int index, Flt64 value)
        {
            if (value != Flt64.Zero)
            {
                Entries.Add(new SparseVectorEntry(index, value));
            }
        }

        /// <summary>创建取反副本 / Create negated copy</summary>
        public SparseVector Negate() =>
            new(Entries.Select(e => new SparseVectorEntry(e.Index, -e.Value)).ToList());

        /// <summary>就地标量乘法 / In-place scalar multiplication</summary>
        public SparseVector ScaleInPlace(Flt64 factor)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                Entries[i] = new SparseVectorEntry(Entries[i].Index, Entries[i].Value * factor);
            }
            return this;
        }

        /// <inheritdoc/>
        public override string ToString() => $"SparseVector[{Count}]";
    }

    // ===== Sparse Matrix =====

    /// <summary>稀疏矩阵（行列表）/ Sparse matrix (row list)</summary>
    public sealed class SparseMatrix
    {
        /// <summary>行向量列表 / Row vector list</summary>
        public List<SparseVector> Rows { get; }

        public SparseMatrix(List<SparseVector>? rows = null)
        {
            Rows = rows ?? new List<SparseVector>();
        }

        /// <summary>行数 / Row count</summary>
        public int RowCount => Rows.Count;

        /// <summary>添加行 / Add row</summary>
        public void AddRow(SparseVector row) => Rows.Add(row);

        /// <summary>创建取反副本 / Create negated copy</summary>
        public SparseMatrix Negate() =>
            new(Rows.Select(r => r.Negate()).ToList());

        /// <summary>就地标量乘法 / In-place scalar multiplication</summary>
        public SparseMatrix ScaleInPlace(Flt64 factor)
        {
            foreach (var row in Rows)
            {
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
    public sealed class SparseQuadraticVector
    {
        /// <summary>条目列表 / Entry list</summary>
        public List<SparseQuadraticEntry> Entries { get; }

        public SparseQuadraticVector(List<SparseQuadraticEntry>? entries = null)
        {
            Entries = entries ?? new List<SparseQuadraticEntry>();
        }

        /// <summary>条目数量 / Entry count</summary>
        public int Count => Entries.Count;

        /// <summary>添加条目 / Add entry</summary>
        public void Add(int col1, int col2, Flt64 value)
        {
            if (value != Flt64.Zero)
            {
                Entries.Add(new SparseQuadraticEntry(col1, col2, value));
            }
        }

        /// <summary>创建取反副本 / Create negated copy</summary>
        public SparseQuadraticVector Negate() =>
            new(Entries.Select(e => new SparseQuadraticEntry(e.Col1, e.Col2, -e.Value)).ToList());

        /// <summary>就地标量乘法 / In-place scalar multiplication</summary>
        public SparseQuadraticVector ScaleInPlace(Flt64 factor)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                Entries[i] = new SparseQuadraticEntry(Entries[i].Col1, Entries[i].Col2, Entries[i].Value * factor);
            }
            return this;
        }

        /// <inheritdoc/>
        public override string ToString() => $"SparseQuadraticVector[{Count}]";
    }

    /// <summary>稀疏二次矩阵 / Sparse quadratic matrix</summary>
    public sealed class SparseQuadraticMatrix
    {
        /// <summary>行向量列表 / Row vector list</summary>
        public List<SparseQuadraticVector> Rows { get; }

        public SparseQuadraticMatrix(List<SparseQuadraticVector>? rows = null)
        {
            Rows = rows ?? new List<SparseQuadraticVector>();
        }

        /// <summary>行数 / Row count</summary>
        public int RowCount => Rows.Count;

        /// <summary>添加行 / Add row</summary>
        public void AddRow(SparseQuadraticVector row) => Rows.Add(row);

        /// <summary>创建取反副本 / Create negated copy</summary>
        public SparseQuadraticMatrix Negate() =>
            new(Rows.Select(r => r.Negate()).ToList());

        /// <summary>就地标量乘法 / In-place scalar multiplication</summary>
        public SparseQuadraticMatrix ScaleInPlace(Flt64 factor)
        {
            foreach (var row in Rows)
            {
                row.ScaleInPlace(factor);
            }
            return this;
        }

        /// <inheritdoc/>
        public override string ToString() => $"SparseQuadraticMatrix[{RowCount}]";
    }

    // ===== Extension methods =====

    /// <summary>稀疏结构扩展方法 / Sparse structure extension methods</summary>
    public static class SparseExtensions
    {
        /// <summary>取反（非破坏性）/ Negate (non-destructive)</summary>
        public static SparseVector Negated(this SparseVector v) => v.Negate();

        /// <summary>取反（非破坏性）/ Negate (non-destructive)</summary>
        public static SparseMatrix Negated(this SparseMatrix m) => m.Negate();

        /// <summary>取反（非破坏性）/ Negate (non-destructive)</summary>
        public static SparseQuadraticVector Negated(this SparseQuadraticVector v) => v.Negate();

        /// <summary>取反（非破坏性）/ Negate (non-destructive)</summary>
        public static SparseQuadraticMatrix Negated(this SparseQuadraticMatrix m) => m.Negate();
    }
}
