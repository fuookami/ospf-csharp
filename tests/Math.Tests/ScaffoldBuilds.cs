#nullable enable

using Xunit;

namespace Fuookami.Ospf.Math.Tests
{
    /// <summary>
    /// 脚手架构建测试 / Scaffold build test
    /// </summary>
    public class ScaffoldBuilds
    {
        [Fact]
        public void Math_Assembly_Loads()
        {
            // Verify that the Math assembly loads and basic types are accessible
            var trivalent = new Trivalent.True();
            Assert.NotNull(trivalent);
            Assert.True(trivalent.IsTrue);
        }

        [Fact]
        public void Operator_Types_Exist()
        {
            // Verify that operator interfaces are accessible
            Assert.NotNull(typeof(Fuookami.Ospf.Math.Operator.IPlus<int, int>));
            Assert.NotNull(typeof(Fuookami.Ospf.Math.Operator.IMinus<int, int>));
            Assert.NotNull(typeof(Fuookami.Ospf.Math.Operator.ITimes<int, int>));
            Assert.NotNull(typeof(Fuookami.Ospf.Math.Operator.IDiv<int, int>));
            Assert.NotNull(typeof(Fuookami.Ospf.Math.Operator.IRem<int, int>));
            Assert.NotNull(typeof(Fuookami.Ospf.Math.Operator.INeg<int>));
            Assert.NotNull(typeof(Fuookami.Ospf.Math.Operator.IAbs<int>));
            Assert.NotNull(typeof(Fuookami.Ospf.Math.Operator.IReciprocal<int>));
        }
    }
}
