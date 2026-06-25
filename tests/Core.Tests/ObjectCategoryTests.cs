#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests
{
    public class ObjectCategoryTests
    {
        [Fact]
        public void Maximum_Reverse_IsMinimum()
        {
            ObjectCategory.Maximum.Reverse().Should().Be(ObjectCategory.Minimum);
        }

        [Fact]
        public void Minimum_Reverse_IsMaximum()
        {
            ObjectCategory.Minimum.Reverse().Should().Be(ObjectCategory.Maximum);
        }

        [Fact]
        public void DoubleReverse_ReturnsOriginal()
        {
            ObjectCategory.Maximum.Reverse().Reverse().Should().Be(ObjectCategory.Maximum);
            ObjectCategory.Minimum.Reverse().Reverse().Should().Be(ObjectCategory.Minimum);
        }

        [Fact]
        public void ToSymbolString_ReturnsName()
        {
            ObjectCategory.Maximum.ToSymbolString().Should().Be("Maximum");
            ObjectCategory.Minimum.ToSymbolString().Should().Be("Minimum");
        }
    }
}
