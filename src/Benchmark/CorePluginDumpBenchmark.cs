#nullable enable

using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Benchmark;
/// <summary>
/// core-plugin dump 数据准备热点基准（不调用真实 solver）/ Core-plugin dump data preparation benchmark (without real solver calls).
/// </summary>
[MemoryDiagnoser]
public class CorePluginDumpBenchmark {
    /// <summary>数据集大小 / Dataset size.</summary>
    [Params("small", "medium", "large")]
    public string Dataset { get; set; } = "small";

    private double[] _lowerBounds = null!;
    private double[] _upperBounds = null!;
    private string[] _names = null!;
    private List<(int Col, Flt64 Coefficient)> _objectiveCells = null!;
    private SparseMatrix _constraintsLhs = null!;
    private Flt64[] _constraintsRhs = null!;
    private int[] _constraintsSigns = null!; // 0=GE, 1=LE, 2=EQ

    /// <summary>初始化基准数据 / Setup benchmark data.</summary>
    [GlobalSetup]
    public void Setup() {
        int variableCount = Dataset switch {
            "small" => 128,
            "medium" => 768,
            "large" => 2048,
            _ => 128
        };
        int constraintCount = Dataset switch {
            "small" => 256,
            "medium" => 2048,
            "large" => 8192,
            _ => 256
        };
        int stride = Dataset switch {
            "small" => 11,
            "medium" => 17,
            "large" => 23,
            _ => 11
        };

        // 变量数据 / Variable data
        _lowerBounds = new double[variableCount];
        _upperBounds = new double[variableCount];
        _names = new string[variableCount];
        var initialResults = new List<(int, double)>();
        for (int i = 0; i < variableCount; i++) {
            bool isBinary = i % 3 == 0;
            _lowerBounds[i] = isBinary ? 0.0 : -50.0 + (i % 5);
            _upperBounds[i] = isBinary ? 1.0 : 500.0 + (i % 7);
            _names[i] = $"v{i}";
            if (i % 4 == 0) {
                initialResults.Add((i, (double)(i % 13)));
            }
        }

        // 目标函数系数 / Objective coefficients
        _objectiveCells = new List<(int, Flt64)>(variableCount);
        for (int i = 0; i < variableCount; i++) {
            _objectiveCells.Add((i, new Flt64((double)((i % 9) + 1))));
        }

        // 约束 LHS / Constraint LHS
        _constraintsLhs = new SparseMatrix();
        _constraintsRhs = new Flt64[constraintCount];
        _constraintsSigns = new int[constraintCount];
        for (int i = 0; i < constraintCount; i++) {
            _constraintsRhs[i] = new Flt64((double)(i % 37));
            _constraintsSigns[i] = i % 3; // 0=GE, 1=LE, 2=EQ
        }
        for (int rowIndex = 0; rowIndex < constraintCount; rowIndex++) {
            var row = new SparseVector();
            int col = rowIndex % stride;
            while (col < variableCount) {
                row.Add(col, new Flt64((double)((col + rowIndex) % 19 + 1)));
                col += stride;
            }
            _constraintsLhs.AddRow(row);
        }
    }

    /// <summary>
    /// 对应各 solver dump 的变量 lower/upper/name/initial 预处理。
    /// Maps to variable lower/upper/name/initial preprocessing in solver dumps.
    /// </summary>
    [Benchmark]
    public int PrepareVariableDumpingDataHotPath() {
        int count = _lowerBounds.Length;
        int initialCount = 0;
        for (int i = 0; i < count; i++) {
            _ = _lowerBounds[i];
            _ = _upperBounds[i];
            _ = _names[i];
            if (i % 4 == 0) {
                initialCount++;
            }
        }
        return initialCount + count;
    }

    /// <summary>
    /// 对应各 solver dump 的 objective 系数到 double 的收集过程。
    /// Maps to objective coefficient collection to double in solver dumps.
    /// </summary>
    [Benchmark]
    public double CollectObjectiveCoefficients() {
        double checksum = 0.0;
        foreach ((int Col, Flt64 Coefficient) cell in _objectiveCells) {
            checksum += cell.Coefficient.ToDouble();
        }
        return checksum;
    }

    /// <summary>
    /// 对应各 solver dump 的约束分块大小推导。
    /// Maps to constraint segment size derivation in solver dumps.
    /// </summary>
    [Benchmark]
    public int ComputeConstraintSegments() {
        int segment = ComputeConstraintSegmentSizeLikeSolver(_constraintsSigns.Length, 12);
        return (_constraintsSigns.Length + segment - 1) / segment;
    }

    /// <summary>
    /// 对应各 solver dump 的 sparse row 扫描与界限转换。
    /// Maps to sparse row traversal and bound conversion in solver dumps.
    /// </summary>
    [Benchmark]
    public double WalkSparseRowsAndBounds() {
        double checksum = 0.0;
        for (int rowIndex = 0; rowIndex < _constraintsSigns.Length; rowIndex++) {
            double lowerBound = double.NegativeInfinity;
            double upperBound = double.PositiveInfinity;
            switch (_constraintsSigns[rowIndex]) {
                case 0: // GreaterEqual
                    lowerBound = _constraintsRhs[rowIndex].ToDouble();
                    break;
                case 1: // LessEqual
                    upperBound = _constraintsRhs[rowIndex].ToDouble();
                    break;
                case 2: // Equal
                    lowerBound = _constraintsRhs[rowIndex].ToDouble();
                    upperBound = _constraintsRhs[rowIndex].ToDouble();
                    break;
            }
            foreach (SparseVectorEntry entry in _constraintsLhs.Rows[rowIndex].Entries) {
                checksum += entry.Value.ToDouble();
            }
            checksum += lowerBound;
            checksum += upperBound;
        }
        return checksum;
    }

    /// <summary>
    /// 复刻 solver dump 约束分块大小逻辑。
    /// Mirrors solver dump constraint segment sizing logic.
    /// </summary>
    private static int ComputeConstraintSegmentSizeLikeSolver(int constraintSize, int availableProcessors) {
        if (constraintSize <= 0) {
            return 10;
        }
        int workerCount = (availableProcessors - 1) > 1 ? (availableProcessors - 1) : 1;
        int ratio = constraintSize / workerCount;
        if (ratio < 10) {
            return 10;
        }
        int segment = 1;
        while (ratio >= 10) {
            ratio /= 10;
            segment *= 10;
        }
        return segment;
    }
}
