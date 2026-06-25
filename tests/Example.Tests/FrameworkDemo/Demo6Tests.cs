#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo6;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo
{
    [Trait("Category", "Solver")]
    public class Demo6GanttSchedulingTests
    {
        [Fact]
        public void BuildModel_ShouldSucceed()
        {
            var demo = new GanttSchedulingDemo();
            var result = demo.BuildModel();
            result.Should().BeOfType<Ok<Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>();
        }

        [Fact]
        public void Model_ShouldHaveMinimumObjective()
        {
            var demo = new GanttSchedulingDemo();
            demo.BuildModel();
            demo.MetaModel.ObjectCategory.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
        }

        [Fact]
        public void Tasks_ShouldNotBeEmpty()
        {
            var demo = new GanttSchedulingDemo();
            demo.ScheduleTasks.Should().NotBeEmpty();
        }

        [Fact]
        public void Model_ShouldHavePrecedenceAndDeadlineConstraints()
        {
            var demo = new GanttSchedulingDemo();
            demo.BuildModel();
            // 5*4 = 20 precedence constraints + 5 deadline constraints = 25
            demo.MetaModel.RelationConstraints.Count.Should().BeGreaterThanOrEqualTo(25);
        }
    }
}
