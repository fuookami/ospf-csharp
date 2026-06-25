#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Example.CoreDemo;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.CoreDemo;

public class GenericNumberDemoTests {
    [Fact]
    public void RunBuildAndDump_ShouldReturnNonNullSummary() {
        // Arrange
        var demo = new GenericNumberDemo();

        // Act
        ModelBuildSummary summary = demo.RunBuildAndDump();

        // Assert
        summary.Should().NotBeNull();
    }

    [Fact]
    public void RunBuildAndDump_ShouldReturnSuccess() {
        // Arrange
        var demo = new GenericNumberDemo();

        // Act
        ModelBuildSummary summary = demo.RunBuildAndDump();

        // Assert
        summary.IsSuccessful.Should().BeTrue();
    }
}
