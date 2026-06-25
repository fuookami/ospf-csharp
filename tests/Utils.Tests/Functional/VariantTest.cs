#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Utils.Tests.Functional
{
    /// <summary>Variant 类型测试 / Variant type tests.</summary>
    public class VariantTest
    {
        [Fact]
        public void Variant2_V1_MatchSelectsCorrect()
        {
            Variant2<string, int> v = new Variant2<string, int>.V1("hello");
            v.Is1.Should().BeTrue();
            v.Is2.Should().BeFalse();
            v.Match(s => s.Length, i => i).Should().Be(5);
        }

        [Fact]
        public void Variant2_V2_MatchSelectsCorrect()
        {
            Variant2<string, int> v = new Variant2<string, int>.V2(42);
            v.Is1.Should().BeFalse();
            v.Is2.Should().BeTrue();
            v.Match(s => s.Length, i => i).Should().Be(42);
        }

        [Fact]
        public void Variant3_Match_Works()
        {
            Variant3<string, int, bool> v = new Variant3<string, int, bool>.V3(true);
            v.Match(s => "str", i => "int", b => "bool").Should().Be("bool");
        }
    }
}
