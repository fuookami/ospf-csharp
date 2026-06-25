#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Example.CoreDemo.Demo7;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.CoreDemo.Demo7;

public class Demo7CoreTests {
    [Fact]
    public void BuildModel_ShouldSucceed() {
        // Arrange
        var demo = new TransportationDemo();

        // Act
        Result<Success, ErrorCode, Error<ErrorCode>> result = demo.BuildModel();

        // Assert
        result.Should().BeOfType<Ok<Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
    }

    [Fact]
    public void Model_ShouldHaveCorrectName() {
        // Arrange
        var demo = new TransportationDemo();

        // Act
        demo.BuildModel();

        // Assert
        demo.MetaModel.Name.Should().Be("demo7-transportation");
    }

    [Fact]
    public void Model_ShouldHaveObjective() {
        // Arrange
        var demo = new TransportationDemo();

        // Act
        demo.BuildModel();

        // Assert
        demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
    }

    [Fact]
    public void Model_ShouldHaveConstraints() {
        // Arrange
        var demo = new TransportationDemo();

        // Act
        demo.BuildModel();

        // Assert
        demo.MetaModel.RelationConstraints.Should().NotBeEmpty();
    }
}
