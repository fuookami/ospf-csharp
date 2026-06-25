#nullable enable

using System;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Algebra.Concept
{
    /// <summary>
    /// 全序接口 / Totally ordered interface
    /// </summary>
    public interface ITotallyOrdered<TSelf> : IOrd<TSelf>
        where TSelf : ITotallyOrdered<TSelf>, IComparable<TSelf>
    {
        /// <summary>取较小值 / Get minimum value</summary>
        TSelf MinValue(TSelf rhs) => CompareTo(rhs) <= 0 ? (TSelf)(object)this : rhs;
        /// <summary>取较大值 / Get maximum value</summary>
        TSelf MaxValue(TSelf rhs) => CompareTo(rhs) >= 0 ? (TSelf)(object)this : rhs;
        /// <summary>是否在范围内 / Whether between bounds</summary>
        bool IsBetween(TSelf lower, TSelf upper);
        /// <summary>限制值在范围内 / Clamp value within bounds</summary>
        TSelf ClampValue(TSelf lower, TSelf upper);
    }

    /// <summary>
    /// 向量空间接口 / Vector space interface
    /// </summary>
    public interface IVectorSpace<TSelf, TScalar> : IPlus<TSelf, TSelf>, IMinus<TSelf, TSelf>
        where TSelf : IVectorSpace<TSelf, TScalar>
    {
        /// <summary>标量乘法 / Scalar multiplication</summary>
        TSelf Scale(TScalar rhs);
    }

    /// <summary>
    /// 赋范空间接口 / Normed space interface
    /// </summary>
    public interface INormedSpace<TSelf, TScalar> : IVectorSpace<TSelf, TScalar>
        where TSelf : IVectorSpace<TSelf, TScalar>
    {
        /// <summary>范数 / Norm</summary>
        TScalar Norm { get; }
        /// <summary>单位向量 / Unit vector</summary>
        TSelf Unit { get; }
        /// <summary>范数平方 / Norm squared</summary>
        TScalar NormSquared();
        /// <summary>归一化（可能失败）/ Normalize (may fail)</summary>
        TSelf? Normalize();
    }

    /// <summary>
    /// 内积空间接口 / Inner product space interface
    /// </summary>
    public interface IInnerProductSpace<TSelf, TScalar> : INormedSpace<TSelf, TScalar>
        where TSelf : IVectorSpace<TSelf, TScalar>
    {
        /// <summary>内积 / Dot product</summary>
        TScalar Dot(TSelf rhs);
        /// <summary>夹角 / Angle</summary>
        object? Angle(TSelf rhs);
        /// <summary>是否正交 / Whether orthogonal</summary>
        bool IsOrthogonal(TSelf rhs, TScalar epsilon);
        /// <summary>余弦相似度 / Cosine similarity</summary>
        TScalar? CosineSimilarity(TSelf rhs);
        /// <summary>投影 / Projection</summary>
        TSelf? Project(TSelf rhs);
        /// <summary>正交分量 / Orthogonal component</summary>
        TSelf? OrthogonalComponent(TSelf rhs);
    }
}
