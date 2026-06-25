#nullable enable

using System.Collections;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math
{
    /// <summary>
    /// 集合别名扩展方法 / Collection alias extension methods
    /// </summary>
    public static class CollectionAliases
    {
        /// <summary>
        /// 获取无符号大小 / Get unsigned size
        /// </summary>
        /// <param name="collection">集合 / Collection</param>
        /// <returns>无符号大小 / Unsigned size</returns>
        public static UInt64 USize(this ICollection collection) => new(collection.Count);

        /// <summary>
        /// 获取无符号索引范围 / Get unsigned index range
        /// </summary>
        /// <param name="collection">集合 / Collection</param>
        /// <returns>无符号索引范围 / Unsigned index range</returns>
        public static IntegerRange<UInt64> UIndices(this ICollection collection)
        {
            UInt64 size = collection.USize();
            return new IntegerRange<UInt64>(UInt64.Zero, size, UInt64.One);
        }
    }
}
