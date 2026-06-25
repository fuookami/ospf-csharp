#nullable enable

using System;

namespace Fuookami.Ospf.Utils.Functional
{
    /// <summary>
    /// 三路比较结果 / Three-way comparison result (mirrors ospf-kotlin sealed class Order).
    /// </summary>
    public abstract record Order(int Value)
    {
        /// <summary>取反 / Negate.</summary>
        public abstract Order Negate();

        /// <summary>一元取反运算符 / Unary negation operator.</summary>
        public static Order operator -(Order o) => o.Negate();

        /// <summary>如果相等则执行 / Execute if equal.</summary>
        public virtual Order IfEqual(Func<Order> f) => this;

        /// <summary>小于 / Less than.</summary>
        public sealed record Less : Order
        {
            public Less(int value = -1) : base(value) { }
            public override Order Negate() => new Greater(-Value);
        }

        /// <summary>相等 / Equal.</summary>
        public sealed record Equal : Order
        {
            public Equal() : base(0) { }
            public override Order Negate() => this;
            public override Order IfEqual(Func<Order> f) => f();
        }

        /// <summary>大于 / Greater.</summary>
        public sealed record Greater : Order
        {
            public Greater(int value = 1) : base(value) { }
            public override Order Negate() => new Less(-Value);
        }
    }

    /// <summary>Order 辅助方法 / Order helper methods.</summary>
    public static class OrderHelpers
    {
        /// <summary>从 int 创建 Order / Create Order from int.</summary>
        public static Order OrderOf(int value) =>
            value < 0 ? new Order.Less(value) : value > 0 ? new Order.Greater(value) : new Order.Equal();

        /// <summary>从 IComparable 比较创建 Order / Create Order from IComparable comparison.</summary>
        public static Order OrderBetween<T>(T lhs, T rhs) where T : IComparable<T> =>
            OrderOf(lhs.CompareTo(rhs));

        /// <summary>Ord 三路比较 / Ord three-way comparison.</summary>
        public static Order Ord<T>(this T lhs, T rhs) where T : IComparable<T> =>
            OrderOf(lhs.CompareTo(rhs));
    }

    /// <summary>部分序接口 / Partial order interface (mirrors ospf-kotlin PartialOrd&lt;in Self&gt;).</summary>
    public interface IPartialOrd<in TSelf> : IPartialEq<TSelf>
    {
        /// <summary>部分序比较 / Partial order comparison.</summary>
        Order? PartialOrd(TSelf rhs);

        /// <inheritdoc/>
        bool? IPartialEq<TSelf>.PartialEq(TSelf rhs) => PartialOrd(rhs) is Order.Equal;
    }

    /// <summary>全序接口 / Total order interface (mirrors ospf-kotlin Ord&lt;in Self&gt;).</summary>
    public interface IOrd<in TSelf> : IPartialOrd<TSelf>, IEq<TSelf>, IComparable<TSelf>
    {
        /// <summary>三路比较 / Three-way comparison.</summary>
        Order Ord(TSelf rhs);

        /// <inheritdoc/>
        int IComparable<TSelf>.CompareTo(TSelf? other) => other is null ? 1 : Ord(other).Value;

        /// <summary>小于 / Less than.</summary>
        bool Ls(TSelf rhs) => CompareTo(rhs) < 0;

        /// <summary>小于等于 / Less than or equal.</summary>
        bool Leq(TSelf rhs) => CompareTo(rhs) <= 0;

        /// <summary>大于 / Greater than.</summary>
        bool Gr(TSelf rhs) => CompareTo(rhs) > 0;

        /// <summary>大于等于 / Greater than or equal.</summary>
        bool Geq(TSelf rhs) => CompareTo(rhs) >= 0;
    }
}
