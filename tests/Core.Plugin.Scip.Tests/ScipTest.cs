#nullable enable

using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

public class ScipTest {
    [Fact]
    public void Solver_Should_Exist() {
        var solver = new ScipLinearSolver();
        solver.Should().NotBeNull();
    }
}
