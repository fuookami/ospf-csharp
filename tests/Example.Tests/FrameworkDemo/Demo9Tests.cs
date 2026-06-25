#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo9;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo
{
    [Trait("Category", "Solver")]
    public class Demo9RemoteSolverTests
    {
        [Fact]
        public void BuildModel_ShouldSucceed()
        {
            var demo = new RemoteSolverDemo();
            var result = demo.BuildModel();
            result.Should().BeOfType<Ok<Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
        }

        [Fact]
        public void Model_ShouldHaveMaximumObjective()
        {
            var demo = new RemoteSolverDemo();
            demo.BuildModel();
            demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Maximum);
        }

        [Fact]
        public void Model_ShouldHaveConstraints()
        {
            var demo = new RemoteSolverDemo();
            demo.BuildModel();
            demo.MetaModel.RelationConstraints.Should().NotBeEmpty();
        }

        [Fact]
        public void SimulateRemoteExport_ShouldSucceed()
        {
            var demo = new RemoteSolverDemo();
            demo.BuildModel();
            var result = demo.SimulateRemoteExport();
            result.Should().BeOfType<Ok<Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
        }
    }
}
