#nullable enable

using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Math.MultiArray.Einsum;
/// <summary>
/// 索引标签 / Index label.
/// Einstein 求和中使用的索引标签枚举。
/// Index label enum used in Einstein summation.
/// </summary>
public enum IndexLabel {
    /// <summary>索引 I / Index I.</summary>
    I = 0,
    /// <summary>索引 J / Index J.</summary>
    J = 1,
    /// <summary>索引 K / Index K.</summary>
    K = 2,
    /// <summary>索引 L / Index L.</summary>
    L = 3,
    /// <summary>索引 M / Index M.</summary>
    M = 4,
    /// <summary>索引 N / Index N.</summary>
    N = 5,
}

/// <summary>索引标签扩展 / Index label extensions.</summary>
public static class IndexLabelExtensions {
    /// <summary>获取索引标签名称 / Get index label name.</summary>
    public static string LabelName(this IndexLabel label) => label switch {
        IndexLabel.I => "i",
        IndexLabel.J => "j",
        IndexLabel.K => "k",
        IndexLabel.L => "l",
        IndexLabel.M => "m",
        IndexLabel.N => "n",
        _ => "?"
    };

    /// <summary>获取默认索引列表 / Get default index labels for given dimension count.</summary>
    public static IndexLabel[] Defaults(int dimension) {
        var labels = new IndexLabel[dimension];
        for (int i = 0; i < dimension && i < 6; i++) {
            labels[i] = (IndexLabel)i;
        }

        return labels;
    }
}

/// <summary>
/// 索引列表 / Index list.
/// 有序索引标签列表。
/// Ordered list of index labels.
/// </summary>
public sealed record IndexList {
    /// <summary>索引标签 / Index labels.</summary>
    public IReadOnlyList<IndexLabel> Labels { get; }

    /// <summary>索引列表长度 / Length of index list.</summary>
    public int Length => Labels.Count;

    /// <summary>索引列表名称（如 "ijk"）/ Index list names (e.g., "ijk").</summary>
    public string Names => string.Join("", Labels.Select(l => l.LabelName()));

    /// <summary>索引列表 ID / Index list IDs.</summary>
    public IReadOnlyList<int> Ids => Labels.Select(l => (int)l).ToArray();

    /// <summary>构造函数 / Constructor.</summary>
    public IndexList(IReadOnlyList<IndexLabel> labels) {
        Labels = labels;
    }
}

/// <summary>
/// 带索引标记的张量表达式 / Tensor expression with index labels.
/// 将 MultiArray 与索引列表关联，用于爱因斯坦求和。
/// Associates a MultiArray with an index list for Einstein summation.
/// </summary>
/// <typeparam name="T">元素类型 / Element type.</typeparam>
/// <typeparam name="S">形状类型 / Shape type.</typeparam>
public sealed record TensorExpr<T, S>
    where T : notnull
    where S : IShape {
    /// <summary>数据引用 / Data reference.</summary>
    public AbstractMultiArray<T, S> Data { get; }

    /// <summary>索引列表 / Index list.</summary>
    public IndexList Indices { get; }

    /// <summary>形状引用 / Shape reference.</summary>
    public S Shape => Data.Shape;

    /// <summary>索引列表名称 / Index list names.</summary>
    public string IndexNames => Indices.Names;

    /// <summary>索引列表 ID / Index list IDs.</summary>
    public IReadOnlyList<int> IndexIds => Indices.Ids;

    /// <summary>元素总数 / Total element count.</summary>
    public int Count => Data.Count;

    /// <summary>维度数 / Number of dimensions.</summary>
    public int Dimension => Data.Shape.Dimension;

    private TensorExpr(AbstractMultiArray<T, S> data, IndexList indices) {
        Data = data;
        Indices = indices;
    }

    /// <summary>创建张量表达式（失败返回 Ret）/ Create, returning Ret on failure.</summary>
    public static Result<TensorExpr<T, S>, ErrorCode, Error<ErrorCode>> NewSafe(
        AbstractMultiArray<T, S> data, IndexList indices) {
        if (indices.Length != data.Shape.Dimension) {
            return new Failed<TensorExpr<T, S>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"Array dimension must match index list length: expected={data.Shape.Dimension}, actual={indices.Length}.");
        }

        return new Ok<TensorExpr<T, S>, ErrorCode, Error<ErrorCode>>(
            new TensorExpr<T, S>(data, indices));
    }

    /// <summary>创建张量表达式 / Create tensor expression.</summary>
    public static Result<TensorExpr<T, S>, ErrorCode, Error<ErrorCode>> New(
        AbstractMultiArray<T, S> data, IndexList indices)
        => NewSafe(data, indices);

    /// <summary>创建张量表达式（从标签数组）/ Create tensor expression from label array.</summary>
    public static Result<TensorExpr<T, S>, ErrorCode, Error<ErrorCode>> New(
        AbstractMultiArray<T, S> data, IReadOnlyList<IndexLabel> labels)
        => NewSafe(data, new IndexList(labels));

    /// <summary>使用默认索引（I,J,K,...）/ Auto-assign default indices.</summary>
    public static Result<TensorExpr<T, S>, ErrorCode, Error<ErrorCode>> WithDefaultIndices(
        AbstractMultiArray<T, S> data) {
        IndexLabel[] defaults = IndexLabelExtensions.Defaults(data.Shape.Dimension);
        return NewSafe(data, new IndexList(defaults));
    }
}
