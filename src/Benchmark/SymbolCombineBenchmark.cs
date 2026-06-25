#nullable enable

using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Benchmark;
/// <summary>
/// math symbol combine 热点基准 / Benchmark for math symbol combine hot paths.
/// </summary>
[MemoryDiagnoser]
public class SymbolCombineBenchmark {
    /// <summary>数据集大小 / Dataset size.</summary>
    [Params("small", "medium", "large")]
    public string Dataset { get; set; } = "small";

    private BenchSymbol[] _symbols = null!;
    private List<LinearMonomial<Flt64>> _linearMonomials = null!;
    private List<QuadraticMonomial<Flt64>> _quadraticMonomials = null!;
    private MutableLinearPolynomial<Flt64> _mutableLinear = null!;
    private MutableQuadraticPolynomial<Flt64> _mutableQuadratic = null!;

    /// <summary>初始化基准数据 / Setup benchmark data.</summary>
    [GlobalSetup]
    public void Setup() {
        int symbolCount = Dataset switch {
            "small" => 64,
            "medium" => 256,
            "large" => 768,
            _ => 64
        };
        int repeat = Dataset switch {
            "small" => 8,
            "medium" => 16,
            "large" => 24,
            _ => 8
        };

        _symbols = new BenchSymbol[symbolCount];
        for (int i = 0; i < symbolCount; i++) {
            _symbols[i] = new BenchSymbol($"x{i}");
        }

        // 线性单项式 / Linear monomials
        _linearMonomials = new List<LinearMonomial<Flt64>>(symbolCount * repeat);
        for (int r = 0; r < repeat; r++) {
            for (int i = 0; i < _symbols.Length; i++) {
                double sign = ((i + r) % 2 == 0) ? 1.0 : -1.0;
                _linearMonomials.Add(new LinearMonomial<Flt64>(
                    new Flt64(sign * ((i % 7) + 1.0)),
                    _symbols[i]));
            }
        }

        // 二次单项式 / Quadratic monomials
        _quadraticMonomials = new List<QuadraticMonomial<Flt64>>(symbolCount * 2);
        for (int i = 0; i < _symbols.Length; i++) {
            BenchSymbol a = _symbols[i];
            BenchSymbol b = _symbols[(i * 17 + 11) % _symbols.Length];
            _quadraticMonomials.Add(new QuadraticMonomial<Flt64>(
                new Flt64((double)((i % 5) + 1)), a, b));
            _quadraticMonomials.Add(new QuadraticMonomial<Flt64>(
                new Flt64((double)((i % 5) + 1)), b, a));
        }

        // 可变多项式（预填充部分数据）/ Mutable polynomials (pre-filled with partial data)
        _mutableLinear = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        _mutableQuadratic = new MutableQuadraticPolynomial<Flt64>(constant: Flt64.Zero);
    }

    /// <summary>
    /// 合并线性单项式列表（字典聚合）。
    /// Combine linear monomial list (dictionary aggregation).
    /// </summary>
    [Benchmark]
    public int CombineLinearIterable() {
        var merged = new Dictionary<ISymbol, Flt64>();
        foreach (LinearMonomial<Flt64> m in _linearMonomials) {
            if (merged.TryGetValue(m.Symbol, out Flt64 existing)) {
                merged[m.Symbol] = existing + m.Coefficient;
            }
            else {
                merged[m.Symbol] = m.Coefficient;
            }
        }
        return merged.Count;
    }

    /// <summary>
    /// 合并线性多项式（多项式内单项式字典聚合）。
    /// Combine linear polynomial (monomial dictionary aggregation within polynomial).
    /// </summary>
    [Benchmark]
    public int CombineLinearPolynomialGeneric() {
        var poly = new LinearPolynomial<Flt64>(_linearMonomials, Flt64.Zero);
        var merged = new Dictionary<ISymbol, Flt64>();
        foreach (LinearMonomial<Flt64> m in poly.Monomials) {
            if (merged.TryGetValue(m.Symbol, out Flt64 existing)) {
                merged[m.Symbol] = existing + m.Coefficient;
            }
            else {
                merged[m.Symbol] = m.Coefficient;
            }
        }
        return merged.Count;
    }

    /// <summary>
    /// 合并二次单项式列表（字典聚合）。
    /// Combine quadratic monomial list (dictionary aggregation).
    /// </summary>
    [Benchmark]
    public int CombineQuadraticIterable() {
        var merged = new Dictionary<(ISymbol, ISymbol?), Flt64>();
        foreach (QuadraticMonomial<Flt64> m in _quadraticMonomials) {
            (ISymbol Symbol1, ISymbol? Symbol2) key = (m.Symbol1, m.Symbol2);
            if (merged.TryGetValue(key, out Flt64 existing)) {
                merged[key] = existing + m.Coefficient;
            }
            else {
                merged[key] = m.Coefficient;
            }
        }
        return merged.Count;
    }

    /// <summary>
    /// 合并二次多项式（多项式内单项式字典聚合）。
    /// Combine quadratic polynomial (monomial dictionary aggregation within polynomial).
    /// </summary>
    [Benchmark]
    public int CombineQuadraticPolynomialGeneric() {
        var poly = new QuadraticPolynomial<Flt64>(_quadraticMonomials, Flt64.Zero);
        var merged = new Dictionary<(ISymbol, ISymbol?), Flt64>();
        foreach (QuadraticMonomial<Flt64> m in poly.Monomials) {
            (ISymbol Symbol1, ISymbol? Symbol2) key = (m.Symbol1, m.Symbol2);
            if (merged.TryGetValue(key, out Flt64 existing)) {
                merged[key] = existing + m.Coefficient;
            }
            else {
                merged[key] = m.Coefficient;
            }
        }
        return merged.Count;
    }

    /// <summary>
    /// 可变线性多项式增量累积并合并。
    /// Mutable linear polynomial incremental accumulate and combine.
    /// </summary>
    [Benchmark]
    public int MutableLinearAccumulateAndCombine() {
        var mutable = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (LinearMonomial<Flt64> monomial in _linearMonomials) {
            mutable.AddMonomial(monomial);
        }
        // combine: deduplicate by symbol
        var merged = new Dictionary<ISymbol, Flt64>();
        foreach (LinearMonomial<Flt64> m in mutable.Monomials) {
            if (merged.TryGetValue(m.Symbol, out Flt64 existing)) {
                merged[m.Symbol] = existing + m.Coefficient;
            }
            else {
                merged[m.Symbol] = m.Coefficient;
            }
        }
        return merged.Count;
    }

    /// <summary>
    /// 可变二次多项式增量累积并合并。
    /// Mutable quadratic polynomial incremental accumulate and combine.
    /// </summary>
    [Benchmark]
    public int MutableQuadraticAccumulateAndCombine() {
        var mutable = new MutableQuadraticPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (QuadraticMonomial<Flt64> monomial in _quadraticMonomials) {
            mutable.AddMonomial(monomial);
        }
        // combine: deduplicate by (symbol1, symbol2)
        var merged = new Dictionary<(ISymbol, ISymbol?), Flt64>();
        foreach (QuadraticMonomial<Flt64> m in mutable.Monomials) {
            (ISymbol Symbol1, ISymbol? Symbol2) key = (m.Symbol1, m.Symbol2);
            if (merged.TryGetValue(key, out Flt64 existing)) {
                merged[key] = existing + m.Coefficient;
            }
            else {
                merged[key] = m.Coefficient;
            }
        }
        return merged.Count;
    }

    /// <summary>
    /// 基准测试用符号实现 / Symbol implementation for benchmarking.
    /// </summary>
    private sealed record BenchSymbol(string Name, string? DisplayName = null) : ISymbol;
}
