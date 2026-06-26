#nullable enable

using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Optverse.Tests;

public class OptverseTest {
    [Fact]
    public void Solver_Should_Exist() {
        var solver = new OptverseLinearSolver();
        solver.Should().NotBeNull();
    }
}
