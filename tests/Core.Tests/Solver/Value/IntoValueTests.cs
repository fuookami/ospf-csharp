#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Value
{
    public class IntoValueTests
    {
        [Fact]
        public void Identity_ConvertFlt64ToFlt64_ShouldReturnSameValue()
        {
            var converter = IIntoValue<Flt64>.Identity;
            var value = new Flt64(42);
            converter.IntoValue(value).Should().Be(value);
        }

        [Fact]
        public void Identity_Zero_ShouldBeFlt64Zero()
        {
            var converter = IIntoValue<Flt64>.Identity;
            converter.Zero.Should().Be(Flt64.Zero);
        }

        [Fact]
        public void Identity_One_ShouldBeFlt64One()
        {
            var converter = IIntoValue<Flt64>.Identity;
            converter.One.Should().Be(Flt64.One);
        }

        [Fact]
        public void Identity_FromValue_ShouldReturnSameValue()
        {
            var converter = IIntoValue<Flt64>.Identity;
            var value = new Flt64(3.14);
            converter.FromValue(value).Should().Be(value);
        }

        [Fact]
        public void Identity_RoundTrip_ShouldBeIdempotent()
        {
            var converter = IIntoValue<Flt64>.Identity;
            var original = new Flt64(123.456);
            var converted = converter.IntoValue(original);
            var backConverted = converter.FromValue(converted);
            backConverted.Should().Be(original);
        }

        [Fact]
        public void Flt64ToInt64_Converter_ShouldWork()
        {
            var converter = new Flt64ToInt64Converter();
            var flt64Value = new Flt64(42);
            var int64Value = converter.IntoValue(flt64Value);
            int64Value.Should().Be(new Fuookami.Ospf.Math.Algebra.Number.Int64(42));
        }

        [Fact]
        public void Flt64ToInt64_Converter_ShouldTruncate()
        {
            var converter = new Flt64ToInt64Converter();
            var flt64Value = new Flt64(42.7);
            var int64Value = converter.IntoValue(flt64Value);
            int64Value.Should().Be(new Fuookami.Ospf.Math.Algebra.Number.Int64(42));
        }

        [Fact]
        public void Flt64ToInt64_Converter_Zero_ShouldBeZero()
        {
            var converter = new Flt64ToInt64Converter();
            converter.Zero.Should().Be(new Fuookami.Ospf.Math.Algebra.Number.Int64(0));
        }

        [Fact]
        public void Flt64ToInt64_Converter_One_ShouldBeOne()
        {
            var converter = new Flt64ToInt64Converter();
            converter.One.Should().Be(new Fuookami.Ospf.Math.Algebra.Number.Int64(1));
        }

        [Fact]
        public void Flt64ToInt64_Converter_FromValue_ShouldConvertBack()
        {
            var converter = new Flt64ToInt64Converter();
            var int64Value = new Fuookami.Ospf.Math.Algebra.Number.Int64(42);
            var flt64Value = converter.FromValue(int64Value);
            flt64Value.Should().Be(new Flt64(42));
        }

        [Fact]
        public void Flt64ToInt64_Converter_RoundTrip()
        {
            var converter = new Flt64ToInt64Converter();
            var original = new Fuookami.Ospf.Math.Algebra.Number.Int64(100);
            var flt64 = converter.FromValue(original);
            var backConverted = converter.IntoValue(flt64);
            backConverted.Should().Be(original);
        }

        [Fact]
        public void Flt64ToInt64_Converter_NegativeValues()
        {
            var converter = new Flt64ToInt64Converter();
            var flt64Value = new Flt64(-7.0);
            var int64Value = converter.IntoValue(flt64Value);
            int64Value.Should().Be(new Fuookami.Ospf.Math.Algebra.Number.Int64(-7));
        }
    }

    /// <summary>
    /// Test converter: Flt64 to Int64 (truncating).
    /// </summary>
    internal sealed class Flt64ToInt64Converter : IIntoValue<Fuookami.Ospf.Math.Algebra.Number.Int64>
    {
        public Fuookami.Ospf.Math.Algebra.Number.Int64 IntoValue(Flt64 value) =>
            new((long)value.ToDouble());

        public Fuookami.Ospf.Math.Algebra.Number.Int64 Zero => new(0);
        public Fuookami.Ospf.Math.Algebra.Number.Int64 One => new(1);

        public Flt64 FromValue(Fuookami.Ospf.Math.Algebra.Number.Int64 value) =>
            value.ToFlt64();
    }
}
