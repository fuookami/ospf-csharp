#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

[Trait("Category", "Solver")]
public class Demo4Bpp3dTests {
    [Fact]
    public void BuildModel_ShouldSucceed() {
        var demo = new Bpp3dDemo();
        Result<Success, ErrorCode, Error<ErrorCode>> result = demo.BuildModel();
        result.Should().BeOfType<Ok<Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
    }

    [Fact]
    public void Packages_ShouldNotBeEmpty() {
        var demo = new Bpp3dDemo();
        demo.Packages.Should().NotBeEmpty();
    }

    [Fact]
    public void Bin_ShouldHaveValidDimensions() {
        var demo = new Bpp3dDemo();
        demo.Bin.Width.Should().BeGreaterThan(0);
        demo.Bin.Height.Should().BeGreaterThan(0);
        demo.Bin.Depth.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AllPackages_ShouldFitInBin() {
        var demo = new Bpp3dDemo();
        foreach (PackageItem item in demo.Packages) {
            item.Width.Should().BeLessThanOrEqualTo(demo.Bin.Width);
            item.Height.Should().BeLessThanOrEqualTo(demo.Bin.Height);
            item.Depth.Should().BeLessThanOrEqualTo(demo.Bin.Depth);
        }
    }
}
