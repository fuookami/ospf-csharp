#nullable enable

using System;

namespace Fuookami.Ospf.Quantities.Dimension
{
    /// <summary>
    /// 基础量纲值 / Fundamental quantity value.
    /// 表示量纲的幂次，用于构建导出量纲.
    /// Represents the power of a dimension, used to build derived quantities.
    /// </summary>
    public sealed record FundamentalQuantity(IFundamentalQuantityDimension Dimension, int Index = 1)
    {
        /// <summary>
        /// 相加（幂次相加）/ Add (add powers).
        /// </summary>
        public FundamentalQuantity Add(FundamentalQuantity rhs)
        {
            if (Dimension != rhs.Dimension)
                throw new ArgumentException($"Cannot add quantities with different dimensions: {Dimension.Symbol} vs {rhs.Dimension.Symbol}");
            return new FundamentalQuantity(Dimension, Index + rhs.Index);
        }

        /// <summary>
        /// 相减（幂次相减）/ Subtract (subtract powers).
        /// </summary>
        public FundamentalQuantity Subtract(FundamentalQuantity rhs)
        {
            if (Dimension != rhs.Dimension)
                throw new ArgumentException($"Cannot subtract quantities with different dimensions: {Dimension.Symbol} vs {rhs.Dimension.Symbol}");
            return new FundamentalQuantity(Dimension, Index - rhs.Index);
        }

        /// <summary>取负（幂次取反）/ Negate (negate power).</summary>
        public FundamentalQuantity Negate() => new(Dimension, -Index);

        /// <inheritdoc/>
        public override string ToString() => $"{Dimension}{Index}";

        /// <summary>运算符 + / Operator +.</summary>
        public static FundamentalQuantity operator +(FundamentalQuantity lhs, FundamentalQuantity rhs) => lhs.Add(rhs);
        /// <summary>运算符 - / Operator -.</summary>
        public static FundamentalQuantity operator -(FundamentalQuantity lhs, FundamentalQuantity rhs) => lhs.Subtract(rhs);
        /// <summary>取负运算符 / Unary negation operator.</summary>
        public static FundamentalQuantity operator -(FundamentalQuantity fq) => fq.Negate();
        /// <summary>基础量纲值与整数相乘 / Multiply fundamental quantity by int.</summary>
        public static FundamentalQuantity operator *(FundamentalQuantity fq, int index) => new(fq.Dimension, fq.Index * index);
        /// <summary>基础量纲值除以整数 / Divide fundamental quantity by int.</summary>
        public static FundamentalQuantity operator /(FundamentalQuantity fq, int index) => new(fq.Dimension, fq.Index / index);
    }
}
