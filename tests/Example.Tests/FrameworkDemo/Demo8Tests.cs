#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo8;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

public class Demo8MultiObjectiveTests {
    [Fact]
    public void BuildModel_ShouldSucceed() {
        var demo = new MultiObjectiveDemo();
        Result<Success, ErrorCode, Error<ErrorCode>> result = demo.BuildModel();
        result.Should().BeOfType<Ok<Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
    }

    [Fact]
    public void Model_ShouldHaveMaximumObjective() {
        var demo = new MultiObjectiveDemo();
        demo.BuildModel();
        demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Maximum);
    }

    [Fact]
    public void Products_ShouldNotBeEmpty() {
        var demo = new MultiObjectiveDemo();
        demo.InvestmentProducts.Should().NotBeEmpty();
    }

    [Fact]
    public void Model_ShouldHaveFundsAndRiskConstraints() {
        var demo = new MultiObjectiveDemo();
        demo.BuildModel();
        // At least: 1 funds constraint + 1 risk constraint
        demo.MetaModel.RelationConstraints.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Model_ShouldHaveObjective() {
        var demo = new MultiObjectiveDemo();
        demo.BuildModel();
        demo.MetaModel.MetaSubObjects.Should().NotBeEmpty();
    }
}
