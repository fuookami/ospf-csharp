#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.Model
{
    /// <summary>
    /// Canonical key 测试 / Canonical key tests.
    /// </summary>
    public class CuttingPlanCanonicalKeyTest
    {
        [Fact]
        public void CustomKey_ShouldCreateDegenerateKey()
        {
            var key = new CuttingPlanCanonicalKey("custom-key");

            key.MaterialId.Should().Be("custom-key");
            key.MachineId.Should().BeNull();
            key.CapacityConsumption.Should().BeNull();
            key.Slices.Should().BeEmpty();
            key.DemandContributions.Should().BeEmpty();
        }

        [Fact]
        public void TwoCustomKeys_SameString_ShouldBeEqual()
        {
            var key1 = new CuttingPlanCanonicalKey("key-a");
            var key2 = new CuttingPlanCanonicalKey("key-a");

            key1.Should().Be(key2);
        }

        [Fact]
        public void TwoCustomKeys_DifferentString_ShouldNotBeEqual()
        {
            var key1 = new CuttingPlanCanonicalKey("key-a");
            var key2 = new CuttingPlanCanonicalKey("key-b");

            key1.Should().NotBe(key2);
        }

        [Fact]
        public void FullKey_ShouldPreserveFields()
        {
            var slices = new[]
            {
                new CuttingPlanSliceCanonicalKey("product", "p-1", "100:MM", new UInt64(3UL))
            };
            var contributions = new[]
            {
                new CuttingPlanDemandContributionCanonicalKey("p-1", "MM", "300")
            };
            var key = new CuttingPlanCanonicalKey(
                MaterialId: "mat-1",
                MachineId: "machine-1",
                CapacityConsumption: "50:SEC",
                Slices: slices,
                DemandContributions: contributions
            );

            key.MaterialId.Should().Be("mat-1");
            key.MachineId.Should().Be("machine-1");
            key.Slices.Should().HaveCount(1);
            key.DemandContributions.Should().HaveCount(1);
        }

        [Fact]
        public void DistinctByCanonicalKey_ShouldDeduplicate()
        {
            var keys = new[]
            {
                new CuttingPlanCanonicalKey("a"),
                new CuttingPlanCanonicalKey("b"),
                new CuttingPlanCanonicalKey("a"),
                new CuttingPlanCanonicalKey("c"),
            };

            var distinct = keys.DistinctByCanonicalKey(k => k);

            distinct.Should().HaveCount(3);
        }
    }
}
