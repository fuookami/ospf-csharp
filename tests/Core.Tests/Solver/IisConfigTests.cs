#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Iis;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

public class IisConfigTests {
    [Fact]
    public void Default_ShouldBeEnabled() {
        var config = new IisConfig();
        config.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Default_MaxIterations_ShouldBe1000() {
        var config = new IisConfig();
        config.MaxIterations.Should().Be(1000);
    }

    [Fact]
    public void Default_TimeoutSeconds_ShouldBe300() {
        var config = new IisConfig();
        config.TimeoutSeconds.Should().Be(300.0);
    }

    [Fact]
    public void CustomValues_ShouldBePreserved() {
        var config = new IisConfig {
            Enabled = false,
            MaxIterations = 500,
            TimeoutSeconds = 60.0
        };

        config.Enabled.Should().BeFalse();
        config.MaxIterations.Should().Be(500);
        config.TimeoutSeconds.Should().Be(60.0);
    }

    [Fact]
    public void RecordEquality_SameValues_ShouldBeEqual() {
        var a = new IisConfig { MaxIterations = 100 };
        var b = new IisConfig { MaxIterations = 100 };
        a.Should().Be(b);
    }

    [Fact]
    public void RecordEquality_DifferentValues_ShouldNotBeEqual() {
        var a = new IisConfig { MaxIterations = 100 };
        var b = new IisConfig { MaxIterations = 200 };
        a.Should().NotBe(b);
    }

    [Fact]
    public void IisComputingStatus_ShouldStoreValues() {
        var status = new IisComputingStatus(
            Iteration: 5,
            ElapsedTime: System.TimeSpan.FromSeconds(10),
            Found: true);

        status.Iteration.Should().Be(5);
        status.ElapsedTime.Should().Be(System.TimeSpan.FromSeconds(10));
        status.Found.Should().BeTrue();
    }

    [Fact]
    public void IisComputingStatus_NotFound_ShouldBeFalse() {
        var status = new IisComputingStatus(
            Iteration: 0,
            ElapsedTime: System.TimeSpan.Zero,
            Found: false);

        status.Found.Should().BeFalse();
    }
}
