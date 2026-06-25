#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Utils.Tests.Functional
{
    /// <summary>Eq/Ord 类型测试 / Eq/Ord type tests.</summary>
    public class EqOrdTest
    {
        [Fact]
        public void Order_Less_NegatesToGreater()
        {
            var less = new Order.Less();
            var negated = less.Negate();
            negated.Should().BeOfType<Order.Greater>();
        }

        [Fact]
        public void Order_Equal_NegatesToEqual()
        {
            var equal = new Order.Equal();
            var negated = equal.Negate();
            negated.Should().BeOfType<Order.Equal>();
        }

        [Fact]
        public void Order_Greater_NegatesToLess()
        {
            var greater = new Order.Greater();
            var negated = greater.Negate();
            negated.Should().BeOfType<Order.Less>();
        }

        [Fact]
        public void OrderHelpers_OrderOf_CorrectMapping()
        {
            OrderHelpers.OrderOf(-1).Should().BeOfType<Order.Less>();
            OrderHelpers.OrderOf(0).Should().BeOfType<Order.Equal>();
            OrderHelpers.OrderOf(1).Should().BeOfType<Order.Greater>();
        }

        [Fact]
        public void OrderBetween_Integers()
        {
            var order = OrderHelpers.OrderBetween(1, 2);
            order.Should().BeOfType<Order.Less>();
        }

        [Fact]
        public void Ord_Extension_Works()
        {
            int a = 1, b = 2;
            a.Ord(b).Should().BeOfType<Order.Less>();
            b.Ord(a).Should().BeOfType<Order.Greater>();
            a.Ord(a).Should().BeOfType<Order.Equal>();
        }
    }
}
