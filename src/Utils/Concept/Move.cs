#nullable enable

namespace Fuookami.Ospf.Utils.Concept
{
    /// <summary>可移动接口 / Movable interface (mirrors ospf-kotlin Movable&lt;Self&gt;).</summary>
    public interface IMovable<TSelf>
    {
        /// <summary>移动（转移所有权）/ Move (transfer ownership).</summary>
        TSelf Move();
    }

    /// <summary>Move 辅助方法 / Move helper methods.</summary>
    public static class MoveExtensions
    {
        /// <summary>移动元素 / Move element.</summary>
        public static T Move<T>(T element) where T : IMovable<T> => element.Move();
    }
}
