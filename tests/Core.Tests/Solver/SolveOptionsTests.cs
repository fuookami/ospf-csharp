#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Value;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

public class SolveOptionsTests {
    [Fact]
    public void Default_EffectiveValueConversionPolicy_ShouldBeAllowRounding() {
        var options = new SolveOptions();
        options.EffectiveValueConversionPolicy.Should().Be(SolveValueConversionPolicy.AllowRounding);
    }

    [Fact]
    public void ExplicitValueConversionPolicy_Strict_ShouldReturnStrict() {
        var options = new SolveOptions(ValueConversionPolicy: SolveValueConversionPolicy.Strict);
        options.EffectiveValueConversionPolicy.Should().Be(SolveValueConversionPolicy.Strict);
    }

    [Fact]
    public void Builder_ShouldSetAllProperties() {
        var options = SolveOptions.Build(b => {
            b.SolutionAmount = 10;
            b.ValueConversionPolicy = SolveValueConversionPolicy.Strict;
        });

        options.SolutionAmount.Should().Be(10);
        options.ValueConversionPolicy.Should().Be(SolveValueConversionPolicy.Strict);
        options.EffectiveValueConversionPolicy.Should().Be(SolveValueConversionPolicy.Strict);
    }

    [Fact]
    public void Builder_Default_ShouldHaveNullValues() {
        SolveOptions.Builder builder = SolveOptions.CreateBuilder();
        SolveOptions options = builder.Build();

        options.SolutionAmount.Should().BeNull();
        options.ModelBuildingStatusCallBack.Should().BeNull();
        options.SolvingStatusCallBack.Should().BeNull();
        options.ValueConversionPolicy.Should().BeNull();
        options.EffectiveValueConversionPolicy.Should().Be(SolveValueConversionPolicy.AllowRounding);
    }

    [Fact]
    public void RecordEquality_SameValues_ShouldBeEqual() {
        var a = new SolveOptions(SolutionAmount: 5);
        var b = new SolveOptions(SolutionAmount: 5);
        a.Should().Be(b);
    }

    [Fact]
    public void RecordEquality_DifferentValues_ShouldNotBeEqual() {
        var a = new SolveOptions(SolutionAmount: 5);
        var b = new SolveOptions(SolutionAmount: 10);
        a.Should().NotBe(b);
    }
}
