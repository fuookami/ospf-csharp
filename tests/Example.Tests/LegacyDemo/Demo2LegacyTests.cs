#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

public class Demo2KnapsackTests {
    [Fact]
    public void BuildModel_ShouldSucceed() {
        var demo = new KnapsackDemo();
        Result<Success, ErrorCode, Error<ErrorCode>> result = demo.BuildModel();
        result.Should().BeOfType<Ok<Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
    }

    [Fact]
    public void Model_ShouldHaveMaximumObjective() {
        var demo = new KnapsackDemo();
        demo.BuildModel();
        demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Maximum);
    }

    [Fact]
    public void Model_ShouldHaveConstraints() {
        var demo = new KnapsackDemo();
        demo.BuildModel();
        demo.MetaModel.RelationConstraints.Should().NotBeEmpty();
    }
}

public class Demo2AssignmentTests {
    [Fact]
    public void BuildModel_ShouldSucceed() {
        var demo = new AssignmentDemo();
        Result<Success, ErrorCode, Error<ErrorCode>> result = demo.BuildModel();
        result.Should().BeOfType<Ok<Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
    }

    [Fact]
    public void Model_ShouldHaveMinimumObjective() {
        var demo = new AssignmentDemo();
        demo.BuildModel();
        demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
    }

    [Fact]
    public void Model_ShouldHaveCompanyAndProductConstraints() {
        var demo = new AssignmentDemo();
        demo.BuildModel();
        // 4 company constraints + 4 product constraints = 8
        demo.MetaModel.RelationConstraints.Count.Should().BeGreaterThanOrEqualTo(8);
    }
}
