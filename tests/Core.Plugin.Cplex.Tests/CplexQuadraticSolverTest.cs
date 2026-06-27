#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Cplex;
using Fuookami.Ospf.Core.Solver;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Cplex.Tests;

/// <summary>
/// CPLEX 二次求解器结构测试 / CPLEX quadratic solver structural tests.
/// </summary>
public class CplexQuadraticSolverTest {
    [Fact]
    [Trait("Solver", "CPLEX")]
    public void QuadraticSolver_Should_Exist() {
        var solver = new CplexQuadraticSolver();
        solver.Should().NotBeNull();
        solver.Name.Should().Be("cplex");
    }

    [Fact]
    [Trait("Solver", "CPLEX")]
    public void QuadraticSolver_Should_Implement_IQuadraticSolver() {
        var solver = new CplexQuadraticSolver();
        solver.Should().BeAssignableTo<IQuadraticSolver>();
    }

    [Fact]
    [Trait("Solver", "CPLEX")]
    public void QuadraticSolver_Should_Have_Default_Config() {
        var solver = new CplexQuadraticSolver();
        solver.Config.Should().NotBeNull();
    }
}
