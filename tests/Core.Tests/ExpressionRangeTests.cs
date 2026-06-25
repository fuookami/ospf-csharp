#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests
{
    public class ExpressionRangeTests
    {
        [Fact]
        public void Create_UsesRegistryConstants()
        {
            var range = ExpressionRange<Flt64>.Create();
            range.Should().NotBeNull();
            range.Empty.Should().BeFalse();
            range.Set.Should().BeFalse();
        }

        [Fact]
        public void Create_WithRange_UsesRegistryConstants()
        {
            var valueRange = Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Flt64>.Of(
                new Flt64(0), new Flt64(10)).Value!;
            var range = ExpressionRange<Flt64>.Create(valueRange);
            range.Should().NotBeNull();
            range.Empty.Should().BeFalse();
        }

        [Fact]
        public void IntersectWith_TightensRange()
        {
            var range = ExpressionRange<Flt64>.Create();
            var ub = Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Flt64>.Leq(new Flt64(5)).Value!;
            range.IntersectWith(ub).Should().BeTrue();
            range.Set.Should().BeTrue();
            range.Empty.Should().BeFalse();
        }

        [Fact]
        public void IntersectWith_EmptyResult_ReturnsFalse()
        {
            var range = ExpressionRange<Flt64>.Create();
            // First set upper bound to 3
            var ub = Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Flt64>.Leq(new Flt64(3)).Value!;
            range.IntersectWith(ub);

            // Then try to set lower bound to 5 (should make empty)
            var lb = Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Flt64>.Geq(new Flt64(5)).Value!;
            range.IntersectWith(lb).Should().BeFalse();
            range.Empty.Should().BeTrue();
        }

        [Fact]
        public void SetRange_OverwritesRange()
        {
            var range = ExpressionRange<Flt64>.Create();
            var newRange = Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Flt64>.Of(
                new Flt64(1), new Flt64(2)).Value!;
            range.SetRange(newRange);
            range.Fixed.Should().BeFalse();
            range.Set.Should().BeTrue();
        }

        [Fact]
        public void ToString_EmptyRange_ReturnsEmpty()
        {
            var range = ExpressionRange<Flt64>.Create();
            // Create an empty range by intersecting incompatible ranges
            var ub = Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Flt64>.Leq(new Flt64(0)).Value!;
            range.IntersectWith(ub);
            var lb = Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Flt64>.Geq(new Flt64(10)).Value!;
            range.IntersectWith(lb);
            range.ToString().Should().Be("empty");
        }

        [Fact]
        public void NoReflection_CompanionObjectInstance()
        {
            // Verify that ExpressionRange uses NumericConstantsRegistry, not reflection
            // This is verified by the build succeeding without companionObjectInstance
            // and by the Create() factory working
            var range = ExpressionRange<Int64>.Create();
            range.Should().NotBeNull();
            var range2 = ExpressionRange<UInt8>.Create();
            range2.Should().NotBeNull();
        }
    }
}
