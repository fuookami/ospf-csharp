#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model
{
    /// <summary>
    /// dominance 剪枝策略 / Dominance pruning strategy.
    ///
    /// - SameContribution：按精确需求贡献分组，只比较相同贡献的方案余宽
    ///   Group by exact demand contribution, compare rest width only for same-contribution plans
    /// - CrossContribution：按产品集合分组，贡献超集 + 余宽更优的方案 dominate 旧方案
    ///   Group by product set, contribution superset + better rest width dominates old plans
    /// </summary>
    public enum DominanceStrategy
    {
        /// <summary>同贡献 dominance / Same-contribution dominance.</summary>
        SameContribution,
        /// <summary>跨贡献 dominance / Cross-contribution dominance.</summary>
        CrossContribution
    }

    /// <summary>
    /// 切割方案生成约束配置 / Cutting plan generation constraint configuration.
    ///
    /// 同时作为便捷工厂：ToConstraints 将此 record 的字段转换为约束列表。
    /// Also serves as a convenience factory: ToConstraints converts fields to constraint list.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="MaxKnifeCount">最大刀数（切片总数上限）/ Max knife count (upper bound on total slices).</param>
    /// <param name="MinKnifeCount">最小刀数（切片总数下限）/ Min knife count (lower bound on total slices).</param>
    /// <param name="MaxOverProduceLength">最大超产长度 / Max over-produce length.</param>
    /// <param name="Parallelism">按物料并行生成的并发度，1 表示关闭 / Parallelism by material, 1 means disabled.</param>
    /// <param name="EnableDominancePruning">是否启用 dominance 剪枝 / Whether to enable dominance pruning.</param>
    /// <param name="DominanceStrategy">dominance 剪枝策略 / Dominance pruning strategy.</param>
    public sealed record GenerationConstraints<V>(
        UInt64? MaxKnifeCount = null,
        UInt64? MinKnifeCount = null,
        Quantity<V>? MaxOverProduceLength = null,
        long Parallelism = 1L,
        bool EnableDominancePruning = false,
        DominanceStrategy DominanceStrategy = DominanceStrategy.SameContribution
    ) where V : struct, IComparable<V>
    {
        /// <summary>
        /// 创建无约束配置 / Create unconstrained configuration.
        /// </summary>
        /// <returns>无约束配置 / Unconstrained configuration.</returns>
        public static GenerationConstraints<V> Unconstrained() => new();

        /// <summary>
        /// 转换为约束 predicate 列表 / Convert to constraint predicate list.
        /// </summary>
        /// <returns>约束列表 / Constraint list.</returns>
        public IReadOnlyList<ICuttingPlanConstraint<V>> ToConstraints()
        {
            var constraints = new List<ICuttingPlanConstraint<V>>();
            if (MaxKnifeCount is not null)
            {
                constraints.Add(new MaxKnifeCountConstraint<V>(MaxKnifeCount.Value));
            }
            if (MinKnifeCount is not null)
            {
                constraints.Add(new MinKnifeCountConstraint<V>(MinKnifeCount.Value));
            }
            if (MaxOverProduceLength is Quantity<V> maxLen)
            {
                constraints.Add(new MaxOverProduceLengthConstraint<V>(maxLen));
            }
            constraints.Add(new WidthUpperBoundConstraint<V>());
            return constraints;
        }
    }
}
