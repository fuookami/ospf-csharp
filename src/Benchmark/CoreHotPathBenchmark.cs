#nullable enable

using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Benchmark;
/// <summary>
/// core 热点路径基准 / Core hot path benchmark.
/// <para>对应 P19 flatten merge 单趟累积热点。</para>
/// <para>Maps to P19 flatten merge one-pass accumulation hot path.</para>
/// </summary>
[MemoryDiagnoser]
public class CoreHotPathBenchmark {
    /// <summary>数据集大小 / Dataset size.</summary>
    [Params("small", "medium", "large")]
    public string Dataset { get; set; } = "small";

    private List<LinearMonomial<Flt64>> _linearMonomials = null!;
    private List<QuadraticMonomial<Flt64>> _quadraticMonomials = null!;
    private SparseMatrix _sparseMatrix = null!;
    private int _block;

    /// <summary>初始化基准数据 / Setup benchmark data.</summary>
    [GlobalSetup]
    public void Setup() {
        int variableCount = Dataset switch {
            "small" => 64,
            "medium" => 256,
            "large" => 768,
            _ => 64
        };
        _block = Dataset switch {
            "small" => 16,
            "medium" => 32,
            "large" => 64,
            _ => 16
        };

        var symbols = new BenchSymbol[variableCount];
        for (int i = 0; i < variableCount; i++) {
            symbols[i] = new BenchSymbol($"b{i}");
        }

        // 线性单项式 / Linear monomials
        _linearMonomials = new List<LinearMonomial<Flt64>>(_block * variableCount);
        for (int bi = 0; bi < _block; bi++) {
            for (int i = 0; i < symbols.Length; i++) {
                _linearMonomials.Add(new LinearMonomial<Flt64>(
                    new Flt64((double)((i + bi) % 5 + 1)),
                    symbols[i]));
            }
        }

        // 二次单项式 / Quadratic monomials
        _quadraticMonomials = new List<QuadraticMonomial<Flt64>>(_block * variableCount);
        for (int bi = 0; bi < _block; bi++) {
            for (int i = 0; i < symbols.Length; i++) {
                BenchSymbol left = symbols[i];
                BenchSymbol right = symbols[(i * 13 + bi) % symbols.Length];
                _quadraticMonomials.Add(new QuadraticMonomial<Flt64>(
                    new Flt64((double)((i % 3) + 1)),
                    left,
                    right));
            }
        }

        // 稀疏矩阵 / Sparse matrix
        int rows = Dataset switch {
            "small" => 128,
            "medium" => 512,
            "large" => 1536,
            _ => 128
        };
        int cols = Dataset switch {
            "small" => 256,
            "medium" => 1024,
            "large" => 3072,
            _ => 256
        };
        _sparseMatrix = new SparseMatrix();
        for (int r = 0; r < rows; r++) {
            var row = new SparseVector();
            int c = r % 5;
            while (c < cols) {
                row.Add(c, new Flt64((double)((c + r) % 11 + 1)));
                c += 17;
            }
            _sparseMatrix.AddRow(row);
        }
    }

    /// <summary>
    /// 对应 P19 linear flatten merge 聚合热点。
    /// Maps to the P19 linear flatten merge aggregation hot path.
    /// </summary>
    [Benchmark]
    public int MergeLinearFlattenDataLikeP19() {
        var merged = new Dictionary<ISymbol, Flt64>();
        foreach (LinearMonomial<Flt64> monomial in _linearMonomials) {
            ISymbol key = monomial.Symbol;
            if (merged.TryGetValue(key, out Flt64 existing)) {
                merged[key] = existing + monomial.Coefficient;
            }
            else {
                merged[key] = monomial.Coefficient;
            }
        }
        return merged.Count;
    }

    /// <summary>
    /// 对应 P19 quadratic flatten merge 聚合热点。
    /// Maps to the P19 quadratic flatten merge aggregation hot path.
    /// </summary>
    [Benchmark]
    public int MergeQuadraticFlattenDataLikeP19() {
        var merged = new Dictionary<(ISymbol, ISymbol?), Flt64>();
        foreach (QuadraticMonomial<Flt64> monomial in _quadraticMonomials) {
            ISymbol s1 = monomial.Symbol1;
            ISymbol? s2 = monomial.Symbol2;
            (ISymbol, ISymbol?) key = s2 == null || CompareSymbolKey(s1, s2) <= 0
                ? (s1, s2)
                : (s2, s1);
            if (merged.TryGetValue(key, out Flt64 existing)) {
                merged[key] = existing + monomial.Coefficient;
            }
            else {
                merged[key] = monomial.Coefficient;
            }
        }
        return merged.Count;
    }

    /// <summary>
    /// 对应 P19 SparseMatrix.transpose 单趟转置热点。
    /// Maps to the P19 SparseMatrix transpose one-pass hot path.
    /// </summary>
    [Benchmark]
    public int SparseMatrixTranspose() {
        int maxCol = 0;
        foreach (SparseVector row in _sparseMatrix.Rows) {
            foreach (SparseVectorEntry entry in row.Entries) {
                if (entry.Index > maxCol) {
                    maxCol = entry.Index;
                }
            }
        }
        var transposedRows = new List<SparseVector>(maxCol + 1);
        for (int c = 0; c <= maxCol; c++) {
            transposedRows.Add(new SparseVector());
        }
        for (int r = 0; r < _sparseMatrix.RowCount; r++) {
            foreach (SparseVectorEntry entry in _sparseMatrix.Rows[r].Entries) {
                transposedRows[entry.Index].Add(r, entry.Value);
            }
        }
        return transposedRows.Count;
    }

    private static int CompareSymbolKey(ISymbol lhs, ISymbol rhs) => string.Compare(lhs.Name, rhs.Name, StringComparison.Ordinal);

    /// <summary>
    /// 基准测试用符号实现 / Symbol implementation for benchmarking.
    /// </summary>
    private sealed record BenchSymbol(string Name, string? DisplayName = null) : ISymbol;
}
