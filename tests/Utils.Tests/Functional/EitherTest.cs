#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Utils.Tests.Functional
{
    /// <summary>Either 类型测试 / Either type tests.</summary>
    public class EitherTest
    {
        [Fact]
        public void Left_IsLeft_True()
        {
            Either<string, int> either = new Either<string, int>.Left("error");
            either.IsLeft.Should().BeTrue();
            either.IsRight.Should().BeFalse();
            either.LeftValue.Should().Be("error");
        }

        [Fact]
        public void Right_IsRight_True()
        {
            Either<string, int> either = new Either<string, int>.Right(42);
            either.IsRight.Should().BeTrue();
            either.IsLeft.Should().BeFalse();
            either.RightValue.Should().Be(42);
        }

        [Fact]
        public void Match_Left_ExecutesOnLeft()
        {
            Either<string, int> either = new Either<string, int>.Left("error");
            var result = either.Match(l => $"left:{l}", r => $"right:{r}");
            result.Should().Be("left:error");
        }

        [Fact]
        public void Match_Right_ExecutesOnRight()
        {
            Either<string, int> either = new Either<string, int>.Right(42);
            var result = either.Match(l => $"left:{l}", r => $"right:{r}");
            result.Should().Be("right:42");
        }

        [Fact]
        public void MapLeft_TransformsLeftValue()
        {
            Either<string, int> either = new Either<string, int>.Left("error");
            var mapped = either.MapLeft(l => l.Length);
            mapped.IsLeft.Should().BeTrue();
            mapped.LeftValue.Should().Be(5);
        }

        [Fact]
        public void MapRight_TransformsRightValue()
        {
            Either<string, int> either = new Either<string, int>.Right(42);
            var mapped = either.MapRight(r => r.ToString());
            mapped.IsRight.Should().BeTrue();
            mapped.RightValue.Should().Be("42");
        }
    }
}
