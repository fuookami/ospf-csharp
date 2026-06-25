#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver
{
    public class SolverStatusTests
    {
        [Fact]
        public void Optimal_ShouldBeSucceeded()
        {
            SolverStatus.Optimal.Succeeded().Should().BeTrue();
            SolverStatus.Optimal.Failed().Should().BeFalse();
        }

        [Fact]
        public void Feasible_ShouldBeSucceeded()
        {
            SolverStatus.Feasible.Succeeded().Should().BeTrue();
            SolverStatus.Feasible.Failed().Should().BeFalse();
        }

        [Fact]
        public void Infeasible_ShouldBeFailed()
        {
            SolverStatus.Infeasible.Succeeded().Should().BeFalse();
            SolverStatus.Infeasible.Failed().Should().BeTrue();
        }

        [Fact]
        public void Unbounded_ShouldBeFailed()
        {
            SolverStatus.Unbounded.Succeeded().Should().BeFalse();
            SolverStatus.Unbounded.Failed().Should().BeTrue();
        }

        [Fact]
        public void InfeasibleOrUnbounded_ShouldBeFailed()
        {
            SolverStatus.InfeasibleOrUnbounded.Succeeded().Should().BeFalse();
            SolverStatus.InfeasibleOrUnbounded.Failed().Should().BeTrue();
        }

        [Fact]
        public void SolvingException_ShouldBeFailed()
        {
            SolverStatus.SolvingException.Succeeded().Should().BeFalse();
            SolverStatus.SolvingException.Failed().Should().BeTrue();
        }

        [Fact]
        public void Infeasible_ErrCode_ShouldBeORModelInfeasible()
        {
            SolverStatus.Infeasible.ErrCode().Should().Be(ErrorCode.ORModelInfeasible);
        }

        [Fact]
        public void Unbounded_ErrCode_ShouldBeORModelUnbounded()
        {
            SolverStatus.Unbounded.ErrCode().Should().Be(ErrorCode.ORModelUnbounded);
        }

        [Fact]
        public void InfeasibleOrUnbounded_ErrCode_ShouldBeORModelInfeasibleOrUnbounded()
        {
            SolverStatus.InfeasibleOrUnbounded.ErrCode().Should().Be(ErrorCode.ORModelInfeasibleOrUnbounded);
        }

        [Fact]
        public void SolvingException_ErrCode_ShouldBeOREngineSolvingException()
        {
            SolverStatus.SolvingException.ErrCode().Should().Be(ErrorCode.OREngineSolvingException);
        }

        [Fact]
        public void Optimal_ErrCode_ShouldBeNull()
        {
            SolverStatus.Optimal.ErrCode().Should().BeNull();
        }

        [Fact]
        public void Feasible_ErrCode_ShouldBeNull()
        {
            SolverStatus.Feasible.ErrCode().Should().BeNull();
        }
    }
}
