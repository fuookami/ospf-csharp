#nullable enable

using Fuookami.Ospf.Math;
using Xunit;

namespace Fuookami.Ospf.Math.Tests
{
    /// <summary>
    /// 三值逻辑测试 / Trivalent logic tests
    /// </summary>
    public class TrivalentTest
    {
        [Fact]
        public void True_And_True_Is_True()
        {
            var result = new Trivalent.True().And(new Trivalent.True());
            Assert.IsType<Trivalent.True>(result);
            Assert.True(result.IsTrue);
        }

        [Fact]
        public void True_And_False_Is_False()
        {
            var result = new Trivalent.True().And(new Trivalent.False());
            Assert.IsType<Trivalent.False>(result);
            Assert.False(result.IsTrue);
        }

        [Fact]
        public void True_And_Unknown_Is_Unknown()
        {
            var result = new Trivalent.True().And(new Trivalent.Unknown());
            Assert.IsType<Trivalent.Unknown>(result);
            Assert.Null(result.IsTrue);
        }

        [Fact]
        public void False_And_Anything_Is_False()
        {
            Assert.IsType<Trivalent.False>(new Trivalent.False().And(new Trivalent.True()));
            Assert.IsType<Trivalent.False>(new Trivalent.False().And(new Trivalent.False()));
            Assert.IsType<Trivalent.False>(new Trivalent.False().And(new Trivalent.Unknown()));
        }

        [Fact]
        public void True_Or_Anything_Is_True()
        {
            Assert.IsType<Trivalent.True>(new Trivalent.True().Or(new Trivalent.True()));
            Assert.IsType<Trivalent.True>(new Trivalent.True().Or(new Trivalent.False()));
            Assert.IsType<Trivalent.True>(new Trivalent.True().Or(new Trivalent.Unknown()));
        }

        [Fact]
        public void False_Or_False_Is_False()
        {
            var result = new Trivalent.False().Or(new Trivalent.False());
            Assert.IsType<Trivalent.False>(result);
        }

        [Fact]
        public void False_Or_Unknown_Is_Unknown()
        {
            var result = new Trivalent.False().Or(new Trivalent.Unknown());
            Assert.IsType<Trivalent.Unknown>(result);
        }

        [Fact]
        public void Not_True_Is_False()
        {
            var result = new Trivalent.True().Not();
            Assert.IsType<Trivalent.False>(result);
        }

        [Fact]
        public void Not_False_Is_True()
        {
            var result = new Trivalent.False().Not();
            Assert.IsType<Trivalent.True>(result);
        }

        [Fact]
        public void Not_Unknown_Is_Unknown()
        {
            var result = new Trivalent.Unknown().Not();
            Assert.IsType<Trivalent.Unknown>(result);
        }

        [Fact]
        public void Invoke_From_Bool()
        {
            Assert.IsType<Trivalent.True>(Trivalent.Invoke(true));
            Assert.IsType<Trivalent.False>(Trivalent.Invoke(false));
        }

        [Fact]
        public void Invoke_From_Nullable_Bool()
        {
            Assert.IsType<Trivalent.True>(Trivalent.Invoke((bool?)true));
            Assert.IsType<Trivalent.False>(Trivalent.Invoke((bool?)false));
            Assert.IsType<Trivalent.Unknown>(Trivalent.Invoke((bool?)null));
        }

        [Fact]
        public void BalancedTrivalent_Logic()
        {
            Assert.IsType<BalancedTrivalent.True>(new BalancedTrivalent.True().And(new BalancedTrivalent.True()));
            Assert.IsType<BalancedTrivalent.False>(new BalancedTrivalent.True().And(new BalancedTrivalent.False()));
            Assert.IsType<BalancedTrivalent.True>(new BalancedTrivalent.False().Or(new BalancedTrivalent.True()));
            Assert.IsType<BalancedTrivalent.False>(new BalancedTrivalent.True().Not());
        }
    }
}
