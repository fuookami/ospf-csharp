#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo5;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo
{
    public class Demo5CuttingStockTests
    {
        [Fact]
        public void BuildModel_ShouldSucceed()
        {
            var demo = new CuttingStockDemo();
            var result = demo.BuildModel();
            result.Should().BeOfType<Ok<Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
        }

        [Fact]
        public void Model_ShouldHaveMinimumObjective()
        {
            var demo = new CuttingStockDemo();
            demo.BuildModel();
            demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
        }

        [Fact]
        public void Demands_ShouldNotBeEmpty()
        {
            var demo = new CuttingStockDemo();
            demo.StockDemands.Should().NotBeEmpty();
        }

        [Fact]
        public void Model_ShouldHaveDemandConstraints()
        {
            var demo = new CuttingStockDemo();
            demo.BuildModel();
            demo.MetaModel.RelationConstraints.Count.Should().BeGreaterThanOrEqualTo(4);
        }
    }
}
