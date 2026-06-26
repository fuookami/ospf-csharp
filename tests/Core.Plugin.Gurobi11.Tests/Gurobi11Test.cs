#nullable enable

using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi11.Tests;

public class Gurobi11Test {
    [Fact]
    public void Solver_Should_Exist() {
        var solver = new Gurobi11LinearSolver();
        solver.Should().NotBeNull();
    }
}
