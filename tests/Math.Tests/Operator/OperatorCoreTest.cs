#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Operator;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Operator
{
    /// <summary>
    /// 运算符核心测试 / Operator core tests
    /// </summary>
    public class OperatorCoreTest
    {
        [Fact]
        public void Int8_Plus()
        {
            var a = new Int8(3);
            var b = new Int8(5);
            var result = a.Plus(b);
            Assert.Equal(new Int8(8), result);
        }

        [Fact]
        public void Int8_Minus()
        {
            var a = new Int8(10);
            var b = new Int8(3);
            var result = a.Minus(b);
            Assert.Equal(new Int8(7), result);
        }

        [Fact]
        public void Int8_Times()
        {
            var a = new Int8(4);
            var b = new Int8(5);
            var result = a.Times(b);
            Assert.Equal(new Int8(20), result);
        }

        [Fact]
        public void Int8_Div()
        {
            var a = new Int8(20);
            var b = new Int8(4);
            var result = a.Div(b);
            Assert.Equal(new Int8(5), result);
        }

        [Fact]
        public void Int8_Rem()
        {
            var a = new Int8(17);
            var b = new Int8(5);
            var result = a.Rem(b);
            Assert.Equal(new Int8(2), result);
        }

        [Fact]
        public void Int8_Negate()
        {
            var a = new Int8(5);
            var result = a.Negate();
            Assert.Equal(new Int8(-5), result);
        }

        [Fact]
        public void Int8_Abs()
        {
            var a = new Int8(-5);
            var result = a.Abs();
            Assert.Equal(new Int8(5), result);
        }

        [Fact]
        public void Int8_Comparison()
        {
            var a = new Int8(5);
            var b = new Int8(10);
            Assert.True(a.Ls(b));
            Assert.True(b.Gr(a));
            Assert.True(a.Leq(a));
            Assert.True(a.Geq(a));
        }

        [Fact]
        public void Flt64_Arithmetic()
        {
            var a = new Flt64(3.0);
            var b = new Flt64(2.0);
            var sum = a.Plus(b);
            Assert.True(sum.Eq(new Flt64(5.0)));
        }

        [Fact]
        public void Rem_Mod_Extension()
        {
            var a = new Int8(17);
            var b = new Int8(5);
            var result = a.Mod(b);
            Assert.Equal(new Int8(2), result);
        }
    }
}
