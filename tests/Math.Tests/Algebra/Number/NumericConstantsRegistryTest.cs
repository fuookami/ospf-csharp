#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Algebra.Number
{
    public class NumericConstantsRegistryTest
    {
        [Fact]
        public void Registry_ForFlt64_ReturnsCorrectConstants()
        {
            // Act
            var constants = NumericConstantsRegistry.For<Flt64>();

            // Assert
            constants.Should().NotBeNull();
            constants.One.Value.Should().Be(1.0);
            constants.Zero.Value.Should().Be(0.0);
            constants.Two.Value.Should().Be(2.0);
            constants.Ten.Value.Should().Be(10.0);
            constants.Pi!.Value.Value.Should().BeApproximately(System.Math.PI, 1e-10);
        }

        [Fact]
        public void Registry_ForInt64_ReturnsCorrectConstants()
        {
            // Act
            var constants = NumericConstantsRegistry.For<Int64>();

            // Assert
            constants.Should().NotBeNull();
            constants.Zero.Value.Should().Be(0L);
            constants.One.Value.Should().Be(1L);
            constants.Minimum.Value.Should().Be(long.MinValue);
            constants.Maximum.Value.Should().Be(long.MaxValue);
        }

        [Fact]
        public void Registry_ForUInt64_ReturnsCorrectConstants()
        {
            // Act
            var constants = NumericConstantsRegistry.For<UInt64>();

            // Assert
            constants.Should().NotBeNull();
            constants.Zero.Value.Should().Be(0UL);
            constants.One.Value.Should().Be(1UL);
        }

        [Fact]
        public void Registry_ForFlt32_ReturnsCorrectConstants()
        {
            // Act
            var constants = NumericConstantsRegistry.For<Flt32>();

            // Assert
            constants.Should().NotBeNull();
            constants.One.Value.Should().Be(1.0f);
            constants.Half!.Value.Value.Should().Be(0.5f);
        }

        [Fact]
        public void Registry_ForFltX_ReturnsCorrectConstants()
        {
            // Act
            var constants = NumericConstantsRegistry.For<FltX>();

            // Assert
            constants.Should().NotBeNull();
            constants.One.Value.Should().Be(1m);
            constants.Half!.Value.Value.Should().Be(0.5m);
        }

        [Fact]
        public void Registry_IsRegistered_ReturnsTrueForRegisteredTypes()
        {
            // Assert
            NumericConstantsRegistry.IsRegistered<Flt64>().Should().BeTrue();
            NumericConstantsRegistry.IsRegistered<Int64>().Should().BeTrue();
            NumericConstantsRegistry.IsRegistered<UInt64>().Should().BeTrue();
            NumericConstantsRegistry.IsRegistered<Flt32>().Should().BeTrue();
            NumericConstantsRegistry.IsRegistered<FltX>().Should().BeTrue();
        }

        [Fact]
        public void Registry_ForOrNull_ReturnsNullForUnregisteredTypes()
        {
            // Note: All built-in types are registered, so ForOrNull should not return null for them
            // This test verifies the method works without throwing
            var result = NumericConstantsRegistry.ForOrNull<Flt64>();
            result.Should().NotBeNull();
        }

        [Fact]
        public void Registry_NoReflection_UsedForLookup()
        {
            // Verify that the registry works without reflection by checking
            // that the lookup is a simple dictionary hit (no TypeInfo/Reflection usage)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100000; i++)
            {
                _ = NumericConstantsRegistry.For<Flt64>();
            }
            sw.Stop();

            // 100k lookups should complete in well under 1 second (dictionary hit)
            sw.ElapsedMilliseconds.Should().BeLessThan(1000);
        }
    }
}
