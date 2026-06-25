#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.Service;
/// <summary>
/// NSame 生成器测试 / NSame generator tests.
/// </summary>
public class NSameGeneratorTest {
    [Fact]
    public void EmptyDemands_ShouldReturnEmpty() {
        var constraints = GenerationConstraints<Flt64>.Unconstrained();
        var generator = new NSameGenerator<Flt64>(constraints);
        var input = new GenerationInput<Flt64>();

        IReadOnlyList<CuttingPlan<Flt64>> result = generator.Generate(input);

        result.Should().BeEmpty();
    }

    [Fact]
    public void AllAmount_ShouldBeConfigurable() {
        var constraints = GenerationConstraints<Flt64>.Unconstrained();
        var generator = new NSameGenerator<Flt64>(constraints, allAmount: true);

        generator.Should().NotBeNull();
    }
}
