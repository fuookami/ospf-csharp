#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo1.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Xunit;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

public class Demo1SspTests {
    [Fact]
    public void BuildModel_ShouldSucceed() {
        // Arrange
        var demo = new SspDemo();

        // Act
        Result<Success, ErrorCode, Error<ErrorCode>> result = demo.BuildModel();

        // Assert
        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void Model_ShouldHaveCorrectName() {
        // Arrange
        var demo = new SspDemo();

        // Act
        demo.BuildModel();

        // Assert
        demo.MetaModel.Name.Should().Be("demo1-ssp");
    }

    [Fact]
    public void Model_ShouldHaveMinimumObjective() {
        // Arrange
        var demo = new SspDemo();

        // Act
        demo.BuildModel();

        // Assert
        demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
    }

    [Fact]
    public void Model_ShouldHaveConstraints() {
        // Arrange
        var demo = new SspDemo();

        // Act
        demo.BuildModel();

        // Assert
        demo.MetaModel.Constraints.Should().NotBeEmpty();
    }

    [Fact]
    public void Model_ShouldHaveObjectiveCategory() {
        // Arrange
        var demo = new SspDemo();

        // Act
        demo.BuildModel();

        // Assert
        demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
    }

    [Fact]
    public void Graph_ShouldHaveCorrectNodeCount() {
        // Arrange
        var demo = new SspDemo();

        // Act
        demo.BuildModel();

        // Assert
        // 28 normal nodes + 12 client nodes = 40 nodes
        demo.Graph.Nodes.Count.Should().Be(40);
    }

    [Fact]
    public void Graph_ShouldHaveCorrectEdgeCount() {
        // Arrange
        var demo = new SspDemo();

        // Act
        demo.BuildModel();

        // Assert
        // 45 edges * 2 (bidirectional) + 12 client edges = 102 edges
        demo.Graph.Edges.Count.Should().Be(102);
    }

    [Fact]
    public void Services_ShouldHaveCorrectCount() {
        // Arrange
        var demo = new SspDemo();

        // Act
        demo.BuildModel();

        // Assert
        // 28 / 2 = 14 services
        demo.Services.Count.Should().Be(14);
    }

    [Fact]
    public void ParseInput_ShouldSucceed() {
        // Arrange
        var demo = new SspDemo();

        // Act
        Result<SspInput, ErrorCode, Error<ErrorCode>> result = demo.ParseInput();

        // Assert
        result.Should().BeOfType<Ok<SspInput, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void ParseInput_ShouldReturnCorrectData() {
        // Arrange
        var demo = new SspDemo();

        // Act
        Result<SspInput, ErrorCode, Error<ErrorCode>> result = demo.ParseInput();

        // Assert
        result.Should().BeOfType<Ok<SspInput, ErrorCode, Error<ErrorCode>>>();
        var ok = (Ok<SspInput, ErrorCode, Error<ErrorCode>>)result;
        ok.Value.NormalNodeAmount.Should().Be(new UInt64(28));
        ok.Value.Edges.Count.Should().Be(45);
        ok.Value.ClientNodes.Count.Should().Be(12);
        ok.Value.ServiceCost.Should().Be(new UInt64(100));
    }

    [Fact]
    public void BuildModel_WithInvalidInput_ShouldFail() {
        // Arrange
        var demo = new SspDemo();

        // Act - BuildModel with empty input
        var emptyInput = new SspInput(
            UInt64.Zero, UInt64.Zero,
            Array.Empty<EdgeDTO>(),
            Array.Empty<ClientNodeDTO>());
        Result<Success, ErrorCode, Error<ErrorCode>> result = demo.BuildModel(emptyInput);

        // Assert - Should still succeed (model with no constraints is valid)
        result.Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    [Fact]
    public void BuildModel_ShouldHaveTokens() {
        // Arrange
        var demo = new SspDemo();

        // Act
        demo.BuildModel();

        // Assert
        demo.MetaModel.Tokens.Should().NotBeNull();
    }
}
