#nullable enable

using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Lingo.Tests;

public class LingoTest {
    [Fact]
    public void Solver_Should_Exist() {
        var solver = new LingoLinearSolver();
        solver.Should().NotBeNull();
    }
}
