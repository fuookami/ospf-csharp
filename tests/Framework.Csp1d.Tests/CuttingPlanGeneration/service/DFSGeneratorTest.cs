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
    /// DFS 生成器测试 / DFS generator tests.
    /// </summary>
    public class DFSGeneratorTest
    {
        [Fact]
        public void EmptyDemands_ShouldReturnEmpty()
        {
            var constraints = GenerationConstraints<Flt64>.Unconstrained();
            var generator = new DFSGenerator<Flt64>(constraints);
            var input = new GenerationInputStub<Flt64>();

            var result = generator.Generate(input);

            result.Should().BeEmpty();
        }

        [Fact]
        public void GenerateWithReport_ShouldReturnReport()
        {
            var constraints = GenerationConstraints<Flt64>.Unconstrained();
            var generator = new DFSGenerator<Flt64>(constraints);
            var input = new GenerationInputStub<Flt64>();

            var report = generator.GenerateWithReport(input);

            report.Should().NotBeNull();
            report.Statistics.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_WithMaxPlans_ShouldAccept()
        {
            var constraints = GenerationConstraints<Flt64>.Unconstrained();
            var generator = new DFSGenerator<Flt64>(constraints, maxPlans: 500L);

            generator.Should().NotBeNull();
        }
    }
}
