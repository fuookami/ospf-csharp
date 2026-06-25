#nullable enable

using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Ordinary
{
    /// <summary>
    /// 钳位函数 / Clamp Function.
    /// <para>将值限制在 [min, max] 范围内。</para>
    /// <para>Restricts value to [min, max] range.</para>
    /// </summary>
    public static class Clamp
    {
        /// <summary>将值限制在 [min, max] / Restrict value to [min, max].</summary>
        public static T ClampValue<T>(T v, T min, T max)
            where T : IOrd<T>
            => v.PartialOrd(min) is Order.Less ? min
             : v.PartialOrd(max) is Order.Greater ? max
             : v;
    }
}
