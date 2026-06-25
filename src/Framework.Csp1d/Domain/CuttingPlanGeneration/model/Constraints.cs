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
    /// 最大刀数约束：slices 中 amount 总和不超过 maxKnifeCount / Max knife count constraint.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    public sealed class MaxKnifeCountConstraint<V> : ICuttingPlanConstraint<V>
        where V : struct
    {
        /// <summary>最大刀数阈值 / Max knife count threshold.</summary>
        public UInt64 Threshold { get; }

        public MaxKnifeCountConstraint(UInt64 threshold)
        {
            Threshold = threshold;
        }

        /// <inheritdoc/>
        public bool IsSatisfied(CuttingPlanConstraintContext<V> context)
        {
            var totalCuts = UInt64.Zero;
            foreach (var slice in context.Slices)
            {
                totalCuts = totalCuts.Plus(slice.Amount);
            }
            return totalCuts.Leq(Threshold);
        }

        /// <inheritdoc/>
        public bool IsPruning => true;
    }

    /// <summary>
    /// 最小刀数约束：slices 中 amount 总和不小于 minKnifeCount / Min knife count constraint.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    public sealed class MinKnifeCountConstraint<V> : ICuttingPlanConstraint<V>
        where V : struct
    {
        /// <summary>最小刀数阈值 / Min knife count threshold.</summary>
        public UInt64 Threshold { get; }

        public MinKnifeCountConstraint(UInt64 threshold)
        {
            Threshold = threshold;
        }

        /// <inheritdoc/>
        public bool IsSatisfied(CuttingPlanConstraintContext<V> context)
        {
            var totalCuts = UInt64.Zero;
            foreach (var slice in context.Slices)
            {
                totalCuts = totalCuts.Plus(slice.Amount);
            }
            return totalCuts.Geq(Threshold);
        }

        /// <inheritdoc/>
        public bool IsPruning => false;
    }

    /// <summary>
    /// 最大超产长度约束 / Max over-produce length constraint.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    public sealed class MaxOverProduceLengthConstraint<V> : ICuttingPlanConstraint<V>
        where V : struct, IComparable<V>
    {
        /// <summary>最大超产长度阈值 / Max over-produce length threshold.</summary>
        public Quantity<V> MaxLength { get; }

        public MaxOverProduceLengthConstraint(Quantity<V> maxLength)
        {
            MaxLength = maxLength;
        }

        /// <inheritdoc/>
        public bool IsSatisfied(CuttingPlanConstraintContext<V> context)
        {
            foreach (var slice in context.Slices)
            {
                var productLength = slice.ProductionLength;
                if (productLength is null) continue;
                if (productLength.Value.CompareTo(MaxLength.Value) > 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public bool IsPruning => true;
    }

    /// <summary>
    /// 幅宽上界约束：总宽度不超过物料幅宽上界 / Width upper bound constraint.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    public sealed class WidthUpperBoundConstraint<V> : ICuttingPlanConstraint<V>
        where V : struct, IComparable<V>
    {
        /// <inheritdoc/>
        public bool IsSatisfied(CuttingPlanConstraintContext<V> context)
        {
            return context.TotalWidth.Value.CompareTo(context.UpperBound.Value) <= 0;
        }

        /// <inheritdoc/>
        public bool IsPruning => true;
    }
}
