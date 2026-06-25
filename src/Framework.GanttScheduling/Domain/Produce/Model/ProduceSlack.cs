#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
/// <summary>
/// 生产松弛工具方法 / Produce slack utility methods
/// </summary>
public static class ProduceSlackHelper {
    /// <summary>创建常量线性多项式值 / Create a constant linear polynomial value</summary>
    public static Flt64 ConstantPolynomial(Flt64 value) => value;

    /// <summary>计算生产松弛值 / Calculate produce slack value</summary>
    /// <param name="x">当前值 / Current value</param>
    /// <param name="threshold">阈值 / Threshold</param>
    /// <param name="withNegative">是否包含负松弛 / Whether to include negative slack</param>
    /// <param name="withPositive">是否包含正松弛 / Whether to include positive slack</param>
    /// <returns>松弛值 / Slack value</returns>
    public static Flt64 ProduceSlack(Flt64 x, Flt64 threshold, bool withNegative, bool withPositive) {
        Flt64 diff = x - threshold;
        if (withPositive && diff > Flt64.Zero) {
            return diff;
        }

        if (withNegative && diff < Flt64.Zero) {
            return -diff;
        }

        return Flt64.Zero;
    }
}
