#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
/// <summary>
/// 贡献长度计算辅助方法 / Contribution length calculation helper.
/// </summary>
internal static class GenerationContribution {
    /// <summary>
    /// 计算生成贡献长度 / Calculate generation contribution length.
    ///
    /// 优先使用产品自身长度，若产品支持动态长度则使用物料长度。
    /// Prefers product's own length; uses material length if product supports dynamic length.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="productLength">产品长度（可空）/ Product length (nullable).</param>
    /// <param name="productDynamicLength">产品是否支持动态长度 / Whether product supports dynamic length.</param>
    /// <param name="materialLength">物料长度（可空）/ Material length (nullable).</param>
    /// <returns>贡献长度（可空）/ Contribution length (nullable).</returns>
    public static Quantity<V>? ContributionLength<V>(
        Quantity<V>? productLength,
        bool productDynamicLength,
        Quantity<V>? materialLength) {
        if (productLength is not null) {
            return productLength;
        }
        if (productDynamicLength) {
            return materialLength;
        }
        return null;
    }
}
