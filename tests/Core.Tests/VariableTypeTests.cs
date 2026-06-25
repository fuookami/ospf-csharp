#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests
{
    public class VariableTypeTests
    {
        private static IVariableTypeKind AsKind(IVariableTypeKind type) => type;

        [Fact]
        public void Binary_HasCorrectProperties()
        {
            var type = Binary.Instance;
            type.Name.Should().Be("Binary");
            type.ShortName.Should().Be("bin");
            var kind = AsKind(type);
            kind.IsBinaryType.Should().BeTrue();
            kind.IsUnsignedType.Should().BeTrue();
            kind.IsIntegerType.Should().BeTrue();
            kind.IsUnsignedIntegerType.Should().BeTrue();
            kind.IsContinuousType.Should().BeFalse();
            type.Maximum.Should().Be(UInt8.One);
            type.ToString().Should().Be("Binary");
        }

        [Fact]
        public void Ternary_HasCorrectProperties()
        {
            var type = Ternary.Instance;
            type.Name.Should().Be("Ternary");
            type.ShortName.Should().Be("ter");
            var kind = AsKind(type);
            kind.IsBinaryType.Should().BeFalse();
            kind.IsUnsignedType.Should().BeTrue();
            kind.IsIntegerType.Should().BeTrue();
            type.Maximum.Should().Be(UInt8.Two);
            type.ToString().Should().Be("Ternary");
        }

        [Fact]
        public void BalancedTernary_HasCorrectProperties()
        {
            var type = BalancedTernary.Instance;
            type.Name.Should().Be("BalancedTernary");
            type.ShortName.Should().Be("bter");
            var kind = AsKind(type);
            kind.IsBinaryType.Should().BeFalse();
            kind.IsUnsignedType.Should().BeFalse();
            kind.IsIntegerType.Should().BeTrue();
            type.ToString().Should().Be("BalancedTernary");
        }

        [Fact]
        public void Percentage_HasCorrectProperties()
        {
            var type = Percentage.Instance;
            type.Name.Should().Be("Percentage");
            type.ShortName.Should().Be("pct");
            var kind = AsKind(type);
            kind.IsUnsignedType.Should().BeTrue();
            kind.IsContinuousType.Should().BeTrue();
            kind.IsUnsignedContinuousType.Should().BeTrue();
            type.Maximum.Should().Be(Flt64.One);
            type.ToString().Should().Be("Percentage");
        }

        [Fact]
        public void Integer_HasCorrectProperties()
        {
            var type = Integer.Instance;
            type.Name.Should().Be("Integer");
            type.ShortName.Should().Be("int");
            var kind = AsKind(type);
            kind.IsIntegerType.Should().BeTrue();
            kind.IsUnsignedType.Should().BeFalse();
            type.ToString().Should().Be("Integer");
        }

        [Fact]
        public void UInteger_HasCorrectProperties()
        {
            var type = UInteger.Instance;
            type.Name.Should().Be("UInteger");
            type.ShortName.Should().Be("uint");
            var kind = AsKind(type);
            kind.IsIntegerType.Should().BeTrue();
            kind.IsUnsignedType.Should().BeTrue();
            kind.IsUnsignedIntegerType.Should().BeTrue();
            type.ToString().Should().Be("UInteger");
        }

        [Fact]
        public void Continuous_HasCorrectProperties()
        {
            var type = Continuous.Instance;
            type.Name.Should().Be("Continuous");
            type.ShortName.Should().Be("real");
            var kind = AsKind(type);
            kind.IsContinuousType.Should().BeTrue();
            kind.IsUnsignedType.Should().BeFalse();
            type.ToString().Should().Be("Continuous");
        }

        [Fact]
        public void UContinuous_HasCorrectProperties()
        {
            var type = UContinuous.Instance;
            type.Name.Should().Be("UContinuous");
            type.ShortName.Should().Be("ureal");
            var kind = AsKind(type);
            kind.IsContinuousType.Should().BeTrue();
            kind.IsUnsignedType.Should().BeTrue();
            kind.IsUnsignedContinuousType.Should().BeTrue();
            type.ToString().Should().Be("UContinuous");
        }

        [Fact]
        public void AllTypes_UseRegistryConstants()
        {
            Binary.Instance.Constants.Should().NotBeNull();
            Ternary.Instance.Constants.Should().NotBeNull();
            BalancedTernary.Instance.Constants.Should().NotBeNull();
            Percentage.Instance.Constants.Should().NotBeNull();
            Integer.Instance.Constants.Should().NotBeNull();
            UInteger.Instance.Constants.Should().NotBeNull();
            Continuous.Instance.Constants.Should().NotBeNull();
            UContinuous.Instance.Constants.Should().NotBeNull();
        }

        [Fact]
        public void IsContinuousType_IsNotIntegerType()
        {
            var kind = AsKind(Continuous.Instance);
            kind.IsContinuousType.Should().BeTrue();
            kind.IsIntegerType.Should().BeFalse();
        }

        [Fact]
        public void IsNotBinaryIntegerType_Integer_True()
        {
            AsKind(Integer.Instance).IsNotBinaryIntegerType.Should().BeTrue();
        }

        [Fact]
        public void IsNotBinaryIntegerType_Binary_False()
        {
            AsKind(Binary.Instance).IsNotBinaryIntegerType.Should().BeFalse();
        }
    }
}
