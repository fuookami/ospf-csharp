#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Cplex;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Cplex.Tests;

public class CplexLinearSolverTest {
    [Fact]
    [Trait("Solver", "CPLEX")]
    public void Solver_Should_Exist() {
        var solver = new CplexLinearSolver();
        solver.Should().NotBeNull();
        solver.Name.Should().Be("cplex");
    }
}
