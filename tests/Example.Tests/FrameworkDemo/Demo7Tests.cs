#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo7;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

[Trait("Category", "Solver")]
public class Demo7HeuristicOptimizationTests {
    [Fact]
    public void CallBackModel_ShouldBeCreated() {
        var demo = new HeuristicOptimizationDemo();
        demo.Model.Should().NotBeNull();
    }

    [Fact]
    public void Solver_ShouldBeCreated() {
        var demo = new HeuristicOptimizationDemo();
        demo.Solver.Should().NotBeNull();
    }

    [Fact]
    public void Variables_ShouldNotBeEmpty() {
        var demo = new HeuristicOptimizationDemo();
        demo.Variables.Should().NotBeEmpty();
    }

    [Fact]
    public void Model_ShouldHaveMinimumObjective() {
        var demo = new HeuristicOptimizationDemo();
        demo.Model.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
    }
}

public class Demo7LinearToHeuristicTests {
    [Fact]
    public void BuildModel_ShouldSucceed() {
        var demo = new LinearToHeuristicDemo();
        Result<Success, ErrorCode, Error<ErrorCode>> result = demo.BuildModel();
        result.Should().BeOfType<Ok<Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
    }

    [Fact]
    public void Model_ShouldHaveMaximumObjective() {
        var demo = new LinearToHeuristicDemo();
        demo.BuildModel();
        demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Maximum);
    }

    [Fact]
    public void Model_ShouldHaveObjectives() {
        var demo = new LinearToHeuristicDemo();
        demo.BuildModel();
        demo.MetaModel.MetaSubObjects.Should().NotBeEmpty();
    }
}
