#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Math.Sequence;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Sequence;

public class ChannelProjectionTest {
    [Fact]
    public void ScalarAcceptsSingleComponent() {
        Assert.Equal(new Flt64(3.0), new ChannelProjection.Scalar().Project(new Flt64(3.0)).Value);
        Assert.Equal(
            new Flt64(3.0),
            new ChannelProjection.Scalar().Project(new[] { new Flt64(3.0) }).Value);
    }

    [Fact]
    public void ScalarRejectsMultiComponent() {
        Assert.True(new ChannelProjection.Scalar().Project(
            new[] { Flt64.One, new Flt64(2.0) }).IsFailed);
    }

    [Fact]
    public void ComponentSelectsInRangeIndex() {
        Flt64[] components = new[] { new Flt64(1.0), new Flt64(2.0), new Flt64(3.0) };

        Assert.Equal(new Flt64(2.0), new ChannelProjection.Component(1).Project(components).Value);
        Assert.True(new ChannelProjection.Component(5).Project(components).IsFailed);
    }

    [Fact]
    public void MagnitudeReturnsL2Norm() {
        Flt64[] components = new[] { new Flt64(3.0), new Flt64(4.0) };

        Assert.Equal(new Flt64(5.0), new ChannelProjection.Magnitude().Project(components).Value);
        Assert.Equal(
            new Flt64(5.0),
            new ChannelProjection.Magnitude().Project(PointFactory.point2(
                new Flt64(3.0),
                new Flt64(4.0))).Value);
    }

    [Fact]
    public void PhaseReturnsPolarAngle() {
        double phase = new ChannelProjection.Phase().Project(
            new[] { Flt64.Zero, Flt64.One }).Value.ToDouble();

        Assert.InRange(phase, System.Math.PI / 2.0 - 1.0e-12, System.Math.PI / 2.0 + 1.0e-12);
    }

    [Fact]
    public void PhaseRequiresAtLeastTwoComponents() {
        Assert.True(new ChannelProjection.Phase().Project(new[] { Flt64.One }).IsFailed);
        Assert.True(new ChannelProjection.Phase().Project(Flt64.One).IsFailed);
    }

    [Fact]
    public void EmptyComponentsAreRejected() {
        Assert.True(new ChannelProjection.Magnitude().Project(System.Array.Empty<Flt64>()).IsFailed);
        Assert.True(new ChannelProjection.Phase().Project(System.Array.Empty<Flt64>()).IsFailed);
    }

    [Fact]
    public void ComponentRejectsScalarProjection()
        => Assert.True(new ChannelProjection.Component(0).Project(Flt64.One).IsFailed);
}
