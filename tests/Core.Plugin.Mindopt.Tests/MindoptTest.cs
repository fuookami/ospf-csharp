#nullable enable

using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Mindopt.Tests;

public class MindoptTest {
    [Fact]
    public void Solver_Should_Exist() {
        var solver = new MindoptLinearSolver();
        solver.Should().NotBeNull();
    }
}
