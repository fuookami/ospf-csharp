#nullable enable

namespace Fuookami.Ospf.Utils.Concept
{
    /// <summary>可交换接口 / Swappable interface (mirrors ospf-kotlin Swappable&lt;Self&gt;).</summary>
    public interface ISwappable<TSelf>
    {
        /// <summary>交换 / Swap.</summary>
        void Swap(TSelf rhs);
    }

    /// <summary>Swap 辅助方法 / Swap helper methods.</summary>
    public static class SwapExtensions
    {
        /// <summary>交换两个元素 / Swap two elements.</summary>
        public static void Swap<T>(T lhs, T rhs) where T : ISwappable<T> => lhs.Swap(rhs);
    }
}
