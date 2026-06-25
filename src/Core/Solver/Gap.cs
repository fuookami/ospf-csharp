#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver
{
    /// <summary>
    /// 间隙计算工具 / Gap computation utility.
    /// </summary>
    public static class Gap
    {
        /// <summary>
        /// 计算 MIP 间隙（相对差）。
        /// Compute MIP gap (relative difference).
        /// </summary>
        /// <param name="obj">当前目标值 / Current objective value</param>
        /// <param name="possibleBestObj">可能的最优目标值 / Possible best objective value</param>
        /// <returns>间隙值（绝对值）/ Gap value (absolute)</returns>
        public static Flt64 Compute(Flt64 obj, Flt64 possibleBestObj)
        {
            var diff = obj - possibleBestObj;
            if (diff == Flt64.Zero)
            {
                return Flt64.Zero;
            }

            var absObj = obj.Abs();
            var absBest = possibleBestObj.Abs();
            var denominator = absObj.Geq(absBest) ? absObj : absBest;
            if (denominator == Flt64.Zero)
            {
                return Flt64.Zero;
            }

            return (diff / denominator).Abs();
        }
    }
}
