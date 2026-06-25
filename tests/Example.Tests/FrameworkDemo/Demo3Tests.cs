#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo3;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

public class Demo3ColumnGenerationTests {
    [Fact]
    public void BuildModel_ShouldSucceed() {
        var demo = new ColumnGenerationDemo();
        Result<Success, ErrorCode, Error<ErrorCode>> result = demo.BuildModel();
        result.Should().BeOfType<Ok<Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
    }

    [Fact]
    public void Model_ShouldHaveMinimumObjective() {
        var demo = new ColumnGenerationDemo();
        demo.BuildModel();
        demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
    }

    [Fact]
    public void Model_ShouldHaveDemandConstraints() {
        var demo = new ColumnGenerationDemo();
        demo.BuildModel();
        // 4 demand constraints
        demo.MetaModel.RelationConstraints.Count.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Patterns_ShouldBeGenerated() {
        var demo = new ColumnGenerationDemo();
        demo.BuildModel();
        demo.Patterns.Should().NotBeEmpty();
    }
}
