#nullable enable

using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Core.Solver;

namespace Fuookami.Ospf.Core.Plugin.Lingo.Tests;

public class LingoTest {
    [Fact]
    public void Placeholder_Should_Match_Pinned_Kotlin_Surface() {
        var solver = new LingoLinearSolver();
        solver.Should().NotBeNull();
        typeof(ILinearSolver).IsAssignableFrom(solver.GetType()).Should().BeFalse();
    }
}
