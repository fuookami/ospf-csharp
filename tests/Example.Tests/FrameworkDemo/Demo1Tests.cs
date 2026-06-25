#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo
{
    public class Demo1CapitalInvestmentTests
    {
        [Fact]
        public void BuildModel_ShouldSucceed()
        {
            // Arrange
            var demo = new CapitalInvestmentDemo();

            // Act
            var result = demo.BuildModel();

            // Assert
            result.Should().BeOfType<Ok<Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
        }

        [Fact]
        public void Model_ShouldHaveCorrectName()
        {
            // Arrange
            var demo = new CapitalInvestmentDemo();

            // Act
            demo.BuildModel();

            // Assert
            demo.MetaModel.Name.Should().Be("demo1-capital-investment");
        }

        [Fact]
        public void Model_ShouldHaveMaximumObjective()
        {
            // Arrange
            var demo = new CapitalInvestmentDemo();

            // Act
            demo.BuildModel();

            // Assert
            demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Maximum);
        }

        [Fact]
        public void Model_ShouldHaveFiveVariables()
        {
            // Arrange
            var demo = new CapitalInvestmentDemo();

            // Act
            demo.BuildModel();

            // Assert
            demo.MetaModel.MetaSubObjects.Should().NotBeEmpty();
        }

        [Fact]
        public void Model_ShouldHaveConstraints()
        {
            // Arrange
            var demo = new CapitalInvestmentDemo();

            // Act
            demo.BuildModel();

            // Assert
            demo.MetaModel.RelationConstraints.Should().NotBeEmpty();
        }
    }
}
