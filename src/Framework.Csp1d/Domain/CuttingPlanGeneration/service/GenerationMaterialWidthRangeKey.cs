#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service
{
    /// <summary>
    /// 物料宽度范围等价键 / Material width range equivalence key.
    ///
    /// 用于缓存具有相同宽度范围的物料的索引和模板。
    /// Used to cache index and templates for materials with the same width range.
    /// </summary>
    /// <param name="LowerValue">下界值 / Lower bound value.</param>
    /// <param name="LowerUnit">下界单位 / Lower bound unit.</param>
    /// <param name="UpperValue">上界值 / Upper bound value.</param>
    /// <param name="UpperUnit">上界单位 / Upper bound unit.</param>
    /// <param name="LowerInclusive">下界是否包含 / Lower bound inclusive.</param>
    /// <param name="UpperInclusive">上界是否包含 / Upper bound inclusive.</param>
    /// <param name="StepValue">步长值 / Step value.</param>
    /// <param name="StepUnit">步长单位 / Step unit.</param>
    internal sealed record GenerationMaterialWidthRangeKey(
        string LowerValue,
        string LowerUnit,
        string UpperValue,
        string UpperUnit,
        bool LowerInclusive,
        bool UpperInclusive,
        string StepValue,
        string StepUnit
    );

    /// <summary>
    /// 物料宽度范围键构建辅助 / Material width range key building helpers.
    /// </summary>
    internal static class GenerationMaterialWidthRangeKeyExtensions
    {
        /// <summary>
        /// 从物料宽度范围构建等价键 / Build equivalence key from material width range.
        /// </summary>
        /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
        /// <param name="lowerBound">下界 / Lower bound.</param>
        /// <param name="upperBound">上界 / Upper bound.</param>
        /// <param name="lowerInclusive">下界是否包含 / Lower bound inclusive.</param>
        /// <param name="upperInclusive">上界是否包含 / Upper bound inclusive.</param>
        /// <param name="step">步长 / Step.</param>
        /// <returns>宽度范围等价键 / Width range equivalence key.</returns>
        public static GenerationMaterialWidthRangeKey ToWidthRangeKey<V>(
            Quantity<V> lowerBound,
            Quantity<V> upperBound,
            bool lowerInclusive,
            bool upperInclusive,
            Quantity<V> step)
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference — sealed record parameters are never null
            return new GenerationMaterialWidthRangeKey(
                LowerValue: lowerBound!.Value.ToString() ?? "",
                LowerUnit: CanonicalUnitKey(lowerBound.Unit),
                UpperValue: upperBound!.Value.ToString() ?? "",
                UpperUnit: CanonicalUnitKey(upperBound.Unit),
                LowerInclusive: lowerInclusive,
                UpperInclusive: upperInclusive,
                StepValue: step!.Value.ToString() ?? "",
                StepUnit: CanonicalUnitKey(step.Unit)
            );
#pragma warning restore CS8602
        }

        private static string CanonicalUnitKey(PhysicalUnit unit)
        {
            return unit.Symbol ?? unit.Name ?? unit.ToString() ?? "";
        }
    }
}
