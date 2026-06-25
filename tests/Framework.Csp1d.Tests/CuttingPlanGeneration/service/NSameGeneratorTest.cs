#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.Service
{
    /// <summary>
    /// NSame 生成器测试 / NSame generator tests.
    /// </summary>
    public class NSameGeneratorTest
    {
        [Fact]
        public void EmptyDemands_ShouldReturnEmpty()
        {
            var constraints = GenerationConstraints<Flt64>.Unconstrained();
            var generator = new NSameGenerator<Flt64>(constraints);
            var input = new GenerationInputStub<Flt64>();

            var result = generator.Generate(input);

            result.Should().BeEmpty();
        }

        [Fact]
        public void AllAmount_ShouldBeConfigurable()
        {
            var constraints = GenerationConstraints<Flt64>.Unconstrained();
            var generator = new NSameGenerator<Flt64>(constraints, allAmount: true);

            generator.Should().NotBeNull();
        }
    }
}
