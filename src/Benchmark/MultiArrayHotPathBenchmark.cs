#nullable enable

using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Benchmark;
/// <summary>
/// multiarray 热点路径基准 / Multiarray hot path benchmark.
/// </summary>
[MemoryDiagnoser]
public class MultiArrayHotPathBenchmark {
    /// <summary>数据集大小 / Dataset size.</summary>
    [Params("small", "medium", "large")]
    public string Dataset { get; set; } = "small";

    private Shape3 _shape = null!;
    private int[][] _vectors = null!;
    private MutableMultiArray<int, Shape3> _dense = null!;
    private BlockMultiArray<int, Shape3> _sparse = null!;
    private List<int> _sourceList = null!;

    /// <summary>初始化基准数据 / Setup benchmark data.</summary>
    [GlobalSetup]
    public void Setup() {
        int n = Dataset switch {
            "small" => 12,
            "medium" => 28,
            "large" => 48,
            _ => 12
        };
        _shape = Shape3.WithOrder(n, n, n, StorageOrder.RowMajor);
        int size = _shape.Size;
        _sourceList = new List<int>(size);
        for (int i = 0; i < size; i++) {
            _sourceList.Add(i % 97);
        }
        _dense = MutableMultiArray<int, Shape3>.Factory.NewWith(_shape, 0);

        _vectors = new int[size][];
        for (int i = 0; i < size; i++) {
            _vectors[i] = _shape.VectorUnchecked(i);
            _dense[_vectors[i]] = _sourceList[i];
        }

        _sparse = BlockMultiArray<int, Shape3>.FromMultiArray(_dense.ToImmutable(), v => v % 5 == 0);
    }

    /// <summary>
    /// BlockMultiArray get 和 contains 操作。
    /// BlockMultiArray get and contains operations.
    /// </summary>
    [Benchmark]
    public int BlockGetAndContains() {
        int sum = 0;
        foreach (int[] vector in _vectors) {
            if (_sparse.ContainsIndex(vector)) {
                sum += _sparse.Get(vector);
            }
        }
        return sum;
    }

    /// <summary>
    /// BlockMultiArray set 和 remove 操作。
    /// BlockMultiArray set and remove operations.
    /// </summary>
    [Benchmark]
    public int BlockSetAndRemove() {
        int touched = 0;
        for (int i = 0; i < _vectors.Length; i += 3) {
            int[] v = _vectors[i];
            _sparse.Set(v, i);
            if ((i & 1) == 0) {
                _sparse.Remove(v);
                touched++;
            }
        }
        return touched + _sparse.Count;
    }

    /// <summary>
    /// 从列表创建 MultiArray（行主序）。
    /// Create MultiArray from list (row-major).
    /// </summary>
    [Benchmark]
    public int FromListRowMajor() {
        Result<MultiArray<int, Shape3>, ErrorCode, Error<ErrorCode>> result = MultiArray<int, Shape3>.Factory.FromList(_shape, _sourceList, AccessOrder.RowMajor);
        MultiArray<int, Shape3> arr = result.Value!;
        int midIndex = _shape.Size / 2;
        return arr[_shape.VectorUnchecked(midIndex)];
    }

    /// <summary>
    /// 将 MultiArray 转换为列表（列主序）。
    /// Convert MultiArray to list (column-major).
    /// </summary>
    [Benchmark]
    public int FlattenColumnMajor() {
        var list = _dense.ToImmutable().ToList();
        return list.Count;
    }
}
