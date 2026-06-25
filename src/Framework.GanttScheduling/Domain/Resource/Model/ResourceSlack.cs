#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
/// <summary>
/// 资源松弛工具方法 / Resource slack utility methods.
/// </summary>
public static class ResourceSlackHelper {
    /// <summary>创建常量线性多项式值 / Create a constant linear polynomial value.</summary>
    public static Flt64 ConstantPolynomial(Flt64 value) => value;

    /// <summary>计算资源松弛值 / Calculate resource slack value.</summary>
    public static Flt64 ResourceSlack(
        Flt64 x, Flt64 threshold, bool withNegative, bool withPositive) {
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
