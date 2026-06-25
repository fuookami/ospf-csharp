#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Core.Solver.Output
{
    /// <summary>
    /// 解向量封装 / Solution vector wrapper.
    /// </summary>
    /// <typeparam name="V">值类型 / Value type</typeparam>
    public sealed record Solution<V>(IReadOnlyList<V> Values)
        where V : struct
    {
        /// <summary>解向量长度 / Solution vector length</summary>
        public int Length => Values.Count;

        /// <summary>按索引访问 / Access by index</summary>
        public V this[int index] => Values[index];

        /// <summary>转换为另一种值类型 / Convert to another value type</summary>
        public Solution<U> ConvertTo<U>(System.Func<V, U> converter)
            where U : struct
        {
            return new Solution<U>(Values.Select(converter).ToList());
        }

        /// <inheritdoc/>
        public override string ToString() => $"Solution[{Length}]";
    }
}
