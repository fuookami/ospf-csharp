#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.MultiArray;
/// <summary>
/// 虚拟索引范围接口
/// Dummy index range interface
/// </summary>
public interface IDummyIndexRange {
    /// <summary>获取范围的起始边界 / Get start bound</summary>
    int? Start();

    /// <summary>获取范围的结束边界 / Get end bound</summary>
    int? End();

    /// <summary>是否包含结束边界 / Whether end bound is inclusive</summary>
    bool IsInclusive() => false;

    /// <summary>检查值是否在范围内 / Check if value is contained</summary>
    bool Contains(int v, int len);
}

/// <summary>
/// 虚拟索引
/// Dummy index
/// </summary>
public abstract record DummyIndex {
    /// <summary>
    /// 单个索引
    /// Single index
    /// </summary>
    public sealed record Index(int Value) : DummyIndex;

    /// <summary>
    /// 范围索引
    /// Range index
    /// </summary>
    public sealed record Range(IDummyIndexRange RangeValue) : DummyIndex;

    /// <summary>
    /// 索引数组
    /// Index array
    /// </summary>
    public sealed record IndexArray(IReadOnlyList<int> Indices) : DummyIndex;

    /// <summary>
    /// 全范围索引
    /// Full range index
    /// </summary>
    public sealed record All : DummyIndex;

    /// <summary>All 单例 / All singleton</summary>
    public static readonly All AllInstance = new();

    /// <summary>计算虚拟索引在给定维度上的长度 / Calculate length in given dimension</summary>
    public int LenOf(IShape shape, int dimension) {
        switch (this) {
            case Index:
                return 1;
            case Range r: {
                    int len = shape[dimension];
                    int start = ActualBound(r.RangeValue.Start(), len) ?? 0;
                    int end = ActualBound(r.RangeValue.End(), len) ?? len;
                    return Math.Max(0, end - start);
                }
            case IndexArray ia:
                return ia.Indices.Count;
            case All:
                return shape[dimension];
            default:
                return 0;
        }
    }

    /// <summary>将虚拟索引转换为迭代器 / Convert to iterator</summary>
    public DummyIndexIterator IteratorOf(IShape shape, int dimension) {
        switch (this) {
            case Index idx: {
                    int? actualIdx = shape.ActualIndex(dimension, idx.Value);
                    return new DummyIndexIterator.Single(actualIdx ?? 0);
                }
            case Range r: {
                    int len = shape[dimension];
                    int start = ActualBound(r.RangeValue.Start(), len) ?? 0;
                    int end = ActualBound(r.RangeValue.End(), len) ?? len;
                    return start < end
                        ? new DummyIndexIterator.Continuous(start, end)
                        : new DummyIndexIterator.Continuous(0, 0);
                }
            case IndexArray ia: {
                    var validIndices = ia.Indices
                        .Select(i => shape.ActualIndex(dimension, i))
                        .Where(i => i.HasValue)
                        .Select(i => i!.Value)
                        .ToList();
                    return new DummyIndexIterator.Discrete(validIndices);
                }
            case All:
                return new DummyIndexIterator.Continuous(0, shape[dimension]);
            default:
                return new DummyIndexIterator.Continuous(0, 0);
        }
    }

    private static int? ActualBound(int? bound, int len) {
        if (bound == null) {
            return null;
        }

        return bound >= 0
            ? Math.Min(bound.Value, len)
            : Math.Max(len + bound.Value, 0);
    }

    /// <summary>从整数值创建单索引 / Create single index from int</summary>
    public static DummyIndex From(int value) => new Index(value);

    /// <summary>从 Range 创建范围索引 / Create range index from Range</summary>
    public static DummyIndex From(System.Range range) {
        int start = range.Start.IsFromEnd ? -range.Start.Value : range.Start.Value;
        int end = range.End.IsFromEnd ? -range.End.Value : range.End.Value;
        return new Range(new SimpleDummyIndexRange(start, end));
    }

    /// <summary>从整数列表创建索引数组 / Create index array from list</summary>
    public static DummyIndex From(IReadOnlyList<int> indices) => new IndexArray(indices);

    /// <summary>创建全范围索引 / Create full range index</summary>
    public static DummyIndex AllIndex() => AllInstance;
}

/// <summary>
/// 简单虚拟索引范围实现
/// Simple dummy index range implementation
/// </summary>
internal sealed record SimpleDummyIndexRange(int StartVal, int EndVal) : IDummyIndexRange {
    public int? Start() => StartVal;
    public int? End() => EndVal;
    public bool IsInclusive() => false;
    public bool Contains(int v, int len) => v >= StartVal && v < EndVal;
}

/// <summary>
/// 全范围虚拟索引的便捷访问对象
/// Convenience access for full range dummy index
/// </summary>
public static class DummyIndices {
    /// <summary>All 索引 / All index</summary>
    public static DummyIndex.All A => DummyIndex.AllInstance;
}

/// <summary>
/// 虚拟索引迭代器
/// Dummy index iterator
/// </summary>
public abstract record DummyIndexIterator {
    /// <summary>
    /// 单个索引
    /// Single index
    /// </summary>
    public sealed record Single(int Index) : DummyIndexIterator;

    /// <summary>
    /// 连续范围索引
    /// Continuous range indices
    /// </summary>
    public sealed record Continuous(int Start, int End) : DummyIndexIterator;

    /// <summary>
    /// 离散索引集合
    /// Discrete index collection
    /// </summary>
    public sealed record Discrete(IReadOnlyList<int> Indices) : DummyIndexIterator;

    /// <summary>获取指定位置的索引值 / Get index value at position</summary>
    public int? Get(int i) {
        switch (this) {
            case Single s:
                return i == 0 ? s.Index : null;
            case Continuous c: {
                    int length = Math.Max(0, c.End - c.Start);
                    return i < length ? c.Start + i : null;
                }
            case Discrete d:
                return i >= 0 && i < d.Indices.Count ? d.Indices[i] : null;
            default:
                return null;
        }
    }

    /// <summary>获取迭代器的长度 / Get iterator length</summary>
    public int Len() {
        switch (this) {
            case Single:
                return 1;
            case Continuous c:
                return Math.Max(0, c.End - c.Start);
            case Discrete d:
                return d.Indices.Count;
            default:
                return 0;
        }
    }

    /// <summary>检查迭代器是否为空 / Check if iterator is empty</summary>
    public bool IsEmpty() => Len() == 0;
}

/// <summary>
/// 映射索引
/// Map index
/// </summary>
public abstract record MapIndex {
    /// <summary>
    /// 虚拟索引
    /// Dummy index
    /// </summary>
    public sealed record Dummy(DummyIndex DummyValue) : MapIndex;

    /// <summary>
    /// 映射占位符
    /// Map placeholder
    /// </summary>
    public sealed record Map(int Index) : MapIndex;

    /// <summary>从 DummyIndex 创建 / Create from DummyIndex</summary>
    public static MapIndex From(DummyIndex d) => new Dummy(d);

    /// <summary>创建映射占位符 / Create map placeholder</summary>
    public static MapIndex CreateMap(int i) => new Map(i);
}
