#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Copt;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Copt.Tests;

public class CoptLinearSolverTest {
    [Fact]
    [Trait("Solver", "COPT")]
    public void Solver_Should_Exist() {
        var solver = new CoptLinearSolver();
        solver.Should().NotBeNull();
        solver.Name.Should().Be("copt");
    }
}
