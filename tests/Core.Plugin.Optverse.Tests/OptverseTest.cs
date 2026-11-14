#nullable enable

using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Core.Solver;

namespace Fuookami.Ospf.Core.Plugin.Optverse.Tests;

public class OptverseTest {
    [Fact]
    public void Placeholder_Should_Match_Pinned_Kotlin_Surface() {
        var solver = new OptverseLinearSolver();
        solver.Should().NotBeNull();
        typeof(ILinearSolver).IsAssignableFrom(solver.GetType()).Should().BeFalse();
    }
}
