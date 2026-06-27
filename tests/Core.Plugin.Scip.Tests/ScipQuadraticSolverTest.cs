#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Core.Solver;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

/// <summary>
/// SCIP 二次求解器结构测试 / SCIP quadratic solver structural tests.
/// </summary>
public class ScipQuadraticSolverTest {
    [Fact]
    [Trait("Solver", "SCIP")]
    public void QuadraticSolver_Should_Exist() {
        var solver = new ScipQuadraticSolver();
        solver.Should().NotBeNull();
        solver.Name.Should().Be("scip");
    }

    [Fact]
    [Trait("Solver", "SCIP")]
    public void QuadraticSolver_Should_Implement_IQuadraticSolver() {
        var solver = new ScipQuadraticSolver();
        solver.Should().BeAssignableTo<IQuadraticSolver>();
    }

    [Fact]
    [Trait("Solver", "SCIP")]
    public void QuadraticSolver_Should_Have_Default_Config() {
        var solver = new ScipQuadraticSolver();
        solver.Config.Should().NotBeNull();
    }
}
